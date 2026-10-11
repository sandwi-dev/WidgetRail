using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed class MarketplaceCapabilityDomain(Func<MarketplaceCommand,CancellationToken,ValueTask<MarketplaceSnapshot>>? execute)
{
    internal async Task<JsonElement> ExecuteAsync(string operation,JsonElement payload,CancellationToken token)
    {
        var command = BrokerJson.ParsePayload<MarketplaceCommand>(payload);
        if (MarketplaceValidation.CommandError(command) is { } error) throw new BrokerException(error,"Marketplace command is invalid.");
        if (operation != "marketplace." + command.Action) throw new BrokerException("invalid_payload","Marketplace action does not match its operation.");
        if (execute is null) throw new BrokerException("marketplace_unavailable","Marketplace management is unavailable.");
        var result = await execute(command,token).ConfigureAwait(false);
        if (MarketplaceValidation.SnapshotError(result) is { } failure) throw new BrokerException(failure,"Marketplace response is invalid.");
        return BrokerJson.ToElement(result);
    }
}
