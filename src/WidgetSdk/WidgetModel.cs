namespace WidgetRail.WidgetSdk;

/// <summary>An immutable point-in-time view of widget-owned model state.</summary>
public readonly record struct WidgetModelSnapshot<TState>(TState Value, long Revision)
    where TState : notnull;

/// <summary>The result of one serialized model update.</summary>
public readonly record struct WidgetModelUpdate<TState>(
    TState Previous,
    TState Current,
    long Revision,
    bool Changed) where TState : notnull;

/// <summary>
/// The result of one serialized model update that also derives an operation input
/// from the exact state revision that was committed.
/// </summary>
public readonly record struct WidgetModelUpdate<TState, TResult>(
    TState Previous,
    TState Current,
    TResult Result,
    long Revision,
    bool Changed) where TState : notnull;

/// <summary>Describes one committed transition observed after its lock is released.</summary>
public sealed class WidgetModelChangedEventArgs<TState>(
    WidgetModelSnapshot<TState> previous,
    WidgetModelSnapshot<TState> current) : EventArgs where TState : notnull
{
    public WidgetModelSnapshot<TState> Previous { get; } = previous;
    public WidgetModelSnapshot<TState> Current { get; } = current;
}

/// <summary>
/// A small serialized state container for immutable widget state. Successful
/// changes publish one invalidation; equal replacements publish none.
/// </summary>
public sealed class WidgetModel<TState> where TState : notnull
{
    private readonly object _gate = new();
    private readonly IEqualityComparer<TState> _comparer;
    private readonly Action _invalidate;
    private TState _value;
    private long _revision;

    internal WidgetModel(
        TState initialValue,
        IEqualityComparer<TState>? comparer,
        Action invalidate)
    {
        ArgumentNullException.ThrowIfNull(initialValue);
        ArgumentNullException.ThrowIfNull(invalidate);
        _value = initialValue;
        _comparer = comparer ?? EqualityComparer<TState>.Default;
        _invalidate = invalidate;
    }

    /// <summary>
    /// Creates a model without a runtime widget invalidation owner for isolated
    /// unit tests. Changed-state observers still publish normally.
    /// </summary>
    public static WidgetModel<TState> CreateForTesting(
        TState initialValue,
        IEqualityComparer<TState>? comparer = null) =>
        new(initialValue, comparer, static () => { });

    /// <summary>
    /// Observes committed transitions. Handlers are diagnostic observers: an
    /// exception from one handler is contained and does not block other handlers.
    /// </summary>
    public event EventHandler<WidgetModelChangedEventArgs<TState>>? Changed;

    /// <summary>Returns the current immutable state value.</summary>
    public TState Value
    {
        get
        {
            lock (_gate) return _value;
        }
    }

    /// <summary>Returns the state and revision from one atomic read.</summary>
    public WidgetModelSnapshot<TState> Snapshot
    {
        get
        {
            lock (_gate) return new(_value, _revision);
        }
    }

    /// <summary>Replaces the current state if it is not equal to the existing value.</summary>
    public WidgetModelUpdate<TState> Set(TState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Commit(_ => value);
    }

    /// <summary>
    /// Serializes an immutable update against the latest committed value.
    /// The updater runs while the model lock is held and must remain quick and
    /// side-effect free.
    /// </summary>
    public WidgetModelUpdate<TState> Update(Func<TState, TState> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);
        return Commit(updater);
    }

    /// <summary>
    /// Atomically commits state and derives a result from the same prior snapshot.
    /// This is useful for producing an operation input without rereading mutable
    /// widget fields after the update.
    /// </summary>
    public WidgetModelUpdate<TState, TResult> Update<TResult>(
        Func<TState, (TState State, TResult Result)> updater)
        => UpdateCore(updater, beforePublication: null);

    /// <summary>
    /// Commits facility metadata after the model value but before synchronous
    /// invalidation and change observers can see that value. This is internal:
    /// widget authors still compose independent owners through public models.
    /// </summary>
    internal WidgetModelUpdate<TState, TResult> UpdateBeforePublication<TResult>(
        Func<TState, (TState State, TResult Result)> updater,
        Action<WidgetModelUpdate<TState, TResult>> beforePublication)
    {
        ArgumentNullException.ThrowIfNull(updater);
        ArgumentNullException.ThrowIfNull(beforePublication);
        return UpdateCore(updater, beforePublication);
    }

    private WidgetModelUpdate<TState, TResult> UpdateCore<TResult>(
        Func<TState, (TState State, TResult Result)> updater,
        Action<WidgetModelUpdate<TState, TResult>>? beforePublication)
    {
        ArgumentNullException.ThrowIfNull(updater);

        WidgetModelUpdate<TState, TResult> result;
        WidgetModelChangedEventArgs<TState>? changed = null;
        lock (_gate)
        {
            var previous = _value;
            var mutation = updater(previous);
            ArgumentNullException.ThrowIfNull(mutation.State);
            var didChange = !_comparer.Equals(previous, mutation.State);
            if (didChange)
            {
                _value = mutation.State;
                _revision++;
                changed = new(
                    new(previous, _revision - 1),
                    new(_value, _revision));
            }
            result = new(previous, _value, mutation.Result, _revision, didChange);
            beforePublication?.Invoke(result);
        }

        Publish(changed);
        return result;
    }

    private WidgetModelUpdate<TState> Commit(Func<TState, TState> updater)
    {
        WidgetModelUpdate<TState> result;
        WidgetModelChangedEventArgs<TState>? changed = null;
        lock (_gate)
        {
            var previous = _value;
            var current = updater(previous);
            ArgumentNullException.ThrowIfNull(current);
            var didChange = !_comparer.Equals(previous, current);
            if (didChange)
            {
                _value = current;
                _revision++;
                changed = new(
                    new(previous, _revision - 1),
                    new(_value, _revision));
            }
            result = new(previous, _value, _revision, didChange);
        }

        Publish(changed);
        return result;
    }

    private void Publish(WidgetModelChangedEventArgs<TState>? args)
    {
        if (args is null) return;
        _invalidate();
        if (Changed?.GetInvocationList() is { } subscriptions)
        {
            foreach (var subscription in subscriptions)
            {
                try
                {
                    ((EventHandler<WidgetModelChangedEventArgs<TState>>)subscription)(
                        this, args);
                }
                catch
                {
                    // Observers cannot prevent the committed state from rendering.
                }
            }
        }
    }
}

public abstract partial class Widget
{
    /// <summary>
    /// Creates a runtime-integrated immutable state model. Construct models once
    /// in the widget constructor and treat every state value as immutable.
    /// </summary>
    protected WidgetModel<TState> CreateModel<TState>(
        TState initialValue,
        IEqualityComparer<TState>? comparer = null) where TState : notnull =>
        new(initialValue, comparer, () =>
        {
            if (LifecycleState != WidgetLifecycleState.Destroying) Invalidate();
        });
}
