using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.NetworkControls;

internal readonly record struct NetworkControlsSelection(string? Id, int Index);

internal readonly record struct NetworkControlsWifiCommandState(
    string? PendingNetworkId,
    bool ControlBusy,
    bool ScanBusy,
    string Status,
    bool StatusIsError,
    NetworkControlsViewState ViewState);

internal static class NetworkControlsProviderPolicy
{
    internal static WidgetNetworkStatus Normalize(WidgetNetworkStatus status) => status with
    {
        ActiveProfileId = TrimOrNull(status.ActiveProfileId),
        ActiveProfileName = TrimOrNull(status.ActiveProfileName),
        AttemptProfileId = TrimOrNull(status.AttemptProfileId),
        SignalPercent = status.SignalPercent is { } signal ? Math.Clamp(signal, 0, 100) : null,
    };

    internal static WidgetAvailableWifiNetworks Normalize(
        WidgetAvailableWifiNetworks snapshot)
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

    internal static WidgetWifiRadio Normalize(WidgetWifiRadio radio)
    {
        ArgumentNullException.ThrowIfNull(radio);
        var canControl = radio.CanControl &&
            radio.State is WidgetWifiRadioState.On or WidgetWifiRadioState.Off;
        return radio with { CanControl = canControl };
    }

    internal static WidgetBluetoothSnapshot Normalize(WidgetBluetoothSnapshot snapshot)
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

    internal static WidgetBluetoothSnapshot ReconcileBluetoothResults(
        WidgetBluetoothSnapshot? previous, WidgetBluetoothSnapshot incoming, bool refreshResults = false)
    {
        if (refreshResults || previous is null || previous.DiscoveryState != WidgetBluetoothDiscoveryState.Ready ||
            incoming.DiscoveryState != WidgetBluetoothDiscoveryState.Ready) return incoming;
        var current = incoming.Devices.ToDictionary(device => device.DeviceId, StringComparer.Ordinal);
        var visible = new List<WidgetBluetoothDevice>();
        foreach (var prior in previous.Devices)
        {
            if (current.Remove(prior.DeviceId, out var updated)) visible.Add(updated);
            else if (!prior.IsPaired)
                visible.Add(prior with { IsConnected = false, IsPresent = false });
        }
        // Paired-device additions/removals are meaningful system changes. Nearby
        // discovery never inserts or reorders rows outside an explicit scan result.
        visible.AddRange(incoming.Devices.Where(device => device.IsPaired && current.ContainsKey(device.DeviceId)));
        return incoming with
        {
            // Stable partition: unavailable cached results cannot obscure live
            // nearby entries, but neither group is re-sorted on every event.
            Devices = visible.OrderBy(device => !device.IsPaired && !device.IsPresent).Take(64).ToArray(),
        };
    }

    internal static NetworkControlsSelection ReconcileWifiSelection(
        WidgetAvailableWifiNetworks? wifi,
        WidgetNetworkStatus? status,
        string? selectedId,
        int selectedIndex)
    {
        var networks = wifi?.Networks ?? [];
        var retained = selectedId is null ? -1 : IndexOf(networks, network =>
            string.Equals(network.NetworkId, selectedId, StringComparison.Ordinal));
        if (retained < 0 && status?.AttemptProfileId is { } attemptId)
            retained = IndexOf(networks, network =>
                string.Equals(network.NetworkId, attemptId, StringComparison.Ordinal));
        if (retained < 0)
            retained = IndexOf(networks, network => network.IsConnected);
        var index = retained >= 0
            ? retained
            : Math.Clamp(selectedIndex, 0, Math.Max(0, networks.Count - 1));
        return new(networks.Count == 0 ? null : networks[index].NetworkId, index);
    }

    internal static NetworkControlsSelection ReconcileBluetoothSelection(
        WidgetBluetoothSnapshot? snapshot,
        string? selectedId,
        int selectedIndex)
    {
        var devices = snapshot?.Devices ?? [];
        var retained = selectedId is null ? -1 : IndexOf(devices, device =>
            string.Equals(device.DeviceId, selectedId, StringComparison.Ordinal));
        var index = retained >= 0
            ? retained
            : Math.Clamp(selectedIndex, 0, Math.Max(0, devices.Count - 1));
        return new(devices.Count == 0 ? null : devices[index].DeviceId, index);
    }

    internal static NetworkControlsWifiCommandState ReconcileWifiCommand(
        WidgetNetworkStatus status,
        WidgetAvailableWifiNetworks wifi)
    {
        string? pendingId = null;
        var busy = false;
        string message;
        var error = false;
        switch (status.ConnectionAttemptState)
        {
            case WidgetNetworkConnectionAttemptState.Connecting:
                pendingId = status.AttemptProfileId;
                busy = pendingId is not null;
                var name = wifi.Networks.FirstOrDefault(network => string.Equals(
                    network.NetworkId, pendingId, StringComparison.Ordinal))?.DisplayName;
                message = pendingId is null
                    ? "Connecting to Wi-Fi…"
                    : $"Connecting to {name ?? "Wi-Fi network"}…";
                break;
            case WidgetNetworkConnectionAttemptState.Failed:
                message = "Could not connect · previous connection retained";
                error = true;
                break;
            default:
                message = LiveStatus(status, wifi);
                break;
        }
        return new(
            pendingId,
            busy,
            wifi.ScanState == WidgetWifiScanState.Scanning,
            message,
            error,
            DeriveViewState(status, wifi));
    }

    internal static NetworkControlsViewState DeriveViewState(
        WidgetNetworkStatus status,
        WidgetAvailableWifiNetworks wifi)
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
            WidgetWifiScanState.PreciseLocationDenied =>
                NetworkControlsViewState.PreciseLocationDenied,
            WidgetWifiScanState.Unavailable => NetworkControlsViewState.WirelessUnavailable,
            _ when wifi.Networks.Count == 0 => NetworkControlsViewState.EmptyNetworks,
            _ => NetworkControlsViewState.Ready,
        };
    }

    internal static string LiveStatus(
        WidgetNetworkStatus status,
        WidgetAvailableWifiNetworks wifi)
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
            WidgetWifiScanState.PreciseLocationDenied =>
                $"{live} · location permission required",
            WidgetWifiScanState.Ready =>
                $"{live} · {wifi.Networks.Count} nearby · scan complete",
            _ => $"{live} · Wi-Fi scan unavailable",
        };
    }

    internal static bool CanScan(WidgetNetworkStatus status) =>
        status.WirelessAvailability == WidgetNetworkWirelessAvailability.Available;

    internal static bool IsConnected(WidgetNetworkStatus? status) =>
        status?.Connectivity is WidgetNetworkConnectivity.Internet or
            WidgetNetworkConnectivity.Local;

    internal static (NetworkControlsViewState State, string Message) MapFailure(
        string errorCode) => errorCode switch
        {
            "permission_denied" or "capability_revoked" or "capability_not_declared" =>
                (NetworkControlsViewState.PermissionDenied, "Nearby Wi-Fi permission denied"),
            "lifecycle_denied" =>
                (NetworkControlsViewState.LifecycleDenied,
                    "Network request denied by widget lifecycle"),
            "channel_closed" =>
                (NetworkControlsViewState.ChannelClosed, "Network service channel closed"),
            "platform_unavailable" or "provider_unavailable" =>
                (NetworkControlsViewState.ServiceUnavailable,
                    "Windows Wi-Fi provider unavailable"),
            _ => (NetworkControlsViewState.Error, "Network provider request failed"),
        };

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static int IndexOf<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        for (var index = 0; index < items.Count; index++)
            if (predicate(items[index])) return index;
        return -1;
    }
}
