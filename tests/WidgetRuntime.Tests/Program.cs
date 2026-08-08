using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

if (args.Contains("--containment-sleeper", StringComparer.Ordinal))
{
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

if (args.Contains("--widget-pipe", StringComparer.Ordinal))
    return await RunWorkerAsync(args);

var tests = new (string Name, Func<Task> Run)[]
{
    ("Length framing rejects oversized input before allocation", OversizedFrameIsRejected),
    ("Worker launch is lazy and snapshot is validated", LazyLaunchAndSnapshot),
    ("Worker memory policy rejects unsafe bounds", MemoryPolicyIsBounded),
    ("Windows worker Job Object applies trusted limits", WindowsJobAppliesLimits),
    ("Windows Job Object kill-on-close cleans up its process", WindowsJobCleansUpProcess),
    ("Windows worker Job Object allows only one active process", WindowsJobIsSingleProcess),
    ("Community workers have package-specific AppContainer authority", AppContainerIsolation),
    ("Lifecycle callbacks and lifetime tokens follow exact transition order", LifecycleContract),
    ("Runtime-owned lifecycle states cannot be host targets", InvalidLifecycleTargets),
    ("Widget activation transitions are idempotent and cancel their lifetime", ActivationTransitions),
    ("Worker remains inactive until explicit activity transport", ActivityTransport),
    ("Actions deliver invalidation notifications", ActionsInvalidate),
    ("Raw controller input resolves only after a rendered snapshot", ControllerInputUsesLatestSnapshot),
    ("Rapid dashboard actions acknowledge quickly and execute in order", RapidDashboardActionsAreQueued),
    ("Dashboard authority is granted through the exact worker companion and revoked when unhandled", DashboardAuthorityUsesCompanion),
    ("Slow queued dashboard work starts each authority lifetime only when its action executes", SlowDashboardQueueActivatesJustInTime),
    ("Dormant dashboard reservations expire on a host-owned monotonic clock", DormantDashboardReservationExpires),
    ("Custom async dashboard handlers can activate authority without blocking the pipe reader", CustomDashboardHandlerActivatesWithoutDeadlock),
    ("Broker adapter attaches gesture sequences only inside the queued invocation scope", BrokerAdapterBindsGestureContext),
    ("Rapid controller inputs acknowledge quickly and execute in order", RapidControllerInputsAreQueued),
    ("Runtime shortcut fallback respects explicit active input surface", RuntimeScopedShortcutRouting),
    ("Queued controller work cancels on deactivation", ControllerQueueCancelsOnDeactivation),
    ("Controller queue rejects saturation without waiting", ControllerQueueIsBounded),
    ("Queued controller failures are observable without crashing", ControllerQueueFailuresAreObservable),
    ("Unexpected worker exit is reported and recoverable", CrashRecovery),
    ("Companion endpoint ownership is established before worker launch", CompanionEndpointPrecedesLaunch),
    ("Host companion sessions are recreated and lifecycle-restored after crashes", CompanionSessionsFollowWorkerRestarts),
    ("Intentional idle unload destroys resources without consuming crash budget", IntentionalUnloadIsReusable),
    ("A crashed nonexistent session cannot gain an intentional-resume exemption", CrashedSessionIsNotIntentionalUnload),
    ("Non-completing companion disposal cannot hold worker teardown", CompanionDisposalIsBounded),
    ("Request timeout terminates a hung worker", HungWorkerTimesOut),
    ("Malformed worker snapshots are rejected by host", MalformedSnapshotIsRejected),
    ("Worker destruction is bounded when widget cleanup hangs", DestroyIsBounded),
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
        failures.Add($"FAIL {test.Name}: {exception}");
        Console.Error.WriteLine(failures[^1]);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task<int> RunWorkerAsync(string[] arguments)
{
    var pipe = RequiredValue(arguments, "--widget-pipe");
    var instance = RequiredValue(arguments, "--widget-instance");
    var maximumBytes = int.Parse(
        RequiredValue(arguments, "--max-message-bytes"), CultureInfo.InvariantCulture);
    if (arguments.Contains("--malformed-worker", StringComparer.Ordinal))
        return await RunMalformedWorkerAsync(pipe, instance, maximumBytes);
    VerifyPrecreatedCompanionEndpoint(arguments);

    Widget widget = arguments.Contains("--hanging-destroy", StringComparer.Ordinal)
        ? new HangingDestroyWidget()
        : arguments.Contains("--gesture-queue-probe", StringComparer.Ordinal)
            ? new GestureQueueWidget()
        : arguments.Contains("--gesture-custom-probe", StringComparer.Ordinal)
            ? new CustomGestureWidget()
        : arguments.Contains("--isolation-probe", StringComparer.Ordinal)
            ? new IsolationProbeWidget(
                RequiredValue(arguments, "--probe-readable-path"),
                RequiredValue(arguments, "--probe-denied-path"),
                OptionalValue(arguments, "--probe-other-profile-path"),
                int.Parse(RequiredValue(arguments, "--probe-network-port"), CultureInfo.InvariantCulture),
                RequiredValue(arguments, "--probe-secret-name"))
            : new TestWidget();
    IWidgetCapabilityClient? capabilityClient =
        arguments.Contains("--gesture-queue-probe", StringComparer.Ordinal) ||
        arguments.Contains("--gesture-custom-probe", StringComparer.Ordinal)
            ? new GestureProbeCapabilityClient()
            : null;
    await new WidgetWorkerServer(
        widget, instance, pipe, maximumBytes, capabilityClient).RunAsync();
    return 0;
}

static void VerifyPrecreatedCompanionEndpoint(string[] arguments)
{
    var endpoint = OptionalValue(arguments, "--probe-precreated-pipe");
    if (endpoint is null || !OperatingSystem.IsWindows()) return;
    try
    {
        using var stolen = new System.IO.Pipes.NamedPipeServerStream(
            endpoint, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous |
            System.IO.Pipes.PipeOptions.CurrentUserOnly |
            System.IO.Pipes.PipeOptions.FirstPipeInstance);
        throw new InvalidOperationException(
            "Worker launched before the companion owned its first pipe instance.");
    }
    catch (IOException)
    {
        // Expected: the host companion already owns the first instance.
    }
}

static async Task<int> RunMalformedWorkerAsync(string pipeName, string instanceId, int maximumBytes)
{
    await using var pipe = new System.IO.Pipes.NamedPipeClientStream(
        ".", pipeName, System.IO.Pipes.PipeDirection.InOut,
        System.IO.Pipes.PipeOptions.Asynchronous);
    await pipe.ConnectAsync();
    var channel = new LengthPrefixedJsonChannel(pipe, maximumBytes);
    await channel.WriteAsync(new RuntimeEnvelope
    {
        Type = MessageTypes.Hello,
        Payload = RuntimeJson.ToElement(new HelloPayload(instanceId)),
    }, CancellationToken.None);
    _ = await channel.ReadAsync(CancellationToken.None);
    var request = await channel.ReadAsync(CancellationToken.None);
    using var document = JsonDocument.Parse(
        "{\"protocolVersion\":1,\"sequence\":1,\"widgetInstanceId\":\"runtime.test\",\"root\":{" +
        "\"id\":\"root\",\"kind\":\"stack\",\"styleClasses\":[],\"shortcuts\":[],\"children\":[]},\"unknown\":true}");
    await channel.WriteAsync(new RuntimeEnvelope
    {
        Type = MessageTypes.Snapshot,
        RequestId = request.RequestId,
        Payload = document.RootElement.Clone(),
    }, CancellationToken.None);
    return 0;
}

static async Task OversizedFrameIsRejected()
{
    var bytes = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(bytes, 1025);
    await using var stream = new MemoryStream(bytes);
    var channel = new LengthPrefixedJsonChannel(stream, 1024);
    await Assert.ThrowsAsync<WidgetProtocolViolationException>(
        () => channel.ReadAsync(CancellationToken.None).AsTask());
}

static async Task LazyLaunchAndSnapshot()
{
    await using var client = CreateClient();
    Assert.Equal(0, client.Starts);
    Assert.False(client.IsRunning, "Constructor must not launch a worker.");
    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal(1, client.Starts);
    Assert.True(client.IsRunning, "First request must lazily launch the worker.");
    Assert.Equal("runtime.test", snapshot.WidgetInstanceId);
    Assert.Equal("button", snapshot.InitialFocusId);
    await client.StopAsync();
}

static Task MemoryPolicyIsBounded()
{
    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("Test process path is unavailable.");
    _ = Assert.Throws<ArgumentOutOfRangeException>(() => new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = executable,
        WidgetInstanceId = "runtime.test",
        MemoryLimitBytes = 15L * 1024 * 1024,
    }));
    _ = Assert.Throws<ArgumentOutOfRangeException>(() => new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = executable,
        WidgetInstanceId = "runtime.test",
        MemoryLimitBytes = 513L * 1024 * 1024,
    }));
    return Task.CompletedTask;
}

