namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteLibraryActions
{
    internal const string RefreshSource = "playnite-library.refresh-source";
    internal const string Previous = "playnite-library.previous";
    internal const string Next = "playnite-library.next";
    internal const string Refresh = "playnite-library.refresh";
    internal const string Retry = "playnite-library.retry";
    internal const string Launch = "playnite-library.launch";
    internal const string Favorite = "playnite-library.favorite";
    internal const string Hide = "playnite-library.hide";
    internal const string Restore = "playnite-library.restore";
    internal const string HiddenOpen = "playnite-library.hidden.open";
    internal const string HiddenBack = "playnite-library.hidden.back";
    internal const string SearchCommit = "playnite-library.search.commit";
    internal const string QueryClear = "playnite-library.query.clear";
    internal const string HomeOpen = "playnite-library.home.open";
    internal const string BrowseOpen = "playnite-library.browse.open";
    internal const string FavoritesFilter = "playnite-library.filter.favorites";
    internal const string InstalledFilter = "playnite-library.filter.installed";
    internal const string RecentlyPlayedFilter = "playnite-library.filter.recent";
    internal const string SourceFilter = "playnite-library.filter.source";
    internal const string SortFilter = "playnite-library.filter.sort";
    internal const string CategoryFilter = "playnite-library.filter.category";
    internal const string CategoryAll = "playnite-library.filter.category.option.all";
    internal const string SourceOptionPrefix = "playnite-library.filter.source.option.";
    internal const string SortOptionPrefix = "playnite-library.filter.sort.option.";
    internal const string SourceAll = SourceOptionPrefix + "all";
    internal const string SortDisplayName = SortOptionPrefix + "display-name";
    internal const string SortDisplayNameDescending =
        SortOptionPrefix + "display-name-descending";
    internal const string SortSourceThenDisplayName =
        SortOptionPrefix + "source-then-display-name";
    internal const string CategoriesOpen = "playnite-library.categories.open";
    internal const string CategoriesBack = "playnite-library.categories.back";
    internal const string CategoryCreate = "playnite-library.category.create";
    internal const string CollectionPrevious = "playnite-library.collection.previous";
    internal const string CollectionNext = "playnite-library.collection.next";
    internal const string CategoryMembershipPrefix = "playnite-library.category.";
    internal const string CategoryOpenPrefix = "playnite-library.category.open.";

    internal static string CategoryMembership(string categoryId) =>
        CategoryMembershipPrefix + categoryId;

    internal static string CategoryOpen(string categoryId) => CategoryOpenPrefix + categoryId;

    internal static string SourceOption(string source) =>
        SourceOptionPrefix + PlayniteLibraryIdentity.SourceKey(source);

    internal static bool TryParseCategoryMembership(string actionId, out string categoryId)
    {
        categoryId = string.Empty;
        if (!actionId.StartsWith(CategoryMembershipPrefix, StringComparison.Ordinal) ||
            actionId.StartsWith(CategoryOpenPrefix, StringComparison.Ordinal)) return false;
        var value = actionId[CategoryMembershipPrefix.Length..];
        if (!PlayniteLibraryCategoryPolicy.ValidId(value)) return false;
        categoryId = value;
        return true;
    }

    internal static bool TryParseCategoryOpen(string actionId, out string categoryId)
    {
        categoryId = string.Empty;
        if (!actionId.StartsWith(CategoryOpenPrefix, StringComparison.Ordinal)) return false;
        var value = actionId[CategoryOpenPrefix.Length..];
        if (!PlayniteLibraryCategoryPolicy.ValidId(value)) return false;
        categoryId = value;
        return true;
    }
}
