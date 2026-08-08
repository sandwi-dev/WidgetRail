using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using GameBarAlternative.PlatformBroker;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Capability vocabulary is closed and versioned", CapabilityVocabularyIsClosed),
    ("Composite backend keeps provider event domains separated", CompositeProviderDomainsAreSeparated),
    ("Missing and explicit consent fail closed", ConsentFailsClosed),
    ("Authenticated channel identity cannot be substituted", IdentityMismatchIsDenied),
    ("Request JSON is strict and bounded", RequestsAreStrictAndBounded),
    ("Audio operations expose sanitized task-shaped DTOs", AudioOperationsAreSanitized),
    ("Master output capability validates payload lifecycle and events", MasterOutputContracts),
    ("Audio device and input permissions are granular opaque and lifecycle-gated", AudioDeviceInputContracts),
    ("Network operations switch only opaque saved profiles", NetworkOperationsAreSanitized),
    ("Available Wi-Fi operations enforce lifecycle payload and event contracts", AvailableWifiContracts),
    ("Recent activity is a sanitized read-only capability", RecentActivityContracts),
    ("App library enumeration is opaque paged consent and lifecycle gated", AppLibraryContracts),
    ("Media session read and transport controls are sanitized granular and lifecycle-gated", MediaSessionContracts),
    ("Dashboard gesture authority is exact sequence-bound expiring and single-use", DashboardGestureAuthorityIsBounded),
    ("Wi-Fi radio read and control permissions are granular and host-gated", WifiRadioContracts),
    ("Bluetooth read and radio control are opaque granular and lifecycle-gated", BluetoothContracts),
    ("Consent updates are atomic across store instances", ConsentUpdatesAreAtomic),
    ("Retired consent migrates without weakening unknown-capability validation", RetiredConsentMigratesSafely),
    ("Subscriptions coalesce and suspend with lifecycle", EventsCoalesceAcrossLifecycle),
    ("Consent revocation terminates subscriptions", RevocationTerminatesSubscriptions),
    ("Lifecycle gates read control and destroying states", LifecycleGatesOperations),
    ("Lifecycle transitions cancel leased reads and controls before effects", LifecycleCancelsLeasedRequests),
    ("Consent denial corruption and deletion cancel leased requests", ConsentLossCancelsLeasedRequests),
    ("Cancellation reaches the broker boundary", CancellationIsObserved),
    ("Broker pipe scopes and isolated client SIDs are closed", BrokerPipeScopesAreClosed),
    ("Pipe framing rejects oversized payloads before allocation", PipeFramesAreBounded),
    ("Pipe handshake binds nonce identity and one client", PipeHandshakeIsBound),
    ("Pipe requests preserve identity correlation and lifecycle", PipeRequestsAreBound),
    ("Pipe request cancellation reaches the fixed backend", PipeCancellationIsObserved),
    ("Pipe lifecycle transitions cancel leased reads and controls", PipeLifecycleCancelsLeasedRequests),
    ("Pipe consent revocation cancels a leased control before effects", PipeConsentCancelsLeasedControl),
    ("Pipe events coalesce suspend and unsubscribe", PipeEventsAreBounded),
    ("Pipe consent denial revokes a live subscription", PipeConsentDenialRevokesLiveSubscription),
    ("Pipe consent corruption fails closed", PipeMalformedConsentRevokesLiveSubscription),
    ("Pipe consent deletion fails closed", PipeDeletedConsentRevokesLiveSubscription),
    ("Pipe disposal revokes and completes promptly", PipeDisposalIsBounded),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}

if (failures != 0) Environment.Exit(1);
Console.WriteLine($"PlatformBroker.Tests passed ({tests.Length} tests)");

static Task CapabilityVocabularyIsClosed()
{
    Assert.Equal(20, PlatformCapabilities.All.Count);
    foreach (var capability in PlatformCapabilities.All)
    {
        Assert.True(capability.Id.EndsWith($".v{capability.Version}", StringComparison.Ordinal));
        Assert.True(capability.Version == 1);
        Assert.True(capability.Operations.Count != 0);
    }
    Assert.True(!PlatformCapabilities.TryGet("system.full-access.v1", out _));
    Assert.True(!PlatformCapabilities.TryGet("system.audio.sessions.read.v2", out _));
    Assert.True(!PlatformCapabilities.TryGet("system.activity.recent.activate.v1", out _));
    Assert.True(PlatformCapabilities.TryGet(
        PlatformCapabilities.AppLibraryLaunchV1, out var appLaunch));
    Assert.Equal(BrokerCapabilityKind.Control, appLaunch.Kind);
    Assert.True(!appLaunch.AllowsDashboardGesture);
    Assert.True(PlatformCapabilities.TryGet(
        PlatformCapabilities.MediaSessionsControlV1, out var mediaControl));
    Assert.True(mediaControl.AllowsDashboardGesture);
    Assert.True(PlatformCapabilities.All
        .Where(capability => capability.Kind == BrokerCapabilityKind.Control &&
            capability.Id != PlatformCapabilities.MediaSessionsControlV1)
        .All(capability => !capability.AllowsDashboardGesture));
    var defaultControl = new BrokerCapabilityDefinition(
        "test.future.control.v1",
        1,
        BrokerCapabilityKind.Control,
        new HashSet<string>(["future.control"], StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));
    Assert.True(!defaultControl.AllowsDashboardGesture);
    return Task.CompletedTask;
}

static async Task BrokerPipeScopesAreClosed()
{
    BrokerPipeNames.Validate("gba-broker-test");
    BrokerPipeNames.ValidateAppContainerSid("S-1-15-2-1-2-3-4-5-6-7");

    Assert.Throws<ArgumentException>(() =>
        BrokerPipeNames.Validate(@"LOCAL\nested\pipe"));
    Assert.Throws<ArgumentException>(() => BrokerPipeNames.Validate("gba-broker\nested"));
    Assert.Throws<ArgumentException>(() => BrokerPipeNames.Validate("gba-broker\ncontrol"));
    Assert.Throws<ArgumentException>(() =>
        BrokerPipeNames.ValidateAppContainerSid("S-1-5-21-1"));
    Assert.Throws<ArgumentException>(() =>
        BrokerPipeNames.ValidateAppContainerSid("S-1-15-2-1\\other"));

    using var temporary = new TemporaryDirectory();
    await using var isolated = new BrokerPipeServer(
        $"gba-isolated-unbound-{Guid.NewGuid():N}",
        Identity(),
        [PlatformCapabilities.AudioSessionsReadV1],
        new ConsentStore(temporary.Path),
        new SimulatedPlatformBrokerBackend(),
        isolatedClientAppContainerSid: "S-1-15-2-1-2-3-4-5-6-7");
    await Assert.ThrowsAsync<InvalidOperationException>(() => isolated.RunAsync());
}

static async Task CompositeProviderDomainsAreSeparated()
{
    var audio = new SplitAudioBackend();
    var network = new SplitNetworkBackend();
    await using var composite = new CompositePlatformBrokerBackend(audio, network);
    var published = new List<BrokerPlatformEvent>();
    composite.EventPublished += (_, platformEvent) => published.Add(platformEvent);

    audio.Publish(new(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsChanged,
        new AudioSessionsChangedEvent([])));
    audio.Publish(new(
        PlatformCapabilities.NetworkReadV1,
        PlatformCapabilities.NetworkStatusChanged,
        new NetworkStatusChangedEvent(TestNetwork.Disconnected())));
    network.Publish(new(
        PlatformCapabilities.NetworkReadV1,
        PlatformCapabilities.NetworkStatusChanged,
        new NetworkStatusChangedEvent(TestNetwork.Disconnected())));
    network.Publish(new(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsChanged,
        new AudioSessionsChangedEvent([])));

    Assert.Equal(2, published.Count);
    Assert.Equal(PlatformCapabilities.AudioSessionsReadV1, published[0].CapabilityId);
    Assert.Equal(PlatformCapabilities.NetworkReadV1, published[1].CapabilityId);
}

