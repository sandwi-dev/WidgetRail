using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string UpdatesOperation = "playnite-library.integration-updates";

    private (string Instance, string Connection)? playingAuthority;
    private void StartPlayniteUpdates() => Operations.RunLatest(UpdatesOperation, async context =>
    {
        // The SDK owns cancellation/replacement. These cursors belong to the
        // external Playnite process and must be reset across process/profile changes.
        long cursor = -1;
        string? instance = null, connection = null;
        var retryMs = 250;
        while (!context.CancellationToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _application.ReadEventsAsync(cursor, context.CancellationToken).ConfigureAwait(false);
                context.CancellationToken.ThrowIfCancellationRequested();
                if (batch is null) { ApplyRunningGames(null); return; }
                if (!context.IsCurrent) return;
                var replaced = instance != batch.InstanceId || connection != batch.ConnectionId;
                instance = batch.InstanceId; connection = batch.ConnectionId;
                cursor = replaced && !batch.ResetRequired ? -1 : batch.NextSequence;
                retryMs = 250;
                lock (_gate)
                {
                    if (!context.IsCurrent) return;
                    var authority = (batch.InstanceId, batch.ConnectionId);
                    if (playingAuthority is { } previous && previous != authority)
                    {
                        // The transport fences profile changes until explicit setup.
                        // A Playnite restart also changes the instance. Retire the
                        // shelf reads without disturbing the user's current route.
                        ResetPlayingResources();
                        homeCaptures.Reset(invalidate: false);
                        homeRecentActivity.Reset(invalidate: false);
                    }
                    playingAuthority = authority;
                }
                ApplyRunningGames(batch.RunningGames, batch.RunningSince, batch.RunningNames, batch.RunningProcesses);
                if (replaced) LoadHomeActivity();
                var state = _model.Value;
                if (!state.Setup.Busy && state.DetailsItem is { } item &&
                    (replaced || batch.ResetRequired || batch.Changes.Any(change => change.GameId is null || change.GameId == item.Value.SavedId)))
                    LoadDetailsSection(state.DetailsExtras.Tab, refresh: true);
                if (!HiddenCards.HasFlag(PlayingCardKind.Achievements) && PlayingId(state) is { } game && batch.Changes.Any(change =>
                    (change.GameId is null || change.GameId == game) &&
                    change.Kind is "achievements-changed" or "achievement-unlocked"))
                {
                    if (_navigation.Value.Route == PlayniteLibraryRoute.Library && LifecycleState == WidgetLifecycleState.Interactive)
                    {
                        playingAwards.Cancel(invalidate: false);
                        playingAwards.EnsureLoaded(AwardsQuery(game), _navigation.Value.RouteCancellationToken, forceRefresh: true);
                    }
                    else playingAwards.Reset(invalidate: false);
                }
                HandleProgressUpdates(batch);
                if (_navigation.Value.Route == PlayniteLibraryRoute.Achievements && LifecycleState == WidgetLifecycleState.Interactive &&
                    (replaced || batch.ResetRequired || batch.Changes.Count > 0))
                {
                    // An invalidation must supersede a read started before this
                    // event, not join it and lose the newly published progress.
                    achievementHub.Cancel(invalidate: false);
                    _ = achievementHub.Refresh();
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { return; }
            catch (Exception error) when (error is PlayniteBridgeTransportException or PlayniteBridgeDataException or PlayniteCredentialException or WidgetCapabilityException or System.Text.Json.JsonException)
            {
                ApplyRunningGames(null);
                cursor = -1; instance = connection = null;
                await Task.Delay(retryMs, context.CancellationToken).ConfigureAwait(false);
                retryMs = Math.Min(5000, retryMs * 2);
            }
        }
    }, WidgetOperationLifetime.Active);

    private void RefreshAchievementsFromProvider(PlayniteLibraryRenderState state)
    {
        if (state.DetailsItem is not { } item || state.DetailsExtras.Achievements?.CanRefresh != true || state.DetailsExtras.AchievementRefreshBusy) return;
        var generation = state.DetailsGeneration;
        UpdateDetails(generation, value => value with { AchievementRefreshBusy = true, AchievementRefreshMessage = "Refreshing from provider…" });
        var route = _navigation.Value.RouteCancellationToken;
        var operation = Operations.RunSingleFlight(DetailsPrefix + "provider-refresh", async context =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, route);
            try
            {
                var result = await _application.RefreshAchievementsAsync(item.Value.SavedId, lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                UpdateDetails(generation, value => value with { AchievementRefreshBusy = false, AchievementRefreshMessage = result switch
                {
                    PlayniteProviderRefreshResult.Completed => "Provider refresh completed.",
                    PlayniteProviderRefreshResult.StillRunning => "Still refreshing in Playnite. Updates will appear when available.",
                    PlayniteProviderRefreshResult.Failed => "The provider could not refresh. Check its account and connection in Playnite.",
                    _ => "The refresh could not be confirmed. Check Playnite before retrying.",
                } });
                if (_model.Value.DetailsGeneration == generation)
                {
                    // Supersede a cache read started before the provider completed.
                    _detailsAchievements.Cancel(invalidate: false);
                    LoadDetailsSection(PlayniteDetailsTab.Achievements, refresh: true);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is PlayniteBridgeTransportException or PlayniteBridgeDataException or PlayniteCredentialException or WidgetCapabilityException or System.Text.Json.JsonException)
            { UpdateDetails(generation, value => value with { AchievementRefreshBusy = false, AchievementRefreshMessage = "The refresh could not be confirmed. Check Playnite before retrying." }); }
        }, WidgetOperationLifetime.Active);
        if (!operation.IsAccepted) UpdateDetails(generation, value => value with { AchievementRefreshBusy = false, AchievementRefreshMessage = "Try again when the widget is open." });
    }
}
