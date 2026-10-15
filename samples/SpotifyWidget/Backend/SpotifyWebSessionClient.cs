using System.Text.Json;
using WidgetRail.SpotifyPlayback;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WindowsSpotifyProvider;

internal sealed class SpotifyWebSessionClient(WidgetWebSessionService sessions) : ISpotifyPlaybackHostClient
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim startup = new(1, 1);
    private readonly HashSet<string> tokens = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WidgetWebSession? session;
    private Task? events;
    private int disposed;
    public event EventHandler<SpotifyPlaybackEventEnvelope>? EventReceived;
    public bool IsRunning => disposed == 0 && session is { Completion.IsCompleted: false } && events?.IsCompleted == false;

    public async Task StartAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        await startup.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (session is not null) { await loaded.Task.WaitAsync(token).ConfigureAwait(false); return; }
            var source = WebSessionSource.Package("web/player.html",
                [new() { Path = "web/player.html", ContentType = "text/html" },
                 new() { Path = "web/web-session.js", ContentType = "text/javascript" }]) with
            {
                AllowAutoplay = true,
                NetworkOrigins = ["https://*.spotify.com", "https://*.scdn.co", "https://*.spotifycdn.com", "https://*.akamaized.net"],
            };
            session = await sessions.OpenAsync(new("playback", "Spotify playback", source)
                { Browser = new() { Chrome = BrowserChrome.OnDemand, Background = BrowserBackgroundMode.KeepAlive } }, token).ConfigureAwait(false);
            events = ReceiveAsync();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(35), token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is WidgetCapabilityException or TimeoutException)
        { throw new SpotifyPlaybackHostClientException("host_unavailable", "Spotify playback could not start.", error); }
        finally { startup.Release(); }
    }

    public Task<SpotifyPlaybackEventEnvelope> ConnectAsync(SpotifyPlaybackConnectOptions options, CancellationToken token)
    { options.Validate(); return SendAsync("connect", options, token); }

    public Task<SpotifyPlaybackEventEnvelope> ProvideTokenAsync(string tokenRequestId, TrustedHostSpotifyAccessToken token, CancellationToken cancellationToken)
    {
        new SpotifyAccessTokenLease(token.AccessToken, token.ExpiresAt, token.GrantedScopes).Validate(DateTimeOffset.UtcNow);
        lock (tokens) if (!tokens.Remove(tokenRequestId)) throw new SpotifyPlaybackHostClientException("unknown_token_request", "Playback token request has expired.");
        return SendAsync("provide_token", new { tokenRequestId, accessToken = token.AccessToken }, cancellationToken);
    }

    public async Task<SpotifyPlaybackEventEnvelope> SendAsync(string type, object? payload, CancellationToken token)
    {
        if (!IsRunning) throw new SpotifyPlaybackHostClientException("host_not_running", "Spotify playback is unavailable.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var response = await session!.RequestAsync<object?, SpotifyPlaybackEventEnvelope>(type, payload, timeout.Token).ConfigureAwait(false);
            Validate(response);
            if (response.Type == "command_failed") throw new SpotifyPlaybackHostClientException("sdk_command_failed", "Spotify could not complete the playback command.");
            var expected = type switch { "get_current_state" => "current_state", "get_volume" => "volume", _ => "command_completed" };
            if (response.Type != expected) throw new SpotifyPlaybackProtocolException("invalid_page_event", "Unexpected playback response.");
            return response;
        }
        catch (WidgetCapabilityException error) { throw new SpotifyPlaybackHostClientException("host_unavailable", "Spotify playback session is unavailable.", error); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested && !lifetime.IsCancellationRequested)
        { throw new SpotifyPlaybackHostClientException("host_command_timeout", "Spotify playback command timed out.", error); }
    }

    private async Task ReceiveAsync()
    {
        try
        {
            await foreach (var message in session!.ReadMessagesAsync(lifetime.Token).ConfigureAwait(false))
            {
                var value = SpotifyPlaybackProtocolCodec.DecodeEvent(message.GetRawText());
                Validate(value);
                if (value.Type == "sdk_loaded") loaded.TrySetResult();
                else if (value.Type == "sdk_error") loaded.TrySetException(new SpotifyPlaybackHostClientException(
                    value.Payload.GetProperty("code").GetString()!, "Spotify's playback SDK could not start."));
                if (value.Type == "token_requested")
                {
                    var id = value.Payload.GetProperty("tokenRequestId").GetString()!;
                    lock (tokens) if (tokens.Count >= 8 || !tokens.Add(id)) throw new SpotifyPlaybackProtocolException("invalid_page_event", "Invalid playback token request.");
                }
                EventReceived?.Invoke(this, value);
            }
            if (!lifetime.IsCancellationRequested) throw new IOException("Playback session ended.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            loaded.TrySetException(new SpotifyPlaybackHostClientException("host_unavailable", "Spotify playback stopped.", error));
            if (!lifetime.IsCancellationRequested) EventReceived?.Invoke(this, new(1, "sdk_error", null,
                JsonSerializer.SerializeToElement(new { code = "playback_error", message = "Playback session stopped." })));
        }
    }

    private static void Validate(SpotifyPlaybackEventEnvelope value)
    {
        if (value is null || value.Version != SpotifyPlaybackProtocol.Version || value.Payload.ValueKind != JsonValueKind.Object)
            throw new SpotifyPlaybackProtocolException("invalid_page_event", "Invalid playback message.");
        if (value.Type is "current_state" or "player_state_changed")
            SpotifyPlaybackStateValidator.ValidatePlaybackState(SpotifyPlaybackProtocolCodec.DecodePayload<SpotifyLocalPlaybackState>(value.Payload));
        else if (value.Type is "ready" or "not_ready" or "token_requested")
        {
            var key = value.Type == "token_requested" ? "tokenRequestId" : "deviceId";
            if (!value.Payload.TryGetProperty(key, out var property) || property.ValueKind != JsonValueKind.String ||
                property.GetString() is not { Length: > 0 and <= 128 } text || text.Any(c => c is < '!' or > '~'))
                throw new SpotifyPlaybackProtocolException("invalid_page_event", "Invalid playback identifier.");
        }
        else if (value.Type == "volume" && (!value.Payload.TryGetProperty("volume", out var volume) || !volume.TryGetDouble(out var number) || !double.IsFinite(number) || number is < 0 or > 1))
            throw new SpotifyPlaybackProtocolException("invalid_page_event", "Invalid playback volume.");
        else if (value.Type == "sdk_error" && (!value.Payload.TryGetProperty("code", out var code) || code.GetString() is not
            ("initialization_error" or "authentication_error" or "account_error" or "playback_error")))
            throw new SpotifyPlaybackProtocolException("invalid_page_event", "Invalid playback error.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        if (events is not null) await events.ConfigureAwait(false);
        if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
        lock (tokens) tokens.Clear();
        lifetime.Dispose();
    }
}
