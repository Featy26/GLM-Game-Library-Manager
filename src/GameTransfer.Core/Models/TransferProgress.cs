namespace GameTransfer.Core.Models;

public class TransferProgress
{
    public long BytesCopied { get; init; }
    public long TotalBytes { get; init; }
    public int FilesCopied { get; init; }
    public int TotalFiles { get; init; }
    public string? CurrentFile { get; init; }
    public TransferPhase Phase { get; init; }
    public double Percentage => TotalBytes > 0 ? (double)BytesCopied / TotalBytes * 100 : 0;
}

public enum TransferPhase
{
    Preflight,
    Copying,
    Verifying,
    UpdatingReferences,
    CreatingSymlink,
    Cleanup,
    Completed,
    Failed,
    RolledBack
}
