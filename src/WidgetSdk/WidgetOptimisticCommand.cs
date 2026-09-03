namespace WidgetRail.WidgetSdk;

/// <summary>Coordination policy for one optimistic command key.</summary>
public enum WidgetCommandPolicy
{
    /// <summary>Join the in-flight command and do not project a duplicate request.</summary>
    SingleFlight,
    /// <summary>Cancel stale work and retain only the newest request.</summary>
    Latest,
    /// <summary>Execute every admitted request in bounded FIFO order.</summary>
    Serial,
}

/// <summary>A safe bounded error that widget UI may retain after a command fails.</summary>
public sealed record WidgetCommandError
{
    public const int MaximumMessageLength = 256;

    public WidgetCommandError(string code, string message)
    {
        StableIdentifier.Validate(code, nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Length > MaximumMessageLength || message.Any(char.IsControl))
            throw new ArgumentException(
                $"Command error messages must contain at most {MaximumMessageLength} visible characters.",
                nameof(message));
        Code = code;
        Message = message;
    }

    public string Code { get; }
    public string Message { get; }

    public static WidgetCommandError Unexpected { get; } =
        new("command_error", "That action could not be completed. Try again.");
}

/// <summary>
/// The optimistic state and exact provider input derived from one serialized
/// model revision.
/// </summary>
public readonly record struct WidgetCommandProjection<TState, TExecution>(
    TState State,
    TExecution Execution,
    bool ShouldExecute = true)
    where TState : notnull
    where TExecution : notnull;

/// <summary>Construction policy for a lifecycle-owned optimistic command.</summary>
public sealed record WidgetOptimisticCommandOptions<
    TState,
    TRequest,
    TExecution,
    TResult>
    where TState : notnull
    where TRequest : notnull
    where TExecution : notnull
{
    /// <summary>
    /// Projects UI state and derives the provider input from the same serialized
    /// state revision. This callback runs under the model lock. Keep it quick
    /// and side-effect free; do not call the model, this command, providers, or
    /// blocking code from it.
    /// </summary>
    public required Func<TState, TRequest, WidgetCommandProjection<TState, TExecution>> Apply
    {
        get;
        init;
    }

    /// <summary>
    /// Executes the one provider mutation. The SDK never retries it. This
    /// callback runs without the model or command lock and may await. Honor
    /// <see cref="WidgetOperationContext.CancellationToken"/> and inspect
    /// <see cref="WidgetOperationContext.IsCurrent"/> before committing any
    /// provider-side work not otherwise owned by this command.
    /// </summary>
    public required Func<TExecution, WidgetOperationContext, ValueTask<TResult>> Execute
    {
        get;
        init;
    }

    /// <summary>
    /// Merges a successful result into the current model. The current value may
    /// include unrelated subscription updates that arrived during execution.
    /// This callback runs under the model lock. Keep it quick and side-effect
    /// free; do not call the model, this command, providers, or blocking code.
    /// </summary>
    public required Func<TState, TExecution, TResult, TState> Reconcile
    {
        get;
        init;
    }

    /// <summary>
    /// Removes this command's optimistic projection after cancellation without
    /// discarding unrelated changes. The second value is the first baseline for
    /// a chain of Latest replacements. This callback runs under the model lock
    /// with the same restrictions as <see cref="Apply"/>.
    /// </summary>
    public required Func<TState, TState, TExecution, TState> Rollback
    {
        get;
        init;
    }

    /// <summary>
    /// Maps provider failures to bounded presentation-safe data. This callback
    /// runs without the model or command lock; keep it bounded, side-effect
    /// free, and non-throwing. A thrown exception maps to the safe fallback.
    /// </summary>
    public Func<Exception, WidgetCommandError> MapError { get; init; } =
        _ => WidgetCommandError.Unexpected;

    /// <summary>
    /// Applies a mapped failure. By default this uses <see cref="Rollback"/>.
    /// This callback runs under the model lock with the same restrictions as
    /// <see cref="Apply"/>.
    /// </summary>
    public Func<TState, TState, TExecution, WidgetCommandError, TState>? Fail
    {
        get;
        init;
    }

    public WidgetCommandPolicy Policy { get; init; } = WidgetCommandPolicy.Latest;
    public WidgetOperationLifetime Lifetime { get; init; } = WidgetOperationLifetime.Active;
}

