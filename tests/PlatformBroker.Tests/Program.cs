using System.Text;
using System.Text.Json;
using GameBarAlternative.PlatformBroker;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Capability vocabulary is closed and versioned", CapabilityVocabularyIsClosed),
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
        NetworkStatus = new(NetworkConnectivity.Internet, "home-5g", "Home 5G", 92),
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
}
