using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GamesApps;

internal enum GamesAppsCatalogPageTransition
{
    Initial,
    Next,
    Previous,
}

internal sealed record GamesAppsCatalogState(
    IReadOnlyList<WidgetAppLibraryItem> Items,
    int Offset,
    int? NextOffset,
    IReadOnlyList<int> BackOffsets)
{
    internal static GamesAppsCatalogState Empty { get; } = new([], 0, null, []);
    internal bool CanLoadPrevious => BackOffsets.Count != 0;
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
        int requestedOffset,
        int pageSize,
        int maximumItems)
    {
        ArgumentNullException.ThrowIfNull(current);
        var normalized = Normalize(page?.Items, pageSize);
        if (transition == GamesAppsCatalogPageTransition.Initial && normalized.Count == 0)
            return new GamesAppsCatalogPageResult(
                GamesAppsCatalogState.Empty, EmptyInitial: true, EmptyContinuation: false);
        if (transition != GamesAppsCatalogPageTransition.Initial && normalized.Count == 0)
        {
            var noMore = transition == GamesAppsCatalogPageTransition.Next
                ? current with { NextOffset = null }
                : current;
            return new GamesAppsCatalogPageResult(
                noMore, EmptyInitial: false, EmptyContinuation: true);
        }

        IReadOnlyList<int> backOffsets;
        if (transition == GamesAppsCatalogPageTransition.Initial)
        {
            backOffsets = [];
        }
        else if (transition == GamesAppsCatalogPageTransition.Next)
        {
            backOffsets = current.BackOffsets.Concat([current.Offset]).ToArray();
        }
        else if (current.BackOffsets.Count != 0 &&
            current.BackOffsets[^1] == requestedOffset)
        {
            backOffsets = current.BackOffsets.Take(current.BackOffsets.Count - 1).ToArray();
        }
        else
        {
            backOffsets = current.BackOffsets;
        }

        var items = normalized.Take(pageSize).ToArray();
        int? nextOffset = requestedOffset + items.Length < maximumItems &&
            page?.NextOffset is int next && next > requestedOffset && next <= maximumItems
                ? next
                : null;
        return new GamesAppsCatalogPageResult(
            new GamesAppsCatalogState(items, requestedOffset, nextOffset, backOffsets),
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
                !string.IsNullOrWhiteSpace(item.DisplayName) && ids.Add(item.AppId))
            .Take(maximum)
            .Select(item => item with { DisplayName = NormalizeDisplayName(item.DisplayName) })
            .ToArray();
    }

    private static string NormalizeDisplayName(string value)
    {
        var name = value.Trim();
        return name.Length > 120 ? name[..120] : name;
    }
}
