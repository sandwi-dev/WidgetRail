using System.Net;
using System.Text;
using System.Text.Json;
using GameBarAlternative.Samples.YtMusicWidget;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Endpoint validation permits only bounded loopback HTTP", EndpointValidation),
    ("HTTP client parses now playing and sends authorization", HttpClientParsesSnapshot),
    ("HTTP client maps rating command and body", HttpClientMapsCommand),
    ("Disconnected UI offers controller-first connect and pair", DisconnectedUi),
    ("First activation starts one non-blocking automatic connection", AutoConnectStartsOnce),
    ("Lifecycle preserves one visibility lifetime across visible and interactive states", LifecycleVisibilityLifetime),
    ("Active playback interpolates and polls only while active", ActivePlaybackUpdates),
    ("Optimistic playback survives stale confirmation and rolls back failures", OptimisticStateRules),
    ("Secondary actions render immediate selected and busy feedback without stale flicker", SecondaryActionFeedback),
    ("Rating actions toggle off and send the requested false state", RatingToggleOff),
    ("Secondary action failures roll back their optimistic state", SecondaryActionRollback),
    ("Independent pending secondary actions reconcile without clobbering each other", IndependentSecondaryActions),
    ("A track change clears rating optimism instead of leaking it to the next track", RatingGuardStopsAtTrackChange),
    ("Manual retry works after automatic connection failure", ManualRetryAfterAutoFailure),
    ("Connect exposes loading then renders now playing", ConnectStateFlow),
    ("Connected UI exposes native artwork layout primitives", ConnectedArtworkLayout),
    ("Missing artwork uses a semantic native glyph", MissingArtworkUsesGlyph),
    ("Connected card exposes bounded host quick actions", ConnectedQuickActions),
    ("Dashboard-reserved buttons are rejected as quick actions", ReservedQuickActionIsRejected),
    ("Transport shortcuts route commands and refresh state", TransportCommandFlow),
    ("Errors render a focused retry action", ErrorState),
    ("Pairing displays approval code before completing", PairingStateFlow),
    ("Time formatting is stable and defensive", TimeFormatting),
    ("Shipped manifest is valid", ManifestIsValid),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static Task EndpointValidation()
{
    Assert.Equal("http://127.0.0.1:13091/", YtmDesktopApiClient.ValidateEndpoint("http://127.0.0.1:13091/api").AbsoluteUri);
    Assert.Equal("http://localhost:39999/", YtmDesktopApiClient.ValidateEndpoint("http://localhost:39999").AbsoluteUri);
    Assert.Equal("http://[::1]:13091/", YtmDesktopApiClient.ValidateEndpoint("http://[::1]:13091").AbsoluteUri);
    Assert.Throws<InvalidOperationException>(() => YtmDesktopApiClient.ValidateEndpoint("https://127.0.0.1:13091"));
    Assert.Throws<InvalidOperationException>(() => YtmDesktopApiClient.ValidateEndpoint("http://example.com:13091"));
    Assert.Throws<InvalidOperationException>(() => YtmDesktopApiClient.ValidateEndpoint("http://127.0.0.1:9000"));
    Assert.Throws<InvalidOperationException>(() => YtmDesktopApiClient.ValidateEndpoint("http://user@127.0.0.1:13091"));
    Assert.Throws<InvalidOperationException>(() => YtmDesktopApiClient.ValidateEndpoint("http://127.0.0.1:13091?token=bad"));
    return Task.CompletedTask;
}

static async Task HttpClientParsesSnapshot()
{
    var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
    {
        "/track" => Json("""
            {"video":{"title":"Example Song","author":"Example Artist","videoId":"video-1"},
             "music":{"album":"Example Album"},
             "meta":{"thumbnail":"https://img.example/cover.jpg","duration":245}}
            """),
        "/track/state" => Json("""
            {"id":"video-1","playing":true,"liked":true,"disliked":false,"shuffle":true,"repeat":"one","uiProgress":42.5,"duration":245}
            """),
        _ => throw new InvalidOperationException("Unexpected request."),
    });
    using var client = new YtmDesktopApiClient(token: " secret-token ", handler: handler);
    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal("Example Song", snapshot.Title);
    Assert.Equal("Example Artist", snapshot.Artist);
    Assert.Equal("Example Album", snapshot.Album);
    Assert.Equal(42.5, snapshot.PositionSeconds);
    Assert.True(snapshot.IsPlaying, "Expected playing state.");
    Assert.True(snapshot.IsLiked, "Expected liked state.");
    Assert.Equal(true, snapshot.IsShuffleEnabled);
    Assert.Equal(YtMusicRepeatMode.One, snapshot.RepeatMode);
    Assert.Equal(2, handler.Requests.Count);
    Assert.True(handler.Requests.All(request => request.Authorization == "Bearer secret-token"), "Bearer token was not applied.");
}

