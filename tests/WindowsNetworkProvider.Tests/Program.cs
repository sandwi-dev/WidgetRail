using System.Threading.Channels;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsNetworkProvider;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Construction and subscription are inert", ConstructionIsLazy),
    ("Snapshots expose bounded sanitized labels and stable opaque IDs", SnapshotsAreSafeAndStable),
    ("Privacy restriction suppresses active Wi-Fi identity and signal", PrivacyRestrictionSuppressesDetails),
    ("Transport, radio, service, and access states remain explicit", NetworkStatesAreExplicit),
    ("Disconnected enabled Wi-Fi remains available while explicit radio-off is distinct", RadioAvailabilityIsExplicit),
    ("Missing native change registrations remain degraded", RegistrationHealthIsExplicit),
    ("Native callbacks coalesce profile and adapter churn without polling", CallbacksCoalesceWithoutPolling),
    ("Stale-generation callbacks cannot mutate current state", StaleGenerationCallbacksAreIgnored),
    ("Explicit reads recover a transient native failure", ExplicitReadRecoversTransientFailure),
    ("Live native failure publishes service unavailable and recovers", LiveFailurePublishesUnavailable),
    ("Switch accepts only currently enumerated opaque saved IDs", SwitchOnlyAcceptsEnumeratedOpaqueIds),
    ("Switch returns on acceptance and reports async success or failure", ConnectionAttemptsAreAsynchronous),
    ("Missing connection notification fails through a one-shot timeout", ConnectionAttemptTimesOut),
    ("A stale earlier timeout cannot fail a newer attempt", StaleConnectionTimeoutIsIgnored),
    ("Cancelled queued switches never reach Native Wi-Fi", CancelledSwitchDoesNotExecute),
    ("Unavailable native services degrade without hanging controls", UnavailableProviderIsBounded),
    ("Disposal unregisters native resources on owner thread", DisposalUsesOwnerThread),
    ("Production provider read smoke is privacy preserving", ProductionProviderSmoke),
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
Console.WriteLine($"Executed {tests.Length} Windows network provider tests; {failures} failed.");
return failures == 0 ? 0 : 1;

static async Task ConstructionIsLazy()
{
    var adapter = new FakeNativeAdapter(Snapshot());
    var factory = new FakeFactory(adapter);
    var backend = new WindowsNetworkPlatformBackend(factory);
    backend.EventPublished += (_, _) => { };
    await Task.Delay(100);
    Assert.False(backend.IsStarted);
    Assert.Equal(0, factory.CreateCalls);
    await backend.DisposeAsync();
    Assert.Equal(0, factory.CreateCalls);
    Assert.False(adapter.IsDisposed);
}

static async Task SnapshotsAreSafeAndStable()
{
    const string privateNativeKey = "{private-interface-guid}|My Home";
    var unsafeLabel = "  Home\u0001   Wi-Fi  " + new string('x', 300);
    var many = Enumerable.Range(0, 140)
        .Select(index => new NativeSavedNetworkProfile($"native-secret-{index}", $"Network {index}", false, null))
        .ToList();
    many.Insert(0, new(privateNativeKey, unsafeLabel, true, 145));
    var adapter = new FakeNativeAdapter(Snapshot(
        medium: NativeNetworkMedium.WiFi,
        activeKey: privateNativeKey,
        activeName: unsafeLabel,
        signal: 145,
        profiles: many));
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));

    var profiles = await backend.GetSavedNetworkProfilesAsync(CancellationToken.None);
    Assert.Equal(128, profiles.Count);
    Assert.True(profiles.All(profile => profile.ProfileId.StartsWith("network_", StringComparison.Ordinal)));
    Assert.False(profiles.Any(profile => profile.ProfileId.Contains("private", StringComparison.OrdinalIgnoreCase)));
    Assert.False(profiles.Any(profile => profile.ProfileId.Contains("native", StringComparison.OrdinalIgnoreCase)));
    Assert.True(profiles.All(profile => profile.DisplayName.Length is > 0 and <= 160));
    Assert.False(profiles.Any(profile => profile.DisplayName.Any(char.IsControl)));
    var stableId = profiles.Single(profile => profile.IsConnected).ProfileId;
    var status = await backend.GetNetworkStatusAsync(CancellationToken.None);
    Assert.Equal(stableId, status.ActiveProfileId);
    Assert.Equal(100, status.SignalPercent);

    var events = EventChannel(backend);
    adapter.SetSnapshot(Snapshot(
        medium: NativeNetworkMedium.WiFi,
        activeKey: privateNativeKey,
        activeName: "Renamed",
        signal: 70,
        profiles: [new(privateNativeKey, "Renamed", true, 70)]));
    adapter.RaiseChanged();
    _ = await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    var updated = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));
    Assert.Equal(stableId, updated.ProfileId);
}

