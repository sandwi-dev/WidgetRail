using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteAchievementHubState(string Search = "", string Filter = "all", string Sort = "recent",
    IndexedCollectionFocusTarget? ReturnTarget = null, string? ReturnCollection = null, string? ReturnConnection = null);

internal sealed record PlayniteAchievementGameQuery(PlayniteAchievementOverview Data, string Search, string Filter, string Sort, IReadOnlyList<PlayniteAchievementGame> Rows);

public sealed partial class PlayniteLibraryWidget
{
    private const string Hub = "playnite-library.achievement-hub";
    private readonly WidgetResource<PlayniteAchievementOverview> achievementHub;
    private WidgetLoadedCollection<PlayniteAchievementGameQuery, PlayniteAchievementGame>? achievementGames;
    private WidgetLoadedCollection<PlayniteAchievementOverview, PlayniteRecentAchievement>? recentAchievements;
    private PlayniteAchievementOverview? publishedAchievementOverview;
    private PlayniteAchievementHubState? publishedAchievementFilter;

    private WidgetResource<PlayniteAchievementOverview> CreateAchievementHubResource() => CreateResource<PlayniteAchievementOverview>(Hub, new()
    {
        Load = token => _application.GetAchievementOverviewAsync(token),
        MapError = _ => new("overview_unavailable", "Achievements could not be loaded. Check that Playnite is running, then retry."),
        CacheDuration = TimeSpan.FromSeconds(30), Lifetime = WidgetOperationLifetime.Active, RetainLastGoodValue = true,
    });

