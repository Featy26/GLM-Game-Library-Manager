using System.Diagnostics;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using Serilog;

namespace GameTransfer.Plugins.Base;

public abstract class LauncherPluginBase : ILauncherPlugin
{
    protected readonly IRegistryService _registry;
    protected readonly ILogger _log;

    protected LauncherPluginBase(IRegistryService registry)
    {
        _registry = registry;
        _log = Log.ForContext(GetType());
    }

    public abstract string LauncherName { get; }
    public abstract LauncherType Type { get; }
    public abstract bool SupportsDirectReconfiguration { get; }

    public abstract string LauncherProcessName { get; }

    public abstract bool IsInstalled();
    public abstract Task<IReadOnlyList<GameInfo>> DetectInstalledGamesAsync();
    public abstract Task UpdateGamePathAsync(GameInfo game, string newInstallPath);

    /// <summary>
    /// Uninstalls a game. Base implementation deletes the install directory.
    /// Override to also clean up launcher-specific config (manifests, registry, etc.).
    /// </summary>
    public virtual async Task UninstallGameAsync(GameInfo game)
    {
        await Task.Run(() =>
        {
            if (Directory.Exists(game.InstallPath))
            {
                _log.Information("Deleting game directory {Path}", game.InstallPath);
                Directory.Delete(game.InstallPath, recursive: true);
            }
        });
    }

    public bool IsLauncherRunning() => IsProcessRunning(LauncherProcessName);

    protected bool IsProcessRunning(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            var running = processes.Length > 0;
            foreach (var p in processes)
                p.Dispose();
            return running;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to check if process {ProcessName} is running", processName);
            return false;
        }
    }
}
