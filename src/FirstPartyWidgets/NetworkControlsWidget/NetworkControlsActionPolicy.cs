namespace GameBarAlternative.FirstPartyWidgets.NetworkControls;

internal enum NetworkControlsAction
{
    None,
    SelectTab,
    ToggleTab,
    Scan,
    ConnectWifi,
    ToggleWifiRadio,
    ToggleBluetoothRadio,
    ShowBluetoothDetails,
    PairBluetooth,
    ManageBluetooth,
    Retry,
}

internal static class NetworkControlsActionPolicy
{
    internal static NetworkControlsAction Resolve(string actionId) => actionId switch
    {
        "network.tab.select" => NetworkControlsAction.SelectTab,
        "network.tab.previous" or "network.tab.next" => NetworkControlsAction.ToggleTab,
        "wifi.scan" => NetworkControlsAction.Scan,
        "wifi.connect.item" => NetworkControlsAction.ConnectWifi,
        "wifi.radio.toggle" => NetworkControlsAction.ToggleWifiRadio,
        "bluetooth.radio.toggle" => NetworkControlsAction.ToggleBluetoothRadio,
        "bluetooth.device.details" => NetworkControlsAction.ShowBluetoothDetails,
        "bluetooth.device.pair" => NetworkControlsAction.PairBluetooth,
        "bluetooth.device.manage" => NetworkControlsAction.ManageBluetooth,
        "retry" => NetworkControlsAction.Retry,
        _ => NetworkControlsAction.None,
    };
}
