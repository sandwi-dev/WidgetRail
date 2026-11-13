using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed class WebSessionCapabilityDomain(Func<WebSessionCommand, CancellationToken, ValueTask<WebSessionReply>>? execute)
{
    internal async ValueTask<JsonElement> ExecuteAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if (execute is null) throw new BrokerException("platform_unavailable", "Hosted web sessions are unavailable.");
        var command=BrokerJson.ParsePayload<WebSessionCommand>(payload);
        if(command is null || (operation=="web-session.microphone") != (command.Operation==WebSessionOperation.RequestMicrophone) ||
            (operation=="web-session.permission") != (command.Operation==WebSessionOperation.RequestPermission))
            throw new BrokerException("invalid_payload","Website permission consent requires its interactive operation.");
        return BrokerJson.ToElement(await execute(command, token).ConfigureAwait(false));
    }
}
