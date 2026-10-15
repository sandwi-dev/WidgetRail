using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SpotifyNotificationTests
{
    internal static Task Run()
    {
        var playback = new SpotifyPlaybackSummary(true, true, 0, 180000, 0, SpotifyRepeatState.Off, false,
            new(SpotifyPlaybackItemType.Track, "First", "Artist", null, "https://example.com/art.png", "spotify:track:first"),
            new(false, false, false, false, false, false, false, false), "Spotify");
        var policy = new SpotifyNotificationPolicy();
        Check(policy.Observe(playback) is null);
        Check(policy.Observe(playback with { ProgressMilliseconds = 1000 }) is null);
        Check(policy.Observe(playback with { IsPlaying = false }) is null);
        Check(policy.Observe(playback) is null);
        playback = playback with { Item = playback.Item! with { Uri = "spotify:track:second", Title = "Second" } };
        var notice = policy.Observe(playback)!.Value;
        Check(!notice.UpdateOnly && notice.Content.CategoryId == "song-changes" && notice.Content.Metadata == "3:00" && !notice.Content.ShowWidgetIcon);
        Check(notice.Content.Visibility == WidgetNotificationVisibility.Always && notice.Content.Retention == WidgetNotificationRetention.BannerOnly);
        Check(policy.Observe(playback with { DurationMilliseconds = 181000 }) is { UpdateOnly: true });
        var local = new SpotifyLocalPlaybackSummary(SpotifyLocalPlaybackState.Active, "This PC", 50, null);
        Check(SpotifyNotificationPolicy.Activity(playback, playback, local) == new WidgetActivity(LocalAudio: true));
        Check(SpotifyNotificationPolicy.Activity(playback, playback, local with { VolumePercent = 0 }) == new WidgetActivity());
        Check(SpotifyNotificationPolicy.Activity(playback, null, local) == new WidgetActivity());
        Check(SpotifyNotificationPolicy.Activity(playback, null, local with { State = SpotifyLocalPlaybackState.Ready }) == new WidgetActivity(RemotePlayback: true));
        Check(SpotifyNotificationPolicy.Activity(playback with { IsPlaying = false }, null, null) == new WidgetActivity());
        return Task.CompletedTask;
    }
    internal static async Task BackgroundPlayback()
    {
        var harness = SpotifyHarness.Ready();
        harness.LocalPlayback = new(SpotifyLocalPlaybackState.Active, "This PC", 60, null);
        harness.Transport = new(harness.Playback);
        var commands = new List<WidgetNotificationCommand>();
        var activity = new List<WidgetActivity>();
        var widget = WidgetTestHost.Attach(new SpotifyWidget(harness), new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetNotificationCapabilities.Publish, (command, _) => { lock(commands) commands.Add(command); return ValueTask.FromResult(new WidgetNotificationReceipt(true)); })
            .WithHandler(WidgetActivityCapabilities.Report, (value, _) => { lock(activity) activity.Add(value); return ValueTask.FromResult(new WidgetActivityReceipt(true)); }).Build());
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => widget.ViewState == SpotifyWidgetViewState.Ready);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            var reads = harness.PlaybackCalls;
            var next = harness.Playback with { Item = harness.Playback.Item! with { Uri = "spotify:track:background", Title = "Background track" } };
            harness.PublishTransport(new(next));
            await Until(() => { lock(commands) return commands.Any(c => c.Notification?.Title == "Background track" && !c.UpdateOnly); });
            Check(harness.PlaybackCalls == reads);
            harness.PublishTransport(new(next with { IsPlaying = false }));
            await Until(() => { lock(activity) return activity.Count > 0 && activity[^1] == new WidgetActivity(); });
            await WidgetTestHost.DestroyAsync(widget);
            int before; lock(commands) before=commands.Count;
            harness.PublishTransport(new(next with { Item = next.Item! with { Uri = "spotify:track:retired" } }));
            await Task.Delay(300);
            lock(commands) Check(commands.Count == before);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
        static async Task Until(Func<bool> ready)
        { var end=Environment.TickCount64+5000; while(!ready()) { if(Environment.TickCount64>end) throw new TimeoutException("Background playback effect"); await Task.Delay(20); } }
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Spotify notification/activity policy failed"); }
}
