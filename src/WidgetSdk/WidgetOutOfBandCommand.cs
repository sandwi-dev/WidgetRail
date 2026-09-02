namespace WidgetRail.WidgetSdk;

/// <summary>The result of attempting to project an out-of-band command.</summary>
public enum WidgetOutOfBandCommandAdmission
{
    /// <summary>The projection became the one current command.</summary>
    Started,
    /// <summary>The projection replaced the previous current command.</summary>
    Replaced,
    /// <summary>A single-flight projection was rejected while another command was current.</summary>
    RejectedPending,
    /// <summary>The author rejected the request from the current immutable state.</summary>
    RejectedProjection,
}

/// <summary>The result and SDK-assigned correlation sequence for one admission.</summary>
public readonly record struct WidgetOutOfBandCommandRun(
    WidgetOutOfBandCommandAdmission Admission,
    long Sequence)
{
    public bool IsAccepted =>
        Admission is WidgetOutOfBandCommandAdmission.Started or
            WidgetOutOfBandCommandAdmission.Replaced;
}

/// <summary>The result of admitting an independent observation.</summary>
public enum WidgetOutOfBandObservationAdmission
{
    /// <summary>The observation was current and was merged into the model.</summary>
    Accepted,
    /// <summary>The observation sequence was at or behind the last admitted observation.</summary>
    RejectedStale,
    /// <summary>The observation did not describe the authority held by the current state.</summary>
    RejectedAuthority,
    /// <summary>The observation named a command that this facility does not currently own.</summary>
    RejectedCorrelation,
}

/// <summary>
/// An immutable ticket captured when an independent poll or subscription read
/// begins, so completion order cannot disguise a pre-projection observation as
/// a successor observation.
/// </summary>
public sealed class WidgetOutOfBandObservationTicket
{
    private readonly object _owner;

    internal WidgetOutOfBandObservationTicket(object owner, long sequence)
    {
        _owner = owner;
        Sequence = sequence;
    }

    public long Sequence { get; }

    internal bool IsOwnedBy(object owner) => ReferenceEquals(_owner, owner);
}

/// <summary>
/// One immutable optimistic state projection and the authority descriptor used
/// to match its eventual external confirmation.
/// </summary>
public readonly record struct WidgetOutOfBandCommandProjection<TState, TAuthority>(
    TState State,
    TAuthority Authority,
    bool ShouldStart = true,
    TimeSpan? ExpiresAfter = null)
    where TState : notnull
    where TAuthority : notnull;

/// <summary>Author policy for one externally confirmed optimistic command slot.</summary>
public sealed record WidgetOutOfBandCommandOptions<
    TState,
    TRequest,
    TObservation,
    TAuthority>
    where TState : notnull
    where TRequest : notnull
    where TObservation : notnull
    where TAuthority : notnull
{
    /// <summary>
    /// Projects state and derives immutable correlation authority from one
    /// serialized model revision. The SDK supplies the command sequence.
    /// </summary>
    public required Func<TState, TRequest, long,
        WidgetOutOfBandCommandProjection<TState, TAuthority>> Apply { get; init; }

    /// <summary>Returns the monotonic sequence carried by an observation source.</summary>
    public Func<TObservation, long>? ObservationSequence { get; init; }

    /// <summary>
    /// Returns the command sequence named by an observation, or zero when an
    /// independent poll or subscription observation names no command.
    /// </summary>
    public Func<TObservation, long> CorrelationSequence { get; init; } = static _ => 0;

    /// <summary>
    /// Confirms that an observation describes the entity currently held by the
    /// immutable model. Keep this callback quick and side-effect free.
    /// </summary>
    public required Func<TState, TObservation, bool> MatchesAuthority { get; init; }

    /// <summary>
    /// For uncorrelated poll or subscription observations, confirms that the
    /// current projection has become authoritative. Correlated observations are
    /// confirmed by their SDK-owned command sequence and do not call this hook.
    /// </summary>
    public Func<TState, TAuthority, TObservation, bool, bool> ConfirmsProjection
    {
        get;
        init;
    } = static (_, _, _, _) => false;

    /// <summary>
    /// Merges an admitted observation into current immutable state. The final
    /// argument identifies whether it confirms the current projection.
    /// </summary>
    public required Func<TState, TObservation, bool, TState> Reconcile { get; init; }

    /// <summary>Lifecycle used only by projections that request bounded expiry.</summary>
    public WidgetOperationLifetime ExpiryLifetime { get; init; } =
        WidgetOperationLifetime.State;

    /// <summary>Clock used only by projections that request bounded expiry.</summary>
    public TimeProvider? TimeProvider { get; init; }
}

