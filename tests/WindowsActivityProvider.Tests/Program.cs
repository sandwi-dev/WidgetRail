using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsActivityProvider;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Observation starts lazily after authorized broker read reaches provider", StartsLazily),
    ("Foreground events deduplicate reorder and bound sanitized apps", DeduplicatesAndBounds),
    ("Private surfaces are filtered without pretending most-recent means foreground", FiltersPrivateProcesses),
    ("Destroyed and stale windows disappear safely", RemovesDestroyedAndStale),
    ("Opaque IDs rotate when an application process lifetime changes", RotatesIdAcrossProcessLifetimes),
    ("Activation accepts only live opaque observations", ActivatesOnlyLiveOpaqueIds),
    ("Path-like executable metadata never becomes a public display name", RejectsPathLikeDisplayMetadata),
    ("Activation permission alone cannot start private observation", ControlCannotStartObservation),
    ("Provider events expose no process or window identifiers", PayloadIsSanitized),
    ("Real WinEvent adapter starts snapshots and disposes without polling", NativeAdapterSmoke),
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
Console.WriteLine($"WindowsActivityProvider.Tests passed ({tests.Length} tests)");

static async Task StartsLazily()
{
    var native = new FakeNative();
    await using var provider = new WindowsActivityPlatformBackend(native);
    Assert.False(provider.IsStarted);
    native.Add(1, 10, "Notepad", "Notepad");
    native.Foreground = 1;
    var snapshot = await provider.GetRecentActivitiesAsync(default);
    Assert.True(provider.IsStarted);
    Assert.Equal(1, native.StartCalls);
    Assert.Equal("Notepad", snapshot.Single().DisplayName);
}

static async Task DeduplicatesAndBounds()
{
    var native = new FakeNative();
    await using var provider = new WindowsActivityPlatformBackend(native);
    await provider.GetRecentActivitiesAsync(default);
    for (var index = 1; index <= WindowsActivityPlatformBackend.MaximumActivities + 3; index++)
    {
        native.Add(index, (uint)(100 + index), "App" + index, "Application " + index);
        native.Publish(NativeActivityEventKind.Foreground, index);
    }
    await WaitUntil(async () => (await provider.GetRecentActivitiesAsync(default)).Count ==
        WindowsActivityPlatformBackend.MaximumActivities);
    native.Add(100, 900, "App5", "Application Five");
    native.Publish(NativeActivityEventKind.Foreground, 100);
    await WaitUntil(async () =>
        (await provider.GetRecentActivitiesAsync(default)).First().DisplayName == "Application Five");
    var snapshot = await provider.GetRecentActivitiesAsync(default);
    Assert.Equal(WindowsActivityPlatformBackend.MaximumActivities, snapshot.Count);
    Assert.Equal(1, snapshot.Count(item => item.DisplayName == "Application Five"));
    Assert.True(snapshot[0].IsMostRecent);
    Assert.True(snapshot.All(item => item.Kind == RecentActivityKind.Application));
}

static async Task FiltersPrivateProcesses()
{
    var native = new FakeNative();
    await using var provider = new WindowsActivityPlatformBackend(native);
    await provider.GetRecentActivitiesAsync(default);
    native.Add(1, 11, "OverlayHost", "Game Bar Alternative");
    native.Add(2, 12, "RecentAppsWidget.Worker", "Recent Apps Worker");
    native.Add(3, 13, "Dwm", "Desktop Window Manager");
    native.Add(4, 14, "RealApp", "Real Application");
    native.Publish(NativeActivityEventKind.Foreground, 4);
    for (var window = 1; window <= 3; window++)
        native.Publish(NativeActivityEventKind.Foreground, window);
    await WaitUntil(async () => (await provider.GetRecentActivitiesAsync(default)).Count == 1);
    var snapshot = (await provider.GetRecentActivitiesAsync(default)).Single();
    Assert.Equal("Real Application", snapshot.DisplayName);
    Assert.True(snapshot.IsMostRecent);
}