/// <summary>
/// Bounded optimistic mutation coordination over one immutable widget model.
/// It reuses WidgetOperations for concurrency and lifecycle ownership; it does
/// not create another controller-input queue.
/// </summary>
public sealed class WidgetOptimisticCommand<TState, TRequest, TExecution, TResult>
    where TState : notnull
    where TRequest : notnull
    where TExecution : notnull
{
    private sealed record Attempt(
        long Id,
        TExecution Execution,
        TState Baseline,
        bool ShouldExecute);

    private readonly string _key;
    private readonly WidgetModel<TState> _model;
    private readonly WidgetOptimisticCommandOptions<TState, TRequest, TExecution, TResult> _options;
    private readonly WidgetOperations _operations;
    private long _nextAttemptId;
    private long _ownerAttemptId;
    private TState _latestBaseline = default!;
    private bool _hasLatestBaseline;

    internal WidgetOptimisticCommand(
        string key,
        WidgetModel<TState> model,
        WidgetOptimisticCommandOptions<TState, TRequest, TExecution, TResult> options,
        WidgetOperations operations)
    {
        StableIdentifier.Validate(key, nameof(key));
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Apply);
        ArgumentNullException.ThrowIfNull(options.Execute);
        ArgumentNullException.ThrowIfNull(options.Reconcile);
        ArgumentNullException.ThrowIfNull(options.Rollback);
        ArgumentNullException.ThrowIfNull(options.MapError);
        ArgumentNullException.ThrowIfNull(operations);
        if (!Enum.IsDefined(options.Policy))
            throw new ArgumentOutOfRangeException(nameof(options.Policy));
        if (!Enum.IsDefined(options.Lifetime))
            throw new ArgumentOutOfRangeException(nameof(options.Lifetime));

        _key = key;
        _model = model;
        _options = options;
        _operations = operations;
    }

    public string Key => _key;
    public bool IsBusy => _operations.IsBusy(_key);

    /// <summary>
    /// Admits one request. Rejected requests never mutate the model. Latest
    /// projections are applied before this method returns; Serial projections
    /// are applied when their bounded FIFO turn begins.
    /// </summary>
    public WidgetOperationHandle Run(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var start = new TaskCompletionSource<Attempt?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = _options.Policy switch
        {
            WidgetCommandPolicy.SingleFlight => _operations.RunSingleFlight(
                _key, context => ExecuteAsync(request, start.Task, context), _options.Lifetime),
            WidgetCommandPolicy.Latest => _operations.RunLatest(
                _key,
                context => ExecuteAsync(request, start.Task, context),
                _options.Lifetime,
                () =>
                {
                    try
                    {
                        start.TrySetResult(BeginAttempt(
                            request, preserveLatestBaseline: true));
                    }
                    catch (Exception exception)
                    {
                        start.TrySetException(exception);
                    }
                }),
            WidgetCommandPolicy.Serial => _operations.RunSerial(
                _key, context => ExecuteAsync(request, start.Task, context), _options.Lifetime),
            _ => throw new ArgumentOutOfRangeException(nameof(_options.Policy)),
        };

        if (!handle.IsAccepted)
        {
            start.TrySetCanceled();
            return handle;
        }

        if (handle.Admission == WidgetOperationAdmission.Joined)
        {
            start.TrySetCanceled();
            return handle;
        }

        if (_options.Policy != WidgetCommandPolicy.Latest)
            start.TrySetResult(null);
        return handle;
    }

    private async ValueTask ExecuteAsync(
        TRequest request,
        Task<Attempt?> start,
        WidgetOperationContext context)
    {
        var attempt = await start.ConfigureAwait(false) ??
            BeginAttempt(request, preserveLatestBaseline: false);
        if (!attempt.ShouldExecute)
        {
            CommitIfOwned(attempt, static current => current);
            return;
        }
        try
        {
            var result = await _options.Execute(attempt.Execution, context)
                .ConfigureAwait(false);
            if (!context.IsCurrent) return;
            CommitIfOwned(attempt, current =>
                _options.Reconcile(current, attempt.Execution, result));
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            RollbackIfOwned(attempt);
            throw;
        }
        catch (Exception exception)
        {
            FailIfOwned(attempt, exception);
            throw;
        }
    }

    private Attempt BeginAttempt(TRequest request, bool preserveLatestBaseline)
    {
        var attemptId = Interlocked.Increment(ref _nextAttemptId);
        var update = _model.UpdateBeforePublication(
            state =>
            {
                var projection = _options.Apply(state, request);
                ArgumentNullException.ThrowIfNull(projection.State);
                ArgumentNullException.ThrowIfNull(projection.Execution);
                var baseline = preserveLatestBaseline && _hasLatestBaseline
                    ? _latestBaseline
                    : state;
                return (projection.State, new Attempt(
                    attemptId,
                    projection.Execution,
                    baseline,
                    projection.ShouldExecute));
            },
            committed =>
            {
                var attempt = committed.Result;
                _ownerAttemptId = attempt.Id;
                if (preserveLatestBaseline)
                {
                    _latestBaseline = attempt.Baseline;
                    _hasLatestBaseline = true;
                }
                else
                {
                    _latestBaseline = default!;
                    _hasLatestBaseline = false;
                }
            });
        return update.Result;
    }

    private void CommitIfOwned(Attempt attempt, Func<TState, TState> merge)
    {
        _model.UpdateBeforePublication(
            state => _ownerAttemptId == attempt.Id
                ? (merge(state), true)
                : (state, false),
            committed =>
            {
                if (committed.Result) ClearOwner();
            });
    }

    private void RollbackIfOwned(Attempt attempt) => CommitIfOwned(attempt, current =>
        _options.Rollback(current, attempt.Baseline, attempt.Execution));

    private void FailIfOwned(Attempt attempt, Exception exception)
    {
        WidgetCommandError error;
        try { error = _options.MapError(exception) ?? WidgetCommandError.Unexpected; }
        catch { error = WidgetCommandError.Unexpected; }
        CommitIfOwned(attempt, current => _options.Fail is { } fail
            ? fail(current, attempt.Baseline, attempt.Execution, error)
            : _options.Rollback(current, attempt.Baseline, attempt.Execution));
    }

    private void ClearOwner()
    {
        _ownerAttemptId = 0;
        _latestBaseline = default!;
        _hasLatestBaseline = false;
    }
}

public abstract partial class Widget
{
    /// <summary>
    /// Creates one bounded optimistic command. Construct it once in the widget
    /// constructor and route controller actions through <see cref="WidgetOptimisticCommand{TState,TRequest,TExecution,TResult}.Run"/>.
    /// </summary>
    protected WidgetOptimisticCommand<TState, TRequest, TExecution, TResult>
        CreateOptimisticCommand<TState, TRequest, TExecution, TResult>(
            string key,
            WidgetModel<TState> model,
            WidgetOptimisticCommandOptions<TState, TRequest, TExecution, TResult> options)
        where TState : notnull
        where TRequest : notnull
        where TExecution : notnull =>
        new(key, model, options, Operations);
}
