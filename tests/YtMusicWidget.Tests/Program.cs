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
    ("Windows Credential Manager persists and removes the token", WindowsCredentialStoreRoundTrip),
    ("HTTP client reloads a durable credential", HttpClientLoadsDurableCredential),
    ("Pairing never sends stale authorization and replaces the token", PairingReplacesStaleCredential),
    ("HTTP 401 is classified as expired authorization", HttpUnauthorizedIsTyped),
    ("Disconnected UI offers controller-first connect and pair", DisconnectedUi),
    ("Every connection state publishes one bounded standard surface", SurfaceContractAcrossConnectionStates),
    ("First activation starts one non-blocking automatic connection", AutoConnectStartsOnce),
    ("Lifecycle preserves one visibility lifetime across visible and interactive states", LifecycleVisibilityLifetime),
    ("Active playback interpolates and polls only while active", ActivePlaybackUpdates),
    ("Authoritative polls reconcile drift without visible regressions", ProgressPollReconciliation),
    ("Optimistic playback survives stale confirmation and rolls back failures", OptimisticStateRules),
    ("Secondary actions render immediate selected and busy feedback without stale flicker", SecondaryActionFeedback),
    ("Rating actions toggle off and send the requested false state", RatingToggleOff),
    ("Secondary action failures roll back their optimistic state", SecondaryActionRollback),
    ("Independent pending secondary actions reconcile without clobbering each other", IndependentSecondaryActions),
    ("A track change clears rating optimism instead of leaking it to the next track", RatingGuardStopsAtTrackChange),
    ("Manual retry works after automatic connection failure", ManualRetryAfterAutoFailure),
    ("Valid credentials reconnect when the server reports auth required", ValidCredentialReconnects),
    ("An unauthorized connect clears the credential and returns to pairing", UnauthorizedConnectRequiresPairing),
    ("An unauthorized poll clears the credential and returns to pairing", UnauthorizedPollRequiresPairing),
    ("Connect exposes loading then renders now playing", ConnectStateFlow),
    ("Connected UI exposes native artwork layout primitives", ConnectedArtworkLayout),
    ("Missing artwork uses a semantic native glyph", MissingArtworkUsesGlyph),
    ("Connected card exposes bounded host quick actions", ConnectedQuickActions),
    ("Dashboard-reserved buttons are rejected as quick actions", ReservedQuickActionIsRejected),
    ("Open-window shortcuts route from every connected control and focusless root", OpenWindowShortcutsRouteFromEveryFocus),
    ("Focused A activation remains local while host-owned inputs remain unclaimed", FocusedActivationRemainsLocal),
    ("YT Music shortcuts cannot escape their active input scope", OpenWindowShortcutsRespectInputScopes),
    ("Transport shortcuts route commands and refresh state", TransportCommandFlow),
    ("Transport transitions preserve complete metadata through stale snapshots", TransportTransitionPreservesMetadata),
    ("Repeated transport commands do not wait for snapshot reconciliation", RepeatedTransportCommandsBypassReconciliation),
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

static async Task SurfaceContractAcrossConnectionStates()
{
    var statusGate = new TaskCompletionSource<YtMusicConnectionInfo>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeClient
    {
        StatusTask = statusGate.Task,
        Snapshot = PlayingSnapshot("Surface Song"),
    };
    var widget = new YtMusicWidget(fake);
    AssertStandardSurface(widget.Render().CreateSnapshot("ytmusic.surface", 0));

    var connect = widget.OnActionAsync(new WidgetActionEvent("connect", "connect")).AsTask();
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connecting);
    AssertStandardSurface(widget.Render().CreateSnapshot("ytmusic.surface", 1));
    statusGate.SetResult(new YtMusicConnectionInfo(false));
    await connect;
    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    AssertStandardSurface(widget.Render().CreateSnapshot("ytmusic.surface", 2));

    var pairingGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pairingFake = new FakeClient
    {
        PairCompletionTask = pairingGate.Task,
        Snapshot = PlayingSnapshot("Paired Surface Song"),
    };
    var pairingWidget = new YtMusicWidget(pairingFake);
    var pairing = pairingWidget.OnActionAsync(new WidgetActionEvent("pair", "pair")).AsTask();
    await WaitUntil(() => pairingWidget.ConnectionState == YtMusicWidgetConnectionState.Pairing);
    AssertStandardSurface(pairingWidget.Render().CreateSnapshot("ytmusic.surface", 3));
    pairingGate.SetResult();
    await pairing;
    AssertStandardSurface(pairingWidget.Render().CreateSnapshot("ytmusic.surface", 4));

    var errorWidget = new YtMusicWidget(new FakeClient
    {
        StatusException = new InvalidOperationException("unavailable"),
    });
    await errorWidget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    Assert.Equal(YtMusicWidgetConnectionState.Error, errorWidget.ConnectionState);
    AssertStandardSurface(errorWidget.Render().CreateSnapshot("ytmusic.surface", 5));
}

