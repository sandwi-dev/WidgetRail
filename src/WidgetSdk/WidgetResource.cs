namespace WidgetRail.WidgetSdk;

/// <summary>Observable loading state for one non-paged resource.</summary>
public enum WidgetResourceStatus
{
    NotLoaded,
    Loading,
    Ready,
    Refreshing,
    Error,
}

/// <summary>Construction policy for one lifecycle-owned non-paged resource.</summary>
public sealed record WidgetResourceOptions<TValue> where TValue : notnull
{
    /// <summary>Loads one current authoritative value.</summary>
    public required Func<CancellationToken, ValueTask<TValue>> Load { get; init; }

    /// <summary>Maps provider exceptions to bounded presentation-safe data.</summary>
    public required Func<Exception, WidgetResourceError> MapError { get; init; }

    /// <summary>How long EnsureLoaded may reuse a successful value.</summary>
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Runtime lifecycle that owns every load.</summary>
    public WidgetOperationLifetime Lifetime { get; init; } = WidgetOperationLifetime.Active;

    /// <summary>Whether a refresh failure keeps the last good value visible.</summary>
    public bool RetainLastGoodValue { get; init; } = true;

    public TimeProvider? TimeProvider { get; init; }
}

/// <summary>Immutable render-facing state for one non-paged resource.</summary>
public sealed record WidgetResourceSnapshot<TValue>(
    WidgetResourceStatus Status,
    TValue? Value,
    WidgetResourceError? Error,
    long Revision) where TValue : notnull
{
    /// <summary>Whether Value is present, including a successfully loaded default value type.</summary>
    public bool HasValue { get; init; } = Status is WidgetResourceStatus.Ready or WidgetResourceStatus.Refreshing ||
        Status == WidgetResourceStatus.Error && Value is not null;
}

/// <summary>
/// Runtime-integrated read resource with bounded caching, duplicate coalescing,
/// safe errors, retry, stale-result rejection, and lifecycle cancellation.
/// Provider calls happen only when the author invokes a load method.
/// </summary>
public sealed class WidgetResource<TValue> where TValue : notnull
{
    private sealed class LoadRequest(
        long epoch,
        WidgetResourceSnapshot<TValue> before,
        Func<CancellationToken, ValueTask<TValue>> load,
        CancellationToken scope)
    {
        internal long Epoch { get; } = epoch;
        internal Func<CancellationToken, ValueTask<TValue>> Load { get; } = load;
        internal CancellationToken Scope { get; } = scope;
        internal CancellationTokenRegistration Registration;
        internal WidgetResourceSnapshot<TValue> Before { get; } = before;
        internal TaskCompletionSource<WidgetOperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _gate = new();
    private readonly object _admissionGate = new();
    private readonly string _operationKey;
    private readonly WidgetResourceOptions<TValue> _options;
    private readonly WidgetOperations _operations;
    private readonly Action _invalidate;
    private readonly TimeProvider _timeProvider;
    private WidgetResourceSnapshot<TValue> _snapshot =
        new(WidgetResourceStatus.NotLoaded, default, null, 0);
    private LoadRequest? _currentRequest;
    private TValue? _cachedValue;
    private DateTimeOffset _loadedAt;
    private bool _hasCachedValue;
    private long _epoch;

    internal WidgetResource(
        string operationKey,
        WidgetResourceOptions<TValue> options,
        WidgetOperations operations,
        Action invalidate)
    {
        StableIdentifier.Validate(operationKey, nameof(operationKey));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Load);
        ArgumentNullException.ThrowIfNull(options.MapError);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(invalidate);
        if (options.CacheDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.CacheDuration));
        if (!Enum.IsDefined(options.Lifetime))
            throw new ArgumentOutOfRangeException(nameof(options.Lifetime));

