using System.Collections.ObjectModel;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

/// <summary>
/// Immutable render inputs for one exact Browse query. Content updates may reuse
/// its logical identity only when membership and ordering remain unchanged.
/// </summary>
internal sealed class PlayniteLibraryBrowseContent
{
    internal PlayniteLibraryBrowseContent(PlayniteLibraryBrowseQuery query,
        bool actionsEnabled = true, string? launchingSavedId = null,
        IReadOnlyDictionary<string, PlayniteLibraryLaunchState>? launchStates = null,
        object? queryOwner = null)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        QueryOwner = queryOwner ?? query;
        ActionsEnabled = actionsEnabled;
        LaunchingSavedId = launchingSavedId;
        LaunchStates = new ReadOnlyDictionary<string, PlayniteLibraryLaunchState>(
            launchStates is null ? new Dictionary<string, PlayniteLibraryLaunchState>(StringComparer.Ordinal)
                : new Dictionary<string, PlayniteLibraryLaunchState>(launchStates, StringComparer.Ordinal));
    }

    internal PlayniteLibraryBrowseQuery Query { get; }
    internal object QueryOwner { get; }
    internal bool ActionsEnabled { get; }
    internal string? LaunchingSavedId { get; }
    internal IReadOnlyDictionary<string, PlayniteLibraryLaunchState> LaunchStates { get; }
}

/// <summary>
/// Indexed Browse declaration using the existing poster and game-options design.
/// The owner handles captured actions directly; it must never resolve them by
/// looking up SourceElementId in the currently retained cursor window.
/// </summary>
internal static class PlayniteLibraryIndexedBrowse
{
    internal static WidgetIndexedCollectionOptions<PlayniteLibraryBrowseContent, PlayniteLibraryBrowseItem> Options(
        Func<PlayniteLibraryBrowseContent, PlayniteLibraryBrowseItem, WidgetActionEvent,
            CancellationToken, ValueTask> onAction) => new()
    {
        ReadRange = (content, start, count, token) =>
            ValueTask.FromResult(content.Query.ReadRange(start, count, token)),
        ItemKey = item => item.Key,
        RenderItem = (content, item, _) => PlayniteLibraryPresentation.RenderTile(new(
            item.Row.Display.DisplayName, item.Row.Display.SourceAttribution, item.Row.Display.SavedId,
            item.Item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile)?.Handle,
            string.Equals(content.LaunchingSavedId, item.Row.Display.SavedId, StringComparison.Ordinal),
            content.LaunchStates.TryGetValue(item.Row.Display.SavedId, out var launch) ? launch : null,
            item.Item, item.Row.Favorite, content.ActionsEnabled, item.Key,
            CollectionItem: true, content.Query.Categories, CompletionStatus: null,
            FocusSummaryContext: false, BrowseLayout: true)),
        OnAction = onAction ?? throw new ArgumentNullException(nameof(onAction)),
        ResolveArtwork = (content, item, handle, token) => content.Query.ResolveArtworkAsync(item, handle, token),
    };

    internal static WidgetElement Grid(
        WidgetIndexedCollection<PlayniteLibraryBrowseContent, PlayniteLibraryBrowseItem> source) =>
        UI.CollectionGrid(PlayniteLibraryPresentation.ScrollId, source, 150, 225,
                "Games", maximumColumns: 7)
            .Classes("playnite-library-catalog-scroll", "playnite-library-browse-scroll");
}
