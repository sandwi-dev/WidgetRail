using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsBluetoothProvider;
using Windows.Devices.Enumeration;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Provider is lazy and starts one event-driven adapter", LazyStart),
    ("Native Bluetooth identifiers are replaced with stable opaque tokens", OpaqueIdentity),
    ("Device snapshots are bounded sanitized and consistently ordered", SanitizedBoundedSnapshot),
    ("Discovery events publish full coalescible snapshots", EventSnapshot),
    ("Bluetooth radio control reconciles effective state", RadioControl),
    ("Bluetooth radio failures are typed and sanitized", RadioFailures),
    ("Non-ready discovery never exposes stale devices", NonReadyDiscovery),
    ("Disposal detaches and releases the Windows adapter", Disposal),
    ("Canceled startup rolls back and a later call retries cleanly", StartupCancellationRetries),
    ("Partial radio mutation is typed and publishes authoritative state", PartialRadioFailure),
    ("Opaque identity retention stays bounded under device churn", OpaqueRetentionIsBounded),
    ("Pairing resolves only opaque current devices and reconciles state", PairingReconciles),
    ("Pairing cancellation propagates without optimistic state", PairingCancellation),
    ("Windows pairing statuses are mapped without claiming connection", PairingStatusMapping),
    ("Unpairing resolves one current paired device and reconciles removal", UnpairingReconciles),
    ("Unpair cancellation and stale identity never remove a neighbor", UnpairingCancellation),
    ("Windows unpairing statuses map to the closed destructive outcome", UnpairingStatusMapping),
    ("Bluetooth Settings handoff is exact validated and authoritative", SettingsHandoff),
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

static async Task LazyStart()
{
    var adapter = ReadyAdapter();
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    Assert.Equal(0, adapter.StartCalls);
    var first = await backend.GetBluetoothAsync(CancellationToken.None);
    var second = await backend.GetBluetoothAsync(CancellationToken.None);
    Assert.Equal(1, adapter.StartCalls);
    Assert.Equal(BluetoothRadioState.On, first.RadioState);
    Assert.Equal(first, second);
}

static async Task OpaqueIdentity()
{
    var adapter = ReadyAdapter(Device("native-address-11:22", "Controller", paired: true));
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    var first = await backend.GetBluetoothAsync(CancellationToken.None);
    var opaque = first.Devices.Single().DeviceId;
    Assert.True(opaque.StartsWith("bluetooth-", StringComparison.Ordinal));
    Assert.True(!opaque.Contains("native", StringComparison.OrdinalIgnoreCase));

    adapter.Snapshot = adapter.Snapshot with
    {
        Devices = [Device("native-address-11:22", "Renamed controller", paired: true)],
    };
    adapter.EmitChanged();
    var second = await backend.GetBluetoothAsync(CancellationToken.None);
    Assert.Equal(opaque, second.Devices.Single().DeviceId);
    Assert.True(!JsonSerializer.Serialize(second).Contains(
        "native-address", StringComparison.OrdinalIgnoreCase));
}

static async Task SanitizedBoundedSnapshot()
{
    var devices = Enumerable.Range(0, 80)
        .Select(index => Device($"native-{index}",
            index == 0 ? "  Gamepad\0  " : $"Device {index:00}",
            paired: index % 2 == 0, present: true,
            connected: index == 2))
        .Append(Device("ignored", "Hidden", paired: false, present: false))
        .ToArray();
    var adapter = ReadyAdapter(devices);
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    var snapshot = await backend.GetBluetoothAsync(CancellationToken.None);
    Assert.Equal(64, snapshot.Devices.Count);
    Assert.Equal("Device 02", snapshot.Devices[0].DisplayName);
    Assert.True(snapshot.Devices.All(device => device.IsPaired || device.IsPresent));
    Assert.True(snapshot.Devices.All(device => !device.DisplayName.Any(char.IsControl)));
    Assert.True(snapshot.Devices.All(device => device.DisplayName != "Hidden"));
}

