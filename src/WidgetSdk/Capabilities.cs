namespace WidgetRail.WidgetSdk;

internal sealed class WidgetCapabilityGestureContext(
    long inputSequence,
    long snapshotSequence)
{
    private int _active = 1;
    internal long InputSequence { get; } = inputSequence;
    internal long SnapshotSequence { get; } = snapshotSequence;
    internal bool IsActive => Volatile.Read(ref _active) != 0;
    internal void Deactivate() => Interlocked.Exchange(ref _active, 0);
}

/// <summary>
/// Private runtime context for the currently executing dashboard action. It is
/// deliberately absent from the widget-author API; the broker adapter reads it
/// only to bind an invocation to the host-issued authority.
/// </summary>
internal static class WidgetCapabilityInvocationContext
{
    private static readonly AsyncLocal<WidgetCapabilityGestureContext?> CurrentValue = new();

    internal static WidgetCapabilityGestureContext? Current => CurrentValue.Value;

    internal static IDisposable Enter(WidgetCapabilityGestureContext? value)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = value;
        return new Scope(previous, value);
    }

    private sealed class Scope(
        WidgetCapabilityGestureContext? previous,
        WidgetCapabilityGestureContext? current) : IDisposable
    {
        private readonly WidgetCapabilityGestureContext? _previous = previous;
        private readonly WidgetCapabilityGestureContext? _current = current;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _current?.Deactivate();
                CurrentValue.Value = _previous;
            }
        }
    }
}

internal enum WidgetActionExecutionOutcome
{
    Succeeded,
    Failed,
    Canceled,
}

internal sealed record WidgetActionExecutionTerminal(
    long ExecutionId,
    WidgetActionExecutionOutcome Outcome);

/// <summary>
/// Private execution scope for the one action currently owned by the serial
/// action queue. It is distinct from dashboard gesture authority and tracks
/// only close-on-launch capability calls that must settle before a successful
/// action terminal can release a host effect.
/// </summary>
internal static class WidgetActionInvocationContext
{
    private static readonly AsyncLocal<WidgetActionInvocationState?> CurrentValue = new();

    internal static WidgetActionInvocationState? Current => CurrentValue.Value;

    internal static IDisposable Enter(WidgetActionInvocationState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var previous = CurrentValue.Value;
        CurrentValue.Value = value;
        return new Scope(previous, value);
    }

    private sealed class Scope(
        WidgetActionInvocationState? previous,
        WidgetActionInvocationState current) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            current.Seal();
            CurrentValue.Value = previous;
        }
    }
}

internal sealed class WidgetActionInvocationState(long executionId)
{
    private readonly object _gate = new();
    private TaskCompletionSource? _closeRequestsDrained;
    private int _activeCloseRequests;
    private bool _accepting = true;
    private bool _hadCloseRequest;

    internal long ExecutionId { get; } = executionId > 0
        ? executionId
        : throw new ArgumentOutOfRangeException(nameof(executionId));

    internal bool IsActive
    {
        get { lock (_gate) return _accepting; }
    }

    internal bool HadCloseRequest
    {
        get { lock (_gate) return _hadCloseRequest; }
    }

    internal WidgetActionCloseRequestLease? TryBeginCloseRequest()
    {
        lock (_gate)
        {
            if (!_accepting) return null;
            _hadCloseRequest = true;
            _activeCloseRequests++;
            return new WidgetActionCloseRequestLease(this);
        }
    }

