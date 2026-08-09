namespace GameBarAlternative.WidgetSdk;

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
    public bool HasValue => Value is not null;
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
        WidgetResourceSnapshot<TValue> before)
    {
        internal long Epoch { get; } = epoch;
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
                _currentRequest = null;
                _cachedValue = value;
                _loadedAt = _timeProvider.GetUtcNow();
                _hasCachedValue = true;
                changed = SetSnapshotLocked(WidgetResourceStatus.Ready, value, null);
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
                _currentRequest = null;
                _cachedValue = default;
                _loadedAt = default;
                _hasCachedValue = false;
                changed = SetSnapshotLocked(
                    WidgetResourceStatus.NotLoaded, default, null);
            }
            _operations.Cancel(_operationKey);
        }
        if (changed && invalidate) _invalidate();
    }

    public Task WhenIdleAsync(CancellationToken cancellationToken = default) =>
        _operations.WhenIdleAsync(_operationKey, cancellationToken);

    private WidgetOperationHandle Start()
    {
        lock (_admissionGate)
        {
            LoadRequest request;
            bool changed;
            lock (_gate)
            {
                if (_currentRequest is { } duplicate)
                    return new(WidgetOperationAdmission.Joined, duplicate.Completion.Task);

                request = new(_epoch, _snapshot);
                _currentRequest = request;
                changed = SetSnapshotLocked(
                    _snapshot.HasValue
                        ? WidgetResourceStatus.Refreshing
                        : WidgetResourceStatus.Loading,
                    _snapshot.Value,
                    null);
            }

            var underlying = _operations.RunSingleFlight(
                _operationKey,
                context => LoadCoreAsync(request, context),
                _options.Lifetime);
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
            var value = await _options.Load(context.CancellationToken).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate)
            {
                if (!CanCommitLocked(request, context)) return;
                _cachedValue = value;
                _loadedAt = _timeProvider.GetUtcNow();
                _hasCachedValue = true;
                _currentRequest = null;
                SetSnapshotLocked(WidgetResourceStatus.Ready, value, null);
                committed = true;
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (!CanCommitLocked(request, context)) throw;
                var value = _options.RetainLastGoodValue
                    ? request.Before.Value
                    : default;
                _currentRequest = null;
                SetSnapshotLocked(
                    WidgetResourceStatus.Error, value, MapError(exception));
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
        underlying.Completion.GetAwaiter().OnCompleted(() =>
        {
            var result = underlying.Completion.GetAwaiter().GetResult();
            RestoreCanceledRequest(request, invalidate: !underlying.IsAccepted);
            request.Completion.TrySetResult(result);
        });
    }

    private void RestoreCanceledRequest(LoadRequest request, bool invalidate)
    {
        bool changed = false;
        lock (_gate)
        {
            if (ReferenceEquals(_currentRequest, request) && request.Epoch == _epoch)
            {
                _currentRequest = null;
                changed = SetSnapshotLocked(
                    request.Before.Status,
                    request.Before.Value,
                    request.Before.Error);
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
        context.IsCurrent;

    private WidgetResourceError MapError(Exception exception)
    {
        try { return _options.MapError(exception) ?? WidgetResourceError.Unexpected; }
        catch { return WidgetResourceError.Unexpected; }
    }

    private bool SetSnapshotLocked(
        WidgetResourceStatus status,
        TValue? value,
        WidgetResourceError? error)
    {
        if (_snapshot.Status == status && Equals(_snapshot.Value, value) &&
            Equals(_snapshot.Error, error))
            return false;
        _snapshot = new(status, value, error, _snapshot.Revision + 1);
        return true;
    }

    private static WidgetOperationHandle Completed() => new(
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
