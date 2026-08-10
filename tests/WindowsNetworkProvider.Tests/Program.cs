using System.Threading.Channels;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsNetworkProvider;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Construction and subscription are inert", ConstructionIsLazy),
    ("Command policy owns typed native operation results", WindowsNetworkPolicyScenarios.CommandResultsAreClosed),
    ("Operation policy serializes deadlines and provider outcomes", WindowsNetworkPolicyScenarios.OperationOrderingIsDeterministic),
    ("Manually completed deadlines stay ordered on the provider owner thread", WindowsNetworkPolicyScenarios.ManualDeadlinesAreOwnerSerialized),
    ("Reconciliation policy preserves bounded identities and suppresses duplicates", WindowsNetworkPolicyScenarios.ReconciliationIsStable),
    ("Event projection uses the closed network capability vocabulary", WindowsNetworkPolicyScenarios.EventProjectionIsClosed),
    ("Snapshots expose bounded sanitized labels and stable opaque IDs", SnapshotsAreSafeAndStable),
    ("Privacy restriction suppresses active Wi-Fi identity and signal", PrivacyRestrictionSuppressesDetails),
    ("Transport, radio, service, and access states remain explicit", NetworkStatesAreExplicit),
    ("Disconnected enabled Wi-Fi remains available while explicit radio-off is distinct", RadioAvailabilityIsExplicit),
    ("Native Wi-Fi connection callbacks require the exact requested target", NativeConnectionCallbacksAreCorrelated),
    ("Missing native change registrations remain degraded", RegistrationHealthIsExplicit),
    ("Native callbacks coalesce profile and adapter churn without polling", CallbacksCoalesceWithoutPolling),
    ("Stale-generation callbacks cannot mutate current state", StaleGenerationCallbacksAreIgnored),
    ("Explicit reads recover a transient native failure", ExplicitReadRecoversTransientFailure),
    ("Live native failure publishes service unavailable and recovers", LiveFailurePublishesUnavailable),
    ("Wi-Fi software radio reads and controls on the provider owner thread", WifiRadioControlIsOwnerThreadBound),
    ("Wi-Fi radio control failures remain typed and authoritative", WifiRadioControlFailuresAreTyped),
    ("Multi-PHY radio writes roll back or report an explicit partial failure", WifiRadioMultiTargetTransaction),
    ("Failed and cancelled radio mutations still publish authoritative state", WifiRadioMutationAlwaysReconciles),
    ("Available Wi-Fi scans are explicit coalesced and event-driven", AvailableWifiScanIsExplicitAndEventDriven),
    ("Available Wi-Fi IDs are stable only within one scan generation", AvailableWifiIdsAreGenerationBound),
    ("Available Wi-Fi scan denial and unavailability remain explicit", AvailableWifiScanFailuresAreExplicit),
    ("Saved and open visible Wi-Fi connections start through opaque IDs", AvailableWifiSavedAndOpenConnectionsStart),
    ("Available Wi-Fi outcomes correlate private native keys to opaque attempts", AvailableWifiOutcomesCorrelateOpaqueAttempts),
    ("Available Wi-Fi connection prerequisites return typed errors", AvailableWifiConnectionErrorsAreTyped),
    ("Available Wi-Fi scan timeout completes without polling", AvailableWifiScanTimeoutIsEventDriven),
    ("Cancelled queued Wi-Fi commands never reach Native Wi-Fi", CancelledWifiCommandsDoNotExecute),
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

