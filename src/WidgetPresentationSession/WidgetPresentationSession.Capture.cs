using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public async Task<BridgeCaptureTake> TakeCaptureAsync(CancellationToken token = default) =>
        BridgeJson.FromElement<BridgeCaptureTake>((await RequestAsync(BridgeMessageTypes.TakeCapture, new BridgeEmptyPayload(), BridgeMessageTypes.TakeCapture, token).ConfigureAwait(false)).Payload);
    public async Task<bool> IsCaptureCurrentAsync(string id, CancellationToken token = default) =>
        BridgeJson.FromElement<BridgeCaptureCurrent>((await RequestAsync(BridgeMessageTypes.CheckCapture, new BridgeCaptureReference(id), BridgeMessageTypes.CheckCapture, token).ConfigureAwait(false)).Payload).Current;
    public async Task SetCaptureRecordingAsync(string id, CancellationToken token = default) =>
        _ = await RequestAsync(BridgeMessageTypes.RecordingCapture, new BridgeCaptureReference(id), BridgeMessageTypes.RecordingCapture, token).ConfigureAwait(false);
    public async Task<bool> CompleteCaptureAsync(HostCaptureCompletion completion, CancellationToken token = default) =>
        BridgeJson.FromElement<BridgeCaptureCurrent>((await RequestAsync(BridgeMessageTypes.CompleteCapture, completion, BridgeMessageTypes.CompleteCapture, token).ConfigureAwait(false)).Payload).Current;
    public async Task<HostCaptureAttachment> ResolveCaptureAttachmentAsync(WidgetPresentationFrame displayed, string elementId,
        WidgetPinnedSelection? selection = null, WidgetPinnedProjection? projection = null, CancellationToken token = default)
    {
        var attachment = ResolveCapturedMedia(displayed, elementId, selection, projection);
        var result = BridgeJson.FromElement<HostCaptureAttachment>((await RequestAsync(BridgeMessageTypes.ResolveCaptureAttachment,
            new BridgeCaptureAttachmentRequest(displayed.Authority.WidgetId, attachment.Id, displayed.Authority.WorkerRun!),
            BridgeMessageTypes.ResolveCaptureAttachment, token).ConfigureAwait(false)).Payload);
        if (ResolveCapturedMedia(displayed, elementId, selection, projection) != result.Attachment) throw CaptureStale();
        return result;
    }
    public WidgetRail.WidgetProtocol.CaptureAttachment ResolveCapturedMedia(WidgetPresentationFrame displayed, string elementId,
        WidgetPinnedSelection? selection = null, WidgetPinnedProjection? projection = null)
    {
        using (_gate.Enter())
        {
            var current = DemandPinnedFrameLocked(displayed);
            var origin = displayed.Snapshot;
            var latest = current.Snapshot;
            if (selection is not null || projection is not null)
            {
                if (selection is null || projection is null || !ReferenceEquals(projection.Frame, displayed)) throw CaptureStale();
                latest = DemandPinnedInputLocked(selection, projection); origin = projection.Snapshot;
            }
            var before = CaptureFor(FindCapture(origin.Root, elementId));
            var after = CaptureFor(FindCapture(latest.Root, elementId));
            if (displayed.Authority.WorkerRun is null || displayed.Authority.WorkerRun != current.Authority.WorkerRun ||
                before is null || before != after || !before.IsWellFormed()) throw CaptureStale();
            return before;
        }
    }
    private static WidgetRail.WidgetProtocol.CaptureAttachment? CaptureFor(WidgetRail.WidgetProtocol.ViewNode? node) => node is null ? null : WidgetRail.WidgetProtocol.MediaPlayerDefinition.FromNode(node)?.Source.Attachment;
    private static WidgetRail.WidgetProtocol.ViewNode? FindCapture(WidgetRail.WidgetProtocol.ViewNode node, string id) =>
        node.Id == id && node.Kind is WidgetRail.WidgetProtocol.ViewNodeKind.CapturedMedia or WidgetRail.WidgetProtocol.ViewNodeKind.MediaPlayer ? node :
        node.Children.Select(child => FindCapture(child, id)).FirstOrDefault(child => child is not null);
    private static WidgetPresentationSessionException CaptureStale() => new("capture_stale", "The displayed capture retired or changed.");
}
