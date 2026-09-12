using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.SpotifyPlayback;
using WidgetRail.WindowsSpotifyProvider;
using BrokerSpotifyLocalPlaybackState =
    WidgetRail.Samples.SpotifyWidget.SpotifyLocalPlaybackState;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Search encodes queries, bounds paging and parses every result type", SearchContract),
    ("PKCE authorization uses exact callback state S256 and no client secret", PkceContract),
    ("Client ID change invalidates package authorization", ClientChangeInvalidatesAuthorization),
    ("External Client ID change invalidates stored authorization", ExternalClientChangeInvalidatesAuthorization),
    ("Refresh retains existing refresh token when Spotify omits rotation", RefreshRetainsToken),
    ("Player endpoints methods and parameters match the Web API contract", ExactPlayerEndpoints),
    ("Rate limiting respects Retry-After with bounded retry", RateLimitPolicy),
    ("Retry policy rejects cancellation-ignoring transport completion", RetryPolicyCancellationWins),
    ("Canceled refresh cannot publish or rotate session credentials", CanceledRefreshCannotPublish),
    ("Playback and collection endpoints run without authorization construction", EndpointFamiliesAreIndependent),
    ("Concurrent token demand and 401 refresh retain one session authority", ConcurrentTokenAndUnauthorizedRefresh),
    ("Strict parser rejects malformed and oversized injected responses", StrictParserRejectsUnsafeResponses),
    ("Playback parsing projects tracks restrictions and attribution", PlaybackProjection),
    ("Errors are meaningful sanitized and never expose tokens", SafeErrors),
    ("Credential targets are stable package authorities", CredentialTargetScope),
    ("Windows Credential Manager persists token and granted scopes", CredentialRoundTrip),
    ("Expired refresh authorization is deleted and requires reconnect", ExpiredRefreshIsDeleted),
    ("Browser failure cancels the pending callback listener", BrowserFailureCancelsCallback),
    ("Loopback callback survives a speculative probe", LoopbackCallbackSurvivesProbe),
    ("Loopback callback is not blocked by a silent probe", LoopbackCallbackBypassesSilentProbe),
    ("Loopback callback ignores a wrong-state request", LoopbackCallbackRejectsWrongState),
    ("Occupied callback port never opens the browser", OccupiedCallbackPortStopsBrowser),
    ("Trusted playback host gets only a short-lived scoped token lease", TrustedHostTokenLease),
    ("Web API devices queue and playlist collections are bounded and projected", WebApiCollections),
    ("Web API playback mutations use exact encoded URLs and typed JSON bodies", WebApiMutations),
    ("Playlist authorization expands to the least privileged read scopes", PlaylistScopeExpansion),
    ("Local playback hands off tokens and exposes only its public pseudo-device", LocalPlaybackLifecycle),
    ("Local playback routing follows exact successful device decisions", LocalPlaybackRoutingAuthority),
    ("Activation failure and cancellation cannot reach device transfer", LocalPlaybackActivationFailureCleanup),
    ("Disconnect clears cached credentials and stops local playback", DisconnectClearsSession),
    ("Canceled local playback startup releases the host and resets Starting", LocalPlaybackCancellation),
    ("Local playback maps reauthorization premium and SDK errors", LocalPlaybackErrorStates),
    ("Provider disposal tears down an active local playback host", LocalPlaybackBackendDisposal),
    ("Application boundary maps typed Spotify data without exposing tokens", ApplicationContractMapping),
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
    Assert.Equal(WindowsSpotifyPlatformBackend.AuthorizationCallbackTimeout, callback.Timeout);
    Assert.Equal(TimeSpan.FromMinutes(15), callback.Timeout);
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

static async Task RetryPolicyCancellationWins()
{
    var started = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource<SpotifyHttpResponse>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await using var http = new AsyncFakeHttp(async (_, _) =>
    {
        started.SetResult();
        return await release.Task.ConfigureAwait(false);
    });
    var policy = new SpotifyHttpPolicy(http, new FakeDelay(), new ManualTimeProvider());
    using var cancellation = new CancellationTokenSource();
    var request = policy.SendAsync(
        new SpotifyHttpRequest(HttpMethod.Get,
            new Uri("https://api.spotify.com/v1/me/player")),
        cancellation.Token);
    await started.Task;
    cancellation.Cancel();
    release.SetResult(new SpotifyHttpResponse(200, "{}", EmptyHeaders()));
    await Assert.ThrowsAsync<OperationCanceledException>(() => request, string.Empty);
}

static async Task CanceledRefreshCannotPublish()
{
    var refreshStarted = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseRefresh = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var tokenCalls = 0;
    var apiCalls = 0;
    var vault = new FakeVault("durable-refresh");
    await using var http = new AsyncFakeHttp(async (request, _) =>
    {
        if (request.Uri.Host == "accounts.spotify.com")
        {
            var call = Interlocked.Increment(ref tokenCalls);
            if (call == 1)
            {
                refreshStarted.SetResult();
                await releaseRefresh.Task.ConfigureAwait(false);
                return Json(200, Token("canceled-access", "rotated-refresh"));
            }
            return Json(200, Token("fresh-access", null));
        }

        Interlocked.Increment(ref apiCalls);
        Assert.Equal("Bearer fresh-access", request.Headers!["Authorization"]);
        return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        vault, http, new NullBrowser(), new NullCallback());
    using var cancellation = new CancellationTokenSource();

    var canceled = backend.GetPlaybackAsync(Identity(), cancellation.Token);
    await refreshStarted.Task;
    cancellation.Cancel();
    releaseRefresh.SetResult();
    await Assert.ThrowsAsync<OperationCanceledException>(() => canceled, string.Empty);

    Assert.Equal("durable-refresh", vault.Token);
    Assert.Equal(0, vault.SaveCalls);
    Assert.Equal(0, apiCalls);

    await backend.GetPlaybackAsync(Identity(), default);
    Assert.Equal(2, tokenCalls);
    Assert.Equal(1, apiCalls);
    Assert.Equal("durable-refresh", vault.Token);
    Assert.Equal(0, vault.SaveCalls);
}

static async Task SearchContract()
{
    foreach (var kind in Enum.GetValues<SpotifySearchKind>())
    {
        var type = kind.ToString().ToLowerInvariant();
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [type + "s"] = new { offset = 0, limit = 10, total = 5000, items = new object?[]
            {
                new { id = "result1", type, name = "Result", artists = new[] { new { name = "Artist" } },
                    owner = new { display_name = "Owner" }, images = new[] { new { url = "https://i.scdn.co/image/result" } },
                    is_playable = false }, null,
            } },
        });
        var sender = new FakeAuthorizedSender(_ => Json(200, body));
        var api = new SpotifySearchApi(sender);
        var page = await api.SearchAsync(Identity(), "A & B?type=album", kind, 0, 10, default);
        var request = sender.Requests.Single();
        Assert.Equal("/v1/search", request.Uri.AbsolutePath);
        Assert.Contains("q=A%20%26%20B%3Ftype%3Dalbum", request.Uri.AbsoluteUri);
        Assert.Contains("&type=" + type, request.Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(1000, page.Total);
        Assert.Equal(kind, page.Items.Single().Kind);
        Assert.Equal("spotify:" + type + ":result1", page.Items.Single().Uri);
        Assert.True(!page.Items.Single().IsPlayable && !page.HasAuthoritativeWindow);
        await Assert.ThrowsAsync<SpotifyProviderException>(() => api.SearchAsync(Identity(), " ", kind, 0, 10, default), "invalid_request");
        await Assert.ThrowsAsync<SpotifyProviderException>(() => api.SearchAsync(Identity(), "a", kind, 1000, 10, default), "invalid_request");
        Assert.Equal(1, sender.Requests.Count);
    }
    await Assert.ThrowsAsync<SpotifyProviderException>(() => Task.Run(() =>
        SpotifyResponseParser.ParseSearchPage("{}", SpotifySearchKind.Track, 0, 10)), "invalid_response");
    await Assert.ThrowsAsync<SpotifyProviderException>(() => Task.Run(() =>
        SpotifyResponseParser.ParseSearchPage("{\"tracks\":{\"offset\":10,\"limit\":10,\"total\":50,\"items\":[]}}", SpotifySearchKind.Track, 0, 10)), "invalid_response");
}

