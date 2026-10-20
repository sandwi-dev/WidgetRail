using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteBridgeClient
{
    // Use only the add-on's public, cached read service. Reflection avoids a binary
    // dependency on its assembly; missing/changed contracts use SuccessStory instead.
    // A canonical GUID is the only variable inserted into this fixed C# 5 script
    // (the installed Bridge uses the .NET Framework CodeDOM compiler).
    internal static string CachedAchievementsRequestBody(string gameId) => JsonSerializer.Serialize(new
    {
        code = "var gameId = \"" + CanonicalGameId(gameId) + "\";\n" + CachedAchievementsScript,
        timeoutMs = 3000,
        onUiThread = false,
    });

    internal const string CachedAchievementsScript = """
        var plugin = PlayniteApi.Addons.Plugins.FirstOrDefault(p => p.Id == new Guid("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b"));
        if (plugin == null) return null;
        var serviceProperty = plugin.GetType().GetProperty("AchievementDataService");
        var service = serviceProperty == null ? null : serviceProperty.GetValue(plugin);
        if (service == null) return null;
        var method = service.GetType().GetMethod("GetVisibleGameAchievementData", new[] { typeof(string) })
            ?? service.GetType().GetMethod("GetGameAchievementData", new[] { typeof(string) });
        if (method == null) return null;
        var data = method.Invoke(service, new object[] { gameId });
        if (data == null) return null;
        Func<object, string, object> read = (value, name) => {
            var property = value == null ? null : value.GetType().GetProperty(name);
            return property == null ? null : property.GetValue(value);
        };
        var source = read(data, "Achievements") as System.Collections.IEnumerable;
        if (source == null) return null;
        Func<object, int, string> text = (value, max) => {
            var s = value as string;
            return s == null || s.Length <= max ? s : s.Substring(0, max);
        };
        var rows = new List<object>();
        int unlockedCount = 0;
        int characters = 0;
        foreach (var item in source) {
            if (rows.Count >= 4000) return null;
            var unlockedValue = read(item, "Unlocked");
            var hiddenValue = read(item, "Hidden");
            if (!(unlockedValue is bool) || !(hiddenValue is bool)) return null;
            bool unlocked = (bool)unlockedValue;
            var name = text(read(item, "DisplayName"), 256);
            if (name == null) return null;
            var description = text(read(item, "Description"), 2048);
            var icon = text(read(item, unlocked ? "UnlockedIconPath" : "LockedIconPath"), 2048);
            if (string.IsNullOrEmpty(icon)) icon = text(read(item, "UnlockedIconPath"), 2048);
            characters += name.Length + (description == null ? 0 : description.Length) + (icon == null ? 0 : icon.Length);
            if (characters > 200000) return null;
            if (unlocked) unlockedCount++;
            rows.Add(new {
                apiName = text(read(item, "ApiName"), 256),
                name = name, description = description, unlocked = unlocked,
                dateUnlocked = read(item, "UnlockTimeUtc"), percent = read(item, "GlobalPercentUnlocked"),
                gamerScore = read(item, "Points"), isHidden = (bool)hiddenValue,
                progress = read(item, "ProgressNum"), progressMax = read(item, "ProgressDenom"), icon = icon
            });
        }
        return new { gameId = gameId, total = rows.Count, unlocked = unlockedCount, achievements = rows };
        """;

    private async ValueTask<PlayniteAchievements?> ReadCachedAchievementsAsync(string gameId, CancellationToken token)
    {
        try
        {
            var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.CachedAchievements, gameId), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (response.StatusCode != 200 || response.Body.Length > PlayniteBridgeHttpTransport.MaximumDetailBytes) return null;
            using var document = JsonDocument.Parse(response.Body, new() { MaxDepth = 12 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryRequiredBoolean(root, "success", out var success) || !success ||
                !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
                OptionalString(result, "gameId", 36) != gameId) return null;
            return ParseAchievements(result, PlayniteAchievementProvider.PlayniteAchievements);
        }
        catch (Exception error) when (error is PlayniteBridgeTransportException or PlayniteBridgeDataException or JsonException)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static string? AchievementIconSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl)) return null;
        if (ArtworkFileContract.IsLocalPath(value)) return value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host) ? value : null;
    }
}
