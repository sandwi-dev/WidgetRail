using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.GamesApps;

public sealed partial class GamesAppsWidget
{
    // The broker returns finite observations. Their actual counts are authoritative;
    // provider cursor paging remains isolated to the existing Catalog page controls.
    private sealed record AppQuery(GamesAppsPage Page, long Generation, string? RunningRevision,
        GamesAppsPresentationState? State, WidgetAppLibraryItem[] Items);
    private sealed record AppActionOrigin(GamesAppsPage Page, long Generation, string? RunningRevision, string SavedId, string AppId);
    private readonly WidgetIndexedCollection<AppQuery, WidgetAppLibraryItem> _indexedLibrary;
    private readonly WidgetIndexedCollection<AppQuery, WidgetAppLibraryItem> _indexedRunning;
    private AppQuery? _libraryQuery;
    private AppQuery? _runningQuery;
    private long _viewFocusSequence;
    private long _lastFocusNavigationRevision = -1;
    private FocusGroupEntryRequest? _viewFocusRequest;
    private IndexedCollectionFocusTarget? _requestedItemFocus;

    private WidgetIndexedCollection<AppQuery, WidgetAppLibraryItem> CreateAppCollection(string id, GamesAppsPage page) =>
        CreateIndexedCollection<AppQuery, WidgetAppLibraryItem>(id, new(page, 0, null, null, []), 0, new()
        {
            ReadRange = (query, start, count, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<WidgetAppLibraryItem>>(query.Items.AsSpan(start, count).ToArray());
            },
            // Saved IDs are opaque broker values, not necessarily SDK identifiers.
            ItemKey = item => new(GamesAppsPresentation.LibraryElementId(item.SavedId)),
            RenderItem = (query, item, _) => query.Page == GamesAppsPage.Library
                ? GamesAppsPresentation.LibraryTile(query.State!, item)
                : GamesAppsPresentation.CatalogTile(query.State!, item, running: true,
                    saved: query.State!.LibrarySavedIds.Contains(item.SavedId, StringComparer.Ordinal)),
            OnAction = HandleCapturedAppActionAsync,
            // Broker-owned AppLibrary icons deliberately have no widget resolver.
            // The bridge validates exact lease/row and current broker icon identity.
        });

    // Called with _gate held. Query values contain copied arrays and render state;
    // range reads/rendering never consult mutable widget state.
    private GamesAppsPresentationState PrepareIndexedPresentation(GamesAppsPresentationState state)
    {
        var page = state.Navigation.RootRoute;
        if (state.ViewState != GamesAppsViewState.Ready || page == GamesAppsPage.Catalog) return state;
        var bySavedId = state.Items.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var items = page == GamesAppsPage.Library
            ? state.LibrarySavedIds.Where(bySavedId.ContainsKey).Select(saved => bySavedId[saved]).ToArray()
            : state.Items.ToArray();
        var source = page == GamesAppsPage.Library ? _indexedLibrary : _indexedRunning;
        var previous = page == GamesAppsPage.Library ? _libraryQuery : _runningQuery;
        var next = new AppQuery(page, _generation, page == GamesAppsPage.Running ? _runningRevision : null, state, items);
        var membershipChanged = previous is null || !previous.Items.Select(item => item.SavedId).SequenceEqual(items.Select(item => item.SavedId));
        var contentChanged = membershipChanged || previous!.Generation != next.Generation || previous.RunningRevision != next.RunningRevision ||
            !previous.Items.SequenceEqual(items) || !SameRowState(previous.State!, state);
        if (page == GamesAppsPage.Library) _libraryQuery = next; else _runningQuery = next;
        if (contentChanged)
        {
            if (membershipChanged) source.PublishQuery(next, items.Length); else source.UpdateContent(next);
        }
        var scrollId = page == GamesAppsPage.Library ? "games.library.scroll" : "games.running.scroll";
        if (page == GamesAppsPage.Library && state.LifecycleState == WidgetLifecycleState.Interactive &&
            (previous is not null || state.Navigation.FocusGroupEntryRequest is not null) &&
            previous?.State?.SelectedAppId != state.SelectedAppId &&
            Array.FindIndex(items, item => item.AppId == state.SelectedAppId) is var selected && selected >= 0)
            _requestedItemFocus = source.FocusTarget(scrollId,
                new(GamesAppsPresentation.LibraryElementId(items[selected].SavedId)), selected);
        return state with
        {
            Collection = UI.CollectionGrid(scrollId, source, 240, 90,
                page == GamesAppsPage.Library ? "Saved games and apps" : "Running applications", 3)
                .Classes("games-page-scroll", page == GamesAppsPage.Library ? "games-library-scroll" : "games-running-scroll", "games-collection-scroll"),
        };
    }

