using System.Runtime.CompilerServices;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// Builds deterministic, transport-free host services for widget unit tests.
/// Production workers receive services only from <c>WidgetWorkerBootstrap</c>.
/// </summary>
public sealed class WidgetTestHostServicesBuilder
{
    private readonly Dictionary<(string Capability, string Operation), ITestOperationHandler>
        _operations = [];
    private readonly Dictionary<(string Capability, string Event), ITestEventHandler>
        _events = [];

    /// <summary>Adds a constant typed response for an operation.</summary>
    public WidgetTestHostServicesBuilder WithResponse<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return WithHandler(
            operation,
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(response);
            });
    }

    /// <summary>Adds a typed asynchronous operation handler.</summary>
    public WidgetTestHostServicesBuilder WithHandler<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(handler);
        ValidateContract(operation.CapabilityId, operation.OperationId);
        if (!_operations.TryAdd(
                (operation.CapabilityId, operation.OperationId),
                new TestOperationHandler<TRequest, TResponse>(handler)))
            throw new InvalidOperationException(
                $"A test handler already exists for operation '{operation.OperationId}'.");
        return this;
    }

    /// <summary>Adds a finite, deterministic typed event sequence.</summary>
    public WidgetTestHostServicesBuilder WithEvents<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        IEnumerable<TPayload> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var snapshot = events.ToArray();
        if (snapshot.Any(item => item is null))
            throw new ArgumentException("Test events cannot contain null values.", nameof(events));
        return WithEventStream(platformEvent, cancellationToken => Enumerate(snapshot, cancellationToken));
    }

    /// <summary>Adds a typed asynchronous event source.</summary>
    public WidgetTestHostServicesBuilder WithEventStream<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        Func<CancellationToken, IAsyncEnumerable<TPayload>> streamFactory)
    {
        ArgumentNullException.ThrowIfNull(platformEvent);
        ArgumentNullException.ThrowIfNull(streamFactory);
        ValidateContract(platformEvent.CapabilityId, platformEvent.EventType);
        if (!_events.TryAdd(
                (platformEvent.CapabilityId, platformEvent.EventType),
                new TestEventHandler<TPayload>(streamFactory)))
            throw new InvalidOperationException(
                $"A test stream already exists for event '{platformEvent.EventType}'.");
        return this;
    }

    /// <summary>
    /// Adds the public package-scoped state service backed by a deterministic
    /// in-memory fixture. The fixture can simulate another host writer between
    /// widget calls to exercise compare-and-exchange recovery.
    /// </summary>
    public WidgetTestHostServicesBuilder WithPrivateState(
        WidgetTestPrivateState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return WithHandler(WidgetPrivateStateCapabilities.Read, state.ReadAsync)
            .WithHandler(WidgetPrivateStateCapabilities.Write, state.WriteAsync)
            .WithHandler(WidgetPrivateStateCapabilities.Clear, state.ClearAsync);
    }

    /// <summary>Creates an immutable service snapshot for one or more tests.</summary>
    public WidgetHostServices Build() => new(new TestCapabilityClient(
        new Dictionary<(string, string), ITestOperationHandler>(_operations),
        new Dictionary<(string, string), ITestEventHandler>(_events)));

    private static void ValidateContract(string capabilityId, string memberId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId) || string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException("Capability test contracts require non-empty IDs.");
    }

    private static async IAsyncEnumerable<TPayload> Enumerate<TPayload>(
        IReadOnlyList<TPayload> values,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return value;
            await Task.Yield();
        }
    }

    private interface ITestOperationHandler
    {
        ValueTask<object?> InvokeAsync(object request, CancellationToken cancellationToken);
    }

    private sealed class TestOperationHandler<TRequest, TResponse>(
        Func<TRequest, CancellationToken, ValueTask<TResponse>> handler) : ITestOperationHandler
    {
        public async ValueTask<object?> InvokeAsync(
            object request,
            CancellationToken cancellationToken)
        {
            if (request is not TRequest typedRequest)
                throw ContractMismatch();
            return await handler(typedRequest, cancellationToken).ConfigureAwait(false);
        }
    }

    private interface ITestEventHandler
    {
        object Open(CancellationToken cancellationToken);
    }

    private sealed class TestEventHandler<TPayload>(
        Func<CancellationToken, IAsyncEnumerable<TPayload>> streamFactory) : ITestEventHandler
    {
        public object Open(CancellationToken cancellationToken) =>
            new TestCapabilitySubscription<TPayload>(streamFactory, cancellationToken);
    }

    private sealed class TestCapabilitySubscription<TPayload> :
        IWidgetCapabilitySubscription<TPayload>
    {
        private readonly CancellationTokenSource _lifetime;
        private readonly IAsyncEnumerable<TPayload> _stream;
        private int _readerStarted;
        private int _disposed;

        internal TestCapabilitySubscription(
            Func<CancellationToken, IAsyncEnumerable<TPayload>> streamFactory,
            CancellationToken openCancellationToken)
        {
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(openCancellationToken);
            // Calling the factory is the test-host subscription acknowledgement.
            // Custom factories must register their source synchronously before
            // returning the stream, matching the production open contract.
            _stream = streamFactory(_lifetime.Token)
                ?? throw new InvalidOperationException("The test event factory returned no stream.");
        }

        public IAsyncEnumerable<TPayload> ReadAllAsync(
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Interlocked.Exchange(ref _readerStarted, 1) != 0)
                throw new InvalidOperationException("A capability subscription supports one event reader.");
            return ReadAsync(_stream, _lifetime.Token, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _lifetime.Cancel();
                _lifetime.Dispose();
            }
            return ValueTask.CompletedTask;
        }

        private static async IAsyncEnumerable<TPayload> ReadAsync(
            IAsyncEnumerable<TPayload> stream,
            CancellationToken lifetime,
            [EnumeratorCancellation] CancellationToken readCancellation)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                lifetime, readCancellation);
            await foreach (var item in stream.WithCancellation(linked.Token).ConfigureAwait(false))
                yield return item;
        }
    }

    private sealed class TestCapabilityClient(
        IReadOnlyDictionary<(string Capability, string Operation), ITestOperationHandler> operations,
        IReadOnlyDictionary<(string Capability, string Event), ITestEventHandler> events)
        : IWidgetCapabilityClient
    {
        public bool IsAvailable => true;

        public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
            WidgetCapabilityOperation<TRequest, TResponse> operation,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(request);
            if (!operations.TryGetValue(
                    (operation.CapabilityId, operation.OperationId), out var handler))
                throw Missing("operation", operation.OperationId);
            var response = await handler.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            if (response is not TResponse typedResponse)
                throw ContractMismatch();
            return typedResponse;
        }

        public ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(
            WidgetCapabilityEvent<TPayload> platformEvent,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(platformEvent);
            if (!events.TryGetValue(
                    (platformEvent.CapabilityId, platformEvent.EventType), out var handler))
                throw Missing("event", platformEvent.EventType);
            if (handler.Open(cancellationToken) is not IWidgetCapabilitySubscription<TPayload> subscription)
                throw ContractMismatch();
            return ValueTask.FromResult(subscription);
        }
    }

    private static WidgetCapabilityException Missing(string memberKind, string memberId) =>
        new(
            "test_handler_missing",
            $"No test {memberKind} is configured for '{memberId}'.");

    private static WidgetCapabilityException ContractMismatch() =>
        new(
            "test_contract_mismatch",
            "The configured test capability contract does not match the requested type.");
}

