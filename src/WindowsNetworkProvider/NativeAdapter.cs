using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

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
    int? SignalPercent);

public sealed record NativeNetworkSnapshot(
    NetworkConnectivity Connectivity,
    NativeNetworkMedium ActiveMedium,
    string? ActiveProfileNativeKey,
    string? ActiveProfileName,
    int? SignalPercent,
    IReadOnlyList<NativeSavedNetworkProfile> SavedProfiles,
    NetworkWirelessAvailability WirelessAvailability = NetworkWirelessAvailability.Available,
    bool IsWirelessAccessRestricted = false);

public sealed class NativeNetworkStateChangedEventArgs(long generation) : EventArgs
{
    public long Generation { get; } = generation;
    public NativeNetworkConnectionOutcome? ConnectionOutcome { get; init; }
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
    bool TryConnectSavedProfile(string nativeProfileKey);
}

public interface IWindowsNetworkNativeAdapterFactory
{
    IWindowsNetworkNativeAdapter Create(long generation);
}
