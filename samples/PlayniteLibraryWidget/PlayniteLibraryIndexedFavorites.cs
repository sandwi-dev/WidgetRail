using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteLibraryIndexedFavorites
{
    internal const string Id = "playnite-library.home.favorites";
    internal static WidgetIndexedCollectionOptions<PlayniteLibraryHomeContent, PlayniteLibraryHomeItem> Options(
        Func<WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>> fixedArtwork,
        Func<PlayniteLibraryHomeContent, PlayniteLibraryHomeItem, WidgetActionEvent, CancellationToken, ValueTask> action) => new()
    {
        ReadRange = (content, start, count, token) => ValueTask.FromResult(content.Query.ReadFavorites(start, count, token)),
        ItemKey = item => item.Key,
        ResolveArtwork = (content, item, handle, token) => content.Query.ResolveArtworkAsync(item, handle, fixedArtwork, token),
        ResolveArtworkSource = (content, item, handle, token) => content.Query.ResolveArtworkSourceAsync(item, handle, fixedArtwork, token),
        OnAction = action,
        RenderItem = (content, item, _) => PlayniteLibraryPresentation.FavoriteCard(content, item),
    };

    internal static WidgetElement Row(WidgetIndexedCollection<PlayniteLibraryHomeContent, PlayniteLibraryHomeItem> source) =>
        UI.CollectionGrid(Id, source, 240, 80, "Favorites", maximumColumns: 3)
            .Classes("playnite-library-favorites-row");
}
