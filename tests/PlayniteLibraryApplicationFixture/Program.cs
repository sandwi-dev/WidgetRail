using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using System.Text.Json;

// This executable is never shipped. Only its provider is fake; widget, application
// service, state persistence and the supervised process protocol are production code.
var fixtureRoot = Environment.GetEnvironmentVariable("WRAIL_PLAYNITE_PROCESS_FIXTURE_ROOT");
if (string.IsNullOrWhiteSpace(fixtureRoot) || !Path.IsPathFullyQualified(fixtureRoot) ||
    !File.Exists(Path.Combine(fixtureRoot, "fixture-owned"))) return 2;
return await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var client = new JournalClient(fixtureRoot);
    return new PlayniteLibraryWidget(new PlayniteLibraryApplicationService(client,
        new PlayniteLibraryStateFileStore(Path.Combine(fixtureRoot, "state.json"))), client);
});

file sealed class JournalClient : IPlayniteLibraryBridgeClient, IPlayniteBridgeClient
{
    private readonly string root;
    private readonly PerformanceOptions? performance;
    private readonly ActivityLibrary? activityLibrary;
    private readonly PlayniteBridgeGame[] games;
    public JournalClient(string root)
    {
        this.root=root;
        var activityPath = Path.Combine(root, "activity-library.json");
        if (File.Exists(activityPath))
        {
            activityLibrary = JsonSerializer.Deserialize<ActivityLibrary>(File.ReadAllText(activityPath))
                ?? throw new InvalidDataException("Missing activity library.");
            games = activityLibrary.Games;
            return;
        }
        var path=Path.Combine(root,"performance.json");
        if(File.Exists(path))
        {
            performance=JsonSerializer.Deserialize<PerformanceOptions>(File.ReadAllText(path))
                ??throw new InvalidDataException("Missing performance fixture configuration.");
            if(performance.GameCount is < 1 or > 20000 || performance.DelayMilliseconds is < 0 or > 1000)
                throw new InvalidDataException("Invalid performance fixture bounds.");
        }
        games=Enumerable.Range(1,performance?.GameCount??3).Select(index =>
        new PlayniteBridgeGame($"00000000-0000-0000-0000-{index:D12}",
            performance is not null?$"Fixture Game {index:D5}":index == 2 ? "Process Target" : $"Other Game {index}", "Fixture",
            true, false, false, null, [], [], ["Windows"], 120, null)
        { Description = "Cross-process production details" }).ToArray();
    }

    public ValueTask<PlayniteBridgeGamePage> QueryGamesAsync(PlayniteBridgeGameQuery query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new PlayniteBridgeGamePage(games.Length, query.Offset,
            query.Limit, games.Skip(query.Offset).Take(query.Limit).ToArray()));
    }
    public ValueTask<PlayniteBridgeGame?> ResolveGameAsync(string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var game = games.SingleOrDefault(game => game.Id == id);
        if (game is not null && activityLibrary?.TrailerLinks?.TryGetValue(id, out var trailer) == true)
            game = game with { Links = [new("Official trailer", trailer)] };
        if (game is not null && activityLibrary?.OfficialWebsites?.TryGetValue(id, out var website) == true)
            game = game with { Links = [.. game.Links, new("Official Website", website)] };
        if (game is not null && activityLibrary?.SteamAppIds?.TryGetValue(id, out var appId) == true)
        {
            game = game with { SteamAppId = appId };
            if (activityLibrary.DetailDelayMilliseconds is > 0 and <= 8000)
                return AfterDelay(game, activityLibrary.DetailDelayMilliseconds, token);
        }
        return ValueTask.FromResult(game);

        static async ValueTask<PlayniteBridgeGame?> AfterDelay(PlayniteBridgeGame game, int milliseconds, CancellationToken token)
        {
            await Task.Delay(milliseconds, token);
            return game;
        }
    }
    public async ValueTask<PlayniteBridgeArtworkResult> ResolveArtworkAsync(string id, PlayniteBridgeArtworkKind kind, CancellationToken token)
    {
        if (activityLibrary is not null && Guid.TryParseExact(id, "D", out var game))
        {
            var file = Path.Combine(root, "artwork", $"{game:D}-{kind}.jpg");
            if (!File.Exists(file)) return new(null, "not-found", "none");
            return new(new WidgetEncodedArtwork(WidgetArtworkContentType.Jpeg, await File.ReadAllBytesAsync(file, token)), "fixture", "large");
        }
        if(performance is null||!performance.Artwork)return new(null,"not-found","none");
        if(!Guid.TryParse(id,out _)||!int.TryParse(id.AsSpan(id.Length-12),out var index)||index<1||index>games.Length)
            throw new InvalidDataException("Unknown fixture artwork.");
        if(performance.DelayMilliseconds>0)await Task.Delay(performance.DelayMilliseconds,token);
        var bytes=await File.ReadAllBytesAsync(Path.Combine(root,"artwork",$"{(kind==PlayniteBridgeArtworkKind.Background?"background":"cover")}-{index%8}.jpg"),token);
        return new(new WidgetEncodedArtwork(WidgetArtworkContentType.Jpeg,bytes),"fixture","large");
    }
    public ValueTask<PlayniteRecentActivity> GetRecentActivityAsync(IReadOnlyList<string> gameIds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(activityLibrary is null ? PlayniteRecentActivity.Empty :
            new PlayniteRecentActivity(activityLibrary.Sessions.Where(row => gameIds.Contains(row.GameId)).ToArray(),
                activityLibrary.Unlocks.Where(row => gameIds.Contains(row.GameId)).ToArray()));
    }
    public ValueTask<PlayniteActivity> GetActivityAsync(string gameId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (activityLibrary is null) return ValueTask.FromResult(PlayniteActivity.Unavailable);
        var sessions = activityLibrary.Sessions.Where(row => row.GameId == gameId)
            .Select(row => new PlayniteSession(row.StartedAt, row.Seconds, null)).ToArray();
        return ValueTask.FromResult(new PlayniteActivity(true, sessions.Sum(row => row.Seconds), sessions));
    }
    public async ValueTask<PlayniteAchievementOverview> GetAchievementOverviewAsync(CancellationToken token)
    {
        var path = Path.Combine(root, "achievement-overview.json");
        if (!File.Exists(path)) return PlayniteAchievementOverview.Unavailable;
        var mode = JsonSerializer.Deserialize<AchievementOptions>(await File.ReadAllTextAsync(path, token))!;
        await File.AppendAllTextAsync(Path.Combine(root, "overview-reads.txt"), "read" + Environment.NewLine, token);
        if (mode.Availability != "ready") return PlayniteAchievementOverview.Unavailable with { Availability = mode.Availability };
        var rows = games.Select((game, index) => new PlayniteAchievementGame(game.Id, game.Name, "Steam", true,
            40 + index, index % 3 == 0 ? 0 : index % 3 == 1 ? 40 + index : 12, index % 3 == 2 ? 3 : 0,
            index % 3 == 0 ? null : DateTimeOffset.UtcNow.AddDays(-index), null)).ToArray();
        var recent = rows.Where(game => game.Unlocked > 0).Take(8).Select(game => new PlayniteRecentAchievement(game.Id, game.Name,
            "milestone", "A new milestone", game.LastUnlockedAt!.Value, 24.5)).ToArray();
        return new("ready", "fixture-profile", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, rows, recent);
    }
    public ValueTask<PlayniteAchievements> GetAchievementsAsync(string gameId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (activityLibrary is not null)
        {
            var unlocked = activityLibrary.Unlocks.Where(row => row.GameId == gameId).Select(row =>
                new PlayniteAchievement(row.Name, row.Description ?? "", true, row.UnlockedAt, row.Percent, null, false)
                    { Id = row.AchievementId, Icon = row.Icon }).ToArray();
            return ValueTask.FromResult(new PlayniteAchievements(true, unlocked.Length, unlocked.Length, unlocked)
                { Provider = PlayniteAchievementProvider.PlayniteAchievements, ConnectionId = "fixture-profile" });
        }
        if (!File.Exists(Path.Combine(root, "achievement-overview.json"))) return ValueTask.FromResult(PlayniteAchievements.Unavailable);
        return ValueTask.FromResult(new PlayniteAchievements(true, 2, 1,
        [new("A new milestone", "Complete your first chapter", true, DateTimeOffset.UtcNow.AddDays(-1), 24.5, null, false)
            { Id = "milestone", Icon = Path.Combine(root, "achievement.png") },
         new("Keep moving forward", "Reach the next milestone", false, null, 15, null, false)
            { Id = "progress", Progress = new(2, 10), Icon = Path.Combine(root, "achievement.png") }])
            { Provider = PlayniteAchievementProvider.PlayniteAchievements, ConnectionId = "fixture-profile" });
    }
    public async ValueTask<bool> LaunchAsync(string id, CancellationToken token)
    {
        if (!games.Any(game => game.Id == id)) throw new InvalidOperationException("Unknown fixture game.");
        await File.AppendAllTextAsync(Path.Combine(root, "launches.txt"), id + Environment.NewLine, token);
        return true;
    }
    public ValueTask<PlayniteCaptureIntegration?> GetCaptureIntegrationAsync(CancellationToken token, string? gameId = null)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(activityLibrary?.CaptureProfile is { } profile
            ? new PlayniteCaptureIntegration(profile, [], [], "fixture-profile") : null);
    }
    // Unused mutations fail loudly so adding workflow coverage cannot silently
    // bypass either the fake's contract or real system-action restrictions.
    public ValueTask<PlayniteBridgeGame?> SetFavoriteAsync(string id, bool value, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<PlayniteBridgeGame?> SetHiddenAsync(string id, bool value, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<PlayniteBridgeGame?> SetCategoriesAsync(string id, IReadOnlyList<string> values, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<PlayniteBridgeGame?> SetCompletionStatusAsync(string id, string value, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<PlayniteBridgeNamedItem?> CreateCategoryAsync(string name, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCategoriesAsync(CancellationToken token) => ValueTask.FromResult<IReadOnlyList<PlayniteBridgeNamedItem>>([]);
    public ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCompletionStatusesAsync(CancellationToken token) => ValueTask.FromResult<IReadOnlyList<PlayniteBridgeNamedItem>>([]);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
    public ValueTask<PlayniteBridgeConnectionResult> ProbeAsync(CancellationToken token) =>
        ValueTask.FromResult(new PlayniteBridgeConnectionResult(PlayniteBridgeConnectionKind.Connected, "fixture"));
    public ValueTask SaveCredentialAsync(string value, CancellationToken token) => throw new NotSupportedException();
    public ValueTask DeleteCredentialAsync(CancellationToken token) => throw new NotSupportedException();
}

file sealed record PerformanceOptions(int GameCount=2000,bool Artwork=true,int DelayMilliseconds=15);

file sealed record AchievementOptions(string Availability = "ready");
file sealed record ActivityLibrary(PlayniteBridgeGame[] Games, PlayniteRecentSession[] Sessions, PlayniteRecentAchievement[] Unlocks,
    string? CaptureProfile = null, Dictionary<string, uint>? SteamAppIds = null, int DetailDelayMilliseconds = 0,
    Dictionary<string, string>? TrailerLinks = null, Dictionary<string, string>? OfficialWebsites = null);
