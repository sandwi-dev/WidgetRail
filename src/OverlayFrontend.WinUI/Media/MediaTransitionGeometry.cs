using System.Numerics;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal static class MediaTransitionGeometry
{
    internal static Rect Fit(Rect viewport, double aspectRatio)
    {
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return viewport;
        var width = Math.Min(viewport.Width, viewport.Height * aspectRatio);
        var height = width / aspectRatio;
        return new(viewport.X + (viewport.Width - width) / 2, viewport.Y + (viewport.Height - height) / 2, width, height);
    }

    internal static WidgetMotionPose Match(Rect media, Rect destination)
    {
        var scale = (float)Math.Min(destination.Width / Math.Max(1, media.Width), destination.Height / Math.Max(1, media.Height));
        return WidgetMotionPose.Identity with { Scale = new(scale, scale, 1), Translation = new Vector3(
            (float)(destination.X + (destination.Width - media.Width * scale) / 2 - media.X * scale),
            (float)(destination.Y + (destination.Height - media.Height * scale) / 2 - media.Y * scale), 0) };
    }
}
