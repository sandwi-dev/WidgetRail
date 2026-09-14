using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using WidgetRail.PlatformBroker;

var allTests = new (string Name, Func<Task> Run)[]
{
    ("Task windows are permission gated and tokens stay broker scoped", TaskWindowBroker),

    ("Admitted launches retain completion across background but revoke and destroy cancel", AdmittedLaunchLifecycle),
    ("Capability vocabulary is closed and versioned", CapabilityVocabularyIsClosed),
    ("Composite backend keeps provider event domains separated", CompositeProviderDomainsAreSeparated),
    ("Missing and explicit consent fail closed", ConsentFailsClosed),
    ("Authenticated channel identity cannot be substituted", IdentityMismatchIsDenied),
    ("Request JSON is strict and bounded", RequestsAreStrictAndBounded),
    ("Capability domains keep broker authority singular", CapabilityDomainAuthorityIsSingular),
    ("Capability domain policies are directly bounded and fail closed", CapabilityDomainPoliciesAreBounded),
    ("Audio operations expose sanitized task-shaped DTOs", AudioOperationsAreSanitized),
    ("Master output capability validates payload lifecycle and events", MasterOutputContracts),
    ("Audio device and input permissions are granular opaque and lifecycle-gated", AudioDeviceInputContracts),
    ("Audio device selection requires its own control permission and current opaque device", AudioDeviceSelectionContracts),
    ("Spatial audio is separately consented validated and lifecycle gated", SpatialAudioContracts),
    ("Network operations switch only opaque saved profiles", NetworkOperationsAreSanitized),
    ("Connection details are separately consented bounded and invalidation-only", NetworkConnectionDetailsContracts),
    ("Available Wi-Fi operations enforce lifecycle payload and event contracts", AvailableWifiContracts),
    ("Recent activity is a sanitized read-only capability", RecentActivityContracts),
    ("Normalized app-library simulator rows preserve one presentation contract",
        NormalizedAppLibrarySimulatorRoundTrip),
    ("App library enumeration is opaque paged consent and lifecycle gated", AppLibraryContracts),
    ("Running app observation is separately consented opaque and stale-safe",
        RunningAppContracts),
    ("Running app registration is explicit durable scoped and forgettable",
        RunningAppRegistrationContracts),
    ("Running app registration retains only the bounded launch window",
        RunningAppRegistrationLaunchWindowIsBounded),
    ("App library cursors and registrations stay bounded across ten thousand items",
        AppLibraryCursorBounds),
    ("App artwork handles are generation-bound lazy and bounded", AppLibraryIconsAreBounded),
    ("Durable app IDs persist and remain authority scoped", AppLibrarySavedIdsAreDurableAndScoped),
    ("Pipe host effects publish only after requested successful app launch", AppLaunchHostEffectIsSuccessBound),
    ("Media session read and transport controls are sanitized granular and lifecycle-gated", MediaSessionContracts),
    ("Media session artwork is canonical bounded and snapshot-limited", MediaSessionArtworkIsBounded),
    ("Exact-port loopback separates visible reads from interactive controls", LoopbackHttpContracts),
    ("Private secrets are write-only and revocation cancels dependent loopback work", PrivateSecretContracts),
    ("Private state is host-granted consentless identity-bound and active-lifecycle safe", PrivateStateHostGrantContracts),
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
    ("Pipe diagnostics preserve bounded Media Sessions stage and code", PipeMediaDiagnosticsAreTyped),
    ("Pipe loopback timeout extends only the declared long operation", PipeLoopbackTimeoutIsOperationSpecific),
    ("Pipe transports a near-limit loopback JSON response", PipeLoopbackNearLimitResponse),
    ("Pipe request cancellation reaches the fixed backend", PipeCancellationIsObserved),
    ("Pipe lifecycle transitions cancel leased reads and controls", PipeLifecycleCancelsLeasedRequests),
    ("Pipe consent revocation cancels a leased control before effects", PipeConsentCancelsLeasedControl),
    ("Pipe events coalesce suspend and unsubscribe", PipeEventsAreBounded),
    ("Pipe consent denial revokes a live subscription", PipeConsentDenialRevokesLiveSubscription),
    ("Pipe consent corruption fails closed", PipeMalformedConsentRevokesLiveSubscription),
    ("Pipe consent deletion fails closed", PipeDeletedConsentRevokesLiveSubscription),
    ("Pipe disposal revokes and completes promptly", PipeDisposalIsBounded),
};

var runningRegistrationOnly =
    args.Contains("--running-registration-only", StringComparer.Ordinal);
var tests = runningRegistrationOnly
    ? allTests.Where(test => test.Name is
        "Capability vocabulary is closed and versioned" or
        "Running app observation is separately consented opaque and stale-safe" or
        "Running app registration is explicit durable scoped and forgettable" or
        "Running app registration retains only the bounded launch window" or
        "App library cursors and registrations stay bounded across ten thousand items")
        .ToArray()
    : allTests;

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

static AppLibraryCursorRequest AppQuery(
    int limit,
    string? cursor = null,
    AppLibraryCursorDirection? direction = null,
    bool refresh = false) =>
    new(new AppLibraryQuery(), cursor, direction, limit, refresh);

static Task CapabilityDomainAuthorityIsSingular()
{
    var rootFields = typeof(PlatformCapabilityBroker).GetFields(
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.DeclaredOnly);
    Assert.True(rootFields.Any(field => field.Name == "_requestLeases"));
    Assert.True(rootFields.Any(field => field.Name == "_subscriptions"));
    Assert.True(rootFields.Any(field => field.Name == "_dashboardGestureAuthorities"));
    Assert.True(rootFields.Any(field => field.Name == "_eventSequence"));
    Assert.Equal(8, rootFields.Count(field =>
        field.FieldType.Name.EndsWith("CapabilityDomain", StringComparison.Ordinal)));

    Type[] domainTypes =
    [
        typeof(DisplayProfilesCapabilityDomain),
        typeof(PowerCapabilityDomain),
        typeof(AudioCapabilityDomain),
        typeof(NetworkCapabilityDomain),
        typeof(AppLibraryCapabilityDomain),
        typeof(MediaCapabilityDomain),
        typeof(PrivateSecretCapabilityDomain),
        typeof(PrivateStateCapabilityDomain),
    ];
    foreach (var domainType in domainTypes)
    {
        var fields = domainType.GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly);
        Assert.True(fields.All(field =>
                field.FieldType != typeof(ConsentStore) &&
                field.FieldType != typeof(BrokerLifecycleState) &&
                field.FieldType != typeof(BrokerEventSubscription) &&
                !field.FieldType.Name.Contains("RequestLease", StringComparison.Ordinal)),
            $"{domainType.Name} acquired broker authorization or lifecycle authority.");
    }

    Assert.Equal(BrokerCapabilityDomain.Power,
        BrokerCapabilityDomains.Resolve(PlatformCapabilities.PowerControlV1));
    Assert.Equal(BrokerCapabilityDomain.Displays,
        BrokerCapabilityDomains.Resolve(PlatformCapabilities.DisplaysReadV1));
    Assert.Equal(BrokerCapabilityDomain.Displays,
        BrokerCapabilityDomains.Resolve(PlatformCapabilities.DisplaysControlV1));
    Assert.Equal(BrokerCapabilityDomain.Audio,
        BrokerCapabilityDomains.Resolve(PlatformCapabilities.AudioSessionsReadV1));
    Assert.Equal(BrokerCapabilityDomain.Network,
        BrokerCapabilityDomains.Resolve(PlatformCapabilities.NetworkBluetoothReadV1));
    Assert.Equal(BrokerCapabilityDomain.AppLibrary,
        BrokerCapabilityDomains.Resolve(PlatformCapabilities.AppLibraryReadV1));
    Assert.Equal(BrokerCapabilityDomain.Media,
        BrokerCapabilityDomains.Resolve(PlatformCapabilities.MediaSessionsReadV1));
    Assert.Equal(BrokerCapabilityDomain.Loopback,
        BrokerCapabilityDomains.Resolve("network.loopback:13091"));
    Assert.Equal(BrokerCapabilityDomain.PrivateSecrets,
        BrokerCapabilityDomains.Resolve(PlatformCapabilities.PrivateSecretsV1));
    Assert.Equal(BrokerCapabilityDomain.PrivateState,
        BrokerCapabilityDomains.Resolve(PlatformCapabilities.PrivateStateV1));
    return Task.CompletedTask;
}

static async Task TaskWindowBroker()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var consent = new ConsentStore(temp.Path);
    var capabilities = new[] { PlatformCapabilities.TaskWindowsReadV1,
        PlatformCapabilities.TaskWindowsSwitchV1, PlatformCapabilities.TaskWindowsCloseV1 };
    foreach (var cap in capabilities)
        await consent.SetDecisionAsync(identity, cap, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend
    { TaskWindows = [new("native-target", "Editor", "Document", false) { PreviewTarget = new("1234", 42, "12345678", "EditorClass") }] };
    string? switched = null;
    string? closed = null;
    backend.TaskWindowSwitch = (id, token) => { switched = id; return Task.CompletedTask; };
    backend.TaskWindowClose = (id, token) => { closed = id; return Task.CompletedTask; };
    await using var broker = new PlatformCapabilityBroker(identity, capabilities, consent, backend);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    long sequence = 0;
    Task<JsonElement> Send(PlatformCapabilityBroker target, string cap, string op, object payload) =>
        target.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence, identity, cap, op,
            JsonSerializer.SerializeToElement(payload, BrokerJson.StrictOptions)));
    var list = (await Send(broker, capabilities[0], PlatformCapabilities.TaskWindowsList, new {}))
        .Deserialize<TaskWindowSummary[]>(BrokerJson.StrictOptions)!;
    Assert.True(list[0].WindowId.StartsWith("window-", StringComparison.Ordinal));
    Assert.True(list[0].WindowId != "native-target");
    Assert.True(list[0].PreviewTarget is null);
    Assert.True(WindowPreviewRegistry.Resolve(identity, list[0].WindowId)?.Handle == "1234");
    Assert.True(WindowPreviewRegistry.Resolve(identity with { InstanceId = "another.instance" }, list[0].WindowId) is null);
    await Send(broker, capabilities[1], PlatformCapabilities.TaskWindowsSwitch, new TaskWindowRequest(list[0].WindowId));
    Assert.Equal("native-target", switched);
    await using var other = new PlatformCapabilityBroker(identity, capabilities, consent, backend);
    other.SetLifecycle(BrokerLifecycleState.Interactive);
    await Assert.ThrowsAsync<BrokerException>(() =>
        Send(other, capabilities[2], PlatformCapabilities.TaskWindowsClose, new TaskWindowRequest(list[0].WindowId)));
    Assert.True(closed is null);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    await Assert.ThrowsAsync<BrokerException>(() =>
        Send(broker, capabilities[2], PlatformCapabilities.TaskWindowsClose, new TaskWindowRequest(list[0].WindowId)));
    Assert.True(closed is null);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    backend.TaskWindows = [];
    await Send(broker, capabilities[0], PlatformCapabilities.TaskWindowsList, new {});
    Assert.True(WindowPreviewRegistry.Resolve(identity, list[0].WindowId) is null);
    await Assert.ThrowsAsync<BrokerException>(() =>
        Send(broker, capabilities[2], PlatformCapabilities.TaskWindowsClose, new TaskWindowRequest(list[0].WindowId)));
    Assert.True(closed is null);
}

