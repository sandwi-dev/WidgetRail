using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Reserved host-granted management service. Ordinary widgets cannot acquire this authority through a manifest or consent.</summary>
public static class WidgetMarketplaceCapabilities
{
    public const string Service = "widgets.marketplace.v1";
    public static WidgetCapabilityOperation<MarketplaceCommand, MarketplaceSnapshot> Command(string action) => action is
        "status" or "refresh" or "inventory" or "download" or "install" or "cancel" ? new(Service, "marketplace." + action) : throw new ArgumentException("Unknown marketplace action.", nameof(action));
}
public sealed class WidgetMarketplaceService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetMarketplaceService(IWidgetCapabilityClient client) => this.client = client;
    /// <summary>Only the verified bundled marketplace receives access. Mutations require interaction and exact package-review tokens.</summary>
    public async ValueTask<MarketplaceSnapshot> ExecuteAsync(MarketplaceCommand command, CancellationToken cancellationToken = default)
    {
        if (MarketplaceValidation.CommandError(command) is { } error) throw new ArgumentException(error, nameof(command));
        var result = await client.InvokeAsync(WidgetMarketplaceCapabilities.Command(command.Action),command,cancellationToken).ConfigureAwait(false);
        if (MarketplaceValidation.SnapshotError(result) is not null) throw new WidgetCapabilityException("malformed_response","Marketplace response is invalid.");
        return result;
    }
}
