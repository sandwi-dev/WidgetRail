using System.Text.Json;
using WidgetRail.WindowsSpotifyProvider;
using WidgetRail.SpotifyPlayback;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Premium and streaming scope are explicit preconditions", PreconditionsContract),
    ("Token lease is bounded expiring scoped and redacted", TokenLeaseContract),
    ("Protocol is exact versioned and bounded", ProtocolContract),
    ("Events are camel-case bounded envelopes", EventContract),
    ("Page playback state rejects unsafe metadata and artwork", PageStateContract),
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
Console.WriteLine($"SpotifyPlaybackHost.Tests passed ({tests.Length} tests)");


static Task PreconditionsContract()
{
    new SpotifyPlaybackConnectOptions("Game Bar", 0.5,
        new(true, ["user-read-playback-state", .. SpotifyPlaybackProtocol.RequiredScopes]))
        .Validate();
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        new SpotifyPlaybackPreconditions(false, ["streaming"]).Validate(), "premium_required");
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        new SpotifyPlaybackPreconditions(true, ["user-read-playback-state"]).Validate(),
        "local_playback_scope_required");
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        new SpotifyPlaybackConnectOptions("Game Bar", 1.1,
            new(true, ["streaming"])).Validate(), "invalid_volume");
    return Task.CompletedTask;
}

static Task TokenLeaseContract()
{
    const string secret = "secret-access-token";
    var lease = new SpotifyAccessTokenLease(secret,
        DateTimeOffset.UtcNow.AddMinutes(5), SpotifyPlaybackProtocol.RequiredScopes);
    lease.Validate(DateTimeOffset.UtcNow);
    Assert.True(!lease.ToString().Contains(secret, StringComparison.Ordinal));
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        new SpotifyAccessTokenLease(secret, DateTimeOffset.UtcNow.AddSeconds(-1), ["streaming"])
            .Validate(DateTimeOffset.UtcNow), "expired_token");
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        new SpotifyAccessTokenLease(secret, DateTimeOffset.UtcNow.AddMinutes(1), ["other"])
            .Validate(DateTimeOffset.UtcNow), "local_playback_scope_required");
    return Task.CompletedTask;
}

static Task ProtocolContract()
{
    var encoded = SpotifyPlaybackProtocolCodec.EncodeRequest(
        "parent-1", "set_volume", new { volume = 0.5 });
    var encodedRequest = SpotifyPlaybackProtocolCodec.DecodeRequest(encoded);
    Assert.Equal("parent-1", encodedRequest.RequestId);
    Assert.Equal("set_volume", encodedRequest.Type);
    Assert.Equal(0.5, encodedRequest.Payload.GetProperty("volume").GetDouble());

    var request = SpotifyPlaybackProtocolCodec.DecodeRequest(
        "{\"version\":1,\"requestId\":\"r-1\",\"type\":\"pause\",\"payload\":{}}");
    Assert.Equal("r-1", request.RequestId);
    Assert.Equal("pause", request.Type);
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        SpotifyPlaybackProtocolCodec.DecodeRequest(
            "{\"version\":2,\"requestId\":\"r\",\"type\":\"pause\",\"payload\":{}}"),
        "unsupported_version");
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        SpotifyPlaybackProtocolCodec.DecodeRequest(
            "{\"version\":1,\"requestId\":\"r\",\"type\":\"pause\",\"payload\":{},\"extra\":1}"),
        "invalid_message");
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        SpotifyPlaybackProtocolCodec.DecodeRequest(
            new string('x', SpotifyPlaybackProtocol.MaximumMessageCharacters + 1)),
        "message_too_large");
    return Task.CompletedTask;
}

static Task EventContract()
{
    var json = SpotifyPlaybackProtocolCodec.EncodeEvent(new(
        SpotifyPlaybackProtocol.Version, "ready", null, new { deviceId = "device-1" }));
    using var document = JsonDocument.Parse(json);
    Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
    Assert.Equal("ready", document.RootElement.GetProperty("type").GetString());
    Assert.Equal("device-1",
        document.RootElement.GetProperty("payload").GetProperty("deviceId").GetString());
    Assert.True(json.Length <= SpotifyPlaybackProtocol.MaximumMessageCharacters);

    var decoded = SpotifyPlaybackProtocolCodec.DecodeEvent(json);
    Assert.Equal("ready", decoded.Type);
    Assert.Equal<string?>(null, decoded.RequestId);
    Assert.Equal("device-1", decoded.Payload.GetProperty("deviceId").GetString());
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        SpotifyPlaybackProtocolCodec.DecodeEvent(
            "{\"version\":1,\"requestId\":null,\"type\":\"ready\",\"payload\":{},\"extra\":true}"),
        "invalid_message");
    return Task.CompletedTask;
}



static Task PageStateContract()
{
    var safe = PlaybackState("https://i.scdn.co/image/abc");
    Assert.Equal(safe, SpotifyPlaybackStateValidator.ValidatePlaybackState(safe));
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        SpotifyPlaybackStateValidator.ValidatePlaybackState(
            PlaybackState("file:///C:/Users/example/secret")), "invalid_page_event");
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        SpotifyPlaybackStateValidator.ValidatePlaybackState(
            PlaybackState("https://attacker.example/art.jpg")), "invalid_page_event");
    Assert.Throws<SpotifyPlaybackProtocolException>(() =>
        SpotifyPlaybackStateValidator.ValidatePlaybackState(
            PlaybackState("https://i.scdn.co/image/abc", "bad\nname")), "invalid_page_event");
    return Task.CompletedTask;
}

static SpotifyLocalPlaybackState PlaybackState(string artworkUrl, string name = "Track") => new(
    true, false, 1_000, 120_000, 0, false,
    new(false, false, false, false, false),
    new("spotify:track:abc", "abc123", "track", "audio", name, true,
        "Album", artworkUrl, ["Artist"]));



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

    internal static TException Throws<TException>(Action action, string code)
        where TException : Exception
    {
        try { action(); }
        catch (TException exception)
        {
            if (exception is SpotifyPlaybackProtocolException protocol && protocol.Code != code)
                throw new InvalidOperationException(
                    $"Expected code <{code}>; actual <{protocol.Code}>.", exception);
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    internal static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string code)
        where TException : Exception
    {
        try { await action(); }
        catch (TException exception)
        {
            if (exception is SpotifyPlaybackProtocolException protocol && protocol.Code != code)
                throw new InvalidOperationException(
                    $"Expected code <{code}>; actual <{protocol.Code}>.", exception);
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