static async Task WindowsJobAppliesLimits()
{
    if (!OperatingSystem.IsWindows()) return;
    const long limit = 72L * 1024 * 1024;
    await using var client = CreateClient(memoryLimitBytes: limit);
    _ = await client.GetSnapshotAsync();
    Assert.Equal(limit, client.AppliedJobMemoryLimitBytes);
    Assert.Equal((uint)1, client.AppliedJobActiveProcessLimit);
}

static async Task WindowsJobCleansUpProcess()
{
    if (!OperatingSystem.IsWindows()) return;
    var startInfo = SleeperStartInfo();
    var job = WindowsWorkerJob.Create(64L * 1024 * 1024);
    using var process = job.StartProcess(startInfo);
    try
    {
        Assert.True(!process.HasExited, "Contained sleeper exited before cleanup test.");
        job.Dispose();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(process.HasExited, "Closing the Job Object left its worker alive.");
    }
    finally
    {
        job.Dispose();
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
}

static async Task WindowsJobIsSingleProcess()
{
    if (!OperatingSystem.IsWindows()) return;
    using var job = WindowsWorkerJob.Create(64L * 1024 * 1024);
    using var first = job.StartProcess(SleeperStartInfo());
    try
    {
        _ = Assert.Throws<System.ComponentModel.Win32Exception>(() =>
        {
            using var unexpected = job.StartProcess(SleeperStartInfo());
        });
    }
    finally
    {
        job.Terminate();
        await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
    }
}

static async Task AppContainerIsolation()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temp = new TemporaryDirectory();
    var packageA = Path.Combine(temp.Path, "package-a");
    var packageB = Path.Combine(temp.Path, "package-b");
    Directory.CreateDirectory(packageA);
    Directory.CreateDirectory(packageB);
    var readableA = Path.Combine(packageA, "payload.txt");
    var readableB = Path.Combine(packageB, "payload.txt");
    var privateUserFile = Path.Combine(temp.Path, "host-private.txt");
    await File.WriteAllTextAsync(readableA, "package-a");
    await File.WriteAllTextAsync(readableB, "package-b");
    await File.WriteAllTextAsync(privateUserFile, "host-private");

    const string secretName = "GBA_ISOLATION_TEST_SECRET";
    var priorSecret = Environment.GetEnvironmentVariable(secretName);
    Environment.SetEnvironmentVariable(secretName, "must-not-cross-token-boundary");
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    try
    {
        using var firstProfile = WindowsAppContainer.OpenOrCreate("publisher-a/package-a");
        var firstProfilePrivate = Path.Combine(firstProfile.ProfilePath, "host-seeded-private.txt");
        await File.WriteAllTextAsync(firstProfilePrivate, "package-a-private");
        await using var first = CreateIsolatedClient(
            "publisher-a/package-a",
            packageA,
            readableA,
            privateUserFile,
            null,
            port,
            secretName);
        var firstSnapshot = await first.GetSnapshotAsync();
        AssertIsolationProbe(firstSnapshot, "package-a");
        var firstSid = Find(firstSnapshot.Root, "probe-sid").Text;
        Assert.Equal(firstProfile.Sid, firstSid);
        Assert.True(!string.IsNullOrWhiteSpace(Find(firstSnapshot.Root, "probe-profile-file").Text),
            "First AppContainer did not report its writable virtualized profile file.");

        await using var second = CreateIsolatedClient(
            "publisher-b/package-b",
            packageB,
            readableB,
            readableA,
            firstProfilePrivate,
            port,
            secretName);
        var secondSnapshot = await second.GetSnapshotAsync();
        AssertIsolationProbe(secondSnapshot, "package-b");
        Assert.Equal("denied", Find(secondSnapshot.Root, "probe-other-profile-read").Text);
        var secondSid = Find(secondSnapshot.Root, "probe-sid").Text;
        Assert.True(!string.Equals(firstSid, secondSid, StringComparison.Ordinal),
            "Distinct host isolation keys produced the same AppContainer SID.");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(first.StopAsync(), second.StopAsync());
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3.5),
            $"Isolated worker cleanup was not bounded ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");
        Assert.True(!first.IsRunning && !second.IsRunning,
            "Bounded cleanup left an isolated worker running.");
    }
    finally
    {
        listener.Stop();
        Environment.SetEnvironmentVariable(secretName, priorSecret);
    }
}

static void AssertIsolationProbe(ViewSnapshot snapshot, string expectedContent)
{
    Assert.Equal("true", Find(snapshot.Root, "probe-appcontainer").Text);
    Assert.Equal("low", Find(snapshot.Root, "probe-integrity").Text);
    Assert.Equal("0", Find(snapshot.Root, "probe-capabilities").Text);
    Assert.Equal(expectedContent, Find(snapshot.Root, "probe-readable").Text);
    Assert.Equal("denied", Find(snapshot.Root, "probe-package-write").Text);
    Assert.Equal("denied", Find(snapshot.Root, "probe-denied-read").Text);
    Assert.Equal("denied", Find(snapshot.Root, "probe-network").Text);
    Assert.Equal("absent", Find(snapshot.Root, "probe-secret").Text);
    Assert.True(!string.IsNullOrWhiteSpace(Find(snapshot.Root, "probe-sid").Text),
        "Worker did not report its AppContainer SID.");
}

static WidgetProcessClient CreateIsolatedClient(
    string isolationKey,
    string packageRoot,
    string readablePath,
    string deniedPath,
    string? otherProfilePath,
    int networkPort,
    string secretName)
{
    var arguments = new List<string>
    {
        "--isolation-probe",
        "--probe-readable-path", readablePath,
        "--probe-denied-path", deniedPath,
        "--probe-network-port", networkPort.ToString(CultureInfo.InvariantCulture),
        "--probe-secret-name", secretName,
    };
    if (otherProfilePath is not null)
    {
        arguments.Add("--probe-other-profile-path");
        arguments.Add(otherProfilePath);
    }
    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("Test process path is unavailable.");
    return new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = executable,
        Arguments = arguments,
        WidgetInstanceId = "runtime.test",
        ConnectTimeout = TimeSpan.FromSeconds(8),
        RequestTimeout = TimeSpan.FromSeconds(3),
        MaximumMessageBytes = 64 * 1024,
        MemoryLimitBytes = 96L * 1024 * 1024,
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = isolationKey,
        ReadOnlyPaths = [packageRoot],
    });
}

static System.Diagnostics.ProcessStartInfo SleeperStartInfo()
{
    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("Test process path is unavailable.");
    var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = AppContext.BaseDirectory,
    };
    startInfo.ArgumentList.Add("--containment-sleeper");
    return startInfo;
}

