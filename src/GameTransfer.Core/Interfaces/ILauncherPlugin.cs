using GameTransfer.Core.Models;

namespace GameTransfer.Core.Interfaces;

public interface ILauncherPlugin
{
    string LauncherName { get; }
    LauncherType Type { get; }
    bool IsInstalled();
    Task<IReadOnlyList<GameInfo>> DetectInstalledGamesAsync();
    Task UpdateGamePathAsync(GameInfo game, string newInstallPath);
    bool SupportsDirectReconfiguration { get; }
    bool IsLauncherRunning();
    string LauncherProcessName { get; }

    /// <summary>
    /// Called after all games of this launcher have been imported.
    /// Allows cleanup of old library folders (e.g. removing empty Steam libraries).
    /// </summary>
    Task PostImportCleanupAsync(IReadOnlyList<GameInfo> movedGames) => Task.CompletedTask;

    /// <summary>
    /// Returns the native library install path for the given drive, if the launcher has one there.
    /// For example, Steam returns its default steamapps\common path on C:.
    /// Returns null to use the GLM Library path instead.
    /// </summary>
    string? GetNativeLibraryPath(string driveRoot) => null;

    /// <summary>
    /// Uninstalls a game by removing its files and launcher-specific configuration.
    /// </summary>
    Task UninstallGameAsync(GameInfo game);
}
