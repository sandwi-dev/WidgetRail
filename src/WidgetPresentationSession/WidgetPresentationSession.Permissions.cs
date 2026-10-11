using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public async Task<BridgePermissionTake> TakePermissionAsync(CancellationToken token = default) =>
        BridgeJson.FromElement<BridgePermissionTake>((await RequestAsync(BridgeMessageTypes.TakePermission, new BridgeEmptyPayload(), BridgeMessageTypes.TakePermission, token).ConfigureAwait(false)).Payload);
    public async Task<bool> IsPermissionCurrentAsync(string id, CancellationToken token = default) =>
        BridgeJson.FromElement<BridgePermissionCurrent>((await RequestAsync(BridgeMessageTypes.CheckPermission, new WidgetPermissionTicket(id), BridgeMessageTypes.CheckPermission, token).ConfigureAwait(false)).Payload).Current;
    public async Task<bool> CompletePermissionAsync(string id, WidgetPermissionOutcome outcome, CancellationToken token = default) =>
        BridgeJson.FromElement<BridgePermissionCurrent>((await RequestAsync(BridgeMessageTypes.CompletePermission, new BridgePermissionComplete(id, outcome), BridgeMessageTypes.CompletePermission, token).ConfigureAwait(false)).Payload).Current;
}