static async Task EndpointFamiliesAreIndependent()
{
    var sender = new FakeAuthorizedSender(request => request.Uri.AbsolutePath switch
    {
        "/v1/me/player/devices" => Json(200,
            "{\"devices\":[{\"id\":\"device-one\",\"name\":\"Desk\"," +
            "\"type\":\"Computer\",\"is_active\":true,\"is_restricted\":false," +
            "\"supports_volume\":true,\"volume_percent\":40}]}"),
        "/v1/me/player/next" => new SpotifyHttpResponse(204, string.Empty, EmptyHeaders()),
        "/v1/me/playlists" => Json(200,
            "{\"items\":[{\"id\":\"playlist-one\",\"name\":\"Focus\"," +
            "\"owner\":{\"display_name\":\"Owner\"},\"items\":{\"total\":0}," +
            "\"images\":[],\"collaborative\":false,\"public\":true}]," +
            "\"limit\":5,\"offset\":0,\"total\":1}"),
        _ => throw new InvalidOperationException("Unexpected endpoint request."),
    });
    var identity = Identity();
    var playback = new SpotifyPlaybackApi(sender);
    var devices = await playback.GetDevicesAsync(identity, default);
    Assert.Equal("Desk", devices.Devices.Single().Name);
    await playback.ControlPlaybackAsync(identity,
        new SpotifyProviderPlaybackCommand(SpotifyProviderPlaybackOperation.Next), default);

    var collections = new SpotifyCollectionApi(sender);
    var playlists = await collections.GetPlaylistsAsync(identity, 0, 5, default);
    Assert.Equal("Focus", playlists.Items.Single().Name);
    Assert.Equal(3, sender.Requests.Count);
    Assert.Equal(WindowsSpotifyPlatformBackend.PlaybackReadScope,
        sender.Requests[0].RequiredScope);
    Assert.Equal(WindowsSpotifyPlatformBackend.PlaybackControlScope,
        sender.Requests[1].RequiredScope);
    Assert.Equal(WindowsSpotifyPlatformBackend.PlaylistReadPrivateScope,
        sender.Requests[2].RequiredScope);
    Assert.Equal(WindowsSpotifyPlatformBackend.PlaylistReadCollaborativeScope,
        sender.Requests[2].AdditionalRequiredScope);
}