    private FocusGroupEntryRequest? ResolveIndexedFocusRequest(WidgetNavigationSnapshot<GamesAppsPage> navigation,
        GamesAppsPresentationState state)
    {
        if (_lastFocusNavigationRevision != navigation.Revision)
        {
            _lastFocusNavigationRevision = navigation.Revision;
            _viewFocusRequest = navigation.FocusGroupEntryRequest is { } entry
                ? entry with { RequestId = ++_viewFocusSequence } : null;
        }
        if (_viewFocusRequest?.IndexedItem is { } old &&
            (state.Collection?.Source.SourceId != old.SourceId || state.Collection.Source.QueryGeneration != old.QueryGeneration))
        {
            // Keep the same one-shot intent/key valid as its query reorders. A
            // host that already consumed its ID must not execute it a second time.
            var index = _libraryQuery is { } query ? Array.FindIndex(query.Items,
                item => GamesAppsPresentation.LibraryElementId(item.SavedId) == old.ItemKey) : -1;
            _viewFocusRequest = index >= 0 && state.Collection?.Source.SourceId == old.SourceId
                ? _viewFocusRequest with { IndexedItem = _indexedLibrary.FocusTarget(old.CollectionId, new(old.ItemKey), index) }
                : null;
        }
        if (_requestedItemFocus is { } target && state.Collection?.Source.QueryGeneration == target.QueryGeneration &&
            state.Collection.Source.SourceId == target.SourceId)
            _viewFocusRequest = _indexedLibrary.Enter(target.CollectionId, ++_viewFocusSequence, target);
        _requestedItemFocus = null;
        return _viewFocusRequest;
    }

    private static bool SameRowState(GamesAppsPresentationState left, GamesAppsPresentationState right) =>
        left.LifecycleState == right.LifecycleState && left.LaunchingAppId == right.LaunchingAppId &&
        left.LibraryMutationBusy == right.LibraryMutationBusy && left.LoadingMore == right.LoadingMore &&
        left.LibrarySavedIds.SequenceEqual(right.LibrarySavedIds) && left.ResolvedSavedIds.SetEquals(right.ResolvedSavedIds);

    private async ValueTask HandleCapturedAppActionAsync(AppQuery query, WidgetAppLibraryItem item,
        WidgetActionEvent action, CancellationToken cancellationToken)
    {
        var origin = new AppActionOrigin(query.Page, query.Generation, query.RunningRevision, item.SavedId, item.AppId);
        lock (_gate) if (!IsAppActionCurrentLocked(origin)) return;
        switch (query.Page, action.ActionId)
        {
            case (GamesAppsPage.Library, "games.launch") when query.State!.ResolvedSavedIds.Contains(item.SavedId):
                await LaunchAsync(item.AppId, cancellationToken, origin).ConfigureAwait(false);
                break;
            case (GamesAppsPage.Library, "games.remove"):
                await RemoveCuratedAsync(item.AppId, cancellationToken, origin).ConfigureAwait(false);
                break;
            case (GamesAppsPage.Running, "games.toggle-curation"):
                await AddRunningAsync(item.SavedId, cancellationToken, origin).ConfigureAwait(false);
                break;
        }
    }
    // Rechecked inside each command's own mutation lock after its semaphore
    // admission. The renderer callback is not permission to reinterpret a
    // reused position or a replacement running observation.
    private bool IsAppActionCurrentLocked(AppActionOrigin? origin) => origin is null ||
        LifecycleState == WidgetLifecycleState.Interactive && Page == origin.Page && _generation == origin.Generation &&
        _items.Any(current => current.SavedId == origin.SavedId && current.AppId == origin.AppId) &&
        (origin.Page != GamesAppsPage.Running || _runningRevision == origin.RunningRevision);

}
