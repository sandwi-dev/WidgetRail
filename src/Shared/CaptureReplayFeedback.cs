using WidgetRail.WidgetProtocol;

namespace WidgetRail.Internal;

internal static class CaptureReplayFeedback
{
    internal static string Saved(CaptureReplaySaveResult result)
    {
        var source = result.Item?.Media.SourceApplication;
        return source is null
            ? $"Saved {result.ActualDurationSeconds:0.#} seconds to Capture Library."
            : $"Saved {result.ActualDurationSeconds:0.#} seconds of replay from {source.ApplicationName}.";
    }
}