static async Task ConcurrentTokenAndUnauthorizedRefresh()
{
    var tokenStarted = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseToken = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var tokenCalls = 0;
    var apiCalls = 0;
    await using var http = new AsyncFakeHttp(async (request, cancellationToken) =>
    {
        if (request.Uri.Host == "accounts.spotify.com")
        {
            var call = Interlocked.Increment(ref tokenCalls);
            if (call == 1)
            {
                tokenStarted.SetResult();
                await releaseToken.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return Json(200, Token("access-" + call, null));
        }
        var apiCall = Interlocked.Increment(ref apiCalls);
        return apiCall == 3
            ? Json(401, "{\"error\":{\"message\":\"expired\"}}")
            : new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh"), http, new NullBrowser(), new NullCallback());
    var first = backend.GetPlaybackAsync(Identity(), default);
    await tokenStarted.Task;
    var second = backend.GetPlaybackAsync(Identity(), default);
    releaseToken.SetResult();
    await Task.WhenAll(first, second);
    Assert.Equal(1, tokenCalls);
    Assert.Equal(2, apiCalls);

    await backend.GetPlaybackAsync(Identity(), default);
    Assert.Equal(2, tokenCalls);
    Assert.Equal(4, apiCalls);
}

static async Task StrictParserRejectsUnsafeResponses()
{
    await Assert.ThrowsAsync<SpotifyProviderException>(
        () => Task.Run(() => SpotifyResponseParser.ParseDevices("{\"devices\":{}}")),
        "invalid_response");
    await Assert.ThrowsAsync<SpotifyProviderException>(
        () => Task.Run(() => SpotifyResponseParser.ParsePlaylistPage(
            "{\"items\":[],\"limit\":5,\"offset\":1,\"total\":0}", 0, 5)),
        "invalid_response");
    var oversized = new string('x', SpotifyHttpTransport.MaximumResponseBytes + 1);
    await Assert.ThrowsAsync<SpotifyProviderException>(
        () => Task.Run(() => SpotifyResponseParser.ParsePlayback(oversized)),
        "response_too_large");
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

static async Task ApplicationContractMapping()
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
    var identity = Identity();
    var configuration = await backend.GetSpotifyConfigurationAsync(identity, default);
    Assert.True(configuration.IsConfigured);
    Assert.Equal(WindowsSpotifyPlatformBackend.ExactRedirectUri, configuration.RedirectUri);
    var playback = await backend.GetSpotifyPlaybackAsync(identity, default);
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

static async Task LoopbackCallbackSurvivesProbe()
{
    var receiver = new LoopbackSpotifyAuthorizationCallbackReceiver();
    var receive = receiver.ReceiveAsync(
        new Uri(WindowsSpotifyPlatformBackend.ExactRedirectUri),
        "verified-state",
        TimeSpan.FromSeconds(5), CancellationToken.None);

    // Simulate a browser/security preconnect that opens and closes before the
    // real callback navigation sends its request.
    using var probe = new TcpClient();
    await probe.ConnectAsync(IPAddress.Loopback, 43827);

    using var callbackClient = new TcpClient();
    await callbackClient.ConnectAsync(IPAddress.Loopback, 43827);
    using var stream = callbackClient.GetStream();
    var request = Encoding.ASCII.GetBytes(
        "GET /callback/?code=authorization-code&state=verified-state HTTP/1.1\r\n" +
        "Host: 127.0.0.1:43827\r\nConnection: close\r\n\r\n");
    await stream.WriteAsync(request);
    await stream.FlushAsync();

    var callback = await receive;
    Assert.Equal("authorization-code", callback.Code);
    Assert.Equal("verified-state", callback.State);
    Assert.Equal(null, callback.Error);
}

static async Task LoopbackCallbackBypassesSilentProbe()
{
    var receiver = new LoopbackSpotifyAuthorizationCallbackReceiver();
    var receive = receiver.ReceiveAsync(
        new Uri(WindowsSpotifyPlatformBackend.ExactRedirectUri),
        "verified-state",
        TimeSpan.FromSeconds(5), CancellationToken.None);

    using var silentProbe = new TcpClient();
    await silentProbe.ConnectAsync(IPAddress.Loopback, 43827);
    var started = DateTimeOffset.UtcNow;
    await SendCallbackAsync("authorization-code", "verified-state");

    var callback = await receive;
    Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(1));
    Assert.Equal("authorization-code", callback.Code);
}

static async Task LoopbackCallbackRejectsWrongState()
{
    var receiver = new LoopbackSpotifyAuthorizationCallbackReceiver();
    var receive = receiver.ReceiveAsync(
        new Uri(WindowsSpotifyPlatformBackend.ExactRedirectUri),
        "verified-state",
        TimeSpan.FromSeconds(5), CancellationToken.None);

    await SendCallbackAsync("interfering-code", "wrong-state");
    await SendCallbackAsync("authorization-code", "verified-state");

    var callback = await receive;
    Assert.Equal("authorization-code", callback.Code);
    Assert.Equal("verified-state", callback.State);
}

static async Task SendCallbackAsync(string code, string state)
{
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, 43827);
    using var stream = client.GetStream();
    var request = Encoding.ASCII.GetBytes(
        $"GET /callback/?code={code}&state={state} HTTP/1.1\r\n" +
        "Host: 127.0.0.1:43827\r\nConnection: close\r\n\r\n");
    await stream.WriteAsync(request);
    await stream.FlushAsync();
}

static async Task OccupiedCallbackPortStopsBrowser()
{
    var occupied = new TcpListener(IPAddress.Loopback, 43827)
    {
        ExclusiveAddressUse = true,
    };
    occupied.Start(1);
    try
    {
        var browser = new RecordingBrowser();
        await using var backend = Backend(
            new FakeConfigurationStore("Client123456789"),
            new FakeVault(),
            new FakeHttp(_ => throw new InvalidOperationException()),
            browser,
            new LoopbackSpotifyAuthorizationCallbackReceiver());
        await Assert.ThrowsAsync<SpotifyProviderException>(
            () => backend.ConnectAsync(Identity(), default), "callback_unavailable");
        Assert.Equal(0, browser.OpenCalls);
    }
    finally
    {
        occupied.Stop();
    }
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

static async Task WebApiCollections()
{
    var apiRequests = new Queue<Func<SpotifyHttpRequest, SpotifyHttpResponse>>();
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.spotify.com/v1/me/player/devices", request.Uri.AbsoluteUri);
        return Json(200, """
            {"devices":[{"id":"device-1","is_active":true,"is_restricted":false,
              "name":"Living Room\u0000","supports_volume":true,"type":"Computer",
              "volume_percent":67}]}
            """);
    });
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.spotify.com/v1/me/player/queue", request.Uri.AbsoluteUri);
        return Json(200, """
            {"currently_playing":{"id":"track-1","type":"track","name":"Current",
              "duration_ms":180000,"uri":"spotify:track:track-1","is_playable":true,
              "artists":[{"name":"Artist"}],"album":{"images":[
                {"url":"https://i.scdn.co/image/current"}]}},
             "queue":[{"id":"episode-1","type":"episode","name":"Episode",
              "duration_ms":240000,"uri":"spotify:episode:episode-1","is_playable":false,
              "show":{"name":"Show"},"images":[]}]}
            """);
    });
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.spotify.com/v1/me/playlists?offset=0&limit=20",
            request.Uri.AbsoluteUri);
        return Json(200, """
            {"items":[{"collaborative":false,"description":"A playlist","id":"playlist-1",
              "images":[{"url":"https://i.scdn.co/image/playlist"}],"name":"Road Trip",
              "owner":{"display_name":"Owner"},"public":false,
              "items":{"total":1}}],"limit":20,"offset":0,"total":1}
            """);
    });
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.spotify.com/v1/playlists/playlist-1", request.Uri.AbsoluteUri);
        return Json(200, """
            {"collaborative":true,"description":"Details","id":"playlist-1","images":[],
             "name":"Road Trip","owner":{"display_name":"Owner"},"public":null,
             "items":{"total":1}}
            """);
    });
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://api.spotify.com/v1/playlists/playlist-1/items?offset=0&limit=20",
            request.Uri.AbsoluteUri);
        Assert.True(!request.Uri.AbsolutePath.EndsWith("/tracks", StringComparison.Ordinal));
        return Json(200, """
            {"items":[{"item":{"id":"track-2","type":"track","name":"Next",
              "duration_ms":200000,"uri":"spotify:track:track-2","artists":[{"name":"Two"}],
              "album":{"images":[]}}}],"limit":20,"offset":0,"total":1}
            """);
    });
    var scopes = new HashSet<string>(StringComparer.Ordinal)
    {
        WindowsSpotifyPlatformBackend.PlaybackReadScope,
        WindowsSpotifyPlatformBackend.PlaybackControlScope,
        WindowsSpotifyPlatformBackend.PlaylistReadPrivateScope,
        WindowsSpotifyPlatformBackend.PlaylistReadCollaborativeScope,
    };
    var http = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
        ? Json(200, Token("access", null, string.Join(' ', scopes)))
        : apiRequests.Dequeue()(request));
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh", scopes), http, new NullBrowser(), new NullCallback());
    var identity = Identity();

    var devices = await backend.GetSpotifyDevicesAsync(identity, default);
    Assert.Equal(1, devices.Devices.Count);
    Assert.Equal("Living Room", devices.Devices[0].Name);
    Assert.True(devices.Devices[0].IsActive && devices.Devices[0].SupportsVolume);

    var queue = await backend.GetSpotifyQueueAsync(identity, default);
    Assert.Equal("Current", queue.CurrentlyPlaying!.Title);
    Assert.Equal("https://open.spotify.com/track/track-1",
        queue.CurrentlyPlaying.SpotifyUrl);
    Assert.Equal(1, queue.Items.Count);
    Assert.True(!queue.Items[0].IsPlayable && !queue.IsTruncated);

    var playlists = await backend.GetSpotifyPlaylistsAsync(
        identity, new SpotifyPlaylistPageRequest(0, 20), default);
    Assert.Equal(1, playlists.Items.Count);
    Assert.Equal("https://open.spotify.com/playlist/playlist-1",
        playlists.Items[0].SpotifyUrl);

    var playlist = await backend.GetSpotifyPlaylistAsync(
        identity, "playlist-1", default);
    Assert.True(playlist.IsCollaborative);

    var items = await backend.GetSpotifyPlaylistItemsAsync(
        identity, new SpotifyPlaylistItemsRequest("playlist-1", 0, 20), default);
    Assert.Equal("Next", items.Items[0].Title);
    Assert.Equal(0, apiRequests.Count);
}

