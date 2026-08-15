using System.Text.Json;

namespace GameBarAlternative.PlatformBroker;

internal sealed class MediaCapabilityDomain(IPlatformBrokerBackend backend)
{
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
            default:
                throw new BrokerException(
                    "unsupported_operation", "Media operation is unsupported.");
        }
    }

    internal static JsonElement ProjectEvent(string eventType, object payload) =>
        eventType switch
        {
            PlatformCapabilities.MediaSessionsChanged when
                payload is MediaSessionsChangedEvent change =>
                BrokerJson.ToElement(new MediaSessionsChangedEvent(
                    ValidateMediaSessions(change.Sessions))),
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
}
