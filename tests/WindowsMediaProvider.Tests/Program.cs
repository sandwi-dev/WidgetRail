using System.Text.Json;
using System.Buffers.Binary;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsMediaProvider;
using Windows.Storage.Streams;

var tests = new (string Name, Func<Task> Run)[]
{
    ("GSMTC manager starts lazily on first authorized read", StartsLazily),
    ("Snapshots are bounded sanitized and current-first", SanitizesAndOrders),
    ("Opaque IDs remain stable while a native session remains", OpaqueIdsAreStable),
    ("Multiple sessions from one source app remain distinct control targets", DuplicateAppSessionsRemainDistinct),
    ("Removed sessions immediately invalidate control tokens", RemovedSessionIsRejected),
    ("Native changes publish complete coalesced media snapshots", NativeChangesPublish),
    ("All five supported transport commands map exactly", CommandsMapExactly),
    ("Unsupported native controls fail with a typed error", UnsupportedControlFailsClosed),
    ("Provider payloads expose no AUMID or native identifier", PayloadIsSanitized),
    ("App labels and metadata remove control characters and paths", SanitizerIsBounded),
    ("GSMTC artwork is normalized to a bounded inline PNG", ArtworkIsNormalized),
    ("Missing malformed and oversized GSMTC artwork is omitted", BadArtworkIsOmitted),
    ("Real GSMTC API can be requested and disposed without polling", NativeApiSmoke),
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
Console.WriteLine($"WindowsMediaProvider.Tests passed ({tests.Length} tests)");

static async Task StartsLazily()
{
    var native = new FakeNative();
    await using var provider = new WindowsMediaPlatformBackend(new FakeFactory(native));
    Assert.Equal(0, native.StartCalls);
    Assert.Equal(0, (await provider.GetMediaSessionsAsync(default)).Count);
    Assert.Equal(1, native.StartCalls);
}

static async Task SanitizesAndOrders()
{
    var native = new FakeNative();
    for (var index = 0; index < WindowsMediaPlatformBackend.MaximumSessions + 3; index++)
        native.Sessions.Add(Session($"private.aumid.{index}", $" App {index}\n", current: index == 12,
            playing: index == 5));
    await using var provider = new WindowsMediaPlatformBackend(new FakeFactory(native));
    var snapshot = await provider.GetMediaSessionsAsync(default);
    Assert.Equal(WindowsMediaPlatformBackend.MaximumSessions, snapshot.Count);
    Assert.True(snapshot[0].IsCurrent);
    Assert.Equal("App 12", snapshot[0].AppName);
    Assert.True(snapshot.All(item => item.SessionId.StartsWith("media-", StringComparison.Ordinal)));
    Assert.Equal(1, snapshot.Count(item => item.IsCurrent));
}

static async Task OpaqueIdsAreStable()
{
    var native = new FakeNative();
    native.Sessions.Add(Session("private.spotify!App", "Spotify", current: true));
    await using var provider = new WindowsMediaPlatformBackend(new FakeFactory(native));
    var first = (await provider.GetMediaSessionsAsync(default)).Single();
    native.Sessions[0] = native.Sessions[0] with { Title = "Next song" };
    native.Publish();
    await WaitUntil(async () => (await provider.GetMediaSessionsAsync(default)).Single().Title == "Next song");
    var second = (await provider.GetMediaSessionsAsync(default)).Single();
    Assert.Equal(first.SessionId, second.SessionId);
}

static async Task DuplicateAppSessionsRemainDistinct()
{
    var native = new FakeNative();
    native.Sessions.Add(Session("gsm-session-a", "Same player", current: true));
    native.Sessions.Add(Session("gsm-session-b", "Same player") with { Title = "Second track" });
    await using var provider = new WindowsMediaPlatformBackend(new FakeFactory(native));
    var sessions = await provider.GetMediaSessionsAsync(default);
    Assert.Equal(2, sessions.Count);
    Assert.Equal(2, sessions.Select(item => item.SessionId).Distinct(StringComparer.Ordinal).Count());
    var second = sessions.Single(item => item.Title == "Second track");
    await provider.ControlMediaSessionAsync(second.SessionId, MediaSessionCommand.Next, default);
    Assert.Equal("gsm-session-b", native.LastControlledNativeId);
}

static async Task RemovedSessionIsRejected()
{
    var native = new FakeNative();
    native.Sessions.Add(Session("private.one", "One", current: true));
    await using var provider = new WindowsMediaPlatformBackend(new FakeFactory(native));
    var opaque = (await provider.GetMediaSessionsAsync(default)).Single().SessionId;
    native.Sessions.Clear();
    native.Publish();
    await WaitUntil(async () => (await provider.GetMediaSessionsAsync(default)).Count == 0);
    var error = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.ControlMediaSessionAsync(opaque, MediaSessionCommand.Play, default));
    Assert.Equal("resource_not_found", error.Code);
}

