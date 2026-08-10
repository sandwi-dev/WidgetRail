using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsNetworkProvider;
using System.Threading.Channels;

internal static class WindowsNetworkPolicyScenarios
{
    public static Task CommandResultsAreClosed()
    {
        using var adapter = new FakeNativeAdapter(new NativeNetworkSnapshot(
            NetworkConnectivity.None,
            NativeNetworkMedium.None,
            null,
            null,
            null,
            [new NativeSavedNetworkProfile("native-saved", "Saved", false, null)]));

        WindowsNetworkCommandPolicy.StartSavedConnection(adapter, "native-saved");
        Assert.Equal("native-saved", Assert.Single(adapter.ConnectedNativeKeys));
        ThrowsBroker(
            () => WindowsNetworkCommandPolicy.StartSavedConnection(adapter, "missing"),
            "resource_not_found");

        adapter.SetWifiScanStartResult(NativeWifiScanStartResult.Started);
        var started = WindowsNetworkCommandPolicy.StartWifiScan(adapter);
        Assert.True(started.ChangesState);
        Assert.True(started.StartsDeadline);
        Assert.Equal(WifiScanState.Scanning, started.State);
        adapter.SetWifiScanStartResult(NativeWifiScanStartResult.AlreadyScanning);
        Assert.False(WindowsNetworkCommandPolicy.StartWifiScan(adapter).ChangesState);

        adapter.SetWifiConnectStartResult(NativeWifiConnectStartResult.CredentialRequired);
        ThrowsBroker(
            () => WindowsNetworkCommandPolicy.StartAvailableWifiConnection(adapter, "wifi-native"),
            "credential_required");
        adapter.SetWifiConnectStartResult(NativeWifiConnectStartResult.Started);
        WindowsNetworkCommandPolicy.StartAvailableWifiConnection(adapter, "wifi-native");

        adapter.SetWifiRadioSetResult(NativeWifiRadioSetResult.PolicyDenied);
        ThrowsBroker(
            () => WindowsNetworkCommandPolicy.SetWifiRadio(adapter, enabled: false),
            "wifi_radio_policy_denied");
        adapter.SetWifiRadioSetResult(NativeWifiRadioSetResult.Succeeded);
        WindowsNetworkCommandPolicy.SetWifiRadio(adapter, enabled: false);

        Assert.True(WindowsNetworkCommandPolicy.IsValidSavedProfileId(
            "network_0123456789abcdef0123456789abcdef"));
        Assert.False(WindowsNetworkCommandPolicy.IsValidSavedProfileId("native-saved"));
        Assert.True(WindowsNetworkCommandPolicy.IsValidAvailableWifiId(
            "wifi_0123456789abcdef0123456789abcdef"));
        Assert.False(WindowsNetworkCommandPolicy.IsValidAvailableWifiId("Cafe Wi-Fi"));
        return Task.CompletedTask;
    }

    public static Task OperationOrderingIsDeterministic()
    {
        var policy = new WindowsNetworkOperationPolicy();
        var first = policy.BeginConnection("network_first", "native-first");
        Assert.Equal(NetworkConnectionAttemptState.Connecting, first.State);
        ThrowsBroker(policy.EnsureConnectionCanStart, "provider_busy");
        Assert.False(policy.ApplyConnectionTimeout(first.Generation - 1));
        Assert.False(policy.ApplyConnectionOutcome(
            new("other-native", NativeNetworkConnectionResult.Failed)));
        Assert.Equal(NetworkConnectionAttemptState.Connecting, policy.Connection.State);

        Assert.True(policy.ApplyConnectionOutcome(
            new("native-first", NativeNetworkConnectionResult.Failed)));
        Assert.Equal(NetworkConnectionAttemptState.Failed, policy.Connection.State);
        Assert.Equal("network_first", policy.Connection.ProfileId);

        var second = policy.BeginConnection("network_second", "native-second");
        Assert.True(second.Generation > first.Generation);
        Assert.False(policy.ApplyConnectionTimeout(first.Generation));
        Assert.True(policy.ApplyConnectedSnapshot("native-second", wirelessAccessRestricted: false));
        Assert.Equal(NetworkConnectionAttemptState.None, policy.Connection.State);

        var scanOne = policy.BeginScanDeadline();
        var scanTwo = policy.BeginScanDeadline();
        Assert.False(policy.IsCurrentScanDeadline(scanOne, WifiScanState.Scanning));
        Assert.True(policy.IsCurrentScanDeadline(scanTwo, WifiScanState.Scanning));
        Assert.False(policy.IsCurrentScanDeadline(scanTwo, WifiScanState.Ready));
        policy.InvalidateScanDeadline();
        Assert.False(policy.IsCurrentScanDeadline(scanTwo, WifiScanState.Scanning));

        policy.Reset();
        Assert.Equal(NetworkConnectionAttemptState.None, policy.Connection.State);
        return Task.CompletedTask;
    }

