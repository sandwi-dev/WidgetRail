namespace WidgetRail.PlatformBroker;

using WidgetRail.Internal;

public enum BrokerCapabilityKind
{
    Read,
    Control,
}

public enum BrokerCapabilityAccessPolicy
{
    /// <summary>The widget manifest declares the capability and the user decides consent.</summary>
    ManifestConsent,
    /// <summary>
    /// The trusted host may attach the capability to an authenticated channel.
    /// It cannot be declared by a widget manifest and has no consent decision.
    /// </summary>
    HostGranted,
}

public sealed record BrokerCapabilityDefinition(
    string Id,
    int Version,
    BrokerCapabilityKind Kind,
    IReadOnlySet<string> Operations,
    IReadOnlySet<string> Events,
    bool AllowsDashboardGesture = false,
    IReadOnlySet<string>? ReadOperations = null,
    BrokerCapabilityAccessPolicy AccessPolicy = BrokerCapabilityAccessPolicy.ManifestConsent,
    bool AllowsBackground = false,
    IReadOnlySet<string>? InFlightContinuationOperations = null)
{
    public BrokerCapabilityKind KindForOperation(string operation) =>
        ReadOperations?.Contains(operation) == true ? BrokerCapabilityKind.Read : Kind;

    public bool AllowsDashboardGestureForOperation(string operation) =>
        KindForOperation(operation) == BrokerCapabilityKind.Control && AllowsDashboardGesture;

    /// <summary>
    /// Identifies exact operations whose already-authorized request lease may
    /// finish after the widget leaves Interactive. This never authorizes a new
    /// request that begins in Visible or Background.
    /// </summary>
    public bool AllowsInFlightContinuationForOperation(string operation) =>
        InFlightContinuationOperations?.Contains(operation) == true;
}

/// <summary>Closed public capability vocabulary. Version is part of every ID.</summary>
public static class PlatformCapabilities
{
    public const string DisplaysReadV1 = "system.displays.read.v1";
    public const string DisplaysControlV1 = "system.displays.control.v1";
    public const string DisplayProfilesGet = "display-profiles.get";
    public const string DisplayProfilesSave = "display-profiles.save";
    public const string DisplayProfilesRename = "display-profiles.rename";
    public const string DisplayProfilesReplace = "display-profiles.replace";
    public const string DisplayProfilesDelete = "display-profiles.delete";
    public const string DisplayProfilesApply = "display-profiles.apply";
    public const string DisplayProfilesKeep = "display-profiles.keep";
    public const string DisplayProfilesRevert = "display-profiles.revert";
    public const string DisplayProfilesChanged = "display-profiles.changed";
    public const string PowerReadV1 = "system.power.read.v1";
    public const string PowerControlV1 = "system.power.control.v1";
    public const string PowerGet = "power.get";
    public const string PowerShutDown = "power.shut-down";
    public const string PowerRestart = "power.restart";
    public const string PowerSleep = "power.sleep";
    public const string AudioSessionsReadV1 = "system.audio.sessions.read.v1";
    public const string AudioSessionsControlV1 = "system.audio.sessions.control.v1";
    public const string AudioOutputReadV1 = "system.audio.output.read.v1";
    public const string AudioOutputControlV1 = "system.audio.output.control.v1";
    public const string AudioDevicesReadV1 = "system.audio.devices.read.v1";
    public const string AudioSpatialReadV1 = "system.audio.spatial.read.v1";
    public const string AudioSpatialControlV1 = "system.audio.spatial.control.v1";
    public const string AudioSpatialGet = "audio.spatial.get";
    public const string AudioSpatialSet = "audio.spatial.set-format";
    public const string AudioSpatialChanged = "audio.spatial.changed";
    public const string AudioDevicesControlV1 = "system.audio.devices.control.v1";
    public const string AudioInputReadV1 = "system.audio.input.read.v1";
    public const string AudioInputControlV1 = "system.audio.input.control.v1";
    public const string NetworkReadV1 = "system.network.read.v1";
    public const string NetworkDetailsReadV1 = "system.network.details.read.v1";
    public const string NetworkSavedProfileSwitchV1 = "system.network.saved-profile.switch.v1";
    public const string NetworkWifiReadV1 = "system.network.wifi.read.v1";
    public const string NetworkWifiManageV1 = "system.network.wifi.manage.v1";
    public const string NetworkWifiConnectV1 = "system.network.wifi.connect.v1";
    public const string NetworkWifiRadioReadV1 = "system.network.wifi.radio.read.v1";
    public const string NetworkWifiRadioControlV1 = "system.network.wifi.radio.control.v1";
    public const string NetworkBluetoothReadV1 = "system.network.bluetooth.read.v1";
    public const string NetworkBluetoothRadioControlV1 = "system.network.bluetooth.radio.control.v1";
    public const string NetworkBluetoothPairV1 = "system.network.bluetooth.pair.v1";
    public const string NetworkBluetoothUnpairV1 = "system.network.bluetooth.unpair.v1";
    public const string NetworkBluetoothManageV1 = "system.network.bluetooth.manage.v1";
    public const string RecentActivityReadV1 = "system.activity.recent.read.v1";
    public const string AppLibraryReadV1 = "system.apps.library.read.v1";
    public const string TaskWindowsPreviewV1 = "system.apps.windows.preview.v1";
    public const string TaskWindowsReadV1 = "system.apps.windows.read.v1";
    public const string TaskWindowsSwitchV1 = "system.apps.windows.switch.v1";
    public const string TaskWindowsCloseV1 = "system.apps.windows.close.v1";
    public const string TaskWindowsList = "apps.windows.list";
    public const string TaskWindowsSwitch = "apps.windows.switch";
    public const string TaskWindowsClose = "apps.windows.close";
    public const string AppRunningReadV1 = "system.apps.running.read.v1";
    public const string AppRunningRegisterV1 = "system.apps.running.register.v1";
    public const string AppLibraryLaunchV1 = "system.apps.library.launch.v1";
    public const string MediaSessionsReadV1 = "system.media.sessions.read.v1";
    public const string MediaSessionsControlV1 = "system.media.sessions.control.v1";
    public const string PrivateSecretsV1 = "storage.private-secrets.v1";
    public const string PrivateStateV1 = "storage.private-state.v1";

