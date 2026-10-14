using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;
internal sealed partial class OverlayShellPage
{
    private void ReceiveWidgetNotification(object? sender, WidgetNotificationEventArgs args)
    {
        var session = owner?.Session;
        if (session is null || !ReferenceEquals(sender, session)) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (notificationCenter is not { } center) return;
            await center.Ready;
            bool Current() => !retired && ReferenceEquals(owner?.Session, session) && session.IsHostEffectAuthorityCurrent(args.Notification.Authority);
            if (!Current()) return;
            center.Publish(args.Notification.Authority.WidgetId, args.Notification.Authority, args.Notification.Command, Current);
        });
    }
}
