using System.Text.Json.Serialization;

namespace GameBarAlternative.WidgetSdk;

public sealed record WidgetCapabilityQuery;

public sealed record WidgetCapabilityAcknowledgement(
    [property: JsonRequired] bool Acknowledged);

public sealed record WidgetAudioSession(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] double Volume,
    [property: JsonRequired] bool IsMuted,
    [property: JsonRequired] bool IsActive);

public sealed record SetWidgetAudioSessionVolumeRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] double Volume);

public sealed record SetWidgetAudioSessionMutedRequest(
    [property: JsonRequired] string SessionId,
    [property: JsonRequired] bool IsMuted);

public sealed record WidgetAudioSessionsChanged(
    [property: JsonRequired] IReadOnlyList<WidgetAudioSession> Sessions,
    [property: JsonRequired] bool IsAvailable = true);

public sealed record WidgetAudioOutput(
    [property: JsonRequired] double Volume,
    [property: JsonRequired] bool IsMuted);

public sealed record SetWidgetAudioOutputVolumeRequest(
    [property: JsonRequired] double Volume);

public sealed record SetWidgetAudioOutputMutedRequest(
    [property: JsonRequired] bool IsMuted);

public sealed record WidgetAudioOutputChanged(
    [property: JsonRequired] WidgetAudioOutput? Output,
    [property: JsonRequired] bool IsAvailable = true);

public enum WidgetNetworkConnectivity
{
    None,
    Local,
    Internet,
}

public enum WidgetNetworkTransportKind
{
    None,
    Ethernet,
    Wifi,
    Other,
}

public enum WidgetNetworkWirelessAvailability
{
    Available,
    NoAdapter,
    RadioOff,
    ServiceUnavailable,
}

public enum WidgetNetworkDetailsAccess
{
    Available,
    PrivacyRestricted,
    Unavailable,
}

public enum WidgetNetworkConnectionAttemptState
{
    None,
    Connecting,
    Failed,
}

public sealed record WidgetNetworkStatus(
    [property: JsonRequired] WidgetNetworkConnectivity Connectivity,
    [property: JsonRequired] WidgetNetworkTransportKind Transport,
    [property: JsonRequired] WidgetNetworkWirelessAvailability WirelessAvailability,
    [property: JsonRequired] WidgetNetworkDetailsAccess DetailsAccess,
    [property: JsonRequired] WidgetNetworkConnectionAttemptState ConnectionAttemptState,
    [property: JsonRequired] string? AttemptProfileId,
    [property: JsonRequired] string? ActiveProfileId,
    [property: JsonRequired] string? ActiveProfileName,
    [property: JsonRequired] int? SignalPercent);

public sealed record WidgetSavedNetworkProfile(
    [property: JsonRequired] string ProfileId,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] bool IsConnected,
    [property: JsonRequired] int? SignalPercent);

public sealed record SwitchWidgetSavedNetworkProfileRequest(
    [property: JsonRequired] string ProfileId);

public sealed record WidgetNetworkStatusChanged(
    [property: JsonRequired] WidgetNetworkStatus Status);

/// <summary>Reusable typed definitions for the audio provider.</summary>
public static class WidgetAudioCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, IReadOnlyList<WidgetAudioSession>>
        GetSessions { get; } = new("system.audio.sessions.read.v1", "audio.sessions.list");

    public static WidgetCapabilityOperation<SetWidgetAudioSessionVolumeRequest, WidgetCapabilityAcknowledgement>
        SetSessionVolume { get; } = new("system.audio.sessions.control.v1", "audio.session.set-volume");

    public static WidgetCapabilityOperation<SetWidgetAudioSessionMutedRequest, WidgetCapabilityAcknowledgement>
        SetSessionMuted { get; } = new("system.audio.sessions.control.v1", "audio.session.set-muted");

    public static WidgetCapabilityEvent<WidgetAudioSessionsChanged> SessionsChanged { get; } =
        new("system.audio.sessions.read.v1", "audio.sessions.changed");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetAudioOutput>
        GetOutput { get; } = new("system.audio.output.read.v1", "audio.output.get");

    public static WidgetCapabilityOperation<SetWidgetAudioOutputVolumeRequest, WidgetCapabilityAcknowledgement>
        SetOutputVolume { get; } = new("system.audio.output.control.v1", "audio.output.set-volume");

    public static WidgetCapabilityOperation<SetWidgetAudioOutputMutedRequest, WidgetCapabilityAcknowledgement>
        SetOutputMuted { get; } = new("system.audio.output.control.v1", "audio.output.set-muted");

    public static WidgetCapabilityEvent<WidgetAudioOutputChanged> OutputChanged { get; } =
        new("system.audio.output.read.v1", "audio.output.changed");
}

