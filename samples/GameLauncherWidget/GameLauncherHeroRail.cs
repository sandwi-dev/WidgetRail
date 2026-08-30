using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameLauncher;

internal sealed record GameLauncherHeroRailItem(
    GameLauncherItem? Current,
    GameLauncherDisplayItem Display,
    bool Favorite,
    bool Preferred,
    int GroupSize,
    bool CollectionItem)
{
    internal WidgetCollectionItemKey Key =>
        Current?.Key ?? GameLauncherIdentity.Key(Display.SavedId);

    internal string FocusId => GameLauncherIdentity.FocusId("grid", Key);
}

internal sealed record GameLauncherHeroRailModel(
    IReadOnlyList<GameLauncherHeroRailItem> Items,
    int SelectedIndex,
    string? CatalogAnchorKey,
    bool PageBumpers)
{
    internal GameLauncherHeroRailItem? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
}

/// <summary>
/// Owns the bounded, non-authorizing Library rail order and focus-to-hero selection.
/// Provider authority and launch routing remain in <see cref="GameLauncherWidget"/>.
/// </summary>
internal static class GameLauncherHeroRailPolicy
{
    internal static GameLauncherHeroRailModel Project(
        GameLauncherPresentationState state,
        string? selectedSavedId,
        int fallbackIndex)
    {
        var snapshot = state.Collection;
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
        var resolved = snapshot.Items.Concat(state.FixedRows.All)
            .DistinctBy(item => item.Value.SavedId, StringComparer.Ordinal)
            .ToDictionary(item => item.Value.SavedId, StringComparer.Ordinal);
        var stored = state.Organization.Items.ToDictionary(
            item => item.SavedId, StringComparer.Ordinal);
        var excluded = state.Organization.ExcludedSavedIds.ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        var recent = state.RecentMode == GameLauncherRecentMode.Off
            ? []
            : state.Organization.RecentSavedIds
                .Where(savedId => !excluded.Contains(savedId))
                .Select(savedId => RowFor(savedId, resolved, stored))
                .Where(row => row is not null && Matches(
                    row.Display, state.Query, state.FavoriteFilter, favorites))
                .Select(row => row!)
                .Take(GameLauncherPrivateState.MaximumRecentItems)
                .ToArray();
        foreach (var row in recent) used.Add(row.Display.SavedId);

        var manual = state.RecentMode == GameLauncherRecentMode.RecentOnly
            ? []
            : state.Organization.ManualSavedIds
                .Where(savedId => !used.Contains(savedId) && !excluded.Contains(savedId))
                .Select(savedId => RowFor(savedId, resolved, stored))
                .Where(row => row is not null &&
                    row.Current?.Presentation.Kind != WidgetAppLibraryKind.Game &&
                    Matches(row.Display, state.Query, state.FavoriteFilter, favorites))
                .Select(row => row!)
                .OrderBy(row => row, RowComparer(state.Query.Sort))
                .Take(GameLauncherPrivateState.MaximumManualItems)
                .ToArray();
        foreach (var row in manual) used.Add(row.Display.SavedId);

        var titleMatches = state.FixedRows.TitleMatches
            .Where(item => !used.Contains(item.Value.SavedId) &&
                !excluded.Contains(item.Value.SavedId))
            .Select(item => RowFor(item.Value.SavedId, resolved, stored))
            .Where(row => row is not null && Matches(
                row.Display, state.Query, state.FavoriteFilter, favorites))
            .Select(row => row!)
            .Take(WidgetAppLibraryService.MaximumSavedItems)
            .ToArray();
        foreach (var row in titleMatches) used.Add(row.Display.SavedId);

        var catalog = snapshot.Items
            .Where(item => !used.Contains(item.Value.SavedId) &&
                !excluded.Contains(item.Value.SavedId))
            .Select((item, index) => (item, index))
            .OrderBy(value => favorites.ContainsKey(value.item.Value.SavedId) ? 0 : 1)
            .ThenBy(value => favorites.GetValueOrDefault(
                value.item.Value.SavedId, int.MaxValue))
            .ThenBy(value => preferred.ContainsKey(value.item.Value.SavedId) ? 0 : 1)
            .ThenBy(value => value.index)
            .Select(value => new RailRow(value.item, new(
                value.item.Value.SavedId, value.item.Presentation.DisplayName,
                value.item.Presentation.Source.DisplayName), CollectionItem: true))
            .ToArray();

        var liveIds = state.FixedRows.All.Concat(snapshot.Items)
            .Select(item => item.Value.SavedId)
            .ToHashSet(StringComparer.Ordinal);
        var fixedMembership = state.Organization.RecentSavedIds
            .Concat(state.Organization.ManualSavedIds)
            .ToHashSet(StringComparer.Ordinal);
        var unavailable = state.Organization.Items.Where(item =>
                GameLauncherOrganizationPolicy.ReferencedSavedIds(state.Organization)
                    .Contains(item.SavedId, StringComparer.Ordinal) &&
                !fixedMembership.Contains(item.SavedId) &&
                !excluded.Contains(item.SavedId) &&
                !liveIds.Contains(item.SavedId) &&
                Matches(item, state.Query, state.FavoriteFilter, favorites))
            .Select(item => new RailRow(null, item, CollectionItem: false))
            .Take(WidgetAppLibraryService.MaximumSavedItems)
            .ToArray();

        var rows = recent.Select(row => row with { CollectionItem = false })
            .Concat(manual.Select(row => row with { CollectionItem = false }))
            .Concat(titleMatches.Select(row => row with { CollectionItem = false }))
            .Concat(catalog)
            .Concat(unavailable)
            .Select(row => new GameLauncherHeroRailItem(
                row.Current,
                row.Display,
                favorites.ContainsKey(row.Display.SavedId),
                preferred.ContainsKey(row.Display.SavedId),
                groups.GetValueOrDefault(row.Display.SavedId)?.SavedIds.Count ?? 0,
                row.CollectionItem))
            .ToArray();

        var selectedIndex = selectedSavedId is null ? -1 : Array.FindIndex(rows,
            row => string.Equals(row.Display.SavedId, selectedSavedId,
                StringComparison.Ordinal));
        if (selectedIndex < 0 && rows.Length != 0)
            selectedIndex = Math.Clamp(fallbackIndex, 0, rows.Length - 1);

        var catalogIds = catalog.Select(row => row.Display.SavedId)
            .ToHashSet(StringComparer.Ordinal);
        var anchor = snapshot.Anchor is { } currentAnchor && snapshot.Items.Any(item =>
                item.Key == currentAnchor && catalogIds.Contains(item.Value.SavedId))
            ? currentAnchor.Value
            : catalog.FirstOrDefault()?.Current?.Key.Value;
        return new(rows, selectedIndex, anchor,
            snapshot.HasBefore || snapshot.HasAfter);
    }