static async Task EventSnapshot()
{
    var adapter = ReadyAdapter(Device("native-a", "Headset", paired: true));
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    await backend.GetBluetoothAsync(CancellationToken.None);
    BrokerPlatformEvent? published = null;
    backend.EventPublished += (_, change) => published = change;
    adapter.Snapshot = adapter.Snapshot with
    {
        Devices = [Device("native-a", "Headset", paired: true, connected: true)],
    };
    adapter.EmitChanged();
    Assert.True(published is not null);
    Assert.Equal(PlatformCapabilities.NetworkBluetoothReadV1, published!.CapabilityId);
    Assert.Equal(PlatformCapabilities.NetworkBluetoothChanged, published.EventType);
    var change = (BluetoothChangedEvent)published.Payload;
    Assert.True(change.Snapshot.Devices.Single().IsConnected);
}

static async Task RadioControl()
{
    var adapter = ReadyAdapter();
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    await backend.SetBluetoothRadioAsync(false, CancellationToken.None);
    Assert.Equal(1, adapter.SetCalls);
    Assert.Equal(false, adapter.LastEnabled);
    Assert.Equal(BluetoothRadioState.Off,
        (await backend.GetBluetoothAsync(CancellationToken.None)).RadioState);
}

static async Task RadioFailures()
{
    var cases = new[]
    {
        (NativeBluetoothRadioSetResult.NoAdapter, "no_adapter"),
        (NativeBluetoothRadioSetResult.HardwareDisabled, "hardware_disabled"),
        (NativeBluetoothRadioSetResult.DeniedByUser, "permission_denied"),
        (NativeBluetoothRadioSetResult.DeniedBySystem, "platform_denied"),
        (NativeBluetoothRadioSetResult.Unavailable, "platform_unavailable"),
    };
    foreach (var (native, code) in cases)
    {
        var adapter = ReadyAdapter();
        adapter.SetResult = native;
        await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
        await Assert.ThrowsBroker(
            () => backend.SetBluetoothRadioAsync(true, CancellationToken.None), code);
    }
}

static async Task NonReadyDiscovery()
{
    foreach (var state in new[]
             { NativeBluetoothDiscoveryState.Enumerating, NativeBluetoothDiscoveryState.Unavailable })
    {
        var adapter = ReadyAdapter(Device("native-secret", "Stale", paired: true));
        adapter.Snapshot = adapter.Snapshot with { DiscoveryState = state };
        await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
        var snapshot = await backend.GetBluetoothAsync(CancellationToken.None);
        Assert.Equal(0, snapshot.Devices.Count);
    }
}

static async Task Disposal()
{
    var adapter = ReadyAdapter();
    var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    await backend.GetBluetoothAsync(CancellationToken.None);
    await backend.DisposeAsync();
    Assert.Equal(1, adapter.DisposeCalls);
    adapter.EmitChanged();
    await Assert.Throws<ObjectDisposedException>(
        () => backend.GetBluetoothAsync(CancellationToken.None));
}

static async Task StartupCancellationRetries()
{
    var canceledAdapter = ReadyAdapter();
    canceledAdapter.HoldStartUntilCanceled = true;
    var retryAdapter = ReadyAdapter();
    var factory = new FakeFactory(canceledAdapter, retryAdapter);
    await using var backend = new WindowsBluetoothPlatformBackend(factory);
    using var cancellation = new CancellationTokenSource();
    var first = backend.GetBluetoothAsync(cancellation.Token);
    await canceledAdapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    await Assert.Canceled(first);
    Assert.Equal(1, canceledAdapter.DisposeCalls);

    var retry = await backend.GetBluetoothAsync(CancellationToken.None);
    Assert.Equal(BluetoothRadioState.On, retry.RadioState);
    Assert.Equal(2, factory.CreateCalls);
    Assert.Equal(1, retryAdapter.StartCalls);
}