static void AssertStandardSurface(ViewSnapshot snapshot)
{
    Assert.Equal(ProtocolConstants.SurfaceHintsVersion, snapshot.ProtocolVersion);
    Assert.True(snapshot.Surface is not null, "YT Music omitted its bounded surface hint.");
    Assert.Equal(WidgetSurfaceMode.Standard, snapshot.Surface!.Mode);
    Assert.Equal(760D, snapshot.Surface.PreferredWidth);
    Assert.Equal(440D, snapshot.Surface.PreferredHeight);
    Assert.Equal(640D, snapshot.Surface.MinimumWidth);
    Assert.Equal(420D, snapshot.Surface.MinimumHeight);
    var errors = ViewSnapshotValidator.Validate(snapshot);
    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
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
    using var client = new YtmDesktopApiClient(
        token: " secret-token ",
        handler: handler,
        credentialStore: new FakeCredentialStore());
    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal("Example Song", snapshot.Title);
    Assert.Equal("Example Artist", snapshot.Artist);
    Assert.Equal("Example Album", snapshot.Album);
    Assert.Equal(42.5, snapshot.PositionSeconds);
    Assert.True(snapshot.IsPlaying, "Expected playing state.");
    Assert.True(snapshot.IsLiked, "Expected liked state.");
    Assert.Equal(true, snapshot.IsShuffleEnabled);
    Assert.Equal(YtMusicRepeatMode.One, snapshot.RepeatMode);
    Assert.Equal("video-1", snapshot.MetadataTrackId);
    Assert.True(snapshot.HasCompleteMetadata, "Complete API metadata was not identified.");
    Assert.Equal(2, handler.Requests.Count);
    Assert.True(handler.Requests.All(request => request.Authorization == "Bearer secret-token"), "Bearer token was not applied.");
}

static async Task HttpClientMapsCommand()
{
    var handler = new RecordingHandler(_ => Json("{}"));
    using var client = new YtmDesktopApiClient(
        handler: handler,
        credentialStore: new FakeCredentialStore());
    await client.SendCommandAsync(YtMusicCommand.Like, toggleState: false);
    var request = handler.Requests.Single();
    Assert.Equal("POST", request.Method);
    Assert.Equal("/track/like", request.Path);
    Assert.Equal("false", request.Body);
}

static Task WindowsCredentialStoreRoundTrip()
{
    if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
    var endpoint = $"http://127.0.0.1:{Random.Shared.Next(10000, 39999)}";
    var token = $"credential-test-{Guid.NewGuid():N}";
    var store = new WindowsCredentialManagerYtMusicCredentialStore(endpoint);
    store.ClearToken();
    try
    {
        Assert.Equal(null, store.LoadToken());
        store.SaveToken(token);
        Assert.Equal(token, store.LoadToken());
        store.ClearToken();
        Assert.Equal(null, store.LoadToken());
    }
    finally
    {
        store.ClearToken();
    }
    return Task.CompletedTask;
}

static async Task HttpClientLoadsDurableCredential()
{
    var store = new FakeCredentialStore("durable-token");
    var handler = new RecordingHandler(_ => Json("""{"authRequired":true}"""));
    using var client = new YtmDesktopApiClient(handler: handler, credentialStore: store);

    var status = await client.GetStatusAsync();

    Assert.True(status.AuthRequired, "Expected auth-required server configuration.");
    Assert.True(status.HasCredential, "The durable credential was not loaded.");
    Assert.Equal("Bearer durable-token", handler.Requests.Single().Authorization);
}