    public static async Task ManualDeadlinesAreOwnerSerialized()
    {
        const string nativeKey = "native-saved";
        var adapter = new FakeNativeAdapter(new NativeNetworkSnapshot(
            NetworkConnectivity.None,
            NativeNetworkMedium.None,
            null,
            null,
            null,
            [new NativeSavedNetworkProfile(nativeKey, "Saved", false, null)]));
        var deadlines = new ManualNetworkDeadlineScheduler();
        await using var backend = new WindowsNetworkPlatformBackend(
            new FakeFactory(adapter), TimeSpan.FromSeconds(30), deadlines);
        var statusEvents = Channel.CreateUnbounded<NetworkStatusSummary>();
        var wifiEvents = Channel.CreateUnbounded<AvailableWifiNetworksSummary>();
        backend.EventPublished += (_, value) =>
        {
            if (value.Payload is NetworkStatusChangedEvent status)
                statusEvents.Writer.TryWrite(status.Status);
            if (value.Payload is AvailableWifiNetworksChangedEvent wifi)
                wifiEvents.Writer.TryWrite(wifi.Snapshot);
        };

        var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));
        await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
        Assert.Equal(NetworkConnectionAttemptState.Connecting,
            (await ReadStatusAsync(statusEvents.Reader,
                NetworkConnectionAttemptState.Connecting)).ConnectionAttemptState);
        Assert.Equal(1, deadlines.Count);
        deadlines.Fire(0);
        Assert.Equal(NetworkConnectionAttemptState.Failed,
            (await ReadStatusAsync(statusEvents.Reader,
                NetworkConnectionAttemptState.Failed)).ConnectionAttemptState);

        await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
        _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Connecting);
        Assert.Equal(2, deadlines.Count);
        adapter.BlockNextRead();
        deadlines.Fire(0); // The disposed first deadline is deliberately late.
        adapter.RaiseChanged(); // FIFO barrier behind the late deadline.
        Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(NetworkConnectionAttemptState.Connecting,
            (await backend.GetNetworkStatusAsync(CancellationToken.None)).ConnectionAttemptState);
        adapter.AllowRead.Set();
        deadlines.Fire(1);
        _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Failed);

        await backend.RequestWifiScanAsync(CancellationToken.None);
        _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Scanning);
        Assert.Equal(3, deadlines.Count);
        await backend.RequestWifiScanAsync(CancellationToken.None);
        _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Scanning);
        Assert.Equal(4, deadlines.Count);
        adapter.SetAvailableWifiSnapshot(new(1, NativeWifiScanState.Scanning, []));
        adapter.BlockNextRead();
        deadlines.Fire(2); // The first scan deadline cannot terminate the replacement scan.
        adapter.RaiseChanged(); // FIFO barrier behind the late deadline.
        Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(WifiScanState.Scanning,
            (await backend.GetAvailableWifiNetworksAsync(CancellationToken.None)).ScanState);
        adapter.AllowRead.Set();
        deadlines.Fire(3);
        _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Unavailable);

        Assert.True(adapter.NativeCallThreadIds.Count > 0);
        Assert.True(adapter.NativeCallThreadIds.All(id => id == adapter.NativeCallThreadIds[0]));
    }

    public static Task ReconciliationIsStable()
    {
        var reconciler = new WindowsNetworkStateReconciler();
        var operations = new WindowsNetworkOperationPolicy();
        var native = new NativeNetworkSnapshot(
            NetworkConnectivity.Internet,
            NativeNetworkMedium.WiFi,
            "private-native",
            "  Home\u0001   Wi-Fi  ",
            140,
            [new NativeSavedNetworkProfile("private-native", "  Home\u0001   Wi-Fi  ", true, 140)]);

        var first = reconciler.ReconcileNetwork(native, operations, []);
        var repeated = reconciler.ReconcileNetwork(native, operations, []);
        Assert.Equal(first.Status, repeated.Status);
        Assert.True(WindowsNetworkStateReconciler.ProfilesEqual(first.Profiles, repeated.Profiles));
        Assert.Equal(Assert.Single(first.Profiles).ProfileId, Assert.Single(repeated.Profiles).ProfileId);
        Assert.Equal("Home Wi-Fi", Assert.Single(first.Profiles).DisplayName);
        Assert.Equal(100, first.Status.SignalPercent);

        var missing = reconciler.ReconcileNetwork(native with
        {
            ActiveProfileNativeKey = null,
            ActiveProfileName = null,
            SavedProfiles = [],
        }, operations, []);
        Assert.Equal(0, missing.Profiles.Count);
        var reappeared = reconciler.ReconcileNetwork(native, operations, []);
        Assert.True(Assert.Single(first.Profiles).ProfileId != Assert.Single(reappeared.Profiles).ProfileId);

        var visible = new NativeAvailableWifiSnapshot(
            7,
            NativeWifiScanState.Ready,
            [new NativeAvailableWifiNetwork(
                "wifi-native", "Cafe", 75, WifiSecurityKind.Open, false, false, false)]);
        var wifiFirst = reconciler.ReconcileAvailableWifi(visible);
        var wifiRepeated = reconciler.ReconcileAvailableWifi(visible);
        Assert.True(WindowsNetworkStateReconciler.AvailableWifiEqual(
            wifiFirst.Snapshot, wifiRepeated.Snapshot));
        Assert.Equal(
            Assert.Single(wifiFirst.Snapshot.Networks).NetworkId,
            Assert.Single(wifiRepeated.Snapshot.Networks).NetworkId);

        reconciler.ResetAvailableWifi(WifiScanState.Scanning);
        var nextGeneration = reconciler.ReconcileAvailableWifi(visible with { ScanGeneration = 8 });
        Assert.True(
            Assert.Single(wifiFirst.Snapshot.Networks).NetworkId !=
            Assert.Single(nextGeneration.Snapshot.Networks).NetworkId);
        return Task.CompletedTask;
    }

    public static Task EventProjectionIsClosed()
    {
        var status = new NetworkStatusSummary(
            NetworkConnectivity.Local,
            NetworkTransportKind.Ethernet,
            NetworkWirelessAvailability.Available,
            NetworkDetailsAccess.Available,
            NetworkConnectionAttemptState.None,
            null,
            null,
            null,
            null);
        var statusEvent = WindowsNetworkEventProjection.FromStatus(status);
        Assert.Equal(PlatformCapabilities.NetworkReadV1, statusEvent.CapabilityId);
        Assert.Equal(PlatformCapabilities.NetworkStatusChanged, statusEvent.EventType);
        Assert.Equal(status, ((NetworkStatusChangedEvent)statusEvent.Payload).Status);

        var wifi = new AvailableWifiNetworksSummary(WifiScanState.NotScanned, []);
        var wifiEvent = WindowsNetworkEventProjection.FromAvailableWifi(wifi);
        Assert.Equal(PlatformCapabilities.NetworkWifiReadV1, wifiEvent.CapabilityId);
        Assert.Equal(PlatformCapabilities.NetworkAvailableWifiChanged, wifiEvent.EventType);
        Assert.Equal(wifi, ((AvailableWifiNetworksChangedEvent)wifiEvent.Payload).Snapshot);

        var radio = new WifiRadioSummary(WifiRadioState.On, true);
        var radioEvent = WindowsNetworkEventProjection.FromRadio(radio);
        Assert.Equal(PlatformCapabilities.NetworkWifiRadioReadV1, radioEvent.CapabilityId);
        Assert.Equal(PlatformCapabilities.NetworkWifiRadioChanged, radioEvent.EventType);
        Assert.Equal(radio, ((WifiRadioChangedEvent)radioEvent.Payload).Radio);
        return Task.CompletedTask;
    }

    private static void ThrowsBroker(Action action, string code)
    {
        try { action(); }
        catch (BrokerException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException($"Expected BrokerException code '{code}'.");
    }

    private static async Task<NetworkStatusSummary> ReadStatusAsync(
        ChannelReader<NetworkStatusSummary> reader,
        NetworkConnectionAttemptState state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (await reader.WaitToReadAsync(timeout.Token))
        {
            while (reader.TryRead(out var value))
                if (value.ConnectionAttemptState == state) return value;
        }
        throw new TimeoutException($"Network status '{state}' was not published.");
    }

    private static async Task<AvailableWifiNetworksSummary> ReadWifiAsync(
        ChannelReader<AvailableWifiNetworksSummary> reader,
        WifiScanState state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (await reader.WaitToReadAsync(timeout.Token))
        {
            while (reader.TryRead(out var value))
                if (value.ScanState == state) return value;
        }
        throw new TimeoutException($"Wi-Fi scan state '{state}' was not published.");
    }
}

internal sealed class ManualNetworkDeadlineScheduler : INetworkDeadlineScheduler
{
    private readonly List<Entry> _entries = [];

    public int Count => _entries.Count;

    public IDisposable Schedule(TimeSpan dueTime, Action callback)
    {
        Assert.True(dueTime > TimeSpan.Zero);
        var entry = new Entry(callback);
        _entries.Add(entry);
        return entry;
    }

    public void Fire(int index) => _entries[index].Callback();

    private sealed class Entry(Action callback) : IDisposable
    {
        public Action Callback { get; } = callback;
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