static async Task HttpClientMapsCommand()
{
    var handler = new RecordingHandler(_ => Json("{}"));
    using var client = new YtmDesktopApiClient(handler: handler);
    await client.SendCommandAsync(YtMusicCommand.Like, toggleState: false);
    var request = handler.Requests.Single();
    Assert.Equal("POST", request.Method);
    Assert.Equal("/track/like", request.Path);
    Assert.Equal("false", request.Body);
}

static Task DisconnectedUi()
{
    var widget = new YtMusicWidget(new FakeClient());
    var snapshot = widget.Render().CreateSnapshot("ytmusic.test", 0);
    Assert.Equal("connect", snapshot.InitialFocusId);
    Assert.Equal("Connect", Find(snapshot.Root, "connect").Text);
    Assert.Equal("Pair device", Find(snapshot.Root, "pair").Text);
    Assert.Equal("pair", Find(snapshot.Root, "connect").Focus!.Right);
    return Task.CompletedTask;
}

static async Task AutoConnectStartsOnce()
{
    var statusGate = new TaskCompletionSource<YtMusicConnectionInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeClient
    {
        StatusTask = statusGate.Task,
        Snapshot = PlayingSnapshot("Automatic Song"),
    };
    var widget = new YtMusicWidget(fake);
    var invalidations = 0;
    widget.Invalidated += (_, _) => Interlocked.Increment(ref invalidations);

    var first = widget.Render().CreateSnapshot("ytmusic.test", 0);
    Assert.Equal("connect", first.InitialFocusId);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => fake.StatusCalls == 1);

    _ = widget.Render();
    _ = widget.Render();
    await Task.Delay(30);
    Assert.Equal(1, fake.StatusCalls);
    Assert.Equal(YtMusicWidgetConnectionState.Connecting, widget.ConnectionState);

    statusGate.SetResult(new YtMusicConnectionInfo(false));
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connected);
    Assert.Equal(1, fake.StatusCalls);
    Assert.True(invalidations >= 2, "Automatic state changes must invalidate the host view.");
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task LifecycleVisibilityLifetime()
{
    var fake = new FakeClient { Snapshot = PlayingSnapshot("Lifecycle Song") };
    var widget = new LifecycleProbeYtMusicWidget(fake, FastUpdatePolicy());
    var invalidations = 0;
    widget.Invalidated += (_, _) => Interlocked.Increment(ref invalidations);

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
    await Task.Delay(320);
    Assert.Equal(0, fake.StatusCalls);
    Assert.Equal(0, fake.SnapshotCalls);
    Assert.Equal(0, widget.ActivationCount);

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connected);
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    Assert.Equal(1, fake.StatusCalls);
    Assert.Equal(1, widget.ActivationCount);
    Assert.Equal(0, widget.DeactivationCount);
    var visible = widget.LastTransitionTo(WidgetLifecycleState.Visible);
    Assert.True(!visible.StateLifetime.IsCancellationRequested,
        "Visible state token was canceled before leaving Visible.");
    Assert.True(!visible.ActiveLifetime.IsCancellationRequested,
        "Visibility-lifetime token was canceled while visible.");

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, CancellationToken.None);
    var interactive = widget.LastTransitionTo(WidgetLifecycleState.Interactive);
    Assert.True(visible.StateLifetime.IsCancellationRequested,
        "Visible state token must cancel on the transition to Interactive.");
    Assert.True(!interactive.StateLifetime.IsCancellationRequested,
        "Interactive state token was canceled before leaving Interactive.");
    Assert.Equal(visible.ActiveLifetime, interactive.ActiveLifetime);
    Assert.Equal(1, widget.ActivationCount);
    Assert.Equal(1, fake.StatusCalls);

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
    Assert.Equal(1, widget.DeactivationCount);
    Assert.True(interactive.StateLifetime.IsCancellationRequested,
        "Interactive state token must cancel on backgrounding.");
    Assert.True(interactive.ActiveLifetime.IsCancellationRequested,
        "Visibility-lifetime work must cancel on backgrounding.");
    var stoppedCalls = fake.SnapshotCalls;
    var stoppedInvalidations = Volatile.Read(ref invalidations);
    await Task.Delay(320);
    Assert.Equal(stoppedCalls, fake.SnapshotCalls);
    Assert.Equal(stoppedInvalidations, Volatile.Read(ref invalidations));

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    Assert.Equal(2, widget.ActivationCount);
    Assert.Equal(1, fake.StatusCalls);
    await WaitUntil(() => fake.SnapshotCalls > stoppedCalls);
    var transitionCount = widget.TransitionCount;
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    Assert.Equal(transitionCount, widget.TransitionCount);
    Assert.Equal(2, widget.ActivationCount);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
    Assert.Equal(2, widget.DeactivationCount);

    Assert.Equal(
        "Created->Background,Background->Visible,Visible->Interactive," +
        "Interactive->Background,Background->Visible,Visible->Background",
        string.Join(',', widget.Transitions.Select(item => $"{item.Previous}->{item.Current}")));
}