static async Task CapabilityDomainPoliciesAreBounded()
{
    Assert.Throws<BrokerException>(
        () => BrokerCapabilityDomains.Resolve("system.unknown.v1"),
        "unsupported_operation");
    Assert.Throws<BrokerException>(
        () => AudioCapabilityDomain.ValidateSessions(null),
        "invalid_backend_data");
    await Assert.ThrowsAsync<BrokerException>(
        () => new AudioCapabilityDomain(AudioBackend()).ExecuteAsync(
            "audio.unknown", BrokerJson.ToElement(new { }), CancellationToken.None),
        "unsupported_operation");

    var tooManyBluetoothDevices = Enumerable.Range(0, BrokerJson.MaximumArrayItems + 1)
        .Select(index => new BluetoothDeviceSummary(
            $"device-{index}", $"Device {index}", false, false, true))
        .ToArray();
    Assert.Throws<BrokerException>(
        () => NetworkCapabilityDomain.ValidateBluetooth(new BluetoothSummary(
            BluetoothRadioState.On,
            true,
            BluetoothDiscoveryState.Ready,
            tooManyBluetoothDevices)),
        "invalid_backend_data");

    var tooManyApps = Enumerable.Range(
            0, PlatformCapabilityBroker.MaximumAppLibraryPageSize + 1)
        .Select(index => new AppLibraryBackendItemSummary(
            $"provider-{index}", $"stable-{index}", $"App {index}",
            AppLibraryKind.Application))
        .ToArray();
    Assert.Throws<BrokerException>(
        () => AppLibraryCapabilityDomain.ValidatePage(
            new AppLibraryBackendCursorPage(
                tooManyApps, null, null, "revision"),
            PlatformCapabilityBroker.MaximumAppLibraryPageSize),
        "invalid_backend_data");
    var boundedSources = Enumerable.Range(0, 16)
        .Select(index => new AppLibrarySourceSummary(
            $"source-{index}", $"Source {index}", AppLibrarySourceHealth.Healthy,
            index, "healthy"))
        .ToArray();
    var sourcePage = AppLibraryCapabilityDomain.ValidatePage(
        new AppLibraryBackendCursorPage([], null, null, "revision")
        {
            Sources = boundedSources,
        }, PlatformCapabilityBroker.MaximumAppLibraryPageSize);
    Assert.Equal(16, sourcePage.Sources.Count);
    Assert.Throws<BrokerException>(() => AppLibraryCapabilityDomain.ValidatePage(
        sourcePage with
        {
            Sources = [.. boundedSources,
                new("source-overflow", "Overflow", AppLibrarySourceHealth.Healthy,
                    1, "healthy")],
        }, PlatformCapabilityBroker.MaximumAppLibraryPageSize), "invalid_backend_data");
    Assert.Throws<BrokerException>(() => AppLibraryCapabilityDomain.ValidatePage(
        sourcePage with
        {
            Sources = [new("source-one", "Source", AppLibrarySourceHealth.Degraded,
                1, "unsafe status")],
        }, PlatformCapabilityBroker.MaximumAppLibraryPageSize), "invalid_backend_data");

    var tooManyMediaSessions = Enumerable.Range(0, 33)
        .Select(index => new MediaSessionSummary(
            $"session-{index}", "Player", "Track", "Artist",
            MediaPlaybackStatus.Paused, 0, 1, 0, 1, false,
            true, true, true, true, true))
        .ToArray();
    Assert.Throws<BrokerException>(
        () => MediaCapabilityDomain.ValidateMediaSessions(
            tooManyMediaSessions),
        "invalid_backend_data");

    Assert.Throws<BrokerException>(() => LoopbackCapabilityPolicy.ValidateRequest(
        new LoopbackJsonRequest(
            "http://127.0.0.1/escape", [], null, null, 1_000),
        isPost: false), "invalid_payload");
    Assert.Throws<BrokerException>(
        () => PrivateSecretCapabilityDomain.ValidateSlot(
            new string('a', CommunityPlatformLimits.MaximumPrivateSecretSlotCharacters + 1)),
        "invalid_payload");
    Assert.Throws<BrokerException>(
        () => PrivateStateCapabilityDomain.ValidateSnapshot(
            new PrivateStateSnapshotSummary(false, "e30=", 0)),
        "invalid_backend_data");
}

static async Task AppLibraryIconsAreBounded()
{
    const string png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
        "AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var consent = new ConsentStore(temp.Path);
    await consent.SetDecisionAsync(
        identity, PlatformCapabilities.AppLibraryReadV1, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.AppLibrarySources =
    [
        new AppLibrarySourceSummary(
            "source-0123456789abcdef01234567", "Windows", AppLibrarySourceHealth.Healthy,
            4, "healthy"),
        new AppLibrarySourceSummary(
            "source-89abcdef0123456789abcdef", "Steam", AppLibrarySourceHealth.Degraded,
            7, "source_degraded"),
    ];
    backend.SetAppLibraryBackend(Enumerable.Range(0, 33).Select(index =>
        new AppLibraryBackendItemSummary(
            $"provider-{index}", $"stable-{index}", $"App {index}",
            AppLibraryKind.Application, "artwork-a")));
    backend.AppLibraryIconHandler = (_, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AppLibraryIconSummary(png));
    };
    var artwork = new AppLibraryArtworkRegistry();
    var artworkSession = artwork.BeginSession(identity, backend);
    await using var broker = new PlatformCapabilityBroker(
        identity, [PlatformCapabilities.AppLibraryReadV1], consent, backend,
        null, AppLibrarySavedIdIssuer.Shared, artworkSession);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var listPayload = await broker.ExecuteAsync(new BrokerRequestEnvelope(
        BrokerJson.ProtocolVersion, 1, identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        JsonSerializer.SerializeToElement(AppQuery(64),
            BrokerJson.StrictOptions)));
    var page = listPayload.Deserialize<AppLibraryCursorPageSummary>(BrokerJson.StrictOptions)!;
    Assert.Equal(33, page.Items.Count);
    Assert.Equal(2, page.Sources.Count);
    Assert.Equal("Windows", page.Sources[0].DisplayName);
    Assert.Equal(AppLibrarySourceHealth.Degraded, page.Sources[1].Health);
    Assert.Equal("source_degraded", page.Sources[1].StatusCode);
    Assert.True(page.Sources.All(source => source.SourceId.StartsWith(
        "source-", StringComparison.Ordinal)));
    Assert.True(page.Items.All(item =>
        AppLibraryArtworkRegistry.IsHandle(
            item.Presentation.Artwork.Items.Single().Handle)));
    Assert.Equal(33, page.Items.Select(item =>
        item.Presentation.Artwork.Items.Single().Handle).Distinct().Count());
    Assert.Equal(0, backend.AppLibraryIconCalls);

    var resolvePayload = await broker.ExecuteAsync(new BrokerRequestEnvelope(
        BrokerJson.ProtocolVersion, 2, identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryResolveSaved,
        JsonSerializer.SerializeToElement(
            new ResolveSavedAppLibraryItemsRequest(
                page.Items.Select(item => item.SavedId).ToArray()),
            BrokerJson.StrictOptions)));
    var resolved = resolvePayload.Deserialize<ResolveSavedAppLibraryItemsSummary>(
        BrokerJson.StrictOptions)!;
    Assert.Equal(33, resolved.Items.Count);
    Assert.True(resolved.Items.All(item => item.Presentation.Artwork.Items.Count == 1));
    Assert.Equal(0, backend.AppLibraryIconCalls);

    var firstHandle = resolved.Items[0].Presentation.Artwork.Items.Single().Handle;
    var neighborHandle = resolved.Items[1].Presentation.Artwork.Items.Single().Handle;
    Assert.Equal(png, await artwork.ResolveAsync(identity, firstHandle, CancellationToken.None));
    Assert.Equal(1, backend.AppLibraryIconCalls);

    Assert.Equal(null, await artwork.ResolveAsync(
        new BrokerWidgetIdentity("other.package", "other.publisher", "other.instance"),
        firstHandle, CancellationToken.None));
    Assert.Equal(null, await artwork.ResolveAsync(
        identity, "library.art.00000000000000000000000000000000", CancellationToken.None));
    Assert.Equal(1, backend.AppLibraryIconCalls);

    backend.AppLibraryIconHandler = (_, _) =>
        Task.FromResult(new AppLibraryIconSummary("not-base64"));
    Assert.Equal(null, await artwork.ResolveAsync(
        identity, firstHandle, CancellationToken.None));

    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "provider-0", "stable-0", "App 0",
            AppLibraryKind.Application, "artwork-b"),
        new AppLibraryBackendItemSummary(
            "provider-1", "stable-1", "App 1",
            AppLibraryKind.Application, "artwork-a"),
    ]);
    var rotatedPayload = await broker.ExecuteAsync(new BrokerRequestEnvelope(
        BrokerJson.ProtocolVersion, 3, identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        JsonSerializer.SerializeToElement(
            AppQuery(64, refresh: true), BrokerJson.StrictOptions)));
    var rotated = rotatedPayload.Deserialize<AppLibraryCursorPageSummary>(BrokerJson.StrictOptions)!;
    Assert.True(rotated.Items[0].Presentation.Artwork.Items.Single().Handle != firstHandle,
        "Changed trusted artwork revision reused its decoded-cache handle.");
    Assert.Equal(neighborHandle,
        rotated.Items[1].Presentation.Artwork.Items.Single().Handle);
    Assert.Equal(null, await artwork.ResolveAsync(identity, firstHandle, CancellationToken.None));

    for (var index = 0; index < 10_000; index++)
        Assert.Equal(null, await artwork.ResolveAsync(
            identity, $"library.art.{index:x32}", CancellationToken.None));
    Assert.Equal(2, backend.AppLibraryIconCalls);
}

static Task CapabilityVocabularyIsClosed()
{
    Assert.Equal(39, PlatformCapabilities.All.Count);
    foreach (var capability in PlatformCapabilities.All)
    {
        Assert.True(capability.Id.EndsWith($".v{capability.Version}", StringComparison.Ordinal));
        Assert.True(capability.Version == 1);
        // Preview consent authorizes host rendering, without giving the worker a pixel-reading operation.
        Assert.True(capability.Id == PlatformCapabilities.TaskWindowsPreviewV1
            ? capability.Operations.Count == 0 : capability.Operations.Count != 0);
    }
    Assert.True(!PlatformCapabilities.TryGet("system.full-access.v1", out _));
    Assert.True(!PlatformCapabilities.TryGet("system.audio.sessions.read.v2", out _));
    Assert.True(!PlatformCapabilities.TryGet("system.activity.recent.activate.v1", out _));
    Assert.True(PlatformCapabilities.TryGet("network.loopback:13091", out var loopback));
    Assert.Equal(BrokerCapabilityKind.Read,
        loopback.KindForOperation(PlatformCapabilities.LoopbackHttpGetJson));
    Assert.Equal(BrokerCapabilityKind.Control,
        loopback.KindForOperation(PlatformCapabilities.LoopbackHttpPostJson));
    Assert.True(loopback.AllowsDashboardGestureForOperation(
        PlatformCapabilities.LoopbackHttpPostJson));
    Assert.True(!loopback.AllowsDashboardGestureForOperation(
        PlatformCapabilities.LoopbackHttpGetJson));
    Assert.True(!PlatformCapabilities.TryGet("network.loopback:0", out _));
    Assert.True(!PlatformCapabilities.TryGet("network.loopback:80", out _));
    Assert.True(!PlatformCapabilities.TryGet("network.loopback:013091", out _));
    Assert.True(!PlatformCapabilities.TryGet("network.loopback:13091/path", out _));
    Assert.True(PlatformCapabilities.TryGet(
        PlatformCapabilities.AppLibraryLaunchV1, out var appLaunch));
    Assert.Equal(BrokerCapabilityKind.Control, appLaunch.Kind);
    Assert.True(!appLaunch.AllowsDashboardGesture);
    Assert.True(PlatformCapabilities.TryGet(
        PlatformCapabilities.MediaSessionsControlV1, out var mediaControl));
    Assert.True(mediaControl.AllowsDashboardGesture);
    Assert.True(PlatformCapabilities.TryGet(
        PlatformCapabilities.AudioOutputControlV1, out var audioOutputControl));
    Assert.True(audioOutputControl.AllowsDashboardGestureForOperation(
        PlatformCapabilities.AudioOutputSetVolume));
    Assert.True(audioOutputControl.AllowsDashboardGestureForOperation(
        PlatformCapabilities.AudioOutputSetMuted));
    Assert.True(PlatformCapabilities.All
        .Where(capability => capability.Kind == BrokerCapabilityKind.Control &&
            capability.Id != PlatformCapabilities.AudioOutputControlV1 &&
            capability.Id != PlatformCapabilities.MediaSessionsControlV1)
        .All(capability => !capability.AllowsDashboardGesture));
    Assert.True(PlatformCapabilities.All
        .Where(capability => capability.Id != PlatformCapabilities.AppLibraryLaunchV1 &&
            capability.Id != PlatformCapabilities.TaskWindowsSwitchV1 &&
            capability.Id != PlatformCapabilities.DisplaysControlV1)
        .All(capability => capability.InFlightContinuationOperations is null ||
            capability.InFlightContinuationOperations.Count == 0));
    Assert.True(PlatformCapabilities.All.Single(capability => capability.Id == PlatformCapabilities.AppLibraryLaunchV1)
        .InFlightContinuationOperations!.SetEquals([PlatformCapabilities.AppLibraryLaunch, PlatformCapabilities.AppLibraryLaunchObserved]));
    Assert.True(PlatformCapabilities.All.Single(capability => capability.Id == PlatformCapabilities.TaskWindowsSwitchV1)
        .InFlightContinuationOperations!.SetEquals([PlatformCapabilities.TaskWindowsSwitch]));
    Assert.True(PlatformCapabilities.All.Single(capability => capability.Id == PlatformCapabilities.DisplaysControlV1)
        .InFlightContinuationOperations!.SetEquals([PlatformCapabilities.DisplayProfilesApply,
            PlatformCapabilities.DisplayProfilesKeep, PlatformCapabilities.DisplayProfilesRevert]));
    var defaultControl = new BrokerCapabilityDefinition(
        "test.future.control.v1",
        1,
        BrokerCapabilityKind.Control,
        new HashSet<string>(["future.control"], StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));
    Assert.True(!defaultControl.AllowsDashboardGesture);
    Assert.True(PlatformCapabilities.TryGet(
        PlatformCapabilities.PrivateStateV1, out var privateState));
    Assert.Equal(BrokerCapabilityAccessPolicy.HostGranted, privateState.AccessPolicy);
    Assert.True(privateState.AllowsBackground);
    Assert.True(!PlatformCapabilities.IsManifestDeclarable(
        PlatformCapabilities.PrivateStateV1));
    return Task.CompletedTask;
}