static async Task RecentActivityContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetRecentActivities(
    [
        new RecentActivitySummary("activity-one", "Safe App", RecentActivityKind.Application,
            IsRunning: true, IsMostRecent: true),
    ]);
    await store.SetDecisionAsync(identity, PlatformCapabilities.RecentActivityReadV1,
        ConsentDecision.Grant);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.RecentActivityReadV1);

    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var read = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.RecentActivityReadV1,
        PlatformCapabilities.RecentActivitiesList, new { }));
    Assert.True(read.Succeeded);
    Assert.True(!read.Payload!.Value.GetRawText().Contains("pid", StringComparison.OrdinalIgnoreCase));

    var subscription = await broker.SubscribeAsync(
        PlatformCapabilities.RecentActivityReadV1,
        PlatformCapabilities.RecentActivitiesChanged);
    backend.Publish(new BrokerPlatformEvent(
        PlatformCapabilities.RecentActivityReadV1,
        PlatformCapabilities.RecentActivitiesChanged,
        new RecentActivitiesChangedEvent(
        [
            new RecentActivitySummary("activity-two", "Another App",
                RecentActivityKind.Application, true, true),
        ])));
    var change = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal(PlatformCapabilities.RecentActivitiesChanged, change.EventType);
}

static async Task AppLibraryContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibrary(Enumerable.Range(0, 70).Select(index =>
        new AppLibraryItemSummary(
            $"app-{index:D3}",
            $"Launchable {index:D3}",
            index == 0 ? AppLibraryKind.Game : AppLibraryKind.Application)));
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryLaunchV1);

    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var denied = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new { offset = 0, limit = 64 }));
    Assert.Equal("permission_denied", denied.ErrorCode);

    await store.SetDecisionAsync(identity, PlatformCapabilities.AppLibraryReadV1,
        ConsentDecision.Grant);
    broker.SetLifecycle(BrokerLifecycleState.Background);
    var background = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new { offset = 0, limit = 64 }));
    Assert.Equal("lifecycle_denied", background.ErrorCode);

    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var first = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new { offset = 0, limit = 64 }));
    Assert.True(first.Succeeded, $"First app-library page failed: {first.ErrorCode}");
    var firstPayload = first.Payload!.Value;
    Assert.Equal(64, firstPayload.GetProperty("items").GetArrayLength());
    Assert.Equal(64, firstPayload.GetProperty("nextOffset").GetInt32());
    var firstItem = firstPayload.GetProperty("items")[0];
    var publicAppId = firstItem.GetProperty("appId").GetString()!;
    Assert.True(publicAppId.StartsWith("app-", StringComparison.Ordinal));
    Assert.True(publicAppId != "app-000",
        "A provider-global ID escaped the widget-scoped broker projection.");
    Assert.Equal("Launchable 000", firstItem.GetProperty("displayName").GetString());
    var json = firstPayload.GetRawText();
    Assert.True(!json.Contains(".lnk", StringComparison.OrdinalIgnoreCase));
    Assert.True(!json.Contains("C:\\\\", StringComparison.OrdinalIgnoreCase));
    Assert.True(!json.Contains("aumid", StringComparison.OrdinalIgnoreCase));
    Assert.Equal(1, backend.AppLibraryRefreshCalls);

    // Later pages remain bound to the first-page snapshot even if the shared
    // backend changes. A new first page is the explicit refresh boundary.
    backend.SetAppLibrary([
        new AppLibraryItemSummary("changed", "Changed", AppLibraryKind.Application),
    ]);
    var last = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new { offset = 64, limit = 64 }));
    Assert.True(last.Succeeded);
    Assert.Equal(6, last.Payload!.Value.GetProperty("items").GetArrayLength());
    Assert.Equal(JsonValueKind.Null,
        last.Payload.Value.GetProperty("nextOffset").ValueKind);
    Assert.Equal(1, backend.AppLibraryRefreshCalls);

    var invalidPage = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new { offset = -1, limit = 65 }));
    Assert.Equal("invalid_payload", invalidPage.ErrorCode);

    backend.SetAppLibrary([
        new AppLibraryItemSummary("duplicate", "First", AppLibraryKind.Application),
        new AppLibraryItemSummary("duplicate", "Second", AppLibraryKind.Application),
    ]);
    var invalidBackend = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new { offset = 0, limit = 64 }));
    Assert.Equal("invalid_backend_data", invalidBackend.ErrorCode);

    var launchWithoutConsent = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new { appId = publicAppId }));
    Assert.Equal("permission_denied", launchWithoutConsent.ErrorCode);
    Assert.Equal(0, backend.AppLibraryLaunchCalls);

    await store.SetDecisionAsync(identity, PlatformCapabilities.AppLibraryLaunchV1,
        ConsentDecision.Grant);
    Assert.Throws<BrokerException>(() => broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        inputSequence: 10,
        snapshotSequence: 20,
        TimeSpan.FromSeconds(1)));
    var visibleLaunch = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new { appId = publicAppId }));
    Assert.Equal("lifecycle_denied", visibleLaunch.ErrorCode);
    Assert.Equal(0, backend.AppLibraryLaunchCalls);

    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var invalidIdentifier = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new { appId = @"C:\private\Game.lnk" }));
    Assert.Equal("invalid_payload", invalidIdentifier.ErrorCode);
    Assert.Equal(0, backend.AppLibraryLaunchCalls);

    var providerTokenForgery = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new { appId = "app-000" }));
    Assert.Equal("app_not_found", providerTokenForgery.ErrorCode);
    Assert.Equal(0, backend.AppLibraryLaunchCalls);

    var launched = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new { appId = publicAppId }));
    Assert.True(launched.Succeeded);
    Assert.True(launched.Payload!.Value.GetProperty("acknowledged").GetBoolean());
    Assert.Equal(1, backend.AppLibraryLaunchCalls);
    Assert.Equal("app-000", backend.LastLaunchedAppId);

    backend.SetAppLibrary([
        new AppLibraryItemSummary("changed", "Changed", AppLibraryKind.Application),
    ]);
    var refreshed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new { offset = 0, limit = 64 }));
    Assert.True(refreshed.Succeeded);
    Assert.Equal("Changed", refreshed.Payload!.Value.GetProperty("items")[0]
        .GetProperty("displayName").GetString());
    Assert.Equal(3, backend.AppLibraryRefreshCalls);

    var staleToken = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new { appId = publicAppId }));
    Assert.Equal("app_not_found", staleToken.ErrorCode);

    var secondIdentity = new BrokerWidgetIdentity(
        "dev.test.widget-two", "dev.test", "default");
    await store.SetDecisionAsync(secondIdentity, PlatformCapabilities.AppLibraryReadV1,
        ConsentDecision.Grant);
    await using var secondBroker = Broker(secondIdentity, store, backend,
        PlatformCapabilities.AppLibraryReadV1);
    secondBroker.SetLifecycle(BrokerLifecycleState.Visible);
    var secondWidget = await secondBroker.HandleAsync(Request(secondIdentity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new { offset = 0, limit = 64 }));
    var secondWidgetId = secondWidget.Payload!.Value.GetProperty("items")[0]
        .GetProperty("appId").GetString();
    var firstWidgetId = refreshed.Payload.Value.GetProperty("items")[0]
        .GetProperty("appId").GetString();
    Assert.True(firstWidgetId != secondWidgetId,
        "Opaque app IDs must not correlate two widget broker sessions.");
}

static async Task MediaSessionContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.MediaSessionsReadV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.MediaSessionsControlV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetMediaSessions([
        new("media-1", "Player", "Safe title", "Safe artist", MediaPlaybackStatus.Playing,
            1_000, 10_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, true,
            true, true, true, true, true),
    ]);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var read = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsGet, new { }));
    Assert.True(read.Succeeded && read.Payload is not null);
    var json = read.Payload!.Value.GetRawText();
    Assert.Contains("Safe title", json);
    Assert.DoesNotContain("aumid", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("process", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("handle", json, StringComparison.OrdinalIgnoreCase);

    var visibleControl = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "next" }));
    Assert.Equal("lifecycle_denied", visibleControl.ErrorCode);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var controlled = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "next" }));
    Assert.True(controlled.Succeeded);
    Assert.Equal(1, backend.MediaControlCalls);
    Assert.Equal(MediaSessionCommand.Next, backend.LastMediaCommand);

    var malformed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "private id", command = "play" }));
    Assert.Equal("invalid_payload", malformed.ErrorCode);

    var subscription = await broker.SubscribeAsync(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsChanged);
    backend.Publish(new BrokerPlatformEvent(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsChanged,
        new MediaSessionsChangedEvent([
            new("media-2", "Second player", "Another title", "Another artist",
                MediaPlaybackStatus.Paused, 2_000, 8_000,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, true,
                true, true, true, false, false),
        ])));
    var change = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal(PlatformCapabilities.MediaSessionsChanged, change.EventType);
    Assert.Equal("media-2", change.Payload.GetProperty("sessions")[0]
        .GetProperty("sessionId").GetString());
    await subscription.DisposeAsync();
}

