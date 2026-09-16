using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsNetworkProvider;

public enum NativeNetworkMedium
{
    None,
    Ethernet,
    WiFi,
    Other,
}

public sealed record NativeSavedNetworkProfile(
    string NativeProfileKey,
    string DisplayName,
    bool IsConnected,
    int? SignalPercent)
{
    public bool? AutoConnect { get; init; }
    public bool CanManage { get; init; }
}

public sealed record NativeAvailableWifiNetwork(
    string NativeNetworkKey,
    string DisplayName,
    int SignalPercent,
    WifiSecurityKind Security,
    bool CredentialRequired,
    bool IsConnected,
    bool HasSavedProfile);

public enum NativeWifiScanState
{
    NotScanned,
    Scanning,
    Ready,
    PreciseLocationDenied,
    Unavailable,
}

public enum NativeWifiScanStartResult
{
    Started,
    AlreadyScanning,
    PreciseLocationDenied,
    Unavailable,
}

public enum NativeWifiConnectStartResult
{
    Started,
    NotFound,
    CredentialRequired,
    UnsupportedAuthentication,
    Unavailable,
}

public enum NativeProtectedWifiConnectStartResult
{
    Started,
    NotFound,
    InvalidCredential,
    UnsupportedAuthentication,
    ProfileAlreadyExists,
    RollbackUnverified,
    Unavailable,
}

public enum NativeProtectedWifiRollbackResult
{
    NothingToRollback,
    Deleted,
    OwnershipMismatch,
    DeleteFailed,
    VerificationUnavailable,
}

public enum NativeWifiRadioState
{
    On,
    Off,
    HardwareDisabled,
    NoAdapter,
    Unavailable,
}

public enum NativeWifiRadioSetResult
{
    Succeeded,
    NoAdapter,
    HardwareDisabled,
    PolicyDenied,
    Unavailable,
    PartialFailure,
}

public sealed record NativeWifiRadioSnapshot(NativeWifiRadioState State, bool CanControl);

public enum NativeWifiScanOutcome
{
    Completed,
    Failed,
}

public sealed record NativeNetworkSnapshot(
    NetworkConnectivity Connectivity,
    NativeNetworkMedium ActiveMedium,
    string? ActiveProfileNativeKey,
    string? ActiveProfileName,
    int? SignalPercent,
    IReadOnlyList<NativeSavedNetworkProfile> SavedProfiles,
    NetworkWirelessAvailability WirelessAvailability = NetworkWirelessAvailability.Available,
    bool IsWirelessAccessRestricted = false);

public sealed record NativeNetworkConnectionDetails(
    NetworkConnectionDetailsState State,
    NetworkConnectionDetailsConnectivity Connectivity,
    NativeNetworkMedium Transport,
    IReadOnlyList<string> IpAddresses,
    IReadOnlyList<string> DefaultGateways,
    IReadOnlyList<string> DnsServers);

public sealed record NativeAvailableWifiSnapshot(
    long ScanGeneration,
    NativeWifiScanState ScanState,
    IReadOnlyList<NativeAvailableWifiNetwork> Networks);

public sealed class NativeNetworkStateChangedEventArgs(long generation) : EventArgs
{
    public long Generation { get; } = generation;
    public NativeNetworkConnectionOutcome? ConnectionOutcome { get; init; }
    public NativeWifiScanOutcome? WifiScanOutcome { get; init; }
}

public enum NativeNetworkConnectionResult
{
    Succeeded,
    Failed,
}

public sealed record NativeNetworkConnectionOutcome(
    string NativeProfileKey,
    NativeNetworkConnectionResult Result);

/// <summary>
/// Testable seam around Windows Native Wi-Fi and IP Helper.
/// Implementations are owned and called only by the provider's dedicated MTA thread.
/// Native callbacks may raise <see cref="StateChanged"/> from arbitrary threads.
/// </summary>
public interface IWindowsNetworkNativeAdapter : IDisposable
{
    event EventHandler<NativeNetworkStateChangedEventArgs>? StateChanged;
    long Generation { get; }
    bool IsDegraded { get; }
    NativeNetworkSnapshot ReadSnapshot();
    NativeNetworkConnectionDetails ReadConnectionDetails() => new(
        NetworkConnectionDetailsState.Unavailable,
        NetworkConnectionDetailsConnectivity.None,
        NativeNetworkMedium.None, [], [], []);
    bool TryConnectSavedProfile(string nativeProfileKey);
    NativeAvailableWifiSnapshot ReadAvailableWifiSnapshot();
    NativeWifiScanStartResult TryStartWifiScan();
    NativeWifiConnectStartResult TryConnectAvailableWifiNetwork(string nativeNetworkKey);
    NativeProtectedWifiConnectStartResult TryConnectProtectedWifiNetwork(
        string nativeNetworkKey,
        ReadOnlySpan<char> secret) => NativeProtectedWifiConnectStartResult.Unavailable;
    NativeProtectedWifiRollbackResult RollbackProtectedWifiConnection(string nativeNetworkKey) =>
        NativeProtectedWifiRollbackResult.NothingToRollback;
    void DisconnectWifi(string nativeNetworkKey) =>
        throw new BrokerException("platform_unavailable", "Wi-Fi disconnect is unavailable.");
    void ManageWifiProfile(string nativeProfileKey, bool? autoConnect) =>
        throw new BrokerException("platform_unavailable", "Wi-Fi profile management is unavailable.");
    NativeWifiRadioSnapshot ReadWifiRadio();
    NativeWifiRadioSetResult TrySetWifiRadio(bool enabled);
}

public interface IWindowsNetworkNativeAdapterFactory
{
    IWindowsNetworkNativeAdapter Create(long generation);
}
