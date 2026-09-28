using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string IndexedHomeOperation = "playnite-library.indexed-home";
    private readonly WidgetIndexedCollection<PlayniteLibraryHomeContent, PlayniteLibraryHomeItem> _indexedHome;
    private PlayniteLibraryIndexedHomeState HomeState => _model.Value.IndexedHome;
    private void UpdateHomeState(Func<PlayniteLibraryIndexedHomeState, PlayniteLibraryIndexedHomeState> update) =>
        _model.Update(state => state with { IndexedHome = update(state.IndexedHome) });

    private WidgetIndexedCollection<PlayniteLibraryHomeContent, PlayniteLibraryHomeItem> CreateIndexedHome()
    {
        var source = CreateIndexedCollection("playnite-library.home", new PlayniteLibraryHomeContent(
            PlayniteLibraryHomeQuery.Empty), 0, PlayniteLibraryIndexedHome.Options(OnResolveArtworkAsync, HandleIndexedHomeActionAsync));
        _model.Changed += (_, change) =>
        {
            if (change.Previous.Value.OrganizationBusy != change.Current.Value.OrganizationBusy ||
                change.Previous.Value.LaunchingSavedId != change.Current.Value.LaunchingSavedId)
                UpdateIndexedHomePresentation();
        };
        return source;
    }

    private WidgetCursorResourceSnapshot<PlayniteLibraryItem> IndexedHomeStatus
    {
        // Shared page/status adapter only: indexed Home has no cursor or row buffer.
        get { lock (_gate) return new(HomeState.Status, [], null, null, null, null, HomeState.Error, HomeState.Attempt); }
    }

    private WidgetOperationHandle EnsureIndexedHome()
    {
        lock (_gate)
            if (HomeState.Publication is { Query.IsWarmDisplayOnly: false } && IndexedHomeSelectionIsCurrent() || Operations.IsBusy(IndexedHomeOperation))
                return new(WidgetOperationAdmission.Completed, Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Succeeded)));
        return RefreshIndexedHome();
    }

    private bool IndexedHomeSelectionIsCurrent() => HomeState.Selection is { } selection &&
        SameCollectionQuery(selection, _model.Value.Collection);

    private WidgetOperationHandle RefreshIndexedHome()
    {
        PlayniteLibraryCollectionState selection;
        WidgetAppLibraryQuery query;
        PlayniteLibraryQueryContext queryContext;
        long attempt, authorityRevision, authorityGeneration;
        WidgetPagedResourceStatus before;
        lock (_gate)
        {
            before = HomeState.Status;
            attempt = HomeState.Attempt + 1;
            var local = _model.Value;
            selection = local.Collection;
            query = EffectiveQueryLocked(PlayniteLibraryRoute.Library, local);
            queryContext = new(selection.RecentlyPlayed ? PlayniteLibraryQueryScope.RecentlyPlayed : PlayniteLibraryQueryScope.Home);
            RetireCurrentQueryAuthorityLocked(PlayniteLibraryRoute.Library);
            authorityGeneration = _homeQueryAuthorityGeneration;
            authorityRevision = _authorityRevision;
            UpdateHomeState(state => state with { Attempt = attempt, Error = null,
                Status = state.Publication is null ? WidgetPagedResourceStatus.Loading : WidgetPagedResourceStatus.Refreshing });
        }
        Invalidate();
        var operation = Operations.RunLatest(IndexedHomeOperation, async context =>
        {
            var token = context.CancellationToken;
            try
            {
                var captured = await _application.CaptureQueryAsync(query, queryContext, true, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var fixedRows = await CaptureHomeFixedRowsAsync(captured, token).ConfigureAwait(false);
                var display = captured.Games.Take(PlayniteLibraryPrivateState.MaximumItems)
                    .Select(game => new PlayniteLibraryDisplayItem(game.Id, game.Name, game.Source))
                    .Concat(fixedRows.All.Select(item => new PlayniteLibraryDisplayItem(item.Value.SavedId,
                        item.Presentation.DisplayName, item.Presentation.Source.DisplayName))).DistinctBy(item => item.SavedId).ToArray();
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
                    if (attempt != HomeState.Attempt || !context.IsCurrent) return;
                    EnsureQueryAuthorityCurrent(PlayniteLibraryRoute.Library, authorityGeneration, token);
                    _ = TryPublishQueryAuthority(PlayniteLibraryRoute.Library, authorityGeneration, authorityRevision,
                        captured.Authority, captured.RetainedLastGood, token);
                    var projected = new PlayniteLibraryHomeQuery(captured, PresentationOrganizationLocked(), fixedRows, selection.FavoriteFilter);
                    var sameQuery = HomeState.Publication is { } previous &&
                        HomeState.Selection is { } oldSelection && SameCollectionQuery(oldSelection, selection) &&
                        previous.Query.SameMembership(projected);
                    var content = CaptureHomeContent(projected,
                        sameQuery ? HomeState.Publication!.QueryOwner : new object());
                    if (sameQuery) _indexedHome.UpdateContent(content);
                    else _indexedHome.PublishQuery(content, projected.Count);
                    _model.Update(state => state with
                    {
                        IndexedHome = state.IndexedHome with { Publication = content, Selection = selection,
                            Status = WidgetPagedResourceStatus.Ready,
                            ModalReturn = sameQuery ? state.IndexedHome.ModalReturn : null },
                        HomeGameCount = new(authorityGeneration, projected.Count),
                        SourceObservations = PlayniteLibrarySourceCatalog.RetainObservations(state.SourceObservations, captured.Sources),
                        FixedRows = fixedRows, FixedRowsRevision = state.FixedRowsRevision + 1,
                        Status = !persistence.Saved ? "Games loaded · organization was not saved"
                            : projected.Count == 0 ? "No games" : "Games loaded",
                    });
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || !context.IsCurrent)
            {
                lock (_gate)
                    if (attempt == HomeState.Attempt)
                        UpdateHomeState(state => state with { Status = state.Publication is null || !IndexedHomeSelectionIsCurrent()
                            ? WidgetPagedResourceStatus.NotLoaded : WidgetPagedResourceStatus.Ready });
            }
            catch (Exception error)
            {
                lock (_gate)
                    if (attempt == HomeState.Attempt && context.IsCurrent)
                    {
                        var retirePriorQuery = HomeState.Publication is not null && !IndexedHomeSelectionIsCurrent();
                        if (retirePriorQuery)
                            _indexedHome.PublishQuery(new(PlayniteLibraryHomeQuery.Empty), 0);
                        _model.Update(state => state with {
                            IndexedHome = state.IndexedHome with
                            {
                                Status = WidgetPagedResourceStatus.Error, Error = MapError(error),
                                Publication = retirePriorQuery ? null : state.IndexedHome.Publication,
                                ModalReturn = retirePriorQuery ? null : state.IndexedHome.ModalReturn,
                            } });
                    }
            }
            finally { Invalidate(); }
        }, WidgetOperationLifetime.Active);
        if (!operation.IsAccepted)
        {
            lock (_gate) if (attempt == HomeState.Attempt) UpdateHomeState(state => state with { Status = before });
            Invalidate();
        }
        return operation;
    }

    private PlayniteLibraryHomeContent CaptureHomeContent(PlayniteLibraryHomeQuery query, object owner)
    {
        var local = _model.Value;
        return new(query, !query.IsWarmDisplayOnly && !local.OrganizationBusy,
            local.LaunchingSavedId, _launchStates, owner);
    }

    private void UpdateIndexedHomePresentation()
    {
        lock (_gate)
        {
            if (HomeState.Publication is not { } previous) return;
            var next = CaptureHomeContent(previous.Query, previous.QueryOwner);
            if (next.ActionsEnabled == previous.ActionsEnabled && next.LaunchingSavedId == previous.LaunchingSavedId &&
                next.LaunchStates.Count == previous.LaunchStates.Count &&
                next.LaunchStates.All(pair => previous.LaunchStates.TryGetValue(pair.Key, out var state) && state == pair.Value)) return;
            _indexedHome.UpdateContent(next);
            UpdateHomeState(state => state with { Publication = next });
        }
    }

    private ValueTask HandleIndexedHomeActionAsync(PlayniteLibraryHomeContent content,
        PlayniteLibraryHomeItem item, WidgetActionEvent action, CancellationToken token)
    {
        var route = _navigation.Value;
        if (item.Row.Current is not { } current) return ValueTask.CompletedTask;
        var target = new PlayniteLibraryGameTarget(current, () =>
        {
            lock (_gate) return !route.RouteCancellationToken.IsCancellationRequested &&
                _navigation.Value.Route == PlayniteLibraryRoute.Library &&
                ReferenceEquals(HomeState.Publication?.QueryOwner, content.QueryOwner);
        });
        if (action.ActionId == PlayniteLibraryActions.DetailsOpen && target.IsCurrent)
            lock (_gate) UpdateHomeState(state => state with { ModalReturn = action.FocusedCollectionItem, ModalReturnRequestId = 0 });
        return HandleCapturedGameActionAsync(target, action, token);
    }

    private PlayniteLibraryGameTarget? CaptureIndexedHomeTarget(string sourceElementId)
    {
        const string prefix = "playnite-library.item.grid.";
        if (!sourceElementId.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var route = _navigation.Value;
        lock (_gate)
        {
            if (HomeState.Publication is not { } content ||
                content.Query.Find(new(sourceElementId[prefix.Length..])) is not { } row) return null;
            return row.Row.Current is not { } current ? null : new(current, () => !route.RouteCancellationToken.IsCancellationRequested &&
                ReferenceEquals(HomeState.Publication?.QueryOwner, content.QueryOwner));
        }
    }

    private void ReprojectIndexedHomeOrganization()
    {
        lock (_gate)
        {
            if (HomeState.Publication is not { } previous) return;
            var query = new PlayniteLibraryHomeQuery(previous.Query.Source, PresentationOrganizationLocked(),
                previous.Query.FixedRows, HomeState.Selection?.FavoriteFilter == true, previous.Query.IsWarmDisplayOnly);
            if (previous.Query.SameProjection(query)) return;
            var same = previous.Query.SameMembership(query);
            var next = CaptureHomeContent(query, same ? previous.QueryOwner : new object());
            if (same) _indexedHome.UpdateContent(next);
            else _indexedHome.PublishQuery(next, query.Count);
            UpdateHomeState(state => state with { Publication = next, ModalReturn = same ? state.ModalReturn : null });
        }
    }

    private void RetireIndexedHome()
    {
        Operations.Cancel(IndexedHomeOperation);
        lock (_gate)
        {
            _indexedHome.PublishQuery(new(PlayniteLibraryHomeQuery.Empty), 0);
            UpdateHomeState(state => new() { Attempt = state.Attempt + 1 });
        }
    }

    private async Task<PlayniteLibraryFixedRows> CaptureHomeFixedRowsAsync(PlayniteLibraryCapturedQuery captured, CancellationToken cancellationToken)
    {
        var query = captured.Query;
        PlayniteLibraryPrivateState organization;
        lock (_gate) organization = PresentationOrganizationLocked(captured.Authority);
        var titleMatchIds = PlayniteLibraryTitlePolicy.SearchMatches(
            organization, query.SearchText);
        var fixedSavedIds = titleMatchIds
            .Concat(organization.ManualSavedIds)
            .Distinct(StringComparer.Ordinal)
            .Take(WidgetAppLibraryService.MaximumSavedItems)
            .ToArray();
        IReadOnlyList<WidgetAppLibraryItem> resolved = fixedSavedIds.Length == 0
            ? Array.Empty<WidgetAppLibraryItem>()
            : await _application.ResolveSavedAsync(
                fixedSavedIds, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedBySavedId = resolved.ToDictionary(
            item => item.SavedId, StringComparer.Ordinal);
        var automaticManualGames = organization.ManualSavedIds
            .Where(savedId => resolvedBySavedId.TryGetValue(savedId, out var item) &&
                item.Presentation.Kind == WidgetAppLibraryKind.Game)
            .ToHashSet(StringComparer.Ordinal);
        if (automaticManualGames.Count != 0)
        {
            await SaveStateAsync(
                state => PlayniteLibraryOrganizationPolicy.RemoveAutomaticManualGames(
                    state, automaticManualGames), cancellationToken).ConfigureAwait(false);
            lock (_gate) organization = PresentationOrganizationLocked(captured.Authority);
        }

        var manual = organization.ManualSavedIds.Select(id => resolvedBySavedId.GetValueOrDefault(id))
            .OfType<WidgetAppLibraryItem>().Where(item => item.Presentation.Kind != WidgetAppLibraryKind.Game &&
                MatchesFixedQuery(PlayniteLibraryTitlePolicy.Project(organization, item), query))
            .Take(PlayniteLibraryPrivateState.MaximumManualItems).Select(PlayniteLibraryItem.From).ToArray();
        var occupied = manual.Select(item => item.Value.SavedId).ToHashSet(StringComparer.Ordinal);
        var matches = titleMatchIds.Where(id => !occupied.Contains(id)).Select(id => resolvedBySavedId.GetValueOrDefault(id))
            .OfType<WidgetAppLibraryItem>().Where(item => MatchesFixedQuery(PlayniteLibraryTitlePolicy.Project(organization, item), query))
            .Select(PlayniteLibraryItem.From).ToArray();
        return new([], manual, matches);
    }
}