static Task NativeConnectionCallbacksAreCorrelated()
{
    var expectedInterface = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var otherInterface = Guid.Parse("22222222-2222-2222-2222-222222222222");
    Assert.True(WindowsNetworkNativeAdapter.ConnectionNotificationMatches(
        expectedInterface, expectedInterface, "Saved Home", [], "Saved Home", []));
    Assert.False(WindowsNetworkNativeAdapter.ConnectionNotificationMatches(
        expectedInterface, otherInterface, "Saved Home", [], "Saved Home", []));
    Assert.False(WindowsNetworkNativeAdapter.ConnectionNotificationMatches(
        expectedInterface, expectedInterface, "Saved Home", [], "Other profile", []));

    byte[] expectedSsid = [0x43, 0x61, 0x66, 0x65];
    Assert.True(WindowsNetworkNativeAdapter.ConnectionNotificationMatches(
        expectedInterface, expectedInterface, null, expectedSsid, "", expectedSsid.ToArray()));
    Assert.False(WindowsNetworkNativeAdapter.ConnectionNotificationMatches(
        expectedInterface, expectedInterface, null, expectedSsid, "", [0x4F, 0x74, 0x68, 0x65, 0x72]));
    Assert.False(WindowsNetworkNativeAdapter.ConnectionNotificationMatches(
        expectedInterface, expectedInterface, null, [], "", []));

    Assert.True(WindowsNetworkNativeAdapter.ShouldExposeAvailableNetwork(
        isConnectable: true, isConnected: false, hasSavedProfile: false, ssidLength: 4));
    Assert.True(WindowsNetworkNativeAdapter.ShouldExposeAvailableNetwork(
        isConnectable: false, isConnected: true, hasSavedProfile: false, ssidLength: 4));
    Assert.False(WindowsNetworkNativeAdapter.ShouldExposeAvailableNetwork(
        isConnectable: false, isConnected: false, hasSavedProfile: true, ssidLength: 4));
    Assert.False(WindowsNetworkNativeAdapter.ShouldExposeAvailableNetwork(
        isConnectable: true, isConnected: false, hasSavedProfile: false, ssidLength: 0));
    Assert.True(WindowsNetworkNativeAdapter.ShouldExposeAvailableNetwork(
        isConnectable: true, isConnected: false, hasSavedProfile: true, ssidLength: 0));
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

static async Task WifiRadioControlIsOwnerThreadBound()
{
    var adapter = new FakeNativeAdapter(Snapshot());
    adapter.SetWifiRadio(new(NativeWifiRadioState.On, true));
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    var events = RadioEventChannel(backend);

    var initial = await backend.GetWifiRadioAsync(CancellationToken.None);
    Assert.Equal(WifiRadioState.On, initial.State);
    Assert.True(initial.CanControl);

    await backend.SetWifiRadioAsync(false, CancellationToken.None);
    var changed = await ReadRadioUntilAsync(events.Reader,
        radio => radio.State == WifiRadioState.Off);
    Assert.True(changed.CanControl);
    Assert.Equal(1, adapter.WifiRadioSetCalls);
    Assert.True(adapter.NativeCallThreadIds.All(id => id == adapter.NativeCallThreadIds[0]));
}

static async Task WifiRadioControlFailuresAreTyped()
{
    foreach (var (native, code) in new[]
    {
        (NativeWifiRadioSetResult.NoAdapter, "wifi_no_adapter"),
        (NativeWifiRadioSetResult.HardwareDisabled, "wifi_hardware_disabled"),
        (NativeWifiRadioSetResult.PolicyDenied, "wifi_radio_policy_denied"),
        (NativeWifiRadioSetResult.Unavailable, "platform_unavailable"),
    })
    {
        var adapter = new FakeNativeAdapter(Snapshot());
        adapter.SetWifiRadioSetResult(native);
        await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
        await Assert.ThrowsBrokerAsync(
            () => backend.SetWifiRadioAsync(true, CancellationToken.None), code);
        Assert.Equal(1, adapter.WifiRadioSetCalls);
    }
}

static Task WifiRadioMultiTargetTransaction()
{
    var targets = new[]
    {
        new RadioTarget("first", 2, 1),
        new RadioTarget("second", 2, 1),
    };
    var states = targets.ToDictionary(target => target.Id, target => target.Software);
    var result = WindowsNetworkNativeAdapter.ApplySoftwareRadioTransaction(
        targets, true, target => states[target.Id], target => target.Hardware,
        (target, desired) =>
        {
            if (target.Id == "second" && desired == 1)
                return NativeWifiRadioSetResult.PolicyDenied;
            states[target.Id] = desired;
            return NativeWifiRadioSetResult.Succeeded;
        });
    Assert.Equal(NativeWifiRadioSetResult.PolicyDenied, result);
    Assert.Equal(2, states["first"]);
    Assert.Equal(2, states["second"]);

    states["first"] = 2;
    states["second"] = 2;
    result = WindowsNetworkNativeAdapter.ApplySoftwareRadioTransaction(
        targets, true, target => states[target.Id], target => target.Hardware,
        (target, desired) =>
        {
            if (target.Id == "second" && desired == 1)
                return NativeWifiRadioSetResult.Unavailable;
            if (target.Id == "first" && desired == 2)
                return NativeWifiRadioSetResult.Unavailable;
            states[target.Id] = desired;
            return NativeWifiRadioSetResult.Succeeded;
        });
    Assert.Equal(NativeWifiRadioSetResult.PartialFailure, result);
    Assert.Equal(1, states["first"]);
    return Task.CompletedTask;
}

static async Task WifiRadioMutationAlwaysReconciles()
{
    var adapter = new FakeNativeAdapter(Snapshot());
    adapter.SetWifiRadio(new(NativeWifiRadioState.On, true));
    adapter.SetWifiRadioSetResult(NativeWifiRadioSetResult.PartialFailure);
    adapter.SetWifiRadioMutation(new(NativeWifiRadioState.Off, true));
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    var events = RadioEventChannel(backend);
    _ = await backend.GetWifiRadioAsync(CancellationToken.None);
    await Assert.ThrowsBrokerAsync(
        () => backend.SetWifiRadioAsync(false, CancellationToken.None),
        "wifi_radio_partial_failure");
    var failedState = await ReadRadioUntilAsync(events.Reader,
        radio => radio.State == WifiRadioState.Off);
    Assert.Equal(WifiRadioState.Off, failedState.State);
    Assert.Equal(WifiRadioState.Off,
        (await backend.GetWifiRadioAsync(CancellationToken.None)).State);
    Assert.True(adapter.ReadCalls >= 2);
    Assert.True(adapter.AvailableWifiReadCalls >= 1);

    adapter.SetWifiRadioSetResult(NativeWifiRadioSetResult.Succeeded);
    adapter.SetWifiRadioMutation(null);
    adapter.SetWifiRadio(new(NativeWifiRadioState.On, true));
    adapter.RaiseChanged();
    _ = await ReadRadioUntilAsync(events.Reader, radio => radio.State == WifiRadioState.On);
    adapter.BlockNextRadioSet();
    using var cancellation = new CancellationTokenSource();
    var cancelled = backend.SetWifiRadioAsync(false, cancellation.Token);
    Assert.True(adapter.RadioSetEntered.Wait(TimeSpan.FromSeconds(2)));
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
    adapter.AllowRadioSet.Set();
    var cancelledState = await ReadRadioUntilAsync(events.Reader,
        radio => radio.State == WifiRadioState.Off);
    Assert.Equal(WifiRadioState.Off, cancelledState.State);
}

static async Task AvailableWifiScanIsExplicitAndEventDriven()
{
    const string privateKey = "{interface-guid}|private-ssid|profile";
    var adapter = new FakeNativeAdapter(Snapshot());
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    var events = WifiEventChannel(backend);

    var initial = await backend.GetAvailableWifiNetworksAsync(CancellationToken.None);
    Assert.Equal(WifiScanState.NotScanned, initial.ScanState);
    Assert.Equal(0, adapter.WifiScanCalls);
    Assert.Equal(0, adapter.AvailableWifiReadCalls);

    await backend.RequestWifiScanAsync(CancellationToken.None);
    var scanning = await ReadWifiUntilAsync(events.Reader,
        snapshot => snapshot.ScanState == WifiScanState.Scanning);
    Assert.Equal(0, scanning.Networks.Count);
    Assert.Equal(1, adapter.WifiScanCalls);

    adapter.SetWifiScanStartResult(NativeWifiScanStartResult.AlreadyScanning);
    await backend.RequestWifiScanAsync(CancellationToken.None);
    Assert.Equal(2, adapter.WifiScanCalls);
    Assert.Equal(0, adapter.AvailableWifiReadCalls);

    adapter.SetAvailableWifiSnapshot(new(7, NativeWifiScanState.Ready, [
        new(privateKey, "  Cafe\u0001   Wi-Fi  ", 117, WifiSecurityKind.Open,
            false, false, false),
    ]));
    adapter.RaiseWifiScanOutcome(NativeWifiScanOutcome.Completed);
    var ready = await ReadWifiUntilAsync(events.Reader,
        snapshot => snapshot.ScanState == WifiScanState.Ready);
    var network = Assert.Single(ready.Networks);
    Assert.Equal("Cafe Wi-Fi", network.DisplayName);
    Assert.Equal(100, network.SignalPercent);
    Assert.True(network.NetworkId.StartsWith("wifi_", StringComparison.Ordinal));
    Assert.False(network.NetworkId.Contains("private", StringComparison.OrdinalIgnoreCase));
    Assert.Equal(1, adapter.AvailableWifiReadCalls);

    var cached = await backend.GetAvailableWifiNetworksAsync(CancellationToken.None);
    Assert.Equal(network.NetworkId, Assert.Single(cached.Networks).NetworkId);
    var networkReadsAfterCallback = adapter.ReadCalls;
    await Task.Delay(200);
    Assert.Equal(1, adapter.AvailableWifiReadCalls);
    Assert.Equal(networkReadsAfterCallback, adapter.ReadCalls);
}

static async Task AvailableWifiIdsAreGenerationBound()
{
    const string nativeKey = "interface|same-network";
    var adapter = new FakeNativeAdapter(Snapshot());
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    var events = WifiEventChannel(backend);

    await backend.RequestWifiScanAsync(CancellationToken.None);
    adapter.SetAvailableWifiSnapshot(new(1, NativeWifiScanState.Ready, [
        new(nativeKey, "Home", 80, WifiSecurityKind.Personal, false, false, true),
    ]));
    adapter.RaiseWifiScanOutcome(NativeWifiScanOutcome.Completed);
    var firstId = Assert.Single((await ReadWifiUntilAsync(events.Reader,
        snapshot => snapshot.ScanState == WifiScanState.Ready)).Networks).NetworkId;

    adapter.SetAvailableWifiSnapshot(new(1, NativeWifiScanState.Ready, [
        new(nativeKey, "Home renamed", 70, WifiSecurityKind.Personal, false, false, true),
    ]));
    adapter.RaiseChanged();
    var sameGeneration = Assert.Single((await ReadWifiUntilAsync(events.Reader,
        snapshot => snapshot.ScanState == WifiScanState.Ready &&
            snapshot.Networks.Count == 1 && snapshot.Networks[0].DisplayName == "Home renamed"))
        .Networks);
    Assert.Equal(firstId, sameGeneration.NetworkId);

    adapter.SetWifiScanStartResult(NativeWifiScanStartResult.Started);
    await backend.RequestWifiScanAsync(CancellationToken.None);
    await Assert.ThrowsBrokerAsync(
        () => backend.ConnectAvailableWifiNetworkAsync(firstId, CancellationToken.None),
        "resource_not_found");
    adapter.SetAvailableWifiSnapshot(new(2, NativeWifiScanState.Ready, [
        new(nativeKey, "Home", 75, WifiSecurityKind.Personal, false, false, true),
    ]));
    adapter.RaiseWifiScanOutcome(NativeWifiScanOutcome.Completed);
    var nextId = Assert.Single((await ReadWifiUntilAsync(events.Reader,
        snapshot => snapshot.ScanState == WifiScanState.Ready)).Networks).NetworkId;
    Assert.True(firstId != nextId);
    await Assert.ThrowsBrokerAsync(
        () => backend.ConnectAvailableWifiNetworkAsync(firstId, CancellationToken.None),
        "resource_not_found");
}

static async Task AvailableWifiScanFailuresAreExplicit()
{
    foreach (var (native, expected) in new[]
    {
        (NativeWifiScanStartResult.PreciseLocationDenied, WifiScanState.PreciseLocationDenied),
        (NativeWifiScanStartResult.Unavailable, WifiScanState.Unavailable),
    })
    {
        var adapter = new FakeNativeAdapter(Snapshot());
        adapter.SetWifiScanStartResult(native);
        await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
        var events = WifiEventChannel(backend);
        await backend.RequestWifiScanAsync(CancellationToken.None);
        var snapshot = await ReadWifiUntilAsync(events.Reader,
            value => value.ScanState == expected);
        Assert.Equal(0, snapshot.Networks.Count);
        Assert.Equal(expected,
            (await backend.GetAvailableWifiNetworksAsync(CancellationToken.None)).ScanState);
        Assert.Equal(0, adapter.AvailableWifiReadCalls);
    }
}

static async Task AvailableWifiSavedAndOpenConnectionsStart()
{
    foreach (var network in new[]
    {
        new NativeAvailableWifiNetwork("interface|saved", "Saved", 90,
            WifiSecurityKind.Personal, false, false, true),
        new NativeAvailableWifiNetwork("interface|open", "Open", 65,
            WifiSecurityKind.Open, false, false, false),
    })
    {
        var adapter = new FakeNativeAdapter(Snapshot());
        await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
        var wifiEvents = WifiEventChannel(backend);
        await backend.RequestWifiScanAsync(CancellationToken.None);
        adapter.SetAvailableWifiSnapshot(new(1, NativeWifiScanState.Ready, [network]));
        adapter.RaiseWifiScanOutcome(NativeWifiScanOutcome.Completed);
        var visible = Assert.Single((await ReadWifiUntilAsync(wifiEvents.Reader,
            snapshot => snapshot.ScanState == WifiScanState.Ready)).Networks);

        await backend.ConnectAvailableWifiNetworkAsync(visible.NetworkId, CancellationToken.None);
        Assert.Equal(1, adapter.AvailableWifiConnectCalls);
        Assert.Equal(network.NativeNetworkKey, Assert.Single(adapter.ConnectedAvailableWifiNativeKeys));
        var status = await backend.GetNetworkStatusAsync(CancellationToken.None);
        Assert.Equal(NetworkConnectionAttemptState.Connecting, status.ConnectionAttemptState);
        Assert.Equal(visible.NetworkId, status.AttemptProfileId);
    }
}

static async Task AvailableWifiOutcomesCorrelateOpaqueAttempts()
{
    const string privateNativeKey = "{private-interface-guid}|Secret SSID|saved-profile";
    foreach (var (nativeResult, expectedState) in new[]
    {
        (NativeNetworkConnectionResult.Succeeded, NetworkConnectionAttemptState.None),
        (NativeNetworkConnectionResult.Failed, NetworkConnectionAttemptState.Failed),
    })
    {
        var adapter = new FakeNativeAdapter(Snapshot());
        await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
        var events = EventChannel(backend);
        var wifiEvents = WifiEventChannel(backend);
        await backend.RequestWifiScanAsync(CancellationToken.None);
        adapter.SetAvailableWifiSnapshot(new(41, NativeWifiScanState.Ready, [
            new(privateNativeKey, "Visible network", 88, WifiSecurityKind.Personal,
                false, false, true),
        ]));
        adapter.RaiseWifiScanOutcome(NativeWifiScanOutcome.Completed);
        var opaqueId = Assert.Single((await ReadWifiUntilAsync(wifiEvents.Reader,
            snapshot => snapshot.ScanState == WifiScanState.Ready)).Networks).NetworkId;

        await backend.ConnectAvailableWifiNetworkAsync(opaqueId, CancellationToken.None);
        var connecting = await ReadUntilAsync(events.Reader,
            status => status.ConnectionAttemptState == NetworkConnectionAttemptState.Connecting);
        Assert.Equal(opaqueId, connecting.AttemptProfileId);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(connecting)
            .Contains(privateNativeKey, StringComparison.Ordinal));

        adapter.RaiseOutcome(privateNativeKey, nativeResult);
        var completed = await ReadUntilAsync(events.Reader,
            status => status.ConnectionAttemptState == expectedState);
        Assert.Equal(expectedState, completed.ConnectionAttemptState);
        if (nativeResult == NativeNetworkConnectionResult.Succeeded)
            Assert.Null(completed.AttemptProfileId);
        else
            Assert.Equal(opaqueId, completed.AttemptProfileId);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(completed)
            .Contains(privateNativeKey, StringComparison.Ordinal));

        var cached = await backend.GetNetworkStatusAsync(CancellationToken.None);
        Assert.Equal(expectedState, cached.ConnectionAttemptState);
        Assert.Equal(completed.AttemptProfileId, cached.AttemptProfileId);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(cached)
            .Contains(privateNativeKey, StringComparison.Ordinal));
    }
}

