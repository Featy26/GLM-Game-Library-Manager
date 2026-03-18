using System.Diagnostics;
using GameTransfer.Core.Interfaces;
using Serilog;

namespace GameTransfer.Core.Services;

public class SymlinkService : ISymlinkService
{
    public bool CreateJunction(string linkPath, string targetPath)
    {
        try
        {
            if (Directory.Exists(linkPath))
            {
                Log.Warning("Link path already exists: {LinkPath}", linkPath);
                return false;
            }

            if (!Directory.Exists(targetPath))
            {
                Log.Warning("Target path does not exist: {TargetPath}", targetPath);
                return false;
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            process?.WaitForExit();

            if (process?.ExitCode == 0)
            {
                Log.Information("Created junction: {Link} -> {Target}", linkPath, targetPath);
                return true;
            }

            var error = process?.StandardError.ReadToEnd();
            Log.Error("Failed to create junction. Exit code: {ExitCode}, Error: {Error}", process?.ExitCode, error);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create junction: {Link} -> {Target}", linkPath, targetPath);
            return false;
        }
    }

    public bool RemoveJunction(string linkPath)
    {
        try
        {
            if (!Directory.Exists(linkPath))
            {
                Log.Warning("Junction path does not exist: {Path}", linkPath);
                return false;
            }

            if (!IsJunction(linkPath))
            {
                Log.Warning("Path is not a junction: {Path}", linkPath);
                return false;
            }

            Directory.Delete(linkPath, recursive: false);
            Log.Information("Removed junction: {Path}", linkPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove junction: {Path}", linkPath);
            return false;
        }
    }

    public bool IsJunction(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check if path is a junction: {Path}", path);
            return false;
        }
    }
}
