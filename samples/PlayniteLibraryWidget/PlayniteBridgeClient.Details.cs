using System.Globalization;
using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteBridgeClient
{
    private static IReadOnlyList<string> OptionalNames(JsonElement root, string name) =>
        TryStrings(root, name, 64, out var values) ? values! : [];
    private static long? OptionalCount(JsonElement root, string name) =>
        TryNonNegativeLong(root, name, out var value) ? value : null;
    private static int? OptionalScore(JsonElement root, string name) =>
        TryNonNegativeInteger(root, name, out var value) && value <= 100 ? value : null;
    internal static uint? ParseSteamAppId(JsonElement root)
    {
        if (root.TryGetProperty("steamAppId", out var identity))
            return identity.ValueKind == JsonValueKind.Number && identity.TryGetUInt32(out var id) && id > 0 ? id : null;
        // Older add-ons projected at most sixteen links. At the cap we cannot exclude a conflicting link.
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array ||
            links.GetArrayLength() >= 16) return null;
        return WidgetRail.Playnite.SteamGameIdentity.Resolve(Guid.Empty, null, ParseLinks(root).Select(link => link.Url));
    }
    private static IReadOnlyList<PlayniteGameLink> ParseLinks(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array) return [];
        return links.EnumerateArray().Take(16).Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(value => new PlayniteGameLink(OptionalString(value, "name", 96) ?? "Website",
                OptionalString(value, "url", 2048) ?? ""))
            .Where(link => Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)).ToArray();
    }
    private async ValueTask<(JsonDocument? Document, string? ConnectionId)> ReadPluginDataAsync(string gameId, PlayniteBridgeCommandKind kind,
        CancellationToken token)
    {
        var id = CanonicalGameId(gameId);
        var response = await SendRequiredAsync(new(kind, id), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (response.StatusCode == 404) return (null, response.ConnectionId);
        EnsureSuccess(response);
        if (response.Body.Length > PlayniteBridgeHttpTransport.MaximumDetailBytes)
            throw new PlayniteBridgeDataException("invalid_playnite_data");
        var document = JsonDocument.Parse(response.Body, new JsonDocumentOptions { MaxDepth = 24 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _) ||
            OptionalString(root, "gameId", 36) is not { } observed ||
            !Guid.TryParse(observed, out var parsed) || parsed.ToString("D") != id ||
            !TryRequiredBoolean(root, "installed", out var available))
        {
            document.Dispose();
            throw new PlayniteBridgeDataException("invalid_playnite_data");
        }
        if (available) return (document, response.ConnectionId);
        document.Dispose();
        return (null, response.ConnectionId);
    }
    public async ValueTask<PlayniteAchievements> GetAchievementsAsync(string gameId, CancellationToken token)
    {
        var read = await ReadPluginDataAsync(gameId, PlayniteBridgeCommandKind.Achievements, token).ConfigureAwait(false);
        using var typed = read.Document;
        if (typed is null) return PlayniteAchievements.Unavailable;
        return ParseAchievements(typed.RootElement, OptionalString(typed.RootElement, "provider", 64) == "playnite-achievements"
            ? PlayniteAchievementProvider.PlayniteAchievements : PlayniteAchievementProvider.SuccessStory) with { ConnectionId = read.ConnectionId,
                ProviderKey = OptionalString(typed.RootElement, "providerKey", 64) };
    }

    private static PlayniteAchievements ParseAchievements(JsonElement root, PlayniteAchievementProvider provider)
    {
        if (!root.TryGetProperty("achievements", out var array) || array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() > 4000 || !TryNonNegativeInteger(root, "total", out var total) ||
            !TryNonNegativeInteger(root, "unlocked", out var unlocked) || unlocked > total || total != array.GetArrayLength())
            throw new PlayniteBridgeDataException("invalid_playnite_data");
        var values = new List<PlayniteAchievement>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryRequiredBoolean(item, "unlocked", out var achieved))
                throw new PlayniteBridgeDataException("invalid_playnite_data");
            double? percent = item.TryGetProperty("percent", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var rarity) &&
                double.IsFinite(rarity) && rarity is >= 0 and <= 100 ? rarity : null;
            values.Add(new(OptionalString(item, "name", 256) ?? "Achievement",
                OptionalString(item, "description", 2048) ?? "", achieved,
                ParseOptionalDate(item, "dateUnlocked"), percent,
                TryNonNegativeInteger(item, "gamerScore", out var score) ? score : null,
                item.TryGetProperty("isHidden", out var hidden) && hidden.ValueKind == JsonValueKind.True)
            {
                Id = OptionalString(item, "apiName", 256),
                Category = OptionalString(item, "category", 256), TrophyType = OptionalString(item, "trophyType", 64),
                IsGoal = item.TryGetProperty("isGoal", out var goal) && goal.ValueKind == JsonValueKind.True,
                IsCapstone = item.TryGetProperty("isCapstone", out var capstone) && capstone.ValueKind == JsonValueKind.True,
                Progress = TryNonNegativeInteger(item, "progress", out var progress) &&
                    TryNonNegativeInteger(item, "progressMax", out var maximum) && maximum > 0 && progress <= maximum
                    ? new PlayniteAchievementProgress(progress, maximum) : null,
                Icon = AchievementIconSource(OptionalString(item, "icon", 2048)),
            });
        }
        if (values.Count(value => value.Unlocked) != unlocked)
            throw new PlayniteBridgeDataException("invalid_playnite_data");
        return new(true, total, unlocked, values.OrderByDescending(value => value.UnlockedAt).ToArray()) { Provider = provider, CanRefresh = root.TryGetProperty("canRefresh", out var refresh) && refresh.ValueKind == JsonValueKind.True,
            ObservedAt = ParseOptionalDate(root, "observedAt") };
    }
    public async ValueTask<PlayniteActivity> GetActivityAsync(string gameId, CancellationToken token)
    {
        using var document = (await ReadPluginDataAsync(gameId, PlayniteBridgeCommandKind.Activity, token).ConfigureAwait(false)).Document;
        if (document is null) return PlayniteActivity.Unavailable;
        var root = document.RootElement;
        if (!root.TryGetProperty("sessions", out var array) || array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() > 10000 || !TryNonNegativeLong(root, "totalSeconds", out var total))
            throw new PlayniteBridgeDataException("invalid_playnite_data");
        var values = new List<PlayniteSession>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryNonNegativeLong(item, "elapsedSeconds", out var seconds))
                throw new PlayniteBridgeDataException("invalid_playnite_data");
            values.Add(new(ParseOptionalDate(item, "date"), seconds, OptionalString(item, "action", 128)));
        }
        return new(true, total, values.OrderByDescending(value => value.Date).ToArray());
    }
    private static DateTimeOffset? ParseOptionalDate(JsonElement item, string property) =>
        OptionalString(item, property, 64) is { } text && DateTimeOffset.TryParse(text,
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null;
    public async ValueTask<PlayniteRecentActivity> GetRecentActivityAsync(IReadOnlyList<string> games, CancellationToken token)
    {
        if (!UsesLocalConnection || games.Count == 0) return PlayniteRecentActivity.Empty;
        var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.RecentActivity) { ActivityGameIds = games }, token).ConfigureAwait(false);
        if (response.StatusCode == 404) return PlayniteRecentActivity.Empty;
        EnsureSuccess(response);
        using var document = ParseJson(response, PlayniteBridgeHttpTransport.MaximumDetailBytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("sessions", out var sessions) || sessions.ValueKind != JsonValueKind.Array ||
            sessions.GetArrayLength() > 32) throw new PlayniteBridgeDataException("invalid_recent_activity");
        var result = new List<PlayniteRecentSession>();
        foreach (var row in sessions.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || OptionalString(row, "gameId", 36) is not { } id ||
                !games.Contains(id, StringComparer.Ordinal) || OptionalString(row, "gameName", 256) is not { Length: > 0 } name ||
                ParseOptionalDate(row, "startedAt") is not { } when || !TryNonNegativeLong(row, "seconds", out var seconds))
                throw new PlayniteBridgeDataException("invalid_recent_activity");
            result.Add(new(id, name, when, seconds));
        }
        if (!document.RootElement.TryGetProperty("unlocks", out var unlocks) || unlocks.ValueKind != JsonValueKind.Array ||
            unlocks.GetArrayLength() > 32) throw new PlayniteBridgeDataException("invalid_recent_activity");
        var earned = unlocks.EnumerateArray().Select(ParseRecentUnlock).ToArray();
        if (earned.Any(item => !games.Contains(item.GameId, StringComparer.Ordinal)) ||
            earned.DistinctBy(item => (item.GameId, item.AchievementId)).Count() != earned.Length)
            throw new PlayniteBridgeDataException("invalid_recent_activity");
        return new(result, earned);
    }
    public async ValueTask<bool> ChangeInstallationAsync(string gameId, bool install, CancellationToken token)
    {
        var response = await SendRequiredAsync(new(install ? PlayniteBridgeCommandKind.Install : PlayniteBridgeCommandKind.Uninstall,
            CanonicalGameId(gameId)), token).ConfigureAwait(false);
        EnsureSuccess(response);
        using var document = ParseJson(response);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
            !document.RootElement.TryGetProperty("error", out _) &&
            TryRequiredBoolean(document.RootElement, "ok", out var okay) && okay;
    }
}