static async Task RemovesDestroyedAndStale()
{
    var native = new FakeNative();
    native.Add(1, 10, "One", "One");
    native.Foreground = 1;
    await using var provider = new WindowsActivityPlatformBackend(native);
    var first = (await provider.GetRecentActivitiesAsync(default)).Single();
    native.Publish(NativeActivityEventKind.Destroyed, 1);
    await WaitUntil(async () => (await provider.GetRecentActivitiesAsync(default)).Count == 0);

    native.Add(2, 20, "Two", "Two");
    native.Publish(NativeActivityEventKind.Foreground, 2);
    await WaitUntil(async () => (await provider.GetRecentActivitiesAsync(default)).Count == 1);
    native.Available.Remove(2);
    Assert.Equal(0, (await provider.GetRecentActivitiesAsync(default)).Count);
    Assert.True(first.ActivityId.StartsWith("activity-", StringComparison.Ordinal));
}

static async Task ActivatesOnlyLiveOpaqueIds()
{
    var native = new FakeNative();
    native.Add(1, 10, "One", "One");
    native.Foreground = 1;
    await using var provider = new WindowsActivityPlatformBackend(native);
    var activity = (await provider.GetRecentActivitiesAsync(default)).Single();
    await provider.ActivateRecentActivityAsync(activity.ActivityId, default);
    Assert.Equal((nint)1, native.LastActivated);

    native.ActivationAllowed = false;
    var denied = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.ActivateRecentActivityAsync(activity.ActivityId, default));
    Assert.Equal("activation_denied", denied.Code);
    native.ActivationAllowed = true;

    native.Available.Remove(1);
    var missing = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.ActivateRecentActivityAsync(activity.ActivityId, default));
    Assert.Equal("resource_not_found", missing.Code);

    var unknown = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.ActivateRecentActivityAsync("activity-not-observed", default));
    Assert.Equal("resource_not_found", unknown.Code);
}

static async Task RotatesIdAcrossProcessLifetimes()
{
    var native = new FakeNative();
    native.Add(1, 10, "SameApp", "First lifetime");
    native.Foreground = 1;
    await using var provider = new WindowsActivityPlatformBackend(native);
    var original = (await provider.GetRecentActivitiesAsync(default)).Single();

    native.Add(2, 20, "SameApp", "Second lifetime");
    native.Publish(NativeActivityEventKind.Foreground, 2);
    await WaitUntil(async () =>
        (await provider.GetRecentActivitiesAsync(default)).Single().DisplayName == "Second lifetime");
    var replacement = (await provider.GetRecentActivitiesAsync(default)).Single();
    Assert.False(original.ActivityId == replacement.ActivityId);

    var stale = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.ActivateRecentActivityAsync(original.ActivityId, default));
    Assert.Equal("resource_not_found", stale.Code);
    await provider.ActivateRecentActivityAsync(replacement.ActivityId, default);
    Assert.Equal((nint)2, native.LastActivated);
}

static Task RejectsPathLikeDisplayMetadata()
{
    Assert.Equal("Safe Product", WindowsActivityNativeAdapter.ResolveDisplayName(
        @"C:\Users\person\Private\app.exe", "Safe Product", "privateTool"));
    Assert.Equal("Private Tool", WindowsActivityNativeAdapter.ResolveDisplayName(
        "../private/app.exe", @"D:\Games\Private\app.exe", "PrivateTool"));
    Assert.Equal("Friendly App", WindowsActivityNativeAdapter.ResolveDisplayName(
        "Friendly App", "Ignored Product", "privateTool"));
    return Task.CompletedTask;
}

static async Task ControlCannotStartObservation()
{
    var native = new FakeNative();
    native.Add(1, 10, "One", "One");
    native.Foreground = 1;
    await using var provider = new WindowsActivityPlatformBackend(native);
    var exception = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.ActivateRecentActivityAsync("activity-untrusted", default));
    Assert.Equal("resource_not_found", exception.Code);
    Assert.False(provider.IsStarted);
    Assert.Equal(0, native.StartCalls);
}

