using System.Diagnostics;

namespace GameBarAlternative.WindowsAppLibraryProvider;

internal interface IWindowsShellLauncher
{
    void Launch(string exactShortcutPath, CancellationToken cancellationToken);
}

/// <summary>
/// Invokes the documented Windows Shell "open" verb for one trusted,
/// provider-revalidated Start Menu shortcut. No arguments, working directory,
/// elevation verb, or window handle are supplied.
/// </summary>
internal sealed class WindowsShellLauncher : IWindowsShellLauncher
{
    public void Launch(string exactShortcutPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(CreateStartInfo(exactShortcutPath));
        if (process is null)
            throw new InvalidOperationException("Windows Shell did not accept the launch request.");
    }

    internal static ProcessStartInfo CreateStartInfo(string exactShortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactShortcutPath);
        if (!Path.IsPathFullyQualified(exactShortcutPath) ||
            !Path.GetExtension(exactShortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A fully qualified Shell shortcut is required.",
                nameof(exactShortcutPath));
        return new ProcessStartInfo
        {
            FileName = exactShortcutPath,
            Verb = "open",
            UseShellExecute = true,
        };
    }
}
