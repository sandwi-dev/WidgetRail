using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.Samples.YtMusicWidget;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

if (args is ["--export-renderer-fixture", var rendererFixturePath])
{
    await ExportRendererFixture(rendererFixturePath);
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Client is bound to the declared exact loopback capability", EndpointValidation),
    ("Broker client parses now playing and requests host authorization", BrokerClientParsesSnapshot),
    ("Broker client caches stable track metadata and refreshes it on identity change", BrokerClientCachesStableMetadata),
    ("Broker client maps rating command and body", BrokerClientMapsCommand),
    ("Private secret service persists and removes the token without reading it", PrivateSecretRoundTrip),
    ("Broker client detects a durable private secret without reading it", BrokerClientLoadsDurableSecret),
    ("Pairing never sends stale authorization and replaces the token", PairingReplacesStaleCredential),
    ("HTTP 401 is classified as expired authorization", HttpUnauthorizedIsTyped),
    ("HTTP failure bodies never escape the companion client", HttpFailureBodyIsDiscarded),
    ("Capability failures render actionable status without provider details", CapabilityFailuresAreSafe),
    ("Disconnected UI offers controller-first connect and pair", DisconnectedUi),
    ("Connected idle exposes only refresh without media authority", ConnectedIdleIsTruthful),
    ("Transient refresh failure preserves last-good media and recovers", TransientRefreshPreservesLastGood),
    ("Every connection state publishes one bounded standard surface", SurfaceContractAcrossConnectionStates),
    ("First activation starts one non-blocking automatic connection", AutoConnectStartsOnce),
    ("Runtime operation lanes replace widget-owned task registries", RuntimeOperationsOwnLifecycleWork),
    ("YT Music internals remain split by stable responsibility", ResponsibilitySplitContract),
    ("Compiled render path references only the current Shortcut contract", CurrentShortcutContract),
    ("Closed action and connection policies are directly testable", DirectActionAndConnectionPolicies),
    ("Companion confirmation timeout and rollback are directly testable", DirectCompanionConfirmationPolicy),
    ("Repeated immutable presentation is byte deterministic", PurePresentationIsDeterministic),
    ("Lifecycle preserves one visibility lifetime across visible and interactive states", LifecycleVisibilityLifetime),
    ("Late pairing completion cannot survive the active lifetime", LatePairingCannotCommitAfterDeactivation),
    ("Late polling completion cannot publish after deactivation", LatePollCannotCommitAfterDeactivation),
    ("Active playback interpolates and polls only while active", ActivePlaybackUpdates),
    ("Authoritative polls reconcile drift without visible regressions", ProgressPollReconciliation),
    ("Optimistic playback survives stale confirmation and rolls back failures", OptimisticStateRules),
    ("Secondary actions render immediate selected and busy feedback without stale flicker", SecondaryActionFeedback),
    ("Repeat one uses a distinct non-color semantic glyph", RepeatOneUsesDistinctGlyph),
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
    ("Connected media stays one responsive controller composition", ResponsiveConnectedComposition),
    ("Missing artwork uses a semantic native glyph", MissingArtworkUsesGlyph),
    ("Connected card exposes bounded host quick actions", ConnectedQuickActions),
    ("Dashboard-reserved buttons are rejected as quick actions", ReservedQuickActionIsRejected),
    ("Open-window shortcuts route from every connected control and focusless root", OpenWindowShortcutsRouteFromEveryFocus),
    ("Focused A activation remains local while host-owned inputs remain unclaimed", FocusedActivationRemainsLocal),
    ("YT Music shortcuts cannot escape their active input scope", OpenWindowShortcutsRespectInputScopes),
    ("Transport shortcuts route commands and refresh state", TransportCommandFlow),
    ("Transport transitions preserve complete metadata through stale snapshots", TransportTransitionPreservesMetadata),
    ("Repeated transport commands do not wait for snapshot reconciliation", RepeatedTransportCommandsBypassReconciliation),
    ("Repeated play-pause commands do not wait for snapshot reconciliation", RepeatedPlaybackCommandsBypassReconciliation),
    ("Accepted playback reconciliation outlives its action request", PlaybackReconciliationOutlivesActionRequest),
    ("Transport reconciliation is canceled and drained when the widget becomes inactive", TransportReconciliationStopsWhenInactive),
    ("Superseded transport failures cannot commit attempt-local state", SupersededTransportFailuresCannotCommit),
    ("Lifecycle-stale authorization failures cannot disconnect", LifecycleStaleAuthorizationCannotCommit),
    ("Lifecycle-stale explicit Refresh ordinary failures cannot commit", LifecycleStaleExplicitRefreshOrdinaryFailureCannotCommit),
    ("Lifecycle-stale explicit Refresh authorization failures cannot commit", LifecycleStaleExplicitRefreshAuthorizationCannotCommit),
    ("Optimistic playback does not masquerade as companion confirmation", PlaybackReconciliationRejectsStaleState),
    ("Errors render a focused retry action", ErrorState),
    ("Pairing displays approval code before completing", PairingStateFlow),
    ("Time formatting is stable and defensive", TimeFormatting),
    ("Shipped package metadata and responsive styles are valid", PackageAssetsAreValid),
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

static async Task EndpointValidation()
{
    var host = new BrokerClientHarness(request => BrokerJson("{\"authRequired\":false}"));
    using var client = new YtmDesktopApiClient(host.Services);
    _ = await client.GetStatusAsync();

    Assert.Equal(13091, YtmDesktopApiClient.CompanionPort);
    Assert.Equal("network.loopback:13091",
        WidgetLoopbackCapabilities.CapabilityId(YtmDesktopApiClient.CompanionPort));
    var request = host.Requests.Single();
    Assert.Equal(false, request.IsPost);
    Assert.Equal("/", request.Request.Path);
    Assert.Equal(null, request.Request.BearerSecretSlot);
    Assert.Equal(WidgetCommunityPlatformLimits.DefaultLoopbackTimeoutMilliseconds,
        request.Request.TimeoutMilliseconds);
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
    var expectedVersion = ProtocolConstants.SurfaceHintsVersion;
    if (snapshot.QuickActions.Any(action => action.Capability is not null))
        expectedVersion = Math.Max(
            expectedVersion, ProtocolConstants.DashboardGestureAuthorityVersion);
    if (Nodes(snapshot.Root).SelectMany(node => node.Shortcuts)
        .Any(shortcut => !string.IsNullOrWhiteSpace(shortcut.Label)))
        expectedVersion = Math.Max(
            expectedVersion, ProtocolConstants.ControllerShortcutLabelVersion);
    Assert.Equal(expectedVersion, snapshot.ProtocolVersion);
    Assert.True(snapshot.Surface is not null, "YT Music omitted its bounded surface hint.");
    Assert.Equal(WidgetSurfaceMode.Standard, snapshot.Surface!.Mode);
    Assert.Equal(760D, snapshot.Surface.PreferredWidth);
    Assert.Equal(440D, snapshot.Surface.PreferredHeight);
    Assert.Equal(480D, snapshot.Surface.MinimumWidth);
    Assert.Equal(340D, snapshot.Surface.MinimumHeight);
    var errors = ViewSnapshotValidator.Validate(snapshot);
    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
}

static async Task BrokerClientParsesSnapshot()
{
    var host = new BrokerClientHarness(request => request.Request.Path switch
    {
        "/track" => BrokerJson("""
            {"video":{"title":"Example Song","author":"Example Artist","videoId":"video-1"},
             "music":{"album":"Example Album"},
             "meta":{"thumbnail":"https://img.example/cover.jpg","duration":245}}
            """),
        "/track/state" => BrokerJson("""
            {"id":"video-1","playing":true,"liked":true,"disliked":false,"shuffle":true,"repeat":"one","uiProgress":42.5,"duration":245}
            """),
        _ => throw new InvalidOperationException("Unexpected request."),
    }) { SecretExists = true };
    using var client = new YtmDesktopApiClient(host.Services);
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
    Assert.Equal(2, host.Requests.Count);
    Assert.True(host.Requests.All(request =>
            request.Request.BearerSecretSlot == YtmDesktopApiClient.BearerSecretSlot),
        "The client did not request host-side bearer injection.");
    Assert.True(host.Requests.All(request =>
            request.Request.Headers.All(header =>
                !header.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))),
        "The addon attempted to send a raw authorization header.");
}

