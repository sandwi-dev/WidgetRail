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
    private static IReadOnlyList<PlayniteGameLink> ParseLinks(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array) return [];
        return links.EnumerateArray().Take(16).Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(value => new PlayniteGameLink(OptionalString(value, "name", 96) ?? "Website",
                OptionalString(value, "url", 2048) ?? ""))
            .Where(link => Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)).ToArray();
    }
    private async ValueTask<JsonDocument?> ReadPluginDataAsync(string gameId, PlayniteBridgeCommandKind kind,
        CancellationToken token)
    {
        var id = CanonicalGameId(gameId);
        var response = await SendRequiredAsync(new(kind, id), token).ConfigureAwait(false);
        if (response.StatusCode == 404) return null;
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
        if (available) return document;
        document.Dispose();
        return null;
    }
    public async ValueTask<PlayniteAchievements> GetAchievementsAsync(string gameId, CancellationToken token)
    {
        using var document = await ReadPluginDataAsync(gameId, PlayniteBridgeCommandKind.Achievements, token).ConfigureAwait(false);
        if (document is null) return PlayniteAchievements.Unavailable;
        var root = document.RootElement;
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
                item.TryGetProperty("isHidden", out var hidden) && hidden.ValueKind == JsonValueKind.True));
        }
        if (values.Count(value => value.Unlocked) != unlocked)
            throw new PlayniteBridgeDataException("invalid_playnite_data");
        return new(true, total, unlocked, values.OrderByDescending(value => value.UnlockedAt).ToArray());
    }
    public async ValueTask<PlayniteActivity> GetActivityAsync(string gameId, CancellationToken token)
    {
        using var document = await ReadPluginDataAsync(gameId, PlayniteBridgeCommandKind.Activity, token).ConfigureAwait(false);
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
