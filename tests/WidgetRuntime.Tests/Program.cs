using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

#pragma warning disable CA1416 // Windows ACL fixtures return early on other platforms.

if (args.Contains("--containment-sleeper", StringComparer.Ordinal))
{
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

if (args.Contains("--containment-parent", StringComparer.Ordinal))
{
    var childPath = RequiredValue(args, "--child-pid-file");
    try
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--containment-sleeper");
        using var child = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Contained helper did not start.");
        await File.WriteAllTextAsync(childPath, child.Id.ToString(CultureInfo.InvariantCulture));
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }
    catch (Exception exception)
    {
        await File.WriteAllTextAsync(childPath, $"error:{exception.GetType().Name}:{exception.HResult}");
        return 97;
    }
}

if (args.Contains("--authority-crash-probe", StringComparer.Ordinal))
{
    RunAuthorityCrashProbe(args);
    return 92;
}

if (args.Contains("--widget-pipe", StringComparer.Ordinal))
{
    try
    {
        return await RunWorkerAsync(args);
    }
    catch (Exception exception) when (exception is not OutOfMemoryException)
    {
        var diagnosticPath = OptionalValue(args, "--worker-exception-diagnostic");
        if (diagnosticPath is not null)
            await File.WriteAllTextAsync(diagnosticPath, exception.ToString());
        throw;
    }
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Length framing rejects oversized input before allocation", OversizedFrameIsRejected),
    ("Pending worker requests correlate and drain through one typed owner", WidgetProcessOwnershipScenarios.PendingRequestsCorrelateExactly),
    ("Dashboard gesture reservations match and expire through one policy", WidgetProcessOwnershipScenarios.GestureReservationsAreExactAndExpire),
    ("Worker session terminal cleanup is shared and exact", WidgetProcessOwnershipScenarios.SessionTerminalCleanupIsShared),
    ("Replacement sessions share no request or gesture state", WidgetProcessOwnershipScenarios.ReplacementSessionsDoNotShareMutableAuthority),
    ("Stop linearizes against paused worker construction", WidgetProcessOwnershipScenarios.StopDuringConstructionRejectsLateResources),
    ("Retired sessions cannot publish notifications into replacements", WidgetProcessOwnershipScenarios.StaleNotificationsCannotCrossReplacement),
    ("Retired responses cannot complete after replacement", WidgetProcessOwnershipScenarios.StaleResponseCannotCompleteAfterReplacement),
    ("Retired sessions cannot grant gesture authority into replacements", WidgetProcessOwnershipScenarios.StaleGestureCannotGrantReplacementAuthority),
    ("Cancellation-ignoring retired gesture grants are revoked", WidgetProcessOwnershipScenarios.CancellationIgnoringGestureGrantIsRevoked),
    ("Worker launch is lazy and snapshot is validated", LazyLaunchAndSnapshot),
    ("Negotiated presentation updates materialize against the exact runtime base", NegotiatedPresentationUpdates),
    ("Frozen runtime-v2 applications retain checkpoint compatibility", WidgetRuntimeProtocolCompatibilityScenarios.FrozenV2ApplicationCheckpointCompatibility),
    ("Worker handshake requires the exact random session nonce", SessionNonceMismatchIsRejected),
    ("Process admission failures stay pre-launch and are not worker failures", ProcessAdmissionFailsBeforeLaunch),
    ("Process residency leases follow exact worker sessions", ProcessLeaseFollowsSession),
    ("Content admission failures release residency before launch", ContentAdmissionFailsBeforeLaunch),
    ("Content admission has a bounded pre-launch deadline", ContentAdmissionTimeoutIsBounded),
    ("Caller cancellation remains cancellation during content admission", ContentAdmissionHonorsCallerCancellation),
    ("Verified content cannot overlap the trusted runtime grant", ContentAuthorityCannotOverlapRuntime),
    ("Content authority transactions restore every attempted DACL", ContentAuthorityTransactionRollsBack),
    ("Content authority recovery cancellation retains unfinished work", ContentAuthorityRecoveryHonorsCancellation),
    ("Complete content authority rollback clears its write-ahead record", ContentAuthorityRollbackIsRecoverable),
    ("Incomplete content authority rollback recovers before the next launch", ContentAuthorityRollbackRecoversGeneration),
    ("Content authority journal failures happen before ACL mutation", ContentAuthorityJournalFailsBeforeMutation),
    ("Host authority journal rejects corrupt and hostile entries", ContentAuthorityJournalRejectsUnsafeState),
    ("Profile authority quarantine permits disjoint worker recovery", ProfileQuarantinePermitsDisjointWorker),
    ("Profile authority quarantine rejects overlapping worker targets", ProfileQuarantineRejectsOverlap),
    ("Authority recovery retries exact state and rejects stale confirmation", AuthorityRecoveryIsExact),
    ("Legacy authority recovery uses the recorded profile owner", LegacyAuthorityRecoveryUsesOwner),
    ("Host authority journal recovers real DACLs after process termination", AuthorityJournalRecoversAfterHostTermination),
    ("Handle-bound authority cannot be redirected by path replacement", AuthorityHandlesResistPathReplacement),
    ("Pending authority recovery rejects changed object identity", AuthorityRecoveryRejectsIdentityChange),
    ("Alternate AppContainer package authority fails before mutation", AlternateAppContainerAuthorityFailsClosed),
    ("Content leases require exact object identity evidence", ContentLeaseRequiresObjectIdentity),
    ("Catalog identity mismatch rejects before authority mutation", ContentLeaseIdentityMismatchFailsClosed),
    ("Verified content leases follow exact worker sessions", ContentLeaseFollowsSession),
    ("Private worker memory exceeds the prototype ceiling without Job refusal", PrivateMemoryExceedsPrototypeCeiling),
    ("Windows worker Job Object preserves containment without size ceilings", WindowsJobPreservesContainment),
    ("Windows Job Object kill-on-close cleans up its process", WindowsJobCleansUpProcess),
    ("Windows worker Job Object owns and kills a helper process tree", WindowsJobOwnsProcessTree),
    ("Community workers have package-specific AppContainer authority", AppContainerIsolation),
    ("AppContainer content authority excludes late unverified files", ExactContentAuthority),
    ("Content-generation identities do not inherit stale root grants", StaleContentRootIsIsolated),
    ("Maximum exact content grant stays within the activation budget", MaximumExactContentGrantIsBounded),
    ("Lifecycle callbacks and lifetime tokens follow exact transition order", LifecycleContract),
    ("Runtime-owned lifecycle states cannot be host targets", InvalidLifecycleTargets),
    ("Widget activation transitions are idempotent and cancel their lifetime", ActivationTransitions),
    ("Worker remains inactive until explicit activity transport", ActivityTransport),
    ("Action admission preserves protocol-v1 empty acknowledgements", LegacyActionAdmissionCompatibility),
    ("Committed text crosses worker receive and action execution", CommittedTextCrossesWorkerActionQueue),
    ("Actions deliver invalidation notifications", ActionsInvalidate),
    ("Worker notification lane coalesces orders and drains boundedly", WidgetWorkerNotificationScenarios.LaneCoalescesOrdersAndDrainsBoundedly),
    ("Worker notification transport failure has one request-loop outcome", WidgetWorkerNotificationScenarios.TransportFailureHasOneRequestLoopOutcome),
    ("Worker action cursor completion reaches the worker pipe", WidgetWorkerNotificationScenarios.WorkerActionCursorCompletionReachesPipe),
    ("Lifecycle-first generated cursor action reaches the worker pipe", WidgetWorkerNotificationScenarios.LifecycleFirstCursorActionReachesPipe),
    ("Process client admits exactly one current invalidation", ProcessClientAdmitsExactlyOneCurrentInvalidation),
    ("Process client preserves exact-base cursor update semantics", ProcessClientCarriesLifecycleFirstCursorPagination),
    ("Direct and controller actions share one ordered queue", DirectAndControllerActionsShareQueue),
    ("Direct action admission is bounded and lifecycle-owned", DirectActionAdmissionIsBounded),
    ("Direct action failures are observable without crashing", DirectActionFailuresAreObservable),
    ("Raw controller input resolves only after a rendered snapshot", ControllerInputUsesLatestSnapshot),
    ("Rapid dashboard actions acknowledge quickly and execute in order", RapidDashboardActionsAreQueued),
    ("Dashboard authority is granted through the exact worker companion and revoked when unhandled", DashboardAuthorityUsesCompanion),
    ("Accessibility automation cannot reserve dashboard gesture authority", AccessibilityAutomationCannotReserveAuthority),
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
    ("Hung actions acknowledge promptly and cancel without restart", HungActionAdmissionIsPrompt),
    ("Worker protocol diagnostics expose only the first validation path and code", ProtocolValidationDiagnosticIsStructural),
    ("Malformed worker snapshots are rejected by host", MalformedSnapshotIsRejected),
    ("Worker destruction is bounded when widget cleanup hangs", DestroyIsBounded),
};

var testPrefixIndex = Array.IndexOf(args, "--test-prefix");
if (testPrefixIndex >= 0)
{
    if (testPrefixIndex + 1 >= args.Length)
        throw new ArgumentException("Missing --test-prefix value.");
    var prefix = args[testPrefixIndex + 1];
    tests = tests.Where(test => test.Name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
}

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
    var sessionNonce = RequiredValue(arguments, "--widget-session-nonce");
    var maximumBytes = int.Parse(
        RequiredValue(arguments, "--max-message-bytes"), CultureInfo.InvariantCulture);
    if (arguments.Contains("--malformed-worker", StringComparer.Ordinal))
        return await RunMalformedWorkerAsync(pipe, instance, sessionNonce, maximumBytes);
    VerifyPrecreatedCompanionEndpoint(arguments);

    var usesGestureProbe = arguments.Contains("--gesture-queue-probe", StringComparer.Ordinal) ||
        arguments.Contains("--gesture-custom-probe", StringComparer.Ordinal) ||
        arguments.Contains("--gesture-adversarial-probe", StringComparer.Ordinal);
    var gestureProbe = usesGestureProbe ? new GestureProbeCapabilityClient() : null;
    Widget widget = arguments.Contains("--hanging-destroy", StringComparer.Ordinal)
        ? new HangingDestroyWidget()
        : arguments.Contains("--invalid-protocol-widget", StringComparer.Ordinal)
            ? new InvalidProtocolWidget()
        : arguments.Contains("--gesture-queue-probe", StringComparer.Ordinal)
            ? new GestureQueueWidget()
        : arguments.Contains("--gesture-custom-probe", StringComparer.Ordinal)
            ? new CustomGestureWidget()
        : arguments.Contains("--gesture-adversarial-probe", StringComparer.Ordinal)
            ? new AdversarialGestureWidget(gestureProbe!)
        : arguments.Contains("--notification-cursor-probe", StringComparer.Ordinal)
            ? new DiagnosticCursorNotificationWidget()
        : arguments.Contains("--notification-lifecycle-cursor-probe", StringComparer.Ordinal)
            ? new DiagnosticCursorNotificationWidget(loadOnActivation: true)
        : arguments.Contains("--isolation-probe", StringComparer.Ordinal)
            ? new IsolationProbeWidget(
                RequiredValue(arguments, "--probe-readable-path"),
                RequiredValue(arguments, "--probe-denied-path"),
                OptionalValue(arguments, "--probe-other-profile-path"),
                int.Parse(RequiredValue(arguments, "--probe-network-port"), CultureInfo.InvariantCulture),
                RequiredValue(arguments, "--probe-secret-name"))
            : new TestWidget(int.TryParse(
                OptionalValue(arguments, "--private-memory-probe-mb"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var privateMemoryMb)
                ? privateMemoryMb
                : 0);
    IWidgetCapabilityClient? capabilityClient = gestureProbe;
    var workerNonce = arguments.Contains("--wrong-session-nonce", StringComparer.Ordinal)
        ? new string('0', 64)
        : sessionNonce;
    await new WidgetWorkerServer(
        widget, instance, pipe, maximumBytes, capabilityClient, workerNonce).RunAsync();
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

static async Task<int> RunMalformedWorkerAsync(
    string pipeName,
    string instanceId,
    string sessionNonce,
    int maximumBytes)
{
    await using var pipe = new System.IO.Pipes.NamedPipeClientStream(
        ".", pipeName, System.IO.Pipes.PipeDirection.InOut,
        System.IO.Pipes.PipeOptions.Asynchronous);
    await pipe.ConnectAsync();
    var channel = new LengthPrefixedJsonChannel(pipe, maximumBytes);
    await channel.WriteAsync(new RuntimeEnvelope
    {
        Type = MessageTypes.Hello,
        Payload = RuntimeJson.ToElement(new HelloPayload(instanceId, sessionNonce)),
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

static async Task ProcessAdmissionFailsBeforeLaunch()
{
    await using var client = CreateClient(processLeaseFactory: () =>
        throw new WidgetProcessAdmissionException("test capacity exhausted"));
    var failures = 0;
    client.Failed += (_, _) => failures++;

    var exception = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
        () => client.GetSnapshotAsync());
    Assert.True(exception.Message.Contains("capacity exhausted", StringComparison.Ordinal),
        "Admission refusal lost its actionable host diagnostic.");
    Assert.Equal(0, client.Starts);
    Assert.Equal(0, failures);
    Assert.True(!client.IsRunning, "Admission refusal started a worker process.");
}

static async Task ProcessLeaseFollowsSession()
{
    var acquired = 0;
    var released = 0;
    await using var client = CreateClient(
        maximumRestarts: 1,
        processLeaseFactory: () =>
        {
            Interlocked.Increment(ref acquired);
            return new CallbackDisposable(() => Interlocked.Increment(ref released));
        });

    _ = await client.GetSnapshotAsync();
    Assert.Equal(1, acquired);
    Assert.Equal(0, released);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    await SendCrashingActionAsync(client);
    await WaitUntilAsync(() => Volatile.Read(ref released) == 1, TimeSpan.FromSeconds(3));

    _ = await client.GetSnapshotAsync();
    Assert.Equal(2, acquired);
    Assert.Equal(1, released);
    await client.StopAsync();
    Assert.Equal(2, released);
}

static async Task ContentAdmissionFailsBeforeLaunch()
{
    var residencyAcquired = 0;
    var residencyReleased = 0;
    await using var client = CreateClient(
        processLeaseFactory: () =>
        {
            Interlocked.Increment(ref residencyAcquired);
            return new CallbackDisposable(() => Interlocked.Increment(ref residencyReleased));
        },
        contentLeaseFactory: _ => throw new WidgetProcessAdmissionException(
            "test content changed"),
        contentIsolationKey: $"runtime-content-refusal-{Guid.NewGuid():N}");
    var failures = 0;
    client.Failed += (_, _) => failures++;

    var exception = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
        () => client.GetSnapshotAsync());
    Assert.True(exception.Message.Contains("content changed", StringComparison.Ordinal),
        "Content admission refusal lost its stable host diagnostic.");
    Assert.Equal(1, residencyAcquired);
    Assert.Equal(1, residencyReleased);
    Assert.Equal(0, client.Starts);
    Assert.Equal(0, failures);
}

static async Task ContentAdmissionTimeoutIsBounded()
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    await using var client = CreateClient(
        contentLeaseFactory: cancellationToken =>
        {
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unreachable content-admission branch.");
        },
        contentIsolationKey: $"runtime-content-timeout-{Guid.NewGuid():N}",
        contentLeaseTimeout: TimeSpan.FromMilliseconds(100));

    var exception = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
        () => client.GetSnapshotAsync());
    stopwatch.Stop();
    Assert.True(exception.Message.Contains("time limit", StringComparison.Ordinal),
        "Content timeout lost its stable admission diagnostic.");
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
        $"Content admission timeout was not bounded ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");
    Assert.Equal(0, client.Starts);
}

