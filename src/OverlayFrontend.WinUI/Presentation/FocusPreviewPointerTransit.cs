namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Pointer-only travel intent between an anchor and its popup. No focus or input capture.</summary>
internal sealed class FocusPreviewPointerTransit
{
    private (double X, double Y)? origin;
    private bool fromPopup;
    private double previousDistance = double.PositiveInfinity;
    private AnchorRectangle anchor, popup;
    internal void Reset() { origin = null; previousDistance = double.PositiveInfinity; }
    internal void SetBounds(AnchorRectangle source, AnchorRectangle content)
    {
        if (source != anchor || content != popup) Reset();
        anchor = source; popup = content;
    }
    internal bool Retain((double X, double Y) point)
    {
        if (!anchor.Valid || !popup.Valid || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) { Reset(); return false; }
        if (Contains(popup, point)) { origin = point; fromPopup = true; previousDistance = Distance(point, anchor); return true; }
        if (Contains(anchor, point)) { origin = point; fromPopup = false; previousDistance = Distance(point, popup); return true; }
        if (origin is not { } start) return false;
        var destination = fromPopup ? anchor : popup;
        var distance = Distance(point, destination);
        // A path away from the destination must release neighboring rows promptly.
        if (distance > previousDistance + 2 || !InCorridor(point, start, destination)) { Reset(); return false; }
        previousDistance = distance;
        return true;
    }
    internal static bool Contains(AnchorRectangle rect, (double X, double Y) point) =>
        rect.Valid && point.X >= rect.X && point.X <= rect.Right && point.Y >= rect.Y && point.Y <= rect.Bottom;
    private static double Distance((double X, double Y) point, AnchorRectangle rect)
    {
        var x = Math.Max(Math.Max(rect.X - point.X, 0), point.X - rect.Right);
        var y = Math.Max(Math.Max(rect.Y - point.Y, 0), point.Y - rect.Bottom);
        return Math.Sqrt(x*x + y*y);
    }
    private static bool InCorridor((double X, double Y) p, (double X, double Y) start, AnchorRectangle target)
    {
        const double buffer = 4;
        (double X, double Y) a, b, c, d;
        if (start.X < target.X)
        { a=(start.X-buffer,start.Y-buffer); b=(target.X+buffer,target.Y-buffer); c=(target.X+buffer,target.Bottom+buffer); d=(start.X-buffer,start.Y+buffer); }
        else if (start.X > target.Right)
        { a=(start.X+buffer,start.Y-buffer); b=(target.Right-buffer,target.Y-buffer); c=(target.Right-buffer,target.Bottom+buffer); d=(start.X+buffer,start.Y+buffer); }
        else if (start.Y < target.Y)
        { a=(start.X-buffer,start.Y-buffer); b=(target.X-buffer,target.Y+buffer); c=(target.Right+buffer,target.Y+buffer); d=(start.X+buffer,start.Y-buffer); }
        else
        { a=(start.X-buffer,start.Y+buffer); b=(target.X-buffer,target.Bottom-buffer); c=(target.Right+buffer,target.Bottom-buffer); d=(start.X+buffer,start.Y+buffer); }
        return InTriangle(p,a,b,c) || InTriangle(p,a,c,d);
    }
    private static bool InTriangle((double X,double Y) p,(double X,double Y) a,(double X,double Y) b,(double X,double Y) c)
    {
        static double Cross((double X,double Y) q,(double X,double Y) r,(double X,double Y) s) => (q.X-s.X)*(r.Y-s.Y)-(r.X-s.X)*(q.Y-s.Y);
        var x=Cross(p,a,b);var y=Cross(p,b,c);var z=Cross(p,c,a);
        return !((x < -.001 || y < -.001 || z < -.001) && (x > .001 || y > .001 || z > .001));
    }
}
