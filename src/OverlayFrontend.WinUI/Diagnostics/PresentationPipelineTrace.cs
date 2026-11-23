using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace WidgetRail.OverlayFrontend.WinUI.Diagnostics;

internal enum PipelineStage
{
    RangeData = 1, RangeUiQueue = 2, RangeBatch = 3, RangeTotal = 4,
    ArtworkResolve = 5, ArtworkDecode = 6, ArtworkPublish = 7, ArtworkTotal = 8,
    RangeNormalProbe = 9, RangeBatchQueue = 10,
    IndexedRowApply = 11, IndexedRowRetire = 12
}

internal enum PipelineOutcome { Completed, Cancelled, Failed, Retired }
internal enum ScrollTracePhase { Request = 1, NativeViewChanged = 2 }

/// <summary>
/// Opt-in EventPipe/ETW timings. No files, labels, paths, handles or request bodies.
/// Operation IDs correlate phases within this process only. Durations are elapsed
/// time (including waits), not CPU time or proof of presentation on the display.
/// </summary>
[EventSource(Name = "WidgetRail-PresentationPipeline", Guid = "d32b0e30-924a-598f-9296-f318d749fa45")]
internal sealed class PresentationPipelineTrace : EventSource
{
    internal static readonly PresentationPipelineTrace Log = new();
    private long sequence;

    [NonEvent]
    internal (long Id, long Started) Begin() => IsEnabled()
        ? (Interlocked.Increment(ref sequence), Stopwatch.GetTimestamp()) : default;

    [NonEvent]
    internal static long Timestamp(long id) => id == 0 ? 0 : Stopwatch.GetTimestamp();

    [NonEvent]
    internal void End(long id, PipelineStage stage, long started, int count, PipelineOutcome outcome)
    {
        if (id != 0 && IsEnabled())
            Stage(id, (int)stage, Stopwatch.GetElapsedTime(started).TotalMilliseconds, count, (int)outcome);
    }

    [Event(1, Level = EventLevel.Informational)]
    public void Stage(long operationId, int stage, double milliseconds, int count, int outcome)
    {
        if (IsEnabled()) WriteEvent(1, operationId, stage, milliseconds, count, outcome);
    }

    internal bool ScrollEnabled
    {
        [NonEvent]
        get => IsEnabled(EventLevel.Verbose, EventKeywords.None);
    }

    // Numeric gesture-local identity only; no widget, item, title or content IDs.
    // Native ViewChanged is not a display-present event or a frame-rate measure.
    [Event(2, Level = EventLevel.Verbose)]
    public void Scroll(long gestureId, int phase, double requestedX, double requestedY,
        double offsetX, double offsetY, double extentX, double extentY, int flags)
    {
        if (ScrollEnabled)
            WriteEvent(2, gestureId, phase, requestedX, requestedY, offsetX, offsetY, extentX, extentY, flags);
    }
}
