using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.NetworkControls;

internal static class NetworkControlsPresentation
{
    private static readonly WidgetSurfaceHints CompactSurface = new()
    {
        Mode = WidgetSurfaceMode.Compact,
        WidthMode = WidgetSurfaceAxisMode.Preferred,
        HeightMode = WidgetSurfaceAxisMode.Preferred,
        PreferredWidth = 560,
        PreferredHeight = 700,
        MinimumWidth = 320,
        MinimumHeight = 420,
    };

    internal static WidgetView Render(NetworkControlsPresentationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var header = UI.Stack("network.header",
                UI.Text("CONTROL CENTER", "network.eyebrow", "Control Center")
                    .Classes("network-eyebrow"),
                UI.Text("Network Controls", "network.title", "Network Controls")
                    .Classes("network-title"),
                UI.Text(state.Status, "network.status", state.Status).Classes(
                    "network-status",
                    state.StatusIsError ? "is-error" :
                        NetworkControlsProviderPolicy.IsConnected(state.NetworkStatus)
                            ? "is-live" : "is-neutral"))
            .Classes("network-header");

        if (state.NetworkStatus is null || state.Wifi is null || state.WifiRadio is null)
            return RenderProviderState(header, state.ViewState);

        if (state.ConnectionDetailsOpen)
            return RenderConnectionDetails(header, state.ConnectionDetails,
                state.ConnectionDetailsMessage);

        if (state.UnpairConfirmationDevice is { } removing)
        {
            var sheet = UI.ActionSheet(
                $"Remove {removing.DisplayName}?",
                "network.bluetooth.unpair.sheet",
                "network.bluetooth.unpair.scope",
                "bluetooth.device.unpair.cancel",
                [
                    new ActionSheetItem(
                        "network.bluetooth.unpair.cancel", "Cancel",
                        "bluetooth.device.unpair.cancel", null,
                        "Cancel device removal"),
                    new ActionSheetItem(
                        "network.bluetooth.unpair.confirm", "Remove device",
                        "bluetooth.device.unpair.confirm", WidgetGlyph.Warning,
                        $"Remove {removing.DisplayName}", ActionSheetItemTone.Danger,
                        IsDisabled: !state.Interactive || state.BluetoothBusy,
                        IsBusy: state.BluetoothBusy),
                ],
                "Windows will remove this pairing. You may need to pair the device again.");
            return new WidgetView(
                sheet, "network.bluetooth.unpair.cancel", Surface: CompactSurface);
        }

        var status = state.NetworkStatus;
        var wifi = state.Wifi;
        var radio = state.WifiRadio;
        var networks = wifi.ScanState == WidgetWifiScanState.Ready ? wifi.Networks : [];
        var selectedNetwork = networks.FirstOrDefault(network => string.Equals(
            network.NetworkId, state.SelectedNetworkId, StringComparison.Ordinal)) ??
            networks.FirstOrDefault();
        var wifiInitialFocus = selectedNetwork is null
            ? "network.wifi.scan"
            : NetworkControlsElementIds.Wifi(selectedNetwork.NetworkId);
        var bluetoothDevices = state.Bluetooth?.DiscoveryState ==
            WidgetBluetoothDiscoveryState.Ready ? state.Bluetooth.Devices : [];
        var selectedBluetooth = bluetoothDevices.FirstOrDefault(device => string.Equals(
            device.DeviceId, state.SelectedBluetoothDeviceId, StringComparison.Ordinal)) ??
            bluetoothDevices.FirstOrDefault();
        var bluetoothInitialFocus = selectedBluetooth is null
            ? "network.bluetooth.radio"
            : NetworkControlsElementIds.Bluetooth(selectedBluetooth.DeviceId);
        var activeInitialFocus = state.ActiveTab == NetworkControlsTab.Wifi
            ? wifiInitialFocus
            : bluetoothInitialFocus;

        var tabs = UI.SegmentedTabs("network.tabs",
            state.ActiveTab == NetworkControlsTab.Wifi
                ? "network.tab.wifi" : "network.tab.bluetooth",
            new SegmentedTab("network.tab.wifi", "Wi-Fi", "network.tab.select",
                "Wi-Fi controls"),
            new SegmentedTab("network.tab.bluetooth", "Bluetooth", "network.tab.select",
                "Bluetooth controls"));
        tabs = tabs with
        {
            Children = tabs.Children.Select(child => child is ButtonElement button
                ? button.FocusUp("network.details.open").FocusDown(activeInitialFocus)
                : child).ToArray(),
        };

        var content = new List<WidgetElement>
        {
            header,
            RenderConnectionCard(status),
            UI.Button("Connection details", "network.details.open", "network.details.open")
                .Icon(WidgetGlyph.Connection, "Show current connection details")
                .FocusUp("network.details.open")
                .FocusDown(state.ActiveTab == NetworkControlsTab.Wifi
                    ? "network.tab.wifi" : "network.tab.bluetooth")
                .Classes("network-details-action"),
            tabs,
        };

        if (state.ActiveTab == NetworkControlsTab.Bluetooth)
        {
            content.Add(UI.VerticalScroll("network.bluetooth.body.scroll",
                    RenderBluetoothSection(state).ToArray())
                .Classes("network-view-scroll", "network-bluetooth-view"));
        }
        else
        {
            var wifiContent = new List<WidgetElement>
            {
                RenderRadioControl(radio, state.RadioBusy, state.Interactive),
            };
            var note = WirelessNote(status, wifi.ScanState);
            if (note is not null) wifiContent.Add(RenderNotice(note.Value));

            var scanButton = UI.Button(
                    state.ScanBusy ? "Scanning…" :
                        wifi.ScanState == WidgetWifiScanState.NotScanned
                            ? "Scan for networks" : "Scan again",
                    "wifi.scan", "network.wifi.scan")
                .Icon(WidgetGlyph.Refresh, state.ScanBusy
                    ? "Scanning for nearby Wi-Fi networks"
                    : "Scan for nearby Wi-Fi networks")
                .Busy(state.ScanBusy)
                .Disabled(!state.Interactive || state.ScanBusy || state.ControlBusy ||
                    !NetworkControlsProviderPolicy.CanScan(status))
                .FocusUp("network.wifi.radio")
                .FocusLeft("network.wifi.scan")
                .FocusRight("network.wifi.scan")
                .Classes("network-scan-action");

            var wifiHeadingIndex = wifiContent.Count;
            wifiContent.Add(RenderWifiHeading(wifi, scanButton));

            if (networks.Count == 0)
                wifiContent.Add(RenderWifiState(wifi.ScanState));
            else
                wifiContent.AddRange(RenderNetworkRows(
                    networks, state.ControlBusy, state.PendingNetworkId, state.Interactive));

            if (networks.Count > 0)
                scanButton = scanButton.FocusDown(
                    NetworkControlsElementIds.Wifi(networks[0].NetworkId));
            wifiContent[wifiHeadingIndex] = RenderWifiHeading(wifi, scanButton);

            content.Add(UI.VerticalScroll("network.wifi.body.scroll", wifiContent.ToArray())
                .Classes("network-view-scroll", "network-wifi-view"));
        }

        var root = UI.Stack("network.root", content.ToArray())
            .InputScope("network-controls")
            .Shortcut(ControllerButton.LeftBumper, "network.tab.previous")
            .Shortcut(ControllerButton.RightBumper, "network.tab.next")
            .Classes("network-controls-widget",
                state.ActiveTab == NetworkControlsTab.Wifi ? "is-wifi" : "is-bluetooth",
                networks.Count == 0 ? "has-state" : "has-networks");
        return new WidgetView(root, InitialFocusId: activeInitialFocus, Surface: CompactSurface);
    }

