using System.Text.Json;
using GameTransfer.Core.Helpers;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using GameTransfer.Plugins.Base;

namespace GameTransfer.Plugins.EpicGames;

public class EpicGamesPlugin : LauncherPluginBase
{
    private const string ManifestsDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
    private const string LauncherInstalledPath = @"C:\ProgramData\Epic\UnrealEngineLauncher\LauncherInstalled.dat";

    public EpicGamesPlugin(IRegistryService registry) : base(registry) { }

    public override string LauncherName => "Epic Games";
    public override LauncherType Type => LauncherType.EpicGames;
    public override bool SupportsDirectReconfiguration => true;
    public override string LauncherProcessName => "EpicGamesLauncher";

    public override bool IsInstalled()
    {
        try
        {
            return Directory.Exists(ManifestsDir);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to check if Epic Games is installed");
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
                if (!Directory.Exists(ManifestsDir))
                {
                    _log.Warning("Epic Games manifests directory not found");
                    return games;
                }

                var itemFiles = Directory.GetFiles(ManifestsDir, "*.item");
                foreach (var itemFile in itemFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(itemFile);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        var appName = root.TryGetProperty("AppName", out var an) ? an.GetString() : null;
                        var displayName = root.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : null;
                        var installLocation = root.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;
                        var launchExe = root.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() : null;

                        long installSize = 0;
                        if (root.TryGetProperty("InstallSize", out var isz))
                        {
                            installSize = isz.ValueKind == JsonValueKind.Number
                                ? isz.GetInt64()
                                : long.TryParse(isz.GetString(), out var parsed) ? parsed : 0;
                        }

                        if (string.IsNullOrEmpty(appName) || string.IsNullOrEmpty(displayName))
                            continue;

                        var exePath = !string.IsNullOrEmpty(installLocation) && !string.IsNullOrEmpty(launchExe)
                            ? Path.Combine(installLocation, launchExe)
                            : null;

                        games.Add(new GameInfo
                        {
                            Id = appName,
                            Name = displayName,
                            InstallPath = PathHelper.NormalizePath(installLocation ?? string.Empty),
                            SizeBytes = installSize,
                            Launcher = LauncherType.EpicGames,
                            ExecutablePath = exePath,
                            Metadata = new Dictionary<string, string>
                            {
                                ["ManifestFile"] = itemFile
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Failed to parse Epic Games manifest {Path}", itemFile);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to detect Epic Games titles");
            }

            return games;
        });
    }

    public override async Task UpdateGamePathAsync(GameInfo game, string newInstallPath)
    {
        await Task.Run(() =>
        {
            var normalizedNew = PathHelper.NormalizePath(newInstallPath);

            // Update the .item manifest file
            UpdateManifestFile(game, normalizedNew);

            // Update LauncherInstalled.dat
            UpdateLauncherInstalledDat(game.Id, normalizedNew);

            _log.Information("Updated Epic Games game {Name} path to {NewPath}", game.Name, normalizedNew);
        });
    }

    public override async Task UninstallGameAsync(GameInfo game)
    {
        await Task.Run(() =>
        {
            // Delete the .item manifest
            var manifestFile = game.Metadata.GetValueOrDefault("ManifestFile");
            if (!string.IsNullOrEmpty(manifestFile) && File.Exists(manifestFile))
            {
                File.Delete(manifestFile);
                _log.Information("Deleted Epic manifest {Path}", manifestFile);
            }

            // Remove from LauncherInstalled.dat
            RemoveFromLauncherInstalledDat(game.Id);

            // Delete game files
            if (Directory.Exists(game.InstallPath))
            {
                _log.Information("Deleting Epic game directory {Path}", game.InstallPath);
                Directory.Delete(game.InstallPath, recursive: true);
            }
        });
    }

    private void RemoveFromLauncherInstalledDat(string appName)
    {
        try
        {
            if (!File.Exists(LauncherInstalledPath))
                return;

            var json = File.ReadAllText(LauncherInstalledPath);
            using var doc = JsonDocument.Parse(json);

            var options = new JsonWriterOptions { Indented = true };
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "InstallationList" &&
                        prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        writer.WriteStartArray("InstallationList");
                        foreach (var entry in prop.Value.EnumerateArray())
                        {
                            var entryAppName = entry.TryGetProperty("AppName", out var an)
                                ? an.GetString() : null;
                            // Skip the entry we're uninstalling
                            if (entryAppName != appName)
                                entry.WriteTo(writer);
                        }
                        writer.WriteEndArray();
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            var updatedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            File.WriteAllText(LauncherInstalledPath, updatedJson);
            _log.Information("Removed {AppName} from LauncherInstalled.dat", appName);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to remove {AppName} from LauncherInstalled.dat", appName);
        }
    }

    private void UpdateManifestFile(GameInfo game, string newInstallPath)
    {
        // Find the manifest file for this game
        var manifestFile = game.Metadata.GetValueOrDefault("ManifestFile");

        if (string.IsNullOrEmpty(manifestFile) || !File.Exists(manifestFile))
        {
            // Try to find by scanning
            if (Directory.Exists(ManifestsDir))
            {
                foreach (var itemFile in Directory.GetFiles(ManifestsDir, "*.item"))
                {
                    try
                    {
                        var content = File.ReadAllText(itemFile);
                        using var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("AppName", out var an) &&
                            an.GetString() == game.Id)
                        {
                            manifestFile = itemFile;
                            break;
                        }
                    }
                    catch
                    {
                        // Skip unreadable files
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(manifestFile) || !File.Exists(manifestFile))
        {
            _log.Warning("Could not find Epic Games manifest for {AppName}", game.Id);
            return;
        }

        try
        {
            var json = File.ReadAllText(manifestFile);
            using var doc = JsonDocument.Parse(json);

            // Read old InstallLocation to detect path changes in related fields
            var oldInstallPath = doc.RootElement.TryGetProperty("InstallLocation", out var oldIl)
                ? oldIl.GetString() ?? "" : "";

            // Rebuild JSON with updated paths
            var options = new JsonWriterOptions { Indented = true };
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "InstallLocation")
                    {
                        writer.WriteString("InstallLocation", newInstallPath);
                    }
                    else if (prop.Name == "ManifestLocation")
                    {
                        // Update ManifestLocation to point to .egstore in new location
                        writer.WriteString("ManifestLocation",
                            Path.Combine(newInstallPath, ".egstore").Replace('\\', '/'));
                    }
                    else if (prop.Name == "StagingLocation")
                    {
                        // Update StagingLocation to new install path
                        writer.WriteString("StagingLocation",
                            Path.Combine(newInstallPath, ".egstore", "bps").Replace('\\', '/'));
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            var updatedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            File.WriteAllText(manifestFile, updatedJson);

            _log.Information("Updated Epic manifest {File}", manifestFile);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update Epic Games manifest {File}", manifestFile);
            throw;
        }
    }

    private void UpdateLauncherInstalledDat(string appName, string newInstallPath)
    {
        try
        {
            if (!File.Exists(LauncherInstalledPath))
            {
                _log.Warning("LauncherInstalled.dat not found at {Path}", LauncherInstalledPath);
                return;
            }

            var json = File.ReadAllText(LauncherInstalledPath);
            using var doc = JsonDocument.Parse(json);

            var options = new JsonWriterOptions { Indented = true };
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "InstallationList" &&
                        prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        writer.WriteStartArray("InstallationList");
                        foreach (var entry in prop.Value.EnumerateArray())
                        {
                            var entryAppName = entry.TryGetProperty("AppName", out var an)
                                ? an.GetString()
                                : null;

                            if (entryAppName == appName)
                            {
                                writer.WriteStartObject();
                                foreach (var entryProp in entry.EnumerateObject())
                                {
                                    if (entryProp.Name == "InstallLocation")
                                    {
                                        writer.WriteString("InstallLocation", newInstallPath);
                                    }
                                    else
                                    {
                                        entryProp.WriteTo(writer);
                                    }
                                }
                                writer.WriteEndObject();
                            }
                            else
                            {
                                entry.WriteTo(writer);
                            }
                        }
                        writer.WriteEndArray();
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            var updatedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            File.WriteAllText(LauncherInstalledPath, updatedJson);

            _log.Information("Updated LauncherInstalled.dat for {AppName}", appName);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to update LauncherInstalled.dat for {AppName}", appName);
        }
    }
}
