using System.Threading.Channels;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAudioProvider;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Construction and event subscription are completely inert", ConstructionIsLazy),
    ("Sessions are sanitized, bounded, opaque, and stable through churn", SessionsAreSafeAndStable),
    ("Controls execute on the dedicated native owner thread", ControlsUseOwnerThread),
    ("Master output controls reconcile and fail independently from sessions", MasterOutputIsIndependent),
    ("Devices stay opaque and default microphone controls reconcile", DevicesAndInputAreSafe),
    ("Device switching is opaque confirmed and rejects stale queued controls", DeviceSwitchingIsSafe),
    ("Spatial read and control failures never disable ordinary audio", SpatialFailuresAreIsolated),
    ("Spatial device identity requires the exact enabled output interface", SpatialIdentityIsExact),
    ("Spatial subtype resolution includes newer runtime DTS metadata", SpatialSubtypeResolution),
    ("Production spatial support agrees with the Windows interface query", ProductionSpatialSupportMatchesWindows),
    ("Default-device policy preserves communications and rolls back partial writes", DeviceSwitchPolicy),
    ("Native callbacks coalesce and the provider never polls", CallbacksCoalesceWithoutPolling),
    ("Live native failure and recovery publish explicit availability", ProviderAvailabilityEvents),
    ("An explicit GET reports then retries one transient enumeration failure", ExplicitGetRecoversTransientFailure),
    ("An explicit GET reports then retries a degraded endpoint bind", ExplicitGetRecoversDegradedBind),
    ("Endpoint generations discard stale retained session callbacks", EndpointGenerationsDiscardStaleSessions),
    ("Cancelled queued controls never reach Core Audio", CancelledControlsDoNotExecute),
    ("Unavailable Core Audio is distinct from a healthy empty session list", UnavailableAudioIsDegraded),
    ("Disposal unregisters native resources on the owner thread", DisposalIsOwnerThreadSafe),
    ("Production Core Audio adapter initializes without leaking native identity", ProductionAdapterSmoke),
    ("Windows default-device policy COM interface is available without changing devices", PolicyInterfaceProbe),
    ("Production Core Audio applies and restores opt-in per-session controls", ProductionSessionControlSmoke),
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

Console.WriteLine($"Executed {tests.Length} Windows audio provider tests; {failures} failed.");
return failures == 0 ? 0 : 1;

static Task SpatialSubtypeResolution()
{
    Assert.Equal<string?>(null, WindowsSpatialAudioAdapter.ResolveFormatSubtype("UnknownSpatialFormat"));
    Assert.Equal(Windows.Media.Audio.SpatialAudioFormatSubtype.WindowsSonic,
        WindowsSpatialAudioAdapter.ResolveFormatSubtype("WindowsSonic"));
    var available = Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(
        "Windows.Media.Audio.SpatialAudioFormatSubtype", "DTSXForHomeTheater");
    Assert.Equal(available ? "{10201B4A-3322-4967-BF40-2CAA9BAFCA44}" : null,
        WindowsSpatialAudioAdapter.ResolveFormatSubtype("DTSXForHomeTheater"));
    return Task.CompletedTask;
}

static Task SpatialIdentityIsExact()
{
    const string endpoint = "{0.0.0.00000000}.{11111111-2222-3333-4444-555555555555}";
    var instance = "SWD\\MMDEVAPI\\" + endpoint;
    Assert.Equal("interface-correct", SpatialAudioDeviceIdentity.Resolve(endpoint,
        [new("interface-wrong-default", "SWD\\MMDEVAPI\\other", true),
         new("interface-disabled", instance, false), new("interface-correct", instance.ToLowerInvariant(), true)]));
    Assert.Equal<string?>(null, SpatialAudioDeviceIdentity.Resolve(endpoint,
        [new("interface-name-only", null, true), new("interface-partial", instance + "suffix", true)]));
    Assert.Equal<string?>(null, SpatialAudioDeviceIdentity.Resolve(endpoint,
        [new("interface-a", instance, true), new("interface-b", instance, true)]));
    Assert.Equal<string?>(null, SpatialAudioDeviceIdentity.Resolve("", [new("any", instance, true)]));
    return Task.CompletedTask;
}

