namespace GameBarAlternative.WidgetCatalog;

internal static class FileSystemSafety
{
    public static void EnsureNoReparsePoints(string root, string target)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullTarget = Path.GetFullPath(target);
        if (!IsWithin(fullRoot, fullTarget))
            throw new WidgetPackageException("path_escape", $"Path escapes the catalog root: {fullTarget}");

        var relative = Path.GetRelativePath(fullRoot, fullTarget);
        var current = fullRoot;
        Check(current);
        if (relative == ".") return;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) break;
            Check(current);
        }

        static void Check(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new WidgetPackageException("reparse_point", $"Reparse points are not allowed in catalog paths: {path}");
        }
    }

    public static bool IsWithin(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureTreeContainsNoReparsePoints(
        string root,
        string directory,
        int maximumEntries = int.MaxValue,
        Action? checkpoint = null)
    {
        if (maximumEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        EnsureNoReparsePoints(root, directory);
        var pending = new Stack<string>();
        pending.Push(directory);
        var entryCount = 0;
        while (pending.Count != 0)
        {
            checkpoint?.Invoke();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                checkpoint?.Invoke();
                if (entryCount == maximumEntries)
                    throw new WidgetPackageException(
                        "integrity_limit",
                        "Installed widget tree exceeds its filesystem-entry limit.");
                checked { ++entryCount; }
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new WidgetPackageException("reparse_point", $"Reparse points are not allowed in catalog trees: {entry}");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }
}
