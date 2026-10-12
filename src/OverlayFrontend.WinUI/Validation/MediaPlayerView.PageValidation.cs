using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal FrameworkElement PageVideo => video;
    private string pageGeometrySnapshot = "";
    internal string PageVideoDiagnostics => $"{pageGeometrySnapshot}; stage={videoStage?.Visibility}; slotLoaded={stage.IsLoaded}; videoLoaded={video.IsLoaded}";
    internal bool PageVideoMatchesSlot()
    {
        if (videoStage?.Parent is not FrameworkElement root) return false;
        var slotBounds = stage.TransformToVisual(root).TransformBounds(new(0, 0, stage.ActualWidth, stage.ActualHeight));
        var expected = MediaTransitionGeometry.Fit(slotBounds, VideoAspectRatio);
        var actual = video.TransformToVisual(root).TransformBounds(new(0, 0, video.ActualWidth, video.ActualHeight));
        pageGeometrySnapshot = $"expected={expected}; actual={actual}";
        return Math.Abs(actual.X - expected.X) < 1 && Math.Abs(actual.Y - expected.Y) < 1 &&
            Math.Abs(actual.Width - expected.Width) < 1 && Math.Abs(actual.Height - expected.Height) < 1;
    }
}
