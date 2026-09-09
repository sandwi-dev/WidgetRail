using System.Text;
using System.Text.Json;
using WidgetRail.SpotifyPlayback;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WidgetRail.SpotifyPlaybackHost;

internal sealed class SpotifyPlaybackHostForm : Form
{
    private const int WsExNoActivate = 0x08000000;

    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly EphemeralUserDataDirectory _userData = new();
    private readonly SpotifyPlaybackStateMachine _lifecycle = new();
    private readonly HashSet<string> _pendingTokenRequests = new(StringComparer.Ordinal);
    private readonly PendingPageResponses _pendingPageResponses = new();
    private readonly System.Windows.Forms.Timer _sdkLoadTimeout = new() { Interval = 30_000 };
    private readonly Action<SpotifyPlaybackEvent> _emit;
    private bool _initialized;

    internal SpotifyPlaybackHostForm(Action<SpotifyPlaybackEvent> emit)
    {
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1, 1);
        FormBorderStyle = FormBorderStyle.None;
        Location = new Point(-32_000, -32_000);
        Opacity = 0;
        ShowInTaskbar = false;
        Text = string.Empty;
        Controls.Add(_webView);
        _sdkLoadTimeout.Tick += (_, _) => HandleSdkLoadTimeout();
        Shown += async (_, _) => await InitializeAsync().ConfigureAwait(true);
    }

    // This process is an audio engine, not an interactive application surface. A normal
    // WinForms top-level window can briefly become foreground while Application.Run shows
    // it, even when it is transparent and off-screen. That foreground transition closes
    // the overlay and cancels the start request, so enforce both WinForms' no-activation
    // path and the native extended style.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate;
            return parameters;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sdkLoadTimeout.Stop();
            _sdkLoadTimeout.Dispose();
            _webView.Dispose();
            _userData.Dispose();
        }
        base.Dispose(disposing);
    }

    internal void HandleRequest(SpotifyPlaybackRequest request)
    {
        try
        {
            if (!_initialized && request.Type != "shutdown")
                throw new SpotifyPlaybackProtocolException(
                    "host_not_ready", "The Spotify playback host is still initializing.");
            switch (request.Type)
            {
                case "connect":
                    var connect = SpotifyPlaybackProtocolCodec
                        .DecodePayload<SpotifyPlaybackConnectOptions>(request);
                    connect.Validate();
                    Transition(SpotifyPlaybackSignal.ConnectRequested);
                    SendToPage(request, connect);
                    break;
                case "provide_token":
                    HandleToken(request);
                    break;
                case "disconnect":
                    RequireEmpty(request);
                    Transition(SpotifyPlaybackSignal.DisconnectRequested);
                    _pendingTokenRequests.Clear();
                    _pendingPageResponses.Clear();
                    SendToPage(request, new { });
                    break;
                case "pause":
                case "resume":
                case "toggle_play":
                case "previous_track":
                case "next_track":
                case "get_current_state":
                case "get_volume":
                    RequireEmpty(request);
                    SendToPage(request, new { });
                    break;
                case "seek":
                    var seek = SpotifyPlaybackProtocolCodec.DecodePayload<SeekPayload>(request);
                    if (seek.PositionMilliseconds is < 0 or > SpotifyPlaybackProtocol.MaximumSeekMilliseconds)
                        throw new SpotifyPlaybackProtocolException(
                            "invalid_position", "The Spotify seek position is invalid.");
                    SendToPage(request, seek);
                    break;
                case "set_name":
                    var name = SpotifyPlaybackProtocolCodec.DecodePayload<NamePayload>(request);
                    new SpotifyPlaybackConnectOptions(name.Name, 1,
                        new(true, SpotifyPlaybackProtocol.RequiredScopes)).Validate();
                    SendToPage(request, name);
                    break;
                case "set_volume":
                    var volume = SpotifyPlaybackProtocolCodec.DecodePayload<VolumePayload>(request);
                    if (!double.IsFinite(volume.Volume) || volume.Volume is < 0 or > 1)
                        throw new SpotifyPlaybackProtocolException(
                            "invalid_volume", "The Spotify playback volume is invalid.");
                    SendToPage(request, volume);
                    break;
                case "shutdown":
                    RequireEmpty(request);
                    Transition(SpotifyPlaybackSignal.Shutdown);
                    Emit("command_completed", request.RequestId, new { });
                    Close();
                    break;
                default:
                    throw new SpotifyPlaybackProtocolException(
                        "unknown_command", "The Spotify playback-host command is unknown.");
            }
        }
        catch (SpotifyPlaybackProtocolException exception)
        {
            Emit("command_failed", request.RequestId,
                new { code = exception.Code, message = exception.Message });
        }
        catch (Exception)
        {
            Emit("command_failed", request.RequestId,
                new { code = "host_failure", message = "The Spotify playback host failed safely." });
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _userData.RootPath).ConfigureAwait(true);
            await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            var core = _webView.CoreWebView2;
            Harden(core);
            core.AddWebResourceRequestedFilter(
                SpotifyPlaybackPage.TopLevelUri,
                CoreWebView2WebResourceContext.Document);
            core.WebResourceRequested += (_, args) =>
            {
                if (!string.Equals(args.Request.Uri, SpotifyPlaybackPage.TopLevelUri,
                        StringComparison.Ordinal))
                    return;
                args.Response = core.Environment.CreateWebResourceResponse(
                    new MemoryStream(Encoding.UTF8.GetBytes(SpotifyPlaybackPage.Html),
                        writable: false),
                    200,
                    "OK",
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    "Cache-Control: no-store\r\n" +
                    "X-Content-Type-Options: nosniff");
            };
            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationStarting += (_, args) =>
                args.Cancel = !string.Equals(args.Uri, SpotifyPlaybackPage.TopLevelUri,
                    StringComparison.Ordinal);
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) =>
            {
                args.Cancel = true;
                args.Handled = true;
            };
            core.PermissionRequested += (_, args) =>
            {
                args.State = args.PermissionKind == CoreWebView2PermissionKind.Autoplay
                    ? CoreWebView2PermissionState.Allow
                    : CoreWebView2PermissionState.Deny;
                args.SavesInProfile = false;
                args.Handled = true;
            };
            core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
            core.ServerCertificateErrorDetected += (_, args) =>
                args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            core.ProcessFailed += (_, _) =>
            {
                Transition(SpotifyPlaybackSignal.InitializationError);
                Emit("sdk_error", null, new
                {
                    code = "webview_process_failed",
                    message = "The secure Spotify playback process stopped unexpectedly."
                });
                Close();
            };
            _initialized = true;
            _sdkLoadTimeout.Start();
            core.Navigate(SpotifyPlaybackPage.TopLevelUri);
            Emit("host_initialized", null, new { });
        }
        catch (Exception)
        {
            Transition(SpotifyPlaybackSignal.InitializationError);
            Emit("sdk_error", null, new
            {
                code = "host_initialization_error",
                message = "The secure Spotify playback surface could not be initialized."
            });
            Close();
        }
    }

    private static void Harden(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsReputationCheckingRequired = true;
        settings.IsStatusBarEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsZoomControlEnabled = false;
    }

    private void HandleToken(SpotifyPlaybackRequest request)
    {
        var payload = SpotifyPlaybackProtocolCodec.DecodePayload<TokenPayload>(request);
        var tokenRequestId = payload.TokenRequestId;
        if (string.IsNullOrEmpty(tokenRequestId) ||
            tokenRequestId.Length > SpotifyPlaybackProtocol.MaximumRequestIdCharacters ||
            tokenRequestId.Any(character => character is < '!' or > '~'))
            throw new SpotifyPlaybackProtocolException(
                "invalid_token_request", "The Spotify token request identifier is invalid.");
        if (!_pendingTokenRequests.Contains(tokenRequestId))
            throw new SpotifyPlaybackProtocolException(
                "unknown_token_request", "Spotify did not request this access token.");
        if (payload.GrantedScopes is null)
            throw new SpotifyPlaybackProtocolException(
                "invalid_scopes", "The granted Spotify scopes are invalid.");
        if (payload.AccessToken is null)
            throw new SpotifyPlaybackProtocolException(
                "invalid_token", "The Spotify access-token lease is invalid.");
        var lease = new SpotifyAccessTokenLease(payload.AccessToken,
            DateTimeOffset.FromUnixTimeMilliseconds(payload.ExpiresAtUnixMilliseconds),
            payload.GrantedScopes);
        lease.Validate(DateTimeOffset.UtcNow);
        _pendingTokenRequests.Remove(tokenRequestId);
        SendToPage(request, new
        {
            TokenRequestId = tokenRequestId,
            lease.AccessToken,
        });
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!SpotifyPlaybackPageValidator.IsTrustedSource(args.Source)) return;
        string json;
        try { json = args.WebMessageAsJson; }
        catch (Exception) { return; }
        if (json.Length > SpotifyPlaybackProtocol.MaximumMessageCharacters) return;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.GetProperty("version").GetInt32() != SpotifyPlaybackProtocol.Version)
                return;
            var type = root.GetProperty("type").GetString();
            if (string.IsNullOrEmpty(type) || type.Length > 32) return;
            var requestId = root.TryGetProperty("requestId", out var requestElement) &&
                requestElement.ValueKind == JsonValueKind.String
                ? requestElement.GetString() : null;
            var payload = root.TryGetProperty("payload", out var payloadElement)
                ? payloadElement.Clone() : JsonSerializer.SerializeToElement(new { });
            object normalized;
            switch (type)
            {
                case "sdk_loaded":
                    _sdkLoadTimeout.Stop();
                    Transition(SpotifyPlaybackSignal.SdkLoaded);
                    normalized = new { };
                    break;
                case "connect_succeeded":
                    normalized = new { };
                    break;
                case "ready":
                    Transition(SpotifyPlaybackSignal.Ready);
                    normalized = ValidateDevice(payload);
                    break;
                case "not_ready":
                    Transition(SpotifyPlaybackSignal.NotReady);
                    normalized = ValidateDevice(payload);
                    break;
                case "disconnected":
                    Transition(SpotifyPlaybackSignal.Disconnected);
                    normalized = new { };
                    break;
                case "autoplay_failed":
                    Transition(SpotifyPlaybackSignal.AutoplayFailed);
                    normalized = new { };
                    break;
                case "sdk_error":
                    normalized = ValidateError(payload);
                    ApplyErrorSignal((PageError)normalized);
                    break;
                case "command_completed":
                    ValidateRequestId(requestId);
                    RequireExpectedPageResponse(requestId!, type);
                    normalized = new { };
                    break;
                case "command_failed":
                    ValidateRequestId(requestId);
                    normalized = ValidateCommandFailure(payload);
                    RequireExpectedPageResponse(requestId!, type);
                    break;
                case "token_requested":
                    normalized = RegisterTokenRequest(payload);
                    break;
                case "player_state_changed":
                    normalized = ValidatePlaybackState(payload);
                    break;
                case "current_state":
                    ValidateRequestId(requestId);
                    normalized = ValidatePlaybackState(payload);
                    RequireExpectedPageResponse(requestId!, type);
                    break;
                case "volume":
                    ValidateRequestId(requestId);
                    normalized = ValidateVolume(payload);
                    RequireExpectedPageResponse(requestId!, type);
                    break;
                default: return;
            }
            Emit(type, requestId, normalized);
        }
        catch (Exception) { }
    }

    private static PageDevice ValidateDevice(JsonElement payload)
    {
        var value = SpotifyPlaybackProtocolCodec.DecodePayload<PageDevice>(payload);
        if (string.IsNullOrWhiteSpace(value.DeviceId) || value.DeviceId.Length > 128 ||
            value.DeviceId.Any(character => character is < '!' or > '~'))
            throw new SpotifyPlaybackProtocolException(
                "invalid_page_event", "The Spotify SDK device event is invalid.");
        return value;
    }

    private static PageError ValidateError(JsonElement payload)
    {
        var value = SpotifyPlaybackProtocolCodec.DecodePayload<PageError>(payload);
        if (value.Code is not ("initialization_error" or "authentication_error" or
            "account_error" or "playback_error") || value.Message is null ||
            value.Message.Length > 512 || value.Message.Any(char.IsControl))
            throw new SpotifyPlaybackProtocolException(
                "invalid_page_event", "The Spotify SDK error event is invalid.");
        return value;
    }

    private static PageCommandFailure ValidateCommandFailure(JsonElement payload)
    {
        var value = SpotifyPlaybackProtocolCodec.DecodePayload<PageCommandFailure>(payload);
        if (string.IsNullOrEmpty(value.Code) || value.Code.Length > 64 ||
            value.Code.Any(character => character is < '!' or > '~'))
            throw new SpotifyPlaybackProtocolException(
                "invalid_page_event", "The Spotify SDK command event is invalid.");
        return value;
    }

    private PageTokenRequest RegisterTokenRequest(JsonElement payload)
    {
        var value = SpotifyPlaybackProtocolCodec.DecodePayload<PageTokenRequest>(payload);
        ValidateRequestId(value.TokenRequestId);
        if (_pendingTokenRequests.Count >= 8 || !_pendingTokenRequests.Add(value.TokenRequestId))
            throw new SpotifyPlaybackProtocolException(
                "invalid_page_event", "The Spotify SDK token event is invalid.");
        return value;
    }

    private static SpotifyLocalPlaybackState ValidatePlaybackState(JsonElement payload)
    {
        var value = SpotifyPlaybackProtocolCodec.DecodePayload<SpotifyLocalPlaybackState>(payload);
        return SpotifyPlaybackPageValidator.ValidatePlaybackState(value);
    }

    private static PageVolume ValidateVolume(JsonElement payload)
    {
        var value = SpotifyPlaybackProtocolCodec.DecodePayload<PageVolume>(payload);
        if (!double.IsFinite(value.Volume) || value.Volume is < 0 or > 1)
            throw new SpotifyPlaybackProtocolException(
                "invalid_page_event", "The Spotify SDK volume event is invalid.");
        return value;
    }

    private static void ValidateRequestId(string? requestId)
    {
        if (string.IsNullOrEmpty(requestId) ||
            requestId.Length > SpotifyPlaybackProtocol.MaximumRequestIdCharacters ||
            requestId.Any(character => character is < '!' or > '~'))
            throw new SpotifyPlaybackProtocolException(
                "invalid_page_event", "The Spotify SDK request identifier is invalid.");
    }

    private void ApplyErrorSignal(PageError error)
    {
        Transition(error.Code switch
        {
            "initialization_error" => SpotifyPlaybackSignal.InitializationError,
            "authentication_error" => SpotifyPlaybackSignal.AuthenticationError,
            "account_error" => SpotifyPlaybackSignal.AccountError,
            _ => SpotifyPlaybackSignal.PlaybackError,
        });
    }

    private void SendToPage(SpotifyPlaybackRequest request, object payload)
    {
        var expectedType = request.Type switch
        {
            "get_current_state" => "current_state",
            "get_volume" => "volume",
            _ => "command_completed",
        };
        var message = JsonSerializer.Serialize(new
        {
            version = SpotifyPlaybackProtocol.Version,
            requestId = request.RequestId,
            type = request.Type,
            payload,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        _pendingPageResponses.Register(request.RequestId, expectedType);
        try { _webView.CoreWebView2.PostWebMessageAsJson(message); }
        catch
        {
            _pendingPageResponses.Cancel(request.RequestId);
            throw;
        }
    }

    private void RequireExpectedPageResponse(string requestId, string type)
    {
        if (!_pendingPageResponses.TryConsume(requestId, type))
            throw new SpotifyPlaybackProtocolException(
                "unexpected_page_response", "The Spotify SDK response was not expected.");
    }

    private void HandleSdkLoadTimeout()
    {
        _sdkLoadTimeout.Stop();
        if (_lifecycle.State != SpotifyPlaybackLifecycleState.LoadingSdk) return;
        Transition(SpotifyPlaybackSignal.InitializationError);
        Emit("sdk_error", null, new
        {
            code = "sdk_load_timeout",
            message = "The Spotify playback SDK did not load in time."
        });
        Close();
    }

    private static void RequireEmpty(SpotifyPlaybackRequest request)
    {
        if (request.Payload.EnumerateObject().Any())
            throw new SpotifyPlaybackProtocolException(
                "invalid_payload", "This playback-host command requires an empty payload.");
    }

    private void Transition(SpotifyPlaybackSignal signal)
    {
        var transition = _lifecycle.Apply(signal);
        if (transition.Changed)
            Emit("lifecycle_changed", null, new
            {
                previous = transition.Previous.ToString(),
                current = transition.Current.ToString(),
                signal = signal.ToString(),
            });
    }

    private void Emit(string type, string? requestId, object payload) =>
        _emit(new(SpotifyPlaybackProtocol.Version, type, requestId, payload));

    private sealed record SeekPayload(long PositionMilliseconds);
    private sealed record NamePayload(string Name);
    private sealed record VolumePayload(double Volume);
    private sealed record TokenPayload(
        string? TokenRequestId,
        string? AccessToken,
        long ExpiresAtUnixMilliseconds,
        IReadOnlyList<string>? GrantedScopes);
    private sealed record PageDevice(string DeviceId);
    private sealed record PageError(string Code, string Message);
    private sealed record PageCommandFailure(string Code);
    private sealed record PageTokenRequest(string TokenRequestId);
    private sealed record PageVolume(double Volume);
}
