using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static partial class PlayniteLibraryPresentation
{
    internal static WidgetElement FavoriteCard(PlayniteLibraryHomeContent content, PlayniteLibraryHomeItem item)
    {
        var id = "playnite-library.item.favorite." + item.Key.Value;
        var current = item.Row.Current;
        var surface = UI.ActionSurface(PlayniteLibraryActions.DetailsOpen, id, item.Row.Display.DisplayName,
            ActionSurfaceOrientation.Horizontal, UI.Icon(FavoriteIcon(true), id + ".heart", "Favorite").Classes("playnite-library-favorite-icon"),
            UI.Stack(id + ".copy",
                UI.Text(item.Row.Display.DisplayName, id + ".title").Classes("playnite-library-favorite-title"),
                UI.Text(item.Row.Display.SourceAttribution, id + ".source").Classes("playnite-library-summary-meta")))
            .Disabled(!content.ActionsEnabled || current is null)
            .Classes("playnite-library-favorite-card");
        if (current is not null)
            surface = surface with { ContextMenuButton = ControllerButton.X,
                ContextActions = PlayniteLibraryGameOptions.Create(current, true, content.Query.Categories, !content.ActionsEnabled) };
        WidgetElement card = surface.Shortcut(ControllerButton.Y, actionId: PlayniteLibraryActions.QuickPlay, label: "Play");
        if (current is not null)
            card = card.PresentOnFocus(FocusedGameSummary(item.Row.Display.SavedId, item.Row.Display.DisplayName,
                item.Row.Display.SourceAttribution, "Play", true, null, current));
        if (current?.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Hero) is { Handle.Length: > 0 } background)
            card = card.FocusBackground(new WidgetArtworkHandle(background.Handle));
        return card.CollectionItem(item.Key);
    }
    internal static WidgetIcon FavoriteIcon(bool selected) => WidgetIcon.PackageSvg(
        selected ? "playnite.favorite.filled" : "playnite.favorite.outline", WidgetPackageIconColorMode.ThemeTint, WidgetGlyph.Like);

    private static WidgetElement BrowseToolbar(PlayniteLibraryPresentationState state, bool enabled)
    {
        var search = UI.TextEntry(state.Query.SearchText ?? "", "Search games", PlayniteLibraryActions.SearchCommit,
            "playnite-library.search", WidgetAppLibraryQuery.MaximumSearchTextLength);
        if (state.SuggestionSource is { } source) search = search.Suggest(source);
        var active = new List<WidgetElement>
        {
            Chip("All games", PlayniteLibraryActions.AllGames, !state.Query.InstalledOnly && !state.FavoriteFilter &&
                !state.RecentlyPlayed && state.ActiveCategoryId is null && state.Query.SourceAttribution is null),
            Chip("Installed", PlayniteLibraryActions.InstalledFilter, state.Query.InstalledOnly),
            Chip("Favorites", PlayniteLibraryActions.FavoritesFilter, state.FavoriteFilter),
        };
        if (state.RecentlyPlayed) active.Add(UI.Text("Recently played", "playnite-library.filter-summary.recent").Classes("playnite-library-filter-tag"));
        if (state.Query.SourceAttribution is { } attribution)
            active.Add(UI.Text(attribution, "playnite-library.filter-summary.source").Classes("playnite-library-filter-tag"));
        if (state.Organization.Categories.FirstOrDefault(category => category.Id == state.ActiveCategoryId) is { } category)
            active.Add(UI.Text(category.Name, "playnite-library.filter-summary.category").Classes("playnite-library-filter-tag"));
        active.Add(UI.Text(BrowseStatus(state), "playnite-library.status").Classes("playnite-library-result-count"));
        return UI.Stack("playnite-library.query",
            UI.Row("playnite-library.toolbar",
                UI.Stack("playnite-library.search.group", search.Disabled(!enabled).Classes("playnite-library-control", "playnite-library-search"))
                    .RememberChildFocus("playnite-library.search").Classes("playnite-library-search-group"),
                UI.Select("Sort", SortOptions(state.Query.Sort), PlayniteLibraryActions.SortFilter, "Sort games")
                    .Disabled(!enabled || state.RecentlyPlayed).Classes("playnite-library-control", "playnite-library-sort"),
                UI.Button("Filters", PlayniteLibraryActions.FiltersOpen, PlayniteLibraryActions.FiltersOpen)
                    .Disabled(!enabled).Classes("playnite-library-control", "playnite-library-filter-button"))
                .Classes("playnite-library-toolbar"),
            UI.HorizontalScroll("playnite-library.filters", active.ToArray()).Classes("playnite-library-filter-chips"))
            .Classes("playnite-library-query");

        WidgetElement Chip(string title, string action, bool selected) =>
            UI.Button(title, action, action).Disabled(!enabled || action == PlayniteLibraryActions.FavoritesFilter && !selected && state.Organization.FavoriteSavedIds.Count == 0).Classes(selected
                ? ["playnite-library-chip", "playnite-library-filter-active"] : ["playnite-library-chip"]);
    }

    internal static WidgetModal Filters(PlayniteLibraryPresentationState state, PlayniteLibraryFilterDraft draft)
    {
        var view = state with { Query = draft.Collection.Query, FavoriteFilter = draft.Collection.FavoriteFilter,
            RecentlyPlayed = draft.Collection.RecentlyPlayed, ActiveCategoryId = draft.CategoryId };
        return new("playnite-library.filter-dialog", "Filter library",
            UI.Stack("playnite-library.filter-dialog.content",
                UI.Switch("Installed only", view.Query.InstalledOnly, "playnite-library.filters.installed", "playnite-library.filters.installed"),
                UI.Switch("Favorites only", view.FavoriteFilter, "playnite-library.filters.favorites", "playnite-library.filters.favorites"),
                UI.Switch("Recently played", view.RecentlyPlayed, "playnite-library.filters.recent", "playnite-library.filters.recent"),
                UI.Select("Source", DraftOptions(SourceOptions(view)), "playnite-library.filters.source"),
                UI.Select("Category", DraftOptions(CategoryOptions(view)), "playnite-library.filters.category"),
                UI.Row("playnite-library.filters.actions",
                    UI.Button("Reset", "playnite-library.filters.reset", "playnite-library.filters.reset"),
                    UI.Button("Apply", PlayniteLibraryActions.FiltersApply, PlayniteLibraryActions.FiltersApply).Classes("playnite-library-primary")))
                .Classes("playnite-library-filter-dialog"),
            "playnite-library.filters.installed", PlayniteLibraryActions.FiltersCancel)
        {
            ScrollContent = true,
            HeaderActions = UI.ControllerHint(ControllerButton.B, "Cancel", "playnite-library.filters.cancel-hint"),
            AnchoredPresentation = new(new(ElementId: PlayniteLibraryActions.FiltersOpen), Width: 420, Height: 560)
                { SizeToContentHeight = true },
        };
    }

    private static IReadOnlyList<SelectOption> DraftOptions(IReadOnlyList<SelectOption> options) =>
        options.Select(option => option with { ActionId = "playnite-library.filters.draft." + option.ActionId }).ToArray();

    private static WidgetElement DefaultBrowseSummary() =>
        UI.Stack("playnite-library.browse.summary.default",
            UI.Text("Your library", "playnite-library.browse.summary.default.title").Classes("playnite-library-summary-title", "playnite-library-summary-title-compact"),
            UI.Text("Choose a game to see its details", "playnite-library.browse.summary.default.meta").Classes("playnite-library-summary-meta"))
            .Classes("playnite-library-summary-strip");

    private static WidgetElement BrowseSummary(string title, string metadata, List<WidgetElement> badges,
        PlayniteLibraryItem current, string id) =>
        UI.Row("playnite-library.summary." + id,
            UI.Stack("playnite-library.summary.copy." + id,
                UI.Text(title, "playnite-library.summary.title." + id).Classes("playnite-library-summary-title", "playnite-library-summary-title-compact"),
                UI.Row("playnite-library.summary.meta." + id,
                    UI.Text(metadata, "playnite-library.summary.metadata." + id).Classes("playnite-library-summary-meta"),
                    UI.Row("playnite-library.summary.badges." + id, badges.ToArray()).Classes("playnite-library-summary-badges"))
                    .Classes("playnite-library-summary-meta-row")).Classes("playnite-library-summary-copy"),
            QuickPlayHint(current, "playnite-library.summary.play." + id))
            .Classes("playnite-library-summary-strip");

    private static WidgetElement QuickPlayHint(PlayniteLibraryItem current, string id)
    {
        var unavailable = !PlayniteLibraryAvailabilityPresentation.Tile(current).Launchable &&
            !PlayniteLibraryAvailabilityPresentation.IsUninstalled(current);
        return UI.Row(id,
            UI.ControllerGlyph(ControllerButton.Y, id + ".key").Classes("playnite-library-play-key"),
            UI.Text(PlayniteLibraryAvailabilityPresentation.IsUninstalled(current) ? "Install" : "Play", id + ".label")
                .Classes("playnite-library-play-label"))
            .Classes(unavailable ? ["playnite-library-play-hint", "playnite-library-hint-unavailable"] : ["playnite-library-play-hint"]);
    }
}
