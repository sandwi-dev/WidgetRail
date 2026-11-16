using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;

namespace WidgetRail.Tests.PlayniteLibraryCommunityApplication;

[TestClass]
public sealed class PlayniteAchievementsTests
{
    private const string GameId = "00000000-0000-0000-0000-000000000001";

    [TestMethod]
    public async Task CachedProviderWinsAndMapsProgressWithoutLosingHiddenState()
    {
        var transport = new Transport(Payload());
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        var result = await client.GetAchievementsAsync(GameId, default);
        Assert.AreEqual(PlayniteAchievementProvider.PlayniteAchievements, result.Provider);
        Assert.AreEqual(1, result.Total);
        Assert.AreEqual(0, result.Unlocked);
        var item = result.Items.Single();
        Assert.IsTrue(item.Hidden);
        Assert.AreEqual(new PlayniteAchievementProgress(3, 10), item.Progress);
        Assert.AreEqual(@"C:\cache\achievement.png", item.Icon);
        Assert.AreEqual(12.5, item.Percent);
        Assert.AreEqual(20, item.GamerScore);
        Assert.AreEqual(1, transport.Reads);
    }

    [TestMethod]
    [DataRow("malformed")]
    [DataRow("foreign")]
    [DataRow("count")]
    [DataRow("oversized")]
    public async Task InvalidTypedProviderDataIsRejected(string scenario)
    {
        var transport = new Transport(scenario switch
        {
            "malformed" => new(200, "not json"),
            "foreign" => Payload(id: "00000000-0000-0000-0000-000000000002"),
            "count" => Payload(total: 2),
            _ => new(200, new byte[PlayniteBridgeHttpTransport.MaximumDetailBytes + 1]),
        });
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        try { await client.GetAchievementsAsync(GameId, default); Assert.Fail("Invalid provider payload was accepted."); }
        catch (Exception error) when (error is JsonException or PlayniteBridgeDataException) { }
        Assert.AreEqual(1, transport.Reads, "The client must not query a second provider after rejecting data.");
    }

    [TestMethod]
    public async Task AbsentProviderIsUnavailable()
    {
        using var client = new PlayniteBridgeClient(new Transport(new(200, JsonSerializer.Serialize(new { gameId = GameId, installed = false }))), new Credentials());
        Assert.IsFalse((await client.GetAchievementsAsync(GameId, default)).Available);
    }

    [TestMethod]
    public async Task AvailableEmptyProviderDoesNotResurrectOldFallbackProgress()
    {
        var transport = new Transport(new(200, JsonSerializer.Serialize(new { installed = true, provider = "playnite-achievements", gameId = GameId, total = 0, unlocked = 0, achievements = Array.Empty<object>() })));
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        var result = await client.GetAchievementsAsync(GameId, default);
        Assert.IsTrue(result.Available);
        Assert.AreEqual(0, result.Total);
        Assert.AreEqual(PlayniteAchievementProvider.PlayniteAchievements, result.Provider);
        Assert.AreEqual(1, transport.Reads);
    }

    [TestMethod]
    [DataRow("file:///C:/cache/achievement.png")]
    [DataRow("https://user:secret@example.com/image.png")]
    [DataRow(@"\\server\share\image.png")]
    [DataRow(@"C:\cache\image.png:secret")]
    public async Task UnsafeIconsAndInvalidProgressAreOmitted(string icon)
    {
        var transport = new Transport(Payload(icon: icon, progress: 11));
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        var item = (await client.GetAchievementsAsync(GameId, default)).Items.Single();
        Assert.IsNull(item.Icon);
        Assert.IsNull(item.Progress);
    }