static async Task ActivePlaybackUpdates()
{
    var fake = new FakeClient { Snapshot = PlayingSnapshot("Clocked") with { PositionSeconds = 10 } };
    var widget = new YtMusicWidget(fake, new YtMusicUpdatePolicy
    {
        ProgressInterval = TimeSpan.FromMilliseconds(250),
        PollInterval = TimeSpan.FromMilliseconds(500),
        OptimisticConfirmationWindow = TimeSpan.FromMilliseconds(500),
    });
    var invalidations = 0;
    widget.Invalidated += (_, _) => Interlocked.Increment(ref invalidations);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connected);
    var before = Find(widget.Render().CreateSnapshot("ytmusic.test", 1).Root, "track-progress").Value!.Value;
    await Task.Delay(320);
    var after = Find(widget.Render().CreateSnapshot("ytmusic.test", 2).Root, "track-progress").Value!.Value;
    Assert.True(after > before + 0.2, "Playing progress was not interpolated from monotonic time.");
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    Assert.True(invalidations >= 2, "Active progress and polling did not invalidate the view.");

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
    var stoppedCalls = fake.SnapshotCalls;
    var stoppedInvalidations = Volatile.Read(ref invalidations);
    await Task.Delay(650);
    Assert.Equal(stoppedCalls, fake.SnapshotCalls);
    Assert.Equal(stoppedInvalidations, Volatile.Read(ref invalidations));
}

static async Task OptimisticStateRules()
{
    var fake = new FakeClient { Snapshot = PlayingSnapshot("Before") };
    var widget = new YtMusicWidget(fake);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));

    await widget.OnActionAsync(new WidgetActionEvent("toggle-playback", "play-pause"));
    var optimistic = widget.Render().CreateSnapshot("ytmusic.test", 1);
    Assert.Equal(WidgetGlyph.Play, Find(optimistic.Root, "play-pause").Glyph);

    fake.CommandException = new InvalidOperationException("Companion rejected command");
    await widget.OnActionAsync(new WidgetActionEvent("next", "next"));
    var rolledBack = widget.Render().CreateSnapshot("ytmusic.test", 2);
    Assert.Equal("Before", Find(rolledBack.Root, "track-title").Text);
    Assert.True(Find(rolledBack.Root, "track-progress").Value!.Value > 60,
        "Failed next command did not restore the prior progress.");
    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    Assert.Equal("Companion rejected command", Find(rolledBack.Root, "connection-status").Text);
}

