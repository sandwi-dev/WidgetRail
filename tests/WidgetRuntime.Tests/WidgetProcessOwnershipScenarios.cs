using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

internal static class WidgetProcessOwnershipScenarios
{
    internal static async Task PendingRequestsCorrelateExactly()
    {
        var pending = new WidgetPendingRequests();
        using var first = pending.Register();
        using var second = pending.Register();
        Equal(1L, first.RequestId);
        Equal(2L, second.RequestId);
        Equal(2, pending.Count);

        True(pending.TryComplete(Response(second.RequestId, MessageTypes.Acknowledged)),
            "The exact second request was not completed.");
        Equal(MessageTypes.Acknowledged, (await second.Response).Type);
        Equal(1, pending.Count);
        False(pending.TryComplete(Response(77, MessageTypes.Acknowledged)),
            "An unknown response was accepted.");

        pending.FailAll(new WidgetProcessException("session ended"));
        await ThrowsAsync<WidgetProcessException>(async () => await first.Response);
        Equal(0, pending.Count);
    }

    internal static Task GestureReservationsAreExactAndExpire()
    {
        var clock = new OwnershipTimeProvider();
        var reservations = new WidgetDashboardGestureReservations(
            clock, TimeSpan.FromSeconds(10));
        var input = Input(17, 31);
        var authority = Authority(17, 31);
        reservations.Reserve(input, authority);

        var mismatch = reservations.Take(Activation(17, 31, operationId: "wrong"));
        True(mismatch is null, "A mismatched operation consumed gesture authority.");
        Equal(1, reservations.Count);
        Equal(authority, reservations.Take(Activation(17, 31)));
        Equal(0, reservations.Count);

        reservations.Reserve(Input(18, 32), Authority(18, 32));
        clock.Advance(TimeSpan.FromSeconds(10));
        True(reservations.Take(Activation(18, 32)) is null,
            "Expired gesture authority remained usable.");
        Equal(0, reservations.Count);
        return Task.CompletedTask;
    }

    internal static async Task SessionTerminalCleanupIsShared()
    {
        var session = new WidgetProcessSession(
            TimeProvider.System, TimeSpan.FromSeconds(10));
        var processLease = new CountingLease();
        var contentLease = new CountingContentLease();
        var companion = new GatedDisposeCompanion();
        session.AttachProcessLease(processLease);
        session.AttachContentLease(contentLease);
        session.AttachCompanion(companion);
        session.StartCompanion();
        session.StartReader(_ => Task.CompletedTask);

        var first = session.DisposeAsync();
        var second = session.DisposeAsync();
        True(ReferenceEquals(first, second),
            "Concurrent terminal callers did not share one cleanup task.");
        True(companion.DisposeStarted, "Companion cleanup did not start.");
        False(first.IsCompleted, "Terminal cleanup completed before its companion drained.");
        Equal(1, processLease.DisposeCount);
        Equal(1, contentLease.DisposeCount);

        companion.ReleaseDispose();
        await Task.WhenAll(first, second);
        True(session.IsTerminal, "The session did not retain its terminal state.");
        Equal(1, companion.DisposeCount);
        True(ReferenceEquals(first, session.DisposeAsync()),
            "A later terminal call did not observe the completed terminal task.");
    }

    internal static Task ReplacementSessionsDoNotShareMutableAuthority()
    {
        var clock = new OwnershipTimeProvider();
        var retired = new WidgetProcessSession(clock, TimeSpan.FromSeconds(10));
        var replacement = new WidgetProcessSession(clock, TimeSpan.FromSeconds(10));
        using var retiredRequest = retired.PendingRequests.Register();
        using var replacementRequest = replacement.PendingRequests.Register();
        Equal(retiredRequest.RequestId, replacementRequest.RequestId);

        True(retired.PendingRequests.TryComplete(
                Response(retiredRequest.RequestId, MessageTypes.Acknowledged)),
            "The retired session could not complete its own request.");
        False(replacementRequest.Response.IsCompleted,
            "A retired response completed the replacement session's same-numbered request.");

        var input = Input(22, 44);
        var authority = Authority(22, 44);
        retired.GestureReservations.Reserve(input, authority);
        replacement.GestureReservations.Reserve(input, authority);
        retired.GestureReservations.Clear();
        Equal(authority, replacement.GestureReservations.Take(Activation(22, 44)));
        return Task.CompletedTask;
    }

