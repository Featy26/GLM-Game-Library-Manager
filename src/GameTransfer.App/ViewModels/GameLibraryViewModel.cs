using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;

namespace GameTransfer.App.ViewModels;

public partial class GameLibraryViewModel : ObservableObject
{
    private readonly IEnumerable<ILauncherPlugin> _plugins;
    private readonly List<GameItemViewModel> _allGames = new();

    [ObservableProperty]
    private ObservableCollection<GameItemViewModel> _games = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedLauncherFilter = "Alle";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Bereit";

    public IReadOnlyList<string> LauncherFilters { get; } = new[]
    {
        "Alle", "Steam", "Epic Games", "GOG", "Ubisoft Connect", "EA App", "Battle.net"
    };

    public IReadOnlyList<GameItemViewModel> SelectedGames =>
        Games.Where(g => g.IsSelected).ToList();

    public GameLibraryViewModel(IEnumerable<ILauncherPlugin> plugins)
    {
        _plugins = plugins;
    }

    [RelayCommand]
    public async Task LoadGamesAsync()
    {
        IsLoading = true;
        StatusText = "Spiele werden erkannt...";
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
                    _allGames.Add(new GameItemViewModel(game));
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to detect games from {Launcher}", plugin.LauncherName);
            }
        }

        ApplyFilter();
        IsLoading = false;
        StatusText = $"{_allGames.Count} Spiele gefunden";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLauncherFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = _allGames.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(g =>
                g.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedLauncherFilter != "Alle")
        {
            filtered = filtered.Where(g =>
            {
                return SelectedLauncherFilter switch
                {
                    "Steam" => g.Launcher == LauncherType.Steam,
                    "Epic Games" => g.Launcher == LauncherType.EpicGames,
                    "GOG" => g.Launcher == LauncherType.GOG,
                    "Ubisoft Connect" => g.Launcher == LauncherType.UbisoftConnect,
                    "EA App" => g.Launcher == LauncherType.EAApp,
                    "Battle.net" => g.Launcher == LauncherType.BattleNet,
                    _ => true
                };
            });
        }

        Games = new ObservableCollection<GameItemViewModel>(filtered.OrderBy(g => g.Name));
    }

    [ObservableProperty]
    private bool _isAllSelected;

    partial void OnIsAllSelectedChanged(bool value)
    {
        foreach (var game in Games)
            game.IsSelected = value;
    }

    [RelayCommand]
    private void SelectAll()
    {
        IsAllSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        IsAllSelected = false;
    }
}