static async Task PrivateStateHostGrantContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var consent = new ConsentStore(temp.Path);
    var backend = new SimulatedPlatformBrokerBackend();
    await Assert.ThrowsAsync<BrokerException>(() => consent.SetDecisionAsync(
        identity, PlatformCapabilities.PrivateStateV1, ConsentDecision.Grant),
        "unsupported_capability");
    Assert.Throws<BrokerException>(() => _ = new PlatformCapabilityBroker(
        identity, [PlatformCapabilities.PrivateStateV1], consent, backend),
        "invalid_declaration");
    Assert.Throws<BrokerException>(() => _ = new PlatformCapabilityBroker(
        identity, [], consent, backend,
        [PlatformCapabilities.AudioSessionsReadV1]), "invalid_declaration");

    await using var broker = new PlatformCapabilityBroker(
        identity, [], consent, backend, [PlatformCapabilities.PrivateStateV1]);
    await Assert.ThrowsAsync<BrokerException>(() => broker.ExecuteAsync(new BrokerRequestEnvelope(
        BrokerJson.ProtocolVersion, 1, identity,
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList,
        JsonSerializer.SerializeToElement(new { }))), "capability_not_declared");

    async Task<PrivateStateSnapshotSummary> ReadAsync(long requestId)
    {
        var payload = await broker.ExecuteAsync(new BrokerRequestEnvelope(
            BrokerJson.ProtocolVersion, requestId, identity,
            PlatformCapabilities.PrivateStateV1,
            PlatformCapabilities.PrivateStateRead,
            JsonSerializer.SerializeToElement(new { })));
        return payload.Deserialize<PrivateStateSnapshotSummary>(BrokerJson.StrictOptions) ??
            throw new InvalidOperationException("State response was null.");
    }

    await Assert.ThrowsAsync<BrokerException>(() => ReadAsync(2), "lifecycle_denied");
    var canonical = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"value\":1}"));
    Task<JsonElement> WriteAsync(long requestId) => broker.ExecuteAsync(
        new BrokerRequestEnvelope(
            BrokerJson.ProtocolVersion, requestId, identity,
            PlatformCapabilities.PrivateStateV1,
            PlatformCapabilities.PrivateStateWrite,
            JsonSerializer.SerializeToElement(new WritePrivateStateRequest(canonical, 0),
                BrokerJson.StrictOptions)));
    Task<JsonElement> ClearAsync(long requestId) => broker.ExecuteAsync(
        new BrokerRequestEnvelope(
            BrokerJson.ProtocolVersion, requestId, identity,
            PlatformCapabilities.PrivateStateV1,
            PlatformCapabilities.PrivateStateClear,
            JsonSerializer.SerializeToElement(new ClearPrivateStateRequest(0),
                BrokerJson.StrictOptions)));
    await Assert.ThrowsAsync<BrokerException>(() => WriteAsync(3), "lifecycle_denied");
    await Assert.ThrowsAsync<BrokerException>(() => ClearAsync(4), "lifecycle_denied");

    broker.SetLifecycle(BrokerLifecycleState.Background);
    Assert.Equal(0L, (await ReadAsync(5)).Revision);
    var mutationPayload = await WriteAsync(6);
    Assert.Equal(1L, mutationPayload.Deserialize<PrivateStateMutationSummary>(
        BrokerJson.StrictOptions)!.Revision);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    Assert.Equal(1L, (await ReadAsync(7)).Revision);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    Assert.Equal(1L, (await ReadAsync(8)).Revision);
    broker.SetLifecycle(BrokerLifecycleState.Destroying);
    await Assert.ThrowsAsync<BrokerException>(() => ReadAsync(9), "lifecycle_denied");
}

static async Task LoopbackHttpContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var capability = "network.loopback:13091";
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend
    {
        LoopbackHandler = (_, _, _, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new LoopbackJsonResponse(
                200,
                "{\r\n  \"state\": true\r\n}",
                []));
        },
    };
    await using var broker = new PlatformCapabilityBroker(
        identity, [capability], store, backend);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var get = await broker.HandleAsync(Request(identity, capability,
        PlatformCapabilities.LoopbackHttpGetJson, new
        {
            path = "/api/v1/state?compact=true",
            headers = Array.Empty<object>(),
            bearerSecretSlot = (string?)null,
            jsonBody = (string?)null,
            timeoutMilliseconds = 10_000,
        }));
    Assert.True(get.Succeeded);
    Assert.Equal("{\"state\":true}",
        get.Payload!.Value.GetProperty("jsonBody").GetString());
    Assert.Equal(13091, backend.LastLoopbackPort);
    Assert.True(!backend.LastLoopbackWasPost);

    var visiblePost = await broker.HandleAsync(Request(identity, capability,
        PlatformCapabilities.LoopbackHttpPostJson, new
        {
            path = "/api/v1/player/next",
            headers = Array.Empty<object>(),
            bearerSecretSlot = (string?)null,
            jsonBody = "{}",
            timeoutMilliseconds = 10_000,
        }));
    Assert.Equal("lifecycle_denied", visiblePost.ErrorCode);
    Assert.Equal(1, backend.LoopbackCalls);

    broker.GrantDashboardGestureAuthority(capability,
        PlatformCapabilities.LoopbackHttpPostJson, 11, 12, TimeSpan.FromSeconds(1));
    var dashboardPost = await broker.HandleAsync(GestureRequest(identity, capability,
        PlatformCapabilities.LoopbackHttpPostJson, new
        {
            path = "/api/v1/player/next",
            headers = Array.Empty<object>(),
            bearerSecretSlot = (string?)null,
            jsonBody = "{}",
            timeoutMilliseconds = 10_000,
        }, 11, 12));
    Assert.True(dashboardPost.Succeeded);

    var authorityHeader = await broker.HandleAsync(Request(identity, capability,
        PlatformCapabilities.LoopbackHttpGetJson, new
        {
            path = "/",
            headers = new[] { new { name = "Authorization", value = "Bearer stolen" } },
            bearerSecretSlot = (string?)null,
            jsonBody = (string?)null,
            timeoutMilliseconds = 10_000,
        }));
    Assert.Equal("invalid_payload", authorityHeader.ErrorCode);
    var absolutePath = await broker.HandleAsync(Request(identity, capability,
        PlatformCapabilities.LoopbackHttpGetJson, new
        {
            path = "//example.com/steal",
            headers = Array.Empty<object>(),
            bearerSecretSlot = (string?)null,
            jsonBody = (string?)null,
            timeoutMilliseconds = 10_000,
        }));
    Assert.Equal("invalid_payload", absolutePath.ErrorCode);
    var invalidBearerInvalidation = await broker.HandleAsync(Request(identity, capability,
        PlatformCapabilities.LoopbackHttpGetJson, new
        {
            path = "/",
            headers = Array.Empty<object>(),
            bearerSecretSlot = (string?)null,
            jsonBody = (string?)null,
            timeoutMilliseconds = 10_000,
            invalidateBearerSecretOnUnauthorized = true,
        }));
    Assert.Equal("invalid_payload", invalidBearerInvalidation.ErrorCode);

    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var longPairing = await broker.HandleAsync(Request(identity, capability,
        PlatformCapabilities.LoopbackHttpPostJson, new
        {
            path = "/auth/request",
            headers = Array.Empty<object>(),
            bearerSecretSlot = (string?)null,
            jsonBody = "{\"app\":\"test\"}",
            timeoutMilliseconds = 40_000,
        }));
    Assert.True(longPairing.Succeeded);
    var excessiveTimeout = await broker.HandleAsync(Request(identity, capability,
        PlatformCapabilities.LoopbackHttpPostJson, new
        {
            path = "/auth/request",
            headers = Array.Empty<object>(),
            bearerSecretSlot = (string?)null,
            jsonBody = "{}",
            timeoutMilliseconds = 40_001,
        }));
    Assert.Equal("invalid_payload", excessiveTimeout.ErrorCode);
}

static async Task PrivateSecretContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var loopbackCapability = "network.loopback:13091";
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, loopbackCapability, ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.PrivateSecretsV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    await using var broker = new PlatformCapabilityBroker(identity,
        [loopbackCapability, PlatformCapabilities.PrivateSecretsV1], store, backend);

    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var visibleSave = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.PrivateSecretsV1,
        PlatformCapabilities.PrivateSecretSave,
        new { slot = "ytm.token", secret = "private-value" }));
    Assert.Equal("lifecycle_denied", visibleSave.ErrorCode);
    var absent = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.PrivateSecretsV1,
        PlatformCapabilities.PrivateSecretMetadata,
        new { slot = "ytm.token" }));
    Assert.True(absent.Succeeded);
    Assert.True(absent.Payload!.Value.GetProperty("exists").GetBoolean() == false);

    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var saved = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.PrivateSecretsV1,
        PlatformCapabilities.PrivateSecretSave,
        new { slot = "ytm.token", secret = "private-value" }));
    Assert.True(saved.Succeeded);
    Assert.Equal(1, backend.PrivateSecretSaveCalls);
    Assert.True(!PlatformCapabilities.TryGet(
        PlatformCapabilities.PrivateSecretsV1, out var secrets) ||
        !secrets.Operations.Contains("private-secret.read"));

    broker.SetLifecycle(BrokerLifecycleState.Visible);
    backend.LoopbackHandler = (_, _, _, _, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new LoopbackJsonResponse(401, "{}", []));
    };
    broker.GrantDashboardGestureAuthority(
        loopbackCapability,
        PlatformCapabilities.LoopbackHttpPostJson,
        inputSequence: 31,
        snapshotSequence: 32,
        TimeSpan.FromSeconds(1));
    var rejected = await broker.HandleAsync(GestureRequest(
        identity,
        loopbackCapability,
        PlatformCapabilities.LoopbackHttpPostJson,
        new
        {
            path = "/track/next",
            headers = Array.Empty<object>(),
            bearerSecretSlot = "ytm.token",
            jsonBody = "{}",
            timeoutMilliseconds = 10_000,
            invalidateBearerSecretOnUnauthorized = true,
        },
        inputSequence: 31,
        snapshotSequence: 32));
    Assert.True(rejected.Succeeded, rejected.ErrorCode);
    Assert.Equal(401, rejected.Payload!.Value.GetProperty("statusCode").GetInt32());
    Assert.Equal(1, backend.PrivateSecretDeleteCalls);
    var invalidated = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.PrivateSecretsV1,
        PlatformCapabilities.PrivateSecretMetadata,
        new { slot = "ytm.token" }));
    Assert.True(invalidated.Succeeded);
    Assert.True(!invalidated.Payload!.Value.GetProperty("exists").GetBoolean());

    // Restore the independent revocation fixture through the normal
    // Interactive control path; the preceding scenario intentionally removed
    // the durable slot while Visible.
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var restored = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.PrivateSecretsV1,
        PlatformCapabilities.PrivateSecretSave,
        new { slot = "ytm.token", secret = "replacement-value" }));
    Assert.True(restored.Succeeded);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    backend.LoopbackHandler = async (_, _, _, _, cancellationToken) =>
    {
        started.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException)
        {
            canceled.TrySetResult();
            throw;
        }
        throw new InvalidOperationException();
    };
    var pending = broker.HandleAsync(Request(identity, loopbackCapability,
        PlatformCapabilities.LoopbackHttpGetJson, new
        {
            path = "/api/v1/state",
            headers = Array.Empty<object>(),
            bearerSecretSlot = "ytm.token",
            jsonBody = (string?)null,
            timeoutMilliseconds = 10_000,
        }));
    await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await store.SetDecisionAsync(identity, PlatformCapabilities.PrivateSecretsV1,
        ConsentDecision.Deny);
    await broker.RefreshConsentAsync();
    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    var revoked = await pending;
    Assert.Equal("capability_revoked", revoked.ErrorCode);

    var deniedUse = await broker.HandleAsync(Request(identity, loopbackCapability,
        PlatformCapabilities.LoopbackHttpGetJson, new
        {
            path = "/api/v1/state",
            headers = Array.Empty<object>(),
            bearerSecretSlot = "ytm.token",
            jsonBody = (string?)null,
            timeoutMilliseconds = 10_000,
        }));
    Assert.Equal("permission_denied", deniedUse.ErrorCode);
}