static Task ProductionSpatialSupportMatchesWindows()
{
    if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
    var initialized = CoreAudioInterop.InitializeMta();
    try
    {
        using var native = new CoreAudioNativeAdapter();
        var endpoint = native.EnumerateDevices().FirstOrDefault(device =>
            device.Direction == NativeAudioDeviceDirection.Output && device.IsDefault);
        if (endpoint is null) return Task.CompletedTask;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var interfaces = Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                Windows.Media.Devices.MediaDevice.GetAudioRenderSelector(), new[] { SpatialAudioDeviceIdentity.InstanceIdProperty })
            .AsTask(deadline.Token).GetAwaiter().GetResult();
        var matches = interfaces.Where(item => item.IsEnabled &&
            item.Properties.TryGetValue(SpatialAudioDeviceIdentity.InstanceIdProperty, out var value) &&
            string.Equals(value as string, "SWD\\MMDEVAPI\\" + endpoint.NativeDeviceKey, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(1, matches.Length);
        var windows = Windows.Media.Audio.SpatialAudioDeviceConfiguration.GetForDeviceId(matches[0].Id);
        for (var index = 0; index < 2; index++)
        {
            var actual = native.GetSpatialAudio();
            Assert.True(actual is not null);
            Assert.Equal(endpoint.NativeDeviceKey, actual!.NativeDeviceKey);
            Assert.Equal(windows.IsSpatialAudioSupported, actual.IsSupported);
            Assert.Equal(windows.IsSpatialAudioFormatSupported(Windows.Media.Audio.SpatialAudioFormatSubtype.WindowsSonic),
                actual.Formats.Any(format => format.FormatId == "sonic"));
            Assert.Equal(windows.IsSpatialAudioFormatSupported(Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHeadphones),
                actual.Formats.Any(format => format.FormatId == "atmos-headphones"));
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(
                    "Windows.Media.Audio.SpatialAudioFormatSubtype", "DTSXForHomeTheater"))
                Assert.Equal(windows.IsSpatialAudioFormatSupported("{10201B4A-3322-4967-BF40-2CAA9BAFCA44}"),
                    actual.Formats.Any(format => format.FormatId == "dts-home"));
        }
    }
    finally { if (initialized) CoreAudioInterop.Uninitialize(); }
    return Task.CompletedTask;
}

static async Task SpatialFailuresAreIsolated()
{
    var adapter = new FakeNativeAdapter([new("session", "Game", 0.5, false, true)]);
    adapter.Spatial = new("native-output", true, "off", "off", [new("off", "Off"), new("sonic", "Windows Sonic")]);
    await using var backend = new WindowsAudioPlatformBackend(new FakeFactory(adapter));
    var value = await backend.GetAudioSpatialAsync(CancellationToken.None);
    Assert.True(value.DeviceId.StartsWith("device_", StringComparison.Ordinal));
    await backend.SetAudioSpatialFormatAsync(value.DeviceId, "sonic", CancellationToken.None);
    Assert.Equal("sonic", (await backend.GetAudioSpatialAsync(CancellationToken.None)).SelectedFormatId);
    await Assert.ThrowsBrokerAsync(() => backend.SetAudioSpatialFormatAsync("old-output", "sonic", CancellationToken.None), "resource_not_found");
    await Assert.ThrowsBrokerAsync(() => backend.SetAudioSpatialFormatAsync(value.DeviceId, "invented", CancellationToken.None), "spatial_not_supported");
    foreach (var result in new[] { "spatial_license_required", "spatial_access_denied", "spatial_timeout", "spatial_not_supported" })
    {
        adapter.SpatialSetResult = result;
        await Assert.ThrowsBrokerAsync(() => backend.SetAudioSpatialFormatAsync(value.DeviceId, "off", CancellationToken.None), result);
        Assert.Equal(1, (await backend.GetAudioSessionsAsync(CancellationToken.None)).Count);
        await backend.SetAudioOutputVolumeAsync(0.4, CancellationToken.None);
        Assert.Equal(0.4, (await backend.GetAudioOutputAsync(CancellationToken.None)).Volume);
        Assert.False(backend.IsDegraded);
    }
    adapter.ThrowSpatial = true;
    await Assert.ThrowsBrokerAsync(() => backend.SetAudioSpatialFormatAsync(value.DeviceId, "off", CancellationToken.None), "resource_not_found");
    await Assert.ThrowsBrokerAsync(() => backend.GetAudioSpatialAsync(CancellationToken.None), "spatial_unavailable");
    Assert.Equal(1, (await backend.GetAudioSessionsAsync(CancellationToken.None)).Count);
    Assert.False(backend.IsDegraded);
    adapter.ThrowSpatial = false;
    adapter.ThrowSpatialSet = true;
    _ = await backend.GetAudioSpatialAsync(CancellationToken.None);
    await Assert.ThrowsBrokerAsync(() => backend.SetAudioSpatialFormatAsync(value.DeviceId, "off", CancellationToken.None), "spatial_unavailable");
    Assert.False(backend.IsDegraded);
    adapter.ThrowSpatialSet = false;
    adapter.SpatialSetResult = "ok";
    await backend.SetAudioSpatialFormatAsync(value.DeviceId, "off", CancellationToken.None);
    Assert.Equal("off", (await backend.GetAudioSpatialAsync(CancellationToken.None)).SelectedFormatId);
}

static Task DeviceSwitchPolicy()
{
    var state = new Dictionary<ERole, string> { [ERole.Console] = "speakers", [ERole.Multimedia] = "speakers", [ERole.Communications] = "calls" };
    var writes = new List<ERole>();
    Assert.True(DefaultAudioDeviceSwitch.TrySwitch("headphones", role => state[role], (id, role) =>
    { writes.Add(role); state[role] = id; return true; }));
    Assert.Equal("calls", state[ERole.Communications]);
    Assert.SequenceEqual(new[] { ERole.Console, ERole.Multimedia }, writes);
    state[ERole.Console] = state[ERole.Multimedia] = "speakers";
    Assert.False(DefaultAudioDeviceSwitch.TrySwitch("headphones", role => state[role], (id, role) =>
    { if (role == ERole.Multimedia) return false; state[role] = id; return true; }));
    Assert.Equal("speakers", state[ERole.Console]);
    Assert.Equal("speakers", state[ERole.Multimedia]);
    Assert.False(DefaultAudioDeviceSwitch.TrySwitch("headphones", role => state[role], (id, role) => true));
    Assert.Equal("calls", state[ERole.Communications]);
    Assert.False(DefaultAudioDeviceSwitch.TrySwitch("headphones", role => state[role], (id, role) =>
    { if (role == ERole.Multimedia) { state[ERole.Console] = "external"; return false; } state[role] = id; return true; }));
    Assert.Equal("external", state[ERole.Console]);
    return Task.CompletedTask;
}