static async Task ContentLeaseFollowsSession()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temp = new TemporaryDirectory();
    var verified = Path.Combine(temp.Path, "verified.txt");
    await File.WriteAllTextAsync(verified, "verified");
    var acquired = 0;
    var released = 0;
    await using var client = CreateClient(
        maximumRestarts: 1,
        contentLeaseFactory: _ =>
        {
            Interlocked.Increment(ref acquired);
            return new TestContentLease(
                temp.Path,
                [temp.Path],
                [verified],
                () => Interlocked.Increment(ref released));
        },
        contentIsolationKey: $"runtime-content-lifecycle-{Guid.NewGuid():N}");

    _ = await client.GetSnapshotAsync();
    Assert.Equal(1, acquired);
    Assert.Equal(0, released);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    await SendCrashingActionAsync(client);
    await WaitUntilAsync(() => Volatile.Read(ref released) == 1, TimeSpan.FromSeconds(3));

    _ = await client.GetSnapshotAsync();
    Assert.Equal(2, acquired);
    Assert.Equal(1, released);
    await client.StopAsync();
    Assert.Equal(2, released);
}

static async Task ContentAuthorityCannotOverlapRuntime()
{
    if (!OperatingSystem.IsWindows()) return;
    var executable = Environment.ProcessPath ??
        throw new InvalidOperationException("Test process path is unavailable.");
    var executableDirectory = Path.GetDirectoryName(executable) ??
        throw new InvalidOperationException("Test process directory is unavailable.");
    var released = 0;
    await using var client = CreateClient(
        contentLeaseFactory: _ => new TestContentLease(
            executableDirectory,
            [executableDirectory],
            [executable],
            () => Interlocked.Increment(ref released)),
        contentIsolationKey: $"runtime-content-overlap-{Guid.NewGuid():N}");

    var exception = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
        () => client.GetSnapshotAsync());
    Assert.True(exception.Message.Contains("overlaps", StringComparison.Ordinal),
        "Overlapping content authority lost its stable admission diagnostic.");
    Assert.Equal(0, client.Starts);
    Assert.Equal(1, released);
}

static Task ContentAuthorityTransactionRollsBack()
{
    var targets = new[]
    {
        new AppContainerAuthorityTarget(
            "root", AppContainerAuthorityTargetKind.AuthorityRootDirectory),
        new AppContainerAuthorityTarget("directory", AppContainerAuthorityTargetKind.VerifiedDirectory),
        new AppContainerAuthorityTarget("file", AppContainerAuthorityTargetKind.VerifiedFile),
    };
    var operations = new TestAuthorityOperations(failApplyAt: 2);
    var failure = Assert.Throws<IOException>(
        () => AppContainerAuthorityTransaction.Apply(targets, operations));
    Assert.True(failure.Message.Contains("apply 2", StringComparison.Ordinal),
        "Transaction did not preserve the original apply failure.");
    Assert.SequenceEqual(targets.Reverse(), operations.RestoreOrder);
    foreach (var target in targets)
        Assert.Equal(TestAuthorityOperations.Original(target), operations.StateFor(target));

    var rollbackFailureOperations = new TestAuthorityOperations(
        failApplyAt: 2,
        failRestoreKinds: [AppContainerAuthorityTargetKind.VerifiedDirectory]);
    var rollbackFailure = Assert.Throws<AppContainerAuthorityRollbackException>(
        () => AppContainerAuthorityTransaction.Apply(targets, rollbackFailureOperations));
    Assert.True(rollbackFailure.ApplyFailure is IOException,
        "Rollback failure lost its initiating apply error.");
    Assert.Equal(1, rollbackFailure.RollbackFailures.Count);
    Assert.SequenceEqual(targets.Reverse(), rollbackFailureOperations.RestoreOrder);
    Assert.Equal(
        TestAuthorityOperations.Granted(targets[1]),
        rollbackFailureOperations.StateFor(targets[1]));
    Assert.Equal(
        TestAuthorityOperations.Original(targets[0]),
        rollbackFailureOperations.StateFor(targets[0]));
    return Task.CompletedTask;
}

static Task ContentAuthorityRecoveryHonorsCancellation()
{
    var targets = new[]
    {
        new AppContainerAuthorityTarget(
            "root", AppContainerAuthorityTargetKind.AuthorityRootDirectory),
        new AppContainerAuthorityTarget(
            "directory", AppContainerAuthorityTargetKind.VerifiedDirectory),
        new AppContainerAuthorityTarget(
            "file", AppContainerAuthorityTargetKind.VerifiedFile),
    };
    using var cancellation = new CancellationTokenSource();
    var operations = new TestAuthorityOperations(
        afterRestore: count =>
        {
            if (count == 1) cancellation.Cancel();
        });
    var snapshots = AppContainerAuthorityTransaction.Capture(targets, operations);
    AppContainerAuthorityTransaction.Apply(snapshots, operations);

    Assert.Throws<OperationCanceledException>(() =>
        AppContainerAuthorityTransaction.Recover(
            snapshots, operations, cancellation.Token));
    Assert.SequenceEqual(new[] { targets[^1] }, operations.RestoreOrder);
    Assert.Equal(TestAuthorityOperations.Original(targets[^1]),
        operations.StateFor(targets[^1]));
    Assert.Equal(TestAuthorityOperations.Granted(targets[0]),
        operations.StateFor(targets[0]));
    Assert.Equal(TestAuthorityOperations.Granted(targets[1]),
        operations.StateFor(targets[1]));
    return Task.CompletedTask;
}

static async Task ContentAuthorityRollbackIsRecoverable()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temp = new TemporaryDirectory();
    var verified = Path.Combine(temp.Path, "verified.txt");
    await File.WriteAllTextAsync(verified, "verified");
    var isolationKey = $"runtime-content-recoverable-{Guid.NewGuid():N}";
    var released = 0;
    var journal = new TestAuthorityJournal();
    var operations = new TestAuthorityOperations(failApplyAt: 1);
    await using (var client = CreateClient(
        contentLeaseFactory: _ => new TestContentLease(
            temp.Path,
            [temp.Path],
            [verified],
            () => Interlocked.Increment(ref released)),
        contentIsolationKey: isolationKey,
        contentAuthorityOperations: operations,
        contentAuthorityJournal: journal))
    {
        var exception = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
            () => client.GetSnapshotAsync());
        Assert.Equal("Worker content authority could not be established.", exception.Message);
        Assert.True(exception.InnerException is IOException,
            "Recoverable authority failure lost its host diagnostic cause.");
        Assert.Equal(0, client.Starts);
    }
    Assert.Equal(1, released);
    Assert.Equal(2, operations.ApplyCount);
    Assert.True(journal.Pending is null,
        "Complete rollback retained a pending authority record.");
}

static async Task ContentAuthorityRollbackRecoversGeneration()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temp = new TemporaryDirectory();
    var verified = Path.Combine(temp.Path, "verified.txt");
    await File.WriteAllTextAsync(verified, "verified");
    var isolationKey = $"runtime-content-quarantine-{Guid.NewGuid():N}";
    var released = 0;
    var states = new Dictionary<AppContainerAuthorityTarget, string>();
    var journal = new TestAuthorityJournal();
    var operations = new TestAuthorityOperations(
        failApplyAt: 1,
        failRestoreKinds: [AppContainerAuthorityTargetKind.AuthorityRootDirectory],
        states: states);
    await using (var client = CreateClient(
        contentLeaseFactory: _ => new TestContentLease(
            temp.Path,
            [temp.Path],
            [verified],
            () => Interlocked.Increment(ref released)),
        contentIsolationKey: isolationKey,
        contentAuthorityOperations: operations,
        contentAuthorityJournal: journal))
    {
        var exception = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
            () => client.GetSnapshotAsync());
        Assert.Equal(
            "Worker content authority is quarantined pending host recovery.",
            exception.Message);
        Assert.True(exception.InnerException is AppContainerAuthorityRollbackException,
            "Quarantine admission did not retain the host diagnostic cause.");
        Assert.Equal(0, client.Starts);
    }
    Assert.Equal(1, released);
    Assert.True(journal.Pending is not null,
        "Incomplete rollback did not retain its write-ahead record.");
    Assert.SequenceEqual(
        new[]
        {
            new AppContainerAuthorityTarget(
                verified, AppContainerAuthorityTargetKind.VerifiedFile),
            new AppContainerAuthorityTarget(
                temp.Path, AppContainerAuthorityTargetKind.AuthorityRootDirectory),
        },
        operations.RestoreOrder);

    var secondRelease = 0;
    var recoveryOperations = new TestAuthorityOperations(states: states);
    await using var recoveredClient = CreateClient(
        contentLeaseFactory: _ => new TestContentLease(
            temp.Path,
            [temp.Path],
            [verified],
            () => Interlocked.Increment(ref secondRelease)),
        contentIsolationKey: isolationKey,
        contentAuthorityOperations: recoveryOperations,
        contentAuthorityJournal: journal);
    _ = await recoveredClient.GetSnapshotAsync();
    Assert.Equal(1, recoveredClient.Starts);
    Assert.True(journal.Pending is null,
        "Successful recovery and reapply left a pending authority record.");
    Assert.True(recoveryOperations.RestoreOrder.Count >= 2,
        "A later admission did not recover the pending transaction first.");
    await recoveredClient.StopAsync();
    Assert.Equal(1, secondRelease);
}

