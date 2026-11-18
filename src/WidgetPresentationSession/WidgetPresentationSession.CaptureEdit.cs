using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public async Task<HostCaptureEditStatus> GetCaptureEditStatusAsync(string id, CancellationToken token = default) =>
        BridgeJson.FromElement<HostCaptureEditStatus>((await RequestAsync(BridgeMessageTypes.CheckCaptureEdit,
            new BridgeCaptureReference(id), BridgeMessageTypes.CheckCaptureEdit, token).ConfigureAwait(false)).Payload);
    public async Task<bool> CompleteCaptureEditAsync(string id, HostCaptureEditRecipe recipe, CancellationToken token = default) =>
        BridgeJson.FromElement<BridgeCaptureCurrent>((await RequestAsync(BridgeMessageTypes.CompleteCaptureEdit,
            new BridgeCaptureEditCompletion(id, recipe), BridgeMessageTypes.CompleteCaptureEdit, token).ConfigureAwait(false)).Payload).Current;
    public async Task<HostCaptureEditStatus> CancelCaptureEditAsync(string id, CancellationToken token = default) =>
        BridgeJson.FromElement<HostCaptureEditStatus>((await RequestAsync(BridgeMessageTypes.CancelCaptureEdit,
            new BridgeCaptureReference(id), BridgeMessageTypes.CancelCaptureEdit, token).ConfigureAwait(false)).Payload);
}