static async Task PrivacyRestrictionSuppressesDetails()
{
    var adapter = new FakeNativeAdapter(Snapshot(
        medium: NativeNetworkMedium.WiFi,
        activeKey: "raw-interface|secret-profile",
        activeName: "Secret SSID",
        signal: 91,
        profiles: [new("raw-interface|secret-profile", "Saved label", true, 91)],
        restricted: true));
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));

    var status = await backend.GetNetworkStatusAsync(CancellationToken.None);
    Assert.Equal(NetworkDetailsAccess.PrivacyRestricted, status.DetailsAccess);
    Assert.Equal(NetworkTransportKind.Wifi, status.Transport);
    Assert.Null(status.ActiveProfileId);
    Assert.Null(status.ActiveProfileName);
    Assert.Null(status.SignalPercent);
    Assert.False(Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None)).IsConnected);
    Assert.True(backend.IsWirelessAccessRestricted);
}

static async Task NetworkStatesAreExplicit()
{
    var adapter = new FakeNativeAdapter(Snapshot(
        connectivity: NetworkConnectivity.Internet,
        medium: NativeNetworkMedium.Ethernet,
        wireless: NetworkWirelessAvailability.RadioOff));
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    var status = await backend.GetNetworkStatusAsync(CancellationToken.None);
    Assert.Equal(NetworkTransportKind.Ethernet, status.Transport);
    Assert.Equal(NetworkWirelessAvailability.RadioOff, status.WirelessAvailability);
    Assert.Equal(NetworkDetailsAccess.Available, status.DetailsAccess);
    Assert.Null(status.ActiveProfileName);

    var events = EventChannel(backend);
    adapter.SetSnapshot(Snapshot(
        connectivity: NetworkConnectivity.Local,
        medium: NativeNetworkMedium.Other,
        wireless: NetworkWirelessAvailability.ServiceUnavailable));
    adapter.RaiseChanged();
    var changed = await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(NetworkTransportKind.Other, changed.Status.Transport);
    Assert.Equal(NetworkWirelessAvailability.ServiceUnavailable, changed.Status.WirelessAvailability);
}

static Task RadioAvailabilityIsExplicit()
{
    Assert.Equal(NetworkWirelessAvailability.Available,
        WindowsNetworkNativeAdapter.ResolveRadioAvailability([true]));
    Assert.Equal(NetworkWirelessAvailability.RadioOff,
        WindowsNetworkNativeAdapter.ResolveRadioAvailability([false, false]));
    Assert.Equal(NetworkWirelessAvailability.Available,
        WindowsNetworkNativeAdapter.ResolveRadioAvailability([null]));
    Assert.Equal(NetworkWirelessAvailability.Available,
        WindowsNetworkNativeAdapter.ResolveRadioAvailability([false, null]));
    return Task.CompletedTask;
}