        _operationKey = operationKey;
        _options = options;
        _operations = operations;
        _invalidate = invalidate;
        _timeProvider = options.TimeProvider ?? TimeProvider.System;
    }

    public WidgetResourceSnapshot<TValue> Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    internal object AdmissionGate => _admissionGate;

    public bool IsBusy => _operations.IsBusy(_operationKey);

    /// <summary>Loads when no fresh successful value is cached.</summary>
    public WidgetOperationHandle EnsureLoaded(bool forceRefresh = false)
    {
        lock (_admissionGate)
        {
            lock (_gate)
            {
                if (!forceRefresh && HasFreshCacheLocked())
                    return Completed();
            }
            return Start();
        }
    }

    internal WidgetOperationHandle EnsureLoaded(
        Func<CancellationToken, ValueTask<TValue>> load,
        CancellationToken scope, bool forceRefresh)
    {
        lock (_admissionGate)
        {
            if (scope.IsCancellationRequested)
                return new(WidgetOperationAdmission.RejectedInactive,
                    Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Rejected)));
            bool replaceOwner;
            lock (_gate)
            {
                if (!forceRefresh && HasFreshCacheLocked()) return Completed();
                replaceOwner = _currentRequest is { } current && current.Scope != scope;
            }
            // The same query under a different owner must not join retired work.
            if (replaceOwner) Cancel();
            return Start(load, scope);
        }
    }

    /// <summary>Cancels the current read, restoring its previous state and retaining its cache.</summary>
    public void Cancel(bool invalidate = true)
    {
        bool changed = false;
        lock (_admissionGate)
        {
            lock (_gate)
            {
                _epoch++;
                if (_currentRequest is { } request)
                {
                    request.Registration.Unregister();
                    _currentRequest = null;
                    changed = SetSnapshotLocked(request.Before.Status, request.Before.Value, request.Before.Error, request.Before.HasValue);
                }
            }
            _operations.Cancel(_operationKey);
        }
        if (changed && invalidate) _invalidate();
    }

    private void CancelScope(LoadRequest request)
    {
        lock (_admissionGate)
        {
            lock (_gate)
                if (!ReferenceEquals(_currentRequest, request)) return;
            Cancel();
        }
    }

    /// <summary>Bypasses the cache and reads a fresh value.</summary>
    public WidgetOperationHandle Refresh()
    {
        lock (_admissionGate) return Start();
    }

    /// <summary>Retries after an error, or refreshes when no error is present.</summary>
    public WidgetOperationHandle Retry()
    {
        lock (_admissionGate) return Start();
    }

    /// <summary>
    /// Publishes an authoritative subscription value and cancels any older
    /// in-flight read so it cannot overwrite the event.
    /// </summary>
    public void Publish(TValue value, bool invalidate = true)
    {
        ArgumentNullException.ThrowIfNull(value);
        bool changed;
        lock (_admissionGate)
        {
            lock (_gate)
            {
                _epoch++;
                _currentRequest?.Registration.Unregister();
                _currentRequest = null;
                _cachedValue = value;
                _loadedAt = _timeProvider.GetUtcNow();
                _hasCachedValue = true;
                changed = SetSnapshotLocked(WidgetResourceStatus.Ready, value, null, hasValue: true);
            }
            _operations.Cancel(_operationKey);
        }
        if (changed && invalidate) _invalidate();
    }

    /// <summary>Cancels work and clears all value, cache, and error state.</summary>
    public void Reset(bool invalidate = true)
    {
        bool changed;
        lock (_admissionGate)
        {
            lock (_gate)
            {
                _epoch++;
                _currentRequest?.Registration.Unregister();
                _currentRequest = null;
                _cachedValue = default;
                _loadedAt = default;
                _hasCachedValue = false;
                changed = SetSnapshotLocked(
                    WidgetResourceStatus.NotLoaded, default, null, hasValue: false);
            }
            _operations.Cancel(_operationKey);
        }
        if (changed && invalidate) _invalidate();
    }

    public Task WhenIdleAsync(CancellationToken cancellationToken = default) =>
        _operations.WhenIdleAsync(_operationKey, cancellationToken);

    private WidgetOperationHandle Start(
        Func<CancellationToken, ValueTask<TValue>>? load = null, CancellationToken scope = default)
    {
        lock (_admissionGate)
        {
            LoadRequest request;
            bool changed;
            lock (_gate)
            {
                if (_currentRequest is { } duplicate)
                    return new(WidgetOperationAdmission.Joined, duplicate.Completion.Task);

                request = new(_epoch, _snapshot, load ?? _options.Load, scope);
                _currentRequest = request;
                changed = SetSnapshotLocked(
                    _snapshot.HasValue
                        ? WidgetResourceStatus.Refreshing
                        : WidgetResourceStatus.Loading,
                    _snapshot.Value,
                    null, _snapshot.HasValue);
            }

            // Duplicate requests join _currentRequest above. A Reset/Publish
            // retires that owner even if its provider has not drained yet; its
            // successor must queue, never join the canceled underlying read.
            var underlying = _operations.RunLatest(
                _operationKey,
                context => LoadCoreAsync(request, context),
                _options.Lifetime, onAdmitted: () =>
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_currentRequest, request) && scope.CanBeCanceled)
                            request.Registration = scope.Register(() => CancelScope(request));
                    }
                });
            if (changed && underlying.IsAccepted &&
                underlying.Admission != WidgetOperationAdmission.Started)
                _invalidate();
            ObserveCompletion(request, underlying);
            return new(underlying.Admission, request.Completion.Task);
        }
    }

    private async ValueTask LoadCoreAsync(
        LoadRequest request,
        WidgetOperationContext context)
    {
        var committed = false;
        try
        {
            using var linked = request.Scope.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, request.Scope) : null;
            var value = await request.Load(linked?.Token ?? context.CancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(value);
            if (request.Scope.IsCancellationRequested) CancelScope(request);
            lock (_gate)
            {
                if (!CanCommitLocked(request, context)) return;
                _cachedValue = value;
                _loadedAt = _timeProvider.GetUtcNow();
                _hasCachedValue = true;
                _currentRequest?.Registration.Unregister();
                _currentRequest = null;
                SetSnapshotLocked(WidgetResourceStatus.Ready, value, null, hasValue: true);
                committed = true;
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested || request.Scope.IsCancellationRequested)
        {
            if (request.Scope.IsCancellationRequested) CancelScope(request);
            throw;
        }
        catch (Exception exception)
        {
            lock (_gate)
                if (!CanCommitLocked(request, context)) throw;
            var error = MapError(exception);
            lock (_gate)
            {
                if (!CanCommitLocked(request, context)) throw;
                var value = _options.RetainLastGoodValue
                    ? request.Before.Value
                    : default;
                _currentRequest?.Registration.Unregister();
                _currentRequest = null;
                SetSnapshotLocked(
                    WidgetResourceStatus.Error, value, error, _options.RetainLastGoodValue && request.Before.HasValue);
                committed = true;
            }
            throw;
        }
        finally
        {
            if (!committed) RestoreCanceledRequest(request, invalidate: false);
        }
    }

    private void ObserveCompletion(
        LoadRequest request,
        WidgetOperationHandle underlying)
    {
        void Complete()
        {
            var result = underlying.Completion.GetAwaiter().GetResult();
            if (underlying.Admission == WidgetOperationAdmission.RejectedCapacity)
            {
                bool changed = false;
                lock (_gate)
                {
                    if (ReferenceEquals(_currentRequest, request) && request.Epoch == _epoch)
                    {
                        request.Registration.Unregister();
                        _currentRequest = null;
                        changed = SetSnapshotLocked(WidgetResourceStatus.Error,
                            _options.RetainLastGoodValue ? request.Before.Value : default,
                            new WidgetResourceError("resource_busy", "This resource could not start loading. Try again."),
                            _options.RetainLastGoodValue && request.Before.HasValue);
                    }
                }
                if (changed) _invalidate();
            }
            RestoreCanceledRequest(request, invalidate: !underlying.IsAccepted);
            request.Registration.Unregister();
            request.Completion.TrySetResult(result);
        }
        if (underlying.Completion.IsCompleted) Complete();
        else underlying.Completion.GetAwaiter().OnCompleted(Complete);
    }

    private void RestoreCanceledRequest(LoadRequest request, bool invalidate)
    {
        bool changed = false;
        lock (_gate)
        {
            if (ReferenceEquals(_currentRequest, request) && request.Epoch == _epoch)
            {
                _currentRequest?.Registration.Unregister();
                _currentRequest = null;
                changed = SetSnapshotLocked(
                    request.Before.Status,
                    request.Before.Value,
                    request.Before.Error, request.Before.HasValue);
            }
        }
        if (changed && invalidate) _invalidate();
    }

    private bool HasFreshCacheLocked() =>
        _snapshot.Status == WidgetResourceStatus.Ready &&
            _hasCachedValue &&
            _options.CacheDuration > TimeSpan.Zero &&
            (_options.CacheDuration == TimeSpan.MaxValue ||
             _timeProvider.GetUtcNow() - _loadedAt < _options.CacheDuration);

    private bool CanCommitLocked(
        LoadRequest request,
        WidgetOperationContext context) =>
        request.Epoch == _epoch &&
        ReferenceEquals(_currentRequest, request) &&
        !request.Scope.IsCancellationRequested && context.IsCurrent;

    private WidgetResourceError MapError(Exception exception)
    {
        try { return _options.MapError(exception) ?? WidgetResourceError.Unexpected; }
        catch { return WidgetResourceError.Unexpected; }
    }

    private bool SetSnapshotLocked(
        WidgetResourceStatus status,
        TValue? value,
        WidgetResourceError? error, bool hasValue)
    {
        if (_snapshot.HasValue == hasValue && _snapshot.Status == status && Equals(_snapshot.Value, value) &&
            Equals(_snapshot.Error, error))
            return false;
        _snapshot = new(status, value, error, _snapshot.Revision + 1) { HasValue = hasValue };
        return true;
    }

    private static WidgetOperationHandle Completed() => CachedCompletion;

    private static readonly WidgetOperationHandle CachedCompletion = new(
        WidgetOperationAdmission.Completed,
        Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Succeeded)));
}

public abstract partial class Widget
{
    /// <summary>Creates one runtime-integrated non-paged read resource.</summary>
    protected WidgetResource<TValue> CreateResource<TValue>(
        string operationKey,
        WidgetResourceOptions<TValue> options) where TValue : notnull =>
        new(operationKey, options, Operations, () =>
        {
            if (LifecycleState != WidgetLifecycleState.Destroying) Invalidate();
        });
}
