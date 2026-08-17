using System.Globalization;

namespace WidgetRail.WindowsSpotifyProvider;

/// <summary>
/// Owns bounded Spotify HTTP retry and retained rate-limit policy. Transport
/// owns trusted URI, request/response byte, and per-attempt timeout bounds.
/// </summary>
internal sealed class SpotifyHttpPolicy
{
    internal const int MaximumAutomaticRetryDelaySeconds = 30;
    internal const int MaximumAttempts = 3;

    private readonly ISpotifyHttpTransport _transport;
    private readonly ISpotifyDelay _delay;
    private readonly TimeProvider _time;
    private readonly object _rateLimitGate = new();
    private DateTimeOffset _rateLimitedUntil;

    internal SpotifyHttpPolicy(
        ISpotifyHttpTransport transport,
        ISpotifyDelay delay,
        TimeProvider time)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    internal async Task<SpotifyHttpResponse> SendAsync(
        SpotifyHttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfRateLimited();
            var response = await _transport.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            // Injected transports are permitted for deterministic tests but do
            // not gain authority to turn caller cancellation into a late
            // successful retry/token publication.
            cancellationToken.ThrowIfCancellationRequested();
            var retryable = response.StatusCode == 429 ||
                response.StatusCode is >= 500 and <= 599;
            if (!retryable) return response;

            var exponential = TimeSpan.FromMilliseconds(250 * (1 << attempt));
            var retryAfter = response.StatusCode == 429
                ? ParseRetryAfter(response.Headers)
                : null;
            var retryDelay = retryAfter is { } requested && requested > exponential
                ? requested
                : exponential;
            if (attempt + 1 >= MaximumAttempts)
            {
                if (response.StatusCode == 429) RetainRateLimit(retryDelay);
                return response;
            }
            if (retryDelay > TimeSpan.FromSeconds(MaximumAutomaticRetryDelaySeconds))
            {
                RetainRateLimit(retryDelay);
                throw new SpotifyProviderException(
                    "rate_limited", "Spotify asked the app to wait before trying again.");
            }
            await _delay.DelayAsync(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ThrowIfRateLimited()
    {
        lock (_rateLimitGate)
        {
            if (_rateLimitedUntil > _time.GetUtcNow())
                throw new SpotifyProviderException(
                    "rate_limited", "Spotify asked the app to wait before trying again.");
            _rateLimitedUntil = default;
        }
    }

    private void RetainRateLimit(TimeSpan delay)
    {
        var requestedUntil = _time.GetUtcNow().Add(delay);
        lock (_rateLimitGate)
        {
            if (requestedUntil > _rateLimitedUntil)
                _rateLimitedUntil = requestedUntil;
        }
    }

    private TimeSpan? ParseRetryAfter(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Retry-After", out var value)) return null;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                out var seconds) && seconds >= 0)
            return TimeSpan.FromSeconds(seconds);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date))
            return date <= _time.GetUtcNow() ? TimeSpan.Zero : date - _time.GetUtcNow();
        return null;
    }
}
