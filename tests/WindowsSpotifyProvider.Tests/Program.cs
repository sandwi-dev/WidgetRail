using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WindowsSpotifyProvider;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformSettings;

var tests = new (string Name, Func<Task> Run)[]
{
    ("PKCE authorization uses exact callback state S256 and no client secret", PkceContract),
    ("Client ID change invalidates package authorization", ClientChangeInvalidatesAuthorization),
    ("External Client ID change invalidates stored authorization", ExternalClientChangeInvalidatesAuthorization),
    ("Refresh retains existing refresh token when Spotify omits rotation", RefreshRetainsToken),
    ("Player endpoints methods and parameters match the Web API contract", ExactPlayerEndpoints),
    ("Rate limiting respects Retry-After with bounded retry", RateLimitPolicy),
    ("Playback parsing projects tracks restrictions and attribution", PlaybackProjection),
    ("Errors are meaningful sanitized and never expose tokens", SafeErrors),
    ("Credential targets are stable package authorities", CredentialTargetScope),
    ("Windows Credential Manager persists token and granted scopes", CredentialRoundTrip),
    ("Generic widget configuration adapter stays package scoped", ConfigurationAdapterScope),
    ("Expired refresh authorization is deleted and requires reconnect", ExpiredRefreshIsDeleted),
    ("Browser failure cancels the pending callback listener", BrowserFailureCancelsCallback),
    ("Trusted playback host gets only a short-lived scoped token lease", TrustedHostTokenLease),
    ("Provider implements typed broker mappings without exposing tokens", BrokerContractMapping),
    ("Incremental scopes are explicit and closed", IncrementalScopes),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}
if (failures != 0) Environment.Exit(1);
Console.WriteLine($"WindowsSpotifyProvider.Tests passed ({tests.Length} tests)");

static async Task PkceContract()
{
    var configuration = new FakeConfigurationStore("Client123456789");
    var vault = new FakeVault();
    var callback = new CoordinatedCallback();
    var browser = new CoordinatedBrowser(callback);
    var http = new FakeHttp(request =>
    {
        Assert.Equal("https://accounts.spotify.com/api/token", request.Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, request.Method);
        var form = ParseForm(request.FormBody!);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("Client123456789", form["client_id"]);
        Assert.Equal(WindowsSpotifyPlatformBackend.ExactRedirectUri, form["redirect_uri"]);
        Assert.True(!form.ContainsKey("client_secret"));
        Assert.True(form["code_verifier"].Length is >= 43 and <= 128);
        var expected = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"])));
        Assert.Equal(callback.Challenge, expected);
        return Json(200, Token("access-one", "refresh-one"));
    });
    await using var backend = Backend(configuration, vault, http, browser, callback);
    await backend.ConnectAsync(Identity(), default);

    Assert.Equal(WindowsSpotifyPlatformBackend.ExactRedirectUri, callback.RedirectUri);
    Assert.Equal("S256", callback.ChallengeMethod);
    Assert.True(callback.State!.Length >= 32);
    Assert.Equal("refresh-one", vault.Token);
    Assert.Equal(1, browser.OpenCalls);
    Assert.Equal("https", browser.Uri!.Scheme);
    Assert.Equal("accounts.spotify.com", browser.Uri.Host);
    Assert.Equal("/authorize", browser.Uri.AbsolutePath);
    var query = ParseForm(browser.Uri.Query.TrimStart('?'));
    Assert.Equal("code", query["response_type"]);
    Assert.Equal("user-modify-playback-state user-read-playback-state", query["scope"]);
    Assert.True(!query.ContainsKey("client_secret"));
}

static async Task ClientChangeInvalidatesAuthorization()
{
    var configuration = new FakeConfigurationStore("FirstClient123");
    var vault = new FakeVault("old-refresh");
    await using var backend = Backend(configuration, vault, new FakeHttp(_ =>
        throw new InvalidOperationException()), new NullBrowser(), new NullCallback());

    await backend.ConfigureClientAsync(Identity(), "SecondClient456", default);
    Assert.Equal("SecondClient456", configuration.Value!.ClientId);
    Assert.Equal(1, vault.DeleteCalls);
    Assert.Equal(null, vault.Token);
    await backend.ConfigureClientAsync(Identity(), "SecondClient456", default);
    Assert.Equal(1, vault.DeleteCalls);
}

