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
    bool RecentlyPlayed,
    bool FavoriteFilter,
    PlayniteLibraryRoute Route,
    PlayniteLibraryFixedRows FixedRows,
    IReadOnlyList<PlayniteLibraryItem> HiddenRows,
    IReadOnlyList<WidgetAppLibrarySource> Sources,
    string? HeroSavedId,
    int HeroIndex)
{
    internal string? ActiveCategoryId { get; init; }
    internal bool SearchExpanded { get; init; }
    internal bool AlternateBrowseViewport { get; init; }
    internal string? BrowseInitialFocusId { get; init; }
    internal bool BrowseRetained { get; init; }
    internal IReadOnlyList<PlayniteLibraryCollectionOption> Collections { get; init; } = [];
    internal IReadOnlyDictionary<string, string?> CompletionStatuses { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);
    internal string? CategoryFeedback { get; init; }
    internal bool CategoryFeedbackSucceeded { get; init; }
}

internal static class PlayniteLibraryPresentation
{
    private const double CompactPosterWidth = 150;
    private const int CompactPosterMaximumColumns = 7;
    internal const string ScrollId = "playnite-library.library.scroll";
    internal const string AlternateBrowseScrollId =
        "playnite-library.library.scroll.alternate";
    internal const string HomeRailId = "playnite-library.library.grid";
    internal const string RetryId = PlayniteLibraryActions.Retry;
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
    private static readonly WidgetSurfaceHints CinematicSurface = Surface with
    {
        WidthMode = WidgetSurfaceAxisMode.FillAvailable,
        HeightMode = WidgetSurfaceAxisMode.FillAvailable,
    };
    private static readonly WidgetSurfaceHints CategoriesSurface = Surface with
    {
        Mode = WidgetSurfaceMode.Standard,
        WidthMode = WidgetSurfaceAxisMode.FillAvailable,
        HeightMode = WidgetSurfaceAxisMode.FillAvailable,
        PreferredWidth = 900,
        PreferredHeight = 620,
        MinimumWidth = 420,
        MinimumHeight = 340,
    };

    internal static string BrowseScrollId(bool alternate) =>
        alternate ? AlternateBrowseScrollId : ScrollId;

