using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

// Fixture-only timing observer. Production timings remain opt-in and contain no
// message text, URLs, account identifiers or payload bodies.
internal sealed class PresentationPipelineValidationProbe : EventListener
{
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly ConcurrentQueue<object> events = new();
    private int count;

    internal long ElapsedMilliseconds => elapsed.ElapsedMilliseconds;
    internal object[] Events => events.ToArray();

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "WidgetRail-PresentationPipeline")
            EnableEvents(source, EventLevel.Informational);
    }

    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        if (data.EventId != 1 || data.Payload is not { Count: 5 } payload ||
            payload[1] is not int stage || stage is not (1 or 2 or 4 or 10) ||
            Interlocked.Increment(ref count) > 512) return;
        events.Enqueue(new { ms = elapsed.ElapsedMilliseconds, operation = payload[0], stage,
            duration = payload[2], items = payload[3], outcome = payload[4] });
    }
}
