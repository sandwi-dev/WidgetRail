namespace WidgetRail.WidgetBridge;

internal enum BridgeRevisionNotificationKind
{
    Appearance,
    Catalog,
}

/// <summary>
/// Owns the two replaceable Bridge-wide revision notifications. At most one
/// active publication and one latest pending value per kind are retained while
/// a connected native reader applies backpressure.
/// </summary>
internal sealed class BridgeRevisionNotificationLane
{
    private readonly object _gate = new();
    private Notification? _appearance;
    private Notification? _catalog;
    private long _sequence;
    private Task? _pump;
    private bool _pumpScheduled;
    private bool _closed;

    internal void Enqueue(
        BridgeRevisionNotificationKind kind,
        long revision,
        Func<CancellationToken, Task> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        if (revision <= 0) return;
        var startPump = false;
        lock (_gate)
        {
            if (_closed) return;
            var pending = kind == BridgeRevisionNotificationKind.Appearance
                ? _appearance
                : _catalog;
            if (pending is not null && pending.Revision >= revision) return;
            var notification = new Notification(++_sequence, revision, publish);
            if (kind == BridgeRevisionNotificationKind.Appearance)
                _appearance = notification;
            else
                _catalog = notification;
            if (!_pumpScheduled)
            {
                _pumpScheduled = true;
                startPump = true;
            }
        }
        if (startPump) StartPump();
    }

    internal async Task CloseAndDrainAsync()
    {
        Task? pump;
        lock (_gate)
        {
            _closed = true;
            _appearance = null;
            _catalog = null;
            pump = _pump;
            if (pump is null) _pumpScheduled = false;
        }
        if (pump is not null) await pump.ConfigureAwait(false);
    }

    private void StartPump()
    {
        lock (_gate)
        {
            if (_pump is not null || !_pumpScheduled) return;
            _pump = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        await Task.Yield();
        while (true)
        {
            Notification? item;
            lock (_gate)
            {
                item = TakeNextLocked();
                if (item is null)
                {
                    _pumpScheduled = false;
                    _pump = null;
                    return;
                }
            }
            await item.Publish(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private Notification? TakeNextLocked()
    {
        if (_appearance is null)
        {
            var pendingCatalog = _catalog;
            _catalog = null;
            return pendingCatalog;
        }
        if (_catalog is null)
        {
            var appearance = _appearance;
            _appearance = null;
            return appearance;
        }
        if (_appearance.Sequence < _catalog.Sequence)
        {
            var appearance = _appearance;
            _appearance = null;
            return appearance;
        }
        var catalog = _catalog;
        _catalog = null;
        return catalog;
    }

    private sealed record Notification(
        long Sequence,
        long Revision,
        Func<CancellationToken, Task> Publish);
}
