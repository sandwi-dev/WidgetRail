namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal enum PinnedPlacementDirection { None, Left, Right, Up, Down }

/// <summary>One reversible preview; durable placement uses the existing DIP/anchor contract.</summary>
internal sealed class PinnedPlacementAdjustment(PinnedBounds original, PinnedMonitor monitor, PinnedPlacementLimits limits,
    string layoutId, int opacity)
{
    internal PinnedBounds Current { get; private set; } = original;
    internal int OpacityPercent { get; private set; } = Math.Clamp(opacity, 30, 100);
    internal PinnedPlacement Original { get; } = PinnedPlacementPolicy.Capture(original, monitor, limits, layoutId, opacity);
    internal PinnedMonitor Monitor { get; private set; } = monitor;
    private readonly PinnedBounds originalBounds=original;
    private readonly string originalMonitorId=monitor.Id;
    internal bool GeometryChanged => Current != originalBounds || Monitor.Id != originalMonitorId;
    internal PinnedPlacementLimits Limits { get; } = limits;

    internal bool StepOpacity(PinnedPlacementDirection direction)
    {
        var delta = direction switch { PinnedPlacementDirection.Left => -5, PinnedPlacementDirection.Right => 5, _ => 0 };
        var next = Math.Clamp(OpacityPercent + delta, 30, 100);
        if (next == OpacityPercent) return false;
        OpacityPercent = next;
        return true;
    }

    internal bool SetOpacity(int value)
    {
        var next = Math.Clamp(value, 30, 100);
        if (next == OpacityPercent) return false;
        OpacityPercent = next;
        return true;
    }

    internal bool Step(PinnedPlacementDirection direction, bool resize, IReadOnlyList<PinnedMonitor>? monitors = null)
    {
        var step = Math.Max(1, (int)Math.Round(32 * Monitor.Scale, MidpointRounding.AwayFromZero));
        var x = direction switch { PinnedPlacementDirection.Left => -step, PinnedPlacementDirection.Right => step, _ => 0 };
        var y = direction switch { PinnedPlacementDirection.Up => -step, PinnedPlacementDirection.Down => step, _ => 0 };
        if (x == 0 && y == 0) return false;
        var next = resize ? Current with { Width = Add(Current.Width, x), Height = Add(Current.Height, y) }
            : Current with { X = Add(Current.X, x), Y = Add(Current.Y, y) };
        next = PinnedPlacementPolicy.Constrain(next, Monitor, Limits);
        if (next == Current) return !resize && monitors is not null && MoveToAdjacentMonitor(direction, monitors);
        Current = next;
        return true;
    }

    internal bool Snap(PinnedPlacementDirection direction)
    {
        if (direction == PinnedPlacementDirection.None) return false;
        var area = Monitor.WorkArea;
        int right = area.X + (area.Width - Current.Width);
        int bottom = area.Y + (area.Height - Current.Height);
        int middle = area.Y + (area.Height - Current.Height) / 2;
        int x = direction switch
        {
            PinnedPlacementDirection.Left => area.X,
            PinnedPlacementDirection.Right => right,
            _ => Current.X - (long)area.X <= (long)right - Current.X ? area.X : right,
        };
        int y = direction switch
        {
            PinnedPlacementDirection.Up => Current.Y > middle ? middle : area.Y,
            PinnedPlacementDirection.Down => Current.Y < middle ? middle : bottom,
            _ when Current.Y - (long)area.Y < (long)middle - Current.Y => area.Y,
            _ when (long)bottom - Current.Y < Current.Y - (long)middle => bottom,
            _ => middle,
        };
        var next = Current with { X = x, Y = y };
        if (next == Current) return false;
        Current = next;
        return true;
    }

    internal bool Move(int x, int y, IReadOnlyList<PinnedMonitor> monitors)
    {
        if (x == 0 && y == 0) return false;
        var next = PinnedPlacementPolicy.Constrain(Current with { X = Add(Current.X, x), Y = Add(Current.Y, y) }, Monitor, Limits);
        bool horizontal = Math.Abs(x) >= Math.Abs(y);
        // Cross the facing edge even when the secondary axis can still slide along it.
        if ((horizontal ? next.X == Current.X : next.Y == Current.Y) && monitors.Count > 1)
        {
            var direction = horizontal ? x > 0 ? PinnedPlacementDirection.Right : PinnedPlacementDirection.Left
                : y > 0 ? PinnedPlacementDirection.Down : PinnedPlacementDirection.Up;
            if (MoveToAdjacentMonitor(direction, monitors)) return true;
        }
        if (next == Current) return false;
        Current = next;
        return true;
    }

    private bool MoveToAdjacentMonitor(PinnedPlacementDirection direction, IReadOnlyList<PinnedMonitor> monitors)
    {
        var horizontal = direction is PinnedPlacementDirection.Left or PinnedPlacementDirection.Right;
        var forward = direction is PinnedPlacementDirection.Right or PinnedPlacementDirection.Down;
        var area = Monitor.WorkArea;
        var edge = horizontal ? (long)area.X : area.Y;
        if (forward) edge += horizontal ? area.Width : area.Height;
        var center = horizontal ? Current.Y + Current.Height / 2d : Current.X + Current.Width / 2d;
        PinnedMonitor? destination = null;
        var nearest = long.MaxValue;
        foreach (var candidate in monitors.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (candidate.Id == Monitor.Id) continue;
            var target = candidate.WorkArea;
            var start = horizontal ? target.Y : target.X;
            var extent = horizontal ? target.Height : target.Width;
            if (center < start || center >= (long)start + extent) continue;
            var facingEdge = horizontal ? (long)target.X : target.Y;
            if (!forward) facingEdge += horizontal ? target.Width : target.Height;
            var distance = forward ? facingEdge - edge : edge - facingEdge;
            if (distance < 0 || distance >= nearest) continue;
            if (PinnedPlacementPolicy.Resolve(new[] { candidate }, null, Limits) is null) continue;
            destination = candidate;
            nearest = distance;
        }
        if (destination is null) return false;

        var logical = PinnedPlacementPolicy.Capture(Current, Monitor, Limits, Original.LayoutId, OpacityPercent);
        var resolved = PinnedPlacementPolicy.Resolve(new[] { destination }, logical with { MonitorId = destination.Id }, Limits)!;
        var bounds = resolved.Bounds;
        var work = destination.WorkArea;
        bounds = horizontal
            ? bounds with { X = forward ? work.X : work.X + work.Width - bounds.Width,
                Y = (int)Math.Round(center - bounds.Height / 2d) }
            : bounds with { X = (int)Math.Round(center - bounds.Width / 2d),
                Y = forward ? work.Y : work.Y + work.Height - bounds.Height };
        Monitor = destination;
        Current = PinnedPlacementPolicy.Constrain(bounds, destination, Limits);
        return true;
    }

    internal PinnedBounds PreviewPointer(PinnedBounds proposed,int edge,IReadOnlyList<PinnedMonitor> monitors)
    {
        // The pointer can cross a display boundary in one native drag. Choose
        // the nearest valid work area to its proposed center, then apply the
        // same limits/anchors used by keyboard/controller placement and saving.
        var centerX=proposed.X+proposed.Width/2d;var centerY=proposed.Y+proposed.Height/2d;
        var destination=edge!=0?Monitor:monitors.Where(item=>PinnedPlacementPolicy.Resolve(new[]{item},null,Limits) is not null)
            .OrderBy(item=>
            {
                var area=item.WorkArea;
                var dx=centerX-Math.Clamp(centerX,area.X,(double)area.X+area.Width);
                var dy=centerY-Math.Clamp(centerY,area.Y,(double)area.Y+area.Height);
                return dx*dx+dy*dy;
            }).ThenBy(item=>item.Id==Monitor.Id?0:1).FirstOrDefault()??Monitor;
        if(edge==0)
            proposed=proposed with{Width=(int)Math.Round(Current.Width/Monitor.Scale*destination.Scale),
                Height=(int)Math.Round(Current.Height/Monitor.Scale*destination.Scale)};
        var constrained=PinnedPlacementPolicy.Constrain(proposed,destination,Limits);
        // Keep the opposite edge stationary when left/top sizing meets a limit.
        if(edge is 1 or 4 or 7)constrained=constrained with{X=proposed.X+proposed.Width-constrained.Width};
        if(edge is 3 or 4 or 5)constrained=constrained with{Y=proposed.Y+proposed.Height-constrained.Height};
        Current=PinnedPlacementPolicy.Constrain(constrained,destination,Limits);
        Monitor=destination;
        return Current;
    }

    private static int Add(int value, int delta) => (int)Math.Clamp((long)value + delta, int.MinValue, int.MaxValue);
}

