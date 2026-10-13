namespace WidgetRail.WidgetBridge;

public sealed record BridgeBrowserRetention(string WidgetId, BridgeWorkerRun WorkerRun, string DocumentId, bool Retain);
public sealed record BridgeBrowserRetentionResult(bool Retained);