    internal static async Task StopDuringConstructionRejectsLateResources()
    {
        var processStartReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProcessStart = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new CountingLease();
        var companion = new TrackingCompanion();
        var hooks = new WidgetProcessClientTestHooks
        {
            LifecycleDrainTimeout = TimeSpan.Zero,
            BeforeProcessStartAsync = _ =>
            {
                processStartReached.TrySetResult();
                return releaseProcessStart.Task;
            },
            SessionTerminalStarted = () => terminalStarted.TrySetResult(),
        };
        await using var client = CreateClient(hooks, companion, () => lease);

        var connect = client.GetSnapshotAsync();
        await processStartReached.Task;
        var stop = client.StopAsync();
        await terminalStarted.Task;
        await stop;
        releaseProcessStart.TrySetResult();
        await ThrowsAnyAsync(async () => await connect);

        Equal(0, client.Starts);
        False(client.IsRunning, "Stop allowed a paused construction to start a worker.");
        Equal(1, lease.DisposeCount);
        Equal(1, companion.DisposeCount);
        Equal(0, companion.RunCount);
    }

    internal static async Task StaleNotificationsCannotCrossReplacement()
    {
        await AssertStalePublicationSuppressedAsync(
            "invalidated",
            async client =>
            {
                await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
                await client.AdmitActionAsync(new WidgetActionEvent("invalidate", "button"));
            });
        await AssertStalePublicationSuppressedAsync(
            "action-failed",
            async client =>
            {
                await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
                await client.AdmitActionAsync(new WidgetActionEvent("queued-fail", "direct"));
            });
        await AssertStalePublicationSuppressedAsync(
            "worker-failed",
            async client =>
            {
                await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
                await client.AdmitActionAsync(new WidgetActionEvent("crash", "button"));
            });
    }

    internal static async Task StaleResponseCannotCompleteAfterReplacement()
    {
        using var responseRelease = new ManualResetEventSlim();
        var responseReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var responseCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSnapshot = 0;
        var hooks = new WidgetProcessClientTestHooks
        {
            BeforeResponseCorrelation = kind =>
            {
                if (!string.Equals(kind, MessageTypes.Snapshot, StringComparison.Ordinal) ||
                    Interlocked.Increment(ref firstSnapshot) != 1)
                    return;
                responseReached.TrySetResult();
                responseRelease.Wait();
            },
            ResponseCorrelationCompleted = (kind, completed) =>
            {
                if (string.Equals(kind, MessageTypes.Snapshot, StringComparison.Ordinal) &&
                    !completed)
                    responseCompleted.TrySetResult(completed);
            },
            SessionTerminalStarted = () => terminalStarted.TrySetResult(),
        };
        await using var client = CreateClient(hooks);
        var staleSnapshot = client.GetSnapshotAsync();
        await responseReached.Task;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await client.StopAsync(canceled.Token);
        await terminalStarted.Task;
        var replacementSnapshot = await client.GetSnapshotAsync();
        responseRelease.Set();
        False(await responseCompleted.Task,
            "A retired response completed after the replacement session won.");
        await ThrowsAnyAsync(async () => await staleSnapshot);
        True(replacementSnapshot.Sequence > 0,
            "The replacement session did not return its own snapshot.");
    }

    internal static async Task StaleGestureCannotGrantReplacementAuthority()
    {
        using var publicationRelease = new ManualResetEventSlim();
        var publicationReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var companion = new TrackingCompanion();
        var hooks = new WidgetProcessClientTestHooks
        {
            BeforePublicationAdmission = kind =>
            {
                if (!string.Equals(kind, "gesture", StringComparison.Ordinal)) return;
                publicationReached.TrySetResult();
                publicationRelease.Wait();
            },
            SessionTerminalStarted = () => terminalStarted.TrySetResult(),
            PublicationAdmissionCompleted = (kind, admitted) =>
            {
                if (string.Equals(kind, "gesture", StringComparison.Ordinal))
                    publicationCompleted.TrySetResult(admitted);
            },
        };
        await using var client = CreateClient(
            hooks, companion, extraArguments: ["--gesture-custom-probe"]);
        var snapshot = await client.GetSnapshotAsync();
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
        var authority = new WidgetDashboardGestureAuthority(
            WidgetMediaCapabilities.Control.CapabilityId,
            WidgetMediaCapabilities.Control.OperationId,
            91,
            snapshot.Sequence,
            TimeSpan.FromSeconds(2));
        var input = client.SendControllerInputAsync(
            new ControllerInputEvent(
                ControllerButton.X,
                ControllerEventPhase.Pressed,
                ControllerInputContext.DashboardQuickAction,
                Sequence: 91,
                SnapshotSequence: snapshot.Sequence),
            authority);
        await publicationReached.Task;

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await client.StopAsync(canceled.Token);
        await terminalStarted.Task;
        _ = await client.GetSnapshotAsync();
        publicationRelease.Set();
        False(await publicationCompleted.Task,
            "A retired gesture publication was admitted after replacement.");
        await ThrowsAnyAsync(async () => await input);
        Equal(0, companion.GrantedAuthorities.Count);
    }