static async Task WebApiMutations()
{
    var apiRequests = new Queue<Func<SpotifyHttpRequest, SpotifyHttpResponse>>();
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://api.spotify.com/v1/me/player", request.Uri.AbsoluteUri);
        using var body = JsonDocument.Parse(request.JsonBody!);
        Assert.Equal("device+value",
            body.RootElement.GetProperty("device_ids")[0].GetString());
        Assert.True(body.RootElement.GetProperty("play").GetBoolean());
        return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://api.spotify.com/v1/me/player/queue?uri=spotify%3Atrack%3Aabc&device_id=device%2Bvalue",
            request.Uri.AbsoluteUri);
        Assert.Equal(null, request.JsonBody);
        return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(
            "https://api.spotify.com/v1/me/player/play?device_id=device%2Bvalue",
            request.Uri.AbsoluteUri);
        using var body = JsonDocument.Parse(request.JsonBody!);
        Assert.Equal("spotify:playlist:playlist-1",
            body.RootElement.GetProperty("context_uri").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("offset").GetProperty("position").GetInt32());
        return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://api.spotify.com/v1/me/player/play", request.Uri.AbsoluteUri);
        using var body = JsonDocument.Parse(request.JsonBody!);
        Assert.Equal("spotify:track:one", body.RootElement.GetProperty("uris")[0].GetString());
        Assert.True(!body.RootElement.TryGetProperty("context_uri", out _));
        return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    apiRequests.Enqueue(request =>
    {
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://api.spotify.com/v1/me/player/play", request.Uri.AbsoluteUri);
        using var body = JsonDocument.Parse(request.JsonBody!);
        Assert.Equal("spotify:playlist:playlist-1",
            body.RootElement.GetProperty("context_uri").GetString());
        Assert.Equal("spotify:track:exact",
            body.RootElement.GetProperty("offset").GetProperty("uri").GetString());
        Assert.True(!body.RootElement.GetProperty("offset").TryGetProperty("position", out _));
        return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
    });
    var scopes = new HashSet<string>(StringComparer.Ordinal)
    {
        WindowsSpotifyPlatformBackend.PlaybackReadScope,
        WindowsSpotifyPlatformBackend.PlaybackControlScope,
    };
    var http = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
        ? Json(200, Token("access", null)) : apiRequests.Dequeue()(request));
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault("refresh", scopes), http, new NullBrowser(), new NullCallback());
    var identity = Identity();

    await backend.TransferSpotifyPlaybackAsync(identity,
        new TransferSpotifyPlaybackRequest("device+value", true), default);
    await backend.AddSpotifyQueueItemAsync(identity,
        new AddSpotifyQueueItemRequest("spotify:track:abc", "device+value"), default);
    await backend.StartSpotifyPlaybackAsync(identity,
        new StartSpotifyPlaybackRequest(
            "spotify:playlist:playlist-1", null, "device+value", 3), default);
    await backend.StartSpotifyPlaybackAsync(identity,
        new StartSpotifyPlaybackRequest(null, ["spotify:track:one"], null, null), default);
    await backend.StartSpotifyPlaybackAsync(identity,
        new StartSpotifyPlaybackRequest(
            "spotify:playlist:playlist-1", null, null, null,
            "spotify:track:exact"), default);
    Assert.Equal(0, apiRequests.Count);
}

static async Task PlaylistScopeExpansion()
{
    var callback = new CoordinatedCallback();
    var browser = new CoordinatedBrowser(callback);
    var scopeText = string.Join(' ',
        WindowsSpotifyPlatformBackend.PlaybackReadScope,
        WindowsSpotifyPlatformBackend.PlaylistReadPrivateScope,
        WindowsSpotifyPlatformBackend.PlaylistReadCollaborativeScope);
    var http = new FakeHttp(_ => Json(200, Token("access", "refresh", scopeText)));
    await using var backend = Backend(new FakeConfigurationStore("Client123456789"),
        new FakeVault(), http, browser, callback);
    var summary = await backend.ConnectSpotifyAsync(
        Identity(),
        new ConnectSpotifyRequest([
            SpotifyAuthorizationScope.PlaybackStateRead,
            SpotifyAuthorizationScope.PlaylistsRead,
        ]), default);

    Assert.Contains("playlist-read-private", browser.Uri!.Query);
    Assert.Contains("playlist-read-collaborative", browser.Uri.Query);
    Assert.True(summary.GrantedScopes.Contains(SpotifyAuthorizationScope.PlaylistsRead));
    Assert.Equal(2, summary.GrantedScopes.Count);
}