static async Task ActivationTransitions()
{
    var widget = new LifecycleProbeWidget();
    Assert.True(widget.Lifetime.IsCancellationRequested, "Inactive widgets need a canceled lifetime token.");
    await widget.SetActiveAsync(true, CancellationToken.None);
    Assert.True(widget.IsActive, "Widget did not activate.");
    Assert.Equal(1, widget.Activations);
    var lifetime = widget.Lifetime;
    Assert.True(!lifetime.IsCancellationRequested, "Active lifetime was already canceled.");
    await widget.SetActiveAsync(true, CancellationToken.None);
    Assert.Equal(1, widget.Activations);
    await widget.SetActiveAsync(false, CancellationToken.None);
    Assert.True(!widget.IsActive, "Widget did not deactivate.");
    Assert.True(lifetime.IsCancellationRequested, "Deactivation did not cancel the active lifetime.");
    Assert.Equal(1, widget.Deactivations);
    await widget.SetActiveAsync(false, CancellationToken.None);
    Assert.Equal(1, widget.Deactivations);
}

static async Task LifecycleContract()
{
    var widget = new LifecycleContractProbeWidget();
    var createdStateLifetime = widget.CurrentStateLifetime;
    var widgetLifetime = widget.CurrentWidgetLifetime;
    Assert.Equal(WidgetLifecycleState.Created, widget.CurrentState);
    Assert.True(!createdStateLifetime.IsCancellationRequested, "Created state token started canceled.");

    await widget.InitializeAsync(CancellationToken.None);
    Assert.Equal(WidgetLifecycleState.Background, widget.CurrentState);
    Assert.True(createdStateLifetime.IsCancellationRequested, "Created state exit did not cancel its token.");
    Assert.True(!widgetLifetime.IsCancellationRequested, "Widget lifetime ended during initialization.");
    var backgroundStateLifetime = widget.CurrentStateLifetime;

    await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
        widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None).AsTask()));
    Assert.True(backgroundStateLifetime.IsCancellationRequested, "Background state token was not canceled.");
    var visibleStateLifetime = widget.CurrentStateLifetime;
    var visibleLifetime = widget.CurrentActiveLifetime;
    Assert.True(!visibleLifetime.IsCancellationRequested, "Visible lifetime started canceled.");

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, CancellationToken.None);
    Assert.True(visibleStateLifetime.IsCancellationRequested, "Visible state token was not canceled.");
    Assert.True(widget.CurrentActiveLifetime == visibleLifetime,
        "Visible to Interactive must preserve the visible lifetime token.");
    var interactiveStateLifetime = widget.CurrentStateLifetime;
    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, CancellationToken.None);
    Assert.True(widget.CurrentStateLifetime == interactiveStateLifetime,
        "An idempotent transition must not replace its state token.");

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, CancellationToken.None);
    Assert.True(interactiveStateLifetime.IsCancellationRequested,
        "Interactive state token was not canceled on exit.");
    Assert.True(widget.CurrentActiveLifetime == visibleLifetime,
        "Interactive to Visible must preserve the visible lifetime token.");
    var secondVisibleStateLifetime = widget.CurrentStateLifetime;

    await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, CancellationToken.None);
    Assert.True(secondVisibleStateLifetime.IsCancellationRequested,
        "Visible state token was not canceled on backgrounding.");
    Assert.True(visibleLifetime.IsCancellationRequested,
        "Visible lifetime was not canceled on backgrounding.");
    Assert.True(!widgetLifetime.IsCancellationRequested,
        "Backgrounding must not stop process-lifetime work.");
    await widget.ProcessLifetimeWork.WaitAsync(TimeSpan.FromSeconds(1));
    var finalBackgroundStateLifetime = widget.CurrentStateLifetime;

    await widget.DestroyAsync(CancellationToken.None);
    Assert.Equal(WidgetLifecycleState.Destroying, widget.CurrentState);
    Assert.True(finalBackgroundStateLifetime.IsCancellationRequested,
        "Destroying did not cancel the final state lifetime.");
    Assert.True(widgetLifetime.IsCancellationRequested,
        "Destroying did not cancel process-lifetime work.");
    await widget.DestroyAsync(CancellationToken.None);

    Assert.Equal(
        "created|Created->Background|Background->Visible|activated|Visible->Interactive|" +
        "Interactive->Visible|Visible->Background|deactivated|destroying",
        string.Join('|', widget.Events));
}

static async Task InvalidLifecycleTargets()
{
    var widget = new LifecycleContractProbeWidget();
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        widget.SetLifecycleStateAsync(WidgetLifecycleState.Created, CancellationToken.None).AsTask());
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        widget.SetLifecycleStateAsync(WidgetLifecycleState.Destroying, CancellationToken.None).AsTask());

    await using var client = CreateClient();
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        client.SetLifecycleStateAsync(WidgetLifecycleState.Destroying));
    Assert.Equal(0, client.Starts);
}

static async Task ActivityTransport()
{
    await using var client = CreateClient();
    var inactive = await client.GetSnapshotAsync();
    Assert.Equal("inactive", Find(inactive.Root, "activity").Text);
    await client.SetActiveAsync(true);
    Assert.Equal("active", Find((await client.GetSnapshotAsync()).Root, "activity").Text);
    await client.SetActiveAsync(true);
    Assert.Equal("active", Find((await client.GetSnapshotAsync()).Root, "activity").Text);
    await client.SetActiveAsync(false);
    Assert.Equal("inactive", Find((await client.GetSnapshotAsync()).Root, "activity").Text);
}

static async Task ActionsInvalidate()
{
    await using var client = CreateClient();
    var invalidated = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Invalidated += (_, revision) => invalidated.TrySetResult(revision);
    await client.SendActionAsync(new WidgetActionEvent("invalidate", "button"));
    Assert.Equal(1L, await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(2)));
}

static async Task ControllerInputUsesLatestSnapshot()
{
    await using var client = CreateClient();
    var beforeRender = await client.SendControllerInputAsync(new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        Sequence: 99,
        SnapshotSequence: 1));
    Assert.True(!beforeRender, "A worker must not invent routing before its first snapshot.");

    var snapshot = await client.GetSnapshotAsync();
    await client.SetActiveAsync(true);
    var revisions = new System.Threading.Channels.UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    };
    var invalidations = System.Threading.Channels.Channel.CreateUnbounded<long>(revisions);
    client.Invalidated += (_, revision) => invalidations.Writer.TryWrite(revision);

    var quickHandled = await client.SendControllerInputAsync(new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        Sequence: 4,
        SnapshotSequence: snapshot.Sequence));
    Assert.True(quickHandled, "Dashboard quick action should resolve from latest snapshot.");
    Assert.Equal(1L, await invalidations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));

    var shortcutHandled = await client.SendControllerInputAsync(OpenInput(
        snapshot, ControllerButton.RightBumper, "button", inputSequence: 5));
    Assert.True(shortcutHandled, "Focused shortcut should resolve from latest snapshot.");
    Assert.Equal(2L, await invalidations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
}

static async Task RapidControllerInputsAreQueued()
{
    await using var client = CreateClient();
    var rendered = await client.GetSnapshotAsync();
    await client.SetActiveAsync(true);
    var invalidations = System.Threading.Channels.Channel.CreateUnbounded<long>();
    client.Invalidated += (_, revision) => invalidations.Writer.TryWrite(revision);

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    for (var sequence = 1; sequence <= 3; sequence++)
    {
        var handled = await client.SendControllerInputAsync(OpenInput(
            rendered, ControllerButton.RightBumper, "button", inputSequence: sequence));
        Assert.True(handled, $"Rapid input {sequence} was not accepted.");
    }
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
        $"Acknowledgements waited for action work ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");

    for (var expected = 1L; expected <= 3L; expected++)
        Assert.Equal(expected, await invalidations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal("1,2,3", Find(snapshot.Root, "controller-history").Text);
}

