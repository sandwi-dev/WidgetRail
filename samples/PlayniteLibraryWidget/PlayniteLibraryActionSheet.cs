using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteLibraryActionSheet
{
    private const int FixedActionCount = 8;
    internal const string ScopeId = "playnite-library.actions.scope";
    internal const string CloseAction = "playnite-library.actions.close";
    internal const string OpenAction = "playnite-library.actions.open";
    internal const string RefreshSourceAction = "playnite-library.actions.refresh-source";
    internal const string ManageCategoriesAction = "playnite-library.actions.categories";
    internal const string EditTitleAction = "playnite-library.actions.edit-title";
    internal const string DetailsAction = "playnite-library.details.open";
    internal const string DetailsItemId = "playnite-library.actions.details";
    internal const string CategoryActionPrefix = "playnite-library.actions.category.";
    internal const string InitialFocusId = "playnite-library.actions.favorite";

    internal static WidgetView Render(
        PlayniteLibraryDetailsState state,
        IReadOnlyList<PlayniteLibraryCategory> categories)
    {
        var available = state.Interactive && state.Resolved && !state.Busy;
        var source = string.IsNullOrWhiteSpace(state.SourceAttribution)
            ? "game source"
            : state.SourceAttribution + " source";
        var items = new List<ActionSheetItem>
        {
            new(
                DetailsItemId,
                "View details",
                DetailsAction,
                WidgetGlyph.Play,
                "View full details for this exact game",
                IsDisabled: !available),
            new(
                InitialFocusId,
                state.Favorite ? "Remove favorite" : "Add favorite",
                "playnite-library.favorite",
                WidgetGlyph.Like,
                state.Favorite ? "Remove from favorites" : "Add to favorites",
                IsDisabled: !available),
            new(
                "playnite-library.actions.hide",
                "Hide game",
                "playnite-library.hide",
                WidgetGlyph.Warning,
                "Hide this exact game from the library",
                ActionSheetItemTone.Danger,
                IsDisabled: !available),
            new(
                "playnite-library.actions.variant",
                state.VariantActionLabel,
                "playnite-library.variant",
                WidgetGlyph.Settings,
                state.VariantActionLabel,
                IsDisabled: !available || !state.VariantActionEnabled),
            new(
                "playnite-library.actions.prefer",
                state.Preferred ? "Preferred variant" : "Prefer variant",
                "playnite-library.prefer",
                WidgetGlyph.Check,
                state.Preferred ? "Already the preferred variant" : "Prefer this variant",
                IsDisabled: !available || state.GroupSize < 2 || state.Preferred),
        };
        items.AddRange(categories.Take(UI.MaximumActionSheetItems - FixedActionCount)
            .Select(category =>
        {
            var included = PlayniteLibraryCategoryPolicy.Contains(
                category, state.Selection.SavedId);
            return new ActionSheetItem(
                CategoryActionPrefix + category.Id,
                included ? $"Remove from {category.Name}" : $"Add to {category.Name}",
                CategoryActionPrefix + category.Id,
                included ? WidgetGlyph.Check : WidgetGlyph.Play,
                included
                    ? $"Remove {state.DisplayName} from {category.Name}"
                    : $"Add {state.DisplayName} to {category.Name}",
                IsDisabled: !available);
        }));
        items.Add(new(
            "playnite-library.actions.edit-title",
            "Edit title",
            EditTitleAction,
            WidgetGlyph.Settings,
            "Set or reset the display and search title for this exact game",
            IsDisabled: !available));
        items.Add(new(
            "playnite-library.actions.categories",
            "Manage categories",
            ManageCategoriesAction,
            WidgetGlyph.Settings,
            "Create, rename, browse, or delete categories",
            IsDisabled: !state.Interactive || state.Busy));
        items.Add(new(
            "playnite-library.actions.refresh-source",
            $"Refresh {source}",
            RefreshSourceAction,
            WidgetGlyph.Refresh,
            $"Refresh current installed evidence for {state.SourceAttribution}",
            IsDisabled: !state.Interactive || state.Busy));

        var sheet = UI.ActionSheet(
            $"Actions for {state.DisplayName}",
            "playnite-library.actions.sheet",
            ScopeId,
            CloseAction,
            items,
            $"{state.SourceAttribution} · {state.Availability}.");
        return new WidgetView(sheet, InitialFocusId,
            ActiveInputScopeId: ScopeId,
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Compact,
                PreferredWidth = 560,
                PreferredHeight = 620,
                MinimumWidth = 320,
                MinimumHeight = 360,
            });
    }
}