static async Task PairingReplacesStaleCredential()
{
    var store = new FakeCredentialStore("stale-token");
    var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
    {
        "/auth/requestcode" => Json("""{"code":"739204"}"""),
        "/auth/request" => Json("""{"token":"replacement-token"}"""),
        "/" => Json("""{"authRequired":true}"""),
        _ => throw new InvalidOperationException($"Unexpected request {request.RequestUri!.AbsolutePath}."),
    });
    using var client = new YtmDesktopApiClient(handler: handler, credentialStore: store);

    var pairing = await client.RequestPairingCodeAsync();
    await client.CompletePairingAsync(pairing.Code);
    var status = await client.GetStatusAsync();

    Assert.Equal("replacement-token", store.Token);
    Assert.Equal(1, store.SaveCalls);
    Assert.True(handler.Requests.Take(2).All(request => request.Authorization is null),
        "Pairing endpoints must not receive a stale bearer credential.");
    Assert.Equal("Bearer replacement-token", handler.Requests[2].Authorization);
    Assert.True(status.HasCredential, "The replacement token was not activated.");
}

static async Task HttpUnauthorizedIsTyped()
{
    var store = new FakeCredentialStore("rejected-token");
    var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
    {
        Content = new StringContent("credential rejected", Encoding.UTF8, "application/json"),
    });
    using var client = new YtmDesktopApiClient(handler: handler, credentialStore: store);
    var classified = false;
    try
    {
        _ = await client.GetStatusAsync();
    }
    catch (YtMusicAuthorizationRequiredException)
    {
        classified = true;
    }
    Assert.True(classified, "HTTP 401 did not use the authorization-required recovery path.");
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

static async Task ProgressPollReconciliation()
{
    var clock = new ManualTimeProvider();
    var initial = PlayingSnapshot("Progress Track") with
    {
        TrackId = "progress-id",
        MetadataTrackId = "progress-id",
        PositionSeconds = 10,
        DurationSeconds = 100,
    };
    var staleBackward = initial with { PositionSeconds = 11 };
    var modestForward = initial with { PositionSeconds = 14 };
    var realSeek = initial with { PositionSeconds = 20 };
    var paused = initial with { IsPlaying = false, PositionSeconds = 20.5 };
    var changedTrack = PlayingSnapshot("Changed Progress Track") with
    {
        TrackId = "changed-id",
        MetadataTrackId = "changed-id",
        PositionSeconds = 2,
        DurationSeconds = 100,
    };
    var shortenedDuration = changedTrack with
    {
        PositionSeconds = 12,
        DurationSeconds = 5,
    };
    var gates = Enumerable.Range(0, 6).Select(_ => NewSnapshotGate()).ToArray();
    var finalBlockedPoll = NewSnapshotGate();
    var fake = new FakeClient { Snapshot = initial };
    fake.SnapshotAsync = (call, token) => call switch
    {
        1 => Task.FromResult(initial),
        2 => gates[0].Task.WaitAsync(token),
        3 => gates[1].Task.WaitAsync(token),
        4 => gates[2].Task.WaitAsync(token),
        5 => gates[3].Task.WaitAsync(token),
        6 => gates[4].Task.WaitAsync(token),
        7 => gates[5].Task.WaitAsync(token),
        _ => finalBlockedPoll.Task.WaitAsync(token),
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy(), clock);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);

    clock.Advance(TimeSpan.FromSeconds(2));
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    AssertBetween(11.999, 12.001, ProgressValue(widget, 1),
        "Monotonic projection did not reach the expected pre-poll position.");
    gates[0].SetResult(staleBackward);
    await WaitUntil(() => fake.SnapshotCalls >= 3);
    AssertBetween(11.999, 12.001, ProgressValue(widget, 2),
        "A small stale backward poll moved progress backward.");

    clock.Advance(TimeSpan.FromSeconds(1));
    gates[1].SetResult(modestForward);
    await WaitUntil(() => fake.SnapshotCalls >= 4);
    AssertBetween(12.999, 13.001, ProgressValue(widget, 3),
        "A modest forward poll snapped instead of beginning a correction.");
    clock.Advance(TimeSpan.FromSeconds(1));
    AssertBetween(14.34, 14.36, ProgressValue(widget, 4),
        "Forward drift was not eased at the bounded correction rate.");

    gates[2].SetResult(realSeek);
    await WaitUntil(() => fake.SnapshotCalls >= 5);
    AssertBetween(19.999, 20.001, ProgressValue(widget, 5),
        "A real seek of at least three seconds did not snap to the server.");

    clock.Advance(TimeSpan.FromSeconds(1));
    gates[3].SetResult(paused);
    await WaitUntil(() => fake.SnapshotCalls >= 6);
    AssertBetween(20.499, 20.501, ProgressValue(widget, 6),
        "Pause did not anchor the authoritative position.");
    clock.Advance(TimeSpan.FromSeconds(1));
    AssertBetween(20.499, 20.501, ProgressValue(widget, 7),
        "Paused progress continued advancing.");

    gates[4].SetResult(changedTrack);
    await WaitUntil(() => fake.SnapshotCalls >= 7);
    AssertBetween(1.999, 2.001, ProgressValue(widget, 8),
        "Track change did not anchor the new track position.");

    clock.Advance(TimeSpan.FromSeconds(10));
    gates[5].SetResult(shortenedDuration);
    await WaitUntil(() => fake.SnapshotCalls >= 8);
    AssertBetween(4.999, 5.001, ProgressValue(widget, 9),
        "Progress was not clamped to a shortened duration.");
    Assert.Equal(5d, Find(
        widget.Render().CreateSnapshot("ytmusic.test", 10).Root,
        "track-progress").Maximum!.Value);

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static double ProgressValue(YtMusicWidget widget, long sequence) => Find(
    widget.Render().CreateSnapshot("ytmusic.test", sequence).Root,
    "track-progress").Value!.Value;