    public const string LoopbackCapabilityPrefix = "network.loopback:";
    public const int MinimumLoopbackPort = CommunityPlatformContractLimits.MinimumLoopbackPort;
    public const int MaximumLoopbackPort = CommunityPlatformContractLimits.MaximumLoopbackPort;

    public const string AudioSessionsList = "audio.sessions.list";
    public const string AudioSessionSetVolume = "audio.session.set-volume";
    public const string AudioSessionSetMuted = "audio.session.set-muted";
    public const string AudioOutputGet = "audio.output.get";
    public const string AudioOutputSetVolume = "audio.output.set-volume";
    public const string AudioOutputSetMuted = "audio.output.set-muted";
    public const string AudioDevicesList = "audio.devices.list";
    public const string AudioDefaultOutputSet = "audio.devices.set-default-output";
    public const string AudioDefaultInputSet = "audio.devices.set-default-input";
    public const string AudioInputGet = "audio.input.get";
    public const string AudioInputSetVolume = "audio.input.set-volume";
    public const string AudioInputSetMuted = "audio.input.set-muted";
    public const string NetworkStatusGet = "network.status.get";
    public const string NetworkDetailsGet = "network.details.get";
    public const string NetworkSavedProfilesList = "network.saved-profiles.list";
    public const string NetworkSavedProfileSwitch = "network.saved-profile.switch";
    public const string NetworkAvailableWifiGet = "network.wifi.available.get";
    public const string NetworkWifiScan = "network.wifi.scan";
    public const string NetworkWifiDisconnect = "network.wifi.disconnect";
    public const string NetworkWifiProfileForget = "network.wifi.profile.forget";
    public const string NetworkWifiAutoConnectSet = "network.wifi.profile.auto-connect.set";
    public const string NetworkAvailableWifiConnect = "network.wifi.connect";
    public const string NetworkWifiRadioGet = "network.wifi.radio.get";
    public const string NetworkWifiRadioSet = "network.wifi.radio.set";
    public const string NetworkBluetoothGet = "network.bluetooth.get";
    public const string NetworkBluetoothRadioSet = "network.bluetooth.radio.set";
    public const string NetworkBluetoothDevicePair = "network.bluetooth.device.pair";
    public const string NetworkBluetoothDeviceUnpair = "network.bluetooth.device.unpair";
    public const string NetworkBluetoothDeviceSettingsOpen =
        "network.bluetooth.device.settings.open";
    public const string RecentActivitiesList = "activity.recent.list";
    public const string AppLibraryList = "apps.library.list";
    public const string AppLibraryResolveSaved = "apps.library.resolve-saved";
    public const string AppRunningList = "apps.running.list";
    public const string AppRunningConfirm = "apps.running.confirm";
    public const string AppRunningRegister = "apps.running.register";
    public const string AppRunningForget = "apps.running.forget";
    public const string AppLibraryLaunch = "apps.library.launch";
    internal const string AppLibraryLaunchObserved = "apps.library.launch-observed";
    public const string MediaSessionsGet = "media.sessions.get";
    public const string MediaSessionControl = "media.session.control";
    public const string LoopbackHttpGetJson = "loopback.http.get-json";
    public const string LoopbackHttpPostJson = "loopback.http.post-json";
    public const string PrivateSecretExists = "private-secret.exists";
    public const string PrivateSecretMetadata = "private-secret.metadata";
    public const string PrivateSecretSave = "private-secret.save";
    public const string PrivateSecretDelete = "private-secret.delete";
    public const string PrivateStateRead = "private-state.read";
    public const string PrivateStateWrite = "private-state.write";
    public const string PrivateStateClear = "private-state.clear";

