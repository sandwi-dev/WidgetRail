using System.Collections.ObjectModel;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

/// <summary>
/// Immutable render inputs for one exact Home query. Content updates may reuse
/// its logical identity only when membership and ordering remain unchanged.
/// </summary>
internal sealed class PlayniteLibraryHomeContent
{
    internal PlayniteLibraryHomeContent(PlayniteLibraryHomeQuery query,
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

    internal PlayniteLibraryHomeQuery Query { get; }
    internal object QueryOwner { get; }
    internal bool ActionsEnabled { get; }
    internal string? LaunchingSavedId { get; }
    internal IReadOnlyDictionary<string, PlayniteLibraryLaunchState> LaunchStates { get; }
}

/// <summary>
/// Indexed Home declaration using the existing poster and game-options design.
/// The owner handles captured actions directly; it must never resolve them by
/// looking up SourceElementId in the currently retained cursor window.
/// </summary>
internal static class PlayniteLibraryIndexedHome
{
    internal static WidgetIndexedCollectionOptions<PlayniteLibraryHomeContent, PlayniteLibraryHomeItem> Options(
        Func<WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>> fixedArtwork,
        Func<PlayniteLibraryHomeContent, PlayniteLibraryHomeItem, WidgetActionEvent,
            CancellationToken, ValueTask> onAction) => new()
    {
        ReadRange = (content, start, count, token) =>
            ValueTask.FromResult(content.Query.ReadRange(start, count, token)),
        ItemKey = item => item.Key,
        RenderItem = (content, item, _) => RenderItem(content, item),
        OnAction = onAction ?? throw new ArgumentNullException(nameof(onAction)),
        ResolveArtwork = (content, item, handle, token) => content.Query.ResolveArtworkAsync(item, handle, fixedArtwork, token),
    };

    internal static WidgetElement RenderItem(PlayniteLibraryHomeContent content, PlayniteLibraryHomeItem item) => PlayniteLibraryPresentation.RenderTile(new(
            item.Row.Display.DisplayName, item.Row.Display.SourceAttribution, item.Row.Display.SavedId,
            item.Row.Current?.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile)?.Handle,
            string.Equals(content.LaunchingSavedId, item.Row.Display.SavedId, StringComparison.Ordinal),
            content.LaunchStates.TryGetValue(item.Row.Display.SavedId, out var launch) ? launch : null,
            item.Row.Current, item.Row.Favorite, content.ActionsEnabled, item.Key,
            CollectionItem: item.Row.CollectionItem, content.Query.Categories, CompletionStatus: null,
            FocusSummaryContext: true, BrowseLayout: false));

    internal static WidgetElement Rail(
        WidgetIndexedCollection<PlayniteLibraryHomeContent, PlayniteLibraryHomeItem> source) =>
        UI.CollectionList(PlayniteLibraryPresentation.HomeRailId, source, 212, "Games", ScrollAxis.Horizontal)
            .Classes("playnite-library-rail");
}
