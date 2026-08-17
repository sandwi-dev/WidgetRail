namespace WidgetRail.Samples.SpotifyWidget;

internal enum SpotifyRefreshFailureDisposition
{
    Transient,
    PermissionDenied,
    AuthorizationRequired,
    ConfigurationError,
}

internal sealed record SpotifyRefreshFailure(
    SpotifyRefreshFailureDisposition Disposition,
    SpotifyWidgetViewState FallbackState,
    string DiagnosticCode,
    string Status);

internal sealed record SpotifyRefreshWarning(
    string DiagnosticCode,
    string Status,
    string Detail,
    int ConsecutiveFailures,
    TimeSpan RetryDelay);

internal static class SpotifyRefreshFailurePolicy
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    ];

    internal const int MaximumTrackedFailures = 3;

    internal static SpotifyRefreshFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is not SpotifyApplicationException applicationError)
            return TransientFailure();

        return applicationError.Code switch
        {
            "forbidden" => new(
                SpotifyRefreshFailureDisposition.PermissionDenied,
                SpotifyWidgetViewState.PermissionDenied,
                "spotify_refresh_permission_denied",
                "Spotify permission is off"),
            "authorization_expired" or "authorization_scope_required" => new(
                SpotifyRefreshFailureDisposition.AuthorizationRequired,
                SpotifyWidgetViewState.Disconnected,
                "spotify_refresh_authorization_required",
                "Spotify needs you to reconnect"),
            "invalid_configuration" or "invalid_payload" => new(
                    SpotifyRefreshFailureDisposition.ConfigurationError,
                    SpotifyWidgetViewState.Error,
                    "spotify_refresh_incompatible",
                    "Spotify needs a compatible overlay configuration"),
            "spotify_unavailable" or "platform_unavailable" or "rate_limited" or
                "loopback_timeout" => TransientUnavailable(),
            "response_too_large" or "invalid_response" or "malformed_response" or
                "protocol_violation" =>
                    TransientInvalidResponse(),
            _ => TransientFailure(),
        };
    }

    internal static SpotifyRefreshWarning CreateWarning(
        SpotifyRefreshFailure failure,
        int consecutiveFailures)
    {
        if (failure.Disposition != SpotifyRefreshFailureDisposition.Transient)
            throw new ArgumentException("Only transient failures produce a warning.",
                nameof(failure));
        var boundedFailures = Math.Clamp(
            consecutiveFailures, 1, MaximumTrackedFailures);
        var retryDelay = RetryDelays[boundedFailures - 1];
        var status = boundedFailures == MaximumTrackedFailures
            ? "Spotify updates still delayed · press Y to retry"
            : failure.Status;
        return new(
            failure.DiagnosticCode,
            status,
            $"Current playback and navigation are retained. Automatic retry in up to " +
                $"{retryDelay.TotalSeconds:0} seconds.",
            boundedFailures,
            retryDelay);
    }

    private static SpotifyRefreshFailure TransientUnavailable() => new(
        SpotifyRefreshFailureDisposition.Transient,
        SpotifyWidgetViewState.ServiceUnavailable,
        "spotify_refresh_provider_unavailable",
        "Spotify updates unavailable · press Y to retry");

    private static SpotifyRefreshFailure TransientInvalidResponse() => new(
        SpotifyRefreshFailureDisposition.Transient,
        SpotifyWidgetViewState.Error,
        "spotify_refresh_invalid_response",
        "Spotify returned an invalid update · press Y to retry");

    private static SpotifyRefreshFailure TransientFailure() => new(
        SpotifyRefreshFailureDisposition.Transient,
        SpotifyWidgetViewState.Error,
        "spotify_refresh_failed",
        "Spotify update failed · press Y to retry");
}
