namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Shared normalized pointer motion for browser pages and native dialogs.</summary>
internal sealed class ControllerPointerMotion
{
    private long sampledAt;
    internal void Reset() => sampledAt = 0;
    internal (double X, double Y) Sample(short x, short y, long now)
    {
        var elapsed = sampledAt == 0 ? 0 : Math.Clamp(now - sampledAt, 0, 50);
        sampledAt = now;
        static double Axis(short value) => Math.Abs((int)value) < 7849 ? 0 : value / 32768d;
        return (Axis(x) * elapsed * .0006, -Axis(y) * elapsed * .0006);
    }
}
