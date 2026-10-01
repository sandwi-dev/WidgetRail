namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal enum FocusDirection { Up, Down, Left, Right }
internal readonly record struct FocusRectangle(double X, double Y, double Width, double Height)
{
    internal bool IsUsable => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) &&
        double.IsFinite(Height) && Width > 0 && Height > 0;
}
internal readonly record struct FocusCandidate(string Id, FocusRectangle Bounds);

/// <summary>Directional policy only. WinUI supplies geometry and performs focus/scrolling.</summary>
internal static class DirectionalFocusPolicy
{
    internal static string? Choose(FocusRectangle origin, FocusDirection direction, IEnumerable<FocusCandidate> candidates)
    {
        if (!origin.IsUsable) return null;
        string? best = null;
        var bestOutsideBeam = true;
        var bestPrimary = double.PositiveInfinity;
        var bestPerpendicular = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            var rect = candidate.Bounds;
            if (!rect.IsUsable) continue;
            var dx = rect.X + rect.Width / 2 - origin.X - origin.Width / 2;
            var dy = rect.Y + rect.Height / 2 - origin.Y - origin.Height / 2;
            var horizontal = direction is FocusDirection.Left or FocusDirection.Right;
            var primary = direction switch { FocusDirection.Left => -dx, FocusDirection.Right => dx,
                FocusDirection.Up => -dy, _ => dy };
            if (primary <= .5) continue;
            var overlap = horizontal
                ? Math.Min(origin.Y + origin.Height, rect.Y + rect.Height) - Math.Max(origin.Y, rect.Y)
                : Math.Min(origin.X + origin.Width, rect.X + rect.Width) - Math.Max(origin.X, rect.X);
            // Left/Right cannot jump diagonally into another row or header.
            if (horizontal && overlap <= .5) continue;
            var outsideBeam = overlap < 0;
            var perpendicular = Math.Abs(horizontal ? dy : dx);
            var comparison = outsideBeam.CompareTo(bestOutsideBeam);
            if (comparison == 0) comparison = primary.CompareTo(bestPrimary);
            if (comparison == 0) comparison = perpendicular.CompareTo(bestPerpendicular);
            if (comparison == 0) comparison = best is null ? -1 : StringComparer.Ordinal.Compare(candidate.Id, best);
            if (comparison >= 0 && best is not null) continue;
            best = candidate.Id; bestOutsideBeam = outsideBeam; bestPrimary = primary; bestPerpendicular = perpendicular;
        }
        return best;
    }
}
