using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;

namespace GameTransfer.App.ViewModels;

public partial class LibraryImportViewModel : ObservableObject
{
    private readonly IEnumerable<ILauncherPlugin> _plugins;
    private readonly List<ImportGameItemViewModel> _allGames = new();

    [ObservableProperty]
    private ObservableCollection<LauncherImportGroup> _launcherGroups = new();

    [ObservableProperty]
    private ObservableCollection<ImportGameItemViewModel> _managedGames = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Bereit";

    /// <summary>
    /// Returns all games from selected launchers that are not yet in the library system.
    /// Excludes Steam games on C: (those stay in the default Steam library).
    /// </summary>
    public IReadOnlyList<ImportGameItemViewModel> SelectedUnmanagedGames =>
        _allGames.Where(g => !IsInLibrarySystem(g.Game.InstallPath) &&
            !IsExcludedFromImport(g) &&
            LauncherGroups.Any(lg => lg.IsSelected && lg.LauncherType == g.Launcher))
            .ToList();

    public LibraryImportViewModel(IEnumerable<ILauncherPlugin> plugins)
    {
        _plugins = plugins;
    }

    [RelayCommand]
    public async Task ScanGamesAsync()
    {
        IsLoading = true;
        StatusText = "Spiele werden gescannt...";
        _allGames.Clear();

        foreach (var plugin in _plugins)
        {
            try
            {
                if (!plugin.IsInstalled())
                    continue;

                StatusText = $"{plugin.LauncherName} wird gescannt...";
                var games = await plugin.DetectInstalledGamesAsync();
                foreach (var game in games)
                {
                    _allGames.Add(new ImportGameItemViewModel(game));
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to detect games from {Launcher}", plugin.LauncherName);
            }
        }

        CategorizeGames();
        IsLoading = false;
    }

    private void CategorizeGames()
    {
        var managed = new ObservableCollection<ImportGameItemViewModel>();
        var groups = new ObservableCollection<LauncherImportGroup>();

        // Exclude Steam games on C: from import — they stay in the default Steam library
        var unmanagedGames = _allGames
            .Where(g => !IsInLibrarySystem(g.Game.InstallPath) && !IsExcludedFromImport(g))
            .ToList();
        var managedGames = _allGames.Where(g => IsInLibrarySystem(g.Game.InstallPath)).OrderBy(g => g.Name).ToList();

        // Group unmanaged games by launcher
        var byLauncher = unmanagedGames
            .GroupBy(g => g.Launcher)
            .OrderBy(g => g.Key.ToString())
            .ToList();

        foreach (var group in byLauncher)
        {
            var totalSize = group.Sum(g => g.SizeBytes);
            var gameCount = group.Count();

            groups.Add(new LauncherImportGroup
            {
                LauncherType = group.Key,
                LauncherName = GetLauncherDisplayName(group.Key),
                GameCount = gameCount,
                TotalSizeBytes = totalSize,
                SizeText = FormatSize(totalSize),
                EstimatedTime = EstimateTransferTime(totalSize),
                Color = GetLauncherColor(group.Key),
                Games = group.OrderBy(g => g.Name).ToList()
            });
        }

        foreach (var game in managedGames)
            managed.Add(game);

        LauncherGroups = groups;
        ManagedGames = managed;

        var totalUnmanaged = unmanagedGames.Count;
        StatusText = $"{_allGames.Count} Spiele gefunden — {managed.Count} im Library-System, {totalUnmanaged} noch nicht";
    }

    private static bool IsInLibrarySystem(string installPath)
    {
        var normalized = installPath.Replace('/', '\\');
        return normalized.Contains("GLM Library", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Steam games on C: are excluded from import — they stay in the default Steam library.
    /// </summary>
    private static bool IsExcludedFromImport(ImportGameItemViewModel game)
    {
        if (game.Launcher != LauncherType.Steam)
            return false;

        var root = Path.GetPathRoot(game.Game.InstallPath);
        return string.Equals(root, @"C:\", StringComparison.OrdinalIgnoreCase);
    }

    private static string EstimateTransferTime(long totalBytes)
    {
        // Estimate based on ~150 MB/s copy speed (typical for same-drive copy)
        const double bytesPerSecond = 150.0 * 1024 * 1024;
        var seconds = totalBytes / bytesPerSecond;

        if (seconds < 60)
            return "< 1 Min.";
        if (seconds < 3600)
            return $"~{(int)Math.Ceiling(seconds / 60)} Min.";
        var hours = seconds / 3600;
        return $"~{hours:F1} Std.";
    }

    private static string GetLauncherDisplayName(LauncherType launcher) => launcher switch
    {
        LauncherType.Steam => "Steam",
        LauncherType.EpicGames => "Epic Games",
        LauncherType.GOG => "GOG Galaxy",
        LauncherType.UbisoftConnect => "Ubisoft Connect",
        LauncherType.EAApp => "EA App",
        LauncherType.BattleNet => "Battle.net",
        _ => "Andere"
    };

    private static string GetLauncherColor(LauncherType launcher) => launcher switch
    {
        LauncherType.Steam => "#1B2838",
        LauncherType.EpicGames => "#0078F2",
        LauncherType.GOG => "#A348B5",
        LauncherType.UbisoftConnect => "#0070FF",
        LauncherType.EAApp => "#FF4747",
        LauncherType.BattleNet => "#00AEFF",
        _ => "#888888"
    };

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}

public partial class LauncherImportGroup : ObservableObject
{
    public required LauncherType LauncherType { get; init; }
    public required string LauncherName { get; init; }
    public int GameCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public required string SizeText { get; init; }
    public required string EstimatedTime { get; init; }
    public required string Color { get; init; }
    public required IReadOnlyList<ImportGameItemViewModel> Games { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}

public partial class ImportGameItemViewModel : ObservableObject
{
    public GameInfo Game { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string Name => Game.Name;
    public string InstallPath => Game.InstallPath;
    public long SizeBytes => Game.SizeBytes;
    public string SizeText => FormatSize(Game.SizeBytes);
    public LauncherType Launcher => Game.Launcher;
    public string LauncherName => Game.Launcher.ToString();

    private System.Windows.Media.ImageSource? _iconSource;
    public System.Windows.Media.ImageSource? IconSource =>
        _iconSource ??= Helpers.IconHelper.GetGameIcon(Game.InstallPath, Game.ExecutablePath);

    public ImportGameItemViewModel(GameInfo game)
    {
        Game = game;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}
