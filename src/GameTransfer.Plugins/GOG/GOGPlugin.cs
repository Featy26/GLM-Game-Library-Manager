using GameTransfer.Core.Helpers;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using GameTransfer.Plugins.Base;
using Microsoft.Win32;

namespace GameTransfer.Plugins.GOG;

public class GOGPlugin : LauncherPluginBase
{
    private const string GamesRegistryKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";
    private const string ClientPathsKey = @"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths";

    public GOGPlugin(IRegistryService registry) : base(registry) { }

    public override string LauncherName => "GOG Galaxy";
    public override LauncherType Type => LauncherType.GOG;
    public override bool SupportsDirectReconfiguration => true;
    public override string LauncherProcessName => "GalaxyClient";

    public override bool IsInstalled()
    {
        try
        {
            var clientPath = _registry.ReadValue(
                RegistryHive.LocalMachine,
                ClientPathsKey,
                "client");
            return !string.IsNullOrEmpty(clientPath);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to check if GOG Galaxy is installed");
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
                var subKeys = _registry.GetSubKeyNames(RegistryHive.LocalMachine, GamesRegistryKey);

                foreach (var gameId in subKeys)
                {
                    try
                    {
                        var gameKey = $@"{GamesRegistryKey}\{gameId}";

                        var gameName = _registry.ReadValue(RegistryHive.LocalMachine, gameKey, "gameName");
                        var gamePath = _registry.ReadValue(RegistryHive.LocalMachine, gameKey, "path");
                        var exePath = _registry.ReadValue(RegistryHive.LocalMachine, gameKey, "exe");

                        if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(gamePath))
                            continue;

                        var normalizedPath = PathHelper.NormalizePath(gamePath);
                        var sizeBytes = PathHelper.GetDirectorySize(normalizedPath);

                        games.Add(new GameInfo
                        {
                            Id = gameId,
                            Name = gameName,
                            InstallPath = normalizedPath,
                            SizeBytes = sizeBytes,
                            Launcher = LauncherType.GOG,
                            ExecutablePath = exePath,
                            Metadata = new Dictionary<string, string>
                            {
                                ["RegistryKey"] = gameKey
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Failed to read GOG game {GameId}", gameId);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to detect GOG games");
            }

            return games;
        });
    }

    public override async Task UninstallGameAsync(GameInfo game)
    {
        await Task.Run(() =>
        {
            // Remove registry entry
            var gameKey = $@"{GamesRegistryKey}\{game.Id}";
            try
            {
                _registry.DeleteKey(RegistryHive.LocalMachine, gameKey);
                _log.Information("Deleted GOG registry key {Key}", gameKey);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to delete GOG registry key {Key}", gameKey);
            }

            // Delete game files
            if (Directory.Exists(game.InstallPath))
            {
                _log.Information("Deleting GOG game directory {Path}", game.InstallPath);
                Directory.Delete(game.InstallPath, recursive: true);
            }
        });
    }

    public override Task UpdateGamePathAsync(GameInfo game, string newInstallPath)
    {
        var normalizedNew = PathHelper.NormalizePath(newInstallPath);
        var gameKey = $@"{GamesRegistryKey}\{game.Id}";

        try
        {
            // Update the path value
            _registry.WriteValue(
                RegistryHive.LocalMachine,
                gameKey,
                "path",
                normalizedNew);

            // Update exe value - replace old base path with new
            var currentExe = _registry.ReadValue(RegistryHive.LocalMachine, gameKey, "exe");
            if (!string.IsNullOrEmpty(currentExe))
            {
                var updatedExe = PathHelper.ReplacePath(currentExe, game.InstallPath, normalizedNew);
                _registry.WriteValue(
                    RegistryHive.LocalMachine,
                    gameKey,
                    "exe",
                    updatedExe);
            }

            _log.Information("Updated GOG game {Name} registry path to {NewPath}", game.Name, normalizedNew);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update GOG game {Name} path in registry", game.Name);
            throw;
        }

        return Task.CompletedTask;
    }
}
