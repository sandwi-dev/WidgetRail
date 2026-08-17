using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.NetworkControls;

internal enum NetworkConnectionAdmissionKind
{
    Rejected,
    Guidance,
    Start,
}

internal readonly record struct NetworkConnectionAdmission(
    NetworkConnectionAdmissionKind Kind,
    string Message,
    bool IsError);

internal readonly record struct NetworkBluetoothFeedback(string Message, bool IsError);

internal static class NetworkControlsCommandPolicy
{
    internal static NetworkConnectionAdmission AdmitConnection(
        WidgetAvailableWifiNetwork? selected,
        bool controlBusy,
        bool scanBusy)
    {
        if (selected is null || selected.IsConnected || controlBusy || scanBusy)
            return new(NetworkConnectionAdmissionKind.Rejected, string.Empty, false);
        if (selected.CredentialRequired)
            return new(NetworkConnectionAdmissionKind.Guidance,
                $"{selected.DisplayName} needs a password · use Windows Quick Settings to connect",
                false);
        if ((selected.Security is WidgetWifiSecurityKind.Enterprise or
                WidgetWifiSecurityKind.Unknown) && !selected.HasSavedProfile)
            return new(NetworkConnectionAdmissionKind.Guidance,
                "This Wi-Fi authentication method needs Windows network settings", true);
        return new(NetworkConnectionAdmissionKind.Start,
            $"Requesting {selected.DisplayName}…", false);
    }

    internal static string MapScanFailure(string errorCode) => errorCode switch
    {
        "permission_denied" or "capability_revoked" or "capability_not_declared" =>
            "Nearby Wi-Fi permission denied",
        "lifecycle_denied" => "Wi-Fi scan paused by lifecycle",
        "provider_busy" => "A Wi-Fi scan is already in progress",
        "platform_unavailable" => "Windows Wi-Fi scan is unavailable",
        _ => "Wi-Fi scan could not be started",
    };

    internal static string MapConnectFailure(string errorCode) => errorCode switch
    {
        "credential_required" =>
            "This network needs a password · use Windows Quick Settings to connect",
        "unsupported_authentication" =>
            "This Wi-Fi authentication method needs Windows network settings",
        "resource_not_found" => "That scan result expired · scan again",
        "provider_busy" => "Another network connection is already in progress",
        "permission_denied" or "capability_revoked" or "capability_not_declared" =>
            "Wi-Fi connection permission denied",
        "lifecycle_denied" => "Network control paused by lifecycle",
        "platform_unavailable" => "Windows Wi-Fi connection control is unavailable",
        _ => "Connection request failed · previous connection retained",
    };

    internal static string MapWifiRadioFailure(string errorCode) => errorCode switch
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

    internal static string MapBluetoothRadioFailure(string errorCode) => errorCode switch
    {
        "permission_denied" => "Bluetooth radio permission denied",
        "platform_denied" => "Windows policy blocked Bluetooth radio control",
        "hardware_disabled" => "Bluetooth is disabled by hardware or device policy",
        "no_adapter" => "No Bluetooth adapter is available",
        "partial_failure" => "Bluetooth changed partially · current Windows state refreshed",
        _ => "Bluetooth radio could not be changed",
    };

    internal static string MapBluetoothPairFailure(string errorCode) => errorCode switch
    {
        "permission_denied" or "capability_not_declared" =>
            "Allow Bluetooth pairing in Settings → Permissions",
        "unknown_device" => "That Bluetooth device is no longer available",
        "lifecycle_denied" =>
            "Bluetooth pairing stopped when this widget left the foreground",
        _ => "Windows could not start Bluetooth pairing",
    };

    internal static string MapBluetoothManageFailure(string errorCode) => errorCode switch
    {
        "permission_denied" or "capability_not_declared" =>
            "Allow Bluetooth device management in Settings → Permissions",
        "unknown_device" => "That Bluetooth device is no longer available",
        "lifecycle_denied" =>
            "Bluetooth management is available only while this widget is open",
        _ => "Windows Bluetooth Settings could not be opened",
    };

    internal static string MapBluetoothUnpairFailure(string errorCode) => errorCode switch
    {
        "permission_denied" or "capability_not_declared" =>
            "Allow Bluetooth device removal in Settings → Permissions",
        "unknown_device" or "resource_not_found" =>
            "That Bluetooth device is no longer available",
        "lifecycle_denied" =>
            "Bluetooth removal is available only while this widget is open",
        "request_timeout" => "Bluetooth device removal timed out",
        _ => "Windows could not remove the Bluetooth device",
    };

    internal static NetworkBluetoothFeedback PairingFeedback(
        WidgetBluetoothDevice requested,
        WidgetBluetoothPairingOutcome outcome) => outcome switch
        {
            WidgetBluetoothPairingOutcome.Paired =>
                new($"{requested.DisplayName} paired · waiting for Windows connection state", false),
            WidgetBluetoothPairingOutcome.AlreadyPaired or
                WidgetBluetoothPairingOutcome.RemoteAlreadyAssociated =>
                new($"{requested.DisplayName} is already paired", false),
            WidgetBluetoothPairingOutcome.UserInteractionRequired =>
                new($"{requested.DisplayName} needs Windows confirmation · press X to open Bluetooth Settings", false),
            WidgetBluetoothPairingOutcome.CanceledByUser =>
                new($"Pairing {requested.DisplayName} was canceled", false),
            WidgetBluetoothPairingOutcome.OperationInProgress =>
                new($"Windows is already pairing {requested.DisplayName}", false),
            WidgetBluetoothPairingOutcome.NotReady =>
                new($"{requested.DisplayName} is not ready to pair", true),
            WidgetBluetoothPairingOutcome.AccessDenied or
                WidgetBluetoothPairingOutcome.AuthenticationNotAllowed =>
                new("Windows denied this Bluetooth pairing request", true),
            WidgetBluetoothPairingOutcome.AuthenticationTimedOut =>
                new($"Pairing {requested.DisplayName} timed out", true),
            WidgetBluetoothPairingOutcome.AuthenticationFailed or
                WidgetBluetoothPairingOutcome.InvalidCeremonyData =>
                new($"{requested.DisplayName} could not be authenticated", true),
            WidgetBluetoothPairingOutcome.NoSupportedProfiles or
                WidgetBluetoothPairingOutcome.ProtectionLevelNotMet =>
                new($"{requested.DisplayName} needs Windows Bluetooth Settings to finish setup", true),
            WidgetBluetoothPairingOutcome.DeviceUnavailable =>
                new($"{requested.DisplayName} is no longer available", true),
            _ => new($"Windows could not pair {requested.DisplayName}", true),
        };
}
