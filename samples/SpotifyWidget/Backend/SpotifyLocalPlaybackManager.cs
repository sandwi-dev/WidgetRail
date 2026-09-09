using System.Diagnostics;
using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.SpotifyPlayback;
using SpotifyLocalPlaybackState =
    WidgetRail.Samples.SpotifyWidget.SpotifyLocalPlaybackState;

namespace WidgetRail.WindowsSpotifyProvider;

internal sealed record SpotifyLocalPlaybackStartResult(
    string SpotifyDeviceId,
    SpotifyLocalPlaybackSummary Summary);

/// <summary>
/// Owns the isolated Web Playback SDK process. OAuth material and the SDK's
/// real Spotify Connect device identifier never leave this trusted assembly.
/// </summary>
internal sealed class SpotifyLocalPlaybackManager : IAsyncDisposable
{
    internal const string PublicDeviceId = "wrail-local-playback";
    internal const string DeviceName = "WidgetRail";

    private static readonly IReadOnlyCollection<string> RequiredScopes =
    [
        WindowsSpotifyPlatformBackend.StreamingScope,
        WindowsSpotifyPlatformBackend.UserReadEmailScope,
        WindowsSpotifyPlatformBackend.UserReadPrivateScope,
    ];
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(35);

    private readonly string _executablePath;
    private readonly Func<ISpotifyPlaybackHostClient> _clientFactory;
    private readonly Func<SpotifyIntegrationIdentity, IReadOnlyCollection<string>,
        CancellationToken, Task<TrustedHostSpotifyAccessToken>> _tokenProvider;
    private readonly ISpotifyRuntimeDiagnostics _runtimeDiagnostics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private ISpotifyPlaybackHostClient? _client;
    private SpotifyIntegrationIdentity? _owner;
    private SpotifyLocalPlaybackState _state;
    private string? _spotifyDeviceId;
    private int _volumePercent = 80;
    private string? _message;
    private TaskCompletionSource<string>? _ready;
    private long _startTimestamp;
    private int _disposed;

    internal SpotifyLocalPlaybackManager(
        string executablePath,
        Func<SpotifyIntegrationIdentity, IReadOnlyCollection<string>, CancellationToken,
            Task<TrustedHostSpotifyAccessToken>> tokenProvider)
        : this(executablePath, tokenProvider, () => new SpotifyPlaybackHostClient(
            SpotifyPlaybackHostClientOptions.CreateDefault(executablePath)),
            SpotifyRuntimeDiagnostics.None) { }

    internal SpotifyLocalPlaybackManager(
        string executablePath,
        Func<SpotifyIntegrationIdentity, IReadOnlyCollection<string>, CancellationToken,
            Task<TrustedHostSpotifyAccessToken>> tokenProvider,
        Func<ISpotifyPlaybackHostClient> clientFactory,
        ISpotifyRuntimeDiagnostics? runtimeDiagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = Path.GetFullPath(executablePath);
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _runtimeDiagnostics = runtimeDiagnostics ?? SpotifyRuntimeDiagnostics.None;
        _state = IsHostAvailable ? SpotifyLocalPlaybackState.Disabled :
            SpotifyLocalPlaybackState.Unavailable;
    }

    internal bool IsHostAvailable => File.Exists(_executablePath) &&
        (File.GetAttributes(_executablePath) & FileAttributes.ReparsePoint) == 0;

    internal SpotifyLocalPlaybackSummary GetSummary(SpotifyIntegrationIdentity identity)
    {
        lock (_stateGate)
        {
            if (_owner is { } owner && owner != identity)
                return new(SpotifyLocalPlaybackState.Disabled, DeviceName, null,
                    "Local playback is currently owned by another authorized widget.");
            return SummaryLocked();
        }
    }