static async Task ContentAuthorityJournalFailsBeforeMutation()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temp = new TemporaryDirectory();
    var verified = Path.Combine(temp.Path, "verified.txt");
    await File.WriteAllTextAsync(verified, "verified");
    var released = 0;
    var operations = new TestAuthorityOperations();
    await using var client = CreateClient(
        contentLeaseFactory: _ => new TestContentLease(
            temp.Path,
            [temp.Path],
            [verified],
            () => Interlocked.Increment(ref released)),
        contentIsolationKey: $"runtime-content-journal-failure-{Guid.NewGuid():N}",
        contentAuthorityOperations: operations,
        contentAuthorityJournal: new TestAuthorityJournal(failWrite: true));
    var failure = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
        () => client.GetSnapshotAsync());
    Assert.Equal("Worker content authority journal is unavailable.", failure.Message);
    Assert.Equal(0, operations.ApplyCount);
    Assert.Equal(0, client.Starts);
    Assert.Equal(1, released);
}

static Task ContentAuthorityJournalRejectsUnsafeState()
{
    if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
    using var temp = new TemporaryDirectory();
    var profile = $"WidgetRail.Widget.{Guid.NewGuid():N}";
    var target = Path.Combine(temp.Path, "target.txt");
    File.WriteAllText(target, "target");
    var snapshot = new AppContainerAuthoritySnapshot(
        new AppContainerAuthorityTarget(
            target, AppContainerAuthorityTargetKind.VerifiedFile),
        "D:",
        new AppContainerAuthorityObjectIdentity(
            1, "00000000000000000000000000000001"));
    var journal = new FileAppContainerAuthorityJournal(
        Path.Combine(temp.Path, "journal"), TimeSpan.FromMilliseconds(100));
    using (var lease = journal.Acquire(profile))
    {
        _ = Assert.Throws<AppContainerAuthorityJournalException>(
            () => journal.Acquire(
                $"WidgetRail.Widget.{Guid.NewGuid():N}"));
        lease.WritePending([snapshot]);
    }
    using (var lease = journal.Acquire(
               $"WidgetRail.Widget.{Guid.NewGuid():N}"))
        Assert.True(lease.ReadPending() is null,
            "A disjoint profile observed another profile's pending transaction.");
    using (var lease = journal.Acquire(profile))
        Assert.SequenceEqual(new[] { snapshot }, lease.ReadPending()!.Snapshots);

    var pendingPath = Path.Combine(journal.RootPath, "pending", profile + ".json");
    var validDocument = File.ReadAllText(pendingPath);
    File.WriteAllText(
        pendingPath,
        validDocument[..^1] + ",\"Unexpected\":true}");
    using (var lease = journal.Acquire(profile))
        _ = Assert.Throws<AppContainerAuthorityJournalException>(
            () => lease.ReadPending());

    File.WriteAllText(pendingPath, "{");
    using (var lease = journal.Acquire(profile))
        _ = Assert.Throws<AppContainerAuthorityJournalException>(
            () => lease.ReadPending());

    File.WriteAllText(
        pendingPath,
        "{\"Version\":3,\"ProfileName\":\"invalid\",\"Snapshots\":[]}");
    using (var lease = journal.Acquire(profile))
        _ = Assert.Throws<AppContainerAuthorityJournalException>(
            () => lease.ReadPending());

    File.Delete(pendingPath);
    Directory.CreateDirectory(pendingPath);
    _ = Assert.Throws<AppContainerAuthorityJournalException>(
        () => journal.Acquire(profile));

    var reparseTarget = Path.Combine(temp.Path, "reparse-target");
    var reparseRoot = Path.Combine(temp.Path, "reparse-root");
    Directory.CreateDirectory(reparseTarget);
    try
    {
        Directory.CreateSymbolicLink(reparseRoot, reparseTarget);
        var reparseJournal = new FileAppContainerAuthorityJournal(reparseRoot);
        _ = Assert.Throws<AppContainerAuthorityJournalException>(
            () => reparseJournal.Acquire(profile));
        Directory.Delete(reparseRoot);
    }
    catch (UnauthorizedAccessException)
    {
        // Windows developer-mode policy can prohibit creating the adversarial fixture.
    }

    var oversizedJournal = new FileAppContainerAuthorityJournal(
        Path.Combine(temp.Path, "oversized-journal"));
    var oversizedDescriptor = new string('A', 65_536);
    var oversized = Enumerable.Range(0, 43)
        .Select(index => new AppContainerAuthoritySnapshot(
            new AppContainerAuthorityTarget(
                Path.Combine(temp.Path, $"oversized-{index}.txt"),
                AppContainerAuthorityTargetKind.VerifiedFile),
            oversizedDescriptor,
            new AppContainerAuthorityObjectIdentity(
                1, $"{index + 1:X32}")))
        .ToArray();
    using (var lease = oversizedJournal.Acquire(profile))
        _ = Assert.Throws<AppContainerAuthorityJournalException>(
            () => lease.WritePending(oversized));
    return Task.CompletedTask;
}

static async Task ProfileQuarantinePermitsDisjointWorker()
{
    if (!OperatingSystem.IsWindows()) return;
    using var first = new TemporaryDirectory();
    using var second = new TemporaryDirectory();
    using var controlPlane = new TemporaryDirectory();
    var firstFile = Path.Combine(first.Path, "verified.txt");
    var secondFile = Path.Combine(second.Path, "verified.txt");
    await File.WriteAllTextAsync(firstFile, "first");
    await File.WriteAllTextAsync(secondFile, "second");
    var journal = new FileAppContainerAuthorityJournal(
        Path.Combine(controlPlane.Path, "journal"));
    var firstProfile = WindowsAppContainer.ProfileNameFor(
        $"runtime-profile-first-{Guid.NewGuid():N}");
    var firstSnapshots = TestFileObjectIdentity.Targets(
            first.Path, [first.Path], [firstFile])
        .Select(expected => new AppContainerAuthoritySnapshot(
            expected.Target, "D:", expected.ObjectIdentity))
        .ToArray();
    using (var lease = journal.Acquire(firstProfile))
        lease.WritePending(firstSnapshots);

    var secondKey = $"runtime-profile-second-{Guid.NewGuid():N}";
    using var secondContainer = WindowsAppContainer.OpenOrCreate(secondKey);
    var operations = new TestAuthorityOperations();
    secondContainer.ReplaceReadAndExecuteGrant(
        TestFileObjectIdentity.Targets(
            second.Path, [second.Path], [secondFile]),
        operations,
        journal);
    Assert.Equal(2, operations.ApplyCount);
    var pending = journal.ListPending();
    Assert.Equal(1, pending.Count);
    Assert.Equal(firstProfile, pending[0].ProfileName);
}

static async Task ProfileQuarantineRejectsOverlap()
{
    if (!OperatingSystem.IsWindows()) return;
    using var package = new TemporaryDirectory();
    using var controlPlane = new TemporaryDirectory();
    var verified = Path.Combine(package.Path, "verified.txt");
    await File.WriteAllTextAsync(verified, "verified");
    var journal = new FileAppContainerAuthorityJournal(
        Path.Combine(controlPlane.Path, "journal"));
    var targets = TestFileObjectIdentity.Targets(
        package.Path, [package.Path], [verified]);
    var firstProfile = WindowsAppContainer.ProfileNameFor(
        $"runtime-overlap-first-{Guid.NewGuid():N}");
    using (var lease = journal.Acquire(firstProfile))
        lease.WritePending(targets.Select(expected => new AppContainerAuthoritySnapshot(
            expected.Target, "D:", expected.ObjectIdentity)).ToArray());

    using var secondContainer = WindowsAppContainer.OpenOrCreate(
        $"runtime-overlap-second-{Guid.NewGuid():N}");
    var operations = new TestAuthorityOperations();
    var failure = Assert.Throws<WidgetProcessAdmissionException>(() =>
        secondContainer.ReplaceReadAndExecuteGrant(targets, operations, journal));
    Assert.Equal("Worker content authority journal is unavailable.", failure.Message);
    Assert.Equal(0, operations.ApplyCount);
    Assert.Equal(1, journal.ListPending().Count);
}

static async Task AuthorityRecoveryIsExact()
{
    if (!OperatingSystem.IsWindows()) return;
    using var package = new TemporaryDirectory();
    using var controlPlane = new TemporaryDirectory();
    var verified = Path.Combine(package.Path, "verified.txt");
    await File.WriteAllTextAsync(verified, "verified");
    var isolationKey = $"runtime-repair-{Guid.NewGuid():N}";
    var profile = WindowsAppContainer.ProfileNameFor(isolationKey);
    var journal = new FileAppContainerAuthorityJournal(
        Path.Combine(controlPlane.Path, "journal"));
    using var container = WindowsAppContainer.OpenOrCreate(isolationKey);
    var targets = TestFileObjectIdentity.Targets(
            package.Path, [package.Path], [verified])
        .Select(expected => expected.Target)
        .ToArray();
    IReadOnlyList<AppContainerAuthoritySnapshot> originals;
    using (var operations = container.CreateAuthorityOperationsForTesting())
    {
        originals = AppContainerAuthorityTransaction.Capture(targets, operations);
        using (var lease = journal.Acquire(profile)) lease.WritePending(originals);
        AppContainerAuthorityTransaction.Apply(originals, operations);
    }

    var service = new AppContainerAuthorityRecoveryService(journal);
    var candidate = service.ListPending().Single();
    Assert.Equal(profile, candidate.ProfileName);
    Assert.Equal(originals.Count, candidate.TargetCount);
    Assert.True(!candidate.IsLegacy, "New recovery state was mislabeled as legacy.");
    Assert.True(!candidate.ToString().Contains(package.Path, StringComparison.OrdinalIgnoreCase),
        "Sanitized recovery metadata leaked an authority path.");
    using (var cancellation = new CancellationTokenSource())
    {
        var gate = new AppContainerAuthorityRecoveryCommitGate(
            cancellation.Token, cancellation.Cancel);
        Assert.Throws<OperationCanceledException>(() => service.Retry(
            candidate.ConfirmationToken, cancellation.Token, gate));
    }
    var retained = service.ListPending().Single();
    Assert.Equal(candidate.ConfirmationToken, retained.ConfirmationToken);
    Assert.Equal(candidate.ProfileName, retained.ProfileName);
    service.Retry(candidate.ConfirmationToken);
    Assert.Equal(0, service.ListPending().Count);
    using (var operations = container.CreateAuthorityOperationsForTesting())
    {
        foreach (var original in originals)
            Assert.Equal(
                original.AccessDescriptor,
                operations.Capture(original.Target).AccessDescriptor);
    }
    var stale = Assert.Throws<AppContainerAuthorityRecoveryException>(() =>
        service.Retry(candidate.ConfirmationToken));
    Assert.Equal("stale_confirmation", stale.Code);
}

static async Task LegacyAuthorityRecoveryUsesOwner()
{
    if (!OperatingSystem.IsWindows()) return;
    using var first = new TemporaryDirectory();
    using var second = new TemporaryDirectory();
    using var controlPlane = new TemporaryDirectory();
    var firstFile = Path.Combine(first.Path, "verified.txt");
    var secondFile = Path.Combine(second.Path, "verified.txt");
    await File.WriteAllTextAsync(firstFile, "first");
    await File.WriteAllTextAsync(secondFile, "second");
    var firstKey = $"runtime-legacy-first-{Guid.NewGuid():N}";
    var firstProfile = WindowsAppContainer.ProfileNameFor(firstKey);
    var journal = new FileAppContainerAuthorityJournal(
        Path.Combine(controlPlane.Path, "journal"));
    using var firstContainer = WindowsAppContainer.OpenOrCreate(firstKey);
    var firstTargets = TestFileObjectIdentity.Targets(
            first.Path, [first.Path], [firstFile])
        .Select(expected => expected.Target)
        .ToArray();
    IReadOnlyList<AppContainerAuthoritySnapshot> originals;
    string transactionId;
    using (var operations = firstContainer.CreateAuthorityOperationsForTesting())
    {
        originals = AppContainerAuthorityTransaction.Capture(firstTargets, operations);
        using (var lease = journal.Acquire(firstProfile))
        {
            lease.WritePending(originals);
            transactionId = lease.ReadPending()!.ConfirmationToken;
        }
        AppContainerAuthorityTransaction.Apply(originals, operations);
    }
    var profilePath = Path.Combine(
        journal.RootPath, "pending", firstProfile + ".json");
    var legacyPath = Path.Combine(journal.RootPath, ".authority.pending.json");
    var legacyDocument = File.ReadAllText(profilePath)
        .Replace("\"Version\":3", "\"Version\":2", StringComparison.Ordinal)
        .Replace(
            $",\"TransactionId\":\"{transactionId}\"",
            string.Empty,
            StringComparison.Ordinal);
    File.WriteAllText(legacyPath, legacyDocument);
    File.Delete(profilePath);

    using var secondContainer = WindowsAppContainer.OpenOrCreate(
        $"runtime-legacy-second-{Guid.NewGuid():N}");
    secondContainer.ReplaceReadAndExecuteGrant(
        TestFileObjectIdentity.Targets(
            second.Path, [second.Path], [secondFile]),
        journal: journal);
    Assert.Equal(0, journal.ListPending().Count);
    using var verification = firstContainer.CreateAuthorityOperationsForTesting();
    foreach (var original in originals)
        Assert.Equal(
            original.AccessDescriptor,
            verification.Capture(original.Target).AccessDescriptor);
}