static async Task SecondaryActionFeedback()
{
    var scenarios = new[]
    {
        new SecondaryActionScenario(
            "like", YtMusicCommand.Like,
            snapshot => snapshot with { IsLiked = true, IsDisliked = false }),
        new SecondaryActionScenario(
            "dislike", YtMusicCommand.Dislike,
            snapshot => snapshot with { IsLiked = false, IsDisliked = true }),
        new SecondaryActionScenario(
            "shuffle", YtMusicCommand.Shuffle,
            snapshot => snapshot with { IsShuffleEnabled = true }),
        new SecondaryActionScenario(
            "repeat", YtMusicCommand.Repeat,
            snapshot => snapshot with { RepeatMode = YtMusicRepeatMode.All }),
    };

    foreach (var scenario in scenarios)
    {
        var before = PlayingSnapshot($"Before {scenario.NodeId}") with
        {
            IsShuffleEnabled = false,
            RepeatMode = YtMusicRepeatMode.Off,
        };
        var commandGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeClient { Snapshot = before, CommandTask = commandGate.Task };
        var widget = new YtMusicWidget(fake, FastUpdatePolicy());
        await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
        var invalidations = 0;
        widget.Invalidated += (_, _) => Interlocked.Increment(ref invalidations);

        var actionTask = widget.OnActionAsync(
            new WidgetActionEvent(scenario.NodeId, scenario.NodeId)).AsTask();
        await WaitUntil(() => fake.Commands.Contains(scenario.Command));
        var immediate = widget.Render().CreateSnapshot("ytmusic.test", 1);
        Assert.Equal(true, Find(immediate.Root, scenario.NodeId).IsSelected);
        Assert.Equal(true, Find(immediate.Root, scenario.NodeId).IsBusy);
        Assert.True(invalidations >= 1, $"{scenario.NodeId} did not invalidate immediately.");

        commandGate.SetResult();
        await actionTask;
        var guarded = widget.Render().CreateSnapshot("ytmusic.test", 2);
        Assert.Equal(true, Find(guarded.Root, scenario.NodeId).IsSelected);
        Assert.Equal(true, Find(guarded.Root, scenario.NodeId).IsBusy);

        fake.Snapshot = scenario.Confirm(before);
        await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
        await WaitUntil(() => Find(
            widget.Render().CreateSnapshot("ytmusic.test", 3).Root,
            scenario.NodeId).IsBusy is not true);
        var reconciled = widget.Render().CreateSnapshot("ytmusic.test", 4);
        Assert.Equal(true, Find(reconciled.Root, scenario.NodeId).IsSelected);
        Assert.True(Find(reconciled.Root, scenario.NodeId).IsBusy is not true,
            $"{scenario.NodeId} stayed busy after authoritative confirmation.");
        await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
    }
}

static async Task SecondaryActionRollback()
{
    foreach (var actionId in new[] { "like", "dislike", "shuffle", "repeat" })
    {
        var before = PlayingSnapshot($"Rollback {actionId}") with
        {
            IsShuffleEnabled = false,
            RepeatMode = YtMusicRepeatMode.Off,
        };
        var fake = new FakeClient
        {
            Snapshot = before,
            CommandException = new InvalidOperationException($"{actionId} rejected"),
        };
        var widget = new YtMusicWidget(fake);
        await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
        var invalidations = 0;
        widget.Invalidated += (_, _) => Interlocked.Increment(ref invalidations);

        await widget.OnActionAsync(new WidgetActionEvent(actionId, actionId));
        var rolledBack = widget.Render().CreateSnapshot("ytmusic.test", 1);
        Assert.True(Find(rolledBack.Root, actionId).IsSelected is not true,
            $"{actionId} did not roll back selected state.");
        Assert.True(Find(rolledBack.Root, actionId).IsBusy is not true,
            $"{actionId} did not clear busy state after failure.");
        Assert.Equal($"{actionId} rejected", Find(rolledBack.Root, "connection-status").Text);
        Assert.True(invalidations >= 2, $"{actionId} needs an optimistic and rollback invalidation.");
    }
}

