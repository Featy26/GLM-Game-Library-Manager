using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using Serilog;
using System.Diagnostics;

namespace GameTransfer.Core.Services;

public class TransferOrchestrator
{
    private readonly IFileTransferService _fileTransfer;
    private readonly IRegistryService _registry;
    private readonly IShortcutService _shortcuts;
    private readonly ISymlinkService _symlinks;
    private readonly IReadOnlyList<ILauncherPlugin> _plugins;
    private readonly ILogger _log = Log.ForContext<TransferOrchestrator>();

    public TransferOrchestrator(
        IFileTransferService fileTransfer,
        IRegistryService registry,
        IShortcutService shortcuts,
        ISymlinkService symlinks,
        IEnumerable<ILauncherPlugin> plugins)
    {
        _fileTransfer = fileTransfer;
        _registry = registry;
        _shortcuts = shortcuts;
        _symlinks = symlinks;
        _plugins = plugins.ToList();
    }

    public async Task<TransferResult> ExecuteTransferAsync(
        TransferJob job,
        IProgress<TransferProgress> progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var plugin = _plugins.FirstOrDefault(p => p.Type == job.Game.Launcher);
        if (plugin is null)
            return TransferResult.Failed($"No plugin found for launcher {job.Game.Launcher}");

        job.OriginalPath = job.Game.InstallPath;
        var gameFolderName = Path.GetFileName(job.Game.InstallPath.TrimEnd('\\', '/'));

        // Check if the plugin wants to use a native library path for this drive
        var destDriveRoot = Path.GetPathRoot(job.DestinationPath);
        var nativePath = plugin.GetNativeLibraryPath(destDriveRoot ?? @"C:\");
        var destinationGamePath = nativePath is not null
            ? Path.Combine(nativePath, gameFolderName)
            : GetLauncherDestinationPath(job.DestinationPath, job.Game.Launcher, gameFolderName);

        RegistryBackup? registryBackup = null;

        // Detect same-drive transfer (can use fast move instead of copy+delete)
        var sourceDrive = Path.GetPathRoot(job.Game.InstallPath)?.TrimEnd('\\').ToUpperInvariant();
        var destDriveNorm = Path.GetPathRoot(destinationGamePath)?.TrimEnd('\\').ToUpperInvariant();
        var isSameDrive = string.Equals(sourceDrive, destDriveNorm, StringComparison.OrdinalIgnoreCase);

        try
        {
            // Phase 1: Pre-flight
            job.State = TransferPhase.Preflight;
            progress.Report(new TransferProgress { Phase = TransferPhase.Preflight });
            _log.Information("Pre-flight check for {Game} -> {Dest}", job.Game.Name, destinationGamePath);

            if (!Directory.Exists(job.Game.InstallPath))
                return TransferResult.Failed($"Quellpfad existiert nicht: {job.Game.InstallPath}");

            // Check if the launcher is running
            if (plugin.IsLauncherRunning())
                return TransferResult.Failed(
                    $"{plugin.LauncherName} läuft noch. Bitte schließe {plugin.LauncherName} bevor du das Spiel verschiebst oder importierst.");

            // Only check space for cross-drive transfers (same-drive move doesn't need extra space)
            if (!isSameDrive)
            {
                var driveRoot = Path.GetPathRoot(destinationGamePath);
                if (driveRoot is not null)
                {
                    var driveInfo = new DriveInfo(driveRoot);
                    var requiredSpace = job.Game.SizeBytes;
                    if (driveInfo.AvailableFreeSpace < requiredSpace)
                        return TransferResult.Failed(
                            $"Nicht genug Speicherplatz auf {driveRoot}. Benötigt: {requiredSpace / (1024 * 1024 * 1024.0):F1} GB, Verfügbar: {driveInfo.AvailableFreeSpace / (1024 * 1024 * 1024.0):F1} GB");
                }
            }

            if (Directory.Exists(destinationGamePath))
            {
                _log.Information("Removing leftover destination folder {Path}", destinationGamePath);
                try
                {
                    Directory.Delete(destinationGamePath, true);
                }
                catch (Exception ex)
                {
                    return TransferResult.Failed($"Zielordner existiert bereits und konnte nicht gelöscht werden: {destinationGamePath} ({ex.Message})");
                }
            }

            // Phase 2: Move/Copy
            Directory.CreateDirectory(Path.GetDirectoryName(destinationGamePath)!);

            if (isSameDrive)
            {
                // Same drive: fast move (just renames directory entries, no data copied)
                job.State = TransferPhase.Copying;
                progress.Report(new TransferProgress { Phase = TransferPhase.Copying, CurrentFile = "Verschiebe (gleiche Festplatte)..." });
                _log.Information("Same-drive move {Game} to {Dest}", job.Game.Name, destinationGamePath);

                Directory.Move(job.Game.InstallPath, destinationGamePath);
            }
            else
            {
                // Cross-drive: copy files then verify
                job.State = TransferPhase.Copying;
                _log.Information("Copying {Game} to {Dest}", job.Game.Name, destinationGamePath);

                var copyResult = await _fileTransfer.MoveDirectoryAsync(
                    job.Game.InstallPath, destinationGamePath, progress, cancellationToken);

                if (!copyResult.Success)
                    return copyResult;

                cancellationToken.ThrowIfCancellationRequested();

                // Phase 3: Verify (only for cross-drive)
                job.State = TransferPhase.Verifying;
                _log.Information("Verifying {Game}", job.Game.Name);

                var verified = await _fileTransfer.VerifyDirectoryAsync(
                    job.Game.InstallPath, destinationGamePath, progress, cancellationToken);

                if (!verified)
                {
                    _log.Error("Verification failed for {Game}, rolling back", job.Game.Name);
                    await RollbackCopyAsync(destinationGamePath);
                    return TransferResult.Failed("Dateiverifikation fehlgeschlagen");
                }
            }

            // Phase 4: Update References
            job.State = TransferPhase.UpdatingReferences;
            progress.Report(new TransferProgress { Phase = TransferPhase.UpdatingReferences });
            _log.Information("Updating references for {Game}", job.Game.Name);

            // Backup registry before making changes
            try
            {
                var registryKeys = GetRegistryKeysForLauncher(job.Game.Launcher);
                foreach (var key in registryKeys)
                {
                    registryBackup = _registry.BackupKey(key.hive, key.subKey);
                    _log.Debug("Backed up registry key {Key}", key.subKey);
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Could not backup registry keys, continuing without backup");
            }

            try
            {
                await plugin.UpdateGamePathAsync(job.Game, destinationGamePath);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to update launcher config for {Game}", job.Game.Name);
                if (registryBackup is not null)
                    _registry.RestoreBackup(registryBackup);
                await RollbackCopyAsync(destinationGamePath);
                return TransferResult.Failed($"Failed to update launcher config: {ex.Message}");
            }

            // Update shortcuts
            try
            {
                _shortcuts.UpdateAllShortcuts(job.Game.InstallPath, destinationGamePath);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to update some shortcuts for {Game}", job.Game.Name);
            }

            // Phase 5: Cleanup (skip for same-drive moves — source already gone)
            job.State = TransferPhase.Cleanup;
            progress.Report(new TransferProgress { Phase = TransferPhase.Cleanup });

            if (!isSameDrive && Directory.Exists(job.Game.InstallPath))
            {
                _log.Information("Cleaning up source for {Game}", job.Game.Name);
                try
                {
                    Directory.Delete(job.Game.InstallPath, true);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to delete source directory {Path}", job.Game.InstallPath);
                }
            }

            job.State = TransferPhase.Completed;
            stopwatch.Stop();

            progress.Report(new TransferProgress
            {
                Phase = TransferPhase.Completed,
                BytesCopied = job.Game.SizeBytes,
                TotalBytes = job.Game.SizeBytes
            });

            _log.Information("Transfer completed for {Game} in {Duration}", job.Game.Name, stopwatch.Elapsed);

            return TransferResult.Succeeded(destinationGamePath, stopwatch.Elapsed, job.Game.SizeBytes);
        }
        catch (OperationCanceledException)
        {
            _log.Information("Transfer cancelled for {Game}", job.Game.Name);
            if (isSameDrive && Directory.Exists(destinationGamePath) && !Directory.Exists(job.Game.InstallPath))
            {
                // Same-drive move already happened — move back to original location
                _log.Information("Rolling back same-drive move: {Dest} -> {Source}", destinationGamePath, job.Game.InstallPath);
                try { Directory.Move(destinationGamePath, job.Game.InstallPath); }
                catch (Exception rollbackEx) { _log.Error(rollbackEx, "Failed to rollback same-drive move"); }
            }
            else
            {
                await RollbackCopyAsync(destinationGamePath);
            }
            if (registryBackup is not null)
                _registry.RestoreBackup(registryBackup);
            job.State = TransferPhase.RolledBack;
            return TransferResult.Failed("Transfer abgebrochen");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error during transfer of {Game}", job.Game.Name);
            if (isSameDrive && Directory.Exists(destinationGamePath) && !Directory.Exists(job.Game.InstallPath))
            {
                try { Directory.Move(destinationGamePath, job.Game.InstallPath); }
                catch (Exception rollbackEx) { _log.Error(rollbackEx, "Failed to rollback same-drive move"); }
            }
            else
            {
                await RollbackCopyAsync(destinationGamePath);
            }
            if (registryBackup is not null)
                _registry.RestoreBackup(registryBackup);
            job.State = TransferPhase.Failed;
            return TransferResult.Failed($"Unerwarteter Fehler: {ex.Message}");
        }
    }

    private Task RollbackCopyAsync(string destinationPath)
    {
        try
        {
            if (Directory.Exists(destinationPath))
            {
                _log.Information("Rolling back: deleting {Path}", destinationPath);
                Directory.Delete(destinationPath, true);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to rollback copy at {Path}", destinationPath);
        }
        return Task.CompletedTask;
    }

    public void PauseTransfer() => _fileTransfer.Pause();
    public void ResumeTransfer() => _fileTransfer.Resume();

    /// <summary>
    /// Builds the correct destination path per launcher.
    /// Steam needs: {base}\Steam\steamapps\common\{game}
    /// Others get:  {base}\{LauncherName}\{game}
    /// </summary>
    private static string GetLauncherDestinationPath(string basePath, LauncherType launcher, string gameFolderName)
    {
        return launcher switch
        {
            LauncherType.Steam => Path.Combine(basePath, "Steam", "steamapps", "common", gameFolderName),
            LauncherType.EpicGames => Path.Combine(basePath, "Epic Games", gameFolderName),
            LauncherType.GOG => Path.Combine(basePath, "GOG", gameFolderName),
            LauncherType.UbisoftConnect => Path.Combine(basePath, "Ubisoft", gameFolderName),
            LauncherType.EAApp => Path.Combine(basePath, "EA", gameFolderName),
            LauncherType.BattleNet => Path.Combine(basePath, "Battle.net", gameFolderName),
            _ => Path.Combine(basePath, "Andere", gameFolderName)
        };
    }

    private static IReadOnlyList<(Microsoft.Win32.RegistryHive hive, string subKey)> GetRegistryKeysForLauncher(LauncherType launcher)
    {
        return launcher switch
        {
            LauncherType.GOG => [(Microsoft.Win32.RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\Games")],
            LauncherType.UbisoftConnect => [(Microsoft.Win32.RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs")],
            LauncherType.EAApp => [(Microsoft.Win32.RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Electronic Arts")],
            LauncherType.BattleNet => [(Microsoft.Win32.RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Blizzard Entertainment")],
            _ => []
        };
    }
}
