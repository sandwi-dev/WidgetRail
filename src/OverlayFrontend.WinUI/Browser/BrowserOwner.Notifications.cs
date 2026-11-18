using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    internal NotificationCenter NotificationCenter { get; }
    internal NotificationInbox Notifications => NotificationCenter.Inbox;
    internal NotificationPreferences NotificationPreferences => NotificationCenter.Preferences;
    internal event Action<NotificationEntry, bool>? NotificationBannerRequested
    { add => NotificationCenter.BannerRequested += value; remove => NotificationCenter.BannerRequested -= value; }
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
        var mode = NotificationCenter.Mode(source.NotificationWidgetId);
        var renotify = notification.Renotify;
        var entry = Notifications.Add(source.NotificationWidgetId, source.ProfileName, origin, notification.Tag,
            notification.Title, notification.Body, source, callbacks, mode);
        id = entry?.Id;
        if (entry is not null && source.SupportsNativeNotificationIntents &&
            entries.Values.FirstOrDefault(item => ReferenceEquals(item.Surface, source))?.Hosted is { } hosted)
        {
            // Keep the raw native tag only within this source/epoch-bound entry.
            // NotificationInbox.Tag is a deduplication hash and is never a route.
            var activation = new WebNotificationIntentPayload(hosted.Id, notification.Tag);
            if (activation.IsWellFormed()) entry.NativeIntent = activation;
        }
        if (entry is not null && mode == NotificationPresentation.BannerAndList)
            parking.DispatcherQueue.TryEnqueue(() => { if (!retired && Notifications.Find(entry.Id) is not null) NotificationCenter.RequestBanner(entry, !entry.ReplacesPrevious || renotify); });
    }
    internal bool TryGetNativeNotificationIntent(NotificationEntry notification, out WebNotificationIntentPayload? intent)
    {
        intent = null;
        if (retired || !ReferenceEquals(Notifications.Find(notification.Id), notification) || !notification.Available ||
            notification.NativeIntent is not { } candidate || notification.Owner is not BrowserSurface source ||
            !source.SupportsNativeNotificationIntents || !entries.Values.Any(entry => ReferenceEquals(entry.Surface, source) && entry.Hosted?.Id == candidate.SessionId))
            return false;
        intent = candidate;
        return true;
    }

}