static void AssertBetween(double minimum, double maximum, double actual, string message)
{
    if (actual < minimum || actual > maximum)
        throw new InvalidOperationException($"{message} Expected {minimum}..{maximum}, got {actual}.");
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
    TransportConfirmationWindow = TimeSpan.FromSeconds(1),
    TransportRefreshInitialDelay = TimeSpan.FromMilliseconds(10),
    TransportRefreshDelayStep = TimeSpan.FromMilliseconds(10),
    TransportRefreshAttempts = 5,
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

static async Task ValidCredentialReconnects()
{
    var fake = new FakeClient
    {
        HasCredential = true,
        StatusInfo = new YtMusicConnectionInfo(AuthRequired: true, HasCredential: true),
        Snapshot = PlayingSnapshot("Authenticated Song"),
    };
    var widget = new YtMusicWidget(fake);

    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));

    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    Assert.Equal(1, fake.SnapshotCalls);
    Assert.Equal(0, fake.ClearCredentialCalls);
    Assert.Equal("Authenticated Song", Find(
        widget.Render().CreateSnapshot("ytmusic.test", 1).Root,
        "track-title").Text);
}

static async Task UnauthorizedConnectRequiresPairing()
{
    var fake = new FakeClient
    {
        HasCredential = true,
        StatusInfo = new YtMusicConnectionInfo(AuthRequired: true, HasCredential: true),
        SnapshotFailure = _ => new YtMusicAuthorizationRequiredException(),
    };
    var widget = new YtMusicWidget(fake);

    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));

    Assert.Equal(YtMusicWidgetConnectionState.Disconnected, widget.ConnectionState);
    Assert.Equal(1, fake.ClearCredentialCalls);
    Assert.True(!fake.HasCredential, "The rejected credential was not removed from the client.");
    var snapshot = widget.Render().CreateSnapshot("ytmusic.test", 1);
    Assert.Equal("connect", snapshot.InitialFocusId);
    Assert.Equal("Authorization expired · pair device", Find(snapshot.Root, "connection-status").Text);
}

