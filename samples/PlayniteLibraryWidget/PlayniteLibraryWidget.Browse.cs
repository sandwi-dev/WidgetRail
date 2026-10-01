using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string IndexedBrowseOperation = "playnite-library.indexed-browse";
    private readonly WidgetIndexedCollection<PlayniteLibraryBrowseContent, PlayniteLibraryBrowseItem> _indexedBrowse;
    private PlayniteLibraryIndexedBrowseState BrowseState => _model.Value.IndexedBrowse;
    private void UpdateBrowseState(Func<PlayniteLibraryIndexedBrowseState, PlayniteLibraryIndexedBrowseState> update) =>
        _model.Update(state => state with { IndexedBrowse = update(state.IndexedBrowse) });

    private WidgetIndexedCollection<PlayniteLibraryBrowseContent, PlayniteLibraryBrowseItem> CreateIndexedBrowse()
    {
        var source = CreateIndexedCollection("playnite-library.browse", new PlayniteLibraryBrowseContent(
            PlayniteLibraryBrowseQuery.Empty), 0, PlayniteLibraryIndexedBrowse.Options(HandleIndexedBrowseActionAsync));
        _model.Changed += (_, change) =>
        {
            if (change.Previous.Value.OrganizationBusy != change.Current.Value.OrganizationBusy ||
                change.Previous.Value.LaunchingSavedId != change.Current.Value.LaunchingSavedId ||
                change.Previous.Value.ActiveBrowseReload != change.Current.Value.ActiveBrowseReload)
                UpdateIndexedBrowsePresentation();
        };
        return source;
    }

    private WidgetCursorResourceSnapshot<PlayniteLibraryItem> IndexedBrowseStatus
    {
        // Shared page/status adapter only: indexed Browse has no cursor or row buffer.
        get { lock (_gate) return new(BrowseState.Status, [], null, null, null, null, BrowseState.Error, BrowseState.Attempt); }
    }

    private WidgetOperationHandle EnsureIndexedBrowse()
    {
        lock (_gate)
            if (BrowseState.Publication is not null && IndexedBrowseSelectionIsCurrent() || Operations.IsBusy(IndexedBrowseOperation))
                return new(WidgetOperationAdmission.Completed, Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Succeeded)));
        return RefreshIndexedBrowse();
    }

    private bool IndexedBrowseSelectionIsCurrent() => BrowseState.Selection is { } selection &&
        SameCollectionQuery(selection, _model.Value.BrowseCollection) && BrowseState.Category == _model.Value.ActiveCategoryId;

    private WidgetOperationHandle RefreshIndexedBrowse()
    {
        PlayniteLibraryCollectionState selection;
        WidgetAppLibraryQuery query;
        PlayniteLibraryQueryContext queryContext;
        string? categoryId;
        long attempt, authorityRevision, authorityGeneration;
        WidgetPagedResourceStatus before;
        lock (_gate)
        {
            before = BrowseState.Status;
            attempt = BrowseState.Attempt + 1;
            var local = _model.Value;
            selection = local.BrowseCollection;
            categoryId = local.ActiveCategoryId;
            query = EffectiveQueryLocked(PlayniteLibraryRoute.Browse, local);
            var categoryName = categoryId is null ? null :
                PlayniteLibraryCategoryPolicy.Find(PresentationOrganizationLocked(), categoryId)?.Name;
            queryContext = new(selection.RecentlyPlayed ? PlayniteLibraryQueryScope.RecentlyPlayed :
                categoryId is not null ? PlayniteLibraryQueryScope.Category : PlayniteLibraryQueryScope.Library, categoryName);
            RetireCurrentQueryAuthorityLocked(PlayniteLibraryRoute.Browse);
            authorityGeneration = _browseQueryAuthorityGeneration;
            authorityRevision = _authorityRevision;
            UpdateBrowseState(state => state with { Attempt = attempt, Error = null,
                Status = state.Publication is null ? WidgetPagedResourceStatus.Loading : WidgetPagedResourceStatus.Refreshing });
        }
        Invalidate();
        var operation = Operations.RunLatest(IndexedBrowseOperation, async context =>
        {
            var token = context.CancellationToken;
            try
            {
                var captured = await _application.CaptureQueryAsync(query, queryContext, true, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var display = captured.Games.Take(PlayniteLibraryPrivateState.MaximumItems)
                    .Select(game => new PlayniteLibraryDisplayItem(game.Id, game.Name, game.Source)).ToArray();
                var sources = captured.Games.Select(game => game.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var persistence = await SaveStateAsync(state => PlayniteLibraryStateMutation.Apply(
                    PlayniteLibraryOrganizationPolicy.ProjectDisplay(state, display) with
                    {
                        ProvenSources = PlayniteLibrarySourceCatalog.ReconcileSources(state.ProvenSources,
                            selection, null, sources, captured.Sources, completeCatalog: true),
                    }), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (attempt != BrowseState.Attempt || !context.IsCurrent) return;
                    EnsureQueryAuthorityCurrent(PlayniteLibraryRoute.Browse, authorityGeneration, token);
                    _ = TryPublishQueryAuthority(PlayniteLibraryRoute.Browse, authorityGeneration, authorityRevision,
                        captured.Authority, captured.RetainedLastGood, token);
                    var projected = new PlayniteLibraryBrowseQuery(captured, PresentationOrganizationLocked(), selection.FavoriteFilter);
                    var sameQuery = BrowseState.Publication is { } previous &&
                        BrowseState.Selection is { } oldSelection && SameCollectionQuery(oldSelection, selection) &&
                        BrowseState.Category == categoryId && previous.Query.SameMembership(projected);
                    var content = CaptureBrowseContent(projected,
                        sameQuery ? BrowseState.Publication!.QueryOwner : new object());
                    if (sameQuery) _indexedBrowse.UpdateContent(content);
                    else _indexedBrowse.PublishQuery(content, projected.Count);
                    _model.Update(state => state with
                    {
                        IndexedBrowse = state.IndexedBrowse with { Publication = content, Selection = selection,
                            Category = categoryId, Status = WidgetPagedResourceStatus.Ready,
                            ModalReturn = sameQuery ? state.IndexedBrowse.ModalReturn : null },
                        BrowseGameCount = new(authorityGeneration, projected.Count),
                        SourceObservations = PlayniteLibrarySourceCatalog.RetainObservations(state.SourceObservations, captured.Sources),
                        ActiveBrowseReload = null,
                        Status = !persistence.Saved ? "Games loaded · organization was not saved"
                            : projected.Count == 0 ? "No games" : "Games loaded",
                    });
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || !context.IsCurrent)
            {
                lock (_gate)
                    if (attempt == BrowseState.Attempt)
                        UpdateBrowseState(state => state with { Status = state.Publication is null || !IndexedBrowseSelectionIsCurrent()
                            ? WidgetPagedResourceStatus.NotLoaded : WidgetPagedResourceStatus.Ready });
            }
            catch (Exception error)
            {
                lock (_gate)
                    if (attempt == BrowseState.Attempt && context.IsCurrent)
                    {
                        var retirePriorQuery = BrowseState.Publication is not null && !IndexedBrowseSelectionIsCurrent();
                        if (retirePriorQuery)
                            _indexedBrowse.PublishQuery(new(PlayniteLibraryBrowseQuery.Empty), 0);
                        _model.Update(state => state with { ActiveBrowseReload = null,
                            IndexedBrowse = state.IndexedBrowse with
                            {
                                Status = WidgetPagedResourceStatus.Error, Error = MapError(error),
                                Publication = retirePriorQuery ? null : state.IndexedBrowse.Publication,
                                ModalReturn = retirePriorQuery ? null : state.IndexedBrowse.ModalReturn,
                            } });
                    }
            }
            finally { Invalidate(); }
        }, WidgetOperationLifetime.Active);
        if (!operation.IsAccepted)
        {
            lock (_gate) if (attempt == BrowseState.Attempt) UpdateBrowseState(state => state with { Status = before });
            Invalidate();
        }
        return operation;
    }

    private PlayniteLibraryBrowseContent CaptureBrowseContent(PlayniteLibraryBrowseQuery query, object owner)
    {
        var local = _model.Value;
        return new(query, !local.OrganizationBusy && local.ActiveBrowseReload is null,
            local.LaunchingSavedId, _launchStates, owner);
    }

    private void UpdateIndexedBrowsePresentation()
    {
        lock (_gate)
        {
            if (BrowseState.Publication is not { } previous) return;
            var next = CaptureBrowseContent(previous.Query, previous.QueryOwner);
            if (next.ActionsEnabled == previous.ActionsEnabled && next.LaunchingSavedId == previous.LaunchingSavedId &&
                next.LaunchStates.Count == previous.LaunchStates.Count &&
                next.LaunchStates.All(pair => previous.LaunchStates.TryGetValue(pair.Key, out var state) && state == pair.Value)) return;
            _indexedBrowse.UpdateContent(next);
            UpdateBrowseState(state => state with { Publication = next });
        }
    }

    private ValueTask HandleIndexedBrowseActionAsync(PlayniteLibraryBrowseContent content,
        PlayniteLibraryBrowseItem item, WidgetActionEvent action, CancellationToken token)
    {
        var route = _navigation.Value;
        var target = new PlayniteLibraryGameTarget(item.Item, () =>
        {
            lock (_gate) return !route.RouteCancellationToken.IsCancellationRequested &&
                _navigation.Value.Route == PlayniteLibraryRoute.Browse &&
                ReferenceEquals(BrowseState.Publication?.QueryOwner, content.QueryOwner);
        });
        if (action.ActionId == PlayniteLibraryActions.DetailsOpen && target.IsCurrent)
            lock (_gate) UpdateBrowseState(state => state with { ModalReturn = action.FocusedCollectionItem, ModalReturnRequestId = 0 });
        return HandleCapturedGameActionAsync(target, action, token);
    }

    private PlayniteLibraryGameTarget? CaptureIndexedBrowseTarget(string sourceElementId)
    {
        const string prefix = "playnite-library.item.grid.";
        if (!sourceElementId.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var route = _navigation.Value;
        lock (_gate)
        {
            if (BrowseState.Publication is not { } content ||
                content.Query.Find(new(sourceElementId[prefix.Length..])) is not { } row) return null;
            return new(row.Item, () => !route.RouteCancellationToken.IsCancellationRequested &&
                ReferenceEquals(BrowseState.Publication?.QueryOwner, content.QueryOwner));
        }
    }

    private void ReprojectIndexedBrowseOrganization()
    {
        lock (_gate)
        {
            if (BrowseState.Publication is not { } previous) return;
            var query = new PlayniteLibraryBrowseQuery(previous.Query.Source, PresentationOrganizationLocked(),
                BrowseState.Selection?.FavoriteFilter == true);
            if (previous.Query.SameProjection(query)) return;
            var same = previous.Query.SameMembership(query);
            var next = CaptureBrowseContent(query, same ? previous.QueryOwner : new object());
            if (same) _indexedBrowse.UpdateContent(next);
            else _indexedBrowse.PublishQuery(next, query.Count);
            UpdateBrowseState(state => state with { Publication = next, ModalReturn = same ? state.ModalReturn : null });
        }
    }

    private void RetireIndexedBrowse()
    {
        Operations.Cancel(IndexedBrowseOperation);
        lock (_gate)
        {
            UpdateBrowseState(state => new() { Attempt = state.Attempt + 1 });
        }
    }
}
