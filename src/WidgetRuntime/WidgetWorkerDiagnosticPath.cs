namespace WidgetRail.WidgetRuntime;

internal static class WidgetWorkerDiagnosticPath
{
    internal const int RetainedProcessDirectories = 4;

    internal static string? TryPrepare(string? root, string widgetInstanceId)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        try
        {
            var fullRoot = Path.GetFullPath(root);
            if (!Path.IsPathFullyQualified(fullRoot) || fullRoot.Length > 4096)
                return null;
            Directory.CreateDirectory(fullRoot);
            if (IsReparsePoint(fullRoot)) return null;

            var widgetRoot = Path.Combine(fullRoot, widgetInstanceId);
            Directory.CreateDirectory(widgetRoot);
            if (IsReparsePoint(widgetRoot)) return null;
            var processDirectory = Path.Combine(
                widgetRoot, $"process-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(processDirectory);
            RetireOldProcesses(widgetRoot, processDirectory);
            return Path.Combine(processDirectory, WidgetWorkerDiagnosticLog.FileName);
        }
        catch (Exception exception) when (exception is ArgumentException or
            IOException or UnauthorizedAccessException or NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }

    private static void RetireOldProcesses(string widgetRoot, string current)
    {
        try
        {
            var retained = new DirectoryInfo(widgetRoot)
                .EnumerateDirectories("process-*", SearchOption.TopDirectoryOnly)
                .Where(directory => !directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
                .ToArray();
            foreach (var directory in retained.Skip(RetainedProcessDirectories))
            {
                if (string.Equals(directory.FullName, current, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { directory.Delete(recursive: true); }
                catch (Exception exception) when (exception is IOException or
                    UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException) { }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
