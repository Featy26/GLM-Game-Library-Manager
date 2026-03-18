using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;

namespace GameTransfer.App.ViewModels;

public partial class DriveManagerViewModel : ObservableObject
{
    private readonly IEnumerable<ILauncherPlugin> _plugins;

    [ObservableProperty]
    private ObservableCollection<DriveItemViewModel> _drives = new();

    [ObservableProperty]
    private DriveItemViewModel? _selectedDrive;

    public DriveManagerViewModel(IEnumerable<ILauncherPlugin> plugins)
    {
        _plugins = plugins;
    }

    [RelayCommand]
    public async Task RefreshDrivesAsync()
    {
        Drives.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
            Drives.Add(new DriveItemViewModel(drive));
        }
        if (Drives.Count > 0 && SelectedDrive is null)
            SelectedDrive = Drives[0];

        await UpdateLauncherUsageAsync();
    }

    public async Task UpdateLauncherUsageAsync()
    {
        // Detect all games to calculate per-launcher usage per drive
        var allGames = new List<GameInfo>();
        foreach (var plugin in _plugins)
        {
            try
            {
                if (!plugin.IsInstalled()) continue;
                var games = await plugin.DetectInstalledGamesAsync();
                allGames.AddRange(games);
            }
            catch { }
        }

        foreach (var drive in Drives)
        {
            drive.UpdateLauncherUsage(allGames);
        }
    }

    public string? GetSelectedDrivePath()
    {
        if (SelectedDrive is null) return null;
        return Path.Combine(SelectedDrive.RootPath, "GLM Library");
    }
}

public partial class DriveItemViewModel : ObservableObject
{
    public string Name { get; }
    public string RootPath { get; }
    public long TotalBytes { get; }
    public long FreeBytes { get; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercentage => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
    public string FreeText => $"{FreeBytes / (1024.0 * 1024 * 1024):F1} GB frei";
    public string TotalText => $"{TotalBytes / (1024.0 * 1024 * 1024):F1} GB gesamt";
    public string DisplayName => $"{Name} ({FreeText} von {TotalText})";

    [ObservableProperty]
    private ObservableCollection<LauncherUsageSegment> _launcherUsage = new();

    public DriveItemViewModel(DriveInfo drive)
    {
        Name = drive.Name.TrimEnd('\\');
        RootPath = drive.RootDirectory.FullName;
        TotalBytes = drive.TotalSize;
        FreeBytes = drive.AvailableFreeSpace;
    }

    public void UpdateLauncherUsage(IReadOnlyList<GameInfo> allGames)
    {
        var segments = new ObservableCollection<LauncherUsageSegment>();
        var driveRoot = RootPath.TrimEnd('\\').ToUpperInvariant();

        var gamesOnDrive = allGames.Where(g =>
        {
            var gameRoot = Path.GetPathRoot(g.InstallPath)?.TrimEnd('\\').ToUpperInvariant();
            return gameRoot == driveRoot;
        }).ToList();

        var byLauncher = gamesOnDrive
            .GroupBy(g => g.Launcher)
            .Select(g => new { Launcher = g.Key, TotalSize = g.Sum(x => x.SizeBytes) })
            .OrderByDescending(g => g.TotalSize)
            .ToList();

        foreach (var group in byLauncher)
        {
            var percentage = TotalBytes > 0 ? (double)group.TotalSize / TotalBytes * 100 : 0;
            if (percentage < 0.1) continue; // Skip tiny entries

            segments.Add(new LauncherUsageSegment
            {
                LauncherName = GetLauncherDisplayName(group.Launcher),
                SizeBytes = group.TotalSize,
                SizeText = FormatSize(group.TotalSize),
                Percentage = percentage,
                Color = GetLauncherColor(group.Launcher)
            });
        }

        // Add "Other used" for non-game usage
        var gameTotal = byLauncher.Sum(g => g.TotalSize);
        var otherUsed = UsedBytes - gameTotal;
        if (otherUsed > 0)
        {
            var otherPercentage = TotalBytes > 0 ? (double)otherUsed / TotalBytes * 100 : 0;
            segments.Add(new LauncherUsageSegment
            {
                LauncherName = "Sonstiges",
                SizeBytes = otherUsed,
                SizeText = FormatSize(otherUsed),
                Percentage = otherPercentage,
                Color = "#666666"
            });
        }

        // Free space
        if (FreeBytes > 0)
        {
            var freePercentage = TotalBytes > 0 ? (double)FreeBytes / TotalBytes * 100 : 0;
            segments.Add(new LauncherUsageSegment
            {
                LauncherName = "Frei",
                SizeBytes = FreeBytes,
                SizeText = FormatSize(FreeBytes),
                Percentage = freePercentage,
                Color = "#333333"
            });
        }

        LauncherUsage = segments;
    }

    private static string GetLauncherDisplayName(LauncherType launcher) => launcher switch
    {
        LauncherType.Steam => "Steam",
        LauncherType.EpicGames => "Epic Games",
        LauncherType.GOG => "GOG",
        LauncherType.UbisoftConnect => "Ubisoft",
        LauncherType.EAApp => "EA",
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

public class LauncherUsageSegment
{
    public required string LauncherName { get; init; }
    public long SizeBytes { get; init; }
    public required string SizeText { get; init; }
    public double Percentage { get; init; }
    public required string Color { get; init; }
}
