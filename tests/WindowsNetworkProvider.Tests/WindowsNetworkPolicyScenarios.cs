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
        adapter.RaiseOutcome(nativeKey, NativeNetworkConnectionResult.Failed);
        _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Failed);

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

    public static async Task DeadlinesSurviveOrdinaryCapacity()
    {
        await ConnectionDeadlineSurvivesOrdinaryCapacity();
        await ScanDeadlineSurvivesOrdinaryCapacity();
    }

    public static async Task DelayedStaleCallbacksRetainCurrentDeadline()
    {
        await DelayedConnectionCallbacksRetainCurrentDeadline();
        await DelayedScanCallbacksRetainCurrentDeadline();
    }

    public static Task CrossTypeOverflowOrderIsStable()
    {
        using var queue = new WindowsNetworkCommandQueue(observer: null);
        for (var generation = 1; generation <= 4; generation++)
            queue.EnqueueDeadline(new ConnectionTimeoutCommand(generation));
        queue.EnqueueDeadline(new ConnectionTimeoutCommand(5));
        queue.EnqueueDeadline(new WifiScanTimeoutCommand(5));
        Assert.Equal(4, queue.DeadlineCommandsQueued);
        Assert.Equal(2, queue.PendingDeadlineOverflowCount);

        Assert.True(queue.TryTake(out var first));
        queue.Release(first);
        Assert.SequenceEqual(
            ["connection:2", "connection:3", "connection:4", "connection:5"],
            queue.QueuedCommandKinds);
        Assert.Equal(1, queue.PendingDeadlineOverflowCount);

        Assert.True(queue.TryTake(out var second));
        queue.Release(second);
        Assert.SequenceEqual(
            ["connection:3", "connection:4", "connection:5", "scan:5"],
            queue.QueuedCommandKinds);
        Assert.Equal(0, queue.PendingDeadlineOverflowCount);

        queue.Close();
        while (queue.TryTake(out var pending)) queue.Release(pending);
        Assert.Equal(0, queue.DeadlineCommandsQueued);
        return Task.CompletedTask;
    }

    public static async Task ReplacementDeadlineKeepsTailOrder()
    {
        const string nativeKey = "native-saved";
        var adapter = AdapterWithSavedProfile(nativeKey);
        var deadlines = new ManualNetworkDeadlineScheduler();
        var backend = new WindowsNetworkPlatformBackend(
            new FakeFactory(adapter), TimeSpan.FromSeconds(30), deadlines);
        var statusEvents = Channel.CreateUnbounded<NetworkStatusSummary>();
        var failures = 0;
        backend.EventPublished += (_, value) =>
        {
            if (value.Payload is not NetworkStatusChangedEvent status) return;
            statusEvents.Writer.TryWrite(status.Status);
            if (status.Status.ConnectionAttemptState == NetworkConnectionAttemptState.Failed)
                Interlocked.Increment(ref failures);
        };

        var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));
        await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
        _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Connecting);

        adapter.BlockNextRead();
        adapter.RaiseOutcome(nativeKey, NativeNetworkConnectionResult.Succeeded);
        Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));
        deadlines.BlockNextScheduleReturn();
        var replacement = backend.SwitchSavedNetworkProfileAsync(
            profile.ProfileId, CancellationToken.None);
        deadlines.Fire(0);

        adapter.AllowRead.Set();
        Assert.True(deadlines.ScheduleEntered.Wait(TimeSpan.FromSeconds(2)));
        var intervening = backend.SetWifiRadioAsync(enabled: true, CancellationToken.None);
        deadlines.Fire(1);
        Assert.Equal(2, backend.DeadlineCommandsQueued);
        Assert.SequenceEqual(
            ["connection:1", "radio", "connection:2"],
            backend.QueuedCommandKinds);
        deadlines.AllowScheduleReturn.Set();

        await replacement;
        await intervening;
        _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Failed);
        await backend.DisposeAsync();
        Assert.Equal(1, failures);
        Assert.Equal(0, backend.OrdinaryCommandsQueued);
        Assert.Equal(0, backend.DeadlineCommandsQueued);
    }

    public static async Task ReplacementScanDeadlineKeepsTailOrder()
    {
        var adapter = AdapterWithSavedProfile("native-scan-order");
        var deadlines = new ManualNetworkDeadlineScheduler();
        var backend = new WindowsNetworkPlatformBackend(
            new FakeFactory(adapter), TimeSpan.FromSeconds(30), deadlines);
        var wifiEvents = Channel.CreateUnbounded<AvailableWifiNetworksSummary>();
        var failures = 0;
        backend.EventPublished += (_, value) =>
        {
            if (value.Payload is not AvailableWifiNetworksChangedEvent wifi) return;
            wifiEvents.Writer.TryWrite(wifi.Snapshot);
            if (wifi.Snapshot.ScanState == WifiScanState.Unavailable)
                Interlocked.Increment(ref failures);
        };

        _ = await backend.GetNetworkStatusAsync(CancellationToken.None);
        await backend.RequestWifiScanAsync(CancellationToken.None);
        _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Scanning);
        adapter.SetAvailableWifiSnapshot(new(1, NativeWifiScanState.Scanning, []));
        adapter.BlockNextRead();
        adapter.RaiseChanged();
        Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));

        deadlines.BlockNextScheduleReturn();
        var replacement = backend.RequestWifiScanAsync(CancellationToken.None);
        deadlines.Fire(0);
        adapter.AllowRead.Set();
        Assert.True(deadlines.ScheduleEntered.Wait(TimeSpan.FromSeconds(2)));
        var intervening = backend.SetWifiRadioAsync(enabled: true, CancellationToken.None);
        deadlines.Fire(1);
        Assert.Equal(2, backend.DeadlineCommandsQueued);
        Assert.SequenceEqual(
            ["scan:1", "radio", "scan:2"],
            backend.QueuedCommandKinds);
        deadlines.AllowScheduleReturn.Set();

        await replacement;
        await intervening;
        _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Unavailable);
        await backend.DisposeAsync();
        Assert.Equal(1, failures);
        Assert.Equal(0, backend.OrdinaryCommandsQueued);
        Assert.Equal(0, backend.DeadlineCommandsQueued);
    }

    public static async Task AdmissionAndDisposalAreBalanced()
    {
        await OrdinaryAdmissionAndDisposalAreBalanced();
        await DeadlineAdmissionAndDisposalAreBalanced();
    }

    private static async Task OrdinaryAdmissionAndDisposalAreBalanced()
    {
        var adapter = AdapterWithSavedProfile("native-admission");
        var observer = new ManualCommandAdmissionObserver(typeof(SetWifiRadioCommand));
        var backend = new WindowsNetworkPlatformBackend(
            new FakeFactory(adapter),
            TimeSpan.FromSeconds(30),
            new ManualNetworkDeadlineScheduler(),
            observer);
        _ = await backend.GetNetworkStatusAsync(CancellationToken.None);

        var admission = Task.Run(async () =>
        {
            try { await backend.SetWifiRadioAsync(enabled: true, CancellationToken.None); }
            catch (ObjectDisposedException) { }
        });
        Assert.True(observer.ReservationEntered.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, backend.OrdinaryCommandsQueued);
        var dispose = Task.Run(async () => await backend.DisposeAsync());
        Assert.True(SpinWait.SpinUntil(() => backend.CommandAdmissionClosed, TimeSpan.FromSeconds(2)));
        await dispose.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, backend.OrdinaryCommandsQueued);
        observer.AllowAdmission.Set();

        await admission;
        Assert.Equal(0, backend.OrdinaryCommandsQueued);
        Assert.Equal(0, backend.DeadlineCommandsQueued);
        Assert.Equal(0, backend.PendingDeadlineOverflowCount);
        Assert.Equal(0, backend.QueuedCommandKinds.Count);
    }

    private static async Task DeadlineAdmissionAndDisposalAreBalanced()
    {
        var adapter = AdapterWithSavedProfile("native-deadline-admission");
        var observer = new ManualCommandAdmissionObserver(typeof(ConnectionTimeoutCommand));
        var deadlines = new ManualNetworkDeadlineScheduler();
        var backend = new WindowsNetworkPlatformBackend(
            new FakeFactory(adapter), TimeSpan.FromSeconds(30), deadlines, observer);
        var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));
        await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);

        var admission = Task.Run(() => deadlines.Fire(0));
        Assert.True(observer.ReservationEntered.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, backend.DeadlineCommandsQueued);
        var dispose = Task.Run(async () => await backend.DisposeAsync());
        Assert.True(SpinWait.SpinUntil(() => backend.CommandAdmissionClosed, TimeSpan.FromSeconds(2)));
        await dispose.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, backend.DeadlineCommandsQueued);
        observer.AllowAdmission.Set();

        await admission;
        Assert.Equal(0, backend.OrdinaryCommandsQueued);
        Assert.Equal(0, backend.DeadlineCommandsQueued);
        Assert.Equal(0, backend.PendingDeadlineOverflowCount);
        Assert.Equal(0, backend.QueuedCommandKinds.Count);
    }

    private static async Task ConnectionDeadlineSurvivesOrdinaryCapacity()
    {
        var adapter = AdapterWithSavedProfile("native-capacity");
        var deadlines = new ManualNetworkDeadlineScheduler();
        var backend = new WindowsNetworkPlatformBackend(
            new FakeFactory(adapter), TimeSpan.FromSeconds(30), deadlines);
        var statusEvents = Channel.CreateUnbounded<NetworkStatusSummary>();
        var terminalCount = 0;
        backend.EventPublished += (_, value) =>
        {
            if (value.Payload is not NetworkStatusChangedEvent status) return;
            statusEvents.Writer.TryWrite(status.Status);
            if (status.Status.ConnectionAttemptState == NetworkConnectionAttemptState.Failed)
                Interlocked.Increment(ref terminalCount);
        };

        var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));
        await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
        _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Connecting);
        adapter.BlockNextRead();
        adapter.RaiseChanged();
        Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));

        var ordinary = await FillOrdinaryCapacityAsync(backend);
        deadlines.Fire(0);
        Assert.Equal(WindowsNetworkPlatformBackend.MaximumOrdinaryQueuedCommands,
            backend.OrdinaryCommandsQueued);
        Assert.Equal(1, backend.DeadlineCommandsQueued);
        adapter.AllowRead.Set();

        await Task.WhenAll(ordinary);
        _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Failed);
        await backend.DisposeAsync();
        Assert.Equal(1, terminalCount);
        Assert.Equal(0, backend.OrdinaryCommandsQueued);
        Assert.Equal(0, backend.DeadlineCommandsQueued);
    }

    private static async Task DelayedConnectionCallbacksRetainCurrentDeadline()
    {
        const string nativeKey = "native-delayed-deadlines";
        var adapter = AdapterWithSavedProfile(nativeKey);
        var deadlines = new ManualNetworkDeadlineScheduler();
        var backend = new WindowsNetworkPlatformBackend(
            new FakeFactory(adapter), TimeSpan.FromSeconds(30), deadlines);
        var statusEvents = Channel.CreateUnbounded<NetworkStatusSummary>();
        var terminalCount = 0;
        backend.EventPublished += (_, value) =>
        {
            if (value.Payload is not NetworkStatusChangedEvent status) return;
            statusEvents.Writer.TryWrite(status.Status);
            if (status.Status.ConnectionAttemptState == NetworkConnectionAttemptState.Failed)
                Interlocked.Increment(ref terminalCount);
        };

        var profile = Assert.Single(await backend.GetSavedNetworkProfilesAsync(CancellationToken.None));
        for (var index = 0; index < 6; index++)
        {
            await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
            _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Connecting);
            adapter.RaiseOutcome(nativeKey, NativeNetworkConnectionResult.Succeeded);
            _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.None);
        }
        await backend.SwitchSavedNetworkProfileAsync(profile.ProfileId, CancellationToken.None);
        _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Connecting);
        Assert.Equal(7, deadlines.Count);

        adapter.BlockNextRead();
        adapter.RaiseChanged();
        Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));
        for (var index = 0; index < deadlines.Count; index++) deadlines.Fire(index);
        Assert.Equal(4, backend.DeadlineCommandsQueued);
        Assert.Equal(1, backend.PendingDeadlineOverflowCount);
        adapter.AllowRead.Set();

        _ = await ReadStatusAsync(statusEvents.Reader, NetworkConnectionAttemptState.Failed);
        await backend.DisposeAsync();
        Assert.Equal(1, terminalCount);
        Assert.Equal(0, backend.DeadlineCommandsQueued);
        Assert.Equal(0, backend.PendingDeadlineOverflowCount);
    }

    private static async Task DelayedScanCallbacksRetainCurrentDeadline()
    {
        var adapter = AdapterWithSavedProfile("native-delayed-scan-deadlines");
        var deadlines = new ManualNetworkDeadlineScheduler();
        var backend = new WindowsNetworkPlatformBackend(
            new FakeFactory(adapter), TimeSpan.FromSeconds(30), deadlines);
        var wifiEvents = Channel.CreateUnbounded<AvailableWifiNetworksSummary>();
        var terminalCount = 0;
        backend.EventPublished += (_, value) =>
        {
            if (value.Payload is not AvailableWifiNetworksChangedEvent wifi) return;
            wifiEvents.Writer.TryWrite(wifi.Snapshot);
            if (wifi.Snapshot.ScanState == WifiScanState.Unavailable)
                Interlocked.Increment(ref terminalCount);
        };

        _ = await backend.GetNetworkStatusAsync(CancellationToken.None);
        for (var index = 0; index < 6; index++)
        {
            await backend.RequestWifiScanAsync(CancellationToken.None);
            _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Scanning);
            adapter.SetAvailableWifiSnapshot(new(index + 1, NativeWifiScanState.Ready, []));
            adapter.RaiseChanged();
            _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Ready);
        }
        await backend.RequestWifiScanAsync(CancellationToken.None);
        _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Scanning);
        Assert.Equal(7, deadlines.Count);

        adapter.SetAvailableWifiSnapshot(new(7, NativeWifiScanState.Scanning, []));
        adapter.BlockNextRead();
        adapter.RaiseChanged();
        Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));
        for (var index = 0; index < deadlines.Count; index++) deadlines.Fire(index);
        Assert.Equal(4, backend.DeadlineCommandsQueued);
        Assert.Equal(1, backend.PendingDeadlineOverflowCount);
        adapter.AllowRead.Set();

        _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Unavailable);
        await backend.DisposeAsync();
        Assert.Equal(1, terminalCount);
        Assert.Equal(0, backend.DeadlineCommandsQueued);
        Assert.Equal(0, backend.PendingDeadlineOverflowCount);
    }

    private static async Task ScanDeadlineSurvivesOrdinaryCapacity()
    {
        var adapter = AdapterWithSavedProfile("native-scan-capacity");
        var deadlines = new ManualNetworkDeadlineScheduler();
        var backend = new WindowsNetworkPlatformBackend(
            new FakeFactory(adapter), TimeSpan.FromSeconds(30), deadlines);
        var wifiEvents = Channel.CreateUnbounded<AvailableWifiNetworksSummary>();
        var terminalCount = 0;
        backend.EventPublished += (_, value) =>
        {
            if (value.Payload is not AvailableWifiNetworksChangedEvent wifi) return;
            wifiEvents.Writer.TryWrite(wifi.Snapshot);
            if (wifi.Snapshot.ScanState == WifiScanState.Unavailable)
                Interlocked.Increment(ref terminalCount);
        };

        _ = await backend.GetNetworkStatusAsync(CancellationToken.None);
        await backend.RequestWifiScanAsync(CancellationToken.None);
        _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Scanning);
        adapter.SetAvailableWifiSnapshot(new(1, NativeWifiScanState.Scanning, []));
        adapter.BlockNextRead();
        adapter.RaiseChanged();
        Assert.True(adapter.ReadEntered.Wait(TimeSpan.FromSeconds(2)));

        var ordinary = await FillOrdinaryCapacityAsync(backend);
        deadlines.Fire(0);
        Assert.Equal(WindowsNetworkPlatformBackend.MaximumOrdinaryQueuedCommands,
            backend.OrdinaryCommandsQueued);
        Assert.Equal(1, backend.DeadlineCommandsQueued);
        adapter.AllowRead.Set();

        await Task.WhenAll(ordinary);
        _ = await ReadWifiAsync(wifiEvents.Reader, WifiScanState.Unavailable);
        await backend.DisposeAsync();
        Assert.Equal(1, terminalCount);
        Assert.Equal(0, backend.OrdinaryCommandsQueued);
        Assert.Equal(0, backend.DeadlineCommandsQueued);
    }

    private static async Task<Task[]> FillOrdinaryCapacityAsync(
        WindowsNetworkPlatformBackend backend)
    {
        var commands = Enumerable.Range(
                0, WindowsNetworkPlatformBackend.MaximumOrdinaryQueuedCommands)
            .Select(_ => backend.SetWifiRadioAsync(enabled: true, CancellationToken.None))
            .ToArray();
        Assert.Equal(
            WindowsNetworkPlatformBackend.MaximumOrdinaryQueuedCommands,
            backend.OrdinaryCommandsQueued);
        await Assert.ThrowsBrokerAsync(
            () => backend.SetWifiRadioAsync(enabled: true, CancellationToken.None),
            "provider_busy");
        return commands;
    }

    private static FakeNativeAdapter AdapterWithSavedProfile(string nativeKey) => new(
        new NativeNetworkSnapshot(
            NetworkConnectivity.None,
            NativeNetworkMedium.None,
            null,
            null,
            null,
            [new NativeSavedNetworkProfile(nativeKey, "Saved", false, null)]));

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
    private int _blockNextScheduleReturn;

    public int Count => _entries.Count;
    public ManualResetEventSlim ScheduleEntered { get; } = new(false);
    public ManualResetEventSlim AllowScheduleReturn { get; } = new(false);

    public void BlockNextScheduleReturn()
    {
        ScheduleEntered.Reset();
        AllowScheduleReturn.Reset();
        Interlocked.Exchange(ref _blockNextScheduleReturn, 1);
    }

    public IDisposable Schedule(TimeSpan dueTime, Action callback)
    {
        Assert.True(dueTime > TimeSpan.Zero);
        var entry = new Entry(callback);
        _entries.Add(entry);
        if (Interlocked.Exchange(ref _blockNextScheduleReturn, 0) != 0)
        {
            ScheduleEntered.Set();
            Assert.True(AllowScheduleReturn.Wait(TimeSpan.FromSeconds(5)));
        }
        return entry;
    }

    public void Fire(int index) => _entries[index].Fire();

    private sealed class Entry(Action callback) : IDisposable
    {
        private readonly Action _callback = callback;
        private int _fired;
        public bool IsDisposed { get; private set; }
        public void Fire()
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0) _callback();
        }
        public void Dispose() => IsDisposed = true;
    }
}

internal sealed class ManualCommandAdmissionObserver(Type commandType)
    : INetworkCommandAdmissionObserver
{
    private readonly Type _commandType = commandType;
    private int _blocked;

    public ManualResetEventSlim ReservationEntered { get; } = new(false);
    public ManualResetEventSlim AllowAdmission { get; } = new(false);
    public NetworkCommand? ObservedCommand { get; private set; }

    public void AfterReservation(NetworkCommand command)
    {
        if (command.GetType() != _commandType ||
            Interlocked.Exchange(ref _blocked, 1) != 0) return;
        ObservedCommand = command;
        ReservationEntered.Set();
        Assert.True(AllowAdmission.Wait(TimeSpan.FromSeconds(10)));
    }
}