static async Task AvailableWifiConnectionErrorsAreTyped()
{
    foreach (var (native, code) in new[]
    {
        (NativeWifiConnectStartResult.CredentialRequired, "credential_required"),
        (NativeWifiConnectStartResult.UnsupportedAuthentication, "unsupported_authentication"),
    })
    {
        var adapter = new FakeNativeAdapter(Snapshot());
        await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
        var wifiEvents = WifiEventChannel(backend);
        await backend.RequestWifiScanAsync(CancellationToken.None);
        adapter.SetAvailableWifiSnapshot(new(1, NativeWifiScanState.Ready, [
            new("interface|secured", "Secured", 80, WifiSecurityKind.Personal,
                true, false, false),
        ]));
        adapter.RaiseWifiScanOutcome(NativeWifiScanOutcome.Completed);
        var id = Assert.Single((await ReadWifiUntilAsync(wifiEvents.Reader,
            snapshot => snapshot.ScanState == WifiScanState.Ready)).Networks).NetworkId;
        adapter.SetWifiConnectStartResult(native);
        await Assert.ThrowsBrokerAsync(
            () => backend.ConnectAvailableWifiNetworkAsync(id, CancellationToken.None), code);
        Assert.Equal(1, adapter.AvailableWifiConnectCalls);
        Assert.Equal(NetworkConnectionAttemptState.None,
            (await backend.GetNetworkStatusAsync(CancellationToken.None)).ConnectionAttemptState);
    }
}

