namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Keeps one accepted destination while native ChangeView catches up. A later
/// relative movement must not use an older reported offset and undo that request.
/// The scroll gesture owns this state; there is no timer or movement backlog.
/// </summary>
internal sealed class ScrollOffsetAccumulator
{
    private double? requested;
    internal bool IsPending => requested is not null;

    internal double Propose(double current, double delta, double maximum, double tolerance)
    {
        Observe(current, maximum, tolerance);
        return Math.Clamp((requested ?? current) + delta, 0, maximum);
    }

    internal void Accepted(double destination) => requested = destination;

    internal void Observe(double current, double maximum, double tolerance)
    {
        if (requested is not { } destination) return;
        destination = Math.Clamp(destination, 0, maximum);
        requested = Math.Abs(destination - current) <= tolerance ? null : destination;
    }

    internal void Reset() => requested = null;
}