static async Task PipeLoopbackTimeoutIsOperationSpecific()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var capability = "network.loopback:13091";
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend
    {
        LoopbackHandler = async (_, _, _, _, cancellationToken) =>
        {
            await Task.Delay(200, cancellationToken);
            return new LoopbackJsonResponse(200, "{}", []);
        },
    };
    var options = new BrokerPipeTransportOptions
    {
        MaximumFrameBytes = 64 * 1024,
        AcceptTimeout = TimeSpan.FromSeconds(2),
        HandshakeTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromMilliseconds(50),
        MaximumInFlightRequests = 4,
        MaximumSubscriptions = 2,
    };
    var pipeName = $"wrail-broker-community-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName, identity, [capability], store, backend,
        options, new string('T', 64));
    var serverTask = server.RunAsync();
    await using var client = new BrokerPipeClient(
        pipeName, identity, server.ChannelNonce, options);
    await client.ConnectAsync();
    server.SetLifecycle(BrokerLifecycleState.Visible);
    var response = await client.RequestAsync(capability,
        PlatformCapabilities.LoopbackHttpGetJson, new
        {
            path = "/slow-but-bounded",
            headers = Array.Empty<object>(),
            bearerSecretSlot = (string?)null,
            jsonBody = (string?)null,
            timeoutMilliseconds = 500,
        });
    Assert.True(response.Succeeded, response.ErrorCode);
    await client.DisposeAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
}

static async Task PipeLoopbackNearLimitResponse()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var capability = "network.loopback:13091";
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
    var padding = new string(
        'x', CommunityPlatformLimits.MaximumLoopbackResponseBodyUtf8Bytes - 32);
    var body = $"{{\"data\":\"{padding}\"}}";
    Assert.True(Encoding.UTF8.GetByteCount(body) >
        CommunityPlatformLimits.MaximumLoopbackResponseBodyUtf8Bytes - 64);
    var backend = new SimulatedPlatformBrokerBackend
    {
        LoopbackHandler = (_, _, _, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new LoopbackJsonResponse(200, body, []));
        },
    };
    var options = new BrokerPipeTransportOptions
    {
        MaximumFrameBytes = 256 * 1024,
        AcceptTimeout = TimeSpan.FromSeconds(2),
        HandshakeTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromSeconds(2),
        MaximumInFlightRequests = 4,
        MaximumSubscriptions = 2,
    };
    var pipeName = $"wrail-broker-near-limit-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName, identity, [capability], store, backend,
        options, new string('L', 64));
    var serverTask = server.RunAsync();
    await using var client = new BrokerPipeClient(
        pipeName, identity, server.ChannelNonce, options);
    await client.ConnectAsync();
    server.SetLifecycle(BrokerLifecycleState.Visible);
    var response = await client.RequestAsync(capability,
        PlatformCapabilities.LoopbackHttpGetJson, new
        {
            path = "/large-state",
            headers = Array.Empty<object>(),
            bearerSecretSlot = (string?)null,
            jsonBody = (string?)null,
            timeoutMilliseconds = 1_000,
        });
    Assert.True(response.Succeeded, response.ErrorCode);
    Assert.Equal(body,
        response.Payload!.Value.GetProperty("jsonBody").GetString());
    await client.DisposeAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
}

static async Task BrokerPipeScopesAreClosed()
{
    BrokerPipeNames.Validate("wrail-broker-test");
    BrokerPipeNames.ValidateAppContainerSid("S-1-15-2-1-2-3-4-5-6-7");

    Assert.Throws<ArgumentException>(() =>
        BrokerPipeNames.Validate(@"LOCAL\nested\pipe"));
    Assert.Throws<ArgumentException>(() => BrokerPipeNames.Validate("wrail-broker\nested"));
    Assert.Throws<ArgumentException>(() => BrokerPipeNames.Validate("wrail-broker\ncontrol"));
    Assert.Throws<ArgumentException>(() =>
        BrokerPipeNames.ValidateAppContainerSid("S-1-5-21-1"));
    Assert.Throws<ArgumentException>(() =>
        BrokerPipeNames.ValidateAppContainerSid("S-1-15-2-1\\other"));

    using var temporary = new TemporaryDirectory();
    await using var isolated = new BrokerPipeServer(
        $"wrail-isolated-unbound-{Guid.NewGuid():N}",
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

static async Task NormalizedAppLibrarySimulatorRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibrary(
    [
        new AppLibraryItemSummary(
            "backend-game", "backend-stable-game",
            new AppLibraryItemPresentation(
                "Normalized Game",
                AppLibraryKind.Game,
                new AppLibrarySourceReference("source-steam", "Steam"),
                new AppLibraryAvailabilitySummary(
                    AppLibraryAvailabilityState.Installed, true, "installed"),
                new AppLibraryArtworkSet([]),
                Metadata: null,
                new AppLibraryCapabilitySet([AppLibraryAction.Launch]),
                ActiveOperation: null)),
        new AppLibraryItemSummary(
            "backend-owned", "backend-stable-owned",
            new AppLibraryItemPresentation(
                "Owned Game",
                AppLibraryKind.Game,
                new AppLibrarySourceReference("source-store", "Store"),
                new AppLibraryAvailabilitySummary(
                    AppLibraryAvailabilityState.Unavailable, false,
                    "owned_not_installed"),
                new AppLibraryArtworkSet([]),
                Metadata: null,
                new AppLibraryCapabilitySet([AppLibraryAction.Install]),
                ActiveOperation: null)),
    ]);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AppLibraryReadV1);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AppLibraryReadV1,
        ConsentDecision.Grant);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var response = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, refresh: true)));
    Assert.True(response.Succeeded,
        $"Normalized simulator page failed: {response.ErrorCode}");
    var page = response.Payload!.Value.Deserialize<AppLibraryCursorPageSummary>(
        BrokerJson.StrictOptions)!;
    Assert.Equal(2, page.Items.Count);
    var installed = page.Items.Single(item =>
        item.Presentation.DisplayName == "Normalized Game");
    Assert.Equal("source-steam", installed.Presentation.Source.SourceId);
    Assert.Equal(AppLibraryAvailabilityState.Installed,
        installed.Presentation.Availability.State);
    Assert.Equal(AppLibraryAction.Launch,
        installed.Presentation.Capabilities.Actions.Single());
    var owned = page.Items.Single(item =>
        item.Presentation.DisplayName == "Owned Game");
    Assert.Equal(AppLibraryAvailabilityState.Unavailable,
        owned.Presentation.Availability.State);
    Assert.Equal("owned_not_installed",
        owned.Presentation.Availability.StatusCode);
    Assert.True(!owned.Presentation.Availability.IsLaunchable);
    Assert.Equal(AppLibraryAction.Install,
        owned.Presentation.Capabilities.Actions.Single());
    Assert.Equal(2, page.Sources.Count);
    Assert.True(page.Sources.All(source =>
        source.AccountState == AppLibrarySourceAccountState.NotApplicable));
}

static async Task AppLibraryContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibraryBackend(Enumerable.Range(0, 70).Select(index =>
        new AppLibraryBackendItemSummary(
            $"app-{index:D3}",
            $"private-stable-{index:D3}",
            $"Launchable {index:D3}",
            index == 0 ? AppLibraryKind.Game : AppLibraryKind.Application)));
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryLaunchV1);

    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var denied = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, refresh: true)));
    Assert.Equal("permission_denied", denied.ErrorCode);

    await store.SetDecisionAsync(identity, PlatformCapabilities.AppLibraryReadV1,
        ConsentDecision.Grant);
    broker.SetLifecycle(BrokerLifecycleState.Background);
    var background = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, refresh: true)));
    Assert.Equal("lifecycle_denied", background.ErrorCode);

    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var first = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, refresh: true)));
    Assert.True(first.Succeeded, $"First app-library page failed: {first.ErrorCode}");
    var firstPayload = first.Payload!.Value;
    Assert.Equal(64, firstPayload.GetProperty("items").GetArrayLength());
    var after = firstPayload.GetProperty("after").GetString()!;
    Assert.True(after.StartsWith("sim.", StringComparison.Ordinal));
    var firstItem = firstPayload.GetProperty("items")[0];
    var publicAppId = firstItem.GetProperty("appId").GetString()!;
    var savedAppId = firstItem.GetProperty("savedId").GetString()!;
    Assert.True(publicAppId.StartsWith("app-", StringComparison.Ordinal));
    Assert.True(savedAppId.StartsWith("saved-", StringComparison.Ordinal));
    Assert.True(publicAppId != "app-000",
        "A provider-global ID escaped the widget-scoped broker projection.");
    var firstPresentation = firstItem.GetProperty("presentation");
    Assert.Equal(AppLibraryItemSummary.CurrentPresentationVersion,
        firstItem.GetProperty("presentationVersion").GetInt32());
    Assert.Equal("Launchable 000", firstPresentation.GetProperty("displayName").GetString());
    Assert.Equal("game", firstPresentation.GetProperty("kind").GetString());
    Assert.Equal("Windows",
        firstPresentation.GetProperty("source").GetProperty("displayName").GetString());
    Assert.Equal("installed",
        firstPresentation.GetProperty("availability").GetProperty("state").GetString());
    Assert.True(firstPresentation.GetProperty("availability")
        .GetProperty("isLaunchable").GetBoolean());
    Assert.Equal("launch", firstPresentation.GetProperty("capabilities")
        .GetProperty("actions")[0].GetString());
    Assert.Equal(JsonValueKind.Null, firstPresentation.GetProperty("metadata").ValueKind);
    Assert.Equal(JsonValueKind.Null,
        firstPresentation.GetProperty("activeOperation").ValueKind);
    var json = firstPayload.GetRawText();
    Assert.True(!json.Contains(".lnk", StringComparison.OrdinalIgnoreCase));
    Assert.True(!json.Contains("C:\\\\", StringComparison.OrdinalIgnoreCase));
    Assert.True(!json.Contains("aumid", StringComparison.OrdinalIgnoreCase));
    Assert.True(!json.Contains("private-stable", StringComparison.OrdinalIgnoreCase));
    Assert.Equal(1, backend.AppLibraryRefreshCalls);

    var second = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, after, AppLibraryCursorDirection.After)));
    Assert.True(second.Succeeded);
    Assert.Equal(6, second.Payload!.Value.GetProperty("items").GetArrayLength());
    Assert.Equal(JsonValueKind.Null, second.Payload.Value.GetProperty("after").ValueKind);
    var before = second.Payload.Value.GetProperty("before").GetString()!;
    var reversed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, before, AppLibraryCursorDirection.Before)));
    Assert.True(reversed.Succeeded);
    Assert.Equal("Launchable 000", reversed.Payload!.Value.GetProperty("items")[0]
        .GetProperty("presentation").GetProperty("displayName").GetString());

    var allSavedIds = firstPayload.GetProperty("items").EnumerateArray()
        .Concat(second.Payload.Value.GetProperty("items").EnumerateArray())
        .Select(item => item.GetProperty("savedId").GetString()!).ToArray();
    Assert.Equal(70, allSavedIds.Length);
    var manyFavorites = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new AppLibraryCursorRequest(new AppLibraryQuery
        {
            FavoriteSavedIds = allSavedIds,
        }, null, null, 64)));
    Assert.True(manyFavorites.Succeeded,
        $"Bounded favorite filter failed: {manyFavorites.ErrorCode}");
    Assert.Equal(64,
        manyFavorites.Payload!.Value.GetProperty("items").GetArrayLength());

    var filteredRequest = new AppLibraryCursorRequest(
        new AppLibraryQuery(Kind: AppLibraryKind.Game,
            Sort: AppLibrarySortOrder.DisplayNameDescending)
        {
            SearchText = "Launchable 000",
            FavoriteSavedIds = [savedAppId],
        }, null, null, 64, Refresh: true);
    var filtered = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList, filteredRequest));
    Assert.True(filtered.Succeeded, $"Filtered app-library page failed: {filtered.ErrorCode}");
    var filteredItems = filtered.Payload!.Value.GetProperty("items");
    Assert.Equal(1, filteredItems.GetArrayLength());
    Assert.Equal(savedAppId, filteredItems[0].GetProperty("savedId").GetString());
    publicAppId = filteredItems[0].GetProperty("appId").GetString()!;
    Assert.Equal("Launchable 000",
        filteredItems[0].GetProperty("presentation").GetProperty("displayName").GetString());

    var noMatch = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        filteredRequest with
        {
            Refresh = false,
            Query = filteredRequest.Query with
            {
                FavoriteSavedIds = [firstPayload.GetProperty("items")[1]
                    .GetProperty("savedId").GetString()!],
            },
        }));
    Assert.True(noMatch.Succeeded, $"Empty favorite result failed: {noMatch.ErrorCode}");
    Assert.Equal(0, noMatch.Payload!.Value.GetProperty("items").GetArrayLength());

    // A source revision change makes an older cursor stale.
    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "changed", "changed-stable", "Changed", AppLibraryKind.Application),
    ]);
    var last = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, after, AppLibraryCursorDirection.After)));
    Assert.Equal("invalid_cursor", last.ErrorCode);
    Assert.Equal(2, backend.AppLibraryRefreshCalls);

    var invalidPage = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new AppLibraryCursorRequest(
            new AppLibraryQuery(), "bad", null, 65)));
    Assert.Equal("invalid_payload", invalidPage.ErrorCode);

    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "duplicate", "stable-one", "First", AppLibraryKind.Application),
        new AppLibraryBackendItemSummary(
            "duplicate", "stable-two", "Second", AppLibraryKind.Application),
    ]);
    var invalidBackend = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, refresh: true)));
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

    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "changed", "changed-stable", "Changed", AppLibraryKind.Application),
    ]);
    var refreshed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, refresh: true)));
    Assert.True(refreshed.Succeeded);
    Assert.Equal("Changed", refreshed.Payload!.Value.GetProperty("items")[0]
        .GetProperty("presentation").GetProperty("displayName").GetString());
    Assert.Equal(4, backend.AppLibraryRefreshCalls);

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
        AppQuery(64, refresh: true)));
    var secondWidgetId = secondWidget.Payload!.Value.GetProperty("items")[0]
        .GetProperty("appId").GetString();
    var firstWidgetId = refreshed.Payload.Value.GetProperty("items")[0]
        .GetProperty("appId").GetString();
    Assert.True(firstWidgetId != secondWidgetId,
        "Opaque app IDs must not correlate two widget broker sessions.");
}

