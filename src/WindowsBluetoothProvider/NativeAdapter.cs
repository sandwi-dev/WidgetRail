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
}

public interface IWindowsBluetoothNativeAdapterFactory
{
    IWindowsBluetoothNativeAdapter Create();
}
