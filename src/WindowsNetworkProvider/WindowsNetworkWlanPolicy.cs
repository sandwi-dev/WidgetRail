using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsNetworkProvider;

internal readonly record struct NativeWlanNotificationProjection(
    NativeNetworkConnectionOutcome? ConnectionOutcome,
    NativeWifiScanOutcome? ScanOutcome,
    NativeProtectedWifiRollbackResult? RollbackResult = null);

/// <summary>
/// Bounded saved-profile, available-network, scan, connect, and WLAN callback state.
/// The adapter supplies its current handle and serializes every call through its sole WLAN gate;
/// this policy retains no native lifetime, callback, generation, or synchronization authority.
/// </summary>
internal sealed class WindowsNetworkWlanPolicy
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorAccessDenied = 5;
    private const uint WlanNotificationSourceAcm = 0x00000008;
    private const uint WlanNotificationAcmScanComplete = 7;
    private const uint WlanNotificationAcmScanFail = 8;
    private const uint WlanNotificationAcmConnectionComplete = 10;
    private const uint WlanNotificationAcmConnectionAttemptFail = 11;
    private const int WlanConnectionModeProfile = 0;
    private const int WlanConnectionModeDiscoveryUnsecure = 3;
    private const int Dot11BssTypeAny = 3;
    private const int MaximumInterfaces = 32;
    private const int MaximumProfiles = 128;
    private const int MaximumAvailableNetworks = 256;
    private const uint WlanAvailableNetworkConnected = 0x00000001;
    private const uint WlanAvailableNetworkHasProfile = 0x00000002;

    private readonly Dictionary<string, WifiProfileOptions> _profileOptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeProfileTarget> _connectableProfiles =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeAvailableNetworkTarget> _connectableNetworks =
        new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _pendingScanInterfaces = [];
    private NativeWifiScanState _scanState = NativeWifiScanState.NotScanned;
    private long _scanGeneration;
    private bool _scanHadSuccess;
    private PendingNativeConnection? _pendingConnection;
    private long _cachedAvailableGeneration = -1;
    private IReadOnlyList<NativeAvailableWifiNetwork> _cachedAvailableNetworks = [];

    public IReadOnlyList<WirelessInterface> EnumerateInterfaces(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle)
    {
        if (handle == IntPtr.Zero ||
            calls.EnumerateWlanInterfaces(handle, out var pointer) != ErrorSuccess ||
            pointer == IntPtr.Zero) return [];
        try
        {
            var count = ReadBoundedCount(pointer, MaximumInterfaces);
            if (count == 0) return [];
            var offset = 8;
            var size = Marshal.SizeOf<WlanInterfaceInfo>();
            var interfaces = new List<WirelessInterface>(count);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<WlanInterfaceInfo>(
                    pointer + offset + index * size);
                if (item.InterfaceGuid != Guid.Empty)
                    interfaces.Add(new(item.InterfaceGuid, item.State));
            }
            return interfaces;
        }
        finally
        {
            calls.FreeWlanMemory(pointer);
        }
    }

    public IReadOnlyList<NativeSavedNetworkProfile> ReadSavedProfiles(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        IReadOnlyList<WirelessInterface> interfaces)
    {
        var profiles = new List<NativeSavedNetworkProfile>();
        _connectableProfiles.Clear();
        foreach (var wireless in interfaces)
        {
            foreach (var profileName in EnumerateProfiles(calls, handle, wireless.InterfaceId))
            {
                if (profiles.Count >= MaximumProfiles) break;
                var nativeKey = NativeKey(wireless.InterfaceId, profileName);
                if (!_connectableProfiles.TryAdd(
                        nativeKey,
                        new(wireless.InterfaceId, profileName))) continue;
                if (!_profileOptions.TryGetValue(nativeKey, out var options))
                {
                    options = WindowsWifiProfilePolicy.Read(calls, handle, wireless.InterfaceId, profileName);
                    _profileOptions[nativeKey] = options;
                }
                profiles.Add(new(nativeKey, profileName, false, null)
                { AutoConnect = options.AutoConnect, CanManage = options.CanManage });
            }
            if (profiles.Count >= MaximumProfiles) break;
        }
        foreach (var missing in _profileOptions.Keys.Where(key => !_connectableProfiles.ContainsKey(key)).ToArray())
            _profileOptions.Remove(missing);
        return profiles;
    }

    public void ManageWifiProfile(IWindowsNetworkNativeCalls calls, IntPtr handle,
        string nativeKey, bool? autoConnect)
    {
        if (handle == IntPtr.Zero || !_connectableProfiles.TryGetValue(nativeKey, out var target))
            throw new BrokerException("resource_not_found", "That saved network is no longer available.");
        try { WindowsWifiProfilePolicy.Change(calls, handle, target.InterfaceId, target.ProfileName, autoConnect); }
        finally { _profileOptions.Remove(nativeKey); }
        // Invalidate cached HasSavedProfile flags without initiating a radio scan.
        _cachedAvailableGeneration = -1;
    }

    public void DisconnectWifi(IWindowsNetworkNativeCalls calls, IntPtr handle, string nativeKey)
    {
        if (handle == IntPtr.Zero || !_connectableNetworks.TryGetValue(nativeKey, out var target))
            throw new BrokerException("resource_not_found", "That Wi-Fi connection is no longer available.");
        // Validate the selected connection again. Never disconnect a different network
        // that Windows may have connected to since the widget snapshot was captured.
        var current = EnumerateAvailableNetworks(calls, handle, target.InterfaceId).Networks
            .Any(item => (item.Flags & WlanAvailableNetworkConnected) != 0 &&
                item.Ssid.AsSpan().SequenceEqual(target.Ssid) &&
                item.AuthenticationAlgorithm == target.AuthenticationAlgorithm &&
                item.CipherAlgorithm == target.CipherAlgorithm);
        if (!current)
            throw new BrokerException("resource_not_found", "The selected network is no longer connected.");
        WindowsWifiProfilePolicy.DemandSuccess(calls.DisconnectWlan(handle, target.InterfaceId));
        _cachedAvailableGeneration = -1;
    }

    public bool TryConnectSavedProfile(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        string nativeProfileKey)
    {
        if (handle == IntPtr.Zero) return false;
        if (!_connectableProfiles.TryGetValue(nativeProfileKey, out var target)) return false;
        var result = calls.ConnectWlan(
            handle,
            target.InterfaceId,
            new(WlanConnectionModeProfile, target.ProfileName, null, Dot11BssTypeAny));
        if (result == ErrorSuccess)
        {
            _pendingConnection = new(
                nativeProfileKey,
                target.InterfaceId,
                target.ProfileName,
                [],
                false);
            return true;
        }
        throw new Win32Exception(
            (int)result,
            "Windows could not start the saved Wi-Fi connection.");
    }

    public NativeAvailableWifiSnapshot ReadAvailableSnapshot(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle)
    {
        var state = _scanState;
        var generation = _scanGeneration;
        if (state == NativeWifiScanState.Ready && _cachedAvailableGeneration == generation)
            return new(generation, state, _cachedAvailableNetworks.ToArray());
        if (state != NativeWifiScanState.Ready) return new(generation, state, []);

        // Profile notifications may refresh the saved/credential flags while a
        // connection is in flight. Keep its target stable until the next scan;
        // StartScan clears this map before admitting new scan results.
        var previousTargets = _connectableNetworks.ToDictionary(
            entry => AvailableNetworkDeduplicationKey(entry.Value.InterfaceId, entry.Value.Ssid,
                entry.Value.AuthenticationAlgorithm, entry.Value.CipherAlgorithm),
            entry => entry.Key, StringComparer.Ordinal);
        var networks = new List<NativeAvailableWifiNetwork>();
        var targets = new Dictionary<string, NativeAvailableNetworkTarget>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accessDenied = false;
        var anyInterfaceRead = false;
        foreach (var wireless in EnumerateInterfaces(calls, handle))
        {
            var result = EnumerateAvailableNetworks(calls, handle, wireless.InterfaceId);
            if (result.AccessDenied)
            {
                accessDenied = true;
                continue;
            }
            if (!result.Succeeded) continue;
            anyInterfaceRead = true;
            foreach (var item in result.Networks)
            {
                if (networks.Count >= MaximumAvailableNetworks) break;
                var connected = (item.Flags & WlanAvailableNetworkConnected) != 0;
                var hasProfile = (item.Flags & WlanAvailableNetworkHasProfile) != 0 &&
                    !string.IsNullOrWhiteSpace(item.ProfileName);
                if (!ShouldExposeAvailableNetwork(
                        item.IsConnectable,
                        connected,
                        hasProfile,
                        item.Ssid.Length)) continue;
                var deduplicationKey = AvailableNetworkDeduplicationKey(
                    wireless.InterfaceId,
                    item.Ssid,
                    item.AuthenticationAlgorithm,
                    item.CipherAlgorithm);
                if (!seen.Add(deduplicationKey)) continue;
                var nativeKey = previousTargets.TryGetValue(deduplicationKey, out var retainedKey)
                    ? retainedKey : $"wifi_native_{Guid.NewGuid():N}";
                var security = ClassifySecurity(item.SecurityEnabled, item.AuthenticationAlgorithm);
                var credentialRequired = !connected && !hasProfile && item.SecurityEnabled;
                targets.Add(nativeKey, new(
                    wireless.InterfaceId,
                    item.Ssid,
                    item.BssType,
                    hasProfile ? item.ProfileName : null,
                    security,
                    credentialRequired,
                    item.AuthenticationAlgorithm,
                    item.CipherAlgorithm));
                networks.Add(new(
                    nativeKey,
                    DecodeSsid(item.Ssid),
                    checked((int)Math.Min(item.SignalQuality, 100u)),
                    security,
                    credentialRequired,
                    connected,
                    hasProfile));
            }
        }

        if (_scanGeneration != generation || _scanState != NativeWifiScanState.Ready)
            return new(_scanGeneration, _scanState, []);
        _connectableNetworks.Clear();
        if (!anyInterfaceRead)
        {
            _scanState = accessDenied
                ? NativeWifiScanState.PreciseLocationDenied
                : NativeWifiScanState.Unavailable;
            return new(_scanGeneration, _scanState, []);
        }
        foreach (var target in targets) _connectableNetworks.Add(target.Key, target.Value);
        _cachedAvailableGeneration = generation;
        _cachedAvailableNetworks = networks.ToArray();
        return new(generation, NativeWifiScanState.Ready, _cachedAvailableNetworks.ToArray());
    }

    public NativeWifiScanStartResult TryStartScan(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return SetScanStartFailure(
                NativeWifiScanState.Unavailable,
                NativeWifiScanStartResult.Unavailable);
        var interfaces = EnumerateInterfaces(calls, handle);
        if (interfaces.Count == 0)
            return SetScanStartFailure(
                NativeWifiScanState.Unavailable,
                NativeWifiScanStartResult.Unavailable);

        if (_scanState == NativeWifiScanState.Scanning)
            return NativeWifiScanStartResult.AlreadyScanning;
        _pendingScanInterfaces.Clear();
        _connectableNetworks.Clear();
        _cachedAvailableGeneration = -1;
        _cachedAvailableNetworks = [];
        _scanHadSuccess = false;
        _scanState = NativeWifiScanState.Scanning;
        var denied = false;
        foreach (var wireless in interfaces)
        {
            _pendingScanInterfaces.Add(wireless.InterfaceId);
            var result = calls.StartWlanScan(handle, wireless.InterfaceId);
            if (result == ErrorSuccess) continue;
            _pendingScanInterfaces.Remove(wireless.InterfaceId);
            denied |= result == ErrorAccessDenied;
        }
        if (_pendingScanInterfaces.Count != 0) return NativeWifiScanStartResult.Started;
        _scanState = denied
            ? NativeWifiScanState.PreciseLocationDenied
            : NativeWifiScanState.Unavailable;
        return denied
            ? NativeWifiScanStartResult.PreciseLocationDenied
            : NativeWifiScanStartResult.Unavailable;
    }

    public NativeWifiConnectStartResult TryConnectAvailableNetwork(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        string nativeNetworkKey)
    {
        if (handle == IntPtr.Zero) return NativeWifiConnectStartResult.Unavailable;
        if (!_connectableNetworks.TryGetValue(nativeNetworkKey, out var target))
            return NativeWifiConnectStartResult.NotFound;
        if (target.CredentialRequired)
            return target.Security == WifiSecurityKind.Enterprise
                ? NativeWifiConnectStartResult.UnsupportedAuthentication
                : NativeWifiConnectStartResult.CredentialRequired;
        var result = calls.ConnectWlan(
            handle,
            target.InterfaceId,
            new(
                target.ProfileName is null
                    ? WlanConnectionModeDiscoveryUnsecure
                    : WlanConnectionModeProfile,
                target.ProfileName,
                target.ProfileName is null ? target.Ssid.ToArray() : null,
                target.ProfileName is null ? target.BssType : Dot11BssTypeAny));
        if (result != ErrorSuccess)
            return result == ErrorAccessDenied
                ? NativeWifiConnectStartResult.Unavailable
                : NativeWifiConnectStartResult.NotFound;
        _pendingConnection = new(
            nativeNetworkKey,
            target.InterfaceId,
            target.ProfileName,
            target.Ssid.ToArray(),
            false);
        return NativeWifiConnectStartResult.Started;
    }

    public NativeProtectedWifiConnectStartResult TryConnectProtectedNetwork(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        string nativeNetworkKey,
        ReadOnlySpan<char> secret)
    {
        if (handle == IntPtr.Zero) return NativeProtectedWifiConnectStartResult.Unavailable;
        if (!_connectableNetworks.TryGetValue(nativeNetworkKey, out var target))
            return NativeProtectedWifiConnectStartResult.NotFound;
        if (!target.CredentialRequired || target.ProfileName is not null)
            return NativeProtectedWifiConnectStartResult.NotFound;
        if (target.AuthenticationAlgorithm is not (7u or 9u) || target.CipherAlgorithm != 4u)
            return NativeProtectedWifiConnectStartResult.UnsupportedAuthentication;
        if (!ProtectedWifiProfile.TryCreate(
                target.Ssid,
                target.AuthenticationAlgorithm,
                target.CipherAlgorithm,
                secret,
                out var profile))
            return NativeProtectedWifiConnectStartResult.InvalidCredential;
        using (profile)
        {
            var setResult = calls.SetWlanProfile(
                handle, target.InterfaceId, profile!.Xml, out _);
            if (setResult == 183u)
                return NativeProtectedWifiConnectStartResult.ProfileAlreadyExists;
            if (setResult != ErrorSuccess)
                return setResult == ErrorAccessDenied
                    ? NativeProtectedWifiConnectStartResult.Unavailable
                    : NativeProtectedWifiConnectStartResult.UnsupportedAuthentication;
            if (calls.SetWlanProfileCustomUserData(
                    handle,
                    target.InterfaceId,
                    profile.Name,
                    profile.OwnershipToken) != ErrorSuccess)
                return NativeProtectedWifiConnectStartResult.RollbackUnverified;
            if (VerifyProfileOwnership(
                    calls,
                    handle,
                    target.InterfaceId,
                    profile.Name,
                    profile.OwnershipToken) != ProfileOwnershipVerification.Match)
                return NativeProtectedWifiConnectStartResult.RollbackUnverified;
            var connectResult = calls.ConnectWlan(
                handle,
                target.InterfaceId,
                new(WlanConnectionModeProfile, profile.Name, null, Dot11BssTypeAny));
            if (connectResult != ErrorSuccess)
            {
                var rollback = RollbackProfile(
                    calls,
                    handle,
                    target.InterfaceId,
                    profile.Name,
                    profile.OwnershipToken);
                return rollback == NativeProtectedWifiRollbackResult.Deleted
                    ? NativeProtectedWifiConnectStartResult.Unavailable
                    : NativeProtectedWifiConnectStartResult.RollbackUnverified;
            }
            _pendingConnection = new(
                nativeNetworkKey,
                target.InterfaceId,
                profile.Name,
                target.Ssid.ToArray(),
                true,
                profile.TakeOwnershipToken());
            return NativeProtectedWifiConnectStartResult.Started;
        }
    }

    public NativeWlanNotificationProjection ProcessNotification(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        ref WlanNotificationData data)
    {
        if (data.NotificationSource == WlanNotificationSourceAcm &&
            data.NotificationCode is WlanNotificationAcmScanComplete or
                WlanNotificationAcmScanFail)
        {
            NativeWifiScanOutcome? scanOutcome = null;
            if (_scanState == NativeWifiScanState.Scanning &&
                _pendingScanInterfaces.Remove(data.InterfaceGuid))
            {
                _scanHadSuccess |= data.NotificationCode == WlanNotificationAcmScanComplete;
                if (_pendingScanInterfaces.Count == 0)
                {
                    _scanState = _scanHadSuccess
                        ? NativeWifiScanState.Ready
                        : NativeWifiScanState.Unavailable;
                    _scanGeneration++;
                    scanOutcome = _scanHadSuccess
                        ? NativeWifiScanOutcome.Completed
                        : NativeWifiScanOutcome.Failed;
                }
            }
            if (scanOutcome is not null) return new(null, scanOutcome);
        }

        if (data.NotificationSource == WlanNotificationSourceAcm && data.NotificationCode == 15)
            _profileOptions.Clear();

        // WlanDisconnect acknowledges the request before Windows disconnects.
        // Re-read on the completion event rather than keeping the pre-disconnect cache.
        if (data.NotificationSource == WlanNotificationSourceAcm && data.NotificationCode is 15 or 21)
            _cachedAvailableGeneration = -1; // profile change / disconnected

        NativeNetworkConnectionOutcome? outcome = null;
        NativeProtectedWifiRollbackResult? rollbackResult = null;
        if (data.NotificationSource == WlanNotificationSourceAcm &&
            data.NotificationCode is WlanNotificationAcmConnectionComplete or
                WlanNotificationAcmConnectionAttemptFail &&
            data.DataPointer != IntPtr.Zero && data.DataSize >= 516)
        {
            var profileName = Marshal.PtrToStringUni(data.DataPointer + 4, 256)?.TrimEnd('\0');
            var notificationSsid = data.DataSize >= 516u + (uint)Marshal.SizeOf<Dot11Ssid>()
                ? ReadSsid(Marshal.PtrToStructure<Dot11Ssid>(data.DataPointer + 516))
                : [];
            PendingNativeConnection? matchedPending = null;
            if (_pendingConnection is { } pending && ConnectionNotificationMatches(
                    pending.InterfaceId,
                    data.InterfaceGuid,
                    pending.ProfileName,
                    pending.Ssid,
                    profileName,
                    notificationSsid))
            {
                matchedPending = pending;
                _pendingConnection = null;
            }
            if (matchedPending is not null &&
                data.NotificationCode == WlanNotificationAcmConnectionComplete)
            {
                _cachedAvailableNetworks = _cachedAvailableNetworks.Select(network =>
                {
                    var connected = network.NativeNetworkKey == matchedPending.NativeKey;
                    return network with
                    {
                        IsConnected = connected,
                        CredentialRequired = connected ? false : network.CredentialRequired,
                        HasSavedProfile = network.HasSavedProfile || connected && !string.IsNullOrEmpty(matchedPending.ProfileName),
                    };
                }).ToArray();
                if (_connectableNetworks.TryGetValue(matchedPending.NativeKey, out var target))
                    _connectableNetworks[matchedPending.NativeKey] = target with
                    { CredentialRequired = false, ProfileName = matchedPending.ProfileName ?? target.ProfileName };
            }
            if (matchedPending is not null)
            {
                if (matchedPending is { CreatedProfile: true } &&
                    data.NotificationCode == WlanNotificationAcmConnectionComplete &&
                    matchedPending.ProfileName is { Length: > 0 } &&
                    calls.SetWlanProfileCustomUserData(
                        handle,
                        matchedPending.InterfaceId,
                        matchedPending.ProfileName,
                        []) != ErrorSuccess)
                    rollbackResult = NativeProtectedWifiRollbackResult.VerificationUnavailable;
                if (matchedPending is { CreatedProfile: true } &&
                    data.NotificationCode == WlanNotificationAcmConnectionAttemptFail &&
                    matchedPending.ProfileName is { Length: > 0 })
                    rollbackResult = RollbackProfile(
                        calls,
                        handle,
                        matchedPending.InterfaceId,
                        matchedPending.ProfileName,
                        matchedPending.OwnershipToken);
                CryptographicOperations.ZeroMemory(matchedPending.OwnershipToken);
            }
            if (matchedPending is not null || !string.IsNullOrEmpty(profileName))
            {
                outcome = new(
                    matchedPending?.NativeKey ?? NativeKey(data.InterfaceGuid, profileName!),
                    data.NotificationCode == WlanNotificationAcmConnectionComplete
                        ? NativeNetworkConnectionResult.Succeeded
                        : NativeNetworkConnectionResult.Failed);
            }
        }
        return new(outcome, null, rollbackResult);
    }

    public void Clear()
    {
        _profileOptions.Clear();
        _connectableProfiles.Clear();
        _connectableNetworks.Clear();
        _pendingScanInterfaces.Clear();
        var pending = _pendingConnection;
        _pendingConnection = null;
        if (pending is not null)
            CryptographicOperations.ZeroMemory(pending.OwnershipToken);
        _cachedAvailableGeneration = -1;
        _cachedAvailableNetworks = [];
    }

    public NativeProtectedWifiRollbackResult RollbackPendingProtected(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle)
    {
        if (_pendingConnection is not { CreatedProfile: true, ProfileName: { Length: > 0 } } pending)
            return NativeProtectedWifiRollbackResult.NothingToRollback;
        _pendingConnection = null;
        try
        {
            return RollbackProfile(
                calls,
                handle,
                pending.InterfaceId,
                pending.ProfileName,
                pending.OwnershipToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pending.OwnershipToken);
        }
    }

    public NativeProtectedWifiRollbackResult RollbackProtected(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        string nativeNetworkKey)
    {
        if (_pendingConnection is not
            { CreatedProfile: true, ProfileName: { Length: > 0 } } pending ||
            !string.Equals(pending.NativeKey, nativeNetworkKey, StringComparison.Ordinal))
            return NativeProtectedWifiRollbackResult.NothingToRollback;
        _pendingConnection = null;
        try
        {
            return RollbackProfile(
                calls,
                handle,
                pending.InterfaceId,
                pending.ProfileName,
                pending.OwnershipToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pending.OwnershipToken);
        }
    }

    private static NativeProtectedWifiRollbackResult RollbackProfile(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        Guid interfaceId,
        string profileName,
        byte[] expectedOwnershipToken)
    {
        var verification = VerifyProfileOwnership(
            calls,
            handle,
            interfaceId,
            profileName,
            expectedOwnershipToken);
        if (verification == ProfileOwnershipVerification.Unavailable)
            return NativeProtectedWifiRollbackResult.VerificationUnavailable;
        if (verification == ProfileOwnershipVerification.Mismatch)
            return NativeProtectedWifiRollbackResult.OwnershipMismatch;
        return calls.DeleteWlanProfile(handle, interfaceId, profileName) == ErrorSuccess
            ? NativeProtectedWifiRollbackResult.Deleted
            : NativeProtectedWifiRollbackResult.DeleteFailed;
    }

    private static ProfileOwnershipVerification VerifyProfileOwnership(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        Guid interfaceId,
        string profileName,
        byte[] expectedOwnershipToken)
    {
        var readResult = calls.GetWlanProfileCustomUserData(
            handle, interfaceId, profileName, out var actualOwnershipToken);
        try
        {
            if (readResult != ErrorSuccess)
                return ProfileOwnershipVerification.Unavailable;
            return actualOwnershipToken.Length == expectedOwnershipToken.Length &&
                CryptographicOperations.FixedTimeEquals(
                    actualOwnershipToken,
                    expectedOwnershipToken)
                ? ProfileOwnershipVerification.Match
                : ProfileOwnershipVerification.Mismatch;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualOwnershipToken);
        }
    }

    private enum ProfileOwnershipVerification
    {
        Match,
        Mismatch,
        Unavailable,
    }

    internal static bool ShouldExposeAvailableNetwork(
        bool isConnectable,
        bool isConnected,
        bool hasSavedProfile,
        int ssidLength) =>
        (isConnectable || isConnected) && (ssidLength > 0 || hasSavedProfile);

    internal static bool ConnectionNotificationMatches(
        Guid expectedInterface,
        Guid actualInterface,
        string? expectedProfile,
        byte[]? expectedSsid,
        string? actualProfile,
        byte[]? actualSsid)
    {
        if (expectedInterface != actualInterface) return false;
        if (!string.IsNullOrEmpty(expectedProfile))
            return string.Equals(expectedProfile, actualProfile, StringComparison.Ordinal);
        return expectedSsid is { Length: > 0 } && actualSsid is not null &&
            expectedSsid.AsSpan().SequenceEqual(actualSsid);
    }

    internal static int ReadBoundedCount(IntPtr pointer, int maximum)
    {
        if (pointer == IntPtr.Zero || maximum <= 0) return 0;
        var declared = Marshal.ReadInt32(pointer);
        return declared <= 0 ? 0 : Math.Min(declared, maximum);
    }

    private IReadOnlyList<string> EnumerateProfiles(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        Guid interfaceId)
    {
        if (calls.EnumerateWlanProfiles(handle, interfaceId, out var pointer) != ErrorSuccess ||
            pointer == IntPtr.Zero) return [];
        try
        {
            var count = ReadBoundedCount(pointer, MaximumProfiles);
            if (count == 0) return [];
            var offset = 8;
            var size = Marshal.SizeOf<WlanProfileInfo>();
            var profiles = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<WlanProfileInfo>(pointer + offset + index * size);
                if (!string.IsNullOrEmpty(item.ProfileName)) profiles.Add(item.ProfileName);
            }
            return profiles;
        }
        finally
        {
            calls.FreeWlanMemory(pointer);
        }
    }

    private AvailableNetworkReadResult EnumerateAvailableNetworks(
        IWindowsNetworkNativeCalls calls,
        IntPtr handle,
        Guid interfaceId)
    {
        var result = calls.EnumerateAvailableNetworks(handle, interfaceId, out var pointer);
        if (result == ErrorAccessDenied) return new(false, true, []);
        if (result != ErrorSuccess || pointer == IntPtr.Zero) return new(false, false, []);
        try
        {
            var count = ReadBoundedCount(pointer, MaximumAvailableNetworks);
            if (count == 0) return new(true, false, []);
            var offset = 8;
            var size = Marshal.SizeOf<WlanAvailableNetwork>();
            var networks = new List<AvailableNetworkData>(count);
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<WlanAvailableNetwork>(
                    pointer + offset + index * size);
                var source = item.Dot11Ssid.Ssid ?? [];
                if (item.Dot11Ssid.SsidLength > 32u ||
                    item.Dot11Ssid.SsidLength > source.Length) continue;
                var ssid = source.AsSpan(0, checked((int)item.Dot11Ssid.SsidLength)).ToArray();
                networks.Add(new(
                    item.ProfileName ?? string.Empty,
                    ssid,
                    item.BssType,
                    item.NetworkConnectable != 0,
                    item.SignalQuality,
                    item.SecurityEnabled != 0,
                    item.DefaultAuthenticationAlgorithm,
                    item.DefaultCipherAlgorithm,
                    item.Flags));
            }
            return new(true, false, networks);
        }
        finally
        {
            calls.FreeWlanMemory(pointer);
        }
    }

    private NativeWifiScanStartResult SetScanStartFailure(
        NativeWifiScanState state,
        NativeWifiScanStartResult result)
    {
        _pendingScanInterfaces.Clear();
        _connectableNetworks.Clear();
        _cachedAvailableGeneration = -1;
        _cachedAvailableNetworks = [];
        _scanState = state;
        return result;
    }

    private static string AvailableNetworkDeduplicationKey(
        Guid interfaceId,
        byte[] ssid,
        uint authentication,
        uint cipher)
    {
        var material = new byte[16 + ssid.Length + 8];
        interfaceId.TryWriteBytes(material);
        ssid.CopyTo(material, 16);
        BitConverter.TryWriteBytes(material.AsSpan(16 + ssid.Length, 4), authentication);
        BitConverter.TryWriteBytes(material.AsSpan(20 + ssid.Length, 4), cipher);
        return Convert.ToHexString(SHA256.HashData(material));
    }

    private static string DecodeSsid(byte[] ssid)
    {
        if (ssid.Length == 0) return "Hidden network";
        var value = Encoding.UTF8.GetString(ssid).Trim();
        return string.IsNullOrWhiteSpace(value) ? "Hidden network" : value;
    }

    private static WifiSecurityKind ClassifySecurity(bool enabled, uint authentication)
    {
        if (!enabled) return WifiSecurityKind.Open;
        return authentication switch
        {
            4 or 7 or 9 => WifiSecurityKind.Personal,
            3 or 6 or 8 or 11 => WifiSecurityKind.Enterprise,
            _ => WifiSecurityKind.Unknown,
        };
    }

    private static byte[] ReadSsid(Dot11Ssid value)
    {
        var source = value.Ssid ?? [];
        if (value.SsidLength > 32u || value.SsidLength > source.Length) return [];
        return source.AsSpan(0, checked((int)value.SsidLength)).ToArray();
    }

    private static string NativeKey(Guid interfaceId, string profileName) =>
        $"{interfaceId:N}|{profileName}";

    private sealed record NativeProfileTarget(Guid InterfaceId, string ProfileName);
    private sealed record PendingNativeConnection(
        string NativeKey,
        Guid InterfaceId,
        string? ProfileName,
        byte[] Ssid,
        bool CreatedProfile,
        byte[] OwnershipToken)
    {
        public PendingNativeConnection(
            string nativeKey,
            Guid interfaceId,
            string? profileName,
            byte[] ssid,
            bool createdProfile)
            : this(nativeKey, interfaceId, profileName, ssid, createdProfile, [])
        {
        }
    }
    private sealed record NativeAvailableNetworkTarget(
        Guid InterfaceId,
        byte[] Ssid,
        int BssType,
        string? ProfileName,
        WifiSecurityKind Security,
        bool CredentialRequired,
        uint AuthenticationAlgorithm,
        uint CipherAlgorithm);
    private sealed record AvailableNetworkData(
        string ProfileName,
        byte[] Ssid,
        int BssType,
        bool IsConnectable,
        uint SignalQuality,
        bool SecurityEnabled,
        uint AuthenticationAlgorithm,
        uint CipherAlgorithm,
        uint Flags);
    private sealed record AvailableNetworkReadResult(
        bool Succeeded,
        bool AccessDenied,
        IReadOnlyList<AvailableNetworkData> Networks);
}
