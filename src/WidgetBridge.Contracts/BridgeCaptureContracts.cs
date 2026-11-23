using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

public sealed record BridgeCaptureTake(HostWindowCapture? Request, string? WidgetId)
{
    public HostReplayApprovalPicker? ApprovalPicker { get; init; }
    public string? ApprovalWidgetId { get; init; }
    public bool NeedsCapabilities { get; init; }
    public bool NeedsEncodingCapabilities { get; init; }
    public IReadOnlyList<HostCaptureDiscovery> RecordingDiscoveries { get; init; } = [];
    public HostCaptureSourceRequest? SourceFolder { get; init; }
    public string? SourceWidgetId { get; init; }
    public HostCaptureCopyRequest? CopyDestination { get; init; }
    public string? CopyWidgetId { get; init; }
    public HostCaptureFolderRequest? OpenFolder { get; init; }
    public string? FolderWidgetId { get; init; }
    public HostCaptureDestinationReview? DestinationReview { get; init; }
    public string? DestinationWidgetId { get; init; }
    public HostCaptureEditRequest? Edit { get; init; }
    public string? EditWidgetId { get; init; }
}
public sealed record BridgeCaptureEditCompletion(string Id, HostCaptureEditRecipe Recipe);
public sealed record BridgeCaptureCopyDestination(string Id, string? Destination);
public sealed record BridgeCaptureFolderResult(string Id,bool Opened);
public sealed record BridgeCaptureDestinationDecision(string Id, bool Approved);
public sealed record BridgeReplayApprovalChoice(string Id, WidgetRail.WidgetProtocol.ReplayApprovedApplication? Selected);
public sealed record BridgeCaptureCapabilities(WidgetRail.WidgetProtocol.CaptureCapabilities Capabilities, bool EncodingProbed, string? DiscoveryId = null);
public sealed record BridgeCaptureReference(string RequestId);
public sealed record BridgeCaptureCurrent(bool Current)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ApplicationName { get; init; }
    public bool StopRequested { get; init; }
}
public sealed record BridgeCaptureAttachmentRequest(string WidgetId, string AttachmentId, BridgeWorkerRun WorkerRun);