static async Task RatingToggleOff()
{
    var before = PlayingSnapshot("Unlike") with { IsLiked = true };
    var fake = new FakeClient { Snapshot = before };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.OnActionAsync(new WidgetActionEvent("like", "like"));

    Assert.Equal(false, fake.CommandToggleStates.Single());
    var guarded = widget.Render().CreateSnapshot("ytmusic.test", 1);
    Assert.True(Find(guarded.Root, "like").IsSelected is not true,
        "Unlike must render deselected while the companion still reports liked.");
    Assert.Equal(true, Find(guarded.Root, "like").IsBusy);

    fake.Snapshot = before with { IsLiked = false };
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => Find(
        widget.Render().CreateSnapshot("ytmusic.test", 2).Root, "like").IsBusy is not true);
    var confirmed = widget.Render().CreateSnapshot("ytmusic.test", 3);
    Assert.True(Find(confirmed.Root, "like").IsSelected is not true,
        "Authoritative unlike confirmation must remain deselected.");
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task IndependentSecondaryActions()
{
    var before = PlayingSnapshot("Independent") with
    {
        IsShuffleEnabled = false,
        RepeatMode = YtMusicRepeatMode.Off,
    };
    var fake = new FakeClient { Snapshot = before };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));

    await widget.OnActionAsync(new WidgetActionEvent("like", "like"));
    await widget.OnActionAsync(new WidgetActionEvent("shuffle", "shuffle"));
    var guarded = widget.Render().CreateSnapshot("ytmusic.test", 1);
    Assert.Equal(true, Find(guarded.Root, "like").IsSelected);
    Assert.Equal(true, Find(guarded.Root, "like").IsBusy);
    Assert.Equal(true, Find(guarded.Root, "shuffle").IsSelected);
    Assert.Equal(true, Find(guarded.Root, "shuffle").IsBusy);

    fake.Snapshot = before with { IsLiked = true, IsShuffleEnabled = true };
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() =>
    {
        var view = widget.Render().CreateSnapshot("ytmusic.test", 2);
        return Find(view.Root, "like").IsBusy is not true &&
               Find(view.Root, "shuffle").IsBusy is not true;
    });
    var reconciled = widget.Render().CreateSnapshot("ytmusic.test", 3);
    Assert.Equal(true, Find(reconciled.Root, "like").IsSelected);
    Assert.Equal(true, Find(reconciled.Root, "shuffle").IsSelected);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task RatingGuardStopsAtTrackChange()
{
    var before = PlayingSnapshot("Old track");
    var fake = new FakeClient { Snapshot = before };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.OnActionAsync(new WidgetActionEvent("like", "like"));
    Assert.Equal(true, Find(
        widget.Render().CreateSnapshot("ytmusic.test", 1).Root, "like").IsSelected);

    fake.Snapshot = PlayingSnapshot("New track") with { TrackId = "new-track" };
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => Find(
        widget.Render().CreateSnapshot("ytmusic.test", 2).Root, "like").IsBusy is not true);
    var changed = widget.Render().CreateSnapshot("ytmusic.test", 3);
    Assert.True(Find(changed.Root, "like").IsSelected is not true,
        "Optimistic rating leaked to a different authoritative track.");
    Assert.Equal("New track", Find(changed.Root, "track-title").Text);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static YtMusicUpdatePolicy FastUpdatePolicy() => new()
{
    ProgressInterval = TimeSpan.FromMilliseconds(250),
    PollInterval = TimeSpan.FromMilliseconds(250),
    OptimisticConfirmationWindow = TimeSpan.FromSeconds(1),
};