    internal SpotifyDeviceSummary? GetPublicDevice(SpotifyIntegrationIdentity identity)
    {
        var summary = GetSummary(identity);
        if (summary.State == SpotifyLocalPlaybackState.Unavailable) return null;
        return new(PublicDeviceId, DeviceName, "Computer",
            summary.State is SpotifyLocalPlaybackState.Active or
                SpotifyLocalPlaybackState.AutoplayBlocked,
            false, true, summary.VolumePercent, true);
    }

    internal async Task<SpotifyLocalPlaybackStartResult> StartAsync(
        SpotifyIntegrationIdentity identity,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var started = Stopwatch.GetTimestamp();
        _runtimeDiagnostics.Record("local-playback-start", "admitted");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsHostAvailable)
            {
                SetState(identity, SpotifyLocalPlaybackState.Unavailable,
                    "The local Spotify playback component is not installed.");
                throw new SpotifyProviderException(
                    "platform_unavailable", "Spotify local playback is unavailable.");
            }

            lock (_stateGate)
            {
                if (_client?.IsRunning == true && _owner == identity &&
                    _state is SpotifyLocalPlaybackState.Ready or
                        SpotifyLocalPlaybackState.Active &&
                    _spotifyDeviceId is { } existingDevice)
                    return new(existingDevice, SummaryLocked());
            }

            await StopClientLockedAsync().ConfigureAwait(false);
            TrustedHostSpotifyAccessToken initialToken;
            try
            {
                initialToken = await _tokenProvider(identity, RequiredScopes, cancellationToken)
                    .ConfigureAwait(false);
                Record("local-playback-token", "acquired", started);
            }
            catch (SpotifyProviderException exception)
                when (exception.Code is "insufficient_scope" or "not_connected" or
                    "authorization_expired" or "authorization_scope_required")
            {
                SetState(identity, SpotifyLocalPlaybackState.ReauthorizationRequired,
                    "Reconnect Spotify to allow playback on this PC.");
                throw;
            }

            var client = _clientFactory();
            client.EventReceived += OnClientEvent;
            lock (_stateGate)
            {
                _client = client;
                _owner = identity;
                _spotifyDeviceId = null;
                _state = SpotifyLocalPlaybackState.Starting;
                _message = "Starting Spotify playback on this PC.";
                _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _startTimestamp = started;
            }

