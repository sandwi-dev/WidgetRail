using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace WidgetRail.Playnite;

internal sealed partial class AchievementAdapter
{
    private static MethodInfo? FindOverviewRead(Type serviceType)
    {
        var signature = new[] { typeof(Guid) };
        var overview = serviceType.GetMethod("GetGameAchievementDataForOverview", signature);
        if (overview != null) return overview;
        // Newer providers expose their summary exclusions on the visible game
        // and achievement models instead of applying them in a separate reader.
        var visible = serviceType.GetMethod("GetVisibleGameAchievementData", signature);
        var model = visible?.ReturnType;
        var rows = model?.GetProperty("Achievements")?.PropertyType;
        var enumerable = rows?.GetInterfaces().Concat(new[] { rows }).FirstOrDefault(type =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        var row = enumerable?.GetGenericArguments()[0];
        return model?.GetProperty("ExcludedFromSummaries")?.PropertyType == typeof(bool) &&
            row?.GetProperty("IsFilteredFromSummaries")?.PropertyType == typeof(bool) ? visible : null;
    }

    internal AchievementOverviewCatalog OverviewCatalog()
    {
        EnsureBinding();
        if (plugin == null) return new AchievementOverviewCatalog("missing", Interlocked.Read(ref revision), Array.Empty<Guid>());
        if (service == null) return new AchievementOverviewCatalog("unavailable", Interlocked.Read(ref revision), Array.Empty<Guid>());
        var ids = service.GetType().GetMethod("GetCachedGameIds", Type.EmptyTypes);
        if (ids == null || overviewRead == null) return new AchievementOverviewCatalog("unsupported", Interlocked.Read(ref revision), Array.Empty<Guid>());
        var generation = Interlocked.Read(ref revision);
        if (!(ids.Invoke(service, Array.Empty<object>()) is IEnumerable source))
            return new AchievementOverviewCatalog("unavailable", generation, Array.Empty<Guid>());
        var games = new HashSet<Guid>(); var visited = 0;
        foreach (var value in source)
        {
            if (++visited > 10000) throw new ApiException(413, "achievement_library_limit");
            if (value is string text && Guid.TryParse(text, out var id) && id != Guid.Empty) games.Add(id);
        }
        return new AchievementOverviewCatalog("ready", generation, games.OrderBy(id => id).ToArray());
    }

    internal bool OverviewIsCurrent(long expected)
    { EnsureBinding(); return Interlocked.Read(ref revision) == expected; }

    internal AchievementOverviewUnlock[] RecentUnlocks(Guid game)
    {
        EnsureBinding();
        return OverviewGame(game, Interlocked.Read(ref revision), includeEventDetails: true)?.Recent ?? Array.Empty<AchievementOverviewUnlock>();
    }

    internal AchievementOverviewGame? OverviewGame(Guid id, long expected, bool includeEventDetails = false)
    {
        if (!OverviewIsCurrent(expected)) throw new ApiException(409, "achievements_changed");
        var game = api.Database.Games.Get(id);
        if (game == null || game.Hidden) return null;
        var data = overviewRead?.Invoke(service, new object[] { id });
        if (data == null || Value(data, "ExcludedFromSummaries") is true) return null;
        if (!(Value(data, "Achievements") is IEnumerable source)) throw new ApiException(502, "achievement_overview_invalid");
        var result = new AchievementOverviewGame
        {
            id = id.ToString("D"), name = WireJson.Text(game.Name), source = WireJson.Text(game.Source?.Name, 128),
            installed = game.IsInstalled, lastPlayedAt = game.LastActivity?.ToString("o", CultureInfo.InvariantCulture),
        };
        var recent = new List<AchievementOverviewUnlock>(); var visited = 0;
        foreach (var item in source)
        {
            if (++visited > 4000) throw new ApiException(413, "achievement_limit");
            if (Value(item, "IsFiltered") is true || Value(item, "IsFilteredFromSummaries") is true) continue;
            if (!(Value(item, "Unlocked") is bool unlocked) || !(Value(item, "Hidden") is bool hidden))
                throw new ApiException(502, "achievement_overview_invalid");
            result.total++;
            if (!unlocked)
            {
                var numerator = Nonnegative(item, "ProgressNum"); var denominator = Nonnegative(item, "ProgressDenom");
                if (!hidden && numerator.HasValue && denominator > 0 && numerator < denominator) result.trackable++;
                continue;
            }
            result.unlocked++;
            var stamp = Value(item, "UnlockTimeUtc") as DateTime?;
            if (!stamp.HasValue || stamp.Value.Year < 1970) continue;
            var instant = stamp.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(stamp.Value, DateTimeKind.Utc) : stamp.Value.ToUniversalTime();
            var date = instant.ToString("o", CultureInfo.InvariantCulture);
            if (result.lastUnlockedAt == null || string.CompareOrdinal(date, result.lastUnlockedAt) > 0) result.lastUnlockedAt = date;
            var achievementId = WireJson.Text(Value(item, "ApiName"));
            if (achievementId.Length == 0) continue;
            var rarity = Value(item, "GlobalPercentUnlocked") as double?;
            var row = includeEventDetails ? new AchievementFeedUnlock { Source = item } : new AchievementOverviewUnlock();
            row.gameId = result.id;
            row.gameName = result.name;
            row.achievementId = achievementId;
            row.name = WireJson.Text(Value(item, "DisplayName"));
            row.unlockedAt = date;
            row.percent = rarity.HasValue && !double.IsNaN(rarity.Value) && rarity >= 0 && rarity <= 100 ? rarity : null;
            recent.Add(row);
            // Keep bounded candidates, never whole hydrated achievement arrays.
            if (recent.Count > 16) recent = recent.OrderByDescending(entry => entry.unlockedAt, StringComparer.Ordinal)
                .ThenBy(entry => entry.achievementId, StringComparer.Ordinal).Take(8).ToList();
        }
        result.Recent = recent.OrderByDescending(entry => entry.unlockedAt, StringComparer.Ordinal)
            .ThenBy(entry => entry.achievementId, StringComparer.Ordinal).Take(8).ToArray();
        foreach (var entry in result.Recent.OfType<AchievementFeedUnlock>())
            if (entry.Source is { } sourceItem)
            {
                entry.description = WireJson.Text(Value(sourceItem, "Description"), 512);
                entry.icon = WireJson.Text(Value(sourceItem, "UnlockedIconPath"), 2048);
                entry.Source = null;
            }
        return result.total == 0 ? null : result;
    }
}

internal sealed class AchievementOverviewGame
{
    public string id = "", name = "", source = "";
    public bool installed;
    public int total, unlocked, trackable;
    public string? lastUnlockedAt, lastPlayedAt;
    internal AchievementOverviewUnlock[] Recent = Array.Empty<AchievementOverviewUnlock>();
}
internal class AchievementOverviewUnlock
{
    public string gameId = "", gameName = "", achievementId = "", name = "", unlockedAt = "";
    public double? percent;
}
internal sealed class AchievementFeedUnlock : AchievementOverviewUnlock
{
    internal object? Source;
    public string? description, icon;
}

internal sealed class AchievementOverviewCatalog
{
    internal string Availability { get; }
    internal long Revision { get; }
    internal Guid[] Games { get; }
    internal AchievementOverviewCatalog(string availability, long revision, Guid[] games)
    { Availability = availability; Revision = revision; Games = games; }
}