static async Task ConsentFailsClosed()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = AudioBackend();
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AudioSessionsReadV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var missing = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsReadV1, PlatformCapabilities.AudioSessionsList, new { }));
    Assert.Equal("permission_denied", missing.ErrorCode);

    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Deny);
    var denied = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsReadV1, PlatformCapabilities.AudioSessionsList, new { }));
    Assert.Equal("permission_denied", denied.ErrorCode);
}

static async Task DashboardGestureAuthorityIsBounded()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.MediaSessionsControlV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsControlV1,
        ConsentDecision.Grant);
    var backend = AudioBackend();
    backend.SetMediaSessions([
        new("media-1", "Player", "Title", "Artist", MediaPlaybackStatus.Paused,
            0, 10_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, true,
            true, true, true, true, true),
    ]);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.AudioSessionsControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var noGrant = await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "next" }, 1, 5));
    Assert.Equal("lifecycle_denied", noGrant.ErrorCode);

    broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl, 10, 5, TimeSpan.FromSeconds(2));
    var wrongCapability = await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.AudioSessionsControlV1,
        PlatformCapabilities.AudioSessionSetMuted,
        new { sessionId = "audio-1", isMuted = true }, 10, 5));
    Assert.Equal("lifecycle_denied", wrongCapability.ErrorCode);
    var exact = await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "next" }, 10, 5));
    Assert.True(exact.Succeeded);
    var replay = await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "previous" }, 10, 5));
    Assert.Equal("lifecycle_denied", replay.ErrorCode);
    Assert.Equal(1, backend.MediaControlCalls);

    Assert.Throws<BrokerException>(() => broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.AudioSessionsControlV1,
        PlatformCapabilities.AudioSessionSetMuted, 11, 5, TimeSpan.FromSeconds(2)),
        "unsupported_capability");
    Assert.Equal(0, backend.AudioControlCalls);

    broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl, 12, 6, TimeSpan.FromSeconds(2));
    broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl, 13, 6, TimeSpan.FromSeconds(2));
    Assert.True((await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "next" }, 12, 6))).Succeeded);
    Assert.True((await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "previous" }, 13, 6))).Succeeded);
    Assert.Equal(3, backend.MediaControlCalls);

    broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl, 14, 7, TimeSpan.FromMilliseconds(10));
    await Task.Delay(40);
    var expired = await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "next" }, 14, 7));
    Assert.Equal("lifecycle_denied", expired.ErrorCode);
    Assert.Throws<BrokerException>(() => broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl, 14, 7, TimeSpan.FromSeconds(1)),
        "gesture_replayed");

    broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl, 15, 8, TimeSpan.FromSeconds(2));
    broker.SetLifecycle(BrokerLifecycleState.Background);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    Assert.Equal("lifecycle_denied", (await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "next" }, 15, 8))).ErrorCode);

    broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl, 16, 9, TimeSpan.FromSeconds(2));
    await store.SetDecisionAsync(identity, PlatformCapabilities.MediaSessionsControlV1,
        ConsentDecision.Deny);
    Assert.Equal("permission_denied", (await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        new { sessionId = "media-1", command = "next" }, 16, 9))).ErrorCode);

    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    Assert.Throws<BrokerException>(() => broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl, 17, 10, TimeSpan.FromSeconds(1)),
        "lifecycle_denied");
}

static async Task IdentityMismatchIsDenied()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var impostor = identity with { InstanceId = "other-instance" };
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    await using var broker = Broker(identity, store, AudioBackend(),
        PlatformCapabilities.AudioSessionsReadV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var response = await broker.HandleAsync(Request(impostor,
        PlatformCapabilities.AudioSessionsReadV1, PlatformCapabilities.AudioSessionsList, new { }));
    Assert.Equal("identity_mismatch", response.ErrorCode);
}

static async Task RequestsAreStrictAndBounded()
{
    var identity = Identity();
    var duplicate = Encoding.UTF8.GetBytes("""
        {"protocolVersion":1,"protocolVersion":1,"requestId":1,
         "widget":{"packageId":"dev.test.widget","publisherId":"dev.test","instanceId":"default"},
         "capabilityId":"system.audio.sessions.read.v1","operation":"audio.sessions.list","payload":{}}
        """);
    Assert.Throws<BrokerException>(() => BrokerJson.ParseRequest(duplicate), "malformed_request");

    var unknown = Encoding.UTF8.GetBytes("""
        {"protocolVersion":1,"requestId":1,
         "widget":{"packageId":"dev.test.widget","publisherId":"dev.test","instanceId":"default"},
         "capabilityId":"system.audio.sessions.read.v1","operation":"audio.sessions.list","payload":{},
         "surprise":true}
        """);
    Assert.Throws<BrokerException>(() => BrokerJson.ParseRequest(unknown), "malformed_request");

    var oversized = new byte[BrokerJson.MaximumRequestBytes + 1];
    Assert.Throws<BrokerException>(() => BrokerJson.ParseRequest(oversized), "request_too_large");

    var missingIdentity = Encoding.UTF8.GetBytes("""
        {"protocolVersion":1,"requestId":1,"widget":null,
         "capabilityId":"system.audio.sessions.read.v1","operation":"audio.sessions.list","payload":{}}
        """);
    Assert.Throws<BrokerException>(() => BrokerJson.ParseRequest(missingIdentity), "invalid_identity");

    using var temp = new TemporaryDirectory();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsControlV1,
        ConsentDecision.Grant);
    var backend = AudioBackend();
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AudioSessionsControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var unsafeId = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsControlV1, PlatformCapabilities.AudioSessionSetVolume,
        new { sessionId = "C:\\private\\device", volume = 0.5 }));
    Assert.Equal("invalid_payload", unsafeId.ErrorCode);
    Assert.Equal(0, backend.AudioControlCalls);
    var missingRequiredField = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsControlV1, PlatformCapabilities.AudioSessionSetVolume,
        new { sessionId = "audio-1" }));
    Assert.Equal("invalid_payload", missingRequiredField.ErrorCode);
    Assert.Equal(0, backend.AudioControlCalls);

    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    await using var readBroker = Broker(identity, store, backend,
        PlatformCapabilities.AudioSessionsReadV1);
    readBroker.SetLifecycle(BrokerLifecycleState.Visible);
    var unexpectedPayload = await readBroker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsReadV1, PlatformCapabilities.AudioSessionsList,
        new { ignored = true }));
    Assert.Equal("invalid_payload", unexpectedPayload.ErrorCode);
}

static async Task AudioOperationsAreSanitized()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsControlV1,
        ConsentDecision.Grant);
    var backend = AudioBackend();
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var listed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsReadV1, PlatformCapabilities.AudioSessionsList, new { }));
    Assert.True(listed.Succeeded && listed.Payload is not null);
    var json = listed.Payload.GetValueOrDefault().GetRawText();
    Assert.Contains("Game audio", json);
    Assert.DoesNotContain("process", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("handle", json, StringComparison.OrdinalIgnoreCase);

    var visibleControl = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsControlV1, PlatformCapabilities.AudioSessionSetMuted,
        new { sessionId = "audio-1", isMuted = true }));
    Assert.Equal("lifecycle_denied", visibleControl.ErrorCode);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var controlled = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsControlV1, PlatformCapabilities.AudioSessionSetVolume,
        new { sessionId = "audio-1", volume = 0.4 }));
    Assert.True(controlled.Succeeded);
    Assert.Equal(1, backend.AudioControlCalls);
}