static async Task AuthorityJournalRecoversAfterHostTermination()
{
    if (!OperatingSystem.IsWindows()) return;
    using var package = new TemporaryDirectory();
    using var controlPlane = new TemporaryDirectory();
    var verified = Path.Combine(package.Path, "verified.txt");
    await File.WriteAllTextAsync(verified, "verified");
    var isolationKey = $"runtime-content-crash-{Guid.NewGuid():N}";
    var profile = WindowsAppContainer.ProfileNameFor(isolationKey);
    var journalRoot = Path.Combine(controlPlane.Path, "journal");
    var journal = new FileAppContainerAuthorityJournal(journalRoot);
    using (journal.Acquire(profile)) { }

    var targets = new[]
    {
        new AppContainerAuthorityTarget(
            package.Path, AppContainerAuthorityTargetKind.AuthorityRootDirectory),
        new AppContainerAuthorityTarget(
            verified, AppContainerAuthorityTargetKind.VerifiedFile),
    };
    using var container = WindowsAppContainer.OpenOrCreate(isolationKey);
    IReadOnlyList<AppContainerAuthoritySnapshot> originals;
    using (var captureOperations = container.CreateAuthorityOperationsForTesting())
        originals = AppContainerAuthorityTransaction.Capture(targets, captureOperations);

    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("Test process path is unavailable.");
    var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = AppContext.BaseDirectory,
    };
    foreach (var argument in new[]
    {
        "--authority-crash-probe",
        "--authority-isolation-key", isolationKey,
        "--authority-journal-root", journalRoot,
        "--authority-package-root", package.Path,
        "--authority-verified-file", verified,
    }) startInfo.ArgumentList.Add(argument);

    using var child = System.Diagnostics.Process.Start(startInfo)
        ?? throw new InvalidOperationException("Authority crash probe did not start.");
    try
    {
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
    }
    catch
    {
        if (!child.HasExited) child.Kill(entireProcessTree: true);
        throw;
    }
    Assert.Equal(91, child.ExitCode);

    using var recoveryOperations = container.CreateAuthorityOperationsForTesting();
    using (var lease = journal.Acquire(profile))
    {
        var pending = lease.ReadPending();
        Assert.True(pending is not null,
            "Process termination left no durable pending authority record.");
        AppContainerAuthorityTransaction.Recover(pending!.Snapshots, recoveryOperations);
        lease.ClearPending();
    }
    foreach (var original in originals)
        Assert.Equal(
            original.AccessDescriptor,
            recoveryOperations.Capture(original.Target).AccessDescriptor);
    using (var lease = journal.Acquire(profile))
        Assert.True(lease.ReadPending() is null,
            "Verified crash recovery did not clear the pending record.");
}

static void RunAuthorityCrashProbe(string[] arguments)
{
    var isolationKey = RequiredValue(arguments, "--authority-isolation-key");
    var journalRoot = RequiredValue(arguments, "--authority-journal-root");
    var packageRoot = RequiredValue(arguments, "--authority-package-root");
    var verifiedFile = RequiredValue(arguments, "--authority-verified-file");
    using var container = WindowsAppContainer.OpenOrCreate(isolationKey);
    using var operations = new TerminatingAuthorityOperations(
        container.CreateAuthorityOperationsForTesting(), terminateAfterApply: 2);
    container.ReplaceReadAndExecuteGrant(
        TestFileObjectIdentity.Targets(
            packageRoot, [packageRoot], [verifiedFile]),
        operations,
        new FileAppContainerAuthorityJournal(journalRoot));
    throw new InvalidOperationException(
        "Authority crash probe completed without terminating.");
}

static Task AuthorityHandlesResistPathReplacement()
{
    if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
    using var temp = new TemporaryDirectory();
    var originalPath = Path.Combine(temp.Path, "verified.txt");
    var movedPath = Path.Combine(temp.Path, "moved.txt");
    File.WriteAllText(originalPath, "original");
    var isolationKey = $"runtime-content-handle-{Guid.NewGuid():N}";
    using var container = WindowsAppContainer.OpenOrCreate(isolationKey);
    using var operations = container.CreateAuthorityOperationsForTesting();
    var target = new AppContainerAuthorityTarget(
        originalPath, AppContainerAuthorityTargetKind.VerifiedFile);
    var snapshot = operations.Capture(target);

    File.Move(originalPath, movedPath);
    File.WriteAllText(originalPath, "replacement");
    using var replacementOperations = container.CreateAuthorityOperationsForTesting();
    var replacementBefore = replacementOperations.Capture(target);

    operations.Apply(snapshot);
    operations.VerifyApplied(snapshot);
    var boundApplied = operations.Capture(target);
    Assert.Equal(snapshot.ObjectIdentity, boundApplied.ObjectIdentity);
    Assert.True(
        !string.Equals(
            snapshot.AccessDescriptor,
            boundApplied.AccessDescriptor,
            StringComparison.Ordinal),
        "Handle-bound apply did not change the originally captured object.");
    var replacementAfter = replacementOperations.Capture(target);
    Assert.Equal(replacementBefore.ObjectIdentity, replacementAfter.ObjectIdentity);
    Assert.Equal(replacementBefore.AccessDescriptor, replacementAfter.AccessDescriptor);

    operations.Restore(snapshot);
    operations.VerifyRestored(snapshot);
    return Task.CompletedTask;
}

static Task AuthorityRecoveryRejectsIdentityChange()
{
    if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
    using var package = new TemporaryDirectory();
    using var controlPlane = new TemporaryDirectory();
    var originalPath = Path.Combine(package.Path, "verified.txt");
    var movedPath = Path.Combine(package.Path, "moved.txt");
    File.WriteAllText(originalPath, "original");
    var isolationKey = $"runtime-content-recovery-identity-{Guid.NewGuid():N}";
    using var container = WindowsAppContainer.OpenOrCreate(isolationKey);
    var target = new AppContainerAuthorityTarget(
        originalPath, AppContainerAuthorityTargetKind.VerifiedFile);
    AppContainerAuthoritySnapshot snapshot;
    using (var operations = container.CreateAuthorityOperationsForTesting())
        snapshot = operations.Capture(target);

    var profile = WindowsAppContainer.ProfileNameFor(isolationKey);
    var journal = new FileAppContainerAuthorityJournal(
        Path.Combine(controlPlane.Path, "journal"));
    using (var lease = journal.Acquire(profile)) lease.WritePending([snapshot]);
    File.Move(originalPath, movedPath);
    File.WriteAllText(originalPath, "replacement");
    using var replacementOperations = container.CreateAuthorityOperationsForTesting();
    var replacementBefore = replacementOperations.Capture(target);

    var failure = Assert.Throws<WidgetProcessAdmissionException>(() =>
        container.ReplaceReadAndExecuteGrant(
            TestFileObjectIdentity.Targets(
                package.Path, [package.Path], [originalPath]),
            journal: journal));
    Assert.Equal(
        "Worker content authority is quarantined pending host recovery.",
        failure.Message);
    Assert.True(failure.InnerException is AggregateException,
        "Identity mismatch did not remain a recovery failure.");
    var replacementAfter = replacementOperations.Capture(target);
    Assert.Equal(replacementBefore.ObjectIdentity, replacementAfter.ObjectIdentity);
    Assert.Equal(replacementBefore.AccessDescriptor, replacementAfter.AccessDescriptor);
    using (var lease = journal.Acquire(profile))
    {
        Assert.True(lease.ReadPending() is not null,
            "Identity mismatch cleared the pending recovery record.");
        lease.ClearPending();
    }
    return Task.CompletedTask;
}

static async Task AlternateAppContainerAuthorityFailsClosed()
{
    if (!OperatingSystem.IsWindows()) return;
    using var package = new TemporaryDirectory();
    using var controlPlane = new TemporaryDirectory();
    var verified = Path.Combine(package.Path, "verified.txt");
    await File.WriteAllTextAsync(verified, "verified");
    var directory = new DirectoryInfo(package.Path);
    var original = directory.GetAccessControl(AccessControlSections.Access)
        .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
    using var alternate = WindowsAppContainer.OpenOrCreate(
        $"runtime-content-alternate-owner-{Guid.NewGuid():N}");
    try
    {
        foreach (var sid in new[] { "S-1-15-2-1", "S-1-15-2-2", alternate.Sid })
        {
            var modified = new DirectorySecurity();
            modified.SetSecurityDescriptorSddlForm(original, AccessControlSections.Access);
            modified.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid),
                FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
            directory.SetAccessControl(modified);
            var released = 0;
            var journal = new FileAppContainerAuthorityJournal(
                Path.Combine(controlPlane.Path, Guid.NewGuid().ToString("N")));
            await using var client = CreateClient(
                contentLeaseFactory: _ => new TestContentLease(
                    package.Path,
                    [package.Path],
                    [verified],
                    () => Interlocked.Increment(ref released)),
                contentIsolationKey:
                    $"runtime-content-alternate-authority-{Guid.NewGuid():N}",
                contentAuthorityJournal: journal);
            var failure = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
                () => client.GetSnapshotAsync());
            Assert.Equal("Worker content authority could not be established.", failure.Message);
            Assert.True(failure.InnerException is IOException,
                "Alternate AppContainer authority lost its host diagnostic cause.");
            Assert.Equal(0, client.Starts);
            Assert.Equal(1, released);
            using var lease = journal.Acquire(
                $"WidgetRail.Widget.{Guid.NewGuid():N}");
            Assert.True(lease.ReadPending() is null,
                "Alternate authority failure wrote a pending mutation record.");
        }
    }
    finally
    {
        var restore = new DirectorySecurity();
        restore.SetSecurityDescriptorSddlForm(original, AccessControlSections.Access);
        directory.SetAccessControl(restore);
    }
}

static async Task ContentLeaseRequiresObjectIdentity()
{
    if (!OperatingSystem.IsWindows()) return;
    using var package = new TemporaryDirectory();
    var verified = Path.Combine(package.Path, "verified.txt");
    await File.WriteAllTextAsync(verified, "verified");
    var valid = TestFileObjectIdentity.Targets(
        package.Path, [package.Path], [verified]).ToArray();
    var malformed = valid.ToArray();
    malformed[^1] = malformed[^1] with
    {
        ObjectIdentity = new AppContainerAuthorityObjectIdentity(
            0, "00000000000000000000000000000000"),
    };
    var cases = new[]
    {
        (Name: "missing", Targets: Array.Empty<AppContainerAuthorityExpectedTarget>(),
            Message: "Worker content admission returned invalid authority bounds."),
        (Name: "malformed", Targets: malformed,
            Message: "Worker content admission returned invalid object identity evidence."),
        (Name: "duplicate", Targets: valid.Append(valid[0]).ToArray(),
            Message: "Worker content admission returned conflicting object identity evidence."),
    };
    foreach (var item in cases)
    {
        var released = 0;
        var residencyReleased = 0;
        var operations = new TestAuthorityOperations();
        var journal = new TestAuthorityJournal();
        await using var client = CreateClient(
            processLeaseFactory: () => new CallbackDisposable(
                () => Interlocked.Increment(ref residencyReleased)),
            contentLeaseFactory: _ => new TestContentLease(
                package.Path,
                [package.Path],
                [verified],
                () => Interlocked.Increment(ref released),
                item.Targets),
            contentIsolationKey:
                $"runtime-content-identity-{item.Name}-{Guid.NewGuid():N}",
            contentAuthorityOperations: operations,
            contentAuthorityJournal: journal);

        var failure = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
            () => client.GetSnapshotAsync());
        Assert.Equal(item.Message, failure.Message);
        Assert.Equal(0, client.Starts);
        Assert.Equal(1, released);
        Assert.Equal(1, residencyReleased);
        Assert.Equal(0, operations.CaptureCount);
        Assert.True(journal.Pending is null,
            $"Invalid {item.Name} identity evidence reached journal publication.");
    }
}

