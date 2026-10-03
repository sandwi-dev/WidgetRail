namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    internal bool IsCaptureWorkerCurrent(string widgetId, BridgeWorkerRun run)
    {
        lock (_gate)
        {
            return _clients.TryGetValue(widgetId, out var registration) && IsCurrentLocked(registration) &&
                registration.HasCurrentSnapshotWorker && registration.CachedWorkerRun == run;
        }
    }
}