static async Task ManualRetryAfterAutoFailure()
{
    var fake = new FakeClient
    {
        Snapshot = PlayingSnapshot("Recovered Song"),
        StatusFailure = call => call == 1 ? new InvalidOperationException("Companion unavailable") : null,
    };
    var widget = new YtMusicWidget(fake);

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Error);
    for (var attempt = 0; attempt < 20 && widget.ConnectionState != YtMusicWidgetConnectionState.Connected; attempt++)
    {
        await widget.OnActionAsync(new WidgetActionEvent("connect", "retry", ControllerButton.A));
        if (widget.ConnectionState != YtMusicWidgetConnectionState.Connected)
            await Task.Delay(10);
    }

    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    Assert.Equal(2, fake.StatusCalls);
    Assert.Equal("Recovered Song", Find(widget.Render().CreateSnapshot("ytmusic.test", 1).Root, "track-title").Text);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task ConnectStateFlow()
{
    var fake = new FakeClient { Snapshot = PlayingSnapshot("First Song") };
    var statusGate = new TaskCompletionSource<YtMusicConnectionInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
    fake.StatusTask = statusGate.Task;
    var widget = new YtMusicWidget(fake);
    var actionTask = widget.OnActionAsync(new WidgetActionEvent("connect", "connect", ControllerButton.A)).AsTask();

    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connecting);
    var loading = widget.Render().CreateSnapshot("ytmusic.test", 1);
    Assert.Equal("Checking the local companion API and loading now playing…", Find(loading.Root, "loading-detail").Text);

    statusGate.SetResult(new YtMusicConnectionInfo(false));
    await actionTask;
    var connected = widget.Render().CreateSnapshot("ytmusic.test", 2);
    Assert.Equal("play-pause", connected.InitialFocusId);
    Assert.Equal("First Song", Find(connected.Root, "track-title").Text);
    Assert.Equal(ControllerButton.X, Find(connected.Root, "play-pause").Shortcuts.Single().Button);
    Assert.Equal(ControllerButton.LeftBumper, Find(connected.Root, "previous").Shortcuts.Single().Button);
    Assert.Equal(ControllerButton.RightBumper, Find(connected.Root, "next").Shortcuts.Single().Button);
    Assert.Equal(ControllerButton.Y, Find(connected.Root, "refresh").Shortcuts.Single().Button);
}

static async Task ConnectedArtworkLayout()
{
    var widget = new YtMusicWidget(new FakeClient { Snapshot = PlayingSnapshot("Artwork Song") });
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    var snapshot = widget.Render().CreateSnapshot("ytmusic.test", 1);

    var layout = Find(snapshot.Root, "media-layout");
    Assert.Equal(ViewNodeKind.Row, layout.Kind);
    Assert.True(layout.StyleClasses.Contains("media-layout"), "Media layout needs a stable semantic class.");
    Assert.True(Find(snapshot.Root, "media-details").StyleClasses.Contains("media-details"), "Media details class is missing.");
    var artwork = Find(snapshot.Root, "album-artwork");
    Assert.Equal(ViewNodeKind.Image, artwork.Kind);
    Assert.Equal("https://img.example/cover.jpg", artwork.ImageSource);
    Assert.Equal(ImageFit.Cover, artwork.ImageFit);
    Assert.Equal("Album artwork for Artwork Song", artwork.AccessibilityLabel);
    Assert.True(artwork.StyleClasses.Contains("artwork-image"), "Artwork class is missing.");
}

static async Task MissingArtworkUsesGlyph()
{
    var fake = new FakeClient { Snapshot = PlayingSnapshot("No Cover") with { ArtworkUrl = "" } };
    var widget = new YtMusicWidget(fake);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    var snapshot = widget.Render().CreateSnapshot("ytmusic.test", 1);

    Assert.True(FindOrNull(snapshot.Root, "album-artwork") is null, "An empty URL must not produce an image node.");
    var placeholder = Find(snapshot.Root, "artwork-placeholder");
    Assert.Equal(ViewNodeKind.Icon, placeholder.Kind);
    Assert.Equal(WidgetGlyph.Music, placeholder.Glyph);
    Assert.Equal("No album artwork", placeholder.AccessibilityLabel);
}

static async Task TransportCommandFlow()
{
    var fake = new FakeClient { Snapshot = PlayingSnapshot("Before") };
    var widget = new YtMusicWidget(fake);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    fake.Snapshot = PlayingSnapshot("After");
    await widget.OnActionAsync(new WidgetActionEvent("next", "next", ControllerButton.RightBumper));
    Assert.Equal(YtMusicCommand.Next, fake.Commands.Single());
    Assert.Equal(2, fake.SnapshotCalls);
    Assert.Equal("After", Find(widget.Render().CreateSnapshot("ytmusic.test", 3).Root, "track-title").Text);
}

