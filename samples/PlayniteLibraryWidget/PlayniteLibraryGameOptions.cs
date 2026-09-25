using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteLibraryGameOptions
{
    internal static WidgetContextAction[] Create(PlayniteLibraryItem item, bool favorite,
        IReadOnlyList<PlayniteLibraryCategory>? categories, bool busy = false)
    {
        var enabled = !busy && PlayniteLibraryAvailabilityPresentation.CanManage(item);
        var actions = new List<WidgetContextAction>
        {
            new(PlayniteLibraryActions.Launch, "Play",
                IsDisabled: busy || !PlayniteLibraryAvailabilityPresentation.Tile(item).Launchable),
            new(PlayniteLibraryActions.Favorite, favorite ? "Remove favorite" : "Add favorite", IsDisabled: !enabled),
            new(PlayniteLibraryActions.Hide, "Hide", IsDisabled: !enabled),
            new(PlayniteLibraryActions.RefreshSource, "Refresh source", IsDisabled: !enabled),
        };
        foreach (var category in (categories ?? []).Take(5))
        {
            var included = PlayniteLibraryCategoryPolicy.Contains(category, item.Value.SavedId);
            actions.Add(new(PlayniteLibraryActions.CategoryMembership(category.Id),
                included ? $"Remove from {category.Name}" : $"Add to {category.Name}", IsDisabled: !enabled));
        }
        return actions.ToArray();
    }
}