static async Task PayloadIsSanitized()
{
    var native = new FakeNative();
    await using var provider = new WindowsActivityPlatformBackend(native);
    RecentActivitiesChangedEvent? observed = null;
    provider.EventPublished += (_, platformEvent) =>
        observed = platformEvent.Payload as RecentActivitiesChangedEvent;
    await provider.GetRecentActivitiesAsync(default);
    native.Add(0x1234, 8675309, "PrivateProcess", "Safe App");
    native.Publish(NativeActivityEventKind.Foreground, 0x1234);
    await WaitUntil(() => Task.FromResult(observed is not null));
    var json = JsonSerializer.Serialize(observed);
    Assert.False(json.Contains("8675309", StringComparison.Ordinal));
    Assert.False(json.Contains("4660", StringComparison.Ordinal));
    Assert.False(json.Contains("PrivateProcess", StringComparison.Ordinal));
    Assert.True(json.Contains("Safe App", StringComparison.Ordinal));
}

static async Task NativeAdapterSmoke()
{
    if (!OperatingSystem.IsWindows()) return;
    await using var provider = new WindowsActivityPlatformBackend();
    var snapshot = await provider.GetRecentActivitiesAsync(default)
        .WaitAsync(TimeSpan.FromSeconds(3));
    Assert.True(snapshot.Count <= WindowsActivityPlatformBackend.MaximumActivities);
    Assert.True(snapshot.All(item => item.ActivityId.StartsWith("activity-", StringComparison.Ordinal) &&
        item.DisplayName.Length is > 0 and <= 160));
}

static async Task WaitUntil(Func<Task<bool>> condition, int timeoutMilliseconds = 2000)
{
    var deadline = Environment.TickCount64 + timeoutMilliseconds;
    while (!await condition())
    {
        if (Environment.TickCount64 >= deadline) throw new TimeoutException();
        await Task.Delay(10);
    }
}

file sealed class FakeNative : IWindowsActivityNativeAdapter
{
    private Action<NativeActivityEvent>? _handler;
    internal Dictionary<nint, NativeActivityCandidate> Candidates { get; } = [];
    internal HashSet<nint> Available { get; } = [];
    internal nint Foreground { get; set; }
    internal nint LastActivated { get; private set; }
    internal int StartCalls { get; private set; }
    internal bool ActivationAllowed { get; set; } = true;

    internal void Add(nint window, uint pid, string processKey, string displayName)
    {
        Candidates[window] = new(window, pid, processKey.ToUpperInvariant(), displayName);
        Available.Add(window);
    }

    internal void Publish(NativeActivityEventKind kind, nint window) =>
        _handler?.Invoke(new(kind, window));

    public IDisposable Start(Action<NativeActivityEvent> handler)
    {
        StartCalls++;
        _handler = handler;
        return new CallbackDisposable(() => _handler = null);
    }

    public nint GetCurrentForegroundWindow() => Foreground;
    public NativeActivityCandidate? InspectWindow(nint window) =>
        Available.Contains(window) && Candidates.TryGetValue(window, out var candidate)
            ? candidate : null;
    public bool IsWindowAvailable(nint window, uint expectedProcessId) =>
        Available.Contains(window) && Candidates.TryGetValue(window, out var candidate) &&
        candidate.ProcessId == expectedProcessId;
    public bool TryActivate(nint window, uint expectedProcessId)
    {
        if (!ActivationAllowed || !IsWindowAvailable(window, expectedProcessId)) return false;
        LastActivated = window;
        return true;
    }
    public void Dispose() => _handler = null;

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }
}

file static class Assert
{
    internal static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }
    internal static void False(bool value) => True(!value);
    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
    internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