static async Task ConnectedQuickActions()
{
    var widget = new YtMusicWidget(new FakeClient { Snapshot = PlayingSnapshot("Quick Song") });
    var disconnected = widget.Render().CreateSnapshot("ytmusic.test", 0);
    Assert.Equal(0, disconnected.QuickActions.Count);

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connected);
    var connected = widget.Render().CreateSnapshot("ytmusic.test", 1);
    Assert.Equal(3, connected.QuickActions.Count);
    AssertQuickAction(connected, ControllerButton.LeftBumper, "previous", "Previous track");
    AssertQuickAction(connected, ControllerButton.X, "toggle-playback", "Play or pause");
    AssertQuickAction(connected, ControllerButton.RightBumper, "next", "Next track");
    Assert.True(connected.QuickActions.All(action => action.Button != ControllerButton.Y), "Y must remain reserved for host reorder on the dashboard.");
    var roundTrip = SnapshotJson.Deserialize(SnapshotJson.Serialize(connected));
    AssertQuickAction(roundTrip, ControllerButton.X, "toggle-playback", "Play or pause");
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static Task ReservedQuickActionIsRejected()
{
    var view = new WidgetView(
        UI.Stack("root"),
        QuickActions: [new WidgetQuickAction(ControllerButton.Y, "widget-reorder", "Widget action")]);
    var exception = Assert.Throws<ProtocolValidationException>(() => view.CreateSnapshot("ytmusic.test", 0));
    Assert.True(exception.Errors.Any(error => error.Code == "reserved_button"), "Expected reserved_button validation error.");
    return Task.CompletedTask;
}

static async Task ErrorState()
{
    var fake = new FakeClient { StatusException = new InvalidOperationException("YTMDesktop2 is not running") };
    var widget = new YtMusicWidget(fake);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    var snapshot = widget.Render().CreateSnapshot("ytmusic.test", 1);
    Assert.Equal(YtMusicWidgetConnectionState.Error, widget.ConnectionState);
    Assert.Equal("retry", snapshot.InitialFocusId);
    Assert.Equal("YTMDesktop2 is not running", Find(snapshot.Root, "connection-status").Text);
}

static async Task PairingStateFlow()
{
    var fake = new FakeClient { Snapshot = PlayingSnapshot("Paired Song") };
    var approval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    fake.PairCompletionTask = approval.Task;
    var widget = new YtMusicWidget(fake);
    var pairingTask = widget.OnActionAsync(new WidgetActionEvent("pair", "pair")).AsTask();

    await WaitUntil(() => FindOrNull(widget.Render().CreateSnapshot("ytmusic.test", 1).Root, "pairing-code") is not null);
    Assert.Equal("739204", Find(widget.Render().CreateSnapshot("ytmusic.test", 2).Root, "pairing-code").Text);
    approval.SetResult();
    await pairingTask;
    Assert.Equal("739204", fake.CompletedPairingCode);
    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
}

static Task TimeFormatting()
{
    Assert.Equal("0:00", YtMusicWidget.FormatTime(double.NaN));
    Assert.Equal("4:05", YtMusicWidget.FormatTime(245));
    Assert.Equal("1:01:01", YtMusicWidget.FormatTime(3661));
    return Task.CompletedTask;
}

static Task ManifestIsValid()
{
    var path = Path.Combine(AppContext.BaseDirectory, "manifest.json");
    Assert.True(File.Exists(path), $"Manifest was not copied to {path}.");
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(path));
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.Equal(0, errors.Count);
    Assert.True(manifest.Permissions.Contains("network.loopback:13091"), "Loopback permission is missing.");
    return Task.CompletedTask;
}

static YtMusicPlaybackSnapshot PlayingSnapshot(string title) => new(
    "track-id", title, "Artist", "Album", "https://img.example/cover.jpg",
    true, false, false, 65, 240);

static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json"),
};

static async Task WaitUntil(Func<bool> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    while (!predicate())
    {
        await Task.Delay(10, timeout.Token);
    }
}

static ViewNode Find(ViewNode node, string id) =>
    FindOrNull(node, id) ?? throw new InvalidOperationException($"Node '{id}' was not found.");

static ViewNode? FindOrNull(ViewNode node, string id)
{
    if (node.Id == id) return node;
    foreach (var child in node.Children)
    {
        var found = FindOrNull(child, id);
        if (found is not null) return found;
    }
    return null;
}

