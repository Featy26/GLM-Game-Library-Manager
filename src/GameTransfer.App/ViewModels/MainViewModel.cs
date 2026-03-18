using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTransfer.App.Helpers;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using GameTransfer.Plugins.Steam;

namespace GameTransfer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IEnumerable<ILauncherPlugin> _plugins;

    public GameLibraryViewModel Library { get; }
    public TransferViewModel Transfer { get; }
    public DriveManagerViewModel DriveManager { get; }
    public LibraryImportViewModel LibraryImport { get; }

    [ObservableProperty]
    private bool _createSymlinks = true;

    public bool IsAdmin => AdminHelper.IsRunningAsAdmin();

    public MainViewModel(
        GameLibraryViewModel library,
        TransferViewModel transfer,
        DriveManagerViewModel driveManager,
        LibraryImportViewModel libraryImport,
        IEnumerable<ILauncherPlugin> plugins)
    {
        Library = library;
        Transfer = transfer;
        DriveManager = driveManager;
        LibraryImport = libraryImport;
        _plugins = plugins;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await DriveManager.RefreshDrivesAsync();
        await Library.LoadGamesAsync();
        await LibraryImport.ScanGamesAsync();
    }

    [RelayCommand]
    private async Task ImportSelectedLaunchersAsync()
    {
        var selectedGames = LibraryImport.SelectedUnmanagedGames;
        if (selectedGames.Count == 0)
        {
            MessageBox.Show(
                "Bitte wähle mindestens einen Launcher aus.",
                "Kein Launcher ausgewählt",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Check for admin rights
        if (!AdminHelper.IsRunningAsAdmin())
        {
            var result = MessageBox.Show(
                "Zum Importieren von Spielen werden Administrator-Rechte benötigt.\n\nSoll die Anwendung als Administrator neu gestartet werden?",
                "Administrator-Rechte erforderlich",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (AdminHelper.RestartAsAdmin())
                    Application.Current.Shutdown();
            }
            return;
        }

        // Group games by their current drive and import each to the same drive's library
        var gamesByDrive = selectedGames.GroupBy(g =>
            Path.GetPathRoot(g.Game.InstallPath)?.TrimEnd('\\') ?? "C:");

        foreach (var driveGroup in gamesByDrive)
        {
            var destPath = Path.Combine(driveGroup.Key + "\\", "GLM Library");
            var gameItems = driveGroup.Select(g => new GameItemViewModel(g.Game)).ToList();
            Transfer.QueueTransfers(gameItems, destPath, CreateSymlinks);
        }

        await Transfer.StartTransfersAsync();

        // Post-import cleanup: remove empty old library folders (e.g. Steam)
        var gamesByLauncher = selectedGames.GroupBy(g => g.Launcher);
        foreach (var launcherGroup in gamesByLauncher)
        {
            var plugin = _plugins.FirstOrDefault(p => p.Type == launcherGroup.Key);
            if (plugin is not null)
            {
                var movedGameInfos = launcherGroup.Select(g => g.Game).ToList();
                await plugin.PostImportCleanupAsync(movedGameInfos);
            }
        }

        // Refresh after import
        await LibraryImport.ScanGamesAsync();
        await Library.LoadGamesAsync();
    }

    [RelayCommand]
    private async Task CleanEmptySteamLibrariesAsync()
    {
        var steamPlugin = _plugins.OfType<SteamPlugin>().FirstOrDefault();
        if (steamPlugin is null)
        {
            MessageBox.Show("Steam ist nicht installiert.", "Steam nicht gefunden",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (steamPlugin.IsLauncherRunning())
        {
            MessageBox.Show(
                "Steam läuft noch. Bitte schließe Steam bevor du leere Libraries löschst.",
                "Steam aktiv", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var removed = await steamPlugin.CleanEmptyLibrariesAsync();

        if (removed.Count == 0)
        {
            MessageBox.Show("Keine leeren Steam Libraries gefunden.",
                "Bereinigung", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var paths = string.Join("\n", removed.Select(p => $"  • {p}"));
            MessageBox.Show(
                $"{removed.Count} leere Steam Library(s) entfernt:\n\n{paths}",
                "Bereinigung abgeschlossen", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        await LibraryImport.ScanGamesAsync();
        await DriveManager.RefreshDrivesAsync();
    }

    [RelayCommand]
    private async Task MoveSelectedGamesAsync()
    {
        var selectedGames = Library.SelectedGames;
        if (selectedGames.Count == 0) return;

        var destPath = DriveManager.GetSelectedDrivePath();
        if (destPath is null) return;

        // Check for admin rights - needed for registry and junction creation
        if (!AdminHelper.IsRunningAsAdmin())
        {
            var result = MessageBox.Show(
                "Zum Verschieben von Spielen werden Administrator-Rechte benötigt (für Registry-Änderungen und Verknüpfungen).\n\nSoll die Anwendung als Administrator neu gestartet werden?",
                "Administrator-Rechte erforderlich",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (AdminHelper.RestartAsAdmin())
                    Application.Current.Shutdown();
            }
            return;
        }

        Transfer.QueueTransfers(selectedGames, destPath, CreateSymlinks);
        await Transfer.StartTransfersAsync();

        // Refresh game list after transfers
        await Library.LoadGamesAsync();
    }

    [RelayCommand]
    private async Task UninstallSelectedGamesAsync()
    {
        var selectedGames = Library.SelectedGames;
        if (selectedGames.Count == 0) return;

        var totalSize = selectedGames.Sum(g => g.SizeBytes);
        var sizeText = totalSize >= 1024L * 1024 * 1024
            ? $"{totalSize / (1024.0 * 1024 * 1024):F1} GB"
            : $"{totalSize / (1024.0 * 1024):F0} MB";

        var gameNames = string.Join("\n", selectedGames.Select(g => $"  • {g.Name} ({g.LauncherName})"));

        var result = MessageBox.Show(
            $"Folgende {selectedGames.Count} Spiel(e) werden deinstalliert ({sizeText}):\n\n{gameNames}\n\nDieser Vorgang kann nicht rückgängig gemacht werden!",
            "Spiele deinstallieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        // Check launcher running
        var launcherTypes = selectedGames.Select(g => g.Launcher).Distinct();
        foreach (var launcherType in launcherTypes)
        {
            var plugin = _plugins.FirstOrDefault(p => p.Type == launcherType);
            if (plugin is not null && plugin.IsLauncherRunning())
            {
                MessageBox.Show(
                    $"{plugin.LauncherName} läuft noch. Bitte schließe {plugin.LauncherName} vor der Deinstallation.",
                    "Launcher aktiv",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        var errors = new List<string>();
        foreach (var game in selectedGames)
        {
            var plugin = _plugins.FirstOrDefault(p => p.Type == game.Launcher);
            if (plugin is null)
            {
                errors.Add($"{game.Name}: Kein Plugin gefunden");
                continue;
            }

            try
            {
                await plugin.UninstallGameAsync(game.Game);
            }
            catch (Exception ex)
            {
                errors.Add($"{game.Name}: {ex.Message}");
                Serilog.Log.Error(ex, "Failed to uninstall {Game}", game.Name);
            }
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(
                $"Einige Spiele konnten nicht vollständig deinstalliert werden:\n\n{string.Join("\n", errors)}",
                "Fehler bei Deinstallation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // Refresh
        await Library.LoadGamesAsync();
        await DriveManager.RefreshDrivesAsync();
    }
}
