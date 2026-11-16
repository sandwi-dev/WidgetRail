using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal static class PinningCapabilityDomain
{
    internal static JsonElement Admit(string operation,JsonElement payload)
    {
        var close=operation is "pin.close" or "pin.manage";
        if(payload.ValueKind!=JsonValueKind.Object || payload.EnumerateObject().Any(p=>p.Name!="layoutId" && !(close && p.Name=="selectionId")))
            throw new BrokerException("invalid_payload","Unexpected pin request fields.");
        var valid=operation=="pin.manage" ? BrokerJson.ParsePayload<WidgetPinManageRequest>(payload)?.IsWellFormed()
            :close ? BrokerJson.ParsePayload<WidgetPinCloseRequest>(payload)?.IsWellFormed()
            : BrokerJson.ParsePayload<WidgetPinRequest>(payload)?.IsWellFormed();
        if(valid!=true)throw new BrokerException("invalid_payload","Invalid pin target.");
        return BrokerJson.ToElement(new WidgetPinReceipt(true));
    }
}