static async Task MasterOutputContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioOutputReadV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioOutputControlV1,
        ConsentDecision.Grant);
    var backend = AudioBackend();
    backend.AudioOutput = new AudioOutputSummary(0.55, false);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AudioOutputReadV1,
        PlatformCapabilities.AudioOutputControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var malformed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioOutputReadV1, PlatformCapabilities.AudioOutputGet,
        new { ignored = true }));
    Assert.Equal("invalid_payload", malformed.ErrorCode);
    var read = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioOutputReadV1, PlatformCapabilities.AudioOutputGet, new { }));
    Assert.True(read.Succeeded && read.Payload is not null);
    Assert.Contains("0.55", read.Payload!.Value.GetRawText());

    var denied = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioOutputControlV1, PlatformCapabilities.AudioOutputSetMuted,
        new { isMuted = true }));
    Assert.Equal("lifecycle_denied", denied.ErrorCode);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var controlled = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioOutputControlV1, PlatformCapabilities.AudioOutputSetVolume,
        new { volume = 0.7 }));
    Assert.True(controlled.Succeeded);
    Assert.Equal(0.7, backend.AudioOutput.Volume);

    var subscription = await broker.SubscribeAsync(
        PlatformCapabilities.AudioOutputReadV1,
        PlatformCapabilities.AudioOutputChanged);
    backend.Publish(new(PlatformCapabilities.AudioOutputReadV1,
        PlatformCapabilities.AudioOutputChanged,
        new AudioOutputChangedEvent(new AudioOutputSummary(0.1, false), false)));
    backend.Publish(new(PlatformCapabilities.AudioOutputReadV1,
        PlatformCapabilities.AudioOutputChanged,
        new AudioOutputChangedEvent(new AudioOutputSummary(0.8, true), true)));
    var change = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Contains("0.8", change.Payload.GetRawText());
    Assert.Contains("true", change.Payload.GetRawText());
    await subscription.DisposeAsync();
}

static async Task AudioDeviceInputContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    foreach (var capability in new[]
             {
                 PlatformCapabilities.AudioDevicesReadV1,
                 PlatformCapabilities.AudioInputReadV1,
                 PlatformCapabilities.AudioInputControlV1,
             })
        await store.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
    var backend = AudioBackend();
    backend.SetAudioDevices([
        new AudioDeviceSummary("device_output", "Speakers", AudioDeviceDirection.Output, true),
        new AudioDeviceSummary("device_input", "Microphone", AudioDeviceDirection.Input, true),
    ]);
    backend.AudioInput = new AudioInputSummary(0.4, false);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AudioDevicesReadV1,
        PlatformCapabilities.AudioInputReadV1,
        PlatformCapabilities.AudioInputControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var devices = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioDevicesReadV1, PlatformCapabilities.AudioDevicesList, new { }));
    Assert.True(devices.Succeeded && devices.Payload is not null);
    var deviceJson = devices.Payload!.Value.GetRawText();
    Assert.Contains("device_output", deviceJson);
    Assert.Contains("Speakers", deviceJson);
    Assert.DoesNotContain("MMDEVAPI", deviceJson, StringComparison.OrdinalIgnoreCase);
    var input = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioInputReadV1, PlatformCapabilities.AudioInputGet, new { }));
    Assert.True(input.Succeeded && input.Payload is not null);

    var visibleControl = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioInputControlV1, PlatformCapabilities.AudioInputSetMuted,
        new { isMuted = true }));
    Assert.Equal("lifecycle_denied", visibleControl.ErrorCode);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var volume = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioInputControlV1, PlatformCapabilities.AudioInputSetVolume,
        new { volume = 0.65 }));
    Assert.True(volume.Succeeded);
    Assert.Equal(0.65, backend.AudioInput.Volume);
    var malformed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioInputControlV1, PlatformCapabilities.AudioInputSetVolume,
        new { volume = 1.5 }));
    Assert.Equal("invalid_payload", malformed.ErrorCode);

    var subscription = await broker.SubscribeAsync(
        PlatformCapabilities.AudioDevicesReadV1,
        PlatformCapabilities.AudioDevicesChanged);
    backend.Publish(new(PlatformCapabilities.AudioDevicesReadV1,
        PlatformCapabilities.AudioDevicesChanged,
        new AudioDevicesChangedEvent([
            new AudioDeviceSummary("device_input", "USB microphone", AudioDeviceDirection.Input, true),
        ])));
    var change = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Contains("USB microphone", change.Payload.GetRawText());
    await subscription.DisposeAsync();
}

static async Task NetworkOperationsAreSanitized()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkReadV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkSavedProfileSwitchV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend
    {
        NetworkStatus = new(
            NetworkConnectivity.Internet,
            NetworkTransportKind.Wifi,
            NetworkWirelessAvailability.Available,
            NetworkDetailsAccess.Available,
            NetworkConnectionAttemptState.None,
            null,
            "home-5g",
            "Home 5G",
            92),
    };
    backend.SetSavedNetworkProfiles([
        new("home-5g", "Home 5G", true, 92),
        new("office", "Office", false, 54),
    ]);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.NetworkReadV1,
        PlatformCapabilities.NetworkSavedProfileSwitchV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var profiles = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkReadV1, PlatformCapabilities.NetworkSavedProfilesList, new { }));
    Assert.True(profiles.Succeeded && profiles.Payload is not null);
    var json = profiles.Payload.GetValueOrDefault().GetRawText();
    Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("handle", json, StringComparison.OrdinalIgnoreCase);

    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var switched = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkSavedProfileSwitchV1,
        PlatformCapabilities.NetworkSavedProfileSwitch,
        new { profileId = "office" }));
    Assert.True(switched.Succeeded);
    Assert.Equal(1, backend.NetworkSwitchCalls);

    var publicProperties = typeof(SwitchSavedNetworkProfileRequest).GetProperties()
        .Select(property => property.Name).ToArray();
    Assert.SequenceEqual(["ProfileId"], publicProperties);
}

static async Task AvailableWifiContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkWifiReadV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkWifiConnectV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAvailableWifiNetworks([
        new("wifi_0123456789abcdef0123456789abcdef", "Cafe Wi-Fi", 74,
            WifiSecurityKind.Open, false, false, false),
        new("wifi_fedcba9876543210fedcba9876543210", "Saved home", 92,
            WifiSecurityKind.Personal, false, true, true),
    ]);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.NetworkWifiReadV1,
        PlatformCapabilities.NetworkWifiConnectV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var listed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiReadV1,
        PlatformCapabilities.NetworkAvailableWifiGet, new { }));
    Assert.True(listed.Succeeded && listed.Payload is not null);
    var json = listed.Payload!.Value.GetRawText();
    Assert.Contains("Cafe Wi-Fi", json);
    Assert.Contains("Saved home", json);
    Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("bssid", json, StringComparison.OrdinalIgnoreCase);

    var malformedGet = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiReadV1,
        PlatformCapabilities.NetworkAvailableWifiGet, new { refresh = true }));
    Assert.Equal("invalid_payload", malformedGet.ErrorCode);
    var malformedScan = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiReadV1,
        PlatformCapabilities.NetworkWifiScan, new { poll = true }));
    Assert.Equal("invalid_payload", malformedScan.ErrorCode);

    var subscription = await broker.SubscribeAsync(
        PlatformCapabilities.NetworkWifiReadV1,
        PlatformCapabilities.NetworkAvailableWifiChanged);
    backend.Publish(new(
        PlatformCapabilities.NetworkWifiReadV1,
        PlatformCapabilities.NetworkAvailableWifiChanged,
        new AvailableWifiNetworksChangedEvent(new(
            WifiScanState.PreciseLocationDenied, []))));
    var deniedEvent = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("preciseLocationDenied",
        deniedEvent.Payload.GetProperty("snapshot").GetProperty("scanState").GetString());

    var scanned = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiReadV1,
        PlatformCapabilities.NetworkWifiScan, new { }));
    Assert.True(scanned.Succeeded);
    Assert.Equal(1, backend.WifiScanCalls);

    var visibleConnect = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiConnectV1,
        PlatformCapabilities.NetworkAvailableWifiConnect,
        new { networkId = "wifi_0123456789abcdef0123456789abcdef" }));
    Assert.Equal("lifecycle_denied", visibleConnect.ErrorCode);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var malformedConnect = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiConnectV1,
        PlatformCapabilities.NetworkAvailableWifiConnect,
        new { networkId = "Cafe Wi-Fi" }));
    Assert.Equal("invalid_payload", malformedConnect.ErrorCode);
    var connected = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiConnectV1,
        PlatformCapabilities.NetworkAvailableWifiConnect,
        new { networkId = "wifi_0123456789abcdef0123456789abcdef" }));
    Assert.True(connected.Succeeded);
    Assert.Equal(1, backend.WifiConnectCalls);
    await subscription.DisposeAsync();
}