static async Task ContentLeaseIdentityMismatchFailsClosed()
{
    if (!OperatingSystem.IsWindows()) return;
    using var package = new TemporaryDirectory();
    using var controlPlane = new TemporaryDirectory();
    var originalDirectory = Path.Combine(package.Path, "assets");
    var movedDirectory = Path.Combine(package.Path, "moved-assets");
    Directory.CreateDirectory(originalDirectory);
    var originalPath = Path.Combine(originalDirectory, "verified.txt");
    await File.WriteAllTextAsync(originalPath, "byte-identical");
    var expectedIdentities = TestFileObjectIdentity.Capture(
        [package.Path, originalDirectory, originalPath]);
    Directory.Move(originalDirectory, movedDirectory);
    Directory.CreateDirectory(originalDirectory);
    await File.WriteAllTextAsync(originalPath, "byte-identical");
    var replacement = new FileInfo(originalPath);
    var replacementDescriptor = replacement.GetAccessControl(AccessControlSections.Access)
        .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
    var released = 0;
    var journal = new FileAppContainerAuthorityJournal(
        Path.Combine(controlPlane.Path, "journal"));
    await using var client = CreateClient(
        contentLeaseFactory: _ => new TestContentLease(
            package.Path,
            [package.Path, originalDirectory],
            [originalPath],
            () => Interlocked.Increment(ref released),
            TestFileObjectIdentity.Targets(
                package.Path,
                [package.Path, originalDirectory],
                [originalPath],
                expectedIdentities)),
        contentIsolationKey: $"runtime-content-identity-mismatch-{Guid.NewGuid():N}",
        contentAuthorityJournal: journal);

    var failure = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(
        () => client.GetSnapshotAsync());
    Assert.Equal("Worker content authority could not be established.", failure.Message);
    Assert.True(failure.InnerException is IOException,
        "Catalog identity mismatch lost its host diagnostic cause.");
    Assert.Equal(0, client.Starts);
    Assert.Equal(1, released);
    Assert.Equal(
        replacementDescriptor,
        replacement.GetAccessControl(AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Access));
    using var lease = journal.Acquire(
        $"WidgetRail.Widget.{Guid.NewGuid():N}");
    Assert.True(lease.ReadPending() is null,
        "Catalog identity mismatch published a pending authority record.");
}

static async Task ContentAdmissionHonorsCallerCancellation()
{
    var residencyReleased = 0;
    await using var client = CreateClient(
        processLeaseFactory: () => new CallbackDisposable(
            () => Interlocked.Increment(ref residencyReleased)),
        contentLeaseFactory: cancellationToken =>
        {
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unreachable content-admission branch.");
        },
        contentIsolationKey: $"runtime-content-cancel-{Guid.NewGuid():N}");
    var failures = 0;
    client.Failed += (_, _) => failures++;
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

    _ = await Assert.ThrowsAsync<OperationCanceledException>(
        () => client.GetSnapshotAsync(cancellation.Token));
    Assert.Equal(1, residencyReleased);
    Assert.Equal(0, client.Starts);
    Assert.Equal(0, failures);
}

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!condition())
    {
        if (DateTime.UtcNow >= deadline)
            throw new TimeoutException("Condition was not reached before the timeout.");
        await Task.Delay(25);
    }
}

static async Task SendCrashingActionAsync(WidgetProcessClient client)
{
    try
    {
        await client.SendActionAsync(new WidgetActionEvent("crash", "button"));
    }
    catch (WidgetProcessException)
    {
        // The deliberate Environment.Exit probe may win the race with its
        // action-admission acknowledgement. The worker failure is asserted by
        // each caller independently.
    }
}

static async Task PrivateMemoryExceedsPrototypeCeiling()
{
    if (!OperatingSystem.IsWindows()) return;
    await using var client = CreateClient(extraArguments: ["--private-memory-probe-mb", "272"]);
    var snapshot = await client.GetSnapshotAsync();
    Assert.Equal("private-memory:272", Find(snapshot.Root, "private-memory").Text);
    Assert.Equal(null, client.AppliedJobMemoryLimitBytes);
}

static async Task WindowsJobPreservesContainment()
{
    if (!OperatingSystem.IsWindows()) return;
    await using var client = CreateClient();
    _ = await client.GetSnapshotAsync();
    Assert.Equal(null, client.AppliedJobMemoryLimitBytes);
    Assert.Equal(null, client.AppliedJobActiveProcessLimit);
    Assert.True(client.AppliedJobAccounting?.ActiveProcesses >= 1,
        "The running worker was absent from Job accounting.");
}

static async Task WindowsJobCleansUpProcess()
{
    if (!OperatingSystem.IsWindows()) return;
    var startInfo = SleeperStartInfo();
    var job = WindowsWorkerJob.Create();
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

static async Task WindowsJobOwnsProcessTree()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temporary = new TemporaryDirectory();
    var childPidFile = Path.Combine(temporary.Path, "child.pid");
    var parentStart = SleeperStartInfo();
    parentStart.ArgumentList.Clear();
    parentStart.ArgumentList.Add("--containment-parent");
    parentStart.ArgumentList.Add("--child-pid-file");
    parentStart.ArgumentList.Add(childPidFile);
    var job = WindowsWorkerJob.Create();
    using var parent = job.StartProcess(parentStart);
    Process? child = null;
    try
    {
        await WaitUntilAsync(() => File.Exists(childPidFile), TimeSpan.FromSeconds(3));
        var childPid = int.Parse(await File.ReadAllTextAsync(childPidFile), CultureInfo.InvariantCulture);
        child = Process.GetProcessById(childPid);
        await WaitUntilAsync(
            () => job.Accounting.ActiveProcesses >= 2,
            TimeSpan.FromSeconds(3));
        Assert.True(!parent.HasExited && !child.HasExited,
            "The owned helper tree exited before containment was observed.");
        Assert.Equal(null, job.MemoryLimitBytes);
        Assert.Equal(null, job.ActiveProcessLimit);
        job.Dispose();
        await Task.WhenAll(
            parent.WaitForExitAsync(),
            child.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(parent.HasExited && child.HasExited,
            "Closing the Job Object left an owned process-tree member alive.");
    }
    finally
    {
        job.Dispose();
        if (!parent.HasExited)
        {
            parent.Kill(entireProcessTree: true);
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        child?.Dispose();
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
    var authorityJournal = FileAppContainerAuthorityJournal.Default;
    var journalProbeProfile = $"WidgetRail.Widget.{Guid.NewGuid():N}";
    using (authorityJournal.Acquire(journalProbeProfile)) { }
    var privateUserFile = Path.Combine(
        authorityJournal.RootPath, $".isolation-probe-{Guid.NewGuid():N}.txt");
    await File.WriteAllTextAsync(readableA, "package-a");
    await File.WriteAllTextAsync(readableB, "package-b");
    await File.WriteAllTextAsync(privateUserFile, "host-private");

    const string secretName = "WRAIL_ISOLATION_TEST_SECRET";
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
        if (File.Exists(privateUserFile)) File.Delete(privateUserFile);
    }
}

static async Task ExactContentAuthority()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temp = new TemporaryDirectory();
    var verified = Path.Combine(temp.Path, "verified.txt");
    var late = Path.Combine(temp.Path, "late.txt");
    await File.WriteAllTextAsync(verified, "verified");
    await File.WriteAllTextAsync(late, "must-not-be-readable");
    var isolationKey = $"runtime-exact-content-{Guid.NewGuid():N}";
    using (var priorProfile = WindowsAppContainer.OpenOrCreate(isolationKey))
        priorProfile.GrantReadAndExecute([temp.Path]);
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var released = 0;
    try
    {
        await using var client = CreateIsolatedClient(
            isolationKey,
            temp.Path,
            verified,
            late,
            null,
            ((IPEndPoint)listener.LocalEndpoint).Port,
            "WRAIL_EXACT_CONTENT_UNUSED",
            _ => new TestContentLease(
                temp.Path,
                [temp.Path],
                [verified],
                () => Interlocked.Increment(ref released)));
        var snapshot = await client.GetSnapshotAsync();
        Assert.Equal("verified", Find(snapshot.Root, "probe-readable").Text);
        Assert.Equal("denied", Find(snapshot.Root, "probe-denied-read").Text);
        Assert.Equal("denied", Find(snapshot.Root, "probe-package-write").Text);
        await client.StopAsync();
        Assert.Equal(1, released);
    }
    finally
    {
        listener.Stop();
    }
}

static async Task MaximumExactContentGrantIsBounded()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temp = new TemporaryDirectory();
    var files = new string[512];
    for (var index = 0; index < files.Length; index++)
    {
        files[index] = Path.Combine(temp.Path, $"entry-{index:000}.txt");
        await File.WriteAllTextAsync(files[index], "x");
    }
    await using var client = CreateClient(
        contentLeaseFactory: _ => new TestContentLease(
            temp.Path,
            [temp.Path],
            files,
            () => { }),
        contentIsolationKey: $"runtime-content-maximum-{Guid.NewGuid():N}");

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    _ = await client.GetSnapshotAsync();
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
        $"Maximum exact content grant exceeded ten seconds ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");
    Console.WriteLine(
        $"METRIC appcontainer_exact_grant files=512 milliseconds={stopwatch.Elapsed.TotalMilliseconds:F3}");
}

static async Task StaleContentRootIsIsolated()
{
    if (!OperatingSystem.IsWindows()) return;
    using var current = new TemporaryDirectory();
    using var stale = new TemporaryDirectory();
    var verified = Path.Combine(current.Path, "verified.txt");
    var staleFile = Path.Combine(stale.Path, "stale.txt");
    await File.WriteAllTextAsync(verified, "verified");
    await File.WriteAllTextAsync(staleFile, "must-not-be-readable");
    var staleIsolationKey = $"runtime-stale-content-{Guid.NewGuid():N}";
    var currentIsolationKey = $"runtime-current-content-{Guid.NewGuid():N}";
    using (var staleProfile = WindowsAppContainer.OpenOrCreate(staleIsolationKey))
        staleProfile.GrantReadAndExecute([stale.Path]);

    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        await using var client = CreateIsolatedClient(
            currentIsolationKey,
            current.Path,
            verified,
            staleFile,
            null,
            ((IPEndPoint)listener.LocalEndpoint).Port,
            "WRAIL_STALE_CONTENT_UNUSED",
            _ => new TestContentLease(
                current.Path,
                [current.Path],
                [verified],
                () => { }));
        var snapshot = await client.GetSnapshotAsync();
        Assert.Equal("verified", Find(snapshot.Root, "probe-readable").Text);
        Assert.Equal("denied", Find(snapshot.Root, "probe-denied-read").Text);
    }
    finally
    {
        listener.Stop();
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
    Assert.Equal("denied", Find(snapshot.Root, "probe-denied-write").Text);
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
    string secretName,
    Func<CancellationToken, IWidgetProcessContentLease>? contentLeaseFactory = null)
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
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = isolationKey,
        ReadOnlyPaths = contentLeaseFactory is null ? [packageRoot] : [],
        ContentLeaseFactory = contentLeaseFactory,
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

static async Task NegotiatedPresentationUpdates()
{
    await using var client = CreateClient();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var generation = new string('A', 32);

    var initial = await client.GetPresentationAsync(
        PresentationUpdateCapabilities.Current,
        generation,
        baseSequence: 0,
        requireCheckpoint: false);
    Assert.True(initial.Update is null,
        "A missing runtime base must negotiate a complete checkpoint.");
    Assert.Equal("none", Find(initial.Snapshot.Root, "scoped-action").Text);

    var invalidated = new TaskCompletionSource<long>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    client.Invalidated += (_, revision) => invalidated.TrySetResult(revision);
    await client.SendActionAsync(new WidgetActionEvent("nested", "nested-command"));
    _ = await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var changed = await client.GetPresentationAsync(
        PresentationUpdateCapabilities.Current,
        generation,
        initial.Snapshot.Sequence,
        requireCheckpoint: false);
    Assert.True(changed.Update is not null,
        "An exact negotiated base did not receive an atomic update batch.");
    Assert.Equal(initial.Snapshot.Sequence, changed.Update!.BaseSequence);
    Assert.Equal(changed.Snapshot.Sequence, changed.Update.Sequence);
    Assert.Equal(generation, changed.Update.PresentationGeneration);
    Assert.Equal("nested", Find(changed.Snapshot.Root, "scoped-action").Text);

    var wrongBase = await client.GetPresentationAsync(
        PresentationUpdateCapabilities.Current,
        generation,
        baseSequence: initial.Snapshot.Sequence,
        requireCheckpoint: false);
    Assert.True(wrongBase.Update is null,
        "A stale negotiated base must fall back to a complete checkpoint.");

    var legacy = await client.GetPresentationAsync(
        PresentationUpdateCapabilities.None,
        generation,
        changed.Snapshot.Sequence,
        requireCheckpoint: false);
    Assert.True(legacy.Update is null,
        "A consumer without update capability received update traffic.");
}

