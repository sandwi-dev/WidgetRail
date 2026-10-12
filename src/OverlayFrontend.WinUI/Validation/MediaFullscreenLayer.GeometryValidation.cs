using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaFullscreenLayer
{
    internal bool MediaEndpointMatches(Rect expected)
    {
        if (current is not { } active || active.MediaBounds?.Invoke() is not { } media) return false;
        var pose = InlinePose(active);
        var mapped = new Rect(media.X * pose.Scale.X + pose.Translation.X,
            media.Y * pose.Scale.Y + pose.Translation.Y, media.Width * pose.Scale.X, media.Height * pose.Scale.Y);
        return Near(mapped, expected);
    }

    internal static bool ValidateLetterboxedEndpoints()
    {
        // Same 16:9 video inside differently shaped outer fullscreen areas.
        var expected = new Rect(80, 120, 640, 360);
        foreach (var media in new[] { new Rect(400, 0, 1600, 900), new Rect(0, 250, 1600, 900), new Rect(0, 0, 1600, 900) })
        {
            var pose = MediaTransitionGeometry.Match(media, expected);
            if (!Near(new(media.X * pose.Scale.X + pose.Translation.X, media.Y * pose.Scale.Y + pose.Translation.Y,
                media.Width * pose.Scale.X, media.Height * pose.Scale.Y), expected)) return false;
        }
        return true;
    }

    private static bool Near(Rect a, Rect b) => Math.Abs(a.X - b.X) < .5 && Math.Abs(a.Y - b.Y) < .5 &&
        Math.Abs(a.Width - b.Width) < .5 && Math.Abs(a.Height - b.Height) < .5;
}
