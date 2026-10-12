using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private MediaPresentationStage? videoStage;

    private void EnsureVideoStage()
    {
        if (disposed || !IsVideo || MediaFullscreenLayer.Find(this) is not { } host) return;
        if (videoStage is null)
        {
            videoStage = host.CreateMediaStage(VideoAspectRatio);
            stage.Children.Remove(video);
            videoStage.SurfaceHost.Children.Add(video);
        }
        videoStage.Configure(VideoAspectRatio);
        if (!IsExpanded) videoStage.SetSlot(stage);
    }

    private void ReleaseVideoStage()
    {
        if (videoStage is not { } resident) return;
        videoStage = null;
        resident.SurfaceHost.Children.Remove(video);
        resident.Dispose();
        stage.Children.Add(video);
    }
}