static async Task WifiRadioContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkWifiRadioReadV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkWifiRadioControlV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend
    {
        WifiRadio = new WifiRadioSummary(WifiRadioState.On, true),
    };
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.NetworkWifiRadioReadV1,
        PlatformCapabilities.NetworkWifiRadioControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var read = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiRadioReadV1,
        PlatformCapabilities.NetworkWifiRadioGet, new { }));
    Assert.True(read.Succeeded);
    Assert.Equal("on", read.Payload!.Value.GetProperty("state").GetString());
    var malformed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiRadioReadV1,
        PlatformCapabilities.NetworkWifiRadioGet, new { adapter = "secret" }));
    Assert.Equal("invalid_payload", malformed.ErrorCode);

    var deniedWhileVisible = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiRadioControlV1,
        PlatformCapabilities.NetworkWifiRadioSet, new { enabled = false }));
    Assert.Equal("lifecycle_denied", deniedWhileVisible.ErrorCode);

    var subscription = await broker.SubscribeAsync(
        PlatformCapabilities.NetworkWifiRadioReadV1,
        PlatformCapabilities.NetworkWifiRadioChanged);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var changed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiRadioControlV1,
        PlatformCapabilities.NetworkWifiRadioSet, new { enabled = false }));
    Assert.True(changed.Succeeded);
    Assert.Equal(1, backend.WifiRadioControlCalls);
    var radioEvent = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("off", radioEvent.Payload.GetProperty("radio").GetProperty("state").GetString());

    var malformedSet = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkWifiRadioControlV1,
        PlatformCapabilities.NetworkWifiRadioSet, new { enabled = false, force = true }));
    Assert.Equal("invalid_payload", malformedSet.ErrorCode);
    await subscription.DisposeAsync();
}

static async Task BluetoothContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkBluetoothReadV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkBluetoothRadioControlV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend
    {
        BluetoothRadioState = BluetoothRadioState.On,
        CanControlBluetoothRadio = true,
    };
    backend.SetBluetoothDevices([
        new("bluetooth-1", "Wireless controller", true, true, true),
        new("bluetooth-2", "Nearby keyboard", false, false, true),
    ]);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.NetworkBluetoothReadV1,
        PlatformCapabilities.NetworkBluetoothRadioControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var malformed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothReadV1,
        PlatformCapabilities.NetworkBluetoothGet, new { nativeId = "secret" }));
    Assert.Equal("invalid_payload", malformed.ErrorCode);

    var listed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothReadV1,
        PlatformCapabilities.NetworkBluetoothGet, new { }));
    Assert.True(listed.Succeeded && listed.Payload is not null);
    var json = listed.Payload!.Value.GetRawText();
    Assert.Contains("Wireless controller", json);
    Assert.Contains("Nearby keyboard", json);
    Assert.DoesNotContain("address", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("handle", json, StringComparison.OrdinalIgnoreCase);

    var subscription = await broker.SubscribeAsync(
        PlatformCapabilities.NetworkBluetoothReadV1,
        PlatformCapabilities.NetworkBluetoothChanged);
    backend.Publish(new BrokerPlatformEvent(
        PlatformCapabilities.NetworkBluetoothReadV1,
        PlatformCapabilities.NetworkBluetoothChanged,
        new BluetoothChangedEvent(new BluetoothSummary(
            BluetoothRadioState.Off, true, BluetoothDiscoveryState.Ready, []))));
    var change = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("off", change.Payload.GetProperty("snapshot").GetProperty("radioState").GetString());

    var visibleControl = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothRadioControlV1,
        PlatformCapabilities.NetworkBluetoothRadioSet, new { enabled = false }));
    Assert.Equal("lifecycle_denied", visibleControl.ErrorCode);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var changed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothRadioControlV1,
        PlatformCapabilities.NetworkBluetoothRadioSet, new { enabled = false }));
    Assert.True(changed.Succeeded);
    Assert.Equal(1, backend.BluetoothRadioControlCalls);
    await subscription.DisposeAsync();
}

static async Task ConsentUpdatesAreAtomic()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var capabilities = PlatformCapabilities.All.Select(capability => capability.Id).ToArray();
    await Task.WhenAll(capabilities.Select((capability, index) =>
        new ConsentStore(temp.Path).SetDecisionAsync(identity, capability,
            index % 2 == 0 ? ConsentDecision.Grant : ConsentDecision.Deny)));
    var document = await new ConsentStore(temp.Path).LoadAsync();
    Assert.Equal(capabilities.Length, document.Entries.Count);
    Assert.Equal(capabilities.Length, checked((int)document.Revision));
    Assert.SequenceEqual(capabilities.Order(StringComparer.Ordinal),
        document.Entries.Select(entry => entry.CapabilityId).Order(StringComparer.Ordinal));
}

static async Task RetiredConsentMigratesSafely()
{
    using var temp = new TemporaryDirectory();
    Directory.CreateDirectory(temp.Path);
    var documentPath = Path.Combine(temp.Path, "consent-v1.json");
    await File.WriteAllTextAsync(documentPath,
        """
        {"schemaVersion":1,"revision":7,"entries":[
          {"packageId":"dev.test.widget","publisherId":"dev.test.publisher","capabilityId":"system.audio.sessions.read.v1","decision":"grant"},
          {"packageId":"org.gbar.firstparty.recent-apps","publisherId":"org.gbar.firstparty","capabilityId":"system.activity.recent.activate.v1","decision":"grant"},
          {"packageId":"dev.test.widget","publisherId":"dev.test.publisher","capabilityId":"system.audio.sessions.control.v1","decision":"deny"}
        ]}
        """);

    var store = new ConsentStore(temp.Path);
    var migrated = await store.LoadAsync();
    Assert.Equal(7L, migrated.Revision);
    Assert.Equal(2, migrated.Entries.Count);
    Assert.True(!migrated.Entries.Any(entry =>
        entry.CapabilityId == "system.activity.recent.activate.v1"));
    var identity = new BrokerWidgetIdentity("dev.test.widget", "dev.test.publisher", "test");
    Assert.Equal(ConsentDecision.Grant, await store.GetDecisionAsync(
        identity, PlatformCapabilities.AudioSessionsReadV1));
    Assert.Equal(ConsentDecision.Deny, await store.GetDecisionAsync(
        identity, PlatformCapabilities.AudioSessionsControlV1));

    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkReadV1,
        ConsentDecision.Grant);
    var persisted = await File.ReadAllTextAsync(documentPath);
    Assert.True(!persisted.Contains("system.activity.recent.activate.v1", StringComparison.Ordinal));

    await File.WriteAllTextAsync(documentPath,
        """{"schemaVersion":1,"revision":8,"entries":[{"packageId":"dev.test.widget","publisherId":"dev.test.publisher","capabilityId":"system.unknown.future.v1","decision":"grant"}]}""");
    await Assert.ThrowsAsync<BrokerException>(async () => await store.LoadAsync(),
        "invalid_consent");

    await File.WriteAllTextAsync(documentPath,
        """
        {"schemaVersion":1,"revision":9,"entries":[
          {"packageId":"org.gbar.firstparty.recent-apps","publisherId":"org.gbar.firstparty","capabilityId":"system.activity.recent.activate.v1","decision":"grant"},
          {"packageId":"org.gbar.firstparty.recent-apps","publisherId":"org.gbar.firstparty","capabilityId":"system.activity.recent.activate.v1","decision":"deny"}
        ]}
        """);
    await Assert.ThrowsAsync<BrokerException>(async () => await store.LoadAsync(),
        "invalid_consent");
}

static async Task EventsCoalesceAcrossLifecycle()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    var backend = AudioBackend();
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AudioSessionsReadV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    await using var subscription = await broker.SubscribeAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsChanged);

    PublishAudio(backend, "one");
    PublishAudio(backend, "two");
    PublishAudio(backend, "three");
    var latest = await subscription.ReadAsync();
    Assert.Contains("three", latest.Payload.GetRawText());

    broker.SetLifecycle(BrokerLifecycleState.Background);
    PublishAudio(backend, "four");
    PublishAudio(backend, "five");
    using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)))
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await subscription.ReadAsync(timeout.Token));
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var resumed = await subscription.ReadAsync();
    Assert.Contains("five", resumed.Payload.GetRawText());
}

static async Task RevocationTerminatesSubscriptions()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkReadV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.NetworkReadV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    await using var subscription = await broker.SubscribeAsync(
        PlatformCapabilities.NetworkReadV1, PlatformCapabilities.NetworkStatusChanged);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkReadV1,
        ConsentDecision.Deny);
    await broker.RefreshConsentAsync();
    Assert.True(subscription.IsRevoked);
    await Assert.ThrowsAsync<BrokerException>(async () => await subscription.ReadAsync(),
        "capability_revoked");
}

