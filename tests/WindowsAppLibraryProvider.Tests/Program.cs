using System.Text.Json;
using GameBarAlternative.WindowsAppLibraryProvider;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Catalog is lazy cached and refreshable", LazyAndRefreshable),
    ("Duplicate registrations prefer current-user entries", DeduplicatesByInternalIdentity),
    ("Distinct applications with the same display name remain distinct", PreservesNameCollisions),
    ("Names and catalog size are bounded and sanitized", SanitizesAndBounds),
    ("Opaque IDs are stable only while the registration remains current", OpaqueIdLifecycle),
    ("Public payload contains no trusted launch descriptors", PayloadIsOpaque),
    ("Only current opaque IDs resolve inside the trusted provider", ResolvesOnlyCurrentIds),
    ("Cancellation reaches the source without publishing partial state", CancellationIsAtomic),
    ("Real Start Menu scan is read-only bounded and sanitized", NativeReadOnlySmoke),
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
Console.WriteLine($"WindowsAppLibraryProvider.Tests passed ({tests.Length} tests)");

static async Task LazyAndRefreshable()
{
    var source = new FakeSource(Reg("one", "One", StartMenuScope.CurrentUser, @"C:\Menu\One.lnk"));
    var provider = new WindowsAppLibraryProvider(source);
    Assert.Equal(0, source.Calls);
    Assert.Equal(1, (await provider.GetAppsAsync()).Count);
    Assert.Equal(1, source.Calls);
    Assert.Equal(1, (await provider.GetAppsAsync()).Count);
    Assert.Equal(1, source.Calls);
    source.Items = [Reg("two", "Two", StartMenuScope.CurrentUser, @"C:\Menu\Two.lnk")];
    var refreshed = await provider.RefreshAsync();
    Assert.Equal("Two", refreshed.Single().DisplayName);
    Assert.Equal(2, source.Calls);
}

static async Task DeduplicatesByInternalIdentity()
{
    var source = new FakeSource(
        Reg("same", "Common Name", StartMenuScope.AllUsers, @"C:\Common\Same.lnk"),
        Reg("same", "My Name", StartMenuScope.CurrentUser, @"C:\User\Same.lnk"));
    var provider = new WindowsAppLibraryProvider(source);
    var item = (await provider.GetAppsAsync()).Single();
    Assert.Equal("My Name", item.DisplayName);
    Assert.True(provider.TryResolveForLaunch(item.AppId, out var registration));
    Assert.Equal(@"C:\User\Same.lnk", registration!.ShortcutPath);
}

static async Task PreservesNameCollisions()
{
    var provider = new WindowsAppLibraryProvider(new FakeSource(
        Reg("one", "Editor", StartMenuScope.CurrentUser, @"C:\One.lnk"),
        Reg("two", "Editor", StartMenuScope.CurrentUser, @"C:\Two.lnk")));
    var items = await provider.GetAppsAsync();
    Assert.Equal(2, items.Count);
    Assert.Equal(2, items.Select(item => item.AppId).Distinct(StringComparer.Ordinal).Count());
}

static async Task SanitizesAndBounds()
{
    var items = Enumerable.Range(0, WindowsAppLibraryProvider.MaximumApps + 20)
        .Select(index => Reg(
            "id-" + index,
            index == 0 ? "  A\t\r\nName\0\u202e  " + new string('x', 200) : $"App {index:0000}",
            StartMenuScope.CurrentUser,
            $@"C:\Menu\App{index}.lnk"))
        .Append(Reg("empty", "\0\r\n", StartMenuScope.CurrentUser, @"C:\Empty.lnk"))
        .ToArray();
    var snapshot = await new WindowsAppLibraryProvider(new FakeSource(items)).GetAppsAsync();
    Assert.Equal(WindowsAppLibraryProvider.MaximumApps, snapshot.Count);
    Assert.True(snapshot.All(item => item.DisplayName.Length is > 0 and <=
        WindowsAppLibraryProvider.MaximumDisplayNameLength));
    Assert.True(snapshot.All(item => !item.DisplayName.Any(char.IsControl)));
    Assert.True(snapshot.Any(item => item.DisplayName.StartsWith("A Name", StringComparison.Ordinal)));
}

