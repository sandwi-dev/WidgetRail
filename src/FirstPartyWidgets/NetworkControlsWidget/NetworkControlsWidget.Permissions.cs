using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.NetworkControls;

public sealed partial class NetworkControlsWidget
{
    private string[] ActionPermissions(WidgetActionEvent action)
    {
        if (action.ActionId == "network.tab.select" && action.SourceElementId == "network.tab.bluetooth" ||
            action.ActionId is "network.tab.previous" or "network.tab.next" && ActiveTab == NetworkControlsTab.Wifi)
            return ["system.network.bluetooth.read.v1"];
        return action.ActionId switch
        {
            "retry" => ActiveTab == NetworkControlsTab.Bluetooth ? ["system.network.bluetooth.read.v1"] : ["system.network.read.v1", "system.network.wifi.read.v1", "system.network.wifi.radio.read.v1"],
            "network.details.open" => ["system.network.details.read.v1"],
            "wifi.scan" => ["system.network.wifi.read.v1"],
            "wifi.radio.toggle" => ["system.network.wifi.radio.control.v1"],
            "wifi.connect.item" => ["system.network.wifi.connect.v1"],
            "wifi.saved.open" or "wifi.saved.refresh" => ["system.network.read.v1"],
            "wifi.disconnect" or "wifi.auto.toggle" or "wifi.forget.confirm" => ["system.network.wifi.manage.v1"],
            "wifi.saved.connect" => ["system.network.saved-profile.switch.v1"],
            "bluetooth.device.details" => ["system.network.bluetooth.read.v1"],
            "bluetooth.scan" => ["system.network.bluetooth.read.v1", "system.network.bluetooth.pair.v1"],
            "bluetooth.radio.toggle" => ["system.network.bluetooth.radio.control.v1"],
            "bluetooth.device.pair" => ["system.network.bluetooth.pair.v1"],
            "bluetooth.device.manage" => ["system.network.bluetooth.manage.v1"],
            "bluetooth.device.unpair.confirm" => ["system.network.bluetooth.unpair.v1"],
            _ => [],
        };
    }
    private void PermissionFeedback(string text) { SetFeedback(text, true); }
    private void RestartAfterPermission(WidgetActionEvent action)
    {
        // Optional subscriptions may have ended on the earlier denied read.
        if (action.ActionId is "network.tab.select" or "network.tab.previous" or "network.tab.next" or "network.details.open" or "bluetooth.scan")
            StartActiveRun();
    }
}