static async Task UnauthorizedPollRequiresPairing()
{
    var fake = new FakeClient
    {
        HasCredential = true,
        StatusInfo = new YtMusicConnectionInfo(AuthRequired: true, HasCredential: true),
        Snapshot = PlayingSnapshot("Initially Authorized"),
        SnapshotFailure = call => call >= 2 ? new YtMusicAuthorizationRequiredException() : null,
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connected);
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Disconnected);

    Assert.Equal(1, fake.ClearCredentialCalls);
    Assert.True(!fake.HasCredential, "The rejected polling credential was not removed.");
    Assert.Equal("Authorization expired · pair device", Find(
        widget.Render().CreateSnapshot("ytmusic.test", 1).Root,
        "connection-status").Text);
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
    Assert.Equal(0, Find(connected.Root, "play-pause").Shortcuts.Count);
    Assert.Equal(0, Find(connected.Root, "previous").Shortcuts.Count);
    Assert.Equal(0, Find(connected.Root, "next").Shortcuts.Count);
    Assert.Equal(0, Find(connected.Root, "refresh").Shortcuts.Count);
    AssertWindowShortcut(connected.Root, ControllerButton.X, "toggle-playback");
    AssertWindowShortcut(connected.Root, ControllerButton.LeftBumper, "previous");
    AssertWindowShortcut(connected.Root, ControllerButton.RightBumper, "next");
    AssertWindowShortcut(connected.Root, ControllerButton.Y, "refresh");
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
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    fake.Snapshot = PlayingSnapshot("After") with
    {
        TrackId = "after-id",
        MetadataTrackId = "after-id",
    };
    await widget.OnActionAsync(new WidgetActionEvent("next", "next", ControllerButton.RightBumper));
    Assert.Equal(YtMusicCommand.Next, fake.Commands.Single());
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    await WaitUntil(() => "After" == Find(
        widget.Render().CreateSnapshot("ytmusic.test", 3).Root,
        "track-title").Text);
    Assert.Equal(2, fake.SnapshotCalls);
    Assert.Equal("After", Find(widget.Render().CreateSnapshot("ytmusic.test", 3).Root, "track-title").Text);
}

static async Task TransportTransitionPreservesMetadata()
{
    var clock = new ManualTimeProvider();
    var oldTrack = PlayingSnapshot("Complete Old Track") with
    {
        TrackId = "old-id",
        MetadataTrackId = "old-id",
        PositionSeconds = 90,
    };
    var stale = oldTrack with { PositionSeconds = 92 };
    var empty = oldTrack with
    {
        TrackId = "new-id",
        Title = "YouTube Music",
        Artist = "No track metadata available",
        Album = string.Empty,
        ArtworkUrl = string.Empty,
        PositionSeconds = 0.2,
        MetadataTrackId = string.Empty,
        HasCompleteMetadata = false,
    };
    var mismatched = oldTrack with
    {
        TrackId = "new-id",
        Title = "Mismatched Intermediate Track",
        MetadataTrackId = "old-id",
        PositionSeconds = 0.5,
    };
    var completeNew = PlayingSnapshot("Complete New Track") with
    {
        TrackId = "new-id",
        MetadataTrackId = "new-id",
        PositionSeconds = 1,
    };
    var staleGate = NewSnapshotGate();
    var emptyGate = NewSnapshotGate();
    var mismatchedGate = NewSnapshotGate();
    var completeGate = NewSnapshotGate();
    var fake = new FakeClient { Snapshot = oldTrack };
    fake.SnapshotAsync = (call, token) => call switch
    {
        1 => Task.FromResult(oldTrack),
        2 => staleGate.Task.WaitAsync(token),
        3 => emptyGate.Task.WaitAsync(token),
        4 => mismatchedGate.Task.WaitAsync(token),
        5 => completeGate.Task.WaitAsync(token),
        _ => Task.FromResult(completeNew),
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy(), clock);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    clock.Advance(TimeSpan.FromSeconds(3));
    Assert.True(Find(widget.Render().CreateSnapshot("ytmusic.test", 1).Root,
        "track-progress").Value!.Value >= 93, "The test clock did not advance playback.");

    await widget.OnActionAsync(new WidgetActionEvent("next", "next", ControllerButton.RightBumper));
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    var immediate = widget.Render().CreateSnapshot("ytmusic.test", 2);
    Assert.Equal(0d, Find(immediate.Root, "track-progress").Value!.Value);
    Assert.Equal("Complete Old Track", Find(immediate.Root, "track-title").Text);

    staleGate.SetResult(stale);
    await WaitUntil(() => fake.SnapshotCalls >= 3);
    var afterStale = widget.Render().CreateSnapshot("ytmusic.test", 3);
    Assert.Equal(0d, Find(afterStale.Root, "track-progress").Value!.Value);
    Assert.Equal("Complete Old Track", Find(afterStale.Root, "track-title").Text);

    emptyGate.SetResult(empty);
    await WaitUntil(() => fake.SnapshotCalls >= 4);
    var afterEmpty = widget.Render().CreateSnapshot("ytmusic.test", 4);
    Assert.Equal("Complete Old Track", Find(afterEmpty.Root, "track-title").Text);
    Assert.Equal("https://img.example/cover.jpg", Find(afterEmpty.Root, "album-artwork").ImageSource);

    mismatchedGate.SetResult(mismatched);
    await WaitUntil(() => fake.SnapshotCalls >= 5);
    var afterMismatch = widget.Render().CreateSnapshot("ytmusic.test", 5);
    Assert.Equal("Complete Old Track", Find(afterMismatch.Root, "track-title").Text);

    completeGate.SetResult(completeNew);
    await WaitUntil(() => "Complete New Track" == Find(
        widget.Render().CreateSnapshot("ytmusic.test", 6).Root,
        "track-title").Text);
    Assert.Equal(5, fake.SnapshotCalls);
}

