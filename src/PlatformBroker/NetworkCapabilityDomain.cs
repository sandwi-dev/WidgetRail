using System.Text.Json;

namespace GameBarAlternative.PlatformBroker;

internal sealed class NetworkCapabilityDomain(IPlatformBrokerBackend backend)
{
    internal async Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case PlatformCapabilities.NetworkStatusGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateStatus(
                    await backend.GetNetworkStatusAsync(cancellationToken).ConfigureAwait(false)));
            case PlatformCapabilities.NetworkSavedProfilesList:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateProfiles(
                    await backend.GetSavedNetworkProfilesAsync(cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.NetworkSavedProfileSwitch:
            {
                var request = BrokerJson.ParsePayload<SwitchSavedNetworkProfileRequest>(payload);
                ContractValidation.OpaqueId(request.ProfileId);
                await backend.SwitchSavedNetworkProfileAsync(
                    request.ProfileId, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.NetworkAvailableWifiGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateAvailableWifi(
                    await backend.GetAvailableWifiNetworksAsync(cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.NetworkWifiScan:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                await backend.RequestWifiScanAsync(cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            case PlatformCapabilities.NetworkAvailableWifiConnect:
            {
                var request = BrokerJson.ParsePayload<ConnectAvailableWifiNetworkRequest>(payload);
                ContractValidation.OpaqueId(request.NetworkId);
                await backend.ConnectAvailableWifiNetworkAsync(
                    request.NetworkId, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.NetworkWifiRadioGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateWifiRadio(
                    await backend.GetWifiRadioAsync(cancellationToken).ConfigureAwait(false)));
            case PlatformCapabilities.NetworkWifiRadioSet:
            {
                var request = BrokerJson.ParsePayload<SetWifiRadioStateRequest>(payload);
                await backend.SetWifiRadioAsync(request.Enabled, cancellationToken)
                    .ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.NetworkBluetoothGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateBluetooth(
                    await backend.GetBluetoothAsync(cancellationToken).ConfigureAwait(false)));
            case PlatformCapabilities.NetworkBluetoothRadioSet:
            {
                var request = BrokerJson.ParsePayload<SetBluetoothRadioStateRequest>(payload);
                await backend.SetBluetoothRadioAsync(request.Enabled, cancellationToken)
                    .ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.NetworkBluetoothDevicePair:
            {
                var request = BrokerJson.ParsePayload<PairBluetoothDeviceRequest>(payload);
                ContractValidation.OpaqueId(request.DeviceId);
                var result = await backend.PairBluetoothDeviceAsync(
                    request.DeviceId, cancellationToken).ConfigureAwait(false);
                if (result is null || !Enum.IsDefined(result.Outcome))
                    throw new BrokerException(
                        "invalid_backend_data", "Bluetooth pairing result is invalid.");
                return BrokerJson.ToElement(result);
            }
            case PlatformCapabilities.NetworkBluetoothDeviceSettingsOpen:
            {
                var request = BrokerJson.ParsePayload<OpenBluetoothDeviceSettingsRequest>(payload);
                ContractValidation.OpaqueId(request.DeviceId);
                await backend.OpenBluetoothDeviceSettingsAsync(
                    request.DeviceId, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.RecentActivitiesList:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateRecentActivities(
                    await backend.GetRecentActivitiesAsync(cancellationToken)
                        .ConfigureAwait(false)));
            default:
                throw new BrokerException(
                    "unsupported_operation", "Network operation is unsupported.");
        }
    }

    internal static JsonElement ProjectEvent(string eventType, object payload) =>
        eventType switch
        {
            PlatformCapabilities.NetworkStatusChanged when
                payload is NetworkStatusChangedEvent change =>
                BrokerJson.ToElement(new NetworkStatusChangedEvent(
                    ValidateStatus(change.Status))),
            PlatformCapabilities.NetworkAvailableWifiChanged when
                payload is AvailableWifiNetworksChangedEvent change =>
                BrokerJson.ToElement(new AvailableWifiNetworksChangedEvent(
                    ValidateAvailableWifi(change.Snapshot))),
            PlatformCapabilities.NetworkWifiRadioChanged when
                payload is WifiRadioChangedEvent change =>
                BrokerJson.ToElement(new WifiRadioChangedEvent(
                    ValidateWifiRadio(change.Radio))),
            PlatformCapabilities.NetworkBluetoothChanged when
                payload is BluetoothChangedEvent change =>
                BrokerJson.ToElement(new BluetoothChangedEvent(
                    ValidateBluetooth(change.Snapshot))),
            PlatformCapabilities.RecentActivitiesChanged when
                payload is RecentActivitiesChangedEvent change =>
                BrokerJson.ToElement(new RecentActivitiesChangedEvent(
                    ValidateRecentActivities(change.Activities))),
            _ => throw new BrokerException(
                "invalid_backend_data", "Network event payload is invalid."),
        };

    internal static NetworkStatusSummary ValidateStatus(NetworkStatusSummary? status)
    {
        if (status is null)
            throw new BrokerException("invalid_backend_data", "Network status is invalid.");
        if (!Enum.IsDefined(status.Connectivity))
            throw new BrokerException("invalid_backend_data", "Network connectivity is invalid.");
        if (!Enum.IsDefined(status.Transport) ||
            !Enum.IsDefined(status.WirelessAvailability) ||
            !Enum.IsDefined(status.DetailsAccess) ||
            !Enum.IsDefined(status.ConnectionAttemptState))
            throw new BrokerException("invalid_backend_data", "Network status state is invalid.");
        if ((status.ConnectionAttemptState == NetworkConnectionAttemptState.None) !=
            (status.AttemptProfileId is null))
            throw new BrokerException(
                "invalid_backend_data", "Network attempt state is inconsistent.");
        if (status.AttemptProfileId is not null)
            ContractValidation.OpaqueId(status.AttemptProfileId, "invalid_backend_data");
        if (status.ActiveProfileId is not null)
            ContractValidation.OpaqueId(status.ActiveProfileId, "invalid_backend_data");
        if (status.ActiveProfileName is not null)
            ContractValidation.DisplayName(status.ActiveProfileName);
        if ((status.ActiveProfileId is null) != (status.ActiveProfileName is null))
            throw new BrokerException(
                "invalid_backend_data", "Network profile summary is incomplete.");
        if ((status.Transport != NetworkTransportKind.Wifi ||
             status.DetailsAccess != NetworkDetailsAccess.Available) &&
            (status.ActiveProfileId is not null || status.ActiveProfileName is not null ||
             status.SignalPercent is not null))
            throw new BrokerException(
                "invalid_backend_data", "Network details state is inconsistent.");
        ContractValidation.Percent(status.SignalPercent);
        return status;
    }

    internal static IReadOnlyList<SavedNetworkProfileSummary> ValidateProfiles(
        IReadOnlyList<SavedNetworkProfileSummary>? profiles)
    {
        if (profiles is null)
            throw new BrokerException(
                "invalid_backend_data", "Network profile result is invalid.");
        if (profiles.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException(
                "invalid_backend_data", "Too many saved network profiles.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            if (profile is null)
                throw new BrokerException(
                    "invalid_backend_data", "Network profile result is invalid.");
            ContractValidation.OpaqueId(profile.ProfileId, "invalid_backend_data");
            ContractValidation.DisplayName(profile.DisplayName);
            ContractValidation.Percent(profile.SignalPercent);
            if (!ids.Add(profile.ProfileId))
                throw new BrokerException(
                    "invalid_backend_data", "Network profile IDs are duplicated.");
        }
        return profiles.ToArray();
    }

    internal static AvailableWifiNetworksSummary ValidateAvailableWifi(
        AvailableWifiNetworksSummary? snapshot)
    {
        if (snapshot is null || !Enum.IsDefined(snapshot.ScanState) ||
            snapshot.Networks is null)
            throw new BrokerException(
                "invalid_backend_data", "Available Wi-Fi result is invalid.");
        if (snapshot.Networks.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException(
                "invalid_backend_data", "Too many available Wi-Fi networks.");
        if (snapshot.ScanState != WifiScanState.Ready && snapshot.Networks.Count != 0)
            throw new BrokerException(
                "invalid_backend_data", "Available Wi-Fi state contains stale networks.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var network in snapshot.Networks)
        {
            if (network is null || !Enum.IsDefined(network.Security))
                throw new BrokerException(
                    "invalid_backend_data", "Available Wi-Fi entry is invalid.");
            ContractValidation.OpaqueId(network.NetworkId, "invalid_backend_data");
            ContractValidation.DisplayName(network.DisplayName);
            ContractValidation.Percent(network.SignalPercent);
            if (network.SignalPercent is < 0 or > 100 ||
                network.Security == WifiSecurityKind.Open && network.CredentialRequired ||
                network.IsConnected && network.CredentialRequired)
                throw new BrokerException(
                    "invalid_backend_data", "Available Wi-Fi entry is inconsistent.");
            if (!ids.Add(network.NetworkId))
                throw new BrokerException(
                    "invalid_backend_data", "Available Wi-Fi IDs are duplicated.");
        }
        return snapshot with { Networks = snapshot.Networks.ToArray() };
    }

    internal static WifiRadioSummary ValidateWifiRadio(WifiRadioSummary? radio)
    {
        if (radio is null || !Enum.IsDefined(radio.State))
            throw new BrokerException(
                "invalid_backend_data", "Wi-Fi radio state is invalid.");
        if (radio.CanControl && radio.State is WifiRadioState.HardwareDisabled or
                WifiRadioState.NoAdapter or WifiRadioState.Unavailable)
            throw new BrokerException(
                "invalid_backend_data", "Wi-Fi radio control availability is invalid.");
        return radio;
    }

    internal static BluetoothSummary ValidateBluetooth(BluetoothSummary? snapshot)
    {
        if (snapshot is null || !Enum.IsDefined(snapshot.RadioState) ||
            !Enum.IsDefined(snapshot.DiscoveryState) || snapshot.Devices is null ||
            snapshot.Devices.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException("invalid_backend_data", "Bluetooth result is invalid.");
        if (snapshot.CanControlRadio && snapshot.RadioState is
                BluetoothRadioState.HardwareDisabled or BluetoothRadioState.NoAdapter or
                BluetoothRadioState.Unavailable)
            throw new BrokerException(
                "invalid_backend_data", "Bluetooth radio control availability is invalid.");
        if (snapshot.DiscoveryState != BluetoothDiscoveryState.Ready &&
            snapshot.Devices.Count != 0)
            throw new BrokerException(
                "invalid_backend_data", "Bluetooth discovery state contains stale devices.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in snapshot.Devices)
        {
            if (device is null)
                throw new BrokerException(
                    "invalid_backend_data", "Bluetooth device result is invalid.");
            ContractValidation.OpaqueId(device.DeviceId, "invalid_backend_data");
            ContractValidation.DisplayName(device.DisplayName);
            if (!ids.Add(device.DeviceId) || device.IsConnected && !device.IsPaired ||
                !device.IsPaired && !device.IsPresent)
                throw new BrokerException(
                    "invalid_backend_data", "Bluetooth device state is inconsistent.");
        }
        return snapshot with { Devices = snapshot.Devices.ToArray() };
    }

    internal static IReadOnlyList<RecentActivitySummary> ValidateRecentActivities(
        IReadOnlyList<RecentActivitySummary>? activities)
    {
        if (activities is null || activities.Count > 32)
            throw new BrokerException(
                "invalid_backend_data", "Recent activity result is invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var mostRecentCount = 0;
        foreach (var activity in activities)
        {
            if (activity is null || !Enum.IsDefined(activity.Kind))
                throw new BrokerException(
                    "invalid_backend_data", "Recent activity entry is invalid.");
            ContractValidation.OpaqueId(activity.ActivityId, "invalid_backend_data");
            ContractValidation.DisplayName(activity.DisplayName);
            if (!ids.Add(activity.ActivityId) ||
                activity.IsMostRecent && ++mostRecentCount > 1 ||
                activity.IsMostRecent && !activity.IsRunning)
                throw new BrokerException(
                    "invalid_backend_data", "Recent activity entries are inconsistent.");
        }
        return activities.ToArray();
    }
}
