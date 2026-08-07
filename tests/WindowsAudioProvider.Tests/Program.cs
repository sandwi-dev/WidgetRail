using System.Threading.Channels;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsAudioProvider;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Construction and event subscription are completely inert", ConstructionIsLazy),
    ("Sessions are sanitized, bounded, opaque, and stable through churn", SessionsAreSafeAndStable),
    ("Controls execute on the dedicated native owner thread", ControlsUseOwnerThread),
    ("Master output controls reconcile and fail independently from sessions", MasterOutputIsIndependent),
    ("Native callbacks coalesce and the provider never polls", CallbacksCoalesceWithoutPolling),
    ("Live native failure and recovery publish explicit availability", ProviderAvailabilityEvents),
    ("An explicit GET reports then retries one transient enumeration failure", ExplicitGetRecoversTransientFailure),
    ("An explicit GET reports then retries a degraded endpoint bind", ExplicitGetRecoversDegradedBind),
    ("Endpoint generations discard stale retained session callbacks", EndpointGenerationsDiscardStaleSessions),
    ("Cancelled queued controls never reach Core Audio", CancelledControlsDoNotExecute),
    ("Unavailable Core Audio is distinct from a healthy empty session list", UnavailableAudioIsDegraded),
    ("Disposal unregisters native resources on the owner thread", DisposalIsOwnerThreadSafe),
    ("Production Core Audio adapter initializes without leaking native identity", ProductionAdapterSmoke),
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
    Assert.False(privateSession.SessionId.Contains("4242", StringComparison.Ordinal));
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

static async Task ProductionAdapterSmoke()
{
    if (!OperatingSystem.IsWindows()) return;
    await using var backend = new WindowsAudioPlatformBackend();
    var sessions = await backend.GetAudioSessionsAsync(CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(5));
    foreach (var session in sessions)
    {
        Assert.True(session.SessionId.StartsWith("audio_", StringComparison.Ordinal));
        Assert.True(session.SessionId.Length <= 128);
        Assert.True(session.DisplayName.Length is > 0 and <= 160);
        Assert.False(session.DisplayName.Any(char.IsControl));
        Assert.True(session.Volume is >= 0 and <= 1);
    }
}

static async Task ProductionSessionControlSmoke()
{
    if (!OperatingSystem.IsWindows() ||
        !string.Equals(Environment.GetEnvironmentVariable("GBA_TEST_LIVE_AUDIO_CONTROL"), "1",
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
    public NativeAudioOutputSnapshot? Output { get; set; } = new(0.5, false);
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
