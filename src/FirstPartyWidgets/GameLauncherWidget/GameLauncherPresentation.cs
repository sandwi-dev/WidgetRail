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
    bool FavoriteFilter);

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
                UI.Text("Game Launcher", "game-launcher.title", "Game Launcher")
                    .Classes("game-launcher-title"),
                UI.Text(state.Status, "game-launcher.status", state.Status)
                    .Classes("game-launcher-status"))
            .Classes("game-launcher-header");

        var queryControls = UI.Stack("game-launcher.query",
            UI.TextEntry(
                    state.Query.SearchText ?? string.Empty,
                    "Search installed games",
                    "game-launcher.search.commit",
                    "game-launcher.search",
                    WidgetAppLibraryQuery.MaximumSearchTextLength)
                .Disabled(!state.Interactive)
                .Classes("game-launcher-search"),
            UI.Row("game-launcher.filters",
                UI.ToggleButton("Favorites", state.FavoriteFilter,
                    "game-launcher.filter.favorites", "game-launcher.filter.favorites")
                    .Disabled(!state.Interactive ||
                        state.Organization.FavoriteSavedIds.Count == 0),
                UI.Button("Source: " + (state.Query.SourceAttribution ?? "All"),
                        "game-launcher.filter.source", "game-launcher.filter.source")
                    .Disabled(!state.Interactive),
                UI.Button("Sort: " + (state.Query.Sort switch
                    {
                        WidgetAppLibrarySortOrder.DisplayNameDescending => "Z–A",
                        WidgetAppLibrarySortOrder.SourceThenDisplayName => "Source",
                        _ => "A–Z",
                    }), "game-launcher.filter.sort", "game-launcher.filter.sort")
                    .Disabled(!state.Interactive),
                UI.Button(state.RecentMode switch
                    {
                        GameLauncherRecentMode.RecentFirst => "Recent: First",
                        GameLauncherRecentMode.RecentOnly => "Recent: Only",
                        _ => "Recent: Off",
                    }, "game-launcher.filter.recent", "game-launcher.filter.recent")
                    .Disabled(!state.Interactive ||
                        state.Organization.RecentSavedIds.Count == 0),
                UI.Button("Clear", "game-launcher.query.clear", "game-launcher.query.clear")
                    .Disabled(!state.Interactive || !state.FavoriteFilter &&
                        state.RecentMode == GameLauncherRecentMode.Off &&
                        state.Query == new WidgetAppLibraryQuery(
                        InstalledOnly: true, Kind: WidgetAppLibraryKind.Game,
                        Sort: WidgetAppLibrarySortOrder.DisplayName)))
            .Classes("game-launcher-filters"))
        .Classes("game-launcher-query");

        WidgetElement content;
        string? initialFocus = snapshot.RequestedFocusId;
        if (snapshot.Items.Count != 0)
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
            var recent = state.Organization.RecentSavedIds
                .Select((savedId, index) => (savedId, index))
                .ToDictionary(value => value.savedId, value => value.index,
                    StringComparer.Ordinal);
            var ordered = snapshot.Items.Select((item, index) => (item, index))
                .OrderBy(value => state.RecentMode == GameLauncherRecentMode.RecentFirst &&
                    recent.ContainsKey(value.item.Value.SavedId) ? 0 : 1)
                .ThenBy(value => state.RecentMode == GameLauncherRecentMode.RecentFirst
                    ? recent.GetValueOrDefault(value.item.Value.SavedId, int.MaxValue)
                    : int.MaxValue)
                .ThenBy(value => favorites.ContainsKey(value.item.Value.SavedId) ? 0 : 1)
                .ThenBy(value => favorites.GetValueOrDefault(
                    value.item.Value.SavedId, int.MaxValue))
                .ThenBy(value => preferred.ContainsKey(value.item.Value.SavedId) ? 0 : 1)
                .ThenBy(value => value.index)
                .Select(value => value.item).ToArray();
            var tiles = ordered.Select(item => Tile(
                item.Value.DisplayName,
                item.Value.SourceAttribution,
                item.Value.SavedId,
                item.Value.ArtworkHandle,
                state.LaunchingSavedId,
                state.LaunchStates.GetValueOrDefault(item.Value.SavedId),
                resolved: true,
                favorite: favorites.ContainsKey(item.Value.SavedId),
                preferred: preferred.ContainsKey(item.Value.SavedId),
                groupSize: groups.GetValueOrDefault(item.Value.SavedId)?.SavedIds.Count ?? 0,
                state.Interactive && !state.OrganizationBusy,
                item.Key)).ToArray();
            var liveIds = snapshot.Items.Select(item => item.Value.SavedId)
                .ToHashSet(StringComparer.Ordinal);
            var unavailable = state.Organization.Items.Where(item =>
                    GameLauncherOrganizationPolicy.ReferencedSavedIds(state.Organization)
                        .Contains(item.SavedId, StringComparer.Ordinal) &&
                    !liveIds.Contains(item.SavedId))
                .Select(item => Tile(item.DisplayName, item.SourceAttribution,
                    item.SavedId, null, null, resolved: false,
                    launchState: null,
                    favorite: favorites.ContainsKey(item.SavedId),
                    preferred: preferred.ContainsKey(item.SavedId),
                    groupSize: groups.GetValueOrDefault(item.SavedId)?.SavedIds.Count ?? 0,
                    interactive: false, key: GameLauncherIdentity.Key(item.SavedId)))
                .ToArray();
            tiles = [.. tiles, .. unavailable];
            var grid = UI.ResponsiveGrid("game-launcher.library.grid", 170, 5, tiles)
                .Classes("game-launcher-grid");
            var scroll = UI.VerticalScroll(ScrollId, grid).Classes("game-launcher-scroll");
            if (snapshot.Status != WidgetPagedResourceStatus.Error &&
                (snapshot.HasBefore || snapshot.HasAfter))
                scroll = scroll.Paginate(
                    snapshot.HasBefore ? "game-launcher.library.cursor.before" : null,
                    snapshot.HasAfter ? "game-launcher.library.cursor.after" : null, 2);
            scroll = scroll with { CollectionAnchorKey = snapshot.Anchor?.Value };
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
            initialFocus ??= snapshot.Anchor is { } anchor
                ? GameLauncherIdentity.FocusId("grid", anchor)
                : GameLauncherIdentity.FocusId("grid", snapshot.Items[0].Key);
        }
        else if (state.Organization.Items.Count != 0 && snapshot.Status is
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

        var root = UI.Stack("game-launcher.root", header, queryControls, content)
            .InputScope("game-launcher")
            .Classes("game-launcher-widget");
        return new WidgetView(root, initialFocus,
            ActiveInputScopeId: "game-launcher", Surface: Surface);
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
        WidgetCollectionItemKey key)
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
        return UI.AppTile(title, state,
                "game-launcher.launch", id, subtitle: subtitle, artwork: artwork,
                accessibilityLabel: $"{title}, {subtitle}, {state}",
                orientation: ActionSurfaceOrientation.Vertical)
            .Shortcut(ControllerButton.X, actionId: "game-launcher.favorite")
            .Shortcut(ControllerButton.LeftBumper, actionId: "game-launcher.variant")
            .Shortcut(ControllerButton.RightBumper, actionId: "game-launcher.prefer")
            .Busy(launching)
            .Disabled(!interactive || !resolved)
            .CollectionItem(key)
            .Classes("game-launcher-tile");
    }
}
