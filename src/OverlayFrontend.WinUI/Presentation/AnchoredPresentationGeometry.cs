using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal readonly record struct AnchorRectangle(double X, double Y, double Width, double Height)
{
    internal double Right => X + Width;
    internal double Bottom => Y + Height;
    internal bool Valid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) &&
        double.IsFinite(Height) && Width > 0 && Height > 0;
}

/// <summary>Shared passive/interactive placement policy. No screen coordinates, timers or native layout replacements.</summary>
internal static class AnchoredPresentationGeometry
{
    internal static AnchorRectangle Place(double viewportWidth, double viewportHeight, AnchorRectangle? anchor,
        double width, double height, AnchorPlacement preferred = AnchorPlacement.Auto, double inset = 16)
    {
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) || viewportWidth <= 0 || viewportHeight <= 0)
            return default;
        inset = double.IsFinite(inset) ? Math.Clamp(inset, 0, 128) : 16;
        var insetX = Math.Min(inset, viewportWidth / 2);
        var insetY = Math.Min(inset, viewportHeight / 2);
        width = Math.Clamp(double.IsFinite(width) ? width : 360, 0, viewportWidth - 2 * insetX);
        height = Math.Clamp(double.IsFinite(height) ? height : 280, 0, viewportHeight - 2 * insetY);
        var center = new AnchorRectangle((viewportWidth - width) / 2, (viewportHeight - height) / 2, width, height);
        if (anchor is not { Valid: true } a || a.Right <= 0 || a.Bottom <= 0 || a.X >= viewportWidth || a.Y >= viewportHeight)
            return center;
        // Only the visible part of a partially clipped anchor supplies geometry.
        a = new(Math.Max(0, a.X), Math.Max(0, a.Y), Math.Min(viewportWidth, a.Right) - Math.Max(0, a.X),
            Math.Min(viewportHeight, a.Bottom) - Math.Max(0, a.Y));
        var order = preferred switch
        {
            AnchorPlacement.Left => new[] { AnchorPlacement.Left, AnchorPlacement.Right, AnchorPlacement.Below, AnchorPlacement.Above },
            AnchorPlacement.Below => [AnchorPlacement.Below, AnchorPlacement.Above, AnchorPlacement.Right, AnchorPlacement.Left],
            AnchorPlacement.Above => [AnchorPlacement.Above, AnchorPlacement.Below, AnchorPlacement.Right, AnchorPlacement.Left],
            _ => [AnchorPlacement.Right, AnchorPlacement.Left, AnchorPlacement.Below, AnchorPlacement.Above],
        };
        const double gap = 8;
        foreach (var side in order)
        {
            var x = side switch { AnchorPlacement.Right => a.Right + gap, AnchorPlacement.Left => a.X - gap - width, _ => a.X + (a.Width - width) / 2 };
            var y = side switch { AnchorPlacement.Below => a.Bottom + gap, AnchorPlacement.Above => a.Y - gap - height, _ => a.Y + (a.Height - height) / 2 };
            if (side is AnchorPlacement.Right or AnchorPlacement.Left)
            {
                if (x < insetX || x + width > viewportWidth - insetX) continue;
                y = Math.Clamp(y, insetY, viewportHeight - insetY - height);
            }
            else
            {
                if (y < insetY || y + height > viewportHeight - insetY) continue;
                x = Math.Clamp(x, insetX, viewportWidth - insetX - width);
            }
            return new(x, y, width, height);
        }
        return center;
    }
}
