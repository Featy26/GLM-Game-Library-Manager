using GameTransfer.Core.Helpers;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using GameTransfer.Plugins.Base;
using Microsoft.Win32;

namespace GameTransfer.Plugins.Ubisoft;

public class UbisoftPlugin : LauncherPluginBase
{
    private const string LauncherKey = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher";
    private const string InstallsKey = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
    private const string UninstallKeyBase = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public UbisoftPlugin(IRegistryService registry) : base(registry) { }

    public override string LauncherName => "Ubisoft Connect";
    public override LauncherType Type => LauncherType.UbisoftConnect;
    public override bool SupportsDirectReconfiguration => false;
    public override string LauncherProcessName => "UbisoftConnect";

    public override bool IsInstalled()
    {
        try
        {
            var installDir = _registry.ReadValue(
                RegistryHive.LocalMachine,
                LauncherKey,
                "InstallDir");
            return !string.IsNullOrEmpty(installDir);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to check if Ubisoft Connect is installed");
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
                var subKeys = _registry.GetSubKeyNames(RegistryHive.LocalMachine, InstallsKey);

                foreach (var gameId in subKeys)
                {
                    try
                    {
                        var gameKey = $@"{InstallsKey}\{gameId}";
                        var installDir = _registry.ReadValue(
                            RegistryHive.LocalMachine, gameKey, "InstallDir");

                        if (string.IsNullOrEmpty(installDir))
                            continue;

                        // Try to get display name from Uninstall registry
                        var gameName = TryGetDisplayName(gameId) ?? $"Ubisoft Game {gameId}";
                        var normalizedPath = PathHelper.NormalizePath(installDir);
                        var sizeBytes = PathHelper.GetDirectorySize(normalizedPath);

                        games.Add(new GameInfo
                        {
                            Id = gameId,
                            Name = gameName,
                            InstallPath = normalizedPath,
                            SizeBytes = sizeBytes,
                            Launcher = LauncherType.UbisoftConnect,
                            Metadata = new Dictionary<string, string>
                            {
                                ["RegistryKey"] = gameKey
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Failed to read Ubisoft game {GameId}", gameId);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to detect Ubisoft Connect games");
            }

            return games;
        });
    }

    public override async Task UninstallGameAsync(GameInfo game)
    {
        await Task.Run(() =>
        {
            // Remove Installs registry entry
            var gameKey = $@"{InstallsKey}\{game.Id}";
            try
            {
                _registry.DeleteKey(RegistryHive.LocalMachine, gameKey);
                _log.Information("Deleted Ubisoft registry key {Key}", gameKey);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to delete Ubisoft registry key {Key}", gameKey);
            }

            // Delete game files
            if (Directory.Exists(game.InstallPath))
            {
                _log.Information("Deleting Ubisoft game directory {Path}", game.InstallPath);
                Directory.Delete(game.InstallPath, recursive: true);
            }
        });
    }

    public override Task UpdateGamePathAsync(GameInfo game, string newInstallPath)
    {
        var normalizedNew = PathHelper.NormalizePath(newInstallPath);
        var gameKey = $@"{InstallsKey}\{game.Id}";

        try
        {
            _registry.WriteValue(
                RegistryHive.LocalMachine,
                gameKey,
                "InstallDir",
                normalizedNew);

            _log.Information(
                "Updated Ubisoft game {Name} registry path to {NewPath} (symlink fallback recommended)",
                game.Name, normalizedNew);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update Ubisoft game {Name} path in registry", game.Name);
            throw;
        }

        return Task.CompletedTask;
    }

    private string? TryGetDisplayName(string gameId)
    {
        try
        {
            var uninstallKey = $@"{UninstallKeyBase}\Uplay Install {gameId}";
            return _registry.ReadValue(RegistryHive.LocalMachine, uninstallKey, "DisplayName");
        }
        catch
        {
            return null;
        }
    }
}