            try
            {
                await client.StartAsync(cancellationToken).ConfigureAwait(false);
                Record("local-playback-host", "started", started);
                var connect = client.ConnectAsync(new(
                    DeviceName,
                    _volumePercent / 100d,
                    new(true, initialToken.GrantedScopes)), cancellationToken);
                Record("local-playback-connect", "dispatched", started);
                using var readyLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _lifetime.Token);
                readyLifetime.CancelAfter(ReadyTimeout);
                var readyTask = _ready!.Task.WaitAsync(readyLifetime.Token);
                var first = await Task.WhenAny(connect, readyTask).ConfigureAwait(false);
                var commandAcknowledged = false;
                if (ReferenceEquals(first, connect))
                {
                    await connect.ConfigureAwait(false);
                    commandAcknowledged = true;
                    Record("local-playback-connect", "command-acknowledged", started);
                }
                var deviceId = await readyTask.ConfigureAwait(false);
                Record("local-playback-connect", "ready-observed", started);
                if (!commandAcknowledged)
                {
                    await connect.ConfigureAwait(false);
                    Record("local-playback-connect", "command-acknowledged", started);
                }
                Record("local-playback-sdk", "ready", started);
                SetState(identity, SpotifyLocalPlaybackState.Ready,
                    "Ready to play through this PC.", deviceId);
                return new(deviceId, GetSummary(identity));
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                Record("local-playback-start", "ready-timeout", started);
                SetState(identity, SpotifyLocalPlaybackState.Error,
                    "Spotify playback did not become ready in time.");
                await StopClientLockedAsync().ConfigureAwait(false);
                throw new SpotifyProviderException(
                    "local_playback_timeout",
                    "Spotify local playback did not become ready in time.", exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Record("local-playback-start", "caller-canceled", started);
                // The overlay can transition out of its interactive lifecycle while the
                // SDK is starting. Never retain a half-started client or a terminal-looking
                // Starting state after the caller cancels the broker operation.
                await StopClientLockedAsync().ConfigureAwait(false);
                SetState(null, IsHostAvailable ? SpotifyLocalPlaybackState.Disabled :
                    SpotifyLocalPlaybackState.Unavailable, null);
                throw;
            }
            catch (SpotifyPlaybackHostClientException exception)
            {
                Record("local-playback-start", exception.Code, started);
                var state = GetSummary(identity).State;
                if (state is SpotifyLocalPlaybackState.Starting or
                    SpotifyLocalPlaybackState.Ready)
                    SetState(identity, SpotifyLocalPlaybackState.Error,
                        "Spotify local playback could not be started.");
                await StopClientLockedAsync().ConfigureAwait(false);
                var code = state switch
                {
                    SpotifyLocalPlaybackState.PremiumRequired => "premium_required",
                    SpotifyLocalPlaybackState.ReauthorizationRequired =>
                        "reauthorization_required",
                    _ => exception.Code,
                };
                throw new SpotifyProviderException(
                    code, GetSummary(identity).DisplayMessage ??
                        "Spotify local playback could not be started.", exception);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Record("local-playback-start", "unexpected-failure", started);
                SetState(identity, SpotifyLocalPlaybackState.Error,
                    "Spotify local playback could not be started.");
                await StopClientLockedAsync().ConfigureAwait(false);
                throw new SpotifyProviderException(
                    "local_playback_failed",
                    "Spotify local playback could not be started.", exception);
            }
        }
        finally { _gate.Release(); }
    }

    internal void MarkActive(SpotifyIntegrationIdentity identity) =>
        SetState(identity, SpotifyLocalPlaybackState.Active,
            "Playing through this PC.");

    internal async Task StopAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_owner is { } owner && owner != identity) return;
            await StopClientLockedAsync().ConfigureAwait(false);
            SetState(null, IsHostAvailable ? SpotifyLocalPlaybackState.Disabled :
                SpotifyLocalPlaybackState.Unavailable, null);
        }
        finally { _gate.Release(); }
    }

    internal async Task SetVolumeAsync(
        SpotifyIntegrationIdentity identity, int volumePercent,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (volumePercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volumePercent));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = RequireOwnedClient(identity);
            await client.SendAsync("set_volume", new { volume = volumePercent / 100d },
                cancellationToken).ConfigureAwait(false);
            lock (_stateGate) _volumePercent = volumePercent;
        }
        catch (SpotifyPlaybackHostClientException exception)
        {
            throw new SpotifyProviderException(
                exception.Code, "Spotify local playback volume could not be changed.", exception);
        }
        finally { _gate.Release(); }
    }

    internal string? GetSpotifyDeviceId(SpotifyIntegrationIdentity identity)
    {
        lock (_stateGate)
            return _owner == identity ? _spotifyDeviceId : null;
    }

    internal async Task<bool> TryControlAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyProviderPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        ISpotifyPlaybackHostClient? client;
        lock (_stateGate)
            client = _owner == identity && _state is SpotifyLocalPlaybackState.Active or
                SpotifyLocalPlaybackState.AutoplayBlocked ? _client : null;
        if (client?.IsRunning != true) return false;

        // resume()/pause() resolve when the Web Playback SDK accepts the call,
        // not when its player state changes. Keep Spotify's Web API as the
        // authoritative play/pause command boundary for the active device.
        if (command.Operation is SpotifyProviderPlaybackOperation.Play or
                SpotifyProviderPlaybackOperation.Pause)
        {
            _runtimeDiagnostics.Record(
                "local-playback-control",
                command.Operation == SpotifyProviderPlaybackOperation.Play
                    ? "web-api-play"
                    : "web-api-pause");
            return false;
        }

        string? operation = command.Operation switch
        {
            SpotifyProviderPlaybackOperation.Next => "next_track",
            SpotifyProviderPlaybackOperation.Previous => "previous_track",
            SpotifyProviderPlaybackOperation.Seek => "seek",
            _ => null,
        };
        if (operation is null) return false;
        var payload = operation == "seek"
            ? new { positionMilliseconds = command.PositionMilliseconds }
            : (object)new { };
        try
        {
            await client.SendAsync(operation, payload, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SpotifyPlaybackHostClientException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await StopClientLockedAsync().ConfigureAwait(false); }
        finally
        {
            _gate.Release();
            _gate.Dispose();
            _lifetime.Dispose();
        }
    }

    private void OnClientEvent(object? sender, SpotifyPlaybackEventEnvelope value)
    {
        long started;
        lock (_stateGate)
        {
            if (!ReferenceEquals(sender, _client)) return;
            started = _startTimestamp;
        }
        switch (value.Type)
        {
            case "token_requested":
                RecordIfStarted("local-playback-token", "requested", started);
                _ = AnswerTokenRequestAsync(
                    sender as ISpotifyPlaybackHostClient, value, started);
                break;
            case "connect_succeeded":
                RecordIfStarted(
                    "local-playback-connect", "promise-succeeded", started);
                break;
            case "ready":
                RecordIfStarted("local-playback-sdk", "ready-event", started);
                var ready = SpotifyPlaybackProtocolCodec.DecodePayload<PageDevice>(value.Payload);
                lock (_stateGate)
                {
                    if (!ReferenceEquals(sender, _client) ||
                        started != _startTimestamp) return;
                    _spotifyDeviceId = ready.DeviceId;
                    _state = SpotifyLocalPlaybackState.Ready;
                    _message = "Ready to play through this PC.";
                    _ready?.TrySetResult(ready.DeviceId);
                }
                break;
            case "not_ready":
                RecordIfStarted("local-playback-sdk", "not-ready-event", started);
                lock (_stateGate)
                {
                    if (!ReferenceEquals(sender, _client) ||
                        started != _startTimestamp) return;
                    _spotifyDeviceId = null;
                    _state = SpotifyLocalPlaybackState.NotReady;
                    _message = "Spotify temporarily disconnected this PC.";
                }
                break;
            case "player_state_changed":
                var playback = SpotifyPlaybackProtocolCodec
                    .DecodePayload<WidgetRail.SpotifyPlayback.SpotifyLocalPlaybackState>(
                        value.Payload);
                RecordIfStarted("local-playback-state",
                    playback.IsAvailable
                        ? playback.Paused ? "available-paused" : "available-playing"
                        : "unavailable", started);
                if (playback.IsAvailable)
                    lock (_stateGate)
                    {
                        if (!ReferenceEquals(sender, _client) ||
                            started != _startTimestamp) return;
                        if (!playback.Paused)
                        {
                            _state = SpotifyLocalPlaybackState.Active;
                            _message = "Spotify reported playback on this PC.";
                        }
                        else if (_state != SpotifyLocalPlaybackState.AutoplayBlocked)
                        {
                            _state = SpotifyLocalPlaybackState.Active;
                            _message = "Paused on this PC.";
                        }
                    }
                break;
            case "autoplay_policy":
                var policy = SpotifyPlaybackProtocolCodec
                    .DecodePayload<SpotifyAutoplayPolicyDiagnostic>(value.Payload);
                RecordAutoplayPolicy(policy, started);
                break;
            case "autoplay_permission":
                var permission = SpotifyPlaybackProtocolCodec
                    .DecodePayload<SpotifyAutoplayPermissionDiagnostic>(value.Payload);
                permission.Validate();
                RecordIfStarted("autoplay-permission",
                    permission.OriginClass + '-' +
                    (permission.IsUserInitiated ? "user" : "not-user") + '-' +
                    permission.Decision, started);
                break;
            case "autoplay_failed":
                RecordIfStarted("local-playback-sdk", "autoplay-failed", started);
                lock (_stateGate)
                {
                    if (!ReferenceEquals(sender, _client) ||
                        started != _startTimestamp) return;
                    _state = SpotifyLocalPlaybackState.AutoplayBlocked;
                    _message = "Spotify audio was blocked by browser autoplay policy. " +
                        "Stop local playback or try Play again.";
                }
                break;
            case "sdk_error":
                var error = SpotifyPlaybackProtocolCodec.DecodePayload<PageError>(value.Payload);
                RecordIfStarted("local-playback-sdk", error.Code, started);
                var state = error.Code switch
                {
                    "account_error" => SpotifyLocalPlaybackState.PremiumRequired,
                    "authentication_error" => SpotifyLocalPlaybackState.ReauthorizationRequired,
                    _ => SpotifyLocalPlaybackState.Error,
                };
                var message = state switch
                {
                    SpotifyLocalPlaybackState.PremiumRequired =>
                        "Spotify Premium is required for playback on this PC.",
                    SpotifyLocalPlaybackState.ReauthorizationRequired =>
                        "Reconnect Spotify to resume playback on this PC.",
                    _ => "Spotify local playback reported an error.",
                };
                lock (_stateGate)
                {
                    if (!ReferenceEquals(sender, _client) ||
                        started != _startTimestamp) return;
                    _ready?.TrySetException(
                        new SpotifyPlaybackHostClientException(error.Code, message));
                    _state = state;
                    _message = message;
                }
                break;
        }
    }

    private async Task AnswerTokenRequestAsync(
        ISpotifyPlaybackHostClient? source,
        SpotifyPlaybackEventEnvelope value,
        long started)
    {
        SpotifyIntegrationIdentity? owner = null;
        try
        {
            var request = SpotifyPlaybackProtocolCodec.DecodePayload<PageTokenRequest>(value.Payload);
            lock (_stateGate) owner = ReferenceEquals(source, _client) ? _owner : null;
            if (source is null || owner is null) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var token = await _tokenProvider(owner.Value, RequiredScopes, timeout.Token)
                .ConfigureAwait(false);
            lock (_stateGate)
                if (!ReferenceEquals(source, _client) || owner != _owner ||
                    started == 0 || started != _startTimestamp) return;
            Record("local-playback-token", "refresh-acquired", started);
            await source.ProvideTokenAsync(request.TokenRequestId, token, timeout.Token)
                .ConfigureAwait(false);
            lock (_stateGate)
                if (!ReferenceEquals(source, _client) || owner != _owner ||
                    started != _startTimestamp) return;
            Record("local-playback-token", "callback-acknowledged", started);
        }
        catch (Exception exception) when (exception is SpotifyProviderException or
            SpotifyPlaybackHostClientException or SpotifyPlaybackProtocolException or
            OperationCanceledException)
        {
            if (source is not { } exactSource || owner is not { } exactOwner) return;
            lock (_stateGate)
            {
                if (!ReferenceEquals(exactSource, _client) || exactOwner != _owner ||
                    started == 0 || started != _startTimestamp) return;
                _state = SpotifyLocalPlaybackState.ReauthorizationRequired;
                _message = "Spotify could not renew local playback authorization.";
                _ready?.TrySetException(new SpotifyPlaybackHostClientException(
                    "reauthorization_required", _message));
            }
            Record("local-playback-token", exception switch
            {
                SpotifyProviderException provider => provider.Code,
                SpotifyPlaybackHostClientException host => host.Code,
                _ => SpotifyRuntimeDiagnostics.Code(exception),
            }, started);
            _ = Task.Run(() => StopFailedClientAsync(exactSource, exactOwner));
        }
    }

    private async Task StopFailedClientAsync(
        ISpotifyPlaybackHostClient source,
        SpotifyIntegrationIdentity owner)
    {
        try
        {
            await _gate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                lock (_stateGate)
                    if (!ReferenceEquals(source, _client) || _owner != owner) return;
                await StopClientLockedAsync().ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
    }

    private ISpotifyPlaybackHostClient RequireOwnedClient(SpotifyIntegrationIdentity identity)
    {
        lock (_stateGate)
        {
            if (_owner != identity || _client?.IsRunning != true)
                throw new SpotifyProviderException(
                    "local_playback_not_started",
                    "Start Spotify playback on this PC before changing it.");
            return _client;
        }
    }

    private async Task StopClientLockedAsync()
    {
        ISpotifyPlaybackHostClient? client;
        lock (_stateGate)
        {
            client = _client;
            _client = null;
            _spotifyDeviceId = null;
            _ready?.TrySetCanceled();
            _ready = null;
            _startTimestamp = 0;
        }
        if (client is null) return;
        client.EventReceived -= OnClientEvent;
        await client.DisposeAsync().ConfigureAwait(false);
    }

    private void SetState(
        SpotifyIntegrationIdentity? owner,
        SpotifyLocalPlaybackState state,
        string? message,
        string? spotifyDeviceId = null)
    {
        lock (_stateGate)
        {
            _owner = owner;
            _state = state;
            _message = message;
            if (spotifyDeviceId is not null) _spotifyDeviceId = spotifyDeviceId;
        }
    }

    private SpotifyLocalPlaybackSummary SummaryLocked() =>
        new(_state, DeviceName,
            _state is SpotifyLocalPlaybackState.Disabled or
                SpotifyLocalPlaybackState.Unavailable ? null : _volumePercent,
            _message);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private void Record(string boundary, string code, long started) =>
        _runtimeDiagnostics.Record(
            boundary, code, elapsedMilliseconds: Math.Max(
                0, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));

    private void RecordIfStarted(string boundary, string code, long started)
    {
        if (started != 0) Record(boundary, code, started);
    }

    private void RecordAutoplayPolicy(
        SpotifyAutoplayPolicyDiagnostic value, long started)
    {
        value.Validate();
        if (started == 0) return;
        var elapsed = Math.Max(
            0, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        _runtimeDiagnostics.Record(
            "autoplay-policy", value.Stage,
            value.FrameCount, value.ExactSdkFrameCount, elapsed);
        if (value.FrameCountCapped)
            _runtimeDiagnostics.Record(
                "autoplay-policy", "frame-count-capped",
                value.FrameCount, value.ExactSdkFrameCount, elapsed);
        _runtimeDiagnostics.Record(
            "autoplay-allow", "autoplay-" + value.AllowAutoplay,
            elapsedMilliseconds: elapsed);
        _runtimeDiagnostics.Record(
            "autoplay-allow", "encrypted-media-" + value.AllowEncryptedMedia,
            elapsedMilliseconds: elapsed);
        _runtimeDiagnostics.Record(
            "autoplay-parent-policy",
            PolicyCode("autoplay", value.ParentPolicyState,
                value.ParentAllowsAutoplay), elapsedMilliseconds: elapsed);
        _runtimeDiagnostics.Record(
            "autoplay-parent-policy",
            PolicyCode("encrypted-media", value.ParentPolicyState,
                value.ParentAllowsEncryptedMedia), elapsedMilliseconds: elapsed);
        _runtimeDiagnostics.Record(
            "autoplay-frame-policy",
            PolicyCode("autoplay", value.FramePolicyState,
                value.FrameAllowsAutoplay), elapsedMilliseconds: elapsed);
        _runtimeDiagnostics.Record(
            "autoplay-frame-policy",
            PolicyCode("encrypted-media", value.FramePolicyState,
                value.FrameAllowsEncryptedMedia), elapsedMilliseconds: elapsed);
    }

    private static string PolicyCode(string feature, string state, bool? allowed) =>
        state == "supported"
            ? feature + "-" + (allowed == true ? "allowed" : "denied")
            : feature + "-" + state;

    private sealed record PageDevice(string DeviceId);
    private sealed record PageError(string Code, string Message);
    private sealed record PageTokenRequest(string TokenRequestId);
}
