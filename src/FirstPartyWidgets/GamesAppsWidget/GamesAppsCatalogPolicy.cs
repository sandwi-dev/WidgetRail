using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.GamesApps;

internal enum GamesAppsCatalogPageTransition
{
    Initial,
    Next,
    Previous,
}

internal sealed record GamesAppsCatalogState(
    IReadOnlyList<WidgetAppLibraryItem> Items,
    WidgetCollectionCursor? Before,
    WidgetCollectionCursor? After,
    string Revision)
{
    internal static GamesAppsCatalogState Empty { get; } = new([], null, null, string.Empty);
    internal bool CanLoadPrevious => Before is not null;
}

internal sealed record GamesAppsCatalogPageResult(
    GamesAppsCatalogState State,
    bool EmptyInitial,
    bool EmptyContinuation);

/// <summary>
/// Pure bounded Catalog navigation policy. Provider I/O and lifecycle admission
/// remain with <see cref="GamesAppsWidget"/>.
/// </summary>
internal static class GamesAppsCatalogPolicy
{
    internal static GamesAppsCatalogPageResult ApplyPage(
        GamesAppsCatalogState current,
        WidgetAppLibraryPage? page,
        GamesAppsCatalogPageTransition transition,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(current);
        var normalized = Normalize(page?.Items, pageSize);
        if (transition == GamesAppsCatalogPageTransition.Initial && normalized.Count == 0)
            return new GamesAppsCatalogPageResult(
                GamesAppsCatalogState.Empty, EmptyInitial: true, EmptyContinuation: false);
        if (transition != GamesAppsCatalogPageTransition.Initial && normalized.Count == 0)
        {
            var noMore = transition == GamesAppsCatalogPageTransition.Next
                ? current with { After = null }
                : current;
            return new GamesAppsCatalogPageResult(
                noMore, EmptyInitial: false, EmptyContinuation: true);
        }

        var items = normalized.Take(pageSize).ToArray();
        WidgetCollectionCursor? before = page?.Before is { } beforeValue
            ? new WidgetCollectionCursor(beforeValue) : null;
        WidgetCollectionCursor? after = page?.After is { } afterValue
            ? new WidgetCollectionCursor(afterValue) : null;
        return new GamesAppsCatalogPageResult(
            new GamesAppsCatalogState(items, before, after, page?.Revision ?? string.Empty),
            EmptyInitial: false,
            EmptyContinuation: false);
    }

    internal static IReadOnlyList<WidgetAppLibraryItem> Normalize(
        IReadOnlyList<WidgetAppLibraryItem>? items,
        int maximum)
    {
        if (items is null) return [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return items.Where(item => item is not null &&
                !string.IsNullOrWhiteSpace(item.AppId) &&
                !string.IsNullOrWhiteSpace(item.SavedId) &&
                !string.IsNullOrWhiteSpace(item.Presentation.DisplayName) && ids.Add(item.AppId))
            .Take(maximum)
            .Select(item =>
            {
                var displayName = NormalizeDisplayName(item.Presentation.DisplayName);
                return item with
                {
                    Presentation = item.Presentation with { DisplayName = displayName },
                };
            })
            .ToArray();
    }

    private static string NormalizeDisplayName(string value)
    {
        var name = value.Trim();
        return name.Length > 120 ? name[..120] : name;
    }
}
