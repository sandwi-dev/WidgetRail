namespace GameBarAlternative.PlatformBroker;

public enum BrokerCapabilityKind
{
    Read,
    Control,
}

public sealed record BrokerCapabilityDefinition(
    string Id,
    int Version,
    BrokerCapabilityKind Kind,
    IReadOnlySet<string> Operations,
    IReadOnlySet<string> Events);

/// <summary>Closed public capability vocabulary. Version is part of every ID.</summary>
public static class PlatformCapabilities
{
    public const string AudioSessionsReadV1 = "system.audio.sessions.read.v1";
    public const string AudioSessionsControlV1 = "system.audio.sessions.control.v1";
    public const string AudioOutputReadV1 = "system.audio.output.read.v1";
    public const string AudioOutputControlV1 = "system.audio.output.control.v1";
    public const string AudioDevicesReadV1 = "system.audio.devices.read.v1";
    public const string AudioInputReadV1 = "system.audio.input.read.v1";
    public const string AudioInputControlV1 = "system.audio.input.control.v1";
    public const string NetworkReadV1 = "system.network.read.v1";
    public const string NetworkSavedProfileSwitchV1 = "system.network.saved-profile.switch.v1";
    public const string NetworkWifiReadV1 = "system.network.wifi.read.v1";
    public const string NetworkWifiConnectV1 = "system.network.wifi.connect.v1";
    public const string NetworkWifiRadioReadV1 = "system.network.wifi.radio.read.v1";
    public const string NetworkWifiRadioControlV1 = "system.network.wifi.radio.control.v1";
    public const string NetworkBluetoothReadV1 = "system.network.bluetooth.read.v1";
    public const string NetworkBluetoothRadioControlV1 = "system.network.bluetooth.radio.control.v1";
    public const string RecentActivityReadV1 = "system.activity.recent.read.v1";
    public const string RecentActivityActivateV1 = "system.activity.recent.activate.v1";
    public const string MediaSessionsReadV1 = "system.media.sessions.read.v1";
    public const string MediaSessionsControlV1 = "system.media.sessions.control.v1";

    public const string AudioSessionsList = "audio.sessions.list";
    public const string AudioSessionSetVolume = "audio.session.set-volume";
    public const string AudioSessionSetMuted = "audio.session.set-muted";
    public const string AudioOutputGet = "audio.output.get";
    public const string AudioOutputSetVolume = "audio.output.set-volume";
    public const string AudioOutputSetMuted = "audio.output.set-muted";
    public const string AudioDevicesList = "audio.devices.list";
    public const string AudioInputGet = "audio.input.get";
    public const string AudioInputSetVolume = "audio.input.set-volume";
    public const string AudioInputSetMuted = "audio.input.set-muted";
    public const string NetworkStatusGet = "network.status.get";
    public const string NetworkSavedProfilesList = "network.saved-profiles.list";
    public const string NetworkSavedProfileSwitch = "network.saved-profile.switch";
    public const string NetworkAvailableWifiGet = "network.wifi.available.get";
    public const string NetworkWifiScan = "network.wifi.scan";
    public const string NetworkAvailableWifiConnect = "network.wifi.connect";
    public const string NetworkWifiRadioGet = "network.wifi.radio.get";
    public const string NetworkWifiRadioSet = "network.wifi.radio.set";
    public const string NetworkBluetoothGet = "network.bluetooth.get";
    public const string NetworkBluetoothRadioSet = "network.bluetooth.radio.set";
    public const string RecentActivitiesList = "activity.recent.list";
    public const string RecentActivityActivate = "activity.recent.activate";
    public const string MediaSessionsGet = "media.sessions.get";
    public const string MediaSessionControl = "media.session.control";

    public const string AudioSessionsChanged = "audio.sessions.changed";
    public const string AudioOutputChanged = "audio.output.changed";
    public const string AudioDevicesChanged = "audio.devices.changed";
    public const string AudioInputChanged = "audio.input.changed";
    public const string NetworkStatusChanged = "network.status.changed";
    public const string NetworkAvailableWifiChanged = "network.wifi.available.changed";
    public const string NetworkWifiRadioChanged = "network.wifi.radio.changed";
    public const string NetworkBluetoothChanged = "network.bluetooth.changed";
    public const string RecentActivitiesChanged = "activity.recent.changed";
    public const string MediaSessionsChanged = "media.sessions.changed";