    [TestMethod]
    public async Task CanceledPageDoesNotStartFallback()
    {
        using var canceled = new CancellationTokenSource();
        var transport = new Transport(Payload()) { BeforeResponse = () => canceled.Cancel() };
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await client.GetAchievementsAsync(GameId, canceled.Token));
        Assert.AreEqual(1, transport.Reads);
    }

    [TestMethod]
    public void TypedReadAcceptsOnlyCanonicalGameIdentity()
    {
        var route = PlayniteBridgeHttpTransport.ResolveLocal(new(PlayniteBridgeCommandKind.Achievements, GameId));
        Assert.AreEqual(HttpMethod.Get, route.Method);
        Assert.AreEqual("v1/games/" + GameId + "/achievements", route.RelativePath);
        Assert.AreEqual(PlayniteBridgeHttpTransport.MaximumDetailBytes, route.MaximumBytes);
        Assert.Throws<ArgumentException>(() => PlayniteBridgeHttpTransport.ResolveLocal(new(PlayniteBridgeCommandKind.Achievements, "../settings")));
    }

    [TestMethod]
    [TestCategory("LivePlaynite")]
    public async Task InstalledAddonReturnsCachedGameThroughProductionAdapter()
    {
        if (Environment.GetEnvironmentVariable("WRAIL_TEST_LIVE_PLAYNITE_ACHIEVEMENTS") != "1")
            Assert.Inconclusive("Explicit opt-in required; reads the current Playnite achievement cache.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var source = LivePlayniteConnection.Source();
        using var client = PlayniteBridgeClient.CreateLocal(source, new PlayniteProfileCredentialStore(source));
        var id = Environment.GetEnvironmentVariable("WRAIL_TEST_PLAYNITE_ACHIEVEMENT_GAME");
        Assert.IsNotNull(id, "Supply the cached game identity for this read-only check.");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await client.GetAchievementsAsync(id, timeout.Token);
        Assert.IsTrue(result.Available);
        Assert.AreEqual(PlayniteAchievementProvider.PlayniteAchievements, result.Provider);
        Assert.AreEqual(result.Total, result.Items.Count);
        Console.WriteLine($"Cached achievement read passed: {result.Total} achievements, {result.Unlocked} unlocked, {watch.ElapsedMilliseconds} ms; game identity omitted.");
        if (Environment.GetEnvironmentVariable("WRAIL_PLAYNITE_ACHIEVEMENTS_REPORT") is { Length: > 0 } report)
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
            {
                result = "passed", provider = result.ProviderName, total = result.Total, unlocked = result.Unlocked,
                elapsedMs = watch.ElapsedMilliseconds, icons = result.Items.Count(item => item.Icon is not null),
                progress = result.Items.Count(item => item.Progress is not null), providerSyncRequested = false,
            }), timeout.Token);
    }

    private static PlayniteBridgeResponse Payload(string id = GameId, int total = 1,
        string icon = @"C:\cache\achievement.png", int progress = 3) => new(200, JsonSerializer.Serialize(new
        {
            installed = true, provider = "playnite-achievements", gameId = id, total, unlocked = 0,
                achievements = new[] { new { name = "Secret", description = "Description", unlocked = false,
                    dateUnlocked = (string?)null, percent = 12.5, gamerScore = 20, isHidden = true, progress, progressMax = 10, icon } },
        }));

    private sealed class Credentials : IPlayniteCredentialStore
    {
        public ValueTask<string?> ReadAsync(CancellationToken token) => ValueTask.FromResult<string?>("fake-playnite-token");
        public ValueTask SaveAsync(string value, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    private sealed class Transport(PlayniteBridgeResponse cached) : IPlayniteBridgeTransport
    {
        internal int Reads;
        internal Action? BeforeResponse { get; init; }
        public ValueTask<PlayniteBridgeResponse> SendAsync(PlayniteBridgeCommand command, string bearer, CancellationToken token)
        {
            Assert.AreEqual(PlayniteBridgeCommandKind.Achievements, command.Kind);
            Reads++;
            BeforeResponse?.Invoke();
            return ValueTask.FromResult(cached);
        }
        public void Dispose() { }
    }
}
