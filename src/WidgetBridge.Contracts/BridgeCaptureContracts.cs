using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

public sealed record BridgeCaptureTake(HostWindowCapture? Request, string? WidgetId)
{
    public bool NeedsCapabilities { get; init; }
    public bool NeedsEncodingCapabilities { get; init; }
}
public sealed record BridgeCaptureCapabilities(WidgetRail.WidgetProtocol.CaptureCapabilities Capabilities, bool EncodingProbed);
public sealed record BridgeCaptureReference(string RequestId);
public sealed record BridgeCaptureCurrent(bool Current)
{
    public bool StopRequested { get; init; }
}
public sealed record BridgeCaptureAttachmentRequest(string WidgetId, string AttachmentId, BridgeWorkerRun WorkerRun);