    private async ValueTask<bool> TryHandleAchievementHubAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (action.ActionId == PlayniteLibraryActions.AchievementsOpen)
        {
            if (LifecycleState != WidgetLifecycleState.Interactive || _navigation.Value.Route is not
                (PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Browse or PlayniteLibraryRoute.Achievements)) return true;
            _navigation.Navigate(PlayniteLibraryRoute.Achievements, action.SourceElementId);
            _ = achievementHub.Refresh(); RequestContentEntry(); return true;
        }
        if (!action.ActionId.StartsWith(Hub + ".", StringComparison.Ordinal)) return false;
        token.ThrowIfCancellationRequested();
        if (_navigation.Value.Route != PlayniteLibraryRoute.Achievements || LifecycleState != WidgetLifecycleState.Interactive) return true;
        var suffix = action.ActionId[(Hub.Length + 1)..];
        if (suffix == "search-focus") { _model.Update(state => state with { SearchFocusPending = true }); RequestContentEntry(); return true; }
        if (suffix == "refresh") { _ = achievementHub.Refresh(); return true; }
        var state = _model.Value.AchievementHub;
        if (suffix == "search" && action.CommittedText is { Length: <= 160 } search) state = state with { Search = search.Trim() };
        else if (suffix == "clear") state = state with { Search = "" };
        else if (suffix.StartsWith("filter.", StringComparison.Ordinal) && suffix[7..] is "all" or "progress" or "completed" or "new") state = state with { Filter = suffix[7..] };
        else if (suffix.StartsWith("sort.", StringComparison.Ordinal) && suffix[5..] is "recent" or "completion" or "name") state = state with { Sort = suffix[5..] };
        else return true;
        _model.Update(value => value with { AchievementHub = state with { ReturnTarget = null, ReturnCollection = null, ReturnConnection = null } });
        await ValueTask.CompletedTask; return true;
    }

    private void PublishAchievementHub(PlayniteAchievementOverview data, PlayniteAchievementHubState controls)
    {
        if (ReferenceEquals(data, publishedAchievementOverview) && publishedAchievementFilter is { } previous && controls.Search == previous.Search && controls.Filter == previous.Filter && controls.Sort == previous.Sort) return;
        var identity = data.ConnectionId + ":" + controls.Search + ":" + controls.Filter + ":" + controls.Sort;
        IEnumerable<PlayniteAchievementGame> rows = data.Games.Where(game => game.Name.Contains(controls.Search, StringComparison.OrdinalIgnoreCase));
        rows = controls.Filter switch
        {
            "progress" => rows.Where(game => game.Unlocked > 0 && game.Unlocked < game.Total),
            "completed" => rows.Where(game => game.Unlocked == game.Total),
            "new" => rows.Where(game => game.Unlocked == 0), _ => rows,
        };
        rows = controls.Sort switch
        {
            "completion" => rows.OrderByDescending(game => game.Completion).ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase),
            "name" => rows.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderByDescending(game => game.LastUnlockedAt).ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase),
        };
        var games = rows.ThenByStableId().ToArray();
        var query = new PlayniteAchievementGameQuery(data, controls.Search, controls.Filter, controls.Sort, games);
        if (achievementGames is null)
            achievementGames = CreateLoadedCollection(Hub + ".games.source", identity, query, games, new WidgetLoadedCollectionOptions<PlayniteAchievementGameQuery, PlayniteAchievementGame>
            {
                PreserveOccurrences = true, ItemKey = game => new(game.Id),
                RenderItem = (_, game, ids) => AchievementGameCard(game, ids.Id("row")),
                OnAction = (owner, game, action, token) => OpenAchievementGameAsync(owner.Data, game.Id, Hub + ".games", token, query: owner),
                ResolveArtworkSource = async (owner, game, handle, token) =>
                {
                    if (handle.Value != "playnite.hub.cover." + game.Id) return null;
                    var details = await _application.GetGameDetailsAsync(game.Id, token).ConfigureAwait(false);
                    var artwork = details?.Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile);
                    return artwork is null ? null : await _application.ResolveArtworkSourceAsync(new(artwork.Handle), token).ConfigureAwait(false);
                },
            });
        else achievementGames.Publish(identity, query, games);
        if (recentAchievements is null)
            recentAchievements = CreateLoadedCollection(Hub + ".recent.source", data.ConnectionId ?? "unavailable", data, data.Recent,
                new WidgetLoadedCollectionOptions<PlayniteAchievementOverview, PlayniteRecentAchievement>
                {
                    PreserveOccurrences = true, ItemKey = row => new(RecentKey(row)),
                    RenderItem = (_, row, ids) => RecentAchievementCard(row, ids.Id("row")),
                    OnAction = (owner, row, action, token) => OpenAchievementGameAsync(owner, row.GameId, Hub + ".recent", token, row),
                    ResolveArtworkSource = async (owner, row, handle, token) =>
                    {
                        if (handle.Value != "playnite.hub.recent." + RecentKey(row)) return null;
                        var data = await _application.GetAchievementsAsync(row.GameId, token).ConfigureAwait(false);
                        if (data.ConnectionId != owner.ConnectionId || data.Provider != PlayniteAchievementProvider.PlayniteAchievements) return null;
                        var matches = data.Items.Where(item => item.Id == row.AchievementId && item.Unlocked).Take(2).ToArray();
                        if (matches is not [var match] || match.Icon is not { } path) return null;
                        return ArtworkFileContract.IsLocalPath(path) ? new WidgetFileArtwork(path, owner.Revision) : null;
                    },
                });
        else recentAchievements.Publish(data.ConnectionId ?? "unavailable", data, data.Recent);
        publishedAchievementOverview = data; publishedAchievementFilter = controls;
    }

    private static string RecentKey(PlayniteRecentAchievement row) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new[] { row.GameId, row.AchievementId })))).ToLowerInvariant();

    private async ValueTask OpenAchievementGameAsync(PlayniteAchievementOverview owner, string id, string collection, CancellationToken token,
        PlayniteRecentAchievement? recent = null, PlayniteAchievementGameQuery? query = null)
    {
        if (!HubCurrent(owner, id) || !HubQueryCurrent(query) || !owner.Games.Any(game => game.Id == id)) return;
        var route = _navigation.Value.RouteCancellationToken;
        var operation = Operations.RunLatest(Hub + ".open", async context =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, route, context.CancellationToken);
            var details = await _application.GetGameDetailsAsync(id, lifetime.Token).ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            if (!context.IsCurrent || !HubCurrent(owner, id) || !HubQueryCurrent(query) || details is null || details.Item.SavedId != id) return;
            var key = new WidgetCollectionItemKey(recent is null ? id : RecentKey(recent));
            var index = recent is null ? query!.Rows.ToList().FindIndex(game => game.Id == id) : owner.Recent.ToList().FindIndex(item => item == recent);
            var target = recent is null ? achievementGames!.FocusTarget(collection, key, index) : recentAchievements!.FocusTarget(collection, key, index);
            _model.Update(state => state with { AchievementHub = state.AchievementHub with { ReturnTarget = target, ReturnCollection = collection, ReturnConnection = owner.ConnectionId } });
            var item = PlayniteLibraryItem.From(details.Item);
            OpenDetails(item, capturedTarget: new(item, () => _navigation.Value.Route == PlayniteLibraryRoute.Achievements &&
                achievementHub.Snapshot.Value is { } current && current.ConnectionId == owner.ConnectionId && current.Games.Any(game => game.Id == id)));
            _model.Update(state => state with { DetailsExtras = state.DetailsExtras with { Tab = PlayniteDetailsTab.Achievements } });
            LoadDetailsSection(PlayniteDetailsTab.Achievements);
        }, WidgetOperationLifetime.Active);
        await operation.Completion.WaitAsync(token).ConfigureAwait(false);
    }

    private bool HubQueryCurrent(PlayniteAchievementGameQuery? query) => query is null ||
        _model.Value.AchievementHub is var state && query.Search == state.Search && query.Filter == state.Filter && query.Sort == state.Sort;
    private bool HubCurrent(PlayniteAchievementOverview owner, string id) => LifecycleState == WidgetLifecycleState.Interactive &&
        _navigation.Value.Route == PlayniteLibraryRoute.Achievements && achievementHub.Snapshot.Value is { } current && current.ConnectionId == owner.ConnectionId && current.Games.Any(game => game.Id == id);

    private static WidgetElement AchievementGameCard(PlayniteAchievementGame game, string id) =>
        UI.ActionSurface(Hub + ".game", id, game.Name + ", " + game.Unlocked + " of " + game.Total + " achievements",
            ActionSurfaceOrientation.Horizontal,
            UI.Artwork(new("playnite.hub.cover." + game.Id), id + ".cover", game.Name).Classes("playnite-hub-cover"),
            UI.Stack(id + ".copy", UI.Text(game.Name, id + ".name").Classes("playnite-hub-game-name"),
                UI.Text(game.Source + (game.Installed ? " · Installed" : ""), id + ".source").Classes("playnite-hub-meta", "playnite-ink-source"),
                UI.Progress(game.Unlocked, game.Total, id + ".progress"),
                UI.Text($"{game.Unlocked} / {game.Total} unlocked · {game.Completion:P0}", id + ".count").Classes("playnite-hub-meta", "playnite-ink-progress"))
                .Classes("playnite-hub-game-copy")).Classes("playnite-hub-game");

    private static WidgetElement RecentAchievementCard(PlayniteRecentAchievement row, string id) =>
        UI.ActionSurface(Hub + ".recent-game", id, row.Name + ", " + row.GameName, ActionSurfaceOrientation.Horizontal,
            UI.Artwork(new("playnite.hub.recent." + RecentKey(row)), id + ".art", row.Name).Classes("playnite-hub-recent-art"),
            UI.Stack(id + ".copy", UI.Text(row.Name, id + ".name").Classes("playnite-hub-game-name", "playnite-ink-reward"),
                UI.Text(row.GameName, id + ".game").Classes("playnite-hub-recent-meta", "playnite-ink-source"),
                UI.Text(row.UnlockedAt.ToLocalTime().ToString("MMM d, yyyy"), id + ".date").Classes("playnite-hub-recent-meta", "playnite-ink-time"))
                .Classes("playnite-hub-game-copy")).Classes("playnite-hub-recent-card");
}

internal static class PlayniteAchievementGameOrder
{
    internal static IEnumerable<PlayniteAchievementGame> ThenByStableId(this IEnumerable<PlayniteAchievementGame> rows) =>
        rows is IOrderedEnumerable<PlayniteAchievementGame> ordered ? ordered.ThenBy(game => game.Id, StringComparer.Ordinal) : rows;
}
