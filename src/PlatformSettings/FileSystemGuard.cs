namespace WidgetRail.PlatformSettings;

internal static class FileSystemGuard
{
    public static void EnsureExistingPathHasNoReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        var existing = File.Exists(full) || Directory.Exists(full)
            ? full
            : Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(existing) && !File.Exists(existing) && !Directory.Exists(existing))
            existing = Path.GetDirectoryName(existing);

        if (string.IsNullOrEmpty(existing)) return;
        for (var current = new DirectoryInfo(
                 (File.GetAttributes(existing) & FileAttributes.Directory) != 0
                     ? existing
                     : Path.GetDirectoryName(existing)!);
             current is not null;
             current = current.Parent)
        {
            Reject(current.FullName);
        }
        if (File.Exists(existing)) Reject(existing);
    }

    public static void RejectReparsePoint(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) Reject(path);
    }

    public static bool IsWithin(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void Reject(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new PlatformSettingsException(
                "reparse_point",
                $"Reparse points are not allowed in platform settings paths: {path}");
    }
}
