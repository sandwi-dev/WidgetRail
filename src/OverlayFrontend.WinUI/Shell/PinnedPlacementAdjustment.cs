namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal enum PinnedPlacementDirection { None, Left, Right, Up, Down }

/// <summary>One reversible preview; durable placement uses the existing DIP/anchor contract.</summary>
internal sealed class PinnedPlacementAdjustment(PinnedBounds original, PinnedMonitor monitor, PinnedPlacementLimits limits,
    string layoutId, int opacity)
{
    internal PinnedBounds Current { get; private set; } = original;
    internal int OpacityPercent { get; private set; } = Math.Clamp(opacity, 30, 100);
    internal PinnedPlacement Original { get; } = PinnedPlacementPolicy.Capture(original, monitor, limits, layoutId, opacity);
    internal PinnedMonitor Monitor { get; } = monitor;
    internal PinnedPlacementLimits Limits { get; } = limits;

    internal bool StepOpacity(PinnedPlacementDirection direction)
    {
        var delta = direction switch { PinnedPlacementDirection.Left => -5, PinnedPlacementDirection.Right => 5, _ => 0 };
        var next = Math.Clamp(OpacityPercent + delta, 30, 100);
        if (next == OpacityPercent) return false;
        OpacityPercent = next;
        return true;
    }

    internal bool Step(PinnedPlacementDirection direction, bool resize)
    {
        var step = Math.Max(1, (int)Math.Round(32 * Monitor.Scale, MidpointRounding.AwayFromZero));
        var x = direction switch { PinnedPlacementDirection.Left => -step, PinnedPlacementDirection.Right => step, _ => 0 };
        var y = direction switch { PinnedPlacementDirection.Up => -step, PinnedPlacementDirection.Down => step, _ => 0 };
        if (x == 0 && y == 0) return false;
        var next = resize ? Current with { Width = Add(Current.Width, x), Height = Add(Current.Height, y) }
            : Current with { X = Add(Current.X, x), Y = Add(Current.Y, y) };
        next = PinnedPlacementPolicy.Constrain(next, Monitor, Limits);
        if (next == Current) return false;
        Current = next;
        return true;
    }

    private static int Add(int value, int delta) => (int)Math.Clamp((long)value + delta, int.MinValue, int.MaxValue);
}

/// <summary>
/// Original pinned-adjustment navigation: 15000/9000 hysteresis, 250ms initial
/// delay and 80ms repeat. Samples consume at most one step; stalls never queue
/// accumulated movement. This is not another controller reader.
/// </summary>
internal sealed class PinnedPlacementInput
{
    private readonly Axis dpad = new(), move = new(), resize = new();

    internal void Prime(ushort buttons, short leftX, short leftY, short rightX, short rightY, long now)
    {
        dpad.Reset(); move.Reset(); resize.Reset();
        _ = Sample(buttons, leftX, leftY, rightX, rightY, now);
    }

    internal (PinnedPlacementDirection Dpad, PinnedPlacementDirection Move, PinnedPlacementDirection Resize) Sample(
        ushort buttons, short leftX, short leftY, short rightX, short rightY, long now) =>
        (dpad.Sample(((buttons & 8) != 0 ? 32767 : 0) - ((buttons & 4) != 0 ? 32767 : 0),
            ((buttons & 1) != 0 ? 32767 : 0) - ((buttons & 2) != 0 ? 32767 : 0), now),
         move.Sample(leftX, leftY, now), resize.Sample(rightX, rightY, now));

    private sealed class Axis
    {
        private PinnedPlacementDirection held;
        private long nextRepeat;
        internal void Reset() { held = PinnedPlacementDirection.None; nextRepeat = 0; }
        internal PinnedPlacementDirection Sample(int x, int y, long now)
        {
            var horizontal = Math.Abs(x); var vertical = Math.Abs(y);
            var isHorizontal = held is PinnedPlacementDirection.Left or PinnedPlacementDirection.Right;
            var sameSign = held switch
            {
                PinnedPlacementDirection.Left => x < 0, PinnedPlacementDirection.Right => x > 0,
                PinnedPlacementDirection.Up => y > 0, PinnedPlacementDirection.Down => y < 0, _ => false,
            };
            var primary = isHorizontal ? horizontal : vertical;
            var perpendicular = isHorizontal ? vertical : horizontal;
            var keep = sameSign && primary >= 9000 && !(perpendicular >= 15000 && perpendicular * 4 > primary * 5);
            var direction = keep ? held : Math.Max(horizontal, vertical) < 15000 ? PinnedPlacementDirection.None :
                horizontal >= vertical ? x < 0 ? PinnedPlacementDirection.Left : PinnedPlacementDirection.Right
                    : y < 0 ? PinnedPlacementDirection.Down : PinnedPlacementDirection.Up;
            if (direction == PinnedPlacementDirection.None) { Reset(); return direction; }
            if (direction != held)
            {
                held = direction; nextRepeat = now + 250; return direction;
            }
            if (now < nextRepeat) return PinnedPlacementDirection.None;
            nextRepeat = now + 80;
            return held;
        }
    }
}
