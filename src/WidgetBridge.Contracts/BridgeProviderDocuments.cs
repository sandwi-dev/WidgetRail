using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

public sealed record BridgeProviderDocumentRequest(string WidgetId, BridgeWorkerRun WorkerRun, ProviderDocumentReference Reference);
public sealed record BridgeProviderDocument(ProviderDocumentReference Reference, IReadOnlyList<string> Html);
