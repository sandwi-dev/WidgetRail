using WidgetRail.SpotifyPlayback;

namespace WidgetRail.WindowsSpotifyProvider;

internal interface ISpotifyPlaybackHostClient : IAsyncDisposable
{
    event EventHandler<SpotifyPlaybackEventEnvelope>? EventReceived;
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task<SpotifyPlaybackEventEnvelope> ConnectAsync(
        SpotifyPlaybackConnectOptions options, CancellationToken cancellationToken);
    Task<SpotifyPlaybackEventEnvelope> ProvideTokenAsync(
        string tokenRequestId,
        TrustedHostSpotifyAccessToken token,
        CancellationToken cancellationToken);
    Task<SpotifyPlaybackEventEnvelope> SendAsync(
        string type, object? payload, CancellationToken cancellationToken);
}

internal sealed class SpotifyPlaybackHostClientException : Exception
{
    internal SpotifyPlaybackHostClientException(
        string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    internal string Code { get; }
}
