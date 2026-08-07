using GameBarAlternative.Samples.ClockWidget;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetWorkerHost;
using WorkerHostProgram = GameBarAlternative.WidgetWorkerHost.Program;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Loads a public package Widget entrypoint", () => Run(LoadsWidget)),
    ("Runs a package through the isolated worker protocol", RunsIsolatedWorker),
    ("Rejects entrypoint path escape", () => Run(RejectsPathEscape)),
    ("Rejects missing and non-Widget types", () => Run(RejectsInvalidTypes)),
    ("Rejects invalid assemblies without leaking paths", () => Run(RejectsInvalidAssembly)),
    ("Broker bootstrap arguments are optional but atomic", () => Run(BrokerArgumentsAreAtomic)),
    ("Typed capability adapter uses the authenticated broker pipe", TypedCapabilityAdapter),
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

static void BrokerArgumentsAreAtomic()
{
    Assert.True(WorkerHostProgram.ParseBrokerConnection([], "widget-1") is null,
        "A worker without broker arguments must preserve unavailable capabilities.");
    Assert.Throws<ArgumentException>(() => WorkerHostProgram.ParseBrokerConnection(
        ["--broker-pipe", "pipe-only"], "widget-1"));
    Assert.Throws<ArgumentException>(() => WorkerHostProgram.ParseBrokerConnection(
        BrokerArguments("different-instance"), "widget-1"));

    var connection = WorkerHostProgram.ParseBrokerConnection(BrokerArguments("widget-1"), "widget-1")!;
    Assert.Equal("dev.test.widget", connection.PackageId);
    Assert.Equal("dev.test", connection.PublisherId);
    Assert.Equal("widget-1", connection.InstanceId);
}

static string[] BrokerArguments(string instanceId) =>
[
    "--broker-pipe", "gba-worker-test",
    "--broker-package", "dev.test.widget",
    "--broker-publisher", "dev.test",
    "--broker-instance", instanceId,
    "--broker-nonce", new string('A', 64),
];

static async Task TypedCapabilityAdapter()
{
    using var temporary = new TemporaryDirectory();
    var identity = new BrokerWidgetIdentity("dev.test.widget", "dev.test", "widget-1");
    var consent = new ConsentStore(temporary.Path);
    await consent.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    await consent.SetDecisionAsync(identity, PlatformCapabilities.AudioSessionsControlV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAudioSessions([new("audio-1", "Game", 0.75, false, true)]);
    var options = new BrokerPipeTransportOptions
    {
        AcceptTimeout = TimeSpan.FromSeconds(2),
        HandshakeTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromSeconds(1),
    };
    var pipeName = $"gba-worker-capability-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName, identity,
        [PlatformCapabilities.AudioSessionsReadV1, PlatformCapabilities.AudioSessionsControlV1],
        consent, backend, options, new string('B', 64));
    var serverTask = server.RunAsync();
    await using var pipeClient = new BrokerPipeClient(
        pipeName, identity, server.ChannelNonce, options);
    await pipeClient.ConnectAsync();
    server.SetLifecycle(BrokerLifecycleState.Interactive);
    var adapter = new BrokerWidgetCapabilityClient(pipeClient);

    var sessions = await adapter.InvokeAsync(
        WidgetAudioCapabilities.GetSessions, new WidgetCapabilityQuery());
    Assert.Equal("audio-1", sessions.Single().SessionId);
    var acknowledged = await adapter.InvokeAsync(
        WidgetAudioCapabilities.SetSessionVolume,
        new SetWidgetAudioSessionVolumeRequest("audio-1", 0.5));
    Assert.True(acknowledged.Acknowledged, "Control acknowledgement was not decoded.");
    Assert.Equal(1, backend.AudioControlCalls);

    await using var events = adapter.SubscribeAsync(WidgetAudioCapabilities.SessionsChanged)
        .GetAsyncEnumerator();
    var moveNext = events.MoveNextAsync().AsTask();
    for (var attempt = 0; attempt < 20 && !moveNext.IsCompleted; attempt++)
    {
        backend.Publish(new BrokerPlatformEvent(
            PlatformCapabilities.AudioSessionsReadV1,
            PlatformCapabilities.AudioSessionsChanged,
            new AudioSessionsChangedEvent(
                [new("audio-2", "Voice", 0.5, false, true)])));
        await Task.Delay(10);
    }
    Assert.True(await moveNext.WaitAsync(TimeSpan.FromSeconds(1)),
        "Typed capability event was not decoded.");
    Assert.Equal("audio-2", events.Current.Sessions.Single().SessionId);

    try
    {
        _ = await adapter.InvokeAsync(
            WidgetNetworkCapabilities.GetStatus, new WidgetCapabilityQuery());
        throw new InvalidOperationException("Expected undeclared capability rejection.");
    }
    catch (WidgetCapabilityException exception)
    {
        Assert.Equal("capability_not_declared", exception.ErrorCode);
    }

    await pipeClient.DisposeAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
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
