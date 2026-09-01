using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryPresentationState(
    WidgetCursorResourceSnapshot<PlayniteLibraryItem> Collection,
    PlayniteLibraryPrivateState Organization,
    string Status,
    string? LaunchingSavedId,
    IReadOnlyDictionary<string, PlayniteLibraryLaunchState> LaunchStates,
    bool OrganizationBusy,
    bool Interactive,
    WidgetAppLibraryQuery Query,
    PlayniteLibraryRecentMode RecentMode,
    bool FavoriteFilter,
    PlayniteLibraryRoute Route,
    PlayniteLibraryFixedRows FixedRows,
    IReadOnlyList<WidgetAppLibrarySource> Sources,
    string? HeroSavedId,
    int HeroIndex)
{
    internal string? ActiveCategoryId { get; init; }
    internal bool SearchExpanded { get; init; }
    internal IReadOnlyList<PlayniteLibraryCollectionOption> Collections { get; init; } = [];
    internal IReadOnlyDictionary<string, string?> CompletionStatuses { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);
}

internal enum PlayniteLibraryRecentMode
{
    Off,
    RecentFirst,
    RecentOnly,
}

internal static class PlayniteLibraryPresentation
{
    internal const string ScrollId = "playnite-library.library.scroll";
    internal const string HomeRailId = "playnite-library.library.grid";
    internal const string RetryId = "playnite-library.retry";
    private static readonly WidgetSurfaceHints Surface = new()
    {
        Mode = WidgetSurfaceMode.Wide,
        WidthMode = WidgetSurfaceAxisMode.Preferred,
        HeightMode = WidgetSurfaceAxisMode.Preferred,
        PreferredWidth = 1180,
        PreferredHeight = 760,
        MinimumWidth = 520,
        MinimumHeight = 420,
        Appearance = WidgetSurfaceAppearance.Transparent,
    };

