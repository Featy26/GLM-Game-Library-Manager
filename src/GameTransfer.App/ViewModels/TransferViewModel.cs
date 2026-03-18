using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameTransfer.Core.Models;
using GameTransfer.Core.Services;

namespace GameTransfer.App.ViewModels;

public partial class TransferViewModel : ObservableObject
{
    private readonly TransferOrchestrator _orchestrator;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private ObservableCollection<TransferItemViewModel> _transfers = new();

    [ObservableProperty]
    private bool _isTransferring;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _pauseButtonText = "Pause";

    [ObservableProperty]
    private string _overallStatus = "Keine aktiven Transfers";

    [ObservableProperty]
    private double _overallProgress;

    public TransferViewModel(TransferOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public void QueueTransfers(IEnumerable<GameItemViewModel> games, string destinationPath, bool createSymlinks)
    {
        foreach (var game in games)
        {
            var job = new TransferJob
            {
                Game = game.Game,
                DestinationPath = destinationPath,
                CreateSymlinkFallback = createSymlinks
            };
            Transfers.Add(new TransferItemViewModel(job));
        }
    }

    [RelayCommand]
    public async Task StartTransfersAsync()
    {
        if (Transfers.Count == 0) return;

        IsTransferring = true;
        IsPaused = false;
        PauseButtonText = "Pause";
        _cts = new CancellationTokenSource();
        var completed = 0;
        var failed = 0;
        var totalCount = Transfers.Count;
        var snapshot = Transfers.ToList();

        foreach (var transfer in snapshot)
        {
            if (_cts.Token.IsCancellationRequested) break;
            if (transfer.IsComplete) { completed++; continue; }

            OverallStatus = $"Verschiebe {transfer.GameName} ({completed + failed + 1}/{totalCount})";

            var progress = new Progress<TransferProgress>(p =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (transfer.HasFailed) return; // Don't overwrite error message
                    transfer.UpdateProgress(p);
                    OverallProgress = (completed + p.Percentage / 100.0) / totalCount * 100;
                });
            });

            var result = await _orchestrator.ExecuteTransferAsync(transfer.Job, progress, _cts.Token);

            if (result.Success)
            {
                completed++;
                Transfers.Remove(transfer);
            }
            else
            {
                failed++;
                transfer.HasFailed = true;
                transfer.StatusText = result.ErrorMessage ?? "Unbekannter Fehler";
            }
        }

        IsTransferring = false;
        OverallProgress = 100;

        if (failed == 0)
            OverallStatus = $"Fertig: alle {completed} Transfers erfolgreich";
        else if (completed == 0)
            OverallStatus = $"Fertig: {failed} von {totalCount} fehlgeschlagen";
        else
            OverallStatus = $"Fertig: {completed} erfolgreich, {failed} fehlgeschlagen";
    }

    [RelayCommand]
    private void PauseResume()
    {
        if (!IsTransferring) return;

        if (IsPaused)
        {
            _orchestrator.ResumeTransfer();
            IsPaused = false;
            PauseButtonText = "Pause";
        }
        else
        {
            _orchestrator.PauseTransfer();
            IsPaused = true;
            PauseButtonText = "Fortsetzen";
        }
    }

    [RelayCommand]
    private void CancelTransfers()
    {
        _cts?.Cancel();
        IsTransferring = false;
        OverallStatus = "Transfer abgebrochen";
    }

}