    internal static GameLauncherHeroRailItem? SelectFromInput(
        GameLauncherHeroRailModel model,
        string? focusedElementId,
        ControllerButton button)
    {
        var focusedIndex = focusedElementId is null ? -1 : model.Items
            .Select((item, index) => (item, index))
            .Where(value => string.Equals(value.item.FocusId, focusedElementId,
                StringComparison.Ordinal))
            .Select(value => value.index)
            .DefaultIfEmpty(-1)
            .First();
        if (focusedIndex < 0) return null;
        var selectedIndex = button switch
        {
            ControllerButton.DPadLeft => Math.Max(0, focusedIndex - 1),
            ControllerButton.DPadRight => Math.Min(model.Items.Count - 1, focusedIndex + 1),
            _ => focusedIndex,
        };
        return model.Items[selectedIndex];
    }

    private sealed record RailRow(
        GameLauncherItem? Current,
        GameLauncherDisplayItem Display,
        bool CollectionItem = false);

    private static RailRow? RowFor(
        string savedId,
        IReadOnlyDictionary<string, GameLauncherItem> resolved,
        IReadOnlyDictionary<string, GameLauncherDisplayItem> stored)
    {
        if (resolved.TryGetValue(savedId, out var current))
            return new(current, new(current.Value.SavedId,
                current.Presentation.DisplayName,
                current.Presentation.Source.DisplayName));
        return stored.TryGetValue(savedId, out var display) ? new(null, display) : null;
    }