    internal static WidgetView Render(PlayniteLibraryPresentationState state)
    {
        var snapshot = state.Collection;
        // Home and Browse retain their last admitted action surface while the
        // host owns inactive input. Actual dispatch remains guarded by the
        // widget's exact Interactive lifecycle check.
        var renderActionsEnabled = state.Route is PlayniteLibraryRoute.Library or
            PlayniteLibraryRoute.Browse || state.Interactive;
        var routeTitle = state.Route switch
        {
            PlayniteLibraryRoute.Browse => "Browse games",
            PlayniteLibraryRoute.Hidden => "Hidden games",
            PlayniteLibraryRoute.Categories => "Categories",
            PlayniteLibraryRoute.PlayniteConnection => "Playnite connection",
            _ => "Playnite Library",
        };
        WidgetElement header = state.Route == PlayniteLibraryRoute.Library
            ? UI.Row("playnite-library.home.actions",
                    UI.Row("playnite-library.home.utilities",
                            UI.Button("Refresh", PlayniteLibraryActions.Refresh,
                                    PlayniteLibraryActions.Refresh)
                                .Disabled(!renderActionsEnabled)
                                .AddClasses("playnite-library-control"))
                        .Classes("playnite-library-home-utilities"),
                    LibraryNavigation(state, renderActionsEnabled))
                .Classes("playnite-library-home-actions")
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
                .Classes("playnite-library-header", "playnite-library-header-copy");
        if (state.Route == PlayniteLibraryRoute.Browse)
            header = header.AddClasses("playnite-library-browse-header");
        if (state.Route == PlayniteLibraryRoute.Categories)
            header = UI.Row("playnite-library.categories.header",
                    UI.Stack("playnite-library.categories.heading",
                            UI.Text("PLAYNITE LIBRARY",
                                    "playnite-library.categories.eyebrow", "Playnite Library")
                                .Classes("playnite-library-eyebrow"),
                            UI.Text("Categories", "playnite-library.categories.title",
                                    "Categories")
                                .Classes("playnite-library-title"))
                        .Classes("playnite-library-categories-heading"),
                    UI.Button("Back", PlayniteLibraryActions.CategoriesBack,
                            PlayniteLibraryActions.CategoriesBack)
                        .Disabled(!renderActionsEnabled)
                        .AddClasses("playnite-library-control",
                            "playnite-library-categories-back"))
                .Classes("playnite-library-categories-header");
        if (state.Route == PlayniteLibraryRoute.Hidden)
            header = UI.Row("playnite-library.hidden.header",
                    UI.Stack("playnite-library.hidden.heading",
                            UI.Text("PLAYNITE LIBRARY", "playnite-library.hidden.eyebrow",
                                    "Playnite Library")
                                .Classes("playnite-library-eyebrow"),
                            UI.Text("Hidden games", "playnite-library.hidden.title",
                                    "Hidden games")
                                .Classes("playnite-library-title"))
                        .Classes("playnite-library-hidden-heading"),
                    UI.Button("Back", PlayniteLibraryActions.HiddenBack,
                            PlayniteLibraryActions.HiddenBack)
                        .Disabled(!renderActionsEnabled)
                        .AddClasses("playnite-library-control",
                            "playnite-library-hidden-back"))
                .Classes("playnite-library-hidden-header");
        var filterControls = new List<WidgetElement>();
        if (state.Route is not (PlayniteLibraryRoute.Library or
            PlayniteLibraryRoute.Browse or PlayniteLibraryRoute.Categories or
            PlayniteLibraryRoute.Hidden))
            filterControls.Add(UI.Button("Back",
                state.Route switch
                {
                    PlayniteLibraryRoute.Categories => "playnite-library.categories.back",
                    _ => "playnite-library.hidden.back",
                },
                state.Route switch
                {
                    PlayniteLibraryRoute.Categories => "playnite-library.categories.back",
                    _ => "playnite-library.hidden.back",
                })
                .Disabled(!renderActionsEnabled)
                .AddClasses("playnite-library-control", "playnite-library-filter-control"));
        if (state.Route == PlayniteLibraryRoute.Browse)
        {
            var favorites = UI.Button(
                    state.FavoriteFilter ? "Favorites: On" : "Favorites: Off",
                    PlayniteLibraryActions.FavoritesFilter,
                    PlayniteLibraryActions.FavoritesFilter)
                .Disabled(!renderActionsEnabled ||
                    state.Organization.FavoriteSavedIds.Count == 0)
                .AddClasses("playnite-library-control", "playnite-library-filter-control");
            if (state.FavoriteFilter)
                favorites = favorites.AddClasses("playnite-library-filter-active");
            filterControls.Add(favorites);
            var recent = UI.Button(
                    state.RecentlyPlayed ? "Recently played: On" : "Recently played: Off",
                    PlayniteLibraryActions.RecentlyPlayedFilter,
                    PlayniteLibraryActions.RecentlyPlayedFilter)
                .Disabled(!renderActionsEnabled)
                .AddClasses("playnite-library-control", "playnite-library-filter-control");
            if (state.RecentlyPlayed)
                recent = recent.AddClasses("playnite-library-filter-active");
            filterControls.Add(recent);
        }
        if (state.Route == PlayniteLibraryRoute.Browse)
        {
            filterControls.Add(UI.Select("Category", CategoryOptions(state),
                    PlayniteLibraryActions.CategoryFilter, "Filter by category")
                .Disabled(!renderActionsEnabled)
                .AddClasses("playnite-library-control", "playnite-library-filter-control"));
            var sourceOptions = SourceOptions(state);
            filterControls.Add(UI.Select("Source", sourceOptions,
                    PlayniteLibraryActions.SourceFilter,
                    "Filter by library source")
                .Disabled(!renderActionsEnabled)
                .AddClasses("playnite-library-control", "playnite-library-filter-control"));
            if (!state.RecentlyPlayed)
                filterControls.Add(UI.Select("Sort", SortOptions(state.Query.Sort),
                        PlayniteLibraryActions.SortFilter, "Sort installed games")
                    .Disabled(!renderActionsEnabled)
                    .AddClasses("playnite-library-control", "playnite-library-filter-control"));
            filterControls.Add(UI.Button("Clear", "playnite-library.query.clear",
                    "playnite-library.query.clear")
                .Disabled(!renderActionsEnabled ||
                    state.Query.SearchText is null &&
                    state.Query.Sort == WidgetAppLibrarySortOrder.DisplayName &&
                    (state.Route == PlayniteLibraryRoute.Library ||
                     !state.FavoriteFilter &&
                     !state.RecentlyPlayed &&
                     state.ActiveCategoryId is null &&
                     state.Query.SourceAttribution is null))
                .AddClasses("playnite-library-control", "playnite-library-filter-control"));
        }

        WidgetElement queryControls;
        if (state.Route is PlayniteLibraryRoute.Categories or
            PlayniteLibraryRoute.Library)
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
                    .Disabled(!renderActionsEnabled)
                    .AddClasses("playnite-library-control", "playnite-library-search"),
            };
            var filters = UI.HorizontalScroll(
                    "playnite-library.filters", filterControls.ToArray())
                .Classes("playnite-library-filters");
            if (state.Route == PlayniteLibraryRoute.Browse)
                filters = filters.RememberChildFocus(
                    PlayniteLibraryActions.RecentlyPlayedFilter);
            queryChildren.Add(filters);
            var queryGroup = UI.Stack("playnite-library.query", queryChildren.ToArray())
                .Classes("playnite-library-query");
            queryControls = queryGroup;
        }

        WidgetElement content;
        var catalogPage = false;
        string? catalogAnchorKey = null;
        string? pageBeforeActionId = null;
        string? pageAfterActionId = null;
        var pageShortcuts = false;
        BackgroundSurfaceArtwork? catalogBackgroundArtwork = null;
        string? initialFocus = state.Route == PlayniteLibraryRoute.Browse
            ? state.BrowseInitialFocusId ?? snapshot.RequestedFocusId
            : snapshot.RequestedFocusId;
        if (state.Route == PlayniteLibraryRoute.Categories)
        {
            var categoryRows = state.Organization.Categories.Select(category =>
                UI.Card("playnite-library.category.card." + category.Id,
                    UI.Row("playnite-library.category.row." + category.Id,
                        UI.Stack("playnite-library.category.copy." + category.Id,
                                UI.Text(category.Name,
                                        "playnite-library.category.summary." + category.Id,
                                        $"{category.Name}, {category.SavedIds.Count} games")
                                    .Classes("playnite-library-category-summary"),
                                UI.Text($"{category.SavedIds.Count} games",
                                        "playnite-library.category.count." + category.Id,
                                        $"{category.SavedIds.Count} games")
                                    .Classes("playnite-library-category-count"))
                            .Classes("playnite-library-category-copy"),
                        (UI.Button("Open",
                                PlayniteLibraryActions.CategoryOpen(category.Id),
                                "playnite-library.category.open-button." + category.Id)
                            with { AccessibilityLabel = "Open " + category.Name })
                            .Disabled(!state.Interactive)
                            .AddClasses("playnite-library-control",
                                "playnite-library-category-open"))
                        .Classes("playnite-library-category-row"))
                    .AddClasses("playnite-library-category"))
                .ToArray();
            var create = UI.TextEntry(string.Empty, "Create category",
                        "playnite-library.category.create", "playnite-library.category.create",
                        PlayniteLibraryPrivateState.MaximumCategoryNameLength)
                    .Disabled(!state.Interactive || state.OrganizationBusy ||
                        state.Organization.Categories.Count >=
                            PlayniteLibraryPrivateState.MaximumCategories)
                    .AddClasses("playnite-library-control", "playnite-library-search",
                        "playnite-library-category-create");
            content = UI.Stack("playnite-library.content",
                    UI.Text("Categories are read from Playnite. Creating or changing " +
                            "membership uses the exact current Playnite game identity.",
                        "playnite-library.categories.help", "Category help"),
                    create,
                    UI.VerticalScroll("playnite-library.categories.list",
                            categoryRows)
                        .Classes("playnite-library-categories-list"))
                .Classes("playnite-library-content", "playnite-library-categories-content");
            initialFocus = "playnite-library.category.create";
        }
        else if (state.Route == PlayniteLibraryRoute.Hidden)
        {
            var resolved = state.HiddenRows
                .DistinctBy(item => item.Value.SavedId, StringComparer.Ordinal)
                .ToDictionary(
                item => item.Value.SavedId, StringComparer.Ordinal);
            var stored = state.Organization.Items.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var favorites = new Dictionary<string, int>(StringComparer.Ordinal);
            var rows = state.Organization.ExcludedSavedIds
                .Select(savedId => PresentedRowFor(savedId, resolved, stored))
                .Where(row => row?.Current is not null && MatchesFixedQuery(
                    row.Display, state.Query, favoriteFilter: false, favorites))
                .Select(row => row!)
                .OrderBy(row => row, PresentedRowComparer(state.Query.Sort))
                .Take(PlayniteLibraryPrivateState.MaximumExcludedItems)
                .ToArray();
            if (rows.Length == 0)
            {
                content = UI.EmptyState("No hidden games",
                    state.Organization.ExcludedSavedIds.Count == 0
                        ? "Installed hidden games will appear here until you restore them."
                        : "No installed hidden games match the current filters.",
                    "playnite-library.hidden.empty",
                    new ComponentAction("Back", "playnite-library.hidden.back",
                        WidgetGlyph.Play),
                    WidgetGlyph.Play);
                initialFocus = "playnite-library.hidden.empty.action";
            }
            else
            {
                var tiles = rows.Select(row => HiddenTile(
                    row, renderActionsEnabled && !state.OrganizationBusy)).ToArray();
                catalogPage = true;
                // Hidden rows are display-only restore targets, not retained cursor
                // collection members. The outer catalog scroll must therefore not
                // declare an anchor for a key that this route does not publish.
                catalogAnchorKey = null;
                content = UI.Stack("playnite-library.content",
                        UI.VerticalScroll(ScrollId,
                                CompactGameGrid("playnite-library.hidden.grid", tiles))
                            .Classes("playnite-library-catalog-scroll",
                                "playnite-library-hidden-scroll"),
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
                 (state.Route != PlayniteLibraryRoute.Browse &&
                  state.FixedRows.All.Any(item =>
                      !state.Organization.ExcludedSavedIds.Contains(
                          item.Value.SavedId, StringComparer.Ordinal))))
        {
            var rail = state.Route == PlayniteLibraryRoute.Browse
                ? PlayniteLibraryHeroRailPolicy.ProjectBrowse(
                    state, state.HeroSavedId, state.HeroIndex)
                : PlayniteLibraryHeroRailPolicy.Project(
                    state, state.HeroSavedId, state.HeroIndex);
            catalogBackgroundArtwork = DefaultBackgroundArtwork(rail.Selected);
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
                    renderActionsEnabled && !state.OrganizationBusy && !state.BrowseRetained,
                    row.Key,
                    rail.PageBumpers,
                    row.CollectionItem,
                    categories: state.Route is PlayniteLibraryRoute.Library or
                        PlayniteLibraryRoute.Browse
                        ? state.Organization.Categories : null,
                    completionStatus: state.CompletionStatuses.GetValueOrDefault(
                        row.Display.SavedId),
                    focusSummaryContext: state.Route == PlayniteLibraryRoute.Library,
                    browseLayout: state.Route == PlayniteLibraryRoute.Browse))
                .ToArray();
            catalogPage = true;
            catalogAnchorKey = rail.CatalogAnchorKey;
            if (snapshot.Status != WidgetPagedResourceStatus.Error &&
                (snapshot.HasBefore || snapshot.HasAfter))
            {
                pageBeforeActionId = snapshot.HasBefore
                    ? state.Route == PlayniteLibraryRoute.Browse
                        ? "playnite-library.browse.cursor.before"
                        : "playnite-library.library.cursor.before"
                    : null;
                pageAfterActionId = snapshot.HasAfter
                    ? state.Route == PlayniteLibraryRoute.Browse
                        ? "playnite-library.browse.cursor.after"
                        : "playnite-library.library.cursor.after"
                    : null;
            }
            pageShortcuts = true;
            var controls = UI.HorizontalScroll("playnite-library.actions",
                    UI.Button("Refresh", "playnite-library.refresh", "playnite-library.refresh")
                .Disabled(!renderActionsEnabled)
                        .AddClasses("playnite-library-control"))
                .Classes("playnite-library-actions")
                .RememberChildFocus(PlayniteLibraryActions.Refresh)
                .VisibleWhen(ResponsiveVisibility.ExpandedOnly);
            var hasActionableGame = renderActionsEnabled && !state.OrganizationBusy &&
                !state.BrowseRetained &&
                state.LaunchingSavedId is null &&
                rail.Items.Any(row => row.Current is not null);
            var hintItems = new List<WidgetElement>();
            if (state.Route == PlayniteLibraryRoute.Library)
            {
                hintItems.Add(StableControllerHint(
                    ControllerButton.X,
                    "Favorite",
                    "playnite-library.hint.favorite",
                    hasActionableGame,
                    hasActionableGame
                        ? rail.Selected?.Favorite == true
                            ? "Remove favorite"
                            : "Add favorite"
                        : "Favorite unavailable"));
                hintItems.Add(StableControllerHint(
                    ControllerButton.Menu,
                    "Game options",
                    "playnite-library.hint.options",
                    hasActionableGame,
                    hasActionableGame ? "Game options" : "Game options unavailable"));
            }
            else if (hasActionableGame)
            {
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.X, "Favorite", "playnite-library.hint.favorite"));
                hintItems.Add(UI.ControllerHint(
                    ControllerButton.Menu, "Game options", "playnite-library.hint.options"));
            }
            if (rail.PageBumpers)
            {
                if (renderActionsEnabled && snapshot.Status == WidgetPagedResourceStatus.Ready &&
                    snapshot.HasBefore)
                    hintItems.Add(UI.ControllerHint(
                        ControllerButton.LeftBumper, "Previous page",
                        "playnite-library.hint.previous"));
                if (renderActionsEnabled && snapshot.Status == WidgetPagedResourceStatus.Ready &&
                    snapshot.HasAfter)
                    hintItems.Add(UI.ControllerHint(
                        ControllerButton.RightBumper, "Next page",
                        "playnite-library.hint.next"));
            }
            WidgetElement catalog = state.Route switch
            {
                PlayniteLibraryRoute.Library => HomeRail(
                    snapshot, catalogAnchorKey, pageBeforeActionId, pageAfterActionId,
                    pageShortcuts, renderActionsEnabled, tiles),
                PlayniteLibraryRoute.Browse => BrowseGrid(
                    BrowseScrollId(state.AlternateBrowseViewport), snapshot,
                    catalogAnchorKey, pageBeforeActionId, pageAfterActionId,
                    pageShortcuts, renderActionsEnabled && !state.BrowseRetained, tiles),
                _ => GameGrid("playnite-library.library.grid", tiles),
            };
            var children = new List<WidgetElement>();
            children.Add(catalog);
            if (state.Route != PlayniteLibraryRoute.Library)
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
            if (state.Route == PlayniteLibraryRoute.Browse &&
                state.BrowseInitialFocusId is null &&
                !rail.Items.Any(row => string.Equals(
                    row.FocusId, initialFocus, StringComparison.Ordinal)))
                initialFocus = rail.Selected?.FocusId ?? "playnite-library.search";
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
        else if (state.Route == PlayniteLibraryRoute.Browse)
        {
            var activeQuery = HasActiveBrowseQuery(state);
            var title = activeQuery ? "No matching games" : "No installed games";
            var detail = activeQuery
                ? "Clear search and filters to see the complete installed library."
                : "Refresh after installing games in Playnite.";
            var actionLabel = activeQuery ? "Clear search and filters" : "Refresh";
            var actionId = activeQuery
                ? "playnite-library.query.clear"
                : "playnite-library.refresh";
            content = UI.Stack("playnite-library.content",
                    UI.Stack("playnite-library.browse.empty",
                            UI.Text(title, "playnite-library.browse.empty.title", title)
                                .Classes("playnite-library-empty-title"),
                            UI.Text(detail, "playnite-library.browse.empty.detail", detail)
                                .Classes("playnite-library-empty-detail"),
                            UI.Button(actionLabel, actionId,
                                    "playnite-library.browse.empty.action")
                                .Disabled(!renderActionsEnabled)
                                .AddClasses("playnite-library-control",
                                    "playnite-library-empty-action"))
                        .Classes("playnite-library-empty"))
                .Classes("playnite-library-content");
            content = content.AddClasses("playnite-library-empty-content");
            initialFocus ??= "playnite-library.browse.empty.action";
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

        if (state.Route == PlayniteLibraryRoute.Browse &&
            state.Organization.Categories.Count != 0 && renderActionsEnabled &&
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
                .Classes("playnite-library-content");
        }
        content = content.AddClasses("playnite-library-main");
        if (state.CategoryFeedback is { } categoryFeedback)
            content = UI.Stack("playnite-library.category.feedback.scope",
                    content,
                    UI.Toast(
                        state.CategoryFeedbackSucceeded
                            ? "Categories updated"
                            : "Category update failed",
                        categoryFeedback,
                        state.CategoryFeedbackSucceeded
                            ? ToastTone.Success
                            : ToastTone.Warning,
                        "playnite-library.category.feedback"))
                .Classes("playnite-library-main");
        WidgetElement root;
        if (catalogPage && state.Route == PlayniteLibraryRoute.Library)
        {
            var homeContent = UI.Stack("playnite-library.home.focus-content", content)
                .Classes("playnite-library-home-content");
            var homeForeground = UI.Stack("playnite-library.home.foreground",
                    header,
                    UI.FocusPresentationSurface(
                        homeContent,
                        DefaultFocusedGameSummary(),
                        "playnite-library.home.focus-summary"))
                .Classes("playnite-library-home-foreground");
            var homeStage = CinematicStage(
                "playnite-library.home.stage", homeForeground,
                "playnite-library-home-stage");
            root = UI.Stack("playnite-library.root",
                    UI.BackgroundSurface(
                            homeStage,
                            "playnite-library.cinematic", catalogBackgroundArtwork)
                        .UseFocusedDescendantArtwork()
                        .AddClasses("playnite-library-cinematic",
                            "playnite-library-home-background"))
                .Classes("playnite-library-home-surface");
        }
        else if (state.Route == PlayniteLibraryRoute.Browse)
        {
            var page = UI.Stack("playnite-library.browse.page",
                    header, queryControls, content)
                .Classes("playnite-library-browse-foreground");
            if (state.Organization.Categories.Count != 0 && renderActionsEnabled &&
                !state.OrganizationBusy)
                page = page
                    .Shortcut(ControllerButton.LeftTrigger,
                        actionId: PlayniteLibraryActions.CollectionPrevious,
                        label: "Previous collection")
                    .Shortcut(ControllerButton.RightTrigger,
                        actionId: PlayniteLibraryActions.CollectionNext,
                        label: "Next collection");
            var browseStage = CinematicStage(
                "playnite-library.browse.stage", page,
                "playnite-library-browse-stage");
            root = UI.Stack("playnite-library.root",
                    UI.BackgroundSurface(browseStage, "playnite-library.browse.cinematic",
                            catalogBackgroundArtwork)
                        .UseFocusedDescendantArtwork()
                        .AddClasses("playnite-library-cinematic",
                            "playnite-library-browse-background"))
                .Classes("playnite-library-route-shell", "playnite-library-browse",
                    "playnite-library-browse-surface");
        }
        else if (state.Route == PlayniteLibraryRoute.Categories)
        {
            var shell = UI.Stack("playnite-library.categories.shell",
                    header, content)
                .Classes("playnite-library-categories-shell");
            root = UI.Stack("playnite-library.root", shell)
                .Classes("playnite-library-categories");
        }
        else if (state.Route == PlayniteLibraryRoute.Hidden)
        {
            var shell = UI.Stack("playnite-library.hidden.shell",
                    header, queryControls, content)
                .Classes("playnite-library-hidden-shell");
            root = UI.Stack("playnite-library.root", shell)
                .Classes("playnite-library-hidden-surface");
        }
        else if (catalogPage)
        {
            var page = UI.VerticalScroll(ScrollId, header, queryControls, content)
                .Classes("playnite-library-page-scroll");
            if (pageBeforeActionId is not null || pageAfterActionId is not null)
                page = page.Paginate(pageBeforeActionId, pageAfterActionId, 2);
            if (pageShortcuts)
                page = PageShortcuts(page, snapshot, renderActionsEnabled);
            page = page with { CollectionAnchorKey = catalogAnchorKey };
            root = UI.Stack("playnite-library.root", page)
                .Classes("playnite-library-widget", "playnite-library-canvas");
        }
        else
        {
            root = UI.Stack("playnite-library.root", header, queryControls, content)
                .Classes("playnite-library-widget", "playnite-library-route-shell");
        }
        if (state.Route == PlayniteLibraryRoute.Browse &&
            state.BrowseInitialFocusId is null &&
            (snapshot.Status is not WidgetPagedResourceStatus.Ready || !catalogPage))
            initialFocus = "playnite-library.search";
        return new WidgetView(root, initialFocus, Surface: state.Route switch
        {
            PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Browse => CinematicSurface,
            PlayniteLibraryRoute.Categories or PlayniteLibraryRoute.Hidden => CategoriesSurface,
            _ => Surface,
        });
    }

    private static bool HasActiveBrowseQuery(PlayniteLibraryPresentationState state) =>
        state.Query.SearchText is not null ||
        state.Query.SourceAttribution is not null ||
        state.Query.Sort != WidgetAppLibrarySortOrder.DisplayName ||
        state.FavoriteFilter || state.RecentlyPlayed || state.ActiveCategoryId is not null;

    private static IReadOnlyList<SelectOption> CategoryOptions(
        PlayniteLibraryPresentationState state) =>
    [
        new SelectOption(PlayniteLibraryActions.CategoryAll, "All",
            PlayniteLibraryActions.CategoryAll,
            IsSelected: state.ActiveCategoryId is null),
        .. state.Organization.Categories.Select(category =>
        {
            var actionId = PlayniteLibraryActions.CategoryOpen(category.Id);
            return new SelectOption(actionId, category.Name, actionId,
                IsSelected: string.Equals(category.Id, state.ActiveCategoryId,
                    StringComparison.Ordinal));
        }),
    ];

    private static IReadOnlyList<SelectOption> SourceOptions(
        PlayniteLibraryPresentationState state)
    {
        var active = state.Query.SourceAttribution;
        return
        [
            new SelectOption(PlayniteLibraryActions.SourceAll, "All",
                PlayniteLibraryActions.SourceAll, IsSelected: active is null),
            .. PlayniteLibrarySourceCatalog.SelectOptions(
                    state.Organization.ProvenSources, state.Sources, active)
                .Select(source =>
                {
                    var actionId = PlayniteLibraryActions.SourceOption(source);
                    return new SelectOption(actionId, source, actionId,
                        IsSelected: string.Equals(source, active,
                            StringComparison.OrdinalIgnoreCase));
                }),
        ];
    }

    private static IReadOnlyList<SelectOption> SortOptions(
        WidgetAppLibrarySortOrder sort)
    {
        var selected = sort is WidgetAppLibrarySortOrder.DisplayNameDescending or
            WidgetAppLibrarySortOrder.SourceThenDisplayName
            ? sort
            : WidgetAppLibrarySortOrder.DisplayName;
        return
        [
            new SelectOption(PlayniteLibraryActions.SortDisplayName, "A–Z",
                PlayniteLibraryActions.SortDisplayName,
                IsSelected: selected == WidgetAppLibrarySortOrder.DisplayName),
            new SelectOption(PlayniteLibraryActions.SortDisplayNameDescending, "Z–A",
                PlayniteLibraryActions.SortDisplayNameDescending,
                IsSelected: selected == WidgetAppLibrarySortOrder.DisplayNameDescending),
            new SelectOption(PlayniteLibraryActions.SortSourceThenDisplayName, "Source",
                PlayniteLibraryActions.SortSourceThenDisplayName,
                IsSelected: selected == WidgetAppLibrarySortOrder.SourceThenDisplayName),
        ];
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
                ControllerButton.LeftBumper, "playnite-library.previous",
                label: "Previous page");
        if (snapshot.HasAfter)
            scroll = scroll.Shortcut(
                ControllerButton.RightBumper, "playnite-library.next",
                label: "Next page");
        return scroll;
    }

    private static ScrollElement BrowseGrid(
        string scrollId,
        WidgetCursorResourceSnapshot<PlayniteLibraryItem> snapshot,
        string? collectionAnchorKey,
        string? nearStartActionId,
        string? nearEndActionId,
        bool pageShortcuts,
        bool interactive,
        params WidgetElement[] tiles)
    {
        var grid = UI.ResponsiveGrid(
                "playnite-library.browse.grid", CompactPosterWidth,
                maximumColumns: CompactPosterMaximumColumns, tiles)
            .Classes("playnite-library-browse-grid");
        var scroll = UI.VerticalScroll(scrollId, grid)
            .Classes("playnite-library-catalog-scroll",
                "playnite-library-browse-scroll") with
            {
                CollectionAnchorKey = collectionAnchorKey,
                CollectionStartIndex = collectionAnchorKey is null ? null : snapshot.StartIndex,
                CollectionNavigation = collectionAnchorKey is null ? null : snapshot.NavigationRequest,
            };
        if (nearStartActionId is not null || nearEndActionId is not null)
            scroll = scroll.Paginate(nearStartActionId, nearEndActionId, 2);
        return pageShortcuts ? PageShortcuts(scroll, snapshot, interactive) : scroll;
    }

    private static StackElement CinematicStage(
        string id,
        WidgetElement foreground,
        string routeClass) =>
        UI.Stack(id, foreground)
            .Classes("playnite-library-surface-stage", routeClass);

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
                CollectionStartIndex = collectionAnchorKey is null ? null : snapshot.StartIndex,
                CollectionNavigation = collectionAnchorKey is null ? null : snapshot.NavigationRequest,
            };
        if (nearStartActionId is not null || nearEndActionId is not null)
            rail = rail.Paginate(nearStartActionId, nearEndActionId, 2);
        return pageShortcuts ? PageShortcuts(rail, snapshot, interactive) : rail;
    }

    private static RowElement StableControllerHint(
        ControllerButton button,
        string visibleLabel,
        string id,
        bool available,
        string accessibilityLabel)
    {
        var hint = UI.ControllerHint(button, visibleLabel, id);
        var label = (TextElement)hint.Children[1];
        hint = hint with
        {
            Children = [hint.Children[0], label with
            {
                AccessibilityLabel = accessibilityLabel,
            }],
        };
        return available
            ? hint
            : hint.AddClasses("playnite-library-hint-unavailable");
    }

    private static GridElement GameGrid(string id, params WidgetElement[] tiles) =>
        UI.ResponsiveGrid(id, 220, 4, tiles)
            .Classes("playnite-library-grid");

    private static GridElement CompactGameGrid(
        string id,
        params WidgetElement[] tiles) =>
        UI.ResponsiveGrid(id, CompactPosterWidth, CompactPosterMaximumColumns, tiles)
            .Classes("playnite-library-grid", "playnite-library-hidden-grid");

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
            .AddClasses("playnite-library-tile", "playnite-library-fixed-tile");
    }

    private static WidgetElement HiddenTile(PresentedRow row, bool interactive)
    {
        var current = row.Current;
        var artwork = current is null ? null : PosterArtwork(current);
        var availability = current is null ? "Unavailable · Restore" : "Hidden · Restore";
        return UI.PosterTile(row.Display.DisplayName, availability,
                PlayniteLibraryActions.Restore,
                PlayniteLibraryIdentity.FocusId(
                    "hidden", PlayniteLibraryIdentity.Key(row.Display.SavedId)),
                subtitle: row.Display.SourceAttribution,
                artwork: artwork,
                accessibilityLabel:
                    $"{row.Display.DisplayName}, {row.Display.SourceAttribution}, {availability}")
            .Disabled(!interactive)
            .AddClasses("playnite-library-tile", "playnite-library-fixed-tile");
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
        bool interactive,
        WidgetCollectionItemKey key,
        bool pageBumpers,
        bool collectionItem = true,
        IReadOnlyList<PlayniteLibraryCategory>? categories = null,
        string? completionStatus = null,
        bool focusSummaryContext = false,
        bool browseLayout = false)
    {
        var id = PlayniteLibraryIdentity.FocusId("grid", key);
        var launching = string.Equals(savedId, launchingSavedId, StringComparison.Ordinal);
        var artwork = artworkHandle is { Length: > 0 }
            ? TileArtwork.FromHandle(new WidgetArtworkHandle(artworkHandle), title, ImageFit.Cover)
            : null;
        var availability = PlayniteLibraryAvailabilityPresentation.Tile(current);
        var state = launching ? "Pending" : launchState switch
        {
            PlayniteLibraryLaunchState.RequestAccepted => "Request accepted",
            PlayniteLibraryLaunchState.LauncherStarted => "Launcher started",
            PlayniteLibraryLaunchState.Running => "Running",
            PlayniteLibraryLaunchState.Failed => "Failed",
            PlayniteLibraryLaunchState.Ended => "Ended",
            _ => availability.Status,
        };
        var traits = new List<string>(1);
        if (favorite) traits.Add("Favorite");
        var subtitle = traits.Count == 0 ? source : source + " · " + string.Join(" · ", traits);
        var tile = UI.PosterTile(title, state,
                PlayniteLibraryActions.Launch, id, subtitle: subtitle, artwork: artwork,
                accessibilityLabel: $"{title}, {subtitle}, {state}")
            .Busy(launching || availability.Busy)
            .Disabled(!interactive || !availability.Launchable)
            .AddClasses("playnite-library-tile");
        if (focusSummaryContext)
            tile = tile.AddClasses("playnite-library-fixed-tile");
        if (browseLayout)
            tile = tile.AddClasses("playnite-library-browse-tile");
        if (interactive && current is not null && !launching)
        {
            var actionEnabled = availability.Launchable;
            tile = tile
                .Shortcut(ControllerButton.X, actionId: PlayniteLibraryActions.Favorite,
                    label: favorite ? "Remove favorite" : "Add favorite")
                .ContextAction(PlayniteLibraryActions.Favorite,
                    favorite ? "Remove favorite" : "Add favorite", disabled: !actionEnabled)
                .ContextAction(PlayniteLibraryActions.Hide, "Hide", disabled: !actionEnabled)
                .ContextAction(PlayniteLibraryActions.RefreshSource, "Refresh source",
                    disabled: !actionEnabled);
            foreach (var category in (categories ?? []).Take(5))
            {
                var included = PlayniteLibraryCategoryPolicy.Contains(category, savedId);
                tile = tile.ContextAction(PlayniteLibraryActions.CategoryMembership(category.Id),
                    included ? $"Remove from {category.Name}" : $"Add to {category.Name}",
                    disabled: !actionEnabled);
            }
        }
        WidgetElement result = tile;
        if (current is not null)
        {
            if (focusSummaryContext)
                result = result.PresentOnFocus(FocusedGameSummary(
                    savedId, title, source, state, favorite, completionStatus,
                    current, categories));
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
                        "Choose a game")
                    .Classes("playnite-library-summary-title"),
                UI.Text("Move across the poster rail to preview game details.",
                        "playnite-library.home.summary.body", "Game summary")
                    .Classes("playnite-library-summary-meta"))
            .Classes("playnite-library-home-summary");

    private static ActionSurfaceElement LibraryNavigation(
        PlayniteLibraryPresentationState state,
        bool renderActionsEnabled) =>
        UI.ActionSurface(PlayniteLibraryActions.BrowseOpen,
                "playnite-library.library.menu",
                "Library navigation. Press Menu for Recently played, Favorites, " +
                "Categories, Hidden games, and Playnite connection.",
                ActionSurfaceOrientation.Horizontal,
                UI.Row("playnite-library.library.menu.content",
                        UI.Text("Library", "playnite-library.library.menu.label",
                                "Open library navigation")
                            .Classes("playnite-library-menu-label"),
                        UI.ControllerHint(ControllerButton.Menu, "Library options",
                                "playnite-library.library.menu.hint"))
                    .Classes("playnite-library-library-menu-content"))
            .Disabled(!renderActionsEnabled)
            .AddClasses("playnite-library-control", "playnite-library-library-menu")
            .ContextAction(PlayniteLibraryActions.RecentlyPlayedFilter, "Recently played",
                disabled: !renderActionsEnabled)
            .ContextAction(PlayniteLibraryActions.FavoritesFilter, "Favorites",
                disabled: !renderActionsEnabled || state.Organization.FavoriteSavedIds.Count == 0)
            .ContextAction(PlayniteLibraryActions.CategoriesOpen, "Categories", disabled: !renderActionsEnabled)
            .ContextAction(PlayniteLibraryActions.HiddenOpen, "Hidden games",
                disabled: !renderActionsEnabled || state.Organization.ExcludedSavedIds.Count == 0)
            .ContextAction(PlayniteLibraryWidget.PlayniteOpenActionId, "Playnite connection",
                disabled: !renderActionsEnabled);

    private static WidgetElement FocusedGameSummary(
        string savedId,
        string title,
        string source,
        string state,
        bool favorite,
        string? completionStatus,
        PlayniteLibraryItem current,
        IReadOnlyList<PlayniteLibraryCategory>? categories)
    {
        var metadata = current.Presentation.Metadata;
        var traits = new List<string> { source,
            current.Presentation.Availability.State.ToString() };
        if (favorite) traits.Add("Favorite");
        if (!string.IsNullOrWhiteSpace(completionStatus)) traits.Add(completionStatus!);
        if (metadata?.PlaytimeMinutes is > 0)
            traits.Add(FormatPlaytime(metadata.PlaytimeMinutes.Value));
        if (metadata?.LastPlayedAtUnixMilliseconds is { } lastPlayed)
            traits.Add("Last played " + DateTimeOffset.FromUnixTimeMilliseconds(lastPlayed)
                .ToLocalTime().ToString("g"));
        if (!string.IsNullOrWhiteSpace(metadata?.Version))
            traits.Add("Version " + metadata.Version);
        var memberships = (categories ?? [])
            .Where(category => PlayniteLibraryCategoryPolicy.Contains(
                category, current.Value.SavedId))
            .Select(category => category.Name)
            .Take(3)
            .ToArray();
        if (memberships.Length != 0) traits.Add(string.Join(", ", memberships));
        var summary = string.Join(" · ", traits);
        var id = PlayniteLibraryIdentity.Key(savedId).Value;
        var children = new List<WidgetElement>
        {
            UI.Text(title, "playnite-library.home.summary.game.title." + id, title)
                .Classes("playnite-library-summary-title"),
            UI.Text(summary, "playnite-library.home.summary.game.subtitle." + id, summary)
                .Classes("playnite-library-summary-meta"),
        };
        if (!string.Equals(state, "Play", StringComparison.Ordinal))
            children.Add(UI.Text(state, "playnite-library.home.summary.game.state." + id, state)
                .Classes("playnite-library-summary-state"));
        if (!string.IsNullOrWhiteSpace(metadata?.Description))
            children.Add(UI.Text(metadata.Description,
                    "playnite-library.home.summary.game.description." + id,
                    metadata.Description)
                .Classes("playnite-library-summary-description"));
        return UI.Stack("playnite-library.home.summary.game." + id, children.ToArray())
            .Classes("playnite-library-home-summary");
    }

    private static string FormatPlaytime(long minutes) => minutes >= 60
        ? $"{minutes / 60}h {minutes % 60}m played"
        : $"{minutes}m played";

    private static TileArtwork? PosterArtwork(PlayniteLibraryItem item) =>
        item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile) is { } artwork
            ? TileArtwork.FromHandle(new WidgetArtworkHandle(artwork.Handle),
                item.Presentation.DisplayName, ImageFit.Cover)
            : null;

    private static BackgroundSurfaceArtwork? DefaultBackgroundArtwork(
        PlayniteLibraryHeroRailItem? selected)
    {
        var artwork = selected?.Current?.Presentation.Artwork.Find(
                WidgetAppLibraryArtworkRole.Hero) ??
            selected?.Current?.Presentation.Artwork.Find(
                WidgetAppLibraryArtworkRole.Tile);
        return artwork is { Handle.Length: > 0 }
            ? BackgroundSurfaceArtwork.FromHandle(
                new WidgetArtworkHandle(artwork.Handle), ImageFit.Cover)
            : null;
    }

    private static PlayniteLibraryLaunchState? LaunchStateFor(
        IReadOnlyDictionary<string, PlayniteLibraryLaunchState> states,
        string savedId) => states.TryGetValue(savedId, out var state) ? state : null;
}