static async Task LocalPlaybackLifecycle()
{
    var hostPath = CreateTemporaryPlaybackHost();
    try
    {
        var tokenCalls = 0;
        var failRenewal = false;
        var startupOrder = new List<string>();
        var client = new FakeLocalPlaybackHostClient("private-real-device-id");
        client.CommandObserved = type => startupOrder.Add(type);
        var remoteDevices = string.Join(',', Enumerable.Range(0, 64).Select(index =>
            "{\"id\":\"remote-" + index + "\",\"is_active\":false," +
            "\"is_restricted\":false,\"name\":\"Remote " + index + "\"," +
            "\"supports_volume\":true,\"type\":\"Computer\"," +
            "\"volume_percent\":50}"));
        var manager = new SpotifyLocalPlaybackManager(
            hostPath,
            (identity, scopes, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal(Identity(), identity);
                Assert.SequenceEqual(LocalPlaybackScopes(), scopes);
                tokenCalls++;
                if (failRenewal)
                    throw new SpotifyProviderException(
                        "authorization_expired", "Reconnect Spotify.");
                return Task.FromResult(new TrustedHostSpotifyAccessToken(
                    tokenCalls == 1 ? "initial-token" : "renewed-token",
                    DateTimeOffset.UtcNow.AddMinutes(10),
                    LocalPlaybackScopes()));
            },
            () => client);
        var http = new FakeHttp(request =>
        {
            if (request.Uri.Host == "accounts.spotify.com")
                return Json(200, Token("web-api-token", null));
            if (request.Method == HttpMethod.Get &&
                request.Uri.AbsolutePath == "/v1/me/player/devices")
                return Json(200, "{\"devices\":[" + remoteDevices + "]}");
            if (request.Method == HttpMethod.Put &&
                request.Uri.AbsolutePath == "/v1/me/player")
            {
                startupOrder.Add("transfer");
                return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
            }
            throw new InvalidOperationException("Unexpected Spotify request: " + request.Uri);
        });
        await using var backend = Backend(
            new FakeConfigurationStore("Client123456789"), new FakeVault("refresh"),
            http, new NullBrowser(), new NullCallback(), localPlayback: manager);
        var brokerIdentity = Identity();

        var before = await backend.GetSpotifyDevicesAsync(brokerIdentity, default);
        Assert.Equal(64, before.Devices.Count);
        var publicDevice = before.Devices.Single(device => device.IsLocalHost);
        Assert.Equal(SpotifyLocalPlaybackManager.PublicDeviceId, publicDevice.DeviceId);
        Assert.True(!publicDevice.IsActive);
        Assert.True(!JsonSerializer.Serialize(before).Contains(
            "private-real-device-id", StringComparison.Ordinal));

        var started = await backend.ControlSpotifyLocalPlaybackAsync(
            brokerIdentity,
            new SpotifyLocalPlaybackCommand(
                SpotifyLocalPlaybackOperation.StartAndTransfer, null, true), default);
        Assert.Equal(BrokerSpotifyLocalPlaybackState.Active, started.State);
        Assert.Equal(1, client.StartCalls);
        Assert.Equal(SpotifyLocalPlaybackManager.DeviceName,
            client.ConnectOptions!.DeviceName);
        Assert.True(client.Commands.Any(command => command.Type == "activate_element"));
        Assert.SequenceEqual(["activate_element", "transfer"], startupOrder);
        var transfer = http.Requests.Single(request => request.Method == HttpMethod.Put &&
            request.Uri.AbsolutePath == "/v1/me/player");
        using (var body = JsonDocument.Parse(transfer.JsonBody!))
        {
            Assert.Equal("private-real-device-id",
                body.RootElement.GetProperty("device_ids")[0].GetString());
            Assert.True(body.RootElement.GetProperty("play").GetBoolean());
        }

        var activeDevices = await backend.GetSpotifyDevicesAsync(brokerIdentity, default);
        Assert.Equal(64, activeDevices.Devices.Count);
        publicDevice = activeDevices.Devices.Single(device => device.IsLocalHost);
        Assert.True(publicDevice.IsActive);
        Assert.Equal(SpotifyLocalPlaybackManager.PublicDeviceId, publicDevice.DeviceId);
        Assert.True(!JsonSerializer.Serialize(activeDevices).Contains(
            "private-real-device-id", StringComparison.Ordinal));

        client.Raise("token_requested", new { tokenRequestId = "renew-1" });
        var provided = await client.TokenProvided.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("renew-1", provided.RequestId);
        Assert.Equal("renewed-token", provided.Token.AccessToken);
        Assert.Equal(2, tokenCalls);

        var volume = await backend.ControlSpotifyLocalPlaybackAsync(
            brokerIdentity,
            new SpotifyLocalPlaybackCommand(
                SpotifyLocalPlaybackOperation.SetVolume, 42, null), default);
        Assert.Equal(42, volume.VolumePercent);
        var volumeCommand = client.Commands.Last(command => command.Type == "set_volume");
        Assert.Equal(0.42, volumeCommand.Payload.GetProperty("volume").GetDouble());

        failRenewal = true;
        client.Raise("token_requested", new { tokenRequestId = "renew-fails" });
        await WaitUntilAsync(() => manager.GetSummary(Identity()).State ==
            BrokerSpotifyLocalPlaybackState.ReauthorizationRequired);
        await WaitUntilAsync(() => client.DisposeCalls == 1 && !client.IsRunning);

        var stopped = await backend.ControlSpotifyLocalPlaybackAsync(
            brokerIdentity,
            new SpotifyLocalPlaybackCommand(
                SpotifyLocalPlaybackOperation.Stop, null, null), default);
        Assert.Equal(BrokerSpotifyLocalPlaybackState.Disabled, stopped.State);
        Assert.Equal(1, client.DisposeCalls);
    }
    finally { File.Delete(hostPath); }
}

