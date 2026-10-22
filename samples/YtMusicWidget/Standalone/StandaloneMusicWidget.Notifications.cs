using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed partial class StandaloneMusicWidget
{
    public sealed record NotificationSettings(bool SetupSeen = false);
    private NotificationSettings notificationSettings = new();
    private bool notificationSettingsReady;
    private readonly MusicNotificationPolicy notificationPolicy = new();
    private readonly WidgetNotificationPublisher trackNotifications;

    private async Task LoadNotificationSettingsAsync(CancellationToken token)
    {
        try
        {
            var stored = await HostServices.PrivateState.ReadAsync<NotificationSettings>(cancellationToken: token);
            lock (_gate) notificationSettings = stored.Value is { } value
                ? value : new();
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
            var update = notificationPolicy.Observe(_service.State, true);
            if (update is null) return;
            trackNotifications.Publish(update.Value.Content, update.Value.UpdateOnly);
        }
    }
    private void NotificationSettingAction(WidgetActionEvent action)
    {
        if (action.ActionId != "notifications.configure") return;
        Operations.RunLatest("music.notification-preferences", async context =>
        {
            try
            {
                if (!(await HostServices.Notifications.ConfigureAsync("song-changes", context.CancellationToken)).Accepted && context.IsCurrent)
                    SetStatus("Notification preferences could not be opened. Try again.");
            }
            catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
            { if (context.IsCurrent) SetStatus("Notification preferences could not be opened. Try again."); }
        });
    }
}
