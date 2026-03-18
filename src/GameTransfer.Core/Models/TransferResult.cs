namespace GameTransfer.Core.Models;

public class TransferResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? NewInstallPath { get; init; }
    public TimeSpan Duration { get; init; }
    public long BytesTransferred { get; init; }

    public static TransferResult Succeeded(string newPath, TimeSpan duration, long bytes) => new()
    {
        Success = true,
        NewInstallPath = newPath,
        Duration = duration,
        BytesTransferred = bytes
    };

    public static TransferResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}