static async Task AvailableWifiScanTimeoutIsEventDriven()
{
    var adapter = new FakeNativeAdapter(Snapshot());
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    var events = WifiEventChannel(backend);
    await backend.RequestWifiScanAsync(CancellationToken.None);
    _ = await ReadWifiUntilAsync(events.Reader,
        snapshot => snapshot.ScanState == WifiScanState.Scanning);
    var timeout = await ReadWifiUntilAsync(events.Reader,
        snapshot => snapshot.ScanState == WifiScanState.Unavailable,
        TimeSpan.FromSeconds(8));
    Assert.Equal(0, timeout.Networks.Count);
    Assert.Equal(0, adapter.AvailableWifiReadCalls);
    Assert.Equal(1, adapter.ReadCalls);
}

static async Task CancelledWifiCommandsDoNotExecute()
{
    var adapter = new FakeNativeAdapter(Snapshot());
    await using var backend = new WindowsNetworkPlatformBackend(new FakeFactory(adapter));
    _ = await backend.GetNetworkStatusAsync(CancellationToken.None);
    adapter.BlockNextRead();
    adapter.RaiseChanged();
    Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));

    using (var scanCancellation = new CancellationTokenSource())
    {
        var scan = backend.RequestWifiScanAsync(scanCancellation.Token);
        scanCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
    }
    using (var connectCancellation = new CancellationTokenSource())
    {
        var connect = backend.ConnectAvailableWifiNetworkAsync(
            "wifi_0123456789abcdef0123456789abcdef", connectCancellation.Token);
        connectCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
    }
    adapter.AllowRead.Set();
    await Task.Delay(150);
    Assert.Equal(0, adapter.WifiScanCalls);
    Assert.Equal(0, adapter.AvailableWifiConnectCalls);
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
    await Assert.ThrowsAnyAsync<ObjectDisposedException>(() =>
        backend.RequestWifiScanAsync(CancellationToken.None));
    await Assert.ThrowsAnyAsync<ObjectDisposedException>(() =>
        backend.ConnectAvailableWifiNetworkAsync(
            "wifi_0123456789abcdef0123456789abcdef", CancellationToken.None));
    Assert.Equal(0, adapter.WifiScanCalls);
    Assert.Equal(0, adapter.AvailableWifiConnectCalls);
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

