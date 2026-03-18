using System.Runtime.InteropServices;
using GameTransfer.Core.Interfaces;
using Serilog;

namespace GameTransfer.Core.Services;

public class ShortcutService : IShortcutService
{
    private static readonly string[] ShortcutSearchPaths =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
    ];

    public IReadOnlyList<string> FindShortcutsPointingTo(string targetPath)
    {
        var matches = new List<string>();
        var normalizedTarget = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var searchPath in ShortcutSearchPaths)
        {
            if (!Directory.Exists(searchPath))
            {
                continue;
            }

            try
            {
                var lnkFiles = Directory.GetFiles(searchPath, "*.lnk", SearchOption.AllDirectories);

                foreach (var lnkFile in lnkFiles)
                {
                    try
                    {
                        var shortcutTarget = GetShortcutTargetPath(lnkFile);
                        if (shortcutTarget is not null &&
                            shortcutTarget.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            matches.Add(lnkFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to read shortcut: {Path}", lnkFile);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to search for shortcuts in: {Path}", searchPath);
            }
        }

        Log.Information("Found {Count} shortcuts pointing to {Target}", matches.Count, targetPath);
        return matches.AsReadOnly();
    }

    public void UpdateShortcut(string shortcutPath, string oldBasePath, string newBasePath)
    {
        dynamic? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell COM object is not available.");

            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Failed to create WScript.Shell instance.");

            dynamic shortcut = shell.CreateShortcut(shortcutPath);

            try
            {
                var currentTarget = (string)shortcut.TargetPath;
                var currentWorkDir = (string)shortcut.WorkingDirectory;

                var updated = false;

                if (currentTarget.StartsWith(oldBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    shortcut.TargetPath = newBasePath + currentTarget[oldBasePath.Length..];
                    updated = true;
                }

                if (!string.IsNullOrEmpty(currentWorkDir) &&
                    currentWorkDir.StartsWith(oldBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    shortcut.WorkingDirectory = newBasePath + currentWorkDir[oldBasePath.Length..];
                    updated = true;
                }

                if (updated)
                {
                    shortcut.Save();
                    Log.Information("Updated shortcut: {Path}", shortcutPath);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update shortcut: {Path}", shortcutPath);
            throw;
        }
        finally
        {
            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    public void UpdateAllShortcuts(string oldBasePath, string newBasePath)
    {
        var shortcuts = FindShortcutsPointingTo(oldBasePath);
        Log.Information("Updating {Count} shortcuts from {Old} to {New}", shortcuts.Count, oldBasePath, newBasePath);

        foreach (var shortcutPath in shortcuts)
        {
            try
            {
                UpdateShortcut(shortcutPath, oldBasePath, newBasePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Skipping shortcut update for: {Path}", shortcutPath);
            }
        }
    }

    private static string? GetShortcutTargetPath(string lnkPath)
    {
        dynamic? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;

            shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;

            dynamic shortcut = shell.CreateShortcut(lnkPath);
            try
            {
                return (string)shortcut.TargetPath;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