static async Task RepeatedTransportCommandsBypassReconciliation()
{
    var oldTrack = PlayingSnapshot("Before Queue") with
    {
        TrackId = "old-id",
        MetadataTrackId = "old-id",
    };
    var finalTrack = PlayingSnapshot("After Queue") with
    {
        TrackId = "final-id",
        MetadataTrackId = "final-id",
        PositionSeconds = 0,
    };
    var neverCompletes = NewSnapshotGate();
    var finalGate = NewSnapshotGate();
    var fake = new FakeClient { Snapshot = oldTrack };
    fake.SnapshotAsync = (call, token) => call switch
    {
        1 => Task.FromResult(oldTrack),
        2 => neverCompletes.Task.WaitAsync(token),
        _ => finalGate.Task.WaitAsync(token),
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));

    await widget.OnActionAsync(new WidgetActionEvent("next", "next", ControllerButton.RightBumper));
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    var secondCommand = widget.OnActionAsync(
        new WidgetActionEvent("next", "next", ControllerButton.RightBumper)).AsTask();
    await secondCommand.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Equal(2, fake.Commands.Count);
    Assert.True(fake.Commands.All(command => command == YtMusicCommand.Next),
        "Repeated RB actions did not preserve transport command order.");
    await WaitUntil(() => fake.SnapshotCalls >= 3);
    finalGate.SetResult(finalTrack);
    await WaitUntil(() => "After Queue" == Find(
        widget.Render().CreateSnapshot("ytmusic.test", 1).Root,
        "track-title").Text);
}

static TaskCompletionSource<YtMusicPlaybackSnapshot> NewSnapshotGate() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

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

