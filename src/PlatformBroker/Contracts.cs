using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace GameBarAlternative.PlatformBroker;

public sealed record BrokerWidgetIdentity(
    [property: JsonRequired] string PackageId,
    [property: JsonRequired] string PublisherId,
    [property: JsonRequired] string InstanceId)
{
    public void Validate()
    {
        if (string.IsNullOrEmpty(PackageId) || string.IsNullOrEmpty(PublisherId) ||
            string.IsNullOrEmpty(InstanceId) ||
            !ContractValidation.PackageId().IsMatch(PackageId) ||
            !ContractValidation.PackageId().IsMatch(PublisherId) ||
            !ContractValidation.InstanceId().IsMatch(InstanceId))
            throw new BrokerException("invalid_identity", "Widget identity is invalid.");
    }
}

public enum BrokerLifecycleState
{
    Background,
    Visible,
    Interactive,
    Destroying,
}

public enum ConsentDecision
{
    Grant,
    Deny,
}

public sealed record AudioSessionSummary(
    string SessionId,
    string DisplayName,
    double Volume,
    bool IsMuted,
    bool IsActive);

public sealed record SetAudioSessionVolumeRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] double Volume);
public sealed record SetAudioSessionMutedRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] bool IsMuted);

public enum NetworkConnectivity
{
    None,
    Local,
    Internet,
}

public enum NetworkTransportKind
{
    None,
    Ethernet,
    Wifi,
    Other,
}

public enum NetworkWirelessAvailability
{
    Available,
    NoAdapter,
    RadioOff,
    ServiceUnavailable,
}

public enum NetworkDetailsAccess
{
    Available,
    PrivacyRestricted,
    Unavailable,
}

public enum NetworkConnectionAttemptState
{
    None,
    Connecting,
    Failed,
}

public sealed record NetworkStatusSummary(
    NetworkConnectivity Connectivity,
    NetworkTransportKind Transport,
    NetworkWirelessAvailability WirelessAvailability,
    NetworkDetailsAccess DetailsAccess,
    NetworkConnectionAttemptState ConnectionAttemptState,
    string? AttemptProfileId,
    string? ActiveProfileId,
    string? ActiveProfileName,
    int? SignalPercent);

public sealed record SavedNetworkProfileSummary(
    string ProfileId,
    string DisplayName,
    bool IsConnected,
    int? SignalPercent);

public sealed record SwitchSavedNetworkProfileRequest(
    [property: JsonRequired] string ProfileId);

public sealed record AudioSessionsChangedEvent(IReadOnlyList<AudioSessionSummary> Sessions);
public sealed record NetworkStatusChangedEvent(NetworkStatusSummary Status);

public sealed record BrokerPlatformEvent(string CapabilityId, string EventType, object Payload);

public interface IPlatformBrokerEventSource
{
    event EventHandler<BrokerPlatformEvent>? EventPublished;
}

public interface IAudioPlatformBrokerBackend : IPlatformBrokerEventSource
{

    Task<IReadOnlyList<AudioSessionSummary>> GetAudioSessionsAsync(CancellationToken cancellationToken);
    Task SetAudioSessionVolumeAsync(string sessionId, double volume, CancellationToken cancellationToken);
    Task SetAudioSessionMutedAsync(string sessionId, bool isMuted, CancellationToken cancellationToken);
}

public interface INetworkPlatformBrokerBackend : IPlatformBrokerEventSource
{
    Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(CancellationToken cancellationToken);
    Task SwitchSavedNetworkProfileAsync(string profileId, CancellationToken cancellationToken);
}

/// <summary>
/// Complete host backend. Providers can implement the narrower audio or network
/// contracts and be joined with <see cref="CompositePlatformBrokerBackend"/>.
/// </summary>
public interface IPlatformBrokerBackend : IAudioPlatformBrokerBackend, INetworkPlatformBrokerBackend
{
}

public sealed class BrokerException : Exception
{
    public BrokerException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

internal static partial class ContractValidation
{
    internal const int MaximumDisplayNameLength = 160;
    internal const int MaximumOpaqueIdLength = 128;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$",
        RegexOptions.CultureInvariant)]
    internal static partial Regex PackageId();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    internal static partial Regex InstanceId();

    internal static void OpaqueId(string value, string code = "invalid_payload")
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumOpaqueIdLength ||
            !InstanceId().IsMatch(value))
            throw new BrokerException(code, "An opaque platform identifier is invalid.");
    }

    internal static void DisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumDisplayNameLength ||
            value.Any(char.IsControl))
            throw new BrokerException("invalid_backend_data", "Platform display data is invalid.");
    }

    internal static void Percent(int? value)
    {
        if (value is < 0 or > 100)
            throw new BrokerException("invalid_backend_data", "Platform percentage is invalid.");
    }
}