static async Task BrokerClientCachesStableMetadata()
{
    var stateTrackId = "track-1";
    var trackRequests = 0;
    var host = new BrokerClientHarness(request => request.Request.Path switch
    {
        "/track/state" => BrokerJson(
            $"{{\"id\":\"{stateTrackId}\",\"playing\":true,\"uiProgress\":12,\"duration\":180}}"),
        "/track" => BrokerJson(
            $"{{\"video\":{{\"title\":\"Title {stateTrackId}\",\"author\":\"Artist\",\"videoId\":\"{stateTrackId}\"}}," +
            $"\"music\":{{\"album\":\"Album\"}},\"meta\":{{\"thumbnail\":\"https://img.example/{stateTrackId}.jpg\",\"duration\":180}}}}"),
        _ => throw new InvalidOperationException("Unexpected request."),
    });
    host.OnRequest = request =>
    {
        if (request.Request.Path == "/track") trackRequests++;
    };
    var clock = new ManualTimeProvider();
    using var client = new YtmDesktopApiClient(host.Services, clock);

    var first = await client.GetSnapshotAsync();
    var second = await client.GetSnapshotAsync();
    Assert.Equal("Title track-1", first.Title);
    Assert.Equal("Title track-1", second.Title);
    Assert.Equal(1, trackRequests);
    Assert.Equal(2, host.Requests.Count(request => request.Request.Path == "/track/state"));

    clock.Advance(TimeSpan.FromMinutes(5));
    var expired = await client.GetSnapshotAsync();
    Assert.Equal("Title track-1", expired.Title);
    Assert.Equal(2, trackRequests);

    stateTrackId = "track-2";
    var changed = await client.GetSnapshotAsync();
    Assert.Equal("track-2", changed.TrackId);
    Assert.Equal("Title track-2", changed.Title);
    Assert.Equal("https://img.example/track-2.jpg", changed.ArtworkUrl);
    Assert.Equal(3, trackRequests);
}

static async Task BrokerClientMapsCommand()
{
    var host = new BrokerClientHarness(_ => BrokerJson("{}"));
    using var client = new YtmDesktopApiClient(host.Services);
    await client.SendCommandAsync(YtMusicCommand.Like, toggleState: false);
    var request = host.Requests.Single();
    Assert.Equal(true, request.IsPost);
    Assert.Equal("/track/like", request.Request.Path);
    Assert.Equal("false", request.Request.JsonBody);
}

static async Task PrivateSecretRoundTrip()
{
    var host = new BrokerClientHarness(request => request.Request.Path switch
    {
        "/auth/request" => BrokerJson("{\"token\":\"credential-test\"}"),
        _ => BrokerJson("{}"),
    });
    using var client = new YtmDesktopApiClient(host.Services);

    await client.CompletePairingAsync("739204");
    Assert.Equal(1, host.SaveCalls);
    Assert.Equal("credential-test", host.LastSavedSecret);
    Assert.True(host.SecretExists, "The fake host did not persist the private secret.");
    await client.ClearCredentialAsync();
    Assert.Equal(1, host.DeleteCalls);
    Assert.True(!host.SecretExists, "The fake host did not remove the private secret.");
}

static async Task BrokerClientLoadsDurableSecret()
{
    var host = new BrokerClientHarness(_ => BrokerJson("""{"authRequired":true}"""))
    {
        SecretExists = true,
    };
    using var client = new YtmDesktopApiClient(host.Services);

    var status = await client.GetStatusAsync();

    Assert.True(status.AuthRequired, "Expected auth-required server configuration.");
    Assert.True(status.HasCredential, "The durable credential was not loaded.");
    Assert.Equal(YtmDesktopApiClient.BearerSecretSlot,
        host.Requests.Single().Request.BearerSecretSlot);
    Assert.Equal(1, host.ExistsCalls);
}

static async Task PairingReplacesStaleCredential()
{
    var host = new BrokerClientHarness(request => request.Request.Path switch
    {
        "/auth/requestcode" => BrokerJson("""{"code":"739204"}"""),
        "/auth/request" => BrokerJson("""{"token":"replacement-token"}"""),
        "/" => BrokerJson("""{"authRequired":true}"""),
        _ => throw new InvalidOperationException($"Unexpected request {request.Request.Path}."),
    }) { SecretExists = true };
    using var client = new YtmDesktopApiClient(host.Services);

    var pairing = await client.RequestPairingCodeAsync();
    await client.CompletePairingAsync(pairing.Code);
    var status = await client.GetStatusAsync();

    using var requestCodeBody = JsonDocument.Parse(
        host.Requests[0].Request.JsonBody!);
    Assert.Equal(YtmDesktopApiClient.PackageVersion,
        requestCodeBody.RootElement.GetProperty("appVersion").GetString());
    Assert.Equal("replacement-token", host.LastSavedSecret);
    Assert.Equal(1, host.SaveCalls);
    Assert.True(host.Requests.Take(2).All(request => request.Request.BearerSecretSlot is null),
        "Pairing endpoints must not receive a stale bearer credential.");
    Assert.Equal(YtmDesktopApiClient.BearerSecretSlot,
        host.Requests[2].Request.BearerSecretSlot);
    Assert.Equal(WidgetCommunityPlatformLimits.MaximumLoopbackTimeoutMilliseconds,
        host.Requests[1].Request.TimeoutMilliseconds);
    Assert.True(status.HasCredential, "The replacement token was not activated.");
}

static async Task HttpUnauthorizedIsTyped()
{
    var host = new BrokerClientHarness(request =>
        request.Request.BearerSecretSlot is null
            ? BrokerJson("{\"authRequired\":true}")
            : BrokerJson("credential rejected", statusCode: 401))
    {
        SecretExists = true,
    };
    using (var client = new YtmDesktopApiClient(host.Services))
    {
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
        Assert.True(host.Requests.Single().Request.InvalidateBearerSecretOnUnauthorized,
            "Authenticated YT requests did not opt into host-side rejected-bearer invalidation.");
        Assert.True(!host.SecretExists, "The host simulation retained the rejected durable bearer.");
        Assert.Equal(1, host.UnauthorizedInvalidationCalls);
        Assert.Equal(0, host.DeleteCalls);
    }

    using (var restarted = new YtmDesktopApiClient(host.Services))
    {
        var status = await restarted.GetStatusAsync();
        Assert.True(!status.HasCredential, "A restarted addon rediscovered the rejected bearer.");
        Assert.Equal(null, host.Requests[^1].Request.BearerSecretSlot);
    }
}

