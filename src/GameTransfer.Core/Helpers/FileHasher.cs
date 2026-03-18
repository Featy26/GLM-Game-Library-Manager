using System.IO.Hashing;

namespace GameTransfer.Core.Helpers;

public static class FileHasher
{
    private const int BufferSize = 8192;

    public static async Task<byte[]> HashFileAsync(string filePath, CancellationToken ct = default)
    {
        var hasher = new XxHash64();
        var buffer = new byte[BufferSize];

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
        {
            hasher.Append(buffer.AsSpan(0, bytesRead));
        }

        return hasher.GetHashAndReset();
    }

    public static async Task<bool> CompareFilesAsync(string file1, string file2, CancellationToken ct = default)
    {
        var hash1Task = HashFileAsync(file1, ct);
        var hash2Task = HashFileAsync(file2, ct);

        await Task.WhenAll(hash1Task, hash2Task);

        return hash1Task.Result.AsSpan().SequenceEqual(hash2Task.Result);
    }
}