static async Task DeviceSwitchingIsSafe()
{
    var adapter = new FakeNativeAdapter([]);
    adapter.Devices = [.. adapter.Devices, new("native-headset", "Headphones", NativeAudioDeviceDirection.Output, false),
        new("native-mic", "USB mic", NativeAudioDeviceDirection.Input, false)];
    await using var backend = new WindowsAudioPlatformBackend(new FakeFactory(adapter));
    var devices = await backend.GetAudioDevicesAsync(CancellationToken.None);
    var headset = devices.Single(device => device.DisplayName == "Headphones");
    var mic = devices.Single(device => device.DisplayName == "USB mic");
    await Assert.ThrowsBrokerAsync(() => backend.SetDefaultAudioOutputDeviceAsync(mic.DeviceId, CancellationToken.None), "invalid_payload");
    await Assert.ThrowsBrokerAsync(() => backend.SetDefaultAudioOutputDeviceAsync("native-headset", CancellationToken.None), "resource_not_found");
    Assert.Equal(0, adapter.DeviceSwitchCalls);
    adapter.BlockDeviceSwitch = true;
    var switching = backend.SetDefaultAudioOutputDeviceAsync(headset.DeviceId, CancellationToken.None);
    Assert.True(adapter.DeviceSwitchEntered.Wait(TimeSpan.FromSeconds(2)));
    var staleVolume = backend.SetAudioOutputVolumeAsync(0.9, CancellationToken.None);
    adapter.AllowDeviceSwitch.Set();
    await switching;
    await Assert.ThrowsBrokerAsync(() => staleVolume, "resource_not_found");
    Assert.Equal(0.5, adapter.Output!.Volume);
    Assert.True((await backend.GetAudioDevicesAsync(CancellationToken.None)).Single(device => device.DeviceId == headset.DeviceId).IsDefault);
    adapter.BlockDeviceSwitch = false;
    await backend.SetDefaultAudioInputDeviceAsync(mic.DeviceId, CancellationToken.None);
    Assert.True((await backend.GetAudioDevicesAsync(CancellationToken.None)).Single(device => device.DeviceId == mic.DeviceId).IsDefault);
    Assert.Equal(1, adapter.NativeCallThreadIds.Distinct().Count());
    adapter.Devices = adapter.Devices.Where(device => device.NativeDeviceKey != "native-headset").ToArray();
    await Assert.ThrowsBrokerAsync(() => backend.SetDefaultAudioOutputDeviceAsync(headset.DeviceId, CancellationToken.None), "resource_not_found");
    Assert.Equal(2, adapter.DeviceSwitchCalls);
    // A successful HRESULT without the new default being observed is not success.
    adapter.IgnoreDeviceSwitch = true;
    var speakers = (await backend.GetAudioDevicesAsync(CancellationToken.None)).Single(device => device.DisplayName == "Speakers");
    await Assert.ThrowsBrokerAsync(() => backend.SetDefaultAudioOutputDeviceAsync(speakers.DeviceId, CancellationToken.None), "platform_unavailable");
}

static async Task ConstructionIsLazy()
{
    var adapter = new FakeNativeAdapter([]);
    var factory = new FakeFactory(adapter);
    var backend = new WindowsAudioPlatformBackend(factory);
    backend.EventPublished += (_, _) => { };
    await Task.Delay(100);

    Assert.False(backend.IsStarted);
    Assert.Equal(0, factory.CreateCalls);
    await backend.DisposeAsync();
    Assert.Equal(0, factory.CreateCalls);
    Assert.False(adapter.IsDisposed); // Never created, so it was never owned by the provider.
}

static async Task SessionsAreSafeAndStable()
{
    const string secretKey = @"C:\games\private\game.exe|pid=4242|endpoint=secret";
    var longName = " Game\u0001 Audio " + new string('x', 300);
    var adapter = new FakeNativeAdapter([
        new(secretKey, @"C:\private\secret.exe", 0.75, false, true),
        new("long-name-secret", longName, 0.3, false, true),
        new("inactive-secret", "Chat", 0.2, true, false),
    ]);
    await using var backend = new WindowsAudioPlatformBackend(new FakeFactory(adapter));

    var initial = await backend.GetAudioSessionsAsync(CancellationToken.None);
    Assert.Equal(3, initial.Count);
    Assert.True(initial[0].IsActive);
    var privateSession = initial.Single(session => session.DisplayName == "Application audio");
    Assert.True(privateSession.SessionId.StartsWith("audio_", StringComparison.Ordinal));
    Assert.True(Guid.TryParseExact(privateSession.SessionId.AsSpan(6), "N", out _));
    Assert.False(privateSession.SessionId.Contains("endpoint", StringComparison.OrdinalIgnoreCase));
    Assert.False(initial.Any(session => session.DisplayName.Contains("private", StringComparison.OrdinalIgnoreCase)));
    Assert.True(initial.All(session => session.DisplayName.Length <= 160));
    Assert.False(initial.Any(session => session.DisplayName.Any(char.IsControl)));
    var stableId = privateSession.SessionId;

    var events = EventChannel(backend);
    adapter.SetSnapshots([
        new(secretKey, "Renamed", 0.5, false, true),
        new("new-native-secret", "Chat", 0.4, false, true),
    ]);
    adapter.RaiseChanged();
    var changed = await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(stableId, changed.Sessions.Single(session => session.DisplayName == "Renamed").SessionId);
    Assert.False(changed.Sessions.Any(session => session.SessionId.Contains("native", StringComparison.Ordinal)));
}