static Task LegacyActionAdmissionCompatibility()
{
    Assert.Equal(WidgetOperationAdmission.Enqueued,
        WidgetProcessClient.ParseActionAdmission(RuntimeJson.ToElement(new { })));
    Assert.Throws<WidgetProtocolViolationException>(() =>
        WidgetProcessClient.ParseActionAdmission(RuntimeJson.ToElement(
            new ActionAdmissionPayload(WidgetOperationAdmission.Completed))));
    Assert.Throws<WidgetProtocolViolationException>(() =>
        WidgetProcessClient.ParseActionAdmission(RuntimeJson.ToElement(new { unexpected = true })));
    return Task.CompletedTask;
}

static async Task CommittedTextCrossesWorkerActionQueue()
{
    await using var client = CreateClient();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    var invalidated = new TaskCompletionSource<long>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    client.Invalidated += (_, revision) => invalidated.TrySetResult(revision);

    Assert.Equal(WidgetOperationAdmission.Enqueued,
        await client.AdmitActionAsync(new WidgetActionEvent(
            "committed-text", "button")
        {
            CommittedText = "Controller Proof",
        }));
    _ = await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("committed:16", Find(
        (await client.GetSnapshotAsync()).Root, "committed-text-status").Text);
}

static async Task ActionsInvalidate()
{
    await using var client = CreateClient();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var invalidated = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Invalidated += (_, revision) => invalidated.TrySetResult(revision);
    await client.SendActionAsync(new WidgetActionEvent("invalidate", "button"));
    Assert.Equal(1L, await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(2)));
}

static async Task ProcessClientAdmitsExactlyOneCurrentInvalidation()
{
    await using var client = CreateClient(
        extraArguments: ["--notification-cursor-probe"]);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    _ = await client.GetSnapshotAsync();

    var delivered = new TaskCompletionSource<long>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var publications = 0;
    client.Invalidated += (_, revision) =>
    {
        Interlocked.Increment(ref publications);
        delivered.TrySetResult(revision);
    };

    Assert.Equal(WidgetOperationAdmission.Enqueued,
        await client.AdmitActionAsync(new WidgetActionEvent(
            DiagnosticCursorNotificationWidget.SingleInvalidationAction,
            "diagnostic.action")));
    Assert.Equal(1L, await delivered.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    _ = await client.GetSnapshotAsync();
    Assert.Equal(1, Volatile.Read(ref publications));
}

static async Task ProcessClientCarriesLifecycleFirstCursorPagination()
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    await using var client = CreateClient(
        extraArguments: ["--notification-lifecycle-cursor-probe"]);
    var initialInvalidation = new TaskCompletionSource<long>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var forwardInvalidations = System.Threading.Channels.Channel.CreateUnbounded<long>(
        new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
    var actionFailure = new TaskCompletionSource<WidgetActionFailure>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var actionAdmitted = 0;
    client.Invalidated += (_, revision) =>
    {
        if (Volatile.Read(ref actionAdmitted) == 0)
            initialInvalidation.TrySetResult(revision);
        else
            forwardInvalidations.Writer.TryWrite(revision);
    };
    client.ActionFailed += (_, failure) => actionFailure.TrySetResult(failure);

    await client.SetLifecycleStateAsync(
        WidgetLifecycleState.Visible, deadline.Token);
    var initialRevision = await initialInvalidation.Task.WaitAsync(deadline.Token);
    Assert.True(initialRevision > 0,
        "The external worker did not publish a positive initial invalidation revision.");
    Assert.Equal(1, client.Starts);
    var workerProcessId = client.WorkerProcessId;
    Assert.True(workerProcessId is > 0,
        "The lifecycle-first cursor worker did not expose its current process identity.");

    var presentationGeneration = new string('D', 32);
    var initialPresentation = await client.GetPresentationAsync(
        PresentationUpdateCapabilities.Current,
        presentationGeneration,
        baseSequence: 0,
        requireCheckpoint: false,
        deadline.Token);
    Assert.True(initialPresentation.Update is null,
        "The missing initial base did not produce a complete checkpoint.");
    var first = initialPresentation.Snapshot;
    Assert.SequenceEqual(
        Enumerable.Range(0, 4).Select(index => $"diagnostic.item.{index}"),
        first.Root.Children.Select(child => child.Id));
    Assert.True(first.Root.VirtualCollectionWindow is
    {
        RequestGeneration: 1,
        FirstItemIndex: 0,
        TotalItemCount: 12,
        HasBefore: false,
        HasAfter: true,
        EstimatedItemExtent: 40,
        Change: VirtualCollectionWindowChange.Replace,
    }, "The external worker did not return the exact Ready generation-1 window.");
    Assert.Equal<string?>(null, first.Root.ScrollNearStartActionId);
    var pageAction = first.Root.ScrollNearEndActionId
        ?? throw new InvalidOperationException(
            "The external worker omitted its generated near-end cursor action.");
    Assert.Equal(1, client.Starts);
    Assert.Equal(workerProcessId, client.WorkerProcessId);

    Volatile.Write(ref actionAdmitted, 1);
    Assert.Equal(
        WidgetOperationAdmission.Enqueued,
        await client.AdmitActionAsync(
            new WidgetActionEvent(pageAction, first.Root.Id), deadline.Token));
    var current = first;
    long currentRevision = initialRevision;
    WidgetRuntimePresentation? converged = null;
    const int maximumForwardInvalidations = 4;
    for (var observation = 0; observation < maximumForwardInvalidations; observation++)
    {
        if (actionFailure.Task.IsCompleted)
            ThrowCursorActionFailure(await actionFailure.Task, pageAction, first.Root.Id);
        var nextInvalidation = forwardInvalidations.Reader.ReadAsync(deadline.Token).AsTask();
        var publication = await Task.WhenAny(nextInvalidation, actionFailure.Task)
            .WaitAsync(deadline.Token);
        if (ReferenceEquals(publication, actionFailure.Task))
            ThrowCursorActionFailure(await actionFailure.Task, pageAction, first.Root.Id);

        var forwardRevision = await nextInvalidation;
        Assert.True(forwardRevision > currentRevision,
            "A current-session cursor invalidation did not advance revision.");
        currentRevision = forwardRevision;
        Assert.Equal(1, client.Starts);
        Assert.Equal(workerProcessId, client.WorkerProcessId);

        var changed = await client.GetPresentationAsync(
            PresentationUpdateCapabilities.Current,
            presentationGeneration,
            current.Sequence,
            requireCheckpoint: false,
            deadline.Token);
        Assert.True(changed.Update is not null,
            "A current exact base did not produce an atomic cursor update.");
        Assert.Equal(current.Sequence, changed.Update!.BaseSequence);
        Assert.Equal(changed.Snapshot.Sequence, changed.Update.Sequence);
        Assert.Equal(presentationGeneration, changed.Update.PresentationGeneration);
        Assert.True(changed.Snapshot.Sequence > current.Sequence,
            "A cursor update did not advance the materialized presentation sequence.");
        current = changed.Snapshot;

        if (current.Root.VirtualCollectionWindow?.RequestGeneration == 2)
        {
            converged = changed;
            break;
        }
        Assert.True(current.Root.VirtualCollectionWindow is
        {
            RequestGeneration: 1,
            FirstItemIndex: 0,
            TotalItemCount: 12,
            HasBefore: false,
            HasAfter: true,
            EstimatedItemExtent: 40,
            Change: VirtualCollectionWindowChange.Replace,
        }, "An intermediate cursor update mutated generation-1 window authority.");
        Assert.SequenceEqual(
            Enumerable.Range(0, 4).Select(index => $"diagnostic.item.{index}"),
            current.Root.Children.Select(child => child.Id));
    }
    Assert.True(converged is not null,
        $"The cursor did not converge after {maximumForwardInvalidations} current-session invalidations.");
    var finalWindowChange = converged!.Update!.Operations
        .SelectMany(operation => operation.Properties ?? [])
        .Single(change => change.Property == PresentationProperty.VirtualCollectionWindow);
    var updateWindow = RuntimeJson.FromElement<VirtualCollectionWindow>(finalWindowChange.Value);
    Assert.True(updateWindow is
    {
        RequestGeneration: 2,
        FirstItemIndex: 0,
        TotalItemCount: 12,
        HasBefore: false,
        HasAfter: true,
        EstimatedItemExtent: 40,
        Change: VirtualCollectionWindowChange.Append,
    }, "The converged atomic update did not carry exact generation-2 Append authority.");

    var second = converged.Snapshot;
    Assert.SequenceEqual(
        Enumerable.Range(0, 8).Select(index => $"diagnostic.item.{index}"),
        second.Root.Children.Select(child => child.Id));
    Assert.True(second.Root.VirtualCollectionWindow is
    {
        RequestGeneration: 2,
        FirstItemIndex: 0,
        TotalItemCount: 12,
        HasBefore: false,
        HasAfter: true,
        EstimatedItemExtent: 40,
        Change: VirtualCollectionWindowChange.Append,
    }, "The exact-base materialized snapshot did not retain generation-2 Append.");
    Assert.Equal<string?>(null, second.Root.ScrollNearStartActionId);
    Assert.True(second.Root.ScrollNearEndActionId is not null,
        "The eight-item durable window lost its remaining forward boundary.");
    Assert.Equal(1, client.Starts);
    Assert.Equal(workerProcessId, client.WorkerProcessId);

    var checkpoint = await client.GetSnapshotAsync(deadline.Token);
    Assert.SequenceEqual(
        Enumerable.Range(0, 8).Select(index => $"diagnostic.item.{index}"),
        checkpoint.Root.Children.Select(child => child.Id));
    Assert.True(checkpoint.Root.VirtualCollectionWindow is
    {
        RequestGeneration: 2,
        FirstItemIndex: 0,
        TotalItemCount: 12,
        HasBefore: false,
        HasAfter: true,
        EstimatedItemExtent: 40,
        Change: VirtualCollectionWindowChange.Replace,
    }, "The base-zero checkpoint did not normalize the same durable window to Replace.");
    Assert.Equal<string?>(null, checkpoint.Root.ScrollNearStartActionId);
    Assert.True(checkpoint.Root.ScrollNearEndActionId is not null,
        "The base-zero checkpoint lost its remaining forward boundary.");
    Assert.Equal(1, client.Starts);
    Assert.Equal(workerProcessId, client.WorkerProcessId);

    static void ThrowCursorActionFailure(
        WidgetActionFailure failure,
        string expectedActionId,
        string expectedSourceElementId)
    {
        Assert.Equal(expectedActionId, failure.ActionId);
        Assert.Equal(expectedSourceElementId, failure.SourceElementId);
        throw new InvalidOperationException(
            $"The external worker published action failure '{failure.Message}'.");
    }
}

static async Task DirectAndControllerActionsShareQueue()
{
    await using var client = CreateClient();
    var snapshot = await client.GetSnapshotAsync();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var invalidations = System.Threading.Channels.Channel.CreateUnbounded<long>();
    client.Invalidated += (_, revision) => invalidations.Writer.TryWrite(revision);

    Assert.Equal(WidgetOperationAdmission.Enqueued,
        await client.AdmitActionAsync(new WidgetActionEvent(
            "invalidate", "direct", ControllerButton.X, Sequence: 1)));
    Assert.True(await client.SendControllerInputAsync(OpenInput(
        snapshot, ControllerButton.RightBumper, "button", inputSequence: 2)),
        "Controller action was not admitted behind the direct action.");

    _ = await invalidations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    _ = await invalidations.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("1,2", Find(
        (await client.GetSnapshotAsync()).Root, "controller-history").Text);
}

static async Task DirectActionAdmissionIsBounded()
{
    await using var client = CreateClient();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var blockingStarted = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    client.Invalidated += (_, _) => blockingStarted.TrySetResult();
    Assert.Equal(WidgetOperationAdmission.Enqueued,
        await client.AdmitActionAsync(new WidgetActionEvent("queued-block", "direct")));
    await blockingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Assert.Equal(WidgetOperationAdmission.Enqueued,
        await client.AdmitActionAsync(new WidgetActionEvent(
            "volume.changed", "volume", RequestedValue: 0.1, InputScopeId: "root")));
    Assert.Equal(WidgetOperationAdmission.Replaced,
        await client.AdmitActionAsync(new WidgetActionEvent(
            "volume.changed", "volume", RequestedValue: 0.2, InputScopeId: "root")));

    for (var sequence = 1; sequence < Widget.ActionQueueCapacity; sequence++)
    {
        Assert.Equal(WidgetOperationAdmission.Enqueued,
            await client.AdmitActionAsync(new WidgetActionEvent(
                "invalidate", "direct", Sequence: sequence)));
    }
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    Assert.Equal(WidgetOperationAdmission.RejectedCapacity,
        await client.AdmitActionAsync(new WidgetActionEvent("invalidate", "overflow")));
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1),
        $"Saturated admission blocked for {stopwatch.Elapsed.TotalMilliseconds:0} ms.");

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    Assert.Equal(WidgetOperationAdmission.RejectedInactive,
        await client.AdmitActionAsync(new WidgetActionEvent("invalidate", "inactive")));
    Assert.True(client.IsRunning, "Queue cancellation restarted or terminated the worker.");
    Assert.Equal(1, client.Starts);
}