static Channel<AvailableWifiNetworksSummary> WifiEventChannel(
    WindowsNetworkPlatformBackend backend)
{
    var channel = Channel.CreateUnbounded<AvailableWifiNetworksSummary>();
    backend.EventPublished += (_, platformEvent) =>
    {
        if (platformEvent.Payload is AvailableWifiNetworksChangedEvent wifi)
            channel.Writer.TryWrite(wifi.Snapshot);
    };
    return channel;
}

static Channel<WifiRadioSummary> RadioEventChannel(WindowsNetworkPlatformBackend backend)
{
    var channel = Channel.CreateUnbounded<WifiRadioSummary>();
    backend.EventPublished += (_, platformEvent) =>
    {
        if (platformEvent.Payload is WifiRadioChangedEvent radio)
            channel.Writer.TryWrite(radio.Radio);
    };
    return channel;
}

static async Task<WifiRadioSummary> ReadRadioUntilAsync(
    ChannelReader<WifiRadioSummary> reader,
    Func<WifiRadioSummary, bool> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while (await reader.WaitToReadAsync(timeout.Token))
        while (reader.TryRead(out var value))
            if (predicate(value)) return value;
    throw new TimeoutException("Expected Wi-Fi radio event was not published.");
}

static async Task<AvailableWifiNetworksSummary> ReadWifiUntilAsync(
    ChannelReader<AvailableWifiNetworksSummary> reader,
    Func<AvailableWifiNetworksSummary, bool> predicate,
    TimeSpan? timeout = null)
{
    using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
    while (true)
    {
        var item = await reader.ReadAsync(cancellation.Token);
        if (predicate(item)) return item;
    }
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

sealed record RadioTarget(string Id, int Software, int Hardware);

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
    private NativeWifiRadioSnapshot _wifiRadio = new(NativeWifiRadioState.On, true);
    private NativeWifiRadioSetResult _wifiRadioSetResult = NativeWifiRadioSetResult.Succeeded;
    private NativeWifiRadioSnapshot? _wifiRadioMutation;
    private int _blockRadioSet;
    public event EventHandler<NativeNetworkStateChangedEventArgs>? StateChanged;
    public long Generation { get; private set; } = 1;
    public bool IsDegraded => Volatile.Read(ref _degraded) != 0;
    public int ReadCalls => Volatile.Read(ref _readCalls);
    public int ConnectCalls => Volatile.Read(ref _connectCalls);
    public int WifiScanCalls { get; private set; }
    public int AvailableWifiReadCalls { get; private set; }
    public int AvailableWifiConnectCalls { get; private set; }
    public int WifiRadioSetCalls { get; private set; }
    public int WifiRadioReadCalls { get; private set; }
    public List<string> ConnectedNativeKeys { get; } = [];
    public List<string> ConnectedAvailableWifiNativeKeys { get; } = [];
    public List<int> NativeCallThreadIds { get; } = [];
    public ManualResetEventSlim ReadEntered { get; } = new(false);
    public ManualResetEventSlim AllowRead { get; } = new(false);
    public ManualResetEventSlim RadioSetEntered { get; } = new(false);
    public ManualResetEventSlim AllowRadioSet { get; } = new(false);
    public bool IsDisposed { get; private set; }
    public int DisposeThreadId { get; private set; }

    public void SetGeneration(long generation) => Generation = generation;
    public void SetSnapshot(NativeNetworkSnapshot snapshot) { lock (_gate) _snapshot = snapshot; }
    public void SetAvailableWifiSnapshot(NativeAvailableWifiSnapshot snapshot) { lock (_gate) _availableWifi = snapshot; }
    public void SetWifiScanStartResult(NativeWifiScanStartResult result) => _scanStartResult = result;
    public void SetWifiConnectStartResult(NativeWifiConnectStartResult result) => _wifiConnectStartResult = result;
    public void SetWifiRadio(NativeWifiRadioSnapshot radio) => _wifiRadio = radio;
    public void SetWifiRadioSetResult(NativeWifiRadioSetResult result) => _wifiRadioSetResult = result;
    public void SetWifiRadioMutation(NativeWifiRadioSnapshot? radio) => _wifiRadioMutation = radio;
    public void BlockNextRadioSet()
    {
        RadioSetEntered.Reset();
        AllowRadioSet.Reset();
        Interlocked.Exchange(ref _blockRadioSet, 1);
    }
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
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        AvailableWifiReadCalls++;
        lock (_gate) return _availableWifi;
    }

    public NativeWifiScanStartResult TryStartWifiScan()
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        WifiScanCalls++;
        return _scanStartResult;
    }

    public NativeWifiConnectStartResult TryConnectAvailableWifiNetwork(string nativeNetworkKey)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        AvailableWifiConnectCalls++;
        ConnectedAvailableWifiNativeKeys.Add(nativeNetworkKey);
        return _wifiConnectStartResult;
    }

    public NativeWifiRadioSnapshot ReadWifiRadio()
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        WifiRadioReadCalls++;
        return _wifiRadio;
    }

    public NativeWifiRadioSetResult TrySetWifiRadio(bool enabled)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        WifiRadioSetCalls++;
        if (Interlocked.Exchange(ref _blockRadioSet, 0) != 0)
        {
            RadioSetEntered.Set();
            AllowRadioSet.Wait(TimeSpan.FromSeconds(5));
        }
        if (_wifiRadioMutation is not null)
            _wifiRadio = _wifiRadioMutation;
        else if (_wifiRadioSetResult == NativeWifiRadioSetResult.Succeeded)
            _wifiRadio = new(enabled ? NativeWifiRadioState.On : NativeWifiRadioState.Off, true);
        return _wifiRadioSetResult;
    }

    public void Dispose()
    {
        IsDisposed = true;
        DisposeThreadId = Environment.CurrentManagedThreadId;
        ReadEntered.Dispose();
        AllowRead.Dispose();
        RadioSetEntered.Dispose();
        AllowRadioSet.Dispose();
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
