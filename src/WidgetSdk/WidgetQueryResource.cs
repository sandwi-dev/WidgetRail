namespace WidgetRail.WidgetSdk;

/// <summary>Loading policy for a single selected immutable query.</summary>
public sealed record WidgetQueryResourceOptions<TQuery, TValue> where TQuery : notnull where TValue : notnull
{
    public required Func<TQuery, CancellationToken, ValueTask<TValue>> Load { get; init; }
    public required Func<Exception, WidgetResourceError> MapError { get; init; }
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(5);
    public WidgetOperationLifetime Lifetime { get; init; } = WidgetOperationLifetime.Active;
    public bool RetainLastGoodValue { get; init; } = true;
    public TimeProvider? TimeProvider { get; init; }
}

/// <summary>
/// One selected query backed by the standard resource state machine. Changing the
/// query retires its value and work; only the latest pending read can run after an
/// uncooperative provider drains. Queries must be immutable and use value equality.
/// This is not a multi-query cache.
/// </summary>
public sealed class WidgetQueryResource<TQuery, TValue> where TQuery : notnull where TValue : notnull
{
    private static readonly WidgetResourceSnapshot<TValue> Empty = new(WidgetResourceStatus.NotLoaded, default, null, 0) { HasValue = false };
    private sealed record Selection(TQuery Query, Func<CancellationToken, ValueTask<TValue>> Load);
    private Selection? _selection;
    private readonly WidgetResource<TValue> _resource;
    private readonly Func<TQuery, CancellationToken, ValueTask<TValue>> _load;

    internal WidgetQueryResource(string operationKey, WidgetQueryResourceOptions<TQuery, TValue> options,
        WidgetOperations operations, Action invalidate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Load);
        _load = options.Load;
        _resource = new(operationKey, new()
        {
            Load = _ => throw new InvalidOperationException("A query must be selected before loading."),
            MapError = options.MapError,
            CacheDuration = options.CacheDuration,
            Lifetime = options.Lifetime,
            RetainLastGoodValue = options.RetainLastGoodValue,
            TimeProvider = options.TimeProvider,
        }, operations, invalidate);
    }

    /// <summary>Returns empty state for any query other than the current selection.</summary>
    public WidgetResourceSnapshot<TValue> GetSnapshot(TQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var selection = Volatile.Read(ref _selection);
        if (selection is null || !EqualityComparer<TQuery>.Default.Equals(selection.Query, query)) return Empty;
        var snapshot = _resource.Snapshot;
        // Reset/reselection may race rendering. Never label another query's value
        // with the captured key; no selection lock is held during invalidation.
        return ReferenceEquals(selection, Volatile.Read(ref _selection)) ? snapshot : Empty;
    }

    /// <summary>
    /// Loads the selected query within both runtime and caller scope. Duplicate reads
    /// coalesce; force refresh retains the same query's last good value. A canceled
    /// caller scope cannot change selection or start work.
    /// </summary>
    public WidgetOperationHandle EnsureLoaded(TQuery query, CancellationToken ownerCancellationToken = default,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_resource.AdmissionGate)
        {
            if (ownerCancellationToken.IsCancellationRequested)
                return new(WidgetOperationAdmission.RejectedInactive,
                    Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Rejected)));
            if (_selection is not { } selected || !EqualityComparer<TQuery>.Default.Equals(selected.Query, query))
            {
                _resource.Reset(invalidate: false);
                Volatile.Write(ref _selection, CaptureSelection(query));
            }
            return _resource.EnsureLoaded(_selection!.Load, ownerCancellationToken, forceRefresh);
        }
    }

    // Isolate closure creation so cached EnsureLoaded calls allocate nothing.
    private Selection CaptureSelection(TQuery query) => new(query, token => _load(query, token));

    /// <summary>Retires current work while preserving the selected query and its last good state.</summary>
    public void Cancel(bool invalidate = true)
    {
        lock (_resource.AdmissionGate) _resource.Cancel(invalidate);
    }

    /// <summary>Clears the selected query, value, errors, and caller-scope registration.</summary>
    public void Reset(bool invalidate = true)
    {
        lock (_resource.AdmissionGate)
        {
            Volatile.Write(ref _selection, null);
            _resource.Reset(invalidate);
        }
    }

    public Task WhenIdleAsync(CancellationToken cancellationToken = default) => _resource.WhenIdleAsync(cancellationToken);
}

public abstract partial class Widget
{
    /// <summary>Creates a bounded read resource whose selected immutable query and caller scope own its result.</summary>
    protected WidgetQueryResource<TQuery, TValue> CreateQueryResource<TQuery, TValue>(
        string operationKey, WidgetQueryResourceOptions<TQuery, TValue> options)
        where TQuery : notnull where TValue : notnull =>
        new(operationKey, options, Operations, () =>
        {
            if (LifecycleState != WidgetLifecycleState.Destroying) Invalidate();
        });
}
