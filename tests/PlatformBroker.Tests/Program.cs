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
    ("Network operations switch only opaque saved profiles", NetworkOperationsAreSanitized),
    ("Consent updates are atomic across store instances", ConsentUpdatesAreAtomic),
    ("Subscriptions coalesce and suspend with lifecycle", EventsCoalesceAcrossLifecycle),
    ("Consent revocation terminates subscriptions", RevocationTerminatesSubscriptions),
    ("Lifecycle gates read control and destroying states", LifecycleGatesOperations),
    ("Cancellation reaches the broker boundary", CancellationIsObserved),
    ("Pipe framing rejects oversized payloads before allocation", PipeFramesAreBounded),
    ("Pipe handshake binds nonce identity and one client", PipeHandshakeIsBound),
    ("Pipe requests preserve identity correlation and lifecycle", PipeRequestsAreBound),
    ("Pipe request cancellation reaches the fixed backend", PipeCancellationIsObserved),
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
    Assert.Equal(4, PlatformCapabilities.All.Count);
    foreach (var capability in PlatformCapabilities.All)
    {
        Assert.True(capability.Id.EndsWith($".v{capability.Version}", StringComparison.Ordinal));
        Assert.True(capability.Version == 1);
        Assert.True(capability.Operations.Count != 0);
    }
    Assert.True(!PlatformCapabilities.TryGet("system.full-access.v1", out _));
    Assert.True(!PlatformCapabilities.TryGet("system.audio.sessions.read.v2", out _));
    return Task.CompletedTask;
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
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
    await Assert.ThrowsAsync<OperationCanceledException>(() => client.RequestAsync(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsList,
        new { }, cancellation.Token));
    await backend.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
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

sealed class BlockingBrokerBackend : IPlatformBrokerBackend
{
    public event EventHandler<BrokerPlatformEvent>? EventPublished;
    public TaskCompletionSource CancellationObserved { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(
        CancellationToken cancellationToken)
    {
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
    public Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TestNetwork.Disconnected());
    public Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SavedNetworkProfileSummary>>([]);
    public Task SwitchSavedNetworkProfileAsync(string profileId, CancellationToken cancellationToken) =>
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