static async Task PartialRadioFailure()
{
    var adapter = ReadyAdapter();
    adapter.SetResult = NativeBluetoothRadioSetResult.PartialFailure;
    adapter.SnapshotAfterSet = adapter.Snapshot with
    {
        RadioState = NativeBluetoothRadioState.Off,
    };
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    await backend.GetBluetoothAsync(CancellationToken.None);
    BrokerPlatformEvent? published = null;
    backend.EventPublished += (_, change) => published = change;
    await Assert.ThrowsBroker(
        () => backend.SetBluetoothRadioAsync(false, CancellationToken.None), "partial_failure");
    Assert.Equal(BluetoothRadioState.Off,
        (await backend.GetBluetoothAsync(CancellationToken.None)).RadioState);
    Assert.True(published?.Payload is BluetoothChangedEvent change &&
                change.Snapshot.RadioState == BluetoothRadioState.Off,
        "Effective partial state was not reconciled and published.");
}

static async Task OpaqueRetentionIsBounded()
{
    var adapter = ReadyAdapter();
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    await backend.GetBluetoothAsync(CancellationToken.None);
    for (var batch = 0; batch < 20; batch++)
    {
        adapter.Snapshot = adapter.Snapshot with
        {
            Devices = Enumerable.Range(0, 64)
                .Select(index => Device($"native-{batch}-{index}", $"Device {index}", present: true))
                .ToArray(),
        };
        adapter.EmitChanged();
        await backend.GetBluetoothAsync(CancellationToken.None);
    }
    var field = typeof(WindowsBluetoothPlatformBackend).GetField(
        "_opaqueIds", System.Reflection.BindingFlags.Instance |
                      System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Opaque identity store was not found.");
    var retained = (System.Collections.IDictionary)field.GetValue(backend)!;
    Assert.True(retained.Count <= 128,
        $"Opaque identity store retained {retained.Count} churned native IDs.");
}

static async Task PairingReconciles()
{
    var nativeId = "native-controller-address";
    var adapter = ReadyAdapter(Device(nativeId, "Controller", present: true));
    adapter.PairResult = BluetoothPairingOutcome.Paired;
    adapter.SnapshotAfterPair = adapter.Snapshot with
    {
        Devices = [Device(nativeId, "Controller", paired: true, present: true)],
    };
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    var opaqueId = (await backend.GetBluetoothAsync(CancellationToken.None))
        .Devices.Single().DeviceId;
    BrokerPlatformEvent? published = null;
    backend.EventPublished += (_, change) => published = change;

    var outcome = await backend.PairBluetoothDeviceAsync(
        opaqueId, CancellationToken.None);
    Assert.Equal(BluetoothPairingResultStatus.Paired, outcome.Outcome);
    Assert.Equal(1, adapter.PairCalls);
    Assert.Equal(nativeId, adapter.LastPairedNativeId);
    var effective = await backend.GetBluetoothAsync(CancellationToken.None);
    Assert.True(effective.Devices.Single().IsPaired);
    Assert.True(published?.Payload is BluetoothChangedEvent change &&
                change.Snapshot.Devices.Single().IsPaired,
        "Completed pairing did not publish the authoritative snapshot.");

    await Assert.ThrowsBroker(
        () => backend.PairBluetoothDeviceAsync(
            "bluetooth-not-current", CancellationToken.None),
        "unknown_device");
    Assert.Equal(1, adapter.PairCalls);
}

static async Task PairingCancellation()
{
    var adapter = ReadyAdapter(Device("native-pad", "Pad", present: true));
    adapter.HoldPairUntilCanceled = true;
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    var opaqueId = (await backend.GetBluetoothAsync(CancellationToken.None))
        .Devices.Single().DeviceId;
    using var cancellation = new CancellationTokenSource();
    var pending = backend.PairBluetoothDeviceAsync(opaqueId, cancellation.Token);
    await adapter.PairStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    await Assert.Canceled(pending);
    Assert.Equal(1, adapter.PairCalls);
    Assert.True(!(await backend.GetBluetoothAsync(CancellationToken.None))
        .Devices.Single().IsPaired,
        "Canceled pairing was presented optimistically as paired.");
}

static Task PairingStatusMapping()
{
    var expected = new Dictionary<DevicePairingResultStatus, BluetoothPairingOutcome>
    {
        [DevicePairingResultStatus.Paired] = BluetoothPairingOutcome.Paired,
        [DevicePairingResultStatus.NotReadyToPair] = BluetoothPairingOutcome.NotReady,
        [DevicePairingResultStatus.NotPaired] = BluetoothPairingOutcome.NotReady,
        [DevicePairingResultStatus.AlreadyPaired] = BluetoothPairingOutcome.AlreadyPaired,
        [DevicePairingResultStatus.ConnectionRejected] = BluetoothPairingOutcome.Rejected,
        [DevicePairingResultStatus.TooManyConnections] = BluetoothPairingOutcome.TooManyConnections,
        [DevicePairingResultStatus.HardwareFailure] = BluetoothPairingOutcome.HardwareFailure,
        [DevicePairingResultStatus.AuthenticationTimeout] = BluetoothPairingOutcome.AuthenticationTimedOut,
        [DevicePairingResultStatus.AuthenticationNotAllowed] = BluetoothPairingOutcome.AuthenticationNotAllowed,
        [DevicePairingResultStatus.AuthenticationFailure] = BluetoothPairingOutcome.AuthenticationFailed,
        [DevicePairingResultStatus.NoSupportedProfiles] = BluetoothPairingOutcome.NoSupportedProfiles,
        [DevicePairingResultStatus.ProtectionLevelCouldNotBeMet] = BluetoothPairingOutcome.ProtectionLevelNotMet,
        [DevicePairingResultStatus.AccessDenied] = BluetoothPairingOutcome.AccessDenied,
        [DevicePairingResultStatus.InvalidCeremonyData] = BluetoothPairingOutcome.InvalidCeremonyData,
        [DevicePairingResultStatus.PairingCanceled] = BluetoothPairingOutcome.CanceledByUser,
        [DevicePairingResultStatus.OperationAlreadyInProgress] = BluetoothPairingOutcome.OperationInProgress,
        [DevicePairingResultStatus.RequiredHandlerNotRegistered] = BluetoothPairingOutcome.UserInteractionRequired,
        [DevicePairingResultStatus.RejectedByHandler] = BluetoothPairingOutcome.Rejected,
        [DevicePairingResultStatus.RemoteDeviceHasAssociation] = BluetoothPairingOutcome.RemoteAlreadyAssociated,
        [DevicePairingResultStatus.Failed] = BluetoothPairingOutcome.Failed,
    };
    foreach (var (native, outcome) in expected)
        Assert.Equal(outcome, WindowsBluetoothNativeAdapter.MapPairingStatus(native));
    Assert.Equal(Enum.GetValues<DevicePairingResultStatus>().Length, expected.Count);
    return Task.CompletedTask;
}

static async Task UnpairingReconciles()
{
    var removedNativeId = "native-headset";
    var neighbor = Device("native-controller", "Controller", paired: true);
    var adapter = ReadyAdapter(Device(removedNativeId, "Headset", paired: true), neighbor);
    adapter.UnpairResult = BluetoothUnpairingOutcome.Unpaired;
    adapter.SnapshotAfterUnpair = adapter.Snapshot with { Devices = [neighbor] };
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    var before = await backend.GetBluetoothAsync(CancellationToken.None);
    var removed = before.Devices.Single(device => device.DisplayName == "Headset");
    var neighborOpaqueId = before.Devices.Single(device => device.DisplayName == "Controller").DeviceId;
    BrokerPlatformEvent? published = null;
    backend.EventPublished += (_, change) => published = change;

    var outcome = await backend.UnpairBluetoothDeviceAsync(
        removed.DeviceId, CancellationToken.None);

    Assert.Equal(BluetoothUnpairingResultStatus.Unpaired, outcome.Outcome);
    Assert.Equal(1, adapter.UnpairCalls);
    Assert.Equal(removedNativeId, adapter.LastUnpairedNativeId);
    var effective = await backend.GetBluetoothAsync(CancellationToken.None);
    Assert.Equal(neighborOpaqueId, effective.Devices.Single().DeviceId);
    Assert.True(effective.Devices.Single().IsPaired);
    Assert.True(published?.Payload is BluetoothChangedEvent change &&
                change.Snapshot.Devices.Count == 1,
        "Completed unpair did not publish authoritative device removal.");

    await Assert.ThrowsBroker(
        () => backend.UnpairBluetoothDeviceAsync(removed.DeviceId, CancellationToken.None),
        "unknown_device");
    Assert.Equal(1, adapter.UnpairCalls);
}

static async Task UnpairingCancellation()
{
    var adapter = ReadyAdapter(
        Device("native-headset", "Headset", paired: true),
        Device("native-controller", "Controller", paired: true));
    adapter.HoldUnpairUntilCanceled = true;
    await using var backend = new WindowsBluetoothPlatformBackend(new FakeFactory(adapter));
    var before = await backend.GetBluetoothAsync(CancellationToken.None);
    var headset = before.Devices.Single(device => device.DisplayName == "Headset");
    using var cancellation = new CancellationTokenSource();

    var pending = backend.UnpairBluetoothDeviceAsync(headset.DeviceId, cancellation.Token);
    await adapter.UnpairStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    await Assert.Canceled(pending);

    Assert.Equal(1, adapter.UnpairCalls);
    var after = await backend.GetBluetoothAsync(CancellationToken.None);
    Assert.Equal(2, after.Devices.Count);
    Assert.True(after.Devices.All(device => device.IsPaired),
        "Cancellation optimistically removed the target or its unaffected neighbor.");
}

static Task UnpairingStatusMapping()
{
    var expected = new Dictionary<DeviceUnpairingResultStatus, BluetoothUnpairingOutcome>
    {
        [DeviceUnpairingResultStatus.Unpaired] = BluetoothUnpairingOutcome.Unpaired,
        [DeviceUnpairingResultStatus.AlreadyUnpaired] =
            BluetoothUnpairingOutcome.AlreadyUnpaired,
        [DeviceUnpairingResultStatus.OperationAlreadyInProgress] =
            BluetoothUnpairingOutcome.OperationInProgress,
        [DeviceUnpairingResultStatus.AccessDenied] = BluetoothUnpairingOutcome.AccessDenied,
        [DeviceUnpairingResultStatus.Failed] = BluetoothUnpairingOutcome.Failed,
    };
    foreach (var (native, outcome) in expected)
        Assert.Equal(outcome, WindowsBluetoothNativeAdapter.MapUnpairingStatus(native));
    Assert.Equal(Enum.GetValues<DeviceUnpairingResultStatus>().Length, expected.Count);
    return Task.CompletedTask;
}

static async Task SettingsHandoff()
{
    var adapter = ReadyAdapter(Device("native-headset", "Headset", paired: true));
    var launcher = new FakeSettingsLauncher();
    await using var backend = new WindowsBluetoothPlatformBackend(
        new FakeFactory(adapter), launcher);
    var opaqueId = (await backend.GetBluetoothAsync(CancellationToken.None))
        .Devices.Single().DeviceId;

    await backend.OpenBluetoothDeviceSettingsAsync(opaqueId, CancellationToken.None);
    Assert.Equal(1, launcher.Calls);

    await Assert.ThrowsBroker(
        () => backend.OpenBluetoothDeviceSettingsAsync(
            "native-headset", CancellationToken.None),
        "unknown_device");
    Assert.Equal(1, launcher.Calls);

    launcher.Opened = false;
    await Assert.ThrowsBroker(
        () => backend.OpenBluetoothDeviceSettingsAsync(opaqueId, CancellationToken.None),
        "settings_unavailable");
    Assert.Equal(2, launcher.Calls);
}

static FakeAdapter ReadyAdapter(params NativeBluetoothDevice[] devices) => new()
{
    Snapshot = new NativeBluetoothSnapshot(
        NativeBluetoothRadioState.On, true,
        NativeBluetoothDiscoveryState.Ready, devices),
};

static NativeBluetoothDevice Device(
    string id, string name, bool paired = false, bool connected = false, bool present = true) =>
    new(id, name, paired, connected, present);

file sealed class FakeFactory(params FakeAdapter[] adapters) : IWindowsBluetoothNativeAdapterFactory
{
    private int _next;
    public int CreateCalls { get; private set; }
    public IWindowsBluetoothNativeAdapter Create()
    {
        CreateCalls++;
        if (_next >= adapters.Length)
            throw new InvalidOperationException("No fake Bluetooth adapter remains.");
        return adapters[_next++];
    }
}

file sealed class FakeAdapter : IWindowsBluetoothNativeAdapter
{
    public event EventHandler? StateChanged;
    public NativeBluetoothSnapshot Snapshot { get; set; } = new(
        NativeBluetoothRadioState.NoAdapter, false,
        NativeBluetoothDiscoveryState.Ready, []);
    public NativeBluetoothRadioSetResult SetResult { get; set; } =
        NativeBluetoothRadioSetResult.Succeeded;
    public NativeBluetoothSnapshot? SnapshotAfterSet { get; set; }
    public NativeBluetoothSnapshot? SnapshotAfterPair { get; set; }
    public NativeBluetoothSnapshot? SnapshotAfterUnpair { get; set; }
    public BluetoothPairingOutcome PairResult { get; set; } =
        BluetoothPairingOutcome.Paired;
    public bool HoldStartUntilCanceled { get; set; }
    public bool HoldPairUntilCanceled { get; set; }
    public bool HoldUnpairUntilCanceled { get; set; }
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource PairStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource UnpairStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int StartCalls { get; private set; }
    public int SetCalls { get; private set; }
    public int PairCalls { get; private set; }
    public int UnpairCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public bool? LastEnabled { get; private set; }
    public string? LastPairedNativeId { get; private set; }
    public string? LastUnpairedNativeId { get; private set; }
    public BluetoothUnpairingOutcome UnpairResult { get; set; } =
        BluetoothUnpairingOutcome.Unpaired;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCalls++;
        Started.TrySetResult();
        if (HoldStartUntilCanceled)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public NativeBluetoothSnapshot ReadSnapshot() => Snapshot;

    public Task<NativeBluetoothRadioSetResult> SetRadioAsync(
        bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetCalls++;
        LastEnabled = enabled;
        if (SnapshotAfterSet is not null) Snapshot = SnapshotAfterSet;
        if (SetResult == NativeBluetoothRadioSetResult.Succeeded)
            Snapshot = Snapshot with
            {
                RadioState = enabled
                    ? NativeBluetoothRadioState.On
                    : NativeBluetoothRadioState.Off,
            };
        return Task.FromResult(SetResult);
    }

    public async Task<BluetoothPairingOutcome> PairAsync(
        string nativeDeviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PairCalls++;
        LastPairedNativeId = nativeDeviceId;
        PairStarted.TrySetResult();
        if (HoldPairUntilCanceled)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (SnapshotAfterPair is not null) Snapshot = SnapshotAfterPair;
        return PairResult;
    }

    public async Task<BluetoothUnpairingOutcome> UnpairAsync(
        string nativeDeviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UnpairCalls++;
        LastUnpairedNativeId = nativeDeviceId;
        UnpairStarted.TrySetResult();
        if (HoldUnpairUntilCanceled)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (SnapshotAfterUnpair is not null) Snapshot = SnapshotAfterUnpair;
        return UnpairResult;
    }

    public void EmitChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }
}

file sealed class FakeSettingsLauncher : IWindowsBluetoothSettingsLauncher
{
    public bool Opened { get; set; } = true;
    public int Calls { get; private set; }

    public Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(Opened);
    }
}

file static class Assert
{
    public static void True(bool condition, string message = "Expected condition to be true.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static async Task ThrowsBroker(Func<Task> action, string code)
    {
        try { await action(); }
        catch (BrokerException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException($"Expected BrokerException '{code}'.");
    }

    public static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static async Task Canceled(Task action)
    {
        try { await action; }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected operation cancellation.");
    }
}
