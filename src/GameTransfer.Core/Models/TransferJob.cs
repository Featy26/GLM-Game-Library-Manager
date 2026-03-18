namespace GameTransfer.Core.Models;

public class TransferJob
{
    public required GameInfo Game { get; init; }
    public required string DestinationPath { get; init; }
    public TransferPhase State { get; set; } = TransferPhase.Preflight;
    public string? OriginalPath { get; set; }
    public bool CreateSymlinkFallback { get; init; }
}