/// <summary>Deterministic in-memory fixture for <see cref="WidgetPrivateStateService"/>.</summary>
public sealed class WidgetTestPrivateState
{
    private readonly object _gate = new();
    private string? _json;
    private long _revision;

    public WidgetTestPrivateState(string? initialJson = null, long initialRevision = 0)
    {
        if (initialRevision < 0 || initialJson is not null && initialRevision == 0)
            throw new ArgumentOutOfRangeException(nameof(initialRevision));
        _json = initialJson is null
            ? null
            : WidgetPrivateStateService.CanonicalizeForHost(initialJson);
        _revision = initialRevision;
    }

    public long Revision { get { lock (_gate) return _revision; } }
    public string? Json { get { lock (_gate) return _json; } }

    public void SimulateExternalWriteJson(string json)
    {
        var canonical = WidgetPrivateStateService.CanonicalizeForHost(json);
        lock (_gate)
        {
            if (_revision == long.MaxValue) throw new InvalidOperationException("Revision exhausted.");
            _json = canonical;
            _revision++;
        }
    }

    public void SimulateExternalClear()
    {
        lock (_gate)
        {
            if (_revision == long.MaxValue) throw new InvalidOperationException("Revision exhausted.");
            _json = null;
            _revision++;
        }
    }

    internal ValueTask<WidgetPrivateStateTransportSnapshot> ReadAsync(
        WidgetCapabilityQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(new WidgetPrivateStateTransportSnapshot(
                _json is not null,
                _json is null ? null : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_json)),
                _revision));
    }

    internal ValueTask<WidgetPrivateStateTransportMutation> WriteAsync(
        WriteWidgetPrivateStateTransportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(request.CanonicalJsonBase64));
        lock (_gate)
        {
            DemandRevision(request.ExpectedRevision);
            if (_revision == long.MaxValue) throw new WidgetCapabilityException(
                "state_revision_exhausted", "Test private state revision is exhausted.");
            _json = json;
            return ValueTask.FromResult(new WidgetPrivateStateTransportMutation(++_revision));
        }
    }

    internal ValueTask<WidgetPrivateStateTransportMutation> ClearAsync(
        ClearWidgetPrivateStateTransportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            DemandRevision(request.ExpectedRevision);
            if (_revision == long.MaxValue) throw new WidgetCapabilityException(
                "state_revision_exhausted", "Test private state revision is exhausted.");
            _json = null;
            return ValueTask.FromResult(new WidgetPrivateStateTransportMutation(++_revision));
        }
    }

    private void DemandRevision(long? expected)
    {
        if (expected is { } value && value != _revision)
            throw new WidgetCapabilityException(
                "state_conflict", "Test private state changed before this update.");
    }
}

