namespace WidgetRail.Shared;

internal static class WindowsHandlePath
{
    // Call only at the Win32 boundary, after validating and canonicalizing the path.
    // Keep ordinary paths for catalog identity, containment checks and returned leases.
    internal static string ForCreateFile(string fullPath)
    {
        if (fullPath.Length < 260 || fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            fullPath.StartsWith(@"\\.\", StringComparison.Ordinal))
            return fullPath;
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath[2..] : @"\\?\" + fullPath;
    }
}