static Task RegistrationHealthIsExplicit()
{
    Assert.False(WindowsNetworkNativeAdapter.HasDegradedRegistrationState(
        true, true, true, true, true));
    Assert.True(WindowsNetworkNativeAdapter.HasDegradedRegistrationState(
        false, true, true, true, true));
    Assert.True(WindowsNetworkNativeAdapter.HasDegradedRegistrationState(
        true, true, false, true, true));
    Assert.True(WindowsNetworkNativeAdapter.HasDegradedRegistrationState(
        true, true, true, true, false));
    Assert.False(WindowsNetworkNativeAdapter.HasDegradedRegistrationState(
        true, false, false, false, false));
    return Task.CompletedTask;
}

static async Task CallbacksCoalesceWithoutPolling()
{
    var adapter = new FakeNativeAdapter(Snapshot());
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    _ = await backend.GetNetworkStatusAsync(CancellationToken.None);
    Assert.Equal(1, adapter.ReadCalls);
    await Task.Delay(150);
    Assert.Equal(1, adapter.ReadCalls);

    adapter.BlockNextRead();
    adapter.RaiseChanged();
    Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));
    for (var index = 0; index < 100; index++) adapter.RaiseChanged();
    adapter.AllowRead.Set();
    await WaitUntilAsync(() => adapter.ReadCalls >= 3);
    await Task.Delay(100);
    Assert.Equal(3, adapter.ReadCalls);
}

static async Task StaleGenerationCallbacksAreIgnored()
{
    var adapter = new FakeNativeAdapter(Snapshot());
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    _ = await backend.GetNetworkStatusAsync(CancellationToken.None);
    adapter.SetSnapshot(Snapshot(connectivity: NetworkConnectivity.Internet));
    adapter.RaiseChanged(generation: adapter.Generation - 1);
    await Task.Delay(100);
    Assert.Equal(1, adapter.ReadCalls);
    Assert.Equal(NetworkConnectivity.None,
        (await backend.GetNetworkStatusAsync(CancellationToken.None)).Connectivity);
}

static async Task ExplicitReadRecoversTransientFailure()
{
    var adapter = new FakeNativeAdapter(Snapshot(connectivity: NetworkConnectivity.Internet));
    adapter.FailNextRead();
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    await Assert.ThrowsBrokerAsync(
        () => backend.GetNetworkStatusAsync(CancellationToken.None),
        "platform_unavailable");
    Assert.True(backend.IsDegraded);
    var recovered = await backend.GetNetworkStatusAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(NetworkConnectivity.Internet, recovered.Connectivity);
    Assert.False(backend.IsDegraded);
    Assert.Equal(2, adapter.ReadCalls);
    _ = await backend.GetNetworkStatusAsync(CancellationToken.None);
    Assert.Equal(2, adapter.ReadCalls);
}

static async Task LiveFailurePublishesUnavailable()
{
    var adapter = new FakeNativeAdapter(Snapshot(
        connectivity: NetworkConnectivity.Internet,
        wireless: NetworkWirelessAvailability.NoAdapter));
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    _ = await backend.GetNetworkStatusAsync(CancellationToken.None);
    var events = EventChannel(backend);

    adapter.FailNextRead();
    adapter.RaiseChanged();
    var unavailable = await ReadUntilAsync(events.Reader,
        status => status.WirelessAvailability == NetworkWirelessAvailability.ServiceUnavailable);
    Assert.Equal(NetworkConnectivity.None, unavailable.Connectivity);
    // The next explicit read is the bounded recovery action.
    var recovered = await backend.GetNetworkStatusAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(NetworkConnectivity.Internet, recovered.Connectivity);
    Assert.Equal(NetworkWirelessAvailability.NoAdapter, recovered.WirelessAvailability);
}

