using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string DetailsPrefix = "playnite-library.details.";

    private readonly record struct DetailsQuery(string SavedId, long Opening);
    private readonly WidgetQueryResource<DetailsQuery, PlayniteAchievements> _detailsAchievements;
    private readonly WidgetQueryResource<DetailsQuery, PlayniteActivity> _detailsActivity;

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
        return state with { DetailsExtras = state.DetailsExtras with
        {
            Achievements = achievements.Value,
            AchievementsLoading = achievements.Status is WidgetResourceStatus.Loading or WidgetResourceStatus.Refreshing,
            AchievementsError = achievements.Error?.Message,
            Activity = activity.Value,
            ActivityLoading = activity.Status is WidgetResourceStatus.Loading or WidgetResourceStatus.Refreshing,
            ActivityError = activity.Error?.Message,
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
                ConfirmUninstall = false,
                OperationMessage = state.DetailsExtras.OperationBusy
                    ? "Request interrupted. Refresh to check its outcome."
                    : state.DetailsExtras.OperationMessage,
            },
        });
        CancelDetailsOperations(retainReadValues: true);
    }

    private void ResumeDetails()
    {
        var state = _model.Value;
        if (state.DetailsItem is not { } item) return;
        if (state.DetailsExtras.Full is null && state.DetailsError is null)
        {
            _model.Update(value => value with { DetailsLoading = true });
            LoadDetailsOverview(item, state.DetailsGeneration);
        }
        LoadCompletionStatuses();
        LoadDetailsSection(state.DetailsExtras.Tab);
    }

    private void CancelDetailsOperations(bool retainReadValues = false)
    {
        if (retainReadValues) { _detailsAchievements.Cancel(); _detailsActivity.Cancel(); }
        else { _detailsAchievements.Reset(); _detailsActivity.Reset(); }
        foreach (var key in new[] { "playnite-library.details", "playnite-library.details.operation", "playnite-library.details.statuses" }) Operations.Cancel(key);
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
                    CompletionStatusesError = "Completion statuses could not be loaded. Refresh to retry." });
            }
        }, WidgetOperationLifetime.Active);
        if (!operation.IsAccepted)
            UpdateDetails(generation, value => value with { CompletionStatusesLoading = false,
                CompletionStatusesError = "Completion statuses could not be loaded. Refresh to retry." });
    }

    private void LoadDetailsSection(PlayniteDetailsTab tab, bool refresh = false)
    {
        var state = _model.Value;
        if (state.DetailsItem is null) return;
        var key = DetailsKey(state);
        var route = _navigation.Value.RouteCancellationToken;
        if (tab == PlayniteDetailsTab.Achievements)
            _detailsAchievements.EnsureLoaded(key, route, refresh);
        else if (tab == PlayniteDetailsTab.Activity)
            _detailsActivity.EnsureLoaded(key, route, refresh);
    }

    private async ValueTask<bool> TryHandleDetailsActionAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (!action.ActionId.StartsWith(DetailsPrefix, StringComparison.Ordinal) ||
            action.ActionId is PlayniteLibraryActions.DetailsOpen or PlayniteLibraryActions.DetailsClose) return false;
        var state = WithDetailsResources(_model.Value);
        if (LifecycleState != WidgetLifecycleState.Interactive || state.DetailsItem is not { } item) return true;
        var name = action.ActionId[DetailsPrefix.Length..];
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
            UpdateDetails(state.DetailsGeneration, value => value with { Tab = tab, Page = 0 });
            LoadDetailsSection(tab);
            return true;
        }
        if (name is "tab.previous" or "tab.next")
        {
            var tabs = Enum.GetValues<PlayniteDetailsTab>();
            var next = tabs[((int)state.DetailsExtras.Tab + (name.EndsWith("next", StringComparison.Ordinal) ? 1 : tabs.Length - 1)) % tabs.Length];
            UpdateDetails(state.DetailsGeneration, value => value with { Tab = next, Page = 0 });
            LoadDetailsSection(next);
            return true;
        }
        if (name == "refresh")
        {
            if (!state.DetailsExtras.OperationBusy && !state.OrganizationBusy && state.LaunchingSavedId is null)
                OpenDetails(item, preserveTab: true);
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
