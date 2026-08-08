using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsBluetoothProvider;

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
    public bool HoldStartUntilCanceled { get; set; }
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int StartCalls { get; private set; }
    public int SetCalls { get; private set; }
    public int DisposeCalls { get; private set; }
    public bool? LastEnabled { get; private set; }

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

    public void EmitChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
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
