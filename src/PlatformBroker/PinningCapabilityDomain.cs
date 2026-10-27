using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal static class PinningCapabilityDomain
{
    internal static JsonElement Admit(JsonElement payload)
    {
        if(payload.ValueKind!=JsonValueKind.Object || payload.EnumerateObject().Any(p=>p.Name!="layoutId"))
            throw new BrokerException("invalid_payload","A pin request accepts only its layout identity.");
        var request=BrokerJson.ParsePayload<WidgetPinRequest>(payload);
        if(request?.IsWellFormed()!=true)throw new BrokerException("invalid_payload","Invalid pinned layout.");
        return BrokerJson.ToElement(new WidgetPinReceipt(true));
    }
}