    internal Task Seal()
    {
        lock (_gate)
        {
            _accepting = false;
            if (_activeCloseRequests == 0) return Task.CompletedTask;
            return (_closeRequestsDrained ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private void EndCloseRequest()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            if (_activeCloseRequests <= 0)
                throw new InvalidOperationException("Action close-request ownership underflowed.");
            _activeCloseRequests--;
            if (!_accepting && _activeCloseRequests == 0)
                drained = _closeRequestsDrained;
        }
        drained?.TrySetResult();
    }

    internal sealed class WidgetActionCloseRequestLease : IDisposable
    {
        private WidgetActionInvocationState? _owner;

        internal WidgetActionCloseRequestLease(WidgetActionInvocationState owner)
        {
            _owner = owner;
            ExecutionId = owner.ExecutionId;
        }

        internal long ExecutionId { get; }

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndCloseRequest();
    }
}

/// <summary>
/// A typed, transport-neutral capability operation. Platform provider packages
/// publish reusable instances; widget authors should not construct ad-hoc IDs.
/// </summary>
public sealed record WidgetCapabilityOperation<TRequest, TResponse>(
    string CapabilityId,
    string OperationId);

/// <summary>A typed, coalesced platform event exposed by a provider package.</summary>
public sealed record WidgetCapabilityEvent<TPayload>(
    string CapabilityId,
    string EventType);

/// <summary>
/// An acknowledged platform-event subscription. A successful open means the
/// host has registered the subscription before returning, so callers may fetch
/// a current snapshot without losing events in the snapshot/subscription gap.
/// The event stream is single-consumer and coalescing semantics are defined by
/// the provider contract.
/// </summary>
public interface IWidgetCapabilitySubscription<TPayload> : IAsyncDisposable
{
    IAsyncEnumerable<TPayload> ReadAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The only OS-capability boundary visible to widget code. The implementation
/// lives in the worker bootstrap and communicates with an identity-bound host
/// broker. Transport credentials and OS objects are not exposed by this API;
/// process sandboxing is still required to prevent hostile same-process code
/// from inspecting its own launch environment.
/// </summary>
public interface IWidgetCapabilityClient
{
    bool IsAvailable { get; }

    ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens and acknowledges a subscription before returning. Widgets that
    /// combine current state with events should open first, fetch second, then
    /// drain events so changes during the fetch are reconciled afterward.
    /// </summary>
    ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compatibility stream for event-only consumers. Snapshot-plus-event
    /// consumers should prefer <see cref="OpenSubscriptionAsync{TPayload}"/>.
    /// </summary>
    IAsyncEnumerable<TPayload> SubscribeAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default) =>
        WidgetCapabilitySubscriptionCompatibility.ReadAsync(
            this, platformEvent, cancellationToken);
}

internal static class WidgetCapabilitySubscriptionCompatibility
{
    internal static async IAsyncEnumerable<TPayload> ReadAsync<TPayload>(
        IWidgetCapabilityClient client,
        WidgetCapabilityEvent<TPayload> platformEvent,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await using var subscription = await client.OpenSubscriptionAsync(
            platformEvent, cancellationToken).ConfigureAwait(false);
        await foreach (var item in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}

public sealed class WidgetHostServices
{
    internal WidgetHostServices(IWidgetCapabilityClient capabilityClient)
    {
        Capabilities = capabilityClient ?? throw new ArgumentNullException(nameof(capabilityClient));
        Audio = new WidgetAudioService(capabilityClient);
        Network = new WidgetNetworkService(capabilityClient);
        RecentActivity = new WidgetRecentActivityService(capabilityClient);
        AppLibrary = new WidgetAppLibraryService(capabilityClient);
        Power = new WidgetPowerService(capabilityClient);
        DisplayProfiles = new WidgetDisplayProfilesService(capabilityClient);
        TaskSwitcher = new WidgetTaskSwitcherService(capabilityClient);
        Media = new WidgetMediaService(capabilityClient);
        Loopback = new WidgetLoopbackHttpService(capabilityClient);
        PrivateSecrets = new WidgetPrivateSecretService(capabilityClient);
        PrivateState = new WidgetPrivateStateService(capabilityClient);
    }

    public IWidgetCapabilityClient Capabilities { get; }
    public WidgetAudioService Audio { get; }
    public WidgetNetworkService Network { get; }
    public WidgetRecentActivityService RecentActivity { get; }
    public WidgetAppLibraryService AppLibrary { get; }
    public WidgetPowerService Power { get; }
    public WidgetDisplayProfilesService DisplayProfiles { get; }
    public WidgetTaskSwitcherService TaskSwitcher { get; }
    public WidgetMediaService Media { get; }
    public WidgetLoopbackHttpService Loopback { get; }
    public WidgetPrivateSecretService PrivateSecrets { get; }
    /// <summary>
    /// Host-granted, package-scoped readable JSON state. This service is not a
    /// manifest permission and never stores authentication secrets.
    /// </summary>
    public WidgetPrivateStateService PrivateState { get; }

    internal static WidgetHostServices Unavailable { get; } =
        new(UnavailableWidgetCapabilityClient.Instance);
}

public sealed class WidgetCapabilityUnavailableException(string message) : Exception(message);

/// <summary>
/// A capability request was rejected by the authenticated host broker. Error
/// codes are stable machine-readable values; messages never contain broker
/// connection details or host filesystem paths.
/// </summary>
public sealed class WidgetCapabilityException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = string.IsNullOrWhiteSpace(errorCode)
        ? throw new ArgumentException("A capability error code is required.", nameof(errorCode))
        : errorCode;
}

internal sealed class UnavailableWidgetCapabilityClient : IWidgetCapabilityClient
{
    internal static UnavailableWidgetCapabilityClient Instance { get; } = new();
    public bool IsAvailable => false;

    public ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<TResponse>(new WidgetCapabilityUnavailableException(
            "This worker was not given an authenticated platform capability channel."));

    public ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<IWidgetCapabilitySubscription<TPayload>>(
            new WidgetCapabilityUnavailableException(
                "This worker was not given an authenticated platform capability channel."));
}