static async Task DirectActionFailuresAreObservable()
{
    await using var client = CreateClient();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var failed = new TaskCompletionSource<WidgetActionFailure>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    client.ActionFailed += (_, failure) => failed.TrySetResult(failure);

    Assert.Equal(WidgetOperationAdmission.Enqueued,
        await client.AdmitActionAsync(new WidgetActionEvent("queued-fail", "direct")));
    var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal("queued-fail", failure.ActionId);
    Assert.Equal("Action failed.", failure.Message);
    Assert.True(client.IsRunning, "An action failure must not crash the worker.");
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

static async Task AccessibilityAutomationCannotReserveAuthority()
{
    var companion = new ProbeCompanionSession();
    await using var client = CreateClient(companionFactory: _ => companion);
    var snapshot = await client.GetSnapshotAsync();
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var input = new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        Sequence: 12,
        SnapshotSequence: snapshot.Sequence,
        Origin: ControllerInputOrigin.AccessibilityAutomation);
    var authority = new WidgetDashboardGestureAuthority(
        WidgetMediaCapabilities.Control.CapabilityId,
        WidgetMediaCapabilities.Control.OperationId,
        input.Sequence,
        input.SnapshotSequence,
        TimeSpan.FromSeconds(2));

    await Assert.ThrowsAsync<WidgetProcessException>(async () =>
        await client.SendControllerInputAsync(input, authority));
    Assert.Equal(0, companion.GrantedAuthorities.Count);
    Assert.Equal(0, companion.RevokedInputSequences.Count);

    var serializedPhysical = RuntimeJson.ToElement(input with
    {
        Origin = ControllerInputOrigin.PhysicalController,
    });
    Assert.True(!serializedPhysical.TryGetProperty("origin", out _),
        "The default physical origin must remain absent for older runtime peers.");
    var serializedAutomation = RuntimeJson.ToElement(input);
    Assert.Equal("accessibilityAutomation",
        serializedAutomation.GetProperty("origin").GetString());
    using var legacyJson = JsonDocument.Parse("""
        {
          "button": "x",
          "phase": "pressed",
          "context": "dashboardQuickAction",
          "sequence": 13,
          "snapshotSequence": 1
        }
        """);
    Assert.Equal(ControllerInputOrigin.PhysicalController,
        RuntimeJson.FromElement<ControllerInputEvent>(legacyJson.RootElement).Origin);
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        await client.SendControllerInputAsync(input with { Origin = (ControllerInputOrigin)99 }));

    Assert.True(await client.SendControllerInputAsync(input),
        "Accessibility automation should still admit the ordinary action without authority.");

    var adversarialCompanion = new ProbeCompanionSession();
    await using var adversarial = CreateClient(
        extraArguments: ["--gesture-adversarial-probe"],
        companionFactory: _ => adversarialCompanion);
    var adversarialSnapshot = await adversarial.GetSnapshotAsync();
    await adversarial.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    Assert.True(await adversarial.SendControllerInputAsync(input with
    {
        Sequence = 14,
        SnapshotSequence = adversarialSnapshot.Sequence,
    }), "The adversarial automation action was not handled as an ordinary action.");
    var adversarialResult = await adversarial.GetSnapshotAsync();
    Assert.Equal(
        "context=false;activations=0;provider=0",
        Find(adversarialResult.Root, "gesture-adversarial-result").Text);
    Assert.Equal(0, adversarialCompanion.GrantedAuthorities.Count);
    Assert.Equal(0, adversarialCompanion.RevokedInputSequences.Count);
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
    var pipeName = $"wrail-runtime-gesture-{Guid.NewGuid():N}";
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

    var activationAttempts = 0;
    ((IDashboardGestureActivatingCapabilityClient)adapter).SetDashboardGestureActivator(
        (_, _, _, _) =>
        {
            Interlocked.Increment(ref activationAttempts);
            return ValueTask.FromResult(false);
        });
    using (WidgetCapabilityInvocationContext.Enter(new(40, 4)))
    {
        await Assert.ThrowsAsync<WidgetCapabilityException>(async () =>
            await adapter.InvokeAsync(
                WidgetMediaCapabilities.Control,
                new ControlWidgetMediaSessionRequest(
                    "media-1", WidgetMediaSessionCommand.Next)));
    }
    Assert.Equal(1, activationAttempts);
    Assert.Equal(0, backend.MediaControlCalls);

    ((IDashboardGestureActivatingCapabilityClient)adapter).SetDashboardGestureActivator(
        (_, _, _, _) =>
        {
            Interlocked.Increment(ref activationAttempts);
            return ValueTask.FromResult(true);
        });
    using (WidgetCapabilityInvocationContext.Enter(new(40, 4)))
    {
        var response = await adapter.InvokeAsync(
            WidgetMediaCapabilities.Control,
            new ControlWidgetMediaSessionRequest("media-1", WidgetMediaSessionCommand.Next));
        Assert.True(response.Acknowledged, "Exact gesture context was not propagated.");
    }
    Assert.Equal(2, activationAttempts);
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
    Assert.Equal("Action failed.", failure.Message);
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
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var firstProcess = client.WorkerProcessId;
    var failed = new TaskCompletionSource<WidgetFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Failed += (_, failure) => failed.TrySetResult(failure);
    await SendCrashingActionAsync(client);
    var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.True(failure.CanRestart, "One restart should remain after first crash.");
    var recovered = await client.GetSnapshotAsync();
    Assert.Equal("runtime.test", recovered.WidgetInstanceId);
    Assert.Equal(2, client.Starts);
    Assert.True(client.WorkerProcessId != firstProcess, "Restart reused the terminated worker process.");
    if (OperatingSystem.IsWindows())
        Assert.True(client.AppliedJobAccounting?.ActiveProcesses >= 1,
            "The recovered worker was absent from Job accounting.");
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

        var failed = new TaskCompletionSource<WidgetFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Failed += (_, failure) => failed.TrySetResult(failure);
        await SendCrashingActionAsync(client);
        _ = await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    await SendCrashingActionAsync(client);
    await WaitUntilAsync(() => !client.IsRunning, TimeSpan.FromSeconds(2));
    Assert.True(!client.IsRunning, "Crash probe worker unexpectedly remained live.");

    // A residency timer can observe the crash only after its delay. Treating
    // this no-session cleanup as an intentional unload would incorrectly let
    // the next launch bypass the zero-restart policy.
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
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

static async Task HungActionAdmissionIsPrompt()
{
    await using var client = CreateClient(requestTimeout: TimeSpan.FromMilliseconds(250));
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    await client.SendActionAsync(new WidgetActionEvent("hang", "button"));
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1),
        $"Action admission blocked for {stopwatch.Elapsed.TotalMilliseconds:0} ms.");
    Assert.True(client.IsRunning, "A slow action terminated the worker.");
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    Assert.True(client.IsRunning, "Cooperative action cancellation restarted the worker.");
    Assert.Equal(1, client.Starts);
}

static async Task MalformedSnapshotIsRejected()
{
    await using var client = CreateClient(extraArguments: ["--malformed-worker"]);
    await Assert.ThrowsAsync<WidgetProtocolViolationException>(() => client.GetSnapshotAsync());
}

static async Task SessionNonceMismatchIsRejected()
{
    await using var client = CreateClient(extraArguments: ["--wrong-session-nonce"]);
    var exception = await Assert.ThrowsAsync<WidgetProcessException>(
        () => client.GetSnapshotAsync());
    Assert.True(exception.InnerException is WidgetProtocolViolationException violation &&
                violation.Message.Contains("wrong session nonce", StringComparison.Ordinal),
        "A nonce mismatch did not retain its sanitized primary protocol failure.");
    Assert.True(!client.IsRunning,
        "A worker with a mismatched session nonce remained admitted.");
}