static async Task SwitchOnlyAcceptsEnumeratedOpaqueIds()
{
    const string secret = "raw-interface-guid|private-profile-name";
    var adapter = new FakeNativeAdapter(Snapshot(profiles: [new(secret, "Home", false, null)]));
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    var events = EventChannel(backend);
    var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));

    await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
    Assert.Equal(secret, Assert.Single(adapter.ConnectedNativeKeys));
    adapter.RaiseOutcome(secret, NativeNetworkConnectionResult.Succeeded);
    _ = await ReadUntilAsync(events.Reader,
        status => status.ConnectionAttemptState == NetworkConnectionAttemptState.None);
    await Assert.ThrowsBrokerAsync(
        () => backend.SwitchSavedNetworkProfileAsync("network_00000000000000000000000000000000", CancellationToken.None),
        "resource_not_found");
    await Assert.ThrowsBrokerAsync(
        () => backend.SwitchSavedNetworkProfileAsync("private-profile-name", CancellationToken.None),
        "invalid_payload");
    Assert.Equal(1, adapter.ConnectCalls);

    adapter.SetSnapshot(Snapshot(profiles: []));
    adapter.RaiseChanged();
    await WaitUntilAsync(() => adapter.ReadCalls >= 2);
    await Assert.ThrowsBrokerAsync(
        () => backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None),
        "resource_not_found");
}

static async Task ConnectionAttemptsAreAsynchronous()
{
    const string native = "interface|saved";
    var adapter = new FakeNativeAdapter(Snapshot(profiles: [new(native, "Saved", false, null)]));
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    var events = EventChannel(backend);
    var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));

    await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(1));
    var connecting = await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(NetworkConnectionAttemptState.Connecting, connecting.Status.ConnectionAttemptState);
    Assert.Equal(profile.ProfileId, connecting.Status.AttemptProfileId);

    adapter.RaiseOutcome(native, NativeNetworkConnectionResult.Failed);
    var failed = await ReadUntilAsync(events.Reader,
        status => status.ConnectionAttemptState == NetworkConnectionAttemptState.Failed);
    Assert.Equal(profile.ProfileId, failed.AttemptProfileId);

    await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
    adapter.RaiseOutcome(native, NativeNetworkConnectionResult.Succeeded);
    var succeeded = await ReadUntilAsync(events.Reader,
        status => status.ConnectionAttemptState == NetworkConnectionAttemptState.None);
    Assert.Null(succeeded.AttemptProfileId);
}

static async Task ConnectionAttemptTimesOut()
{
    const string native = "interface|timeout";
    var adapter = new FakeNativeAdapter(Snapshot(profiles: [new(native, "Timeout", false, null)]));
    await using var backend = new WindowsNetworkPlatformBackend(
        new FakeFactory(adapter), TimeSpan.FromMilliseconds(60));
    var events = EventChannel(backend);
    var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));

    await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
    _ = await ReadUntilAsync(events.Reader,
        status => status.ConnectionAttemptState == NetworkConnectionAttemptState.Connecting);
    var failed = await ReadUntilAsync(events.Reader,
        status => status.ConnectionAttemptState == NetworkConnectionAttemptState.Failed);
    Assert.Equal(profile.ProfileId, failed.AttemptProfileId);
}

static async Task StaleConnectionTimeoutIsIgnored()
{
    const string native = "interface|stale-timeout";
    var adapter = new FakeNativeAdapter(Snapshot(profiles: [new(native, "Saved", false, null)]));
    await using var backend = new WindowsNetworkPlatformBackend(
        new FakeFactory(adapter), TimeSpan.FromMilliseconds(500));
    var events = EventChannel(backend);
    var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));

    await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
    _ = await ReadUntilAsync(events.Reader,
        status => status.ConnectionAttemptState == NetworkConnectionAttemptState.Connecting);
    await Task.Delay(100);
    adapter.RaiseOutcome(native, NativeNetworkConnectionResult.Failed);
    _ = await ReadUntilAsync(events.Reader,
        status => status.ConnectionAttemptState == NetworkConnectionAttemptState.Failed);

    await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
    _ = await ReadUntilAsync(events.Reader,
        status => status.ConnectionAttemptState == NetworkConnectionAttemptState.Connecting);
    await Task.Delay(450);
    var stillConnecting = await backend.GetNetworkStatusAsync(CancellationToken.None);
    Assert.Equal(NetworkConnectionAttemptState.Connecting, stillConnecting.ConnectionAttemptState);
    var secondFailure = await ReadUntilAsync(events.Reader,
        status => status.ConnectionAttemptState == NetworkConnectionAttemptState.Failed);
    Assert.Equal(profile.ProfileId, secondFailure.AttemptProfileId);
}