static async Task HttpFailureBodyIsDiscarded()
{
    const string privateResponse =
        "{\"error\":\"C:\\\\Users\\\\private\\\\ytmusic-token SECRET_RESPONSE_DETAIL\"}";
    var host = new BrokerClientHarness(_ => BrokerJson(privateResponse, statusCode: 503));
    using var client = new YtmDesktopApiClient(host.Services);

    YtMusicServiceException? failure = null;
    try
    {
        _ = await client.GetStatusAsync();
    }
    catch (YtMusicServiceException exception)
    {
        failure = exception;
    }

    Assert.True(failure is not null, "A non-success response was not classified.");
    Assert.Equal(503, failure!.StatusCode);
    Assert.True(!failure.Message.Contains("private", StringComparison.OrdinalIgnoreCase),
        "A private response-body detail escaped through the client exception.");
    Assert.True(!failure.Message.Contains("SECRET_RESPONSE_DETAIL", StringComparison.Ordinal),
        "The companion response body escaped through the client exception.");

    var fake = new FakeClient { StatusException = failure };
    var widget = new YtMusicWidget(fake);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    var status = Find(widget.Render().CreateSnapshot("ytmusic.test", 0).Root,
        "connection-status").Text;
    Assert.Equal("YTMDesktop2 reported an error · try again", status);
}

static async Task CapabilityFailuresAreSafe()
{
    var cases = new (Exception Failure, string Expected)[]
    {
        (new WidgetCapabilityUnavailableException("private provider path"),
            "Overlay services are unavailable · reload the widget"),
        (new WidgetCapabilityException("permission_denied", "private provider path"),
            "Local API access is blocked · review YT Music permissions"),
        (new WidgetCapabilityException("loopback_unavailable", "private provider path"),
            "YTMDesktop2 is not running · start it and retry"),
        (new WidgetCapabilityException("loopback_timeout", "private provider path"),
            "YTMDesktop2 did not respond · try again"),
        (new WidgetCapabilityException("invalid_response", "private provider path"),
            "YTMDesktop2 returned an invalid response · try again"),
        (new WidgetCapabilityException("future_error_code", "private provider path"),
            "YT Music request failed · try again"),
        (new InvalidOperationException("C:\\Users\\private\\unknown SECRET_DETAIL"),
            "YT Music request failed · try again"),
    };

    foreach (var testCase in cases)
    {
        var fake = new FakeClient { StatusException = testCase.Failure };
        var widget = new YtMusicWidget(fake);

        await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));

        var status = Find(widget.Render().CreateSnapshot("ytmusic.test", 0).Root,
            "connection-status").Text;
        Assert.Equal(testCase.Expected, status);
        Assert.True(!(status ?? string.Empty).Contains("private provider path", StringComparison.Ordinal),
            "Private capability-provider text escaped into the widget UI.");
    }
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

static async Task ConnectedIdleIsTruthful()
{
    var fake = new FakeClient { Snapshot = YtMusicPlaybackSnapshot.Empty };
    var widget = new YtMusicWidget(fake);

    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    var snapshot = widget.Render().CreateSnapshot("ytmusic.idle", 1);

    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    Assert.Equal("ytmusic-idle.action", snapshot.InitialFocusId);
    Assert.Equal("No track playing", Find(snapshot.Root, "ytmusic-idle.title").Text);
    Assert.Equal("refresh", Find(snapshot.Root, "ytmusic-idle.action").ActionId);
    Assert.Equal(0, snapshot.QuickActions.Count);
    foreach (var id in new[]
             {
                 "previous", "play-pause", "next", "shuffle", "like", "dislike", "repeat",
             })
        Assert.True(FindOrNull(snapshot.Root, id) is null,
            $"Idle companion exposed stale media action '{id}'.");

    await widget.OnActionAsync(new WidgetActionEvent("toggle-playback", "play-pause"));
    Assert.Equal(0, fake.Commands.Count);
}

static async Task TransientRefreshPreservesLastGood()
{
    var recovered = PlayingSnapshot("Recovered after disconnect") with
    {
        IsPlaying = false,
    };
    var fake = new FakeClient
    {
        SnapshotAsync = (call, _) => call switch
        {
            1 => Task.FromResult(PlayingSnapshot("Last-good track")),
            2 => Task.FromException<YtMusicPlaybackSnapshot>(
                new WidgetCapabilityException("loopback_timeout", "private provider path")),
            _ => Task.FromResult(recovered),
        },
    };
    var widget = new YtMusicWidget(fake);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));

    await widget.OnActionAsync(new WidgetActionEvent("refresh", "refresh"));
    var stale = widget.Render().CreateSnapshot("ytmusic.transient", 1);
    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    Assert.Equal("Last-good track", Find(stale.Root, "track-title").Text);
    Assert.True((Find(stale.Root, "connection-status").Text ?? string.Empty).Contains(
        "showing last known track", StringComparison.Ordinal),
        "Transient refresh failure did not label the retained presentation stale.");
    Assert.Equal(3, stale.QuickActions.Count);

    await widget.OnActionAsync(new WidgetActionEvent("refresh", "refresh"));
    var current = widget.Render().CreateSnapshot("ytmusic.transient", 2);
    Assert.Equal("Recovered after disconnect", Find(current.Root, "track-title").Text);
    Assert.Equal(WidgetGlyph.Play, Find(current.Root, "play-pause").Glyph);
    Assert.Equal("Connected to YTMDesktop2", Find(
        current.Root, "connection-status").Text);
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

static Task RuntimeOperationsOwnLifecycleWork()
{
    var fields = typeof(YtMusicWidget).GetFields(
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.DeclaredOnly);
    var taskRegistry = fields
        .Where(field => typeof(Task).IsAssignableFrom(field.FieldType) ||
                        field.FieldType == typeof(CancellationTokenSource))
        .Select(field => field.Name)
        .ToArray();
    Assert.Equal(0, taskRegistry.Length);
    Assert.Equal(1, fields.Count(field =>
        field.FieldType.Name.Contains("YtMusicPresentationState", StringComparison.Ordinal)));
    Assert.True(fields.All(field => field.Name is not (
            "_autoConnectTask" or "_progressLoop" or "_pollLoop" or "_autoConnectStarted")),
        "Superseded widget-local lifecycle coordination remains in the widget.");
    return Task.CompletedTask;
}

static Task ResponsibilitySplitContract()
{
    var sourceRoot = Path.Combine(AppContext.BaseDirectory, "source");
    var orchestration = File.ReadAllText(Path.Combine(sourceRoot, "YtMusicWidget.cs"));
    var actions = File.ReadAllText(Path.Combine(sourceRoot, "YtMusicActionPolicy.cs"));
    var companion = File.ReadAllText(Path.Combine(sourceRoot, "YtMusicCompanionPolicy.cs"));
    var connection = File.ReadAllText(Path.Combine(sourceRoot, "YtMusicConnectionPolicy.cs"));
    var presentation = File.ReadAllText(Path.Combine(sourceRoot, "YtMusicPresentation.cs"));

    AssertSourceContains(orchestration, "OnActivatedAsync");
    AssertSourceContains(orchestration, "OnDeactivatedAsync");
    AssertSourceContains(orchestration, "OnActionAsync");
    AssertSourceContains(orchestration, "private readonly object _stateLock");
    AssertSourceContains(orchestration, "Operations.RunLatest");
    Assert.True(!orchestration.Contains("UI.VerticalScroll", StringComparison.Ordinal),
        "Lifecycle orchestration regained semantic view composition.");
    Assert.True(!orchestration.Contains("MergeExpectedState", StringComparison.Ordinal),
        "Lifecycle orchestration regained companion confirmation policy.");

    AssertSourceContains(actions, "YtMusicActionRoute Resolve");
    Assert.True(!actions.Contains("WidgetView", StringComparison.Ordinal),
        "Closed action routing acquired presentation ownership.");
    Assert.True(!actions.Contains("IYtMusicClient", StringComparison.Ordinal),
        "Closed action routing acquired provider ownership.");

    AssertSourceContains(companion, "BeginOptimistic");
    AssertSourceContains(companion, "ReconcileAuthoritative");
    AssertSourceContains(companion, "TryRollback");
    Assert.True(!companion.Contains("lock (", StringComparison.Ordinal),
        "Companion policy acquired mutable committed-state ownership.");
    Assert.True(!companion.Contains("Invalidate", StringComparison.Ordinal),
        "Companion policy acquired widget publication ownership.");
    Assert.True(!companion.Contains("IYtMusicClient", StringComparison.Ordinal),
        "Companion policy acquired provider transport ownership.");

    AssertSourceContains(connection, "SafeStatus");
    AssertSourceContains(connection, "AuthorizationRequired");
    Assert.True(!connection.Contains("lock (", StringComparison.Ordinal),
        "Connection policy acquired mutable state ownership.");
    Assert.True(!connection.Contains("IYtMusicClient", StringComparison.Ordinal),
        "Connection policy acquired provider transport ownership.");

    AssertSourceContains(presentation, "WidgetView Compose");
    Assert.True(!presentation.Contains("lock (", StringComparison.Ordinal),
        "Snapshot-only presentation reads mutable widget state.");
    Assert.True(!presentation.Contains("HostServices", StringComparison.Ordinal),
        "Snapshot-only presentation acquired ambient host authority.");
    Assert.True(!presentation.Contains("IYtMusicClient", StringComparison.Ordinal),
        "Snapshot-only presentation acquired provider ownership.");
    return Task.CompletedTask;
}