static async Task RapidDashboardActionsAreQueued()
{
    await using var client = CreateClient();
    var rendered = await client.GetSnapshotAsync();
    Assert.True(!await client.SendControllerInputAsync(new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        Sequence: 1,
        SnapshotSequence: rendered.Sequence)),
        "An inactive dashboard widget must not accept queued work.");
    await client.SetActiveAsync(true);
    var invalidations = System.Threading.Channels.Channel.CreateUnbounded<long>();
    client.Invalidated += (_, revision) => invalidations.Writer.TryWrite(revision);

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    for (var sequence = 1; sequence <= 3; sequence++)
    {
        var handled = await client.SendControllerInputAsync(new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: sequence,
            SnapshotSequence: rendered.Sequence));
        Assert.True(handled, $"Rapid dashboard action {sequence} was not accepted.");
    }
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
        $"Dashboard acknowledgements waited for action work ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");

    for (var expected = 1L; expected <= 3L; expected++)
        Assert.Equal(expected, await invalidations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
    Assert.Equal("1,2,3", Find((await client.GetSnapshotAsync()).Root, "controller-history").Text);
}

static async Task DashboardAuthorityUsesCompanion()
{
    var companion = new ProbeCompanionSession();
    await using var client = CreateClient(companionFactory: _ => companion);
    var snapshot = await client.GetSnapshotAsync();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var authority = new WidgetDashboardGestureAuthority(
        "system.media.sessions.control.v1",
        "media.session.control",
        10,
        snapshot.Sequence,
        TimeSpan.FromSeconds(2));
    Assert.True(await client.SendControllerInputAsync(
        new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 10,
            SnapshotSequence: snapshot.Sequence),
        authority), "Expected the authorized dashboard action to be accepted.");
    Assert.Equal(0, companion.GrantedAuthorities.Count);
    Assert.Equal(0, companion.RevokedInputSequences.Count);

    var rejected = authority with { InputSequence = 11 };
    Assert.True(!await client.SendControllerInputAsync(
        new ControllerInputEvent(
            ControllerButton.LeftTrigger,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 11,
            SnapshotSequence: snapshot.Sequence),
        rejected), "An unhandled dashboard button must reject its authority.");
    Assert.SequenceEqual(new long[] { 11 }, companion.RevokedInputSequences);
}

static async Task SlowDashboardQueueActivatesJustInTime()
{
    var companion = new ProbeCompanionSession();
    await using var client = CreateClient(
        extraArguments: ["--gesture-queue-probe"],
        companionFactory: _ => companion);
    var snapshot = await client.GetSnapshotAsync();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var enqueuedAt = DateTimeOffset.UtcNow;
    foreach (var (button, sequence) in new[]
             {
                 (ControllerButton.X, 21L),
                 (ControllerButton.RightBumper, 22L),
             })
    {
        var authority = new WidgetDashboardGestureAuthority(
            WidgetMediaCapabilities.Control.CapabilityId,
            WidgetMediaCapabilities.Control.OperationId,
            sequence,
            snapshot.Sequence,
            TimeSpan.FromSeconds(2));
        Assert.True(await client.SendControllerInputAsync(
            new ControllerInputEvent(
                button,
                ControllerEventPhase.Pressed,
                ControllerInputContext.DashboardQuickAction,
                Sequence: sequence,
                SnapshotSequence: snapshot.Sequence),
            authority), $"Dashboard input {sequence} was not queued.");
    }
    Assert.Equal(0, companion.GrantedAuthorities.Count);

    await Task.Delay(TimeSpan.FromMilliseconds(2_800));
    var completed = await client.GetSnapshotAsync();
    Assert.Equal("21,22", Find(completed.Root, "gesture-history").Text);
    Assert.SequenceEqual(new long[] { 21, 22 },
        companion.GrantedAuthorities.Select(item => item.InputSequence));
    Assert.True(companion.GrantTimes[1] - enqueuedAt > TimeSpan.FromSeconds(2),
        "Second queued authority started its lifetime before its action executed.");
}

static async Task CustomDashboardHandlerActivatesWithoutDeadlock()
{
    var companion = new ProbeCompanionSession();
    await using var client = CreateClient(
        requestTimeout: TimeSpan.FromSeconds(3),
        extraArguments: ["--gesture-custom-probe"],
        companionFactory: _ => companion);
    var snapshot = await client.GetSnapshotAsync();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var authority = new WidgetDashboardGestureAuthority(
        WidgetMediaCapabilities.Control.CapabilityId,
        WidgetMediaCapabilities.Control.OperationId,
        31,
        snapshot.Sequence,
        TimeSpan.FromSeconds(2));

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    Assert.True(await client.SendControllerInputAsync(
        new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 31,
            SnapshotSequence: snapshot.Sequence),
        authority), "The custom async dashboard handler did not complete its capability call.");
    stopwatch.Stop();

    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1),
        $"The custom handler blocked waiting for the sole pipe reader ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");
    Assert.SequenceEqual(new long[] { 31 },
        companion.GrantedAuthorities.Select(item => item.InputSequence));
    Assert.Equal("31", Find((await client.GetSnapshotAsync()).Root, "gesture-history").Text);
}

static async Task DormantDashboardReservationExpires()
{
    var clock = new ManualTimeProvider();
    var companion = new ProbeCompanionSession();
    await using var client = CreateClient(
        requestTimeout: TimeSpan.FromSeconds(4),
        extraArguments: ["--gesture-queue-probe"],
        companionFactory: _ => companion,
        timeProvider: clock);
    var snapshot = await client.GetSnapshotAsync();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var failures = System.Threading.Channels.Channel.CreateUnbounded<
        WidgetControllerActionFailure>();
    client.ControllerActionFailed += (_, failure) => failures.Writer.TryWrite(failure);
    var authority = new WidgetDashboardGestureAuthority(
        WidgetMediaCapabilities.Control.CapabilityId,
        WidgetMediaCapabilities.Control.OperationId,
        41,
        snapshot.Sequence,
        TimeSpan.FromSeconds(2));

    Assert.True(await client.SendControllerInputAsync(
        new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 41,
            SnapshotSequence: snapshot.Sequence),
        authority), "The slow dashboard action was not queued.");
    clock.Advance(TimeSpan.FromSeconds(11));

    var failure = await failures.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(4));
    Assert.Equal("gesture.slow", failure.ActionId);
    Assert.Equal(0, companion.GrantedAuthorities.Count);
}

static async Task BrokerAdapterBindsGestureContext()
{
    using var temp = new TemporaryDirectory();
    var identity = new BrokerWidgetIdentity("dev.runtime.media", "dev.runtime", "default");
    var store = new ConsentStore(temp.Path);
    await store.SetDecisionAsync(
        identity, PlatformCapabilities.MediaSessionsControlV1, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetMediaSessions([
        new("media-1", "Player", "Title", "Artist", MediaPlaybackStatus.Paused,
            0, 1_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, true,
            true, true, true, true, true),
    ]);
    var pipeName = $"gba-runtime-gesture-{Guid.NewGuid():N}";
    await using var server = new BrokerPipeServer(
        pipeName, identity, [PlatformCapabilities.MediaSessionsControlV1], store, backend);
    var serverTask = server.RunAsync();
    await using var transport = new BrokerPipeClient(pipeName, identity, server.ChannelNonce);
    await transport.ConnectAsync();
    server.SetLifecycle(BrokerLifecycleState.Visible);
    server.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        40,
        4,
        TimeSpan.FromSeconds(2));
    var adapter = new BrokerWidgetCapabilityClient(transport);
    await Assert.ThrowsAsync<WidgetCapabilityException>(async () =>
        await adapter.InvokeAsync(
            WidgetMediaCapabilities.Control,
            new ControlWidgetMediaSessionRequest("media-1", WidgetMediaSessionCommand.Next)));

    using (WidgetCapabilityInvocationContext.Enter(new(40, 4)))
    {
        var response = await adapter.InvokeAsync(
            WidgetMediaCapabilities.Control,
            new ControlWidgetMediaSessionRequest("media-1", WidgetMediaSessionCommand.Next));
        Assert.True(response.Acknowledged, "Exact gesture context was not propagated.");
    }
    Assert.Equal(1, backend.MediaControlCalls);
    using (WidgetCapabilityInvocationContext.Enter(new(40, 4)))
    {
        await Assert.ThrowsAsync<WidgetCapabilityException>(async () =>
            await adapter.InvokeAsync(
                WidgetMediaCapabilities.Control,
                new ControlWidgetMediaSessionRequest("media-1", WidgetMediaSessionCommand.Next)));
    }
    Assert.Equal(1, backend.MediaControlCalls);

    server.GrantDashboardGestureAuthority(
        PlatformCapabilities.MediaSessionsControlV1,
        PlatformCapabilities.MediaSessionControl,
        41,
        5,
        TimeSpan.FromSeconds(2));
    var releaseBackground = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    Task leakedInvocation;
    using (WidgetCapabilityInvocationContext.Enter(new(41, 5)))
    {
        leakedInvocation = Task.Run(async () =>
        {
            await releaseBackground.Task;
            await adapter.InvokeAsync(
                WidgetMediaCapabilities.Control,
                new ControlWidgetMediaSessionRequest(
                    "media-1", WidgetMediaSessionCommand.Next));
        });
    }
    releaseBackground.SetResult();
    await Assert.ThrowsAsync<WidgetCapabilityException>(() => leakedInvocation);
    Assert.Equal(1, backend.MediaControlCalls);
    await transport.DisposeAsync();
    await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
}