static async Task ExternalClientChangeInvalidatesAuthorization()
{
    var configuration = new FakeConfigurationStore("Client123456789");
    var vault = new FakeVault("old-refresh");
    await using var backend = Backend(configuration, vault, new FakeHttp(_ =>
        throw new InvalidOperationException()), new NullBrowser(), new NullCallback());

    configuration.Replace("DifferentClient456");
    var authorization = await backend.GetAuthorizationAsync(Identity(), default);
    Assert.True(!authorization.IsConnected);
    Assert.Equal(null, vault.Token);
    Assert.Equal(1, vault.DeleteCalls);
}

static async Task RefreshRetainsToken()
{
    var vault = new FakeVault("durable-refresh");
    var requests = new Queue<Func<SpotifyHttpRequest, SpotifyHttpResponse>>();
    requests.Enqueue(request =>
    {
        var form = ParseForm(request.FormBody!);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("durable-refresh", form["refresh_token"]);
        Assert.True(!form.ContainsKey("client_secret"));
        return Json(200, Token("fresh-access", refreshToken: null));
    });
    requests.Enqueue(request =>
    {
        Assert.Equal("https://api.spotify.com/v1/me/player", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer fresh-access", request.Headers!["Authorization"]);
        return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"), vault,
        new FakeHttp(request => requests.Dequeue()(request)), new NullBrowser(), new NullCallback());

    var playback = await backend.GetPlaybackAsync(Identity(), default);
    Assert.True(!playback.IsAvailable);
    Assert.Equal("durable-refresh", vault.Token);
    Assert.Equal(0, vault.SaveCalls);
}

static async Task ExactPlayerEndpoints()
{
    var expected = new Queue<(HttpMethod Method, string Uri)>(
    [
        (HttpMethod.Put, "https://api.spotify.com/v1/me/player/play"),
        (HttpMethod.Put, "https://api.spotify.com/v1/me/player/pause"),
        (HttpMethod.Post, "https://api.spotify.com/v1/me/player/next"),
        (HttpMethod.Post, "https://api.spotify.com/v1/me/player/previous"),
        (HttpMethod.Put, "https://api.spotify.com/v1/me/player/seek?position_ms=12345"),
        (HttpMethod.Put, "https://api.spotify.com/v1/me/player/repeat?state=context"),
        (HttpMethod.Put, "https://api.spotify.com/v1/me/player/shuffle?state=true"),
        (HttpMethod.Put, "https://api.spotify.com/v1/me/player/volume?volume_percent=37"),
    ]);
    var http = new FakeHttp(request =>
    {
        if (request.Uri.AbsoluteUri == "https://accounts.spotify.com/api/token")
            return Json(200, Token("access", null));
        var next = expected.Dequeue();
        Assert.Equal(next.Method, request.Method);
        Assert.Equal(next.Uri, request.Uri.AbsoluteUri);
        return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh"), http, new NullBrowser(), new NullCallback());
    var commands = new[]
    {
        new SpotifyProviderPlaybackCommand(SpotifyProviderPlaybackOperation.Play),
        new SpotifyProviderPlaybackCommand(SpotifyProviderPlaybackOperation.Pause),
        new SpotifyProviderPlaybackCommand(SpotifyProviderPlaybackOperation.Next),
        new SpotifyProviderPlaybackCommand(SpotifyProviderPlaybackOperation.Previous),
        new SpotifyProviderPlaybackCommand(SpotifyProviderPlaybackOperation.Seek, 12345),
        new SpotifyProviderPlaybackCommand(SpotifyProviderPlaybackOperation.SetRepeat,
            RepeatState: "context"),
        new SpotifyProviderPlaybackCommand(SpotifyProviderPlaybackOperation.SetShuffle,
            Enabled: true),
        new SpotifyProviderPlaybackCommand(SpotifyProviderPlaybackOperation.SetVolume,
            VolumePercent: 37),
    };
    foreach (var command in commands)
        await backend.ControlPlaybackAsync(Identity(), command, default);
    Assert.Equal(0, expected.Count);
}

static async Task RateLimitPolicy()
{
    var attempts = 0;
    var delay = new FakeDelay();
    var http = new FakeHttp(request =>
    {
        if (request.Uri.AbsoluteUri == "https://accounts.spotify.com/api/token")
            return Json(200, Token("access", null));
        attempts++;
        return attempts == 1
            ? new SpotifyHttpResponse(429, "{\"error\":{\"status\":429,\"message\":\"slow\"}}",
                new Dictionary<string, string> { ["Retry-After"] = "2" })
            : new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh"), http, new NullBrowser(), new NullCallback(), delay);
    await backend.GetPlaybackAsync(Identity(), default);
    Assert.Equal(2, attempts);
    Assert.SequenceEqual([TimeSpan.FromSeconds(2)], delay.Delays);

    var longWaitCalls = 0;
    var longWait = new FakeHttp(request =>
    {
        longWaitCalls++;
        return request.Uri.Host == "accounts.spotify.com"
            ? Json(200, Token("access", null))
            : new SpotifyHttpResponse(429, "{}",
                new Dictionary<string, string> { ["Retry-After"] = "120" });
    });
    await using var bounded = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh"), longWait, new NullBrowser(), new NullCallback(), new FakeDelay());
    await Assert.ThrowsAsync<SpotifyProviderException>(
        () => bounded.GetPlaybackAsync(Identity(), default), "rate_limited");
    var callsAfterLimit = longWaitCalls;
    await Assert.ThrowsAsync<SpotifyProviderException>(
        () => bounded.GetPlaybackAsync(Identity(), default), "rate_limited");
    Assert.Equal(callsAfterLimit, longWaitCalls);
}

static async Task PlaybackProjection()
{
    const string body = """
    {
      "device":{"volume_percent":73},"repeat_state":"track","shuffle_state":true,
      "progress_ms":1234,"is_playing":true,
      "item":{"type":"track","name":"Song","duration_ms":9000,"uri":"spotify:track:1",
        "artists":[{"name":"Artist A"},{"name":"Artist B"}],
        "album":{"images":[{"url":"https://i.scdn.co/image/abc"}]}},
      "context":{"type":"playlist","uri":"spotify:playlist:1"},
      "actions":{"disallows":{"skipping_next":true,"seeking":true}}
    }
    """;
    var http = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
        ? Json(200, Token("access", null)) : Json(200, body));
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh"), http, new NullBrowser(), new NullCallback());
    var playback = await backend.GetPlaybackAsync(Identity(), default);
    Assert.True(playback.IsAvailable && playback.IsPlaying && playback.ShuffleState);
    Assert.Equal("track", playback.ItemType);
    Assert.Equal("Song", playback.Title);
    Assert.Equal("Artist A, Artist B", playback.Subtitle);
    Assert.Equal("Spotify", playback.Attribution);
    Assert.Equal(73, playback.VolumePercent);
    Assert.True(!playback.Actions.CanSkipNext && !playback.Actions.CanSeek);
    Assert.True(playback.Actions.CanPause && playback.Actions.CanSkipPrevious);
}

static async Task SafeErrors()
{
    var http = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
        ? Json(200, Token("access-secret", null))
        : Json(403, "{\"error\":{\"status\":403,\"message\":\"Premium required\\nprivate-path\"}}"));
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh-secret"), http, new NullBrowser(), new NullCallback());
    var exception = await Assert.ThrowsAsync<SpotifyProviderException>(
        () => backend.GetPlaybackAsync(Identity(), default), "forbidden");
    Assert.True(!exception.Message.Contains('\n'));
    Assert.True(!exception.Message.Contains("access-secret", StringComparison.Ordinal));
    Assert.True(!exception.Message.Contains("refresh-secret", StringComparison.Ordinal));
}

static Task CredentialTargetScope()
{
    var first = Identity();
    var same = first with { };
    var otherPackage = first with { PackageId = "dev.other" };
    var otherPublisher = first with { PublisherId = "dev.other.publisher" };
    Assert.Equal(WindowsCredentialSpotifyTokenVault.BuildTarget(first),
        WindowsCredentialSpotifyTokenVault.BuildTarget(same));
    Assert.NotEqual(WindowsCredentialSpotifyTokenVault.BuildTarget(first),
        WindowsCredentialSpotifyTokenVault.BuildTarget(otherPackage));
    Assert.NotEqual(WindowsCredentialSpotifyTokenVault.BuildTarget(first),
        WindowsCredentialSpotifyTokenVault.BuildTarget(otherPublisher));
    Assert.True(!WindowsCredentialSpotifyTokenVault.BuildTarget(first)
        .Contains(first.PackageId, StringComparison.Ordinal));
    return Task.CompletedTask;
}

static async Task CredentialRoundTrip()
{
    var vault = new WindowsCredentialSpotifyTokenVault();
    var identity = new SpotifyIntegrationIdentity(
        "dev.test.spotify", $"dev.test.spotify.{Guid.NewGuid():N}");
    try
    {
        Assert.Equal(null, await vault.ReadAsync(identity, default));
        await vault.SaveAsync(identity, new SpotifyRefreshCredential("Client123456789",
            "refresh-value",
            new HashSet<string>(StringComparer.Ordinal)
            {
                WindowsSpotifyPlatformBackend.PlaybackReadScope,
                WindowsSpotifyPlatformBackend.StreamingScope,
            }), default);
        var stored = await vault.ReadAsync(identity, default);
        Assert.Equal("Client123456789", stored!.ClientId);
        Assert.Equal("refresh-value", stored.RefreshToken);
        Assert.True(stored.GrantedScopes.Contains(WindowsSpotifyPlatformBackend.StreamingScope));
    }
    finally { await vault.DeleteAsync(identity, default); }
    Assert.Equal(null, await vault.ReadAsync(identity, default));
}

static async Task ConfigurationAdapterScope()
{
    var root = Path.Combine(Path.GetTempPath(), "gbar-spotify-config-" + Guid.NewGuid().ToString("N"));
    try
    {
        var adapter = new WidgetConfigurationSpotifyClientStore(
            new WidgetConfigurationStore(new PlatformSettingsPaths(root)));
        var first = Identity();
        var second = first with { PackageId = "dev.spotify.other" };
        await adapter.WriteAsync(first, new SpotifyClientConfiguration("Client123456789"), default);
        Assert.Equal("Client123456789", (await adapter.ReadAsync(first, default))!.ClientId);
        Assert.Equal(null, await adapter.ReadAsync(second, default));
        var raw = await new WidgetConfigurationStore(new PlatformSettingsPaths(root)).ReadAsync(
            first.PackageId, first.PublisherId, default);
        Assert.Equal("Client123456789",
            raw.Values[WidgetConfigurationSpotifyClientStore.ClientIdKey]);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static async Task ExpiredRefreshIsDeleted()
{
    var vault = new FakeVault("expired-refresh");
    var http = new FakeHttp(_ => Json(400,
        "{\"error\":\"invalid_grant\",\"error_description\":\"expired\"}"));
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        vault, http, new NullBrowser(), new NullCallback());
    await Assert.ThrowsAsync<SpotifyProviderException>(
        () => backend.GetPlaybackAsync(Identity(), default), "authorization_expired");
    Assert.Equal(null, vault.Token);
    Assert.Equal(1, vault.DeleteCalls);
}

static async Task BrokerContractMapping()
{
    var http = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
        ? Json(200, Token("access", null))
        : Json(200, "{\"repeat_state\":\"off\",\"shuffle_state\":false," +
            "\"progress_ms\":1,\"is_playing\":false," +
            "\"item\":{\"type\":\"episode\",\"name\":\"Episode\"," +
            "\"duration_ms\":100,\"uri\":\"spotify:episode:1\"," +
            "\"show\":{\"name\":\"Show\"},\"images\":[]}}"));
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh"), http, new NullBrowser(), new NullCallback());
    ISpotifyPlatformBrokerBackend broker = backend;
    var identity = new BrokerWidgetIdentity("dev.spotify.widget", "dev.publisher", "instance");
    var configuration = await broker.GetSpotifyConfigurationAsync(identity, default);
    Assert.True(configuration.IsConfigured);
    Assert.Equal(WindowsSpotifyPlatformBackend.ExactRedirectUri, configuration.RedirectUri);
    var playback = await broker.GetSpotifyPlaybackAsync(identity, default);
    Assert.True(playback.IsAvailable);
    Assert.Equal(SpotifyPlaybackItemType.Episode, playback.Item!.ItemType);
    Assert.Equal("Show", playback.Item.Subtitle);
    Assert.Equal("Spotify", playback.Attribution);
}

static async Task BrowserFailureCancelsCallback()
{
    var callback = new CancellationAwareCallback();
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault(), new FakeHttp(_ => throw new InvalidOperationException()),
        new FailingBrowser(), callback);
    await Assert.ThrowsAsync<SpotifyProviderException>(
        () => backend.ConnectAsync(Identity(), default), "browser_unavailable");
    Assert.True(callback.WasCanceled);
}

static async Task TrustedHostTokenLease()
{
    var scopes = new HashSet<string>(StringComparer.Ordinal)
    {
        WindowsSpotifyPlatformBackend.PlaybackReadScope,
        WindowsSpotifyPlatformBackend.StreamingScope,
    };
    var http = new FakeHttp(_ => Json(200,
        Token("web-playback-access", null,
            "user-read-playback-state streaming")));
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh", scopes), http, new NullBrowser(), new NullCallback());
    var lease = await backend.AcquireTrustedHostAccessTokenAsync(
        Identity(), [WindowsSpotifyPlatformBackend.StreamingScope], default);
    Assert.Equal("web-playback-access", lease.AccessToken);
    Assert.True(lease.GrantedScopes.Contains(WindowsSpotifyPlatformBackend.StreamingScope));
    Assert.True(lease.ExpiresAt > DateTimeOffset.FromUnixTimeSeconds(1_000_000));
}

static async Task IncrementalScopes()
{
    var configuration = new FakeConfigurationStore("Client123456789");
    var callback = new CoordinatedCallback();
    var browser = new CoordinatedBrowser(callback);
    var http = new FakeHttp(_ => Json(200,
        Token("access", "refresh", "user-read-playback-state user-read-recently-played")));
    await using var backend = Backend(configuration, new FakeVault(), http, browser, callback);
    await backend.ConnectAsync(Identity(),
        [WindowsSpotifyPlatformBackend.PlaybackReadScope, "user-read-recently-played"], default);
    Assert.Contains("user-read-recently-played", browser.Uri!.Query);

    await Assert.ThrowsAsync<SpotifyProviderException>(() => backend.ConnectAsync(
        Identity(), ["user-read-private"], default), "invalid_scope");
}

static WindowsSpotifyPlatformBackend Backend(
    ISpotifyClientConfigurationStore configuration,
    ISpotifyTokenVault vault,
    ISpotifyHttpTransport http,
    ISpotifyBrowserLauncher browser,
    ISpotifyAuthorizationCallbackReceiver callback,
    ISpotifyDelay? delay = null) => new(configuration, vault, http, browser, callback,
        delay ?? new FakeDelay(), new ManualTimeProvider());

static SpotifyIntegrationIdentity Identity() => new("dev.publisher", "dev.spotify.widget");

static SpotifyHttpResponse Json(int status, string body) =>
    new(status, body, EmptyHeaders());

static IReadOnlyDictionary<string, string> EmptyHeaders() =>
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

static string Token(string accessToken, string? refreshToken, string? scope = null)
{
    var refresh = refreshToken is null ? string.Empty : $",\"refresh_token\":\"{refreshToken}\"";
    scope ??= "user-read-playback-state user-modify-playback-state";
    return $"{{\"access_token\":\"{accessToken}\",\"token_type\":\"Bearer\"," +
        $"\"expires_in\":3600,\"scope\":\"{scope}\"{refresh}}}";
}

static Dictionary<string, string> ParseForm(string form) => form
    .Split('&', StringSplitOptions.RemoveEmptyEntries)
    .Select(pair => pair.Split('=', 2))
    .ToDictionary(parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
        parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')), StringComparer.Ordinal);

static string Base64Url(ReadOnlySpan<byte> value) =>
    Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

internal sealed class FakeConfigurationStore(string? clientId) : ISpotifyClientConfigurationStore
{
    internal SpotifyClientConfiguration? Value { get; private set; } =
        clientId is null ? null : new SpotifyClientConfiguration(clientId);

    internal void Replace(string clientId) => Value = new SpotifyClientConfiguration(clientId);

    public Task<SpotifyClientConfiguration?> ReadAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = identity.Authority;
        return Task.FromResult(Value);
    }

    public Task WriteAsync(
        SpotifyIntegrationIdentity identity, SpotifyClientConfiguration configuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = identity.Authority;
        Value = configuration;
        return Task.CompletedTask;
    }
}

internal sealed class FakeVault(string? token = null, IReadOnlySet<string>? scopes = null) :
    ISpotifyTokenVault
{
    private SpotifyRefreshCredential? _credential = token is null ? null : new(
        "Client123456789", token, scopes ?? new HashSet<string>(StringComparer.Ordinal)
        {
            WindowsSpotifyPlatformBackend.PlaybackReadScope,
            WindowsSpotifyPlatformBackend.PlaybackControlScope,
        });
    internal string? Token => _credential?.RefreshToken;
    internal int SaveCalls { get; private set; }
    internal int DeleteCalls { get; private set; }

    public Task<SpotifyRefreshCredential?> ReadAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken) =>
        Task.FromResult(_credential);

    public Task SaveAsync(
        SpotifyIntegrationIdentity identity, SpotifyRefreshCredential credential,
        CancellationToken cancellationToken)
    {
        SaveCalls++;
        _credential = credential;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        SpotifyIntegrationIdentity identity, CancellationToken cancellationToken)
    {
        DeleteCalls++;
        _credential = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakeHttp(Func<SpotifyHttpRequest, SpotifyHttpResponse> handler) :
    ISpotifyHttpTransport
{
    internal ConcurrentQueue<SpotifyHttpRequest> Requests { get; } = new();

    public Task<SpotifyHttpResponse> SendAsync(
        SpotifyHttpRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Enqueue(request);
        return Task.FromResult(handler(request));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class CoordinatedCallback : ISpotifyAuthorizationCallbackReceiver
{
    private readonly TaskCompletionSource<SpotifyAuthorizationCallback> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string? RedirectUri { get; private set; }
    internal string? State { get; private set; }
    internal string? Challenge { get; private set; }
    internal string? ChallengeMethod { get; private set; }

    public Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        RedirectUri = exactRedirectUri.AbsoluteUri;
        cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
        return _completion.Task;
    }

    internal void BrowserOpened(Uri uri)
    {
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
                parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')),
                StringComparer.Ordinal);
        State = query["state"];
        Challenge = query["code_challenge"];
        ChallengeMethod = query["code_challenge_method"];
        _completion.TrySetResult(new SpotifyAuthorizationCallback(
            "authorization-code", State, null, null));
    }
}

internal sealed class CoordinatedBrowser(CoordinatedCallback callback) : ISpotifyBrowserLauncher
{
    internal Uri? Uri { get; private set; }
    internal int OpenCalls { get; private set; }

    public Task OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        Uri = uri;
        OpenCalls++;
        callback.BrowserOpened(uri);
        return Task.CompletedTask;
    }
}

internal sealed class NullBrowser : ISpotifyBrowserLauncher
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Browser was not expected.");
}

internal sealed class NullCallback : ISpotifyAuthorizationCallbackReceiver
{
    public Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri, TimeSpan timeout, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Callback was not expected.");
}

internal sealed class CancellationAwareCallback : ISpotifyAuthorizationCallbackReceiver
{
    internal bool WasCanceled { get; private set; }

    public Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<SpotifyAuthorizationCallback>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() =>
        {
            WasCanceled = true;
            completion.TrySetCanceled(cancellationToken);
        });
        return completion.Task;
    }
}

internal sealed class FailingBrowser : ISpotifyBrowserLauncher
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken) =>
        Task.FromException(new SpotifyProviderException(
            "browser_unavailable", "The Spotify sign-in page could not be opened."));
}

internal sealed class FakeDelay : ISpotifyDelay
{
    internal List<TimeSpan> Delays { get; } = [];
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1_000_000);
}

internal static class Assert
{
    internal static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected <{expected}>; actual <{actual}>.");
    }

    internal static void NotEqual<T>(T first, T second)
    {
        if (EqualityComparer<T>.Default.Equals(first, second))
            throw new InvalidOperationException($"Values should differ: <{first}>.");
    }

    internal static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected <{actual}> to contain <{expected}>.");
    }

    internal static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Sequences differ.");
    }

    internal static async Task<TException> ThrowsAsync<TException>(
        Func<Task> action, string code) where TException : Exception
    {
        try { await action(); }
        catch (TException exception)
        {
            if (code.Length > 0 && exception is SpotifyProviderException spotify &&
                spotify.Code != code)
                throw new InvalidOperationException(
                    $"Expected code <{code}>; actual <{spotify.Code}>.", exception);
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
