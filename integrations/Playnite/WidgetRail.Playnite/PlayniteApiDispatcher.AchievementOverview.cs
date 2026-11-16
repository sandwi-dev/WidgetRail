using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

internal sealed partial class PlayniteApiDispatcher
{
    private readonly SemaphoreSlim achievementOverviewGate = new SemaphoreSlim(1, 1);
    private AchievementOverviewSnapshot? achievementOverview;
    private sealed class AchievementOverviewSnapshot
    {
        internal string Revision = "", Availability = "";
        internal long ProviderRevision, LibraryRevision;
        internal DateTime ObservedAt;
        internal AchievementOverviewGame[] Games = Array.Empty<AchievementOverviewGame>();
        internal AchievementOverviewUnlock[] Recent = Array.Empty<AchievementOverviewUnlock>();
    }

    private async Task<LocalApiResponse> GetAchievementOverviewAsync(LocalApiRequest request, CancellationToken token)
    {
        foreach (var key in request.Query.AllKeys)
            if (key != "offset" && key != "limit" && key != "revision") throw new ApiException(400, "unknown_query");
        var offset = WireJson.Integer(request.Query["offset"], 0, 10000);
        var limit = WireJson.Integer(request.Query["limit"], 100, 200);
        if (limit == 0) throw new ApiException(400, "invalid_range");
        var expected = request.Query["revision"];
        if (expected?.Length > 80 || offset != 0 && expected == null) throw new ApiException(400, "invalid_revision");
        await achievementOverviewGate.WaitAsync(token).ConfigureAwait(false);
        AchievementOverviewSnapshot snapshot;
        try
        {
            var current = achievementOverview;
            var library = Interlocked.Read(ref libraryRevision);
            var currentProvider = current != null && await OnUiAsync(() => achievements.OverviewIsCurrent(current.ProviderRevision), token).ConfigureAwait(false);
            if (expected != null)
            {
                if (current == null || current.Revision != expected || !currentProvider || current.LibraryRevision != library ||
                    DateTime.UtcNow - current.ObservedAt > TimeSpan.FromMinutes(2)) throw new ApiException(409, "snapshot_expired");
                snapshot = current;
            }
            else if (current != null && currentProvider && current.LibraryRevision == library &&
                DateTime.UtcNow - current.ObservedAt < TimeSpan.FromSeconds(30)) snapshot = current;
            else
            {
                var catalog = await OnUiAsync(() => achievements.OverviewCatalog(), token).ConfigureAwait(false);
                var games = new List<AchievementOverviewGame>(); var recent = new List<AchievementOverviewUnlock>();
                var index = 0;
                while (index < catalog.Games.Length)
                {
                    token.ThrowIfCancellationRequested();
                    var batch = await OnUiAsync(() =>
                    {
                        var values = new List<AchievementOverviewGame>();
                        var watch = System.Diagnostics.Stopwatch.StartNew(); var scanned = 0;
                        do
                        {
                            var row = achievements.OverviewGame(catalog.Games[index++], catalog.Revision);
                            if (row != null) values.Add(row);
                        } while (index < catalog.Games.Length && ++scanned < 16 && watch.ElapsedMilliseconds < 8);
                        return values;
                    }, token, System.Windows.Threading.DispatcherPriority.Background).ConfigureAwait(false);
                    // Small time-bounded turns run below input priority. No provider
                    // network refresh or full-library hydration happens here.
                    foreach (var row in batch)
                    {
                        recent.AddRange(row.Recent); row.Recent = Array.Empty<AchievementOverviewUnlock>();
                        games.Add(row);
                    }
                    recent = recent.OrderByDescending(value => value.unlockedAt, StringComparer.Ordinal)
                        .GroupBy(value => value.gameId + ":" + value.achievementId, StringComparer.Ordinal).Select(group => group.First())
                        .OrderByDescending(value => value.unlockedAt, StringComparer.Ordinal)
                        .ThenBy(value => value.gameId, StringComparer.Ordinal).ThenBy(value => value.achievementId, StringComparer.Ordinal).Take(8).ToList();
                }
                if (library != Interlocked.Read(ref libraryRevision) ||
                    !await OnUiAsync(() => achievements.OverviewIsCurrent(catalog.Revision), token).ConfigureAwait(false))
                    throw new ApiException(409, "achievements_changed");
                snapshot = new AchievementOverviewSnapshot
                {
                    Revision = Guid.NewGuid().ToString("N"), Availability = catalog.Availability,
                    ProviderRevision = catalog.Revision, LibraryRevision = library, ObservedAt = DateTime.UtcNow,
                    Games = games.OrderBy(value => value.name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(value => value.id, StringComparer.Ordinal).ToArray(), Recent = recent.ToArray(),
                };
                achievementOverview = snapshot; // One bounded immutable snapshot per process/profile.
            }
        }
        finally { achievementOverviewGate.Release(); }
        if (offset > snapshot.Games.Length) throw new ApiException(400, "invalid_range");
        return WireJson.Response(new
        {
            availability = snapshot.Availability, revision = snapshot.Revision,
            observedAt = snapshot.ObservedAt.ToString("o", CultureInfo.InvariantCulture),
            totalGames = snapshot.Games.Length, totalAchievements = snapshot.Games.Sum(game => game.total),
            unlockedAchievements = snapshot.Games.Sum(game => game.unlocked),
            completedGames = snapshot.Games.Count(game => game.total > 0 && game.unlocked == game.total),
            offset, nextOffset = offset + limit < snapshot.Games.Length ? (int?)(offset + limit) : null,
            games = snapshot.Games.Skip(offset).Take(limit).ToArray(),
            recent = offset == 0 ? snapshot.Recent : Array.Empty<AchievementOverviewUnlock>(),
        });
    }
}