static async Task LifecycleGatesOperations()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    var backend = AudioBackend();
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AudioSessionsReadV1);
    var background = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsReadV1, PlatformCapabilities.AudioSessionsList, new { }));
    Assert.Equal("lifecycle_denied", background.ErrorCode);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    Assert.True((await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsReadV1, PlatformCapabilities.AudioSessionsList,
        new { }))).Succeeded);
    broker.SetLifecycle(BrokerLifecycleState.Destroying);
    Assert.Throws<BrokerException>(() => broker.SetLifecycle(BrokerLifecycleState.Visible),
        "invalid_lifecycle");
}

static async Task LifecycleCancelsLeasedRequests()
{
    using var readTemp = new TemporaryDirectory();
    var identity = Identity();
    var readStore = new ConsentStore(readTemp.Path);
    await readStore.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    var readBackend = new LeaseBlockingBrokerBackend(blockRead: true, blockControl: false);
    await using (var broker = Broker(identity, readStore, readBackend,
                     PlatformCapabilities.AudioSessionsReadV1))
    {
        broker.SetLifecycle(BrokerLifecycleState.Visible);
        var pending = broker.HandleAsync(Request(identity,
            PlatformCapabilities.AudioSessionsReadV1,
            PlatformCapabilities.AudioSessionsList, new { }));
        await readBackend.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        broker.SetLifecycle(BrokerLifecycleState.Background);
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("lifecycle_denied", response.ErrorCode);
        await readBackend.ReadCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    using var controlTemp = new TemporaryDirectory();
    var controlStore = new ConsentStore(controlTemp.Path);
    await controlStore.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsControlV1,
        ConsentDecision.Grant);
    var controlBackend = new LeaseBlockingBrokerBackend(blockRead: false, blockControl: true);
    await using (var broker = Broker(identity, controlStore, controlBackend,
                     PlatformCapabilities.AudioSessionsControlV1))
    {
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        var pending = broker.HandleAsync(Request(identity,
            PlatformCapabilities.AudioSessionsControlV1,
            PlatformCapabilities.AudioSessionSetMuted,
            new { sessionId = "audio-1", isMuted = true }));
        await controlBackend.ControlStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        broker.SetLifecycle(BrokerLifecycleState.Visible);
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("lifecycle_denied", response.ErrorCode);
        await controlBackend.ControlCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, controlBackend.ControlEffects);
    }
}

static async Task ConsentLossCancelsLeasedRequests()
{
    foreach (var mutation in new[] { "deny", "corrupt", "delete" })
    {
        using var temp = new TemporaryDirectory();
        var identity = Identity();
        var store = new ConsentStore(temp.Path);
        await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
            ConsentDecision.Grant);
        var backend = new LeaseBlockingBrokerBackend(blockRead: true, blockControl: false);
        await using var broker = Broker(identity, store, backend,
            PlatformCapabilities.AudioSessionsReadV1);
        broker.SetLifecycle(BrokerLifecycleState.Visible);
        var pending = broker.HandleAsync(Request(identity,
            PlatformCapabilities.AudioSessionsReadV1,
            PlatformCapabilities.AudioSessionsList, new { }));
        await backend.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var document = System.IO.Path.Combine(temp.Path, "consent-v1.json");
        if (mutation == "deny")
            await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
                ConsentDecision.Deny);
        else if (mutation == "corrupt")
            await File.WriteAllTextAsync(document, "{ malformed");
        else
            File.Delete(document);

        await broker.RefreshConsentAsync();
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("capability_revoked", response.ErrorCode);
        await backend.ReadCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}

static async Task CancellationIsObserved()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    await using var broker = Broker(identity, store, AudioBackend(),
        PlatformCapabilities.AudioSessionsReadV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        await broker.HandleAsync(Request(identity, PlatformCapabilities.AudioSessionsReadV1,
            PlatformCapabilities.AudioSessionsList, new { }), cancellation.Token));

    var blocking = new LeaseBlockingBrokerBackend(blockRead: true, blockControl: false);
    await using var racingBroker = Broker(identity, store, blocking,
        PlatformCapabilities.AudioSessionsReadV1);
    racingBroker.SetLifecycle(BrokerLifecycleState.Visible);
    using var racingCancellation = new CancellationTokenSource();
    var pending = racingBroker.HandleAsync(Request(identity,
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList, new { }), racingCancellation.Token);
    await blocking.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    racingCancellation.Cancel();
    racingBroker.SetLifecycle(BrokerLifecycleState.Background);
    await Assert.ThrowsAsync<OperationCanceledException>(
        async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
}

static async Task PipeFramesAreBounded()
{
    var prefix = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(prefix, 1025);
    await using var stream = new MemoryStream(prefix);
    var channel = new BrokerPipeFrameChannel(stream, 1024);
    await Assert.ThrowsAsync<BrokerException>(
        async () => await channel.ReadAsync(CancellationToken.None), "frame_too_large");

    var duplicate = Encoding.UTF8.GetBytes(
        "{\"protocolVersion\":1,\"type\":\"hello\",\"type\":\"hello\",\"correlationId\":1,\"payload\":{}}");
    Assert.Throws<BrokerException>(() => BrokerPipeJson.Parse(duplicate), "malformed_frame");
}

static async Task PipeHandshakeIsBound()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var pipeName = $"gba-broker-auth-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName, identity, [PlatformCapabilities.AudioSessionsReadV1], store, AudioBackend(),
        TransportOptions(), channelNonce: new string('A', 64));
    var serverTask = server.RunAsync();
    await using var impostor = new BrokerPipeClient(
        pipeName, identity, new string('B', 64), TransportOptions());
    await Assert.ThrowsAnyAsync(() => impostor.ConnectAsync());
    await Assert.ThrowsAsync<BrokerException>(
        () => serverTask, "authentication_failed");
    await Assert.ThrowsAsync<InvalidOperationException>(() => server.RunAsync());
}

static async Task PipeRequestsAreBound()
{
    await using var harness = await BrokerPipeHarness.StartAsync();
    harness.Server.SetLifecycle(BrokerLifecycleState.Visible);
    var response = await harness.Client.RequestAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList,
        new { });
    Assert.True(response.Succeeded);
    Assert.True(response.RequestId > 0);
    Assert.Contains("Game audio", response.Payload!.Value.GetRawText());

    var impostor = Identity() with { InstanceId = "substituted" };
    var substitution = await harness.Client.SendRequestEnvelopeAsync(new BrokerRequestEnvelope(
        BrokerJson.ProtocolVersion, 0, impostor,
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList,
        BrokerJson.ToElement(new { })));
    Assert.Equal("identity_mismatch", substitution.ErrorCode);

    harness.Server.SetLifecycle(BrokerLifecycleState.Background);
    var denied = await harness.Client.RequestAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList,
        new { });
    Assert.Equal("lifecycle_denied", denied.ErrorCode);
}

static async Task PipeCancellationIsObserved()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    var backend = new BlockingBrokerBackend();
    var pipeName = $"gba-broker-cancel-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName, identity, [PlatformCapabilities.AudioSessionsReadV1], store, backend,
        TransportOptions(requestTimeout: TimeSpan.FromSeconds(2)), new string('C', 64));
    var serverTask = server.RunAsync();
    await using var client = new BrokerPipeClient(
        pipeName, identity, server.ChannelNonce,
        TransportOptions(requestTimeout: TimeSpan.FromSeconds(2)));
    await client.ConnectAsync();
    server.SetLifecycle(BrokerLifecycleState.Visible);
    using var cancellation = new CancellationTokenSource();
    var request = client.RequestAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList,
        new { }, cancellation.Token);
    await backend.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() => request);
    await backend.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await client.DisposeAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
}

static async Task PipeLifecycleCancelsLeasedRequests()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsControlV1,
        ConsentDecision.Grant);
    var backend = new LeaseBlockingBrokerBackend(blockRead: true, blockControl: true);
    var pipeName = $"gba-broker-lease-lifecycle-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName, identity,
        [PlatformCapabilities.AudioSessionsReadV1, PlatformCapabilities.AudioSessionsControlV1],
        store, backend, TransportOptions(requestTimeout: TimeSpan.FromSeconds(5)),
        new string('L', 64));
    var serverTask = server.RunAsync();
    await using var client = new BrokerPipeClient(
        pipeName, identity, server.ChannelNonce,
        TransportOptions(requestTimeout: TimeSpan.FromSeconds(5)));
    await client.ConnectAsync();

    server.SetLifecycle(BrokerLifecycleState.Visible);
    var read = client.RequestAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList, new { });
    await backend.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    server.SetLifecycle(BrokerLifecycleState.Background);
    Assert.Equal("lifecycle_denied",
        (await read.WaitAsync(TimeSpan.FromSeconds(2))).ErrorCode);

    server.SetLifecycle(BrokerLifecycleState.Interactive);
    var control = client.RequestAsync(
        PlatformCapabilities.AudioSessionsControlV1,
        PlatformCapabilities.AudioSessionSetMuted,
        new { sessionId = "audio-1", isMuted = true });
    await backend.ControlStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    server.SetLifecycle(BrokerLifecycleState.Visible);
    Assert.Equal("lifecycle_denied",
        (await control.WaitAsync(TimeSpan.FromSeconds(2))).ErrorCode);
    Assert.Equal(0, backend.ControlEffects);

    await client.DisposeAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
}