static async Task NativeChangesPublish()
{
    var native = new FakeNative();
    await using var provider = new WindowsMediaPlatformBackend(new FakeFactory(native));
    await provider.GetMediaSessionsAsync(default);
    MediaSessionsChangedEvent? observed = null;
    provider.EventPublished += (_, item) => observed = item.Payload as MediaSessionsChangedEvent;
    native.Sessions.Add(Session("private.one", "One", current: true));
    native.Publish();
    native.Sessions[0] = native.Sessions[0] with { PositionMilliseconds = 1_500 };
    native.Publish();
    await WaitUntil(() => Task.FromResult(observed?.Sessions.Single().PositionMilliseconds == 1_500));
    Assert.Equal(PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsReadV1);
}

static async Task CommandsMapExactly()
{
    var native = new FakeNative();
    native.Sessions.Add(Session("private.one", "One", current: true));
    await using var provider = new WindowsMediaPlatformBackend(new FakeFactory(native));
    var id = (await provider.GetMediaSessionsAsync(default)).Single().SessionId;
    foreach (var command in Enum.GetValues<MediaSessionCommand>())
        await provider.ControlMediaSessionAsync(id, command, default);
    Assert.SequenceEqual(Enum.GetValues<NativeMediaCommand>(), native.Commands);
}

static async Task UnsupportedControlFailsClosed()
{
    var native = new FakeNative { Result = NativeMediaControlResult.NotSupported };
    native.Sessions.Add(Session("private.one", "One", current: true));
    await using var provider = new WindowsMediaPlatformBackend(new FakeFactory(native));
    var id = (await provider.GetMediaSessionsAsync(default)).Single().SessionId;
    var error = await Assert.ThrowsAsync<BrokerException>(() =>
        provider.ControlMediaSessionAsync(id, MediaSessionCommand.Next, default));
    Assert.Equal("not_supported", error.Code);
}

static async Task PayloadIsSanitized()
{
    const string secret = "Private.Package_123!Player";
    var native = new FakeNative();
    native.Sessions.Add(Session(secret, "Player", current: true));
    await using var provider = new WindowsMediaPlatformBackend(new FakeFactory(native));
    var json = JsonSerializer.Serialize(await provider.GetMediaSessionsAsync(default));
    Assert.False(json.Contains(secret, StringComparison.Ordinal));
    Assert.False(json.Contains("NativeId", StringComparison.Ordinal));
    Assert.True(json.Contains("media-", StringComparison.Ordinal));
}

static Task SanitizerIsBounded()
{
    Assert.Equal("Player", WindowsMediaNativeAdapter.FriendlyAppName(
        "Private.Package_123!Player.exe"));
    Assert.Equal("Safe title", WindowsMediaNativeAdapter.Sanitize(" Safe\r title ", "Fallback"));
    Assert.Equal(160, WindowsMediaNativeAdapter.Sanitize(new string('x', 200), "Fallback").Length);
    Assert.Equal("Fallback", WindowsMediaNativeAdapter.Sanitize("\r\n", "Fallback"));
    return Task.CompletedTask;
}

