namespace GameTransfer.Core.Helpers;

public static class PathHelper
{
    public static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        full = full.Replace('/', '\\');
        full = full.TrimEnd('\\');

        // Resolve junctions/reparse points to actual target path
        full = ResolveJunction(full);

        return full;
    }

    /// <summary>
    /// If the path is a junction or symlink, resolve it to the actual target.
    /// </summary>
    public static string ResolveJunction(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            if (di.Exists && di.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var target = di.ResolveLinkTarget(true)?.FullName;
                if (!string.IsNullOrEmpty(target))
                    return target.TrimEnd('\\');
            }
        }
        catch
        {
            // Ignore errors resolving junctions
        }
        return path;
    }

    public static bool IsSubPathOf(string childPath, string parentPath)
    {
        var normalizedChild = NormalizePath(childPath);
        var normalizedParent = NormalizePath(parentPath);

        return normalizedChild.StartsWith(normalizedParent + "\\", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedChild, normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    public static string ReplacePath(string fullPath, string oldBase, string newBase)
    {
        var normalizedFull = NormalizePath(fullPath);
        var normalizedOld = NormalizePath(oldBase);
        var normalizedNew = NormalizePath(newBase);

        if (!normalizedFull.StartsWith(normalizedOld, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Path '{fullPath}' is not under base path '{oldBase}'.");

        var relative = normalizedFull[normalizedOld.Length..].TrimStart('\\');
        return string.IsNullOrEmpty(relative)
            ? normalizedNew
            : Path.Combine(normalizedNew, relative);
    }

    public static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        return new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }

    public static int GetFileCount(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
    }
}