    private static RowElement RenderConnectionCard(WidgetNetworkStatus status)
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
                        UI.Text(title, "network.connection.title", title)
                            .Classes("network-card-title"),
                        UI.Row("network.connection.meta",
                                UI.Text(transport, "network.connection.transport", transport)
                                    .Classes("network-card-state", stateClass),
                                UI.Text(connectivity, "network.connection.detail", connectivity)
                                    .Classes("network-card-detail"))
                            .Classes("network-connection-meta"))
                    .Classes("network-connection-copy"),
                status.SignalPercent is { } signal
                    ? UI.Text($"{signal}%", "network.signal.value",
                            $"Signal {signal} percent")
                        .Classes("network-signal-value")
                    : UI.Text("", "network.signal.value", "Signal unavailable")
                        .Classes("network-signal-value", "is-empty"))
            .Classes("network-connection-card");
    }

    private static WidgetView RenderConnectionDetails(
        StackElement header,
        WidgetNetworkConnectionDetails? details,
        string message)
    {
        var back = UI.Button("Back", "network.details.close", "network.details.back")
            .Icon(WidgetGlyph.Previous, "Back to Network Controls")
            .FocusUp("network.details.back")
            .FocusLeft("network.details.back")
            .FocusRight("network.details.back")
            .Classes("network-details-back");
        var content = new List<WidgetElement>
        {
            header,
            back,
            UI.Text(message, "network.details.message", message)
                .Classes("network-details-message"),
        };
        if (details is { State: WidgetNetworkConnectionDetailsState.Available })
        {
            content.Add(DetailRow("Transport", details.Transport switch
            {
                WidgetNetworkTransportKind.Ethernet => "Ethernet",
                WidgetNetworkTransportKind.Wifi => "Wi-Fi",
                WidgetNetworkTransportKind.Other => "Other network",
                _ => "Unavailable",
            }, "transport"));
            content.Add(DetailRow("Connectivity", details.Connectivity switch
            {
                WidgetNetworkConnectionDetailsConnectivity.Internet => "Internet",
                WidgetNetworkConnectionDetailsConnectivity.Constrained => "Constrained",
                WidgetNetworkConnectionDetailsConnectivity.Local => "Local only",
                _ => "Offline",
            }, "connectivity"));
            content.Add(DetailRow("IP addresses", Join(details.IpAddresses), "addresses"));
            content.Add(DetailRow("Default gateway", Join(details.DefaultGateways), "gateway"));
            content.Add(DetailRow("DNS servers", Join(details.DnsServers), "dns"));
        }
        else
        {
            content.Add(UI.Text(
                    "No adapter, route, Wi-Fi identity, or traffic data is exposed.",
                    "network.details.privacy",
                    "No sensitive adapter details are exposed")
                .Classes("network-details-privacy"));
        }
        var root = UI.Stack("network.details.root", content.ToArray())
            .InputScope("network-controls-details")
            .Shortcut(ControllerButton.B, "network.details.close")
            .Classes("network-controls-widget", "network-details-view");
        return new WidgetView(root, "network.details.back", Surface: CompactSurface);
    }

    private static RowElement DetailRow(string label, string value, string suffix) =>
        UI.Row($"network.details.{suffix}.row",
                UI.Text(label, $"network.details.{suffix}.label", label)
                    .Classes("network-details-label"),
                UI.Text(value, $"network.details.{suffix}.value", $"{label}: {value}")
                    .Classes("network-details-value"))
            .Classes("network-details-row");

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "Unavailable" : string.Join(" · ", values);

    private static RowElement RenderRadioControl(
        WidgetWifiRadio radio, bool busy, bool interactive)
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
            WidgetWifiRadioState.HardwareDisabled =>
                "Use the device's wireless switch to enable it",
            WidgetWifiRadioState.NoAdapter =>
                "No controllable wireless adapter was reported",
            WidgetWifiRadioState.Unavailable => "Windows could not read the software radio",
            _ => "Software radio",
        };
        var toggle = UI.Switch(label, isOn, "wifi.radio.toggle", "network.wifi.radio")
            .Icon(WidgetGlyph.Wifi, label)
            .Busy(busy)
            .Disabled(!interactive || busy || !radio.CanControl)
            .FocusUp("network.tab.wifi")
            .FocusDown("network.wifi.scan")
            .FocusLeft("network.wifi.radio")
            .FocusRight("network.wifi.radio")
            .AddClasses("network-radio-toggle", isOn ? "is-on" : "is-off");
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

    private static RowElement RenderNotice(
        (string Title, string Detail, bool IsError) note) =>
        UI.Row("network.wifi.note",
                UI.Icon(WidgetGlyph.Warning, "network.wifi.note.icon", note.Title)
                    .Classes("network-notice-icon", note.IsError ? "is-error" : "is-warning"),
                UI.Stack("network.wifi.note.copy",
                        UI.Text(note.Title, "network.wifi.note.title", note.Title)
                            .Classes("network-card-state",
                                note.IsError ? "is-error" : "is-warning"),
                        UI.Text(note.Detail, "network.wifi.note.detail", note.Detail)
                            .Classes("network-card-detail"))
                    .Classes("network-notice-copy"))
            .Classes("network-wifi-note");

    private static StackElement RenderWifiState(WidgetWifiScanState scanState)
    {
        var (title, detail) = scanState switch
        {
            WidgetWifiScanState.NotScanned => ("Ready to scan",
                "Choose Scan for networks. Scanning only happens when you request it."),
            WidgetWifiScanState.Scanning => ("Looking nearby",
                "Windows is completing one bounded Wi-Fi scan."),
            WidgetWifiScanState.PreciseLocationDenied => ("Location permission required",
                "Windows requires precise-location access to list nearby Wi-Fi networks. Enable it in Windows Settings, then scan again."),
            WidgetWifiScanState.Unavailable => ("Wi-Fi scan unavailable",
                "Windows could not complete this scan. Check the Wi-Fi radio and try again."),
            _ => ("No networks found",
                "No nearby Wi-Fi networks were reported in this scan."),
        };
        return UI.Stack("network.wifi.state",
                UI.Text(title, "network.wifi.state.title", title).Classes("network-state-title"),
                UI.Text(detail, "network.wifi.state.help", detail).Classes("network-help"))
            .Classes("network-state-card");
    }

    private static RowElement RenderWifiHeading(
        WidgetAvailableWifiNetworks wifi,
        ButtonElement scanButton) =>
        UI.Row("network.wifi.heading",
                UI.Stack("network.wifi.heading.copy",
                        UI.Text("AVAILABLE WI-FI", "network.wifi.label",
                                "Available Wi-Fi networks")
                            .Classes("network-section-label"),
                        UI.Text(ScanSummary(wifi), "network.wifi.summary", ScanSummary(wifi))
                            .Classes("network-section-summary"))
                    .Classes("network-section-copy"),
                scanButton)
            .Classes("network-section-heading");

    private static IReadOnlyList<WidgetElement> RenderNetworkRows(
        IReadOnlyList<WidgetAvailableWifiNetwork> networks,
        bool controlBusy,
        string? pendingId,
        bool interactive)
    {
        var ids = networks.Select(network =>
            NetworkControlsElementIds.Wifi(network.NetworkId)).ToArray();
        var rows = new WidgetElement[networks.Count];
        for (var index = 0; index < networks.Count; index++)
        {
            var network = networks[index];
            var id = ids[index];
            var isPending = string.Equals(network.NetworkId, pendingId, StringComparison.Ordinal);
            var (state, detail) = NetworkMetadata(network, isPending);
            var actionLabel = NetworkActionLabel(network);
            WidgetElement button;
            if (network.CredentialRequired &&
                network.Security == WidgetWifiSecurityKind.Personal)
            {
                var protectedEntry = UI.TextEntry(
                    string.Empty,
                    network.DisplayName,
                    "wifi.connect.protected",
                    id,
                    maximumLength: 63) with
                {
                    AccessibilityLabel =
                        $"{network.DisplayName}. {state}. Signal {network.SignalPercent} percent. {actionLabel}",
                    StyleClasses = [
                        "network-profile-button",
                        network.IsConnected ? "is-connected" : "is-available",
                        isPending ? "is-pending" : "is-ready",
                    ],
                };
                protectedEntry = protectedEntry
                    .Disabled(!interactive || controlBusy || network.IsConnected)
                    .FocusUp(index == 0 ? "network.wifi.scan" : ids[index - 1])
                    .FocusLeft(id)
                    .FocusRight(id);
                if (index < networks.Count - 1)
                    protectedEntry = protectedEntry.FocusDown(ids[index + 1]);
                button = protectedEntry;
            }
            else
            {
                var ordinary = UI.Button(network.DisplayName, "wifi.connect.item", id)
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
                if (index < networks.Count - 1) ordinary = ordinary.FocusDown(ids[index + 1]);
                button = ordinary;
            }
            rows[index] = UI.Stack($"{id}.row",
                    button,
                    UI.Row($"{id}.meta",
                            UI.Text(state, $"{id}.state", state).Classes(
                                "network-profile-state",
                                isPending ? "is-connecting" :
                                    network.IsConnected ? "is-connected" : "is-available"),
                            UI.Text(detail, $"{id}.detail", detail)
                                .Classes("network-profile-detail"))
                        .Classes("network-profile-meta"))
                .Classes("network-profile-row", "is-unselected",
                    isPending ? "is-pending" : "is-ready");
        }
        return rows;
    }

    private static IEnumerable<WidgetElement> RenderBluetoothSection(
        NetworkControlsPresentationState state)
    {
        var snapshot = state.Bluetooth;
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
            ? snapshot.Devices : [];
        var firstDeviceId = devices.Count == 0
            ? null : NetworkControlsElementIds.Bluetooth(devices[0].DeviceId);
        var toggle = UI.Switch(
                radioLabel, isOn, "bluetooth.radio.toggle", "network.bluetooth.radio")
            .Icon(WidgetGlyph.Connection, radioLabel)
            .Busy(state.BluetoothBusy)
            .Disabled(!state.Interactive || state.BluetoothBusy ||
                snapshot?.CanControlRadio != true)
            .FocusUp("network.tab.bluetooth")
            .FocusLeft("network.bluetooth.radio")
            .FocusRight("network.bluetooth.radio")
            .AddClasses("network-radio-toggle", "network-bluetooth-toggle",
                isOn ? "is-on" : "is-off");
        if (firstDeviceId is not null) toggle = toggle.FocusDown(firstDeviceId);

        yield return UI.Row("network.bluetooth.heading",
                UI.Stack("network.bluetooth.heading.copy",
                        UI.Text("BLUETOOTH", "network.bluetooth.label", "Bluetooth")
                            .Classes("network-section-label"),
                        UI.Text(state.BluetoothMessage, "network.bluetooth.summary",
                                state.BluetoothMessage)
                            .Classes("network-section-summary",
                                state.BluetoothIsError ? "is-error" : "is-neutral"))
                    .Classes("network-section-copy"),
                toggle)
            .Classes("network-radio-row", "network-bluetooth-row");

        if (snapshot is null || snapshot.DiscoveryState != WidgetBluetoothDiscoveryState.Ready)
        {
            var detail = snapshot?.DiscoveryState == WidgetBluetoothDiscoveryState.Enumerating
                ? "Windows is discovering paired and nearby Bluetooth devices."
                : state.BluetoothMessage;
            yield return UI.Stack("network.bluetooth.state",
                    UI.Text(snapshot?.DiscoveryState == WidgetBluetoothDiscoveryState.Enumerating
                            ? "Discovering devices" : "Bluetooth device list unavailable",
                        "network.bluetooth.state.title", "Bluetooth device status")
                        .Classes("network-state-title"),
                    UI.Text(detail, "network.bluetooth.state.help", detail)
                        .Classes("network-help",
                            state.BluetoothIsError ? "is-error" : "is-neutral"))
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

        var ids = devices.Select(device =>
            NetworkControlsElementIds.Bluetooth(device.DeviceId)).ToArray();
        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var id = ids[index];
            var deviceState = device.IsConnected
                ? "CONNECTED" : device.IsPaired ? "PAIRED" : "NEARBY";
            var detail = BluetoothDeviceDetail(device);
            var isPending = string.Equals(
                device.DeviceId, state.PendingBluetoothDeviceId, StringComparison.Ordinal);
            var actionId = device.IsPaired
                ? "bluetooth.device.unpair.open" : "bluetooth.device.pair";
            var actionLabel = device.IsPaired
                ? "Press A to remove. Press X to manage in Windows Bluetooth Settings"
                : "Press A to pair. Press X if Windows interaction is required";
            var button = UI.Button(device.DisplayName, actionId, id)
                .Icon(WidgetGlyph.Connection,
                    $"{device.DisplayName}. {deviceState}. {detail}. {actionLabel}")
                .Shortcut(ControllerButton.X, actionId: "bluetooth.device.manage")
                .Busy(isPending)
                .FocusUp(index == 0 ? "network.bluetooth.radio" : ids[index - 1])
                .FocusLeft(id)
                .FocusRight(id)
                .Classes("network-profile-button", "network-bluetooth-device",
                    device.IsConnected ? "is-connected" : "is-available",
                    isPending ? "is-pending" : "is-ready");
            if (index < devices.Count - 1) button = button.FocusDown(ids[index + 1]);
            yield return UI.Stack($"{id}.row",
                    button,
                    UI.Row($"{id}.meta",
                            UI.Text(deviceState, $"{id}.state", deviceState)
                                .Classes("network-profile-state",
                                    device.IsConnected ? "is-connected" : "is-available"),
                            UI.Text(detail, $"{id}.detail", detail)
                                .Classes("network-profile-detail"))
                        .Classes("network-profile-meta"))
                .Classes("network-profile-row", "network-bluetooth-device-row",
                    "is-unselected", isPending ? "is-pending" : "is-ready");
        }
    }

    private static (string State, string Detail) NetworkMetadata(
        WidgetAvailableWifiNetwork network, bool pending)
    {
        if (pending) return ("CONNECTING", $"Signal {network.SignalPercent}%");
        if (network.IsConnected) return ("CONNECTED", $"Signal {network.SignalPercent}%");
        if (network.CredentialRequired)
            return ("PASSWORD REQUIRED", $"Signal {network.SignalPercent}%");
        if (network.HasSavedProfile) return ("SAVED", $"Signal {network.SignalPercent}%");
        return network.Security == WidgetWifiSecurityKind.Open
            ? ("OPEN", $"Signal {network.SignalPercent}%")
            : ("AVAILABLE", $"Signal {network.SignalPercent}%");
    }

    private static string NetworkActionLabel(WidgetAvailableWifiNetwork network)
    {
        if (network.IsConnected) return "Connected";
        if (network.CredentialRequired)
            return network.Security == WidgetWifiSecurityKind.Personal
                ? "Press A to enter the password securely"
                : "Use Windows network settings for this authentication method";
        if ((network.Security is WidgetWifiSecurityKind.Enterprise or
                WidgetWifiSecurityKind.Unknown) && !network.HasSavedProfile)
            return "Use Windows network settings for this authentication method";
        return "Press A or X to connect";
    }

    private static string BluetoothDeviceDetail(WidgetBluetoothDevice device) =>
        device.IsConnected
            ? "Connected · press A to manage in Windows Settings"
            : device.IsPaired
                ? device.IsPresent
                    ? "Paired · press A to manage in Windows Settings"
                    : "Paired · not currently nearby · press A to manage"
                : "Nearby · press A to pair";

    private static string ScanSummary(WidgetAvailableWifiNetworks wifi) =>
        wifi.ScanState switch
        {
            WidgetWifiScanState.NotScanned => "Not scanned",
            WidgetWifiScanState.Scanning => "Scanning",
            WidgetWifiScanState.PreciseLocationDenied => "Permission required",
            WidgetWifiScanState.Unavailable => "Unavailable",
            _ => wifi.Networks.Count == 1 ? "1 nearby" : $"{wifi.Networks.Count} nearby",
        };

    private static (string Title, string Detail, bool IsError)? WirelessNote(
        WidgetNetworkStatus status,
        WidgetWifiScanState scanState)
    {
        if (scanState == WidgetWifiScanState.PreciseLocationDenied ||
            status.DetailsAccess == WidgetNetworkDetailsAccess.PrivacyRestricted)
            return ("PRECISE LOCATION REQUIRED",
                "Windows requires precise-location access to show nearby Wi-Fi networks. The widget never receives location coordinates.",
                false);
        if (status.WirelessAvailability == WidgetNetworkWirelessAvailability.RadioOff)
            return ("WI-FI RADIO OFF", "Turn Wi-Fi on in Windows, then scan again.", false);
        if (status.WirelessAvailability == WidgetNetworkWirelessAvailability.NoAdapter)
            return ("NO WI-FI ADAPTER", "No wireless adapter is currently available.", false);
        if (status.WirelessAvailability ==
            WidgetNetworkWirelessAvailability.ServiceUnavailable)
            return ("WI-FI SERVICE UNAVAILABLE",
                "Windows wireless service is unavailable.", true);
        return null;
    }

    private static WidgetView RenderProviderState(
        StackElement header,
        NetworkControlsViewState state)
    {
        var (title, help, buttonLabel, error) = state switch
        {
            NetworkControlsViewState.Initial => ("Ready when you are",
                "Open the widget to load Windows network status.",
                "Load network status", false),
            NetworkControlsViewState.Loading => ("Checking network status",
                "The host network provider is loading one current snapshot.",
                "Loading…", false),
            NetworkControlsViewState.PermissionDenied => ("Network permission required",
                "Grant nearby Wi-Fi read access in Settings → Permissions, then try again.",
                "Try again", true),
            NetworkControlsViewState.LifecycleDenied => ("Network status paused",
                "Return this widget to the foreground before requesting network status.",
                "Try again", true),
            NetworkControlsViewState.ChannelClosed => ("Network service disconnected",
                "The protected host channel closed. Reopen or retry the widget.",
                "Reconnect", true),
            NetworkControlsViewState.ServiceUnavailable => ("Wi-Fi service unavailable",
                "The host could not provide protected Wi-Fi services to this worker.",
                "Try again", true),
            _ => ("Network status could not be loaded",
                "The provider returned an unexpected error. No system details were exposed.",
                "Try again", true),
        };
        var retry = UI.Button(buttonLabel, "retry", "network.retry")
            .Icon(WidgetGlyph.Refresh, buttonLabel)
            .Busy(state == NetworkControlsViewState.Loading)
            .Disabled(state == NetworkControlsViewState.Loading)
            .Classes("network-retry-action");
        var root = UI.Stack("network.root", header,
                UI.Stack("network.state.card",
                        UI.Text(title, "network.state.title", title)
                            .Classes("network-state-title"),
                        UI.Text(help, "network.state.help", help)
                            .Classes("network-help", error ? "is-error" : "is-neutral"),
                        retry)
                    .Classes("network-state-card"))
            .InputScope("network-controls")
            .Classes("network-controls-widget", error ? "has-error" : "has-state");
        return new WidgetView(root, InitialFocusId: "network.retry", Surface: CompactSurface);
    }
}