    private static bool Matches(
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

    private static IComparer<RailRow> RowComparer(WidgetAppLibrarySortOrder sort) =>
        Comparer<RailRow>.Create((left, right) =>
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
}

internal static class GameLauncherHeroRailPresentation
{
    internal static WidgetElement Render(
        GameLauncherHeroRailItem? selected,
        string? launchingSavedId,
        IReadOnlyDictionary<string, GameLauncherLaunchState> launchStates)
    {
        if (selected is null)
            return Fallback("Choose a game", "Installed games will appear in the rail.");

        var title = selected.Display.DisplayName;
        var available = selected.Current is { } current &&
            current.Value.Presentation.Availability.State ==
                WidgetAppLibraryAvailabilityState.Installed &&
            current.Value.Presentation.Availability.IsLaunchable &&
            current.Value.Presentation.Capabilities.Supports(
                WidgetAppLibraryAction.Launch);
        var launching = string.Equals(selected.Display.SavedId, launchingSavedId,
            StringComparison.Ordinal);
        var launchState = launchStates.TryGetValue(
            selected.Display.SavedId, out var recorded) ? recorded : (GameLauncherLaunchState?)null;
        var state = launching ? "Pending" : launchState switch
        {
            GameLauncherLaunchState.RequestAccepted => "Request accepted",
            GameLauncherLaunchState.LauncherStarted => "Launcher started",
            GameLauncherLaunchState.Running => "Running",
            GameLauncherLaunchState.Failed => "Failed",
            GameLauncherLaunchState.Ended => "Ended",
            _ => available ? "Ready" : "Unavailable",
        };
        var traits = new List<string>(3);
        if (selected.Favorite) traits.Add("Favorite");
        if (selected.Preferred) traits.Add("Preferred variant");
        if (selected.GroupSize > 1) traits.Add($"{selected.GroupSize} grouped variants");
        var metadata = traits.Count == 0 ? state : state + " · " + string.Join(" · ", traits);
        WidgetElement artwork = selected.Current?.ArtworkPngBase64 is { } png
            ? UI.InlinePngImage(png, "game-launcher.hero.artwork",
                $"Artwork for {title}", ImageFit.Cover)
            : selected.Current?.Presentation.Artwork.Find(
                WidgetAppLibraryArtworkRole.Hero) is { } hero
            ? UI.Artwork(new WidgetArtworkHandle(hero.Handle), "game-launcher.hero.artwork",
                $"Artwork for {title}", ImageFit.Cover)
            : UI.Icon(WidgetGlyph.Play, "game-launcher.hero.artwork",
                $"Artwork unavailable for {title}");
        return UI.Card("game-launcher.hero",
                artwork.Classes("game-launcher-hero-artwork"),
                UI.Stack("game-launcher.hero.content",
                        UI.Text(title, "game-launcher.hero.title", title)
                            .Classes("game-launcher-hero-title"),
                        UI.Text(selected.Display.SourceAttribution,
                                "game-launcher.hero.source",
                                $"Source: {selected.Display.SourceAttribution}")
                            .Classes("game-launcher-hero-source"),
                        UI.Text(metadata, "game-launcher.hero.state", metadata)
                            .Classes("game-launcher-hero-state"))
                    .Classes("game-launcher-hero-content"))
            .Classes("game-launcher-hero");
    }

    internal static WidgetElement Fallback(string title, string detail) =>
        UI.Card("game-launcher.hero",
                UI.Icon(WidgetGlyph.Play, "game-launcher.hero.artwork", title)
                    .Classes("game-launcher-hero-artwork"),
                UI.Stack("game-launcher.hero.content",
                        UI.Text(title, "game-launcher.hero.title", title)
                            .Classes("game-launcher-hero-title"),
                        UI.Text(detail, "game-launcher.hero.state", detail)
                            .Classes("game-launcher-hero-state"))
                    .Classes("game-launcher-hero-content"))
            .Classes("game-launcher-hero");
}