static async Task ControllerQueueCancelsOnDeactivation()
{
    await using var client = CreateClient();
    var snapshot = await client.GetSnapshotAsync();
    await client.SetActiveAsync(true);
    var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Invalidated += (_, _) => invalidated.TrySetResult();
    Assert.True(await client.SendControllerInputAsync(OpenInput(
        snapshot, ControllerButton.RightBumper, "button", inputSequence: 99)),
        "Delayed controller action was not accepted.");
    await client.SetActiveAsync(false);
    await Task.Delay(TimeSpan.FromMilliseconds(500));
    Assert.True(!invalidated.Task.IsCompleted, "Deactivated queued work must not invalidate later.");
}

static async Task RuntimeScopedShortcutRouting()
{
    await using var client = CreateClient();
    _ = await client.GetSnapshotAsync();
    await client.SetActiveAsync(true);
    await client.SendActionAsync(new WidgetActionEvent("show-nested", "test"));
    var nested = await client.GetSnapshotAsync();
    var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Invalidated += (_, _) => invalidated.TrySetResult();
    Assert.True(!await client.SendControllerInputAsync(OpenInput(
        nested with { Sequence = nested.Sequence - 1 }, ControllerButton.LeftBumper, "nested-focus")),
        "Stale snapshot sequence must be rejected.");
    Assert.True(!await client.SendControllerInputAsync(new ControllerInputEvent(
        ControllerButton.LeftBumper,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        FocusedElementId: "nested-focus",
        ActiveInputScopeId: "root",
        SnapshotSequence: nested.Sequence)), "Wrong active scope must be rejected.");
    Assert.True(!await client.SendControllerInputAsync(OpenInput(
        nested, ControllerButton.LeftBumper, "button")),
        "Focus outside the active scope must be rejected.");
    Assert.True(await client.SendControllerInputAsync(OpenInput(
        nested, ControllerButton.LeftBumper, "nested-focus")),
        "Nested surface shortcut was not accepted.");
    await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var updatedNested = await client.GetSnapshotAsync();
    Assert.Equal("nested", Find(updatedNested.Root, "scoped-action").Text);

    Assert.True(await client.SendControllerInputAsync(OpenInput(
        updatedNested, ControllerButton.B, null)),
        "Focusless modal B shortcut on the scope container was not accepted.");

    await client.SendActionAsync(new WidgetActionEvent("show-empty", "test"));
    var empty = await client.GetSnapshotAsync();
    Assert.True(!await client.SendControllerInputAsync(OpenInput(
        empty, ControllerButton.LeftBumper, "empty-focus")),
        "An empty nested surface must not bubble to the root LeftBumper binding.");
}

static async Task ControllerQueueFailuresAreObservable()
{
    await using var client = CreateClient();
    var snapshot = await client.GetSnapshotAsync();
    await client.SetActiveAsync(true);
    var failed = new TaskCompletionSource<WidgetControllerActionFailure>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    client.ControllerActionFailed += (_, failure) => failed.TrySetResult(failure);
    Assert.True(await client.SendControllerInputAsync(OpenInput(
        snapshot, ControllerButton.LeftBumper, "button", inputSequence: 7)),
        "Failing action was not accepted.");
    var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("queued-fail", failure.ActionId);
    Assert.True(failure.Message.Contains("intentional", StringComparison.Ordinal),
        "Failure should retain bounded diagnostic context.");
    Assert.True(client.IsRunning, "An action failure must not crash the worker.");
}

static async Task ControllerQueueIsBounded()
{
    await using var client = CreateClient();
    var snapshot = await client.GetSnapshotAsync();
    await client.SetActiveAsync(true);
    var accepted = 0;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    for (var sequence = 1; sequence <= Widget.ControllerActionQueueCapacity * 2; sequence++)
    {
        if (await client.SendControllerInputAsync(OpenInput(
                snapshot, ControllerButton.RightTrigger, "button", inputSequence: sequence)))
            accepted++;
    }
    stopwatch.Stop();
    Assert.True(accepted <= Widget.ControllerActionQueueCapacity + 1,
        $"Queue accepted an unbounded number of waiters ({accepted}).");
    Assert.True(accepted < Widget.ControllerActionQueueCapacity * 2,
        "A saturated queue must reject input rather than wait for capacity.");
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1),
        $"Saturated acknowledgements blocked for {stopwatch.Elapsed.TotalMilliseconds:0} ms.");
    await client.SetActiveAsync(false);
}

static ControllerInputEvent OpenInput(
    ViewSnapshot snapshot,
    ControllerButton button,
    string? focusedElementId,
    long inputSequence = 0) => new(
        button,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        focusedElementId,
        Sequence: inputSequence,
        ActiveInputScopeId: snapshot.ActiveInputScopeId,
        SnapshotSequence: snapshot.Sequence);

static async Task CrashRecovery()
{
    await using var client = CreateClient(maximumRestarts: 1);
    _ = await client.GetSnapshotAsync();
    var firstProcess = client.WorkerProcessId;
    var failed = new TaskCompletionSource<WidgetFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Failed += (_, failure) => failed.TrySetResult(failure);
    await Assert.ThrowsAnyAsync(() =>
        client.SendActionAsync(new WidgetActionEvent("crash", "button")));
    var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.True(failure.CanRestart, "One restart should remain after first crash.");
    var recovered = await client.GetSnapshotAsync();
    Assert.Equal("runtime.test", recovered.WidgetInstanceId);
    Assert.Equal(2, client.Starts);
    Assert.True(client.WorkerProcessId != firstProcess, "Restart reused the terminated worker process.");
    if (OperatingSystem.IsWindows())
        Assert.Equal((uint)1, client.AppliedJobActiveProcessLimit);
}