static async Task ControlsUseOwnerThread()
{
    var adapter = new FakeNativeAdapter([new("native-one", "Game", 0.4, false, true)]);
    var factory = new FakeFactory(adapter);
    await using var backend = new WindowsAudioPlatformBackend(factory);
    var session = Assert.Single(await backend.GetAudioSessionsAsync(CancellationToken.None));

    await backend.SetAudioSessionVolumeAsync(session.SessionId, 0.8, CancellationToken.None);
    await backend.SetAudioSessionMutedAsync(session.SessionId, true, CancellationToken.None);
    var updated = Assert.Single(await backend.GetAudioSessionsAsync(CancellationToken.None));

    Assert.Equal(0.8, updated.Volume, precision: 0.0001);
    Assert.True(updated.IsMuted);
    Assert.Equal(2, adapter.ControlCalls);
    Assert.True(factory.CreateThreadId != Environment.CurrentManagedThreadId);
    Assert.True(adapter.NativeCallThreadIds.All(id => id == factory.CreateThreadId));
}

static async Task MasterOutputIsIndependent()
{
    var adapter = new FakeNativeAdapter([new("native-one", "Game", 0.4, false, true)])
    {
        Output = new NativeAudioOutputSnapshot(0.45, false),
    };
    var factory = new FakeFactory(adapter);
    await using var backend = new WindowsAudioPlatformBackend(factory);

    var initial = await backend.GetAudioOutputAsync(CancellationToken.None);
    Assert.Equal(0.45, initial.Volume, precision: 0.0001);
    Assert.False(initial.IsMuted);
    await backend.SetAudioOutputVolumeAsync(0.7, CancellationToken.None);
    await backend.SetAudioOutputMutedAsync(true, CancellationToken.None);
    var controlled = await backend.GetAudioOutputAsync(CancellationToken.None);
    Assert.Equal(0.7, controlled.Volume, precision: 0.0001);
    Assert.True(controlled.IsMuted);
    Assert.True(adapter.NativeCallThreadIds.All(id => id == factory.CreateThreadId));

    var outputEvents = OutputEventChannel(backend);
    adapter.IsOutputDegraded = true;
    adapter.Output = null;
    adapter.RaiseChanged();
    var unavailable = await outputEvents.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(2));
    Assert.False(unavailable.IsAvailable);
    Assert.True(unavailable.Output is null);
    Assert.Equal(1, (await backend.GetAudioSessionsAsync(CancellationToken.None)).Count);

    adapter.Output = new NativeAudioOutputSnapshot(0.2, false);
    adapter.IsOutputDegraded = false;
    var recovered = await backend.GetAudioOutputAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(0.2, recovered.Volume, precision: 0.0001);
    var available = await outputEvents.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(2));
    Assert.True(available.IsAvailable);
    Assert.True(available.Output is not null);
}

static async Task DevicesAndInputAreSafe()
{
    const string privateOutputId = @"SWD\MMDEVAPI\{private-output}";
    const string privateInputId = @"SWD\MMDEVAPI\{private-input}";
    var adapter = new FakeNativeAdapter([])
    {
        Devices =
        [
            new(privateOutputId, "Speakers", NativeAudioDeviceDirection.Output, true),
            new(privateInputId, @"C:\private\microphone.sys", NativeAudioDeviceDirection.Input, true),
        ],
        Input = new NativeAudioInputSnapshot(0.35, false),
    };
    await using var backend = new WindowsAudioPlatformBackend(new FakeFactory(adapter));

    var devices = await backend.GetAudioDevicesAsync(CancellationToken.None);
    Assert.Equal(2, devices.Count);
    Assert.True(devices.All(device => device.DeviceId.StartsWith("device_", StringComparison.Ordinal)));
    Assert.False(devices.Any(device => device.DeviceId.Contains("MMDEVAPI", StringComparison.OrdinalIgnoreCase)));
    Assert.Equal("Microphone", devices.Single(device =>
        device.Direction == AudioDeviceDirection.Input).DisplayName);
    var stableId = devices.Single(device => device.Direction == AudioDeviceDirection.Output).DeviceId;

    adapter.Devices =
    [
        new(privateOutputId, "Renamed speakers", NativeAudioDeviceDirection.Output, true),
        new(privateInputId, "USB microphone", NativeAudioDeviceDirection.Input, true),
    ];
    adapter.RaiseChanged();
    await WaitUntilAsync(() => adapter.EnumerationCalls >= 2);
    var refreshed = await backend.GetAudioDevicesAsync(CancellationToken.None);
    Assert.Equal(stableId, refreshed.Single(device =>
        device.Direction == AudioDeviceDirection.Output).DeviceId);

    var input = await backend.GetAudioInputAsync(CancellationToken.None);
    Assert.Equal(0.35, input.Volume, precision: 0.0001);
    await backend.SetAudioInputVolumeAsync(0.7, CancellationToken.None);
    await backend.SetAudioInputMutedAsync(true, CancellationToken.None);
    input = await backend.GetAudioInputAsync(CancellationToken.None);
    Assert.Equal(0.7, input.Volume, precision: 0.0001);
    Assert.True(input.IsMuted);
}

