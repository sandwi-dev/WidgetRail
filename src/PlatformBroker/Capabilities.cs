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
    public const string NetworkReadV1 = "system.network.read.v1";
    public const string NetworkSavedProfileSwitchV1 = "system.network.saved-profile.switch.v1";

    public const string AudioSessionsList = "audio.sessions.list";
    public const string AudioSessionSetVolume = "audio.session.set-volume";
    public const string AudioSessionSetMuted = "audio.session.set-muted";
    public const string AudioOutputGet = "audio.output.get";
    public const string AudioOutputSetVolume = "audio.output.set-volume";
    public const string AudioOutputSetMuted = "audio.output.set-muted";
    public const string NetworkStatusGet = "network.status.get";
    public const string NetworkSavedProfilesList = "network.saved-profiles.list";
    public const string NetworkSavedProfileSwitch = "network.saved-profile.switch";

    public const string AudioSessionsChanged = "audio.sessions.changed";
    public const string AudioOutputChanged = "audio.output.changed";
    public const string NetworkStatusChanged = "network.status.changed";

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
            [NetworkReadV1] = new(NetworkReadV1, 1, BrokerCapabilityKind.Read,
                Set(NetworkStatusGet, NetworkSavedProfilesList), Set(NetworkStatusChanged)),
            [NetworkSavedProfileSwitchV1] = new(NetworkSavedProfileSwitchV1, 1,
                BrokerCapabilityKind.Control, Set(NetworkSavedProfileSwitch), Set()),
        };

    public static IReadOnlyCollection<BrokerCapabilityDefinition> All { get; } =
        Definitions.Values.ToArray();

    public static bool TryGet(string id, out BrokerCapabilityDefinition definition) =>
        Definitions.TryGetValue(id, out definition!);

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
