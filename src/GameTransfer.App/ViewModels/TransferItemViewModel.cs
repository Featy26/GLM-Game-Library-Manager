using CommunityToolkit.Mvvm.ComponentModel;
using GameTransfer.Core.Models;

namespace GameTransfer.App.ViewModels;

public partial class TransferItemViewModel : ObservableObject
{
    public TransferJob Job { get; }

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "Wartend...";

    [ObservableProperty]
    private TransferPhase _phase = TransferPhase.Preflight;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private bool _hasFailed;

    public string GameName => Job.Game.Name;
    public string LauncherName => Job.Game.Launcher.ToString();
    public string SourcePath => Job.Game.InstallPath;
    public string DestinationPath => Job.DestinationPath;

    public TransferItemViewModel(TransferJob job)
    {
        Job = job;
    }

    public void UpdateProgress(TransferProgress p)
    {
        Phase = p.Phase;
        Progress = p.Percentage;
        StatusText = p.Phase switch
        {
            TransferPhase.Preflight => "Prüfe Voraussetzungen...",
            TransferPhase.Copying => $"Kopiere... {p.Percentage:F0}% ({p.CurrentFile ?? ""})",
            TransferPhase.Verifying => "Verifiziere Dateien...",
            TransferPhase.UpdatingReferences => "Aktualisiere Verweise...",
            TransferPhase.CreatingSymlink => "Erstelle Verknüpfung...",
            TransferPhase.Cleanup => "Räume auf...",
            TransferPhase.Completed => "Abgeschlossen",
            TransferPhase.Failed => "Fehlgeschlagen",
            TransferPhase.RolledBack => "Zurückgesetzt",
            _ => "..."
        };

        if (p.Phase == TransferPhase.Completed)
        {
            IsComplete = true;
            Progress = 100;
        }
        else if (p.Phase is TransferPhase.Failed or TransferPhase.RolledBack)
        {
            HasFailed = true;
        }
    }
}
