using System.Diagnostics;

namespace WidgetRail.WindowsAppLibraryProvider;

/// <summary>Launches one provider-revalidated numeric Steam application ID.</summary>
internal sealed class WindowsSteamLauncher : IWindowsSteamLauncher
{
    public void Launch(string exactSteamAppId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var process = Process.Start(CreateStartInfo(exactSteamAppId));
        if (process is null)
            throw new InvalidOperationException("Windows Shell did not accept the Steam request.");
    }

    internal static ProcessStartInfo CreateStartInfo(string exactSteamAppId)
    {
        if (!WindowsSteamApplicationSource.IsValidAppId(exactSteamAppId))
            throw new ArgumentException("A numeric Steam application ID is required.",
                nameof(exactSteamAppId));
        return new ProcessStartInfo
        {
            FileName = $"steam://rungameid/{exactSteamAppId}",
            Verb = "open",
            UseShellExecute = true,
        };
    }
}