static async Task CallbacksCoalesceWithoutPolling()
{
    var adapter = new FakeNativeAdapter([new("native-one", "Game", 0.4, false, true)]);
    await using var backend = new WindowsAudioPlatformBackend(new FakeFactory(adapter));
    _ = await backend.GetAudioSessionsAsync(CancellationToken.None);
    Assert.Equal(1, adapter.EnumerationCalls);

    await Task.Delay(120);
    Assert.Equal(1, adapter.EnumerationCalls); // No timer or periodic poll.

    adapter.BlockNextEnumeration();
    adapter.RaiseChanged();
    Assert.True(adapter.EnumerationEntered.Wait(TimeSpan.FromSeconds(2)));
    for (var index = 0; index < 100; index++) adapter.RaiseChanged();
    adapter.AllowEnumeration.Set();
    await WaitUntilAsync(() => adapter.EnumerationCalls >= 3);
    await Task.Delay(100);
    Assert.Equal(3, adapter.EnumerationCalls); // One blocked refresh plus one coalesced follow-up.
}

static async Task ExplicitGetRecoversTransientFailure()
{
    var adapter = new FakeNativeAdapter([new("native-one", "Recovered game", 0.6, false, true)]);
    adapter.FailNextEnumeration();
    await using var backend = new WindowsAudioPlatformBackend(new FakeFactory(adapter));

    await Assert.ThrowsBrokerAsync(
        () => backend.GetAudioSessionsAsync(CancellationToken.None),
        "platform_unavailable");
    Assert.True(backend.IsDegraded);
    Assert.Equal(1, adapter.EnumerationCalls);

    var recovered = await backend.GetAudioSessionsAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("Recovered game", Assert.Single(recovered).DisplayName);
    Assert.False(backend.IsDegraded);
    Assert.Equal(2, adapter.EnumerationCalls);

    _ = await backend.GetAudioSessionsAsync(CancellationToken.None);
    Assert.Equal(2, adapter.EnumerationCalls); // Healthy reads use the event-maintained cache.
}

static async Task ProviderAvailabilityEvents()
{
    var adapter = new FakeNativeAdapter([]);
    await using var backend = new WindowsAudioPlatformBackend(new FakeFactory(adapter));
    _ = await backend.GetAudioSessionsAsync(CancellationToken.None);
    var events = EventChannel(backend);

    adapter.FailNextEnumeration();
    adapter.RaiseChanged();
    var unavailable = await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.False(unavailable.IsAvailable);
    Assert.Equal(0, unavailable.Sessions.Count);
    // The next explicit read is the bounded recovery action.
    var recovered = await backend.GetAudioSessionsAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(0, recovered.Count);
    var available = await events.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    Assert.True(available.IsAvailable);
}

static async Task ExplicitGetRecoversDegradedBind()
{
    var adapter = new FakeNativeAdapter([new("native-one", "Bound game", 0.6, false, true)]);
    adapter.DegradeNextEnumeration();
    await using var backend = new WindowsAudioPlatformBackend(new FakeFactory(adapter));

    await Assert.ThrowsBrokerAsync(
        () => backend.GetAudioSessionsAsync(CancellationToken.None),
        "platform_unavailable");
    Assert.True(backend.IsDegraded);
    var recovered = await backend.GetAudioSessionsAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("Bound game", Assert.Single(recovered).DisplayName);
    Assert.False(backend.IsDegraded);
    Assert.Equal(2, adapter.EnumerationCalls);
}

static Task EndpointGenerationsDiscardStaleSessions()
{
    var queue = new EndpointGenerationQueue<int>();
    queue.Enqueue(4, 40);
    queue.Enqueue(5, 50);
    queue.Enqueue(4, 41);
    var released = new List<int>();

    var current = queue.DrainCurrent(5, released.Add).ToArray();
    Assert.Equal(1, current.Length);
    Assert.Equal(50, current[0]);
    Assert.SequenceEqual([40, 41], released);

    queue.Enqueue(6, 60);
    queue.DrainAll(released.Add);
    Assert.SequenceEqual([40, 41, 60], released);
    return Task.CompletedTask;
}

static async Task CancelledControlsDoNotExecute()
{
    var adapter = new FakeNativeAdapter([new("native-one", "Game", 0.4, false, true)]);
    await using var backend = new WindowsAudioPlatformBackend(new FakeFactory(adapter));
    var session = Assert.Single(await backend.GetAudioSessionsAsync(CancellationToken.None));

    adapter.BlockNextEnumeration();
    adapter.RaiseChanged();
    Assert.True(adapter.EnumerationEntered.Wait(TimeSpan.FromSeconds(2)));
    using var cancellation = new CancellationTokenSource();
    var control = backend.SetAudioSessionMutedAsync(session.SessionId, true, cancellation.Token);
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => control);
    adapter.AllowEnumeration.Set();
    await Task.Delay(100);
    Assert.Equal(0, adapter.ControlCalls);
}

