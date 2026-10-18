using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

/// <summary>The host-only password path must preserve ordinary package consent.</summary>
internal static class ProtectedWifiPermissionPolicy
{
    internal static async Task DemandAsync(ConfiguredWidget configured, ConsentStore? store,
        CancellationToken cancellationToken)
    {
        if (!configured.PublicDescriptor().ProtectedWifiPromptSupported || store is null)
            throw new BridgeProtocolException("Wi-Fi access is not available. Review the widget's permissions in Settings.");
        var consent = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var capability in new[] { PlatformCapabilities.NetworkWifiReadV1, PlatformCapabilities.NetworkWifiConnectV1 })
            if (!consent.Entries.Any(entry => entry.PackageId == configured.PackageId &&
                entry.PublisherId == configured.PublisherId && entry.CapabilityId == capability &&
                entry.Decision == ConsentDecision.Grant))
                throw new BridgeProtocolException("Wi-Fi access is not allowed. Review the widget's permissions in Settings.");
    }
}
