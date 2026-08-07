using GameBarAlternative.Samples.ClockWidget;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetWorkerHost;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Loads a public package Widget entrypoint", () => Run(LoadsWidget)),
    ("Runs a package through the isolated worker protocol", RunsIsolatedWorker),
    ("Rejects entrypoint path escape", () => Run(RejectsPathEscape)),
    ("Rejects missing and non-Widget types", () => Run(RejectsInvalidTypes)),
    ("Rejects invalid assemblies without leaking paths", () => Run(RejectsInvalidAssembly)),
};
var failures = new List<string>();
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failures.Add($"FAIL {test.Name}: {exception}"); Console.Error.WriteLine(failures[^1]); }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static void LoadsWidget()
{
    var assembly = typeof(ClockWidget).Assembly.Location;
    var widget = WidgetAssemblyLoader.Load(
        Path.GetDirectoryName(assembly)!, assembly, typeof(ClockWidget).FullName!);
    Assert.Equal(typeof(ClockWidget).FullName, widget.GetType().FullName);
}

static async Task RunsIsolatedWorker()
{
    var root = Path.GetDirectoryName(typeof(WidgetAssemblyLoader).Assembly.Location)!;
    var executable = Path.Combine(root, "WidgetWorkerHost.exe");
    var assembly = typeof(ClockWidget).Assembly.Location;
    await using var client = new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = executable,
        Arguments =
        [
            "--package-root", Path.GetDirectoryName(assembly)!,
            "--widget-assembly", assembly,
            "--widget-type", typeof(ClockWidget).FullName!,
        ],
        WidgetInstanceId = "worker-host.test",
        ConnectTimeout = TimeSpan.FromSeconds(3),
        RequestTimeout = TimeSpan.FromSeconds(2),
        MaximumRestartAttempts = 0,
        MemoryLimitBytes = 64L * 1024 * 1024,
    });
    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal("worker-host.test", snapshot.WidgetInstanceId);
    Assert.Equal("clock-root", snapshot.Root.Id);
    Assert.True(client.IsRunning, "Worker host should remain alive after a valid snapshot.");
}

static Task Run(Action action)
{
    action();
    return Task.CompletedTask;
}

static void RejectsPathEscape()
{
    using var temporary = new TemporaryDirectory();
    var outside = typeof(ClockWidget).Assembly.Location;
    var exception = Assert.Throws<WidgetLoadException>(() =>
        WidgetAssemblyLoader.Load(temporary.Path, outside, typeof(ClockWidget).FullName!));
    Assert.Equal("path_escape", exception.Code);
}

static void RejectsInvalidTypes()
{
    var assembly = typeof(ClockWidget).Assembly.Location;
    var root = Path.GetDirectoryName(assembly)!;
    Assert.Equal("missing_type", Assert.Throws<WidgetLoadException>(() =>
        WidgetAssemblyLoader.Load(root, assembly, "Missing.Widget")).Code);
    var nonWidgetAssembly = typeof(NotAWidget).Assembly.Location;
    Assert.Equal("invalid_type", Assert.Throws<WidgetLoadException>(() =>
        WidgetAssemblyLoader.Load(
            Path.GetDirectoryName(nonWidgetAssembly)!,
            nonWidgetAssembly,
            typeof(NotAWidget).FullName!)).Code);
}

static void RejectsInvalidAssembly()
{
    using var temporary = new TemporaryDirectory();
    var assembly = Path.Combine(temporary.Path, "Widget.dll");
    File.WriteAllText(assembly, "not an assembly");
    var exception = Assert.Throws<WidgetLoadException>(() =>
        WidgetAssemblyLoader.Load(temporary.Path, assembly, "Example.Widget"));
    Assert.Equal("invalid_assembly", exception.Code);
    Assert.True(!exception.Message.Contains(temporary.Path, StringComparison.OrdinalIgnoreCase),
        "Public load diagnostics must not disclose the installed package path.");
}

file sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"gba-worker-host-{Guid.NewGuid():N}");
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

file static class Assert
{
    public static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}

public sealed class NotAWidget;
