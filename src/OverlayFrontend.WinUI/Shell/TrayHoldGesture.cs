namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal enum TrayHoldAction { None, ToggleReorder, Restart }

/// <summary>Host gesture policy only. The shared platform adapter owns physical input.</summary>
internal sealed class TrayHoldGesture
{
    internal const long HoldMilliseconds = 700;
    private enum Phase { Idle, Tap, Hold, Won, Cancelled }
    private Phase phase;
    private object? target;
    private long started;
    internal bool Capturing => phase != Phase.Idle;

    internal void Press(object identity, bool restartAllowed, long now)
    {
        if (Capturing) return;
        target = identity; started = now;
        phase = restartAllowed ? Phase.Hold : Phase.Tap;
    }

    internal TrayHoldAction Tick(object? identity, bool restartAllowed, long now)
    {
        if (phase is not (Phase.Tap or Phase.Hold)) return TrayHoldAction.None;
        if (!Equals(target, identity) || phase == Phase.Hold && !restartAllowed)
        { Cancel(); return TrayHoldAction.None; }
        if (phase != Phase.Hold || now < started || now - started < HoldMilliseconds) return TrayHoldAction.None;
        phase = Phase.Won;
        return TrayHoldAction.Restart;
    }

    internal TrayHoldAction Release(object? identity, bool restartAllowed, long now)
    {
        var action = Tick(identity, restartAllowed, now);
        if (action == TrayHoldAction.None && (phase is Phase.Tap or Phase.Hold) && Equals(target, identity))
            action = TrayHoldAction.ToggleReorder;
        Reset();
        return action;
    }

    // Keep ownership until release so canceled Y cannot reach a new widget.
    internal void Cancel() { if (Capturing) phase = Phase.Cancelled; }
    internal void Reset() { phase = Phase.Idle; target = null; }
}
