using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsNetworkProvider;

internal abstract record NetworkCommand;

internal interface INetworkCommandAdmissionObserver
{
    void AfterReservation(NetworkCommand command);
}

internal sealed record RefreshCommand : NetworkCommand
{
    public static RefreshCommand Instance { get; } = new();
}

internal sealed record RetryRefreshCommand(CancellationToken CancellationToken) : NetworkCommand
{
    public TaskCompletionSource Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record ConnectCommand(string ProfileId, CancellationToken CancellationToken) : NetworkCommand
{
    public TaskCompletionSource Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record WifiScanCommand(CancellationToken CancellationToken) : NetworkCommand
{
    public TaskCompletionSource Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record WifiScanTimeoutCommand(long Generation) : NetworkCommand;

internal sealed record ConnectionTimeoutCommand(long Generation) : NetworkCommand;

internal sealed record ConnectAvailableWifiCommand(
    string NetworkId,
    CancellationToken CancellationToken) : NetworkCommand
{
    public TaskCompletionSource Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record ConnectProtectedWifiCommand(
    string NetworkId,
    char[] Secret,
    CancellationToken CancellationToken) : NetworkCommand, IDisposable
{
    public TaskCompletionSource<ProtectedWifiConnectionResult> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose() => Array.Clear(Secret);
}

internal sealed record ManageWifiCommand(
    string TargetId, bool Disconnect, bool? AutoConnect, CancellationToken CancellationToken) : NetworkCommand
{
    public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record SetWifiRadioCommand(bool Enabled, CancellationToken CancellationToken)
    : NetworkCommand
{
    public TaskCompletionSource Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal readonly record struct WifiScanStartDecision(
    bool ChangesState,
    WifiScanState State,
    bool StartsDeadline);

/// <summary>
/// Closed command vocabulary and native-result policy. This type performs native calls only on
/// the provider owner thread and owns no provider state, lifetime, timer, or event publication.
/// </summary>
internal static class WindowsNetworkCommandPolicy
{
    public static bool IsValidSavedProfileId(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 128 &&
        value.StartsWith("network_", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    public static bool IsValidAvailableWifiId(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 128 &&
        value.StartsWith("wifi_", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    public static void StartSavedConnection(
        IWindowsNetworkNativeAdapter adapter,
        string nativeKey)
    {
        if (adapter.TryConnectSavedProfile(nativeKey)) return;
        if (adapter.IsDegraded)
            throw new BrokerException(
                "platform_unavailable", "Windows networking is temporarily unavailable.");
        throw new BrokerException(
            "resource_not_found", "The saved network is no longer available.");
    }

    public static void StartAvailableWifiConnection(
        IWindowsNetworkNativeAdapter adapter,
        string nativeKey)
    {
        var result = adapter.TryConnectAvailableWifiNetwork(nativeKey);
        if (result == NativeWifiConnectStartResult.Started) return;
        throw result switch
        {
            NativeWifiConnectStartResult.CredentialRequired => new BrokerException(
                "credential_required", "This Wi-Fi network requires a credential."),
            NativeWifiConnectStartResult.UnsupportedAuthentication => new BrokerException(
                "unsupported_authentication", "This Wi-Fi authentication method is unsupported."),
            NativeWifiConnectStartResult.NotFound => new BrokerException(
                "resource_not_found", "The visible Wi-Fi network is no longer available."),
            NativeWifiConnectStartResult.Unavailable => new BrokerException(
                "platform_unavailable", "Windows Wi-Fi connection control is unavailable."),
            _ => new BrokerException(
                "platform_unavailable", "Windows returned an invalid Wi-Fi connection state."),
        };
    }

    public static void StartProtectedWifiConnection(
        IWindowsNetworkNativeAdapter adapter,
        string nativeKey,
        ReadOnlySpan<char> secret)
    {
        var result = adapter.TryConnectProtectedWifiNetwork(nativeKey, secret);
        if (result == NativeProtectedWifiConnectStartResult.Started) return;
        throw result switch
        {
            NativeProtectedWifiConnectStartResult.InvalidCredential => new BrokerException(
                "invalid_credential", "The Wi-Fi password must contain 8 to 63 printable characters."),
            NativeProtectedWifiConnectStartResult.UnsupportedAuthentication => new BrokerException(
                "unsupported_authentication", "This Wi-Fi authentication method is unsupported."),
            NativeProtectedWifiConnectStartResult.ProfileAlreadyExists => new BrokerException(
                "profile_exists", "Windows already has a profile for this network."),
            NativeProtectedWifiConnectStartResult.RollbackUnverified => new BrokerException(
                "rollback_unverified",
                "Windows could not verify ownership of the temporary Wi-Fi profile."),
            NativeProtectedWifiConnectStartResult.NotFound => new BrokerException(
                "resource_not_found", "The visible Wi-Fi network is no longer available."),
            _ => new BrokerException(
                "platform_unavailable", "Windows Wi-Fi connection control is unavailable."),
        };
    }

    public static WifiScanStartDecision StartWifiScan(IWindowsNetworkNativeAdapter adapter) =>
        adapter.TryStartWifiScan() switch
        {
            NativeWifiScanStartResult.AlreadyScanning => new(false, default, false),
            NativeWifiScanStartResult.Started => new(true, WifiScanState.Scanning, true),
            NativeWifiScanStartResult.PreciseLocationDenied =>
                new(true, WifiScanState.PreciseLocationDenied, false),
            _ => new(true, WifiScanState.Unavailable, false),
        };

    public static void SetWifiRadio(IWindowsNetworkNativeAdapter adapter, bool enabled)
    {
        var result = adapter.TrySetWifiRadio(enabled);
        if (result == NativeWifiRadioSetResult.Succeeded) return;
        throw result switch
        {
            NativeWifiRadioSetResult.NoAdapter =>
                new BrokerException("wifi_no_adapter", "No Wi-Fi adapter is available."),
            NativeWifiRadioSetResult.HardwareDisabled => new BrokerException(
                "wifi_hardware_disabled", "Wi-Fi is disabled by a hardware switch."),
            NativeWifiRadioSetResult.PolicyDenied => new BrokerException(
                "wifi_radio_policy_denied", "Windows policy denied Wi-Fi radio control."),
            NativeWifiRadioSetResult.Unavailable => new BrokerException(
                "platform_unavailable", "Windows Wi-Fi radio control is unavailable."),
            NativeWifiRadioSetResult.PartialFailure => new BrokerException(
                "wifi_radio_partial_failure",
                "Windows changed only part of the Wi-Fi radio state and restoration could not be guaranteed."),
            _ => new BrokerException(
                "platform_unavailable", "Windows returned an invalid Wi-Fi radio state."),
        };
    }
}
