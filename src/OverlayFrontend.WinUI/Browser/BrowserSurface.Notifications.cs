using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private NativeWebNotifications? nativeNotifications;
    internal Action<BrowserSurface, NativeWebNotification>? ReceiveNotification { get; set; }
    internal Action<BrowserSurface>? InvalidateNotifications { get; set; }
    internal string NotificationWidgetId => widgetId;
    internal long NotificationEpoch => pageEpoch;
    internal bool NotificationPresentationActive => visible && input && !retired;
    internal bool NotificationSourceCurrent(long epoch) => !retired && !faulted && !sessionStopped && epoch == pageEpoch && core is not null;

    private void InitializeNotifications(CoreWebView2 expected)
    {
        nativeNotifications = new(expected, notification =>
        {
            if (retired || IsProviderContent || !ReferenceEquals(core, expected) || ReceiveNotification is null) notification.Release();
            else ReceiveNotification(this, notification);
        });
    }
}
