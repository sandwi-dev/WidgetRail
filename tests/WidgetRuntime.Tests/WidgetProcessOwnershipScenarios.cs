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
        session.AttachReader(Task.CompletedTask);

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
}