static async Task RunningAppContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibraryBackend([
        new("provider-current", "stable-current", "Visible app",
            AppLibraryKind.Application) { SourceAttribution = "Windows" },
    ]);
    backend.SetRunningAppBackend([
        new("stable-current", "private-process-evidence", "Visible app",
            AppLibraryKind.Application, "Windows")
        {
            ArtworkItem = new("provider-running-icon", "stable-current", "Visible app",
                AppLibraryKind.Application, "running-icon-revision", "Windows") { IsLaunchable = false },
        },
    ]);
    var artwork = new AppLibraryArtworkRegistry();
    await using var broker = new PlatformCapabilityBroker(identity,
        [PlatformCapabilities.AppLibraryReadV1, PlatformCapabilities.AppRunningReadV1], store, backend,
        null, AppLibrarySavedIdIssuer.Shared, artwork.BeginSession(identity, backend));
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var denied = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppRunningReadV1,
        PlatformCapabilities.AppRunningList, new { }));
    Assert.Equal("permission_denied", denied.ErrorCode);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AppRunningReadV1,
        ConsentDecision.Grant);

    var observed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppRunningReadV1,
        PlatformCapabilities.AppRunningList, new { }));
    Assert.True(observed.Succeeded, observed.ErrorCode ?? "running observation failed");
    var payload = observed.Payload!.Value;
    Assert.Equal(1, payload.GetProperty("items").GetArrayLength());
    var item = payload.GetProperty("items")[0];
    var savedId = item.GetProperty("savedId").GetString()!;
    var revision = payload.GetProperty("revision").GetString()!;
    var serialized = payload.GetRawText();
    Assert.True(!serialized.Contains("stable-current", StringComparison.Ordinal));
    Assert.True(!serialized.Contains("private-process-evidence", StringComparison.Ordinal));
    Assert.True(!serialized.Contains("provider-running-icon", StringComparison.Ordinal));
    Assert.True(AppLibraryArtworkRegistry.IsHandle(item.GetProperty("artwork").GetProperty("items")[0].GetProperty("handle").GetString()));
    Assert.Equal(0, backend.AppLibraryIconCalls);

    var confirmed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppRunningReadV1,
        PlatformCapabilities.AppRunningConfirm, new { savedId, revision }));
    Assert.True(confirmed.Succeeded, confirmed.ErrorCode ?? "running confirmation failed");
    Assert.Equal(savedId, confirmed.Payload!.Value.GetProperty("item")
        .GetProperty("savedId").GetString());

    backend.SetRunningAppBackend([]);
    var stale = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppRunningReadV1,
        PlatformCapabilities.AppRunningConfirm, new { savedId, revision }));
    Assert.True(stale.Succeeded, stale.ErrorCode ?? "stale confirmation failed");
    Assert.Equal(JsonValueKind.Null, stale.Payload!.Value.GetProperty("item").ValueKind);
}

static async Task RunningAppRegistrationContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var otherIdentity = new BrokerWidgetIdentity(
        "dev.test.other", "dev.test", "default");
    var store = new ConsentStore(temp.Path);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetRunningAppBackend([
        new("portable-current", "portable-instance", "Portable App",
            AppLibraryKind.Application, "Portable"),
    ]);
    foreach (var capability in new[]
             {
                 PlatformCapabilities.AppRunningReadV1,
                 PlatformCapabilities.AppRunningRegisterV1,
                 PlatformCapabilities.AppLibraryReadV1,
                 PlatformCapabilities.AppLibraryLaunchV1,
             })
    {
        await store.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
        await store.SetDecisionAsync(otherIdentity, capability, ConsentDecision.Grant);
    }

    await using var broker = Broker(
        identity, store, backend,
        PlatformCapabilities.AppRunningReadV1,
        PlatformCapabilities.AppRunningRegisterV1,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryLaunchV1);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var observed = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppRunningReadV1,
        PlatformCapabilities.AppRunningList, new { }));
    Assert.True(observed.Succeeded, observed.ErrorCode ?? "observation failed");
    var savedId = observed.Payload!.Value.GetProperty("items")[0]
        .GetProperty("savedId").GetString()!;
    var revision = observed.Payload.Value.GetProperty("revision").GetString()!;

    var registered = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppRunningRegisterV1,
        PlatformCapabilities.AppRunningRegister, new { savedId, revision }));
    Assert.True(registered.Succeeded, registered.ErrorCode ?? "registration failed");
    Assert.Equal(savedId, registered.Payload!.Value.GetProperty("item")
        .GetProperty("savedId").GetString());
    Assert.Equal(false, registered.Payload.Value.GetProperty("alreadyRegistered")
        .GetBoolean());
    Assert.True(!registered.Payload.Value.GetRawText().Contains(
        "portable-instance", StringComparison.Ordinal));

    var repeated = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppRunningRegisterV1,
        PlatformCapabilities.AppRunningRegister, new { savedId, revision }));
    Assert.True(repeated.Succeeded, repeated.ErrorCode ?? "repeat registration failed");
    Assert.Equal(true, repeated.Payload!.Value.GetProperty("alreadyRegistered")
        .GetBoolean());

    var resolved = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryResolveSaved,
        new ResolveSavedAppLibraryItemsRequest([savedId])));
    Assert.True(resolved.Succeeded, resolved.ErrorCode ?? "resolution failed");
    Assert.Equal(1, resolved.Payload!.Value.GetProperty("items").GetArrayLength());
    var publicAppId = resolved.Payload.Value.GetProperty("items")[0]
        .GetProperty("appId").GetString()!;
    var launch = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new LaunchAppLibraryItemRequest(publicAppId)));
    Assert.True(launch.Succeeded, launch.ErrorCode ?? "launch failed");

    await using var other = Broker(
        otherIdentity, store, backend,
        PlatformCapabilities.AppRunningReadV1,
        PlatformCapabilities.AppRunningRegisterV1,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryLaunchV1);
    other.SetLifecycle(BrokerLifecycleState.Interactive);
    var isolated = await other.HandleAsync(Request(
        otherIdentity, PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryResolveSaved,
        new ResolveSavedAppLibraryItemsRequest([savedId])));
    Assert.True(isolated.Succeeded, isolated.ErrorCode ?? "isolated resolution failed");
    Assert.Equal(0, isolated.Payload!.Value.GetProperty("items").GetArrayLength());

    var forgotten = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppRunningRegisterV1,
        PlatformCapabilities.AppRunningForget, new { savedId }));
    Assert.True(forgotten.Succeeded, forgotten.ErrorCode ?? "forget failed");
    var forgottenAgain = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppRunningRegisterV1,
        PlatformCapabilities.AppRunningForget, new { savedId }));
    Assert.True(forgottenAgain.Succeeded, forgottenAgain.ErrorCode ?? "repeat forget failed");
    var missing = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryResolveSaved,
        new ResolveSavedAppLibraryItemsRequest([savedId])));
    Assert.Equal(0, missing.Payload!.Value.GetProperty("items").GetArrayLength());
}

static async Task RunningAppRegistrationLaunchWindowIsBounded()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = new SimulatedPlatformBrokerBackend();
    foreach (var capability in new[]
             {
                 PlatformCapabilities.AppRunningReadV1,
                 PlatformCapabilities.AppRunningRegisterV1,
                 PlatformCapabilities.AppLibraryLaunchV1,
             })
        await store.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
    await using var broker = Broker(
        identity, store, backend,
        PlatformCapabilities.AppRunningReadV1,
        PlatformCapabilities.AppRunningRegisterV1,
        PlatformCapabilities.AppLibraryLaunchV1);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);

    var projectedIds = new List<string>();
    for (var index = 0; index <= 256; index++)
    {
        var stable = $"stable-installed-{index:D3}";
        backend.SetAppLibraryBackend([
            new($"provider-installed-{index:D3}", stable, $"Installed {index:D3}",
                AppLibraryKind.Application),
        ]);
        backend.SetRunningAppBackend([
            new(stable, $"instance-{index:D3}", $"Installed {index:D3}",
                AppLibraryKind.Application, "Windows"),
        ]);
        var observed = await broker.HandleAsync(Request(
            identity, PlatformCapabilities.AppRunningReadV1,
            PlatformCapabilities.AppRunningList, new { }));
        Assert.True(observed.Succeeded, observed.ErrorCode ?? "observation failed");
        var item = observed.Payload!.Value.GetProperty("items")[0];
        var registered = await broker.HandleAsync(Request(
            identity, PlatformCapabilities.AppRunningRegisterV1,
            PlatformCapabilities.AppRunningRegister,
            new
            {
                savedId = item.GetProperty("savedId").GetString(),
                revision = observed.Payload.Value.GetProperty("revision").GetString(),
            }));
        Assert.True(registered.Succeeded, registered.ErrorCode ?? "registration failed");
        Assert.True(registered.Payload!.Value.GetProperty("alreadyRegistered").GetBoolean());
        projectedIds.Add(registered.Payload.Value.GetProperty("item")
            .GetProperty("appId").GetString()!);
    }

    var retired = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new LaunchAppLibraryItemRequest(projectedIds[0])));
    Assert.Equal("app_not_found", retired.ErrorCode);
    var current = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new LaunchAppLibraryItemRequest(projectedIds[^1])));
    Assert.True(current.Succeeded, current.ErrorCode ?? "newest launch authority was retired");
    Assert.Equal(1, backend.AppLibraryLaunchCalls);
}