static async Task OpenWindowShortcutsRouteFromEveryFocus()
{
    var widget = new RoutingProbeYtMusicWidget(
        new FakeClient { Snapshot = PlayingSnapshot("Controller Song") },
        FastUpdatePolicy());
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connected);
    const long snapshotSequence = 40;
    var snapshot = widget.RenderSnapshot("ytmusic.routing", snapshotSequence);
    Assert.Equal("ytmusic-root", snapshot.ActiveInputScopeId);

    var focusTargets = new[]
    {
        "previous", "play-pause", "next", "refresh",
        "shuffle", "like", "dislike", "repeat",
    };
    var shortcuts = new (ControllerButton Button, string ActionId)[]
    {
        (ControllerButton.LeftBumper, "previous"),
        (ControllerButton.X, "toggle-playback"),
        (ControllerButton.RightBumper, "next"),
        (ControllerButton.Y, "refresh"),
    };
    long inputSequence = 1;
    foreach (var focusedId in focusTargets)
    {
        foreach (var shortcut in shortcuts)
        {
            var handled = await widget.OnControllerInputAsync(OpenWidgetInput(
                shortcut.Button,
                focusedId,
                snapshotSequence,
                snapshot.ActiveInputScopeId,
                inputSequence++));
            Assert.True(handled,
                $"{shortcut.Button} was not handled while '{focusedId}' had focus.");
            var action = await widget.NextActionAsync();
            Assert.Equal(shortcut.ActionId, action.ActionId);
            Assert.Equal("ytmusic-root", action.SourceElementId);
            Assert.Equal(shortcut.Button, action.ControllerButton);
        }
    }

    foreach (var shortcut in shortcuts)
    {
        var handled = await widget.OnControllerInputAsync(OpenWidgetInput(
            shortcut.Button,
            focusedElementId: null,
            snapshotSequence,
            snapshot.ActiveInputScopeId,
            inputSequence++));
        Assert.True(handled, $"{shortcut.Button} did not use the focusless root fallback.");
        var action = await widget.NextActionAsync();
        Assert.Equal(shortcut.ActionId, action.ActionId);
        Assert.Equal("ytmusic-root", action.SourceElementId);
    }

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task FocusedActivationRemainsLocal()
{
    var widget = new RoutingProbeYtMusicWidget(
        new FakeClient { Snapshot = PlayingSnapshot("Activation Song") },
        FastUpdatePolicy());
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connected);
    const long snapshotSequence = 50;
    var snapshot = widget.RenderSnapshot("ytmusic.activation", snapshotSequence);
    var activations = new (string ElementId, string ActionId)[]
    {
        ("previous", "previous"),
        ("play-pause", "toggle-playback"),
        ("next", "next"),
        ("refresh", "refresh"),
        ("shuffle", "shuffle"),
        ("like", "like"),
        ("dislike", "dislike"),
        ("repeat", "repeat"),
    };
    long inputSequence = 1;
    foreach (var activation in activations)
    {
        Assert.True(await widget.OnControllerInputAsync(OpenWidgetInput(
            ControllerButton.A,
            activation.ElementId,
            snapshotSequence,
            snapshot.ActiveInputScopeId,
            inputSequence++)),
            $"A did not activate '{activation.ElementId}'.");
        var action = await widget.NextActionAsync();
        Assert.Equal(activation.ActionId, action.ActionId);
        Assert.Equal(activation.ElementId, action.SourceElementId);

        Assert.True(!await widget.OnControllerInputAsync(OpenWidgetInput(
            ControllerButton.B,
            activation.ElementId,
            snapshotSequence,
            snapshot.ActiveInputScopeId,
            inputSequence++)),
            $"The widget captured host-owned B while '{activation.ElementId}' had focus.");
    }
    Assert.True(!await widget.OnControllerInputAsync(OpenWidgetInput(
        ControllerButton.DPadDown,
        "play-pause",
        snapshotSequence,
        snapshot.ActiveInputScopeId,
        inputSequence)),
        "The widget captured host-owned D-pad navigation.");

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task OpenWindowShortcutsRespectInputScopes()
{
    var widget = new ScopedRoutingProbeYtMusicWidget(
        new FakeClient { Snapshot = PlayingSnapshot("Scoped Song") },
        FastUpdatePolicy());
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => widget.ConnectionState == YtMusicWidgetConnectionState.Connected);

    widget.ActiveScope = "ytmusic-window";
    var primary = widget.RenderSnapshot("ytmusic.scope", 60);
    Assert.True(await widget.OnControllerInputAsync(OpenWidgetInput(
        ControllerButton.RightBumper,
        "like",
        primary.Sequence,
        primary.ActiveInputScopeId)),
        "The active YT Music scope did not inherit its window shortcut.");
    var action = await widget.NextActionAsync();
    Assert.Equal("next", action.ActionId);
    Assert.Equal("ytmusic-root", action.SourceElementId);

    widget.ActiveScope = "dialog-window";
    var dialog = widget.RenderSnapshot("ytmusic.scope", 61);
    Assert.True(!await widget.OnControllerInputAsync(OpenWidgetInput(
        ControllerButton.LeftBumper,
        "dialog-action",
        dialog.Sequence,
        dialog.ActiveInputScopeId)),
        "A nested dialog without LB captured the parent YT Music shortcut.");
    Assert.True(!await widget.OnControllerInputAsync(OpenWidgetInput(
        ControllerButton.X,
        "like",
        dialog.Sequence,
        dialog.ActiveInputScopeId)),
        "Focus outside the active dialog escaped into the YT Music scope.");
    Assert.True(!await widget.OnControllerInputAsync(OpenWidgetInput(
        ControllerButton.RightBumper,
        "like",
        dialog.Sequence,
        "ytmusic-window")),
        "An input naming a non-active scope bypassed the snapshot scope.");

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static ControllerInputEvent OpenWidgetInput(
    ControllerButton button,
    string? focusedElementId,
    long snapshotSequence,
    string activeInputScopeId,
    long inputSequence = 0) => new(
        button,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        focusedElementId,
        Sequence: inputSequence,
        ActiveInputScopeId: activeInputScopeId,
        SnapshotSequence: snapshotSequence);

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