static async Task CancelledSwitchDoesNotExecute()
{
    const string native = "interface|saved";
    var adapter = new FakeNativeAdapter(Snapshot(profiles: [new(native, "Saved", false, null)]));
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));
    adapter.BlockNextRead();
    adapter.RaiseChanged();
    Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));
    using var cancellation = new CancellationTokenSource();
    var operation = backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, cancellation.Token);
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    adapter.AllowRead.Set();
    await Task.Delay(100);
    Assert.Equal(0, adapter.ConnectCalls);
}

static async Task UnavailableProviderIsBounded()
{
    await using var backend = new WindowsNetworkPlatformBackend(new ThrowingFactory());
    await Assert.ThrowsBrokerAsync(
        () => backend.GetNetworkStatusAsync(CancellationToken.None),
        "platform_unavailable");
    await Assert.ThrowsBrokerAsync(
        () => backend.GetSavedNetworkProfilesAsync(CancellationToken.None),
        "platform_unavailable");
    Assert.True(backend.IsDegraded);

    var noAdapter = new FakeNativeAdapter(Snapshot(
        wireless: NetworkWirelessAvailability.NoAdapter));
    await using var healthyNoAdapter = new WindowsNetworkPlatformBackend(new FakeFactory(noAdapter));
    var healthyStatus = await healthyNoAdapter.GetNetworkStatusAsync(CancellationToken.None);
    Assert.Equal(NetworkWirelessAvailability.NoAdapter, healthyStatus.WirelessAvailability);
    Assert.False(healthyNoAdapter.IsDegraded);
    await Assert.ThrowsBrokerAsync(
        () => backend.SwitchSavedNetworkProfileAsync(
            "network_00000000000000000000000000000000", CancellationToken.None),
        "platform_unavailable");
}

static async Task DisposalUsesOwnerThread()
{
    var adapter = new FakeNativeAdapter(Snapshot());
    var factory = new FakeFactory(adapter);
    var backend = new WindowsNetworkPlatformBackend(factory);
    _ = await backend.GetNetworkStatusAsync(CancellationToken.None);
    await backend.DisposeAsync();
    adapter.RaiseChanged();
    Assert.True(adapter.IsDisposed);
    Assert.Equal(factory.CreateThreadId, adapter.DisposeThreadId);
    await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () =>
        _ = await backend.GetNetworkStatusAsync(CancellationToken.None));
}

static async Task ProductionProviderSmoke()
{
    if (!OperatingSystem.IsWindows()) return;
    await using var backend = new WindowsNetworkPlatformBackend();
    var status = await backend.GetNetworkStatusAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(5));
    Assert.True(Enum.IsDefined(status.Connectivity));
    Assert.True(Enum.IsDefined(status.Transport));
    Assert.True(Enum.IsDefined(status.WirelessAvailability));
    Assert.True(Enum.IsDefined(status.DetailsAccess));
    Assert.Null(status.ActiveProfileId); // No prompt-sensitive current_connection read.
    Assert.Null(status.ActiveProfileName);
    Assert.Null(status.SignalPercent);
    foreach (var profile in await backend.GetSavedNetworkProfilesAsync(CancellationToken.None))
    {
        Assert.True(profile.ProfileId.StartsWith("network_", StringComparison.Ordinal));
        Assert.True(profile.ProfileId.Length <= 128);
        Assert.True(profile.DisplayName.Length is > 0 and <= 160);
        Assert.False(profile.DisplayName.Any(char.IsControl));
        Assert.Null(profile.SignalPercent);
    }
}

