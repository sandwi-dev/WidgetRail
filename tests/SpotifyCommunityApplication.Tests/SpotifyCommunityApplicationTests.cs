using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WindowsSpotifyProvider;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WidgetRail.SpotifyCommunityApplication.Tests;

[TestClass]
public sealed class SpotifyCommunityApplicationTests
{
    private static readonly SpotifyIntegrationIdentity Identity =
        new("widgetrail.samples", "widgetrail.samples.spotify");

    [TestMethod]
    public async Task ExistingPackageConfigurationFormatSurvivesApplicationRestart()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "wrail-spotify-community-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new SpotifyClientConfigurationFileStore(root);
            await first.WriteAsync(
                Identity, new SpotifyClientConfiguration("Client123456789"), default);
            var second = new SpotifyClientConfigurationFileStore(root);
            Assert.AreEqual("Client123456789",
                (await second.ReadAsync(Identity, default))?.ClientId);
            var document = Directory.GetFiles(root, "*.json").Single();
            var json = await File.ReadAllTextAsync(document);
            StringAssert.Contains(json, "\"packageId\": \"widgetrail.samples.spotify\"");
            StringAssert.Contains(json, "\"publisherId\": \"widgetrail.samples\"");
            Assert.IsFalse(json.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SetupAuthorizationAndRestartStayPackageOwned()
    {
        var configuration = new FakeConfigurationStore("Client123456789");
        var vault = new FakeVault();
        var callback = new CoordinatedCallback();
        var browser = new CoordinatedBrowser(callback);
        var http = new FakeHttp(request =>
        {
            Assert.AreEqual("https://accounts.spotify.com/api/token",
                request.Uri.AbsoluteUri);
            var form = ParseForm(request.FormBody!);
            Assert.AreEqual("authorization_code", form["grant_type"]);
            Assert.IsFalse(form.ContainsKey("client_secret"));
            return Json(200, Token("access-one", "refresh-one",
                "user-read-playback-state user-modify-playback-state streaming " +
                "user-read-email user-read-private playlist-read-private " +
                "playlist-read-collaborative"));
        });

        await using (var backend = Backend(configuration, vault, http, browser, callback))
        await using (var service = new SpotifyApplicationService(backend, Identity))
        {
            var setup = await service.GetConfigurationAsync();
            Assert.IsTrue(setup.IsConfigured);
            Assert.AreEqual(SpotifyApplicationContract.ExactRedirectUri, setup.RedirectUri);
            Assert.AreEqual(SpotifyAuthorizationState.Disconnected,
                (await service.GetAuthorizationAsync()).State);
            var connected = await service.ConnectAsync(
                SpotifyApplicationContract.RequiredAuthorizationScopes);
            Assert.AreEqual(SpotifyAuthorizationState.Connected, connected.State);
            Assert.AreEqual("refresh-one", vault.Token);
            Assert.AreEqual(1, browser.OpenCalls);
            Assert.AreEqual("S256", callback.ChallengeMethod);
        }

        var restartHttp = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
            ? Json(200, Token("access-two", null,
                "user-read-playback-state user-modify-playback-state streaming " +
                "user-read-email user-read-private playlist-read-private " +
                "playlist-read-collaborative"))
            : new SpotifyHttpResponse(204, string.Empty, EmptyHeaders()));
        await using var restartedBackend = Backend(configuration, vault, restartHttp,
            new NullBrowser(), new NullCallback());
        await using var restarted = new SpotifyApplicationService(restartedBackend, Identity);
        Assert.AreEqual(SpotifyAuthorizationState.Connected,
            (await restarted.GetAuthorizationAsync()).State);
        Assert.IsFalse((await restarted.GetPlaybackAsync()).IsAvailable);
    }

