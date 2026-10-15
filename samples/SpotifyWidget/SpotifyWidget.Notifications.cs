using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

public sealed partial class SpotifyWidget
{
    private readonly SpotifyNotificationPolicy notificationPolicy = new();
    private WidgetActivity? desiredActivity;
    private WidgetNotification? pendingNotification;
    private bool notificationNeedsShow;
    private long notificationRevision;

    private void ObservePlaybackEffects()
    {
        if (LifecycleState == WidgetLifecycleState.Destroying || !HostServices.Capabilities.IsAvailable) return;
        WidgetActivity activity; bool activityChanged; (WidgetNotification Content, bool UpdateOnly)? notice;
        lock (_gate)
        {
            var local = (_spotify as ISpotifyLocalTransport)?.GetLocalTransport()?.Playback;
            activity = local is null && !IsActive ? new() : SpotifyNotificationPolicy.Activity(_playback, local, _localPlayback);
            activityChanged = desiredActivity != activity;
            if (activityChanged) desiredActivity = activity;
            notice = local is not null || IsActive ? notificationPolicy.Observe(local ?? _playback) : null;
            if (notice is { } update)
            {
                pendingNotification = update.Content; notificationNeedsShow |= !update.UpdateOnly; notificationRevision++;
            }
        }
        if (activityChanged)
            Operations.RunLatest("spotify.activity", async context =>
            {
                try
                {
                    var local = HostServices.Activity.SetAsync("local-player", new(LocalAudio: activity.LocalAudio), context.CancellationToken);
                    var remote = HostServices.Activity.SetAsync("remote-playback", new(RemotePlayback: activity.RemotePlayback), context.CancellationToken);
                    var results = await Task.WhenAll(local.AsTask(), remote.AsTask());
                    if (results.Any(result => !result.Accepted && !result.Superseded)) lock (_gate) { if (desiredActivity == activity) desiredActivity = null; }
                }
                catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or OperationCanceledException)
                { lock (_gate) { if (context.IsCurrent && desiredActivity == activity) desiredActivity = null; } }
            }, WidgetOperationLifetime.Widget);
        if (notice is not null)
            Operations.RunLatest("spotify.track-notification", async context =>
            {
                try
                {
                    await Task.Delay(250, context.CancellationToken);
                    WidgetNotification? content; bool show; long revision;
                    lock (_gate) { content = pendingNotification; show = notificationNeedsShow; revision = notificationRevision; }
                    if (content is null) return;
                    var result = show ? await HostServices.Notifications.ShowAsync(content, context.CancellationToken)
                        : await HostServices.Notifications.UpdateAsync(content, context.CancellationToken);
                    if (result.Accepted) lock (_gate) { if (revision == notificationRevision) notificationNeedsShow = false; }
                }
                catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or OperationCanceledException) { }
            }, WidgetOperationLifetime.Widget);
    }
    private void ConfigureSongNotifications() => Operations.RunLatest("spotify.notification-preferences", async context =>
    {
        try
        {
            var result = await HostServices.Notifications.ConfigureAsync("song-changes", context.CancellationToken);
            if (!result.Accepted && context.IsCurrent) { lock (_gate) _status = "Notification preferences could not be opened. Try again."; Invalidate(); }
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
        { if (context.IsCurrent) { lock (_gate) _status = "Notification preferences could not be opened. Try again."; Invalidate(); } }
    });
}