static Task CurrentShortcutContract()
{
    var assemblyPath = typeof(YtMusicWidget).Assembly.Location;
    using var stream = File.OpenRead(assemblyPath);
    using var peReader = new PEReader(stream);
    var metadata = peReader.GetMetadataReader();
    var parameterCounts = new List<int>();

    foreach (var handle in metadata.MemberReferences)
    {
        var member = metadata.GetMemberReference(handle);
        if (!metadata.StringComparer.Equals(member.Name, "Shortcut") ||
            member.Parent.Kind != HandleKind.TypeReference)
            continue;

        var declaringType = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        if (!metadata.StringComparer.Equals(declaringType.Namespace, "WidgetRail.WidgetSdk") ||
            !metadata.StringComparer.Equals(declaringType.Name, "ScrollElement"))
            continue;

        var signature = metadata.GetBlobReader(member.Signature);
        var header = signature.ReadSignatureHeader();
        if (header.IsGeneric)
            _ = signature.ReadCompressedInteger();
        parameterCounts.Add(signature.ReadCompressedInteger());
    }

    Assert.True(parameterCounts.Count > 0,
        "The compiled YT Music render path omitted ScrollElement.Shortcut references.");
    var shortcutMethods = typeof(ScrollElement).GetMethods(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.DeclaredOnly)
        .Where(method => method.Name == "Shortcut")
        .ToArray();
    var unlabeled = new[]
    {
        typeof(ControllerButton), typeof(string), typeof(ControllerEventPhase),
        typeof(ControllerActionRepeatPolicy),
    };
    var labeled = new[]
    {
        typeof(ControllerButton), typeof(string), typeof(string),
        typeof(ControllerEventPhase), typeof(ControllerActionRepeatPolicy),
    };
    Assert.Equal(2, shortcutMethods.Length);
    Assert.True(shortcutMethods.Any(method => method.ReturnType == typeof(ScrollElement) &&
                                             method.GetParameters().Select(parameter => parameter.ParameterType)
                                                 .SequenceEqual(unlabeled)),
        "ScrollElement omitted the exact current four-parameter Shortcut overload.");
    Assert.True(shortcutMethods.Any(method => method.ReturnType == typeof(ScrollElement) &&
                                             method.GetParameters().Select(parameter => parameter.ParameterType)
                                                 .SequenceEqual(labeled)),
        "ScrollElement omitted the exact current labeled five-parameter Shortcut overload.");
    var supportedParameterCounts = shortcutMethods
        .Select(method => method.GetParameters().Length).ToHashSet();
    Assert.True(parameterCounts.All(supportedParameterCounts.Contains),
        $"A ScrollElement.Shortcut reference has no exact current declared overload; found [{string.Join(", ", parameterCounts)}] in {assemblyPath}.");
    return Task.CompletedTask;
}

static Task DirectActionAndConnectionPolicies()
{
    var play = YtMusicActionPolicy.Resolve("toggle-playback");
    Assert.Equal(YtMusicActionKind.Command, play.Kind);
    Assert.Equal<YtMusicCommand?>(YtMusicCommand.TogglePlayback, play.Command);
    Assert.Equal("Toggling playback…", play.Status);
    Assert.Equal(YtMusicActionKind.Refresh,
        YtMusicActionPolicy.Resolve("refresh").Kind);
    Assert.Equal(YtMusicActionKind.None,
        YtMusicActionPolicy.Resolve("unknown-private-action").Kind);

    var connecting = YtMusicPresentationState.Initial(0) with
    {
        ConnectionState = YtMusicWidgetConnectionState.Connecting,
        Status = "Connecting to YTMDesktop2…",
        PairingCode = "1234",
    };
    var deactivated = YtMusicConnectionPolicy.Deactivate(connecting);
    Assert.Equal(YtMusicWidgetConnectionState.Disconnected,
        deactivated.ConnectionState);
    Assert.Equal("Connection paused · reconnect when visible", deactivated.Status);
    Assert.Equal<string?>(null, deactivated.PairingCode);
    var expired = YtMusicConnectionPolicy.AuthorizationRequired(
        YtMusicPresentationState.Connected(PlayingSnapshot("Policy"), 0));
    Assert.Equal("Authorization expired · pair device", expired.Status);
    Assert.Equal(0, expired.PendingOptimistic.Length);
    Assert.Equal("YTMDesktop2 is busy · try again shortly",
        YtMusicConnectionPolicy.SafeStatus(new YtMusicServiceException(429)));
    return Task.CompletedTask;
}

static Task DirectCompanionConfirmationPolicy()
{
    var clock = new ManualTimeProvider();
    var policy = FastUpdatePolicy();
    var authoritative = PlayingSnapshot("Direct policy") with
    {
        IsShuffleEnabled = false,
        RepeatMode = YtMusicRepeatMode.Off,
    };
    var state = YtMusicPresentationState.Connected(
        authoritative, clock.GetTimestamp());

    var like = YtMusicCompanionPolicy.BeginOptimistic(
        state, YtMusicCommand.Like, "Updating like…",
        clock.GetTimestamp(), policy, clock);
    Assert.Equal(true, like.Presentation.Snapshot.IsLiked);
    Assert.Equal(1, like.Presentation.PendingOptimistic.Length);
    var stale = YtMusicCompanionPolicy.ReconcileAuthoritative(
        like.Presentation, authoritative, clock.GetTimestamp(), force: false, clock);
    Assert.Equal(true, stale.Snapshot.IsLiked);
    Assert.Equal(1, stale.PendingOptimistic.Length);

    clock.Advance(policy.OptimisticConfirmationWindow + TimeSpan.FromMilliseconds(1));
    var expired = YtMusicCompanionPolicy.ReconcileAuthoritative(
        stale, authoritative, clock.GetTimestamp(), force: false, clock);
    Assert.Equal(false, expired.Snapshot.IsLiked);
    Assert.Equal(0, expired.PendingOptimistic.Length);

    var shuffle = YtMusicCompanionPolicy.BeginOptimistic(
        expired, YtMusicCommand.Shuffle, "Toggling shuffle…",
        clock.GetTimestamp(), policy, clock);
    var confirmed = YtMusicCompanionPolicy.ReconcileAuthoritative(
        shuffle.Presentation,
        authoritative with { IsShuffleEnabled = true },
        clock.GetTimestamp(),
        force: false,
        clock);
    Assert.Equal(true, confirmed.Snapshot.IsShuffleEnabled);
    Assert.Equal(0, confirmed.PendingOptimistic.Length);

    var dislike = YtMusicCompanionPolicy.BeginOptimistic(
        confirmed, YtMusicCommand.Dislike, "Updating dislike…",
        clock.GetTimestamp(), policy, clock);
    Assert.Equal(true, dislike.Presentation.Snapshot.IsDisliked);
    Assert.True(YtMusicCompanionPolicy.TryRollback(
            dislike.Presentation,
            dislike.Pending,
            "request failed",
            clock.GetTimestamp(),
            clock,
            out var rolledBack),
        "Direct companion rollback did not find its exact pending attempt.");
    Assert.Equal(false, rolledBack.Snapshot.IsDisliked);
    Assert.Equal(0, rolledBack.PendingOptimistic.Length);
    Assert.Equal("request failed", rolledBack.Status);
    return Task.CompletedTask;
}

