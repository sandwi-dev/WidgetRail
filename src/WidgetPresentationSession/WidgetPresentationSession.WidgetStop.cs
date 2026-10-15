using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    /// <summary>Persists a host stop. The next catalog retires every presentation authority.</summary>
    public Task StopWidgetAsync(WidgetPresentationTarget target, CancellationToken token = default) =>
        ChangeStoppedAsync(target, BridgeMessageTypes.StopWidget, token);

    /// <summary>Explicitly clears stop; ordinary selection and intents never do so.</summary>
    public Task StartWidgetAsync(WidgetPresentationTarget target, CancellationToken token = default) =>
        ChangeStoppedAsync(target, BridgeMessageTypes.StartWidget, token);

    private async Task ChangeStoppedAsync(WidgetPresentationTarget target, string operation, CancellationToken token)
    {
        _ = ValidateTarget(target);
        var response = await RequestAsync(operation, new WidgetIdRequest(target.Descriptor.Id), BridgeMessageTypes.Acknowledged, token).ConfigureAwait(false);
        RequireObjectProperties(response.Payload, "widgetId");
        if (ReadString(response.Payload, "widgetId") != target.Descriptor.Id)
            throw new BridgeProtocolException("Widget lifecycle acknowledgement has a different owner.");
    }
}