    private static readonly IReadOnlyDictionary<string, BrokerCapabilityDefinition> Definitions =
        new Dictionary<string, BrokerCapabilityDefinition>(StringComparer.Ordinal)
        {
            [AudioSessionsReadV1] = new(AudioSessionsReadV1, 1, BrokerCapabilityKind.Read,
                Set(AudioSessionsList), Set(AudioSessionsChanged)),
            [AudioSessionsControlV1] = new(AudioSessionsControlV1, 1, BrokerCapabilityKind.Control,
                Set(AudioSessionSetVolume, AudioSessionSetMuted), Set()),
            [AudioOutputReadV1] = new(AudioOutputReadV1, 1, BrokerCapabilityKind.Read,
                Set(AudioOutputGet), Set(AudioOutputChanged)),
            [AudioOutputControlV1] = new(AudioOutputControlV1, 1, BrokerCapabilityKind.Control,
                Set(AudioOutputSetVolume, AudioOutputSetMuted), Set()),
            [AudioDevicesReadV1] = new(AudioDevicesReadV1, 1, BrokerCapabilityKind.Read,
                Set(AudioDevicesList), Set(AudioDevicesChanged)),
            [AudioInputReadV1] = new(AudioInputReadV1, 1, BrokerCapabilityKind.Read,
                Set(AudioInputGet), Set(AudioInputChanged)),
            [AudioInputControlV1] = new(AudioInputControlV1, 1, BrokerCapabilityKind.Control,
                Set(AudioInputSetVolume, AudioInputSetMuted), Set()),
            [NetworkReadV1] = new(NetworkReadV1, 1, BrokerCapabilityKind.Read,
                Set(NetworkStatusGet, NetworkSavedProfilesList), Set(NetworkStatusChanged)),
            [NetworkSavedProfileSwitchV1] = new(NetworkSavedProfileSwitchV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkSavedProfileSwitch), Set()),
            [NetworkWifiReadV1] = new(NetworkWifiReadV1, 1, BrokerCapabilityKind.Read,
                Set(NetworkAvailableWifiGet, NetworkWifiScan), Set(NetworkAvailableWifiChanged)),
            [NetworkWifiConnectV1] = new(NetworkWifiConnectV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkAvailableWifiConnect), Set()),
            [NetworkWifiRadioReadV1] = new(NetworkWifiRadioReadV1, 1,
                BrokerCapabilityKind.Read, Set(NetworkWifiRadioGet), Set(NetworkWifiRadioChanged)),
            [NetworkWifiRadioControlV1] = new(NetworkWifiRadioControlV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkWifiRadioSet), Set()),
            [NetworkBluetoothReadV1] = new(NetworkBluetoothReadV1, 1,
                BrokerCapabilityKind.Read, Set(NetworkBluetoothGet), Set(NetworkBluetoothChanged)),
            [NetworkBluetoothRadioControlV1] = new(NetworkBluetoothRadioControlV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkBluetoothRadioSet), Set()),
            [RecentActivityReadV1] = new(RecentActivityReadV1, 1,
                BrokerCapabilityKind.Read, Set(RecentActivitiesList), Set(RecentActivitiesChanged)),
            [RecentActivityActivateV1] = new(RecentActivityActivateV1, 1,
                BrokerCapabilityKind.Control, Set(RecentActivityActivate), Set()),
            [MediaSessionsReadV1] = new(MediaSessionsReadV1, 1,
                BrokerCapabilityKind.Read, Set(MediaSessionsGet), Set(MediaSessionsChanged)),
            [MediaSessionsControlV1] = new(MediaSessionsControlV1, 1,
                BrokerCapabilityKind.Control, Set(MediaSessionControl), Set()),
        };

    public static IReadOnlyCollection<BrokerCapabilityDefinition> All { get; } =
        Definitions.Values.ToArray();

    public static bool TryGet(string id, out BrokerCapabilityDefinition definition) =>
        Definitions.TryGetValue(id, out definition!);

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