static async Task AppLibraryCursorBounds()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(
        identity, PlatformCapabilities.AppLibraryReadV1, ConsentDecision.Grant);
    await store.SetDecisionAsync(
        identity, PlatformCapabilities.AppLibraryLaunchV1, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibraryBackend(Enumerable.Range(0, 10_000).Select(index =>
        new AppLibraryBackendItemSummary(
            $"provider-{index:D5}", $"stable-{index:D5}", $"Game {index:D5}",
            AppLibraryKind.Game, $"art-{index:D5}", "Steam")));
    var artwork = new AppLibraryArtworkRegistry();
    using var artworkSession = artwork.BeginSession(identity, backend);
    await using var broker = new PlatformCapabilityBroker(
        identity,
        [PlatformCapabilities.AppLibraryReadV1, PlatformCapabilities.AppLibraryLaunchV1],
        store, backend,
        null, AppLibrarySavedIdIssuer.Shared, artworkSession);
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    string? cursor = null;
    AppLibraryCursorPageSummary? page = null;
    string? firstHandle = null;
    string? firstSavedId = null;
    var pages = 0;
    do
    {
        var payload = await broker.ExecuteAsync(new BrokerRequestEnvelope(
            BrokerJson.ProtocolVersion, pages + 1, identity,
            PlatformCapabilities.AppLibraryReadV1,
            PlatformCapabilities.AppLibraryList,
            JsonSerializer.SerializeToElement(
                AppQuery(64, cursor,
                    cursor is null ? null : AppLibraryCursorDirection.After,
                    refresh: cursor is null), BrokerJson.StrictOptions)));
        Assert.True(payload.GetRawText().Length < BrokerJson.MaximumResponseBytes);
        page = payload.Deserialize<AppLibraryCursorPageSummary>(BrokerJson.StrictOptions)!;
        Assert.True(page.Items.Count is > 0 and <= 64);
        firstHandle ??= page.Items[0].Presentation.Artwork.Items.Single().Handle;
        firstSavedId ??= page.Items[0].SavedId;
        Assert.True(artwork.RegistrationCount <=
            AppLibraryArtworkRegistry.MaximumRegistrationsPerSession);
        cursor = page.After;
        pages++;
    } while (cursor is not null);

    Assert.Equal(157, pages);
    Assert.Equal(16, page!.Items.Count);
    Assert.Equal(null, await artwork.ResolveAsync(
        identity, firstHandle!, CancellationToken.None));
    var previousPayload = await broker.ExecuteAsync(new BrokerRequestEnvelope(
        BrokerJson.ProtocolVersion, 1000, identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        JsonSerializer.SerializeToElement(
            AppQuery(64, page.Before, AppLibraryCursorDirection.Before),
            BrokerJson.StrictOptions)));
    var previous = previousPayload.Deserialize<AppLibraryCursorPageSummary>(
        BrokerJson.StrictOptions)!;
    Assert.Equal("Game 09920", previous.Items[0].Presentation.DisplayName);
    Assert.Equal(64, previous.Items.Count);

    var lastSavedId = page.Items[^1].SavedId;
    var resolvedPayload = await broker.ExecuteAsync(new BrokerRequestEnvelope(
        BrokerJson.ProtocolVersion, 1001, identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryResolveSaved,
        JsonSerializer.SerializeToElement(
            new ResolveSavedAppLibraryItemsRequest([firstSavedId!, lastSavedId]),
            BrokerJson.StrictOptions)));
    var resolved = resolvedPayload.Deserialize<ResolveSavedAppLibraryItemsSummary>(
        BrokerJson.StrictOptions)!;
    Assert.Equal(2, resolved.Items.Count);
    Assert.True(resolved.Items.All(item =>
        artwork.IsCurrent(identity, item.Presentation.Artwork.Items.Single().Handle)));
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var launch = await broker.HandleAsync(Request(
        identity, PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new LaunchAppLibraryItemRequest(resolved.Items[0].AppId)));
    Assert.True(launch.Succeeded);
    Assert.Equal("provider-00000", backend.LastLaunchedAppId);
}

static async Task AdmittedLaunchLifecycle()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var consent = new ConsentStore(temp.Path);
    foreach (var capability in new[] { PlatformCapabilities.AppLibraryReadV1, PlatformCapabilities.AppLibraryLaunchV1 })
        await consent.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibraryBackend([new("provider-app", "stable-app", "App", AppLibraryKind.Application)]);
    await using var broker = Broker(identity, consent, backend, PlatformCapabilities.AppLibraryReadV1, PlatformCapabilities.AppLibraryLaunchV1);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var list = await broker.HandleAsync(Request(identity, PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList, AppQuery(16)));
    var appId = list.Payload!.Value.GetProperty("items")[0].GetProperty("appId").GetString()!;
    foreach (var boundary in new[] { "background", "revoke", "destroy" })
    {
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await consent.SetDecisionAsync(identity, PlatformCapabilities.AppLibraryLaunchV1, ConsentDecision.Grant);
        await broker.RefreshConsentAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.AppLibraryObservedLaunchHandler = async (_, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return new(AppLibraryLaunchObservationState.LauncherStarted, false, false);
        };
        var pending = broker.HandleAsync(Request(identity, PlatformCapabilities.AppLibraryLaunchV1,
            PlatformCapabilities.AppLibraryLaunchObserved, new LaunchAppLibraryItemRequest(appId)));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (boundary == "background")
        {
            broker.SetLifecycle(BrokerLifecycleState.Background);
            var denied = await broker.HandleAsync(Request(identity, PlatformCapabilities.AppLibraryLaunchV1,
                PlatformCapabilities.AppLibraryLaunchObserved, new LaunchAppLibraryItemRequest(appId)));
            Assert.Equal("lifecycle_denied", denied.ErrorCode);
            release.TrySetResult();
        }
        else if (boundary == "revoke")
        {
            await consent.SetDecisionAsync(identity, PlatformCapabilities.AppLibraryLaunchV1, ConsentDecision.Deny);
            await broker.RefreshConsentAsync();
        }
        else broker.SetLifecycle(BrokerLifecycleState.Destroying);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(boundary == "background", result.Succeeded);
        if (boundary != "background") Assert.Equal(boundary == "revoke" ? "capability_revoked" : "lifecycle_denied", result.ErrorCode);
    }
}

static async Task AppLaunchHostEffectIsSuccessBound()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AppLibraryReadV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AppLibraryLaunchV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "provider-app", "stable-app", "Test App", AppLibraryKind.Application),
    ]);
    var effects = 0;
    long? actionIntent = null;
    var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondPublished = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var options = new BrokerPipeTransportOptions
    {
        AcceptTimeout = TimeSpan.FromSeconds(2),
        HandshakeTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromSeconds(2),
    };
    var pipeName = $"wrail-broker-host-effect-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName,
        identity,
        [PlatformCapabilities.AppLibraryReadV1, PlatformCapabilities.AppLibraryLaunchV1],
        store,
        backend,
        appLibraryArtworkRegistry: null,
        options: options,
        channelNonce: new string('H', 64),
        contextualHostEffectSink: (effect, executionId, _) =>
        {
            if (effect.Kind != BrokerHostEffectKind.CloseOverlayAfterAppLaunch) return;
            Assert.True(effect.InitiatedAtMilliseconds > 0 && effect.InitiatedAtMilliseconds <= Environment.TickCount64);
            if (executionId is not null)
            {
                actionIntent = executionId;
                return;
            }
            var count = Interlocked.Increment(ref effects);
            published.TrySetResult();
            if (count == 2) secondPublished.TrySetResult();
        },
        actionExecutionAdmission: executionId => executionId > 0 ? 1L : null);
    server.SetLifecycle(BrokerLifecycleState.Interactive);
    var serverTask = server.RunAsync();
    await using var client = new BrokerPipeClient(
        pipeName, identity, server.ChannelNonce, options);
    await client.ConnectAsync();

    var page = await client.RequestAsync(
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(1));
    Assert.True(page.Succeeded);
    var appId = page.Payload!.Value.GetProperty("items")[0]
        .GetProperty("appId").GetString()!;

    var keepOpen = await client.RequestAsync(
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new LaunchAppLibraryItemRequest(appId));
    Assert.True(keepOpen.Succeeded);
    Assert.Equal(0, Volatile.Read(ref effects));

    var close = await client.RequestAsync(
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new LaunchAppLibraryItemRequest(appId) { CloseOverlayOnSuccess = true });
    Assert.True(close.Succeeded);
    await published.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal(1, Volatile.Read(ref effects));

    var actionClose = await client.RequestWithActionAsync(
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new LaunchAppLibraryItemRequest(appId) { CloseOverlayOnSuccess = true },
        null,
        null,
        73);
    Assert.True(actionClose.Succeeded);
    Assert.Equal(73L, actionIntent);
    Assert.Equal(1, Volatile.Read(ref effects));

    var acceptedOnly = await client.RequestAsync(
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunchObserved,
        new LaunchAppLibraryItemRequest(appId) { CloseOverlayOnSuccess = true });
    Assert.True(acceptedOnly.Succeeded);
    Assert.Equal(AppLibraryLaunchObservationState.RequestAccepted,
        acceptedOnly.Payload!.Value.Deserialize<AppLibraryLaunchObservationSummary>(
            BrokerJson.StrictOptions)!.State);
    Assert.Equal(1, Volatile.Read(ref effects));

    backend.AppLibraryObservedLaunchHandler = (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new AppLibraryLaunchObservationSummary(
            AppLibraryLaunchObservationState.LauncherStarted, false, false));
    };
    var evidence = await client.RequestAsync(
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunchObserved,
        new LaunchAppLibraryItemRequest(appId) { CloseOverlayOnSuccess = true });
    Assert.True(evidence.Succeeded);
    await secondPublished.Task.WaitAsync(TimeSpan.FromSeconds(1));

    backend.AppLibraryObservedLaunchHandler = (_, _) => Task.FromResult(
        new AppLibraryLaunchObservationSummary(
            AppLibraryLaunchObservationState.Running, false, false));
    var invalidEvidence = await client.RequestAsync(
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunchObserved,
        new LaunchAppLibraryItemRequest(appId) { CloseOverlayOnSuccess = true });
    Assert.Equal("invalid_backend_data", invalidEvidence.ErrorCode);
    Assert.Equal(2, Volatile.Read(ref effects));

    var failed = await client.RequestAsync(
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunch,
        new LaunchAppLibraryItemRequest("missing") { CloseOverlayOnSuccess = true });
    Assert.Equal("app_not_found", failed.ErrorCode);
    await Task.Delay(50);
    Assert.Equal(2, Volatile.Read(ref effects));

    await client.DisposeAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
}