/// <summary>
/// One optimistic projection whose truth arrives from an inbound event,
/// subscription, poll, or bounded expiry rather than its own awaited execution.
/// The facility owns command and observation sequences plus the single current
/// authority; immutable widget state never contains a mutable completion signal.
/// </summary>
public sealed class WidgetOutOfBandCommand<
    TState,
    TRequest,
    TObservation,
    TAuthority>
    where TState : notnull
    where TRequest : notnull
    where TObservation : notnull
    where TAuthority : notnull
{
    private sealed record Projection(
        long Sequence,
        long ObservationBoundary,
        TAuthority Authority);

    private readonly object _gate = new();
    private readonly object _observationOwner = new();
    private readonly WidgetModel<TState> _model;
    private readonly WidgetOutOfBandCommandOptions<
        TState, TRequest, TObservation, TAuthority> _options;
    private readonly WidgetTimedMutation _expiry;
    private long _nextCommandSequence;
    private long _nextObservationTicket;
    private long _lastDirectObservationSequence;
    private long _lastObservationTicket;
    private Projection? _current;

    internal WidgetOutOfBandCommand(
        WidgetModel<TState> model,
        WidgetOutOfBandCommandOptions<TState, TRequest, TObservation, TAuthority> options,
        WidgetTimedMutation expiry)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Apply);
        ArgumentNullException.ThrowIfNull(options.CorrelationSequence);
        ArgumentNullException.ThrowIfNull(options.MatchesAuthority);
        ArgumentNullException.ThrowIfNull(options.ConfirmsProjection);
        ArgumentNullException.ThrowIfNull(options.Reconcile);
        ArgumentNullException.ThrowIfNull(expiry);
        if (!Enum.IsDefined(options.ExpiryLifetime))
            throw new ArgumentOutOfRangeException(nameof(options.ExpiryLifetime));

        _model = model;
        _options = options;
        _expiry = expiry;
    }

    /// <summary>Whether one admitted projection still awaits confirmation.</summary>
    public bool IsPending
    {
        get { lock (_gate) return _current is not null; }
    }

    /// <summary>The current SDK-owned command sequence, or zero when idle.</summary>
    public long PendingSequence
    {
        get { lock (_gate) return _current?.Sequence ?? 0; }
    }

    /// <summary>Starts only when no projection is currently pending.</summary>
    public WidgetOutOfBandCommandRun Run(TRequest request) => RunCore(request, replace: false);

    /// <summary>Replaces the current projection with the newest admitted intent.</summary>
    public WidgetOutOfBandCommandRun RunLatest(TRequest request) => RunCore(request, replace: true);

    /// <summary>
    /// Captures the start boundary for one independent poll or subscription
    /// read. Complete it exactly once through the matching Observe overload.
    /// </summary>
    public WidgetOutOfBandObservationTicket BeginObservation()
    {
        lock (_gate)
            return new(_observationOwner, checked(++_nextObservationTicket));
    }

    /// <summary>
    /// Admits one external observation using the SDK-owned sequence, authority,
    /// and correlation guards, then merges it into the latest model value.
    /// </summary>
    public WidgetOutOfBandObservationAdmission Observe(TObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var sequence = _options.ObservationSequence?.Invoke(observation) ??
            throw new InvalidOperationException(
                "This command requires BeginObservation and the ticketed Observe overload.");
        return ObserveCore(observation, sequence, ticketed: false);
    }

    /// <summary>Completes one independently started poll or subscription read.</summary>
    public WidgetOutOfBandObservationAdmission Observe(
        WidgetOutOfBandObservationTicket ticket,
        TObservation observation)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(observation);
        if (!ticket.IsOwnedBy(_observationOwner))
            throw new ArgumentException(
                "The observation ticket belongs to another command facility.",
                nameof(ticket));
        return ObserveCore(observation, ticket.Sequence, ticketed: true);
    }

    private WidgetOutOfBandObservationAdmission ObserveCore(
        TObservation observation,
        long observationSequence,
        bool ticketed)
    {
        lock (_gate)
        {
            if (observationSequence <= 0)
                throw new ArgumentOutOfRangeException(nameof(observation),
                    "Observation sequences must be positive.");
            var lastObservationSequence = ticketed
                ? _lastObservationTicket
                : _lastDirectObservationSequence;
            if (observationSequence <= lastObservationSequence)
                return WidgetOutOfBandObservationAdmission.RejectedStale;

            var correlationSequence = _options.CorrelationSequence(observation);
            if (correlationSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(observation),
                    "Correlation sequences cannot be negative.");
            if (correlationSequence > 0 &&
                (_current is not { } correlated ||
                 correlated.Sequence != correlationSequence))
                return WidgetOutOfBandObservationAdmission.RejectedCorrelation;

            var current = _current;
            var confirmed = correlationSequence > 0;
            var update = _model.Update(state =>
            {
                if (!_options.MatchesAuthority(state, observation))
                    return (state, (Matched: false, Confirmed: false));
                var beganAfterProjection = ticketed && current is not null &&
                    observationSequence > current.ObservationBoundary;
                var confirms = current is null || confirmed ||
                    _options.ConfirmsProjection(
                        state, current.Authority, observation, beganAfterProjection);
                return (_options.Reconcile(state, observation, confirms),
                    (Matched: true, Confirmed: confirms));
            });
            if (!update.Result.Matched)
                return WidgetOutOfBandObservationAdmission.RejectedAuthority;

            if (ticketed) _lastObservationTicket = observationSequence;
            else _lastDirectObservationSequence = observationSequence;
            if (update.Result.Confirmed)
            {
                _current = null;
                _expiry.Cancel();
            }
            return WidgetOutOfBandObservationAdmission.Accepted;
        }
    }

    private WidgetOutOfBandCommandRun RunCore(TRequest request, bool replace)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (!replace && _current is not null)
                return new(WidgetOutOfBandCommandAdmission.RejectedPending, 0);

            var sequence = checked(_nextCommandSequence + 1);
            WidgetOutOfBandCommandProjection<TState, TAuthority> projection = default;
            var update = _model.Update(state =>
            {
                projection = _options.Apply(state, request, sequence);
                ArgumentNullException.ThrowIfNull(projection.State);
                ArgumentNullException.ThrowIfNull(projection.Authority);
                ValidateExpiry(projection.ExpiresAfter);
                return projection.State;
            });
            _nextCommandSequence = sequence;
            if (!projection.ShouldStart)
                return new(WidgetOutOfBandCommandAdmission.RejectedProjection, 0);

            var admission = _current is null
                ? WidgetOutOfBandCommandAdmission.Started
                : WidgetOutOfBandCommandAdmission.Replaced;
            _current = new(sequence, _nextObservationTicket, projection.Authority);
            _expiry.Cancel();
            if (projection.ExpiresAfter is { } delay)
                _expiry.ScheduleLatest(delay, () => Expire(sequence));
            return new(admission, sequence);
        }
    }

    private void Expire(long sequence)
    {
        lock (_gate)
        {
            if (_current?.Sequence == sequence) _current = null;
        }
    }

    private static void ValidateExpiry(TimeSpan? expiry)
    {
        if (expiry is not { } value) return;
        if (value <= TimeSpan.Zero || value > WidgetTimedMutation.MaximumDelay)
            throw new ArgumentOutOfRangeException(nameof(expiry));
    }
}

public abstract partial class Widget
{
    /// <summary>
    /// Creates one optimistic command confirmed by external observations.
    /// Construct it once and keep mutable correlation authority in this facility,
    /// not in immutable widget state.
    /// </summary>
    protected WidgetOutOfBandCommand<TState, TRequest, TObservation, TAuthority>
        CreateOutOfBandCommand<TState, TRequest, TObservation, TAuthority>(
            WidgetModel<TState> model,
            WidgetOutOfBandCommandOptions<TState, TRequest, TObservation, TAuthority> options)
        where TState : notnull
        where TRequest : notnull
        where TObservation : notnull
        where TAuthority : notnull =>
        new(model, options, CreateTimedMutation(
            options.ExpiryLifetime, options.TimeProvider));
}
