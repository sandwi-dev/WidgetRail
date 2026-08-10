using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

internal static class WindowsNetworkEventProjection
{
    public static BrokerPlatformEvent FromStatus(NetworkStatusSummary status) => new(
        PlatformCapabilities.NetworkReadV1,
        PlatformCapabilities.NetworkStatusChanged,
        new NetworkStatusChangedEvent(status));

    public static BrokerPlatformEvent FromAvailableWifi(AvailableWifiNetworksSummary snapshot) => new(
        PlatformCapabilities.NetworkWifiReadV1,
        PlatformCapabilities.NetworkAvailableWifiChanged,
        new AvailableWifiNetworksChangedEvent(snapshot));

    public static BrokerPlatformEvent FromRadio(WifiRadioSummary radio) => new(
        PlatformCapabilities.NetworkWifiRadioReadV1,
        PlatformCapabilities.NetworkWifiRadioChanged,
        new WifiRadioChangedEvent(radio));
}
