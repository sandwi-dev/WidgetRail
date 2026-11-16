using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    internal const string SetupPrefix = "playnite-library.setup.";
    internal const string SetupStartAction = SetupPrefix + "start";
    private readonly IPlayniteSetupService? _setupService;

    private void RefreshSetupAvailability()
    {
        if (_setupService is null || _model.Value.Setup.Busy || _navigation.Value.Route == PlayniteLibraryRoute.PlayniteConnection) return;
        _ = Operations.RunLatest(SetupPrefix + "availability", async context =>
        {
            try
            {
                var offer = await _setupService.CheckSetupAsync(context.CancellationToken).ConfigureAwait(false);
                context.CancellationToken.ThrowIfCancellationRequested();
                _model.Update(state => state.Setup.Busy || context.CancellationToken.IsCancellationRequested ? state : state with
                {
                    SetupRequired = offer.NeedsAttention,
                    SetupDialogOpen = offer.NeedsAttention && state.DetailsItem is null && state.FilterDraft is null,
                    Setup = state.Setup with { Plan = offer.Plan, Installations = offer.Discovery.Installations,
                        Message = offer.Plan is null ? offer.Discovery.Message : null, Connected = !offer.NeedsAttention },
                });
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (context.CancellationToken.IsCancellationRequested) return;
                _model.Update(state => state.Setup.Busy ? state : state with { SetupRequired = true,
                    SetupDialogOpen = state.DetailsItem is null && state.FilterDraft is null,
                    Setup = state.Setup with { Plan = null, Message = SetupError(error), Connected = false } });
            }
        }, WidgetOperationLifetime.Active);
    }

    private void RefreshSetup(string? program = null, string? data = null, PlayniteSetupInstallation? selected = null)
    {
        if (_setupService is null || !BeginSetup()) return;
        _ = Operations.RunLatest(SetupPrefix + "work", async context =>
        {
            try
            {
                var found = selected is null ? await _setupService.DiscoverAsync(program, data, context.CancellationToken).ConfigureAwait(false) :
                    new PlayniteSetupDiscovery(_model.Value.Setup.Installations);
                var running = found.Installations.Where(item => item.Running).ToArray();
                selected ??= running.Length == 1 ? running[0] : found.Installations.Count == 1 ? found.Installations[0] : null;
                var plan = selected is null ? null : await _setupService.InspectAsync(selected, context.CancellationToken).ConfigureAwait(false);
                context.CancellationToken.ThrowIfCancellationRequested();
                _model.Update(state => state with { Setup = state.Setup with
                {
                    Installations = found.Installations, Plan = plan, Message = found.Message, Busy = false, Progress = null,
                    Connected = false, ProgramPath = selected?.ProgramDirectory ?? state.Setup.ProgramPath,
                    DataPath = selected?.DataDirectory ?? state.Setup.DataPath,
                } });
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { FinishSetupError("The check was interrupted. Find Playnite again when ready."); }
            catch (Exception error) { FinishSetupError(SetupError(error)); }
        }, WidgetOperationLifetime.Widget);
    }

    private void ApplySetup(bool startOnly)
    {
        if (_setupService is null || !BeginSetup()) return;
        var plan = _model.Value.Setup.Plan;
        if (plan?.CanApply != true) { FinishSetupError("Find and select Playnite before connecting."); return; }
        Operations.Cancel(UpdatesOperation);
        _ = Operations.RunLatest(SetupPrefix + "work", async context =>
        {
            try
            {
                void Progress(string message) => _model.Update(state => state with { Setup = state.Setup with { Progress = message } });
                var result = startOnly
                    ? await _setupService.StartAsync(plan.Installation, Progress, context.CancellationToken).ConfigureAwait(false)
                    : await _setupService.ApplyAsync(plan!, Progress, context.CancellationToken).ConfigureAwait(false);
                if (result.Connected)
                {
                    RetireConnectionPresentation();
                    await _application.ResetConnectionCacheAsync().ConfigureAwait(false);
                    if (_playniteClient is PlayniteBridgeClient client) client.ResetConnection();
                    _playniteConnection.Reset();
                    _model.Update(state => state with { PlayniteKind = PlayniteBridgeConnectionKind.Connected, PlayniteCode = "connected",
                        DetailsItem = null, DetailsCapturedTarget = null, DetailsExtras = new(), DetailsLoading = false, DetailsError = null });
                }
                _model.Update(state => state with { Setup = state.Setup with { Busy = false, Progress = null,
                    Message = result.Message, Connected = result.Connected }, SetupRequired = !result.Connected,
                    SetupDialogOpen = !result.Connected && state.SetupDialogOpen });
                if (result.Connected)
                {
                    if (_navigation.Value.Route == PlayniteLibraryRoute.PlayniteConnection) _navigation.Back(SetupPrefix + "apply");
                    if (_navigation.Value.Route == PlayniteLibraryRoute.Library)
                        await ReturnToLibraryAsync().ConfigureAwait(false);
                    else if (_navigation.Value.Route == PlayniteLibraryRoute.Browse)
                        await ReloadQueryAsync(preserveContentFocus: true).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { FinishSetupError("Setup was interrupted. Open connection settings to retry or recover it."); }
            catch (Exception error) { FinishSetupError(SetupError(error)); }
            finally { StartPlayniteUpdates(); }
        }, WidgetOperationLifetime.Widget);
    }

    private bool BeginSetup()
    {
        var admitted = _model.Update(state => state.Setup.Busy || state.PlayniteBusy
            ? (state, false) : (state with { Setup = state.Setup with { Busy = true, Progress = "Checking Playnite…", Message = null } }, true)).Result;
        if (admitted) Operations.Cancel(SetupPrefix + "availability");
        return admitted;
    }
    private void RetireConnectionPresentation()
    {
        // Ordinary refresh may retain a same-profile query. Reconnecting cannot:
        // even a materialized old row must lose authority before new reads start.
        Operations.Cancel(IndexedHomeOperation);
        Operations.Cancel(IndexedBrowseOperation);
        Operations.Cancel("playnite-library.warm-state");
        Operations.Cancel("playnite-library.launch");
        Operations.Cancel("playnite-library.launch-lifecycle");
        CancelDetailsOperations();
        achievementHub.Reset(); _hiddenRows.Reset(); progressData.Reset();
        lock (_gate)
        {
            RetireCurrentQueryAuthorityLocked(PlayniteLibraryRoute.Library);
            RetireCurrentQueryAuthorityLocked(PlayniteLibraryRoute.Browse);
            _hasLivePlayniteAuthority = false;
            _livePlayniteAuthority = _presentationAuthority = PlayniteLibraryAuthorityProjection.Empty;
            _createdCategoriesPendingReconciliation.Clear();
            _launchGeneration.Invalidate(); _launchStates.Clear(); _launchStateRecency.Clear();
            PublishHomeContent(new(PlayniteLibraryHomeQuery.Empty), replace: true);
            _indexedBrowse.PublishQuery(new(PlayniteLibraryBrowseQuery.Empty), 0);
            _model.Update(state => state with
            {
                IndexedHome = new() { Attempt = state.IndexedHome.Attempt + 1 },
                IndexedBrowse = new() { Attempt = state.IndexedBrowse.Attempt + 1 },
                FixedRows = PlayniteLibraryFixedRows.Empty, FixedRowsRevision = state.FixedRowsRevision + 1,
                HomeGameCount = null, BrowseGameCount = null, SourceObservations = [], ActiveCategoryId = null,
                HeroSavedId = null, HeroIndex = 0, BrowseHeroSavedId = null, BrowseHeroIndex = 0,
                DetailsItem = null, DetailsCapturedTarget = null, DetailsExtras = new(), DetailsLoading = false,
                DetailsError = null, DetailsGeneration = state.DetailsGeneration + 1, Progress = new(),
                ActiveBrowseReload = null, BrowseInitialFocusId = null, FilterDraft = null,
                LaunchingSavedId = null, ContentEntryScopeId = null, PendingRestoredSavedId = null,
            });
        }
    }
    private void FinishSetupError(string message) => _model.Update(state => state with { Setup = state.Setup with { Busy = false, Progress = null, Message = message } });
    private static string SetupError(Exception error) => error is PlayniteSetupException
        ? error.Message : "Playnite setup could not be completed. Check the selected folders or use Advanced connection settings.";

    private ValueTask<bool> TryHandleSetupActionAsync(WidgetActionEvent action)
    {
        if (!action.ActionId.StartsWith(SetupPrefix, StringComparison.Ordinal)) return ValueTask.FromResult(false);
        if (action.ActionId == SetupPrefix + "dismiss" && LifecycleState == WidgetLifecycleState.Interactive)
        {
            _model.Update(state => state with { SetupDialogOpen = false });
            return ValueTask.FromResult(true);
        }
        if (action.ActionId == SetupPrefix + "prompt" && LifecycleState == WidgetLifecycleState.Interactive)
        {
            _model.Update(state => state with { SetupDialogOpen = state.SetupRequired });
            return ValueTask.FromResult(true);
        }
        if (_setupService is null || LifecycleState != WidgetLifecycleState.Interactive || _model.Value.Setup.Busy) return ValueTask.FromResult(true);
        switch (action.ActionId[SetupPrefix.Length..])
        {
            case "start": ApplySetup(true); break;
            case "apply": ApplySetup(false); break;
            case "scan": RefreshSetup(); break;
            case "folders": RefreshSetup(_model.Value.Setup.ProgramPath, _model.Value.Setup.DataPath); break;
            case "advanced.expand": _model.Update(state => state with { Setup = state.Setup with { Advanced = true } }); break;
            case "advanced.collapse": _model.Update(state => state with { Setup = state.Setup with { Advanced = false } }); break;
            case "program" when action.CommittedText is { } value: _model.Update(state => state with { Setup = state.Setup with { ProgramPath = value } }); break;
            case "data" when action.CommittedText is { } value: _model.Update(state => state with { Setup = state.Setup with { DataPath = value } }); break;
            default:
                if (action.ActionId.StartsWith(SetupPrefix + "select.", StringComparison.Ordinal) &&
                    _model.Value.Setup.Installations.FirstOrDefault(item => item.Id == action.ActionId[(SetupPrefix + "select.").Length..]) is { } selected)
                    RefreshSetup(selected: selected);
                break;
        }
        return ValueTask.FromResult(true);
    }
}