static async Task PipeConsentCancelsLeasedControl()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsControlV1,
        ConsentDecision.Grant);
    var backend = new LeaseBlockingBrokerBackend(blockRead: false, blockControl: true);
    var pipeName = $"gba-broker-lease-consent-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName, identity, [PlatformCapabilities.AudioSessionsControlV1], store, backend,
        TransportOptions(requestTimeout: TimeSpan.FromSeconds(5)), new string('R', 64));
    var serverTask = server.RunAsync();
    await using var client = new BrokerPipeClient(
        pipeName, identity, server.ChannelNonce,
        TransportOptions(requestTimeout: TimeSpan.FromSeconds(5)));
    await client.ConnectAsync();
    server.SetLifecycle(BrokerLifecycleState.Interactive);

    var pending = client.RequestAsync(
        PlatformCapabilities.AudioSessionsControlV1,
        PlatformCapabilities.AudioSessionSetMuted,
        new { sessionId = "audio-1", isMuted = true });
    await backend.ControlStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await new ConsentStore(temp.Path).SetDecisionAsync(
        identity, PlatformCapabilities.AudioSessionsControlV1, ConsentDecision.Deny);
    var response = await pending.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("capability_revoked", response.ErrorCode);
    await backend.ControlCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(0, backend.ControlEffects);

    await client.DisposeAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
}

static async Task PipeEventsAreBounded()
{
    await using var harness = await BrokerPipeHarness.StartAsync();
    harness.Server.SetLifecycle(BrokerLifecycleState.Visible);
    await using var subscription = await harness.Client.SubscribeAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsChanged);
    PublishAudio(harness.Backend, "one");
    PublishAudio(harness.Backend, "two");
    PublishAudio(harness.Backend, "three");
    await Task.Delay(30);
    var latest = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Contains("three", latest.Payload.GetRawText());

    harness.Server.SetLifecycle(BrokerLifecycleState.Background);
    PublishAudio(harness.Backend, "four");
    PublishAudio(harness.Backend, "five");
    using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)))
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await subscription.ReadAsync(timeout.Token));
    harness.Server.SetLifecycle(BrokerLifecycleState.Visible);
    var resumed = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Contains("five", resumed.Payload.GetRawText());

    await subscription.DisposeAsync();
    PublishAudio(harness.Backend, "six");
    await Assert.ThrowsAsync<System.Threading.Channels.ChannelClosedException>(
        async () => await subscription.ReadAsync());
}

static async Task PipeConsentDenialRevokesLiveSubscription()
{
    await using var harness = await BrokerPipeHarness.StartAsync();
    harness.Server.SetLifecycle(BrokerLifecycleState.Visible);
    await using var subscription = await harness.Client.SubscribeAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsChanged);

    await new ConsentStore(harness.ConsentRoot).SetDecisionAsync(
        Identity(), PlatformCapabilities.AudioSessionsReadV1, ConsentDecision.Deny);

    await Assert.ThrowsAsync<BrokerException>(async () =>
        await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)),
        "capability_revoked");
}

static async Task PipeMalformedConsentRevokesLiveSubscription()
{
    await using var harness = await BrokerPipeHarness.StartAsync();
    harness.Server.SetLifecycle(BrokerLifecycleState.Visible);
    await using var subscription = await harness.Client.SubscribeAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsChanged);

    await File.WriteAllTextAsync(
        System.IO.Path.Combine(harness.ConsentRoot, "consent-v1.json"), "{ malformed");

    await Assert.ThrowsAsync<BrokerException>(async () =>
        await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)),
        "capability_revoked");
}

static async Task PipeDeletedConsentRevokesLiveSubscription()
{
    await using var harness = await BrokerPipeHarness.StartAsync();
    harness.Server.SetLifecycle(BrokerLifecycleState.Visible);
    await using var subscription = await harness.Client.SubscribeAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsChanged);

    File.Delete(System.IO.Path.Combine(harness.ConsentRoot, "consent-v1.json"));

    await Assert.ThrowsAsync<BrokerException>(async () =>
        await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)),
        "capability_revoked");
}

static async Task PipeDisposalIsBounded()
{
    var harness = await BrokerPipeHarness.StartAsync();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    await harness.DisposeAsync();
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
        $"Broker pipe disposal was not bounded ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");
}

static BrokerPipeTransportOptions TransportOptions(TimeSpan? requestTimeout = null) => new()
{
    MaximumFrameBytes = 64 * 1024,
    AcceptTimeout = TimeSpan.FromSeconds(2),
    HandshakeTimeout = TimeSpan.FromSeconds(1),
    RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(1),
    MaximumInFlightRequests = 4,
    MaximumSubscriptions = 4,
};

static PlatformCapabilityBroker Broker(
    BrokerWidgetIdentity identity,
    ConsentStore store,
    IPlatformBrokerBackend backend,
    params string[] declared) => new(identity, declared, store, backend);

static BrokerWidgetIdentity Identity() => new("dev.test.widget", "dev.test", "default");

static SimulatedPlatformBrokerBackend AudioBackend()
{
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAudioSessions([new("audio-1", "Game audio", 0.75, false, true)]);
    return backend;
}

static void PublishAudio(SimulatedPlatformBrokerBackend backend, string name) =>
    backend.Publish(new(PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsChanged,
        new AudioSessionsChangedEvent([new("audio-1", name, 0.5, false, true)])));

static byte[] Request(
    BrokerWidgetIdentity identity,
    string capability,
    string operation,
    object payload) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        protocolVersion = BrokerJson.ProtocolVersion,
        requestId = 1,
        widget = new
        {
            packageId = identity.PackageId,
            publisherId = identity.PublisherId,
            instanceId = identity.InstanceId,
        },
        capabilityId = capability,
        operation,
        payload,
    });

static byte[] GestureRequest(
    BrokerWidgetIdentity identity,
    string capability,
    string operation,
    object payload,
    long inputSequence,
    long snapshotSequence) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        protocolVersion = BrokerJson.ProtocolVersion,
        requestId = 1,
        widget = new
        {
            packageId = identity.PackageId,
            publisherId = identity.PublisherId,
            instanceId = identity.InstanceId,
        },
        capabilityId = capability,
        operation,
        payload,
        gestureInputSequence = inputSequence,
        gestureSnapshotSequence = snapshotSequence,
    });

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "gba-platform-broker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

sealed class BrokerPipeHarness : IAsyncDisposable
{
    private readonly TemporaryDirectory _temp;
    private readonly Task _serverTask;
    public SimulatedPlatformBrokerBackend Backend { get; }
    public BrokerPipeServer Server { get; }
    public BrokerPipeClient Client { get; }
    public string ConsentRoot => _temp.Path;

    private BrokerPipeHarness(
        TemporaryDirectory temp,
        SimulatedPlatformBrokerBackend backend,
        BrokerPipeServer server,
        BrokerPipeClient client,
        Task serverTask)
    {
        _temp = temp;
        Backend = backend;
        Server = server;
        Client = client;
        _serverTask = serverTask;
    }

    public static async Task<BrokerPipeHarness> StartAsync()
    {
        var temp = new TemporaryDirectory();
        var identity = new BrokerWidgetIdentity("dev.test.widget", "dev.test", "default");
        var store = new ConsentStore(temp.Path);
        await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
            ConsentDecision.Grant);
        var backend = new SimulatedPlatformBrokerBackend();
        backend.SetAudioSessions([new("audio-1", "Game audio", 0.75, false, true)]);
        var options = new BrokerPipeTransportOptions
        {
            MaximumFrameBytes = 64 * 1024,
            AcceptTimeout = TimeSpan.FromSeconds(2),
            HandshakeTimeout = TimeSpan.FromSeconds(1),
            RequestTimeout = TimeSpan.FromSeconds(1),
            MaximumInFlightRequests = 4,
            MaximumSubscriptions = 4,
        };
        var pipeName = $"gba-broker-test-{Guid.NewGuid():N}";
        var server = new BrokerPipeServer(
            pipeName, identity,
            [PlatformCapabilities.AudioSessionsReadV1], store, backend,
            options, new string('D', 64));
        var serverTask = server.RunAsync();
        var client = new BrokerPipeClient(
            pipeName,
            identity, server.ChannelNonce, options);
        try
        {
            await client.ConnectAsync();
            return new BrokerPipeHarness(temp, backend, server, client, serverTask);
        }
        catch
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
            temp.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        await Server.DisposeAsync();
        _temp.Dispose();
    }
}

