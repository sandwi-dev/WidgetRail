using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;

namespace WidgetRail.WidgetBridge;

internal sealed class BridgeControllerControl(PlatformSettingsStore? store, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private ControllerControlStatus _status = ControllerControlStatus.Unavailable;
    private long _reportedAt;
    private bool _hasReport;

    internal ControllerControlStatus Status
    {
        get
        {
            lock (_gate)
                return _hasReport && _time.GetElapsedTime(_reportedAt) <= TimeSpan.FromSeconds(5)
                    ? _status : ControllerControlStatus.Unavailable;
        }
    }

    internal void Report(ControllerControlStatus status)
    {
        if (status is null || !Enum.IsDefined(status.State))
            throw new BridgeProtocolException("Invalid controller status.");
        lock (_gate) { _status = status; _reportedAt = _time.GetTimestamp(); _hasReport = true; }
    }

    internal async ValueTask<ControllerSettings> ReadPreferenceAsync(CancellationToken cancellationToken) =>
        store is null ? new() : (await store.LoadAsync(cancellationToken).ConfigureAwait(false)).Controllers;

    internal async ValueTask<ControllerControlResult> SetAsync(bool enabled, CancellationToken cancellationToken)
    {
        var status = Status;
        if (store is null || enabled && !status.CanEnable)
            return new(false, status);
        try
        {
            await store.UpdateAsync(current => current with
            {
                Controllers = current.Controllers with
                {
                    ExclusiveControl = enabled,
                    Revision = checked(current.Controllers.Revision + 1),
                },
            }, cancellationToken).ConfigureAwait(false);
            // The saved preference is intent; only a later native report can
            // declare the controller active or confirm shutdown/restore.
            return new(true, status with { State = enabled ? ControllerControlState.Starting : ControllerControlState.Stopping });
        }
        catch (Exception exception) when (exception is PlatformSettingsException or IOException or UnauthorizedAccessException)
        {
            return new(false, status);
        }
    }
}