/// <summary>
/// Attaches test-built services through the same one-time, pre-creation gate
/// used by production workers.
/// </summary>
public static class WidgetTestHost
{
    /// <summary>
    /// Attaches services and returns the widget for fluent test setup. A second
    /// attachment, or attachment after lifecycle creation, always fails.
    /// </summary>
    public static TWidget Attach<TWidget>(TWidget widget, WidgetHostServices services)
        where TWidget : Widget
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(services);
        widget.AttachHostServices(services);
        return widget;
    }

    /// <summary>Runs the one-time Created to Background transition.</summary>
    public static ValueTask InitializeAsync(
        Widget widget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return widget.InitializeAsync(cancellationToken);
    }

    /// <summary>
    /// Drives a host-valid lifecycle transition in a unit test. Created and
    /// Destroying remain runtime-owned and are rejected as transition targets.
    /// </summary>
    public static ValueTask SetLifecycleStateAsync(
        Widget widget,
        WidgetLifecycleState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return widget.SetLifecycleStateAsync(state, cancellationToken);
    }

    /// <summary>Runs terminal bounded-cleanup hooks in a unit test.</summary>
    public static ValueTask DestroyAsync(
        Widget widget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return widget.DestroyAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a deterministic host for package-authored pinned-layout selection
    /// and input without creating a native window or a second lifecycle owner.
    /// </summary>
    public static WidgetPinnedLayoutTestHost CreatePinnedLayoutHost(
        Widget widget,
        string widgetInstanceId,
        long initialSequence = 1)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return new WidgetPinnedLayoutTestHost(
            widget, widgetInstanceId, initialSequence);
    }
}

/// <summary>
/// Drives the existing pinned-layout notification and input ingress against
/// deterministic snapshots produced by one widget instance.
/// </summary>
public sealed class WidgetPinnedLayoutTestHost
{
    private readonly Widget _widget;
    private readonly string _widgetInstanceId;
    private long _sequence;