    internal static async Task CancellationIgnoringGestureGrantIsRevoked()
    {
        var terminalStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var companion = new TrackingCompanion { HoldGestureGrant = true };
        var replacementCompanion = new TrackingCompanion();
        var hooks = new WidgetProcessClientTestHooks
        {
            SessionTerminalStarted = () => terminalStarted.TrySetResult(),
        };
        await using var client = CreateClient(
            hooks, companion, extraArguments: ["--gesture-custom-probe"],
            replacementCompanion: replacementCompanion);
        var snapshot = await client.GetSnapshotAsync();
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
        var authority = new WidgetDashboardGestureAuthority(
            WidgetMediaCapabilities.Control.CapabilityId,
            WidgetMediaCapabilities.Control.OperationId,
            92,
            snapshot.Sequence,
            TimeSpan.FromSeconds(2));
        var input = client.SendControllerInputAsync(
            new ControllerInputEvent(
                ControllerButton.X,
                ControllerEventPhase.Pressed,
                ControllerInputContext.DashboardQuickAction,
                Sequence: 92,
                SnapshotSequence: snapshot.Sequence),
            authority);
        await companion.GrantStarted.Task;

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await client.StopAsync(canceled.Token);
        await terminalStarted.Task;
        _ = await client.GetSnapshotAsync();
        companion.ReleaseGrant();
        await companion.Revoked.Task;
        await ThrowsAnyAsync(async () => await input);
        Equal(1, companion.GrantedAuthorities.Count);
        Equal(92L, companion.RevokedInputSequences.Single());
    }

