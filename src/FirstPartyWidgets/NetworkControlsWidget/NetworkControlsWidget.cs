using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using System.Security.Cryptography;
using System.Text;

namespace GameBarAlternative.FirstPartyWidgets.NetworkControls;

public enum NetworkControlsViewState
{
    Initial,
    Loading,
    Ready,
    NotScanned,
    Scanning,
    EmptyNetworks,
    RadioOff,
    WirelessUnavailable,
    PreciseLocationDenied,
    PermissionDenied,
    LifecycleDenied,
    ChannelClosed,
    ServiceUnavailable,
    Error,
}

public enum NetworkControlsTab
{
    Wifi,
    Bluetooth,
}

/// <summary>
/// Event-driven, controller-first view of the Wi-Fi networks in the host's most
/// recent bounded scan. Platform work and network credentials remain in trusted
/// host services; this worker receives only sanitized display data and opaque,
/// scan-generation-bound network IDs.
/// </summary>
public sealed class NetworkControlsWidget : Widget
{
    private static readonly WidgetSurfaceHints CompactSurface = new()
    {
        Mode = WidgetSurfaceMode.Compact,
        PreferredWidth = 560,
        PreferredHeight = 700,
        MinimumWidth = 320,
        MinimumHeight = 420,
    };

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private WidgetNetworkStatus? _networkStatus;
    private WidgetNetworkStatus? _authoritativeStatus;
    private WidgetAvailableWifiNetworks? _wifiSnapshot;
    private WidgetAvailableWifiNetworks? _authoritativeWifiSnapshot;
    private WidgetWifiRadio? _wifiRadio;
    private WidgetWifiRadio? _authoritativeWifiRadio;
    private WidgetBluetoothSnapshot? _bluetooth;
    private WidgetBluetoothSnapshot? _authoritativeBluetooth;
    private string _bluetoothMessage = "Bluetooth loads when this widget becomes visible";
    private bool _bluetoothIsError;
    private bool _bluetoothBusy;
    private string? _selectedBluetoothDeviceId;
    private int _selectedBluetoothIndex;
    private string? _selectedNetworkId;
    private int _selectedIndex;
    private string? _pendingNetworkId;
    private NetworkControlsViewState _viewState = NetworkControlsViewState.Initial;
    private string _status = "Network status loads when this widget becomes visible";
    private bool _statusIsError;
    private bool _controlBusy;
    private bool _scanBusy;
    private bool _radioBusy;
    private CancellationTokenSource? _runLifetime;
    private long _runGeneration;
    private int _activationCount;
    private int _statusFetchCount;
    private int _wifiFetchCount;
    private int _scanRequestCount;
    private int _radioControlCount;
    private int _bluetoothFetchCount;
    private int _bluetoothRadioControlCount;
    private NetworkControlsTab _activeTab;

    public NetworkControlsViewState ViewState
    {
        get { lock (_stateLock) return _viewState; }
    }

    public WidgetNetworkStatus? NetworkStatus
    {
        get { lock (_stateLock) return _networkStatus; }
    }

    public WidgetAvailableWifiNetworks? WifiSnapshot
    {
        get
        {
            lock (_stateLock)
                return _wifiSnapshot is null
                    ? null
                    : _wifiSnapshot with { Networks = _wifiSnapshot.Networks.ToArray() };
        }
    }

    public IReadOnlyList<WidgetAvailableWifiNetwork> Networks
    {
        get { lock (_stateLock) return _wifiSnapshot?.Networks.ToArray() ?? []; }
    }

    public string? SelectedNetworkId
    {
        get { lock (_stateLock) return _selectedNetworkId; }
    }

    public bool ControlBusy
    {
        get { lock (_stateLock) return _controlBusy; }
    }

    public bool ScanBusy
    {
        get { lock (_stateLock) return _scanBusy; }
    }

    public WidgetWifiRadio? WifiRadio
    {
        get { lock (_stateLock) return _wifiRadio; }
    }

    public bool RadioBusy
    {
        get { lock (_stateLock) return _radioBusy; }
    }

    public int ActivationCount => Volatile.Read(ref _activationCount);
    public int StatusFetchCount => Volatile.Read(ref _statusFetchCount);
    public int WifiFetchCount => Volatile.Read(ref _wifiFetchCount);
    public int ScanRequestCount => Volatile.Read(ref _scanRequestCount);
    public int RadioControlCount => Volatile.Read(ref _radioControlCount);
    public int BluetoothFetchCount => Volatile.Read(ref _bluetoothFetchCount);
    public int BluetoothRadioControlCount => Volatile.Read(ref _bluetoothRadioControlCount);
    public NetworkControlsTab ActiveTab
    {
        get { lock (_stateLock) return _activeTab; }
    }
    public WidgetBluetoothSnapshot? Bluetooth
    {
        get
        {
            lock (_stateLock)
                return _bluetooth is null
                    ? null
                    : _bluetooth with { Devices = _bluetooth.Devices.ToArray() };
        }
    }

