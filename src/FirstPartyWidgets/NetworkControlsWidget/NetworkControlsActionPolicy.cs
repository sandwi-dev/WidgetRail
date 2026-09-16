namespace WidgetRail.FirstPartyWidgets.NetworkControls;

internal enum NetworkControlsAction
{
    None,
    ManageWifi,
    CloseBluetoothDetails,
    SelectTab,
    ToggleTab,
    Scan,
    ConnectWifi,
    ToggleWifiRadio,
    ToggleBluetoothRadio,
    ShowBluetoothDetails,
    ShowConnectionDetails,
    CloseConnectionDetails,
    PairBluetooth,
    ManageBluetooth,
    OpenUnpairBluetooth,
    ConfirmUnpairBluetooth,
    CancelUnpairBluetooth,
    Retry,
}

internal static class NetworkControlsActionPolicy
{
    internal static NetworkControlsAction Resolve(string actionId) => actionId switch
    {
        "network.tab.select" => NetworkControlsAction.SelectTab,
        "network.tab.previous" or "network.tab.next" => NetworkControlsAction.ToggleTab,
        "wifi.saved.open" or "wifi.saved.refresh" or "wifi.profile.open" or "wifi.manage.close" or
            "wifi.disconnect" or "wifi.auto.toggle" or "wifi.forget.open" or "wifi.forget.confirm" or
            "wifi.forget.cancel" or "wifi.saved.connect" => NetworkControlsAction.ManageWifi,
        "bluetooth.details.close" => NetworkControlsAction.CloseBluetoothDetails,
        "wifi.scan" => NetworkControlsAction.Scan,
        "wifi.connect.item" => NetworkControlsAction.ConnectWifi,
        "wifi.radio.toggle" => NetworkControlsAction.ToggleWifiRadio,
        "bluetooth.radio.toggle" => NetworkControlsAction.ToggleBluetoothRadio,
        "bluetooth.device.details" => NetworkControlsAction.ShowBluetoothDetails,
        "network.details.open" => NetworkControlsAction.ShowConnectionDetails,
        "network.details.close" => NetworkControlsAction.CloseConnectionDetails,
        "bluetooth.device.pair" => NetworkControlsAction.PairBluetooth,
        "bluetooth.device.manage" => NetworkControlsAction.ManageBluetooth,
        "bluetooth.device.unpair.open" => NetworkControlsAction.OpenUnpairBluetooth,
        "bluetooth.device.unpair.confirm" => NetworkControlsAction.ConfirmUnpairBluetooth,
        "bluetooth.device.unpair.cancel" => NetworkControlsAction.CancelUnpairBluetooth,
        "retry" => NetworkControlsAction.Retry,
        _ => NetworkControlsAction.None,
    };
}