    private static async Task AssertStalePublicationSuppressedAsync(
        string publicationKind,
        Func<WidgetProcessClient, Task> trigger)
    {
        using var publicationRelease = new ManualResetEventSlim();
        var publicationReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var hooks = new WidgetProcessClientTestHooks
        {
            BeforePublicationAdmission = kind =>
            {
                var matches = publicationKind == "worker-failed"
                    ? kind is "process-exited" or "transport-failed"
                    : string.Equals(kind, publicationKind, StringComparison.Ordinal);
                if (!matches) return;
                publicationReached.TrySetResult();
                publicationRelease.Wait();
            },
            SessionTerminalStarted = () => terminalStarted.TrySetResult(),
            PublicationAdmissionCompleted = (kind, admitted) =>
            {
                var matches = publicationKind == "worker-failed"
                    ? kind is "process-exited" or "transport-failed"
                    : string.Equals(kind, publicationKind, StringComparison.Ordinal);
                if (matches) publicationCompleted.TrySetResult(admitted);
            },
        };
        await using var client = CreateClient(hooks);
        var published = 0;
        if (publicationKind == "invalidated") client.Invalidated += (_, _) => published++;
        else if (publicationKind == "action-failed") client.ActionFailed += (_, _) => published++;
        else client.Failed += (_, _) => published++;

        var triggerTask = trigger(client);
        await publicationReached.Task;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await client.StopAsync(canceled.Token);
        await terminalStarted.Task;
        _ = await client.GetSnapshotAsync();
        publicationRelease.Set();
        False(await publicationCompleted.Task,
            "A retired notification was admitted after replacement.");
        try { await triggerTask; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        Equal(0, published);
    }

    private static WidgetProcessClient CreateClient(
        WidgetProcessClientTestHooks hooks,
        IWidgetProcessCompanionSession? companion = null,
        Func<IDisposable>? processLeaseFactory = null,
        IReadOnlyList<string>? extraArguments = null,
        IWidgetProcessCompanionSession? replacementCompanion = null)
    {
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("Test process path is unavailable.");
        var companionGeneration = 0;
        return new WidgetProcessClient(new WidgetProcessOptions
        {
            ExecutablePath = executable,
            Arguments = extraArguments ?? [],
            WidgetInstanceId = "runtime.test",
            ConnectTimeout = TimeSpan.FromSeconds(3),
            RequestTimeout = TimeSpan.FromSeconds(2),
            MaximumRestartAttempts = 2,
            MaximumMessageBytes = 64 * 1024,
            MemoryLimitBytes = 64L * 1024 * 1024,
            CompanionSessionFactory = companion is null
                ? null
                : _ => Interlocked.Increment(ref companionGeneration) == 1 ||
                    replacementCompanion is null
                        ? companion
                        : replacementCompanion,
            ProcessLeaseFactory = processLeaseFactory,
            IsolationPolicy = WidgetWorkerIsolationPolicy.HostTrustedJobOnly,
        }, TimeProvider.System, hooks);
    }

    private static RuntimeEnvelope Response(long requestId, string type) => new()
    {
        Type = type,
        RequestId = requestId,
        Payload = RuntimeJson.ToElement(new { }),
    };

    private static ControllerInputEvent Input(long inputSequence, long snapshotSequence) => new(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        Sequence: inputSequence,
        SnapshotSequence: snapshotSequence);

    private static WidgetDashboardGestureAuthority Authority(
        long inputSequence,
        long snapshotSequence) => new(
            WidgetMediaCapabilities.Control.CapabilityId,
            WidgetMediaCapabilities.Control.OperationId,
            inputSequence,
            snapshotSequence,
            TimeSpan.FromSeconds(2));

    private static DashboardGestureActivationRequestPayload Activation(
        long inputSequence,
        long snapshotSequence,
        string? operationId = null) => new(
            1,
            WidgetMediaCapabilities.Control.CapabilityId,
            operationId ?? WidgetMediaCapabilities.Control.OperationId,
            inputSequence,
            snapshotSequence);

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message) => True(!value, message);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task ThrowsAnyAsync(Func<Task> action)
    {
        try { await action(); }
        catch { return; }
        throw new InvalidOperationException("Expected an exception.");
    }

    private sealed class OwnershipTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        internal void Advance(TimeSpan elapsed) =>
            Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }

    private sealed class CountingLease : IDisposable
    {
        internal int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class CountingContentLease : IWidgetProcessContentLease
    {
        internal int DisposeCount { get; private set; }
        public IReadOnlyList<AppContainerAuthorityExpectedTarget> Targets { get; } = [];
        public void Dispose() => DisposeCount++;
    }

    private sealed class GatedDisposeCompanion : IWidgetProcessCompanionSession
    {
        private readonly TaskCompletionSource _dispose = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> WorkerArguments { get; } = [];
        internal bool DisposeStarted { get; private set; }
        internal int DisposeCount { get; private set; }

        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetLifecycleStateAsync(
            WidgetLifecycleState state,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            DisposeStarted = true;
            DisposeCount++;
            await _dispose.Task;
        }

        internal void ReleaseDispose() => _dispose.TrySetResult();
    }

    private sealed class TrackingCompanion : IWidgetProcessCompanionSession
    {
        private readonly TaskCompletionSource _grantRelease = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string> WorkerArguments { get; } = [];
        internal int RunCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal List<WidgetDashboardGestureAuthority> GrantedAuthorities { get; } = [];
        internal List<long> RevokedInputSequences { get; } = [];
        internal bool HoldGestureGrant { get; init; }
        internal TaskCompletionSource GrantStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Revoked { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            RunCount++;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        public Task SetLifecycleStateAsync(
            WidgetLifecycleState state,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task GrantDashboardGestureAuthorityAsync(
            WidgetDashboardGestureAuthority authority,
            CancellationToken cancellationToken = default)
        {
            GrantStarted.TrySetResult();
            if (HoldGestureGrant) await _grantRelease.Task;
            GrantedAuthorities.Add(authority);
        }

        public Task RevokeDashboardGestureAuthorityAsync(
            long inputSequence,
            CancellationToken cancellationToken = default)
        {
            RevokedInputSequences.Add(inputSequence);
            Revoked.TrySetResult();
            return Task.CompletedTask;
        }

        internal void ReleaseGrant() => _grantRelease.TrySetResult();

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
