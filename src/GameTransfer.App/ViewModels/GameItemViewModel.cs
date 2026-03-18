using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GameTransfer.App.Helpers;
using GameTransfer.Core.Models;

namespace GameTransfer.App.ViewModels;

public partial class GameItemViewModel : ObservableObject
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

    private ImageSource? _iconSource;
    public ImageSource? IconSource => _iconSource ??= IconHelper.GetGameIcon(Game.InstallPath, Game.ExecutablePath);

    public GameItemViewModel(GameInfo game)
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
