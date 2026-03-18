using System.Diagnostics;
using GameTransfer.Core.Interfaces;
using GameTransfer.Core.Models;
using Serilog;

namespace GameTransfer.Core.Services;

public class FileTransferService : IFileTransferService
{
    private readonly ManualResetEventSlim _pauseEvent = new(initialState: true);

    public async Task<TransferResult> MoveDirectoryAsync(
        string sourcePath,
        string destinationPath,
        IProgress<TransferProgress> progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        long totalBytesCopied = 0;

        try
        {
            var sourceDir = new DirectoryInfo(sourcePath);
            if (!sourceDir.Exists)
            {
                return TransferResult.Failed($"Source directory does not exist: {sourcePath}");
            }

            var allFiles = sourceDir.GetFiles("*", SearchOption.AllDirectories);
            var totalBytes = allFiles.Sum(f => f.Length);
            var totalFiles = allFiles.Length;
            var filesCopied = 0;

            Log.Information("Starting transfer of {FileCount} files ({TotalBytes} bytes) from {Source} to {Destination}",
                totalFiles, totalBytes, sourcePath, destinationPath);

            foreach (var sourceFile in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _pauseEvent.Wait(cancellationToken);

                var relativePath = Path.GetRelativePath(sourcePath, sourceFile.FullName);
                var destFile = Path.Combine(destinationPath, relativePath);
                var destDir = Path.GetDirectoryName(destFile)!;

                Directory.CreateDirectory(destDir);

                progress.Report(new TransferProgress
                {
                    BytesCopied = totalBytesCopied,
                    TotalBytes = totalBytes,
                    FilesCopied = filesCopied,
                    TotalFiles = totalFiles,
                    CurrentFile = relativePath,
                    Phase = TransferPhase.Copying
                });

                await CopyFileAsync(sourceFile.FullName, destFile, cancellationToken);

                totalBytesCopied += sourceFile.Length;
                filesCopied++;
            }

            progress.Report(new TransferProgress
            {
                BytesCopied = totalBytesCopied,
                TotalBytes = totalBytes,
                FilesCopied = filesCopied,
                TotalFiles = totalFiles,
                Phase = TransferPhase.Copying
            });

            stopwatch.Stop();
            Log.Information("Transfer completed in {Duration}. {Bytes} bytes transferred", stopwatch.Elapsed, totalBytesCopied);

            return TransferResult.Succeeded(destinationPath, stopwatch.Elapsed, totalBytesCopied);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Transfer was cancelled");
            return TransferResult.Failed("Transfer was cancelled by the user.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Transfer failed");
            return TransferResult.Failed(ex.Message);
        }
    }

    public async Task<bool> VerifyDirectoryAsync(
        string sourcePath,
        string destinationPath,
        IProgress<TransferProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourceDir = new DirectoryInfo(sourcePath);
            var destDir = new DirectoryInfo(destinationPath);

            if (!sourceDir.Exists || !destDir.Exists)
            {
                Log.Warning("Verification failed: source or destination directory does not exist");
                return false;
            }

            var sourceFiles = sourceDir.GetFiles("*", SearchOption.AllDirectories);
            var totalFiles = sourceFiles.Length;
            var verified = 0;
            var totalBytes = sourceFiles.Sum(f => f.Length);
            long bytesVerified = 0;

            foreach (var sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourcePath, sourceFile.FullName);
                var destFile = new FileInfo(Path.Combine(destinationPath, relativePath));

                if (!destFile.Exists)
                {
                    Log.Warning("Verification failed: missing file {File}", relativePath);
                    return false;
                }

                if (sourceFile.Length != destFile.Length)
                {
                    Log.Warning("Verification failed: size mismatch for {File} (source={SourceSize}, dest={DestSize})",
                        relativePath, sourceFile.Length, destFile.Length);
                    return false;
                }

                bytesVerified += sourceFile.Length;
                verified++;

                progress.Report(new TransferProgress
                {
                    BytesCopied = bytesVerified,
                    TotalBytes = totalBytes,
                    FilesCopied = verified,
                    TotalFiles = totalFiles,
                    CurrentFile = relativePath,
                    Phase = TransferPhase.Verifying
                });
            }

            Log.Information("Verification passed: {Count} files verified", verified);
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Verification was cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Verification failed with exception");
            return false;
        }
    }

    public void Pause()
    {
        Log.Information("Transfer paused");
        _pauseEvent.Reset();
    }

    public void Resume()
    {
        Log.Information("Transfer resumed");
        _pauseEvent.Set();
    }

    private static async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
    {
        const int bufferSize = 81920;

        await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
    }
}