    [TestMethod]
    public async Task ConfigureClientWritesAndInvalidatesPriorCredential()
    {
        var configuration = new FakeConfigurationStore("Client123456789");
        var vault = new FakeVault("old-refresh");
        await using var backend = Backend(configuration, vault,
            new FakeHttp(_ => throw new AssertFailedException(
                "Configuration must not make a Spotify network request.")),
            new NullBrowser(), new NullCallback());
        await using var service = new SpotifyApplicationService(backend, Identity);

        var result = await service.ConfigureClientAsync("ReplacementClient123456");

        Assert.IsTrue(result.IsConfigured);
        Assert.AreEqual("ReplacementClient123456", configuration.ClientId);
        Assert.AreEqual(1, vault.DeleteCalls);
        Assert.IsNull(vault.Token);
    }

    [TestMethod]
    public async Task PlayerQueuePlaylistsAndDevicesUseDirectWebApi()
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal)
        {
            WindowsSpotifyPlatformBackend.PlaybackReadScope,
            WindowsSpotifyPlatformBackend.PlaybackControlScope,
            WindowsSpotifyPlatformBackend.PlaylistReadPrivateScope,
            WindowsSpotifyPlatformBackend.PlaylistReadCollaborativeScope,
        };
        var http = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
            ? Json(200, Token("access", null, string.Join(' ', scopes)))
            : ApiResponse(request));
        await using var backend = Backend(
            new FakeConfigurationStore("Client123456789"),
            new FakeVault("refresh", scopes), http, new NullBrowser(), new NullCallback());
        await using var service = new SpotifyApplicationService(backend, Identity);

        var playback = await service.GetPlaybackAsync();
        Assert.AreEqual("Current", playback.Item?.Title);
        Assert.IsTrue(playback.IsPlaying);
        var devices = await service.GetDevicesAsync();
        Assert.AreEqual("Living Room", devices.Devices.Single().Name);
        var queue = await service.GetQueueAsync();
        Assert.AreEqual("Next", queue.Items.Single().Title);
        var playlists = await service.GetPlaylistsAsync(0, 12);
        Assert.AreEqual("Road Trip", playlists.Items.Single().Name);
        var detail = await service.GetPlaylistItemsAsync("playlist-1", 0, 12);
        Assert.AreEqual("Detail Track", detail.Items.Single().Title);
    }

    [TestMethod]
    public async Task MutationsAndDisconnectRetainExactSessionOwnership()
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal)
        {
            WindowsSpotifyPlatformBackend.PlaybackReadScope,
            WindowsSpotifyPlatformBackend.PlaybackControlScope,
        };
        var requests = new ConcurrentQueue<SpotifyHttpRequest>();
        var vault = new FakeVault("refresh", scopes);
        var http = new FakeHttp(request =>
        {
            if (request.Uri.Host == "accounts.spotify.com")
                return Json(200, Token("access", null, string.Join(' ', scopes)));
            requests.Enqueue(request);
            return new SpotifyHttpResponse(204, string.Empty, EmptyHeaders());
        });
        await using var backend = Backend(
            new FakeConfigurationStore("Client123456789"), vault, http,
            new NullBrowser(), new NullCallback());
        await using var service = new SpotifyApplicationService(backend, Identity);

        await service.ControlPlaybackAsync(new(SpotifyPlaybackOperation.Pause));
        await service.AddToQueueAsync("spotify:track:next");
        await service.StartPlaybackAsync(new(
            "spotify:playlist:playlist-1", null, Offset: 2));
        CollectionAssert.AreEqual(
            new[] { HttpMethod.Put, HttpMethod.Post, HttpMethod.Put },
            requests.Select(request => request.Method).ToArray());

        Assert.AreEqual(SpotifyAuthorizationState.Disconnected,
            (await service.DisconnectAsync()).State);
        Assert.IsNull(vault.Token);
        Assert.AreEqual(1, vault.DeleteCalls);
    }

    [TestMethod]
    public async Task ProviderErrorsAreTypedAndSanitizedAtApplicationBoundary()
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal)
        {
            WindowsSpotifyPlatformBackend.PlaybackReadScope,
            WindowsSpotifyPlatformBackend.PlaybackControlScope,
        };
        var http = new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
            ? Json(200, Token("access", null, string.Join(' ', scopes)))
            : Json(500, "{\"error\":{\"status\":500,\"message\":\"secret body\"}}"));
        await using var backend = Backend(
            new FakeConfigurationStore("Client123456789"),
            new FakeVault("refresh", scopes), http, new NullBrowser(), new NullCallback());
        await using var service = new SpotifyApplicationService(backend, Identity);

        var error = await Assert.ThrowsExactlyAsync<SpotifyApplicationException>(async () =>
            await service.GetPlaybackAsync());
        Assert.AreEqual("spotify_unavailable", error.Code);
        Assert.IsFalse(error.Message.Contains("secret body", StringComparison.Ordinal));
    }

    private static WindowsSpotifyPlatformBackend Backend(
        ISpotifyClientConfigurationStore configuration,
        ISpotifyTokenVault vault,
        ISpotifyHttpTransport http,
        ISpotifyBrowserLauncher browser,
        ISpotifyAuthorizationCallbackReceiver callback) => new(
        configuration, vault, http, browser, callback, new FakeDelay(),
        new ManualTimeProvider(), new SpotifyLocalPlaybackManager(
            Path.Combine(Path.GetTempPath(), "spotify-host-fixture.exe"),
            (_, _, _) => Task.FromException<TrustedHostSpotifyAccessToken>(
                new InvalidOperationException("Local playback was not expected.")),
            () => new NullPlaybackHostClient()));

    private static SpotifyHttpResponse ApiResponse(SpotifyHttpRequest request)
    {
        return request.Uri.AbsolutePath switch
        {
            "/v1/me/player" => Json(200, """
                {"is_playing":true,"progress_ms":45000,"repeat_state":"off",
                 "shuffle_state":false,"device":{"volume_percent":45},
                 "actions":{"disallows":{}},"context":{"type":"playlist","uri":"spotify:playlist:playlist-1"},
                 "item":{"type":"track","id":"current","name":"Current","duration_ms":240000,
                 "uri":"spotify:track:current","artists":[{"name":"Artist"}],
                 "album":{"name":"Album","images":[]}}}
                """),
            "/v1/me/player/devices" => Json(200, """
                {"devices":[{"id":"device-1","is_active":true,"is_private_session":false,
                 "is_restricted":false,"name":"Living Room","type":"Speaker","volume_percent":45}]}
                """),
            "/v1/me/player/queue" => Json(200, """
                {"currently_playing":null,"queue":[{"type":"track","id":"next","name":"Next",
                 "duration_ms":200000,"uri":"spotify:track:next","artists":[{"name":"Artist"}],
                 "album":{"images":[]}}]}
                """),
            "/v1/me/playlists" => Json(200, """
                {"items":[{"collaborative":false,"description":"Drive","id":"playlist-1",
                 "images":[],"name":"Road Trip","owner":{"display_name":"Owner"},
                 "public":false,"items":{"total":1}}],"limit":12,"offset":0,"total":1}
                """),
            "/v1/playlists/playlist-1" => Json(200, """
                {"collaborative":false,"description":"Drive","id":"playlist-1","images":[],
                 "name":"Road Trip","owner":{"display_name":"Owner"},"public":false,
                 "items":{"total":1}}
                """),
            "/v1/playlists/playlist-1/items" => Json(200, """
                {"items":[{"item":{"type":"track","id":"detail","name":"Detail Track","duration_ms":180000,
                 "uri":"spotify:track:detail","artists":[{"name":"Artist"}],"album":{"images":[]}}}],
                 "limit":12,"offset":0,"total":1}
                """),
            _ => throw new AssertFailedException($"Unexpected Spotify API path {request.Uri}."),
        };
    }

    private static SpotifyHttpResponse Json(int status, string body) =>
        new(status, body, EmptyHeaders());

    private static IReadOnlyDictionary<string, string> EmptyHeaders() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static string Token(string access, string? refresh, string scopes)
    {
        var refreshProperty = refresh is null ? string.Empty :
            $",\"refresh_token\":\"{refresh}\"";
        return $"{{\"access_token\":\"{access}\",\"token_type\":\"Bearer\"," +
            $"\"expires_in\":3600,\"scope\":\"{scopes}\"{refreshProperty}}}";
    }

    private static Dictionary<string, string> ParseForm(string form) => form
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
            parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')),
            StringComparer.Ordinal);
}

