using WidgetRail.Samples.YtMusicWidget.Standalone;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class MusicNotificationTests
{
    internal static async Task Run()
    {
        var song = new MusicItem("one", "song", "Song", "Artist", "https://fixture.invalid/art.png");
        var state = new MusicState(true, [song], 0, false, "off", new("one", true, Duration: 180));
        var policy = new MusicNotificationPolicy();
        Check(policy.Observe(state, false) is null, "Disabled is quiet");
        Check(policy.Observe(state, true) is { UpdateOnly: true }, "Enabling mid-track cannot replay a banner");
        var next = song with { Id = "two", Title = "Next" };
        state = state with { Queue = [next], Player = new("two", false, true) };
        Check(policy.Observe(state, true) is null, "Buffering is quiet");
        state = state with { Player = new("two", true, Duration: 191) };
        var notice = policy.Observe(state, true)!.Value;
        Check(!notice.UpdateOnly && notice.Content.Metadata == "3:11" && notice.Content.Retention == WidgetNotificationRetention.BannerOnly &&
            notice.Content.Visibility == WidgetNotificationVisibility.Always && !notice.Content.ShowWidgetIcon, "Now-playing contract");
        Check(policy.Observe(state with { Player = state.Player with { Position = 50 } }, true) is null, "Playback ticks do not publish");
        Check(policy.Observe(state with { Player = state.Player with { Playing = false } }, true) is null, "Pause is quiet");
        Check(policy.Observe(state, true) is null, "Resume is quiet");
        Check(policy.Observe(state with { Player = state.Player with { Duration = 192 } }, true) is { UpdateOnly: true }, "Duration only updates existing content");

        Check(policy.Observe(state with { Queue = [next with { Title = "Updated title" }] }, true) is { UpdateOnly: true }, "Recreated metadata for the same song stays quiet");
        var service = new FakeService();
        var store = new WidgetTestPrivateState();
        var published = new List<WidgetNotificationCommand>();
        WidgetHostServices Services() => new WidgetTestHostServicesBuilder().WithPrivateState(store)
            .WithHandler(WidgetNotificationCapabilities.Publish, (command, _) =>
            {
                lock (published) published.Add(command);
                return ValueTask.FromResult(new WidgetNotificationReceipt(true));
            }).Build();
        var widget = WidgetTestHost.Attach(new StandaloneMusicWidget(service), Services());
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            for (var i = 0; i < 100 && !Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Id == "notifications.configure"); i++) await Task.Delay(20);
            Check(Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Text == "Manage song notifications"), "Setup exposes shared preferences");
            Check(!Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Id is "notifications.toggle" or "notifications.opacity"), "No competing local preferences");
            await widget.OnActionAsync(new("notifications.configure", "notifications.configure"));
            await Task.Delay(100);
            lock (published) Check(published.Any(n => n.ConfigureCategoryId == "song-changes"), "Setup opens host category settings");
            service.SetPlayback(true); await Task.Delay(450);
            lock (published) Check(published.Any(n => n.Notification?.CategoryId == "song-changes" && !n.UpdateOnly), "Track uses declared category");
            await widget.OnActionAsync(new("panel.back", "panel.back"));
            int before; lock (published) before = published.Count;
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            service.SetPlayback(true, "background-next"); await Task.Delay(450);
            lock (published) Check(published.Count > before && published[^1].Notification?.CategoryId == "song-changes", "Background track changes use the shared host policy");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
        var restored = WidgetTestHost.Attach(new StandaloneMusicWidget(new FakeService()), Services());
        try
        {
            await WidgetTestHost.InitializeAsync(restored);
            await WidgetTestHost.SetLifecycleStateAsync(restored, WidgetLifecycleState.Visible);
            await Task.Delay(300);
            Check(!Nodes(restored.Render().CreateSnapshot("restored", 1).Root).Any(n => n.Id == "notifications.configure"), "Completed setup is not reopened on restart");
            await restored.OnActionAsync(new("tab.setup", "setup"));
            Check(Nodes(restored.Render().CreateSnapshot("restored", 2).Root).Any(n => n.Text == "Manage song notifications"), "Preferences remain discoverable");
        }
        finally { await WidgetTestHost.DestroyAsync(restored); }
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
