using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

public sealed partial class SpotifyWidget
{
    private readonly SpotifyNotificationPolicy notificationPolicy = new();
    private WidgetActivity? desiredActivity;
    private readonly WidgetNotificationPublisher trackNotifications;

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
                trackNotifications.Publish(update.Content, update.UpdateOnly);
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
