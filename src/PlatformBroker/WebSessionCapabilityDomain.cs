using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed class WebSessionCapabilityDomain(Func<WebSessionCommand, CancellationToken, ValueTask<WebSessionReply>>? execute)
{
    internal async ValueTask<JsonElement> ExecuteAsync(JsonElement payload, CancellationToken token)
    {
        if (execute is null) throw new BrokerException("platform_unavailable", "Hosted web sessions are unavailable.");
        return BrokerJson.ToElement(await execute(BrokerJson.ParsePayload<WebSessionCommand>(payload), token).ConfigureAwait(false));
    }
}