static async Task UnavailableAudioIsDegraded()
{
    await using var backend = new WindowsAudioPlatformBackend(new ThrowingFactory());
    await Assert.ThrowsBrokerAsync(
        () => backend.GetAudioSessionsAsync(CancellationToken.None),
        "platform_unavailable");
    Assert.True(backend.IsDegraded);

    await using var healthyEmpty = new WindowsAudioPlatformBackend(
        new FakeFactory(new FakeNativeAdapter([])));
    Assert.Equal(0, (await healthyEmpty.GetAudioSessionsAsync(CancellationToken.None)).Count);
    Assert.False(healthyEmpty.IsDegraded);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    await Assert.ThrowsBrokerAsync(
        () => backend.SetAudioSessionMutedAsync("audio_dead", true, timeout.Token),
        "platform_unavailable");
}

static async Task DisposalIsOwnerThreadSafe()
{
    var adapter = new FakeNativeAdapter([new("native-one", "Game", 0.4, false, true)]);
    var factory = new FakeFactory(adapter);
    var backend = new WindowsAudioPlatformBackend(factory);
    _ = await backend.GetAudioSessionsAsync(CancellationToken.None);
    await backend.DisposeAsync();
    adapter.RaiseChanged();

    Assert.True(adapter.IsDisposed);
    Assert.Equal(factory.CreateThreadId, adapter.DisposeThreadId);
    await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () =>
        _ = await backend.GetAudioSessionsAsync(CancellationToken.None));
}

static Task PolicyInterfaceProbe()
{
    if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
    var initialized = CoreAudioInterop.InitializeMta();
    object? policy = null;
    IntPtr unknown = IntPtr.Zero, contract = IntPtr.Zero;
    try
    {
        var type = Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), true)!;
        policy = Activator.CreateInstance(type);
        unknown = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(policy!);
        var iid = new Guid("F8679F50-850A-41CF-9C72-430F290290C8");
        Assert.Equal(0, System.Runtime.InteropServices.Marshal.QueryInterface(unknown, ref iid, out contract));
        Assert.True(contract != IntPtr.Zero);
    }
    finally
    {
        if (contract != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(contract);
        if (unknown != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(unknown);
        CoreAudioInterop.Release(policy);
        if (initialized) CoreAudioInterop.Uninitialize();
    }
    return Task.CompletedTask;
}

static async Task ProductionAdapterSmoke()
{
    if (!OperatingSystem.IsWindows()) return;
    bool hasDefaultOutput;
    var apartmentInitialized = CoreAudioInterop.InitializeMta();
    try
    {
        using var native = new CoreAudioNativeAdapter();
        var nativeDevices = native.EnumerateDevices();
        hasDefaultOutput = nativeDevices.Any(device =>
            device.Direction == NativeAudioDeviceDirection.Output && device.IsDefault);
        if (hasDefaultOutput)
        {
            var spatial = native.GetSpatialAudio();
            Assert.True(spatial is not null);
            Assert.True(spatial!.Formats.Any(format => format.FormatId == "off"));
        }
        if (native.IsDeviceListDegraded)
            throw new InvalidOperationException(
                $"Native audio-device enumeration failed ({native.DeviceListDiagnostic}).");
    }
    finally
    {
        if (apartmentInitialized) CoreAudioInterop.Uninitialize();
    }
    await using var backend = new WindowsAudioPlatformBackend();
    IReadOnlyList<AudioSessionSummary> sessions;
    try
    {
        sessions = await backend.GetAudioSessionsAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch (BrokerException exception) when (!hasDefaultOutput && exception.Code == "platform_unavailable")
    {
        // Hosted Windows runners can enumerate devices successfully while
        // having no default render endpoint on which to open session control.
        Assert.Equal("Windows audio is temporarily unavailable.", exception.Message);
        sessions = [];
        Console.WriteLine("Audio smoke: no default output; verified the bounded unavailable result.");
    }
    foreach (var session in sessions)
    {
        Assert.True(session.SessionId.StartsWith("audio_", StringComparison.Ordinal));
        Assert.True(session.SessionId.Length <= 128);
        Assert.True(session.DisplayName.Length is > 0 and <= 160);
        Assert.False(session.DisplayName.Any(char.IsControl));
        Assert.True(session.Volume is >= 0 and <= 1);
    }
    var devices = await backend.GetAudioDevicesAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(5));
    Assert.True(devices.All(device => device.DeviceId.StartsWith("device_", StringComparison.Ordinal)));
    Assert.True(devices.All(device => device.DisplayName.Length is > 0 and <= 160));
    Assert.False(devices.Any(device => device.DisplayName.Any(char.IsControl)));
    if (devices.Any(device => device.Direction == AudioDeviceDirection.Input && device.IsDefault))
    {
        var input = await backend.GetAudioInputAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(input.Volume is >= 0 and <= 1);
    }
}

static async Task ProductionSessionControlSmoke()
{
    if (!OperatingSystem.IsWindows() ||
        !string.Equals(Environment.GetEnvironmentVariable("WRAIL_TEST_LIVE_AUDIO_CONTROL"), "1",
            StringComparison.Ordinal)) return;
    await using var backend = new WindowsAudioPlatformBackend();
    var sessions = await backend.GetAudioSessionsAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(5));
    var session = sessions.OrderBy(candidate => candidate.IsActive).FirstOrDefault();
    if (session is null) return;

    var target = session.Volume <= 0.95
        ? Math.Min(1, session.Volume + 0.05)
        : Math.Max(0, session.Volume - 0.05);
    Console.WriteLine(
        $"LIVE AUDIO session='{session.DisplayName}' original={Percent(session.Volume)}% target={Percent(target)}%");
    try
    {
        await backend.SetAudioSessionVolumeAsync(
            session.SessionId, target, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var changed = (await backend.GetAudioSessionsAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)))
            .Single(candidate => candidate.SessionId == session.SessionId);
        Console.WriteLine(
            $"LIVE AUDIO session='{session.DisplayName}' readback={Percent(changed.Volume)}%");
        Assert.Equal(target, changed.Volume, precision: 0.001);
    }
    finally
    {
        await backend.SetAudioSessionVolumeAsync(
            session.SessionId, session.Volume, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var restored = (await backend.GetAudioSessionsAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)))
            .Single(candidate => candidate.SessionId == session.SessionId);
        Console.WriteLine(
            $"LIVE AUDIO session='{session.DisplayName}' restored={Percent(restored.Volume)}%");
        Assert.Equal(session.Volume, restored.Volume, precision: 0.001);
    }
}