static async Task ProtocolValidationDiagnosticIsStructural()
{
    await using var client = CreateClient(extraArguments: ["--invalid-protocol-widget"]);
    var exception = await Assert.ThrowsAsync<WidgetProcessException>(() => client.GetSnapshotAsync());

    Assert.True(exception.Message.Contains("$.activeInputScopeId", StringComparison.Ordinal),
        "The worker response omitted the first validation path.");
    Assert.True(exception.Message.Contains("invalid_active_input_scope", StringComparison.Ordinal),
        "The worker response omitted the first validation code.");
    Assert.True(!exception.Message.Contains("FIRST_WIDGET_SECRET", StringComparison.Ordinal),
        "The worker response exposed widget-controlled text from the first validation message.");
    Assert.True(!exception.Message.Contains("$.initialFocusId", StringComparison.Ordinal),
        "The worker response exposed a later validation path.");
    Assert.True(!exception.Message.Contains("invalid_focus_target", StringComparison.Ordinal),
        "The worker response exposed a later validation code.");
    Assert.True(!exception.Message.Contains("SECOND_WIDGET_SECRET", StringComparison.Ordinal),
        "The worker response exposed widget-controlled text from a later validation message.");
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
    Func<WidgetProcessCompanionContext, IWidgetProcessCompanionSession>? companionFactory = null,
    Func<IDisposable>? processLeaseFactory = null,
    Func<CancellationToken, IWidgetProcessContentLease>? contentLeaseFactory = null,
    string? contentIsolationKey = null,
    TimeSpan? contentLeaseTimeout = null,
    IAppContainerAuthorityOperations? contentAuthorityOperations = null,
    IAppContainerAuthorityJournal? contentAuthorityJournal = null,
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
        CompanionSessionFactory = companionFactory,
        ProcessLeaseFactory = processLeaseFactory,
        ContentLeaseFactory = contentLeaseFactory,
        ContentLeaseTimeout = contentLeaseTimeout ?? TimeSpan.FromSeconds(5),
        ContentAuthorityOperations = contentAuthorityOperations,
        ContentAuthorityJournal = contentAuthorityJournal,
        IsolationPolicy = contentLeaseFactory is null
            ? WidgetWorkerIsolationPolicy.HostTrustedJobOnly
            : WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = contentLeaseFactory is null
            ? null
            : contentIsolationKey ?? $"runtime-content-{Guid.NewGuid():N}",
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
    private string _committedTextStatus = "committed:none";
    private string _activeScope = "root";
    private readonly byte[]? _privateMemory;

    internal TestWidget(int privateMemoryMb = 0)
    {
        if (privateMemoryMb <= 0) return;
        _privateMemory = GC.AllocateUninitializedArray<byte>(
            checked(privateMemoryMb * 1024 * 1024));
        _privateMemory[0] = 1;
        _privateMemory[^1] = 1;
    }

    public override WidgetView Render() => new(
        UI.Stack("root",
        [
            UI.Text(IsActive ? "active" : "inactive", "activity"),
            .. PrivateMemoryNodes(),
            UI.Text(ControllerHistory(), "controller-history"),
            UI.Text(_scopedAction, "scoped-action"),
            UI.Text(_committedTextStatus, "committed-text-status"),
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
                .InputScope("empty-window-scope"),
        ]),
        _activeScope switch
        {
            "nested-window-scope" => "nested-focus",
            "empty-window-scope" => "empty-focus",
            _ => "button",
        },
        [new WidgetQuickAction(ControllerButton.X, "invalidate", "Refresh")],
        _activeScope);

    private WidgetElement[] PrivateMemoryNodes() => _privateMemory is null
        ? []
        : [UI.Text(
            $"private-memory:{_privateMemory.Length / (1024 * 1024)}",
            "private-memory")];

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
        else if (action.ActionId == "committed-text")
        {
            _committedTextStatus = action.CommittedText is { } committed
                ? $"committed:{committed.Length}"
                : "committed:missing";
            Invalidate();
        }
        else if (action.ActionId == "queued-block")
        {
            Invalidate();
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

file sealed class InvalidProtocolWidget : Widget
{
    public override WidgetView Render() => new(
        UI.Stack("root"),
        InitialFocusId: "SECOND_WIDGET_SECRET",
        ActiveInputScopeId: "FIRST_WIDGET_SECRET");
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

file sealed class AdversarialGestureWidget(
    GestureProbeCapabilityClient probe) : Widget
{
    private readonly object _gate = new();
    private readonly GestureProbeCapabilityClient _probe = probe;
    private string _result = "not-invoked";

    public override WidgetView Render()
    {
        string result;
        lock (_gate) result = _result;
        return new WidgetView(
            UI.Stack("root", UI.Text(result, "gesture-adversarial-result")),
            QuickActions:
            [
                new WidgetQuickAction(
                    ControllerButton.X,
                    "gesture.adversarial",
                    "Adversarial gesture",
                    new WidgetQuickActionCapability(
                        WidgetMediaCapabilities.Control.CapabilityId,
                        WidgetMediaCapabilities.Control.OperationId)),
            ]);
    }

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

        var hasContext = WidgetCapabilityInvocationContext.Current is { IsActive: true };
        try
        {
            await HostServices.Media.ControlAsync(
                "media-1", WidgetMediaSessionCommand.Next, cancellationToken);
        }
        catch (WidgetCapabilityException)
        {
            // The fixture records the independent authority boundaries below.
        }
        lock (_gate)
        {
            _result = $"context={hasContext.ToString().ToLowerInvariant()};" +
                $"activations={_probe.ActivationAttempts};provider={_probe.ProviderCalls}";
        }
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
    private int _activationAttempts;
    private int _providerCalls;

    public int ActivationAttempts => Volatile.Read(ref _activationAttempts);
    public int ProviderCalls => Volatile.Read(ref _providerCalls);

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
        Interlocked.Increment(ref _activationAttempts);
        if (!await _activator(
                gesture,
                operation.CapabilityId,
                operation.OperationId,
                cancellationToken).ConfigureAwait(false))
            throw new WidgetCapabilityException(
                "lifecycle_denied", "The host rejected dashboard gesture activation.");
        Interlocked.Increment(ref _providerCalls);
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
    private string _deniedWrite = "unprobed";
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
            UI.Text(_deniedWrite, "probe-denied-write"),
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
        _deniedWrite = await TryWriteAsync(deniedPath, widgetLifetime);
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
        var pipeName = $"wrail-companion-prelaunch-{Guid.NewGuid():N}";
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
            System.IO.Path.GetTempPath(), $"wrail-runtime-tests-{Guid.NewGuid():N}");
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

file sealed class CallbackDisposable(Action callback) : IDisposable
{
    private Action? _callback = callback;

    public void Dispose() => Interlocked.Exchange(ref _callback, null)?.Invoke();
}

file sealed class TestContentLease(
    string authorityRoot,
    IReadOnlyList<string> readOnlyDirectories,
    IReadOnlyList<string> readOnlyFiles,
    Action release,
    IReadOnlyList<AppContainerAuthorityExpectedTarget>? targets = null)
    : IWidgetProcessContentLease
{
    private Action? _release = release;

    public IReadOnlyList<AppContainerAuthorityExpectedTarget> Targets { get; } =
        targets ?? TestFileObjectIdentity.Targets(
            authorityRoot, readOnlyDirectories, readOnlyFiles);

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

file sealed class TestAuthorityOperations(
    int? failApplyAt = null,
    AppContainerAuthorityTargetKind[]? failRestoreKinds = null,
    Dictionary<AppContainerAuthorityTarget, string>? states = null,
    Action<int>? afterRestore = null)
    : IAppContainerAuthorityOperations
{
    private readonly Dictionary<AppContainerAuthorityTarget, string> _states = states ?? [];
    private int _applyIndex;

    public List<AppContainerAuthorityTarget> RestoreOrder { get; } = [];
    public int ApplyCount => _applyIndex;
    public int CaptureCount { get; private set; }

    public AppContainerAuthoritySnapshot Capture(AppContainerAuthorityTarget target)
    {
        CaptureCount++;
        if (!_states.TryGetValue(target, out var descriptor))
        {
            descriptor = Original(target);
            _states.Add(target, descriptor);
        }
        return new AppContainerAuthoritySnapshot(target, descriptor, Identity(target));
    }

    public void Apply(AppContainerAuthoritySnapshot snapshot)
    {
        var index = _applyIndex++;
        _states[snapshot.Target] = Granted(snapshot.Target);
        if (index == failApplyAt) throw new IOException($"apply {index} failed");
    }

    public void VerifyApplied(AppContainerAuthoritySnapshot snapshot)
    {
        if (_states[snapshot.Target] != Granted(snapshot.Target))
            throw new IOException($"apply verification {snapshot.Target.Kind} failed");
    }

    public void Restore(AppContainerAuthoritySnapshot snapshot)
    {
        RestoreOrder.Add(snapshot.Target);
        if (failRestoreKinds?.Contains(snapshot.Target.Kind) == true)
            throw new IOException($"restore {snapshot.Target.Kind} failed");
        _states[snapshot.Target] = snapshot.AccessDescriptor;
        afterRestore?.Invoke(RestoreOrder.Count);
    }

    public void VerifyRestored(AppContainerAuthoritySnapshot snapshot)
    {
        if (_states[snapshot.Target] != snapshot.AccessDescriptor)
            throw new IOException($"restore verification {snapshot.Target.Kind} failed");
    }

    public string StateFor(AppContainerAuthorityTarget target) => _states[target];

    public static string Original(AppContainerAuthorityTarget target) =>
        $"original:{target.Kind}:{target.Path}";

    public static string Granted(AppContainerAuthorityTarget target) =>
        $"granted:{target.Kind}:{target.Path}";

    public static AppContainerAuthorityObjectIdentity Identity(
        AppContainerAuthorityTarget target) => OperatingSystem.IsWindows() &&
            (File.Exists(target.Path) || Directory.Exists(target.Path))
                ? TestFileObjectIdentity.Read(
                    target.Path,
                    target.Kind != AppContainerAuthorityTargetKind.VerifiedFile)
                : new(1, "00000000000000000000000000000001");

    public void Dispose() { }
}

file sealed class TestAuthorityJournal(bool failWrite = false)
    : IAppContainerAuthorityJournal
{
    private readonly bool _failWrite = failWrite;
    private readonly Dictionary<string, AppContainerAuthorityPendingTransaction> _pending =
        new(StringComparer.Ordinal);
    private int _held;

    public IReadOnlyList<AppContainerAuthoritySnapshot>? Pending =>
        _pending.Count == 0 ? null : _pending.Values.Single().Snapshots;

    public IAppContainerAuthorityJournalLease Acquire(string profileName)
    {
        if (Interlocked.Exchange(ref _held, 1) != 0)
            throw new AppContainerAuthorityJournalException(
                "The test authority journal is already held.");
        return new Lease(this, profileName);
    }

    private sealed class Lease(TestAuthorityJournal owner, string profileName)
        : IAppContainerAuthorityJournalLease
    {
        private TestAuthorityJournal? _owner = owner;

        public AppContainerAuthorityPendingTransaction? ReadPending()
        {
            var current = _owner ?? throw new ObjectDisposedException(nameof(Lease));
            return current._pending.TryGetValue(profileName, out var pending)
                ? pending with { Snapshots = pending.Snapshots.ToArray() }
                : null;
        }

        public void WritePending(IReadOnlyList<AppContainerAuthoritySnapshot> snapshots)
        {
            var current = _owner ?? throw new ObjectDisposedException(nameof(Lease));
            if (current._failWrite)
                throw new AppContainerAuthorityJournalException(
                    "The test journal rejected its pending record.");
            current._pending[profileName] = new AppContainerAuthorityPendingTransaction(
                new string('A', 64), profileName, snapshots.ToArray(), IsLegacy: false);
        }

        public void ClearPending()
        {
            var current = _owner ?? throw new ObjectDisposedException(nameof(Lease));
            current._pending.Remove(profileName);
        }

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is not null) Interlocked.Exchange(ref current._held, 0);
        }
    }
}

file sealed class TerminatingAuthorityOperations(
    IAppContainerAuthorityOperations inner,
    int terminateAfterApply) : IAppContainerAuthorityOperations
{
    private int _applies;

    public AppContainerAuthoritySnapshot Capture(AppContainerAuthorityTarget target) =>
        inner.Capture(target);

    public void Apply(AppContainerAuthoritySnapshot snapshot)
    {
        inner.Apply(snapshot);
        if (Interlocked.Increment(ref _applies) == terminateAfterApply)
            Environment.Exit(91);
    }

    public void VerifyApplied(AppContainerAuthoritySnapshot snapshot) =>
        inner.VerifyApplied(snapshot);

    public void Restore(AppContainerAuthoritySnapshot snapshot) =>
        inner.Restore(snapshot);

    public void VerifyRestored(AppContainerAuthoritySnapshot snapshot) =>
        inner.VerifyRestored(snapshot);

    public void Dispose() => inner.Dispose();
}

file static class TestFileObjectIdentity
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileIdInfoClass = 18;

    internal static IReadOnlyDictionary<string, AppContainerAuthorityObjectIdentity> Capture(
        IEnumerable<string> paths)
    {
        var identities = new Dictionary<string, AppContainerAuthorityObjectIdentity>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var value in paths.Select(Path.GetFullPath).Distinct(
                     StringComparer.OrdinalIgnoreCase))
            identities.Add(value, Read(value, Directory.Exists(value)));
        return identities;
    }

    internal static IReadOnlyList<AppContainerAuthorityExpectedTarget> Targets(
        string authorityRoot,
        IReadOnlyList<string> directories,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, AppContainerAuthorityObjectIdentity>? identities = null)
    {
        identities ??= Capture(
            new[] { authorityRoot }.Concat(directories).Concat(files));
        AppContainerAuthorityExpectedTarget Expected(
            string path,
            AppContainerAuthorityTargetKind kind)
        {
            var fullPath = Path.GetFullPath(path);
            return new AppContainerAuthorityExpectedTarget(
                new AppContainerAuthorityTarget(fullPath, kind),
                identities[fullPath]);
        }
        return new[]
            {
                Expected(
                    authorityRoot,
                    AppContainerAuthorityTargetKind.AuthorityRootDirectory),
            }
            .Concat(directories
                .Where(path => !string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(authorityRoot),
                    StringComparison.OrdinalIgnoreCase))
                .Select(path =>
                    Expected(path, AppContainerAuthorityTargetKind.VerifiedDirectory)))
            .Concat(files.Select(path =>
                Expected(path, AppContainerAuthorityTargetKind.VerifiedFile)))
            .ToArray();
    }

    internal static AppContainerAuthorityObjectIdentity Read(
        string path,
        bool expectDirectory)
    {
        using var handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint |
                (expectDirectory ? FileFlagBackupSemantics : 0),
            IntPtr.Zero);
        if (handle.IsInvalid ||
            !GetFileAttributeTagInfo(
                handle,
                FileAttributeTagInfoClass,
                out var attributes,
                Marshal.SizeOf<FileAttributeTagInfo>()) ||
            (attributes.FileAttributes & FileAttributes.ReparsePoint) != 0 ||
            ((attributes.FileAttributes & FileAttributes.Directory) != 0) != expectDirectory ||
            !GetFileIdInfo(
                handle,
                FileIdInfoClass,
                out var information,
                Marshal.SizeOf<FileIdInfo>()))
            throw new IOException("Test object identity could not be captured.");
        return new AppContainerAuthorityObjectIdentity(
            information.VolumeSerialNumber,
            $"{information.FileId.Low:X16}{information.FileId.High:X16}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        internal FileAttributes FileAttributes;
        internal uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileId128
    {
        internal ulong Low;
        internal ulong High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        internal ulong VolumeSerialNumber;
        internal FileId128 FileId;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileAttributeTagInfo(
        SafeFileHandle file,
        int informationClass,
        out FileAttributeTagInfo information,
        int bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileIdInfo(
        SafeFileHandle file,
        int informationClass,
        out FileIdInfo information,
        int bufferSize);
}
