using GameTransfer.Core.Helpers;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using GameTransfer.Plugins.Base;
using Microsoft.Win32;

namespace GameTransfer.Plugins.EA;

public class EAPlugin : LauncherPluginBase
{
    private const string EARegistryKey = @"SOFTWARE\WOW6432Node\Electronic Arts";
    private const string UninstallKeyBase = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public EAPlugin(IRegistryService registry) : base(registry) { }

    public override string LauncherName => "EA App";
    public override LauncherType Type => LauncherType.EAApp;
    public override bool SupportsDirectReconfiguration => false;
    public override string LauncherProcessName => "EADesktop";

    public override bool IsInstalled()
    {
        try
        {
            var subKeys = _registry.GetSubKeyNames(RegistryHive.LocalMachine, EARegistryKey);
            if (subKeys.Count > 0)
                return true;

            // Fallback: check for EA Desktop folder
            var eaDesktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Electronic Arts", "EA Desktop");
            return Directory.Exists(eaDesktopPath);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to check if EA App is installed");
            return false;
        }
    }

    public override Task<IReadOnlyList<GameInfo>> DetectInstalledGamesAsync()
    {
        return Task.Run<IReadOnlyList<GameInfo>>(() =>
        {
            var games = new List<GameInfo>();

            try
            {
                var subKeys = _registry.GetSubKeyNames(RegistryHive.LocalMachine, EARegistryKey);

                foreach (var subKey in subKeys)
                {
                    try
                    {
                        var gameKey = $@"{EARegistryKey}\{subKey}";
                        var installDir = _registry.ReadValue(
                            RegistryHive.LocalMachine, gameKey, "Install Dir");

                        if (string.IsNullOrEmpty(installDir))
                            continue;

                        var normalizedPath = PathHelper.NormalizePath(installDir);
                        if (!Directory.Exists(normalizedPath))
                            continue;

                        // Try to get display name from the same key or Uninstall entries
                        var displayName = _registry.ReadValue(
                            RegistryHive.LocalMachine, gameKey, "DisplayName");
                        displayName ??= TryGetDisplayNameFromUninstall(subKey);
                        displayName ??= subKey;

                        var sizeBytes = PathHelper.GetDirectorySize(normalizedPath);

                        games.Add(new GameInfo
                        {
                            Id = subKey,
                            Name = displayName,
                            InstallPath = normalizedPath,
                            SizeBytes = sizeBytes,
                            Launcher = LauncherType.EAApp,
                            Metadata = new Dictionary<string, string>
                            {
                                ["RegistryKey"] = gameKey
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Failed to read EA game {SubKey}", subKey);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to detect EA games");
            }

            return games;
        });
    }

    public override async Task UninstallGameAsync(GameInfo game)
    {
        await Task.Run(() =>
        {
            // Remove EA registry entry
            var gameKey = $@"{EARegistryKey}\{game.Id}";
            try
            {
                _registry.DeleteKey(RegistryHive.LocalMachine, gameKey);
                _log.Information("Deleted EA registry key {Key}", gameKey);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to delete EA registry key {Key}", gameKey);
            }

            // Delete game files
            if (Directory.Exists(game.InstallPath))
            {
                _log.Information("Deleting EA game directory {Path}", game.InstallPath);
                Directory.Delete(game.InstallPath, recursive: true);
            }
        });
    }

    public override Task UpdateGamePathAsync(GameInfo game, string newInstallPath)
    {
        var normalizedNew = PathHelper.NormalizePath(newInstallPath);
        var gameKey = $@"{EARegistryKey}\{game.Id}";

        try
        {
            _registry.WriteValue(
                RegistryHive.LocalMachine,
                gameKey,
                "Install Dir",
                normalizedNew);

            _log.Information(
                "Updated EA game {Name} registry path to {NewPath} (symlink fallback recommended)",
                game.Name, normalizedNew);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update EA game {Name} path in registry", game.Name);
            throw;
        }

        return Task.CompletedTask;
    }

    private string? TryGetDisplayNameFromUninstall(string subKey)
    {
        try
        {
            var uninstallKeys = _registry.GetSubKeyNames(RegistryHive.LocalMachine, UninstallKeyBase);
            foreach (var key in uninstallKeys)
            {
                var fullKey = $@"{UninstallKeyBase}\{key}";
                var publisher = _registry.ReadValue(RegistryHive.LocalMachine, fullKey, "Publisher");
                if (publisher != null &&
                    publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase))
                {
                    var installLocation = _registry.ReadValue(
                        RegistryHive.LocalMachine, fullKey, "InstallLocation");
                    var gameInstallDir = _registry.ReadValue(
                        RegistryHive.LocalMachine, $@"{EARegistryKey}\{subKey}", "Install Dir");

                    if (!string.IsNullOrEmpty(installLocation) &&
                        !string.IsNullOrEmpty(gameInstallDir) &&
                        string.Equals(
                            PathHelper.NormalizePath(installLocation),
                            PathHelper.NormalizePath(gameInstallDir),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return _registry.ReadValue(RegistryHive.LocalMachine, fullKey, "DisplayName");
                    }
                }
            }
        }
        catch
        {
            // Swallow lookup errors
        }

        return null;
    }
}