static NativeNetworkSnapshot Snapshot(
    NetworkConnectivity connectivity = NetworkConnectivity.None,
    NativeNetworkMedium medium = NativeNetworkMedium.None,
    string? activeKey = null,
    string? activeName = null,
    int? signal = null,
    IReadOnlyList<NativeSavedNetworkProfile>? profiles = null,
    NetworkWirelessAvailability wireless = NetworkWirelessAvailability.Available,
    bool restricted = false) =>
    new(connectivity, medium, activeKey, activeName, signal, profiles ?? [], wireless, restricted);

static Channel<NetworkStatusChangedEvent> EventChannel(WindowsNetworkPlatformBackend backend)
{
    var channel = Channel.CreateUnbounded<NetworkStatusChangedEvent>();
    backend.EventPublished += (_, platformEvent) =>
    {
        if (platformEvent.Payload is NetworkStatusChangedEvent network) channel.Writer.TryWrite(network);
    };
    return channel;
}

static async Task<NetworkStatusSummary> ReadUntilAsync(
    ChannelReader<NetworkStatusChangedEvent> reader,
    Func<NetworkStatusSummary, bool> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    while (true)
    {
        var item = await reader.ReadAsync(timeout.Token);
        if (predicate(item.Status)) return item.Status;
    }
}

static async Task WaitUntilAsync(Func<bool> condition)
{
    var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (!condition())
    {
        if (DateTime.UtcNow >= timeout) throw new TimeoutException("Condition was not reached.");
        await Task.Delay(10);
    }
}

sealed class FakeFactory(FakeNativeAdapter adapter) : IWindowsNetworkNativeAdapterFactory
{
    public int CreateThreadId { get; private set; }
    public int CreateCalls { get; private set; }
    public IWindowsNetworkNativeAdapter Create(long generation)
    {
        CreateCalls++;
        CreateThreadId = Environment.CurrentManagedThreadId;
        adapter.SetGeneration(generation);
        return adapter;
    }
}

sealed class ThrowingFactory : IWindowsNetworkNativeAdapterFactory
{
    public IWindowsNetworkNativeAdapter Create(long generation) =>
        throw new InvalidOperationException("Native network services absent.");
}

sealed class FakeNativeAdapter(NativeNetworkSnapshot initial) : IWindowsNetworkNativeAdapter
{
    private readonly object _gate = new();
    private NativeNetworkSnapshot _snapshot = initial;
    private int _readCalls;
    private int _connectCalls;
    private int _failReads;
    private int _blockNext;
    private int _degraded;
    private NativeAvailableWifiSnapshot _availableWifi = new(0, NativeWifiScanState.NotScanned, []);
    private NativeWifiScanStartResult _scanStartResult = NativeWifiScanStartResult.Started;
    private NativeWifiConnectStartResult _wifiConnectStartResult = NativeWifiConnectStartResult.Started;
    public event EventHandler<NativeNetworkStateChangedEventArgs>? StateChanged;
    public long Generation { get; private set; } = 1;
    public bool IsDegraded => Volatile.Read(ref _degraded) != 0;
    public int ReadCalls => Volatile.Read(ref _readCalls);
    public int ConnectCalls => Volatile.Read(ref _connectCalls);
    public int WifiScanCalls { get; private set; }
    public int AvailableWifiReadCalls { get; private set; }
    public int AvailableWifiConnectCalls { get; private set; }
    public List<string> ConnectedNativeKeys { get; } = [];
    public List<int> NativeCallThreadIds { get; } = [];
    public ManualResetEventSlim ReadEntered { get; } = new(false);
    public ManualResetEventSlim AllowRead { get; } = new(false);
    public bool IsDisposed { get; private set; }
    public int DisposeThreadId { get; private set; }

