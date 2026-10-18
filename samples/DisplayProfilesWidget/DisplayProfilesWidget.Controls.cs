using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DisplayProfiles;

public sealed partial class DisplayProfilesWidget
{
    private bool _controlsOpen;
    private WidgetDisplayControlsState? _controls;
    private string? _displayId, _modeId;
    private long _controlsReadSequence;
    private bool CurrentControlsRead(long generation, long sequence) => generation == _generation &&
        sequence == _controlsReadSequence && _controlsOpen && _state?.PendingRestore is null;

    private async ValueTask<bool> HandleControlsAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (!action.ActionId.StartsWith("controls-", StringComparison.Ordinal)) return false;
        long generation, busyOwner, readSequence;
        lock (_gate)
        {
            if (_busy || Operations.IsBusy("display.restore")) return true;
            generation = _generation;
            if (action.ActionId == "controls-back")
            { _controlsOpen = false; ++_controlsReadSequence; _focus = "display.adjust"; Invalidate(); return true; }
            if (action.ActionId.StartsWith("controls-monitor.", StringComparison.Ordinal))
            {
                var id = action.ActionId["controls-monitor.".Length..];
                if (_controls?.Displays.FirstOrDefault(display => display.Id == id) is { } display)
                { _displayId = id; _modeId = display.CurrentModeId ?? display.Modes.FirstOrDefault()?.Id; }
                _toast = null; _toastExpiry.Cancel(); Invalidate(); return true;
            }
            if (action.ActionId.StartsWith("controls-mode.", StringComparison.Ordinal))
            {
                var id = action.ActionId["controls-mode.".Length..];
                if (SelectedDisplay()?.Modes.Any(mode => mode.Id == id) == true) _modeId = id;
                _toast = null; _toastExpiry.Cancel(); Invalidate(); return true;
            }
            if (action.ActionId.StartsWith("controls-resolution.", StringComparison.Ordinal))
            {
                var key = action.ActionId["controls-resolution.".Length..];
                var display = SelectedDisplay();
                var selected = display?.Modes.FirstOrDefault(mode => mode.Id == _modeId);
                var matches = display?.Modes.Where(mode => ResolutionKey(mode) == key).ToArray() ?? [];
                _modeId = matches.FirstOrDefault(mode => selected is not null &&
                    (ulong)mode.RefreshNumerator * selected.RefreshDenominator ==
                    (ulong)selected.RefreshNumerator * mode.RefreshDenominator)?.Id ?? matches.FirstOrDefault()?.Id ?? _modeId;
                _toast = null; _toastExpiry.Cancel(); Invalidate(); return true;
            }
            if (action.ActionId == "controls-apply" && _modeId is not null && SelectedDisplay()?.CurrentModeId == _modeId)
            { Toast("This display mode is already active.", ToastTone.Info); Invalidate(); return true; }
            if (action.ActionId is "controls-apply" or "controls-hdr") return false; // Shared guarded mutation below.
            if (action.ActionId != "controls-open") return true;
            _controlsOpen = true; busyOwner = BeginBusy(); readSequence = ++_controlsReadSequence;
            _focus = _controls?.Displays.Count > 0 ? "display.control.monitor" : "display.control.refresh";
        }
        Invalidate();
        try
        {
            var controls = await HostServices.DisplayProfiles.GetControlsAsync(token).ConfigureAwait(false);
            lock (_gate) if (CurrentControlsRead(generation, readSequence)) AdoptControls(controls, resetSelection: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { lock (_gate) if (CurrentControlsRead(generation, readSequence)) Toast(Message(error), ToastTone.Danger); }
        finally { lock (_gate) EndBusy(busyOwner); Invalidate(); }
        return true;
    }
    private void AdoptControls(WidgetDisplayControlsState controls, bool resetSelection = false)
    {
        _controls = controls;
        var display = SelectedDisplay() ?? controls.Displays.FirstOrDefault();
        var retainedMode = !resetSelection && display?.Id == _displayId && display?.Modes.Any(mode => mode.Id == _modeId) == true;
        _displayId = display?.Id;
        if (!retainedMode) _modeId = display?.CurrentModeId ?? display?.Modes.FirstOrDefault()?.Id;
        if (_focus == "display.control.refresh" && display is not null) _focus = "display.control.monitor";
        if (display is null && _focus.StartsWith("display.control.", StringComparison.Ordinal) && _focus != "display.control.back")
            _focus = "display.control.refresh";
        else if (display is not null &&
            ((_focus is "display.control.resolution" or "display.control.refresh-rate" or "display.control.apply" && _modeId is null) ||
             (_focus == "display.control.hdr" && !display.HdrSupported))) _focus = "display.control.monitor";
    }
    private WidgetDisplayControlMonitor? SelectedDisplay() => _controls?.Displays.FirstOrDefault(display => display.Id == _displayId);
    private WidgetDisplayControlRequest? ControlRequest(string action)
    {
        var display = SelectedDisplay();
        if (display is null) return null;
        return action == "controls-hdr" ? display.HdrSupported ? new(display.Id, HdrEnabled: !display.HdrEnabled) : null
            : display.Modes.Any(mode => mode.Id == _modeId) ? new(display.Id, _modeId) : null;
    }
    private static string ResolutionKey(WidgetDisplayControlMode mode) => $"{mode.Width}-{mode.Height}";
    private WidgetElement ControlsView()
    {
        var children = new List<WidgetElement>
        {
            UI.Row("display.control.header", UI.Text("Adjust displays", "display.control.title").Classes("display-title"),
                UI.Button("Back", "controls-back", "display.control.back")).Classes("display-header")
        };
        var selected = SelectedDisplay();
        if (_controls is null || _controls.Displays.Count == 0)
        {
            children.Add(UI.Text(_busy ? "Reading display controls…" : "Display controls are unavailable.", "display.control.status"));
            children.Add(UI.Button("Refresh", "controls-open", "display.control.refresh").Busy(_busy));
        }
        else
        {
            children.Add(UI.Select("Display", _controls.Displays.Select(display =>
                new SelectOption("monitor." + display.Id, display.Name, "controls-monitor." + display.Id,
                    IsSelected: display.Id == selected?.Id)).ToArray(), "display.control.monitor").Busy(_busy));
            if (selected is not null)
            {
                var mode = selected.Modes.FirstOrDefault(mode => mode.Id == _modeId);
                if (mode is not null)
                {
                    children.Add(UI.Select("Resolution", selected.Modes.DistinctBy(ResolutionKey).Take(128).Select(item =>
                        new SelectOption("resolution." + ResolutionKey(item), $"{item.Width} × {item.Height}",
                            "controls-resolution." + ResolutionKey(item), IsSelected: ResolutionKey(item) == ResolutionKey(mode))).ToArray(),
                            "display.control.resolution").Busy(_busy));
                    children.Add(UI.Select("Refresh rate", selected.Modes.Where(item => ResolutionKey(item) == ResolutionKey(mode)).Take(128).Select(item =>
                        new SelectOption("rate." + item.Id, $"{item.RefreshRate:0.###} Hz", "controls-mode." + item.Id,
                            IsSelected: item.Id == _modeId)).ToArray(), "display.control.refresh-rate").Busy(_busy));
                    children.Add(UI.Button("Apply resolution and refresh", "controls-apply", "display.control.apply").Busy(_busy));
                }
                if (selected.ModesTruncated)
                    children.Add(UI.Text("More display modes are available in Windows Display settings.", "display.control.more-modes").Classes("display-summary"));
                if (selected.UnavailableReason is not null)
                    children.Add(UI.Text(selected.UnavailableReason, "display.control.reason").Classes("display-summary"));
                children.Add(selected.HdrSupported
                    ? UI.Row("display.control.hdr-row",
                        UI.Text(selected.HdrEnabled ? selected.HdrActive ? "HDR · On" : "HDR · Enabled, currently inactive" : "HDR · Off", "display.control.hdr-state")
                            .Classes("display-control-copy"),
                        UI.Button(selected.HdrEnabled ? "Turn HDR off" : "Turn HDR on", "controls-hdr", "display.control.hdr").Busy(_busy))
                            .Classes("display-actions")
                    : UI.Text("HDR control isn't available for this display and Windows configuration.", "display.control.hdr-unavailable").Classes("display-summary"));
            }
        }
        children.Add(UI.Text("Changes use a timed Keep / Revert check. Saved profiles are changed only when you save or replace one.",
            "display.control.help").Classes("display-summary"));
        return UI.VerticalScroll("display.controls", children.ToArray()).Classes("display-controls");
    }
}