static async Task AppLibrarySavedIdsAreDurableAndScoped()
{
    using var temp = new TemporaryDirectory();
    var keyPath = Path.Combine(temp.Path, "identity", "saved-id.key");
    var identity = Identity();
    var updatedInstance = identity with { InstanceId = "after-update" };
    var otherPackage = identity with { PackageId = "dev.test.other-widget" };
    var stableProviderIdentity = "provider-private-stable-identity";

    var firstIssuer = new AppLibrarySavedIdIssuer(keyPath);
    var firstSavedId = firstIssuer.Issue(identity, stableProviderIdentity);
    Assert.True(File.Exists(keyPath));
    Assert.Equal(49, firstSavedId.Length);
    Assert.Equal(firstSavedId,
        new AppLibrarySavedIdIssuer(keyPath).Issue(updatedInstance, stableProviderIdentity));
    Assert.True(firstSavedId !=
        new AppLibrarySavedIdIssuer(keyPath).Issue(otherPackage, stableProviderIdentity));
    Assert.True(firstSavedId !=
        new AppLibrarySavedIdIssuer(keyPath).Issue(identity, "provider-other-stable-identity"));

    var racingKeyPath = Path.Combine(temp.Path, "racing", "saved-id.key");
    var racingIds = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        new AppLibrarySavedIdIssuer(racingKeyPath)
            .Issue(identity, stableProviderIdentity))));
    Assert.Equal(1, racingIds.Distinct(StringComparer.Ordinal).Count());
    Assert.Equal(AppLibrarySavedIdIssuer.KeyBytes, new FileInfo(racingKeyPath).Length);

    var corruptKeyPath = Path.Combine(temp.Path, "corrupt", "saved-id.key");
    Directory.CreateDirectory(Path.GetDirectoryName(corruptKeyPath)!);
    await File.WriteAllBytesAsync(corruptKeyPath, new byte[31]);
    Assert.Throws<BrokerException>(() =>
        new AppLibrarySavedIdIssuer(corruptKeyPath)
            .Issue(identity, stableProviderIdentity), "platform_unavailable");

    var store = new ConsentStore(Path.Combine(temp.Path, "consent"));
    await store.SetDecisionAsync(identity, PlatformCapabilities.AppLibraryReadV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "provider-launch-one", stableProviderIdentity,
            "Durable App", AppLibraryKind.Application),
    ]);
    await using var broker = new PlatformCapabilityBroker(
        identity,
        [PlatformCapabilities.AppLibraryReadV1],
        store,
        backend,
        hostGrantedCapabilities: null,
        appLibrarySavedIdIssuer: new AppLibrarySavedIdIssuer(keyPath));
    broker.SetLifecycle(BrokerLifecycleState.Visible);

    var page = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        AppQuery(64, refresh: true)));
    Assert.True(page.Succeeded);
    var pageItem = page.Payload!.Value.GetProperty("items")[0];
    var savedId = pageItem.GetProperty("savedId").GetString()!;
    var oldLaunchId = pageItem.GetProperty("appId").GetString()!;
    Assert.Equal(firstSavedId, savedId);

    // Provider launch tokens may rotate while the durable provider identity
    // and authority-scoped SavedId remain stable.
    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "provider-launch-two", stableProviderIdentity,
            "Durable App Renamed", AppLibraryKind.Application),
    ]);
    var resolved = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryResolveSaved,
        new { savedIds = new[] { "saved-unknown", savedId } }));
    Assert.True(resolved.Succeeded);
    var resolvedItems = resolved.Payload!.Value.GetProperty("items");
    Assert.Equal(1, resolvedItems.GetArrayLength());
    Assert.Equal(savedId, resolvedItems[0].GetProperty("savedId").GetString());
    Assert.Equal("Durable App Renamed",
        resolvedItems[0].GetProperty("presentation").GetProperty("displayName").GetString());
    Assert.True(oldLaunchId != resolvedItems[0].GetProperty("appId").GetString());
    Assert.True(!resolved.Payload.Value.GetRawText().Contains(
        stableProviderIdentity, StringComparison.Ordinal));
    Assert.True(!resolved.Payload.Value.GetRawText().Contains(
        "provider-launch-two", StringComparison.Ordinal));

    var duplicate = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryResolveSaved,
        new { savedIds = new[] { savedId, savedId } }));
    Assert.Equal("invalid_payload", duplicate.ErrorCode);
    var tooMany = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryResolveSaved,
        new
        {
            savedIds = Enumerable.Range(0, 65)
                .Select(index => $"saved-{index:D3}").ToArray(),
        }));
    Assert.Equal("invalid_payload", tooMany.ErrorCode);

    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "provider-one", "duplicate-stable", "One", AppLibraryKind.Application),
        new AppLibraryBackendItemSummary(
            "provider-two", "duplicate-stable", "Two", AppLibraryKind.Application),
    ]);
    var duplicateProviderIdentity = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryResolveSaved,
        new { savedIds = new[] { "saved-any" } }));
    Assert.Equal("invalid_backend_data", duplicateProviderIdentity.ErrorCode);
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
    const string artwork =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lK3xWQAAAABJRU5ErkJggg==";
    backend.SetMediaSessions([
        new MediaSessionSummary("media-1", "Player", "Safe title", "Safe artist", MediaPlaybackStatus.Playing,
            1_000, 10_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, true,
            true, true, true, true, true) { ArtworkPngBase64 = artwork },
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
    Assert.Contains(artwork, json);

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

static async Task MediaSessionArtworkIsBounded()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.MediaSessionsReadV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    var padded = PaddedPngBase64(4_000);
    backend.SetMediaSessions(Enumerable.Range(0, 32).Select(index =>
        new MediaSessionSummary($"media-{index}", "Player", $"Track {index}", "Artist",
            MediaPlaybackStatus.Paused, 0, 1_000, 1, 1, index == 0,
            true, true, true, true, true) { ArtworkPngBase64 = padded }));
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.MediaSessionsReadV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var response = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsGet, new { }));
    Assert.True(response.Succeeded && response.Payload is not null);
    var sessions = response.Payload!.Value.EnumerateArray().ToArray();
    var retainedBytes = sessions
        .Select(item => item.GetProperty("artworkPngBase64"))
        .Where(item => item.ValueKind == JsonValueKind.String)
        .Sum(item => Convert.FromBase64String(item.GetString()!).Length);
    Assert.True(retainedBytes <= MediaSessionImageLimits.MaximumSnapshotPngBytes);
    Assert.True(sessions.Count(item => item.GetProperty("artworkPngBase64").ValueKind ==
        JsonValueKind.String) is > 0 and < 32);

    backend.SetMediaSessions([
        new MediaSessionSummary("media-bad", "Player", "Track", "Artist",
            MediaPlaybackStatus.Paused, 0, 1_000, 1, 1, true,
            true, true, true, true, true) { ArtworkPngBase64 = "not-base64" },
    ]);
    var malformed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsGet, new { }));
    Assert.Equal(JsonValueKind.Null, malformed.Payload!.Value[0]
        .GetProperty("artworkPngBase64").ValueKind);
}

static string PaddedPngBase64(int dataBytes)
{
    var original = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lK3xWQAAAABJRU5ErkJggg==");
    var iend = original.Length - 12;
    var chunk = new byte[12 + dataBytes];
    BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(0, 4), dataBytes);
    "tEXt"u8.CopyTo(chunk.AsSpan(4, 4));
    if (dataBytes > 1)
    {
        chunk[8] = (byte)'x';
        chunk[9] = 0;
    }
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + dataBytes, 4),
        PngCrc(chunk.AsSpan(4, 4 + dataBytes)));
    var padded = new byte[original.Length + chunk.Length];
    original.AsSpan(0, iend).CopyTo(padded);
    chunk.CopyTo(padded, iend);
    original.AsSpan(iend).CopyTo(padded.AsSpan(iend + chunk.Length));
    return Convert.ToBase64String(padded);
}

static uint PngCrc(ReadOnlySpan<byte> bytes)
{
    var crc = uint.MaxValue;
    foreach (var value in bytes)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
    }
    return ~crc;
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
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioOutputControlV1,
        ConsentDecision.Grant);
    var backend = AudioBackend();
    backend.SetMediaSessions([
        new("media-1", "Player", "Title", "Artist", MediaPlaybackStatus.Paused,
            0, 10_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, true,
            true, true, true, true, true),
    ]);
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.AudioSessionsControlV1,
        PlatformCapabilities.AudioOutputControlV1);
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

    var noOutputGrant = await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.AudioOutputControlV1,
        PlatformCapabilities.AudioOutputSetVolume,
        new { volume = 0.4 }, 11, 5));
    Assert.Equal("lifecycle_denied", noOutputGrant.ErrorCode);
    broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.AudioOutputControlV1,
        PlatformCapabilities.AudioOutputSetVolume, 11, 5, TimeSpan.FromSeconds(2));
    var wrongOutputOperation = await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.AudioOutputControlV1,
        PlatformCapabilities.AudioOutputSetMuted,
        new { isMuted = true }, 11, 5));
    Assert.Equal("lifecycle_denied", wrongOutputOperation.ErrorCode);
    var exactOutput = await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.AudioOutputControlV1,
        PlatformCapabilities.AudioOutputSetVolume,
        new { volume = 0.4 }, 11, 5));
    Assert.True(exactOutput.Succeeded);
    Assert.Equal(0.4, backend.AudioOutput.Volume);
    Assert.Equal("lifecycle_denied", (await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.AudioOutputControlV1,
        PlatformCapabilities.AudioOutputSetVolume,
        new { volume = 0.5 }, 11, 5))).ErrorCode);
    Assert.Equal(1, backend.AudioControlCalls);

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

    broker.GrantDashboardGestureAuthority(
        PlatformCapabilities.AudioOutputControlV1,
        PlatformCapabilities.AudioOutputSetMuted, 18, 11, TimeSpan.FromSeconds(2));
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioOutputControlV1,
        ConsentDecision.Deny);
    Assert.Equal("permission_denied", (await broker.HandleAsync(GestureRequest(
        identity, PlatformCapabilities.AudioOutputControlV1,
        PlatformCapabilities.AudioOutputSetMuted,
        new { isMuted = true }, 18, 11))).ErrorCode);
    Assert.Equal(1, backend.AudioControlCalls);

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

static async Task SpatialAudioContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = AudioBackend();
    backend.SpatialAudio = new("output", true, "off", "off", [new("off", "Off"), new("sonic", "Windows Sonic")]);
    await using var broker = Broker(identity, store, backend, PlatformCapabilities.AudioSpatialReadV1, PlatformCapabilities.AudioSpatialControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var request = Request(identity, PlatformCapabilities.AudioSpatialControlV1, PlatformCapabilities.AudioSpatialSet,
        new { deviceId = "output", formatId = "sonic" });
    Assert.True(!(await broker.HandleAsync(request)).Succeeded);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSpatialControlV1, ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSpatialReadV1, ConsentDecision.Grant);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    Assert.Equal("lifecycle_denied", (await broker.HandleAsync(request)).ErrorCode);
    var read = await broker.HandleAsync(Request(identity, PlatformCapabilities.AudioSpatialReadV1, PlatformCapabilities.AudioSpatialGet, new { }));
    Assert.True(read.Succeeded);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    Assert.True((await broker.HandleAsync(request)).Succeeded);
    Assert.Equal("sonic", backend.SpatialAudio.SelectedFormatId);
    var bad = await broker.HandleAsync(Request(identity, PlatformCapabilities.AudioSpatialControlV1, PlatformCapabilities.AudioSpatialSet,
        new { deviceId = "output", formatId = "sonic", nativeDeviceId = "forged" }));
    Assert.Equal("invalid_payload", bad.ErrorCode);
    Assert.Equal(1, backend.AudioControlCalls);
    backend.SpatialAudio = backend.SpatialAudio with { Formats = [new("off", "Off"), new("off", "Duplicate")] };
    read = await broker.HandleAsync(Request(identity, PlatformCapabilities.AudioSpatialReadV1, PlatformCapabilities.AudioSpatialGet, new { }));
    Assert.Equal("invalid_backend_data", read.ErrorCode);
}

static async Task AudioDeviceSelectionContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = AudioBackend();
    backend.SetAudioDevices([new("output", "Speakers", AudioDeviceDirection.Output, true),
        new("headset", "Headphones", AudioDeviceDirection.Output, false),
        new("input", "Microphone", AudioDeviceDirection.Input, true)]);
    await using var broker = Broker(identity, store, backend, PlatformCapabilities.AudioDevicesControlV1);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    var request = Request(identity, PlatformCapabilities.AudioDevicesControlV1,
        PlatformCapabilities.AudioDefaultOutputSet, new { deviceId = "headset" });
    Assert.True(!(await broker.HandleAsync(request)).Succeeded);
    Assert.Equal(0, backend.AudioControlCalls);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioDevicesControlV1, ConsentDecision.Grant);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    Assert.Equal("lifecycle_denied", (await broker.HandleAsync(request)).ErrorCode);
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    Assert.True((await broker.HandleAsync(request)).Succeeded);
    var devices = await backend.GetAudioDevicesAsync(CancellationToken.None);
    Assert.True(devices.Single(device => device.DeviceId == "headset").IsDefault);
    Assert.True(devices.Single(device => device.DeviceId == "input").IsDefault);
    var invalid = await broker.HandleAsync(Request(identity, PlatformCapabilities.AudioDevicesControlV1,
        PlatformCapabilities.AudioDefaultOutputSet, new { deviceId = "missing" }));
    Assert.True(!invalid.Succeeded);
    var unknown = await broker.HandleAsync(Request(identity, PlatformCapabilities.AudioDevicesControlV1,
        PlatformCapabilities.AudioDefaultOutputSet, new { deviceId = "headset", endpoint = "native-id" }));
    Assert.Equal("invalid_payload", unknown.ErrorCode);
    Assert.Equal(1, backend.AudioControlCalls);
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

