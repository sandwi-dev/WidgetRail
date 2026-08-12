using System.Diagnostics;

namespace GameBarAlternative.WindowsAppLibraryProvider;

internal sealed class WindowsEpicLauncher : IWindowsEpicLauncher
{
    public void Launch(string catalogNamespace, string catalogItemId,
        string appName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var process = Process.Start(CreateStartInfo(
            catalogNamespace, catalogItemId, appName));
        if (process is null)
            throw new InvalidOperationException(
                "Windows Shell did not accept the Epic launch request.");
    }

    internal static ProcessStartInfo CreateStartInfo(
        string catalogNamespace, string catalogItemId, string appName)
    {
        if (!Valid(catalogNamespace) || !Valid(catalogItemId) || !Valid(appName))
            throw new ArgumentException("Validated Epic identifiers are required.");
        var identity = string.Join('%' + "3A",
            Uri.EscapeDataString(catalogNamespace),
            Uri.EscapeDataString(catalogItemId),
            Uri.EscapeDataString(appName));
        return new ProcessStartInfo
        {
            FileName = $"com.epicgames.launcher://apps/{identity}?action=launch&silent=true",
            Verb = "open",
            UseShellExecute = true,
        };
    }

    private static bool Valid(string value) => value.Length is > 0 and <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '_' or '-');
}
