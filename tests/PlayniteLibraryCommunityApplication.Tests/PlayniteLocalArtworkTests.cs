using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;

namespace WidgetRail.Tests.PlayniteLibraryCommunityApplication;

[TestClass]
public sealed class PlayniteLocalArtworkTests
{
    private const string GameId = "00000000-0000-0000-0000-000000000001";
    private static readonly byte[] Image = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNgYGBgAAAABQABeqhXUAAAAABJRU5ErkJggg==");

    [TestMethod]
    public async Task LocalBytesMatchSourceAndDeletedFileFallsBackWithoutAnotherEval()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        await File.WriteAllBytesAsync(path, Image);
        try
        {
            var transport = new Transport(Index(path));
            using var client = new PlayniteBridgeClient(transport, new Credentials(), preferLocalArtwork: true);
            foreach (var kind in new[] { PlayniteBridgeArtworkKind.Cover, PlayniteBridgeArtworkKind.Background })
            {
                var image = await client.ResolveArtworkAsync(GameId, kind, default);
                Assert.AreEqual("resolved-local", image.Code);
                CollectionAssert.AreEqual(Image, image.Artwork!.Bytes.ToArray());
            }
            Assert.AreEqual(1, transport.IndexCalls);
            Assert.AreEqual(0, transport.ImageCalls);
            File.Delete(path);
            Assert.AreEqual("resolved", (await client.ResolveArtworkAsync(GameId, PlayniteBridgeArtworkKind.Cover, default)).Code);
            Assert.AreEqual(1, transport.IndexCalls);
            Assert.AreEqual(1, transport.ImageCalls);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow("{\"success\":false,\"error\":\"unsupported\"}")]
    [DataRow("{\"success\":true,\"result\":null}")]
    [DataRow("{\"success\":true,\"result\":[{\"id\":\"bad\"}]}")]
    [DataRow("not json")]
    public async Task InvalidIndexFallsBackAndDoesNotRetryEval(string index)
    {
        var transport = new Transport(index);
        using var client = new PlayniteBridgeClient(transport, new Credentials(), preferLocalArtwork: true);
        await client.ResolveArtworkAsync(GameId, PlayniteBridgeArtworkKind.Cover, default);
        await client.ResolveArtworkAsync(GameId, PlayniteBridgeArtworkKind.Background, default);
        Assert.AreEqual(1, transport.IndexCalls);
        Assert.AreEqual(2, transport.ImageCalls);
    }

    [TestMethod]
    [DataRow(@"\\server\share\image.png")]
    [DataRow(@"C:\image.png:stream")]
    [DataRow("https://example.com/image.png")]
    public async Task NonlocalPathsUseExistingImageEndpoint(string path)
    {
        var transport = new Transport(Index(path));
        using var client = new PlayniteBridgeClient(transport, new Credentials(), preferLocalArtwork: true);
        Assert.AreEqual("resolved", (await client.ResolveArtworkAsync(GameId, PlayniteBridgeArtworkKind.Cover, default)).Code);
        Assert.AreEqual(1, transport.ImageCalls);
    }

    [TestMethod]
    public async Task CancelingOneWaiterDoesNotRepeatOrCancelSharedLookup()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport(Index(null)) { ReleaseIndex = release.Task };
        using var client = new PlayniteBridgeClient(transport, new Credentials(), preferLocalArtwork: true);
        using var canceled = new CancellationTokenSource();
        var first = client.ResolveArtworkAsync(GameId, PlayniteBridgeArtworkKind.Cover, canceled.Token).AsTask();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        var second = client.ResolveArtworkAsync(GameId, PlayniteBridgeArtworkKind.Background, default).AsTask();
        release.SetResult();
        await second;
        Assert.AreEqual(1, transport.IndexCalls);
        Assert.AreEqual(1, transport.ImageCalls);
    }

    [TestMethod]
    public async Task UnsupportedOrOversizedLocalFileUsesBoundedHttpFallback()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        await File.WriteAllTextAsync(path, "not an image");
        try
        {
            var transport = new Transport(Index(path));
            using var client = new PlayniteBridgeClient(transport, new Credentials(), preferLocalArtwork: true);
            Assert.AreEqual("resolved", (await client.ResolveArtworkAsync(GameId, PlayniteBridgeArtworkKind.Cover, default)).Code);
            using (var file = File.OpenWrite(path)) file.SetLength(PlayniteBridgeHttpTransport.MaximumArtworkBytes + 1L);
            Assert.AreEqual("resolved", (await client.ResolveArtworkAsync(GameId, PlayniteBridgeArtworkKind.Cover, default)).Code);
            Assert.AreEqual(2, transport.ImageCalls);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [TestCategory("LivePlaynite")]
    public async Task LiveLocalImageMatchesExistingBridgeBytes()
    {
        if (Environment.GetEnvironmentVariable("WRAIL_TEST_LIVE_PLAYNITE_ARTWORK") != "1")
            Assert.Inconclusive("Explicit opt-in required; reads the current Playnite profile.");
        using var local = PlayniteBridgeClient.CreateDefault();
        using var http = new PlayniteBridgeClient(new PlayniteBridgeHttpTransport(), new WindowsCredentialPlayniteTokenStore());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var games = await local.QueryGamesAsync(new(0, 8, true), deadline.Token);
        foreach (var game in games.Games)
        {
            var actual = await local.ResolveArtworkAsync(game.Id, PlayniteBridgeArtworkKind.Cover, deadline.Token);
            if (actual.Code != "resolved-local") continue;
            var expected = await http.ResolveArtworkAsync(game.Id, PlayniteBridgeArtworkKind.Cover, deadline.Token);
            CollectionAssert.AreEqual(expected.Artwork!.Bytes.ToArray(), actual.Artwork!.Bytes.ToArray());
            return;
        }
        Assert.Fail("No direct disk artwork read succeeded in the sampled games.");
    }

    private static string Index(string? path) => JsonSerializer.Serialize(new
    {
        success = true,
        result = new[] { new { id = GameId, cover = path, background = path } },
    });

    private sealed class Credentials : IPlayniteCredentialStore
    {
        public ValueTask<string?> ReadAsync(CancellationToken token) => ValueTask.FromResult<string?>("fake-playnite-token");
        public ValueTask SaveAsync(string value, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    private sealed class Transport(string index) : IPlayniteBridgeTransport
    {
        internal int IndexCalls;
        internal int ImageCalls;
        internal Task? ReleaseIndex { get; init; }
        public async ValueTask<PlayniteBridgeResponse> SendAsync(PlayniteBridgeCommand command, string bearer, CancellationToken token)
        {
            if (command.Kind == PlayniteBridgeCommandKind.ResolveArtworkPaths)
            {
                Interlocked.Increment(ref IndexCalls);
                if (ReleaseIndex is not null) await ReleaseIndex.WaitAsync(token);
                return new(200, index);
            }
            Interlocked.Increment(ref ImageCalls);
            return new(200, Image, "image/png");
        }
        public void Dispose() { }
    }
}
