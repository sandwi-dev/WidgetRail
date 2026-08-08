using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformSettings;

namespace GameBarAlternative.WindowsSpotifyProvider;

/// <summary>
/// Trusted, typed Spotify integration. It never exposes OAuth tokens or generic
/// remote HTTP authority to a widget.
/// </summary>
public sealed class WindowsSpotifyPlatformBackend :
    ISpotifyPlatformBrokerBackend, IAsyncDisposable
{
    public const string ExactRedirectUri = "http://127.0.0.1:43827/callback/";
    public const string PlaybackReadScope = "user-read-playback-state";
    public const string PlaybackControlScope = "user-modify-playback-state";
    public const string PlaylistReadPrivateScope = "playlist-read-private";
    public const string PlaylistReadCollaborativeScope = "playlist-read-collaborative";
    public const string StreamingScope = "streaming";

    // Browser sign-in is an explicit user interaction and can legitimately
    // outlive the overlay window. Keep the listener bounded, but do not apply
    // the ordinary short broker request deadline to a human OAuth flow.
    internal static readonly TimeSpan AuthorizationCallbackTimeout =
        TimeSpan.FromMinutes(15);

    private const int MaximumClientIdCharacters = 128;
    private const int MaximumAutomaticRetryDelaySeconds = 30;
    private const int MaximumHttpAttempts = 3;
    private const int MaximumDevices = 64;
    private const int MaximumQueueItems = 100;
    private const int MaximumCollectionPageSize = 50;
    private const int MaximumCollectionOffset = 100_000;
    private static readonly Uri RedirectUri = new(ExactRedirectUri, UriKind.Absolute);
    private static readonly Uri AuthorizeUri = new("https://accounts.spotify.com/authorize");
    private static readonly Uri TokenUri = new("https://accounts.spotify.com/api/token");
    private static readonly Uri PlayerUri = new("https://api.spotify.com/v1/me/player");
    private static readonly Uri DevicesUri = new("https://api.spotify.com/v1/me/player/devices");
    private static readonly Uri QueueUri = new("https://api.spotify.com/v1/me/player/queue");
    private static readonly Uri PlaylistsUri = new("https://api.spotify.com/v1/me/playlists");
    private static readonly string[] BaseScopes = [PlaybackReadScope, PlaybackControlScope];
    private static readonly HashSet<string> SupportedIncrementalScopes = new(StringComparer.Ordinal)
    {
        PlaybackReadScope,
        PlaybackControlScope,
        "user-read-recently-played",
        "playlist-read-private",
        "playlist-read-collaborative",
        "user-library-read",
        "user-library-modify",
        "user-top-read",
        StreamingScope,
    };

    private readonly ISpotifyClientConfigurationStore _configuration;
    private readonly ISpotifyTokenVault _vault;
    private readonly ISpotifyHttpTransport _http;
    private readonly ISpotifyBrowserLauncher _browser;
    private readonly ISpotifyAuthorizationCallbackReceiver _callback;
    private readonly ISpotifyDelay _delay;
    private readonly TimeProvider _time;
    private readonly SpotifyLocalPlaybackManager _localPlayback;
    private readonly ConcurrentDictionary<string, IntegrationState> _states = new();
    private readonly object _rateLimitGate = new();
    private DateTimeOffset _rateLimitedUntil;
    private int _disposed;
    private EventHandler<BrokerPlatformEvent>? _eventPublished;

    // Spotify is polled only while the owning widget is active. A global
    // provider event cannot safely identify package authority, so v1 does not
    // fan playback from one package to another through this event source.
    public event EventHandler<BrokerPlatformEvent>? EventPublished
    {
        add => _eventPublished += value;
        remove => _eventPublished -= value;
    }

    public WindowsSpotifyPlatformBackend(ISpotifyClientConfigurationStore configuration)
        : this(configuration, new WindowsCredentialSpotifyTokenVault(),
            new SpotifyHttpTransport(), new SpotifyBrowserLauncher(),
            new LoopbackSpotifyAuthorizationCallbackReceiver(), new SpotifyDelay(),
            TimeProvider.System) { }

    internal WindowsSpotifyPlatformBackend(
        ISpotifyClientConfigurationStore configuration,
        ISpotifyTokenVault vault,
        ISpotifyHttpTransport http,
        ISpotifyBrowserLauncher browser,
        ISpotifyAuthorizationCallbackReceiver callback,
        ISpotifyDelay delay,
        TimeProvider time,
        SpotifyLocalPlaybackManager? localPlayback = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _localPlayback = localPlayback ?? new SpotifyLocalPlaybackManager(
            DefaultPlaybackHostPath(), AcquireTrustedHostAccessTokenAsync);
    }

    public async Task<SpotifyProviderConfiguration> GetConfigurationAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var configured = await _configuration.ReadAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        return new SpotifyProviderConfiguration(configured is not null, ExactRedirectUri);
    }

    public async Task ConfigureClientAsync(
        SpotifyIntegrationIdentity identity, string clientId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateClientId(clientId);
        var existing = await _configuration.ReadAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        if (existing?.ClientId == clientId) return;
        await _localPlayback.StopAsync(identity, cancellationToken).ConfigureAwait(false);
        var state = StateFor(identity);
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await _configuration.ReadAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            if (current?.ClientId == clientId) return;

            // Fail closed: an old refresh token must be unusable before a new
            // client identity becomes active. A failed settings write can make
            // the user sign in again, but can never cross OAuth applications.
            await _vault.DeleteAsync(identity, cancellationToken).ConfigureAwait(false);
            state.AccessToken = null;
            await _configuration.WriteAsync(
                identity, new SpotifyClientConfiguration(clientId), cancellationToken)
                .ConfigureAwait(false);
        }
        finally { state.Gate.Release(); }
    }

    public async Task<SpotifyProviderAuthorization> GetAuthorizationAsync(
        SpotifyIntegrationIdentity identity,
        IReadOnlyCollection<string>? requiredScopes,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var configuration = await _configuration.ReadAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        if (configuration is null)
            return new(false, false, "Spotify client ID is not configured.");
        var refresh = await _vault.ReadAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        var state = StateFor(identity);
        if (refresh is not null &&
            !string.Equals(refresh.ClientId, configuration.ClientId, StringComparison.Ordinal))
        {
            state.AccessToken = null;
            await _vault.DeleteAsync(identity, cancellationToken).ConfigureAwait(false);
            refresh = null;
        }
        var granted = (state.AccessToken?.GrantedScopes ?? refresh?.GrantedScopes ??
            new HashSet<string>(StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        var requested = ValidateScopes(requiredScopes ?? BaseScopes);
        var needsReconsent = granted.Length > 0 && requested.Any(scope => !granted.Contains(scope));
        return new(true, refresh is not null,
            state.IsAuthorizing ? "Waiting for Spotify sign-in." :
            refresh is null ? "Spotify is not connected." :
                needsReconsent ? "Spotify needs additional permission." : "Spotify is connected.",
            granted, needsReconsent, state.IsAuthorizing);
    }

    public Task<SpotifyProviderAuthorization> GetAuthorizationAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken) =>
        GetAuthorizationAsync(identity, BaseScopes, cancellationToken);

    public async Task ConnectAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken) =>
        await ConnectAsync(identity, BaseScopes, cancellationToken).ConfigureAwait(false);

    public async Task ConnectAsync(
        SpotifyIntegrationIdentity identity,
        IReadOnlyCollection<string> requestedScopes,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var scopes = ValidateScopes(requestedScopes);
        var state = StateFor(identity);
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            state.IsAuthorizing = true;
            var configuration = await RequireConfigurationAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            var verifierBytes = RandomNumberGenerator.GetBytes(64);
            var stateBytes = RandomNumberGenerator.GetBytes(32);
            try
            {
                var verifier = Base64Url(verifierBytes);
                if (verifier.Length is < 43 or > 128)
                    throw new SpotifyProviderException(
                        "authorization_failed", "Spotify PKCE verifier was invalid.");
                var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
                var expectedState = Base64Url(stateBytes);
                var authorizationUri = BuildAuthorizationUri(
                    configuration.ClientId, verifierChallenge: challenge,
                    expectedState, scopes);

                // ReceiveAsync starts the loopback listener synchronously before
                // yielding, so the browser can never beat listener startup.
                var callback = await ReceiveAuthorizationAsync(
                    authorizationUri, expectedState, cancellationToken).ConfigureAwait(false);
                ValidateCallback(callback, expectedState);
                var token = await ExchangeAuthorizationCodeAsync(
                    configuration.ClientId, callback.Code!, verifier, scopes, cancellationToken)
                    .ConfigureAwait(false);
                if (token.RefreshToken is null)
                    throw new SpotifyProviderException(
                        "invalid_response", "Spotify did not return renewable authorization.");
                await _vault.SaveAsync(identity,
                    new SpotifyRefreshCredential(
                        configuration.ClientId, token.RefreshToken, token.GrantedScopes),
                    cancellationToken)
                    .ConfigureAwait(false);
                state.AccessToken = ToAccessToken(token);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(verifierBytes);
                CryptographicOperations.ZeroMemory(stateBytes);
            }
        }
        finally
        {
            state.IsAuthorizing = false;
            state.Gate.Release();
        }
    }

    /// <summary>
    /// Supplies a short-lived token only to trusted host components such as a
    /// future singleton Web Playback SDK host. Never project this through the
    /// widget broker or serialize it into widget state.
    /// </summary>
    public async Task<TrustedHostSpotifyAccessToken> AcquireTrustedHostAccessTokenAsync(
        SpotifyIntegrationIdentity identity,
        IReadOnlyCollection<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var scopes = ValidateScopes(requiredScopes);
        var state = StateFor(identity);
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configuration = await RequireConfigurationAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            SpotifyAccessToken? last = null;
            foreach (var scope in scopes)
            {
                last = await GetAccessTokenLockedAsync(identity, configuration, state, scope,
                    forceRefresh: false, cancellationToken).ConfigureAwait(false);
            }
            return new TrustedHostSpotifyAccessToken(last!.Value, last.ExpiresAt,
                last.GrantedScopes.Order(StringComparer.Ordinal).ToArray());
        }
        finally { state.Gate.Release(); }
    }

    public async Task DisconnectAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _localPlayback.StopAsync(identity, cancellationToken).ConfigureAwait(false);
        var state = StateFor(identity);
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            state.AccessToken = null;
            await _vault.DeleteAsync(identity, cancellationToken).ConfigureAwait(false);
        }
        finally { state.Gate.Release(); }
    }

    public async Task<SpotifyProviderPlayback> GetPlaybackAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        var response = await SendPlayerRequestAsync(
            identity, HttpMethod.Get, PlayerUri, PlaybackReadScope, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == 204)
            return UnavailablePlayback();
        EnsureSuccess(response);
        return ParsePlayback(response.Body);
    }

    public async Task ControlPlaybackAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyProviderPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await _localPlayback.TryControlAsync(identity, command, cancellationToken)
                .ConfigureAwait(false))
            return;
        var (method, uri) = BuildControlRequest(command);
        var response = await SendPlayerRequestAsync(
            identity, method, uri, PlaybackControlScope, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, allowNoContent: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _localPlayback.DisposeAsync().ConfigureAwait(false);
        foreach (var state in _states.Values) state.Gate.Dispose();
        _states.Clear();
        await _http.DisposeAsync().ConfigureAwait(false);
    }

    public Task<SpotifyConfigurationSummary> GetSpotifyConfigurationAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        BrokerCallAsync(async () =>
        {
            var summary = await GetConfigurationAsync(ToProviderIdentity(identity), cancellationToken)
                .ConfigureAwait(false);
            return new SpotifyConfigurationSummary(summary.IsConfigured, summary.RedirectUri);
        });

    public Task<SpotifyConfigurationSummary> ConfigureSpotifyClientAsync(
        BrokerWidgetIdentity identity, ConfigureSpotifyClientRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var providerIdentity = ToProviderIdentity(identity);
            await ConfigureClientAsync(providerIdentity, request.ClientId, cancellationToken)
                .ConfigureAwait(false);
            var summary = await GetConfigurationAsync(providerIdentity, cancellationToken)
                .ConfigureAwait(false);
            return new SpotifyConfigurationSummary(summary.IsConfigured, summary.RedirectUri);
        });

    public Task<SpotifyAuthorizationSummary> GetSpotifyAuthorizationAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        BrokerCallAsync(async () => MapAuthorization(
            await GetAuthorizationAsync(ToProviderIdentity(identity), cancellationToken)
                .ConfigureAwait(false), BrokerBaseScopes()));

    public Task<SpotifyAuthorizationSummary> ConnectSpotifyAsync(
        BrokerWidgetIdentity identity, ConnectSpotifyRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var providerIdentity = ToProviderIdentity(identity);
            var brokerScopes = ValidateBrokerScopes(request.RequestedScopes);
            var providerScopes = ExpandBrokerScopes(brokerScopes);
            await ConnectAsync(providerIdentity, providerScopes,
                cancellationToken).ConfigureAwait(false);
            return MapAuthorization(
                await GetAuthorizationAsync(providerIdentity,
                    providerScopes, cancellationToken).ConfigureAwait(false),
                brokerScopes);
        });

    public Task<SpotifyAuthorizationSummary> DisconnectSpotifyAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        BrokerCallAsync(async () =>
        {
            var providerIdentity = ToProviderIdentity(identity);
            await DisconnectAsync(providerIdentity, cancellationToken).ConfigureAwait(false);
            return MapAuthorization(
                await GetAuthorizationAsync(providerIdentity, cancellationToken).ConfigureAwait(false),
                BrokerBaseScopes());
        });

    public Task<SpotifyPlaybackSummary> GetSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        BrokerCallAsync(async () => MapPlayback(
            await GetPlaybackAsync(ToProviderIdentity(identity), cancellationToken)
                .ConfigureAwait(false)));

    public Task ControlSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, SpotifyPlaybackCommand command,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(command);
            await ControlPlaybackAsync(ToProviderIdentity(identity),
                ToProviderCommand(command), cancellationToken).ConfigureAwait(false);
        });

    public Task<SpotifyDevicesSummary> GetSpotifyDevicesAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        BrokerCallAsync(async () =>
        {
            var response = await SendPlayerRequestAsync(
                ToProviderIdentity(identity), HttpMethod.Get, DevicesUri,
                PlaybackReadScope, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == 204)
                return MergeLocalDevice(ToProviderIdentity(identity), []);
            EnsureSuccess(response);
            return MergeLocalDevice(ToProviderIdentity(identity), ParseDevices(response.Body).Devices);
        });

    public Task TransferSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, TransferSpotifyPlaybackRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.DeviceId == SpotifyLocalPlaybackManager.PublicDeviceId)
            {
                await StartAndTransferLocalPlaybackAsync(
                    ToProviderIdentity(identity), request.ContinuePlaying, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            ValidateOpaqueId(request.DeviceId, "device identifier");
            await TransferPlaybackToDeviceAsync(ToProviderIdentity(identity), request.DeviceId,
                request.ContinuePlaying, cancellationToken).ConfigureAwait(false);
        });

    public Task<SpotifyQueueSummary> GetSpotifyQueueAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        BrokerCallAsync(async () =>
        {
            var response = await SendPlayerRequestAsync(
                ToProviderIdentity(identity), HttpMethod.Get, QueueUri,
                PlaybackReadScope, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == 204) return new SpotifyQueueSummary(null, [], false);
            EnsureSuccess(response);
            return ParseQueue(response.Body);
        });

    public Task AddSpotifyQueueItemAsync(
        BrokerWidgetIdentity identity, AddSpotifyQueueItemRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateSpotifyUri(request.Uri);
            var providerIdentity = ToProviderIdentity(identity);
            var deviceId = await ResolveDeviceIdAsync(
                providerIdentity, request.DeviceId, cancellationToken).ConfigureAwait(false);
            if (deviceId is not null) ValidateOpaqueId(deviceId, "device identifier");
            var query = "?uri=" + Uri.EscapeDataString(request.Uri) +
                (deviceId is null ? string.Empty :
                    "&device_id=" + Uri.EscapeDataString(deviceId));
            var response = await SendPlayerRequestAsync(
                providerIdentity, HttpMethod.Post, new Uri(QueueUri + query),
                PlaybackControlScope, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, allowNoContent: true);
        });

    public Task StartSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, StartSpotifyPlaybackRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateStartPlayback(request);
            var providerIdentity = ToProviderIdentity(identity);
            var requestedLocalHost =
                request.DeviceId == SpotifyLocalPlaybackManager.PublicDeviceId;
            var deviceId = await ResolveDeviceIdAsync(
                providerIdentity, request.DeviceId, cancellationToken).ConfigureAwait(false);
            var uri = new Uri("https://api.spotify.com/v1/me/player/play" +
                (deviceId is null ? string.Empty :
                    "?device_id=" + Uri.EscapeDataString(deviceId)));
            string body;
            if (request.ContextUri is not null)
            {
                body = request.Offset is { } offset
                    ? JsonSerializer.Serialize(new
                    {
                        context_uri = request.ContextUri,
                        offset = new { position = offset },
                    })
                    : JsonSerializer.Serialize(new { context_uri = request.ContextUri });
            }
            else
            {
                body = JsonSerializer.Serialize(new { uris = request.ItemUris });
            }
            var response = await SendPlayerRequestAsync(
                providerIdentity, HttpMethod.Put, uri,
                PlaybackControlScope, cancellationToken, body).ConfigureAwait(false);
            EnsureSuccess(response, allowNoContent: true);
            if (requestedLocalHost) _localPlayback.MarkActive(providerIdentity);
        });

    public Task<SpotifyLocalPlaybackSummary> GetSpotifyLocalPlaybackAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        BrokerCallAsync(() => Task.FromResult(
            _localPlayback.GetSummary(ToProviderIdentity(identity))));

    public Task<SpotifyLocalPlaybackSummary> ControlSpotifyLocalPlaybackAsync(
        BrokerWidgetIdentity identity, SpotifyLocalPlaybackCommand command,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(command);
            var providerIdentity = ToProviderIdentity(identity);
            switch (command.Operation)
            {
                case SpotifyLocalPlaybackOperation.StartAndTransfer:
                    await StartAndTransferLocalPlaybackAsync(
                        providerIdentity, command.ContinuePlaying!.Value, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SpotifyLocalPlaybackOperation.Stop:
                    await _localPlayback.StopAsync(providerIdentity, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SpotifyLocalPlaybackOperation.SetVolume:
                    await _localPlayback.SetVolumeAsync(
                        providerIdentity, command.VolumePercent!.Value, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                default:
                    throw new SpotifyProviderException(
                        "invalid_request", "Spotify local-playback command is invalid.");
            }
            return _localPlayback.GetSummary(providerIdentity);
        });

    public Task<SpotifyPlaylistPageSummary> GetSpotifyPlaylistsAsync(
        BrokerWidgetIdentity identity, SpotifyPlaylistPageRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidatePage(request.Offset, request.Limit);
            var uri = new Uri(PlaylistsUri + "?offset=" + request.Offset.ToString(
                CultureInfo.InvariantCulture) + "&limit=" + request.Limit.ToString(
                    CultureInfo.InvariantCulture));
            var response = await SendPlayerRequestAsync(
                ToProviderIdentity(identity), HttpMethod.Get, uri,
                PlaylistReadPrivateScope, cancellationToken,
                additionalRequiredScope: PlaylistReadCollaborativeScope).ConfigureAwait(false);
            EnsureSuccess(response);
            return ParsePlaylistPage(response.Body, request.Offset, request.Limit);
        });

    public Task<SpotifyPlaylistItemsSummary> GetSpotifyPlaylistItemsAsync(
        BrokerWidgetIdentity identity, SpotifyPlaylistItemsRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ValidateOpaqueId(request.PlaylistId, "playlist identifier");
            ValidatePage(request.Offset, request.Limit);
            var escapedId = Uri.EscapeDataString(request.PlaylistId);
            var baseUri = "https://api.spotify.com/v1/playlists/" + escapedId;
            var providerIdentity = ToProviderIdentity(identity);
            var playlistResponse = await SendPlayerRequestAsync(
                providerIdentity, HttpMethod.Get, new Uri(baseUri),
                PlaylistReadPrivateScope, cancellationToken,
                additionalRequiredScope: PlaylistReadCollaborativeScope).ConfigureAwait(false);
            EnsureSuccess(playlistResponse);
            var playlist = ParsePlaylistDocument(playlistResponse.Body);
            var itemsUri = new Uri(baseUri + "/items?offset=" + request.Offset.ToString(
                CultureInfo.InvariantCulture) + "&limit=" + request.Limit.ToString(
                    CultureInfo.InvariantCulture));
            var itemsResponse = await SendPlayerRequestAsync(
                providerIdentity, HttpMethod.Get, itemsUri,
                PlaylistReadPrivateScope, cancellationToken,
                additionalRequiredScope: PlaylistReadCollaborativeScope).ConfigureAwait(false);
            EnsureSuccess(itemsResponse);
            return ParsePlaylistItems(
                playlist, itemsResponse.Body, request.Offset, request.Limit);
        });

    private async Task StartAndTransferLocalPlaybackAsync(
        SpotifyIntegrationIdentity identity,
        bool continuePlaying,
        CancellationToken cancellationToken)
    {
        var started = await _localPlayback.StartAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        await TransferPlaybackToDeviceAsync(
            identity, started.SpotifyDeviceId, continuePlaying, cancellationToken)
            .ConfigureAwait(false);
        _localPlayback.MarkActive(identity);
    }

    private async Task<string?> ResolveDeviceIdAsync(
        SpotifyIntegrationIdentity identity,
        string? requestedDeviceId,
        CancellationToken cancellationToken)
    {
        if (requestedDeviceId != SpotifyLocalPlaybackManager.PublicDeviceId)
            return requestedDeviceId;
        var current = _localPlayback.GetSpotifyDeviceId(identity);
        if (current is not null) return current;
        var started = await _localPlayback.StartAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        return started.SpotifyDeviceId;
    }

    private async Task TransferPlaybackToDeviceAsync(
        SpotifyIntegrationIdentity identity,
        string deviceId,
        bool continuePlaying,
        CancellationToken cancellationToken)
    {
        ValidateOpaqueId(deviceId, "device identifier");
        var body = JsonSerializer.Serialize(new
        {
            device_ids = new[] { deviceId },
            play = continuePlaying,
        });
        var response = await SendPlayerRequestAsync(
            identity, HttpMethod.Put, PlayerUri,
            PlaybackControlScope, cancellationToken, body).ConfigureAwait(false);
        EnsureSuccess(response, allowNoContent: true);
    }

    private SpotifyDevicesSummary MergeLocalDevice(
        SpotifyIntegrationIdentity identity,
        IReadOnlyList<SpotifyDeviceSummary> devices)
    {
        var local = _localPlayback.GetPublicDevice(identity);
        if (local is null) return new SpotifyDevicesSummary(devices.ToArray());
        return new SpotifyDevicesSummary(devices.Take(MaximumDevices - 1).Append(local).ToArray());
    }

    private async Task<SpotifyHttpResponse> SendPlayerRequestAsync(
        SpotifyIntegrationIdentity identity,
        HttpMethod method,
        Uri uri,
        string requiredScope,
        CancellationToken cancellationToken,
        string? jsonBody = null,
        string? additionalRequiredScope = null)
    {
        ThrowIfDisposed();
        var state = StateFor(identity);
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configuration = await RequireConfigurationAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            var token = await GetAccessTokenLockedAsync(
                identity, configuration, state, requiredScope, forceRefresh: false,
                cancellationToken).ConfigureAwait(false);
            if (additionalRequiredScope is not null)
                EnsureScope(token.GrantedScopes, additionalRequiredScope);
            var response = await SendWithRetryAsync(
                new SpotifyHttpRequest(
                    method, uri, Bearer(token.Value), JsonBody: jsonBody), cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode != 401) return response;

            state.AccessToken = null;
            token = await GetAccessTokenLockedAsync(
                identity, configuration, state, requiredScope, forceRefresh: true,
                cancellationToken).ConfigureAwait(false);
            if (additionalRequiredScope is not null)
                EnsureScope(token.GrantedScopes, additionalRequiredScope);
            return await SendWithRetryAsync(
                new SpotifyHttpRequest(
                    method, uri, Bearer(token.Value), JsonBody: jsonBody), cancellationToken)
                .ConfigureAwait(false);
        }
        finally { state.Gate.Release(); }
    }

    private async Task<SpotifyAuthorizationCallback> ReceiveAuthorizationAsync(
        Uri authorizationUri, string expectedState, CancellationToken cancellationToken)
    {
        using var authorizationLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var callbackTask = _callback.ReceiveAsync(
            RedirectUri, expectedState, AuthorizationCallbackTimeout,
            authorizationLifetime.Token);
        try
        {
            // ReceiveAsync starts the listener before its first await, but an
            // immediate bind failure is captured in the returned Task. Observe
            // that completed task before opening a browser so users are never
            // sent to a redirect URI that has no listening server.
            if (callbackTask.IsCompleted)
                return await callbackTask.ConfigureAwait(false);
            await _browser.OpenAsync(authorizationUri, cancellationToken).ConfigureAwait(false);
            return await callbackTask.ConfigureAwait(false);
        }
        catch
        {
            authorizationLifetime.Cancel();
            try { await callbackTask.ConfigureAwait(false); }
            catch { }
            throw;
        }
    }

    private async Task<SpotifyAccessToken> GetAccessTokenLockedAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyClientConfiguration configuration,
        IntegrationState state,
        string requiredScope,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var current = state.AccessToken;
        if (!forceRefresh && current is not null &&
            current.ExpiresAt > _time.GetUtcNow().AddSeconds(30))
        {
            EnsureScope(current.GrantedScopes, requiredScope);
            return current;
        }
        var credential = await _vault.ReadAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
            throw new SpotifyProviderException(
                "not_connected", "Connect Spotify before using this feature.");
        if (!string.Equals(credential.ClientId, configuration.ClientId, StringComparison.Ordinal))
        {
            state.AccessToken = null;
            await _vault.DeleteAsync(identity, cancellationToken).ConfigureAwait(false);
            throw new SpotifyProviderException(
                "not_connected", "Spotify client configuration changed. Connect again.");
        }
        SpotifyTokenResponse token;
        try
        {
            token = await RefreshAccessTokenAsync(configuration.ClientId,
                credential.RefreshToken, credential.GrantedScopes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SpotifyProviderException exception) when (exception.Code == "authorization_expired")
        {
            state.AccessToken = null;
            await _vault.DeleteAsync(identity, cancellationToken).ConfigureAwait(false);
            throw;
        }
        if (token.RefreshToken is { } rotated)
            await _vault.SaveAsync(identity,
                new SpotifyRefreshCredential(
                    configuration.ClientId, rotated, token.GrantedScopes), cancellationToken)
                .ConfigureAwait(false);
        current = ToAccessToken(token);
        EnsureScope(current.GrantedScopes, requiredScope);
        state.AccessToken = current;
        return current;
    }

    private async Task<SpotifyTokenResponse> ExchangeAuthorizationCodeAsync(
        string clientId, string code, string verifier, IReadOnlySet<string> requestedScopes,
        CancellationToken cancellationToken)
    {
        var form = Form(("client_id", clientId), ("grant_type", "authorization_code"),
            ("code", code), ("redirect_uri", ExactRedirectUri),
            ("code_verifier", verifier));
        var response = await SendWithRetryAsync(
            new SpotifyHttpRequest(HttpMethod.Post, TokenUri, FormBody: form), cancellationToken)
            .ConfigureAwait(false);
        return ParseTokenResponse(response, requestedScopes);
    }

    private async Task<SpotifyTokenResponse> RefreshAccessTokenAsync(
        string clientId, string refreshToken, IReadOnlySet<string> previouslyGrantedScopes,
        CancellationToken cancellationToken)
    {
        var form = Form(("grant_type", "refresh_token"),
            ("refresh_token", refreshToken), ("client_id", clientId));
        var response = await SendWithRetryAsync(
            new SpotifyHttpRequest(HttpMethod.Post, TokenUri, FormBody: form), cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == 400 && ReadTokenError(response.Body) == "invalid_grant")
            throw new SpotifyProviderException(
                "authorization_expired", "Spotify authorization expired. Connect again.");
        return ParseTokenResponse(response, previouslyGrantedScopes);
    }

    private async Task<SpotifyHttpResponse> SendWithRetryAsync(
        SpotifyHttpRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            ThrowIfRateLimited();
            var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var retryable = response.StatusCode == 429 || response.StatusCode is >= 500 and <= 599;
            if (!retryable) return response;
            var exponential = TimeSpan.FromMilliseconds(250 * (1 << attempt));
            var retryAfter = response.StatusCode == 429 ? ParseRetryAfter(response.Headers) : null;
            var delay = retryAfter is { } requested && requested > exponential ? requested : exponential;
            if (attempt + 1 >= MaximumHttpAttempts)
            {
                if (response.StatusCode == 429) RetainRateLimit(delay);
                return response;
            }
            if (delay > TimeSpan.FromSeconds(MaximumAutomaticRetryDelaySeconds))
            {
                RetainRateLimit(delay);
                throw new SpotifyProviderException(
                    "rate_limited", "Spotify asked the app to wait before trying again.");
            }
            await _delay.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
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
            if (requestedUntil > _rateLimitedUntil) _rateLimitedUntil = requestedUntil;
        }
    }

    private TimeSpan? ParseRetryAfter(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Retry-After", out var value)) return null;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
            seconds >= 0)
            return TimeSpan.FromSeconds(seconds);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date))
            return date <= _time.GetUtcNow() ? TimeSpan.Zero : date - _time.GetUtcNow();
        return null;
    }

    private static SpotifyTokenResponse ParseTokenResponse(
        SpotifyHttpResponse response, IReadOnlySet<string> scopesWhenOmitted)
    {
        EnsureSuccess(response);
        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            var access = RequiredString(root, "access_token", 4096);
            var type = RequiredString(root, "token_type", 32);
            if (!type.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                throw new SpotifyProviderException(
                    "invalid_response", "Spotify returned an unsupported token type.");
            if (!root.TryGetProperty("expires_in", out var expiresElement) ||
                !expiresElement.TryGetInt32(out var expires) || expires is < 60 or > 86400)
                throw new SpotifyProviderException(
                    "invalid_response", "Spotify returned an invalid token lifetime.");
            var refresh = OptionalString(root, "refresh_token", 4096);
            var scopeText = OptionalString(root, "scope", 2048);
            var scopes = scopeText is null ? scopesWhenOmitted.ToHashSet(StringComparer.Ordinal) :
                scopeText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.Ordinal);
            return new(access, TimeSpan.FromSeconds(expires), refresh, scopes);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw new SpotifyProviderException(
                "invalid_response", "Spotify returned an invalid token response.", exception);
        }
    }

    private SpotifyAccessToken ToAccessToken(SpotifyTokenResponse token) =>
        new(token.AccessToken, _time.GetUtcNow().Add(token.Lifetime), token.GrantedScopes);

    private static SpotifyProviderPlayback ParsePlayback(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var isPlaying = OptionalBoolean(root, "is_playing");
            var progress = OptionalInt64(root, "progress_ms") ?? 0;
            var repeat = OptionalString(root, "repeat_state", 16) ?? "off";
            var shuffle = OptionalBoolean(root, "shuffle_state");
            int? volume = null;
            if (root.TryGetProperty("device", out var device) &&
                device.ValueKind == JsonValueKind.Object &&
                device.TryGetProperty("volume_percent", out var volumeElement) &&
                volumeElement.TryGetInt32(out var parsedVolume))
                volume = Math.Clamp(parsedVolume, 0, 100);

            if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
                return UnavailablePlayback() with
                {
                    IsPlaying = isPlaying,
                    ProgressMilliseconds = Math.Max(0, progress),
                    RepeatState = repeat,
                    ShuffleState = shuffle,
                    VolumePercent = volume,
                };

            var itemType = RequiredString(item, "type", 32);
            if (itemType is not ("track" or "episode"))
                throw new SpotifyProviderException(
                    "invalid_response", "Spotify returned an unsupported playback item.");
            var title = RequiredString(item, "name", 512);
            var duration = OptionalInt64(item, "duration_ms") ?? 0;
            var uri = OptionalString(item, "uri", 2048);
            var subtitle = itemType == "track" ? JoinArtistNames(item) : ReadEpisodeShow(item);
            var artwork = ReadArtwork(item, itemType);
            var contextName = ReadContext(root);
            var actions = ReadActions(root);
            return new SpotifyProviderPlayback(
                true, isPlaying, Math.Clamp(progress, 0, Math.Max(duration, 0)),
                Math.Max(duration, 0), itemType, title, subtitle, contextName,
                artwork, uri, repeat, shuffle, volume, actions);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw new SpotifyProviderException(
                "invalid_response", "Spotify returned invalid playback data.", exception);
        }
    }

    private static SpotifyDevicesSummary ParseDevices(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = RequireObject(document.RootElement);
            var devices = RequireArray(root, "devices");
            if (devices.GetArrayLength() > MaximumDevices)
                throw InvalidSpotifyResponse("Spotify returned too many playback devices.");
            var result = new List<SpotifyDeviceSummary>(devices.GetArrayLength());
            foreach (var value in devices.EnumerateArray())
            {
                var device = RequireObject(value);
                var id = RequiredIdentifier(device, "id");
                var volume = OptionalNullableInt32(device, "volume_percent");
                if (volume is < 0 or > 100)
                    throw InvalidSpotifyResponse("Spotify returned an invalid device volume.");
                result.Add(new SpotifyDeviceSummary(
                    id,
                    RequiredDisplayString(device, "name", 160),
                    RequiredDisplayString(device, "type", 160),
                    OptionalNullableBoolean(device, "is_active") ?? false,
                    OptionalNullableBoolean(device, "is_restricted") ?? false,
                    OptionalNullableBoolean(device, "supports_volume") ?? false,
                    volume,
                    false));
            }
            return new SpotifyDevicesSummary(result);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw InvalidSpotifyResponse("Spotify returned invalid playback devices.", exception);
        }
    }

    private static SpotifyQueueSummary ParseQueue(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = RequireObject(document.RootElement);
            SpotifyMediaItemSummary? current = null;
            if (root.TryGetProperty("currently_playing", out var currentElement) &&
                currentElement.ValueKind == JsonValueKind.Object)
                current = ParseMediaItem(currentElement);
            var queue = RequireArray(root, "queue");
            var items = new List<SpotifyMediaItemSummary>(
                Math.Min(queue.GetArrayLength(), MaximumQueueItems));
            var truncated = queue.GetArrayLength() > MaximumQueueItems;
            foreach (var item in queue.EnumerateArray())
            {
                if (items.Count == MaximumQueueItems) break;
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (ParseMediaItem(item) is { } parsed) items.Add(parsed);
            }
            return new SpotifyQueueSummary(current, items, truncated);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw InvalidSpotifyResponse("Spotify returned an invalid queue.", exception);
        }
    }

    private static SpotifyPlaylistPageSummary ParsePlaylistPage(
        string body, int requestedOffset, int requestedLimit)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = RequireObject(document.RootElement);
            var offset = RequiredBoundedInt32(root, "offset", 0, MaximumCollectionOffset);
            var limit = RequiredBoundedInt32(root, "limit", 1, MaximumCollectionPageSize);
            var total = RequiredBoundedInt32(root, "total", 0, int.MaxValue);
            if (offset != requestedOffset || limit > requestedLimit)
                throw InvalidSpotifyResponse("Spotify returned an inconsistent playlist page.");
            var values = RequireArray(root, "items");
            if (values.GetArrayLength() > limit)
                throw InvalidSpotifyResponse("Spotify returned too many playlists.");
            var items = new List<SpotifyPlaylistSummary>(values.GetArrayLength());
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                items.Add(ParsePlaylist(value));
            }
            return new SpotifyPlaylistPageSummary(items, offset, limit, total);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw InvalidSpotifyResponse("Spotify returned an invalid playlist page.", exception);
        }
    }

    private static SpotifyPlaylistSummary ParsePlaylistDocument(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return ParsePlaylist(document.RootElement);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw InvalidSpotifyResponse("Spotify returned an invalid playlist.", exception);
        }
    }

    private static SpotifyPlaylistItemsSummary ParsePlaylistItems(
        SpotifyPlaylistSummary playlist, string body, int requestedOffset, int requestedLimit)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = RequireObject(document.RootElement);
            var offset = RequiredBoundedInt32(root, "offset", 0, MaximumCollectionOffset);
            var limit = RequiredBoundedInt32(root, "limit", 1, MaximumCollectionPageSize);
            var total = RequiredBoundedInt32(root, "total", 0, int.MaxValue);
            if (offset != requestedOffset || limit > requestedLimit)
                throw InvalidSpotifyResponse("Spotify returned an inconsistent playlist item page.");
            var values = RequireArray(root, "items");
            if (values.GetArrayLength() > limit)
                throw InvalidSpotifyResponse("Spotify returned too many playlist items.");
            var items = new List<SpotifyMediaItemSummary>(values.GetArrayLength());
            foreach (var wrapper in values.EnumerateArray())
            {
                if (wrapper.ValueKind != JsonValueKind.Object ||
                    !wrapper.TryGetProperty("item", out var item) ||
                    item.ValueKind != JsonValueKind.Object) continue;
                if (ParseMediaItem(item) is { } parsed) items.Add(parsed);
            }
            return new SpotifyPlaylistItemsSummary(playlist, items, offset, limit, total);
        }
        catch (SpotifyProviderException) { throw; }
        catch (JsonException exception)
        {
            throw InvalidSpotifyResponse("Spotify returned invalid playlist items.", exception);
        }
    }

    private static SpotifyPlaylistSummary ParsePlaylist(JsonElement value)
    {
        var playlist = RequireObject(value);
        var id = RequiredIdentifier(playlist, "id");
        var count = 0;
        if (playlist.TryGetProperty("items", out var itemPage) &&
            itemPage.ValueKind == JsonValueKind.Object)
            count = OptionalNullableInt32(itemPage, "total") ?? 0;
        else if (playlist.TryGetProperty("tracks", out var trackPage) &&
            trackPage.ValueKind == JsonValueKind.Object)
            count = OptionalNullableInt32(trackPage, "total") ?? 0;
        if (count < 0) throw InvalidSpotifyResponse("Spotify returned an invalid playlist size.");

        var ownerName = "Spotify";
        if (playlist.TryGetProperty("owner", out var owner) &&
            owner.ValueKind == JsonValueKind.Object)
            ownerName = OptionalDisplayString(owner, "display_name", 160) ?? "Spotify";
        return new SpotifyPlaylistSummary(
            id,
            RequiredDisplayString(playlist, "name", 160),
            OptionalDisplayString(playlist, "description", 160),
            ReadImageArray(playlist),
            BuildSpotifyUrl("playlist", id),
            "spotify:playlist:" + id,
            ownerName,
            OptionalNullableBoolean(playlist, "collaborative") ?? false,
            OptionalNullableBoolean(playlist, "public"),
            count);
    }

    private static SpotifyMediaItemSummary? ParseMediaItem(JsonElement value)
    {
        var item = RequireObject(value);
        var type = OptionalString(item, "type", 32);
        if (type is not ("track" or "episode")) return null;
        var id = OptionalString(item, "id", 256);
        var uri = OptionalString(item, "uri", 2_048);
        if (id is null || uri is null) return null;
        ValidateOpaqueId(id, "media identifier", "invalid_response");
        ValidateSpotifyUri(uri, "invalid_response");
        var duration = OptionalInt64(item, "duration_ms") ?? 0;
        if (duration is < 0 or > 604_800_000)
            throw InvalidSpotifyResponse("Spotify returned an invalid media duration.");
        var subtitle = type == "track" ? JoinArtistNames(item) : ReadEpisodeShow(item);
        return new SpotifyMediaItemSummary(
            type == "track" ? SpotifyPlaybackItemType.Track : SpotifyPlaybackItemType.Episode,
            RequiredDisplayString(item, "name", 160),
            SanitizeDisplayValue(subtitle, 160) ?? "Spotify",
            duration,
            ReadArtwork(item, type),
            uri,
            BuildSpotifyUrl(type, id),
            OptionalNullableBoolean(item, "is_playable") ?? true);
    }

    private static string BuildSpotifyUrl(string type, string id) =>
        "https://open.spotify.com/" + type + "/" + Uri.EscapeDataString(id);

    private static SpotifyProviderPlaybackActions ReadActions(JsonElement root)
    {
        var disallows = default(JsonElement);
        if (root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Object)
            actions.TryGetProperty("disallows", out disallows);
        bool Allowed(string name) => disallows.ValueKind != JsonValueKind.Object ||
            !disallows.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.True;
        return new(Allowed("pausing"), Allowed("resuming"), Allowed("seeking"),
            Allowed("skipping_next"), Allowed("skipping_prev"),
            Allowed("toggling_repeat_context"), Allowed("toggling_repeat_track"),
            Allowed("toggling_shuffle"));
    }

    private static string JoinArtistNames(JsonElement item)
    {
        if (!item.TryGetProperty("artists", out var artists) ||
            artists.ValueKind != JsonValueKind.Array) return "Spotify";
        return string.Join(", ", artists.EnumerateArray().Take(8)
            .Select(artist => OptionalString(artist, "name", 256))
            .Where(name => name is not null)) is { Length: > 0 } names ? names : "Spotify";
    }

    private static string ReadEpisodeShow(JsonElement item) =>
        item.TryGetProperty("show", out var show) && show.ValueKind == JsonValueKind.Object
            ? OptionalString(show, "name", 512) ?? "Spotify"
            : "Spotify";

    private static string? ReadArtwork(JsonElement item, string itemType)
    {
        JsonElement images;
        if (itemType == "track")
        {
            if (!item.TryGetProperty("album", out var album) ||
                album.ValueKind != JsonValueKind.Object ||
                !album.TryGetProperty("images", out images)) return null;
        }
        else if (!item.TryGetProperty("images", out images)) return null;
        if (images.ValueKind != JsonValueKind.Array) return null;
        foreach (var image in images.EnumerateArray())
        {
            var url = OptionalString(image, "url", 2048);
            if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return url;
        }
        return null;
    }

    private static string? ReadContext(JsonElement root)
    {
        if (!root.TryGetProperty("context", out var context) ||
            context.ValueKind != JsonValueKind.Object) return null;
        return OptionalString(context, "type", 64);
    }

    private static SpotifyProviderPlayback UnavailablePlayback() => new(
        false, false, 0, 0, "none", "Nothing playing", "Spotify", null, null, null,
        "off", false, null, new(false, false, false, false, false, false, false, false));

    private static (HttpMethod Method, Uri Uri) BuildControlRequest(
        SpotifyProviderPlaybackCommand command)
    {
        var (method, path) = command.Operation switch
        {
            SpotifyProviderPlaybackOperation.Play => (HttpMethod.Put, "/v1/me/player/play"),
            SpotifyProviderPlaybackOperation.Pause => (HttpMethod.Put, "/v1/me/player/pause"),
            SpotifyProviderPlaybackOperation.Next => (HttpMethod.Post, "/v1/me/player/next"),
            SpotifyProviderPlaybackOperation.Previous => (HttpMethod.Post, "/v1/me/player/previous"),
            SpotifyProviderPlaybackOperation.Seek => (HttpMethod.Put,
                "/v1/me/player/seek?position_ms=" + RequiredRange(
                    command.PositionMilliseconds, 0, int.MaxValue, "position_ms")),
            SpotifyProviderPlaybackOperation.SetRepeat => (HttpMethod.Put,
                "/v1/me/player/repeat?state=" + ValidateRepeat(command.RepeatState)),
            SpotifyProviderPlaybackOperation.SetShuffle => (HttpMethod.Put,
                "/v1/me/player/shuffle?state=" + RequiredBoolean(command.Enabled, "state")),
            SpotifyProviderPlaybackOperation.SetVolume => (HttpMethod.Put,
                "/v1/me/player/volume?volume_percent=" + RequiredRange(
                    command.VolumePercent, 0, 100, "volume_percent")),
            _ => throw new SpotifyProviderException(
                "invalid_request", "Spotify playback command is invalid."),
        };
        return (method, new Uri("https://api.spotify.com" + path));
    }

    private static Uri BuildAuthorizationUri(
        string clientId, string verifierChallenge, string expectedState,
        IReadOnlySet<string> scopes)
    {
        var query = Form(("client_id", clientId), ("response_type", "code"),
            ("redirect_uri", ExactRedirectUri),
            ("scope", string.Join(' ', scopes.Order(StringComparer.Ordinal))),
            ("code_challenge_method", "S256"), ("code_challenge", verifierChallenge),
            ("state", expectedState));
        return new UriBuilder(AuthorizeUri) { Query = query }.Uri;
    }

    private static void ValidateCallback(
        SpotifyAuthorizationCallback callback, string expectedState)
    {
        if (callback.Error is { } error)
            throw new SpotifyProviderException(
                "authorization_denied", SanitizeMessage(callback.ErrorDescription) ??
                    $"Spotify sign-in was not completed ({SanitizeMessage(error)})." );
        if (callback.Code is null || callback.State is null ||
            !FixedTimeEquals(callback.State, expectedState))
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback could not be verified.");
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        try { return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes); }
        finally
        {
            CryptographicOperations.ZeroMemory(leftBytes);
            CryptographicOperations.ZeroMemory(rightBytes);
        }
    }

    private static IReadOnlySet<string> ValidateScopes(IReadOnlyCollection<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count is 0 or > 12)
            throw new SpotifyProviderException("invalid_scope", "Spotify permission request is invalid.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in scopes)
            if (!SupportedIncrementalScopes.Contains(scope) || !result.Add(scope))
                throw new SpotifyProviderException(
                    "invalid_scope", "Spotify permission request is unsupported.");
        return result;
    }

    private static void EnsureScope(IReadOnlySet<string> granted, string required)
    {
        if (!granted.Contains(required))
            throw new SpotifyProviderException(
                "authorization_scope_required", "Spotify needs additional permission. Connect again.");
    }

    private static string Form(params (string Name, string Value)[] fields) =>
        string.Join('&', fields.Select(field =>
            Uri.EscapeDataString(field.Name) + "=" + Uri.EscapeDataString(field.Value)));

    private static IReadOnlyDictionary<string, string> Bearer(string token) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer " + token,
        };

    private static void EnsureSuccess(SpotifyHttpResponse response, bool allowNoContent = false)
    {
        if (response.StatusCode is >= 200 and <= 299 &&
            (allowNoContent || response.StatusCode != 204)) return;
        var message = ReadApiError(response.Body) ?? response.StatusCode switch
        {
            400 => "Spotify rejected the request.",
            401 => "Spotify authorization expired. Connect again.",
            403 => "Spotify did not allow this action.",
            404 => "Spotify has no active playback device.",
            429 => "Spotify rate limit reached. Try again later.",
            >= 500 => "Spotify is temporarily unavailable.",
            _ => "Spotify request failed.",
        };
        var code = response.StatusCode switch
        {
            400 => "invalid_request",
            401 => "authorization_expired",
            403 => "forbidden",
            404 => "resource_not_found",
            429 => "rate_limited",
            >= 500 => "spotify_unavailable",
            _ => "spotify_error",
        };
        throw new SpotifyProviderException(code, message);
    }

    private static string? ReadApiError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                    return OptionalString(error, "message", 512) is { } message
                        ? SanitizeMessage(message) : null;
                if (error.ValueKind == JsonValueKind.String)
                    return OptionalString(root, "error_description", 512) is { } description
                        ? SanitizeMessage(description) : SanitizeMessage(error.GetString());
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static string? ReadTokenError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return OptionalString(document.RootElement, "error", 128);
        }
        catch (JsonException) { return null; }
    }

    private static string? SanitizeMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return new string(value.Where(character => !char.IsControl(character)).Take(256).ToArray());
    }

    private static string RequiredString(JsonElement parent, string property, int maximum)
    {
        var value = OptionalString(parent, property, maximum);
        if (value is null)
            throw new SpotifyProviderException(
                "invalid_response", "Spotify response was missing required data.");
        return value;
    }

    private static string? OptionalString(JsonElement parent, string property, int maximum)
    {
        if (!parent.TryGetProperty(property, out var element) ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (element.ValueKind != JsonValueKind.String)
            throw new SpotifyProviderException("invalid_response", "Spotify response was invalid.");
        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new SpotifyProviderException("invalid_response", "Spotify response was invalid.");
        return value;
    }

    private static JsonElement RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw InvalidSpotifyResponse("Spotify response was invalid.");
        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            throw InvalidSpotifyResponse("Spotify response was missing required data.");
        return value;
    }

    private static string RequiredIdentifier(JsonElement parent, string property)
    {
        var value = RequiredString(parent, property, 256);
        ValidateOpaqueId(value, "identifier", "invalid_response");
        return value;
    }

    private static string RequiredDisplayString(
        JsonElement parent, string property, int maximum) =>
        OptionalDisplayString(parent, property, maximum) ??
        throw InvalidSpotifyResponse("Spotify response was missing display data.");

    private static string? OptionalDisplayString(
        JsonElement parent, string property, int maximum)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (value.ValueKind != JsonValueKind.String)
            throw InvalidSpotifyResponse("Spotify response display data was invalid.");
        return SanitizeDisplayValue(value.GetString(), maximum);
    }

    private static string? SanitizeDisplayValue(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sanitized = new string(value.Where(character => !char.IsControl(character))
            .Take(maximum).ToArray()).Trim();
        return sanitized.Length == 0 ? null : sanitized;
    }

    private static int RequiredBoundedInt32(
        JsonElement parent, string property, int minimum, int maximum)
    {
        var value = OptionalNullableInt32(parent, property);
        if (value is null || value < minimum || value > maximum)
            throw InvalidSpotifyResponse("Spotify response contained an invalid number.");
        return value.Value;
    }

    private static int? OptionalNullableInt32(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
            throw InvalidSpotifyResponse("Spotify response contained an invalid number.");
        return parsed;
    }

    private static bool? OptionalNullableBoolean(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw InvalidSpotifyResponse("Spotify response contained an invalid boolean."),
        };
    }

    private static string? ReadImageArray(JsonElement parent)
    {
        if (!parent.TryGetProperty("images", out var images) ||
            images.ValueKind != JsonValueKind.Array) return null;
        foreach (var image in images.EnumerateArray())
        {
            if (image.ValueKind != JsonValueKind.Object) continue;
            var url = OptionalString(image, "url", 2_048);
            if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                parsed.Scheme == Uri.UriSchemeHttps && parsed.IsDefaultPort &&
                string.IsNullOrEmpty(parsed.UserInfo)) return url;
        }
        return null;
    }

    private static SpotifyProviderException InvalidSpotifyResponse(
        string message, Exception? innerException = null) =>
        new("invalid_response", message, innerException);

    private static void ValidateOpaqueId(
        string value, string label, string errorCode = "invalid_request")
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 ||
            value.Any(character => character is < '!' or > '~'))
            throw new SpotifyProviderException(
                errorCode, $"Spotify {label} is invalid.");
    }

    private static void ValidateSpotifyUri(
        string value, string errorCode = "invalid_request")
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2_048 ||
            value.Any(char.IsControl) ||
            !value.StartsWith("spotify:", StringComparison.Ordinal) ||
            value.Count(character => character == ':') < 2)
            throw new SpotifyProviderException(
                errorCode, "Spotify URI is invalid.");
    }

    private static void ValidatePage(int offset, int limit)
    {
        if (offset is < 0 or > MaximumCollectionOffset ||
            limit is < 1 or > MaximumCollectionPageSize)
            throw new SpotifyProviderException(
                "invalid_request", "Spotify page request is invalid.");
    }

    private static void ValidateStartPlayback(StartSpotifyPlaybackRequest request)
    {
        if ((request.ContextUri is null) == (request.ItemUris is null) ||
            request.ItemUris is { Count: < 1 or > MaximumCollectionPageSize } ||
            request.Offset is < 0 || request.Offset is not null && request.ContextUri is null)
            throw new SpotifyProviderException(
                "invalid_request", "Spotify playback selection is invalid.");
        if (request.ContextUri is not null) ValidateSpotifyUri(request.ContextUri);
        if (request.ItemUris is not null)
            foreach (var uri in request.ItemUris) ValidateSpotifyUri(uri);
        if (request.DeviceId is not null)
            ValidateOpaqueId(request.DeviceId, "device identifier");
    }

    private static bool OptionalBoolean(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.True;

    private static long? OptionalInt64(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var element) && element.TryGetInt64(out var value)
            ? value : null;

    private static string ValidateRepeat(string? state) => state switch
    {
        "track" or "context" or "off" => state,
        _ => throw new SpotifyProviderException(
            "invalid_request", "Spotify repeat state is invalid."),
    };

    private static string RequiredBoolean(bool? value, string name) => value switch
    {
        true => "true",
        false => "false",
        _ => throw new SpotifyProviderException("invalid_request", $"Spotify {name} is required."),
    };

    private static long RequiredRange(long? value, long minimum, long maximum, string name)
    {
        if (value is null || value < minimum || value > maximum)
            throw new SpotifyProviderException("invalid_request", $"Spotify {name} is invalid.");
        return value.Value;
    }

    private static int RequiredRange(int? value, int minimum, int maximum, string name)
    {
        if (value is null || value < minimum || value > maximum)
            throw new SpotifyProviderException("invalid_request", $"Spotify {name} is invalid.");
        return value.Value;
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void ValidateClientId(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (clientId.Length is < 8 or > MaximumClientIdCharacters ||
            clientId.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new SpotifyProviderException(
                "invalid_client_id", "Spotify client ID must contain only ASCII letters and digits.");
    }

    private async Task<SpotifyClientConfiguration> RequireConfigurationAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        var configuration = await _configuration.ReadAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        if (configuration is null)
            throw new SpotifyProviderException(
                "not_configured", "Configure a Spotify client ID first.");
        ValidateClientId(configuration.ClientId);
        return configuration;
    }

    private IntegrationState StateFor(SpotifyIntegrationIdentity identity) =>
        _states.GetOrAdd(identity.Authority, static _ => new IntegrationState());

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static string DefaultPlaybackHostPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "SpotifyPlaybackHost", "SpotifyPlaybackHost.exe"));

    private static SpotifyIntegrationIdentity ToProviderIdentity(BrokerWidgetIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        return new(identity.PublisherId, identity.PackageId);
    }

    private static IReadOnlyList<SpotifyAuthorizationScope> BrokerBaseScopes() =>
        [SpotifyAuthorizationScope.PlaybackStateRead,
            SpotifyAuthorizationScope.PlaybackStateControl];

    private static IReadOnlyList<SpotifyAuthorizationScope> ValidateBrokerScopes(
        IReadOnlyList<SpotifyAuthorizationScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count is 0 or > 8 || scopes.Distinct().Count() != scopes.Count ||
            scopes.Any(scope => !Enum.IsDefined(scope)))
            throw new SpotifyProviderException(
                "invalid_scope", "Spotify permission request is unsupported.");
        return scopes.ToArray();
    }

    private static IReadOnlyList<string> ExpandBrokerScopes(
        IReadOnlyList<SpotifyAuthorizationScope> scopes) => scopes
        .SelectMany(ToScopes).Distinct(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> ToScopes(SpotifyAuthorizationScope scope) => scope switch
    {
        SpotifyAuthorizationScope.PlaybackStateRead => [PlaybackReadScope],
        SpotifyAuthorizationScope.PlaybackStateControl => [PlaybackControlScope],
        SpotifyAuthorizationScope.LocalPlayback => [StreamingScope],
        SpotifyAuthorizationScope.PlaylistsRead =>
            [PlaylistReadPrivateScope, PlaylistReadCollaborativeScope],
        SpotifyAuthorizationScope.LibraryRead => ["user-library-read"],
        SpotifyAuthorizationScope.LibraryModify => ["user-library-modify"],
        SpotifyAuthorizationScope.RecentlyPlayedRead => ["user-read-recently-played"],
        SpotifyAuthorizationScope.UserTopRead => ["user-top-read"],
        _ => throw new SpotifyProviderException(
            "invalid_scope", "Spotify permission request is unsupported."),
    };

    private static bool HasBrokerScope(
        IReadOnlySet<string> granted, SpotifyAuthorizationScope scope) =>
        ToScopes(scope).All(granted.Contains);

    private static SpotifyAuthorizationSummary MapAuthorization(
        SpotifyProviderAuthorization authorization,
        IReadOnlyList<SpotifyAuthorizationScope> requested)
    {
        var providerGranted = (authorization.GrantedScopes ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var granted = requested.Where(scope => HasBrokerScope(providerGranted, scope))
            .Distinct().Order().ToArray();
        var state = !authorization.IsConfigured ? SpotifyAuthorizationState.Unconfigured :
            authorization.IsAuthorizing ? SpotifyAuthorizationState.Authorizing :
            !authorization.IsConnected ? SpotifyAuthorizationState.Disconnected :
            authorization.NeedsReconsent ? SpotifyAuthorizationState.ReauthorizationRequired :
            SpotifyAuthorizationState.Connected;
        return new(state, requested, granted, authorization.Message);
    }

    private SpotifyPlaybackSummary MapPlayback(SpotifyProviderPlayback playback)
    {
        SpotifyPlaybackItemSummary? item = null;
        if (playback.IsAvailable)
        {
            item = new SpotifyPlaybackItemSummary(
                playback.ItemType == "track" ? SpotifyPlaybackItemType.Track :
                    SpotifyPlaybackItemType.Episode,
                playback.Title, playback.Subtitle, playback.ContextName,
                playback.ArtworkUrl, playback.SpotifyUri);
        }
        return new SpotifyPlaybackSummary(
            playback.IsAvailable, playback.IsPlaying, playback.ProgressMilliseconds,
            playback.DurationMilliseconds, _time.GetUtcNow().ToUnixTimeMilliseconds(),
            playback.RepeatState switch
            {
                "track" => SpotifyRepeatState.Track,
                "context" => SpotifyRepeatState.Context,
                _ => SpotifyRepeatState.Off,
            },
            playback.ShuffleState, item,
            new SpotifyPlaybackDisallowedActions(
                !playback.Actions.CanPause, !playback.Actions.CanResume,
                !playback.Actions.CanSeek, !playback.Actions.CanSkipNext,
                !playback.Actions.CanSkipPrevious, !playback.Actions.CanSetRepeatContext,
                !playback.Actions.CanSetRepeatTrack, !playback.Actions.CanSetShuffle),
            playback.Attribution);
    }

    private static SpotifyProviderPlaybackCommand ToProviderCommand(SpotifyPlaybackCommand command) =>
        new(command.Operation switch
        {
            SpotifyPlaybackOperation.Play => SpotifyProviderPlaybackOperation.Play,
            SpotifyPlaybackOperation.Pause => SpotifyProviderPlaybackOperation.Pause,
            SpotifyPlaybackOperation.Next => SpotifyProviderPlaybackOperation.Next,
            SpotifyPlaybackOperation.Previous => SpotifyProviderPlaybackOperation.Previous,
            SpotifyPlaybackOperation.Seek => SpotifyProviderPlaybackOperation.Seek,
            SpotifyPlaybackOperation.SetRepeat => SpotifyProviderPlaybackOperation.SetRepeat,
            SpotifyPlaybackOperation.SetShuffle => SpotifyProviderPlaybackOperation.SetShuffle,
            _ => throw new SpotifyProviderException(
                "invalid_request", "Spotify playback command is invalid."),
        },
        command.PositionMilliseconds,
        command.RepeatState switch
        {
            SpotifyRepeatState.Off => "off",
            SpotifyRepeatState.Context => "context",
            SpotifyRepeatState.Track => "track",
            null => null,
            _ => throw new SpotifyProviderException(
                "invalid_request", "Spotify repeat state is invalid."),
        },
        command.Enabled);

    private static async Task<T> BrokerCallAsync<T>(Func<Task<T>> action)
    {
        try { return await action().ConfigureAwait(false); }
        catch (BrokerException) { throw; }
        catch (SpotifyProviderException exception)
        {
            throw new BrokerException(exception.Code, exception.Message, exception);
        }
        catch (PlatformSettingsException exception)
        {
            throw new BrokerException(exception.Code, exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new BrokerException("invalid_payload", "Spotify request is invalid.", exception);
        }
    }

    private static async Task BrokerCallAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (BrokerException) { throw; }
        catch (SpotifyProviderException exception)
        {
            throw new BrokerException(exception.Code, exception.Message, exception);
        }
        catch (PlatformSettingsException exception)
        {
            throw new BrokerException(exception.Code, exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new BrokerException("invalid_payload", "Spotify request is invalid.", exception);
        }
    }

    private sealed class IntegrationState
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal SpotifyAccessToken? AccessToken { get; set; }
        internal bool IsAuthorizing { get; set; }
    }
}
