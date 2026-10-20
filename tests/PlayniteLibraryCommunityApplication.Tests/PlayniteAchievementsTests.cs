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
        Assert.AreEqual(1, transport.EvalCalls);
        Assert.AreEqual(0, transport.FallbackCalls);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("unsupported")]
    [DataRow("malformed")]
    [DataRow("foreign")]
    [DataRow("count")]
    [DataRow("oversized")]
    [DataRow("timeout")]
    public async Task OptionalProviderFailureUsesExistingSuccessStory(string scenario)
    {
        var transport = new Transport(scenario switch
        {
            "missing" => new(200, "{\"success\":true,\"result\":null}"),
            "unsupported" => new(404, ""),
            "malformed" => new(200, "not json"),
            "foreign" => Payload(id: "00000000-0000-0000-0000-000000000002"),
            "count" => Payload(total: 2),
            "oversized" => new(200, new byte[PlayniteBridgeHttpTransport.MaximumDetailBytes + 1]),
            _ => new(200, "{\"success\":false,\"error\":\"timeout\"}"),
        });
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        var result = await client.GetAchievementsAsync(GameId, default);
        Assert.IsTrue(result.Available);
        Assert.AreEqual(PlayniteAchievementProvider.SuccessStory, result.Provider);
        Assert.AreEqual(1, transport.FallbackCalls);
    }

    [TestMethod]
    public async Task AvailableEmptyProviderDoesNotResurrectOldFallbackProgress()
    {
        var transport = new Transport(new(200, JsonSerializer.Serialize(new { success = true,
            result = new { gameId = GameId, total = 0, unlocked = 0, achievements = Array.Empty<object>() } })));
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        var result = await client.GetAchievementsAsync(GameId, default);
        Assert.IsTrue(result.Available);
        Assert.AreEqual(0, result.Total);
        Assert.AreEqual(PlayniteAchievementProvider.PlayniteAchievements, result.Provider);
        Assert.AreEqual(0, transport.FallbackCalls);
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
        Assert.AreEqual(0, transport.FallbackCalls);
    }

    [TestMethod]
    public void EvalAcceptsOnlyCanonicalGameIdentityAndIsNotUiThreadWork()
    {
        using var body = JsonDocument.Parse(PlayniteBridgeClient.CachedAchievementsRequestBody(GameId));
        Assert.IsFalse(body.RootElement.GetProperty("onUiThread").GetBoolean());
        Assert.AreEqual(3000, body.RootElement.GetProperty("timeoutMs").GetInt32());
        StringAssert.Contains(body.RootElement.GetProperty("code").GetString()!, "GetVisibleGameAchievementData");
        Assert.Throws<ArgumentException>(() => PlayniteBridgeClient.CachedAchievementsRequestBody("\"; return null; //"));
        var route = PlayniteBridgeHttpTransport.Resolve(new(PlayniteBridgeCommandKind.CachedAchievements, GameId));
        Assert.AreEqual(HttpMethod.Post, route.Method);
        Assert.AreEqual("api/eval", route.RelativePath);
        Assert.AreEqual(PlayniteBridgeHttpTransport.MaximumDetailBytes, route.MaximumBytes);
    }

    [TestMethod]
    [TestCategory("LivePlaynite")]
    public async Task InstalledAddonReturnsCachedGameThroughProductionAdapter()
    {
        if (Environment.GetEnvironmentVariable("WRAIL_TEST_LIVE_PLAYNITE_ACHIEVEMENTS") != "1")
            Assert.Inconclusive("Explicit opt-in required; reads the current Playnite achievement cache.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var credential = await new WindowsCredentialPlayniteTokenStore().ReadAsync(timeout.Token);
        Assert.IsNotNull(credential);
        // Discovery is test-only, read-only, and returns one identity, not the library's achievements.
        const string discovery = """
            var plugin = PlayniteApi.Addons.Plugins.FirstOrDefault(p => p.Id == new Guid("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b"));
            var property = plugin == null ? null : plugin.GetType().GetProperty("CacheManager");
            var cache = property == null ? null : property.GetValue(plugin);
            if (cache == null) return null;
            var method = cache.GetType().GetMethod("GetCachedGameIds", Type.EmptyTypes);
            var ids = method == null ? null : method.Invoke(cache, null) as System.Collections.IEnumerable;
            if (ids == null) return null;
            foreach (var id in ids) { Guid parsed; if (Guid.TryParse(id as string, out parsed)) return parsed.ToString("D"); }
            return null;
            """;
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:19821/api/eval");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        request.Content = new StringContent(JsonSerializer.Serialize(new { code = discovery, timeoutMs = 3000, onUiThread = false }), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.IsTrue(json.RootElement.GetProperty("success").GetBoolean(), "Read-only cache discovery failed: " +
            json.RootElement.ToString());
        var id = json.RootElement.GetProperty("result").GetString();
        Assert.IsNotNull(id, "No cached PlayniteAchievements game was found.");
        using var client = PlayniteBridgeClient.CreateDefault();
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
            success = true,
            result = new { gameId = id, total, unlocked = 0,
                achievements = new[] { new { name = "Secret", description = "Description", unlocked = false,
                    dateUnlocked = (string?)null, percent = 12.5, gamerScore = 20, isHidden = true, progress, progressMax = 10, icon } } },
        }));

    private sealed class Credentials : IPlayniteCredentialStore
    {
        public ValueTask<string?> ReadAsync(CancellationToken token) => ValueTask.FromResult<string?>("fake-playnite-token");
        public ValueTask SaveAsync(string value, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(CancellationToken token) => ValueTask.CompletedTask;
    }

    private sealed class Transport(PlayniteBridgeResponse cached) : IPlayniteBridgeTransport
    {
        internal int EvalCalls;
        internal int FallbackCalls;
        internal Action? BeforeResponse { get; init; }
        public ValueTask<PlayniteBridgeResponse> SendAsync(PlayniteBridgeCommand command, string bearer, CancellationToken token)
        {
            if (command.Kind == PlayniteBridgeCommandKind.CachedAchievements)
            {
                EvalCalls++;
                BeforeResponse?.Invoke();
                return ValueTask.FromResult(cached);
            }
            Assert.AreEqual(PlayniteBridgeCommandKind.Achievements, command.Kind);
            FallbackCalls++;
            return ValueTask.FromResult(new PlayniteBridgeResponse(200, JsonSerializer.Serialize(new
                { gameId = GameId, installed = true, total = 0, unlocked = 0, achievements = Array.Empty<object>() })));
        }
        public void Dispose() { }
    }
}