static async Task OpaqueIdLifecycle()
{
    var source = new FakeSource(Reg("one", "One", StartMenuScope.CurrentUser, @"C:\One.lnk"));
    var provider = new WindowsAppLibraryProvider(source);
    var original = (await provider.GetAppsAsync()).Single().AppId;
    Assert.Equal(original, (await provider.RefreshAsync()).Single().AppId);

    source.Items = [];
    Assert.Equal(0, (await provider.RefreshAsync()).Count);
    source.Items = [Reg("one", "One", StartMenuScope.CurrentUser, @"C:\One.lnk")];
    var replacement = (await provider.RefreshAsync()).Single().AppId;
    Assert.False(original == replacement);
    Assert.True(replacement.StartsWith("app-", StringComparison.Ordinal));
    Assert.Equal(36, replacement.Length);
}

static async Task PayloadIsOpaque()
{
    const string targetPath = @"C:\Users\private\Secret Game.lnk";
    const string identity = "sensitive-target-and---launch-arguments";
    var provider = new WindowsAppLibraryProvider(new FakeSource(
        Reg(identity, "Safe Game", StartMenuScope.CurrentUser, targetPath)));
    var json = JsonSerializer.Serialize(await provider.GetAppsAsync());
    Assert.False(json.Contains("private", StringComparison.OrdinalIgnoreCase));
    Assert.False(json.Contains("sensitive-target", StringComparison.Ordinal));
    Assert.False(json.Contains("launch-arguments", StringComparison.Ordinal));
    Assert.True(json.Contains("Safe Game", StringComparison.Ordinal));
}

static async Task ResolvesOnlyCurrentIds()
{
    var source = new FakeSource(Reg("one", "One", StartMenuScope.CurrentUser, @"C:\One.lnk"));
    var provider = new WindowsAppLibraryProvider(source);
    var id = (await provider.GetAppsAsync()).Single().AppId;
    Assert.True(provider.TryResolveForLaunch(id, out var registration));
    Assert.Equal(@"C:\One.lnk", registration!.ShortcutPath);
    Assert.False(provider.TryResolveForLaunch("app-00000000000000000000000000000000", out _));
    source.Items = [];
    await provider.RefreshAsync();
    Assert.False(provider.TryResolveForLaunch(id, out _));
}

static async Task CancellationIsAtomic()
{
    var source = new BlockingSource();
    var provider = new WindowsAppLibraryProvider(source);
    using var cancellation = new CancellationTokenSource();
    var scan = provider.GetAppsAsync(cancellation.Token);
    await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() => scan);
    Assert.False(provider.TryResolveForLaunch("app-anything", out _));
}

static async Task NativeReadOnlySmoke()
{
    if (!OperatingSystem.IsWindows()) return;
    var provider = new WindowsAppLibraryProvider();
    var snapshot = await provider.GetAppsAsync().WaitAsync(TimeSpan.FromSeconds(10));
    Assert.True(snapshot.Count <= WindowsAppLibraryProvider.MaximumApps);
    Assert.Equal(snapshot.Count,
        snapshot.Select(item => item.AppId).Distinct(StringComparer.Ordinal).Count());
    Assert.True(snapshot.All(item =>
        item.AppId.StartsWith("app-", StringComparison.Ordinal) &&
        item.AppId.Length == 36 &&
        item.DisplayName.Length is > 0 and <= WindowsAppLibraryProvider.MaximumDisplayNameLength &&
        item.Kind == WindowsAppLibraryKind.Application));
    Console.WriteLine($"INFO Real Start Menu catalog contains {snapshot.Count} launchable shortcuts");
}

static StartMenuRegistration Reg(
    string identity,
    string name,
    StartMenuScope scope,
    string path) => new(identity, name, scope, path);

file sealed class FakeSource(params StartMenuRegistration[] items) : IStartMenuApplicationSource
{
    public IReadOnlyList<StartMenuRegistration> Items { get; set; } = items;
    public int Calls { get; private set; }
    public IReadOnlyList<StartMenuRegistration> Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Items.ToArray();
    }
}

file sealed class BlockingSource : IStartMenuApplicationSource
{
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyList<StartMenuRegistration> Enumerate(CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        cancellationToken.WaitHandle.WaitOne();
        cancellationToken.ThrowIfCancellationRequested();
        return [];
    }
}

file static class Assert
{
    public static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }

    public static void False(bool condition) => True(!condition);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