    internal static WidgetView Render(PlayniteLibraryPresentationState state)
    {
        var snapshot = state.Collection;
        var routeTitle = state.Route switch
        {
            PlayniteLibraryRoute.Browse => "Browse games",
            PlayniteLibraryRoute.Management => "Library management",
            PlayniteLibraryRoute.Hidden => "Hidden games",
            PlayniteLibraryRoute.Categories => "Categories",
            PlayniteLibraryRoute.Category => PlayniteLibraryCategoryPolicy.Find(
                state.Organization, state.ActiveCategoryId)?.Name ?? "Category",
            PlayniteLibraryRoute.PlayniteConnection => "Playnite connection",
            _ => "Playnite Library",
        };
        WidgetElement header = state.Route == PlayniteLibraryRoute.Library
            ? UI.Row("playnite-library.header",
                    UI.Stack("playnite-library.header.content",
                UI.Text(routeTitle, "playnite-library.compact.title", routeTitle)
                    .Classes("playnite-library-title")
                    .VisibleWhen(ResponsiveVisibility.CompactOnly),
                UI.Text(state.Status, "playnite-library.compact.status", state.Status)
                    .Classes("playnite-library-status")
                    .VisibleWhen(ResponsiveVisibility.CompactOnly),
                UI.Stack("playnite-library.header.expanded",
                        UI.Text("INSTALLED GAMES", "playnite-library.eyebrow", "Installed games")
                            .Classes("playnite-library-eyebrow"),
                        UI.Text(routeTitle, "playnite-library.title", routeTitle)
                            .Classes("playnite-library-title"),
                        UI.Text(state.Status, "playnite-library.status", state.Status)
                            .Classes("playnite-library-status"))
                    .Classes("playnite-library-header-expanded")
                    .VisibleWhen(ResponsiveVisibility.ExpandedOnly))
                    .Classes("playnite-library-header"),
                LibraryNavigation(state))
                .Classes("playnite-library-header")
            : UI.Stack("playnite-library.header",
                UI.Text(routeTitle, "playnite-library.compact.title", routeTitle)
                    .Classes("playnite-library-title")
                    .VisibleWhen(ResponsiveVisibility.CompactOnly),
                UI.Text(state.Status, "playnite-library.compact.status", state.Status)
                    .Classes("playnite-library-status")
                    .VisibleWhen(ResponsiveVisibility.CompactOnly),
                UI.Stack("playnite-library.header.expanded",
                        UI.Text("INSTALLED GAMES", "playnite-library.eyebrow", "Installed games")
                            .Classes("playnite-library-eyebrow"),
                        UI.Text(routeTitle, "playnite-library.title", routeTitle)
                            .Classes("playnite-library-title"),
                        UI.Text(state.Status, "playnite-library.status", state.Status)
                            .Classes("playnite-library-status"))
                    .Classes("playnite-library-header-expanded")
                    .VisibleWhen(ResponsiveVisibility.ExpandedOnly))
                .Classes("playnite-library-header");
        var filterControls = new List<WidgetElement>();
        if (state.Route != PlayniteLibraryRoute.Library)
            filterControls.Add(UI.Button("Back",
                state.Route switch
                {
                    PlayniteLibraryRoute.Browse => "playnite-library.browse.back",
                    PlayniteLibraryRoute.Management => "playnite-library.management.back",
                    PlayniteLibraryRoute.Categories => "playnite-library.categories.back",
                    PlayniteLibraryRoute.Category => "playnite-library.category.back",
                    _ => "playnite-library.hidden.back",
                },
                state.Route switch
                {
                    PlayniteLibraryRoute.Browse => "playnite-library.browse.back",
                    PlayniteLibraryRoute.Management => "playnite-library.management.back",
                    PlayniteLibraryRoute.Categories => "playnite-library.categories.back",
                    PlayniteLibraryRoute.Category => "playnite-library.category.back",
                    _ => "playnite-library.hidden.back",
                })
                .Disabled(!state.Interactive));
        if (state.Route == PlayniteLibraryRoute.Browse)
        {
            filterControls.Add(UI.Switch("Favorites", state.FavoriteFilter,
                    "playnite-library.filter.favorites", "playnite-library.filter.favorites")
                .Disabled(!state.Interactive ||
                    state.Organization.FavoriteSavedIds.Count == 0));
            filterControls.Add(UI.Button(state.RecentMode switch
                {
                    PlayniteLibraryRecentMode.RecentFirst => "Recent: First",
                    PlayniteLibraryRecentMode.RecentOnly => "Recent: Only",
                    _ => "Recent: Off",
                }, "playnite-library.filter.recent", "playnite-library.filter.recent")
                .Disabled(!state.Interactive ||
                    state.Organization.RecentSavedIds.Count == 0));
        }
        if (state.Route == PlayniteLibraryRoute.Browse)
        {
            filterControls.Add(UI.Button("Source: " + (state.Query.SourceAttribution ?? "All"),
                    "playnite-library.filter.source", "playnite-library.filter.source")
                .Disabled(!state.Interactive));
            filterControls.Add(UI.Button("Sort: " + (state.Query.Sort switch
                {
                    WidgetAppLibrarySortOrder.DisplayNameDescending => "Z–A",
                    WidgetAppLibrarySortOrder.SourceThenDisplayName => "Source",
                    _ => "A–Z",
                }), "playnite-library.filter.sort", "playnite-library.filter.sort")
                .Disabled(!state.Interactive));
            filterControls.Add(UI.Button("Clear", "playnite-library.query.clear",
                    "playnite-library.query.clear")
                .Disabled(!state.Interactive ||
                    state.Query.SearchText is null &&
                    state.Query.Sort == WidgetAppLibrarySortOrder.DisplayName &&
                    (state.Route == PlayniteLibraryRoute.Library ||
                     !state.FavoriteFilter &&
                     state.RecentMode == PlayniteLibraryRecentMode.Off &&
                     state.Query.SourceAttribution is null)));
        }

        WidgetElement queryControls;
        if (state.Route is PlayniteLibraryRoute.Categories or
            PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Management)
        {
            queryControls =
                UI.HorizontalScroll("playnite-library.query", filterControls.ToArray())
                    .Classes("playnite-library-query");
        }
        else
        {
            var queryChildren = new List<WidgetElement>
            {
                UI.TextEntry(
                        state.Query.SearchText ?? string.Empty,
                        state.Route switch
                        {
                            PlayniteLibraryRoute.Hidden => "Search hidden games",
                            _ => "Search installed games",
                        },
                        "playnite-library.search.commit",
                        "playnite-library.search",
                        WidgetAppLibraryQuery.MaximumSearchTextLength)
                    .Disabled(!state.Interactive)
                    .Classes("playnite-library-search"),
            };
            queryChildren.Add(UI.HorizontalScroll(
                    "playnite-library.filters", filterControls.ToArray())
                .Classes("playnite-library-filters"));
            queryControls = UI.Stack("playnite-library.query", queryChildren.ToArray())
                .Classes("playnite-library-query");
        }

        WidgetElement content;
        var catalogPage = false;
        string? catalogAnchorKey = null;
        string? pageBeforeActionId = null;
        string? pageAfterActionId = null;
        var pageShortcuts = false;
        string? initialFocus = snapshot.RequestedFocusId;
        if (state.Route == PlayniteLibraryRoute.Management)
        {
            content = UI.Stack("playnite-library.content",
                    UI.Card("playnite-library.management.connection",
                        UI.Text("Playnite connection",
                            "playnite-library.management.connection.title",
                            "Playnite connection"),
                        UI.Text("Connection, source health, and library changes are managed by Playnite.",
                            "playnite-library.management.connection.help",
                            "Playnite manages the library"),
                        UI.Button("Open Playnite connection",
                                PlayniteLibraryWidget.PlayniteOpenActionId,
                                "playnite-library.management.connection.open")
                            .Disabled(!state.Interactive)),
                    UI.Card("playnite-library.management.library",
                        UI.Text("Library management", "playnite-library.management.library.title",
                            "Library management"),
                        UI.Text("Importing, metadata editing, and library-manager work belong in Playnite.",
                            "playnite-library.management.library.help",
                            "Library management belongs in Playnite"),
                        UI.Row("playnite-library.management.library.actions",
                            UI.Button($"Hidden ({state.Organization.ExcludedSavedIds.Count})",
                                    "playnite-library.hidden.open",
                                    "playnite-library.management.hidden")
                                .Disabled(!state.Interactive ||
                                    state.Organization.ExcludedSavedIds.Count == 0),
                            UI.Button($"Categories ({state.Organization.Categories.Count})",
                                    "playnite-library.categories.open",
                                    "playnite-library.management.categories")
                                .Disabled(!state.Interactive))))
                .Classes("playnite-library-content", "playnite-library-management-content");
            initialFocus = "playnite-library.management.connection.open";
        }
        else if (state.Route == PlayniteLibraryRoute.Categories)
        {
            var categoryRows = state.Organization.Categories.Select(category =>
                UI.Card("playnite-library.category.card." + category.Id,
                    UI.Text($"{category.Name} · {category.SavedIds.Count} games",
                        "playnite-library.category.summary." + category.Id,
                        $"{category.Name}, {category.SavedIds.Count} games"),
                    UI.Row("playnite-library.category.actions." + category.Id,
                        UI.Button("Open", "playnite-library.category.open." + category.Id,
                                "playnite-library.category.open-button." + category.Id)
                            .Disabled(!state.Interactive)))
                    .Classes("playnite-library-category"))
                .ToArray();
            var management = new List<WidgetElement>
            {
                UI.TextEntry(string.Empty, "Create category",
                        "playnite-library.category.create", "playnite-library.category.create",
                        PlayniteLibraryPrivateState.MaximumCategoryNameLength)
                    .Disabled(!state.Interactive || state.OrganizationBusy ||
                        state.Organization.Categories.Count >=
                            PlayniteLibraryPrivateState.MaximumCategories),
                UI.Button("All Games", "playnite-library.categories.back",
                    "playnite-library.categories.all-games")
                    .Disabled(!state.Interactive),
            };
            management.AddRange(categoryRows);
            content = UI.Stack("playnite-library.content",
                    UI.Text("Categories are read from Playnite. Creating or changing " +
                            "membership uses the exact current Playnite game identity.",
                        "playnite-library.categories.help", "Category help"),
                    UI.VerticalScroll("playnite-library.categories.list",
                        management.ToArray()))
                .Classes("playnite-library-content");
            initialFocus = "playnite-library.category.create";
        }
        else if (state.Route == PlayniteLibraryRoute.Category)
        {
            var category = PlayniteLibraryCategoryPolicy.Find(
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
                    "playnite-library.category.empty",
                    new ComponentAction("Back to All Games", "playnite-library.category.back",
                        WidgetGlyph.Play), WidgetGlyph.Play);
                initialFocus = "playnite-library.category.empty.action";
            }
            else
            {
                var tiles = rows.Select(row =>
                {
                    var current = row.Current;
                    var group = PlayniteLibraryOrganizationPolicy.GroupFor(
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
                        PlayniteLibraryIdentity.Key(row.Display.SavedId),
                        pageBumpers: false,
                        collectionSwitch: state.Organization.Categories.Count != 0);
                }).ToArray();
                catalogPage = true;
                catalogAnchorKey = PlayniteLibraryIdentity.Key(
                    rows[0].Display.SavedId).Value;
                content = UI.Stack("playnite-library.content",
                        GameGrid("playnite-library.category.grid", tiles),
                        UI.ControllerHint(ControllerButton.Y, "Game actions",
                            "playnite-library.category.hint.actions"))
                    .Classes("playnite-library-content");
                var focused = rows.FirstOrDefault(row => string.Equals(
                    row.Display.SavedId, state.HeroSavedId, StringComparison.Ordinal)) ?? rows[0];
                initialFocus ??= PlayniteLibraryIdentity.FocusId(
                    "grid", PlayniteLibraryIdentity.Key(focused.Display.SavedId));
            }
        }
        else if (state.Route == PlayniteLibraryRoute.Hidden)
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
                .Take(PlayniteLibraryPrivateState.MaximumExcludedItems)
                .ToArray();
            if (rows.Length == 0)
            {
                content = UI.EmptyState("No hidden games",
                    state.Organization.ExcludedSavedIds.Count == 0
                        ? "Hidden games will appear here until you restore them."
                        : "No hidden games match the current filters.",
                    "playnite-library.hidden.empty",
                    new ComponentAction("Back", "playnite-library.hidden.back",
                        WidgetGlyph.Play),
                    WidgetGlyph.Play);
                initialFocus = "playnite-library.hidden.empty.action";
            }
            else
            {
                var tiles = rows.Select(row => HiddenTile(
                    row, state.Interactive && !state.OrganizationBusy)).ToArray();
                catalogPage = true;
                // Hidden rows are display-only restore targets, not retained cursor
                // collection members. The outer catalog scroll must therefore not
                // declare an anchor for a key that this route does not publish.
                catalogAnchorKey = null;
                content = UI.Stack("playnite-library.content",
                        GameGrid("playnite-library.hidden.grid", tiles),
                        UI.ControllerHint(ControllerButton.A, "Restore selected game",
                            "playnite-library.hint.restore"))
                    .Classes("playnite-library-content");
                initialFocus ??= PlayniteLibraryIdentity.FocusId(
                    "hidden", PlayniteLibraryIdentity.Key(rows[0].Display.SavedId));
            }
        }
        else if (snapshot.Items.Any(item =>
                     !state.Organization.ExcludedSavedIds.Contains(
                         item.Value.SavedId, StringComparer.Ordinal)) ||
                 state.FixedRows.All.Any(item =>
                     !state.Organization.ExcludedSavedIds.Contains(
                         item.Value.SavedId, StringComparer.Ordinal)))
        {
            var rail = PlayniteLibraryHeroRailPolicy.Project(
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
                    collectionSwitch: state.Organization.Categories.Count != 0,
                    categories: state.Route == PlayniteLibraryRoute.Library
                        ? state.Organization.Categories : null,
                    focusSummaryContext: state.Route == PlayniteLibraryRoute.Library))
                .ToArray();
            catalogPage = true;
            catalogAnchorKey = rail.CatalogAnchorKey;
            if (snapshot.Status != WidgetPagedResourceStatus.Error &&
                (snapshot.HasBefore || snapshot.HasAfter))
            {
                pageBeforeActionId = snapshot.HasBefore
                    ? "playnite-library.library.cursor.before"
                    : null;
                pageAfterActionId = snapshot.HasAfter
                    ? "playnite-library.library.cursor.after"
                    : null;
            }
            pageShortcuts = true;
            var controls = UI.HorizontalScroll("playnite-library.actions",
                    UI.Button("Previous page", "playnite-library.previous", "playnite-library.previous")
                        .Disabled(!snapshot.HasBefore || !state.Interactive),
                    UI.Button("Refresh", "playnite-library.refresh", "playnite-library.refresh")
                        .Disabled(!state.Interactive),
                    UI.Button("Next page", "playnite-library.next", "playnite-library.next")
                        .Disabled(!snapshot.HasAfter || !state.Interactive),
                    UI.Button("Reset organization", "playnite-library.organization.reset",
                            "playnite-library.organization.reset")
                        .Disabled(!state.Interactive || state.OrganizationBusy ||
                            state.Organization.FavoriteSavedIds.Count == 0 &&
                            state.Organization.VariantGroups.Count == 0),
                    UI.Button("Clear recent", "playnite-library.recent.clear",
                            "playnite-library.recent.clear")
                        .Disabled(!state.Interactive || state.OrganizationBusy ||
                            state.Organization.RecentSavedIds.Count == 0))
                .Classes("playnite-library-actions")
                .VisibleWhen(ResponsiveVisibility.ExpandedOnly);
            var hasActionableGame = state.Interactive && !state.OrganizationBusy &&
                state.LaunchingSavedId is null &&
                rail.Items.Any(row => row.Current is not null);
            var hintItems = new List<WidgetElement>();
            if (hasActionableGame)
            {
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.X, "Favorite", "playnite-library.hint.favorite"));
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.Y, "Game actions", "playnite-library.hint.actions"));
            }
            if (rail.PageBumpers)
            {
                if (state.Interactive && snapshot.Status == WidgetPagedResourceStatus.Ready &&
                    snapshot.HasBefore)
                    hintItems.Add(UI.ControllerHint(
                        ControllerButton.LeftBumper, "Previous page",
                        "playnite-library.hint.previous"));
                if (state.Interactive && snapshot.Status == WidgetPagedResourceStatus.Ready &&
                    snapshot.HasAfter)
                    hintItems.Add(UI.ControllerHint(
                        ControllerButton.RightBumper, "Next page",
                        "playnite-library.hint.next"));
            }
            else if (hasActionableGame)
            {
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.LeftBumper, "Group variants",
                    "playnite-library.hint.variant"));
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.RightBumper, "Prefer variant",
                    "playnite-library.hint.prefer"));
            }
            WidgetElement catalog = state.Route switch
            {
                PlayniteLibraryRoute.Library => HomeRail(
                    snapshot, catalogAnchorKey, pageBeforeActionId, pageAfterActionId,
                    pageShortcuts, state.Interactive, tiles),
                PlayniteLibraryRoute.Browse => BrowseGrid("playnite-library.browse.grid", tiles),
                _ => GameGrid("playnite-library.library.grid", tiles),
            };
            var children = new List<WidgetElement>();
            if (state.Route != PlayniteLibraryRoute.Library)
            {
                children.Add(PlayniteLibraryHeroRailPresentation.Render(
                        rail.Selected, state.LaunchingSavedId, state.LaunchStates)
                    .VisibleWhen(ResponsiveVisibility.ExpandedOnly));
            }
            children.Add(catalog);
            children.Add(controls);
            if (hintItems.Count != 0)
                children.Add(UI.Row(
                        "playnite-library.organization.hints", hintItems.ToArray())
                    .Classes("playnite-library-footer"));
            if (snapshot.Error is { } retained)
            {
                var retainedError = PlayniteLibraryAvailabilityPresentation.Error(retained);
                children.Add(UI.Alert(retainedError.Title, retainedError.Message,
                        AlertTone.Warning, "playnite-library.retained-error",
                        new ComponentAction("Try again", "playnite-library.retry", WidgetGlyph.Refresh))
                    .Classes("playnite-library-warning"));
            }
            content = UI.Stack("playnite-library.content", children.ToArray())
                .Classes("playnite-library-content");
            initialFocus ??= rail.Selected?.FocusId;
        }
        else if (state.Route == PlayniteLibraryRoute.Library &&
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
                groupSize: PlayniteLibraryOrganizationPolicy.GroupFor(
                    state.Organization, item.SavedId)?.SavedIds.Count ?? 0,
                interactive: false, key: PlayniteLibraryIdentity.Key(item.SavedId),
                pageBumpers: false)).ToArray();
            catalogPage = true;
            catalogAnchorKey = PlayniteLibraryIdentity.Key(
                warmRows[0].SavedId).Value;
            content = UI.Stack("playnite-library.content",
                    PlayniteLibraryHeroRailPresentation.Fallback(
                        warmRows[0].DisplayName,
                        "Checking current availability · " + warmRows[0].SourceAttribution),
                    UI.Alert("Checking installed games",
                        snapshot.Error?.Message ?? "Saved display rows cannot launch until current provider resolution succeeds.",
                        snapshot.Error is null ? AlertTone.Info : AlertTone.Warning,
                        "playnite-library.warm-status",
                        new ComponentAction("Try again", "playnite-library.retry", WidgetGlyph.Refresh)),
                    GameGrid("playnite-library.library.grid", warm))
                .Classes("playnite-library-content");
            initialFocus = "playnite-library.warm-status.action";
        }
        else if (snapshot.Status is WidgetPagedResourceStatus.Loading or
                 WidgetPagedResourceStatus.Refreshing or WidgetPagedResourceStatus.NotLoaded)
        {
            content = UI.Stack("playnite-library.content",
                    PlayniteLibraryHeroRailPresentation.Fallback(
                        "Loading installed games",
                        "Reading the trusted installed-game catalog…"),
                    UI.Card("playnite-library.loading",
                        UI.LoadingIndicator("playnite-library.loading.indicator",
                            "Loading installed games"),
                        UI.Text("Reading the trusted installed-game catalog…",
                            "playnite-library.loading.text", "Loading installed games")))
                .Classes("playnite-library-content");
        }
        else if (snapshot.Status == WidgetPagedResourceStatus.Error)
        {
            var error = PlayniteLibraryAvailabilityPresentation.Error(snapshot.Error);
            content = UI.Stack("playnite-library.content",
                    PlayniteLibraryHeroRailPresentation.Fallback(
                        error.Title, error.Message),
                    UI.Alert(error.Title, error.Message,
                        AlertTone.Danger, "playnite-library.error",
                        new ComponentAction("Try again", "playnite-library.retry",
                            WidgetGlyph.Refresh)))
                .Classes("playnite-library-content");
            initialFocus = "playnite-library.error.action";
        }
        else
        {
            content = UI.Stack("playnite-library.content",
                    PlayniteLibraryHeroRailPresentation.Fallback(
                        "No installed games",
                        "Trusted installed games will appear here."),
                    UI.EmptyState("No installed games",
                        "No trusted installed game registrations are currently available.",
                        "playnite-library.empty",
                        new ComponentAction("Refresh", "playnite-library.refresh",
                            WidgetGlyph.Refresh),
                        WidgetGlyph.Play))
                .Classes("playnite-library-content");
            initialFocus = "playnite-library.empty.action";
        }

        if (state.Route is PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Category &&
            state.Organization.Categories.Count != 0 && state.Interactive &&
            !state.OrganizationBusy)
        {
            content = UI.Stack("playnite-library.collection.scope",
                    content,
                    UI.Row("playnite-library.collection.hints",
                        UI.ControllerHint(ControllerButton.LeftTrigger,
                            "Previous collection",
                            "playnite-library.collection.hint.previous"),
                        UI.ControllerHint(ControllerButton.RightTrigger,
                            "Next collection",
                            "playnite-library.collection.hint.next"))
                        .Classes("playnite-library-footer"))
                .Shortcut(ControllerButton.LeftTrigger,
                    actionId: "playnite-library.collection.previous")
                .Shortcut(ControllerButton.RightTrigger,
                    actionId: "playnite-library.collection.next")
                .Classes("playnite-library-content");
        }
        content = content.AddClasses("playnite-library-main");
        WidgetElement root;
        if (catalogPage && state.Route == PlayniteLibraryRoute.Library)
        {
            var homeContent = UI.Stack("playnite-library.home.focus-content",
                header, content)
                .Classes("playnite-library-page-scroll", "playnite-library-home-content",
                    "playnite-library-home-foreground");
            root = UI.Stack("playnite-library.root",
                    UI.BackgroundSurface(
                            UI.FocusPresentationSurface(
                                homeContent,
                                DefaultFocusedGameSummary(),
                                "playnite-library.home.focus-summary"),
                            "playnite-library.cinematic", artwork: null)
                        .UseFocusedDescendantArtwork()
                        .AddClasses("playnite-library-cinematic",
                            "playnite-library-home-background"))
                .Classes("playnite-library-home-surface");
        }
        else if (catalogPage && state.Route == PlayniteLibraryRoute.Browse)
        {
            var page = UI.VerticalScroll(ScrollId, header, queryControls, content)
                .Classes("playnite-library-page-scroll", "playnite-library-browse-scroll") with
                {
                    CollectionAnchorKey = catalogAnchorKey,
                };
            if (pageBeforeActionId is not null || pageAfterActionId is not null)
                page = page.Paginate(pageBeforeActionId, pageAfterActionId, 2);
            page = PageShortcuts(page, snapshot, state.Interactive);
            root = UI.Stack("playnite-library.root", page)
                .Classes("playnite-library-widget", "playnite-library-route-shell",
                    "playnite-library-browse");
        }
        else if (catalogPage)
        {
            var page = UI.VerticalScroll(ScrollId, header, queryControls, content)
                .Classes("playnite-library-page-scroll");
            if (pageBeforeActionId is not null || pageAfterActionId is not null)
                page = page.Paginate(pageBeforeActionId, pageAfterActionId, 2);
            if (pageShortcuts)
                page = PageShortcuts(page, snapshot, state.Interactive);
            page = page with { CollectionAnchorKey = catalogAnchorKey };
            root = UI.Stack("playnite-library.root", page)
                .Classes("playnite-library-widget", "playnite-library-canvas");
        }
        else
        {
            var children = state.Route == PlayniteLibraryRoute.Management
                ? new[] { header, SourceStatus(state.Sources, snapshot.Status), queryControls, content }
                : new[] { header, queryControls, content };
            root = UI.Stack("playnite-library.root", children)
                .Classes("playnite-library-widget", "playnite-library-route-shell");
        }
        return new WidgetView(root, initialFocus, Surface: Surface);
    }

    private static WidgetElement SourceStatus(
        IReadOnlyList<WidgetAppLibrarySource> sources,
        WidgetPagedResourceStatus collectionStatus)
    {
        if (sources.Count == 0)
            return UI.Text("Library source status will appear after the first load.",
                    "playnite-library.sources.pending",
                    "Library source status is pending")
                .Classes("playnite-library-source-summary");
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
                : PlayniteLibraryAvailabilityPresentation.SourceDetail(source);
            var text = $"{source.DisplayName}: {health} · {detail}";
            return UI.Text(text, "playnite-library.source." + source.SourceId, text)
                .Classes("playnite-library-source-row");
        }).ToArray();
        var attention = sources.Count(source => source.Health is
            WidgetAppLibrarySourceHealth.Degraded or
            WidgetAppLibrarySourceHealth.Unavailable);
        var compactSummary = $"{sources.Count} library {(sources.Count == 1 ? "source" : "sources")}" +
            (attention == 0 ? " · Healthy" : $" · {attention} need attention");
        return UI.Stack("playnite-library.sources",
                UI.Text(compactSummary, "playnite-library.sources.compact", compactSummary)
                    .Classes("playnite-library-source-summary")
                    .VisibleWhen(ResponsiveVisibility.CompactOnly),
                UI.Stack("playnite-library.sources.expanded",
                        UI.SectionHeader("Library sources", "playnite-library.sources.header",
                            description: $"{sources.Count} active {(sources.Count == 1 ? "source" : "sources")}"),
                        UI.Stack("playnite-library.sources.rows", rows)
                            .Classes("playnite-library-source-rows"))
                    .Classes("playnite-library-source-expanded")
                    .VisibleWhen(ResponsiveVisibility.ExpandedOnly))
            .Classes("playnite-library-source-status");
    }

    private static ScrollElement PageShortcuts(
        ScrollElement scroll,
        WidgetCursorResourceSnapshot<PlayniteLibraryItem> snapshot,
        bool interactive)
    {
        if (!interactive || snapshot.Status != WidgetPagedResourceStatus.Ready)
            return scroll;
        if (snapshot.HasBefore)
            scroll = scroll.Shortcut(
                ControllerButton.LeftBumper, "playnite-library.previous");
        if (snapshot.HasAfter)
            scroll = scroll.Shortcut(
                ControllerButton.RightBumper, "playnite-library.next");
        return scroll;
    }

    private static GridElement BrowseGrid(string id, params WidgetElement[] tiles) =>
        UI.ResponsiveGrid(id, 180, maximumColumns: 6, tiles)
            .Classes("playnite-library-browse-grid");

    private static ScrollElement HomeRail(
        WidgetCursorResourceSnapshot<PlayniteLibraryItem> snapshot,
        string? collectionAnchorKey,
        string? nearStartActionId,
        string? nearEndActionId,
        bool pageShortcuts,
        bool interactive,
        params WidgetElement[] tiles)
    {
        var rail = UI.HorizontalScroll(HomeRailId, tiles)
            .Classes("playnite-library-rail") with
            {
                CollectionAnchorKey = collectionAnchorKey,
            };
        if (nearStartActionId is not null || nearEndActionId is not null)
            rail = rail.Paginate(nearStartActionId, nearEndActionId, 2);
        return pageShortcuts ? PageShortcuts(rail, snapshot, interactive) : rail;
    }

    private static GridElement GameGrid(string id, params WidgetElement[] tiles) =>
        UI.ResponsiveGrid(id, 220, 4, tiles)
            .Classes("playnite-library-grid");

    private sealed record PresentedRow(
        PlayniteLibraryItem? Current,
        PlayniteLibraryDisplayItem Display);

    private static PresentedRow? PresentedRowFor(
        string savedId,
        IReadOnlyDictionary<string, PlayniteLibraryItem> resolved,
        IReadOnlyDictionary<string, PlayniteLibraryDisplayItem> stored)
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
        PlayniteLibraryDisplayItem item,
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
        PlayniteLibraryItem item,
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
            ? "playnite-library.manual.included"
            : "playnite-library.manual.toggle";
        return UI.PosterTile(item.Presentation.DisplayName, state,
                actionId,
                PlayniteLibraryIdentity.FocusId("add", item.Key),
                subtitle: $"{kind} · {item.Presentation.Source.DisplayName}",
                artwork: PosterArtwork(item),
                accessibilityLabel: $"{item.Presentation.DisplayName}, {kind}, " +
                    (automatic ? "Included automatically" : included
                        ? runningRoute ? "Already included" : "Added, remove from library"
                        : "Available, add to library"))
            .Disabled(!interactive || automatic || runningRoute && included)
            .CollectionItem(item.Key)
            .AddClasses("playnite-library-tile");
    }

    private static WidgetElement HiddenTile(PresentedRow row, bool interactive)
    {
        var current = row.Current;
        var artwork = current is null ? null : PosterArtwork(current);
        var availability = current is null ? "Unavailable · Restore" : "Hidden · Restore";
        return UI.PosterTile(row.Display.DisplayName, availability,
                "playnite-library.restore",
                PlayniteLibraryIdentity.FocusId(
                    "hidden", PlayniteLibraryIdentity.Key(row.Display.SavedId)),
                subtitle: row.Display.SourceAttribution,
                artwork: artwork,
                accessibilityLabel:
                    $"{row.Display.DisplayName}, {row.Display.SourceAttribution}, {availability}")
            .Disabled(!interactive)
            .AddClasses("playnite-library-tile");
    }

    private static WidgetElement Tile(
        string title,
        string source,
        string savedId,
        string? artworkHandle,
        string? launchingSavedId,
        PlayniteLibraryLaunchState? launchState,
        PlayniteLibraryItem? current,
        bool favorite,
        bool preferred,
        int groupSize,
        bool interactive,
        WidgetCollectionItemKey key,
        bool pageBumpers,
        bool collectionItem = true,
        bool collectionSwitch = false,
        IReadOnlyList<PlayniteLibraryCategory>? categories = null,
        bool focusSummaryContext = false)
    {
        var id = PlayniteLibraryIdentity.FocusId("grid", key);
        var launching = string.Equals(savedId, launchingSavedId, StringComparison.Ordinal);
        var artwork = artworkHandle is { Length: > 0 }
            ? TileArtwork.FromHandle(new WidgetArtworkHandle(artworkHandle), title, ImageFit.Cover)
            : null;
        var availability = PlayniteLibraryAvailabilityPresentation.Tile(current, interactive);
        var state = launching ? "Pending" : launchState switch
        {
            PlayniteLibraryLaunchState.RequestAccepted => "Request accepted",
            PlayniteLibraryLaunchState.LauncherStarted => "Launcher started",
            PlayniteLibraryLaunchState.Running => "Running",
            PlayniteLibraryLaunchState.Failed => "Failed",
            PlayniteLibraryLaunchState.Ended => "Ended",
            _ => availability.Status,
        };
        var traits = new List<string>(3);
        if (favorite) traits.Add("Favorite");
        if (preferred) traits.Add("Preferred variant");
        if (groupSize > 1) traits.Add($"{groupSize} grouped variants");
        var subtitle = traits.Count == 0 ? source : source + " · " + string.Join(" · ", traits);
        var tile = UI.PosterTile(title, state,
                "playnite-library.launch", id, subtitle: subtitle, artwork: artwork,
                accessibilityLabel: $"{title}, {subtitle}, {state}")
            .Busy(launching || availability.Busy)
            .Disabled(!interactive || !availability.Launchable)
            .AddClasses("playnite-library-tile");
        if (interactive && current is not null && !launching)
        {
            var actionEnabled = availability.Launchable;
            tile = tile
                .Shortcut(ControllerButton.X, actionId: "playnite-library.favorite")
                .ContextAction("playnite-library.favorite",
                    favorite ? "Remove favorite" : "Add favorite", disabled: !actionEnabled)
                .ContextAction("playnite-library.hide", "Hide", disabled: !actionEnabled)
                .ContextAction("playnite-library.refresh-source", "Refresh source",
                    disabled: !actionEnabled);
            foreach (var category in (categories ?? []).Take(5))
            {
                var included = PlayniteLibraryCategoryPolicy.Contains(category, savedId);
                tile = tile.ContextAction("playnite-library.category." + category.Id,
                    included ? $"Remove from {category.Name}" : $"Add to {category.Name}",
                    disabled: !actionEnabled);
            }
        }
        if (interactive && current is not null && !launching && !pageBumpers)
            tile = tile
                .Shortcut(ControllerButton.LeftBumper, actionId: "playnite-library.variant")
                .Shortcut(ControllerButton.RightBumper, actionId: "playnite-library.prefer");
        if (interactive && current is not null && !launching && collectionSwitch)
            tile = tile
                .Shortcut(ControllerButton.LeftTrigger,
                    actionId: "playnite-library.collection.previous")
                .Shortcut(ControllerButton.RightTrigger,
                    actionId: "playnite-library.collection.next");
        WidgetElement result = tile;
        if (current is not null)
        {
            if (focusSummaryContext)
                result = result.PresentOnFocus(FocusedGameSummary(
                    savedId, title, subtitle, state, favorite, preferred));
            var focusedArtwork = current.Presentation.Artwork.Find(
                WidgetAppLibraryArtworkRole.Hero) ?? current.Presentation.Artwork.Find(
                WidgetAppLibraryArtworkRole.Tile);
            if (focusedArtwork is { Handle.Length: > 0 } background)
                result = result.FocusBackground(new WidgetArtworkHandle(background.Handle));
        }
        return collectionItem ? result.CollectionItem(key) : result;
    }

    private static WidgetElement DefaultFocusedGameSummary() =>
        UI.Stack("playnite-library.home.summary.default",
                UI.Text("Choose a game", "playnite-library.home.summary.title",
                    "Choose a game"),
                UI.Text("Move across the poster rail to preview game details.",
                    "playnite-library.home.summary.body", "Game summary"))
            .Classes("playnite-library-home-summary");

    private static ActionSurfaceElement LibraryNavigation(
        PlayniteLibraryPresentationState state) =>
        UI.ActionSurface("playnite-library.browse.open",
                "playnite-library.library.menu", "Library navigation",
                ActionSurfaceOrientation.Horizontal,
                UI.Text("Library", "playnite-library.library.menu.label",
                    "Open library navigation"))
            .Disabled(!state.Interactive)
            .AddClasses("playnite-library-library-menu")
            .ContextAction("playnite-library.search.open", "Search", disabled: !state.Interactive)
            .ContextAction("playnite-library.browse.open", "Browse all", disabled: !state.Interactive)
            .ContextAction("playnite-library.filter.recent", "Continue",
                disabled: !state.Interactive || state.Organization.RecentSavedIds.Count == 0)
            .ContextAction("playnite-library.filter.favorites", "Favorites",
                disabled: !state.Interactive || state.Organization.FavoriteSavedIds.Count == 0)
            .ContextAction("playnite-library.categories.open", "Categories", disabled: !state.Interactive)
            .ContextAction("playnite-library.hidden.open", "Hidden",
                disabled: !state.Interactive || state.Organization.ExcludedSavedIds.Count == 0)
            .ContextAction(PlayniteLibraryWidget.PlayniteOpenActionId, "Playnite connection",
                disabled: !state.Interactive)
            .ContextAction("playnite-library.management.open", "Management", disabled: !state.Interactive);

    private static WidgetElement FocusedGameSummary(
        string savedId,
        string title,
        string subtitle,
        string state,
        bool favorite,
        bool preferred)
    {
        var traits = new List<string>(2);
        if (favorite) traits.Add("Favorite");
        if (preferred) traits.Add("Preferred variant");
        var summary = traits.Count == 0 ? subtitle : subtitle + " · " +
            string.Join(" · ", traits);
        var id = PlayniteLibraryIdentity.Key(savedId).Value;
        return UI.Stack("playnite-library.home.summary.game." + id,
                UI.Text(title, "playnite-library.home.summary.game.title." +
                    id, title),
                UI.Text(summary, "playnite-library.home.summary.game.subtitle." +
                    id, summary),
                UI.Text(state, "playnite-library.home.summary.game.state." +
                    id, state))
            .Classes("playnite-library-home-summary");
    }

    private static TileArtwork? PosterArtwork(PlayniteLibraryItem item) =>
        item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile) is { } artwork
            ? TileArtwork.FromHandle(new WidgetArtworkHandle(artwork.Handle),
                item.Presentation.DisplayName, ImageFit.Cover)
            : null;

    private static PlayniteLibraryLaunchState? LaunchStateFor(
        IReadOnlyDictionary<string, PlayniteLibraryLaunchState> states,
        string savedId) => states.TryGetValue(savedId, out var state) ? state : null;
}
