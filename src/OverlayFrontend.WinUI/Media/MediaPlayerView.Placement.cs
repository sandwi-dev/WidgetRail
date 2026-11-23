using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private bool retainedForPageExit;
    internal bool CanRetainPageExit => !IsVideo;

    internal void RetainForPageExit()
    {
        retainedForPageExit = true;
        ForgetInlineResume();
        RefreshLoadingActivity();
        input = false;
        RefreshGalleryControls();
        timer.Stop(); StopMediaChrome();
        expandedHost?.Close(restoreFocus: false);
        foreach (var control in controls.Concat(imageControls).Append(position).Append(volume).Append(imageViewport))
        { control.IsTabStop = false; control.IsHitTestVisible = false; }
        video.IsTabStop = false; video.IsHitTestVisible = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(video, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

}
