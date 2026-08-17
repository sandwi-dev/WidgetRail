using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

internal sealed class WidgetDashboardGestureReservations(
    TimeProvider timeProvider,
    TimeSpan maximumReservationLifetime)
{
    private readonly object _gate = new();
    private readonly Dictionary<DashboardGestureKey, PendingDashboardGesture> _pending = [];
    private readonly TimeProvider _timeProvider = timeProvider ??
        throw new ArgumentNullException(nameof(timeProvider));

    internal int Count
    {
        get { lock (_gate) return _pending.Count; }
    }

    internal void Reserve(
        ControllerInputEvent input,
        WidgetDashboardGestureAuthority authority)
    {
        if (input.Context != ControllerInputContext.DashboardQuickAction ||
            input.Origin != ControllerInputOrigin.PhysicalController ||
            input.Sequence != authority.InputSequence ||
            input.SnapshotSequence != authority.SnapshotSequence ||
            authority.InputSequence <= 0 || authority.SnapshotSequence <= 0 ||
            string.IsNullOrWhiteSpace(authority.CapabilityId) ||
            string.IsNullOrWhiteSpace(authority.OperationId) ||
            authority.ValidFor <= TimeSpan.Zero ||
            authority.ValidFor > PlatformCapabilityBroker.MaximumDashboardGestureLifetime)
            throw new WidgetProcessException(
                "Dashboard gesture authority does not match its controller input.");

        var key = new DashboardGestureKey(
            authority.InputSequence, authority.SnapshotSequence);
        lock (_gate)
        {
            var nowTimestamp = _timeProvider.GetTimestamp();
            PurgeExpiredNoLock(nowTimestamp);
            if (_pending.Count >= Widget.ControllerActionQueueCapacity)
                throw new WidgetProcessException(
                    "Dashboard gesture reservation capacity was reached.");
            if (!_pending.TryAdd(key, new PendingDashboardGesture(authority, nowTimestamp)))
                throw new WidgetProcessException(
                    "Dashboard gesture authority was already reserved.");
        }
    }

    internal WidgetDashboardGestureAuthority? Take(
        DashboardGestureActivationRequestPayload activation)
    {
        var key = new DashboardGestureKey(
            activation.InputSequence, activation.SnapshotSequence);
        lock (_gate)
        {
            PurgeExpiredNoLock(_timeProvider.GetTimestamp());
            if (!_pending.TryGetValue(key, out var pending) ||
                !string.Equals(
                    pending.Authority.CapabilityId, activation.CapabilityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    pending.Authority.OperationId, activation.OperationId,
                    StringComparison.Ordinal))
                return null;

            _pending.Remove(key);
            return pending.Authority;
        }
    }

    internal void Remove(long inputSequence, long snapshotSequence)
    {
        lock (_gate)
            _pending.Remove(new DashboardGestureKey(inputSequence, snapshotSequence));
    }

    internal void Clear()
    {
        lock (_gate) _pending.Clear();
    }

    private void PurgeExpiredNoLock(long nowTimestamp)
    {
        foreach (var expired in _pending
                     .Where(item => _timeProvider.GetElapsedTime(
                         item.Value.ReservedAtTimestamp, nowTimestamp) >=
                         maximumReservationLifetime)
                     .Select(item => item.Key)
                     .ToArray())
            _pending.Remove(expired);
    }

    private readonly record struct DashboardGestureKey(
        long InputSequence,
        long SnapshotSequence);

    private sealed record PendingDashboardGesture(
        WidgetDashboardGestureAuthority Authority,
        long ReservedAtTimestamp);
}