/// <summary>
/// Existing controller samples drive stick travel, discrete snaps and resize repeats.
/// </summary>
internal sealed class PinnedPlacementInput
{
    private readonly Axis resize = new();
    private ushort lastDpad;
    private long previousSample, movingSince;
    private double carryX, carryY, previousX, previousY;
    private bool moving, waitForNeutral;

    internal void Prime(ushort buttons, short leftX, short leftY, short rightX, short rightY, long now)
    {
        lastDpad = (ushort)(buttons & 15);
        resize.Reset();
        resize.Sample(rightX, rightY, now);
        moving = false;
        previousSample = now;
        waitForNeutral = (double)leftX * leftX + (double)leftY * leftY > 8000d * 8000;
    }

    internal (PinnedPlacementDirection Dpad, int X, int Y, PinnedPlacementDirection Resize) Sample(
        ushort buttons, short leftX, short leftY, short rightX, short rightY, long now, PinnedMonitor monitor)
    {
        var pressed = buttons & ~lastDpad & 15;
        lastDpad = (ushort)(buttons & 15);
        var snap = pressed switch { 1 => PinnedPlacementDirection.Up, 2 => PinnedPlacementDirection.Down,
            4 => PinnedPlacementDirection.Left, 8 => PinnedPlacementDirection.Right, _ => PinnedPlacementDirection.None };
        var size = resize.Sample(rightX, rightY, now);
        var elapsed = now - previousSample;
        previousSample = now;
        double magnitude = Math.Sqrt((double)leftX * leftX + (double)leftY * leftY);
        if (magnitude <= 8000) waitForNeutral = false;
        if (waitForNeutral || magnitude <= 8000 || snap != PinnedPlacementDirection.None)
        {
            moving = false;
            return (snap, 0, 0, size);
        }
        double x = leftX / magnitude, y = -leftY / magnitude;
        if (!moving || elapsed > 100 || x * previousX + y * previousY <= 0)
        {
            movingSince = now;
            carryX = carryY = 0;
            elapsed = Math.Clamp(elapsed, 0, 16);
        }
        moving = true;
        previousX = x;
        previousY = y;
        double strength = Math.Clamp((magnitude - 8000) / (32767 - 8000), 0, 1);
        double ramp = .25 + .75 * Math.Clamp((now - movingSince) / 350d, 0, 1);
        double span = Math.Max(monitor.WorkArea.Width, monitor.WorkArea.Height);
        double speed = 85 * monitor.Scale * strength + span * 1.15 * strength * strength * strength * ramp;
        double distance = speed * Math.Clamp(elapsed, 0, 40) / 1000;
        carryX += x * distance;
        carryY += y * distance;
        int dx = (int)carryX, dy = (int)carryY;
        carryX -= dx;
        carryY -= dy;
        return (snap, dx, dy, size);
    }

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
