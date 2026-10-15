using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public async Task<bool> CheckWebSessionViewAsync(WidgetPresentationAuthority authority, WidgetRail.WidgetProtocol.WebSessionReference reference, CancellationToken token = default)
    {
        if (authority.WorkerRun is null || !IsWebBrowserSessionCurrent(authority, reference.Id)) return false;
        return BridgeJson.FromElement<BridgeWebSessionViewResult>((await RequestAsync(BridgeMessageTypes.CheckWebSessionView,
            new BridgeWebSessionViewRequest(authority.WidgetId, authority.WorkerRun, reference), BridgeMessageTypes.CheckWebSessionView, token).ConfigureAwait(false)).Payload).Current;
    }
    public async Task<BridgeWebSessionBatch> TakeWebSessionsAsync(CancellationToken token = default) =>
        BridgeJson.FromElement<BridgeWebSessionBatch>((await RequestAsync(BridgeMessageTypes.TakeWebSessions,
            new BridgeEmptyPayload(), BridgeMessageTypes.TakeWebSessions, token).ConfigureAwait(false)).Payload);
    public async Task CompleteWebSessionAsync(HostWebSessionResult result, CancellationToken token = default) =>
        _ = await RequestAsync(BridgeMessageTypes.CompleteWebSession, result, BridgeMessageTypes.CompleteWebSession, token).ConfigureAwait(false);
    public async Task PublishWebSessionMessagesAsync(HostWebSessionMessages messages, CancellationToken token = default) =>
        _ = await RequestAsync(BridgeMessageTypes.WebSessionMessages, messages, BridgeMessageTypes.WebSessionMessages, token).ConfigureAwait(false);
}