    public const string AudioSessionsChanged = "audio.sessions.changed";
    public const string AudioOutputChanged = "audio.output.changed";
    public const string AudioDevicesChanged = "audio.devices.changed";
    public const string AudioInputChanged = "audio.input.changed";
    public const string NetworkStatusChanged = "network.status.changed";
    public const string NetworkDetailsChanged = "network.details.changed";
    public const string NetworkAvailableWifiChanged = "network.wifi.available.changed";
    public const string NetworkWifiRadioChanged = "network.wifi.radio.changed";
    public const string NetworkBluetoothChanged = "network.bluetooth.changed";
    public const string RecentActivitiesChanged = "activity.recent.changed";
    public const string MediaSessionsChanged = "media.sessions.changed";

    private static readonly IReadOnlyDictionary<string, BrokerCapabilityDefinition> Definitions =
        new Dictionary<string, BrokerCapabilityDefinition>(StringComparer.Ordinal)
        {
            [DisplaysReadV1] = new(DisplaysReadV1, 1, BrokerCapabilityKind.Read, Set(DisplayProfilesGet), Set(DisplayProfilesChanged)),
            [DisplaysControlV1] = new(DisplaysControlV1, 1, BrokerCapabilityKind.Control,
                Set(DisplayProfilesSave, DisplayProfilesRename, DisplayProfilesReplace, DisplayProfilesDelete,
                    DisplayProfilesApply, DisplayProfilesKeep, DisplayProfilesRevert), Set(),
                InFlightContinuationOperations: Set(DisplayProfilesApply, DisplayProfilesKeep, DisplayProfilesRevert)),
            [PowerReadV1] = new(PowerReadV1, 1, BrokerCapabilityKind.Read, Set(PowerGet), Set()),
            [PowerControlV1] = new(PowerControlV1, 1, BrokerCapabilityKind.Control,
                Set(PowerShutDown, PowerRestart, PowerSleep), Set()),
            [AudioSessionsReadV1] = new(AudioSessionsReadV1, 1, BrokerCapabilityKind.Read,
                Set(AudioSessionsList), Set(AudioSessionsChanged)),
            [AudioSessionsControlV1] = new(AudioSessionsControlV1, 1, BrokerCapabilityKind.Control,
                Set(AudioSessionSetVolume, AudioSessionSetMuted), Set()),
            [AudioOutputReadV1] = new(AudioOutputReadV1, 1, BrokerCapabilityKind.Read,
                Set(AudioOutputGet), Set(AudioOutputChanged)),
            [AudioOutputControlV1] = new(AudioOutputControlV1, 1, BrokerCapabilityKind.Control,
                Set(AudioOutputSetVolume, AudioOutputSetMuted), Set(),
                AllowsDashboardGesture: true),
            [AudioDevicesReadV1] = new(AudioDevicesReadV1, 1, BrokerCapabilityKind.Read,
                Set(AudioDevicesList), Set(AudioDevicesChanged)),
            [AudioSpatialReadV1] = new(AudioSpatialReadV1, 1, BrokerCapabilityKind.Read,
                Set(AudioSpatialGet), Set(AudioSpatialChanged)),
            [AudioSpatialControlV1] = new(AudioSpatialControlV1, 1, BrokerCapabilityKind.Control,
                Set(AudioSpatialSet), Set()),
            [AudioDevicesControlV1] = new(AudioDevicesControlV1, 1, BrokerCapabilityKind.Control,
                Set(AudioDefaultOutputSet, AudioDefaultInputSet), Set()),
            [AudioInputReadV1] = new(AudioInputReadV1, 1, BrokerCapabilityKind.Read,
                Set(AudioInputGet), Set(AudioInputChanged)),
            [AudioInputControlV1] = new(AudioInputControlV1, 1, BrokerCapabilityKind.Control,
                Set(AudioInputSetVolume, AudioInputSetMuted), Set()),
            [NetworkReadV1] = new(NetworkReadV1, 1, BrokerCapabilityKind.Read,
                Set(NetworkStatusGet, NetworkSavedProfilesList), Set(NetworkStatusChanged)),
            [NetworkDetailsReadV1] = new(NetworkDetailsReadV1, 1, BrokerCapabilityKind.Read,
                Set(NetworkDetailsGet), Set(NetworkDetailsChanged)),
            [NetworkSavedProfileSwitchV1] = new(NetworkSavedProfileSwitchV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkSavedProfileSwitch), Set()),
            [NetworkWifiReadV1] = new(NetworkWifiReadV1, 1, BrokerCapabilityKind.Read,
                Set(NetworkAvailableWifiGet, NetworkWifiScan), Set(NetworkAvailableWifiChanged)),
            [NetworkWifiConnectV1] = new(NetworkWifiConnectV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkAvailableWifiConnect), Set()),
            [NetworkWifiManageV1] = new(NetworkWifiManageV1, 1,
                BrokerCapabilityKind.Control,
                Set(NetworkWifiDisconnect, NetworkWifiProfileForget, NetworkWifiAutoConnectSet), Set()),
            [NetworkWifiRadioReadV1] = new(NetworkWifiRadioReadV1, 1,
                BrokerCapabilityKind.Read, Set(NetworkWifiRadioGet), Set(NetworkWifiRadioChanged)),
            [NetworkWifiRadioControlV1] = new(NetworkWifiRadioControlV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkWifiRadioSet), Set()),
            [NetworkBluetoothReadV1] = new(NetworkBluetoothReadV1, 1,
                BrokerCapabilityKind.Read, Set(NetworkBluetoothGet), Set(NetworkBluetoothChanged)),
            [NetworkBluetoothRadioControlV1] = new(NetworkBluetoothRadioControlV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkBluetoothRadioSet), Set()),
            [NetworkBluetoothPairV1] = new(NetworkBluetoothPairV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkBluetoothDevicePair), Set()),
            [NetworkBluetoothUnpairV1] = new(NetworkBluetoothUnpairV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkBluetoothDeviceUnpair), Set()),
            [NetworkBluetoothManageV1] = new(NetworkBluetoothManageV1, 1,
                BrokerCapabilityKind.Control,
                Set(NetworkBluetoothDeviceSettingsOpen), Set()),
            [RecentActivityReadV1] = new(RecentActivityReadV1, 1,
                BrokerCapabilityKind.Read, Set(RecentActivitiesList), Set(RecentActivitiesChanged)),
            [AppLibraryReadV1] = new(AppLibraryReadV1, 1,
                BrokerCapabilityKind.Read,
                Set(AppLibraryList, AppLibraryResolveSaved), Set()),
            [TaskWindowsPreviewV1] = new(TaskWindowsPreviewV1, 1, BrokerCapabilityKind.Read, Set(), Set()),
            [TaskWindowsReadV1] = new(TaskWindowsReadV1, 1, BrokerCapabilityKind.Read,
                Set(TaskWindowsList), Set()),
            [TaskWindowsSwitchV1] = new(TaskWindowsSwitchV1, 1, BrokerCapabilityKind.Control,
                Set(TaskWindowsSwitch), Set(), AllowsDashboardGesture: false,
                InFlightContinuationOperations: Set(TaskWindowsSwitch)),
            [TaskWindowsCloseV1] = new(TaskWindowsCloseV1, 1, BrokerCapabilityKind.Control,
                Set(TaskWindowsClose), Set(), AllowsDashboardGesture: false),
            [AppRunningReadV1] = new(AppRunningReadV1, 1,
                BrokerCapabilityKind.Read,
                Set(AppRunningList, AppRunningConfirm), Set()),
            [AppRunningRegisterV1] = new(AppRunningRegisterV1, 1,
                BrokerCapabilityKind.Control,
                Set(AppRunningRegister, AppRunningForget), Set(),
                AllowsDashboardGesture: false),
            [AppLibraryLaunchV1] = new(AppLibraryLaunchV1, 1,
                BrokerCapabilityKind.Control,
                Set(AppLibraryLaunch, AppLibraryLaunchObserved), Set(),
                AllowsDashboardGesture: false,
                InFlightContinuationOperations: Set(AppLibraryLaunch, AppLibraryLaunchObserved)),
            [MediaSessionsReadV1] = new(MediaSessionsReadV1, 1,
                BrokerCapabilityKind.Read, Set(MediaSessionsGet), Set(MediaSessionsChanged)),
            [MediaSessionsControlV1] = new(MediaSessionsControlV1, 1,
                BrokerCapabilityKind.Control, Set(MediaSessionControl), Set(),
                AllowsDashboardGesture: true),
            [PrivateSecretsV1] = new(PrivateSecretsV1, 1,
                BrokerCapabilityKind.Control,
                Set(PrivateSecretExists, PrivateSecretMetadata,
                    PrivateSecretSave, PrivateSecretDelete),
                Set(),
                AllowsDashboardGesture: false,
                ReadOperations: Set(PrivateSecretExists, PrivateSecretMetadata)),
            [PrivateStateV1] = new(PrivateStateV1, 1,
                BrokerCapabilityKind.Control,
                Set(PrivateStateRead, PrivateStateWrite, PrivateStateClear),
                Set(),
                AllowsDashboardGesture: false,
                ReadOperations: Set(PrivateStateRead),
                AccessPolicy: BrokerCapabilityAccessPolicy.HostGranted,
                AllowsBackground: true),
        };

    public static IReadOnlyCollection<BrokerCapabilityDefinition> All { get; } =
        Definitions.Values.ToArray();

    public static bool TryGet(string id, out BrokerCapabilityDefinition definition)
    {
        if (Definitions.TryGetValue(id, out definition!)) return true;
        if (!TryGetLoopbackPort(id, out _)) return false;
        definition = new BrokerCapabilityDefinition(
            id,
            1,
            BrokerCapabilityKind.Control,
            Set(LoopbackHttpGetJson, LoopbackHttpPostJson),
            Set(),
            AllowsDashboardGesture: true,
            ReadOperations: Set(LoopbackHttpGetJson));
        return true;
    }

    public static bool IsManifestDeclarable(string id) =>
        TryGet(id, out var definition) &&
        definition.AccessPolicy == BrokerCapabilityAccessPolicy.ManifestConsent;

    public static bool IsHostGranted(string id) =>
        TryGet(id, out var definition) &&
        definition.AccessPolicy == BrokerCapabilityAccessPolicy.HostGranted;

    public static bool TryGetLoopbackPort(string? capabilityId, out int port)
    {
        port = 0;
        if (capabilityId is null ||
            !capabilityId.StartsWith(LoopbackCapabilityPrefix, StringComparison.Ordinal))
            return false;
        var text = capabilityId.AsSpan(LoopbackCapabilityPrefix.Length);
        if (text.Length is < 1 or > 5 || text[0] == '0' ||
            !int.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out port) ||
            port is < MinimumLoopbackPort or > MaximumLoopbackPort)
        {
            port = 0;
            return false;
        }
        return true;
    }

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
