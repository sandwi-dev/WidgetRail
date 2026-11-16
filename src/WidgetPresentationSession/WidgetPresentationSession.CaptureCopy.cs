using WidgetRail.WidgetBridge;
namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public async Task<bool> IsCaptureSourceCurrentAsync(string id, CancellationToken token = default) =>
        BridgeJson.FromElement<BridgeCaptureCurrent>((await RequestAsync(BridgeMessageTypes.CheckCaptureSource, new BridgeCaptureReference(id), BridgeMessageTypes.CheckCaptureSource, token).ConfigureAwait(false)).Payload).Current;
    public async Task CompleteCaptureSourceAsync(string id, string? directory, CancellationToken token = default) =>
        _ = await RequestAsync(BridgeMessageTypes.CompleteCaptureSource, new BridgeCaptureCopyDestination(id, directory), BridgeMessageTypes.CompleteCaptureSource, token).ConfigureAwait(false);
    public async Task<bool> IsCaptureCopyCurrentAsync(string id, CancellationToken token = default) =>
        BridgeJson.FromElement<BridgeCaptureCurrent>((await RequestAsync(BridgeMessageTypes.CheckCaptureCopy, new BridgeCaptureReference(id), BridgeMessageTypes.CheckCaptureCopy, token).ConfigureAwait(false)).Payload).Current;
    public async Task CompleteCaptureCopyAsync(string id, string? destination, CancellationToken token = default) =>
        _ = await RequestAsync(BridgeMessageTypes.CompleteCaptureCopy, new BridgeCaptureCopyDestination(id, destination), BridgeMessageTypes.CompleteCaptureCopy, token).ConfigureAwait(false);
}
