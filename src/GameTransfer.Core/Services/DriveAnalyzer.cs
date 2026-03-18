using Serilog;

namespace GameTransfer.Core.Services;

public static class DriveAnalyzer
{
    public static IReadOnlyList<DriveInfo> GetAvailableDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .ToList()
                .AsReadOnly();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enumerate available drives");
            return Array.Empty<DriveInfo>();
        }
    }

    public static long GetDriveFreeSpace(string driveLetter)
    {
        try
        {
            var drive = new DriveInfo(driveLetter);
            if (!drive.IsReady)
            {
                Log.Warning("Drive {Drive} is not ready", driveLetter);
                return 0;
            }

            return drive.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get free space for drive {Drive}", driveLetter);
            return 0;
        }
    }

    public static bool HasEnoughSpace(string driveLetter, long requiredBytes)
    {
        var freeSpace = GetDriveFreeSpace(driveLetter);
        var requiredWithBuffer = (long)(requiredBytes * 1.1);
        var hasSpace = freeSpace >= requiredWithBuffer;

        if (!hasSpace)
        {
            Log.Warning("Insufficient space on {Drive}: {Free} available, {Required} required (with 10% buffer)",
                driveLetter, freeSpace, requiredWithBuffer);
        }

        return hasSpace;
    }
}
