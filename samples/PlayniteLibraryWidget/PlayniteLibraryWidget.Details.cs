using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string DetailsPrefix = "playnite-library.details.";

    private void CancelDetailsOperations()
    {
        foreach (var key in new[] { "playnite-library.details", "playnite-library.details.achievements",
                     "playnite-library.details.activity", "playnite-library.details.operation" }) Operations.Cancel(key);
    }

    private void UpdateDetails(long generation, Func<PlayniteDetailsExtras, PlayniteDetailsExtras> update) =>
        _model.Update(state => state.DetailsItem is null || state.DetailsGeneration != generation
            ? state : state with { DetailsExtras = update(state.DetailsExtras) });

    private void LoadDetailsSection(PlayniteDetailsTab tab)
    {
        var state = _model.Value;
        if (state.DetailsItem is not { } game || tab == PlayniteDetailsTab.Overview) return;
        var achievements = tab == PlayniteDetailsTab.Achievements;
        if (achievements ? state.DetailsExtras.Achievements is not null || state.DetailsExtras.AchievementsLoading
            : state.DetailsExtras.Activity is not null || state.DetailsExtras.ActivityLoading) return;
        var generation = state.DetailsGeneration;
        UpdateDetails(generation, value => achievements
            ? value with { AchievementsLoading = true, AchievementsError = null }
            : value with { ActivityLoading = true, ActivityError = null });
        var route = _navigation.Value.RouteCancellationToken;
        _ = Operations.RunLatest(DetailsPrefix + (achievements ? "achievements" : "activity"), async context =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, route);
            try
            {
                if (achievements)
                {
                    var result = await _application.GetAchievementsAsync(game.Value.SavedId, lifetime.Token).ConfigureAwait(false);
                    lifetime.Token.ThrowIfCancellationRequested();
                    UpdateDetails(generation, value => value with { Achievements = result, AchievementsLoading = false });
                }
                else
                {
                    var result = await _application.GetActivityAsync(game.Value.SavedId, lifetime.Token).ConfigureAwait(false);
                    lifetime.Token.ThrowIfCancellationRequested();
                    UpdateDetails(generation, value => value with { Activity = result, ActivityLoading = false });
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                UpdateDetails(generation, value => achievements
                    ? value with { AchievementsLoading = false, AchievementsError = "Achievements could not be loaded. Press Refresh to retry." }
                    : value with { ActivityLoading = false, ActivityError = "Activity could not be loaded. Press Refresh to retry." });
            }
        }, WidgetOperationLifetime.Active);
    }

    private async ValueTask<bool> TryHandleDetailsActionAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (!action.ActionId.StartsWith(DetailsPrefix, StringComparison.Ordinal) ||
            action.ActionId is PlayniteLibraryActions.DetailsOpen or PlayniteLibraryActions.DetailsClose) return false;
        var state = _model.Value;
        if (LifecycleState != WidgetLifecycleState.Interactive || state.DetailsItem is not { } item) return true;
        var name = action.ActionId[DetailsPrefix.Length..];
        if (name.StartsWith("tab.", StringComparison.Ordinal) &&
            Enum.TryParse<PlayniteDetailsTab>(name[4..], true, out var tab) && Enum.IsDefined(tab))
        {
            UpdateDetails(state.DetailsGeneration, value => value with { Tab = tab, Page = 0 });
            LoadDetailsSection(tab);
            return true;
        }
        if (name is "tab.previous" or "tab.next")
        {
            var next = (PlayniteDetailsTab)(((int)state.DetailsExtras.Tab + (name.EndsWith("next", StringComparison.Ordinal) ? 1 : 2)) % 3);
            UpdateDetails(state.DetailsGeneration, value => value with { Tab = next, Page = 0 });
            LoadDetailsSection(next);
            return true;
        }
        if (name == "refresh") { OpenDetails(item, preserveTab: true); return true; }
        if (name is "page.previous" or "page.next")
        {
            var count = state.DetailsExtras.Tab == PlayniteDetailsTab.Achievements
                ? state.DetailsExtras.Achievements?.Items.Count ?? 0 : state.DetailsExtras.Activity?.Sessions.Count ?? 0;
            UpdateDetails(state.DetailsGeneration, value => value with
            { Page = Math.Clamp(value.Page + (name.EndsWith("next", StringComparison.Ordinal) ? 1 : -1), 0, Math.Max(0, (count - 1) / PlayniteLibraryDetailsPresentation.PageSize)) });
            return true;
        }
        if (name.StartsWith("achievement.", StringComparison.Ordinal) && int.TryParse(name[12..], out var index) &&
            state.DetailsExtras.Achievements is { } list && index >= 0 && index < list.Items.Count)
        {
            UpdateDetails(state.DetailsGeneration, value => value with
            { RevealedAchievements = new HashSet<int>(value.RevealedAchievements) { index } });
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
        if (name == "favorite" && ResolveActionSource(PlayniteLibraryDetailsPresentation.PlayId) is { } favoriteSource)
        {
            await ToggleFavoriteAsync(favoriteSource, token).ConfigureAwait(false);
            if (_model.Value.DetailsGeneration == state.DetailsGeneration && _model.Value.DetailsItem is not null)
                OpenDetails(item, preserveTab: true);
            return true;
        }
        if (name == "completion" && ResolveActionSource(PlayniteLibraryDetailsPresentation.PlayId) is { } source)
        {
            await CycleCompletionStatusAsync(source, token).ConfigureAwait(false);
            if (_model.Value.DetailsGeneration == state.DetailsGeneration && _model.Value.DetailsItem is not null)
                OpenDetails(item, preserveTab: true);
            return true;
        }
        var linkIndex = -1;
        var link = name.StartsWith("link.", StringComparison.Ordinal) && int.TryParse(name[5..], out linkIndex) &&
            state.DetailsExtras.Full?.Game is { } full && linkIndex >= 0 && linkIndex < full.Links.Count
            ? full.Links[linkIndex] : null;
        if (name is not ("install" or "uninstall.confirm") && link is null) return true;
        if (state.DetailsExtras.OperationBusy || name == "uninstall.confirm" && !state.DetailsExtras.ConfirmUninstall) return true;
        if (name == "install" && !PlayniteLibraryAvailabilityPresentation.IsUninstalled(item)) return true;
        var generation = state.DetailsGeneration;
        UpdateDetails(generation, value => value with { OperationBusy = true, ConfirmUninstall = false, OperationMessage = null });
        var routeLifetime = _navigation.Value.RouteCancellationToken;
        _ = Operations.RunLatest(DetailsPrefix + "operation", async context =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, routeLifetime);
            try
            {
                var okay = link is null
                    ? await _application.ChangeInstallationAsync(item.Value.SavedId, name == "install", lifetime.Token).ConfigureAwait(false)
                    : await _application.OpenGameLinkAsync(item.Value.SavedId, link.Url, lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                UpdateDetails(generation, value => value with
                {
                    OperationBusy = false,
                    OperationMessage = !okay ? "Playnite could not accept this request."
                        : link is not null ? "Opened in your browser."
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
        return true;
    }
}
