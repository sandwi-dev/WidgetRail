using System.Text;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsNetworkProvider;

internal sealed record NetworkStateProjection(
    NetworkStatusSummary Status,
    IReadOnlyList<SavedNetworkProfileSummary> Profiles,
    IReadOnlyDictionary<string, string> NativeKeysByOpaqueId,
    bool WirelessAccessRestricted);

internal sealed record AvailableWifiProjection(
    AvailableWifiNetworksSummary Snapshot,
    IReadOnlyDictionary<string, string> NativeKeysByOpaqueId);

/// <summary>
/// Owner-thread-only native snapshot normalization and opaque identity allocation. The backend
/// remains the sole committed provider-state owner and atomically applies these immutable values.
/// </summary>
internal sealed class WindowsNetworkStateReconciler
{
    private const int MaximumProfiles = 128;
    private const int MaximumNativeKeyLength = 2048;
    private const int MaximumDisplayNameLength = 160;
    private readonly Dictionary<string, string> _profileOpaqueIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _wifiOpaqueIds = new(StringComparer.Ordinal);
    private long _mappedWifiScanGeneration = -1;

    public NetworkStateProjection ReconcileNetwork(
        NativeNetworkSnapshot snapshot,
        WindowsNetworkOperationPolicy operations,
        IEnumerable<NativeNetworkConnectionOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var outcome in outcomes) operations.ApplyConnectionOutcome(outcome);

        var seenNativeKeys = new HashSet<string>(StringComparer.Ordinal);
        var profiles = new List<SavedNetworkProfileSummary>(
            Math.Min(snapshot.SavedProfiles?.Count ?? 0, MaximumProfiles));
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        string? activeOpaqueId = null;
        var allowWirelessDetails = !snapshot.IsWirelessAccessRestricted;
        foreach (var profile in snapshot.SavedProfiles ?? [])
        {
            if (profiles.Count >= MaximumProfiles) break;
            if (!IsValidProfile(profile) || !seenNativeKeys.Add(profile.NativeProfileKey)) continue;
            if (!_profileOpaqueIds.TryGetValue(profile.NativeProfileKey, out var opaqueId))
            {
                opaqueId = $"network_{Guid.NewGuid():N}";
                _profileOpaqueIds.Add(profile.NativeProfileKey, opaqueId);
            }
            reverse.Add(opaqueId, profile.NativeProfileKey);
            var isConnected = allowWirelessDetails && (profile.IsConnected ||
                string.Equals(snapshot.ActiveProfileNativeKey, profile.NativeProfileKey,
                    StringComparison.Ordinal));
            if (isConnected) activeOpaqueId = opaqueId;
            profiles.Add(new SavedNetworkProfileSummary(
                opaqueId,
                SanitizeDisplayName(profile.DisplayName, "Saved Wi-Fi network"),
                isConnected,
                ClampPercent(profile.SignalPercent)));
        }
        foreach (var missing in _profileOpaqueIds.Keys
                     .Where(key => !seenNativeKeys.Contains(key)).ToArray())
            _profileOpaqueIds.Remove(missing);

