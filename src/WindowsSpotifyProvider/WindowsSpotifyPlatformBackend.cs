using System.Collections.Concurrent;
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
    private static readonly Uri RedirectUri = new(ExactRedirectUri, UriKind.Absolute);
    private static readonly Uri AuthorizeUri = new("https://accounts.spotify.com/authorize");
    private static readonly Uri TokenUri = new("https://accounts.spotify.com/api/token");
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
    private readonly SpotifyHttpPolicy _httpPolicy;
    private readonly TimeProvider _time;
    private readonly SpotifyLocalPlaybackManager _localPlayback;
    private readonly SpotifyPlaybackApi _playbackApi;
    private readonly SpotifyCollectionApi _collectionApi;
    private readonly ConcurrentDictionary<string, IntegrationState> _states = new();
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
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _httpPolicy = new SpotifyHttpPolicy(
            _http, delay ?? throw new ArgumentNullException(nameof(delay)), _time);
        _localPlayback = localPlayback ?? new SpotifyLocalPlaybackManager(
            DefaultPlaybackHostPath(), AcquireTrustedHostAccessTokenAsync);
        var authorizedSender = new SpotifyAuthorizedRequestSender(
            SendAuthorizedRequestAsync);
        _playbackApi = new SpotifyPlaybackApi(authorizedSender);
        _collectionApi = new SpotifyCollectionApi(authorizedSender);
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
        => await _playbackApi.GetPlaybackAsync(identity, cancellationToken)
            .ConfigureAwait(false);

    public async Task ControlPlaybackAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyProviderPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await _localPlayback.TryControlAsync(identity, command, cancellationToken)
                .ConfigureAwait(false))
            return;
        await _playbackApi.ControlPlaybackAsync(identity, command, cancellationToken)
            .ConfigureAwait(false);
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
            var providerIdentity = ToProviderIdentity(identity);
            var devices = await _playbackApi.GetDevicesAsync(
                providerIdentity, cancellationToken).ConfigureAwait(false);
            return MergeLocalDevice(providerIdentity, devices.Devices);
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
            await _playbackApi.TransferPlaybackAsync(
                ToProviderIdentity(identity), request.DeviceId,
                request.ContinuePlaying, cancellationToken).ConfigureAwait(false);
        });

    public Task<SpotifyQueueSummary> GetSpotifyQueueAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        BrokerCallAsync(async () =>
        {
            return await _playbackApi.GetQueueAsync(
                ToProviderIdentity(identity), cancellationToken).ConfigureAwait(false);
        });

    public Task AddSpotifyQueueItemAsync(
        BrokerWidgetIdentity identity, AddSpotifyQueueItemRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            SpotifyApiValidation.ValidateSpotifyUri(request.Uri);
            var providerIdentity = ToProviderIdentity(identity);
            var deviceId = await ResolveDeviceIdAsync(
                providerIdentity, request.DeviceId, cancellationToken).ConfigureAwait(false);
            await _playbackApi.AddQueueItemAsync(
                providerIdentity, request.Uri, deviceId, cancellationToken)
                .ConfigureAwait(false);
        });

    public Task StartSpotifyPlaybackAsync(
        BrokerWidgetIdentity identity, StartSpotifyPlaybackRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            SpotifyApiValidation.ValidateStartPlayback(
                request.DeviceId, request.ContextUri, request.ItemUris,
                request.Offset, request.OffsetUri);
            var providerIdentity = ToProviderIdentity(identity);
            var requestedLocalHost =
                request.DeviceId == SpotifyLocalPlaybackManager.PublicDeviceId;
            var deviceId = await ResolveDeviceIdAsync(
                providerIdentity, request.DeviceId, cancellationToken).ConfigureAwait(false);
            await _playbackApi.StartPlaybackAsync(
                providerIdentity, deviceId, request.ContextUri, request.ItemUris,
                request.Offset, request.OffsetUri, cancellationToken).ConfigureAwait(false);
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
            return await _collectionApi.GetPlaylistsAsync(
                ToProviderIdentity(identity), request.Offset, request.Limit,
                cancellationToken).ConfigureAwait(false);
        });

    public Task<SpotifyPlaylistItemsSummary> GetSpotifyPlaylistItemsAsync(
        BrokerWidgetIdentity identity, SpotifyPlaylistItemsRequest request,
        CancellationToken cancellationToken) => BrokerCallAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return await _collectionApi.GetPlaylistItemsAsync(
                ToProviderIdentity(identity), request.PlaylistId,
                request.Offset, request.Limit, cancellationToken).ConfigureAwait(false);
        });

    private async Task StartAndTransferLocalPlaybackAsync(
        SpotifyIntegrationIdentity identity,
        bool continuePlaying,
        CancellationToken cancellationToken)
    {
        var started = await _localPlayback.StartAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        await _playbackApi.TransferPlaybackAsync(
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

    private SpotifyDevicesSummary MergeLocalDevice(
        SpotifyIntegrationIdentity identity,
        IReadOnlyList<SpotifyDeviceSummary> devices)
    {
        var local = _localPlayback.GetPublicDevice(identity);
        if (local is null) return new SpotifyDevicesSummary(devices.ToArray());
        return new SpotifyDevicesSummary(
            devices.Take(SpotifyPlaybackApi.MaximumDevices - 1).Append(local).ToArray());
    }

    private Task<SpotifyHttpResponse> SendAuthorizedRequestAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyAuthorizedRequest request,
        CancellationToken cancellationToken) => SendPlayerRequestAsync(
            identity,
            request.Method,
            request.Uri,
            request.RequiredScope,
            cancellationToken,
            request.JsonBody,
            request.AdditionalRequiredScope);

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
            var response = await _httpPolicy.SendAsync(
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
            return await _httpPolicy.SendAsync(
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
        var response = await _httpPolicy.SendAsync(
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
        var response = await _httpPolicy.SendAsync(
            new SpotifyHttpRequest(HttpMethod.Post, TokenUri, FormBody: form), cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == 400 && ReadTokenError(response.Body) == "invalid_grant")
            throw new SpotifyProviderException(
                "authorization_expired", "Spotify authorization expired. Connect again.");
        return ParseTokenResponse(response, previouslyGrantedScopes);
    }

    private static SpotifyTokenResponse ParseTokenResponse(
        SpotifyHttpResponse response, IReadOnlySet<string> scopesWhenOmitted)
    {
        SpotifyResponseParser.DemandBoundedBody(response.Body);
        SpotifyResponseParser.EnsureSuccess(response);
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

    private static string? ReadTokenError(string body)
    {
        SpotifyResponseParser.DemandBoundedBody(body);
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
