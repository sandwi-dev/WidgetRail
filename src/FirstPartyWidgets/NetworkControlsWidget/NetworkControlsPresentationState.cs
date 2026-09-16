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
    WidgetBluetoothDevice? UnpairConfirmationDevice)
{
    public NetworkWifiManagementState Management { get; init; } = new();
    public WidgetBluetoothDevice? BluetoothDetails { get; init; }
}

internal sealed record NetworkWifiManagementState
{
    public bool Open { get; init; }
    public IReadOnlyList<WidgetSavedNetworkProfile> Profiles { get; init; } = [];
    public string? ProfileId { get; init; }
    public WidgetAvailableWifiNetwork? Network { get; init; }
    public bool ConfirmForget { get; init; }
    public bool Busy { get; init; }
    public string Message { get; init; } = "Saved networks are remembered by Windows.";
    public bool IsError { get; init; }
}
