using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.NetworkControls;

internal sealed record NetworkControlsPresentationState(
    NetworkControlsViewState ViewState,
    string Status,
    bool StatusIsError,
    WidgetNetworkStatus? NetworkStatus,
    WidgetNetworkConnectionDetails? ConnectionDetails,
    string ConnectionDetailsMessage,
    bool ConnectionDetailsOpen,
    WidgetAvailableWifiNetworks? Wifi,
    WidgetWifiRadio? WifiRadio,
    bool ControlBusy,
    bool ScanBusy,
    bool RadioBusy,
    WidgetBluetoothSnapshot? Bluetooth,
    string BluetoothMessage,
    bool BluetoothIsError,
    bool BluetoothBusy,
    string? PendingBluetoothDeviceId,
    string? SelectedBluetoothDeviceId,
    string? PendingNetworkId,
    string? SelectedNetworkId,
    NetworkControlsTab ActiveTab,
    bool Interactive,
    WidgetBluetoothDevice? UnpairConfirmationDevice);