static Task PurePresentationIsDeterministic()
{
    var state = YtMusicPresentationState.Connected(
        PlayingSnapshot("Deterministic") with
        {
            IsShuffleEnabled = true,
            RepeatMode = YtMusicRepeatMode.One,
        },
        timestamp: 0,
        status: "Playing through YTMDesktop2");
    var first = YtMusicPresentation.Compose(state, state.Snapshot)
        .CreateSnapshot("ytmusic.presentation", 42);
    var second = YtMusicPresentation.Compose(state, state.Snapshot)
        .CreateSnapshot("ytmusic.presentation", 42);
    Assert.True(
        SnapshotJson.Serialize(first).AsSpan().SequenceEqual(
            SnapshotJson.Serialize(second)),
        "Repeated immutable presentation produced different semantic bytes.");
    return Task.CompletedTask;
}

static void AssertSourceContains(string source, string expected)
{
    Assert.True(source.Contains(expected, StringComparison.Ordinal),
        $"Expected source boundary marker '{expected}'.");
}

static async Task LatePairingCannotCommitAfterDeactivation()
{
    var pairingGate = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var cancellationObserved = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeClient
    {
        StatusInfo = new YtMusicConnectionInfo(AuthRequired: true),
        Snapshot = PlayingSnapshot("Late paired track"),
        PairCompletionAsync = token =>
        {
            token.Register(() => cancellationObserved.TrySetResult());
            return pairingGate.Task; // Deliberately ignores cancellation.
        },
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy() with
    {
        ProgressInterval = TimeSpan.FromSeconds(5),
        PollInterval = TimeSpan.FromSeconds(30),
    });
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => fake.StatusCalls == 1 &&
                          widget.ConnectionState == YtMusicWidgetConnectionState.Disconnected);

    var pairing = widget.OnActionAsync(new WidgetActionEvent("pair", "pair")).AsTask();
    await WaitUntil(() => FindOrNull(
        widget.Render().CreateSnapshot("ytmusic.late-pair", 1).Root,
        "pairing-code") is not null);

    var backgrounding = widget.SetLifecycleStateAsync(
        WidgetLifecycleState.Background,
        CancellationToken.None).AsTask();
    await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
    pairingGate.SetResult();
    await backgrounding.WaitAsync(TimeSpan.FromSeconds(1));
    await pairing.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Equal(YtMusicWidgetConnectionState.Disconnected, widget.ConnectionState);
    var snapshot = widget.Render().CreateSnapshot("ytmusic.late-pair", 2);
    Assert.True(FindOrNull(snapshot.Root, "pairing-code") is null,
        "A lifecycle-stale pairing code remained visible.");
    Assert.True(FindOrNull(snapshot.Root, "track-title") is null,
        "A lifecycle-stale pairing completion published now-playing state.");
    Assert.Equal(0, fake.SnapshotCalls);
}

static async Task LatePollCannotCommitAfterDeactivation()
{
    var initial = PlayingSnapshot("Current poll state");
    var stale = PlayingSnapshot("Lifecycle-stale poll state");
    var pollGate = new TaskCompletionSource<YtMusicPlaybackSnapshot>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var cancellationObserved = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeClient { Snapshot = initial };
    fake.SnapshotAsync = (call, token) => call switch
    {
        1 => Task.FromResult(initial),
        2 => IgnoreCancellationAsync(token, cancellationObserved, pollGate.Task),
        _ => Task.FromResult(initial),
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy() with
    {
        ProgressInterval = TimeSpan.FromMilliseconds(250),
        PollInterval = TimeSpan.FromMilliseconds(250),
    });
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await WaitUntil(() => fake.SnapshotCalls >= 2);

    var backgrounding = widget.SetLifecycleStateAsync(
        WidgetLifecycleState.Background,
        CancellationToken.None).AsTask();
    await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
    pollGate.SetResult(stale);
    await backgrounding.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Equal("Current poll state", Find(
        widget.Render().CreateSnapshot("ytmusic.late-poll", 1).Root,
        "track-title").Text);
}

