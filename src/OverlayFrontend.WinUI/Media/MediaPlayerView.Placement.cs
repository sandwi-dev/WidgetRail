using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private MediaPresentationStage? videoStage;
    private bool retainedForPageExit;

    internal void RetainForPageExit()
    {
        retainedForPageExit = true;
        input = false;
        timer.Stop(); videoChrome.Stop();
        expandedHost?.Close(restoreFocus: false);
        foreach (var control in controls.Concat(imageControls).Append(position).Append(volume).Append(imageViewport))
        { control.IsTabStop = false; control.IsHitTestVisible = false; }
        video.IsTabStop = false; video.IsHitTestVisible = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(video, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        if (videoStage is not null) videoStage.IsHitTestVisible = false;
    }

    internal void SynchronizePagePlacement(bool ancestryChanged = false) => videoStage?.RefreshPlacement(ancestryChanged);

    internal void ApplyPagePose(Motion.WidgetMotionHost host, Motion.WidgetMotionPose pose)
    {
        for (DependencyObject? parent = stage; parent is not null; parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
            if (ReferenceEquals(parent, host.Layer)) { videoStage?.ApplyPagePose(host.Layer, host.Viewport, pose); return; }
    }

    internal void EndPageMotion() => videoStage?.EndPageMotion();

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
