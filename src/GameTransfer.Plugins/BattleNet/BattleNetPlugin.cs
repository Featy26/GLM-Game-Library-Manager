using System.Text.Json;
using GameTransfer.Core.Helpers;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using GameTransfer.Plugins.Base;

namespace GameTransfer.Plugins.BattleNet;

public class BattleNetPlugin : LauncherPluginBase
{
    private const string AgentPath = @"C:\ProgramData\Battle.net\Agent";
    private static readonly string AggregateJsonPath =
        Path.Combine(AgentPath, "aggregate.json");
    private static readonly string BattleNetConfigPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Battle.net", "Battle.net.config");

    public BattleNetPlugin(IRegistryService registry) : base(registry) { }

    public override string LauncherName => "Battle.net";
    public override LauncherType Type => LauncherType.BattleNet;
    public override bool SupportsDirectReconfiguration => false;
    public override string LauncherProcessName => "Battle.net";

    public override bool IsInstalled()
    {
        try
        {
            return Directory.Exists(AgentPath);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to check if Battle.net is installed");
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
                if (!File.Exists(AggregateJsonPath))
                {
                    _log.Warning("aggregate.json not found at {Path}", AggregateJsonPath);
                    return games;
                }

                var json = File.ReadAllText(AggregateJsonPath);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("installed", out var installed))
                    return games;

                foreach (var entry in installed.EnumerateArray())
                {
                    try
                    {
                        var name = entry.GetProperty("name").GetString();
                        var productId = entry.GetProperty("product_id").GetString();

                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(productId))
                            continue;

                        // Skip the Battle.net client itself
                        if (productId == "bna" || productId == "agent")
                            continue;

                        // Get install path from icon_path (points to launcher exe in game dir)
                        var iconPath = entry.TryGetProperty("icon_path", out var ip)
                            ? ip.GetString() : null;
                        if (string.IsNullOrEmpty(iconPath))
                            continue;

                        var installPath = PathHelper.NormalizePath(
                            Path.GetDirectoryName(iconPath)!);

                        if (!Directory.Exists(installPath))
                            continue;

                        var sizeBytes = PathHelper.GetDirectorySize(installPath);

                        games.Add(new GameInfo
                        {
                            Id = productId,
                            Name = name,
                            InstallPath = installPath,
                            SizeBytes = sizeBytes,
                            Launcher = LauncherType.BattleNet,
                            Metadata = new Dictionary<string, string>
                            {
                                ["ProductId"] = productId
                            }
                        });

                        _log.Information("Detected Battle.net game {Name} at {Path}",
                            name, installPath);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Failed to parse Battle.net installed entry");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to read Battle.net aggregate.json");
            }

            return games;
        });
    }

    public override async Task UninstallGameAsync(GameInfo game)
    {
        await Task.Run(() =>
        {
            // Delete game files
            if (Directory.Exists(game.InstallPath))
            {
                _log.Information("Deleting Battle.net game directory {Path}", game.InstallPath);
                Directory.Delete(game.InstallPath, recursive: true);
            }

            _log.Information(
                "Battle.net game {Name} files deleted. The game will be shown as not installed in Battle.net.",
                game.Name);
        });
    }

    public override Task UpdateGamePathAsync(GameInfo game, string newInstallPath)
    {
        // Battle.net does not support external path reconfiguration.
        // The client manages install paths internally via product.db.
        // After moving files, Battle.net will detect the game at the new location
        // when the user points it there via "Locate this game" in the client.
        _log.Information(
            "Battle.net game {Name} moved to {NewPath}. " +
            "User may need to use 'Locate this game' in Battle.net to update the path.",
            game.Name, PathHelper.NormalizePath(newInstallPath));

        return Task.CompletedTask;
    }
}
