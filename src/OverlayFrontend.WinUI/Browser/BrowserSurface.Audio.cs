namespace WidgetRail.OverlayFrontend.WinUI.Browser;
internal sealed partial class BrowserSurface
{
    private Web.HostedWebViewAudio? audio;
    internal bool HasFailedDocument => !retired && (faulted || popup?.HasFailedDocument == true);
    internal bool PlayingAudio => !retired && !faulted && !sessionStopped &&
        (audio?.Playing == true || popup?.PlayingAudio == true);
}
