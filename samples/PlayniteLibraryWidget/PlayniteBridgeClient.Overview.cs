using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteBridgeClient
{
    public async ValueTask<PlayniteAchievementOverview> GetAchievementOverviewAsync(CancellationToken token)
    {
        if (!UsesLocalConnection) return PlayniteAchievementOverview.Unavailable;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var games = new List<PlayniteAchievementGame>(); var recent = new List<PlayniteRecentAchievement>();
            var ids = new HashSet<string>(StringComparer.Ordinal); var recentIds = new HashSet<string>(StringComparer.Ordinal); string? revision = null, connection = null, availability = null;
            DateTimeOffset? observed = null; var totalGames = 0; var total = 0; var unlocked = 0; var completed = 0;
            var offset = 0;
            while (true)
            {
                var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.AchievementOverview)
                    { OverviewOffset = offset, Revision = revision }, token).ConfigureAwait(false);
                if (response.StatusCode == 404) return PlayniteAchievementOverview.Unavailable with { Availability = "update-required" };
                if (response.StatusCode == 409 && attempt == 0) break;
                EnsureSuccess(response);
                using var json = JsonDocument.Parse(response.Body, new() { MaxDepth = 8 }); var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object || response.ConnectionId is null ||
                    OptionalString(root, "revision", 32) is not { } nextRevision || !Guid.TryParseExact(nextRevision, "N", out _) ||
                    OptionalString(root, "availability", 32) is not ("ready" or "missing" or "unsupported" or "unavailable") ||
                    !TryNonNegativeInteger(root, "totalGames", out var count) || count > 10000 ||
                    !TryNonNegativeInteger(root, "offset", out var pageOffset) || pageOffset != offset ||
                    !TryNonNegativeInteger(root, "totalAchievements", out var all) || all > 40000000 ||
                    !TryNonNegativeInteger(root, "unlockedAchievements", out var done) || done > all ||
                    !TryNonNegativeInteger(root, "completedGames", out var finished) || finished > count ||
                    !root.TryGetProperty("games", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 200 ||
                    !root.TryGetProperty("recent", out var events) || events.ValueKind != JsonValueKind.Array || events.GetArrayLength() > 8)
                    throw new PlayniteBridgeDataException("invalid_achievement_overview");
                var nextAvailability = root.GetProperty("availability").GetString()!;
                var nextObserved = ParseOptionalDate(root, "observedAt");
                if (nextObserved is null) throw new PlayniteBridgeDataException("invalid_achievement_overview");
                if (revision is null)
                { revision = nextRevision; connection = response.ConnectionId; availability = nextAvailability; observed = nextObserved;
                    totalGames = count; total = all; unlocked = done; completed = finished; }
                else if (revision != nextRevision || connection != response.ConnectionId || availability != nextAvailability || observed != nextObserved ||
                    totalGames != count || total != all || unlocked != done || completed != finished || events.GetArrayLength() != 0)
                    throw new PlayniteBridgeDataException("achievement_overview_changed");
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) throw new PlayniteBridgeDataException("invalid_achievement_overview");
                    var id = OptionalString(row, "id", 36);
                    if (id is null || !Guid.TryParseExact(id, "D", out _) || !ids.Add(id) ||
                        OptionalString(row, "name", 256) is not { } name || OptionalString(row, "source", 128) is not { } source ||
                        !TryRequiredBoolean(row, "installed", out var installed) ||
                        !TryNonNegativeInteger(row, "total", out var gameTotal) || gameTotal is < 1 or > 4000 ||
                        !TryNonNegativeInteger(row, "unlocked", out var gameDone) || gameDone > gameTotal ||
                        !TryNonNegativeInteger(row, "trackable", out var trackable) || trackable > gameTotal - gameDone)
                        throw new PlayniteBridgeDataException("invalid_achievement_overview");
                    games.Add(new(id, name, source, installed, gameTotal, gameDone, trackable,
                        ParseOptionalDate(row, "lastUnlockedAt"), ParseOptionalDate(row, "lastPlayedAt")));
                }
                foreach (var row in events.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) throw new PlayniteBridgeDataException("invalid_achievement_overview");
                    if (OptionalString(row, "gameId", 36) is not { } game || !Guid.TryParseExact(game, "D", out _) ||
                        OptionalString(row, "gameName", 256) is not { } name || OptionalString(row, "achievementId", 256) is not { Length: > 0 } achievement ||
                        OptionalString(row, "name", 256) is not { } title || ParseOptionalDate(row, "unlockedAt") is not { } date)
                        throw new PlayniteBridgeDataException("invalid_achievement_overview");
                    double? rarity = row.TryGetProperty("percent", out var value) && value.ValueKind != JsonValueKind.Null ?
                        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number is >= 0 and <= 100 ? number :
                        throw new PlayniteBridgeDataException("invalid_achievement_overview") : null;
                    if (!recentIds.Add(game + ":" + achievement)) throw new PlayniteBridgeDataException("invalid_achievement_overview");
                    recent.Add(new(game, name, achievement, title, date, rarity));
                }
                if (games.Count > totalGames || !root.TryGetProperty("nextOffset", out var next)) throw new PlayniteBridgeDataException("invalid_achievement_overview");
                if (next.ValueKind == JsonValueKind.Null)
                {
                    if (games.Count != totalGames || games.Sum(game => game.Total) != total || games.Sum(game => game.Unlocked) != unlocked ||
                        games.Count(game => game.Total == game.Unlocked) != completed || recent.Any(item => !ids.Contains(item.GameId)) ||
                        availability != "ready" && (games.Count != 0 || recent.Count != 0)) throw new PlayniteBridgeDataException("invalid_achievement_overview");
                    return new(availability!, connection, revision, observed, games, recent);
                }
                if (next.ValueKind != JsonValueKind.Number || !next.TryGetInt32(out var nextOffset) || nextOffset != games.Count || nextOffset <= offset || nextOffset >= totalGames)
                    throw new PlayniteBridgeDataException("invalid_achievement_overview");
                offset = nextOffset;
            }
        }
        throw new PlayniteBridgeDataException("achievement_overview_changed");
    }
}
