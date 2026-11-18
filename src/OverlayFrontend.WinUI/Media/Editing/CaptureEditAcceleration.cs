namespace WidgetRail.OverlayFrontend.WinUI.Media.Editing;

// Uses the host's actual navigation phase: repeated taps never count as a hold.
// Only native editing policy lives here; routing and repeat generation stay in the host.
internal sealed class CaptureEditAcceleration
{
    private int direction, repeats;
    private long started;
    internal void Reset() { direction = 0; repeats = 0; started = 0; }
    internal double Step(int nextDirection, bool repeated, long now, double fine, double fast)
    {
        if (!repeated || nextDirection != direction || direction == 0)
        { direction = nextDirection; repeats = 0; started = now; return fine; }
        ++repeats;
        // A delayed first repeat must not jump straight to maximum speed.
        var held = Math.Min(Math.Max(0, now - started), repeats * 100d);
        var ramp = Math.Clamp((held - 300) / 1200, 0, 1);
        return fine + (Math.Max(fine, fast) - fine) * ramp * ramp;
    }
    internal static double TrimFastStep(double seconds) => Math.Clamp(seconds / 100, .2, 30);
}