static int Percent(double value) =>
    (int)Math.Round(Math.Clamp(value, 0, 1) * 100, MidpointRounding.AwayFromZero);

static Channel<AudioSessionsChangedEvent> EventChannel(WindowsAudioPlatformBackend backend)
{
    var channel = Channel.CreateUnbounded<AudioSessionsChangedEvent>();
    backend.EventPublished += (_, platformEvent) =>
    {
        if (platformEvent.Payload is AudioSessionsChangedEvent audio) channel.Writer.TryWrite(audio);
    };
    return channel;
}

static Channel<AudioOutputChangedEvent> OutputEventChannel(WindowsAudioPlatformBackend backend)
{
    var channel = Channel.CreateUnbounded<AudioOutputChangedEvent>();
    backend.EventPublished += (_, platformEvent) =>
    {
        if (platformEvent.Payload is AudioOutputChangedEvent output) channel.Writer.TryWrite(output);
    };
    return channel;
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

sealed class FakeFactory(FakeNativeAdapter adapter) : IWindowsAudioNativeAdapterFactory
{
    public int CreateThreadId { get; private set; }
    public int CreateCalls { get; private set; }
    public IWindowsAudioNativeAdapter Create()
    {
        CreateCalls++;
        CreateThreadId = Environment.CurrentManagedThreadId;
        return adapter;
    }
}

sealed class ThrowingFactory : IWindowsAudioNativeAdapterFactory
{
    public IWindowsAudioNativeAdapter Create() => throw new InvalidOperationException("Audio service absent.");
}

sealed class FakeNativeAdapter(IEnumerable<NativeAudioSessionSnapshot> initial) : IWindowsAudioNativeAdapter
{
    private readonly object _gate = new();
    private List<NativeAudioSessionSnapshot> _snapshots = initial.ToList();
    private int _blockNext;
    private int _enumerationCalls;
    private int _controlCalls;
    private int _failEnumerations;
    private int _degradeEnumerations;
    private int _isDegraded;
    public event EventHandler? StateChanged;
    public bool IsDegraded => Volatile.Read(ref _isDegraded) != 0;
    public bool IsOutputDegraded { get; set; }
    public bool IsDeviceListDegraded { get; set; }
    public bool IsInputDegraded { get; set; }
    public NativeAudioOutputSnapshot? Output { get; set; } = new(0.5, false);
    public NativeAudioInputSnapshot? Input { get; set; } = new(0.5, false);
    public IReadOnlyList<NativeAudioDeviceSnapshot> Devices { get; set; } =
    [
        new("native-output", "Speakers", NativeAudioDeviceDirection.Output, true),
        new("native-input", "Microphone", NativeAudioDeviceDirection.Input, true),
    ];
    public ManualResetEventSlim EnumerationEntered { get; } = new(false);
    public ManualResetEventSlim AllowEnumeration { get; } = new(false);
    public List<int> NativeCallThreadIds { get; } = [];
    public int EnumerationCalls => Volatile.Read(ref _enumerationCalls);
    public int ControlCalls => Volatile.Read(ref _controlCalls);
    public bool IsDisposed { get; private set; }
    public int DisposeThreadId { get; private set; }

    public void SetSnapshots(IEnumerable<NativeAudioSessionSnapshot> snapshots)
    {
        lock (_gate) _snapshots = snapshots.ToList();
    }

    public void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
    public void FailNextEnumeration() => Interlocked.Increment(ref _failEnumerations);
    public void DegradeNextEnumeration() => Interlocked.Increment(ref _degradeEnumerations);

    public void BlockNextEnumeration()
    {
        EnumerationEntered.Reset();
        AllowEnumeration.Reset();
        Interlocked.Exchange(ref _blockNext, 1);
    }

    public IReadOnlyList<NativeAudioSessionSnapshot> EnumerateSessions()
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        Interlocked.Increment(ref _enumerationCalls);
        if (Interlocked.Exchange(ref _failEnumerations, 0) != 0)
            throw new InvalidOperationException("Transient native enumeration failure.");
        if (Interlocked.Exchange(ref _degradeEnumerations, 0) != 0)
        {
            Volatile.Write(ref _isDegraded, 1);
            return [];
        }
        Volatile.Write(ref _isDegraded, 0);
        if (Interlocked.Exchange(ref _blockNext, 0) != 0)
        {
            EnumerationEntered.Set();
            AllowEnumeration.Wait(TimeSpan.FromSeconds(5));
        }
        lock (_gate) return _snapshots.ToArray();
    }

    public NativeAudioOutputSnapshot? GetDefaultOutput()
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        return Output;
    }

    public IReadOnlyList<NativeAudioDeviceSnapshot> EnumerateDevices()
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        return Devices;
    }

    public NativeAudioInputSnapshot? GetDefaultInput()
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        return Input;
    }

    public bool TrySetSessionVolume(string nativeSessionKey, double volume)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        Interlocked.Increment(ref _controlCalls);
        lock (_gate)
        {
            var index = _snapshots.FindIndex(item => item.NativeSessionKey == nativeSessionKey);
            if (index < 0) return false;
            _snapshots[index] = _snapshots[index] with { Volume = volume };
            return true;
        }
    }

    public bool TrySetSessionMuted(string nativeSessionKey, bool isMuted)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        Interlocked.Increment(ref _controlCalls);
        lock (_gate)
        {
            var index = _snapshots.FindIndex(item => item.NativeSessionKey == nativeSessionKey);
            if (index < 0) return false;
            _snapshots[index] = _snapshots[index] with { IsMuted = isMuted };
            return true;
        }
    }

    public bool TrySetDefaultOutputVolume(double volume)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        Interlocked.Increment(ref _controlCalls);
        if (Output is null || IsOutputDegraded) return false;
        Output = Output with { Volume = volume };
        return true;
    }

    public bool TrySetDefaultOutputMuted(bool isMuted)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        Interlocked.Increment(ref _controlCalls);
        if (Output is null || IsOutputDegraded) return false;
        Output = Output with { IsMuted = isMuted };
        return true;
    }

    public bool TrySetDefaultInputVolume(double volume)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        Interlocked.Increment(ref _controlCalls);
        if (Input is null || IsInputDegraded) return false;
        Input = Input with { Volume = volume };
        return true;
    }

    public bool TrySetDefaultInputMuted(bool isMuted)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        Interlocked.Increment(ref _controlCalls);
        if (Input is null || IsInputDegraded) return false;
        Input = Input with { IsMuted = isMuted };
        return true;
    }

    public NativeSpatialAudioSnapshot? Spatial { get; set; }
    public bool ThrowSpatial { get; set; }
    public bool ThrowSpatialSet { get; set; }
    public string SpatialSetResult { get; set; } = "ok";
    public NativeSpatialAudioSnapshot? GetSpatialAudio()
    {
        if (ThrowSpatial) throw new InvalidOperationException("Injected native read failure");
        return Spatial;
    }
    public string SetSpatialFormat(string nativeDeviceKey, string formatId)
    {
        if (ThrowSpatialSet) throw new InvalidOperationException("Injected native setter failure");
        if (SpatialSetResult == "ok" && Spatial is not null)
            Spatial = Spatial with { SelectedFormatId = formatId, ActiveFormatId = formatId };
        return SpatialSetResult;
    }

    public int DeviceSwitchCalls { get; private set; }
    public bool BlockDeviceSwitch { get; set; }
    public bool IgnoreDeviceSwitch { get; set; }
    public ManualResetEventSlim DeviceSwitchEntered { get; } = new(false);
    public ManualResetEventSlim AllowDeviceSwitch { get; } = new(false);
    public bool TrySetDefaultDevice(string nativeDeviceKey, NativeAudioDeviceDirection direction)
    {
        NativeCallThreadIds.Add(Environment.CurrentManagedThreadId);
        DeviceSwitchCalls++;
        if (BlockDeviceSwitch) { DeviceSwitchEntered.Set(); AllowDeviceSwitch.Wait(TimeSpan.FromSeconds(3)); }
        if (!IgnoreDeviceSwitch)
            Devices = Devices.Select(device => device.Direction == direction
                ? device with { IsDefault = device.NativeDeviceKey == nativeDeviceKey } : device).ToArray();
        return true;
    }

    public void Dispose()
    {
        IsDisposed = true;
        DisposeThreadId = Environment.CurrentManagedThreadId;
        EnumerationEntered.Dispose();
        AllowEnumeration.Dispose();
    }
}

static class Assert
{
    public static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }
    public static void False(bool condition) => True(!condition);
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
    public static void Equal(double expected, double actual, double precision)
    {
        if (Math.Abs(expected - actual) > precision)
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
    public static T Single<T>(IReadOnlyList<T> values)
    {
        if (values.Count != 1) throw new InvalidOperationException($"Expected one item, got {values.Count}.");
        return values[0];
    }
    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Sequences differ.");
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