static async Task LocalPlaybackRoutingAuthority()
{
    var hostPath = CreateTemporaryPlaybackHost();
    try
    {
        var client = new FakeLocalPlaybackHostClient("private-routing-device");
        var manager = new SpotifyLocalPlaybackManager(
            hostPath,
            (_, _, _) => Task.FromResult(new TrustedHostSpotifyAccessToken(
                "local-token", DateTimeOffset.UtcNow.AddMinutes(10),
                LocalPlaybackScopes())),
            () => client);
        var http = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
            ? Json(200, Token("web-api-token", null))
            : new SpotifyHttpResponse(204, string.Empty, EmptyHeaders()));
        await using var backend = Backend(
            new FakeConfigurationStore("Client123456789"), new FakeVault("refresh"),
            http, new NullBrowser(), new NullCallback(), localPlayback: manager);
        var identity = Identity();

        await backend.ControlSpotifyLocalPlaybackAsync(identity,
            new(SpotifyLocalPlaybackOperation.StartAndTransfer, null, true), default);
        await backend.ControlSpotifyPlaybackAsync(identity,
            new(SpotifyPlaybackOperation.Pause), default);
        Assert.Equal("pause", client.Commands[^1].Type);
        Assert.Equal(0, http.Requests.Count(request =>
            request.Uri.AbsolutePath == "/v1/me/player/pause"));

        await backend.TransferSpotifyPlaybackAsync(identity,
            new("remote-device", true), default);
        client.Raise("player_state_changed", AvailableLocalPlayback(paused: false));
        await backend.ControlSpotifyPlaybackAsync(identity,
            new(SpotifyPlaybackOperation.Play), default);
        Assert.Equal(1, http.Requests.Count(request =>
            request.Uri.AbsolutePath == "/v1/me/player/play"));
        Assert.True(!(await backend.GetSpotifyDevicesAsync(identity, default)).Devices
            .Single(device => device.IsLocalHost).IsActive);

        await backend.TransferSpotifyPlaybackAsync(identity,
            new(SpotifyLocalPlaybackManager.PublicDeviceId, true), default);
        await backend.ControlSpotifyPlaybackAsync(identity,
            new(SpotifyPlaybackOperation.Play), default);
        Assert.Equal("resume", client.Commands[^1].Type);
        Assert.Equal(1, http.Requests.Count(request =>
            request.Uri.AbsolutePath == "/v1/me/player/play"));

        client.Raise("player_state_changed", UnavailableLocalPlayback());
        await backend.ControlSpotifyPlaybackAsync(identity,
            new(SpotifyPlaybackOperation.Pause), default);
        Assert.Equal(1, http.Requests.Count(request =>
            request.Uri.AbsolutePath == "/v1/me/player/pause"));

        var stale = manager.BeginRoutingDecision(identity);
        var current = manager.BeginRoutingDecision(identity);
        manager.CompleteRoutingDecision(identity, stale, local: true);
        Assert.True(!manager.GetPublicDevice(identity)!.IsActive);
        manager.CompleteRoutingDecision(identity, current, local: false);
        Assert.True(!manager.GetPublicDevice(identity)!.IsActive);
    }
    finally { File.Delete(hostPath); }
}

static async Task LocalPlaybackActivationFailureCleanup()
{
    var timeout = (TimeSpan)typeof(SpotifyLocalPlaybackManager).GetField(
        "ReadyTimeout", System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
    Assert.Equal(TimeSpan.FromSeconds(35), timeout);

    async Task RunAsync(bool cancel)
    {
        var hostPath = CreateTemporaryPlaybackHost();
        try
        {
            var client = new FakeLocalPlaybackHostClient("private-activation-device");
            if (cancel)
                client.CommandWait = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            else
                client.FailCommandType = "activate_element";
            var manager = new SpotifyLocalPlaybackManager(
                hostPath,
                (_, _, _) => Task.FromResult(new TrustedHostSpotifyAccessToken(
                    "local-token", DateTimeOffset.UtcNow.AddMinutes(10),
                    LocalPlaybackScopes())),
                () => client);
            var http = new FakeHttp(_ => throw new InvalidOperationException(
                "Transfer must not run before activation succeeds."));
            await using var backend = Backend(
                new FakeConfigurationStore("Client123456789"), new FakeVault("refresh"),
                http, new NullBrowser(), new NullCallback(), localPlayback: manager);
            using var cancellation = new CancellationTokenSource();
            var operation = backend.ControlSpotifyLocalPlaybackAsync(
                Identity(), new(SpotifyLocalPlaybackOperation.StartAndTransfer, null, true),
                cancellation.Token);
            if (cancel)
            {
                await WaitUntilAsync(() => client.Commands.Any(command =>
                    command.Type == "activate_element"));
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => operation, string.Empty);
            }
            else
            {
                await Assert.ThrowsAsync<SpotifyApplicationException>(
                    () => operation, "sdk_command_failed");
            }
            Assert.Equal(0, http.Requests.Count);
            Assert.Equal(1, client.DisposeCalls);
            Assert.True(!client.IsRunning);
        }
        finally { File.Delete(hostPath); }
    }

    await RunAsync(cancel: false);
    await RunAsync(cancel: true);
}

static WidgetRail.SpotifyPlayback.SpotifyLocalPlaybackState AvailableLocalPlayback(
    bool paused) => new(
    true, paused, 1_000, 120_000, 0, false,
    new(false, false, false, false, false), null);

static WidgetRail.SpotifyPlayback.SpotifyLocalPlaybackState UnavailableLocalPlayback() =>
    new(false, true, 0, 0, 0, false,
        new(false, false, false, false, false), null);

static async Task DisconnectClearsSession()
{
    var hostPath = CreateTemporaryPlaybackHost();
    try
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal)
        {
            WindowsSpotifyPlatformBackend.StreamingScope,
            WindowsSpotifyPlatformBackend.UserReadEmailScope,
            WindowsSpotifyPlatformBackend.UserReadPrivateScope,
        };
        var vault = new FakeVault("durable-refresh", scopes);
        var client = new FakeLocalPlaybackHostClient("private-disconnect-device");
        WindowsSpotifyPlatformBackend? backend = null;
        var manager = new SpotifyLocalPlaybackManager(
            hostPath,
            (identity, requiredScopes, cancellationToken) =>
                backend!.AcquireTrustedHostAccessTokenAsync(
                    identity, requiredScopes, cancellationToken),
            () => client);
        var tokenCalls = 0;
        var apiCalls = 0;
        var http = new FakeHttp(request =>
        {
            if (request.Uri.Host == "accounts.spotify.com")
            {
                tokenCalls++;
                return Json(200, Token(
                    "pre-disconnect-access", null,
                    string.Join(' ', LocalPlaybackScopes())));
            }
            apiCalls++;
            throw new InvalidOperationException(
                "A disconnected session must not reach the Spotify API.");
        });
        await using (backend = Backend(
            new FakeConfigurationStore("Client123456789"), vault, http,
            new NullBrowser(), new NullCallback(), localPlayback: manager))
        {
            await manager.StartAsync(Identity(), default);
            Assert.True(client.IsRunning);
            Assert.Equal(1, tokenCalls);

            await backend.DisconnectAsync(Identity(), default);

            Assert.Equal(null, vault.Token);
            Assert.Equal(1, vault.DeleteCalls);
            Assert.True(!client.IsRunning);
            Assert.Equal(1, client.DisposeCalls);
            Assert.Equal(BrokerSpotifyLocalPlaybackState.Disabled,
                manager.GetSummary(Identity()).State);

            await Assert.ThrowsAsync<SpotifyProviderException>(
                () => backend.GetPlaybackAsync(Identity(), default), "not_connected");
            Assert.Equal(1, tokenCalls);
            Assert.Equal(0, apiCalls);
        }
    }
    finally { File.Delete(hostPath); }
}