static async Task CompanionSessionsFollowWorkerRestarts()
{
    var sessions = new List<ProbeCompanionSession>();
    var client = CreateClient(
        maximumRestarts: 1,
        companionFactory: _ =>
        {
            var session = new ProbeCompanionSession();
            sessions.Add(session);
            return session;
        });
    try
    {
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
        Assert.Equal(1, sessions.Count);
        Assert.Equal(client.WorkerProcessId, sessions[0].BoundWorkerProcessId);
        Assert.True(sessions[0].RunStartedAfterBinding,
            "Companion started accepting IPC before its worker PID was bound.");
        Assert.SequenceEqual(
            new[] { WidgetLifecycleState.Visible },
            sessions[0].LifecycleStates);

        await Assert.ThrowsAnyAsync(() =>
            client.SendActionAsync(new WidgetActionEvent("crash", "button")));
        _ = await client.GetSnapshotAsync();

        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].Disposed, "Restart did not dispose the previous companion session.");
        Assert.Equal(client.WorkerProcessId, sessions[1].BoundWorkerProcessId);
        Assert.True(sessions[1].RunStartedAfterBinding,
            "Restarted companion started accepting IPC before PID binding.");
        Assert.SequenceEqual(
            new[] { WidgetLifecycleState.Visible },
            sessions[1].LifecycleStates);
    }
    finally
    {
        await client.DisposeAsync();
    }
    Assert.True(sessions[1].Disposed, "Client disposal left its companion session alive.");
}

static async Task IntentionalUnloadIsReusable()
{
    var sessions = new List<ProbeCompanionSession>();
    await using var client = CreateClient(
        maximumRestarts: 0,
        companionFactory: _ =>
        {
            var session = new ProbeCompanionSession();
            sessions.Add(session);
            return session;
        });
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var firstProcess = client.WorkerProcessId;
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    await client.UnloadAsync();
    Assert.True(!client.IsRunning, "Idle unload left the worker resident.");
    Assert.True(sessions[0].Disposed, "Idle unload retained the capability companion.");
    Assert.SequenceEqual(
        new[]
        {
            WidgetLifecycleState.Visible,
            WidgetLifecycleState.Background,
            WidgetLifecycleState.Destroying,
        },
        sessions[0].LifecycleStates);

    var resumed = await client.GetSnapshotAsync();
    Assert.Equal("runtime.test", resumed.WidgetInstanceId);
    Assert.Equal(2, client.Starts);
    Assert.True(client.WorkerProcessId != firstProcess,
        "Residency resume reused a destroyed process.");
    Assert.Equal(2, sessions.Count);

    await client.UnloadAsync();
    _ = await client.GetSnapshotAsync();
    Assert.Equal(3, client.Starts);
    Assert.Equal(3, sessions.Count);
}

static async Task CrashedSessionIsNotIntentionalUnload()
{
    await using var client = CreateClient(maximumRestarts: 0);
    _ = await client.GetSnapshotAsync();
    await Assert.ThrowsAnyAsync(() =>
        client.SendActionAsync(new WidgetActionEvent("crash", "button")));
    Assert.True(!client.IsRunning, "Crash probe worker unexpectedly remained live.");

    // A residency timer can observe the crash only after its delay. Treating
    // this no-session cleanup as an intentional unload would incorrectly let
    // the next launch bypass the zero-restart policy.
    await client.UnloadAsync();
    await Assert.ThrowsAsync<WidgetProcessException>(() => client.GetSnapshotAsync());
    Assert.Equal(1, client.Starts);
}

static async Task CompanionDisposalIsBounded()
{
    var companion = new NonCompletingDisposeCompanionSession();
    await using var client = CreateClient(
        maximumRestarts: 0,
        companionFactory: _ => companion);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    await client.UnloadAsync();
    stopwatch.Stop();
    Assert.True(companion.DisposeStarted,
        "Residency teardown did not invoke companion disposal.");
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
        $"Non-completing companion held teardown for {stopwatch.Elapsed.TotalMilliseconds:0} ms.");
    Assert.True(!client.IsRunning,
        "Bounded companion cleanup retained the worker process session.");
}

static async Task CompanionEndpointPrecedesLaunch()
{
    if (!OperatingSystem.IsWindows()) return;
    await using var client = CreateClient(
        companionFactory: _ => new PrecreatedPipeCompanionSession());
    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal("runtime.test", snapshot.WidgetInstanceId);
}

static async Task HungWorkerTimesOut()
{
    await using var client = CreateClient(requestTimeout: TimeSpan.FromMilliseconds(250));
    _ = await client.GetSnapshotAsync();
    var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
        client.SendActionAsync(new WidgetActionEvent("hang", "button")));
    Assert.True(exception.Message.Contains("exceeded", StringComparison.Ordinal), "Expected timeout details.");
}

static async Task MalformedSnapshotIsRejected()
{
    await using var client = CreateClient(extraArguments: ["--malformed-worker"]);
    await Assert.ThrowsAsync<WidgetProtocolViolationException>(() => client.GetSnapshotAsync());
}

static async Task DestroyIsBounded()
{
    await using var client = CreateClient(
        requestTimeout: TimeSpan.FromSeconds(4),
        extraArguments: ["--hanging-destroy"]);
    _ = await client.GetSnapshotAsync();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    await client.StopAsync();
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3.5),
        $"Worker shutdown was not bounded ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");
    Assert.True(!client.IsRunning, "Bounded shutdown left the worker running.");
}

static WidgetProcessClient CreateClient(
    int maximumRestarts = 2,
    TimeSpan? requestTimeout = null,
    IReadOnlyList<string>? extraArguments = null,
    long memoryLimitBytes = 64L * 1024 * 1024,
    Func<WidgetProcessCompanionContext, IWidgetProcessCompanionSession>? companionFactory = null,
    TimeProvider? timeProvider = null)
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test process path is unavailable.");
    var options = new WidgetProcessOptions
    {
        ExecutablePath = executable,
        Arguments = extraArguments ?? [],
        WidgetInstanceId = "runtime.test",
        ConnectTimeout = TimeSpan.FromSeconds(3),
        RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(2),
        MaximumRestartAttempts = maximumRestarts,
        MaximumMessageBytes = 64 * 1024,
        MemoryLimitBytes = memoryLimitBytes,
        CompanionSessionFactory = companionFactory,
    };
    return timeProvider is null
        ? new WidgetProcessClient(options)
        : new WidgetProcessClient(options, timeProvider);
}

static string RequiredValue(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    if (index < 0 || index + 1 >= values.Length) throw new ArgumentException($"Missing {name}.");
    return values[index + 1];
}

static string? OptionalValue(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    return index < 0 ? null : RequiredValue(values, name);
}

static ViewNode Find(ViewNode node, string id)
{
    if (node.Id == id) return node;
    foreach (var child in node.Children)
    {
        try { return Find(child, id); }
        catch (KeyNotFoundException) { }
    }
    throw new KeyNotFoundException(id);
}

file sealed class TestWidget : Widget
{
    private readonly object _historyLock = new();
    private readonly List<long> _controllerHistory = [];
    private string _scopedAction = "none";
    private string _activeScope = "root";

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Text(IsActive ? "active" : "inactive", "activity"),
            UI.Text(ControllerHistory(), "controller-history"),
            UI.Text(_scopedAction, "scoped-action"),
            UI.Button("Test", "invalidate", "button")
                .Shortcut(ControllerButton.RightBumper)
                .Shortcut(ControllerButton.LeftBumper, actionId: "queued-fail")
                .Shortcut(ControllerButton.RightTrigger, actionId: "queued-block"),
            UI.Stack("nested-window",
                UI.Button("Nested command", "nested", "nested-command"),
                UI.Button("Nested focus", "nested-focus", "nested-focus"))
                .InputScope("nested-window-scope")
                .Shortcut(ControllerButton.LeftBumper, "nested")
                .Shortcut(ControllerButton.B, "nested-close"),
            UI.Stack("empty-window",
                UI.Button("Empty focus", "empty-focus", "empty-focus"))
                .InputScope("empty-window-scope")),
        _activeScope switch
        {
            "nested-window-scope" => "nested-focus",
            "empty-window-scope" => "empty-focus",
            _ => "button",
        },
        [new WidgetQuickAction(ControllerButton.X, "invalidate", "Refresh")],
        _activeScope);

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (action.ActionId == "invalidate")
        {
            if (action.ControllerButton is not null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
                lock (_historyLock) _controllerHistory.Add(action.Sequence);
            }
            Invalidate();
        }
        else if (action.ActionId == "queued-fail")
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            throw new InvalidOperationException("intentional queued action failure");
        }
        else if (action.ActionId == "queued-block")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        else if (action.ActionId == "nested")
        {
            _scopedAction = action.ActionId;
            Invalidate();
        }
        else if (action.ActionId == "nested-close")
        {
            _scopedAction = action.ActionId;
            Invalidate();
        }
        else if (action.ActionId == "show-nested")
        {
            _activeScope = "nested-window-scope";
            Invalidate();
        }
        else if (action.ActionId == "show-empty")
        {
            _activeScope = "empty-window-scope";
            Invalidate();
        }
        else if (action.ActionId == "crash")
        {
            Environment.Exit(23);
        }
        else if (action.ActionId == "hang")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private string ControllerHistory()
    {
        lock (_historyLock) return string.Join(',', _controllerHistory);
    }
}

