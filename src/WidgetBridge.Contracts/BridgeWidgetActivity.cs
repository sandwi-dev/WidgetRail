using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;
internal sealed record BridgeWidgetActivity(string WidgetId, string InstanceId, string RuntimeGeneration,
    long Sequence, long InitiatedAtMilliseconds, WidgetActivity Activity, BridgeWorkerRun WorkerRun);
internal sealed record BridgeWidgetActivityRetired(string WidgetId, BridgeWorkerRun WorkerRun);