    public override WidgetView Render()
    {
        WidgetNetworkStatus? status;
        WidgetAvailableWifiNetworks? wifi;
        WidgetWifiRadio? radio;
        NetworkControlsViewState state;
        string statusText;
        bool statusIsError;
        bool controlBusy;
        bool scanBusy;
        bool radioBusy;
        WidgetBluetoothSnapshot? bluetooth;
        string bluetoothMessage;
        bool bluetoothIsError;
        bool bluetoothBusy;
        string? selectedBluetoothId;
        string? pendingId;
        string? selectedId;
        NetworkControlsTab activeTab;
        lock (_stateLock)
        {
            status = _networkStatus;
            wifi = _wifiSnapshot;
            radio = _wifiRadio;
            state = _viewState;
            statusText = _status;
            statusIsError = _statusIsError;
            controlBusy = _controlBusy;
            scanBusy = _scanBusy;
            radioBusy = _radioBusy;
            bluetooth = _bluetooth;
            bluetoothMessage = _bluetoothMessage;
            bluetoothIsError = _bluetoothIsError;
            bluetoothBusy = _bluetoothBusy;
            selectedBluetoothId = _selectedBluetoothDeviceId;
            pendingId = _pendingNetworkId;
            selectedId = _selectedNetworkId;
            activeTab = _activeTab;
        }

        var header = UI.Stack("network.header",
                UI.Text("CONTROL CENTER", "network.eyebrow", "Control Center")
                    .Classes("network-eyebrow"),
                UI.Text("Network Controls", "network.title", "Network Controls")
                    .Classes("network-title"),
                UI.Text(statusText, "network.status", statusText).Classes(
                    "network-status",
                    statusIsError ? "is-error" : IsConnected(status) ? "is-live" : "is-neutral"))
            .Classes("network-header");

        if (status is null || wifi is null || radio is null)
            return RenderProviderState(header, state);

        var interactive = LifecycleState == WidgetLifecycleState.Interactive;
        var networks = wifi.ScanState == WidgetWifiScanState.Ready ? wifi.Networks : [];
        var selectedNetwork = networks.FirstOrDefault(network =>
            string.Equals(network.NetworkId, selectedId, StringComparison.Ordinal)) ??
            networks.FirstOrDefault();
        var wifiInitialFocus = selectedNetwork is null
            ? "network.wifi.scan"
            : NetworkElementId(selectedNetwork.NetworkId);
        var bluetoothDevices = bluetooth?.DiscoveryState == WidgetBluetoothDiscoveryState.Ready
            ? bluetooth.Devices
            : [];
        var selectedBluetooth = bluetoothDevices.FirstOrDefault(device =>
            string.Equals(device.DeviceId, selectedBluetoothId, StringComparison.Ordinal)) ??
            bluetoothDevices.FirstOrDefault();
        var bluetoothInitialFocus = selectedBluetooth is null
            ? "network.bluetooth.radio"
            : BluetoothElementId(selectedBluetooth.DeviceId);
        var activeInitialFocus = activeTab == NetworkControlsTab.Wifi
            ? wifiInitialFocus
            : bluetoothInitialFocus;

        var tabs = UI.SegmentedTabs("network.tabs",
            activeTab == NetworkControlsTab.Wifi ? "network.tab.wifi" : "network.tab.bluetooth",
            new SegmentedTab("network.tab.wifi", "Wi-Fi", "network.tab.select", "Wi-Fi controls"),
            new SegmentedTab("network.tab.bluetooth", "Bluetooth", "network.tab.select", "Bluetooth controls"));
        tabs = tabs with
        {
            Children = tabs.Children.Select(child => child is ButtonElement button
                ? button.FocusUp(button.Id).FocusDown(activeInitialFocus)
                : child).ToArray(),
        };

        var content = new List<WidgetElement>
        {
            header,
            RenderConnectionCard(status),
            tabs,
        };

        if (activeTab == NetworkControlsTab.Bluetooth)
        {
            content.Add(UI.VerticalScroll("network.bluetooth.body.scroll",
                    RenderBluetoothSection(bluetooth, bluetoothMessage, bluetoothIsError,
                        bluetoothBusy).ToArray())
                .Classes("network-view-scroll", "network-bluetooth-view"));
        }
        else
        {
            var wifiContent = new List<WidgetElement>
            {
                RenderRadioControl(radio, radioBusy),
            };
            var note = WirelessNote(status, wifi.ScanState);
            if (note is not null) wifiContent.Add(RenderNotice(note.Value));

        var scanButton = UI.Button(
                scanBusy ? "Scanning…" : wifi.ScanState == WidgetWifiScanState.NotScanned
                    ? "Scan for networks" : "Scan again",
                "wifi.scan", "network.wifi.scan")
            .Icon(WidgetGlyph.Refresh, scanBusy ? "Scanning for nearby Wi-Fi networks" : "Scan for nearby Wi-Fi networks")
            .Busy(scanBusy)
            .Disabled(!interactive || scanBusy || controlBusy || !CanScan(status))
            .FocusUp("network.wifi.radio")
            .FocusLeft("network.wifi.scan")
            .FocusRight("network.wifi.scan")
            .Classes("network-scan-action");

            var wifiHeadingIndex = wifiContent.Count;
            wifiContent.Add(UI.Row("network.wifi.heading",
                UI.Stack("network.wifi.heading.copy",
                        UI.Text("AVAILABLE WI-FI", "network.wifi.label", "Available Wi-Fi networks")
                            .Classes("network-section-label"),
                        UI.Text(ScanSummary(wifi), "network.wifi.summary", ScanSummary(wifi))
                            .Classes("network-section-summary"))
                    .Classes("network-section-copy"),
                scanButton)
            .Classes("network-section-heading"));

        if (networks.Count == 0)
        {
                wifiContent.Add(RenderWifiState(wifi.ScanState));
        }
        else
        {
                wifiContent.AddRange(RenderNetworkRows(
                    networks, controlBusy, pendingId));
        }

        if (networks.Count > 0)
            scanButton = scanButton.FocusDown(NetworkElementId(networks[0].NetworkId));
        // Replace the heading's immutable button with its completed focus graph.
            wifiContent[wifiHeadingIndex] = UI.Row("network.wifi.heading",
                UI.Stack("network.wifi.heading.copy",
                        UI.Text("AVAILABLE WI-FI", "network.wifi.label", "Available Wi-Fi networks")
                            .Classes("network-section-label"),
                        UI.Text(ScanSummary(wifi), "network.wifi.summary", ScanSummary(wifi))
                            .Classes("network-section-summary"))
                    .Classes("network-section-copy"),
                scanButton)
            .Classes("network-section-heading");

            content.Add(UI.VerticalScroll("network.wifi.body.scroll", wifiContent.ToArray())
                .Classes("network-view-scroll", "network-wifi-view"));
        }

        var root = UI.Stack("network.root", content.ToArray())
            .InputScope("network-controls")
            .Shortcut(ControllerButton.LeftBumper, "network.tab.previous")
            .Shortcut(ControllerButton.RightBumper, "network.tab.next")
            .Classes("network-controls-widget",
                activeTab == NetworkControlsTab.Wifi ? "is-wifi" : "is-bluetooth",
                networks.Count == 0 ? "has-state" : "has-networks");
        return new WidgetView(root, InitialFocusId: activeInitialFocus, Surface: CompactSurface);
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        Interlocked.Increment(ref _activationCount);
        StartActiveRun(activeLifetime);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        if (previous is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive ||
            current is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive)
            Invalidate();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        StopActiveRun();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action.ActionId)
        {
            case "network.tab.select":
                SelectTab(action.SourceElementId);
                break;
            case "network.tab.previous":
            case "network.tab.next":
                ToggleTab();
                break;
            case "wifi.scan":
                await RequestScanAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "wifi.connect.item":
                if (SelectNetworkFromElementId(action.SourceElementId))
                    await ConnectSelectedNetworkAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "wifi.radio.toggle":
                await ToggleWifiRadioAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "bluetooth.radio.toggle":
                await ToggleBluetoothRadioAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "bluetooth.device.info":
                SelectBluetoothFromElementId(action.SourceElementId);
                SetFeedback("Bluetooth device connections are managed by Windows for each supported profile", false);
                break;
            case "retry":
                if (IsActive) StartActiveRun(ActiveCancellationToken);
                break;
        }
    }

    public override ValueTask<bool> OnControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Context == ControllerInputContext.OpenWidget &&
            input.FocusedElementId is { } focusedId)
        {
            // The host owns directional focus movement. Remember the last row
            // it reports so switching tabs, returning to the tray, or reopening
            // this live worker restores each view independently.
            if (ActiveTab == NetworkControlsTab.Wifi)
                SelectNetworkFromElementId(focusedId);
            else
                SelectBluetoothFromElementId(focusedId);
        }
        return base.OnControllerInputAsync(input, cancellationToken);
    }

    private RowElement RenderConnectionCard(WidgetNetworkStatus status)
    {
        var title = status.ActiveProfileName ?? status.Transport switch
        {
            WidgetNetworkTransportKind.Ethernet => "Wired connection",
            WidgetNetworkTransportKind.Wifi => "Wi-Fi connection",
            WidgetNetworkTransportKind.Other => "Network connection",
            _ => "No active network",
        };
        var transport = status.Transport switch
        {
            WidgetNetworkTransportKind.Ethernet => "ETHERNET",
            WidgetNetworkTransportKind.Wifi => "WI-FI",
            WidgetNetworkTransportKind.Other => "NETWORK",
            _ => "OFFLINE",
        };
        var connectivity = status.Connectivity switch
        {
            WidgetNetworkConnectivity.Internet => "Internet access",
            WidgetNetworkConnectivity.Local => "Local network only",
            _ => "Not connected",
        };
        var stateClass = status.Connectivity switch
        {
            WidgetNetworkConnectivity.Internet => "is-online",
            WidgetNetworkConnectivity.Local => "is-warning",
            _ => "is-error",
        };
        return UI.Row("network.connection.card",
                UI.Icon(WidgetGlyph.Connection, "network.connection.icon", transport)
                    .Classes("network-connection-icon"),
                UI.Stack("network.connection.copy",
                        UI.Text(title, "network.connection.title", title).Classes("network-card-title"),
                        UI.Row("network.connection.meta",
                                UI.Text(transport, "network.connection.transport", transport)
                                    .Classes("network-card-state", stateClass),
                                UI.Text(connectivity, "network.connection.detail", connectivity)
                                    .Classes("network-card-detail"))
                            .Classes("network-connection-meta"))
                    .Classes("network-connection-copy"),
                status.SignalPercent is { } signal
                    ? UI.Text($"{signal}%", "network.signal.value", $"Signal {signal} percent")
                        .Classes("network-signal-value")
                    : UI.Text("", "network.signal.value", "Signal unavailable")
                        .Classes("network-signal-value", "is-empty"))
            .Classes("network-connection-card");
    }

    private RowElement RenderRadioControl(WidgetWifiRadio radio, bool busy)
    {
        var isOn = radio.State == WidgetWifiRadioState.On;
        var label = radio.State switch
        {
            WidgetWifiRadioState.On => "Wi-Fi on",
            WidgetWifiRadioState.Off => "Wi-Fi off",
            WidgetWifiRadioState.HardwareDisabled => "Wi-Fi hardware disabled",
            WidgetWifiRadioState.NoAdapter => "No Wi-Fi adapter",
            _ => "Wi-Fi unavailable",
        };
        var detail = radio.State switch
        {
            WidgetWifiRadioState.HardwareDisabled => "Use the device's wireless switch to enable it",
            WidgetWifiRadioState.NoAdapter => "No controllable wireless adapter was reported",
            WidgetWifiRadioState.Unavailable => "Windows could not read the software radio",
            _ => "Software radio",
        };
        var toggle = UI.ToggleButton(label, isOn, "wifi.radio.toggle", "network.wifi.radio")
            .Icon(WidgetGlyph.Wifi, label)
            .Busy(busy)
            .Disabled(LifecycleState != WidgetLifecycleState.Interactive || busy || !radio.CanControl)
            .FocusUp("network.tab.wifi")
            .FocusDown("network.wifi.scan")
            .FocusLeft("network.wifi.radio")
            .FocusRight("network.wifi.radio")
            .Classes("network-radio-toggle", isOn ? "is-on" : "is-off");
        return UI.Row("network.wifi.radio.row",
                UI.Stack("network.wifi.radio.copy",
                        UI.Text("WI-FI", "network.wifi.radio.label", "Wi-Fi radio")
                            .Classes("network-section-label"),
                        UI.Text(detail, "network.wifi.radio.detail", detail)
                            .Classes("network-section-summary"))
                    .Classes("network-radio-copy"),
                toggle)
            .Classes("network-radio-row");
    }

    private static RowElement RenderNotice((string Title, string Detail, bool IsError) note) =>
        UI.Row("network.wifi.note",
                UI.Icon(WidgetGlyph.Warning, "network.wifi.note.icon", note.Title)
                    .Classes("network-notice-icon", note.IsError ? "is-error" : "is-warning"),
                UI.Stack("network.wifi.note.copy",
                        UI.Text(note.Title, "network.wifi.note.title", note.Title)
                            .Classes("network-card-state", note.IsError ? "is-error" : "is-warning"),
                        UI.Text(note.Detail, "network.wifi.note.detail", note.Detail)
                            .Classes("network-card-detail"))
                    .Classes("network-notice-copy"))
            .Classes("network-wifi-note");

    private static StackElement RenderWifiState(WidgetWifiScanState scanState)
    {
        var (title, detail) = scanState switch
        {
            WidgetWifiScanState.NotScanned => (
                "Ready to scan",
                "Choose Scan for networks. Scanning only happens when you request it."),
            WidgetWifiScanState.Scanning => (
                "Looking nearby",
                "Windows is completing one bounded Wi-Fi scan."),
            WidgetWifiScanState.PreciseLocationDenied => (
                "Location permission required",
                "Windows requires precise-location access to list nearby Wi-Fi networks. Enable it in Windows Settings, then scan again."),
            WidgetWifiScanState.Unavailable => (
                "Wi-Fi scan unavailable",
                "Windows could not complete this scan. Check the Wi-Fi radio and try again."),
            _ => (
                "No networks found",
                "No nearby Wi-Fi networks were reported in this scan."),
        };
        return UI.Stack("network.wifi.state",
                UI.Text(title, "network.wifi.state.title", title).Classes("network-state-title"),
                UI.Text(detail, "network.wifi.state.help", detail).Classes("network-help"))
            .Classes("network-state-card");
    }

    private IReadOnlyList<WidgetElement> RenderNetworkRows(
        IReadOnlyList<WidgetAvailableWifiNetwork> networks,
        bool controlBusy,
        string? pendingId)
    {
        var interactive = LifecycleState == WidgetLifecycleState.Interactive;
        var ids = networks.Select(network => NetworkElementId(network.NetworkId)).ToArray();
        var rows = new WidgetElement[networks.Count];
        for (var index = 0; index < networks.Count; index++)
        {
            var network = networks[index];
            var id = ids[index];
            var isPending = string.Equals(network.NetworkId, pendingId, StringComparison.Ordinal);
            var (state, detail) = NetworkMetadata(network, isPending);
            var actionLabel = NetworkActionLabel(network);
            var button = UI.Button(network.DisplayName, "wifi.connect.item", id)
                .Icon(network.IsConnected ? WidgetGlyph.Check : WidgetGlyph.Wifi,
                    $"{network.DisplayName}. {state}. Signal {network.SignalPercent} percent. {actionLabel}")
                .Disabled(!interactive || controlBusy || network.IsConnected)
                .Shortcut(ControllerButton.X, actionId: "wifi.connect.item")
                .FocusUp(index == 0 ? "network.wifi.scan" : ids[index - 1])
                .FocusLeft(id)
                .FocusRight(id)
                .Classes("network-profile-button",
                    network.IsConnected ? "is-connected" : "is-available",
                    isPending ? "is-pending" : "is-ready");
            if (index < networks.Count - 1) button = button.FocusDown(ids[index + 1]);
            rows[index] = UI.Stack($"{id}.row",
                    button,
                    UI.Row($"{id}.meta",
                            UI.Text(state, $"{id}.state", state).Classes(
                                "network-profile-state",
                                isPending ? "is-connecting" : network.IsConnected ? "is-connected" : "is-available"),
                            UI.Text(detail, $"{id}.detail", detail).Classes("network-profile-detail"))
                        .Classes("network-profile-meta"))
                .Classes("network-profile-row",
                    "is-unselected",
                    isPending ? "is-pending" : "is-ready");
        }
        return rows;
    }

    private IEnumerable<WidgetElement> RenderBluetoothSection(
        WidgetBluetoothSnapshot? snapshot,
        string message,
        bool messageIsError,
        bool busy)
    {
        var radioState = snapshot?.RadioState ?? WidgetBluetoothRadioState.Unavailable;
        var isOn = radioState == WidgetBluetoothRadioState.On;
        var radioLabel = radioState switch
        {
            WidgetBluetoothRadioState.On => "Bluetooth on",
            WidgetBluetoothRadioState.Off => "Bluetooth off",
            WidgetBluetoothRadioState.HardwareDisabled => "Bluetooth hardware disabled",
            WidgetBluetoothRadioState.NoAdapter => "No Bluetooth adapter",
            _ => "Bluetooth unavailable",
        };
        var devices = snapshot?.DiscoveryState == WidgetBluetoothDiscoveryState.Ready
            ? snapshot.Devices
            : [];
        var firstDeviceId = devices.Count == 0
            ? null
            : BluetoothElementId(devices[0].DeviceId);
        var toggle = UI.ToggleButton(
                radioLabel, isOn, "bluetooth.radio.toggle", "network.bluetooth.radio")
            .Icon(WidgetGlyph.Connection, radioLabel)
            .Busy(busy)
            .Disabled(LifecycleState != WidgetLifecycleState.Interactive || busy ||
                snapshot?.CanControlRadio != true)
            .FocusUp("network.tab.bluetooth")
            .FocusLeft("network.bluetooth.radio")
            .FocusRight("network.bluetooth.radio")
            .Classes("network-radio-toggle", "network-bluetooth-toggle",
                isOn ? "is-on" : "is-off");
        if (firstDeviceId is not null) toggle = toggle.FocusDown(firstDeviceId);

        yield return UI.Row("network.bluetooth.heading",
                UI.Stack("network.bluetooth.heading.copy",
                        UI.Text("BLUETOOTH", "network.bluetooth.label", "Bluetooth")
                            .Classes("network-section-label"),
                        UI.Text(message, "network.bluetooth.summary", message)
                            .Classes("network-section-summary",
                                messageIsError ? "is-error" : "is-neutral"))
                    .Classes("network-section-copy"),
                toggle)
            .Classes("network-radio-row", "network-bluetooth-row");

        if (snapshot is null || snapshot.DiscoveryState != WidgetBluetoothDiscoveryState.Ready)
        {
            var detail = snapshot?.DiscoveryState == WidgetBluetoothDiscoveryState.Enumerating
                ? "Windows is discovering paired and nearby Bluetooth devices."
                : message;
            yield return UI.Stack("network.bluetooth.state",
                    UI.Text(snapshot?.DiscoveryState == WidgetBluetoothDiscoveryState.Enumerating
                            ? "Discovering devices" : "Bluetooth device list unavailable",
                        "network.bluetooth.state.title", "Bluetooth device status")
                        .Classes("network-state-title"),
                    UI.Text(detail, "network.bluetooth.state.help", detail)
                        .Classes("network-help", messageIsError ? "is-error" : "is-neutral"))
                .Classes("network-state-card", "network-bluetooth-state");
            yield break;
        }

        if (devices.Count == 0)
        {
            yield return UI.Stack("network.bluetooth.state",
                    UI.Text(isOn ? "No Bluetooth devices found" : "Bluetooth is off",
                        "network.bluetooth.state.title", "Bluetooth device status")
                        .Classes("network-state-title"),
                    UI.Text(isOn
                            ? "Paired devices and nearby devices in pairing mode will appear here."
                            : "Turn Bluetooth on to discover nearby devices.",
                        "network.bluetooth.state.help", "Bluetooth device help")
                        .Classes("network-help"))
                .Classes("network-state-card", "network-bluetooth-state");
            yield break;
        }

        var ids = devices.Select(device => BluetoothElementId(device.DeviceId)).ToArray();
        var rows = new WidgetElement[devices.Count];
        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var id = ids[index];
            var state = device.IsConnected ? "CONNECTED" : device.IsPaired ? "PAIRED" : "NEARBY";
            var detail = device.IsConnected
                ? "Connected by a supported Windows Bluetooth profile"
                : device.IsPaired
                    ? device.IsPresent
                        ? "Available · press A to manage the connection in Windows"
                        : "Not currently nearby"
                    : "Press A to pair through Windows";
            var button = UI.Button(device.DisplayName, "bluetooth.device.info", id)
                .Icon(device.IsConnected ? WidgetGlyph.Check : WidgetGlyph.Connection,
                    $"{device.DisplayName}. {state}. {detail}")
                .FocusUp(index == 0 ? "network.bluetooth.radio" : ids[index - 1])
                .FocusLeft(id)
                .FocusRight(id)
                .Classes("network-profile-button", "network-bluetooth-device",
                    device.IsConnected ? "is-connected" : "is-available");
            if (index < devices.Count - 1) button = button.FocusDown(ids[index + 1]);
            rows[index] = UI.Stack($"{id}.row",
                    button,
                    UI.Row($"{id}.meta",
                            UI.Text(state, $"{id}.state", state)
                                .Classes("network-profile-state",
                                    device.IsConnected ? "is-connected" : "is-available"),
                            UI.Text(detail, $"{id}.detail", detail)
                                .Classes("network-profile-detail"))
                        .Classes("network-profile-meta"))
                .Classes("network-profile-row", "network-bluetooth-device-row",
                    "is-unselected");
        }
        foreach (var row in rows) yield return row;
    }

    private void SelectTab(string sourceElementId)
    {
        var next = sourceElementId switch
        {
            "network.tab.wifi" => NetworkControlsTab.Wifi,
            "network.tab.bluetooth" => NetworkControlsTab.Bluetooth,
            _ => (NetworkControlsTab?)null,
        };
        if (next is null) return;
        lock (_stateLock) _activeTab = next.Value;
        Invalidate();
    }

    private void ToggleTab()
    {
        lock (_stateLock)
            _activeTab = _activeTab == NetworkControlsTab.Wifi
                ? NetworkControlsTab.Bluetooth
                : NetworkControlsTab.Wifi;
        Invalidate();
    }

    private static (string State, string Detail) NetworkMetadata(
        WidgetAvailableWifiNetwork network, bool pending)
    {
        if (pending) return ("CONNECTING", $"Signal {network.SignalPercent}%");
        if (network.IsConnected) return ("CONNECTED", $"Signal {network.SignalPercent}%");
        if (network.CredentialRequired) return ("PASSWORD REQUIRED", $"Signal {network.SignalPercent}%");
        if (network.HasSavedProfile) return ("SAVED", $"Signal {network.SignalPercent}%");
        return network.Security == WidgetWifiSecurityKind.Open
            ? ("OPEN", $"Signal {network.SignalPercent}%")
            : ("AVAILABLE", $"Signal {network.SignalPercent}%");
    }

    private static string NetworkActionLabel(WidgetAvailableWifiNetwork network)
    {
        if (network.IsConnected) return "Connected";
        if (network.CredentialRequired)
            return "A password is required in Windows Settings";
        if ((network.Security is WidgetWifiSecurityKind.Enterprise or WidgetWifiSecurityKind.Unknown) &&
            !network.HasSavedProfile)
            return "This authentication method is not supported in the overlay";
        return "Press A or X to connect";
    }

    private async Task ObserveNetworkAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            await using var statusSubscription = await HostServices.Network
                .OpenStatusSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            await using var wifiSubscription = await HostServices.Network
                .OpenAvailableWifiSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            await using var radioSubscription = await HostServices.Network
                .OpenWifiRadioSubscriptionAsync(cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref _statusFetchCount);
            var statusTask = HostServices.Network.GetStatusAsync(cancellationToken).AsTask();
            Interlocked.Increment(ref _wifiFetchCount);
            var wifiTask = HostServices.Network.GetAvailableWifiAsync(cancellationToken).AsTask();
            var radioTask = HostServices.Network.GetWifiRadioAsync(cancellationToken).AsTask();
            await Task.WhenAll(statusTask, wifiTask, radioTask).ConfigureAwait(false);
            if (!IsCurrentRun(generation, cancellationToken)) return;
            ApplySnapshot(statusTask.Result, wifiTask.Result, radioTask.Result, generation);

            using var eventLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var statusLoop = ObserveStatusEventsAsync(statusSubscription, generation, eventLifetime.Token);
            var wifiLoop = ObserveWifiEventsAsync(wifiSubscription, generation, eventLifetime.Token);
            var radioLoop = ObserveRadioEventsAsync(radioSubscription, generation, eventLifetime.Token);
            await Task.WhenAny(statusLoop, wifiLoop, radioLoop).ConfigureAwait(false);
            eventLifetime.Cancel();
            try { await Task.WhenAll(statusLoop, wifiLoop, radioLoop).ConfigureAwait(false); }
            catch (OperationCanceledException) when (eventLifetime.IsCancellationRequested) { }

            if (IsCurrentRun(generation, cancellationToken))
                SetProviderError(NetworkControlsViewState.ChannelClosed,
                    "Network service channel closed", generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityUnavailableException)
        {
            SetProviderError(NetworkControlsViewState.ServiceUnavailable,
                "Host Wi-Fi service unavailable", generation);
        }
        catch (WidgetCapabilityException exception)
        {
            var (state, message) = MapCapabilityFailure(exception.ErrorCode);
            SetProviderError(state, message, generation);
        }
        catch (Exception)
        {
            SetProviderError(NetworkControlsViewState.Error,
                "Network provider returned an unexpected error", generation);
        }
    }

    private async Task ObserveStatusEventsAsync(
        IWidgetCapabilitySubscription<WidgetNetworkStatusChanged> subscription,
        long generation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, cancellationToken)) return;
            ApplyStatus(change.Status, generation);
        }
    }

    private async Task ObserveWifiEventsAsync(
        IWidgetCapabilitySubscription<WidgetAvailableWifiNetworksChanged> subscription,
        long generation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, cancellationToken)) return;
            ApplyWifi(change.Snapshot, generation);
        }
    }

    private async Task ObserveRadioEventsAsync(
        IWidgetCapabilitySubscription<WidgetWifiRadioChanged> subscription,
        long generation,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!IsCurrentRun(generation, cancellationToken)) return;
            ApplyRadio(change.Radio, generation);
        }
    }

    private async Task ObserveBluetoothAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            await using var subscription = await HostServices.Network
                .OpenBluetoothSubscriptionAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _bluetoothFetchCount);
            var snapshot = await HostServices.Network.GetBluetoothAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrentRun(generation, cancellationToken)) return;
            ApplyBluetooth(snapshot, generation);
            await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!IsCurrentRun(generation, cancellationToken)) return;
                ApplyBluetooth(change.Snapshot, generation);
            }
            if (IsCurrentRun(generation, cancellationToken))
                SetBluetoothUnavailable("Bluetooth service channel closed", true, generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WidgetCapabilityUnavailableException)
        {
            SetBluetoothUnavailable("Host Bluetooth service unavailable", true, generation);
        }
        catch (WidgetCapabilityException exception)
        {
            var message = exception.ErrorCode switch
            {
                "permission_denied" or "capability_not_declared" =>
                    "Allow Bluetooth device access in Settings → Permissions",
                "lifecycle_denied" => "Bluetooth is paused while this widget is in the background",
                "channel_closed" => "Bluetooth service channel closed",
                _ => "Windows Bluetooth service unavailable",
            };
            SetBluetoothUnavailable(message, true, generation);
        }
        catch (Exception)
        {
            SetBluetoothUnavailable("Windows Bluetooth service unavailable", true, generation);
        }
    }

    private void ApplyBluetooth(WidgetBluetoothSnapshot incoming, long generation)
    {
        var snapshot = NormalizeBluetooth(incoming);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _authoritativeBluetooth = snapshot;
            _bluetooth = snapshot;
            _bluetoothBusy = false;
            _bluetoothIsError = false;
            _bluetoothMessage = snapshot.DiscoveryState switch
            {
                WidgetBluetoothDiscoveryState.Enumerating => "Discovering devices…",
                WidgetBluetoothDiscoveryState.Unavailable => "Device discovery unavailable",
                _ when snapshot.Devices.Count == 0 => "No paired or nearby devices",
                _ => $"{snapshot.Devices.Count} paired or nearby",
            };
            ReconcileBluetoothSelectionLocked();
        }
        Invalidate();
    }

    private void SetBluetoothUnavailable(
        string message, bool error, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _bluetooth = new WidgetBluetoothSnapshot(
                WidgetBluetoothRadioState.Unavailable, false,
                WidgetBluetoothDiscoveryState.Unavailable, []);
            _authoritativeBluetooth = _bluetooth;
            _bluetoothBusy = false;
            _bluetoothMessage = message;
            _bluetoothIsError = error;
            _selectedBluetoothDeviceId = null;
            _selectedBluetoothIndex = 0;
        }
        Invalidate();
    }

    private void ApplySnapshot(
        WidgetNetworkStatus incomingStatus,
        WidgetAvailableWifiNetworks incomingWifi,
        WidgetWifiRadio incomingRadio,
        long generation)
    {
        var status = NormalizeStatus(incomingStatus);
        var wifi = NormalizeWifi(incomingWifi);
        var radio = NormalizeRadio(incomingRadio);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _authoritativeStatus = status;
            _authoritativeWifiSnapshot = wifi;
            _authoritativeWifiRadio = radio;
            _networkStatus = status;
            _wifiSnapshot = wifi;
            _wifiRadio = radio;
            ReconcileSelectionLocked();
            ReconcileCommandStateLocked();
        }
        Invalidate();
    }

    private void ApplyStatus(WidgetNetworkStatus incoming, long generation)
    {
        var status = NormalizeStatus(incoming);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _authoritativeStatus = status;
            _networkStatus = status;
            ReconcileCommandStateLocked();
        }
        Invalidate();
    }

    private void ApplyWifi(WidgetAvailableWifiNetworks incoming, long generation)
    {
        var wifi = NormalizeWifi(incoming);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _authoritativeWifiSnapshot = wifi;
            _wifiSnapshot = wifi;
            _scanBusy = wifi.ScanState == WidgetWifiScanState.Scanning;
            ReconcileSelectionLocked();
            ReconcileCommandStateLocked();
        }
        Invalidate();
    }

    private void ApplyRadio(WidgetWifiRadio incoming, long generation)
    {
        var radio = NormalizeRadio(incoming);
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _authoritativeWifiRadio = radio;
            _wifiRadio = radio;
            _radioBusy = false;
            ReconcileCommandStateLocked();
        }
        Invalidate();
    }

    private void ReconcileSelectionLocked()
    {
        var networks = _wifiSnapshot?.Networks ?? [];
        var priorIndex = _selectedIndex;
        var retained = _selectedNetworkId is null ? -1 : IndexOf(networks, network =>
            string.Equals(network.NetworkId, _selectedNetworkId, StringComparison.Ordinal));
        if (retained < 0 && _networkStatus?.AttemptProfileId is { } attemptId)
            retained = IndexOf(networks, network =>
                string.Equals(network.NetworkId, attemptId, StringComparison.Ordinal));
        if (retained < 0)
            retained = IndexOf(networks, network => network.IsConnected);
        _selectedIndex = retained >= 0
            ? retained
            : Math.Clamp(priorIndex, 0, Math.Max(0, networks.Count - 1));
        _selectedNetworkId = networks.Count == 0 ? null : networks[_selectedIndex].NetworkId;
    }

    private void ReconcileCommandStateLocked()
    {
        if (_networkStatus is null || _wifiSnapshot is null || _wifiRadio is null) return;
        _scanBusy = _wifiSnapshot.ScanState == WidgetWifiScanState.Scanning;
        switch (_networkStatus.ConnectionAttemptState)
        {
            case WidgetNetworkConnectionAttemptState.Connecting:
                _pendingNetworkId = _networkStatus.AttemptProfileId;
                _controlBusy = _pendingNetworkId is not null;
                _status = _pendingNetworkId is { } pending
                    ? $"Connecting to {NetworkNameLocked(pending)}…"
                    : "Connecting to Wi-Fi…";
                _statusIsError = false;
                break;
            case WidgetNetworkConnectionAttemptState.Failed:
                _pendingNetworkId = null;
                _controlBusy = false;
                _status = "Could not connect · previous connection retained";
                _statusIsError = true;
                break;
            default:
                _pendingNetworkId = null;
                _controlBusy = false;
                _status = LiveStatus(_networkStatus, _wifiSnapshot);
                _statusIsError = false;
                break;
        }
        _viewState = DeriveViewState(_networkStatus, _wifiSnapshot);
    }

    private async ValueTask RequestScanAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetFeedback("Open Network Controls before scanning", false);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_scanBusy || _controlBusy || _networkStatus is null || !CanScan(_networkStatus)) return;
                _scanBusy = true;
                _status = "Requesting one Wi-Fi scan…";
                _statusIsError = false;
                _wifiSnapshot = new WidgetAvailableWifiNetworks(WidgetWifiScanState.Scanning, []);
                _viewState = NetworkControlsViewState.Scanning;
            }
            Invalidate();
            try
            {
                Interlocked.Increment(ref _scanRequestCount);
                await HostServices.Network.RequestWifiScanAsync(linked.Token).ConfigureAwait(false);
                lock (_stateLock)
                {
                    // A very fast provider can publish Ready before the ACK is
                    // observed. Do not replace that terminal event with stale
                    // local "Scanning" copy.
                    if (_scanBusy)
                    {
                        _status = "Scanning for nearby Wi-Fi networks…";
                        _statusIsError = false;
                    }
                }
                Invalidate();
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreAuthoritative();
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                RestoreAuthoritative(MapScanFailure(exception.ErrorCode));
            }
            catch (Exception)
            {
                RestoreAuthoritative("Wi-Fi scan could not be started");
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask ToggleWifiRadioAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetFeedback("Open Network Controls before changing Wi-Fi", false);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            bool enabled;
            long generation;
            lock (_stateLock)
            {
                if (_radioBusy || _controlBusy || _wifiRadio is null || !_wifiRadio.CanControl) return;
                enabled = _wifiRadio.State != WidgetWifiRadioState.On;
                generation = _runGeneration;
                _radioBusy = true;
                _status = enabled ? "Turning Wi-Fi on…" : "Turning Wi-Fi off…";
                _statusIsError = false;
            }
            Invalidate();
            try
            {
                Interlocked.Increment(ref _radioControlCount);
                await HostServices.Network.SetWifiRadioAsync(enabled, linked.Token).ConfigureAwait(false);
                // One explicit read handles the idempotent/no-event edge; normal
                // reconciliation arrives through the radio changed subscription.
                var authoritative = await HostServices.Network.GetWifiRadioAsync(linked.Token)
                    .ConfigureAwait(false);
                ApplyRadio(authoritative, generation);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreWifiRadioAfterCancellation(generation);
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                SetWifiRadioFailure(MapRadioFailure(exception.ErrorCode), generation);
            }
            catch (Exception)
            {
                SetWifiRadioFailure("Wi-Fi radio could not be changed", generation);
            }
        }
        finally { _commandGate.Release(); }
    }

    private async ValueTask ToggleBluetoothRadioAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetFeedback("Open Network Controls before changing Bluetooth", false);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            bool enabled;
            long generation;
            lock (_stateLock)
            {
                if (_bluetoothBusy || _bluetooth is null || !_bluetooth.CanControlRadio) return;
                enabled = _bluetooth.RadioState != WidgetBluetoothRadioState.On;
                generation = _runGeneration;
                _bluetoothBusy = true;
                _bluetoothMessage = enabled ? "Turning Bluetooth on…" : "Turning Bluetooth off…";
                _bluetoothIsError = false;
            }
            Invalidate();
            try
            {
                Interlocked.Increment(ref _bluetoothRadioControlCount);
                await HostServices.Network.SetBluetoothRadioAsync(enabled, linked.Token)
                    .ConfigureAwait(false);
                var authoritative = await HostServices.Network.GetBluetoothAsync(linked.Token)
                    .ConfigureAwait(false);
                ApplyBluetooth(authoritative, generation);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreBluetoothAfterCancellation(generation);
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                var message = exception.ErrorCode switch
                {
                    "permission_denied" => "Bluetooth radio permission denied",
                    "platform_denied" => "Windows policy blocked Bluetooth radio control",
                    "hardware_disabled" => "Bluetooth is disabled by hardware or device policy",
                    "no_adapter" => "No Bluetooth adapter is available",
                    "partial_failure" => "Bluetooth changed partially · current Windows state refreshed",
                    _ => "Bluetooth radio could not be changed",
                };
                SetBluetoothFailure(message, generation);
            }
            catch (Exception)
            {
                SetBluetoothFailure("Bluetooth radio could not be changed", generation);
            }
        }
        finally { _commandGate.Release(); }
    }

    private void RestoreWifiRadioAfterCancellation(long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _wifiRadio = _authoritativeWifiRadio;
            _radioBusy = false;
            if (_networkStatus is not null && _wifiSnapshot is not null)
            {
                _status = LiveStatus(_networkStatus, _wifiSnapshot);
                _statusIsError = false;
            }
        }
        Invalidate();
    }

    private void SetWifiRadioFailure(string message, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _wifiRadio = _authoritativeWifiRadio;
            _radioBusy = false;
            _status = message;
            _statusIsError = true;
        }
        Invalidate();
    }

    private void RestoreBluetoothAfterCancellation(long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _bluetooth = _authoritativeBluetooth;
            _bluetoothBusy = false;
            _bluetoothIsError = false;
        }
        Invalidate();
    }

    private void SetBluetoothFailure(string message, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _bluetoothBusy = false;
            _bluetooth = _authoritativeBluetooth;
            _bluetoothMessage = message;
            _bluetoothIsError = true;
        }
        Invalidate();
    }

    private async ValueTask ConnectSelectedNetworkAsync(CancellationToken cancellationToken)
    {
        if (LifecycleState != WidgetLifecycleState.Interactive)
        {
            SetFeedback("Open Network Controls before connecting", false);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, ActiveCancellationToken);
        await _commandGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            WidgetAvailableWifiNetwork? selected;
            lock (_stateLock)
            {
                selected = SelectedNetworkLocked();
                if (selected is null || selected.IsConnected || _controlBusy || _scanBusy) return;
                if (selected.CredentialRequired)
                {
                    _status = $"{selected.DisplayName} needs a password · connect in Windows Settings";
                    _statusIsError = false;
                    selected = null;
                }
                else if ((selected.Security is WidgetWifiSecurityKind.Enterprise or WidgetWifiSecurityKind.Unknown) &&
                         !selected.HasSavedProfile)
                {
                    _status = "This Wi-Fi authentication method is not supported in the overlay";
                    _statusIsError = true;
                    selected = null;
                }
                else
                {
                    _pendingNetworkId = selected.NetworkId;
                    _controlBusy = true;
                    _status = $"Requesting {selected.DisplayName}…";
                    _statusIsError = false;
                }
            }
            Invalidate();
            if (selected is null) return;
            try
            {
                await HostServices.Network.ConnectAvailableWifiAsync(
                    selected.NetworkId, linked.Token).ConfigureAwait(false);
                lock (_stateLock)
                {
                    // Status completion can outrun the acknowledgement. Only
                    // retain the waiting copy while this exact attempt is live.
                    if (_controlBusy && string.Equals(
                            _pendingNetworkId, selected.NetworkId, StringComparison.Ordinal))
                    {
                        _status = $"Connection request accepted for {selected.DisplayName} · waiting for Windows";
                        _statusIsError = false;
                    }
                }
                Invalidate();
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RestoreAuthoritative();
                throw;
            }
            catch (WidgetCapabilityException exception)
            {
                RestoreAuthoritative(MapConnectFailure(exception.ErrorCode));
            }
            catch (Exception)
            {
                RestoreAuthoritative("Connection request failed · previous connection retained");
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private bool SelectNetworkFromElementId(string elementId)
    {
        lock (_stateLock)
        {
            var networks = _wifiSnapshot?.Networks ?? [];
            var index = IndexOf(networks, network =>
                string.Equals(NetworkElementId(network.NetworkId), elementId, StringComparison.Ordinal));
            if (index < 0) return false;
            _selectedIndex = index;
            _selectedNetworkId = networks[index].NetworkId;
            return true;
        }
    }

    private bool SelectBluetoothFromElementId(string elementId)
    {
        lock (_stateLock)
        {
            var devices = _bluetooth?.Devices ?? [];
            var index = IndexOf(devices, device => string.Equals(
                BluetoothElementId(device.DeviceId), elementId, StringComparison.Ordinal));
            if (index < 0) return false;
            _selectedBluetoothIndex = index;
            _selectedBluetoothDeviceId = devices[index].DeviceId;
            return true;
        }
    }

    private WidgetAvailableWifiNetwork? SelectedNetworkLocked()
    {
        var networks = _wifiSnapshot?.Networks ?? [];
        return _selectedIndex >= 0 && _selectedIndex < networks.Count ? networks[_selectedIndex] : null;
    }

    private string NetworkNameLocked(string id) =>
        (_wifiSnapshot?.Networks ?? []).FirstOrDefault(network =>
            string.Equals(network.NetworkId, id, StringComparison.Ordinal))?.DisplayName ?? "Wi-Fi network";

    private static string NetworkElementId(string opaqueId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueId));
        return $"network.wifi.item.{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private static string BluetoothElementId(string opaqueId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueId));
        return $"network.bluetooth.item.{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private void StartActiveRun(CancellationToken activeLifetime)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        long generation;
        lock (_stateLock)
        {
            previous = _runLifetime;
            current = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime);
            _runLifetime = current;
            generation = ++_runGeneration;
            RestoreAuthoritativeLocked();
            _viewState = NetworkControlsViewState.Loading;
            _status = "Loading network status…";
            _statusIsError = false;
        }
        previous?.Cancel();
        previous?.Dispose();
        Invalidate();
        _ = ObserveNetworkAsync(generation, current.Token);
        _ = ObserveBluetoothAsync(generation, current.Token);
    }

    private void StopActiveRun()
    {
        CancellationTokenSource? lifetime;
        lock (_stateLock)
        {
            ++_runGeneration;
            lifetime = _runLifetime;
            _runLifetime = null;
            RestoreAuthoritativeLocked();
        }
        lifetime?.Cancel();
        lifetime?.Dispose();
    }

    private void RestoreAuthoritative(string? message = null)
    {
        lock (_stateLock)
        {
            RestoreAuthoritativeLocked();
            if (message is not null)
            {
                _status = message;
                _statusIsError = true;
            }
        }
        Invalidate();
    }

    private void RestoreAuthoritativeLocked()
    {
        _networkStatus = _authoritativeStatus;
        _wifiSnapshot = _authoritativeWifiSnapshot;
        _wifiRadio = _authoritativeWifiRadio;
        _bluetooth = _authoritativeBluetooth;
        _pendingNetworkId = null;
        _controlBusy = false;
        _scanBusy = _wifiSnapshot?.ScanState == WidgetWifiScanState.Scanning;
        _radioBusy = false;
        _bluetoothBusy = false;
        if (_networkStatus is not null && _wifiSnapshot is not null && _wifiRadio is not null)
        {
            ReconcileSelectionLocked();
            _viewState = DeriveViewState(_networkStatus, _wifiSnapshot);
        }
    }

    private void SetFeedback(string message, bool error, bool preserveBusy = false)
    {
        lock (_stateLock)
        {
            _status = message;
            _statusIsError = error;
            if (!preserveBusy)
            {
                _controlBusy = false;
                _pendingNetworkId = null;
            }
        }
        Invalidate();
    }

    private WidgetView RenderProviderState(StackElement header, NetworkControlsViewState state)
    {
        var (title, help, buttonLabel, error) = state switch
        {
            NetworkControlsViewState.Initial => ("Ready when you are",
                "Open the widget to load Windows network status.", "Load network status", false),
            NetworkControlsViewState.Loading => ("Checking network status",
                "The host network provider is loading one current snapshot.", "Loading…", false),
            NetworkControlsViewState.PermissionDenied => ("Network permission required",
                "Grant nearby Wi-Fi read access in Settings → Permissions, then try again.", "Try again", true),
            NetworkControlsViewState.LifecycleDenied => ("Network status paused",
                "Return this widget to the foreground before requesting network status.", "Try again", true),
            NetworkControlsViewState.ChannelClosed => ("Network service disconnected",
                "The protected host channel closed. Reopen or retry the widget.", "Reconnect", true),
            NetworkControlsViewState.ServiceUnavailable => ("Wi-Fi service unavailable",
                "The host could not provide protected Wi-Fi services to this worker.", "Try again", true),
            _ => ("Network status could not be loaded",
                "The provider returned an unexpected error. No system details were exposed.", "Try again", true),
        };
        var retry = UI.Button(buttonLabel, "retry", "network.retry")
            .Icon(WidgetGlyph.Refresh, buttonLabel)
            .Busy(state == NetworkControlsViewState.Loading)
            .Disabled(state == NetworkControlsViewState.Loading)
            .Classes("network-retry-action");
        var root = UI.Stack("network.root", header,
                UI.Stack("network.state.card",
                        UI.Text(title, "network.state.title", title).Classes("network-state-title"),
                        UI.Text(help, "network.state.help", help)
                            .Classes("network-help", error ? "is-error" : "is-neutral"),
                        retry)
                    .Classes("network-state-card"))
            .InputScope("network-controls")
            .Classes("network-controls-widget", error ? "has-error" : "has-state");
        return new WidgetView(root, InitialFocusId: "network.retry", Surface: CompactSurface);
    }

    private bool IsCurrentRun(long generation, CancellationToken cancellationToken)
    {
        lock (_stateLock)
            return _runGeneration == generation && !cancellationToken.IsCancellationRequested;
    }

    private void SetProviderError(NetworkControlsViewState state, string message, long generation)
    {
        lock (_stateLock)
        {
            if (_runGeneration != generation) return;
            _networkStatus = null;
            _authoritativeStatus = null;
            _wifiSnapshot = null;
            _authoritativeWifiSnapshot = null;
            _wifiRadio = null;
            _authoritativeWifiRadio = null;
            _selectedNetworkId = null;
            _selectedIndex = 0;
            _pendingNetworkId = null;
            _controlBusy = false;
            _scanBusy = false;
            _radioBusy = false;
            _viewState = state;
            _status = message;
            _statusIsError = true;
        }
        Invalidate();
    }

    private static WidgetNetworkStatus NormalizeStatus(WidgetNetworkStatus status) => status with
    {
        ActiveProfileId = TrimOrNull(status.ActiveProfileId),
        ActiveProfileName = TrimOrNull(status.ActiveProfileName),
        AttemptProfileId = TrimOrNull(status.AttemptProfileId),
        SignalPercent = status.SignalPercent is { } signal ? Math.Clamp(signal, 0, 100) : null,
    };

    private static WidgetAvailableWifiNetworks NormalizeWifi(WidgetAvailableWifiNetworks snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var result = new List<WidgetAvailableWifiNetwork>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var network in snapshot.Networks ?? [])
        {
            if (network is null || string.IsNullOrWhiteSpace(network.NetworkId)) continue;
            var id = network.NetworkId.Trim();
            if (!ids.Add(id)) continue;
            result.Add(network with
            {
                NetworkId = id,
                DisplayName = string.IsNullOrWhiteSpace(network.DisplayName)
                    ? "Hidden network" : network.DisplayName.Trim(),
                SignalPercent = Math.Clamp(network.SignalPercent, 0, 100),
            });
        }
        if (snapshot.ScanState != WidgetWifiScanState.Ready) result.Clear();
        return snapshot with { Networks = result };
    }

    private static WidgetWifiRadio NormalizeRadio(WidgetWifiRadio radio)
    {
        ArgumentNullException.ThrowIfNull(radio);
        var canControl = radio.CanControl &&
            radio.State is WidgetWifiRadioState.On or WidgetWifiRadioState.Off;
        return radio with { CanControl = canControl };
    }

    private static WidgetBluetoothSnapshot NormalizeBluetooth(WidgetBluetoothSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var devices = new List<WidgetBluetoothDevice>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in snapshot.Devices ?? [])
        {
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceId) ||
                string.IsNullOrWhiteSpace(device.DisplayName)) continue;
            var id = device.DeviceId.Trim();
            if (!ids.Add(id)) continue;
            devices.Add(device with
            {
                DeviceId = id,
                DisplayName = device.DisplayName.Trim(),
                IsPaired = device.IsPaired || device.IsConnected,
            });
        }
        if (snapshot.DiscoveryState != WidgetBluetoothDiscoveryState.Ready) devices.Clear();
        var canControl = snapshot.CanControlRadio &&
            snapshot.RadioState is WidgetBluetoothRadioState.On or WidgetBluetoothRadioState.Off;
        return snapshot with { CanControlRadio = canControl, Devices = devices };
    }

    private void ReconcileBluetoothSelectionLocked()
    {
        var devices = _bluetooth?.Devices ?? [];
        var priorIndex = _selectedBluetoothIndex;
        var retained = _selectedBluetoothDeviceId is null ? -1 : IndexOf(devices, device =>
            string.Equals(device.DeviceId, _selectedBluetoothDeviceId, StringComparison.Ordinal));
        _selectedBluetoothIndex = retained >= 0
            ? retained
            : Math.Clamp(priorIndex, 0, Math.Max(0, devices.Count - 1));
        _selectedBluetoothDeviceId = devices.Count == 0
            ? null
            : devices[_selectedBluetoothIndex].DeviceId;
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int IndexOf<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        for (var index = 0; index < items.Count; index++)
            if (predicate(items[index])) return index;
        return -1;
    }

    private static NetworkControlsViewState DeriveViewState(
        WidgetNetworkStatus status, WidgetAvailableWifiNetworks wifi)
    {
        if (status.WirelessAvailability == WidgetNetworkWirelessAvailability.RadioOff)
            return NetworkControlsViewState.RadioOff;
        if (status.WirelessAvailability is WidgetNetworkWirelessAvailability.NoAdapter or
            WidgetNetworkWirelessAvailability.ServiceUnavailable)
            return NetworkControlsViewState.WirelessUnavailable;
        return wifi.ScanState switch
        {
            WidgetWifiScanState.NotScanned => NetworkControlsViewState.NotScanned,
            WidgetWifiScanState.Scanning => NetworkControlsViewState.Scanning,
            WidgetWifiScanState.PreciseLocationDenied => NetworkControlsViewState.PreciseLocationDenied,
            WidgetWifiScanState.Unavailable => NetworkControlsViewState.WirelessUnavailable,
            _ when wifi.Networks.Count == 0 => NetworkControlsViewState.EmptyNetworks,
            _ => NetworkControlsViewState.Ready,
        };
    }

    private static string LiveStatus(
        WidgetNetworkStatus status, WidgetAvailableWifiNetworks wifi)
    {
        var live = status.Connectivity switch
        {
            WidgetNetworkConnectivity.Internet => "Internet access",
            WidgetNetworkConnectivity.Local => "Local network only",
            _ => "Offline",
        };
        return wifi.ScanState switch
        {
            WidgetWifiScanState.NotScanned => $"{live} · scan when ready",
            WidgetWifiScanState.Scanning => $"{live} · scanning nearby",
            WidgetWifiScanState.PreciseLocationDenied => $"{live} · location permission required",
            WidgetWifiScanState.Ready => $"{live} · {wifi.Networks.Count} nearby · scan complete",
            _ => $"{live} · Wi-Fi scan unavailable",
        };
    }

    private static string ScanSummary(WidgetAvailableWifiNetworks wifi) => wifi.ScanState switch
    {
        WidgetWifiScanState.NotScanned => "Not scanned",
        WidgetWifiScanState.Scanning => "Scanning",
        WidgetWifiScanState.PreciseLocationDenied => "Permission required",
        WidgetWifiScanState.Unavailable => "Unavailable",
        _ => wifi.Networks.Count == 1 ? "1 nearby" : $"{wifi.Networks.Count} nearby",
    };

    private static bool CanScan(WidgetNetworkStatus status) =>
        status.WirelessAvailability == WidgetNetworkWirelessAvailability.Available;

    private static bool IsConnected(WidgetNetworkStatus? status) =>
        status?.Connectivity is WidgetNetworkConnectivity.Internet or WidgetNetworkConnectivity.Local;

    private static (string Title, string Detail, bool IsError)? WirelessNote(
        WidgetNetworkStatus status, WidgetWifiScanState scanState)
    {
        if (scanState == WidgetWifiScanState.PreciseLocationDenied ||
            status.DetailsAccess == WidgetNetworkDetailsAccess.PrivacyRestricted)
            return ("PRECISE LOCATION REQUIRED",
                "Windows requires precise-location access to show nearby Wi-Fi networks. The widget never receives location coordinates.", false);
        if (status.WirelessAvailability == WidgetNetworkWirelessAvailability.RadioOff)
            return ("WI-FI RADIO OFF", "Turn Wi-Fi on in Windows, then scan again.", false);
        if (status.WirelessAvailability == WidgetNetworkWirelessAvailability.NoAdapter)
            return ("NO WI-FI ADAPTER", "No wireless adapter is currently available.", false);
        if (status.WirelessAvailability == WidgetNetworkWirelessAvailability.ServiceUnavailable)
            return ("WI-FI SERVICE UNAVAILABLE", "Windows wireless service is unavailable.", true);
        return null;
    }

    private static (NetworkControlsViewState State, string Message) MapCapabilityFailure(
        string errorCode) => errorCode switch
        {
            "permission_denied" or "capability_revoked" or "capability_not_declared" =>
                (NetworkControlsViewState.PermissionDenied, "Nearby Wi-Fi permission denied"),
            "lifecycle_denied" =>
                (NetworkControlsViewState.LifecycleDenied, "Network request denied by widget lifecycle"),
            "channel_closed" =>
                (NetworkControlsViewState.ChannelClosed, "Network service channel closed"),
            "platform_unavailable" or "provider_unavailable" =>
                (NetworkControlsViewState.ServiceUnavailable, "Windows Wi-Fi provider unavailable"),
            _ => (NetworkControlsViewState.Error, "Network provider request failed"),
        };

    private static string MapScanFailure(string errorCode) => errorCode switch
    {
        "permission_denied" or "capability_revoked" or "capability_not_declared" =>
            "Nearby Wi-Fi permission denied",
        "lifecycle_denied" => "Wi-Fi scan paused by lifecycle",
        "provider_busy" => "A Wi-Fi scan is already in progress",
        "platform_unavailable" => "Windows Wi-Fi scan is unavailable",
        _ => "Wi-Fi scan could not be started",
    };

    private static string MapConnectFailure(string errorCode) => errorCode switch
    {
        "credential_required" => "This network needs a password · connect in Windows Settings",
        "unsupported_authentication" => "This Wi-Fi authentication method is not supported in the overlay",
        "resource_not_found" => "That scan result expired · scan again",
        "provider_busy" => "Another network connection is already in progress",
        "permission_denied" or "capability_revoked" or "capability_not_declared" =>
            "Wi-Fi connection permission denied",
        "lifecycle_denied" => "Network control paused by lifecycle",
        "platform_unavailable" => "Windows Wi-Fi connection control is unavailable",
        _ => "Connection request failed · previous connection retained",
    };

    private static string MapRadioFailure(string errorCode) => errorCode switch
    {
        "permission_denied" or "capability_revoked" or "capability_not_declared" =>
            "Wi-Fi radio control permission denied",
        "lifecycle_denied" => "Wi-Fi radio control paused by lifecycle",
        "wifi_hardware_disabled" => "Wi-Fi is disabled by a hardware switch",
        "wifi_radio_policy_denied" => "Windows policy denied Wi-Fi radio control",
        "wifi_radio_partial_failure" =>
            "Wi-Fi changed only partially · showing the current Windows state",
        "wifi_no_adapter" => "No Wi-Fi adapter is available",
        "platform_unavailable" => "Windows Wi-Fi radio control is unavailable",
        _ => "Wi-Fi radio could not be changed",
    };
}