/// <summary>Reusable typed definitions for the saved-network provider.</summary>
public static class WidgetNetworkCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetNetworkStatus>
        GetStatus { get; } = new("system.network.read.v1", "network.status.get");

    public static WidgetCapabilityOperation<WidgetCapabilityQuery, IReadOnlyList<WidgetSavedNetworkProfile>>
        GetSavedProfiles { get; } = new("system.network.read.v1", "network.saved-profiles.list");

    public static WidgetCapabilityOperation<SwitchWidgetSavedNetworkProfileRequest, WidgetCapabilityAcknowledgement>
        SwitchSavedProfile { get; } =
            new("system.network.saved-profile.switch.v1", "network.saved-profile.switch");

    public static WidgetCapabilityEvent<WidgetNetworkStatusChanged> StatusChanged { get; } =
        new("system.network.read.v1", "network.status.changed");
}

public sealed class WidgetAudioService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetAudioService(IWidgetCapabilityClient client) => _client = client;

    public ValueTask<IReadOnlyList<WidgetAudioSession>> GetSessionsAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetAudioCapabilities.GetSessions, new WidgetCapabilityQuery(), cancellationToken);

    public ValueTask<WidgetAudioOutput> GetOutputAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetAudioCapabilities.GetOutput, new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask SetSessionVolumeAsync(
        string sessionId, double volume, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetSessionVolume,
            new SetWidgetAudioSessionVolumeRequest(sessionId, volume), cancellationToken)
            .ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask SetSessionMutedAsync(
        string sessionId, bool isMuted, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetSessionMuted,
            new SetWidgetAudioSessionMutedRequest(sessionId, isMuted), cancellationToken)
            .ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask SetOutputVolumeAsync(
        double volume,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between zero and one.");
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetOutputVolume,
            new SetWidgetAudioOutputVolumeRequest(volume), cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public async ValueTask SetOutputMutedAsync(
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetAudioCapabilities.SetOutputMuted,
            new SetWidgetAudioOutputMutedRequest(isMuted), cancellationToken).ConfigureAwait(false);
        DemandAcknowledged(response);
    }

    public IAsyncEnumerable<WidgetAudioSessionsChanged> WatchSessionsAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetAudioCapabilities.SessionsChanged, cancellationToken);

    /// <summary>
    /// Opens an acknowledged session-change subscription. For a race-free
    /// current-state observer, await this first, call <see cref="GetSessionsAsync"/>,
    /// then consume <see cref="IWidgetCapabilitySubscription{TPayload}.ReadAllAsync"/>.
    /// Events are full coalesced session snapshots and therefore reconcile any
    /// changes that occurred while the current snapshot was being fetched.
    /// </summary>
    public ValueTask<IWidgetCapabilitySubscription<WidgetAudioSessionsChanged>>
        OpenSessionsSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(WidgetAudioCapabilities.SessionsChanged, cancellationToken);

    public IAsyncEnumerable<WidgetAudioOutputChanged> WatchOutputAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetAudioCapabilities.OutputChanged, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetAudioOutputChanged>>
        OpenOutputSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(WidgetAudioCapabilities.OutputChanged, cancellationToken);

    private static void DemandAcknowledged(WidgetCapabilityAcknowledgement response)
    {
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response", "The audio provider returned an invalid acknowledgement.");
    }
}

public sealed class WidgetNetworkService
{
    private readonly IWidgetCapabilityClient _client;
    internal WidgetNetworkService(IWidgetCapabilityClient client) => _client = client;

    public ValueTask<WidgetNetworkStatus> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetNetworkCapabilities.GetStatus, new WidgetCapabilityQuery(), cancellationToken);

    public ValueTask<IReadOnlyList<WidgetSavedNetworkProfile>> GetSavedProfilesAsync(
        CancellationToken cancellationToken = default) =>
        _client.InvokeAsync(WidgetNetworkCapabilities.GetSavedProfiles, new WidgetCapabilityQuery(), cancellationToken);

    public async ValueTask SwitchSavedProfileAsync(
        string profileId, CancellationToken cancellationToken = default)
    {
        var response = await _client.InvokeAsync(
            WidgetNetworkCapabilities.SwitchSavedProfile,
            new SwitchWidgetSavedNetworkProfileRequest(profileId), cancellationToken)
            .ConfigureAwait(false);
        if (response is null || !response.Acknowledged)
            throw new WidgetCapabilityException(
                "malformed_response", "The network provider returned an invalid acknowledgement.");
    }

    public IAsyncEnumerable<WidgetNetworkStatusChanged> WatchStatusAsync(
        CancellationToken cancellationToken = default) =>
        _client.SubscribeAsync(WidgetNetworkCapabilities.StatusChanged, cancellationToken);

    public ValueTask<IWidgetCapabilitySubscription<WidgetNetworkStatusChanged>>
        OpenStatusSubscriptionAsync(CancellationToken cancellationToken = default) =>
        _client.OpenSubscriptionAsync(WidgetNetworkCapabilities.StatusChanged, cancellationToken);
}
