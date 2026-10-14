using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed partial class StandaloneMusicWidget
{
    public sealed record NotificationSettings(bool Enabled = false, int BackgroundOpacity = 100, bool SetupSeen = false);
    private NotificationSettings notificationSettings = new();
    private bool notificationSettingsReady;
    private readonly MusicNotificationPolicy notificationPolicy = new();
    private WidgetNotification? pendingNotification;
    private bool notificationNeedsShow;
    private long notificationRevision;

    private async Task LoadNotificationSettingsAsync(CancellationToken token)
    {
        try
        {
            var stored = await HostServices.PrivateState.ReadAsync<NotificationSettings>(cancellationToken: token);
            lock (_gate) notificationSettings = stored.Value is { } value
                ? value with { BackgroundOpacity = Math.Clamp(value.BackgroundOpacity, 40, 100) } : new();
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or System.Text.Json.JsonException)
        { lock (_gate) notificationSettings = new(SetupSeen: true); }
        lock (_gate) { notificationSettingsReady = true; notificationPolicy.Observe(_service.State, false); }
    }
    private void SaveNotificationSettings()
    {
        if (!HostServices.Capabilities.IsAvailable) return;
        Operations.RunLatest("music.notification-settings", async context =>
        {
            await Task.Delay(150, context.CancellationToken);
            NotificationSettings value; lock (_gate) value = notificationSettings;
            try { await HostServices.PrivateState.WriteAsync(value, cancellationToken: context.CancellationToken); }
            catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
            { if (context.IsCurrent) SetStatus("Notification settings could not be saved. Try again."); }
        }, WidgetOperationLifetime.Widget);
    }
    private void ObserveTrackNotification()
    {
        lock (_gate)
        {
            if (!notificationSettingsReady) return;
            var update = notificationPolicy.Observe(_service.State, notificationSettings.Enabled, notificationSettings.BackgroundOpacity);
            if (update is null) return;
            pendingNotification = update.Value.Content;
            notificationNeedsShow |= !update.Value.UpdateOnly;
            notificationRevision++;
        }
        Operations.RunLatest("music.track-notification", async context =>
        {
            try
            {
                // Coalesce rapid skips and early duration/artwork updates before crossing the host channel.
                await Task.Delay(250, context.CancellationToken);
                WidgetNotification? content; bool show; long revision;
                lock (_gate) { content = pendingNotification; show = notificationNeedsShow; revision = notificationRevision; }
                if (content is null) return;
                var result = show ? await HostServices.Notifications.ShowAsync(content, context.CancellationToken)
                    : await HostServices.Notifications.UpdateAsync(content, context.CancellationToken);
                if (result.Accepted) lock (_gate) { if (revision == notificationRevision) notificationNeedsShow = false; }
            }
            catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or OperationCanceledException)
            { /* Playback never depends on optional notification delivery. */ }
        }, WidgetOperationLifetime.Widget);
    }
    private void NotificationSettingAction(WidgetActionEvent action)
    {
        lock (_gate)
        {
            notificationSettings = action.ActionId == "notifications.toggle"
                ? notificationSettings with { Enabled = !notificationSettings.Enabled }
                : notificationSettings with { BackgroundOpacity = (int)Math.Clamp(action.RequestedValue ?? 100, 40, 100) };
            pendingNotification = null; notificationNeedsShow = false; notificationRevision++;
            notificationPolicy.Observe(_service.State, false);
        }
        Operations.Cancel("music.track-notification");
        SaveNotificationSettings(); Invalidate();
        if (!notificationSettings.Enabled)
            Operations.RunLatest("music.notification-dismiss", async context =>
            {
                try
                {
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        if ((await HostServices.Notifications.DismissAsync("now-playing", context.CancellationToken)).Accepted) break;
                        await Task.Delay(100, context.CancellationToken);
                    }
                }
                catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or OperationCanceledException) { }
            }, WidgetOperationLifetime.Widget);
    }
}