file sealed class GestureQueueWidget : Widget
{
    private readonly object _historyLock = new();
    private readonly List<long> _history = [];

    public override WidgetView Render() => new(
        UI.Stack("root", UI.Text(History(), "gesture-history")),
        QuickActions:
        [
            new WidgetQuickAction(
                ControllerButton.X,
                "gesture.slow",
                "Slow",
                new WidgetQuickActionCapability(
                    WidgetMediaCapabilities.Control.CapabilityId,
                    WidgetMediaCapabilities.Control.OperationId)),
            new WidgetQuickAction(
                ControllerButton.RightBumper,
                "gesture.fast",
                "Fast",
                new WidgetQuickActionCapability(
                    WidgetMediaCapabilities.Control.CapabilityId,
                    WidgetMediaCapabilities.Control.OperationId)),
        ]);

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        if (action.ActionId == "gesture.slow")
            await Task.Delay(TimeSpan.FromMilliseconds(2_300), cancellationToken);
        if (action.ActionId is not ("gesture.slow" or "gesture.fast")) return;

        await HostServices.Media.ControlAsync(
            "media-1", WidgetMediaSessionCommand.Next, cancellationToken);
        lock (_historyLock) _history.Add(action.Sequence);
        Invalidate();
    }

    private string History()
    {
        lock (_historyLock) return string.Join(',', _history);
    }
}

file sealed class CustomGestureWidget : Widget
{
    private long _handledSequence;

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Text(Interlocked.Read(ref _handledSequence).ToString(CultureInfo.InvariantCulture),
                "gesture-history")),
        QuickActions:
        [
            new WidgetQuickAction(
                ControllerButton.X,
                "gesture.custom",
                "Custom",
                new WidgetQuickActionCapability(
                    WidgetMediaCapabilities.Control.CapabilityId,
                    WidgetMediaCapabilities.Control.OperationId)),
        ]);

    public override async ValueTask<bool> OnControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        if (input is not
            {
                Button: ControllerButton.X,
                Phase: ControllerEventPhase.Pressed,
                Context: ControllerInputContext.DashboardQuickAction,
            })
            return false;

        await HostServices.Media.ControlAsync(
            "media-1", WidgetMediaSessionCommand.Next, cancellationToken);
        Interlocked.Exchange(ref _handledSequence, input.Sequence);
        Invalidate();
        return true;
    }
}

file sealed class GestureProbeCapabilityClient :
    IWidgetCapabilityClient,
    IDashboardGestureActivatingCapabilityClient
{
    private Func<WidgetCapabilityGestureContext, string, string, CancellationToken, ValueTask<bool>>?
        _activator;

    public bool IsAvailable => true;

    void IDashboardGestureActivatingCapabilityClient.SetDashboardGestureActivator(
        Func<WidgetCapabilityGestureContext, string, string, CancellationToken, ValueTask<bool>>
            activator) =>
        _activator = activator;

    public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var gesture = WidgetCapabilityInvocationContext.Current;
        if (gesture is null || !gesture.IsActive || _activator is null)
            throw new WidgetCapabilityException(
                "lifecycle_denied", "The test control call has no active dashboard gesture.");
        if (!await _activator(
                gesture,
                operation.CapabilityId,
                operation.OperationId,
                cancellationToken).ConfigureAwait(false))
            throw new WidgetCapabilityException(
                "lifecycle_denied", "The host rejected dashboard gesture activation.");
        return (TResponse)(object)new WidgetCapabilityAcknowledgement(true);
    }

    public ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<IWidgetCapabilitySubscription<TPayload>>(
            new NotSupportedException("The gesture probe has no event subscriptions."));
}

file sealed class IsolationProbeWidget(
    string readablePath,
    string deniedPath,
    string? otherProfilePath,
    int networkPort,
    string secretName) : Widget
{
    private IsolationTokenResult _token = new(false, string.Empty, false, uint.MaxValue);
    private string _readable = "unprobed";
    private string _packageWrite = "unprobed";
    private string _deniedRead = "unprobed";
    private string _otherProfileRead = "not-requested";
    private string _network = "unprobed";
    private string _secret = "unprobed";
    private string _profileFile = string.Empty;

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Text(_token.IsAppContainer ? "true" : "false", "probe-appcontainer"),
            UI.Text(_token.Sid, "probe-sid"),
            UI.Text(_token.IsLowIntegrity ? "low" : "not-low", "probe-integrity"),
            UI.Text(_token.CapabilityCount.ToString(CultureInfo.InvariantCulture), "probe-capabilities"),
            UI.Text(_readable, "probe-readable"),
            UI.Text(_packageWrite, "probe-package-write"),
            UI.Text(_deniedRead, "probe-denied-read"),
            UI.Text(_otherProfileRead, "probe-other-profile-read"),
            UI.Text(_network, "probe-network"),
            UI.Text(_secret, "probe-secret"),
            UI.Text(_profileFile, "probe-profile-file")));

    protected override async ValueTask OnCreatedAsync(CancellationToken widgetLifetime)
    {
        _token = IsolationTokenInspector.Read();
        _readable = await File.ReadAllTextAsync(readablePath, widgetLifetime);
        _packageWrite = await TryWriteAsync(
            Path.Combine(Path.GetDirectoryName(readablePath)!, "unauthorized-write.tmp"),
            widgetLifetime);
        _deniedRead = await TryReadAsync(deniedPath, widgetLifetime);
        if (otherProfilePath is not null)
            _otherProfileRead = await TryReadAsync(otherProfilePath, widgetLifetime);
        _secret = Environment.GetEnvironmentVariable(secretName) is null ? "absent" : "present";
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? throw new InvalidOperationException("AppContainer LOCALAPPDATA is unavailable.");
        _profileFile = Path.Combine(localAppData, "isolation-probe-private.txt");
        await File.WriteAllTextAsync(_profileFile, _token.Sid, widgetLifetime);

        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(widgetLifetime);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, networkPort, timeout.Token);
            _network = "connected";
        }
        catch (Exception exception) when (
            exception is SocketException or OperationCanceledException or UnauthorizedAccessException)
        {
            _network = "denied";
        }
    }

    private static async Task<string> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            _ = await File.ReadAllTextAsync(path, cancellationToken);
            return "readable";
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return "denied";
        }
    }

    private static async Task<string> TryWriteAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(path, "unauthorized", cancellationToken);
            return "writable";
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return "denied";
        }
    }
}

file sealed record IsolationTokenResult(
    bool IsAppContainer,
    string Sid,
    bool IsLowIntegrity,
    uint CapabilityCount);

