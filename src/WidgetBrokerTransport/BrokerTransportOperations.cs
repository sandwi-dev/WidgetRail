using WidgetRail.Internal;

namespace WidgetRail.PlatformBroker;

// Wire identifiers shared by the client and host. Authorization policy remains in PlatformBroker.
internal static class BrokerTransportOperations
{
    internal const string AppLibraryLaunch = "apps.library.launch";
    internal const string AppLibraryLaunchObserved = "apps.library.launch-observed";
    internal const string AppLibraryLaunchV1 = "system.apps.library.launch.v1";
    internal const string CaptureLibraryManageV1 = "media.capture-library.manage.v1";
    internal const string DisplayControlsGet = "display-controls.get";
    internal const string DisplayControlsApply = "display-controls.apply";
    internal const string DisplayProfilesApply = "display-profiles.apply";
    internal const string DisplayProfilesGet = "display-profiles.get";
    internal const string DisplayProfilesKeep = "display-profiles.keep";
    internal const string DisplayProfilesRevert = "display-profiles.revert";
    internal const string DisplaysControlV1 = "system.displays.control.v1";
    internal const string DisplaysReadV1 = "system.displays.read.v1";
    internal const string LoopbackCapabilityPrefix = "network.loopback:";
    internal const string LoopbackHttpGetJson = "loopback.http.get-json";
    internal const string LoopbackHttpPostJson = "loopback.http.post-json";
    internal const string NetworkBluetoothPairV1 = "system.network.bluetooth.pair.v1";
    internal const string NetworkBluetoothScan = "network.bluetooth.scan";
    internal const string WindowCaptureV1 = "system.apps.windows.capture.v1";

    internal static bool TryGetLoopbackPort(string? capabilityId, out int port)
    {
        port = 0;
        if (capabilityId is null ||
            !capabilityId.StartsWith(LoopbackCapabilityPrefix, StringComparison.Ordinal))
            return false;
        var text = capabilityId.AsSpan(LoopbackCapabilityPrefix.Length);
        if (text.Length is < 1 or > 5 || text[0] == '0' ||
            !int.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out port) ||
            port is < CommunityPlatformContractLimits.MinimumLoopbackPort or > CommunityPlatformContractLimits.MaximumLoopbackPort)
        {
            port = 0;
            return false;
        }
        return true;
    }

}
