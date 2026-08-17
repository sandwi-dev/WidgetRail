using System.Diagnostics;

namespace WidgetRail.SpotifyPlaybackHost;

internal static class ParentLifetime
{
    internal static async Task WaitForExitAsync(int parentProcessId, CancellationToken cancellationToken)
    {
        if (parentProcessId <= 0 || parentProcessId == Environment.ProcessId)
            throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        using var parent = Process.GetProcessById(parentProcessId);
        await parent.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }
}