file static class IsolationTokenInspector
{
    public static IsolationTokenResult Read()
    {
        const uint tokenQuery = 0x0008;
        const int tokenIntegrityLevel = 25;
        const int tokenIsAppContainer = 29;
        const int tokenCapabilities = 30;
        const int tokenAppContainerSid = 31;
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(), tokenQuery, out var token))
            throw new InvalidOperationException("Could not open the isolation probe token.");
        using (token)
        using (var isAppContainer = Read(token, tokenIsAppContainer))
        using (var appContainer = Read(token, tokenAppContainerSid))
        using (var integrity = Read(token, tokenIntegrityLevel))
        using (var capabilities = Read(token, tokenCapabilities))
        {
            var sid = Marshal.ReadIntPtr(appContainer.Pointer);
            return new IsolationTokenResult(
                Marshal.ReadInt32(isAppContainer.Pointer) == 1,
                SidToString(sid),
                IntegrityRid(Marshal.ReadIntPtr(integrity.Pointer)) == 0x00001000,
                unchecked((uint)Marshal.ReadInt32(capabilities.Pointer)));
        }
    }

    private static uint IntegrityRid(IntPtr sid)
    {
        var countPointer = NativeMethods.GetSidSubAuthorityCount(sid);
        var count = countPointer == IntPtr.Zero ? (byte)0 : Marshal.ReadByte(countPointer);
        var rid = count == 0 ? IntPtr.Zero : NativeMethods.GetSidSubAuthority(sid, (uint)(count - 1));
        return rid == IntPtr.Zero ? uint.MaxValue : unchecked((uint)Marshal.ReadInt32(rid));
    }

    private static string SidToString(IntPtr sid)
    {
        if (sid == IntPtr.Zero || !NativeMethods.ConvertSidToStringSidW(sid, out var value))
            throw new InvalidOperationException("Could not stringify the isolation probe SID.");
        try { return Marshal.PtrToStringUni(value) ?? string.Empty; }
        finally { _ = NativeMethods.LocalFree(value); }
    }

    private static TokenBuffer Read(SafeAccessTokenHandle token, int informationClass)
    {
        _ = NativeMethods.GetTokenInformation(token, informationClass, IntPtr.Zero, 0u, out var required);
        if (required == 0) throw new InvalidOperationException("Could not size isolation token data.");
        var pointer = Marshal.AllocHGlobal(checked((int)required));
        if (!NativeMethods.GetTokenInformation(token, informationClass, pointer, required, out _))
        {
            Marshal.FreeHGlobal(pointer);
            throw new InvalidOperationException("Could not read isolation token data.");
        }
        return new TokenBuffer(pointer);
    }

    private sealed class TokenBuffer(IntPtr pointer) : IDisposable
    {
        public IntPtr Pointer { get; private set; } = pointer;
        public void Dispose()
        {
            if (Pointer == IntPtr.Zero) return;
            Marshal.FreeHGlobal(Pointer);
            Pointer = IntPtr.Zero;
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(
            IntPtr process, uint desiredAccess, out SafeAccessTokenHandle token);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(
            SafeAccessTokenHandle token,
            int informationClass,
            IntPtr tokenInformation,
            uint tokenInformationLength,
            out uint returnLength);

        [DllImport("advapi32.dll")]
        internal static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("advapi32.dll")]
        internal static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr stringSid);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}

file sealed class LifecycleProbeWidget : Widget
{
    public int Activations { get; private set; }
    public int Deactivations { get; private set; }
    public CancellationToken Lifetime => ActiveCancellationToken;
    public override WidgetView Render() => new(UI.Stack("root"));
    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Activations++;
        return ValueTask.CompletedTask;
    }
    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        Deactivations++;
        return ValueTask.CompletedTask;
    }
}

file sealed class LifecycleContractProbeWidget : Widget
{
    public List<string> Events { get; } = [];
    public Task ProcessLifetimeWork { get; private set; } = Task.CompletedTask;
    public WidgetLifecycleState CurrentState => LifecycleState;
    public CancellationToken CurrentWidgetLifetime => WidgetLifetimeToken;
    public CancellationToken CurrentStateLifetime => StateLifetimeToken;
    public CancellationToken CurrentActiveLifetime => ActiveCancellationToken;

    public override WidgetView Render() => new(UI.Stack("root"));

    protected override ValueTask OnCreatedAsync(CancellationToken widgetLifetime)
    {
        Events.Add("created");
        Assert.True(widgetLifetime == WidgetLifetimeToken, "OnCreated received the wrong widget lifetime.");
        ProcessLifetimeWork = Task.Delay(TimeSpan.FromMilliseconds(100), widgetLifetime);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        Events.Add($"{previous}->{current}");
        Assert.Equal(current, LifecycleState);
        Assert.True(stateLifetime == StateLifetimeToken, "Lifecycle callback received the wrong state lifetime.");
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Events.Add("activated");
        Assert.True(activeLifetime == ActiveCancellationToken, "Activation received the wrong lifetime.");
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        Events.Add("deactivated");
        Assert.True(CurrentActiveLifetime.IsCancellationRequested,
            "Active lifetime must be canceled before deactivation.");
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        Events.Add("destroying");
        Assert.True(WidgetLifetimeToken.IsCancellationRequested,
            "Widget lifetime must be canceled before destruction.");
        Assert.True(StateLifetimeToken.IsCancellationRequested,
            "State lifetime must be canceled before destruction.");
        return ValueTask.CompletedTask;
    }
}

file sealed class HangingDestroyWidget : Widget
{
    public override WidgetView Render() => new(UI.Stack("root"));

    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken) =>
        await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
}

file sealed class ProbeCompanionSession : IWidgetProcessCompanionSession
{
    public IReadOnlyList<string> WorkerArguments { get; } = [];
    public List<WidgetLifecycleState> LifecycleStates { get; } = [];
    public bool Disposed { get; private set; }
    public int? BoundWorkerProcessId { get; private set; }
    public bool RunStartedAfterBinding { get; private set; }
    public List<WidgetDashboardGestureAuthority> GrantedAuthorities { get; } = [];
    public List<DateTimeOffset> GrantTimes { get; } = [];
    public List<long> RevokedInputSequences { get; } = [];

    public void BindWorkerProcess(int processId) => BoundWorkerProcessId = processId;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        RunStartedAfterBinding = BoundWorkerProcessId is > 0;
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LifecycleStates.Add(state);
        return Task.CompletedTask;
    }

    public Task GrantDashboardGestureAuthorityAsync(
        WidgetDashboardGestureAuthority authority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrantedAuthorities.Add(authority);
        GrantTimes.Add(DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task RevokeDashboardGestureAuthorityAsync(
        long inputSequence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RevokedInputSequences.Add(inputSequence);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

file sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    public void Advance(TimeSpan elapsed) =>
        Interlocked.Add(ref _timestamp, elapsed.Ticks);
}

file sealed class NonCompletingDisposeCompanionSession : IWidgetProcessCompanionSession
{
    private readonly TaskCompletionSource _never = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyList<string> WorkerArguments { get; } = [];
    public bool DisposeStarted { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeStarted = true;
        return new ValueTask(_never.Task);
    }
}

file sealed class PrecreatedPipeCompanionSession : IWidgetProcessCompanionSession
{
    private readonly System.IO.Pipes.NamedPipeServerStream _endpoint;

    public PrecreatedPipeCompanionSession()
    {
        var pipeName = $"gba-companion-prelaunch-{Guid.NewGuid():N}";
        _endpoint = new System.IO.Pipes.NamedPipeServerStream(
            pipeName, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous |
            System.IO.Pipes.PipeOptions.CurrentUserOnly |
            System.IO.Pipes.PipeOptions.FirstPipeInstance);
        WorkerArguments = ["--probe-precreated-pipe", pipeName];
    }

    public IReadOnlyList<string> WorkerArguments { get; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public Task SetLifecycleStateAsync(
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _endpoint.Dispose();
        return ValueTask.CompletedTask;
    }
}

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"gba-runtime-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

file static class Assert
{
    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void False(bool value, string message) => True(!value, message);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Sequences differ.");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static async Task ThrowsAnyAsync(Func<Task> action)
    {
        try { await action(); }
        catch { return; }
        throw new InvalidOperationException("Expected an exception.");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