static async Task LocalPlaybackErrorStates()
{
    var hostPath = CreateTemporaryPlaybackHost();
    try
    {
        async Task AssertSdkErrorAsync(
            string hostCode,
            string expectedCode,
            BrokerSpotifyLocalPlaybackState expected)
        {
            var client = new FakeLocalPlaybackHostClient(
                "private-device", connectErrorCode: hostCode);
            var manager = new SpotifyLocalPlaybackManager(
                hostPath,
                (_, _, _) => Task.FromResult(new TrustedHostSpotifyAccessToken(
                    "initial-token", DateTimeOffset.UtcNow.AddMinutes(10),
                    LocalPlaybackScopes())),
                () => client);
            await using var backend = Backend(
                new FakeConfigurationStore("Client123456789"), new FakeVault("refresh"),
                new FakeHttp(_ => throw new InvalidOperationException(
                    "A failed local host must not reach the Web API.")),
                new NullBrowser(), new NullCallback(), localPlayback: manager);
            var identity = Identity();
            try
            {
                await backend.ControlSpotifyLocalPlaybackAsync(identity,
                    new SpotifyLocalPlaybackCommand(
                        SpotifyLocalPlaybackOperation.StartAndTransfer, null, true), default);
                throw new InvalidOperationException("Expected local playback to fail.");
            }
            catch (SpotifyApplicationException exception)
            {
                Assert.Equal(expectedCode, exception.Code);
            }
            var summary = await backend.GetSpotifyLocalPlaybackAsync(identity, default);
            Assert.Equal(expected, summary.State);
            Assert.True(!string.IsNullOrWhiteSpace(summary.DisplayMessage));
            Assert.Equal(1, client.DisposeCalls);
        }

        await AssertSdkErrorAsync(
            "account_error", "premium_required",
            BrokerSpotifyLocalPlaybackState.PremiumRequired);
        await AssertSdkErrorAsync(
            "authentication_error", "reauthorization_required",
            BrokerSpotifyLocalPlaybackState.ReauthorizationRequired);
        await AssertSdkErrorAsync(
            "playback_error", "playback_error", BrokerSpotifyLocalPlaybackState.Error);

        async Task AssertTokenFailureAsync(string code)
        {
            var factoryCalls = 0;
            var reauthorization = new SpotifyLocalPlaybackManager(
                hostPath,
                (_, _, _) => Task.FromException<TrustedHostSpotifyAccessToken>(
                    new SpotifyProviderException(code, "Spotify authorization is incomplete.")),
                () =>
                {
                    factoryCalls++;
                    return new FakeLocalPlaybackHostClient("unused-device");
                });
            await using var reauthorizationBackend = Backend(
                new FakeConfigurationStore("Client123456789"), new FakeVault("refresh"),
                new FakeHttp(_ => throw new InvalidOperationException()),
                new NullBrowser(), new NullCallback(), localPlayback: reauthorization);
            var brokerIdentity = Identity();
            try
            {
                await reauthorizationBackend.ControlSpotifyLocalPlaybackAsync(
                    brokerIdentity,
                    new SpotifyLocalPlaybackCommand(
                        SpotifyLocalPlaybackOperation.StartAndTransfer, null, true), default);
                throw new InvalidOperationException("Expected reauthorization to be required.");
            }
            catch (SpotifyApplicationException exception)
            {
                Assert.Equal(code, exception.Code);
            }
            var authorizationSummary = await reauthorizationBackend
                .GetSpotifyLocalPlaybackAsync(brokerIdentity, default);
            Assert.Equal(
                BrokerSpotifyLocalPlaybackState.ReauthorizationRequired,
                authorizationSummary.State);
            Assert.Equal(0, factoryCalls);
        }

        await AssertTokenFailureAsync("authorization_expired");
        await AssertTokenFailureAsync("authorization_scope_required");
    }
    finally { File.Delete(hostPath); }
}

static async Task LocalPlaybackCancellation()
{
    var hostPath = CreateTemporaryPlaybackHost();
    try
    {
        var client = new FakeLocalPlaybackHostClient(
            "private-device", raiseReadyOnConnect: false);
        var manager = new SpotifyLocalPlaybackManager(
            hostPath,
            (_, _, _) => Task.FromResult(new TrustedHostSpotifyAccessToken(
                "initial-token", DateTimeOffset.UtcNow.AddMinutes(10),
                LocalPlaybackScopes())),
            () => client);
        await using var backend = Backend(
            new FakeConfigurationStore("Client123456789"), new FakeVault("refresh"),
            new FakeHttp(_ => throw new InvalidOperationException(
                "Canceled startup must not reach the Web API.")),
            new NullBrowser(), new NullCallback(), localPlayback: manager);
        var identity = Identity();
        using var cancellation = new CancellationTokenSource();

        var start = backend.ControlSpotifyLocalPlaybackAsync(identity,
            new SpotifyLocalPlaybackCommand(
                SpotifyLocalPlaybackOperation.StartAndTransfer, null, true),
            cancellation.Token);
        await WaitUntilAsync(() => manager.GetSummary(Identity()).State ==
            BrokerSpotifyLocalPlaybackState.Starting);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => start, string.Empty);

        var summary = await backend.GetSpotifyLocalPlaybackAsync(identity, default);
        Assert.Equal(BrokerSpotifyLocalPlaybackState.Disabled, summary.State);
        Assert.Equal(1, client.DisposeCalls);
        Assert.True(!client.IsRunning);
    }
    finally { File.Delete(hostPath); }
}

static async Task LocalPlaybackBackendDisposal()
{
    var hostPath = CreateTemporaryPlaybackHost();
    try
    {
        var client = new FakeLocalPlaybackHostClient("private-disposal-device");
        var manager = new SpotifyLocalPlaybackManager(
            hostPath,
            (_, _, _) => Task.FromResult(new TrustedHostSpotifyAccessToken(
                "initial-token", DateTimeOffset.UtcNow.AddMinutes(10),
                LocalPlaybackScopes())),
            () => client);
        var http = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
            ? Json(200, Token("web-api-token", null))
            : new SpotifyHttpResponse(204, string.Empty, EmptyHeaders()));
        var backend = Backend(
            new FakeConfigurationStore("Client123456789"), new FakeVault("refresh"),
            http, new NullBrowser(), new NullCallback(), localPlayback: manager);
        var identity = Identity();
        await backend.ControlSpotifyLocalPlaybackAsync(identity,
            new SpotifyLocalPlaybackCommand(
                SpotifyLocalPlaybackOperation.StartAndTransfer, null, false), default);
        Assert.True(client.IsRunning);

        await backend.DisposeAsync();
        Assert.True(!client.IsRunning);
        Assert.Equal(1, client.DisposeCalls);
        await backend.DisposeAsync();
        Assert.Equal(1, client.DisposeCalls);
    }
    finally { File.Delete(hostPath); }
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
        Identity(), ["playlist-modify-public"], default), "invalid_scope");
}

