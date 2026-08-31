using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryHeroRailItem(
    PlayniteLibraryItem? Current,
    PlayniteLibraryDisplayItem Display,
    bool Favorite,
    bool Preferred,
    int GroupSize,
    bool CollectionItem)
{
    internal WidgetCollectionItemKey Key =>
        Current?.Key ?? PlayniteLibraryIdentity.Key(Display.SavedId);

    internal string FocusId => PlayniteLibraryIdentity.FocusId("grid", Key);
}

internal sealed record PlayniteLibraryHeroRailModel(
    IReadOnlyList<PlayniteLibraryHeroRailItem> Items,
    int SelectedIndex,
    string? CatalogAnchorKey,
    bool PageBumpers)
{
    internal PlayniteLibraryHeroRailItem? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
}

/// <summary>
/// Owns the bounded, non-authorizing Library rail order and focus-to-hero selection.
/// Provider authority and launch routing remain in <see cref="PlayniteLibraryWidget"/>.
/// </summary>
internal static class PlayniteLibraryHeroRailPolicy
{
    internal static PlayniteLibraryHeroRailModel Project(
        PlayniteLibraryPresentationState state,
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

        var recent = state.RecentMode == PlayniteLibraryRecentMode.Off
            ? []
            : state.Organization.RecentSavedIds
                .Where(savedId => !excluded.Contains(savedId))
                .Select(savedId => RowFor(savedId, resolved, stored))
                .Where(row => row is not null && Matches(
                    row.Display, state.Query, state.FavoriteFilter, favorites))
                .Select(row => row!)
                .Take(PlayniteLibraryPrivateState.MaximumRecentItems)
                .ToArray();
        foreach (var row in recent) used.Add(row.Display.SavedId);

        var manual = state.RecentMode == PlayniteLibraryRecentMode.RecentOnly
            ? []
            : state.Organization.ManualSavedIds
                .Where(savedId => !used.Contains(savedId) && !excluded.Contains(savedId))
                .Select(savedId => RowFor(savedId, resolved, stored))
                .Where(row => row is not null &&
                    row.Current?.Presentation.Kind != WidgetAppLibraryKind.Game &&
                    Matches(row.Display, state.Query, state.FavoriteFilter, favorites))
                .Select(row => row!)
                .OrderBy(row => row, RowComparer(state.Query.Sort))
                .Take(PlayniteLibraryPrivateState.MaximumManualItems)
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
                PlayniteLibraryOrganizationPolicy.ReferencedSavedIds(state.Organization)
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
            .Select(row => new PlayniteLibraryHeroRailItem(
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

    internal static PlayniteLibraryHeroRailItem? SelectFromInput(
        PlayniteLibraryHeroRailModel model,
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
        PlayniteLibraryItem? Current,
        PlayniteLibraryDisplayItem Display,
        bool CollectionItem = false);

    private static RailRow? RowFor(
        string savedId,
        IReadOnlyDictionary<string, PlayniteLibraryItem> resolved,
        IReadOnlyDictionary<string, PlayniteLibraryDisplayItem> stored)
    {
        if (resolved.TryGetValue(savedId, out var current))
            return new(current, new(current.Value.SavedId,
                current.Presentation.DisplayName,
                current.Presentation.Source.DisplayName));
        return stored.TryGetValue(savedId, out var display) ? new(null, display) : null;
    }

    private static bool Matches(
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

internal static class PlayniteLibraryHeroRailPresentation
{
    internal static WidgetElement Render(
        PlayniteLibraryHeroRailItem? selected,
        string? launchingSavedId,
        IReadOnlyDictionary<string, PlayniteLibraryLaunchState> launchStates)
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
            selected.Display.SavedId, out var recorded) ? recorded : (PlayniteLibraryLaunchState?)null;
        var state = launching ? "Pending" : launchState switch
        {
            PlayniteLibraryLaunchState.RequestAccepted => "Request accepted",
            PlayniteLibraryLaunchState.LauncherStarted => "Launcher started",
            PlayniteLibraryLaunchState.Running => "Running",
            PlayniteLibraryLaunchState.Failed => "Failed",
            PlayniteLibraryLaunchState.Ended => "Ended",
            _ => available ? "Ready" : "Unavailable",
        };
        var traits = new List<string>(3);
        if (selected.Favorite) traits.Add("Favorite");
        if (selected.Preferred) traits.Add("Preferred variant");
        if (selected.GroupSize > 1) traits.Add($"{selected.GroupSize} grouped variants");
        var metadata = traits.Count == 0 ? state : state + " · " + string.Join(" · ", traits);
        WidgetElement artwork = selected.Current?.Presentation.Artwork.Find(
                WidgetAppLibraryArtworkRole.Hero) is { } hero
            ? UI.Artwork(new WidgetArtworkHandle(hero.Handle), "playnite-library.hero.artwork",
                $"Artwork for {title}", ImageFit.Cover)
            : UI.Icon(WidgetGlyph.Play, "playnite-library.hero.artwork",
                $"Artwork unavailable for {title}");
        return UI.Card("playnite-library.hero",
                artwork.Classes("playnite-library-hero-artwork"),
                UI.Stack("playnite-library.hero.content",
                        UI.Text(title, "playnite-library.hero.title", title)
                            .Classes("playnite-library-hero-title"),
                        UI.Text(selected.Display.SourceAttribution,
                                "playnite-library.hero.source",
                                $"Source: {selected.Display.SourceAttribution}")
                            .Classes("playnite-library-hero-source"),
                        UI.Text(metadata, "playnite-library.hero.state", metadata)
                            .Classes("playnite-library-hero-state"))
                    .Classes("playnite-library-hero-content"))
            .Classes("playnite-library-hero");
    }

    internal static WidgetElement Fallback(string title, string detail) =>
        UI.Card("playnite-library.hero",
                UI.Icon(WidgetGlyph.Play, "playnite-library.hero.artwork", title)
                    .Classes("playnite-library-hero-artwork"),
                UI.Stack("playnite-library.hero.content",
                        UI.Text(title, "playnite-library.hero.title", title)
                            .Classes("playnite-library-hero-title"),
                        UI.Text(detail, "playnite-library.hero.state", detail)
                            .Classes("playnite-library-hero-state"))
                    .Classes("playnite-library-hero-content"))
            .Classes("playnite-library-hero");
}
