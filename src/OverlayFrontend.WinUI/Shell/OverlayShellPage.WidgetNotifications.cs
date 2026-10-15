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
            if (notificationCenter is not { } center) { await ReleaseAsync(); return; }
            await center.Ready;
            bool Current() => !retired && ReferenceEquals(owner?.Session, session) && session.IsHostEffectAuthorityCurrent(args.Notification.Authority);
            if (!Current()) { await ReleaseAsync(); return; }
            var descriptor = catalogItems.FirstOrDefault(item => item.Id == args.Notification.Authority.WidgetId);
            if (descriptor is null) { await ReleaseAsync(); return; }
            try { await center.Preferences.EnsureCategoriesAsync(descriptor.Id, descriptor.NotificationCategories, lifetime.Token); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
            { await ReleaseAsync(); return; }
            if (!Current()) { await ReleaseAsync(); return; }
            if (args.Notification.Command.ConfigureCategoryId is { } category)
            {
                if (visible && !HostChoiceActive && !LocalInstallActive && !SystemFilePickerOpen &&
                    (foreground && interactive && activeWidget == descriptor.Id || PinnedInputActive && pinned?.WidgetId == descriptor.Id))
                    await ShowNotificationPreferencesAsync(descriptor, category);
                return;
            }
            center.Publish(args.Notification.Authority.WidgetId, args.Notification, args.Notification.Command, Current,
                () => _ = ReleaseAsync(), sourceVisible: NotificationSourceVisible(descriptor.Id));
            async Task ReleaseAsync()
            {
                try { if (!retired) await session.ReleaseNotificationIntentAsync(args.Notification, lifetime.Token); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("notification-release", null, error.GetType().Name); }
            }
        });
    }
}
