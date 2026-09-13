using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Power;

public sealed class PowerWidget : Widget
{
    private readonly object _gate = new();
    private readonly WidgetTimedMutation _toastExpiry;
    private WidgetPowerAvailability _availability = new(false, false, false);
    private bool _loaded;
    private bool _busy;
    private long _generation;
    private string? _pending;
    private string _focus = "power.sleep";
    private string? _toast;
    private ToastTone _tone;

    public PowerWidget() => _toastExpiry = CreateTimedMutation();

    protected override async ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        lock (_gate) { ++_generation; _pending = null; _focus = "power.sleep"; _loaded = false; }
        await LoadAsync(activeLifetime).ConfigureAwait(false);
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        lock (_gate) { ++_generation; _pending = null; _toast = null; _toastExpiry.Cancel(); }
        return ValueTask.CompletedTask;
    }

    private async ValueTask LoadAsync(CancellationToken token)
    {
        long generation;
        lock (_gate) generation = _generation;
        try
        {
            var availability = await HostServices.Power.GetAvailabilityAsync(token).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation) return;
                _availability = availability;
                _focus = availability.CanSleep ? "power.sleep" :
                    availability.CanRestart ? "power.restart" : availability.CanShutDown ? "power.shutdown" : "power.check";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (_gate) if (generation == _generation)
            {
                _availability = new(false, false, false);
                _focus = "power.check";
                ShowToastLocked(FriendlyError(error), ToastTone.Danger);
            }
        }
        finally
        {
            lock (_gate) if (generation == _generation) _loaded = true;
            Invalidate();
        }
    }

    public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        string? command = null;
        long generation;
        lock (_gate)
        {
            if (_busy || !_loaded) return;
            generation = _generation;
            if (action.ActionId == "cancel" && _pending is not null)
            {
                _focus = "power." + _pending; _pending = null;
            }
            else if (_pending is null && action.ActionId is "shutdown" or "restart" && Available(action.ActionId))
            {
                _pending = action.ActionId; _focus = "power.cancel"; _toast = null;
            }
            else if (_pending is not null && action.ActionId == "confirm." + _pending && Available(_pending))
            {
                command = _pending; _pending = null; _focus = "power." + command; _busy = true;
            }
            else if (_pending is null && action.ActionId == "sleep" && _availability.CanSleep)
            {
                command = "sleep"; _busy = true;
            }
            else if (_pending is null && action.ActionId == "check") _busy = true;
            else return;
        }
        Invalidate();
        if (command is null && action.ActionId != "check") return;
        try
        {
            if (command == "shutdown") await HostServices.Power.ShutDownAsync(cancellationToken).ConfigureAwait(false);
            else if (command == "restart") await HostServices.Power.RestartAsync(cancellationToken).ConfigureAwait(false);
            else if (command == "sleep") await HostServices.Power.SleepAsync(cancellationToken).ConfigureAwait(false);
            else await LoadAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate) if (generation == _generation && command is "shutdown" or "restart")
                ShowToastLocked("Request sent to Windows. An open app may ask you to save your work.", ToastTone.Info);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (_gate) if (generation == _generation) ShowToastLocked(FriendlyError(error), ToastTone.Danger);
        }
        finally
        {
            lock (_gate) _busy = false;
            Invalidate();
        }
    }

    private bool Available(string command) => command switch
    {
        "shutdown" => _availability.CanShutDown, "restart" => _availability.CanRestart,
        "sleep" => _availability.CanSleep, _ => false,
    };

    private void ShowToastLocked(string text, ToastTone tone)
    {
        _toast = text; _tone = tone;
        _toastExpiry.ScheduleLatest(UI.DefaultToastDuration, () =>
        {
            lock (_gate) _toast = null;
            Invalidate();
        });
    }

    private static string FriendlyError(Exception error) => (error as WidgetCapabilityException)?.ErrorCode switch
    {
        "permission_denied" or "capability_revoked" => "Allow PC power control in this widget's permissions in Settings, then try again.",
        "power_denied" => "Windows isn't allowing this power option. Try it from the Windows Start menu.",
        "power_unavailable" => "This power option isn't available on your PC right now.",
        "power_busy" => "A power request is already in progress. Please wait.",
        _ => "Windows couldn't complete the request. Try again or use the Windows Start menu.",
    };

    public override WidgetView Render()
    {
        lock (_gate)
        {
            var children = new List<WidgetElement>
            {
                UI.Text("Power", "power.title").Classes("power-title"),
                UI.Text(_pending is null ? "Sleep, restart or shut down your PC." : "Save your work before continuing.", "power.subtitle").Classes("power-subtitle"),
            };
            string scope;
            if (_pending is not null)
            {
                var restart = _pending == "restart";
                children.Add(UI.Text(restart ? "Restart your PC?" : "Shut down your PC?", "power.question").Classes("power-question"));
                children.Add(UI.Text(restart ? "Closes your apps and starts Windows again." :
                    "Closes your apps and turns off your PC.", "power.explanation").Classes("power-help"));
                children.Add(UI.Row("power.confirmation",
                    UI.Button("Cancel", "cancel", "power.cancel").FocusRight("power.confirm").Classes("power-cancel"),
                    UI.Button(restart ? "Restart PC" : "Shut down PC", "confirm." + _pending, "power.confirm")
                        .FocusLeft("power.cancel").Classes("power-confirm")).Classes("power-actions"));
                scope = "power.confirm." + _pending;
            }
            else
            {
                children.Add(UI.Row("power.options",
                    Option("Sleep", "Keep your apps open", "sleep", WidgetGlyph.Pause),
                    Option("Restart", "Start Windows again", "restart", WidgetGlyph.Refresh),
                    Option("Shut down", "Turn off your PC", "shutdown", WidgetGlyph.Settings)).Classes("power-options"));
                children.Add(UI.Text(!_loaded ? "Checking power options..." : _busy ? "Waiting for Windows..." :
                    !_availability.CanSleep && !_availability.CanRestart && !_availability.CanShutDown ? "Windows power options are unavailable. Try Check again." :
                    !_availability.CanSleep ? "Sleep isn't available on this PC right now." :
                    "Shut down and Restart will ask you to confirm.", "power.help").Classes("power-help"));
                children.Add(UI.Button("Check again", "check", "power.check").Busy(_busy).Classes("power-check"));
                scope = "power";
            }
            if (_toast is not null) children.Add(UI.Toast("Power", _toast, _tone, "power.toast"));
            var root = UI.Stack("power.root", children.ToArray()).InputScope(scope).Classes("power-widget");
            if (_pending is not null) root = root.Shortcut(ControllerButton.B, "cancel");
            return new(root, _focus, Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard, PreferredWidth = 680, PreferredHeight = 380,
                MinimumWidth = 540, MinimumHeight = 380,
            });
        }
    }

    private ActionSurfaceElement Option(string title, string hint, string action, WidgetGlyph fallback) =>
        UI.ActionSurface(action, "power." + action, title + " PC", ActionSurfaceOrientation.Vertical,
            UI.Icon(WidgetIcon.PackageSvg("power." + action, WidgetPackageIconColorMode.ThemeTint, fallback),
                "power." + action + ".icon", title).Classes("power-icon"),
            UI.Text(title, "power." + action + ".title").Classes("power-option-title"),
            UI.Text(hint, "power." + action + ".hint").Classes("power-option-hint"))
            .Disabled(!_loaded || !Available(action)).Busy(_busy).Classes("power-option");
}