static void AssertWindowShortcut(ViewNode root, ControllerButton button, string actionId)
{
    var shortcut = root.Shortcuts.Single(item => item.Button == button);
    Assert.Equal(actionId, shortcut.ActionId);
    Assert.Equal(ControllerEventPhase.Pressed, shortcut.Phase);
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

file class RoutingProbeYtMusicWidget(
    IYtMusicClient client,
    YtMusicUpdatePolicy updatePolicy) : YtMusicWidget(client, updatePolicy)
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _actions =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();

    public async Task<WidgetActionEvent> NextActionAsync() =>
        await _actions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        _actions.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class ScopedRoutingProbeYtMusicWidget(
    IYtMusicClient client,
    YtMusicUpdatePolicy updatePolicy) : RoutingProbeYtMusicWidget(client, updatePolicy)
{
    public string ActiveScope { get; set; } = "ytmusic-window";

    public override WidgetView Render()
    {
        var view = base.Render();
        var ytmusicWindow = ((StackElement)view.Root).InputScope("ytmusic-window");
        return view with
        {
            Root = UI.Stack("scope-test-shell",
                ytmusicWindow,
                UI.Stack("dialog-root",
                    UI.Button("Dialog action", "dialog-action", "dialog-action"))
                    .InputScope("dialog-window")),
            InitialFocusId = ActiveScope == "dialog-window" ? "dialog-action" : view.InitialFocusId,
            ActiveInputScopeId = ActiveScope,
        };
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
    public YtMusicConnectionInfo StatusInfo { get; set; } = new(false);
    public Exception? StatusException { get; set; }
    public Func<int, Exception?>? StatusFailure { get; set; }
    public YtMusicPlaybackSnapshot Snapshot { get; set; } = YtMusicPlaybackSnapshot.Empty;
    public Func<int, Exception?>? SnapshotFailure { get; set; }
    public Func<int, CancellationToken, Task<YtMusicPlaybackSnapshot>>? SnapshotAsync { get; set; }
    public List<YtMusicCommand> Commands { get; } = [];
    public List<bool?> CommandToggleStates { get; } = [];
    public Exception? CommandException { get; set; }
    public Task? CommandTask { get; set; }
    public int SnapshotCalls => Volatile.Read(ref _snapshotCalls);
    public Task? PairCompletionTask { get; set; }
    public string? CompletedPairingCode { get; private set; }
    public int StatusCalls => Volatile.Read(ref _statusCalls);
    public bool HasCredential { get; set; }
    public int ClearCredentialCalls { get; private set; }

    public Task<YtMusicConnectionInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _statusCalls);
        var failure = StatusException ?? StatusFailure?.Invoke(call);
        if (failure is not null) throw failure;
        return StatusTask ?? Task.FromResult(StatusInfo with
        {
            HasCredential = StatusInfo.HasCredential || HasCredential,
        });
    }

    public Task<YtMusicPlaybackSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _snapshotCalls);
        var failure = SnapshotFailure?.Invoke(call);
        if (failure is not null) throw failure;
        return SnapshotAsync?.Invoke(call, cancellationToken) ?? Task.FromResult(Snapshot);
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
        HasCredential = true;
    }

    public void ClearCredential()
    {
        ClearCredentialCalls++;
        HasCredential = false;
    }
}

file sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    public void Advance(TimeSpan interval) =>
        Interlocked.Add(ref _timestamp, interval.Ticks);
}

file sealed class FakeCredentialStore(string? token = null) : IYtMusicCredentialStore
{
    public string? Token { get; private set; } = token;
    public int SaveCalls { get; private set; }
    public int ClearCalls { get; private set; }

    public string? LoadToken() => Token;

    public void SaveToken(string value)
    {
        SaveCalls++;
        Token = value;
    }

    public void ClearToken()
    {
        ClearCalls++;
        Token = null;
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