    public void SetGeneration(long generation) => Generation = generation;
    public void SetSnapshot(NativeNetworkSnapshot snapshot) { lock (_gate) _snapshot = snapshot; }
    public void SetAvailableWifiSnapshot(NativeAvailableWifiSnapshot snapshot) { lock (_gate) _availableWifi = snapshot; }
    public void SetWifiScanStartResult(NativeWifiScanStartResult result) => _scanStartResult = result;
    public void SetWifiConnectStartResult(NativeWifiConnectStartResult result) => _wifiConnectStartResult = result;
    public void FailNextRead() => Interlocked.Increment(ref _failReads);
    public void RaiseChanged(long? generation = null) => StateChanged?.Invoke(
        this, new NativeNetworkStateChangedEventArgs(generation ?? Generation));
    public void RaiseOutcome(string key, NativeNetworkConnectionResult result) => StateChanged?.Invoke(
        this, new NativeNetworkStateChangedEventArgs(Generation)
        {
            ConnectionOutcome = new(key, result),
        });
    public void RaiseWifiScanOutcome(NativeWifiScanOutcome result, long? generation = null) => StateChanged?.Invoke(
        this, new NativeNetworkStateChangedEventArgs(generation ?? Generation)
        {
            WifiScanOutcome = result,
        });
    public void BlockNextRead()
    {
        ReadEntered.Reset();
        AllowRead.Reset();
        Interlocked.Exchange(ref _blockNext, 1);
    }

    public NativeNetworkSnapshot ReadSnapshot()
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        Interlocked.Increment(ref _readCalls);
        if (Interlocked.Exchange(ref _failReads, 0) != 0)
        {
            Volatile.Write(ref _degraded, 1);
            throw new InvalidOperationException("Transient read failure.");
        }
        Volatile.Write(ref _degraded, 0);
        if (Interlocked.Exchange(ref _blockNext, 0) != 0)
        {
            ReadEntered.Set();
            AllowRead.Wait(TimeSpan.FromSeconds(5));
        }
        lock (_gate) return _snapshot;
    }

    public bool TryConnectSavedProfile(string nativeProfileKey)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        Interlocked.Increment(ref _connectCalls);
        lock (_gate)
        {
            if (!_snapshot.SavedProfiles.Any(profile => profile.NativeProfileKey == nativeProfileKey))
                return false;
            ConnectedNativeKeys.Add(nativeProfileKey);
            return true;
        }
    }

    public NativeAvailableWifiSnapshot ReadAvailableWifiSnapshot()
    {
        AvailableWifiReadCalls++;
        lock (_gate) return _availableWifi;
    }

    public NativeWifiScanStartResult TryStartWifiScan()
    {
        WifiScanCalls++;
        return _scanStartResult;
    }

    public NativeWifiConnectStartResult TryConnectAvailableWifiNetwork(string nativeNetworkKey)
    {
        AvailableWifiConnectCalls++;
        return _wifiConnectStartResult;
    }

    public void Dispose()
    {
        IsDisposed = true;
        DisposeThreadId = Environment.CurrentManagedThreadId;
        ReadEntered.Dispose();
        AllowRead.Dispose();
    }
}

static class Assert
{
    public static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }
    public static void False(bool condition) => True(!condition);
    public static void Null(object? value)
    {
        if (value is not null) throw new InvalidOperationException($"Expected null, got '{value}'.");
    }
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
    public static T Single<T>(IReadOnlyList<T> values)
    {
        if (values.Count != 1) throw new InvalidOperationException($"Expected one item, got {values.Count}.");
        return values[0];
    }
    public static async Task ThrowsAnyAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
    public static async Task ThrowsBrokerAsync(Func<Task> action, string code)
    {
        try { await action(); }
        catch (BrokerException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException($"Expected BrokerException code '{code}'.");
    }
}