static async Task<T> IgnoreCancellationAsync<T>(
    CancellationToken token,
    TaskCompletionSource cancellationObserved,
    Task<T> completion)
{
    token.Register(() => cancellationObserved.TrySetResult());
    return await completion.ConfigureAwait(false);
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
    Assert.Equal("YT Music request failed · try again",
        Find(rolledBack.Root, "connection-status").Text);
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

static async Task RepeatOneUsesDistinctGlyph()
{
    var fake = new FakeClient
    {
        Snapshot = PlayingSnapshot("Repeat one") with
        {
            RepeatMode = YtMusicRepeatMode.One,
        },
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));

    var snapshot = widget.Render().CreateSnapshot("ytmusic.test", 1);
    Assert.Equal(ProtocolConstants.ControllerShortcutLabelVersion, snapshot.ProtocolVersion);
    Assert.Equal(WidgetGlyph.RepeatOne, Find(snapshot.Root, "repeat").Glyph);
    Assert.Equal("Repeat one · change repeat mode",
        Find(snapshot.Root, "repeat").AccessibilityLabel);
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
        Assert.Equal("YT Music request failed · try again",
            Find(rolledBack.Root, "connection-status").Text);
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
    Assert.Equal(0, fake.ClearCredentialCalls);
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

    Assert.Equal(0, fake.ClearCredentialCalls);
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

static async Task ResponsiveConnectedComposition()
{
    var widget = new YtMusicWidget(new FakeClient
    {
        Snapshot = PlayingSnapshot("Responsive Controller Song") with
        {
            Artist = "A deliberately descriptive artist",
            Album = "A deliberately descriptive album",
        },
    });
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    var snapshot = widget.Render().CreateSnapshot("ytmusic.responsive", 1);

    Assert.Equal(ViewNodeKind.Scroll, snapshot.Root.Kind);
    var layout = Find(snapshot.Root, "media-layout");
    Assert.True(layout.Children.Select(child => child.Id).SequenceEqual(
            ["artwork-frame", "media-details"]),
        "The responsive player must retain one artwork/details composition.");

    var details = Find(snapshot.Root, "media-details");
    Assert.True(details.Children.Select(child => child.Id).SequenceEqual(
            ["track-details", "progress-row", "primary-actions", "secondary-actions"]),
        "Metadata, progress, and both control rows must share the right-hand details column.");
    Assert.Equal(1, Nodes(snapshot.Root).Count(node => node.Id == "primary-actions"));
    Assert.Equal(1, Nodes(snapshot.Root).Count(node => node.Id == "secondary-actions"));

    Assert.Equal("play-pause", snapshot.InitialFocusId);
    Assert.Equal("play-pause", Find(snapshot.Root, "previous").Focus!.Right);
    Assert.Equal("shuffle", Find(snapshot.Root, "previous").Focus!.Down);
    Assert.Equal("previous", Find(snapshot.Root, "play-pause").Focus!.Left);
    Assert.Equal("next", Find(snapshot.Root, "play-pause").Focus!.Right);
    Assert.Equal("like", Find(snapshot.Root, "play-pause").Focus!.Down);
    Assert.Equal("refresh", Find(snapshot.Root, "next").Focus!.Right);
    Assert.Equal("repeat", Find(snapshot.Root, "refresh").Focus!.Down);
    AssertWindowShortcut(snapshot.Root, ControllerButton.LeftBumper, "previous");
    AssertWindowShortcut(snapshot.Root, ControllerButton.X, "toggle-playback");
    AssertWindowShortcut(snapshot.Root, ControllerButton.RightBumper, "next");
    AssertWindowShortcut(snapshot.Root, ControllerButton.Y, "refresh");

    var validation = ViewSnapshotValidator.Validate(snapshot);
    Assert.Equal(0, validation.Count);
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
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
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
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
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
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
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
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
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
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);

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
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task RepeatedPlaybackCommandsBypassReconciliation()
{
    var playing = PlayingSnapshot("Playback Queue");
    var blockedRefresh = NewSnapshotGate();
    var finalRefresh = NewSnapshotGate();
    var fake = new FakeClient { Snapshot = playing };
    fake.SnapshotAsync = (call, token) => call switch
    {
        1 => Task.FromResult(playing),
        2 => blockedRefresh.Task.WaitAsync(token),
        _ => finalRefresh.Task.WaitAsync(token),
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);

    await widget.OnActionAsync(new WidgetActionEvent(
        "toggle-playback", "play-pause", ControllerButton.X));
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    var secondCommand = widget.OnActionAsync(new WidgetActionEvent(
        "toggle-playback", "play-pause", ControllerButton.X)).AsTask();
    await secondCommand.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Equal(2, fake.Commands.Count);
    Assert.True(fake.Commands.All(command => command == YtMusicCommand.TogglePlayback),
        "Repeated X actions did not preserve playback command order.");
    await WaitUntil(() => fake.SnapshotCalls >= 3);
    finalRefresh.SetResult(playing);
    await WaitUntil(() => Find(
        widget.Render().CreateSnapshot("ytmusic.test", 1).Root,
        "play-pause").Glyph == WidgetGlyph.Pause);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task PlaybackReconciliationOutlivesActionRequest()
{
    var playing = PlayingSnapshot("Request Lifetime");
    var paused = playing with { IsPlaying = false, PositionSeconds = 66 };
    var refreshGate = NewSnapshotGate();
    var fake = new FakeClient { Snapshot = playing };
    fake.SnapshotAsync = (call, token) => call switch
    {
        1 => Task.FromResult(playing),
        _ => refreshGate.Task.WaitAsync(token),
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    using var actionLifetime = new CancellationTokenSource();

    await widget.OnActionAsync(
        new WidgetActionEvent("toggle-playback", "play-pause", ControllerButton.X),
        actionLifetime.Token);
    actionLifetime.Cancel();
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    refreshGate.SetResult(paused);
    await WaitUntil(() => Find(
        widget.Render().CreateSnapshot("ytmusic.test", 1).Root,
        "play-pause").Glyph == WidgetGlyph.Play);
    Assert.Equal("Connected to YTMDesktop2", Find(
        widget.Render().CreateSnapshot("ytmusic.test", 2).Root,
        "connection-status").Text);
    await Task.Delay(100);
    Assert.Equal(2, fake.SnapshotCalls);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task TransportReconciliationStopsWhenInactive()
{
    var playing = PlayingSnapshot("Lifecycle Reconciliation");
    var refreshCanceled = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeClient { Snapshot = playing };
    fake.SnapshotAsync = (call, token) => call == 1
        ? Task.FromResult(playing)
        : ObserveSnapshotCancellationAsync(token, refreshCanceled);
    var policy = FastUpdatePolicy() with
    {
        ProgressInterval = TimeSpan.FromSeconds(5),
        PollInterval = TimeSpan.FromSeconds(30),
    };
    var widget = new YtMusicWidget(fake, policy);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);

    await widget.OnActionAsync(new WidgetActionEvent(
        "next", "next", ControllerButton.RightBumper));
    await WaitUntil(() => fake.SnapshotCalls >= 2);

    await widget.SetLifecycleStateAsync(
            WidgetLifecycleState.Background, CancellationToken.None)
        .AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    await refreshCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    var callsAfterDeactivation = fake.SnapshotCalls;
    await Task.Delay(80);
    Assert.Equal(callsAfterDeactivation, fake.SnapshotCalls);
}

static async Task SupersededTransportFailuresCannotCommit()
{
    await AssertSupersededTransportFailureCannotCommit(
        new InvalidOperationException("stale ordinary failure"));
    await AssertSupersededTransportFailureCannotCommit(
        new YtMusicAuthorizationRequiredException());
}

static async Task AssertSupersededTransportFailureCannotCommit(Exception staleFailure)
{
    var initial = PlayingSnapshot("Before stale failure") with
    {
        TrackId = "before-stale-id",
        MetadataTrackId = "before-stale-id",
    };
    var confirmed = PlayingSnapshot("Replacement committed") with
    {
        TrackId = "replacement-id",
        MetadataTrackId = "replacement-id",
        PositionSeconds = 3,
    };
    var staleFailureGate = NewSnapshotGate();
    var replacementUnconfirmed = NewSnapshotGate();
    var replacementConfirmed = NewSnapshotGate();
    var fake = new FakeClient { Snapshot = initial, HasCredential = true };
    fake.SnapshotAsync = (call, _) => call switch
    {
        1 => Task.FromResult(initial),
        2 => staleFailureGate.Task, // Deliberately ignores cancellation.
        3 => replacementUnconfirmed.Task,
        _ => replacementConfirmed.Task,
    };
    var policy = FastUpdatePolicy() with
    {
        ProgressInterval = TimeSpan.FromSeconds(5),
        PollInterval = TimeSpan.FromSeconds(30),
    };
    var widget = new YtMusicWidget(fake, policy);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);

    await widget.OnActionAsync(new WidgetActionEvent(
        "next", "next", ControllerButton.RightBumper));
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    await widget.OnActionAsync(new WidgetActionEvent(
        "next", "next", ControllerButton.RightBumper));

    staleFailureGate.SetException(staleFailure);
    await WaitUntil(() => fake.SnapshotCalls >= 3);
    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    Assert.Equal("Next track…", Find(
        widget.Render().CreateSnapshot("ytmusic.stale-failure", 1).Root,
        "connection-status").Text);

    replacementUnconfirmed.SetResult(initial);
    await WaitUntil(() => fake.SnapshotCalls >= 4);
    AssertBetween(0, 1.5, ProgressValue(widget, 2),
        "The stale failure cleared the replacement's optimistic pending state.");
    Assert.Equal("Next track…", Find(
        widget.Render().CreateSnapshot("ytmusic.stale-failure", 3).Root,
        "connection-status").Text);

    replacementConfirmed.SetResult(confirmed);
    await WaitUntil(() => "Replacement committed" == Find(
        widget.Render().CreateSnapshot("ytmusic.stale-failure", 4).Root,
        "track-title").Text);
    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    Assert.Equal("Playing through YTMDesktop2", Find(
        widget.Render().CreateSnapshot("ytmusic.stale-failure", 5).Root,
        "connection-status").Text);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task LifecycleStaleAuthorizationCannotCommit()
{
    var initial = PlayingSnapshot("Lifecycle stale authorization");
    var staleFailureGate = NewSnapshotGate();
    var cancellationObserved = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeClient { Snapshot = initial, HasCredential = true };
    fake.SnapshotAsync = (call, token) =>
    {
        if (call == 1) return Task.FromResult(initial);
        token.Register(() => cancellationObserved.TrySetResult());
        return staleFailureGate.Task; // Deliberately ignores cancellation.
    };
    var policy = FastUpdatePolicy() with
    {
        ProgressInterval = TimeSpan.FromSeconds(5),
        PollInterval = TimeSpan.FromSeconds(30),
    };
    var widget = new YtMusicWidget(fake, policy);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    await widget.OnActionAsync(new WidgetActionEvent(
        "next", "next", ControllerButton.RightBumper));
    await WaitUntil(() => fake.SnapshotCalls >= 2);

    var backgrounding = widget.SetLifecycleStateAsync(
        WidgetLifecycleState.Background, CancellationToken.None).AsTask();
    await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
    staleFailureGate.SetException(
        new YtMusicAuthorizationRequiredException());
    await backgrounding.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    Assert.Equal("Next track…", Find(
        widget.Render().CreateSnapshot("ytmusic.lifecycle-stale", 1).Root,
        "connection-status").Text);
}

static Task LifecycleStaleExplicitRefreshOrdinaryFailureCannotCommit() =>
    AssertLifecycleStaleExplicitRefreshFailureCannotCommit(
        new InvalidOperationException("stale explicit refresh failure"));

static Task LifecycleStaleExplicitRefreshAuthorizationCannotCommit() =>
    AssertLifecycleStaleExplicitRefreshFailureCannotCommit(
        new YtMusicAuthorizationRequiredException());

static async Task AssertLifecycleStaleExplicitRefreshFailureCannotCommit(
    Exception staleFailure)
{
    var initial = PlayingSnapshot("Current explicit refresh track");
    var staleGate = NewSnapshotGate();
    var cancellationObserved = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeClient
    {
        Snapshot = initial,
        HasCredential = true,
    };
    fake.SnapshotAsync = (call, token) =>
    {
        if (call == 1) return Task.FromResult(initial);
        token.Register(() => cancellationObserved.TrySetResult());
        return staleGate.Task; // Deliberately ignores cancellation.
    };
    var policy = FastUpdatePolicy() with
    {
        ProgressInterval = TimeSpan.FromSeconds(5),
        PollInterval = TimeSpan.FromSeconds(30),
    };
    var widget = new YtMusicWidget(fake, policy);
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.SetLifecycleStateAsync(
        WidgetLifecycleState.Visible, CancellationToken.None);

    var refresh = widget.OnActionAsync(
        new WidgetActionEvent("refresh", "refresh")).AsTask();
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    var background = widget.SetLifecycleStateAsync(
        WidgetLifecycleState.Background, CancellationToken.None).AsTask();
    await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
    var replacement = widget.SetLifecycleStateAsync(
        WidgetLifecycleState.Visible, CancellationToken.None).AsTask();
    staleGate.SetException(staleFailure);
    await Task.WhenAll(refresh, background, replacement).WaitAsync(TimeSpan.FromSeconds(2));

    var snapshot = widget.Render().CreateSnapshot("ytmusic.explicit-refresh", 1);
    Assert.Equal(YtMusicWidgetConnectionState.Connected, widget.ConnectionState);
    Assert.Equal("Current explicit refresh track",
        Find(snapshot.Root, "track-title").Text);
    Assert.Equal("Refreshing now playing…",
        Find(snapshot.Root, "connection-status").Text);
    Assert.True(fake.HasCredential,
        "A stale explicit Refresh authorization failure removed current authority.");
    await widget.SetLifecycleStateAsync(
        WidgetLifecycleState.Background, CancellationToken.None);
}

static async Task<YtMusicPlaybackSnapshot> ObserveSnapshotCancellationAsync(
    CancellationToken cancellationToken,
    TaskCompletionSource cancellationObserved)
{
    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The lifecycle-owned refresh unexpectedly completed.");
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        cancellationObserved.TrySetResult();
        throw;
    }
}

static async Task PlaybackReconciliationRejectsStaleState()
{
    var playing = PlayingSnapshot("Stale Playback");
    var paused = playing with { IsPlaying = false, PositionSeconds = 66 };
    var staleGate = NewSnapshotGate();
    var confirmedGate = NewSnapshotGate();
    var fake = new FakeClient { Snapshot = playing };
    fake.SnapshotAsync = (call, token) => call switch
    {
        1 => Task.FromResult(playing),
        2 => staleGate.Task.WaitAsync(token),
        _ => confirmedGate.Task.WaitAsync(token),
    };
    var widget = new YtMusicWidget(fake, FastUpdatePolicy());
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);

    await widget.OnActionAsync(new WidgetActionEvent(
        "toggle-playback", "play-pause", ControllerButton.X));
    await WaitUntil(() => fake.SnapshotCalls >= 2);
    staleGate.SetResult(playing);
    await WaitUntil(() => fake.SnapshotCalls >= 3);
    Assert.Equal(WidgetGlyph.Play, Find(
        widget.Render().CreateSnapshot("ytmusic.test", 1).Root,
        "play-pause").Glyph);

    confirmedGate.SetResult(paused);
    await WaitUntil(() => Find(
        widget.Render().CreateSnapshot("ytmusic.test", 2).Root,
        "connection-status").Text == "Connected to YTMDesktop2");
    await Task.Delay(100);
    Assert.Equal(3, fake.SnapshotCalls);
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
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
    Assert.Equal("YT Music request failed · try again",
        Find(snapshot.Root, "connection-status").Text);
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

static Task PackageAssetsAreValid()
{
    var path = Path.Combine(AppContext.BaseDirectory, "manifest.json");
    Assert.True(File.Exists(path), $"Manifest was not copied to {path}.");
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(path));
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.Equal(0, errors.Count);
    Assert.True(manifest.Permissions.Contains("network.loopback:13091"), "Loopback permission is missing.");
    Assert.True(manifest.OptionalPermissions.Contains("storage.private-secrets.v1"),
        "Private-secret permission is missing.");
    Assert.Equal(WidgetGlyph.Music, manifest.Presentation.Icon);
    Assert.Equal("0.2.12", manifest.Version);
    Assert.Equal("ytmusic.mark", manifest.Presentation.PackageIcon?.AssetId);
    Assert.Equal(WidgetPackageIconColorMode.OriginalColor,
        manifest.Presentation.PackageIcon?.ColorMode);
    Assert.Equal("assets/icons/yt-music.svg", manifest.IconAssets["ytmusic.mark"].Path);
    var packageIcon = Path.Combine(
        AppContext.BaseDirectory, "assets", "icons", "yt-music.svg");
    Assert.True(File.Exists(packageIcon), $"Package icon was not copied to {packageIcon}.");
    Assert.True(new FileInfo(packageIcon).Length is > 0 and <= ProtocolConstants.MaximumPackageIconBytes,
        "Package icon bytes are outside the public bound.");

    var stylesRoot = Path.Combine(AppContext.BaseDirectory, "styles");
    var styles = WrssPackageLoader.Load(
        "default.wrss",
        new WrssFileSourceProvider(stylesRoot));
    var compiled = WrssThemeCompiler.Compile(styles);
    Assert.True(compiled.IsValid,
        string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
    var mediaLayout = compiled.Theme!.Resolve(new WrssElement("row", "media-layout"));
    Assert.Equal("wrap", mediaLayout.Get("flex-wrap")?.Text);
    Assert.Equal("start", mediaLayout.Get("justify")?.Text);
    Assert.Equal("100%", mediaLayout.Get("width")?.Text);
    Assert.Equal(1D, mediaLayout.Get("flex-grow")?.Number);
    Assert.Equal("12px", mediaLayout.Get("padding")?.Text);
    Assert.Equal("1px", mediaLayout.Get("border-width")?.Text);
    var mediaDetails = compiled.Theme.Resolve(new WrssElement("stack", "media-details"));
    Assert.Equal("320px", mediaDetails.Get("min-width")?.Text);
    Assert.Equal("320px", mediaDetails.Get("flex-basis")?.Text);
    Assert.Equal(1D, mediaDetails.Get("flex-grow")?.Number);
    foreach (var id in new[] { "progress-row", "primary-actions", "secondary-actions" })
    {
        var row = compiled.Theme.Resolve(new WrssElement("row", id));
        Assert.Equal("100%", row.Get("width")?.Text);
        Assert.Equal("0px", row.Get("min-width")?.Text);
    }
    return Task.CompletedTask;
}

static async Task ExportRendererFixture(string outputPath)
{
    var widget = new YtMusicWidget(new FakeClient
    {
        Snapshot = PlayingSnapshot("Responsive Renderer Song") with
        {
            Artist = "A deliberately descriptive renderer artist",
            Album = "A deliberately descriptive renderer album",
        },
    });
    await widget.OnActionAsync(new WidgetActionEvent("connect", "connect"));
    var snapshot = widget.Render().CreateSnapshot("ytmusic.renderer", 1);
    var validation = ViewSnapshotValidator.Validate(snapshot);
    Assert.Equal(0, validation.Count);

    var stylesRoot = Path.Combine(AppContext.BaseDirectory, "styles");
    var package = WrssPackageLoader.Load(
        "default.wrss",
        new WrssFileSourceProvider(stylesRoot));
    var compiled = WrssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid,
        string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
    var renderStyles = BridgeRenderStyleResolver.Resolve(snapshot, compiled.Theme);
    using var snapshotDocument = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    var payload = JsonSerializer.Serialize(new
    {
        snapshot = snapshotDocument.RootElement.Clone(),
        renderStyles,
    }, options);
    var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    await File.WriteAllTextAsync(outputPath, payload);
    Console.WriteLine($"Exported YT Music renderer fixture to {outputPath}.");
}

static YtMusicPlaybackSnapshot PlayingSnapshot(string title) => new(
    "track-id", title, "Artist", "Album", "https://img.example/cover.jpg",
    true, false, false, 65, 240);

static WidgetLoopbackJsonResponse BrokerJson(string json, int statusCode = 200) =>
    new(statusCode, json, []);

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

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    {
        foreach (var descendant in Nodes(child)) yield return descendant;
    }
}

static void AssertQuickAction(ViewSnapshot snapshot, ControllerButton button, string actionId, string label)
{
    var action = snapshot.QuickActions.Single(item => item.Button == button);
    Assert.Equal(actionId, action.ActionId);
    Assert.Equal(label, action.Label);
    Assert.Equal("network.loopback:13091", action.Capability?.CapabilityId);
    Assert.Equal(WidgetLoopbackCapabilities.PostJsonOperation, action.Capability?.OperationId);
}

static void AssertWindowShortcut(ViewNode root, ControllerButton button, string actionId)
{
    var shortcut = root.Shortcuts.Single(item => item.Button == button);
    Assert.Equal(actionId, shortcut.ActionId);
    Assert.Equal(ControllerEventPhase.Pressed, shortcut.Phase);
}

file sealed record BrokerRecordedRequest(bool IsPost, WidgetLoopbackJsonRequest Request);

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
        WidgetElement ytmusicWindow = view.Root switch
        {
            StackElement stack => stack.InputScope("ytmusic-window"),
            ScrollElement scroll => scroll.InputScope("ytmusic-window"),
            _ => throw new InvalidOperationException("YT Music root must be an input-scope container."),
        };
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

file sealed class BrokerClientHarness
{
    private readonly Func<BrokerRecordedRequest, WidgetLoopbackJsonResponse> _respond;

    public BrokerClientHarness(Func<BrokerRecordedRequest, WidgetLoopbackJsonResponse> respond)
    {
        _respond = respond;
        Services = new WidgetTestHostServicesBuilder()
            .WithHandler(
                WidgetLoopbackCapabilities.GetJson(YtmDesktopApiClient.CompanionPort),
                (request, cancellationToken) => RecordAsync(false, request, cancellationToken))
            .WithHandler(
                WidgetLoopbackCapabilities.PostJson(YtmDesktopApiClient.CompanionPort),
                (request, cancellationToken) => RecordAsync(true, request, cancellationToken))
            .WithHandler(
                WidgetPrivateSecretCapabilities.Exists,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Assert.Equal(YtmDesktopApiClient.BearerSecretSlot, request.Slot);
                    ExistsCalls++;
                    return ValueTask.FromResult(new WidgetPrivateSecretExists(SecretExists));
                })
            .WithHandler(
                WidgetPrivateSecretCapabilities.Metadata,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Assert.Equal(YtmDesktopApiClient.BearerSecretSlot, request.Slot);
                    return ValueTask.FromResult(new WidgetPrivateSecretMetadata(SecretExists, null));
                })
            .WithHandler(
                WidgetPrivateSecretCapabilities.Save,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Assert.Equal(YtmDesktopApiClient.BearerSecretSlot, request.Slot);
                    SaveCalls++;
                    LastSavedSecret = request.Secret;
                    SecretExists = true;
                    return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
                })
            .WithHandler(
                WidgetPrivateSecretCapabilities.Delete,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Assert.Equal(YtmDesktopApiClient.BearerSecretSlot, request.Slot);
                    DeleteCalls++;
                    SecretExists = false;
                    LastSavedSecret = null;
                    return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
                })
            .Build();
    }

    public WidgetHostServices Services { get; }
    public List<BrokerRecordedRequest> Requests { get; } = [];
    public bool SecretExists { get; set; }
    public string? LastSavedSecret { get; private set; }
    public int ExistsCalls { get; private set; }
    public int SaveCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public int UnauthorizedInvalidationCalls { get; private set; }
    public Action<BrokerRecordedRequest>? OnRequest { get; set; }

    private ValueTask<WidgetLoopbackJsonResponse> RecordAsync(
        bool isPost,
        WidgetLoopbackJsonRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recorded = new BrokerRecordedRequest(isPost, request);
        Requests.Add(recorded);
        OnRequest?.Invoke(recorded);
        var response = _respond(recorded);
        if (response.StatusCode == 401 &&
            request is
            {
                InvalidateBearerSecretOnUnauthorized: true,
                BearerSecretSlot: not null,
            })
        {
            SecretExists = false;
            LastSavedSecret = null;
            UnauthorizedInvalidationCalls++;
        }
        return ValueTask.FromResult(response);
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
    public Func<CancellationToken, Task>? PairCompletionAsync { get; set; }
    public string? CompletedPairingCode { get; private set; }
    public int StatusCalls => Volatile.Read(ref _statusCalls);
    public bool HasCredential { get; set; }
    public int ClearCredentialCalls { get; private set; }

    public Task<YtMusicConnectionInfo> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _statusCalls);
        var failure = StatusException ?? StatusFailure?.Invoke(call);
        if (failure is not null)
        {
            if (failure is YtMusicAuthorizationRequiredException) HasCredential = false;
            throw failure;
        }
        return StatusTask ?? Task.FromResult(StatusInfo with
        {
            HasCredential = StatusInfo.HasCredential || HasCredential,
        });
    }

    public Task<YtMusicPlaybackSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _snapshotCalls);
        var failure = SnapshotFailure?.Invoke(call);
        if (failure is not null)
        {
            if (failure is YtMusicAuthorizationRequiredException) HasCredential = false;
            throw failure;
        }
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
        if (PairCompletionAsync is not null)
            await PairCompletionAsync(cancellationToken);
        else if (PairCompletionTask is not null)
            await PairCompletionTask.WaitAsync(cancellationToken);
        HasCredential = true;
    }

    public Task ClearCredentialAsync(CancellationToken cancellationToken = default)
    {
        ClearCredentialCalls++;
        HasCredential = false;
        return Task.CompletedTask;
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
