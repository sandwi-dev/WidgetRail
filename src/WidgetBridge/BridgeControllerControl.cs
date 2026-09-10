using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;

namespace WidgetRail.WidgetBridge;

internal sealed class BridgeControllerControl(PlatformSettingsStore? store, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private ControllerControlStatus _status = ControllerControlStatus.Unavailable;
    private long _reportedAt;
    private bool _hasReport;
    private ControllerSettings? _issuedPreference;
    private ControllerSettings? _reportedPreference;
    private ControllerSettings? _pendingPreference;

    internal ControllerControlStatus Status
    {
        get
        {
            lock (_gate)
                return CurrentStatusLocked();
        }
    }

    private ControllerControlStatus CurrentStatusLocked()
    {
        if (!_hasReport || _time.GetElapsedTime(_reportedAt) > TimeSpan.FromSeconds(5))
            return ControllerControlStatus.Unavailable;
        return _pendingPreference is not null
            ? _status with { State = _pendingPreference.ExclusiveControl ? ControllerControlState.Starting : ControllerControlState.Stopping }
            : _status;
    }

    internal void Report(ControllerControlStatus status)
    {
        if (status is null || !Enum.IsDefined(status.State))
            throw new BridgeProtocolException("Invalid controller status.");
        lock (_gate)
        {
            _status = status;
            _reportedAt = _time.GetTimestamp();
            _hasReport = true;
            // The native host reports first, receives the next preference, then
            // applies it before its next serialized exchange. This report thus
            // belongs to the revision issued by the preceding exchange.
            _reportedPreference = _issuedPreference;
            if (_pendingPreference == _reportedPreference) _pendingPreference = null;
        }
    }

    internal async ValueTask<ControllerSettings> ReadPreferenceAsync(CancellationToken cancellationToken)
    {
        ControllerSettings? pendingAtStart;
        lock (_gate) pendingAtStart = _pendingPreference;
        var preference = store is null ? new() :
            (await store.LoadAsync(cancellationToken).ConfigureAwait(false)).Controllers;
        lock (_gate)
        {
            _issuedPreference = preference;
            // A reset can supersede a pending request and restart its revision.
            // Do not overwrite a newer request that arrived during the read.
            if (pendingAtStart is not null && _pendingPreference == pendingAtStart && preference != pendingAtStart)
                _pendingPreference = preference;
        }
        return preference;
    }

    internal async ValueTask<ControllerControlResult> SetAsync(bool enabled, CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await SetCoreAsync(enabled, cancellationToken).ConfigureAwait(false); }
        finally { _mutationGate.Release(); }
    }

    private async ValueTask<ControllerControlResult> SetCoreAsync(bool enabled, CancellationToken cancellationToken)
    {
        ControllerControlStatus status;
        bool canEnable;
        lock (_gate)
        {
            status = CurrentStatusLocked();
            canEnable = _hasReport && _time.GetElapsedTime(_reportedAt) <= TimeSpan.FromSeconds(5) && _status.CanEnable;
        }
        if (store is null || enabled && !canEnable)
            return new(false, status);
        try
        {
            var saved = await store.UpdateAsync(current => current with
            {
                Controllers = current.Controllers with
                {
                    ExclusiveControl = enabled,
                    Revision = checked(current.Controllers.Revision + 1),
                },
            }, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _pendingPreference = _reportedPreference == saved.Controllers ? null : saved.Controllers;
                // Do not renew native readiness with a local preference write.
                return new(true, _pendingPreference is null ? CurrentStatusLocked() :
                    status with { State = enabled ? ControllerControlState.Starting : ControllerControlState.Stopping });
            }
        }
        catch (Exception exception) when (exception is PlatformSettingsException or IOException or UnauthorizedAccessException)
        {
            return new(false, status);
        }
    }
}
