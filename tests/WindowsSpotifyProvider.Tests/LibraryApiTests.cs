using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WindowsSpotifyProvider;

internal static class LibraryApiTests
{
    private const string Track = "spotify:track:11dFghVXANMlKmJXsNCbNl";
    private const string OtherTrack = "spotify:track:4iV5W9uYEdYUVa79Axb7Rh";
    private static SpotifyIntegrationIdentity Identity => new("test", "library");
    private static SpotifyHttpResponse Response(int code, string body = "") =>
        new(code, body, new Dictionary<string, string>());

    internal static async Task ExactEndpoints()
    {
        var sender = new FakeAuthorizedSender(request =>
            request.Method == HttpMethod.Get ? Response(200, "[true,false,true]") : Response(204));
        var api = new SpotifyLibraryApi(sender);
        var uris = new[] { Track, OtherTrack, Track };
        var saved = await api.GetSavedItemsAsync(Identity, uris, default);
        Assert.SequenceEqual(new[] { true, false, true }, saved);
        Assert.Equal(1, sender.Requests.Count);
        Assert.Equal("https://api.spotify.com/v1/me/library/contains?uris=" +
            Uri.EscapeDataString(string.Join(',', uris)), sender.Requests[0].Uri.AbsoluteUri);
        Assert.Equal(WindowsSpotifyPlatformBackend.LibraryReadScope, sender.Requests[0].RequiredScope);

        await api.SetSavedItemAsync(Identity, Track, true, default);
        await api.SetSavedItemAsync(Identity, Track, false, default);
        Assert.Equal(HttpMethod.Put, sender.Requests[1].Method);
        Assert.Equal(HttpMethod.Delete, sender.Requests[2].Method);
        foreach (var request in sender.Requests.Skip(1))
        {
            Assert.Equal("https://api.spotify.com/v1/me/library?uris=" +
                Uri.EscapeDataString(Track), request.Uri.AbsoluteUri);
            Assert.Equal(WindowsSpotifyPlatformBackend.LibraryModifyScope, request.RequiredScope);
            Assert.Equal(null, request.JsonBody);
        }
        Assert.True(SpotifyApplicationContract.RequiredAuthorizationScopes.Contains(SpotifyAuthorizationScope.LibraryRead));
        Assert.True(SpotifyApplicationContract.RequiredAuthorizationScopes.Contains(SpotifyAuthorizationScope.LibraryModify));
    }

    internal static async Task BoundsAndResponseValidation()
    {
        var sender = new FakeAuthorizedSender(_ => Response(200, "[]"));
        var api = new SpotifyLibraryApi(sender);
        foreach (var invalid in new[] { "spotify:episode:11dFghVXANMlKmJXsNCbNl",
                     "spotify:local:track", Track + "&uris=other", "https://open.spotify.com/track/abc", "" })
        {
            await Assert.ThrowsAsync<SpotifyProviderException>(() =>
                api.SetSavedItemAsync(Identity, invalid, true, default), "invalid_request");
            await Assert.ThrowsAsync<SpotifyProviderException>(() =>
                api.GetSavedItemsAsync(Identity, [Track, invalid], default), "invalid_request");
        }
        foreach (var count in new[] { 0, 41 })
            await Assert.ThrowsAsync<SpotifyProviderException>(() =>
                api.GetSavedItemsAsync(Identity, Enumerable.Repeat(Track, count).ToArray(), default), "invalid_request");
        Assert.Equal(0, sender.Requests.Count);

        var maximumApi = new SpotifyLibraryApi(new FakeAuthorizedSender(_ =>
            Response(200, "[" + string.Join(',', Enumerable.Repeat("true", 40)) + "]")));
        Assert.Equal(40, (await maximumApi.GetSavedItemsAsync(Identity,
            Enumerable.Repeat(Track, 40).ToArray(), default)).Count);
        foreach (var body in new[] { "[]", "[true,false]", "{}", "[null]", "[1]", "[\"true\"]",
                     "[true", "[[true]]", new string(' ', 4097) })
        {
            var invalidApi = new SpotifyLibraryApi(new FakeAuthorizedSender(_ => Response(200, body)));
            await Assert.ThrowsAsync<SpotifyProviderException>(() =>
                invalidApi.GetSavedItemsAsync(Identity, [Track], default), "invalid_response");
        }
        var failedApi = new SpotifyLibraryApi(new FakeAuthorizedSender(_ => Response(403,
            "{\"error\":{\"message\":\"secret server body\"}}")));
        await Assert.ThrowsAsync<SpotifyProviderException>(() =>
            failedApi.SetSavedItemAsync(Identity, Track, true, default), "forbidden");
    }

