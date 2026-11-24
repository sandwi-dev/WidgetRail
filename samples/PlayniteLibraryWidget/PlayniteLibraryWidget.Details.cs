using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string DetailsPrefix = "playnite-library.details.";

    private readonly record struct DetailsQuery(string SavedId, long Opening);
    private readonly WidgetQueryResource<DetailsQuery, PlayniteAchievements> _detailsAchievements;
    private readonly WidgetQueryResource<DetailsQuery, PlayniteActivity> _detailsActivity;
    private readonly record struct StoreQuery(DetailsQuery Game, PlayniteStoreLookup Lookup);
    private readonly WidgetQueryResource<StoreQuery, SteamStoreMedia> storeMedia;
    private static StoreQuery? StoreKey(PlayniteLibraryRenderState state) =>
        state.DetailsItem is not null && state.DetailsExtras.Full?.StoreLookup is { } lookup &&
        !state.DetailsExtras.CaptureViewerFromHome ? new(DetailsKey(state), lookup) : null;

    private void LoadStoreMedia(PlayniteLibraryRenderState state, CancellationToken route)
    {
        if (StoreKey(state) is { } key) storeMedia.EnsureLoaded(key, route);
    }

    public override ValueTask OnEmbeddedMediaPlaybackEventAsync(EmbeddedMediaPlaybackEvent playbackEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (playbackEvent.State != EmbeddedMediaPlaybackState.Error ||
            playbackEvent.ErrorCode is not ("trailer-unavailable" or "trailer-autoplay-blocked" or "trailer-api-unavailable" or "trailer-timeout"))
            return ValueTask.CompletedTask;
        var state = _model.Value;
        if (StoreKey(state) is { } key && storeMedia.GetSnapshot(key).Value?.PublisherTrailerId is { } id &&
            id == playbackEvent.MediaKey && playbackEvent.SessionId == PlayniteTrailerPresentation.SessionId(id, state.DetailsGeneration))
            UpdateDetails(state.DetailsGeneration, extras => extras with { UnavailablePublisherTrailer = id });
        return ValueTask.CompletedTask;
    }

    private static DetailsQuery DetailsKey(PlayniteLibraryRenderState state) =>
        new(state.DetailsItem!.Value.SavedId, state.DetailsOpening);

    // Resource snapshots own all read state. The model only owns navigation and
    // domain mutations; presentation receives one composed immutable projection.
    private PlayniteLibraryRenderState WithDetailsResources(PlayniteLibraryRenderState state)
    {
        if (state.DetailsItem is null) return state;
        var key = DetailsKey(state);
        var achievements = _detailsAchievements.GetSnapshot(key);
        var activity = _detailsActivity.GetSnapshot(key);
        var store = StoreKey(state) is { } mediaKey ? storeMedia.GetSnapshot(mediaKey) : null;
        return state with { DetailsExtras = state.DetailsExtras with
        {
            Achievements = achievements.Value,
            AchievementsLoading = achievements.Status is WidgetResourceStatus.Loading or WidgetResourceStatus.Refreshing,
            AchievementsError = achievements.Error?.Message,
            Activity = activity.Value,
            StoreMedia = store?.Value,
            AddonSetup = state.Setup,
            StoreMediaLoading = state.DetailsLoading || store?.Status is WidgetResourceStatus.NotLoaded or WidgetResourceStatus.Loading,
            ActivityLoading = activity.Status is WidgetResourceStatus.Loading or WidgetResourceStatus.Refreshing,
            ActivityError = activity.Error?.Message,
            ProgressPinSelectionId = progressPin.Selection?.SelectionId,
            TrackedAchievementId = _organization.TrackedFor(key.SavedId, achievements.Value)?.Id,
        } };
    }

    private void SuspendDetails()
    {
        // Fence late results even if a provider ignores cancellation. Keep the opening
        // identity, completed data and tab so the host can restore focus and scrolling.
        _model.Update(state => state.DetailsItem is null ? state : state with
        {
            DetailsGeneration = state.DetailsGeneration + 1,
            DetailsLoading = false,
            DetailsExtras = state.DetailsExtras with
            {
                CompletionStatusesLoading = false,
                AchievementSearchRequest = 0,
                OperationBusy = false,
                AchievementRefreshBusy = false,
                AchievementRefreshMessage = state.DetailsExtras.AchievementRefreshBusy
                    ? "The add-on may still be syncing in Playnite." : state.DetailsExtras.AchievementRefreshMessage,
                ConfirmUninstall = false,
                ChoosingCompletion = false,
                OperationMessage = state.DetailsExtras.OperationBusy
                    ? "Request interrupted. Check its outcome in Playnite."
                    : state.DetailsExtras.OperationMessage,
            },
        });
        CancelDetailsOperations(retainReadValues: true);
    }

    private void ResumeDetails()
    {
        var state = _model.Value;
        if (state.DetailsItem is not { } item || state.DetailsExtras.CaptureViewerFromHome) return;
        if (state.DetailsExtras.Full is null && state.DetailsError is null)
        {
            _model.Update(value => value with { DetailsLoading = true });
            LoadDetailsOverview(item, state.DetailsGeneration);
        }
        else LoadStoreMedia(state, _navigation.Value.RouteCancellationToken);
        LoadCompletionStatuses();
        LoadDetailsSection(state.DetailsExtras.Tab);
    }

    private void CancelDetailsOperations(bool retainReadValues = false)
    {
        Operations.Cancel(OpenCapturePickerWork);
        if (retainReadValues) { relatedCaptures.Cancel(); capturePicker.Cancel(); }
        else { relatedCaptures.Reset(); capturePicker.Reset(); }
        Operations.Cancel(ReviewLinkWork);
        if (!retainReadValues && _model.Value.DetailsExtras.ReviewSession is not null) sessionRows.EndSelection();
        Operations.Cancel(RelatedCaptureWatch);
        Operations.Cancel(SessionLinks(_model.Value.DetailsOpening));
        if (!retainReadValues) ClearSessionHistory();
        if (retainReadValues) { _detailsAchievements.Cancel(); _detailsActivity.Cancel(); }
        else { _detailsAchievements.Reset(); _detailsActivity.Reset(); }
        if (retainReadValues) timelineCaptures.Cancel(); else timelineCaptures.Reset();
        if (retainReadValues) storeMedia.Cancel(); else storeMedia.Reset();
        foreach (var key in new[] { "playnite-library.details", "playnite-library.details.operation", "playnite-library.details.statuses", DetailsPrefix + "provider-refresh" }) Operations.Cancel(key);
    }

    private void UpdateDetails(long generation, Func<PlayniteDetailsExtras, PlayniteDetailsExtras> update) =>
        _model.Update(state => state.DetailsItem is null || state.DetailsGeneration != generation
            ? state : state with { DetailsExtras = update(state.DetailsExtras) });

    private void LoadCompletionStatuses()
    {
        var state = _model.Value;
        if (state.DetailsItem is null || state.DetailsExtras.CompletionStatuses is not null ||
            state.DetailsExtras.CompletionStatusesLoading) return;
        var generation = state.DetailsGeneration;
        UpdateDetails(generation, value => value with { CompletionStatusesLoading = true, CompletionStatusesError = null });
        var route = _navigation.Value.RouteCancellationToken;
        var operation = Operations.RunLatest(DetailsPrefix + "statuses", async context =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, route);
            try
            {
                var values = (await _application.GetCompletionStatusesAsync(lifetime.Token).ConfigureAwait(false))
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                lifetime.Token.ThrowIfCancellationRequested();
                // Reserve one slot for a current/unset value not in the provider's list.
                var oversized = values.Length >= ProtocolConstants.MaximumSelectOptionCount;
                UpdateDetails(generation, value => value with
                {
                    CompletionStatuses = oversized ? [] : values,
                    CompletionStatusesLoading = false,
                    CompletionStatusesError = oversized ? "Too many completion statuses to display. Manage them in Playnite."
                        : values.Length == 0 ? "No completion statuses are available in Playnite." : null,
                });
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                UpdateDetails(generation, value => value with { CompletionStatusesLoading = false,
                    CompletionStatusesError = "Completion statuses could not be loaded." });
            }
        }, WidgetOperationLifetime.Active);
        if (!operation.IsAccepted)
            UpdateDetails(generation, value => value with { CompletionStatusesLoading = false,
                CompletionStatusesError = "Completion statuses could not be loaded." });
    }

    private void LoadDetailsSection(PlayniteDetailsTab tab, bool refresh = false)
    {
        var state = _model.Value;
        if (state.DetailsItem is null) return;
        var key = DetailsKey(state);
        var route = _navigation.Value.RouteCancellationToken;
        if (tab != PlayniteDetailsTab.Captures)
        {
            Operations.Cancel(RelatedCaptureWatch);
            Operations.Cancel(SessionLinks(state.DetailsOpening));
        }
        if (tab is PlayniteDetailsTab.Description or PlayniteDetailsTab.Activity)
        {
            if (tab == PlayniteDetailsTab.Activity && refresh) sessionAnalysis.Reset(invalidate: false);
            _detailsAchievements.EnsureLoaded(key, route, refresh);
            _detailsActivity.EnsureLoaded(key, route, refresh);
            timelineCaptures.EnsureLoaded(key, route, refresh);
        }
        else if (tab == PlayniteDetailsTab.Achievements)
            _detailsAchievements.EnsureLoaded(key, route, refresh);
        else if (tab == PlayniteDetailsTab.Captures)
        { RefreshCaptureResources(WithDetailsResources(state), refresh); WatchRelatedCaptures(); }
    }

    private void RefreshCaptureResources(PlayniteLibraryRenderState state, bool refresh)
    {
        var query = RelatedCaptureKey(state);
        var route = _navigation.Value.RouteCancellationToken;
        // The native dialog retains its parent. Keep the selected gallery data
        // and its focus targets alive while the picker browses another query.
        relatedCaptures.EnsureLoaded(query.Timeline, route, refresh);
        if (query.PickerOffset is not null) capturePicker.EnsureLoaded(query, route, refresh);
    }

    private async ValueTask<bool> TryHandleDetailsActionAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (!action.ActionId.StartsWith(DetailsPrefix, StringComparison.Ordinal) ||
            action.ActionId is PlayniteLibraryActions.DetailsOpen or PlayniteLibraryActions.DetailsClose) return false;
        var state = WithDetailsResources(_model.Value);
        if (LifecycleState != WidgetLifecycleState.Interactive || state.DetailsItem is not { } item) return true;
        var name = action.ActionId[DetailsPrefix.Length..];
        if (name.StartsWith("sessions.", StringComparison.Ordinal)) return HandleSessionAction(state, action.ActionId);
        if (name.StartsWith("achievement.unpin.", StringComparison.Ordinal) || name.StartsWith("achievement.manage.", StringComparison.Ordinal))
        {
            var selection = progressPin.Selection;
            if (selection is not null && name.EndsWith("." + selection.SelectionId, StringComparison.Ordinal))
                try
                {
                    if (name.StartsWith("achievement.unpin.", StringComparison.Ordinal)) await HostServices.Pinning.RequestCloseAsync(selection, token).ConfigureAwait(false);
                    else await HostServices.Pinning.RequestManageAsync(selection, token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
                { UpdateDetails(state.DetailsGeneration, value => value with { AchievementRefreshMessage = "Pin management is unavailable on this host." }); }
            return true;
        }
        if (action.ActionId == PlayniteLibraryDetailsPresentation.SyncGameAction)
        {
            if (state.DetailsExtras.Tab == PlayniteDetailsTab.Achievements && state.DetailsCapturedTarget?.IsCurrent != false &&
                !state.DetailsExtras.OperationBusy && !state.OrganizationBusy && state.LaunchingSavedId != item.Value.SavedId)
                RefreshAchievementsFromProvider(state);
            return true;
        }
        if (action.ActionId != PlayniteAchievementBrowse.SearchFocusAction && state.DetailsExtras.AchievementSearchRequest != 0)
            UpdateDetails(state.DetailsGeneration, value => value with { AchievementSearchRequest = 0 });
        if (action.ActionId == PlayniteAchievementBrowse.SearchFocusAction)
        {
            if (state.DetailsExtras.Tab == PlayniteDetailsTab.Achievements &&
                !state.DetailsExtras.ConfirmUninstall && state.DetailsExtras.Achievements is { Available: true })
                RequestAchievementSearch(clear: false);
            return true;
        }
        if (name.StartsWith("achievement.reveal.", StringComparison.Ordinal))
        {
            var key = name[19..];
            if (state.DetailsExtras.Tab == PlayniteDetailsTab.Achievements && achievementBrowse?.ByKey.TryGetValue(key, out var entry) == true && entry.Secret)
                UpdateDetails(state.DetailsGeneration, value => value with { RevealedAchievements = new HashSet<string>(value.RevealedAchievements, StringComparer.Ordinal) { key } });
            return true;
        }
        if (name == "achievement.search" && action.CommittedText is { } text)
        {
            UpdateDetails(state.DetailsGeneration, value => value with { AchievementSearch = text.Trim()[..Math.Min(text.Trim().Length, 128)] });
            return true;
        }
        if (name == "achievement.search.clear")
        {
            RequestAchievementSearch(clear: true);
            return true;
        }
        if (name.StartsWith("achievement.filter.", StringComparison.Ordinal) && Enum.TryParse<AchievementFilter>(name[19..], out var filter) && Enum.IsDefined(filter))
        {
            UpdateDetails(state.DetailsGeneration, value => value with { AchievementFilter = filter });
            return true;
        }
        if (name.StartsWith("achievement.sort.", StringComparison.Ordinal) && Enum.TryParse<AchievementSort>(name[17..], out var sort) && Enum.IsDefined(sort))
        {
            UpdateDetails(state.DetailsGeneration, value => value with { AchievementSort = sort });
            return true;
        }
        if (name.StartsWith("tab.", StringComparison.Ordinal) &&
            Enum.TryParse<PlayniteDetailsTab>(name[4..], true, out var tab) && Enum.IsDefined(tab))
        {
            UpdateDetails(state.DetailsGeneration, value => value with { Tab = tab, Page = 0, SessionFocus = null, TabFocus = null, TimelineFocus = null, CapturePageEntry = null,
                CaptureAchievementId = tab == PlayniteDetailsTab.Captures ? null : value.CaptureAchievementId,
                CaptureAchievementName = tab == PlayniteDetailsTab.Captures ? null : value.CaptureAchievementName });
            LoadDetailsSection(tab);
            return true;
        }
        if (name is "tab.previous" or "tab.next")
        {
            var tabs = Enum.GetValues<PlayniteDetailsTab>();
            var next = tabs[((int)state.DetailsExtras.Tab + (name.EndsWith("next", StringComparison.Ordinal) ? 1 : tabs.Length - 1)) % tabs.Length];
            var focus = CreateFocusRequest();
            UpdateDetails(state.DetailsGeneration, value => value with { Tab = next, Page = 0, SessionFocus = null, TabFocus = focus, TimelineFocus = null, CapturePageEntry = null });
            LoadDetailsSection(next);
            return true;
        }
        if (name.StartsWith("retry.", StringComparison.Ordinal))
        {
            var activeTab = state.DetailsExtras.Tab;
            var key = DetailsKey(state);
            switch (action.ActionId)
            {
                case PlayniteLibraryDetailsPresentation.RetryDetailsAction when activeTab is PlayniteDetailsTab.Information or PlayniteDetailsTab.Links:
                    if (state.DetailsError is not null && !state.DetailsLoading)
                    {
                        _model.Update(value => value with { DetailsLoading = true, DetailsError = null });
                        LoadDetailsOverview(item, state.DetailsGeneration);
                    }
                    break;
                case PlayniteLibraryDetailsPresentation.RetryStatusesAction when activeTab == PlayniteDetailsTab.Information:
                    LoadCompletionStatuses();
                    break;
                case PlayniteLibraryDetailsPresentation.RetryAchievementsAction when activeTab == PlayniteDetailsTab.Achievements:
                    _detailsAchievements.EnsureLoaded(key, _navigation.Value.RouteCancellationToken, forceRefresh: true);
                    break;
                case PlayniteLibraryDetailsPresentation.RetryActivityAction when activeTab == PlayniteDetailsTab.Activity:
                    _detailsActivity.EnsureLoaded(key, _navigation.Value.RouteCancellationToken, forceRefresh: true);
                    break;
                case PlayniteLibraryDetailsPresentation.RetrySessionsAction when activeTab == PlayniteDetailsTab.Activity:
                    sessionAnalysis.Reset();
                    break;
                case PlayniteLibraryDetailsPresentation.RetryTimelineAction when activeTab == PlayniteDetailsTab.Description:
                    var route = _navigation.Value.RouteCancellationToken;
                    if (state.DetailsExtras.ActivityError is not null) _detailsActivity.EnsureLoaded(key, route, forceRefresh: true);
                    if (state.DetailsExtras.AchievementsError is not null) _detailsAchievements.EnsureLoaded(key, route, forceRefresh: true);
                    if (timelineCaptures.GetSnapshot(key).Error is not null) timelineCaptures.EnsureLoaded(key, route, forceRefresh: true);
                    break;
            }
            return true;
        }
        if (name is "page.previous" or "page.next")
        {
            var count = state.DetailsExtras.Tab == PlayniteDetailsTab.Achievements
                ? state.DetailsExtras.Achievements?.Items.Count ?? 0 : state.DetailsExtras.Activity?.Sessions.Count ?? 0;
            UpdateDetails(state.DetailsGeneration, value => value with
            { Page = Math.Clamp(value.Page + (name.EndsWith("next", StringComparison.Ordinal) ? 1 : -1), 0, Math.Max(0, (count - 1) / PlayniteLibraryDetailsPresentation.PageSize)) });
            return true;
        }
        if (name is "choose-status" or "cancel-status")
        {
            UpdateDetails(state.DetailsGeneration, value => value with
            {
                ChoosingCompletion = name == "choose-status" && value.CompletionStatuses is { Count: > 0 } &&
                    state.DetailsCapturedTarget?.IsCurrent != false && !state.OrganizationBusy,
            });
            return true;
        }
        if (name == "uninstall")
        {
            UpdateDetails(state.DetailsGeneration, value => value with { ConfirmUninstall = true });
            return true;
        }
        if (name == "uninstall.cancel")
        {
            UpdateDetails(state.DetailsGeneration, value => value with { ConfirmUninstall = false });
            return true;
        }
        if (name.StartsWith("completion.", StringComparison.Ordinal))
        {
            if (state.OrganizationBusy || state.DetailsExtras.OperationBusy || state.LaunchingSavedId is not null ||
                CaptureActionTarget(PlayniteLibraryDetailsPresentation.PlayId) is not { } target) return true;
            var selected = state.DetailsExtras.CompletionStatuses?.FirstOrDefault(value =>
                PlayniteLibraryDetailsPresentation.CompletionAction(state.DetailsGeneration, value) == action.ActionId);
            if (selected is null) return true;
            var completion = await SetCompletionStatusAsync(target, selected, token).ConfigureAwait(false);
            var message = _model.Value.Status;
            UpdateDetails(state.DetailsGeneration, value => value with
            {
                ChoosingCompletion = false,
                ConfirmedCompletionStatus = completion ?? value.ConfirmedCompletionStatus,
                Full = completion is not null && value.Full?.Game is { } game
                    ? value.Full with { Game = game with { CompletionStatus = completion } } : value.Full,
                OperationMessage = completion is null ? message : null,
            });
            return true;
        }
        if (name is not ("install" or "uninstall.confirm")) return true;
        if (state.DetailsExtras.OperationBusy || name == "uninstall.confirm" && !state.DetailsExtras.ConfirmUninstall) return true;
        if (name == "install" && !PlayniteLibraryAvailabilityPresentation.IsUninstalled(item)) return true;
        var generation = state.DetailsGeneration;
        UpdateDetails(generation, value => value with { OperationBusy = true, ConfirmUninstall = false, OperationMessage = null });
        var routeLifetime = _navigation.Value.RouteCancellationToken;
        var operation = Operations.RunLatest(DetailsPrefix + "operation", async context =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, routeLifetime);
            try
            {
                var okay = await _application.ChangeInstallationAsync(item.Value.SavedId, name == "install", lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                UpdateDetails(generation, value => value with
                {
                    OperationBusy = false,
                    OperationMessage = !okay ? "Playnite could not accept this request."
                        : name == "install" ? "Installation requested. Follow Playnite or the launcher, then Refresh."
                        : "Uninstallation requested. Follow Playnite or the launcher, then Refresh.",
                });
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                UpdateDetails(generation, value => value with { OperationBusy = false,
                    OperationMessage = "The request could not complete. Check Playnite and try again." });
            }
        }, WidgetOperationLifetime.Active);
        if (!operation.IsAccepted)
            UpdateDetails(generation, value => value with { OperationBusy = false,
                OperationMessage = "The request could not start. Try again." });
        return true;
    }
}
