using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameLauncher;

internal sealed record GameLauncherPresentationState(
    WidgetCursorResourceSnapshot<GameLauncherItem> Collection,
    GameLauncherPrivateState Organization,
    string Status,
    string? LaunchingSavedId,
    IReadOnlyDictionary<string, GameLauncherLaunchState> LaunchStates,
    bool OrganizationBusy,
    bool Interactive,
    WidgetAppLibraryQuery Query,
    GameLauncherRecentMode RecentMode,
    bool FavoriteFilter,
    GameLauncherRoute Route,
    GameLauncherFixedRows FixedRows,
    IReadOnlyList<WidgetAppLibrarySource> Sources,
    string? HeroSavedId,
    int HeroIndex)
{
    internal string? ActiveCategoryId { get; init; }
    internal IReadOnlyList<GameLauncherCollectionOption> Collections { get; init; } = [];
}

internal enum GameLauncherRecentMode
{
    Off,
    RecentFirst,
    RecentOnly,
}

internal static class GameLauncherPresentation
{
    internal const string ScrollId = "game-launcher.library.scroll";
    internal const string RetryId = "game-launcher.retry";
    private static readonly WidgetSurfaceHints Surface = new()
    {
        Mode = WidgetSurfaceMode.Wide,
        WidthMode = WidgetSurfaceAxisMode.FillAvailable,
        HeightMode = WidgetSurfaceAxisMode.FillAvailable,
        PreferredWidth = 1600,
        PreferredHeight = 1200,
        MinimumWidth = 420,
        MinimumHeight = 340,
    };

