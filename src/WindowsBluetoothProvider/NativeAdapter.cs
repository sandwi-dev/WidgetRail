namespace GameBarAlternative.WindowsBluetoothProvider;

public enum NativeBluetoothRadioState
{
    On,
    Off,
    HardwareDisabled,
    NoAdapter,
    Unavailable,
}

public enum NativeBluetoothDiscoveryState
{
    Enumerating,
    Ready,
    Unavailable,
}

public enum NativeBluetoothRadioSetResult
{
    Succeeded,
    NoAdapter,
    HardwareDisabled,
    DeniedByUser,
    DeniedBySystem,
    PartialFailure,
    Unavailable,
}

/// <summary>
/// Authoritative result of a Windows Association Endpoint pairing attempt.
/// These values intentionally do not imply that a Bluetooth profile is
/// connected; Windows exposes pairing separately from profile-specific use.
/// </summary>
public enum BluetoothPairingOutcome
{
    Paired,
    AlreadyPaired,
    NotReady,
    Rejected,
    TooManyConnections,
    HardwareFailure,
    AuthenticationTimedOut,
    AuthenticationNotAllowed,
    AuthenticationFailed,
    NoSupportedProfiles,
    ProtectionLevelNotMet,
    AccessDenied,
    InvalidCeremonyData,
    CanceledByUser,
    OperationInProgress,
    UserInteractionRequired,
    RemoteAlreadyAssociated,
    DeviceUnavailable,
    Failed,
}

public sealed record NativeBluetoothDevice(
    string NativeId,
    string DisplayName,
    bool IsPaired,
    bool IsConnected,
    bool IsPresent);

public sealed record NativeBluetoothSnapshot(
    NativeBluetoothRadioState RadioState,
    bool CanControlRadio,
    NativeBluetoothDiscoveryState DiscoveryState,
    IReadOnlyList<NativeBluetoothDevice> Devices);

public interface IWindowsBluetoothNativeAdapter : IAsyncDisposable
{
    event EventHandler? StateChanged;
    Task StartAsync(CancellationToken cancellationToken);
    NativeBluetoothSnapshot ReadSnapshot();
    Task<NativeBluetoothRadioSetResult> SetRadioAsync(
        bool enabled, CancellationToken cancellationToken);
    Task<BluetoothPairingOutcome> PairAsync(
        string nativeDeviceId, CancellationToken cancellationToken);
}

public interface IWindowsBluetoothNativeAdapterFactory
{
    IWindowsBluetoothNativeAdapter Create();
}

/// <summary>
/// Launches the Windows-owned Bluetooth management experience. The provider
/// deliberately passes no native device identifier through the URI.
/// </summary>
public interface IWindowsBluetoothSettingsLauncher
{
    Task<bool> OpenAsync(CancellationToken cancellationToken);
}
