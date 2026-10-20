using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    internal const string SetupPrefix = "playnite-library.setup.";
    internal const string SetupStartAction = SetupPrefix + "start";
    private readonly IPlayniteSetupService? _setupService;

    private void RefreshStoppedPlaynite()
    {
        if (_setupService is null || _model.Value.Setup.Busy) return;
        _ = Operations.RunLatest(SetupPrefix + "availability", async context =>
        {
            try
            {
                var stopped = await _setupService.StoppedInstallationAsync(context.CancellationToken).ConfigureAwait(false);
                context.CancellationToken.ThrowIfCancellationRequested();
                _model.Update(state => state.Setup.Busy || context.CancellationToken.IsCancellationRequested ? state : state with
                {
                    StoppedPlaynite = stopped,
                    StoppedPlayniteDialogOpen = stopped is not null && state.DetailsItem is null && state.FilterDraft is null,
                });
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
            catch (Exception) { _model.Update(state => state with { StoppedPlaynite = null }); }
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
                selected ??= found.Installations.Count == 1 ? found.Installations[0] : null;
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
        var stopped = _model.Value.StoppedPlaynite;
        if (startOnly ? stopped is null : plan?.CanApply != true) { FinishSetupError("Find and select Playnite before connecting."); return; }
        _ = Operations.RunLatest(SetupPrefix + "work", async context =>
        {
            try
            {
                void Progress(string message) => _model.Update(state => state with { Setup = state.Setup with { Progress = message } });
                var result = startOnly
                    ? await _setupService.StartAsync(stopped!, Progress, context.CancellationToken).ConfigureAwait(false)
                    : await _setupService.ApplyAsync(plan!, Progress, context.CancellationToken).ConfigureAwait(false);
                _model.Update(state => state with { Setup = state.Setup with { Busy = false, Progress = null,
                    Message = result.Message, Connected = result.Connected }, StoppedPlaynite = result.Connected ? null : state.StoppedPlaynite,
                    StoppedPlayniteDialogOpen = !result.Connected && state.StoppedPlayniteDialogOpen });
                if (result.Connected)
                {
                    _application.ResetConnectionCache();
                    _playniteConnection.Reset();
                    _model.Update(state => state with { PlayniteKind = PlayniteBridgeConnectionKind.Connected, PlayniteCode = "connected" });
                    if (_navigation.Value.Route == PlayniteLibraryRoute.PlayniteConnection) _navigation.Back(SetupPrefix + "apply");
                    if (_navigation.Value.Route == PlayniteLibraryRoute.Library)
                        await ReturnToLibraryAsync().ConfigureAwait(false);
                    else if (_navigation.Value.Route == PlayniteLibraryRoute.Browse)
                        await ReloadQueryAsync(preserveContentFocus: true).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { FinishSetupError("Setup was interrupted. Open connection settings to retry or recover it."); }
            catch (Exception error) { FinishSetupError(SetupError(error)); }
        }, WidgetOperationLifetime.Widget);
    }

    private bool BeginSetup()
    {
        var admitted = _model.Update(state => state.Setup.Busy || state.PlayniteBusy
            ? (state, false) : (state with { Setup = state.Setup with { Busy = true, Progress = "Checking Playnite…", Message = null } }, true)).Result;
        if (admitted) Operations.Cancel(SetupPrefix + "availability");
        return admitted;
    }
    private void FinishSetupError(string message) => _model.Update(state => state with { Setup = state.Setup with { Busy = false, Progress = null, Message = message } });
    private static string SetupError(Exception error) => error is PlayniteSetupException
        ? error.Message : "Playnite setup could not be completed. Check the selected folders or use Advanced connection settings.";

    private ValueTask<bool> TryHandleSetupActionAsync(WidgetActionEvent action)
    {
        if (!action.ActionId.StartsWith(SetupPrefix, StringComparison.Ordinal)) return ValueTask.FromResult(false);
        if (action.ActionId == SetupPrefix + "dismiss" && LifecycleState == WidgetLifecycleState.Interactive)
        {
            _model.Update(state => state with { StoppedPlayniteDialogOpen = false });
            return ValueTask.FromResult(true);
        }
        if (action.ActionId == SetupPrefix + "prompt" && LifecycleState == WidgetLifecycleState.Interactive)
        {
            _model.Update(state => state with { StoppedPlayniteDialogOpen = state.StoppedPlaynite is not null });
            return ValueTask.FromResult(true);
        }
        if (_setupService is null || LifecycleState != WidgetLifecycleState.Interactive || _model.Value.Setup.Busy) return ValueTask.FromResult(true);
        switch (action.ActionId[SetupPrefix.Length..])
        {
            case "start": ApplySetup(true); break;
            case "apply": ApplySetup(false); break;
            case "scan": RefreshSetup(); break;
            case "folders": RefreshSetup(_model.Value.Setup.ProgramPath, _model.Value.Setup.DataPath); break;
            case "advanced": _model.Update(state => state with { Setup = state.Setup with { Advanced = !state.Setup.Advanced } }); break;
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
