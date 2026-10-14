namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal enum SpatialDirection { Left, Right, Up, Down }
internal readonly record struct SpatialPoint(double X, double Y);
internal readonly record struct SpatialRect(double X, double Y, double Width, double Height)
{
    internal double Right => X + Width;
    internal double Bottom => Y + Height;
    internal SpatialPoint Center => new(X + Width / 2, Y + Height / 2);
    internal bool Valid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
    internal bool Contains(SpatialPoint point) => point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;
    internal SpatialRect Intersect(SpatialRect other) => new(Math.Max(X, other.X), Math.Max(Y, other.Y),
        Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X)), Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y)));
}
internal sealed record SpatialCandidate(int Frame, int Index, SpatialRect Rect, SpatialPoint Point, bool Source);

internal static class BrowserSpatialGeometry
{
    internal static bool Advances(SpatialPoint from, SpatialPoint to, SpatialDirection direction) => direction switch
    {
        SpatialDirection.Left => to.X < from.X - .5,
        SpatialDirection.Right => to.X > from.X + .5,
        SpatialDirection.Up => to.Y < from.Y - .5,
        _ => to.Y > from.Y + .5,
    };
    // Prefer nearby controls in the same row/column without letting a distant
    // perfectly aligned control beat a nearby, slightly staggered one.
    internal static SpatialCandidate? Choose(IReadOnlyList<SpatialCandidate> candidates, SpatialRect origin, SpatialDirection direction, SpatialPoint? pointer = null)
    {
        SpatialCandidate? best = null;
        var score = double.PositiveInfinity;
        var horizontal = direction is SpatialDirection.Left or SpatialDirection.Right;
        var positive = direction is SpatialDirection.Right or SpatialDirection.Down;
        foreach (var candidate in candidates)
        {
            if (candidate.Source || !candidate.Rect.Valid || !double.IsFinite(candidate.Point.X) || !double.IsFinite(candidate.Point.Y)) continue;
            if (pointer is { } from && !Advances(from, candidate.Point, direction)) continue;
            var target = candidate.Rect;
            var advance = horizontal ? target.Center.X - origin.Center.X : target.Center.Y - origin.Center.Y;
            if ((positive ? advance : -advance) <= .5) continue;
            var primary = Math.Max(0, horizontal
                ? positive ? target.X - origin.Right : origin.X - target.Right
                : positive ? target.Y - origin.Bottom : origin.Y - target.Bottom);
            var lateral = Math.Max(0, horizontal
                ? Math.Max(origin.Y - target.Bottom, target.Y - origin.Bottom)
                : Math.Max(origin.X - target.Right, target.X - origin.Right));
            var center = horizontal ? Math.Abs(target.Center.Y - origin.Center.Y) : Math.Abs(target.Center.X - origin.Center.X);
            // A wide textbox can overlap the source's leading edge yet have its
            // actual landing point far away. Edge gap alone would let it steal
            // a jump from a nearby aligned button.
            var cost = primary + Math.Abs(advance) * .25 + lateral * 3 + center * .2 + (lateral > 0 ? 40 : 0);
            if (cost >= score) continue;
            score = cost; best = candidate;
        }
        return best;
    }
}
