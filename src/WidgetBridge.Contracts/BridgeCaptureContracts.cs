using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

public sealed record BridgeCaptureTake(HostWindowCapture? Request, string? WidgetId);
public sealed record BridgeCaptureReference(string RequestId);
public sealed record BridgeCaptureCurrent(bool Current);
public sealed record BridgeCaptureAttachmentRequest(string WidgetId, string AttachmentId, BridgeWorkerRun WorkerRun);
