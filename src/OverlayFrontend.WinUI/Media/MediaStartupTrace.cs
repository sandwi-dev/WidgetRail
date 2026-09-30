using System.Diagnostics;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

// Fixed milestones only: never log URLs, resource bytes or provider responses.
internal sealed class MediaStartupTrace(string widgetId)
{
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly HashSet<string> recorded = [];
    internal void Mark(string stage)
    {
        if (!recorded.Add(stage)) return;
        var message = $"widget={widgetId} stage={stage} elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}";
        // The diagnostic sink flushes synchronously; keep it off the UI thread.
        _ = Task.Run(() => Diagnostics.FrontendFailureLog.Current.Write("media-startup", null, message));
    }
}
