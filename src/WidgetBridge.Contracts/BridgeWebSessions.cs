using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

public sealed record BridgeWebSessionResource(string Path, string ContentType, string Sha256, string ContentBase64);
public sealed record BridgeWebSessionWork(HostWebSessionWork Request, IReadOnlyList<BridgeWebSessionResource> Resources);
public sealed record BridgeWebSessionBatch(IReadOnlyList<BridgeWebSessionWork> Work, IReadOnlyList<string> LiveSessions);
public sealed record BridgeWebSessionViewRequest(string WidgetId, BridgeWorkerRun WorkerRun, WidgetRail.WidgetProtocol.WebSessionReference Session);
public sealed record BridgeWebSessionViewResult(bool Current);
