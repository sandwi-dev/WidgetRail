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

        var service = new FakeService();
        var store = new WidgetTestPrivateState();
        var published = new List<WidgetNotificationCommand>();
        var dismissCalls = 0;
        WidgetHostServices Services() => new WidgetTestHostServicesBuilder().WithPrivateState(store)
            .WithHandler(WidgetNotificationCapabilities.Publish, (command, _) =>
            {
                lock (published) published.Add(command);
                return ValueTask.FromResult(new WidgetNotificationReceipt(command.Notification is not null || Interlocked.Increment(ref dismissCalls) > 1));
            }).Build();
        var widget = WidgetTestHost.Attach(new StandaloneMusicWidget(service), Services());
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
            for (var i = 0; i < 100 && !Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Id == "notifications.toggle"); i++) await Task.Delay(20);
            Check(Nodes(widget.Render().CreateSnapshot("test", 1).Root).Any(n => n.Text == "Song-change banners: Off"), "First setup exposes the default-off option");
            await widget.OnActionAsync(new("notifications.toggle", "notifications.toggle"));
            Check(Nodes(widget.Render().CreateSnapshot("test", 2).Root).Any(n => n.Text == "Banner opacity · 100%"),
                "Enabled banner settings visibly identify the opacity slider");
            service.SetPlayback(true);
            await Task.Delay(450);
            lock (published) Check(published.Any(n => n.Notification?.Title == "A song title" && !n.UpdateOnly), "Widget publishes a track banner through SDK");
            await widget.OnActionAsync(new("notifications.opacity", "notifications.opacity", RequestedValue: 70));
            Check(Nodes(widget.Render().CreateSnapshot("test", 3).Root).Any(n => n.Text == "Banner opacity · 70%"),
                "Visible opacity label follows controller adjustment");
            await widget.OnActionAsync(new("panel.back", "panel.back"));
            int before; lock (published) before = published.Count;
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            service.SetPlayback(true); await Task.Delay(450);
            lock (published) Check(published.Count > before && published[^1].Notification?.BackgroundOpacityPercent == 70, "Background track changes still reach the SDK with selected opacity");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
        var restored = WidgetTestHost.Attach(new StandaloneMusicWidget(new FakeService()), Services());
        try
        {
            await WidgetTestHost.InitializeAsync(restored);
            await WidgetTestHost.SetLifecycleStateAsync(restored, WidgetLifecycleState.Visible);
            await Task.Delay(300);
            Check(!Nodes(restored.Render().CreateSnapshot("restored", 1).Root).Any(n => n.Id == "notifications.toggle"), "Completed setup is not reopened on restart");
            await restored.OnActionAsync(new("tab.setup", "setup"));
            Check(Nodes(restored.Render().CreateSnapshot("restored", 2).Root).Any(n => n.Text == "Song-change banners: On"), "Notification opt-in survives widget restart");
            await restored.OnActionAsync(new("notifications.toggle", "notifications.toggle"));
            await Task.Delay(450);
            Check(dismissCalls == 2, "Disabling retries dismissal when an earlier publication is still in flight");
        }
        finally { await WidgetTestHost.DestroyAsync(restored); }
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
