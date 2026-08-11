using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

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
    IReadOnlyList<WidgetAppLibrarySource> Sources);

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
        PreferredWidth = 980,
        PreferredHeight = 700,
        MinimumWidth = 420,
        MinimumHeight = 340,
    };

    internal static WidgetView Render(GameLauncherPresentationState state)
    {
        var snapshot = state.Collection;
        var header = UI.Stack("game-launcher.header",
                UI.Text("INSTALLED GAMES", "game-launcher.eyebrow", "Installed games")
                    .Classes("game-launcher-eyebrow"),
                UI.Text(state.Route == GameLauncherRoute.AddGames ? "Add games" : "Game Launcher",
                        "game-launcher.title",
                        state.Route == GameLauncherRoute.AddGames ? "Add games" : "Game Launcher")
                    .Classes("game-launcher-title"),
                UI.Text(state.Status, "game-launcher.status", state.Status)
                    .Classes("game-launcher-status"))
            .Classes("game-launcher-header");
        var sourceStatus = SourceStatus(state.Sources, snapshot.Status);

        var filterControls = new List<WidgetElement>();
        if (state.Route == GameLauncherRoute.AddGames)
            filterControls.Add(UI.Button("Back", "game-launcher.add.back",
                "game-launcher.add.back").Disabled(!state.Interactive));
        else
        {
            filterControls.Add(UI.Button("Add games", "game-launcher.add.open",
                    "game-launcher.add.open")
                .Disabled(!state.Interactive));
            filterControls.Add(UI.ToggleButton("Favorites", state.FavoriteFilter,
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
            .Disabled(!state.Interactive || !state.FavoriteFilter &&
                state.RecentMode == GameLauncherRecentMode.Off &&
                state.Query.SearchText is null && state.Query.SourceAttribution is null &&
                state.Query.Sort == WidgetAppLibrarySortOrder.DisplayName));

        var queryControls = UI.Stack("game-launcher.query",
            UI.TextEntry(
                    state.Query.SearchText ?? string.Empty,
                    state.Route == GameLauncherRoute.AddGames
                        ? "Search trusted installed registrations"
                        : "Search installed games",
                    "game-launcher.search.commit",
                    "game-launcher.search",
                    WidgetAppLibraryQuery.MaximumSearchTextLength)
                .Disabled(!state.Interactive)
                .Classes("game-launcher-search"),
            UI.Row("game-launcher.filters", filterControls.ToArray())
                .Classes("game-launcher-filters"))
        .Classes("game-launcher-query");

        WidgetElement content;
        string? initialFocus = snapshot.RequestedFocusId;
        if (state.Route == GameLauncherRoute.AddGames && snapshot.Items.Count != 0)
        {
            var tiles = snapshot.Items.Select(item => ManualTile(
                item, state.Organization.ManualSavedIds.Contains(
                    item.Value.SavedId, StringComparer.Ordinal),
                state.Interactive && !state.OrganizationBusy)).ToArray();
            var scroll = UI.VerticalScroll(ScrollId,
                    UI.ResponsiveGrid("game-launcher.library.grid", 170, 5, tiles)
                        .Classes("game-launcher-grid"))
                .Classes("game-launcher-scroll");
            if (snapshot.HasBefore || snapshot.HasAfter)
                scroll = scroll.Paginate(
                    snapshot.HasBefore ? "game-launcher.library.cursor.before" : null,
                    snapshot.HasAfter ? "game-launcher.library.cursor.after" : null, 2);
            scroll = scroll with { CollectionAnchorKey = snapshot.Anchor?.Value };
            content = UI.Stack("game-launcher.content",
                    scroll,
                    UI.Row("game-launcher.actions",
                        UI.Button("Previous page", "game-launcher.previous",
                                "game-launcher.previous")
                            .Disabled(!snapshot.HasBefore || !state.Interactive),
                        UI.Button("Next page", "game-launcher.next",
                                "game-launcher.next")
                            .Disabled(!snapshot.HasAfter || !state.Interactive)))
                .Classes("game-launcher-content");
            initialFocus ??= GameLauncherIdentity.FocusId("add", snapshot.Items[0].Key);
        }
        else if (snapshot.Items.Count != 0 || state.FixedRows.All.Any())
        {
            var favorites = state.Organization.FavoriteSavedIds
                .Select((savedId, index) => (savedId, index))
                .ToDictionary(value => value.savedId, value => value.index,
                    StringComparer.Ordinal);
            var preferred = state.Organization.VariantGroups.ToDictionary(
                group => group.PreferredSavedId, group => group.Id, StringComparer.Ordinal);
            var groups = state.Organization.VariantGroups
                .SelectMany(group => group.SavedIds.Select(savedId => (savedId, group)))
                .ToDictionary(value => value.savedId, value => value.group,
                    StringComparer.Ordinal);
            var resolvedFixed = snapshot.Items.Concat(state.FixedRows.All)
                .DistinctBy(item => item.Value.SavedId, StringComparer.Ordinal)
                .ToDictionary(item => item.Value.SavedId, StringComparer.Ordinal);
            var stored = state.Organization.Items.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var recentRows = state.RecentMode == GameLauncherRecentMode.Off
                ? []
                : state.Organization.RecentSavedIds
                    .Select(savedId => PresentedRowFor(savedId, resolvedFixed, stored))
                    .Where(row => row is not null && MatchesFixedQuery(
                        row.Display, state.Query, state.FavoriteFilter, favorites))
                    .Select(row => row!)
                    .Take(GameLauncherPrivateState.MaximumRecentItems)
                    .ToArray();
            foreach (var row in recentRows) used.Add(row.Display.SavedId);
            var manualRows = state.RecentMode == GameLauncherRecentMode.RecentOnly
                ? []
                : state.Organization.ManualSavedIds
                    .Where(savedId => !used.Contains(savedId))
                    .Select(savedId => PresentedRowFor(savedId, resolvedFixed, stored))
                    .Where(row => row is not null &&
                        row.Current?.Value.Kind != WidgetAppLibraryKind.Game &&
                        MatchesFixedQuery(
                            row.Display, state.Query, state.FavoriteFilter, favorites))
                    .Select(row => row!)
                    .OrderBy(row => row, PresentedRowComparer(state.Query.Sort))
                    .Take(GameLauncherPrivateState.MaximumManualItems)
                    .ToArray();
            foreach (var row in manualRows) used.Add(row.Display.SavedId);
            var orderedCatalog = snapshot.Items
                .Where(item => !used.Contains(item.Value.SavedId))
                .Select((item, index) => (item, index))
                .OrderBy(value => favorites.ContainsKey(value.item.Value.SavedId) ? 0 : 1)
                .ThenBy(value => favorites.GetValueOrDefault(
                    value.item.Value.SavedId, int.MaxValue))
                .ThenBy(value => preferred.ContainsKey(value.item.Value.SavedId) ? 0 : 1)
                .ThenBy(value => value.index)
                .Select(value => new PresentedRow(value.item, new(
                    value.item.Value.SavedId, value.item.Value.DisplayName,
                    value.item.Value.SourceAttribution)))
                .ToArray();
            var liveIds = state.FixedRows.All.Concat(snapshot.Items)
                .Select(item => item.Value.SavedId)
                .ToHashSet(StringComparer.Ordinal);
            var fixedMembership = state.Organization.RecentSavedIds
                .Concat(state.Organization.ManualSavedIds)
                .ToHashSet(StringComparer.Ordinal);
            var unavailableRows = state.Organization.Items.Where(item =>
                    GameLauncherOrganizationPolicy.ReferencedSavedIds(state.Organization)
                        .Contains(item.SavedId, StringComparer.Ordinal) &&
                    !fixedMembership.Contains(item.SavedId) &&
                    !liveIds.Contains(item.SavedId) &&
                    MatchesFixedQuery(item, state.Query, state.FavoriteFilter, favorites))
                .Select(item => new PresentedRow(null, item))
                .ToArray();
            var sections = new List<WidgetElement>();
            AddSection(sections, "Recent", "game-launcher.recent.section", recentRows,
                favorites, preferred, groups, state, collectionItems: false);
            AddSection(sections, "Added games", "game-launcher.manual.section", manualRows,
                favorites, preferred, groups, state, collectionItems: false);
            AddSection(sections, recentRows.Length != 0 || manualRows.Length != 0
                    ? "Catalog" : "Library",
                "game-launcher.catalog.section",
                [.. orderedCatalog, .. unavailableRows], favorites, preferred, groups, state,
                collectionItems: true);
            var scroll = UI.VerticalScroll(ScrollId,
                    UI.Stack("game-launcher.library.sections", sections.ToArray())
                        .Classes("game-launcher-sections"))
                .Classes("game-launcher-scroll");
            if (snapshot.Status != WidgetPagedResourceStatus.Error &&
                (snapshot.HasBefore || snapshot.HasAfter))
                scroll = scroll.Paginate(
                    snapshot.HasBefore ? "game-launcher.library.cursor.before" : null,
                    snapshot.HasAfter ? "game-launcher.library.cursor.after" : null, 2);
            var catalogIds = orderedCatalog.Select(row => row.Display.SavedId)
                .ToHashSet(StringComparer.Ordinal);
            var retainedCatalogAnchor = snapshot.Anchor is { } anchor &&
                snapshot.Items.Any(item => item.Key == anchor &&
                    catalogIds.Contains(item.Value.SavedId))
                ? anchor.Value
                : orderedCatalog.FirstOrDefault()?.Current?.Key.Value;
            scroll = scroll with
            {
                CollectionAnchorKey = retainedCatalogAnchor,
            };
            var controls = UI.Row("game-launcher.actions",
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
                .Classes("game-launcher-actions");
            var hints = UI.Row("game-launcher.organization.hints",
                UI.ControllerHint(ControllerButton.X, "Favorite", "game-launcher.hint.favorite"),
                UI.ControllerHint(ControllerButton.LeftBumper, "Group variants",
                    "game-launcher.hint.variant"),
                UI.ControllerHint(ControllerButton.RightBumper, "Prefer variant",
                    "game-launcher.hint.prefer"));
            var children = new List<WidgetElement> { scroll, controls, hints };
            if (snapshot.Error is { } retained)
                children.Add(UI.Alert("Some games are unavailable", retained.Message,
                        AlertTone.Warning, "game-launcher.retained-error",
                        new ComponentAction("Try again", "game-launcher.retry", WidgetGlyph.Refresh))
                    .Classes("game-launcher-warning"));
            content = UI.Stack("game-launcher.content", children.ToArray())
                .Classes("game-launcher-content");
            initialFocus ??= recentRows.Concat(manualRows).FirstOrDefault() is { } fixedFirst
                ? GameLauncherIdentity.FocusId(
                    "grid", GameLauncherIdentity.Key(fixedFirst.Display.SavedId))
                : snapshot.Anchor is { } focusAnchor
                    ? GameLauncherIdentity.FocusId("grid", focusAnchor)
                    : snapshot.Items.Count == 0 ? null
                    : GameLauncherIdentity.FocusId("grid", snapshot.Items[0].Key);
        }
        else if (state.Route == GameLauncherRoute.Library &&
                 state.Organization.Items.Count != 0 && snapshot.Status is
                     WidgetPagedResourceStatus.NotLoaded or
                     WidgetPagedResourceStatus.Loading or
                     WidgetPagedResourceStatus.Refreshing or
                     WidgetPagedResourceStatus.Error or
                     WidgetPagedResourceStatus.Ready)
        {
            var warm = state.Organization.Items.Select(item => Tile(
                item.DisplayName, item.SourceAttribution, item.SavedId, null,
                null, resolved: false,
                launchState: null,
                favorite: state.Organization.FavoriteSavedIds.Contains(
                    item.SavedId, StringComparer.Ordinal),
                preferred: state.Organization.VariantGroups.Any(group =>
                    group.PreferredSavedId == item.SavedId),
                groupSize: GameLauncherOrganizationPolicy.GroupFor(
                    state.Organization, item.SavedId)?.SavedIds.Count ?? 0,
                interactive: false, key: GameLauncherIdentity.Key(item.SavedId))).ToArray();
            var warmScroll = UI.VerticalScroll(ScrollId,
                    UI.ResponsiveGrid("game-launcher.library.grid", 170, 5, warm)
                        .Classes("game-launcher-grid")) with
                { CollectionAnchorKey = GameLauncherIdentity.Key(
                    state.Organization.Items[0].SavedId).Value };
            content = UI.Stack("game-launcher.content",
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
            content = UI.Card("game-launcher.loading",
                UI.LoadingIndicator("game-launcher.loading.indicator", "Loading installed games"),
                UI.Text("Reading the trusted installed-game catalog…",
                    "game-launcher.loading.text", "Loading installed games"));
        }
        else if (snapshot.Status == WidgetPagedResourceStatus.Error)
        {
            content = UI.Alert("Game library unavailable",
                snapshot.Error?.Message ?? "The installed game library could not be loaded.",
                AlertTone.Danger, "game-launcher.error",
                new ComponentAction("Try again", "game-launcher.retry", WidgetGlyph.Refresh));
            initialFocus = "game-launcher.error.action";
        }
        else
        {
            content = UI.EmptyState("No installed games",
                "No trusted installed game registrations are currently available.",
                "game-launcher.empty",
                new ComponentAction("Refresh", "game-launcher.refresh", WidgetGlyph.Refresh),
                WidgetGlyph.Play);
            initialFocus = "game-launcher.empty.action";
        }

        var root = UI.Stack("game-launcher.root", header, sourceStatus, queryControls, content)
            .Classes("game-launcher-widget");
        return new WidgetView(root, initialFocus,
            Surface: Surface);
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
            var detail = refreshing ? "Refreshing installed games" : source.StatusCode switch
            {
                "healthy" => "Current installed games are available",
                "source_degraded" => "Some installed games may be missing",
                "source_unavailable" => "This source could not be refreshed",
                _ => "Source status is limited",
            };
            var text = $"{source.DisplayName}: {health} · {detail}";
            return UI.Text(text, "game-launcher.source." + source.SourceId, text)
                .Classes("game-launcher-source-row");
        }).ToArray();
        return UI.Stack("game-launcher.sources",
                UI.SectionHeader("Library sources", "game-launcher.sources.header",
                    description: $"{sources.Count} active {(sources.Count == 1 ? "source" : "sources")}"),
                UI.Stack("game-launcher.sources.rows", rows)
                    .Classes("game-launcher-source-rows"))
            .Classes("game-launcher-source-status");
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
            return new(current, new(current.Value.SavedId, current.Value.DisplayName,
                current.Value.SourceAttribution));
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

    private static void AddSection(
        ICollection<WidgetElement> sections,
        string title,
        string id,
        IReadOnlyList<PresentedRow> rows,
        IReadOnlyDictionary<string, int> favorites,
        IReadOnlyDictionary<string, string> preferred,
        IReadOnlyDictionary<string, GameLauncherVariantGroup> groups,
        GameLauncherPresentationState state,
        bool collectionItems)
    {
        if (rows.Count == 0) return;
        var gridId = id == "game-launcher.catalog.section"
            ? "game-launcher.library.grid"
            : id + ".grid";
        var tiles = rows.Select(row => Tile(
                row.Display.DisplayName,
                row.Display.SourceAttribution,
                row.Display.SavedId,
                row.Current?.Value.ArtworkHandle,
                state.LaunchingSavedId,
                state.LaunchStates.GetValueOrDefault(row.Display.SavedId),
                resolved: row.Current is not null,
                favorite: favorites.ContainsKey(row.Display.SavedId),
                preferred: preferred.ContainsKey(row.Display.SavedId),
                groupSize: groups.GetValueOrDefault(row.Display.SavedId)?.SavedIds.Count ?? 0,
                state.Interactive && !state.OrganizationBusy,
                row.Current?.Key ?? GameLauncherIdentity.Key(row.Display.SavedId),
                collectionItem: collectionItems && row.Current is not null))
            .ToArray();
        sections.Add(UI.Stack(id,
                UI.SectionHeader(title, id + ".header",
                    description: $"{rows.Count} {(rows.Count == 1 ? "item" : "items")}"),
                UI.ResponsiveGrid(gridId, 170, 5, tiles)
                    .Classes("game-launcher-grid"))
            .Classes("game-launcher-library-section"));
    }

    private static WidgetElement ManualTile(
        GameLauncherItem item,
        bool included,
        bool interactive)
    {
        var automatic = item.Value.Kind == WidgetAppLibraryKind.Game;
        var kind = item.Value.Kind switch
        {
            WidgetAppLibraryKind.Game => "Game",
            WidgetAppLibraryKind.Application => "Application",
            _ => "Unknown",
        };
        var state = automatic ? "Included" : included ? "Added" : "Available";
        var actionId = automatic
            ? "game-launcher.manual.included"
            : "game-launcher.manual.toggle";
        return UI.AppTile(item.Value.DisplayName, state,
                actionId,
                GameLauncherIdentity.FocusId("add", item.Key),
                subtitle: $"{kind} · {item.Value.SourceAttribution}",
                artwork: item.Value.ArtworkHandle is { Length: > 0 }
                    ? TileArtwork.FromHandle(new WidgetArtworkHandle(item.Value.ArtworkHandle),
                        item.Value.DisplayName, ImageFit.Cover)
                    : TileArtwork.FromGlyph(WidgetGlyph.Play, item.Value.DisplayName),
                accessibilityLabel: $"{item.Value.DisplayName}, {kind}, " +
                    (automatic ? "Included automatically" : included
                        ? "Added, remove from library" : "Available, add to library"),
                orientation: ActionSurfaceOrientation.Vertical)
            .Disabled(!interactive || automatic)
            .CollectionItem(item.Key)
            .Classes("game-launcher-tile");
    }

    private static WidgetElement Tile(
        string title,
        string source,
        string savedId,
        string? artworkHandle,
        string? launchingSavedId,
        GameLauncherLaunchState? launchState,
        bool resolved,
        bool favorite,
        bool preferred,
        int groupSize,
        bool interactive,
        WidgetCollectionItemKey key,
        bool collectionItem = true)
    {
        var id = GameLauncherIdentity.FocusId("grid", key);
        var launching = string.Equals(savedId, launchingSavedId, StringComparison.Ordinal);
        var artwork = artworkHandle is { Length: > 0 }
            ? TileArtwork.FromHandle(new WidgetArtworkHandle(artworkHandle), title, ImageFit.Cover)
            : TileArtwork.FromGlyph(WidgetGlyph.Play, title);
        var state = launching ? "Pending" : launchState switch
        {
            GameLauncherLaunchState.RequestAccepted => "Request accepted",
            GameLauncherLaunchState.LauncherStarted => "Launcher started",
            GameLauncherLaunchState.Running => "Running",
            GameLauncherLaunchState.Failed => "Failed",
            GameLauncherLaunchState.Ended => "Ended",
            _ => resolved ? interactive ? "Ready" : "Paused" : "Unavailable",
        };
        var traits = new List<string>(3);
        if (favorite) traits.Add("Favorite");
        if (preferred) traits.Add("Preferred variant");
        if (groupSize > 1) traits.Add($"{groupSize} grouped variants");
        var subtitle = traits.Count == 0 ? source : source + " · " + string.Join(" · ", traits);
        var tile = UI.AppTile(title, state,
                "game-launcher.launch", id, subtitle: subtitle, artwork: artwork,
                accessibilityLabel: $"{title}, {subtitle}, {state}",
                orientation: ActionSurfaceOrientation.Vertical)
            .Shortcut(ControllerButton.X, actionId: "game-launcher.favorite")
            .Shortcut(ControllerButton.LeftBumper, actionId: "game-launcher.variant")
            .Shortcut(ControllerButton.RightBumper, actionId: "game-launcher.prefer")
            .Busy(launching)
            .Disabled(!interactive || !resolved)
            .Classes("game-launcher-tile");
        return collectionItem ? tile.CollectionItem(key) : tile;
    }
}
