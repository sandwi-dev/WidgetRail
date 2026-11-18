namespace WidgetRail.OverlayFrontend.WinUI.Media.Editing;

internal enum CropGrip { Move, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
internal readonly record struct CropBox(int X, int Y, int Width, int Height)
{
    internal int Right => checked(X + Width);
    internal int Bottom => checked(Y + Height);
}

/// <summary>Pixel geometry shared by pointer, keyboard and controller editing. Coordinates are after rotation.</summary>
internal sealed record ImageCropState
{
    internal int SourceWidth { get; }
    internal int SourceHeight { get; }
    internal int QuarterTurns { get; }
    internal CropBox Box { get; }
    internal double? Aspect { get; }
    internal int Width => QuarterTurns % 2 == 0 ? SourceWidth : SourceHeight;
    internal int Height => QuarterTurns % 2 == 0 ? SourceHeight : SourceWidth;
    internal bool Changed => QuarterTurns != 0 || Box != new CropBox(0, 0, Width, Height);

    private ImageCropState(int width, int height, int turns, CropBox box, double? aspect)
    {
        SourceWidth = width; SourceHeight = height; QuarterTurns = turns; Box = box; Aspect = aspect;
    }
    internal static ImageCropState Full(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return new(width, height, 0, new(0, 0, width, height), null);
    }
    internal ImageCropState Reset() => Full(SourceWidth, SourceHeight);
    internal ImageCropState WithBox(CropBox box)
    {
        if (box.X < 0 || box.Y < 0 || box.Width <= 0 || box.Height <= 0 ||
            (long)box.X + box.Width > Width || (long)box.Y + box.Height > Height)
            throw new ArgumentOutOfRangeException(nameof(box));
        return new(SourceWidth, SourceHeight, QuarterTurns, box, null);
    }
    internal ImageCropState Rotate(bool clockwise)
    {
        var next = clockwise ? new CropBox(Height - Box.Bottom, Box.X, Box.Height, Box.Width)
            : new CropBox(Box.Y, Width - Box.Right, Box.Height, Box.Width);
        return new(SourceWidth, SourceHeight, (QuarterTurns + (clockwise ? 1 : 3)) % 4, next,
            Aspect is { } ratio ? 1 / ratio : null);
    }
    internal ImageCropState SetAspect(double? ratio)
    {
        if (ratio is { } value && (!double.IsFinite(value) || value <= 0)) throw new ArgumentOutOfRangeException(nameof(ratio));
        if (ratio is null) return new(SourceWidth, SourceHeight, QuarterTurns, Box, null);
        var width = Math.Min(Box.Width, Box.Height * ratio.Value);
        return Locked(width, Box.X + Box.Width / 2d, Box.Y + Box.Height / 2d, 0, 0, ratio.Value);
    }
    internal ImageCropState Adjust(CropGrip grip, int dx, int dy)
    {
        if (!Enum.IsDefined(grip)) throw new ArgumentOutOfRangeException(nameof(grip));
        if (grip == CropGrip.Move)
            return new(SourceWidth, SourceHeight, QuarterTurns,
                Box with { X = (int)Math.Clamp((long)Box.X + dx, 0, Width - Box.Width), Y = (int)Math.Clamp((long)Box.Y + dy, 0, Height - Box.Height) }, Aspect);
        var sx = grip is CropGrip.Left or CropGrip.TopLeft or CropGrip.BottomLeft ? -1 : grip is CropGrip.Right or CropGrip.TopRight or CropGrip.BottomRight ? 1 : 0;
        var sy = grip is CropGrip.Top or CropGrip.TopLeft or CropGrip.TopRight ? -1 : grip is CropGrip.Bottom or CropGrip.BottomLeft or CropGrip.BottomRight ? 1 : 0;
        if (Aspect is { } ratio)
        {
            var desired = sx == 0 ? (Box.Height + (double)dy * sy) * ratio : sy == 0 || Math.Abs((double)dx) >= Math.Abs((double)dy) * ratio
                ? Box.Width + (double)dx * sx : (Box.Height + (double)dy * sy) * ratio;
            return Locked(desired, sx < 0 ? Box.Right : sx > 0 ? Box.X : Box.X + Box.Width / 2d,
                sy < 0 ? Box.Bottom : sy > 0 ? Box.Y : Box.Y + Box.Height / 2d, sx, sy, ratio);
        }
        var left = sx < 0 ? (int)Math.Clamp((long)Box.X + dx, 0, Box.Right - 1) : Box.X;
        var right = sx > 0 ? (int)Math.Clamp((long)Box.Right + dx, Box.X + 1, Width) : Box.Right;
        var top = sy < 0 ? (int)Math.Clamp((long)Box.Y + dy, 0, Box.Bottom - 1) : Box.Y;
        var bottom = sy > 0 ? (int)Math.Clamp((long)Box.Bottom + dy, Box.Y + 1, Height) : Box.Bottom;
        return new(SourceWidth, SourceHeight, QuarterTurns, new(left, top, right - left, bottom - top), null);
    }
    private ImageCropState Locked(double desiredWidth, double ax, double ay, int sx, int sy, double ratio)
    {
        var maxWidth = sx < 0 ? ax : sx > 0 ? Width - ax : 2 * Math.Min(ax, Width - ax);
        var maxHeight = sy < 0 ? ay : sy > 0 ? Height - ay : 2 * Math.Min(ay, Height - ay);
        var ideal = Math.Min(Math.Max(1, desiredWidth), Math.Min(maxWidth, maxHeight * ratio));
        var width = Math.Clamp((int)Math.Round(ideal), 1, Math.Max(1, (int)Math.Floor(maxWidth)));
        var height = Math.Clamp((int)Math.Round(width / ratio), 1, Math.Max(1, (int)Math.Floor(maxHeight)));
        var x = (int)Math.Round(sx < 0 ? ax - width : sx > 0 ? ax : ax - width / 2d);
        var y = (int)Math.Round(sy < 0 ? ay - height : sy > 0 ? ay : ay - height / 2d);
        return new(SourceWidth, SourceHeight, QuarterTurns,
            new(Math.Clamp(x, 0, Width - width), Math.Clamp(y, 0, Height - height), width, height), ratio);
    }
}

/// <summary>One inclusive start / exclusive end range; export budgets do not silently move either handle.</summary>
internal sealed record VideoTrimState
{
    internal long DurationTicks { get; }
    internal long MinimumTicks { get; }
    internal long StartTicks { get; }
    internal long EndTicks { get; }
    internal long LengthTicks => EndTicks - StartTicks;
    internal bool Changed => StartTicks != 0 || EndTicks != DurationTicks;
    internal VideoTrimState(long durationTicks, long minimumTicks = 1) : this(durationTicks, minimumTicks, 0, durationTicks) { }
    private VideoTrimState(long durationTicks, long minimumTicks, long start, long end)
    {
        if (durationTicks <= 0 || minimumTicks <= 0 || minimumTicks > durationTicks) throw new ArgumentOutOfRangeException(nameof(durationTicks));
        DurationTicks = durationTicks; MinimumTicks = minimumTicks; StartTicks = start; EndTicks = end;
    }
    internal VideoTrimState WithStart(long ticks) => new(DurationTicks, MinimumTicks, Math.Clamp(ticks, 0, EndTicks - MinimumTicks), EndTicks);
    internal VideoTrimState WithEnd(long ticks) => new(DurationTicks, MinimumTicks, StartTicks, Math.Clamp(ticks, StartTicks + MinimumTicks, DurationTicks));
    internal VideoTrimState Reset() => new(DurationTicks, MinimumTicks);
}