static async Task NetworkConnectionDetailsContracts()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    var backend = new SimulatedPlatformBrokerBackend
    {
        NetworkConnectionDetails = new(
            7, NetworkConnectionDetailsState.Available,
            NetworkConnectionDetailsConnectivity.Constrained,
            NetworkTransportKind.Ethernet,
            ["192.0.2.20"], ["192.0.2.1"], ["9.9.9.9"]),
    };
    await using var broker = Broker(identity, store, backend,
        PlatformCapabilities.NetworkDetailsReadV1);
    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var denied = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkDetailsReadV1,
        PlatformCapabilities.NetworkDetailsGet, new { }));
    Assert.Equal("permission_denied", denied.ErrorCode);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkDetailsReadV1,
        ConsentDecision.Grant);
    await broker.RefreshConsentAsync();
    var response = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkDetailsReadV1,
        PlatformCapabilities.NetworkDetailsGet, new { }));
    Assert.True(response.Succeeded && response.Payload is not null);
    var json = response.Payload.GetValueOrDefault().GetRawText();
    Assert.Contains("192.0.2.20", json);
    Assert.DoesNotContain("interface", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("guid", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("mac", json, StringComparison.OrdinalIgnoreCase);
    var projected = NetworkCapabilityDomain.ProjectEvent(
        PlatformCapabilities.NetworkDetailsChanged,
        new NetworkConnectionDetailsChangedEvent(8));
    Assert.Contains("8", projected.GetRawText());
    Assert.Throws<BrokerException>(() => NetworkCapabilityDomain.ValidateConnectionDetails(
        backend.NetworkConnectionDetails with
        {
            IpAddresses = Enumerable.Repeat("192.0.2.1", 9).ToArray(),
        }), "invalid_backend_data");
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
        PlatformCapabilities.NetworkBluetoothRadioControlV1,
        PlatformCapabilities.NetworkBluetoothPairV1,
        PlatformCapabilities.NetworkBluetoothUnpairV1,
        PlatformCapabilities.NetworkBluetoothManageV1);
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

    var pairWithoutConsent = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothPairV1,
        PlatformCapabilities.NetworkBluetoothDevicePair,
        new { deviceId = "bluetooth-2" }));
    Assert.Equal("permission_denied", pairWithoutConsent.ErrorCode);
    Assert.Equal(0, backend.BluetoothPairCalls);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkBluetoothPairV1,
        ConsentDecision.Grant);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkBluetoothManageV1,
        ConsentDecision.Grant);

    backend.BluetoothPairingResult = BluetoothPairingResultStatus.UserInteractionRequired;
    var paired = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothPairV1,
        PlatformCapabilities.NetworkBluetoothDevicePair,
        new { deviceId = "bluetooth-2" }));
    Assert.True(paired.Succeeded && paired.Payload is not null);
    Assert.Equal("userInteractionRequired",
        paired.Payload!.Value.GetProperty("outcome").GetString());
    Assert.Equal(1, backend.BluetoothPairCalls);

    var unpairWithoutConsent = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothUnpairV1,
        PlatformCapabilities.NetworkBluetoothDeviceUnpair,
        new { deviceId = "bluetooth-1" }));
    Assert.Equal("permission_denied", unpairWithoutConsent.ErrorCode);
    Assert.Equal("bluetooth-2", backend.LastBluetoothDeviceId);
    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkBluetoothUnpairV1,
        ConsentDecision.Grant);
    backend.BluetoothUnpairingResult = BluetoothUnpairingResultStatus.Unpaired;
    var unpaired = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothUnpairV1,
        PlatformCapabilities.NetworkBluetoothDeviceUnpair,
        new { deviceId = "bluetooth-1" }));
    Assert.True(unpaired.Succeeded && unpaired.Payload is not null);
    Assert.Equal("unpaired", unpaired.Payload!.Value.GetProperty("outcome").GetString());
    Assert.Equal("bluetooth-1", backend.LastBluetoothDeviceId);
    var afterUnpair = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothReadV1,
        PlatformCapabilities.NetworkBluetoothGet, new { }));
    Assert.True(afterUnpair.Succeeded && afterUnpair.Payload is not null);
    Assert.DoesNotContain("Wireless controller", afterUnpair.Payload!.Value.GetRawText(),
        StringComparison.Ordinal);
    Assert.Contains("Nearby keyboard", afterUnpair.Payload.Value.GetRawText());

    var malformedUnpair = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothUnpairV1,
        PlatformCapabilities.NetworkBluetoothDeviceUnpair,
        new { deviceId = "bluetooth-1", nativeId = "secret" }));
    Assert.Equal("invalid_payload", malformedUnpair.ErrorCode);
    Assert.Equal("bluetooth-1", backend.LastBluetoothDeviceId);

    var malformedPair = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothPairV1,
        PlatformCapabilities.NetworkBluetoothDevicePair,
        new { deviceId = "bluetooth-2", nativeId = "secret" }));
    Assert.Equal("invalid_payload", malformedPair.ErrorCode);
    Assert.Equal(1, backend.BluetoothPairCalls);

    var managed = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothManageV1,
        PlatformCapabilities.NetworkBluetoothDeviceSettingsOpen,
        new { deviceId = "bluetooth-1" }));
    Assert.True(managed.Succeeded);
    Assert.Equal(1, backend.BluetoothManageCalls);
    Assert.Equal("bluetooth-1", backend.LastBluetoothDeviceId);

    broker.SetLifecycle(BrokerLifecycleState.Visible);
    var backgroundManage = await broker.HandleAsync(Request(identity,
        PlatformCapabilities.NetworkBluetoothManageV1,
        PlatformCapabilities.NetworkBluetoothDeviceSettingsOpen,
        new { deviceId = "bluetooth-1" }));
    Assert.Equal("lifecycle_denied", backgroundManage.ErrorCode);
    Assert.Equal(1, backend.BluetoothManageCalls);
    await subscription.DisposeAsync();
}

static async Task ConsentUpdatesAreAtomic()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var capabilities = PlatformCapabilities.All
        .Where(capability => PlatformCapabilities.IsManifestDeclarable(capability.Id))
        .Select(capability => capability.Id).ToArray();
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
    var retiredSpotifyCapabilities = RetiredSpotifyCapabilities();
    var retiredEntries = string.Join(",\n",
        retiredSpotifyCapabilities.Select((capabilityId, index) =>
            $$"""{"packageId":"dev.retired.widget.{{index % 3}}","publisherId":"dev.retired.publisher.{{index % 2}}","capabilityId":"{{capabilityId}}","decision":"{{(index % 2 == 0 ? "grant" : "deny")}}"}"""));
    await File.WriteAllTextAsync(documentPath,
        $$"""
        {"schemaVersion":1,"revision":7,"entries":[
          {"packageId":"dev.test.widget","publisherId":"dev.test.publisher","capabilityId":"system.audio.sessions.read.v1","decision":"grant"},
          {{retiredEntries}},
          {"packageId":"widgetrail.firstparty.recent-apps","publisherId":"widgetrail.firstparty","capabilityId":"system.activity.recent.activate.v1","decision":"grant"},
          {"packageId":"dev.test.widget","publisherId":"dev.test.publisher","capabilityId":"system.audio.sessions.control.v1","decision":"deny"}
        ]}
        """);

    var store = new ConsentStore(temp.Path);
    var migrated = await store.LoadAsync();
    Assert.Equal(7L, migrated.Revision);
    Assert.Equal(2, migrated.Entries.Count);
    Assert.True(!migrated.Entries.Any(entry =>
        entry.CapabilityId == "system.activity.recent.activate.v1"));
    Assert.True(retiredSpotifyCapabilities.All(capabilityId =>
        !migrated.Entries.Any(entry => entry.CapabilityId == capabilityId)));
    var identity = new BrokerWidgetIdentity("dev.test.widget", "dev.test.publisher", "test");
    Assert.Equal(ConsentDecision.Grant, await store.GetDecisionAsync(
        identity, PlatformCapabilities.AudioSessionsReadV1));
    Assert.Equal(ConsentDecision.Deny, await store.GetDecisionAsync(
        identity, PlatformCapabilities.AudioSessionsControlV1));

    await store.SetDecisionAsync(identity, PlatformCapabilities.NetworkReadV1,
        ConsentDecision.Grant);
    var persisted = await File.ReadAllTextAsync(documentPath);
    Assert.True(!persisted.Contains("system.activity.recent.activate.v1", StringComparison.Ordinal));
    Assert.True(retiredSpotifyCapabilities.All(capabilityId =>
        !persisted.Contains(capabilityId, StringComparison.Ordinal)));
    Assert.Equal(ConsentDecision.Grant, await store.GetDecisionAsync(
        identity, PlatformCapabilities.AudioSessionsReadV1));
    Assert.Equal(ConsentDecision.Deny, await store.GetDecisionAsync(
        identity, PlatformCapabilities.AudioSessionsControlV1));
    Assert.Equal(ConsentDecision.Grant, await store.GetDecisionAsync(
        identity, PlatformCapabilities.NetworkReadV1));

    await File.WriteAllTextAsync(documentPath,
        """{"schemaVersion":1,"revision":8,"entries":[{"packageId":"dev.test.widget","publisherId":"dev.test.publisher","capabilityId":"external.spotify.future.v1","decision":"grant"}]}""");
    await Assert.ThrowsAsync<BrokerException>(async () => await store.LoadAsync(),
        "invalid_consent");

    await File.WriteAllTextAsync(documentPath,
        """
        {"schemaVersion":1,"revision":9,"entries":[
          {"packageId":"dev.retired.duplicate","publisherId":"dev.retired.publisher","capabilityId":"external.spotify.playback.read.v1","decision":"grant"},
          {"packageId":"dev.retired.duplicate","publisherId":"dev.retired.publisher","capabilityId":"external.spotify.playback.read.v1","decision":"deny"}
        ]}
        """);
    await Assert.ThrowsAsync<BrokerException>(async () => await store.LoadAsync(),
        "invalid_consent");

    await File.WriteAllTextAsync(documentPath,
        """{"schemaVersion":1,"revision":10,"entries":[{"packageId":"dev.test.widget","publisherId":"dev.test.publisher","capabilityId":"system.audio.sessions.read.v1","decision":"sometimes"}]}""");
    await Assert.ThrowsAsync<BrokerException>(async () => await store.LoadAsync(),
        "invalid_consent");
}

static string[] RetiredSpotifyCapabilities() =>
[
    "external.spotify.authorization.v1",
    "external.spotify.configuration.v1",
    "external.spotify.local-playback.v1",
    "external.spotify.playback.control.v1",
    "external.spotify.playback.read.v1",
    "external.spotify.playlists.read.v1",
];

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
    var pipeName = $"wrail-broker-auth-{Guid.NewGuid():N}";
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

static async Task PipeMediaDiagnosticsAreTyped()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(
        identity, PlatformCapabilities.MediaSessionsReadV1, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetMediaSessions([
        new MediaSessionSummary(
            "media-one", "Player", "Track", "Artist", MediaPlaybackStatus.Playing,
            1_000, 10_000, 1, 1, true, true, true, true, true, true),
    ]);
    var diagnostics = new List<BrokerCapabilityDiagnostic>();
    var pipeName = $"wrail-broker-media-diagnostic-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName,
        identity,
        [PlatformCapabilities.MediaSessionsReadV1],
        store,
        backend,
        appLibraryArtworkRegistry: null,
        options: TransportOptions(),
        channelNonce: new string('M', 64),
        diagnosticSink: diagnostic => diagnostics.Add(diagnostic));
    var serverTask = server.RunAsync();
    await using var client = new BrokerPipeClient(
        pipeName, identity, server.ChannelNonce, TransportOptions());
    await client.ConnectAsync();

    server.SetLifecycle(BrokerLifecycleState.Visible);
    var response = await client.RequestAsync(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsGet,
        new { });
    Assert.True(response.Succeeded);
    Assert.True(diagnostics.Any(item => item == new BrokerCapabilityDiagnostic(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsGet,
        "request",
        null)));

    await using var subscription = await client.SubscribeAsync(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsChanged);
    Assert.True(diagnostics.Any(item => item == new BrokerCapabilityDiagnostic(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsChanged,
        "subscription-open",
        null)));
    backend.Publish(new BrokerPlatformEvent(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsChanged,
        new MediaSessionsChangedEvent([])));
    _ = await subscription.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.True(diagnostics.Any(item => item == new BrokerCapabilityDiagnostic(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsChanged,
        "subscription-read",
        null)));

    server.SetLifecycle(BrokerLifecycleState.Background);
    var denied = await client.RequestAsync(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsGet,
        new { });
    Assert.Equal("lifecycle_denied", denied.ErrorCode);
    Assert.True(diagnostics.Any(item => item == new BrokerCapabilityDiagnostic(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsGet,
        "request",
        "lifecycle_denied")));

    await subscription.DisposeAsync();
    await client.DisposeAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
}

static async Task PipeCancellationIsObserved()
{
    using var temp = new TemporaryDirectory();
    var identity = Identity();
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    var backend = new BlockingBrokerBackend();
    var pipeName = $"wrail-broker-cancel-{Guid.NewGuid():N}";
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
    var replacement = client.RequestAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList,
        new { });
    backend.ReleaseCancellation.TrySetResult();
    Assert.True((await replacement.WaitAsync(TimeSpan.FromSeconds(2))).Succeeded,
        "A late canceled response poisoned the replacement broker request.");
    Assert.True((await client.RequestAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList,
        new { }).WaitAsync(TimeSpan.FromSeconds(2))).Succeeded,
        "The broker channel did not remain usable after consuming one canceled response.");
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
    var pipeName = $"wrail-broker-lease-lifecycle-{Guid.NewGuid():N}";
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
    var pipeName = $"wrail-broker-lease-consent-{Guid.NewGuid():N}";
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
    params string[] declared) => new(
        identity, declared, store, backend, hostGrantedCapabilities: null,
        appLibrarySavedIdIssuer: new AppLibrarySavedIdIssuer(
            Enumerable.Range(1, AppLibrarySavedIdIssuer.KeyBytes)
                .Select(value => (byte)value).ToArray()));

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
        payload = JsonSerializer.SerializeToElement(payload, BrokerJson.StrictOptions),
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
            "wrail-platform-broker-tests", Guid.NewGuid().ToString("N"));
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
        var pipeName = $"wrail-broker-test-{Guid.NewGuid():N}";
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
    public TaskCompletionSource ReleaseCancellation { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _requests;

    public async Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(
        CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _requests) != 1) return [];
        RequestStarted.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException)
        {
            CancellationObserved.TrySetResult();
            await ReleaseCancellation.Task.ConfigureAwait(false);
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