sealed class LeaseBlockingBrokerBackend : IPlatformBrokerBackend
{
    private readonly bool _blockRead;
    private readonly bool _blockControl;
    private int _controlEffects;

    public LeaseBlockingBrokerBackend(bool blockRead, bool blockControl)
    {
        _blockRead = blockRead;
        _blockControl = blockControl;
    }

    public event EventHandler<BrokerPlatformEvent>? EventPublished;
    public TaskCompletionSource ReadStarted { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReadCancellationObserved { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ControlStarted { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ControlCancellationObserved { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public int ControlEffects => Volatile.Read(ref _controlEffects);

    public async Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(
        CancellationToken cancellationToken)
    {
        ReadStarted.TrySetResult();
        if (_blockRead)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException)
            {
                ReadCancellationObserved.TrySetResult();
                throw;
            }
        }
        return [];
    }

    public Task SetAudioSessionVolumeAsync(
        string sessionId, double volume, CancellationToken cancellationToken) =>
        BlockControlAsync(cancellationToken);

    public Task SetAudioSessionMutedAsync(
        string sessionId, bool isMuted, CancellationToken cancellationToken) =>
        BlockControlAsync(cancellationToken);

    private async Task BlockControlAsync(CancellationToken cancellationToken)
    {
        ControlStarted.TrySetResult();
        if (_blockControl)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException)
            {
                ControlCancellationObserved.TrySetResult();
                throw;
            }
        }
        Interlocked.Increment(ref _controlEffects);
    }

    public Task<AudioOutputSummary> GetAudioOutputAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AudioOutputSummary(0.5, false));
    public Task SetAudioOutputVolumeAsync(double volume, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task SetAudioOutputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<IReadOnlyList<AudioDeviceSummary>> GetAudioDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AudioDeviceSummary>>([]);
    public Task<AudioInputSummary> GetAudioInputAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AudioInputSummary(0.5, false));
    public Task SetAudioInputVolumeAsync(double volume, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task SetAudioInputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TestNetwork.Disconnected());
    public Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SavedNetworkProfileSummary>>([]);
    public Task SwitchSavedNetworkProfileAsync(string profileId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AvailableWifiNetworksSummary(WifiScanState.NotScanned, []));
    public Task RequestWifiScanAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ConnectAvailableWifiNetworkAsync(string networkId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WifiRadioSummary(WifiRadioState.On, true));
    public Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecentActivitySummary>>([]);
    public Task<BluetoothSummary> GetBluetoothAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BluetoothSummary(
            BluetoothRadioState.Unavailable, false, BluetoothDiscoveryState.Unavailable, []));
    public Task SetBluetoothRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public void Publish(BrokerPlatformEvent platformEvent) => EventPublished?.Invoke(this, platformEvent);
}

sealed class BlockingBrokerBackend : IPlatformBrokerBackend
{
    public event EventHandler<BrokerPlatformEvent>? EventPublished;
    public TaskCompletionSource RequestStarted { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CancellationObserved { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(
        CancellationToken cancellationToken)
    {
        RequestStarted.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException)
        {
            CancellationObserved.TrySetResult();
            throw;
        }
        return [];
    }

    public Task SetAudioSessionVolumeAsync(string sessionId, double volume, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task SetAudioSessionMutedAsync(string sessionId, bool isMuted, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<AudioOutputSummary> GetAudioOutputAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AudioOutputSummary(0.5, false));
    public Task SetAudioOutputVolumeAsync(double volume, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task SetAudioOutputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<IReadOnlyList<AudioDeviceSummary>> GetAudioDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AudioDeviceSummary>>([]);
    public Task<AudioInputSummary> GetAudioInputAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AudioInputSummary(0.5, false));
    public Task SetAudioInputVolumeAsync(double volume, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task SetAudioInputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TestNetwork.Disconnected());
    public Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SavedNetworkProfileSummary>>([]);
    public Task SwitchSavedNetworkProfileAsync(string profileId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AvailableWifiNetworksSummary(WifiScanState.NotScanned, []));
    public Task RequestWifiScanAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ConnectAvailableWifiNetworkAsync(string networkId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WifiRadioSummary(WifiRadioState.On, true));
    public Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<IReadOnlyList<RecentActivitySummary>> GetRecentActivitiesAsync(
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RecentActivitySummary>>([]);
    public Task<BluetoothSummary> GetBluetoothAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new BluetoothSummary(
            BluetoothRadioState.Unavailable, false, BluetoothDiscoveryState.Unavailable, []));
    public Task SetBluetoothRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public void Publish(BrokerPlatformEvent platformEvent) => EventPublished?.Invoke(this, platformEvent);
}

sealed class SplitAudioBackend : IAudioPlatformBrokerBackend
{
    public event EventHandler<BrokerPlatformEvent>? EventPublished;
    public Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AudioSessionSummary>>([]);
    public Task SetAudioSessionVolumeAsync(string sessionId, double volume, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task SetAudioSessionMutedAsync(string sessionId, bool isMuted, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<AudioOutputSummary> GetAudioOutputAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AudioOutputSummary(0.5, false));
    public Task SetAudioOutputVolumeAsync(double volume, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task SetAudioOutputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<IReadOnlyList<AudioDeviceSummary>> GetAudioDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AudioDeviceSummary>>([]);
    public Task<AudioInputSummary> GetAudioInputAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AudioInputSummary(0.5, false));
    public Task SetAudioInputVolumeAsync(double volume, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task SetAudioInputMutedAsync(bool isMuted, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public void Publish(BrokerPlatformEvent platformEvent) => EventPublished?.Invoke(this, platformEvent);
}

sealed class SplitNetworkBackend : INetworkPlatformBrokerBackend
{
    public event EventHandler<BrokerPlatformEvent>? EventPublished;
    public Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TestNetwork.Disconnected());
    public Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SavedNetworkProfileSummary>>([]);
    public Task SwitchSavedNetworkProfileAsync(string profileId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AvailableWifiNetworksSummary(WifiScanState.NotScanned, []));
    public Task RequestWifiScanAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ConnectAvailableWifiNetworkAsync(string networkId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WifiRadioSummary(WifiRadioState.On, true));
    public Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public void Publish(BrokerPlatformEvent platformEvent) => EventPublished?.Invoke(this, platformEvent);
}

static class TestNetwork
{
    public static NetworkStatusSummary Disconnected() => new(
        NetworkConnectivity.None,
        NetworkTransportKind.None,
        NetworkWirelessAvailability.NoAdapter,
        NetworkDetailsAccess.Unavailable,
        NetworkConnectionAttemptState.None,
        null,
        null,
        null,
        null);
}

static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Sequences differ.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        Equal(expected, actual);

    public static void Contains(string value, string source)
    {
        if (!source.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected source to contain '{value}'.");
    }

    public static void DoesNotContain(
        string value, string source, StringComparison comparison)
    {
        if (source.Contains(value, comparison))
            throw new InvalidOperationException($"Expected source not to contain '{value}'.");
    }

    public static void Throws<TException>(Action action, string? code = null)
        where TException : Exception
    {
        try { action(); }
        catch (TException exception)
        {
            if (code is not null && exception is BrokerException broker && broker.Code != code)
                throw new InvalidOperationException($"Expected code '{code}', got '{broker.Code}'.");
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static async Task ThrowsAsync<TException>(Func<Task> action, string? code = null)
        where TException : Exception
    {
        try { await action(); }
        catch (TException exception)
        {
            if (code is not null && exception is BrokerException broker && broker.Code != code)
                throw new InvalidOperationException($"Expected code '{code}', got '{broker.Code}'.");
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static async Task ThrowsAnyAsync(Func<Task> action)
    {
        try { await action(); }
        catch { return; }
        throw new InvalidOperationException("Expected an exception.");
    }
}