    internal static async Task AuthorizationAndSafeErrors()
    {
        var apiCalls = 0;
        var forbid = false;
        var scopes = new HashSet<string>(StringComparer.Ordinal)
        {
            WindowsSpotifyPlatformBackend.PlaybackReadScope,
            WindowsSpotifyPlatformBackend.PlaybackControlScope,
            WindowsSpotifyPlatformBackend.LibraryReadScope,
            WindowsSpotifyPlatformBackend.LibraryModifyScope,
        };
        var http = new FakeHttp(request =>
        {
            if (request.Uri.Host == "accounts.spotify.com")
                return Response(200, "{\"access_token\":\"private-access\",\"token_type\":\"Bearer\"," +
                    "\"expires_in\":3600,\"scope\":\"" + string.Join(' ', scopes) + "\"}");
            apiCalls++;
            Assert.Equal("Bearer private-access", request.Headers!["Authorization"]);
            if (forbid) return Response(403, "{\"error\":{\"message\":\"secret server body\"}}");
            return request.Method == HttpMethod.Get ? Response(200, "[true]") : Response(204);
        });
        await using var backend = new WindowsSpotifyPlatformBackend(
            new FakeConfigurationStore("Client123456789"), new FakeVault("refresh", scopes),
            http, new NullBrowser(), new NullCallback(), new FakeDelay(), TimeProvider.System);
        Assert.True((await backend.GetSpotifySavedItemsAsync(Identity, [Track], default))[0]);
        await backend.SetSpotifySavedItemAsync(Identity, Track, true, default);
        Assert.Equal(2, apiCalls);
        forbid = true;
        var error = await Assert.ThrowsAsync<SpotifyApplicationException>(() =>
            backend.SetSpotifySavedItemAsync(Identity, Track, false, default), "");
        Assert.Equal("forbidden", error.Code);
        Assert.True(!error.Message.Contains("secret server body", StringComparison.Ordinal));

        await using var insufficient = new WindowsSpotifyPlatformBackend(
            new FakeConfigurationStore("Client123456789"), new FakeVault("refresh"),
            new FakeHttp(request => request.Uri.Host == "accounts.spotify.com"
                ? Response(200, "{\"access_token\":\"limited\",\"token_type\":\"Bearer\",\"expires_in\":3600}")
                : throw new InvalidOperationException("Missing scope must not call library API.")),
            new NullBrowser(), new NullCallback(), new FakeDelay(), TimeProvider.System);
        var permissionError = await Assert.ThrowsAsync<SpotifyApplicationException>(() =>
            insufficient.GetSpotifySavedItemsAsync(Identity, [Track], default), "");
        Assert.Equal("authorization_scope_required", permissionError.Code);
    }

    internal static async Task CancellationAndCallerMutation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<SpotifyHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new SpotifyAuthorizedRequestSender((_, _, _) =>
        {
            entered.TrySetResult();
            return completion.Task;
        });
        var api = new SpotifyLibraryApi(sender);
        using var cancellation = new CancellationTokenSource();
        var pending = api.GetSavedItemsAsync(Identity, [Track], cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        completion.SetResult(Response(200, "[true]"));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending, "");

        using var writeCancellation = new CancellationTokenSource();
        var canceledWriter = new SpotifyLibraryApi(new SpotifyAuthorizedRequestSender((_, _, _) =>
        {
            writeCancellation.Cancel();
            return Task.FromResult(Response(204));
        }));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            canceledWriter.SetSavedItemAsync(Identity, Track, true, writeCancellation.Token), "");

        var tracks = new List<string> { Track, OtherTrack };
        var mutatingSender = new FakeAuthorizedSender(request =>
        {
            tracks.Clear();
            Assert.Equal("https://api.spotify.com/v1/me/library/contains?uris=" +
                Uri.EscapeDataString(Track + "," + OtherTrack), request.Uri.AbsoluteUri);
            return Response(200, "[true,false]");
        });
        Assert.SequenceEqual(new[] { true, false }, await new SpotifyLibraryApi(mutatingSender)
            .GetSavedItemsAsync(Identity, tracks, default));
    }
}
