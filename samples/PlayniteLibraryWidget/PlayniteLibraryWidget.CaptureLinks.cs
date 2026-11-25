using WidgetRail.Playnite;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string OpenCapturePickerWork = RelatedCapturePrefix + ".open-picker";
    private const string RecentSessionLinks = RelatedCapturePrefix + ".recent-sessions";
    private static string SessionLinks(long opening) => RelatedCapturePrefix + ".session-links." + opening;

    private void CatchUpRecentGameCaptures(PlayniteLibraryCapturedQuery catalog)
    {
        if (catalog.RetainedLastGood || catalog.RecentCaptureGames.Count == 0) return;
        var route = _navigation.Value.RouteCancellationToken;
        var state = StateLifetimeToken;
        Operations.RunLatest(RecentSessionLinks, async operation =>
        {
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(route, state, operation.CancellationToken);
            var token = scope.Token;
            bool Current() => !token.IsCancellationRequested && operation.IsCurrent && LifecycleState == WidgetLifecycleState.Interactive &&
                ReferenceEquals(HomeState.Publication?.Query.Source, catalog) && _navigation.Value.Route == PlayniteLibraryRoute.Library;
            if (!Current()) return;
            var access = await HostServices.Permissions.CheckAsync(
                [WidgetCaptureLibraryCapabilities.ReadPermission, WidgetCaptureLibraryCapabilities.ManagePermission], token);
            bool Granted(string id) => access.Any(p => p.CapabilityId == id && p.State == WidgetPermissionState.Granted);
            if (!Granted(WidgetCaptureLibraryCapabilities.ReadPermission) || !Granted(WidgetCaptureLibraryCapabilities.ManagePermission)) return;
            var connection = await _application.GetCaptureIntegrationAsync(token);
            if (connection is null) return;
            var changed = false;
            foreach (var candidate in catalog.RecentCaptureGames)
            {
                if (!Current()) return;
                if (_model.Value.RunningGames?.Contains(candidate.Id) == true ||
                    !Guid.TryParseExact(candidate.Id, "D", out var game) || string.IsNullOrWhiteSpace(candidate.Name)) continue;
                var evidence = await _application.GetCaptureIntegrationAsync(token, candidate.Id);
                if (evidence is null) continue;
                if (evidence.ProfileId != connection.ProfileId || evidence.ConnectionId != connection.ConnectionId) return;
                if (evidence.Sessions.Count == 0) continue;
                var related = new CaptureLibraryQuery { OriginPackageId = PlaynitePackage,
                    Reference = new(CaptureRelatedIdentity.GameKind, CaptureRelatedIdentity.Game(connection.ProfileId, game)) };
                changed |= await LinkSessionCapturesAsync(related, evidence.Sessions, RelatedCaptureLabel(candidate.Name, 160), Current, token);
            }
            if (changed && Current())
            {
                await homeCaptures.WhenIdleAsync(token);
                if (Current()) await homeCaptures.Refresh().Completion.WaitAsync(token);
            }
        });
    }

    private void ReconcileGameCaptures(CaptureQuery query, CaptureLibraryQuery related, IReadOnlyList<CaptureProcessInterval> sessions)
    {
        bool Current() => _model.Value.DetailsItem is not null && _model.Value.DetailsExtras.Tab == PlayniteDetailsTab.Captures &&
            RelatedCaptureKey(WithDetailsResources(_model.Value)) == query;
        if (!Current()) return;
        var generation = _model.Value.DetailsGeneration;
        Operations.RunSingleFlight(SessionLinks(query.Opening),async operation =>
        {
            try
            {
                var changed = await LinkSessionCapturesAsync(related, sessions, query.Name,
                    () => Current() && operation.IsCurrent, operation.CancellationToken);
                if (changed && Current() && operation.IsCurrent)
                    relatedCaptures.EnsureLoaded(query,_navigation.Value.RouteCancellationToken,true);
            }
            catch (WidgetCapabilityException)
            {
                if (Current() && operation.IsCurrent)
                    UpdateDetails(generation, value => value with { CaptureLinkMessage = "Some game captures could not be linked. Refresh to try again." });
            }
        },WidgetOperationLifetime.Active);
    }

    private async Task<bool> LinkSessionCapturesAsync(CaptureLibraryQuery related, IReadOnlyList<CaptureProcessInterval> sessions,
        string name, Func<bool> current, CancellationToken token)
    {
        var filter = UnlinkedSessionCaptures(related, sessions);
        var changed = false;
        while (current())
        {
            var page = await HostServices.CaptureLibrary.QueryAsync(filter, count: 8, cancellationToken: token);
            var progressed = false;
            foreach (var item in page.Items)
            {
                token.ThrowIfCancellationRequested();
                if (!current()) return changed;
                progressed |= await AttachGameAsync(item, related.Reference!.Id, name, token) == CaptureContextSaveOutcome.Saved;
            }
            changed |= progressed;
            if (!progressed || page.TotalCount <= page.Items.Count) break;
        }
        return changed;
    }

    private static CaptureLibraryQuery UnlinkedSessionCaptures(CaptureLibraryQuery related, IReadOnlyList<CaptureProcessInterval> sessions) =>
        related with { SourceIntervals = sessions, ExcludeReference = true, ExcludeDetachedReference = true, AvailableOnly = true };

    private static string GameLinkKey(string captureId, string reference) =>
        "game-link." + CaptureRelatedIdentity.Hash(captureId + "/" + reference);

    private ValueTask<CaptureContextSaveOutcome> AttachGameAsync(CaptureLibraryItem item, string gameReference, string name, CancellationToken token, bool explicitSelection = false)
    {
        var key = GameLinkKey(item.Id, gameReference);
        var subject = new CaptureContextReference(CaptureContextReferenceRole.Subject, CaptureRelatedIdentity.GameKind, gameReference, name)
            { HasRelatedAction = true };
        return HostServices.CaptureLibrary.EnsureSubjectAsync(new(item.Id, item.Revision, key, subject, explicitSelection), token);
    }

    private static string PickAction(RelatedCapturePage page, CaptureLibraryItem item) =>
        RelatedCapturePrefix + ".pick." + CaptureRelatedIdentity.Hash($"{page.Connection}/{page.Reference}/{page.Query.Opening}/{page.Runs?.Revision}/{item.Id}/{item.Revision}");

    private static string UnlinkAction(RelatedCapturePage page, CaptureLibraryItem item) =>
        RelatedCapturePrefix + ".unlink." + CaptureRelatedIdentity.Hash($"{page.Connection}/{page.Reference}/{page.Query.Opening}/{page.Revision}/{item.Id}/{item.Revision}");
    private static string UndoLinkAction(CaptureDestinationChange change) => RelatedCapturePrefix + ".undo." + change.OperationId;

    private bool HandleCaptureLinkChange(string action, PlayniteLibraryRenderState state)
    {
        var restore = action.StartsWith(RelatedCapturePrefix + ".undo.", StringComparison.Ordinal);
        if (!restore && !action.StartsWith(RelatedCapturePrefix + ".unlink.", StringComparison.Ordinal)) return false;
        var query = RelatedCaptureKey(state);
        var page = ResourceFor(query).GetSnapshot(query).Value;
        if (query.PickerOffset is not null && !query.BrowseLinked || query.Achievement is not null ||
            page is not { CanLink: true, Connection: not null, Reference: not null, Revision: not null } || page.Query != query) return true;
        CaptureDestinationChange change;
        if (restore)
        {
            var undo = state.DetailsExtras.RemovedLink;
            if (undo is null || undo.Connection != page.Connection || undo.Reference != page.Reference ||
                UndoLinkAction(undo.Change) != action) return true;
            change = undo.Change with { Undo = true };
        }
        else
        {
            var capture = page.Items.FirstOrDefault(item => item.Available && item.ContextCount > 0 && UnlinkAction(page, item) == action);
            if (capture is null) return true;
            change = new(capture.Id, capture.Revision, PlaynitePackage, CaptureRelatedIdentity.GameKind,
                page.Reference, Guid.NewGuid().ToString("N"), page.Revision);
        }
        const string work = RelatedCapturePrefix + ".link-work";
        Operations.RunSingleFlight(work, async operation =>
        {
            bool Current() => operation.IsCurrent && _model.Value.DetailsItem is not null &&
                _model.Value.DetailsExtras.Tab == PlayniteDetailsTab.Captures && RelatedCaptureKey(WithDetailsResources(_model.Value)) == query;
            try
            {
                var integration = await _application.GetCaptureIntegrationAsync(operation.CancellationToken);
                if (!Current() || integration?.ConnectionId != page.Connection || !Guid.TryParseExact(query.GameId, "D", out var game) ||
                    CaptureRelatedIdentity.Game(integration.ProfileId, game) != page.Reference) return;
                var accepted = await HostServices.CaptureLibrary.ChangeDestinationAsync(change, operation.CancellationToken);
                if (!Current() || !accepted) return;
                var pageEntry = CreateFocusRequest();
                UpdateDetails(state.DetailsGeneration, extras => extras with
                {
                    RemovedLink = restore ? null : new(page.Connection, page.Reference, change),
                    CaptureLinkMessage = restore ? "Capture linked again." : "Game link removed.",
                    CapturePageEntry = pageEntry,
                    CaptureReturnId = restore ? RelatedCapturePrefix + ".item." + change.CaptureId : RelatedCapturePrefix + ".undo",
                });
            }
            catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or
                PlayniteBridgeTransportException or PlayniteBridgeDataException)
            {
                var message = error is WidgetCapabilityException { ErrorCode: "capture_library_query_changed" }
                    ? "The capture library changed. Check the link and try again."
                    : "Could not change this link. Check the capture and try again.";
                if (Current()) UpdateDetails(state.DetailsGeneration, extras => extras with { CaptureLinkMessage = message });
            }
            if (Current()) LoadDetailsSection(PlayniteDetailsTab.Captures, true);
        }, WidgetOperationLifetime.Active);
        return true;
    }

    private bool HandleCaptureLink(string action, PlayniteLibraryRenderState state)
    {
        var query = RelatedCaptureKey(state);
        if (action == RelatedCapturePrefix+".picker.back" && query.PickerOffset is not null)
        {
            SetPicker(null);
            return true;
        }
        var page = ResourceFor(query).GetSnapshot(query).Value;
        if (page?.Query != query || page.Reference is null) return false;
        const string picker = RelatedCapturePrefix + ".picker.";
        var browse = action == RelatedCapturePrefix + ".linked" && page.Total > page.Items.Count;
        if (query.PickerOffset is null && (browse || action == RelatedCapturePrefix + ".link" && page.CanLink && query.Achievement is null))
        {
            Operations.RunSingleFlight(OpenCapturePickerWork, async operation =>
            {
                var opening = query with { PickerOffset = 0, BrowseLinked = browse };
                var token = operation.CancellationToken;
                await capturePicker.EnsureLoaded(opening, token, true).Completion;
                var current = _model.Value;
                if (!operation.IsCurrent || current.DetailsItem is null || current.DetailsExtras.Tab != PlayniteDetailsTab.Captures ||
                    RelatedCaptureKey(current) != query) return;
                UpdateDetails(state.DetailsGeneration, value => value with { CapturePickerOffset = 0, BrowsingLinkedCaptures = browse, CapturePageEntry = null,
                    CaptureReturnId = RelatedCapturePrefix + (browse ? ".linked" : ".link"), CaptureLinkMessage = null });
            }, WidgetOperationLifetime.Active);
            return true;
        }
        if (query.PickerOffset is { } offset)
        {
            if (action == picker+"next" && page.HasMore) { SetPicker(offset+12); return true; }
            if (action == picker+"previous" && offset > 0) { SetPicker(Math.Max(0,offset-12)); return true; }
        }
        if (query.BrowseLinked || query.Achievement is not null) return false;
        var item = query.PickerOffset is not null ? page.Items.FirstOrDefault(value => action == PickAction(page,value)) : null;
        if (item is not null && page.Runs?.Items.Any(run => run.Latest.Id == item.Id && (long)run.Screenshots + run.Videos > 1) == true) return true;
        if (item is null && action != RelatedCapturePrefix + ".link") return false;
        const string work = RelatedCapturePrefix + ".link-work";
        if (Operations.IsBusy(work)) return true;
        Operations.RunLatest(work, async operation =>
        {
            var token = operation.CancellationToken;
            bool Current() => operation.IsCurrent && _model.Value.DetailsItem is not null &&
                _model.Value.DetailsExtras.Tab == PlayniteDetailsTab.Captures && RelatedCaptureKey(WithDetailsResources(_model.Value)) == query;
            try
            {
                if (item is null)
                {
                    var permission = await HostServices.Permissions.RequestAsync([WidgetCaptureLibraryCapabilities.ManagePermission],token);
                    if (Current()) SetPicker(null, permission == WidgetPermissionOutcome.Granted ? null : "Permission is needed to link captures.");
                    return;
                }
                var integration = await _application.GetCaptureIntegrationAsync(token);
                if (!Current() || integration is null || integration.ConnectionId != page.Connection || !CaptureRelatedIdentity.TryGame(integration.ProfileId,
                    CaptureRelatedIdentity.GameKind,page.Reference,out var game) || game.ToString("D") != query.GameId) return;
                var outcome = await AttachGameAsync(item,page.Reference,query.Name,token, explicitSelection: true);
                if (outcome is not (CaptureContextSaveOutcome.Saved or CaptureContextSaveOutcome.AlreadySaved))
                {
                    if (Current()) SetPicker(query.PickerOffset, "This capture is no longer available.");
                    return;
                }
                await RememberCaptureProcessAsync(page.Reference, item.Media.SourceApplication?.ProcessName, token);
                if (Current())
                {
                    if (state.DetailsExtras.RemovedLink?.Change.CaptureId == item.Id)
                        UpdateDetails(state.DetailsGeneration, value => value with { RemovedLink = null });
                    SetPicker(null, outcome == CaptureContextSaveOutcome.Saved ? "Capture linked." : "Already linked to this game.");
                }
            }
            catch (Exception error) when (error is WidgetCapabilityException or PlayniteBridgeTransportException or PlayniteBridgeDataException)
            { if (Current()) SetPicker(query.PickerOffset,"Could not link the capture. Refresh and try again."); }
        },WidgetOperationLifetime.Active);
        return true;

        void SetPicker(int? next, string? message = null)
        {
            Operations.Cancel(SessionLinks(query.Opening));
            if (next is null) capturePicker.Reset();
            var pageEntry = next.HasValue && next != query.PickerOffset ? CreateFocusRequest() : null;
            UpdateDetails(state.DetailsGeneration, value => value with { CapturePickerOffset = next, BrowsingLinkedCaptures = next is not null && query.BrowseLinked,
                CapturePageEntry = pageEntry, CaptureLinkMessage = message,
                CaptureReturnId = next.HasValue ? null : RelatedCapturePrefix + (query.BrowseLinked ? ".linked" : ".link") });
            LoadDetailsSection(PlayniteDetailsTab.Captures,true);
        }
    }
}