internal sealed class FakeConfigurationStore(string? clientId) : ISpotifyClientConfigurationStore
{
    private SpotifyClientConfiguration? _value = clientId is null
        ? null : new(clientId);

    internal string? ClientId => _value?.ClientId;

    public Task<SpotifyClientConfiguration?> ReadAsync(
        SpotifyIntegrationIdentity identity,
        CancellationToken cancellationToken) => Task.FromResult(_value);

    public Task WriteAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyClientConfiguration configuration,
        CancellationToken cancellationToken)
    {
        _value = configuration;
        return Task.CompletedTask;
    }
}

internal sealed class FakeVault(
    string? token = null,
    IReadOnlySet<string>? scopes = null) : ISpotifyTokenVault
{
    private SpotifyRefreshCredential? _credential = token is null ? null : new(
        "Client123456789", token, scopes ?? new HashSet<string>(StringComparer.Ordinal));

    internal string? Token => _credential?.RefreshToken;
    internal int DeleteCalls { get; private set; }

    public Task<SpotifyRefreshCredential?> ReadAsync(
        SpotifyIntegrationIdentity identity,
        CancellationToken cancellationToken) => Task.FromResult(_credential);

    public Task SaveAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyRefreshCredential credential,
        CancellationToken cancellationToken)
    {
        _credential = credential;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        SpotifyIntegrationIdentity identity,
        CancellationToken cancellationToken)
    {
        DeleteCalls++;
        _credential = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakeHttp(Func<SpotifyHttpRequest, SpotifyHttpResponse> handler) :
    ISpotifyHttpTransport
{
    public Task<SpotifyHttpResponse> SendAsync(
        SpotifyHttpRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(handler(request));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class CoordinatedCallback : ISpotifyAuthorizationCallbackReceiver
{
    private readonly TaskCompletionSource<SpotifyAuthorizationCallback> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal string? ChallengeMethod { get; private set; }

    public Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri,
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
        return _completion.Task;
    }

    internal void BrowserOpened(Uri uri)
    {
        var query = Parse(uri.Query);
        ChallengeMethod = query["code_challenge_method"];
        _completion.TrySetResult(new(
            "authorization-code", query["state"], null, null));
    }

    private static Dictionary<string, string> Parse(string query) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')),
            parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')),
            StringComparer.Ordinal);
}

internal sealed class CoordinatedBrowser(CoordinatedCallback callback) : ISpotifyBrowserLauncher
{
    internal int OpenCalls { get; private set; }

    public Task OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        OpenCalls++;
        callback.BrowserOpened(uri);
        return Task.CompletedTask;
    }
}

