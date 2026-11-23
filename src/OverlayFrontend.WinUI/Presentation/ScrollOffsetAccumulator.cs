namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Keeps one accepted destination while native ChangeView catches up. A later
/// relative movement must not use an older reported offset and undo that request.
/// The scroll gesture owns this state; there is no timer or movement backlog.
/// </summary>
internal sealed class ScrollOffsetAccumulator
{
    private double? requested;
    private double? observedMaximum;
    internal bool IsPending => requested is not null;

    internal double Propose(double current, double delta, double maximum, double tolerance)
    {
        Observe(current, maximum, tolerance);
        var origin = requested ?? current;
        // Native anchoring/measurement can move the viewport past a queued
        // destination without completing that request. Keep unconsumed input
        // only while its destination remains ahead in the requested direction;
        // an old coordinate must never turn held Up into a downward ChangeView.
        if (delta < 0 && origin > current || delta > 0 && origin < current) origin = current;
        return Math.Clamp(origin + delta, 0, maximum);
    }

    internal void Accepted(double destination) => requested = destination;

    internal void Observe(double current, double maximum, double tolerance)
    {
        // Native virtualization can remeasure rows while keeping the reader
        // anchored. Pending absolute coordinates from the old extent no longer
        // describe the same content, even if they are still ahead of input.
        if (observedMaximum is { } previous && Math.Abs(previous - maximum) > tolerance) requested = null;
        observedMaximum = maximum;
        if (requested is not { } destination) return;
        destination = Math.Clamp(destination, 0, maximum);
        requested = Math.Abs(destination - current) <= tolerance ? null : destination;
    }

    internal void Reset() { requested = null; observedMaximum = null; }
}
