using WidgetRail.Samples.ClockWidget;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetWorkerHost;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Loads a public package Widget entrypoint", () => Run(LoadsWidget)),
    ("Runs a package through the isolated worker protocol", RunsIsolatedWorker),
    ("Bootstrap authenticates capabilities before widget lifecycle creation", BrokerServicesPrecedeWidgetCreation),
    ("AppContainer worker uses a PID-bound broker capability channel", AppContainerBrokerIsBound),
    ("Rejects entrypoint path escape", () => Run(RejectsPathEscape)),
    ("Rejects missing and non-Widget types", () => Run(RejectsInvalidTypes)),
    ("Rejects invalid assemblies without leaking paths", () => Run(RejectsInvalidAssembly)),
    ("Generic host surfaces a closed safe loader code before connection", LoaderFailureCodeIsSafe),
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
    });
    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal("worker-host.test", snapshot.WidgetInstanceId);
    Assert.Equal("clock-root", snapshot.Root.Id);
    Assert.True(client.IsRunning, "Worker host should remain alive after a valid snapshot.");
}

static async Task BrokerServicesPrecedeWidgetCreation()
{
    using var temporary = new TemporaryDirectory();
    const string instanceId = "worker-host.broker";
    var identity = new BrokerWidgetIdentity("dev.test.widget", "dev.test", instanceId);
    var pipeName = $"gba-worker-bootstrap-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName,
        identity,
        [PlatformCapabilities.AudioSessionsReadV1],
        new ConsentStore(temporary.Path),
        new SimulatedPlatformBrokerBackend(),
        new BrokerPipeTransportOptions
        {
            AcceptTimeout = TimeSpan.FromSeconds(3),
            HandshakeTimeout = TimeSpan.FromSeconds(2),
            RequestTimeout = TimeSpan.FromSeconds(1),
        },
        new string('C', 64));
    var serverTask = server.RunAsync();

    var root = Path.GetDirectoryName(typeof(WidgetAssemblyLoader).Assembly.Location)!;
    var executable = Path.Combine(root, "WidgetWorkerHost.exe");
    var assembly = typeof(BootstrapCapabilityProbeWidget).Assembly.Location;
    await using var client = new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = executable,
        Arguments =
        [
            "--package-root", Path.GetDirectoryName(assembly)!,
            "--widget-assembly", assembly,
            "--widget-type", typeof(BootstrapCapabilityProbeWidget).FullName!,
            "--broker-pipe", pipeName,
            "--broker-package", identity.PackageId,
            "--broker-publisher", identity.PublisherId,
            "--broker-instance", identity.InstanceId,
            "--broker-nonce", server.ChannelNonce,
        ],
        WidgetInstanceId = instanceId,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        RequestTimeout = TimeSpan.FromSeconds(2),
        MaximumRestartAttempts = 0,
    });

    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal("available", snapshot.Root.Text);
    await client.StopAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
}

static async Task AppContainerBrokerIsBound()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temporary = new TemporaryDirectory();
    const string instanceId = "worker-host.appcontainer-broker";
    var identity = new BrokerWidgetIdentity("dev.test.isolated", "dev.test", instanceId);
    var consent = new ConsentStore(temporary.Path);
    await consent.SetDecisionAsync(
        identity,
        PlatformCapabilities.AudioSessionsReadV1,
        ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAudioSessions([new("isolated-audio", "Isolated game", 0.5, false, true)]);

    var root = Path.GetDirectoryName(typeof(WidgetAssemblyLoader).Assembly.Location)!;
    var executable = Path.Combine(root, "WidgetWorkerHost.exe");
    var assembly = typeof(AppContainerBrokerProbeWidget).Assembly.Location;
    await using var client = new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = executable,
        Arguments =
        [
            "--package-root", Path.GetDirectoryName(assembly)!,
            "--widget-assembly", assembly,
            "--widget-type", typeof(AppContainerBrokerProbeWidget).FullName!,
        ],
        WidgetInstanceId = instanceId,
        ConnectTimeout = TimeSpan.FromSeconds(8),
        RequestTimeout = TimeSpan.FromSeconds(3),
        MaximumRestartAttempts = 0,
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = "community-v1\ndev.test\ndev.test.isolated",
        ReadOnlyPaths = [Path.GetDirectoryName(assembly)!],
        CompanionSessionFactory = context => new IsolatedBrokerCompanion(
            identity,
            consent,
            backend,
            context),
    });

    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal("Isolated game", snapshot.Root.Text);
    await client.StopAsync();
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

static async Task LoaderFailureCodeIsSafe()
{
    using var temporary = new TemporaryDirectory();
    var assembly = Path.Combine(temporary.Path, "Widget.dll");
    File.WriteAllText(assembly, "not an assembly");
    var root = Path.GetDirectoryName(typeof(WidgetAssemblyLoader).Assembly.Location)!;
    var executable = Path.Combine(root, "WidgetWorkerHost.exe");
    WidgetFailure? failure = null;
    await using var client = new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = executable,
        Arguments =
        [
            "--package-root", temporary.Path,
            "--widget-assembly", assembly,
            "--widget-type", "Example.Widget",
        ],
        WidgetInstanceId = "worker-host.loader-failure",
        ConnectTimeout = TimeSpan.FromSeconds(3),
        RequestTimeout = TimeSpan.FromSeconds(2),
        MaximumRestartAttempts = 0,
        StartupExitDiagnostics = WidgetWorkerStartupDiagnostics.LoaderExitCodes,
    });
    client.Failed += (_, item) => failure = item;

    WidgetProcessException? thrown = null;
    try { _ = await client.GetSnapshotAsync(); }
    catch (WidgetProcessException exception) { thrown = exception; }
    if (thrown is null)
        throw new InvalidOperationException("Invalid assembly startup unexpectedly connected.");
    Assert.True(thrown.Message.Contains("invalid_assembly", StringComparison.Ordinal),
        "The safe loader code was not retained through worker connection failure.");
    Assert.True(!thrown.ToString().Contains(temporary.Path, StringComparison.OrdinalIgnoreCase),
        "The public startup failure exposed the installed package path.");
    if (failure is null)
        throw new InvalidOperationException("The runtime did not publish the startup failure.");
    Assert.Equal("invalid_assembly", failure.DiagnosticCode);
    Assert.Equal(WidgetWorkerStartupDiagnostics.ExitCodeFor("invalid_assembly"), failure.ExitCode);
}

