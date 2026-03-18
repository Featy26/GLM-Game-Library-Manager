using GameTransfer.Core.Helpers;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using GameTransfer.Plugins.Base;
using Microsoft.Win32;

namespace GameTransfer.Plugins.Steam;

public class SteamPlugin : LauncherPluginBase
{
    public SteamPlugin(IRegistryService registry) : base(registry) { }

    public override string LauncherName => "Steam";
    public override LauncherType Type => LauncherType.Steam;
    public override bool SupportsDirectReconfiguration => true;
    public override string LauncherProcessName => "steam";

    public override bool IsInstalled()
    {
        try
        {
            var steamPath = _registry.ReadValue(
                RegistryHive.CurrentUser,
                @"Software\Valve\Steam",
                "SteamPath");
            return !string.IsNullOrEmpty(steamPath);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to check if Steam is installed");
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
                var steamPath = _registry.ReadValue(
                    RegistryHive.CurrentUser,
                    @"Software\Valve\Steam",
                    "SteamPath");

                if (string.IsNullOrEmpty(steamPath))
                {
                    _log.Warning("Steam path not found in registry");
                    return games;
                }

                steamPath = steamPath.Replace('/', '\\');

                var libraryFoldersPath = Path.Combine(steamPath, "config", "libraryfolders.vdf");
                if (!File.Exists(libraryFoldersPath))
                {
                    _log.Warning("libraryfolders.vdf not found at {Path}", libraryFoldersPath);
                    return games;
                }

                var vdfContent = File.ReadAllText(libraryFoldersPath);
                var vdfRoot = VdfParser.Parse(vdfContent);

                // The root has a "libraryfolders" child with numbered children (0, 1, 2...)
                var libraryFolders = vdfRoot.Children.ContainsKey("libraryfolders")
                    ? vdfRoot.Children["libraryfolders"]
                    : vdfRoot;

                foreach (var (key, folderNode) in libraryFolders.Children)
                {
                    if (!int.TryParse(key, out _))
                        continue;

                    var libraryPath = folderNode["path"];
                    if (string.IsNullOrEmpty(libraryPath))
                        continue;

                    libraryPath = libraryPath.Replace('/', '\\');
                    var steamAppsPath = Path.Combine(libraryPath, "steamapps");

                    if (!Directory.Exists(steamAppsPath))
                        continue;

                    var manifests = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                    foreach (var manifestPath in manifests)
                    {
                        try
                        {
                            var acfContent = File.ReadAllText(manifestPath);
                            var acfRoot = VdfParser.Parse(acfContent);

                            var appState = acfRoot.Children.ContainsKey("AppState")
                                ? acfRoot.Children["AppState"]
                                : acfRoot;

                            var appId = appState["appid"];
                            var name = appState["name"];
                            var installDir = appState["installdir"];
                            var sizeStr = appState["SizeOnDisk"];

                            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name))
                                continue;

                            long.TryParse(sizeStr, out var sizeBytes);

                            var installPath = Path.Combine(steamAppsPath, "common", installDir ?? name);

                            games.Add(new GameInfo
                            {
                                Id = appId,
                                Name = name,
                                InstallPath = PathHelper.NormalizePath(installPath),
                                SizeBytes = sizeBytes,
                                Launcher = LauncherType.Steam,
                                Metadata = new Dictionary<string, string>
                                {
                                    ["LibraryPath"] = libraryPath,
                                    ["ManifestPath"] = manifestPath
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            _log.Warning(ex, "Failed to parse Steam manifest {Path}", manifestPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to detect Steam games");
            }

            return games;
        });
    }

    public override async Task UpdateGamePathAsync(GameInfo game, string newInstallPath)
    {
        await Task.Run(() =>
        {
            var manifestFileName = $"appmanifest_{game.Id}.acf";

            // Determine old library path from metadata
            var oldLibraryPath = game.Metadata.GetValueOrDefault("LibraryPath")
                ?? Path.GetDirectoryName(Path.GetDirectoryName(game.InstallPath))
                    ?.Replace(Path.Combine("steamapps", "common"), "")?.TrimEnd('\\');

            if (string.IsNullOrEmpty(oldLibraryPath))
                throw new InvalidOperationException("Could not determine old Steam library path.");

            // Determine new library path from newInstallPath
            // newInstallPath = {newLibrary}\steamapps\common\{installdir}
            var newLibraryPath = newInstallPath;
            var commonIdx = newInstallPath.IndexOf(
                Path.Combine("steamapps", "common"), StringComparison.OrdinalIgnoreCase);
            if (commonIdx > 0)
            {
                newLibraryPath = newInstallPath[..commonIdx].TrimEnd('\\');
            }

            var oldSteamApps = Path.Combine(oldLibraryPath, "steamapps");
            var newSteamApps = Path.Combine(newLibraryPath, "steamapps");

            // Create full Steam library structure so Steam recognizes it
            Directory.CreateDirectory(newSteamApps);
            Directory.CreateDirectory(Path.Combine(newSteamApps, "common"));
            Directory.CreateDirectory(Path.Combine(newSteamApps, "workshop"));
            Directory.CreateDirectory(Path.Combine(newSteamApps, "downloading"));
            Directory.CreateDirectory(Path.Combine(newSteamApps, "temp"));

            // Move the manifest file
            var oldManifest = Path.Combine(oldSteamApps, manifestFileName);
            var newManifest = Path.Combine(newSteamApps, manifestFileName);

            if (File.Exists(oldManifest))
            {
                _log.Information("Moving Steam manifest from {Old} to {New}", oldManifest, newManifest);
                File.Copy(oldManifest, newManifest, overwrite: true);
                File.Delete(oldManifest);
            }

            // Update libraryfolders.vdf: move app from old library to new library
            var steamPath = _registry.ReadValue(
                RegistryHive.CurrentUser,
                @"Software\Valve\Steam",
                "SteamPath")?.Replace('/', '\\');

            if (!string.IsNullOrEmpty(steamPath))
            {
                UpdateLibraryFoldersVdf(steamPath, oldLibraryPath, newLibraryPath, game.Id, game.SizeBytes);
            }

            game.Metadata["OldLibrary"] = oldLibraryPath;
            game.Metadata["NewLibrary"] = newLibraryPath;

            _log.Information("Updated Steam game {Name} path to {NewPath}", game.Name, newInstallPath);
        });
    }

    public override async Task UninstallGameAsync(GameInfo game)
    {
        await Task.Run(() =>
        {
            var manifestFileName = $"appmanifest_{game.Id}.acf";

            // Determine library path
            var libraryPath = game.Metadata.GetValueOrDefault("LibraryPath");
            if (!string.IsNullOrEmpty(libraryPath))
            {
                var manifestPath = Path.Combine(libraryPath, "steamapps", manifestFileName);
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                    _log.Information("Deleted Steam manifest {Path}", manifestPath);
                }
            }

            // Remove app from libraryfolders.vdf
            var steamPath = _registry.ReadValue(
                RegistryHive.CurrentUser,
                @"Software\Valve\Steam",
                "SteamPath")?.Replace('/', '\\');

            if (!string.IsNullOrEmpty(steamPath) && !string.IsNullOrEmpty(libraryPath))
            {
                RemoveAppFromVdf(steamPath, libraryPath, game.Id);
            }

            // Delete game files
            if (Directory.Exists(game.InstallPath))
            {
                _log.Information("Deleting Steam game directory {Path}", game.InstallPath);
                Directory.Delete(game.InstallPath, recursive: true);
            }
        });
    }

    private void RemoveAppFromVdf(string steamPath, string libraryPath, string appId)
    {
        try
        {
            var vdfPath = Path.Combine(steamPath, "config", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                return;

            var content = File.ReadAllText(vdfPath);
            var root = VdfParser.Parse(content);

            var libraryFolders = root.Children.ContainsKey("libraryfolders")
                ? root.Children["libraryfolders"]
                : root;

            var normalizedLib = PathHelper.NormalizePath(libraryPath);

            foreach (var (_, folderNode) in libraryFolders.Children)
            {
                var existingPath = folderNode["path"];
                if (string.IsNullOrEmpty(existingPath)) continue;

                var normalizedExisting = PathHelper.NormalizePath(existingPath.Replace('/', '\\'));
                if (string.Equals(normalizedExisting, normalizedLib, StringComparison.OrdinalIgnoreCase))
                {
                    if (folderNode.Children.TryGetValue("apps", out var apps))
                    {
                        apps.Children.Remove(appId);
                        _log.Information("Removed app {AppId} from VDF library {Path}", appId, libraryPath);
                    }
                    break;
                }
            }

            var serialized = "\"libraryfolders\"\n{\n" +
                             VdfParser.Serialize(libraryFolders, 1) + "}\n";
            File.WriteAllText(vdfPath, serialized);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to remove app {AppId} from libraryfolders.vdf", appId);
        }
    }

    public string? GetNativeLibraryPath(string driveRoot)
    {
        // Only return native path for C: — Steam games on C: stay in the default Steam library
        if (!string.Equals(driveRoot, @"C:\", StringComparison.OrdinalIgnoreCase))
            return null;

        var steamPath = _registry.ReadValue(
            RegistryHive.CurrentUser,
            @"Software\Valve\Steam",
            "SteamPath")?.Replace('/', '\\');

        if (string.IsNullOrEmpty(steamPath))
            return null;

        // Only return it if Steam is actually installed on C:
        var steamRoot = Path.GetPathRoot(steamPath);
        if (!string.Equals(steamRoot, @"C:\", StringComparison.OrdinalIgnoreCase))
            return null;

        return Path.Combine(steamPath, "steamapps", "common");
    }

    public async Task PostImportCleanupAsync(IReadOnlyList<GameInfo> movedGames)
    {
        await Task.Run(() =>
        {
            // Collect all old library paths from moved games
            var oldLibraries = movedGames
                .Select(g => g.Metadata.GetValueOrDefault("OldLibrary"))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var steamPath = _registry.ReadValue(
                RegistryHive.CurrentUser,
                @"Software\Valve\Steam",
                "SteamPath")?.Replace('/', '\\');

            if (string.IsNullOrEmpty(steamPath))
                return;

            // Don't delete the main Steam installation library
            var mainSteamLibrary = PathHelper.NormalizePath(steamPath);

            foreach (var oldLibrary in oldLibraries)
            {
                var normalizedOld = PathHelper.NormalizePath(oldLibrary!);

                // Never delete the main Steam installation folder
                if (string.Equals(normalizedOld, mainSteamLibrary, StringComparison.OrdinalIgnoreCase))
                {
                    _log.Information("Skipping cleanup of main Steam folder {Path}", oldLibrary);
                    continue;
                }

                var steamAppsPath = Path.Combine(oldLibrary!, "steamapps");
                if (!Directory.Exists(steamAppsPath))
                    continue;

                // Check if there are any remaining manifests
                var remainingManifests = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                if (remainingManifests.Length > 0)
                {
                    _log.Information("Old Steam library {Path} still has {Count} manifests, skipping cleanup",
                        oldLibrary, remainingManifests.Length);
                    continue;
                }

                // Library is empty — delete the steamapps folder
                try
                {
                    _log.Information("Deleting empty old Steam library at {Path}", steamAppsPath);
                    Directory.Delete(steamAppsPath, recursive: true);

                    // If the parent folder is now empty (e.g. "D:\SteamLibrary"), delete it too
                    if (Directory.Exists(oldLibrary!) &&
                        !Directory.EnumerateFileSystemEntries(oldLibrary!).Any())
                    {
                        Directory.Delete(oldLibrary!);
                        _log.Information("Deleted empty library root folder {Path}", oldLibrary);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to delete old Steam library folder {Path}", steamAppsPath);
                }

                // Remove the old library entry from libraryfolders.vdf
                RemoveLibraryFromVdf(steamPath, oldLibrary!);
            }
        });
    }

    /// <summary>
    /// Finds and removes all empty Steam library folders (no appmanifest_*.acf files)
    /// from disk and from libraryfolders.vdf. Skips the main Steam installation folder.
    /// Returns a list of removed library paths.
    /// </summary>
    public async Task<IReadOnlyList<string>> CleanEmptyLibrariesAsync()
    {
        return await Task.Run<IReadOnlyList<string>>(() =>
        {
            var removed = new List<string>();

            var steamPath = _registry.ReadValue(
                RegistryHive.CurrentUser,
                @"Software\Valve\Steam",
                "SteamPath")?.Replace('/', '\\');

            if (string.IsNullOrEmpty(steamPath))
                return removed;

            var mainSteamLibrary = PathHelper.NormalizePath(steamPath);

            var vdfPath = Path.Combine(steamPath, "config", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                return removed;

            var content = File.ReadAllText(vdfPath);
            var root = VdfParser.Parse(content);

            var libraryFolders = root.Children.ContainsKey("libraryfolders")
                ? root.Children["libraryfolders"]
                : root;

            var keysToRemove = new List<string>();

            foreach (var (key, folderNode) in libraryFolders.Children)
            {
                if (!int.TryParse(key, out _))
                    continue;

                var libPath = folderNode["path"];
                if (string.IsNullOrEmpty(libPath))
                    continue;

                libPath = libPath.Replace('/', '\\');
                var normalizedLib = PathHelper.NormalizePath(libPath);

                // Never touch the main Steam installation
                if (string.Equals(normalizedLib, mainSteamLibrary, StringComparison.OrdinalIgnoreCase))
                    continue;

                var steamAppsPath = Path.Combine(libPath, "steamapps");
                if (!Directory.Exists(steamAppsPath))
                {
                    // Library folder doesn't exist at all — remove from VDF
                    keysToRemove.Add(key);
                    removed.Add(libPath);
                    _log.Information("Steam library {Path} does not exist on disk, removing from VDF", libPath);
                    continue;
                }

                var manifests = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");
                if (manifests.Length > 0)
                    continue;

                // Library is empty — delete it
                try
                {
                    _log.Information("Deleting empty Steam library at {Path}", steamAppsPath);
                    Directory.Delete(steamAppsPath, recursive: true);

                    // Delete parent if empty
                    if (Directory.Exists(libPath) &&
                        !Directory.EnumerateFileSystemEntries(libPath).Any())
                    {
                        Directory.Delete(libPath);
                        _log.Information("Deleted empty library root {Path}", libPath);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to delete Steam library folder {Path}", steamAppsPath);
                }

                keysToRemove.Add(key);
                removed.Add(libPath);
            }

            if (keysToRemove.Count > 0)
            {
                foreach (var key in keysToRemove)
                    libraryFolders.Children.Remove(key);

                // Re-number remaining entries
                var remaining = libraryFolders.Children
                    .Where(kv => int.TryParse(kv.Key, out _))
                    .OrderBy(kv => int.Parse(kv.Key))
                    .Select(kv => kv.Value)
                    .ToList();

                foreach (var key in libraryFolders.Children.Keys
                    .Where(k => int.TryParse(k, out _)).ToList())
                    libraryFolders.Children.Remove(key);

                for (var i = 0; i < remaining.Count; i++)
                    libraryFolders.Children[i.ToString()] = remaining[i];

                var serialized = "\"libraryfolders\"\n{\n" +
                                 VdfParser.Serialize(libraryFolders, 1) + "}\n";
                File.WriteAllText(vdfPath, serialized);

                _log.Information("Cleaned {Count} empty Steam libraries from VDF", keysToRemove.Count);
            }

            return removed;
        });
    }

    private void RemoveLibraryFromVdf(string steamPath, string libraryPath)
    {
        try
        {
            var vdfPath = Path.Combine(steamPath, "config", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                return;

            var content = File.ReadAllText(vdfPath);
            var root = VdfParser.Parse(content);

            var libraryFolders = root.Children.ContainsKey("libraryfolders")
                ? root.Children["libraryfolders"]
                : root;

            var normalizedTarget = PathHelper.NormalizePath(libraryPath);
            string? keyToRemove = null;

            foreach (var (key, folderNode) in libraryFolders.Children)
            {
                if (!int.TryParse(key, out _))
                    continue;

                var existingPath = folderNode["path"];
                if (string.IsNullOrEmpty(existingPath)) continue;

                var normalizedExisting = PathHelper.NormalizePath(existingPath.Replace('/', '\\'));
                if (string.Equals(normalizedExisting, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    keyToRemove = key;
                    break;
                }
            }

            if (keyToRemove is not null)
            {
                libraryFolders.Children.Remove(keyToRemove);
                _log.Information("Removed empty library entry {Key} ({Path}) from libraryfolders.vdf",
                    keyToRemove, libraryPath);

                // Re-number remaining entries (Steam expects 0, 1, 2...)
                var remaining = libraryFolders.Children
                    .Where(kv => int.TryParse(kv.Key, out _))
                    .OrderBy(kv => int.Parse(kv.Key))
                    .Select(kv => kv.Value)
                    .ToList();

                // Remove all numbered entries
                foreach (var key in libraryFolders.Children.Keys
                    .Where(k => int.TryParse(k, out _)).ToList())
                {
                    libraryFolders.Children.Remove(key);
                }

                // Re-add with sequential numbering
                for (var i = 0; i < remaining.Count; i++)
                {
                    libraryFolders.Children[i.ToString()] = remaining[i];
                }

                var serialized = "\"libraryfolders\"\n{\n" +
                                 VdfParser.Serialize(libraryFolders, 1) + "}\n";
                File.WriteAllText(vdfPath, serialized);

                _log.Information("libraryfolders.vdf updated: removed old library, re-numbered entries");
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to remove library {Path} from libraryfolders.vdf", libraryPath);
        }
    }

    private void UpdateLibraryFoldersVdf(string steamPath, string oldLibraryPath, string newLibraryPath, string appId, long sizeBytes)
    {
        try
        {
            var vdfPath = Path.Combine(steamPath, "config", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                return;

            // Backup the original file
            var backupPath = vdfPath + ".bak";
            File.Copy(vdfPath, backupPath, overwrite: true);

            var content = File.ReadAllText(vdfPath);
            var root = VdfParser.Parse(content);

            var libraryFolders = root.Children.ContainsKey("libraryfolders")
                ? root.Children["libraryfolders"]
                : root;

            var normalizedOld = PathHelper.NormalizePath(oldLibraryPath);
            var normalizedNew = PathHelper.NormalizePath(newLibraryPath);

            // Remove app from old library's apps section
            foreach (var (_, folderNode) in libraryFolders.Children)
            {
                var existingPath = folderNode["path"];
                if (string.IsNullOrEmpty(existingPath)) continue;

                var normalizedExisting = PathHelper.NormalizePath(existingPath.Replace('/', '\\'));
                if (string.Equals(normalizedExisting, normalizedOld, StringComparison.OrdinalIgnoreCase))
                {
                    if (folderNode.Children.TryGetValue("apps", out var apps))
                    {
                        apps.Children.Remove(appId);
                        _log.Information("Removed app {AppId} from old library {Path}", appId, oldLibraryPath);
                    }
                    break;
                }
            }

            // Find or create new library entry
            VdfNode? newFolderNode = null;
            foreach (var (_, folderNode) in libraryFolders.Children)
            {
                var existingPath = folderNode["path"];
                if (string.IsNullOrEmpty(existingPath)) continue;

                var normalizedExisting = PathHelper.NormalizePath(existingPath.Replace('/', '\\'));
                if (string.Equals(normalizedExisting, normalizedNew, StringComparison.OrdinalIgnoreCase))
                {
                    newFolderNode = folderNode;
                    break;
                }
            }

            if (newFolderNode is null)
            {
                // Find next available index
                var nextIndex = 0;
                foreach (var key in libraryFolders.Children.Keys)
                {
                    if (int.TryParse(key, out var idx) && idx >= nextIndex)
                        nextIndex = idx + 1;
                }

                // Generate a contentid like Steam does
                var contentId = Random.Shared.NextInt64(1000000000000000000, long.MaxValue);

                // Get drive total size
                long totalSize = 0;
                var driveRoot = Path.GetPathRoot(newLibraryPath);
                if (driveRoot is not null)
                {
                    try { totalSize = new DriveInfo(driveRoot).TotalSize; } catch { }
                }

                newFolderNode = new VdfNode();
                newFolderNode.Children["path"] = new VdfNode { Value = newLibraryPath.Replace('\\', '/') };
                newFolderNode.Children["label"] = new VdfNode { Value = "" };
                newFolderNode.Children["contentid"] = new VdfNode { Value = contentId.ToString() };
                newFolderNode.Children["totalsize"] = new VdfNode { Value = totalSize.ToString() };
                newFolderNode.Children["update_clean_bytes_tally"] = new VdfNode { Value = "0" };
                newFolderNode.Children["time_last_update_verified"] = new VdfNode { Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() };
                newFolderNode.Children["apps"] = new VdfNode();

                libraryFolders.Children[nextIndex.ToString()] = newFolderNode;
                _log.Information("Created new library folder entry for {Path}", newLibraryPath);
            }

            // Add app to new library's apps section
            if (!newFolderNode.Children.ContainsKey("apps"))
            {
                newFolderNode.Children["apps"] = new VdfNode();
            }
            newFolderNode.Children["apps"].Children[appId] = new VdfNode { Value = sizeBytes.ToString() };
            _log.Information("Added app {AppId} to new library {Path}", appId, newLibraryPath);

            // Serialize and write back
            var serialized = "\"libraryfolders\"\n{\n" +
                             VdfParser.Serialize(libraryFolders, 1) + "}\n";
            File.WriteAllText(vdfPath, serialized);

            _log.Information("Updated libraryfolders.vdf successfully");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to update libraryfolders.vdf");
        }
    }
}