internal sealed class NullBrowser : ISpotifyBrowserLauncher
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken) =>
        Task.FromException(new AssertFailedException("Browser was not expected."));
}

internal sealed class NullCallback : ISpotifyAuthorizationCallbackReceiver
{
    public Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri,
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        Task.FromException<SpotifyAuthorizationCallback>(
            new AssertFailedException("Callback was not expected."));
}

internal sealed class FakeDelay : ISpotifyDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

internal sealed class ManualTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1_000_000);
}

internal sealed class NullPlaybackHostClient : ISpotifyPlaybackHostClient
{
    public event EventHandler<WidgetRail.SpotifyPlayback.SpotifyPlaybackEventEnvelope>?
        EventReceived { add { } remove { } }
    public bool IsRunning => false;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<WidgetRail.SpotifyPlayback.SpotifyPlaybackEventEnvelope> ConnectAsync(
        WidgetRail.SpotifyPlayback.SpotifyPlaybackConnectOptions options,
        CancellationToken cancellationToken) => throw new AssertFailedException();
    public Task<WidgetRail.SpotifyPlayback.SpotifyPlaybackEventEnvelope> ProvideTokenAsync(
        string tokenRequestId,
        TrustedHostSpotifyAccessToken token,
        CancellationToken cancellationToken) => throw new AssertFailedException();
    public Task<WidgetRail.SpotifyPlayback.SpotifyPlaybackEventEnvelope> SendAsync(
        string type,
        object? payload,
        CancellationToken cancellationToken) => throw new AssertFailedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