static void BrokerArgumentsAreAtomic()
{
    var withoutBroker = WidgetWorkerLaunchArguments.Parse(RuntimeArguments());
    Assert.True(withoutBroker.Broker is null,
        "A worker without broker arguments must preserve unavailable capabilities.");
    Assert.Throws<ArgumentException>(() => WidgetWorkerLaunchArguments.Parse(
        [.. RuntimeArguments(), "--broker-pipe", "pipe-only"]));
    Assert.Throws<ArgumentException>(() => WidgetWorkerLaunchArguments.Parse(
        [.. RuntimeArguments(), .. BrokerArguments("different-instance")]));
    Assert.Throws<ArgumentException>(() => WidgetWorkerLaunchArguments.Parse(
        [.. RuntimeArguments(), "--widget-instance", "duplicate"]));

    var connection = WidgetWorkerLaunchArguments.Parse(
        [.. RuntimeArguments(), .. BrokerArguments("widget-1")]).Broker!;
    Assert.Equal("dev.test.widget", connection.Identity.PackageId);
    Assert.Equal("dev.test", connection.Identity.PublisherId);
    Assert.Equal("widget-1", connection.Identity.InstanceId);
}

static string[] RuntimeArguments() =>
[
    "--widget-pipe", "gba-runtime-test",
    "--widget-instance", "widget-1",
    "--widget-session-nonce", new string('B', 64),
    "--max-message-bytes", "65536",
];

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

    await using var subscription = await adapter.OpenSubscriptionAsync(
        WidgetAudioCapabilities.SessionsChanged);
    // Publish after Open returns but before a reader exists. Delivery proves
    // the remote broker registered and acknowledged the subscription first.
    backend.Publish(new BrokerPlatformEvent(
        PlatformCapabilities.AudioSessionsReadV1,
        PlatformCapabilities.AudioSessionsChanged,
        new AudioSessionsChangedEvent(
            [new("audio-2", "Voice", 0.5, false, true)])));
    await using var events = subscription.ReadAllAsync().GetAsyncEnumerator();
    var moveNext = events.MoveNextAsync().AsTask();
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

file sealed class IsolatedBrokerCompanion : IWidgetProcessCompanionSession
{
    private readonly BrokerPipeServer _server;

    public IsolatedBrokerCompanion(
        BrokerWidgetIdentity identity,
        ConsentStore consent,
        IPlatformBrokerBackend backend,
        WidgetProcessCompanionContext context)
    {
        if (context.IsolationPolicy != WidgetWorkerIsolationPolicy.RequireAppContainer ||
            string.IsNullOrWhiteSpace(context.AppContainerSid))
            throw new InvalidOperationException("The test broker requires an AppContainer SID.");
        var pipeName = $"gba-worker-isolated-broker-{Guid.NewGuid():N}";
        _server = new BrokerPipeServer(
            pipeName,
            identity,
            [PlatformCapabilities.AudioSessionsReadV1],
            consent,
            backend,
            new BrokerPipeTransportOptions
            {
                AcceptTimeout = TimeSpan.FromSeconds(8),
                HandshakeTimeout = TimeSpan.FromSeconds(3),
                RequestTimeout = TimeSpan.FromSeconds(2),
            },
            isolatedClientAppContainerSid: context.AppContainerSid);
        _server.SetLifecycle(BrokerLifecycleState.Visible);
        WorkerArguments =
        [
            "--broker-pipe", pipeName,
            "--broker-package", identity.PackageId,
            "--broker-publisher", identity.PublisherId,
            "--broker-instance", identity.InstanceId,
            "--broker-nonce", _server.ChannelNonce,
        ];
    }

    public IReadOnlyList<string> WorkerArguments { get; }
    public void BindWorkerProcess(int processId) =>
        _server.BindExpectedIsolatedClientProcess(processId);
    public Task RunAsync(CancellationToken cancellationToken) =>
        _server.RunAsync(cancellationToken);
    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync() => _server.DisposeAsync();
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

public sealed class BootstrapCapabilityProbeWidget : Widget
{
    private string _state = "not-created";

    protected override ValueTask OnCreatedAsync(CancellationToken widgetLifetime)
    {
        _state = HostServices.Capabilities.IsAvailable ? "available" : "unavailable";
        return ValueTask.CompletedTask;
    }

    public override WidgetView Render() => new(UI.Text(_state, "capability-state"));
}

public sealed class AppContainerBrokerProbeWidget : Widget
{
    private string _state = "not-created";

    protected override async ValueTask OnCreatedAsync(CancellationToken widgetLifetime)
    {
        var sessions = await HostServices.Audio
            .GetSessionsAsync(widgetLifetime)
            .ConfigureAwait(false);
        _state = sessions.Single().DisplayName;
    }

    public override WidgetView Render() => new(UI.Text(_state, "broker-result"));
}
