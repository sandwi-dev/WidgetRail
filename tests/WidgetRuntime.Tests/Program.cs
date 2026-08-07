using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
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
    ("Lifecycle callbacks and lifetime tokens follow exact transition order", LifecycleContract),
    ("Runtime-owned lifecycle states cannot be host targets", InvalidLifecycleTargets),
    ("Widget activation transitions are idempotent and cancel their lifetime", ActivationTransitions),
    ("Worker remains inactive until explicit activity transport", ActivityTransport),
    ("Actions deliver invalidation notifications", ActionsInvalidate),
    ("Raw controller input resolves only after a rendered snapshot", ControllerInputUsesLatestSnapshot),
    ("Rapid dashboard actions acknowledge quickly and execute in order", RapidDashboardActionsAreQueued),
    ("Rapid controller inputs acknowledge quickly and execute in order", RapidControllerInputsAreQueued),
    ("Runtime shortcut fallback respects explicit active input surface", RuntimeScopedShortcutRouting),
    ("Queued controller work cancels on deactivation", ControllerQueueCancelsOnDeactivation),
    ("Controller queue rejects saturation without waiting", ControllerQueueIsBounded),
    ("Queued controller failures are observable without crashing", ControllerQueueFailuresAreObservable),
    ("Unexpected worker exit is reported and recoverable", CrashRecovery),
    ("Host companion sessions are recreated and lifecycle-restored after crashes", CompanionSessionsFollowWorkerRestarts),
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

    Widget widget = arguments.Contains("--hanging-destroy", StringComparer.Ordinal)
        ? new HangingDestroyWidget()
        : new TestWidget();
    await new WidgetWorkerServer(widget, instance, pipe, maximumBytes).RunAsync();
    return 0;
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
        ControllerInputContext.DashboardQuickAction));
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
        Sequence: 4));
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
    _ = await client.GetSnapshotAsync();
    Assert.True(!await client.SendControllerInputAsync(new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction)),
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
            Sequence: sequence));
        Assert.True(handled, $"Rapid dashboard action {sequence} was not accepted.");
    }
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
        $"Dashboard acknowledgements waited for action work ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");

    for (var expected = 1L; expected <= 3L; expected++)
        Assert.Equal(expected, await invalidations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
    Assert.Equal("1,2,3", Find((await client.GetSnapshotAsync()).Root, "controller-history").Text);
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
        companionFactory: () =>
        {
            var session = new ProbeCompanionSession();
            sessions.Add(session);
            return session;
        });
    try
    {
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
        Assert.Equal(1, sessions.Count);
        Assert.SequenceEqual(
            new[] { WidgetLifecycleState.Visible },
            sessions[0].LifecycleStates);

        await Assert.ThrowsAnyAsync(() =>
            client.SendActionAsync(new WidgetActionEvent("crash", "button")));
        _ = await client.GetSnapshotAsync();

        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].Disposed, "Restart did not dispose the previous companion session.");
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
    Func<IWidgetProcessCompanionSession>? companionFactory = null)
{
    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test process path is unavailable.");
    return new WidgetProcessClient(new WidgetProcessOptions
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
    });
}

static string RequiredValue(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    if (index < 0 || index + 1 >= values.Length) throw new ArgumentException($"Missing {name}.");
    return values[index + 1];
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
                UI.Button("Nested command", "nested", "nested-command")
                    .Shortcut(ControllerButton.LeftBumper),
                UI.Button("Nested focus", "nested-focus", "nested-focus"))
                .InputScope("nested-window-scope")
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
        LifecycleStates.Add(state);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
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