static WindowsSpotifyPlatformBackend Backend(
    ISpotifyClientConfigurationStore configuration,
    ISpotifyTokenVault vault,
    ISpotifyHttpTransport http,
    ISpotifyBrowserLauncher browser,
    ISpotifyAuthorizationCallbackReceiver callback,
    ISpotifyDelay? delay = null,
    SpotifyLocalPlaybackManager? localPlayback = null) =>
    new(configuration, vault, http, browser, callback,
        delay ?? new FakeDelay(), new ManualTimeProvider(), localPlayback);

static string CreateTemporaryPlaybackHost()
{
    var path = Path.Combine(Path.GetTempPath(), $"wrail-spotify-host-{Guid.NewGuid():N}.exe");
    File.WriteAllBytes(path, [0x4d, 0x5a]);
    return path;
}

static async Task WaitUntilAsync(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    while (!condition()) await Task.Delay(10, timeout.Token);
}

static SpotifyIntegrationIdentity Identity() => new("dev.publisher", "dev.spotify.widget");

static string[] LocalPlaybackScopes() =>
[
    WindowsSpotifyPlatformBackend.StreamingScope,
    WindowsSpotifyPlatformBackend.UserReadEmailScope,
    WindowsSpotifyPlatformBackend.UserReadPrivateScope,
];

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

internal sealed class AsyncFakeHttp(
    Func<SpotifyHttpRequest, CancellationToken, Task<SpotifyHttpResponse>> handler) :
    ISpotifyHttpTransport
{
    internal ConcurrentQueue<SpotifyHttpRequest> Requests { get; } = new();

    public Task<SpotifyHttpResponse> SendAsync(
        SpotifyHttpRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        return handler(request, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeAuthorizedSender(
    Func<SpotifyAuthorizedRequest, SpotifyHttpResponse> handler) :
    ISpotifyAuthorizedRequestSender
{
    internal List<SpotifyAuthorizedRequest> Requests { get; } = [];

    public Task<SpotifyHttpResponse> SendAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyAuthorizedRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = identity.Authority;
        Requests.Add(request);
        return Task.FromResult(handler(request));
    }
}

internal sealed class CoordinatedCallback : ISpotifyAuthorizationCallbackReceiver
{
    private readonly TaskCompletionSource<SpotifyAuthorizationCallback> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string? RedirectUri { get; private set; }
    internal string? State { get; private set; }
    internal string? Challenge { get; private set; }
    internal string? ChallengeMethod { get; private set; }
    internal TimeSpan Timeout { get; private set; }

    public Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri, string expectedState, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        RedirectUri = exactRedirectUri.AbsoluteUri;
        Timeout = timeout;
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

internal sealed class RecordingBrowser : ISpotifyBrowserLauncher
{
    internal int OpenCalls { get; private set; }

    public Task OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class SignalingBrowser : ISpotifyBrowserLauncher
{
    internal TaskCompletionSource<Uri> Opened { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Opened.TrySetResult(uri);
        return Task.CompletedTask;
    }
}

internal sealed class NullCallback : ISpotifyAuthorizationCallbackReceiver
{
    public Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri, string expectedState, TimeSpan timeout,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Callback was not expected.");
}

internal sealed class CancellationAwareCallback : ISpotifyAuthorizationCallbackReceiver
{
    internal bool WasCanceled { get; private set; }

    public Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri, string expectedState, TimeSpan timeout,
        CancellationToken cancellationToken)
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

internal sealed class FakeLocalPlaybackHostClient(
    string readyDeviceId,
    string? connectErrorCode = null,
    bool raiseReadyOnConnect = true) : ISpotifyPlaybackHostClient
{
    private readonly object _gate = new();
    private bool _isRunning;

    public event EventHandler<SpotifyPlaybackEventEnvelope>? EventReceived;
    public bool IsRunning
    {
        get { lock (_gate) return _isRunning; }
    }

    internal int StartCalls { get; private set; }
    internal int DisposeCalls { get; private set; }
    internal SpotifyPlaybackConnectOptions? ConnectOptions { get; private set; }
    internal List<(string Type, JsonElement Payload)> Commands { get; } = [];
    internal Action<string>? CommandObserved { get; set; }
    internal string? FailCommandType { get; set; }
    internal TaskCompletionSource? CommandWait { get; set; }
    internal TaskCompletionSource<(string RequestId, TrustedHostSpotifyAccessToken Token)>
        TokenProvided { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCalls++;
        lock (_gate) _isRunning = true;
        return Task.CompletedTask;
    }

    public Task<SpotifyPlaybackEventEnvelope> ConnectAsync(
        SpotifyPlaybackConnectOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectOptions = options;
        if (connectErrorCode is null && raiseReadyOnConnect)
            Raise("ready", new { deviceId = readyDeviceId });
        else if (connectErrorCode is not null)
            Raise("sdk_error", new { code = connectErrorCode, message = "Host failure" });
        return Task.FromResult(Envelope("command_completed", new { }));
    }

    public Task<SpotifyPlaybackEventEnvelope> ProvideTokenAsync(
        string tokenRequestId,
        TrustedHostSpotifyAccessToken token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TokenProvided.TrySetResult((tokenRequestId, token));
        return Task.FromResult(Envelope("command_completed", new { }));
    }

    public async Task<SpotifyPlaybackEventEnvelope> SendAsync(
        string type, object? payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var element = JsonSerializer.SerializeToElement(payload ?? new { });
        lock (_gate) Commands.Add((type, element));
        CommandObserved?.Invoke(type);
        if (CommandWait is not null)
            await CommandWait.Task.WaitAsync(cancellationToken);
        if (type == FailCommandType)
            throw new SpotifyPlaybackHostClientException(
                "sdk_command_failed", "The local SDK command failed.");
        return Envelope("command_completed", new { });
    }

    internal void Raise(string type, object payload) =>
        EventReceived?.Invoke(this, Envelope(type, payload));

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        lock (_gate) _isRunning = false;
        return ValueTask.CompletedTask;
    }

    private static SpotifyPlaybackEventEnvelope Envelope(string type, object payload) =>
        SpotifyPlaybackProtocolCodec.DecodeEvent(
            SpotifyPlaybackProtocolCodec.EncodeEvent(new(
                SpotifyPlaybackProtocol.Version, type, null, payload)));
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
