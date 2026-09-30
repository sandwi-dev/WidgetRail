namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal readonly record struct SliderValueDispatch(double Value, long Generation);

/// <summary>
/// Bounded optimistic value reconciliation for one exact slider contract. The
/// caller owns identity/range, focus engagement, dispatch and monotonic timing.
/// Mirrors the native host's settling, dispatched and guarded-echo states.
/// </summary>
internal sealed class SliderValueState
{
    internal const long SettlementDelayMilliseconds = 150;
    internal const long PendingTimeoutMilliseconds = 2000;
    internal const int MaximumSentHistory = 16;
    private enum Phase { Authoritative, Settling, Dispatched, Guarded }
    private readonly record struct Sent(double Value, long Generation, long Expires);
    private readonly List<Sent> history = [];
    private readonly double tolerance;
    private Phase phase;
    private double observed;
    private double target;
    private long sequence;
    private long lastChange;
    private long expires;
    private long generation;
    private long nextGeneration;
    private bool busy, disabled;

    internal SliderValueState(double value, long sequence, double toleranceScale = 1)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (!double.IsFinite(toleranceScale)) throw new ArgumentOutOfRangeException(nameof(toleranceScale));
        observed = value;
        this.sequence = sequence;
        tolerance = Math.Max(1, Math.Abs(toleranceScale)) * 1e-9;
    }

    internal double Value => phase == Phase.Authoritative ? observed : target;
    // History still needs bounded expiry after an acknowledgement; a late echo
    // from an earlier accepted request must not move the presented thumb back.
    internal bool HasPending => phase != Phase.Authoritative || history.Count != 0;
    internal int SentHistoryCount => history.Count;
    internal bool IsCurrent(long intentGeneration) => phase == Phase.Dispatched && generation == intentGeneration;

    internal bool Observe(double value, long sequence, bool busy, bool disabled, long now)
    {
        var before = Value;
        Advance(now);
        if (!double.IsFinite(value) || sequence < this.sequence) return !Near(before, Value);
        this.busy = busy; this.disabled = disabled;
        if (sequence > this.sequence)
        {
            var priorObserved = observed;
            observed = value;
            this.sequence = sequence;
            switch (phase)
            {
                case Phase.Authoritative:
                    if (!Near(observed, priorObserved))
                    {
                        if (GuardExpiry(observed) is { } guard) Guard(priorObserved, guard);
                        else history.Clear();
                    }
                    break;
                case Phase.Settling:
                    // Unsent user motion wins over provider refreshes until
                    // settlement, unless the control becomes unavailable.
                    break;
                case Phase.Dispatched:
                    if (Near(observed, target)) phase = Phase.Authoritative;
                    else if (!Near(observed, priorObserved) && GuardExpiry(observed) is null)
                    { history.Clear(); phase = Phase.Authoritative; }
                    break;
                case Phase.Guarded:
                    if (Near(observed, target)) phase = Phase.Authoritative;
                    else if (!Near(observed, priorObserved))
                    {
                        if (GuardExpiry(observed) is { } guard) expires = guard;
                        else { history.Clear(); phase = Phase.Authoritative; }
                    }
                    break;
            }
        }
        // Busy may belong to the previous command, while a newer local intent
        // is settling. Defer that intent; only disabling the control revokes it.
        if (disabled && phase == Phase.Settling) phase = Phase.Authoritative;
        return !Near(before, Value);
    }

    internal bool Request(double requested, long now)
    {
        Advance(now);
        if (disabled || !double.IsFinite(requested) || Near(requested, Value)) return false;
        if (phase == Phase.Settling && Near(requested, observed) &&
            !history.Any(sent => !Near(sent.Value, requested)))
            phase = Phase.Authoritative;
        else
        {
            target = requested;
            lastChange = now;
            phase = Phase.Settling;
        }
        return true;
    }

    internal SliderValueDispatch? TakeDispatch(long now, bool force = false)
    {
        Advance(now);
        if (phase != Phase.Settling) return null;
        if (disabled) { phase = Phase.Authoritative; return null; }
        if (busy) return null;
        if (!force && now < Deadline(lastChange, SettlementDelayMilliseconds)) return null;
        generation = checked(++nextGeneration);
        expires = Deadline(now, PendingTimeoutMilliseconds);
        history.Add(new(target, generation, expires));
        if (history.Count > MaximumSentHistory) history.RemoveAt(0);
        phase = Phase.Dispatched;
        return new(target, generation);
    }

    internal bool Reject(long rejectedGeneration)
    {
        var index = history.FindIndex(sent => sent.Generation == rejectedGeneration);
        if (index < 0) return false;
        history.RemoveAt(index);
        if (phase == Phase.Dispatched && generation == rejectedGeneration) phase = Phase.Authoritative;
        else if (phase == Phase.Guarded)
        {
            if (GuardExpiry(observed) is { } guard) expires = guard;
            else phase = Phase.Authoritative;
        }
        return true;
    }

    internal void Reset()
    {
        history.Clear();
        phase = Phase.Authoritative;
        // Never reuse an intent ID while a rejected asynchronous dispatch may
        // still complete against this instance after a focus/lifecycle reset.
    }

    private void Advance(long now)
    {
        history.RemoveAll(sent => now >= sent.Expires);
        if (phase is Phase.Dispatched or Phase.Guarded && now >= expires) phase = Phase.Authoritative;
    }

    private void Guard(double value, long deadline) { target = value; expires = deadline; phase = Phase.Guarded; }
    private long? GuardExpiry(double value)
    {
        long? result = null;
        foreach (var sent in history)
            if (Near(sent.Value, value) && (result is null || sent.Expires > result)) result = sent.Expires;
        return result;
    }
    private bool Near(double first, double second) => Math.Abs(first - second) <= tolerance;
    private static long Deadline(long now, long delay) => now > long.MaxValue - delay ? long.MaxValue : now + delay;
}