static void AssertQuickAction(ViewSnapshot snapshot, ControllerButton button, string actionId, string label)
{
    var action = snapshot.QuickActions.Single(item => item.Button == button);
    Assert.Equal(actionId, action.ActionId);
    Assert.Equal(label, action.Label);
}

file sealed record RecordedRequest(string Method, string Path, string? Authorization, string? Body);

file sealed record SecondaryActionScenario(
    string NodeId,
    YtMusicCommand Command,
    Func<YtMusicPlaybackSnapshot, YtMusicPlaybackSnapshot> Confirm);

file sealed record LifecycleTransitionObservation(
    WidgetLifecycleState Previous,
    WidgetLifecycleState Current,
    CancellationToken StateLifetime,
    CancellationToken ActiveLifetime);

file sealed class LifecycleProbeYtMusicWidget(
    IYtMusicClient client,
    YtMusicUpdatePolicy updatePolicy) : YtMusicWidget(client, updatePolicy)
{
    private readonly List<LifecycleTransitionObservation> _transitions = [];
    private int _activationCount;
    private int _deactivationCount;

    public int ActivationCount => Volatile.Read(ref _activationCount);
    public int DeactivationCount => Volatile.Read(ref _deactivationCount);
    public int TransitionCount { get { lock (_transitions) return _transitions.Count; } }
    public IReadOnlyList<LifecycleTransitionObservation> Transitions
    {
        get { lock (_transitions) return _transitions.ToArray(); }
    }

    public LifecycleTransitionObservation LastTransitionTo(WidgetLifecycleState state)
    {
        lock (_transitions) return _transitions.Last(item => item.Current == state);
    }

    protected override async ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        lock (_transitions)
        {
            _transitions.Add(new LifecycleTransitionObservation(
                previous, current, stateLifetime, ActiveCancellationToken));
        }
        await base.OnLifecycleStateChangedAsync(previous, current, stateLifetime);
    }

    protected override async ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationCount);
        await base.OnActivatedAsync(activeLifetime);
    }

    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        Interlocked.Increment(ref _deactivationCount);
        await base.OnDeactivatedAsync(transitionToken);
    }
}

file sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.AbsolutePath,
            request.Headers.Authorization?.ToString(),
            body));
        return respond(request);
    }
}

file sealed class FakeClient : IYtMusicClient
{
    private int _statusCalls;
    private int _snapshotCalls;
    public Task<YtMusicConnectionInfo>? StatusTask { get; set; }
    public Exception? StatusException { get; set; }
    public Func<int, Exception?>? StatusFailure { get; set; }
    public YtMusicPlaybackSnapshot Snapshot { get; set; } = YtMusicPlaybackSnapshot.Empty;
    public List<YtMusicCommand> Commands { get; } = [];
    public List<bool?> CommandToggleStates { get; } = [];
    public Exception? CommandException { get; set; }
    public Task? CommandTask { get; set; }
    public int SnapshotCalls => Volatile.Read(ref _snapshotCalls);
    public Task? PairCompletionTask { get; set; }
    public string? CompletedPairingCode { get; private set; }
    public int StatusCalls => Volatile.Read(ref _statusCalls);

    public Task<YtMusicConnectionInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _statusCalls);
        var failure = StatusException ?? StatusFailure?.Invoke(call);
        if (failure is not null) throw failure;
        return StatusTask ?? Task.FromResult(new YtMusicConnectionInfo(false));
    }

    public Task<YtMusicPlaybackSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _snapshotCalls);
        return Task.FromResult(Snapshot);
    }

    public async Task SendCommandAsync(
        YtMusicCommand command,
        CancellationToken cancellationToken = default,
        bool? toggleState = null)
    {
        Commands.Add(command);
        CommandToggleStates.Add(toggleState);
        if (CommandException is not null) throw CommandException;
        if (CommandTask is not null) await CommandTask.WaitAsync(cancellationToken);
    }

    public Task<YtMusicPairingCode> RequestPairingCodeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new YtMusicPairingCode("739204"));

    public async Task CompletePairingAsync(string code, CancellationToken cancellationToken = default)
    {
        CompletedPairingCode = code;
        if (PairCompletionTask is not null) await PairCompletionTask.WaitAsync(cancellationToken);
    }
}

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
