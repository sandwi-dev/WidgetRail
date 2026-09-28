namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>
/// Maps the platform's single controller sample to DIP deltas. Keeps the native
/// ControllerNavigation RightStickScrollKinetics policy: axis hysteresis, 8000
/// deadzone, 2200 DIP/s maximum and a 50ms hitch cap; never accumulates backlog.
/// </summary>
internal sealed class RightStickScroll
{
    private int axis;
    private long lastSample;
    internal void Reset() { axis = 0; lastSample = 0; }
    internal (double X, double Y) Sample(short x, short y, long now)
    {
        var horizontal = Math.Abs((int)x);
        var vertical = Math.Abs((int)y);
        if (horizontal <= 8000 && vertical <= 8000) { Reset(); return default; }
        var elapsed = axis == 0 ? 16 : Math.Clamp(now - lastSample, 0, 50);
        var next = horizontal >= vertical ? 1 : 2;
        if (axis == 1 && vertical < horizontal * 1.25 || axis == 2 && horizontal < vertical * 1.25) next = axis;
        axis = next;
        lastSample = Math.Max(lastSample, now);
        var magnitude = axis == 1 ? horizontal : vertical;
        var strength = Math.Clamp((magnitude - 8000d) / (32767 - 8000), 0, 1);
        var delta = strength * 2200 * elapsed / 1000;
        return axis == 1 ? (Math.Sign(x) * delta, 0) : (0, -Math.Sign(y) * delta);
    }
}
