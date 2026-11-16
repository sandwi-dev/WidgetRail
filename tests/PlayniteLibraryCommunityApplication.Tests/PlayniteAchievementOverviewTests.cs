using System.Text.Json;
using WidgetRail.Samples.PlayniteLibrary;

namespace PlayniteLibraryCommunityApplication.Tests;

[TestClass]
public sealed class PlayniteAchievementOverviewTests
{
    private const string Revision = "0123456789abcdef0123456789abcdef";
    private const string First = "00000000-0000-0000-0000-000000000001", Second = "00000000-0000-0000-0000-000000000002";
    [TestMethod]
    public async Task OverviewReadsStableBoundedPagesAndMapsCounts()
    {
        var transport = new Transport(command => Page(command.OverviewOffset));
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        var result = await client.GetAchievementOverviewAsync(default);
        Assert.AreEqual(2, result.Games.Count); Assert.AreEqual(20, result.Total); Assert.AreEqual(10, result.Unlocked);
        CollectionAssert.AreEqual(new[] { 0, 1 }, transport.Commands.Select(command => command.OverviewOffset).ToArray());
        Assert.AreEqual(Revision, transport.Commands[1].Revision);
        Assert.AreEqual("profile", result.ConnectionId);
        Assert.IsTrue(PlayniteBridgeHttpTransport.ResolveLocal(transport.Commands[1]).RelativePath.Contains("revision=" + Revision));
    }
    [TestMethod]
    [DataRow("connection")]
    [DataRow("revision")]
    [DataRow("duplicate")]
    [DataRow("totals")]
    [DataRow("offset")]
    public async Task OverviewRejectsMixedOrMalformedContinuation(string error)
    {
        var transport = new Transport(command => Page(command.OverviewOffset, error));
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        await Assert.ThrowsAsync<PlayniteBridgeDataException>(async () => await client.GetAchievementOverviewAsync(default));
    }
    [TestMethod]
    public async Task OldPluginGetsExplicitUpdateStateWithoutLegacyFallback()
    {
        using var client = new PlayniteBridgeClient(new Transport(_ => new(404, "{}")), new Credentials());
        Assert.AreEqual("update-required", (await client.GetAchievementOverviewAsync(default)).Availability);
    }
    [TestMethod]
    public async Task ExpiredSnapshotRetriesOnlyOnce()
    {
        var transport = new Transport(_ => new(409, "{}"));
        using var client = new PlayniteBridgeClient(transport, new Credentials());
        await Assert.ThrowsAsync<PlayniteBridgeDataException>(async () => await client.GetAchievementOverviewAsync(default));
        Assert.AreEqual(2, transport.Commands.Count);
    }
    [TestMethod]
    [DataRow("games")]
    [DataRow("recent")]
    [DataRow("nextOffset")]
    public async Task InvalidJsonMemberKindsAreReportedAsBadProviderData(string field)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(Page(0).JsonBody)!.AsObject();
        root[field] = field == "nextOffset" ? System.Text.Json.Nodes.JsonValue.Create("1") :
            new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonValue.Create(42));
        using var client = new PlayniteBridgeClient(new Transport(_ => new PlayniteBridgeResponse(200, root.ToJsonString()) { ConnectionId = "profile" }), new Credentials());
        await Assert.ThrowsAsync<PlayniteBridgeDataException>(async () => await client.GetAchievementOverviewAsync(default));
    }

    [TestMethod]
    public async Task LiveInstalledAddonOverviewUsesTheSelectedProfile()
    {
        if (Environment.GetEnvironmentVariable("WRAIL_LIVE_ACHIEVEMENT_OVERVIEW") != "1")
            Assert.Inconclusive("Read-only opt-in for the selected Playnite profile's cached overview.");
        var source = WidgetRail.Tests.PlayniteLibraryCommunityApplication.LivePlayniteConnection.Source();
        using var client = PlayniteBridgeClient.CreateLocal(source, new PlayniteProfileCredentialStore(source));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var first = await client.GetAchievementOverviewAsync(default);
        var firstMs = clock.Elapsed.TotalMilliseconds;
        Assert.AreEqual("ready", first.Availability);
        Assert.IsNotNull(first.ConnectionId);
        Assert.IsTrue(first.Games.Count <= 10000);
        clock.Restart();
        var second = await client.GetAchievementOverviewAsync(default);
        var warmMs = clock.Elapsed.TotalMilliseconds;
        Assert.AreEqual(first.ConnectionId, second.ConnectionId);
        Assert.AreEqual("ready", second.Availability);
        if (Environment.GetEnvironmentVariable("WRAIL_LIVE_ACHIEVEMENT_OVERVIEW_RESULT") is { Length: > 0 } path)
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true,
                games = second.Games.Count, total = second.Total, unlocked = second.Unlocked, completed = second.Completed,
                recent = second.Recent.Count, firstMs, warmMs, observedAt = second.ObservedAt }));
    }

    private static PlayniteBridgeResponse Page(int offset, string error = "") => new(200, JsonSerializer.Serialize(new
    {
        availability = "ready", revision = offset == 1 && error == "revision" ? new string('a', 32) : Revision,
        observedAt = "2026-11-15T12:00:00Z", totalGames = 2, totalAchievements = error == "totals" ? 21 : 20,
        unlockedAchievements = 10, completedGames = 0, offset = error == "offset" ? 7 : offset,
        nextOffset = offset == 0 ? (int?)1 : null,
        games = new[] { new { id = offset == 0 || error == "duplicate" ? First : Second, name = "Game", source = "Steam", installed = true,
            total = 10, unlocked = 5, trackable = 1, lastUnlockedAt = (string?)null, lastPlayedAt = (string?)null } },
        recent = Array.Empty<object>(),
    })) { ConnectionId = offset == 1 && error == "connection" ? "other-profile" : "profile" };
    private sealed class Transport(Func<PlayniteBridgeCommand, PlayniteBridgeResponse> response) : IPlayniteBridgeTransport, IPlayniteConnectedTransport
    {
        internal List<PlayniteBridgeCommand> Commands = [];
        public bool UsesProfileConnection => true;
        public ValueTask<PlayniteBridgeResponse?> SendConnectedAsync(PlayniteBridgeCommand command, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Assert.AreEqual(PlayniteBridgeCommandKind.AchievementOverview, command.Kind); Commands.Add(command); return ValueTask.FromResult<PlayniteBridgeResponse?>(response(command)); }
        public ValueTask<PlayniteBridgeResponse> SendAsync(PlayniteBridgeCommand command, string bearerToken, CancellationToken token) => throw new AssertFailedException("No legacy request is permitted.");
        public void ResetConnection() { }
        public void Dispose() { }
    }
    private sealed class Credentials : IPlayniteCredentialStore
    {
        public ValueTask<string?> ReadAsync(CancellationToken token) => throw new AssertFailedException("Local connection needs no legacy credentials.");
        public ValueTask SaveAsync(string value, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
}
