using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

public enum WidgetApplicationControl { None, Quit, Restart }

public sealed partial class WidgetPresentationSession
{
    /// <summary>
    /// Consumes a trusted Settings application request once. Poll from the host
    /// control plane, independently of the visible widget or controller input.
    /// </summary>
    public async Task<WidgetApplicationControl> TakeApplicationControlAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(BridgeMessageTypes.ApplicationControl,
            new BridgeEmptyPayload(), BridgeMessageTypes.ApplicationControl, cancellationToken).ConfigureAwait(false);
        RequireObjectProperties(response.Payload, "action");
        var action = ReadInt64(response.Payload, "action", minimum: 0);
        return action switch
        {
            0 => WidgetApplicationControl.None,
            1 => WidgetApplicationControl.Quit,
            2 => WidgetApplicationControl.Restart,
            _ => throw new BridgeProtocolException("WidgetBridge returned an invalid application control request."),
        };
    }
}
