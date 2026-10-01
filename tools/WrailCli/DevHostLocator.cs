using System.Security;
using Microsoft.Win32;

namespace WidgetRail.WrailCli;

internal sealed record DevHostTarget(string ExecutablePath, string InstallationRoot)
{
    internal string FrontendRoot => Path.GetDirectoryName(ExecutablePath)!;
    public override string ToString() => $"{ExecutablePath} ({InstallationRoot})";
}

internal static class DevHostLocator
{
    internal const string FrontendExecutable = "OverlayFrontend.WinUI.exe";

    internal static DevHostTarget Resolve(string? requested, string? installationRoot,
        IEnumerable<string>? searchRoots = null, Func<string?>? installedRoot = null)
    {
        try { return ResolveCore(requested, installationRoot, searchRoots, installedRoot); }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or SecurityException)
        { throw new CliUsageException("WinUI host discovery failed: " + DevSession.SafeMessage(error)); }
    }

    private static DevHostTarget ResolveCore(string? requested, string? installationRoot,
        IEnumerable<string>? searchRoots, Func<string?>? installedRoot)
    {
        string? executable = null;
        string? payload = string.IsNullOrWhiteSpace(installationRoot) ? null : Path.GetFullPath(installationRoot);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!Path.GetFileName(requested).Equals(FrontendExecutable, StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException($"--host requires a path to {FrontendExecutable}.");
            executable = Path.GetFullPath(requested);
            payload ??= Path.GetDirectoryName(executable)!;
        }
        else if (payload is null)
        {
            // A CLI shipped inside a complete payload uses that version first.
            foreach (var start in searchRoots ?? [AppContext.BaseDirectory, Environment.CurrentDirectory])
            {
                for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
                {
                    if (!File.Exists(Path.Combine(directory.FullName, FrontendExecutable))) continue;
                    payload = directory.FullName;
                    break;
                }
                if (payload is not null) break;
            }
            var installed = payload is null ? (installedRoot ?? ReadInstalledRoot)() : null;
            if (!string.IsNullOrWhiteSpace(installed)) payload = Path.GetFullPath(installed);
        }
        if (payload is null)
            throw new CliUsageException($"WinUI host was not found. Install WidgetRail or use --host <{FrontendExecutable}> --installation-root <complete-payload>.");
        executable ??= Path.Combine(payload, FrontendExecutable);
        RequireRegularFile(executable);
        foreach (var relative in new[] { "widget-catalog.json", "runtime/Bridge/WidgetBridge.exe", "runtime/WidgetWorkerHost/WidgetWorkerHost.exe" })
            RequireRegularFile(Path.Combine(payload, relative));
        return new(executable, payload);
    }

    private static void RequireRegularFile(string path)
    {
        if (!File.Exists(path))
            throw new CliUsageException($"WinUI host payload is incomplete; missing regular file {path}.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new CliUsageException($"WinUI host payload contains a linked file: {path}.");
        for (var directory = new FileInfo(path).Directory; directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new CliUsageException($"WinUI host payload contains a linked directory: {directory.FullName}.");
    }

    private static string? ReadInstalledRoot()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\WidgetRail\Installation", writable: false);
        return key?.GetValue("ApplicationRoot") as string;
    }
}