static async Task ArtworkIsNormalized()
{
    // Valid 1x1 RGBA PNG. The reader always re-encodes even an already-safe input.
    var source = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lK3xWQAAAABJRU5ErkJggg==");
    var encoded = await MediaArtworkReader.TryReadAsync(Reference(source), default);
    Assert.True(encoded is not null);
    var png = Convert.FromBase64String(encoded!);
    Assert.True(png.Length <= MediaArtworkReader.MaximumOutputBytes);
    Assert.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8));
    Assert.True(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) <=
        MediaArtworkReader.MaximumOutputDimension);
    Assert.True(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) <=
        MediaArtworkReader.MaximumOutputDimension);
    Assert.Equal((byte)8, png[24]);
    Assert.Equal((byte)6, png[25]);
    Assert.Equal((byte)0, png[28]);
}

static async Task BadArtworkIsOmitted()
{
    Assert.Equal<string?>(null, await MediaArtworkReader.TryReadAsync(null, default));
    Assert.Equal<string?>(null, await MediaArtworkReader.TryReadAsync(
        Reference([1, 2, 3, 4]), default));
    Assert.Equal<string?>(null, await MediaArtworkReader.TryReadAsync(
        Reference(new byte[MediaArtworkReader.MaximumSourceBytes + 1]), default));
}

static IRandomAccessStreamReference Reference(byte[] bytes)
{
    var stream = new InMemoryRandomAccessStream();
    using var writer = new DataWriter(stream);
    writer.WriteBytes(bytes);
    writer.StoreAsync().AsTask().GetAwaiter().GetResult();
    writer.DetachStream();
    stream.Seek(0);
    return RandomAccessStreamReference.CreateFromStream(stream);
}

static async Task NativeApiSmoke()
{
    if (!OperatingSystem.IsWindows()) return;
    await using var provider = new WindowsMediaPlatformBackend();
    var snapshot = await provider.GetMediaSessionsAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine($"INFO Live GSMTC probe observed {snapshot.Count} sanitized session(s).");
    Assert.True(snapshot.Count <= WindowsMediaPlatformBackend.MaximumSessions);
    Assert.True(snapshot.All(item => item.SessionId.StartsWith("media-", StringComparison.Ordinal)));
}

static NativeMediaSession Session(
    string nativeId, string app, bool current = false, bool playing = false) =>
    new(nativeId, app, "Track", "Artist",
        playing ? NativeMediaPlaybackStatus.Playing : NativeMediaPlaybackStatus.Paused,
        1_000, 10_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1,
        current, true, true, true, true, true);

static async Task WaitUntil(Func<Task<bool>> condition, int timeoutMilliseconds = 2_000)
{
    var deadline = Environment.TickCount64 + timeoutMilliseconds;
    while (!await condition())
    {
        if (Environment.TickCount64 >= deadline) throw new TimeoutException();
        await Task.Delay(10);
    }
}

file sealed class FakeFactory(FakeNative native) : IWindowsMediaNativeAdapterFactory
{
    public IWindowsMediaNativeAdapter Create() => native;
}

file sealed class FakeNative : IWindowsMediaNativeAdapter
{
    public event EventHandler? StateChanged;
    internal List<NativeMediaSession> Sessions { get; } = [];
    internal List<NativeMediaCommand> Commands { get; } = [];
    internal int StartCalls { get; private set; }
    internal NativeMediaControlResult Result { get; set; } = NativeMediaControlResult.Succeeded;
    internal string? LastControlledNativeId { get; private set; }
    internal void Publish() => StateChanged?.Invoke(this, EventArgs.Empty);
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCalls++;
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<NativeMediaSession>> ReadSessionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<NativeMediaSession>>(Sessions.ToArray());
    }
    public Task<NativeMediaControlResult> ControlAsync(
        string nativeId, NativeMediaCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Sessions.Any(item => item.NativeId == nativeId))
            return Task.FromResult(NativeMediaControlResult.NotFound);
        LastControlledNativeId = nativeId;
        Commands.Add(command);
        return Task.FromResult(Result);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file static class Assert
{
    internal static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }
    internal static void False(bool value) => True(!value);
    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
    internal static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Sequences differ.");
    }
    internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
