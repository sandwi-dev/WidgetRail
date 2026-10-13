using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    internal NotificationInbox Notifications { get; } = new();
    internal WebNotificationPreferences NotificationPreferences { get; private set; } = null!;
    internal event Action<NotificationEntry, bool>? NotificationBannerRequested;
    private bool notificationPreferencesReady;

    private async Task LoadNotificationPreferencesAsync()
    {
        await NotificationPreferences.LoadAsync(lifetime.Token);
        notificationPreferencesReady = true;
    }
    private void ReceiveNotification(BrowserSurface source, NativeWebNotification notification)
    {
        if (retired) { notification.Release(); return; }
        var origin = BrowserSiteIdentity.Origin(notification.Origin);
        if (origin is null) { notification.Release(); return; }
        var epoch = source.NotificationEpoch;
        long? id = null;
        void Closed() { if (id is { } key) Notifications.Withdraw(key); }
        notification.CloseRequested += Closed;
        var callbacks = new NotificationCallbacks(() => source.NotificationSourceCurrent(epoch), notification.ReportShown,
            notification.ReportClicked, notification.ReportClosed, () =>
            {
                notification.CloseRequested -= Closed;
                notification.Release();
            });
        var mode = notificationPreferencesReady ? NotificationPreferences.Get(source.NotificationWidgetId) : WebNotificationPresentation.ListOnly;
        var renotify = notification.Renotify;
        var entry = Notifications.Add(source.NotificationWidgetId, source.ProfileName, origin, notification.Tag,
            notification.Title, notification.Body, source, callbacks, mode);
        id = entry?.Id;
        if (entry is not null && mode == WebNotificationPresentation.BannerAndList)
            parking.DispatcherQueue.TryEnqueue(() => { if (!retired && Notifications.Find(entry.Id) is not null) NotificationBannerRequested?.Invoke(entry, !entry.ReplacesPrevious || renotify); });
    }
}
