using System.Text.Json;

namespace GameBarAlternative.PlatformBroker;

internal sealed class MediaSpotifyCapabilityDomain(
    IPlatformBrokerBackend backend,
    BrokerWidgetIdentity identity,
    Func<IReadOnlyList<SpotifyAuthorizationScope>, CancellationToken, Task>
        authorizeScopes)
{
    private const string SpotifyRedirectUri = "http://127.0.0.1:43827/callback/";
    private const int MaximumSpotifyClientIdCharacters = 128;
    private const int MaximumSpotifyMessageCharacters = 320;
    private const int MaximumSpotifyUriCharacters = 512;
    private const int MaximumSpotifyArtworkUrlCharacters = 2_048;

    internal async Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case PlatformCapabilities.MediaSessionsGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateMediaSessions(
                    await backend.GetMediaSessionsAsync(cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.MediaSessionControl:
            {
                var request = BrokerJson.ParsePayload<ControlMediaSessionRequest>(payload);
                ContractValidation.OpaqueId(request.SessionId);
                if (!Enum.IsDefined(request.Command))
                    throw new BrokerException(
                        "invalid_payload", "Media session command is invalid.");
                await backend.ControlMediaSessionAsync(
                    request.SessionId, request.Command, cancellationToken)
                    .ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.SpotifyConfigurationGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateSpotifyConfiguration(
                    await backend.GetSpotifyConfigurationAsync(identity, cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.SpotifyConfigurationConfigure:
            {
                var request = BrokerJson.ParsePayload<ConfigureSpotifyClientRequest>(payload);
                ValidateSpotifyClientId(request.ClientId);
                return BrokerJson.ToElement(ValidateSpotifyConfiguration(
                    await backend.ConfigureSpotifyClientAsync(
                        identity, request, cancellationToken).ConfigureAwait(false)));
            }
            case PlatformCapabilities.SpotifyAuthorizationGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateSpotifyAuthorization(
                    await backend.GetSpotifyAuthorizationAsync(identity, cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.SpotifyAuthorizationConnect:
            {
                var request = BrokerJson.ParsePayload<ConnectSpotifyRequest>(payload);
                var scopes = ValidateSpotifyScopes(
                    request.RequestedScopes, "invalid_payload");
                if (scopes.Count == 0)
                    throw new BrokerException(
                        "invalid_payload", "At least one Spotify scope is required.");
                await authorizeScopes(scopes, cancellationToken).ConfigureAwait(false);
                return BrokerJson.ToElement(ValidateSpotifyAuthorization(
                    await backend.ConnectSpotifyAsync(
                        identity,
                        request with { RequestedScopes = scopes },
                        cancellationToken).ConfigureAwait(false)));
            }
            case PlatformCapabilities.SpotifyAuthorizationDisconnect:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateSpotifyAuthorization(
                    await backend.DisconnectSpotifyAsync(identity, cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.SpotifyPlaybackGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateSpotifyPlayback(
                    await backend.GetSpotifyPlaybackAsync(identity, cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.SpotifyPlaybackControl:
            {
                var command = BrokerJson.ParsePayload<SpotifyPlaybackCommand>(payload);
                ValidateSpotifyPlaybackCommand(command);
                await backend.ControlSpotifyPlaybackAsync(
                    identity, command, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.SpotifyPlaybackDevicesGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateSpotifyDevices(
                    await backend.GetSpotifyDevicesAsync(identity, cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.SpotifyPlaybackTransfer:
            {
                var request = BrokerJson.ParsePayload<TransferSpotifyPlaybackRequest>(payload);
                ValidateSpotifyIdentifier(request.DeviceId, "Spotify device identifier");
                await backend.TransferSpotifyPlaybackAsync(
                    identity, request, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.SpotifyPlaybackQueueGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateSpotifyQueue(
                    await backend.GetSpotifyQueueAsync(identity, cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.SpotifyPlaybackQueueAdd:
            {
                var request = BrokerJson.ParsePayload<AddSpotifyQueueItemRequest>(payload);
                ValidateSpotifyUri(request.Uri);
                if (request.DeviceId is not null)
                    ValidateSpotifyIdentifier(
                        request.DeviceId, "Spotify device identifier");
                await backend.AddSpotifyQueueItemAsync(
                    identity, request, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.SpotifyPlaybackStart:
            {
                var request = BrokerJson.ParsePayload<StartSpotifyPlaybackRequest>(payload);
                ValidateSpotifyStartPlayback(request);
                await backend.StartSpotifyPlaybackAsync(
                    identity, request, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.SpotifyLocalPlaybackGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateSpotifyLocalPlayback(
                    await backend.GetSpotifyLocalPlaybackAsync(identity, cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.SpotifyLocalPlaybackControl:
            {
                var command = BrokerJson.ParsePayload<SpotifyLocalPlaybackCommand>(payload);
                ValidateSpotifyLocalPlaybackCommand(command);
                return BrokerJson.ToElement(ValidateSpotifyLocalPlayback(
                    await backend.ControlSpotifyLocalPlaybackAsync(
                        identity, command, cancellationToken).ConfigureAwait(false)));
            }
            case PlatformCapabilities.SpotifyPlaylistsGet:
            {
                var request = BrokerJson.ParsePayload<SpotifyPlaylistPageRequest>(payload);
                ValidateSpotifyPage(request.Offset, request.Limit);
                return BrokerJson.ToElement(ValidateSpotifyPlaylistPage(
                    await backend.GetSpotifyPlaylistsAsync(
                        identity, request, cancellationToken).ConfigureAwait(false)));
            }
            case PlatformCapabilities.SpotifyPlaylistItemsGet:
            {
                var request = BrokerJson.ParsePayload<SpotifyPlaylistItemsRequest>(payload);
                ValidateSpotifyIdentifier(
                    request.PlaylistId, "Spotify playlist identifier");
                ValidateSpotifyPage(request.Offset, request.Limit);
                return BrokerJson.ToElement(ValidateSpotifyPlaylistItems(
                    await backend.GetSpotifyPlaylistItemsAsync(
                        identity, request, cancellationToken).ConfigureAwait(false)));
            }
            default:
                throw new BrokerException(
                    "unsupported_operation", "Media or Spotify operation is unsupported.");
        }
    }

    internal static JsonElement ProjectEvent(string eventType, object payload) =>
        eventType switch
        {
            PlatformCapabilities.MediaSessionsChanged when
                payload is MediaSessionsChangedEvent change =>
                BrokerJson.ToElement(new MediaSessionsChangedEvent(
                    ValidateMediaSessions(change.Sessions))),
            PlatformCapabilities.SpotifyPlaybackChanged when
                payload is SpotifyPlaybackChangedEvent change =>
                BrokerJson.ToElement(new SpotifyPlaybackChangedEvent(
                    ValidateSpotifyPlayback(change.Playback))),
            _ => throw new BrokerException(
                "invalid_backend_data", "Media event payload is invalid."),
        };

    internal static IReadOnlyList<MediaSessionSummary> ValidateMediaSessions(
        IReadOnlyList<MediaSessionSummary>? sessions)
    {
        if (sessions is null || sessions.Count > 32)
            throw new BrokerException(
                "invalid_backend_data", "Media session result is invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var currentCount = 0;
        var artworkBytes = 0;
        var validated = new List<MediaSessionSummary>(sessions.Count);
        foreach (var session in sessions)
        {
            if (session is null || !Enum.IsDefined(session.PlaybackStatus) ||
                !double.IsFinite(session.PlaybackRate) ||
                session.PlaybackRate is < 0 or > 16 ||
                session.PositionMilliseconds < 0 || session.DurationMilliseconds < 0 ||
                session.PositionMilliseconds > session.DurationMilliseconds ||
                session.DurationMilliseconds > TimeSpan.FromDays(7).TotalMilliseconds ||
                session.CapturedAtUnixMilliseconds < 0 ||
                session.IsCurrent && ++currentCount > 1)
                throw new BrokerException(
                    "invalid_backend_data", "Media session entry is inconsistent.");
            ContractValidation.OpaqueId(session.SessionId, "invalid_backend_data");
            ContractValidation.DisplayName(session.AppName);
            ContractValidation.DisplayName(session.Title);
            ContractValidation.DisplayName(session.Artist);
            if (!ids.Add(session.SessionId))
                throw new BrokerException(
                    "invalid_backend_data", "Media session IDs are duplicated.");
            var artwork = BrokerInlinePngPolicy.Validate(
                session.ArtworkPngBase64,
                MediaSessionImageLimits.MaximumPngBytes,
                MediaSessionImageLimits.MaximumPixelDimension);
            if (artwork is not null &&
                artworkBytes + artwork.Value.Bytes <=
                    MediaSessionImageLimits.MaximumSnapshotPngBytes)
            {
                artworkBytes += artwork.Value.Bytes;
                validated.Add(session with { ArtworkPngBase64 = artwork.Value.Base64 });
            }
            else
            {
                validated.Add(session with { ArtworkPngBase64 = null });
            }
        }
        return validated;
    }

    internal static SpotifyAuthorizationSummary ValidateSpotifyAuthorization(
        SpotifyAuthorizationSummary? authorization)
    {
        if (authorization is null || !Enum.IsDefined(authorization.State))
            throw new BrokerException(
                "invalid_backend_data", "Spotify authorization state is invalid.");
        var requested = ValidateSpotifyScopes(
            authorization.RequestedScopes, "invalid_backend_data");
        var granted = ValidateSpotifyScopes(
            authorization.GrantedScopes, "invalid_backend_data");
        if (granted.Except(requested).Any() ||
            authorization.State == SpotifyAuthorizationState.Unconfigured &&
                (requested.Count != 0 || granted.Count != 0) ||
            authorization.State == SpotifyAuthorizationState.Authorizing &&
                requested.Count == 0 ||
            authorization.State == SpotifyAuthorizationState.Connected &&
                (requested.Count == 0 || granted.Count == 0))
            throw new BrokerException(
                "invalid_backend_data", "Spotify authorization scopes are inconsistent.");
        if (authorization.DisplayMessage is { } message &&
            (string.IsNullOrWhiteSpace(message) ||
             message.Length > MaximumSpotifyMessageCharacters ||
             message.Any(char.IsControl)))
            throw new BrokerException(
                "invalid_backend_data", "Spotify authorization message is invalid.");
        return authorization with
        {
            RequestedScopes = requested,
            GrantedScopes = granted,
        };
    }

    internal static SpotifyPlaybackSummary ValidateSpotifyPlayback(
        SpotifyPlaybackSummary? playback)
    {
        if (playback is null || playback.DisallowedActions is null ||
            !Enum.IsDefined(playback.RepeatState) ||
            playback.ProgressMilliseconds < 0 || playback.DurationMilliseconds < 0 ||
            playback.ProgressMilliseconds > playback.DurationMilliseconds ||
            playback.DurationMilliseconds > TimeSpan.FromDays(7).TotalMilliseconds ||
            playback.CapturedAtUnixMilliseconds < 0 ||
            !string.Equals(playback.Attribution, "Spotify", StringComparison.Ordinal) ||
            playback.IsAvailable != (playback.Item is not null) ||
            !playback.IsAvailable &&
                (playback.IsPlaying || playback.ProgressMilliseconds != 0 ||
                 playback.DurationMilliseconds != 0))
            throw new BrokerException(
                "invalid_backend_data", "Spotify playback is invalid.");
        if (playback.Item is { } item)
        {
            if (!Enum.IsDefined(item.ItemType))
                throw new BrokerException(
                    "invalid_backend_data", "Spotify playback item type is invalid.");
            ContractValidation.DisplayName(item.Title);
            ContractValidation.DisplayName(item.Subtitle);
            if (item.ContextName is { } context)
                ContractValidation.DisplayName(context);
            if (item.Uri is { } uri &&
                (string.IsNullOrWhiteSpace(uri) ||
                 uri.Length > MaximumSpotifyUriCharacters ||
                 uri.Any(char.IsControl) ||
                 !uri.StartsWith("spotify:", StringComparison.Ordinal)))
                throw new BrokerException(
                    "invalid_backend_data", "Spotify item URI is invalid.");
            if (item.ArtworkUrl is { } artwork &&
                (artwork.Length > MaximumSpotifyArtworkUrlCharacters ||
                 !Uri.TryCreate(artwork, UriKind.Absolute, out var parsed) ||
                 parsed.Scheme != Uri.UriSchemeHttps ||
                 !string.IsNullOrEmpty(parsed.UserInfo)))
                throw new BrokerException(
                    "invalid_backend_data", "Spotify artwork URL is invalid.");
        }
        return playback;
    }

    private static void ValidateSpotifyClientId(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId) ||
            clientId.Length > MaximumSpotifyClientIdCharacters ||
            clientId.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new BrokerException("invalid_payload", "Spotify client ID is invalid.");
    }

    private static SpotifyConfigurationSummary ValidateSpotifyConfiguration(
        SpotifyConfigurationSummary? configuration)
    {
        if (configuration is null ||
            !string.Equals(
                configuration.RedirectUri, SpotifyRedirectUri, StringComparison.Ordinal))
            throw new BrokerException(
                "invalid_backend_data", "Spotify configuration is invalid.");
        return configuration;
    }

    private static IReadOnlyList<SpotifyAuthorizationScope> ValidateSpotifyScopes(
        IReadOnlyList<SpotifyAuthorizationScope>? scopes,
        string errorCode)
    {
        if (scopes is null || scopes.Count > 8 ||
            scopes.Any(scope => !Enum.IsDefined(scope)) ||
            scopes.Distinct().Count() != scopes.Count)
            throw new BrokerException(
                errorCode, "Spotify authorization scopes are invalid.");
        return scopes.OrderBy(scope => scope).ToArray();
    }

    private static void ValidateSpotifyPlaybackCommand(SpotifyPlaybackCommand command)
    {
        if (!Enum.IsDefined(command.Operation))
            throw new BrokerException(
                "invalid_payload", "Spotify playback operation is invalid.");
        var valid = command.Operation switch
        {
            SpotifyPlaybackOperation.Play or SpotifyPlaybackOperation.Pause or
                SpotifyPlaybackOperation.Next or SpotifyPlaybackOperation.Previous =>
                command.PositionMilliseconds is null && command.RepeatState is null &&
                command.Enabled is null,
            SpotifyPlaybackOperation.Seek =>
                command.PositionMilliseconds is >= 0 and <= 604_800_000 &&
                command.RepeatState is null && command.Enabled is null,
            SpotifyPlaybackOperation.SetRepeat =>
                command.PositionMilliseconds is null &&
                command.RepeatState is { } repeat && Enum.IsDefined(repeat) &&
                command.Enabled is null,
            SpotifyPlaybackOperation.SetShuffle =>
                command.PositionMilliseconds is null && command.RepeatState is null &&
                command.Enabled is not null,
            _ => false,
        };
        if (!valid)
            throw new BrokerException(
                "invalid_payload", "Spotify playback command is invalid.");
    }

    private static SpotifyDevicesSummary ValidateSpotifyDevices(
        SpotifyDevicesSummary? summary)
    {
        if (summary?.Devices is null || summary.Devices.Count > 64)
            throw new BrokerException(
                "invalid_backend_data", "Spotify devices are invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in summary.Devices)
        {
            if (device is null)
                throw new BrokerException(
                    "invalid_backend_data", "Spotify device is invalid.");
            ValidateSpotifyIdentifier(
                device.DeviceId, "Spotify device identifier", "invalid_backend_data");
            ContractValidation.DisplayName(device.Name);
            ContractValidation.DisplayName(device.Type);
            if (!ids.Add(device.DeviceId) || device.VolumePercent is < 0 or > 100 ||
                device.IsRestricted && device.IsLocalHost)
                throw new BrokerException(
                    "invalid_backend_data", "Spotify device is invalid.");
        }
        return new SpotifyDevicesSummary(summary.Devices.ToArray());
    }

    private static SpotifyQueueSummary ValidateSpotifyQueue(SpotifyQueueSummary? summary)
    {
        if (summary?.Items is null || summary.Items.Count > 100)
            throw new BrokerException("invalid_backend_data", "Spotify queue is invalid.");
        if (summary.CurrentlyPlaying is not null)
            ValidateSpotifyMediaItem(summary.CurrentlyPlaying);
        foreach (var item in summary.Items) ValidateSpotifyMediaItem(item);
        return new SpotifyQueueSummary(
            summary.CurrentlyPlaying, summary.Items.ToArray(), summary.IsTruncated);
    }

    private static SpotifyLocalPlaybackSummary ValidateSpotifyLocalPlayback(
        SpotifyLocalPlaybackSummary? summary)
    {
        if (summary is null || !Enum.IsDefined(summary.State) ||
            summary.VolumePercent is < 0 or > 100)
            throw new BrokerException(
                "invalid_backend_data", "Spotify local playback state is invalid.");
        ContractValidation.DisplayName(summary.DeviceName);
        if (summary.DisplayMessage is { } message)
            ContractValidation.DisplayName(message);
        return summary;
    }

    private static void ValidateSpotifyLocalPlaybackCommand(
        SpotifyLocalPlaybackCommand command)
    {
        if (!Enum.IsDefined(command.Operation))
            throw new BrokerException(
                "invalid_payload", "Spotify local-playback operation is invalid.");
        var valid = command.Operation switch
        {
            SpotifyLocalPlaybackOperation.StartAndTransfer =>
                command.VolumePercent is null && command.ContinuePlaying is not null,
            SpotifyLocalPlaybackOperation.Stop =>
                command.VolumePercent is null && command.ContinuePlaying is null,
            SpotifyLocalPlaybackOperation.SetVolume =>
                command.VolumePercent is >= 0 and <= 100 &&
                command.ContinuePlaying is null,
            _ => false,
        };
        if (!valid)
            throw new BrokerException(
                "invalid_payload", "Spotify local-playback command is invalid.");
    }

    private static SpotifyPlaylistPageSummary ValidateSpotifyPlaylistPage(
        SpotifyPlaylistPageSummary? page)
    {
        if (page?.Items is null || page.Offset < 0 || page.Limit is < 1 or > 50 ||
            page.Total < 0 || page.Items.Count > page.Limit ||
            page.Items.Count != 0 && (long)page.Offset + page.Items.Count > page.Total)
            throw new BrokerException(
                "invalid_backend_data", "Spotify playlist page is invalid.");
        foreach (var playlist in page.Items) ValidateSpotifyPlaylist(playlist);
        return page with { Items = page.Items.ToArray() };
    }

    private static SpotifyPlaylistItemsSummary ValidateSpotifyPlaylistItems(
        SpotifyPlaylistItemsSummary? page)
    {
        if (page?.Items is null || page.Playlist is null || page.Offset < 0 ||
            page.Limit is < 1 or > 50 || page.Total < 0 ||
            page.Items.Count > page.Limit ||
            page.Items.Count != 0 && (long)page.Offset + page.Items.Count > page.Total)
            throw new BrokerException(
                "invalid_backend_data", "Spotify playlist items are invalid.");
        ValidateSpotifyPlaylist(page.Playlist);
        foreach (var item in page.Items) ValidateSpotifyMediaItem(item);
        return page with { Items = page.Items.ToArray() };
    }

    private static void ValidateSpotifyPlaylist(SpotifyPlaylistSummary playlist)
    {
        if (playlist is null || playlist.ItemCount < 0)
            throw new BrokerException(
                "invalid_backend_data", "Spotify playlist is invalid.");
        ValidateSpotifyIdentifier(
            playlist.PlaylistId, "Spotify playlist identifier", "invalid_backend_data");
        ContractValidation.DisplayName(playlist.Name);
        ContractValidation.DisplayName(playlist.OwnerName);
        if (playlist.Description is { } description)
            ContractValidation.DisplayName(description);
        ValidateSpotifyUri(playlist.Uri, "invalid_backend_data");
        ValidateSpotifyUrl(playlist.SpotifyUrl, "Spotify URL");
        if (playlist.ArtworkUrl is { } artwork) ValidateSpotifyArtwork(artwork);
    }

    private static void ValidateSpotifyMediaItem(SpotifyMediaItemSummary item)
    {
        if (item is null || !Enum.IsDefined(item.ItemType) ||
            item.DurationMilliseconds is < 0 or > 604_800_000)
            throw new BrokerException(
                "invalid_backend_data", "Spotify media item is invalid.");
        ContractValidation.DisplayName(item.Title);
        ContractValidation.DisplayName(item.Subtitle);
        ValidateSpotifyUri(item.Uri, "invalid_backend_data");
        ValidateSpotifyUrl(item.SpotifyUrl, "Spotify URL");
        if (item.ArtworkUrl is { } artwork) ValidateSpotifyArtwork(artwork);
    }

    private static void ValidateSpotifyStartPlayback(StartSpotifyPlaybackRequest request)
    {
        if ((request.ContextUri is null) == (request.ItemUris is null) ||
            request.ItemUris is { Count: < 1 or > 50 } || request.Offset is < 0 ||
            request.Offset is not null && request.ContextUri is null ||
            request.OffsetUri is not null && request.ContextUri is null ||
            request.Offset is not null && request.OffsetUri is not null)
            throw new BrokerException(
                "invalid_payload", "Spotify playback selection is invalid.");
        if (request.ContextUri is not null) ValidateSpotifyUri(request.ContextUri);
        if (request.OffsetUri is not null) ValidateSpotifyUri(request.OffsetUri);
        if (request.ItemUris is not null)
            foreach (var uri in request.ItemUris) ValidateSpotifyUri(uri);
        if (request.DeviceId is not null)
            ValidateSpotifyIdentifier(request.DeviceId, "Spotify device identifier");
    }

    private static void ValidateSpotifyPage(int offset, int limit)
    {
        if (offset is < 0 or > 100_000 || limit is < 1 or > 50)
            throw new BrokerException(
                "invalid_payload", "Spotify page request is invalid.");
    }

    private static void ValidateSpotifyIdentifier(
        string value,
        string label,
        string errorCode = "invalid_payload")
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 ||
            value.Any(character => character is < '!' or > '~'))
            throw new BrokerException(errorCode, $"{label} is invalid.");
    }

    private static void ValidateSpotifyUri(
        string value,
        string errorCode = "invalid_payload")
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumSpotifyUriCharacters ||
            value.Any(char.IsControl) ||
            !value.StartsWith("spotify:", StringComparison.Ordinal) ||
            value.Count(character => character == ':') < 2)
            throw new BrokerException(errorCode, "Spotify URI is invalid.");
    }

    private static void ValidateSpotifyUrl(string value, string label)
    {
        if (value.Length > MaximumSpotifyArtworkUrlCharacters ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps ||
            !parsed.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase) ||
            !parsed.IsDefaultPort || !string.IsNullOrEmpty(parsed.UserInfo))
            throw new BrokerException("invalid_backend_data", $"{label} is invalid.");
    }

    private static void ValidateSpotifyArtwork(string value)
    {
        if (value.Length > MaximumSpotifyArtworkUrlCharacters ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || !parsed.IsDefaultPort ||
            !string.IsNullOrEmpty(parsed.UserInfo))
            throw new BrokerException(
                "invalid_backend_data", "Spotify artwork URL is invalid.");
    }
}
