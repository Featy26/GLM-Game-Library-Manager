using GameTransfer.Core.Models;

namespace GameTransfer.Core.Interfaces;

public interface IFileTransferService
{
    Task<TransferResult> MoveDirectoryAsync(
        string sourcePath,
        string destinationPath,
        IProgress<TransferProgress> progress,
        CancellationToken cancellationToken);

    Task<bool> VerifyDirectoryAsync(
        string sourcePath,
        string destinationPath,
        IProgress<TransferProgress> progress,
        CancellationToken cancellationToken);

    void Pause();
    void Resume();
}
