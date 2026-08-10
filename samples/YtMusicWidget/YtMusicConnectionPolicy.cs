using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.YtMusicWidget;

internal static class YtMusicConnectionPolicy
{
    internal static bool ShouldAutoConnect(YtMusicPresentationState presentation) =>
        presentation.ConnectionState == YtMusicWidgetConnectionState.Disconnected;

    internal static YtMusicPresentationState Deactivate(
        YtMusicPresentationState presentation) =>
        presentation.ConnectionState is
            YtMusicWidgetConnectionState.Connecting or
            YtMusicWidgetConnectionState.Pairing
            ? presentation with
            {
                ConnectionState = YtMusicWidgetConnectionState.Disconnected,
                Status = "Connection paused · reconnect when visible",
                PairingCode = null,
            }
            : presentation;

    internal static YtMusicPresentationState AuthorizationRequired(
        YtMusicPresentationState presentation) => presentation with
        {
            PendingOptimistic = [],
            ConnectionState = YtMusicWidgetConnectionState.Disconnected,
            Status = "Authorization expired · pair device",
            PairingCode = null,
        };

    internal static string ConnectedStatus(YtMusicPlaybackSnapshot snapshot) =>
        snapshot.IsPlaying
            ? "Playing through YTMDesktop2"
            : "Connected to YTMDesktop2";

    internal static string SafeStatus(Exception exception)
    {
        var message = exception switch
        {
            WidgetCapabilityUnavailableException =>
                "Overlay services are unavailable · reload the widget",
            WidgetCapabilityException capability => capability.ErrorCode switch
            {
                "permission_denied" or "capability_revoked" =>
                    "Local API access is blocked · review YT Music permissions",
                "loopback_timeout" =>
                    "YTMDesktop2 did not respond · try again",
                "loopback_unavailable" =>
                    "YTMDesktop2 is not running · start it and retry",
                "lifecycle_denied" =>
                    "YT Music control paused because the widget is no longer active",
                "capability_not_declared" or "invalid_declaration" or
                    "unsupported_capability" =>
                    "This addon needs a compatible overlay version",
                "response_too_large" or "invalid_response" or
                    "invalid_backend_data" or "malformed_response" =>
                    "YTMDesktop2 returned an invalid response · try again",
                "platform_unavailable" =>
                    "Local companion access is unavailable · try again",
                _ => "YT Music request failed · try again",
            },
            YtMusicServiceException service => service.StatusCode switch
            {
                404 => "YTMDesktop2 API is unavailable · update or restart YTMDesktop2",
                408 or 504 => "YTMDesktop2 did not respond · try again",
                429 => "YTMDesktop2 is busy · try again shortly",
                >= 500 => "YTMDesktop2 reported an error · try again",
                _ => "YTMDesktop2 rejected the request · try again",
            },
            _ when exception.Message.Contains(
                "HTTP 401", StringComparison.OrdinalIgnoreCase) =>
                "Authorization required · select Pair device",
            _ => "YT Music request failed · try again",
        };
        return message.Length > 500 ? message[..500] : message;
    }
}