    internal static WidgetView Render(GameLauncherPresentationState state)
    {
        var snapshot = state.Collection;
        var routeTitle = state.Route switch
        {
            GameLauncherRoute.AddGames => "Add games",
            GameLauncherRoute.Running => "Add running app",
            GameLauncherRoute.Hidden => "Hidden games",
            GameLauncherRoute.Categories => "Categories",
            GameLauncherRoute.Category => GameLauncherCategoryPolicy.Find(
                state.Organization, state.ActiveCategoryId)?.Name ?? "Category",
            _ => "Game Launcher",
        };
        var header = UI.Stack("game-launcher.header",
                UI.Text(routeTitle, "game-launcher.compact.title", routeTitle)
                    .Classes("game-launcher-title")
                    .VisibleWhen(ResponsiveVisibility.CompactOnly),
                UI.Text(state.Status, "game-launcher.compact.status", state.Status)
                    .Classes("game-launcher-status")
                    .VisibleWhen(ResponsiveVisibility.CompactOnly),
                UI.Stack("game-launcher.header.expanded",
                        UI.Text("INSTALLED GAMES", "game-launcher.eyebrow", "Installed games")
                            .Classes("game-launcher-eyebrow"),
                        UI.Text(routeTitle, "game-launcher.title", routeTitle)
                            .Classes("game-launcher-title"),
                        UI.Text(state.Status, "game-launcher.status", state.Status)
                            .Classes("game-launcher-status"))
                    .Classes("game-launcher-header-expanded")
                    .VisibleWhen(ResponsiveVisibility.ExpandedOnly))
            .Classes("game-launcher-header", "game-launcher-fixed");
        var sourceStatus = SourceStatus(state.Sources, snapshot.Status)
            .AddClasses("game-launcher-fixed");
        var filterControls = new List<WidgetElement>();
        if (state.Route != GameLauncherRoute.Library)
            filterControls.Add(UI.Button("Back",
                state.Route switch
                {
                    GameLauncherRoute.AddGames => "game-launcher.add.back",
                    GameLauncherRoute.Running => "game-launcher.running.back",
                    GameLauncherRoute.Categories => "game-launcher.categories.back",
                    GameLauncherRoute.Category => "game-launcher.category.back",
                    _ => "game-launcher.hidden.back",
                },
                state.Route switch
                {
                    GameLauncherRoute.AddGames => "game-launcher.add.back",
                    GameLauncherRoute.Running => "game-launcher.running.back",
                    GameLauncherRoute.Categories => "game-launcher.categories.back",
                    GameLauncherRoute.Category => "game-launcher.category.back",
                    _ => "game-launcher.hidden.back",
                })
                .Disabled(!state.Interactive));
        else
        {
            filterControls.Add(UI.Button("Add games", "game-launcher.add.open",
                    "game-launcher.add.open")
                .Disabled(!state.Interactive));
            filterControls.Add(UI.Button("Add running app", "game-launcher.running.open",
                    "game-launcher.running.open")
                .Disabled(!state.Interactive));
            filterControls.Add(UI.Button(
                    $"Hidden ({state.Organization.ExcludedSavedIds.Count})",
                    "game-launcher.hidden.open", "game-launcher.hidden.open")
                .Disabled(!state.Interactive ||
                    state.Organization.ExcludedSavedIds.Count == 0));
            filterControls.Add(UI.Button(
                    $"Categories ({state.Organization.Categories.Count})",
                    "game-launcher.categories.open", "game-launcher.categories.open")
                .Disabled(!state.Interactive));
            filterControls.Add(UI.Switch("Favorites", state.FavoriteFilter,
                    "game-launcher.filter.favorites", "game-launcher.filter.favorites")
                .Disabled(!state.Interactive ||
                    state.Organization.FavoriteSavedIds.Count == 0));
            filterControls.Add(UI.Button(state.RecentMode switch
                {
                    GameLauncherRecentMode.RecentFirst => "Recent: First",
                    GameLauncherRecentMode.RecentOnly => "Recent: Only",
                    _ => "Recent: Off",
                }, "game-launcher.filter.recent", "game-launcher.filter.recent")
                .Disabled(!state.Interactive ||
                    state.Organization.RecentSavedIds.Count == 0));
        }
        if (state.Route is not (GameLauncherRoute.Running or GameLauncherRoute.Categories))
        {
            filterControls.Add(UI.Button("Source: " + (state.Query.SourceAttribution ?? "All"),
                    "game-launcher.filter.source", "game-launcher.filter.source")
                .Disabled(!state.Interactive));
            filterControls.Add(UI.Button("Sort: " + (state.Query.Sort switch
                {
                    WidgetAppLibrarySortOrder.DisplayNameDescending => "Z–A",
                    WidgetAppLibrarySortOrder.SourceThenDisplayName => "Source",
                    _ => "A–Z",
                }), "game-launcher.filter.sort", "game-launcher.filter.sort")
                .Disabled(!state.Interactive));
            filterControls.Add(UI.Button("Clear", "game-launcher.query.clear",
                    "game-launcher.query.clear")
                .Disabled(!state.Interactive ||
                    state.Query.SearchText is null &&
                    state.Query.Sort == WidgetAppLibrarySortOrder.DisplayName &&
                    (state.Route == GameLauncherRoute.Library ||
                     !state.FavoriteFilter &&
                     state.RecentMode == GameLauncherRecentMode.Off &&
                     state.Query.SourceAttribution is null)));
        }

        WidgetElement queryControls;
        if (state.Route is GameLauncherRoute.Running or GameLauncherRoute.Categories)
        {
            queryControls =
                UI.HorizontalScroll("game-launcher.query", filterControls.ToArray())
                    .Classes("game-launcher-query", "game-launcher-fixed");
        }
        else
        {
            var queryChildren = new List<WidgetElement>
            {
                UI.TextEntry(
                        state.Query.SearchText ?? string.Empty,
                        state.Route switch
                        {
                            GameLauncherRoute.AddGames =>
                                "Search trusted installed registrations",
                            GameLauncherRoute.Hidden => "Search hidden games",
                            _ => "Search installed games",
                        },
                        "game-launcher.search.commit",
                        "game-launcher.search",
                        WidgetAppLibraryQuery.MaximumSearchTextLength)
                    .Disabled(!state.Interactive)
                    .Classes("game-launcher-search"),
            };
            if (state.Route == GameLauncherRoute.Library)
                queryChildren.Add(UI.HorizontalScroll("game-launcher.collections",
                        state.Collections.Select(option => UI.Switch(
                                option.Label,
                                option.Selected,
                                option.ActionId,
                                option.ActionId)
                            .Disabled(!state.Interactive)).ToArray())
                    .Classes("game-launcher-collections"));
            queryChildren.Add(UI.HorizontalScroll(
                    "game-launcher.filters", filterControls.ToArray())
                .Classes("game-launcher-filters")
                .VisibleWhen(ResponsiveVisibility.ExpandedOnly));
            queryControls = UI.Stack("game-launcher.query", queryChildren.ToArray())
                .Classes("game-launcher-query", "game-launcher-fixed");
        }

        WidgetElement content;
        string? initialFocus = snapshot.RequestedFocusId;
        if (state.Route == GameLauncherRoute.Categories)
        {
            var categoryRows = state.Organization.Categories.Select(category =>
                UI.Card("game-launcher.category.card." + category.Id,
                    UI.Text($"{category.Name} · {category.SavedIds.Count} games",
                        "game-launcher.category.summary." + category.Id,
                        $"{category.Name}, {category.SavedIds.Count} games"),
                    UI.TextEntry(category.Name, "Rename category",
                            "game-launcher.category.rename." + category.Id,
                            "game-launcher.category.name." + category.Id,
                            GameLauncherPrivateState.MaximumCategoryNameLength)
                        .Disabled(!state.Interactive || state.OrganizationBusy),
                    UI.Row("game-launcher.category.actions." + category.Id,
                        UI.Button("Open", "game-launcher.category.open." + category.Id,
                                "game-launcher.category.open-button." + category.Id)
                            .Disabled(!state.Interactive),
                        UI.Button("Delete", "game-launcher.category.delete." + category.Id,
                                "game-launcher.category.delete-button." + category.Id)
                            .Disabled(!state.Interactive || state.OrganizationBusy)))
                    .Classes("game-launcher-category"))
                .ToArray();
            var management = new List<WidgetElement>
            {
                UI.TextEntry(string.Empty, "Create category",
                        "game-launcher.category.create", "game-launcher.category.create",
                        GameLauncherPrivateState.MaximumCategoryNameLength)
                    .Disabled(!state.Interactive || state.OrganizationBusy ||
                        state.Organization.Categories.Count >=
                            GameLauncherPrivateState.MaximumCategories),
                UI.Button("All Games", "game-launcher.categories.back",
                    "game-launcher.categories.all-games")
                    .Disabled(!state.Interactive),
            };
            management.AddRange(categoryRows);
            content = UI.Stack("game-launcher.content",
                    UI.Text("Create local categories within the shared 64 KiB " +
                            "organization budget. Membership uses exact saved game identity.",
                        "game-launcher.categories.help", "Category help"),
                    UI.VerticalScroll("game-launcher.categories.list",
                        management.ToArray()))
                .Classes("game-launcher-content");
            initialFocus = "game-launcher.category.create";
        }
        else if ((state.Route is GameLauncherRoute.AddGames or GameLauncherRoute.Running) &&
            snapshot.Items.Count != 0)
        {
            var tiles = snapshot.Items.Select(item => ManualTile(
                item, GameLauncherOrganizationPolicy.ReferencedSavedIds(state.Organization)
                    .Contains(item.Value.SavedId, StringComparer.Ordinal),
                state.Route == GameLauncherRoute.Running,
                state.Interactive && !state.OrganizationBusy)).ToArray();
            var scroll = UI.VerticalScroll(ScrollId,
                    UI.ResponsiveGrid("game-launcher.library.grid", 170, 5, tiles)
                        .Classes("game-launcher-grid"))
                .Classes("game-launcher-scroll");
            if (snapshot.HasBefore || snapshot.HasAfter)
                scroll = scroll.Paginate(
                    snapshot.HasBefore ? "game-launcher.library.cursor.before" : null,
                    snapshot.HasAfter ? "game-launcher.library.cursor.after" : null, 2);
            scroll = PageShortcuts(scroll, snapshot, state.Interactive);
            scroll = scroll with { CollectionAnchorKey = snapshot.Anchor?.Value };
            content = UI.Stack("game-launcher.content",
                    scroll,
                    UI.HorizontalScroll("game-launcher.actions",
                        UI.Button("Previous page", "game-launcher.previous",
                                "game-launcher.previous")
                            .Disabled(!snapshot.HasBefore || !state.Interactive),
                        UI.Button("Next page", "game-launcher.next",
                                "game-launcher.next")
                            .Disabled(!snapshot.HasAfter || !state.Interactive)))
                .Classes("game-launcher-content");
            initialFocus ??= GameLauncherIdentity.FocusId("add", snapshot.Items[0].Key);
        }
        else if (state.Route == GameLauncherRoute.Category)
        {
            var category = GameLauncherCategoryPolicy.Find(
                state.Organization, state.ActiveCategoryId);
            var resolved = snapshot.Items.Concat(state.FixedRows.All)
                .DistinctBy(item => item.Value.SavedId, StringComparer.Ordinal)
                .ToDictionary(
                item => item.Value.SavedId, StringComparer.Ordinal);
            var stored = state.Organization.Items.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var rows = category?.SavedIds
                .Where(savedId => !state.Organization.ExcludedSavedIds.Contains(
                    savedId, StringComparer.Ordinal))
                .Select(savedId => PresentedRowFor(savedId, resolved, stored))
                .Where(row => row is not null && MatchesFixedQuery(
                    row.Display, state.Query, favoriteFilter: false,
                    new Dictionary<string, int>(StringComparer.Ordinal)))
                .Select(row => row!)
                .ToArray() ?? [];
            if (rows.Length == 0)
            {
                content = UI.EmptyState(category is null ? "Category unavailable" :
                        $"{category.Name} is empty",
                    "Use Y on a current game and choose this category.",
                    "game-launcher.category.empty",
                    new ComponentAction("Back to All Games", "game-launcher.category.back",
                        WidgetGlyph.Play), WidgetGlyph.Play);
                initialFocus = "game-launcher.category.empty.action";
            }
            else
            {
                var tiles = rows.Select(row =>
                {
                    var current = row.Current;
                    var group = GameLauncherOrganizationPolicy.GroupFor(
                        state.Organization, row.Display.SavedId);
                    return Tile(
                        row.Display.DisplayName, row.Display.SourceAttribution,
                        row.Display.SavedId,
                        current?.Presentation.Artwork.Find(
                            WidgetAppLibraryArtworkRole.Tile)?.Handle,
                        state.LaunchingSavedId,
                        LaunchStateFor(state.LaunchStates, row.Display.SavedId),
                        current,
                        state.Organization.FavoriteSavedIds.Contains(
                            row.Display.SavedId, StringComparer.Ordinal),
                        group?.PreferredSavedId == row.Display.SavedId,
                        group?.SavedIds.Count ?? 0,
                        state.Interactive && !state.OrganizationBusy,
                        GameLauncherIdentity.Key(row.Display.SavedId),
                        pageBumpers: false,
                        collectionSwitch: state.Organization.Categories.Count != 0);
                }).ToArray();
                var scroll = UI.VerticalScroll(ScrollId,
                        UI.ResponsiveGrid("game-launcher.category.grid", 170, 5, tiles)
                            .Classes("game-launcher-grid"))
                    .Classes("game-launcher-scroll") with
                {
                    CollectionAnchorKey = GameLauncherIdentity.Key(
                        rows[0].Display.SavedId).Value,
                };
                content = UI.Stack("game-launcher.content", scroll,
                        UI.ControllerHint(ControllerButton.Y, "Game actions",
                            "game-launcher.category.hint.actions"))
                    .Classes("game-launcher-content");
                var focused = rows.FirstOrDefault(row => string.Equals(
                    row.Display.SavedId, state.HeroSavedId, StringComparison.Ordinal)) ?? rows[0];
                initialFocus ??= GameLauncherIdentity.FocusId(
                    "grid", GameLauncherIdentity.Key(focused.Display.SavedId));
            }
        }
        else if (state.Route == GameLauncherRoute.Hidden)
        {
            var resolved = snapshot.Items.Concat(state.FixedRows.All)
                .DistinctBy(item => item.Value.SavedId, StringComparer.Ordinal)
                .ToDictionary(
                item => item.Value.SavedId, StringComparer.Ordinal);
            var stored = state.Organization.Items.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var favorites = new Dictionary<string, int>(StringComparer.Ordinal);
            var rows = state.Organization.ExcludedSavedIds
                .Select(savedId => PresentedRowFor(savedId, resolved, stored))
                .Where(row => row is not null && MatchesFixedQuery(
                    row.Display, state.Query, favoriteFilter: false, favorites))
                .Select(row => row!)
                .OrderBy(row => row, PresentedRowComparer(state.Query.Sort))
                .Take(GameLauncherPrivateState.MaximumExcludedItems)
                .ToArray();
            if (rows.Length == 0)
            {
                content = UI.EmptyState("No hidden games",
                    state.Organization.ExcludedSavedIds.Count == 0
                        ? "Hidden games will appear here until you restore them."
                        : "No hidden games match the current filters.",
                    "game-launcher.hidden.empty",
                    new ComponentAction("Back", "game-launcher.hidden.back",
                        WidgetGlyph.Play),
                    WidgetGlyph.Play);
                initialFocus = "game-launcher.hidden.empty.action";
            }
            else
            {
                var tiles = rows.Select(row => HiddenTile(
                    row, state.Interactive && !state.OrganizationBusy)).ToArray();
                var scroll = UI.VerticalScroll(ScrollId,
                        UI.ResponsiveGrid("game-launcher.hidden.grid", 170, 5, tiles)
                            .Classes("game-launcher-grid"))
                    .Classes("game-launcher-scroll");
                content = UI.Stack("game-launcher.content", scroll,
                        UI.ControllerHint(ControllerButton.A, "Restore selected game",
                            "game-launcher.hint.restore"))
                    .Classes("game-launcher-content");
                initialFocus ??= GameLauncherIdentity.FocusId(
                    "hidden", GameLauncherIdentity.Key(rows[0].Display.SavedId));
            }
        }
        else if (snapshot.Items.Any(item =>
                     !state.Organization.ExcludedSavedIds.Contains(
                         item.Value.SavedId, StringComparer.Ordinal)) ||
                 state.FixedRows.All.Any(item =>
                     !state.Organization.ExcludedSavedIds.Contains(
                         item.Value.SavedId, StringComparer.Ordinal)))
        {
            var rail = GameLauncherHeroRailPolicy.Project(
                state, state.HeroSavedId, state.HeroIndex);
            var tiles = rail.Items.Select(row => Tile(
                    row.Display.DisplayName,
                    row.Display.SourceAttribution,
                    row.Display.SavedId,
                    row.Current?.Presentation.Artwork.Find(
                        WidgetAppLibraryArtworkRole.Tile)?.Handle,
                    state.LaunchingSavedId,
                    LaunchStateFor(state.LaunchStates, row.Display.SavedId),
                    row.Current,
                    row.Favorite,
                    row.Preferred,
                    row.GroupSize,
                    state.Interactive && !state.OrganizationBusy,
                    row.Key,
                    rail.PageBumpers,
                    row.CollectionItem,
                    collectionSwitch: state.Organization.Categories.Count != 0))
                .ToArray();
            var scroll = UI.HorizontalScroll(ScrollId, tiles)
                .Classes("game-launcher-scroll", "game-launcher-rail");
            if (snapshot.Status != WidgetPagedResourceStatus.Error &&
                (snapshot.HasBefore || snapshot.HasAfter))
                scroll = scroll.Paginate(
                    snapshot.HasBefore ? "game-launcher.library.cursor.before" : null,
                    snapshot.HasAfter ? "game-launcher.library.cursor.after" : null, 2);
            scroll = PageShortcuts(scroll, snapshot, state.Interactive);
            scroll = scroll with
            {
                CollectionAnchorKey = rail.CatalogAnchorKey,
            };
            var controls = UI.HorizontalScroll("game-launcher.actions",
                    UI.Button("Previous page", "game-launcher.previous", "game-launcher.previous")
                        .Disabled(!snapshot.HasBefore || !state.Interactive),
                    UI.Button("Refresh", "game-launcher.refresh", "game-launcher.refresh")
                        .Disabled(!state.Interactive),
                    UI.Button("Next page", "game-launcher.next", "game-launcher.next")
                        .Disabled(!snapshot.HasAfter || !state.Interactive),
                    UI.Button("Reset organization", "game-launcher.organization.reset",
                            "game-launcher.organization.reset")
                        .Disabled(!state.Interactive || state.OrganizationBusy ||
                            state.Organization.FavoriteSavedIds.Count == 0 &&
                            state.Organization.VariantGroups.Count == 0),
                    UI.Button("Clear recent", "game-launcher.recent.clear",
                            "game-launcher.recent.clear")
                        .Disabled(!state.Interactive || state.OrganizationBusy ||
                            state.Organization.RecentSavedIds.Count == 0))
                .Classes("game-launcher-actions")
                .VisibleWhen(ResponsiveVisibility.ExpandedOnly);
            var hasActionableGame = state.Interactive && !state.OrganizationBusy &&
                state.LaunchingSavedId is null &&
                rail.Items.Any(row => row.Current is not null);
            var hintItems = new List<WidgetElement>();
            if (hasActionableGame)
            {
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.X, "Favorite", "game-launcher.hint.favorite"));
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.Y, "Game actions", "game-launcher.hint.actions"));
            }
            if (rail.PageBumpers)
            {
                if (state.Interactive && snapshot.Status == WidgetPagedResourceStatus.Ready &&
                    snapshot.HasBefore)
                    hintItems.Add(UI.ControllerHint(
                        ControllerButton.LeftBumper, "Previous page",
                        "game-launcher.hint.previous"));
                if (state.Interactive && snapshot.Status == WidgetPagedResourceStatus.Ready &&
                    snapshot.HasAfter)
                    hintItems.Add(UI.ControllerHint(
                        ControllerButton.RightBumper, "Next page",
                        "game-launcher.hint.next"));
            }
            else if (hasActionableGame)
            {
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.LeftBumper, "Group variants",
                    "game-launcher.hint.variant"));
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.RightBumper, "Prefer variant",
                    "game-launcher.hint.prefer"));
            }
            var children = new List<WidgetElement>
            {
                GameLauncherHeroRailPresentation.Render(
                        rail.Selected, state.LaunchingSavedId, state.LaunchStates)
                    .VisibleWhen(ResponsiveVisibility.ExpandedOnly),
                scroll,
                controls,
            };
            if (hintItems.Count != 0)
                children.Add(UI.Row(
                        "game-launcher.organization.hints", hintItems.ToArray())
                    .Classes("game-launcher-footer"));
            if (snapshot.Error is { } retained)
            {
                var retainedError = GameLauncherAvailabilityPresentation.Error(retained);
                children.Add(UI.Alert(retainedError.Title, retainedError.Message,
                        AlertTone.Warning, "game-launcher.retained-error",
                        new ComponentAction("Try again", "game-launcher.retry", WidgetGlyph.Refresh))
                    .Classes("game-launcher-warning"));
            }
            content = UI.Stack("game-launcher.content", children.ToArray())
                .Classes("game-launcher-content");
            initialFocus ??= rail.Selected?.FocusId;
        }
        else if (state.Route == GameLauncherRoute.Library &&
                 state.Organization.Items.Any(item =>
                     !state.Organization.ExcludedSavedIds.Contains(
                         item.SavedId, StringComparer.Ordinal)) && snapshot.Status is
                     WidgetPagedResourceStatus.NotLoaded or
                     WidgetPagedResourceStatus.Loading or
                     WidgetPagedResourceStatus.Refreshing or
                     WidgetPagedResourceStatus.Error or
                     WidgetPagedResourceStatus.Ready)
        {
            var warmRows = state.Organization.Items
                .Where(item => !state.Organization.ExcludedSavedIds.Contains(
                    item.SavedId, StringComparer.Ordinal))
                .ToArray();
            var warm = warmRows.Select(item => Tile(
                item.DisplayName, item.SourceAttribution, item.SavedId, null,
                null, launchState: null,
                current: null,
                favorite: state.Organization.FavoriteSavedIds.Contains(
                    item.SavedId, StringComparer.Ordinal),
                preferred: state.Organization.VariantGroups.Any(group =>
                    group.PreferredSavedId == item.SavedId),
                groupSize: GameLauncherOrganizationPolicy.GroupFor(
                    state.Organization, item.SavedId)?.SavedIds.Count ?? 0,
                interactive: false, key: GameLauncherIdentity.Key(item.SavedId),
                pageBumpers: false)).ToArray();
            var warmScroll = UI.HorizontalScroll(ScrollId, warm) with
                { CollectionAnchorKey = GameLauncherIdentity.Key(
                    warmRows[0].SavedId).Value };
            warmScroll = warmScroll.Classes("game-launcher-scroll", "game-launcher-rail");
            content = UI.Stack("game-launcher.content",
                    GameLauncherHeroRailPresentation.Fallback(
                        warmRows[0].DisplayName,
                        "Checking current availability · " + warmRows[0].SourceAttribution),
                    UI.Alert("Checking installed games",
                        snapshot.Error?.Message ?? "Saved display rows cannot launch until current provider resolution succeeds.",
                        snapshot.Error is null ? AlertTone.Info : AlertTone.Warning,
                        "game-launcher.warm-status",
                        new ComponentAction("Try again", "game-launcher.retry", WidgetGlyph.Refresh)),
                    warmScroll)
                .Classes("game-launcher-content");
            initialFocus = "game-launcher.warm-status.action";
        }
        else if (snapshot.Status is WidgetPagedResourceStatus.Loading or
                 WidgetPagedResourceStatus.Refreshing or WidgetPagedResourceStatus.NotLoaded)
        {
            content = UI.Stack("game-launcher.content",
                    GameLauncherHeroRailPresentation.Fallback(
                        "Loading installed games",
                        "Reading the trusted installed-game catalog…"),
                    UI.Card("game-launcher.loading",
                        UI.LoadingIndicator("game-launcher.loading.indicator",
                            "Loading installed games"),
                        UI.Text("Reading the trusted installed-game catalog…",
                            "game-launcher.loading.text", "Loading installed games")))
                .Classes("game-launcher-content");
        }
        else if (snapshot.Status == WidgetPagedResourceStatus.Error)
        {
            var error = GameLauncherAvailabilityPresentation.Error(snapshot.Error);
            content = UI.Stack("game-launcher.content",
                    GameLauncherHeroRailPresentation.Fallback(
                        error.Title, error.Message),
                    UI.Alert(error.Title, error.Message,
                        AlertTone.Danger, "game-launcher.error",
                        new ComponentAction("Try again", "game-launcher.retry",
                            WidgetGlyph.Refresh)))
                .Classes("game-launcher-content");
            initialFocus = "game-launcher.error.action";
        }
        else
        {
            content = UI.Stack("game-launcher.content",
                    GameLauncherHeroRailPresentation.Fallback(
                        "No installed games",
                        "Trusted installed games will appear here."),
                    UI.EmptyState("No installed games",
                        "No trusted installed game registrations are currently available.",
                        "game-launcher.empty",
                        new ComponentAction("Refresh", "game-launcher.refresh",
                            WidgetGlyph.Refresh),
                        WidgetGlyph.Play))
                .Classes("game-launcher-content");
            initialFocus = "game-launcher.empty.action";
        }

        if (state.Route is GameLauncherRoute.Library or GameLauncherRoute.Category &&
            state.Organization.Categories.Count != 0 && state.Interactive &&
            !state.OrganizationBusy)
        {
            content = UI.Stack("game-launcher.collection.scope",
                    content,
                    UI.Row("game-launcher.collection.hints",
                        UI.ControllerHint(ControllerButton.LeftTrigger,
                            "Previous collection",
                            "game-launcher.collection.hint.previous"),
                        UI.ControllerHint(ControllerButton.RightTrigger,
                            "Next collection",
                            "game-launcher.collection.hint.next"))
                        .Classes("game-launcher-footer"))
                .Shortcut(ControllerButton.LeftTrigger,
                    actionId: "game-launcher.collection.previous")
                .Shortcut(ControllerButton.RightTrigger,
                    actionId: "game-launcher.collection.next")
                .Classes("game-launcher-content");
        }
        content = content.AddClasses("game-launcher-main");
        var root = UI.Stack("game-launcher.root", header, sourceStatus, queryControls, content)
            .Classes("game-launcher-widget");
        return new WidgetView(root, initialFocus, Surface: Surface);
    }

    private static WidgetElement SourceStatus(
        IReadOnlyList<WidgetAppLibrarySource> sources,
        WidgetPagedResourceStatus collectionStatus)
    {
        if (sources.Count == 0)
            return UI.Text("Library source status will appear after the first load.",
                    "game-launcher.sources.pending",
                    "Library source status is pending")
                .Classes("game-launcher-source-summary");
        var refreshing = collectionStatus == WidgetPagedResourceStatus.Refreshing;
        var rows = sources.Select(source =>
        {
            var health = refreshing ? "Refreshing" : source.Health switch
            {
                WidgetAppLibrarySourceHealth.Healthy => "Healthy",
                WidgetAppLibrarySourceHealth.Degraded => "Degraded",
                WidgetAppLibrarySourceHealth.Unavailable => "Unavailable",
                WidgetAppLibrarySourceHealth.Refreshing => "Refreshing",
                _ => "Unavailable",
            };
            var detail = refreshing
                ? "Refreshing installed games"
                : GameLauncherAvailabilityPresentation.SourceDetail(source);
            var text = $"{source.DisplayName}: {health} · {detail}";
            return UI.Text(text, "game-launcher.source." + source.SourceId, text)
                .Classes("game-launcher-source-row");
        }).ToArray();
        var attention = sources.Count(source => source.Health is
            WidgetAppLibrarySourceHealth.Degraded or
            WidgetAppLibrarySourceHealth.Unavailable);
        var compactSummary = $"{sources.Count} library {(sources.Count == 1 ? "source" : "sources")}" +
            (attention == 0 ? " · Healthy" : $" · {attention} need attention");
        return UI.Stack("game-launcher.sources",
                UI.Text(compactSummary, "game-launcher.sources.compact", compactSummary)
                    .Classes("game-launcher-source-summary")
                    .VisibleWhen(ResponsiveVisibility.CompactOnly),
                UI.Stack("game-launcher.sources.expanded",
                        UI.SectionHeader("Library sources", "game-launcher.sources.header",
                            description: $"{sources.Count} active {(sources.Count == 1 ? "source" : "sources")}"),
                        UI.Stack("game-launcher.sources.rows", rows)
                            .Classes("game-launcher-source-rows"))
                    .Classes("game-launcher-source-expanded")
                    .VisibleWhen(ResponsiveVisibility.ExpandedOnly))
            .Classes("game-launcher-source-status");
    }

    private static ScrollElement PageShortcuts(
        ScrollElement scroll,
        WidgetCursorResourceSnapshot<GameLauncherItem> snapshot,
        bool interactive)
    {
        if (!interactive || snapshot.Status != WidgetPagedResourceStatus.Ready)
            return scroll;
        if (snapshot.HasBefore)
            scroll = scroll.Shortcut(
                ControllerButton.LeftBumper, "game-launcher.previous");
        if (snapshot.HasAfter)
            scroll = scroll.Shortcut(
                ControllerButton.RightBumper, "game-launcher.next");
        return scroll;
    }

    private sealed record PresentedRow(
        GameLauncherItem? Current,
        GameLauncherDisplayItem Display);

    private static PresentedRow? PresentedRowFor(
        string savedId,
        IReadOnlyDictionary<string, GameLauncherItem> resolved,
        IReadOnlyDictionary<string, GameLauncherDisplayItem> stored)
    {
        if (resolved.TryGetValue(savedId, out var current))
            return new(current, new(current.Value.SavedId,
                current.Presentation.DisplayName,
                current.Presentation.Source.DisplayName));
        return stored.TryGetValue(savedId, out var display)
            ? new(null, display)
            : null;
    }

    private static bool MatchesFixedQuery(
        GameLauncherDisplayItem item,
        WidgetAppLibraryQuery query,
        bool favoriteFilter,
        IReadOnlyDictionary<string, int> favorites) =>
        (!favoriteFilter || favorites.ContainsKey(item.SavedId)) &&
        (query.SearchText is null || item.DisplayName.Contains(
            query.SearchText, StringComparison.OrdinalIgnoreCase)) &&
        (query.SourceAttribution is null || string.Equals(
            item.SourceAttribution, query.SourceAttribution,
            StringComparison.OrdinalIgnoreCase));

    private static IComparer<PresentedRow> PresentedRowComparer(
        WidgetAppLibrarySortOrder sort) => Comparer<PresentedRow>.Create((left, right) =>
        {
            var result = sort == WidgetAppLibrarySortOrder.SourceThenDisplayName
                ? StringComparer.OrdinalIgnoreCase.Compare(
                    left.Display.SourceAttribution, right.Display.SourceAttribution)
                : 0;
            if (result == 0)
                result = StringComparer.OrdinalIgnoreCase.Compare(
                    left.Display.DisplayName, right.Display.DisplayName);
            if (sort == WidgetAppLibrarySortOrder.DisplayNameDescending) result = -result;
            return result != 0 ? result : string.CompareOrdinal(
                left.Display.SavedId, right.Display.SavedId);
        });

    private static WidgetElement ManualTile(
        GameLauncherItem item,
        bool included,
        bool runningRoute,
        bool interactive)
    {
        var automatic = item.Presentation.Kind == WidgetAppLibraryKind.Game;
        var kind = item.Presentation.Kind switch
        {
            WidgetAppLibraryKind.Game => "Game",
            WidgetAppLibraryKind.Application => "Application",
            _ => "Unknown",
        };
        var state = automatic ? "Included" : included
            ? runningRoute ? "Already included" : "Added"
            : "Available";
        var actionId = automatic
            ? "game-launcher.manual.included"
            : "game-launcher.manual.toggle";
        return UI.Tile(item.Presentation.DisplayName, state,
                actionId,
                GameLauncherIdentity.FocusId("add", item.Key),
                subtitle: $"{kind} · {item.Presentation.Source.DisplayName}",
                artwork: Artwork(item),
                accessibilityLabel: $"{item.Presentation.DisplayName}, {kind}, " +
                    (automatic ? "Included automatically" : included
                        ? runningRoute ? "Already included" : "Added, remove from library"
                        : "Available, add to library"),
                orientation: ActionSurfaceOrientation.Vertical)
            .Disabled(!interactive || automatic || runningRoute && included)
            .CollectionItem(item.Key)
            .Classes("game-launcher-tile");
    }

    private static WidgetElement HiddenTile(PresentedRow row, bool interactive)
    {
        var current = row.Current;
        var artwork = current is null
            ? TileArtwork.FromGlyph(WidgetGlyph.Play, row.Display.DisplayName)
            : Artwork(current);
        var availability = current is null ? "Unavailable · Restore" : "Hidden · Restore";
        return UI.Tile(row.Display.DisplayName, availability,
                "game-launcher.restore",
                GameLauncherIdentity.FocusId(
                    "hidden", GameLauncherIdentity.Key(row.Display.SavedId)),
                subtitle: row.Display.SourceAttribution,
                artwork: artwork,
                accessibilityLabel:
                    $"{row.Display.DisplayName}, {row.Display.SourceAttribution}, {availability}",
                orientation: ActionSurfaceOrientation.Vertical)
            .Disabled(!interactive)
            .Classes("game-launcher-tile");
    }

    private static WidgetElement Tile(
        string title,
        string source,
        string savedId,
        string? artworkHandle,
        string? launchingSavedId,
        GameLauncherLaunchState? launchState,
        GameLauncherItem? current,
        bool favorite,
        bool preferred,
        int groupSize,
        bool interactive,
        WidgetCollectionItemKey key,
        bool pageBumpers,
        bool collectionItem = true,
        bool collectionSwitch = false)
    {
        var id = GameLauncherIdentity.FocusId("grid", key);
        var launching = string.Equals(savedId, launchingSavedId, StringComparison.Ordinal);
        var artwork = current?.ArtworkPngBase64 is { } png
            ? TileArtwork.FromInlinePng(png, title, ImageFit.Cover)
            : artworkHandle is { Length: > 0 }
            ? TileArtwork.FromHandle(new WidgetArtworkHandle(artworkHandle), title, ImageFit.Cover)
            : TileArtwork.FromGlyph(WidgetGlyph.Play, title);
        var availability = GameLauncherAvailabilityPresentation.Tile(current, interactive);
        var state = launching ? "Pending" : launchState switch
        {
            GameLauncherLaunchState.RequestAccepted => "Request accepted",
            GameLauncherLaunchState.LauncherStarted => "Launcher started",
            GameLauncherLaunchState.Running => "Running",
            GameLauncherLaunchState.Failed => "Failed",
            GameLauncherLaunchState.Ended => "Ended",
            _ => availability.Status,
        };
        var traits = new List<string>(3);
        if (favorite) traits.Add("Favorite");
        if (preferred) traits.Add("Preferred variant");
        if (groupSize > 1) traits.Add($"{groupSize} grouped variants");
        var subtitle = traits.Count == 0 ? source : source + " · " + string.Join(" · ", traits);
        var tile = UI.Tile(title, state,
                "game-launcher.launch", id, subtitle: subtitle, artwork: artwork,
                accessibilityLabel: $"{title}, {subtitle}, {state}",
                orientation: ActionSurfaceOrientation.Vertical)
            .Busy(launching || availability.Busy)
            .Disabled(!interactive || !availability.Launchable)
            .Classes("game-launcher-tile");
        if (interactive && current is not null && !launching)
            tile = tile
                .Shortcut(ControllerButton.X, actionId: "game-launcher.favorite")
                .Shortcut(ControllerButton.Y, actionId: GameLauncherActionSheet.OpenAction);
        if (interactive && current is not null && !launching && !pageBumpers)
            tile = tile
                .Shortcut(ControllerButton.LeftBumper, actionId: "game-launcher.variant")
                .Shortcut(ControllerButton.RightBumper, actionId: "game-launcher.prefer");
        if (interactive && current is not null && !launching && collectionSwitch)
            tile = tile
                .Shortcut(ControllerButton.LeftTrigger,
                    actionId: "game-launcher.collection.previous")
                .Shortcut(ControllerButton.RightTrigger,
                    actionId: "game-launcher.collection.next");
        return collectionItem ? tile.CollectionItem(key) : tile;
    }

    private static TileArtwork Artwork(GameLauncherItem item) =>
        item.ArtworkPngBase64 is { } png
            ? TileArtwork.FromInlinePng(
                png, item.Presentation.DisplayName, ImageFit.Cover)
            : item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile) is { } artwork
                ? TileArtwork.FromHandle(new WidgetArtworkHandle(artwork.Handle),
                    item.Presentation.DisplayName, ImageFit.Cover)
                : TileArtwork.FromGlyph(WidgetGlyph.Play, item.Presentation.DisplayName);

    private static GameLauncherLaunchState? LaunchStateFor(
        IReadOnlyDictionary<string, GameLauncherLaunchState> states,
        string savedId) => states.TryGetValue(savedId, out var state) ? state : null;
}