    internal WidgetPinnedLayoutTestHost(
        Widget widget,
        string widgetInstanceId,
        long initialSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetInstanceId);
        if (initialSequence <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(initialSequence), initialSequence,
                "A positive initial snapshot sequence is required.");
        _widget = widget;
        _widgetInstanceId = widgetInstanceId;
        _sequence = initialSequence;
        CurrentSnapshot = _widget.RenderSnapshot(_widgetInstanceId, _sequence);
    }

    /// <summary>The latest exact snapshot used for selection and action authority.</summary>
    public ViewSnapshot CurrentSnapshot { get; private set; }

    /// <summary>The currently selected package-authored layout, or null for Full widget.</summary>
    public string? SelectedLayoutId { get; private set; }

    /// <summary>Selects one current package-authored layout.</summary>
    public async ValueTask<bool> SelectAsync(
        string layoutId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutId);
        if (string.Equals(layoutId,
                PinnedSurfaceContract.FullWidgetLayoutId, StringComparison.Ordinal))
            return await RevokeAsync(cancellationToken).ConfigureAwait(false);
        if (!CurrentSnapshot.PinnedLayouts.Any(layout =>
                string.Equals(layout.Id, layoutId, StringComparison.Ordinal)))
            return false;
        if (SelectedLayoutId is { } selected &&
            !string.Equals(selected, layoutId, StringComparison.Ordinal) &&
            !await RevokeAsync(cancellationToken).ConfigureAwait(false))
            return false;

        var handled = await _widget.OnControllerInputAsync(
                SelectionInput(layoutId, selected: true), cancellationToken)
            .ConfigureAwait(false);
        if (handled) SelectedLayoutId = layoutId;
        return handled;
    }

    /// <summary>Restores one persisted package-authored layout selection.</summary>
    public ValueTask<bool> RestoreAsync(
        string layoutId,
        CancellationToken cancellationToken = default) =>
        SelectAsync(layoutId, cancellationToken);

    /// <summary>Revokes the current package-authored layout and returns to Full widget.</summary>
    public async ValueTask<bool> RevokeAsync(
        CancellationToken cancellationToken = default)
    {
        if (SelectedLayoutId is not { } selected) return true;
        var handled = await _widget.OnControllerInputAsync(
                SelectionInput(selected, selected: false), cancellationToken)
            .ConfigureAwait(false);
        if (handled) SelectedLayoutId = null;
        return handled;
    }

    /// <summary>
    /// Publishes the widget's next immutable snapshot and revokes a selected
    /// layout when that layout no longer exists in the replacement.
    /// </summary>
    public async ValueTask<ViewSnapshot> ReplaceSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CurrentSnapshot = _widget.RenderSnapshot(
            _widgetInstanceId, checked(++_sequence));
        if (SelectedLayoutId is { } selected &&
            !CurrentSnapshot.PinnedLayouts.Any(layout =>
                string.Equals(layout.Id, selected, StringComparison.Ordinal)))
            await RevokeAsync(cancellationToken).ConfigureAwait(false);
        return CurrentSnapshot;
    }

    /// <summary>Routes one controller input against the selected projection root.</summary>
    public ValueTask<bool> RouteActionAsync(
        ControllerButton button,
        string focusedElementId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(focusedElementId);
        if (SelectedLayoutId is not { } selected) return ValueTask.FromResult(false);
        var layout = CurrentSnapshot.PinnedLayouts.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, selected, StringComparison.Ordinal));
        if (layout?.Root is null || string.IsNullOrWhiteSpace(layout.ActiveInputScopeId))
            return ValueTask.FromResult(false);
        return _widget.OnControllerInputAsync(new ControllerInputEvent(
            button,
            phase,
            ControllerInputContext.PinnedSurface,
            focusedElementId,
            ActiveInputScopeId: layout.ActiveInputScopeId,
            SnapshotSequence: CurrentSnapshot.Sequence)
        {
            PinnedLayoutId = selected,
        }, cancellationToken);
    }

    private ControllerInputEvent SelectionInput(string layoutId, bool selected) => new(
        ControllerButton.View,
        ControllerEventPhase.Pressed,
        ControllerInputContext.PinnedLayoutSelection,
        SnapshotSequence: CurrentSnapshot.Sequence)
    {
        PinnedLayoutId = layoutId,
        IsPinnedLayoutSelected = selected,
    };
}