        profiles.Sort(static (left, right) =>
        {
            var connected = right.IsConnected.CompareTo(left.IsConnected);
            return connected != 0 ? connected :
                string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        var status = BuildStatus(snapshot, activeOpaqueId);
        operations.ApplyConnectedSnapshot(
            snapshot.ActiveProfileNativeKey, snapshot.IsWirelessAccessRestricted);
        return new(
            operations.ApplyTo(status),
            profiles.ToArray(),
            reverse,
            snapshot.IsWirelessAccessRestricted);
    }

    public AvailableWifiProjection ReconcileAvailableWifi(NativeAvailableWifiSnapshot native)
    {
        ArgumentNullException.ThrowIfNull(native);
        var state = native.ScanState switch
        {
            NativeWifiScanState.NotScanned => WifiScanState.NotScanned,
            NativeWifiScanState.Scanning => WifiScanState.Scanning,
            NativeWifiScanState.Ready => WifiScanState.Ready,
            NativeWifiScanState.PreciseLocationDenied => WifiScanState.PreciseLocationDenied,
            _ => WifiScanState.Unavailable,
        };
        var networks = new List<AvailableWifiNetworkSummary>();
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        if (state == WifiScanState.Ready && native.ScanGeneration != _mappedWifiScanGeneration)
        {
            _wifiOpaqueIds.Clear();
            _mappedWifiScanGeneration = native.ScanGeneration;
        }
        if (state != WifiScanState.Ready)
        {
            _wifiOpaqueIds.Clear();
            _mappedWifiScanGeneration = -1;
        }
        foreach (var item in native.Networks ?? [])
        {
            if (networks.Count >= MaximumProfiles ||
                string.IsNullOrEmpty(item.NativeNetworkKey) ||
                item.NativeNetworkKey.Length > MaximumNativeKeyLength ||
                !Enum.IsDefined(item.Security)) continue;
            if (!_wifiOpaqueIds.TryGetValue(item.NativeNetworkKey, out var opaqueId))
            {
                opaqueId = $"wifi_{Guid.NewGuid():N}";
                _wifiOpaqueIds.Add(item.NativeNetworkKey, opaqueId);
            }
            if (!reverse.TryAdd(opaqueId, item.NativeNetworkKey)) continue;
            networks.Add(new AvailableWifiNetworkSummary(
                opaqueId,
                SanitizeDisplayName(item.DisplayName, "Hidden network"),
                Math.Clamp(item.SignalPercent, 0, 100),
                item.Security,
                item.CredentialRequired,
                item.IsConnected,
                item.HasSavedProfile));
        }
        networks.Sort(static (left, right) =>
        {
            var connected = right.IsConnected.CompareTo(left.IsConnected);
            if (connected != 0) return connected;
            var signal = right.SignalPercent.CompareTo(left.SignalPercent);
            return signal != 0 ? signal :
                string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
        return new(
            new AvailableWifiNetworksSummary(
                state,
                state == WifiScanState.Ready ? networks.ToArray() : []),
            state == WifiScanState.Ready
                ? reverse
                : new Dictionary<string, string>(StringComparer.Ordinal));
    }

    public AvailableWifiProjection ResetAvailableWifi(WifiScanState state)
    {
        _wifiOpaqueIds.Clear();
        _mappedWifiScanGeneration = -1;
        return new(
            new AvailableWifiNetworksSummary(state, []),
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    public void Reset()
    {
        _profileOpaqueIds.Clear();
        _wifiOpaqueIds.Clear();
        _mappedWifiScanGeneration = -1;
    }

    public static WifiRadioSummary ProjectRadio(NativeWifiRadioSnapshot native)
    {
        var state = native.State switch
        {
            NativeWifiRadioState.On => WifiRadioState.On,
            NativeWifiRadioState.Off => WifiRadioState.Off,
            NativeWifiRadioState.HardwareDisabled => WifiRadioState.HardwareDisabled,
            NativeWifiRadioState.NoAdapter => WifiRadioState.NoAdapter,
            _ => WifiRadioState.Unavailable,
        };
        return new WifiRadioSummary(
            state,
            native.CanControl && state is WifiRadioState.On or WifiRadioState.Off);
    }

    public static bool ProfilesEqual(
        IReadOnlyList<SavedNetworkProfileSummary> left,
        IReadOnlyList<SavedNetworkProfileSummary> right) => left.Count == right.Count &&
        left.Zip(right).All(pair => pair.First == pair.Second);

    public static bool AvailableWifiEqual(
        AvailableWifiNetworksSummary left,
        AvailableWifiNetworksSummary right) =>
        left.ScanState == right.ScanState &&
        left.Networks.Count == right.Networks.Count &&
        left.Networks.Zip(right.Networks).All(pair => pair.First == pair.Second);

    private static NetworkStatusSummary BuildStatus(
        NativeNetworkSnapshot snapshot,
        string? activeOpaqueId)
    {
        if (!Enum.IsDefined(snapshot.Connectivity))
            throw new InvalidOperationException("Invalid native connectivity.");
        var details = snapshot.IsWirelessAccessRestricted
            ? NetworkDetailsAccess.PrivacyRestricted
            : NetworkDetailsAccess.Available;
        var transport = snapshot.ActiveMedium switch
        {
            NativeNetworkMedium.Ethernet => NetworkTransportKind.Ethernet,
            NativeNetworkMedium.WiFi => NetworkTransportKind.Wifi,
            NativeNetworkMedium.Other => NetworkTransportKind.Other,
            _ => NetworkTransportKind.None,
        };
        return snapshot.ActiveMedium switch
        {
            NativeNetworkMedium.Ethernet => new(
                snapshot.Connectivity, transport, snapshot.WirelessAvailability, details,
                NetworkConnectionAttemptState.None, null, null, null, null),
            NativeNetworkMedium.WiFi => new(
                snapshot.Connectivity, transport, snapshot.WirelessAvailability, details,
                NetworkConnectionAttemptState.None, null,
                snapshot.IsWirelessAccessRestricted ? null : activeOpaqueId,
                snapshot.IsWirelessAccessRestricted || activeOpaqueId is null
                    ? null
                    : SanitizeDisplayName(snapshot.ActiveProfileName, "Saved Wi-Fi network"),
                snapshot.IsWirelessAccessRestricted ? null : ClampPercent(snapshot.SignalPercent)),
            _ => new(
                snapshot.Connectivity, transport, snapshot.WirelessAvailability, details,
                NetworkConnectionAttemptState.None, null, null, null, null),
        };
    }

    private static bool IsValidProfile(NativeSavedNetworkProfile profile) =>
        !string.IsNullOrEmpty(profile.NativeProfileKey) &&
        profile.NativeProfileKey.Length <= MaximumNativeKeyLength;

    private static int? ClampPercent(int? value) =>
        value is null ? null : Math.Clamp(value.Value, 0, 100);

    private static string SanitizeDisplayName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (LooksLikePath(value)) return fallback;
        var builder = new StringBuilder(Math.Min(value.Length, MaximumDisplayNameLength));
        var pendingSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) continue;
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length != 0;
                continue;
            }
            var needed = rune.Utf16SequenceLength + (pendingSpace ? 1 : 0);
            if (builder.Length + needed > MaximumDisplayNameLength) break;
            if (pendingSpace) builder.Append(' ');
            builder.Append(rune.ToString());
            pendingSpace = false;
        }
        return builder.Length == 0 ? fallback : builder.ToString();
    }

    private static bool LooksLikePath(string value) =>
        value.StartsWith("\\\\", StringComparison.Ordinal) ||
        value[0] == '/' ||
        value.Contains(":\\", StringComparison.Ordinal) ||
        value.Contains(":/", StringComparison.Ordinal);
}
