using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.DisplayProfiles;

public sealed class DisplayProfilesWidget : Widget
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly WidgetTimedMutation _toastExpiry;
    private readonly WidgetTimedMutation _countdown;
    private WidgetDisplayProfilesState? _state;
    private string? _error, _toast, _editor, _selected, _confirm, _outcome;
    private string _editorText = "";
    private ToastTone _tone;
    private string _focus = "display.save";
    private bool _busy;
    private long _generation;
    private Task _watch = Task.CompletedTask;

    public DisplayProfilesWidget()
    {
        _toastExpiry = CreateTimedMutation();
        _countdown = CreateTimedMutation();
    }

    protected override async ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        long generation;
        lock (_gate) { generation = ++_generation; _editor = _confirm = null; _busy = false; }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _watch = WatchAsync(generation, activeLifetime, ready);
        await ready.Task.WaitAsync(activeLifetime).ConfigureAwait(false);
    }
    private async Task WatchAsync(long generation, CancellationToken token, TaskCompletionSource ready)
    {
        try
        {
            await using var subscription = await HostServices.Capabilities.OpenSubscriptionAsync(
                WidgetDisplayProfilesCapabilities.Changed, token).ConfigureAwait(false);
            await LoadAsync(generation, token).ConfigureAwait(false);
            ready.TrySetResult();
            await foreach (var _ in subscription.ReadAllAsync(token).ConfigureAwait(false))
                await LoadAsync(generation, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (_gate) if (generation == _generation) { _error = Message(error); if (_state is null) _focus = "display.retry"; }
            Invalidate();
        }
        finally { ready.TrySetResult(); }
    }
    private async Task LoadAsync(long generation, CancellationToken token)
    {
        await _refresh.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = await HostServices.DisplayProfiles.GetAsync(token).ConfigureAwait(false);
            lock (_gate) if (generation == _generation) Adopt(state);
            Invalidate();
        }
        finally { _refresh.Release(); }
    }
    private void Adopt(WidgetDisplayProfilesState state)
    {
        var priorPending = _state?.PendingRestore?.Id;
        _state = state; _error = null;
        if (_focus == "display.retry") _focus = "display.save";
        if (state.PendingRestore is { } pending)
        {
            _editor = _confirm = null;
            if (priorPending != pending.Id) _focus = "display.revert";
            TickCountdown();
        }
        else
        {
            _countdown.Cancel();
            if (priorPending is not null) _focus = ProfileFocus(_selected);
        }
        if (state.Outcome is { } outcome && outcome != _outcome)
        {
            _outcome = outcome;
            Toast(outcome switch
            {
                "kept" => "Display setup kept.",
                "reverted" => "Previous display setup restored.",
                "rollback-failed" => "Windows couldn't restore the previous setup. Open Windows Display settings.",
                _ => "Windows couldn't finish the display change.",
            }, outcome is "kept" or "reverted" ? ToastTone.Info : ToastTone.Danger);
        }
    }
    private void TickCountdown()
    {
        _countdown.ScheduleLatest(TimeSpan.FromSeconds(1), () =>
        {
            lock (_gate)
            {
                if (_state?.PendingRestore is null) return;
                TickCountdown();
            }
            Invalidate();
        });
    }
    protected override async ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        lock (_gate) { ++_generation; _toastExpiry.Cancel(); _countdown.Cancel(); _toast = null; }
        await _watch.ConfigureAwait(false);
    }
    public override ValueTask<bool> OnControllerInputAsync(ControllerInputEvent input, CancellationToken cancellationToken = default)
    {
        lock (_gate) if (input.FocusedElementId is { } id) _focus = id;
        return base.OnControllerInputAsync(input, cancellationToken);
    }
    public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        string? id, editor, confirmation;
        long generation;
        lock (_gate)
        {
            if (_busy) return;
            generation = _generation;
            if (action.FocusedElementId is { } focused && focused.StartsWith("display.profile.", StringComparison.Ordinal))
                _selected = focused["display.profile.".Length..];
            if (action.ActionId == "name-new")
            { _editor = "save"; _editorText = ""; _focus = "display.name"; Invalidate(); return; }
            if (action.ActionId == "cancel")
            { _editor = _confirm = null; _focus = ProfileFocus(_selected); Invalidate(); return; }
            var split = action.ActionId.Split('.', 2);
            if (split.Length == 2 && split[0] is "rename" or "replace" or "delete" or "apply")
            {
                _selected = split[1];
                var profile = _state?.Profiles.FirstOrDefault(profile => profile.Id == _selected);
                if (profile is null) return;
                if (split[0] == "rename")
                { _editor = "rename"; _editorText = profile.Name; _focus = "display.name"; Invalidate(); return; }
                if (split[0] is "replace" or "delete")
                { _confirm = split[0]; _focus = "display.cancel"; Invalidate(); return; }
                if (!profile.Available || profile.MatchesCurrent)
                { Toast(profile.UnavailableReason ?? "This display setup is already active.", ToastTone.Info); Invalidate(); return; }
            }
            id = _selected; editor = _editor; confirmation = _confirm;
            if (action.ActionId == "commit-name")
            {
                if (action.CommittedText is not { } text) return;
                _editorText = text;
            }
            _busy = true;
        }
        Invalidate();
        try
        {
            WidgetDisplayProfilesState state;
            if (action.ActionId == "retry")
            {
                if (_watch.IsCompleted)
                {
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _watch = WatchAsync(generation, ActiveCancellationToken, ready);
                    await ready.Task.WaitAsync(cancellationToken);
                    return;
                }
                state = await HostServices.DisplayProfiles.GetAsync(cancellationToken);
            }
            else if (action.ActionId == "commit-name")
                state = editor == "save"
                    ? await HostServices.DisplayProfiles.SaveAsync(action.CommittedText!, cancellationToken)
                    : await HostServices.DisplayProfiles.RenameAsync(id!, action.CommittedText!, cancellationToken);
            else if (action.ActionId == "confirm" && confirmation is not null)
                state = confirmation == "delete"
                    ? await HostServices.DisplayProfiles.DeleteAsync(id!, cancellationToken)
                    : await HostServices.DisplayProfiles.ReplaceAsync(id!, cancellationToken);
            else if (action.ActionId.StartsWith("apply.", StringComparison.Ordinal))
                state = await HostServices.DisplayProfiles.ApplyAsync(id!, cancellationToken);
            else if (action.ActionId is "keep" or "revert")
            {
                string? restore;
                lock (_gate) restore = _state?.PendingRestore?.Id;
                if (restore is null) return;
                state = action.ActionId == "keep"
                    ? await HostServices.DisplayProfiles.KeepAsync(restore, cancellationToken)
                    : await HostServices.DisplayProfiles.RevertAsync(restore, cancellationToken);
            }
            else return;
            lock (_gate)
            {
                if (generation != _generation) return;
                Adopt(state);
                if (action.ActionId is "commit-name" or "confirm")
                {
                    _editor = _confirm = null;
                    if (editor == "save") _selected = state.Profiles.LastOrDefault()?.Id;
                    _focus = ProfileFocus(_selected);
                    Toast(action.ActionId == "commit-name" ? "Profile saved." :
                        confirmation == "delete" ? "Profile deleted." : "Profile replaced with the current setup.", ToastTone.Info);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { lock (_gate) if (generation == _generation) Toast(Message(error), ToastTone.Danger); }
        finally
        {
            lock (_gate) if (generation == _generation) _busy = false;
            Invalidate();
        }
    }
    private string ProfileFocus(string? id) => id is not null && _state?.Profiles.Any(profile => profile.Id == id) == true
        ? "display.profile." + id : "display.save";
    private void Toast(string text, ToastTone tone)
    {
        _toast = text; _tone = tone;
        _toastExpiry.ScheduleLatest(UI.DefaultToastDuration, () => { lock (_gate) _toast = null; Invalidate(); });
    }
    private static string Message(Exception error) => (error as WidgetCapabilityException)?.ErrorCode switch
    {
        "permission_denied" or "capability_revoked" =>
            "Allow display access in Settings → Widgets → Display Profiles, then reopen this widget.",
        "invalid_payload" => "Enter a profile name of 1 to 60 characters.",
        "display_profile_name_exists" => "A profile with this name already exists. Choose another name.",
        "display_profile_limit" => "You can save up to 24 profiles. Delete one before saving another.",
        "display_profile_missing" => "This profile no longer exists.",
        "display_monitor_missing" => "A required monitor is disconnected. Connect it and try again.",
        "display_monitor_ambiguous" => "Windows cannot identify a saved monitor reliably. Reconnect it or save a new profile.",
        "display_topology_unavailable" or "display_mode_unavailable" =>
            "The connected displays do not support this setup. Check Windows Display settings and save the profile again.",
        "display_busy" => "Another display change is still finishing. Keep or revert it, then try again.",
        "display_restore_expired" => "This display confirmation has ended.",
        "display_guard_unavailable" => "Windows couldn't start the restore safety check. Start WidgetRail normally and try again.",
        "display_restore_failed" => "Windows couldn't complete the display change. Check your setup and try again.",
        "display_rollback_failed" => "Windows couldn't restore the previous setup. Open Windows Display settings.",
        "display_profile_invalid" or "display_profile_store_invalid" =>
            "Saved display profiles could not be read. Your existing data has been kept.",
        "display_profile_io" => "Display profiles could not be saved or read. Check disk space and folder access.",
        "display_denied" => "Windows isn't allowing display access in this session.",
        "display_changing" => "The displays are still changing. Try again shortly.",
        _ => "Display profiles are unavailable. Try again.",
    };

    public override WidgetView Render()
    {
        lock (_gate)
        {
            var children = new List<WidgetElement>();
            var scope = "display.main";
            if (_state?.PendingRestore is { } pending)
            {
                scope = "display.restore." + pending.Id;
                var seconds = Math.Max(0, (int)Math.Ceiling((pending.Deadline - DateTimeOffset.UtcNow).TotalSeconds));
                children.Add(UI.Stack("display.confirmation",
                    UI.Text("Keep this display setup?", "display.pending.title").Classes("display-dialog-title"),
                    UI.Text(pending.ProfileName, "display.pending.name"),
                    UI.Text(seconds > 0 ? $"Reverting in {seconds} seconds" : "Restoring the previous setup…", "display.countdown").Classes("display-countdown"),
                    UI.Row("display.pending.actions",
                        UI.Button("Keep changes", "keep", "display.keep").Busy(_busy).Disabled(seconds == 0),
                        UI.Button("Revert", "revert", "display.revert").Busy(_busy)).Classes("display-actions")).Classes("display-dialog"));
            }
            else if (_editor is not null)
            {
                scope = "display.editor." + _editor;
                children.Add(UI.Stack("display.editor",
                    UI.Text(_editor == "save" ? "Save current display setup" : "Rename profile", "display.editor.title").Classes("display-dialog-title"),
                    UI.TextEntry(_editorText, "Profile name", "commit-name", "display.name", 60).Disabled(_busy).Classes("display-name"),
                    UI.Button("Cancel", "cancel", "display.cancel").Busy(_busy)).Classes("display-dialog"));
            }
            else if (_confirm is not null)
            {
                scope = "display.manage." + _confirm;
                children.Add(UI.Stack("display.management",
                    UI.Text(_confirm == "delete" ? "Delete this profile?" : "Replace with your current setup?", "display.manage.title").Classes("display-dialog-title"),
                    UI.Text(_state?.Profiles.FirstOrDefault(profile => profile.Id == _selected)?.Name ?? "Profile", "display.manage.name"),
                    UI.Row("display.manage.actions",
                        UI.Button("Cancel", "cancel", "display.cancel"),
                        UI.Button(_confirm == "delete" ? "Delete profile" : "Replace profile", "confirm", "display.confirm").Busy(_busy)).Classes("display-actions")).Classes("display-dialog"));
            }
            else
            {
                children.Add(UI.Row("display.header",
                    UI.Text("Display Profiles", "display.title").Classes("display-title"),
                    UI.ActionSurface("name-new", "display.save", "Save current display setup", ActionSurfaceOrientation.Horizontal,
                        UI.ControllerHint(ControllerButton.Y, "Save current setup", "display.save.hint"))
                        .Busy(_busy).Disabled(_state is null).Classes("display-save")).Classes("display-header"));
                if (_state is { } state)
                {
                    children.Add(UI.Stack("display.current",
                        UI.Text($"Current setup · {state.Profiles.FirstOrDefault(profile => profile.MatchesCurrent)?.Name ?? state.Mode}",
                            "display.current.title").Classes("display-section-title"),
                        MonitorStrip(state.Displays, "display.current.monitors"),
                        UI.Text(Summary(state.Displays), "display.current.summary").Classes("display-summary")).Classes("display-current"));
                    children.Add(state.Profiles.Count == 0
                        ? UI.Stack("display.empty", UI.Text("Your setups, one press away", "display.empty.title").Classes("display-profile-name"),
                            UI.Text("Set up your displays in Windows, then save them here as Desk, TV gaming or another profile.", "display.empty.help").Classes("display-summary")).Classes("display-empty")
                        : UI.VerticalScroll("display.profiles", state.Profiles.Select(ProfileCard).ToArray()).Classes("display-list"));
                }
                else children.Add(UI.Text(_error ?? "Reading your displays…", "display.loading").Classes("display-summary"));
                if (_error is not null) children.Add(UI.Button("Try again", "retry", "display.retry").Busy(_busy));
                children.Add(UI.Text("Windows manages scaling. Monitor VRR changes can make saved profiles incompatible.",
                    "display.warning").Classes("display-help", "display-warning"));
            }
            if (_toast is not null) children.Add(UI.Toast("Display Profiles", _toast, _tone, "display.toast"));
            var root = UI.Stack("display.root", children.ToArray()).InputScope(scope).Classes("display-widget");
            if (_state?.PendingRestore is not null) root = root.Shortcut(ControllerButton.B, "revert", "Revert");
            else if (_editor is not null || _confirm is not null) root = root.Shortcut(ControllerButton.B, "cancel", "Cancel");
            else if (_state is not null) root = root.Shortcut(ControllerButton.Y, "name-new", "Save current setup");
            return new(root, _focus, ActiveInputScopeId: scope, Surface: new WidgetSurfaceHints
            { HeightMode = WidgetSurfaceAxisMode.Content, PreferredWidth = 780, PreferredHeight = 700, MinimumWidth = 640, MinimumHeight = 420 });
        }
    }
    private WidgetElement ProfileCard(WidgetDisplayProfileSummary profile)
    {
        var id = "display.profile." + profile.Id;
        return UI.ActionSurface("apply." + profile.Id, id, "Apply " + profile.Name, ActionSurfaceOrientation.Horizontal,
            MonitorStrip(profile.Displays, id + ".monitors"),
            UI.Stack(id + ".copy", UI.Text(profile.Name, id + ".name").Classes("display-profile-name"),
                UI.Text(profile.Mode + " · " + Summary(profile.Displays), id + ".summary").Classes("display-summary"),
                UI.Text(profile.MatchesCurrent ? "Matches current setup" : profile.UnavailableReason ?? "Ready to apply", id + ".status")
                    .Classes(profile.MatchesCurrent ? "display-current-badge" : profile.Available ? "display-summary" : "display-unavailable")).Classes("display-card-copy"))
            .Busy(_busy).ContextMenuShortcut(ControllerButton.Menu)
            .ContextAction("rename." + profile.Id, "Rename")
            .ContextAction("replace." + profile.Id, "Replace with current setup")
            .ContextAction("delete." + profile.Id, "Delete profile", WidgetContextActionStyle.Danger).Classes("display-card");
    }
    private static WidgetElement MonitorStrip(IReadOnlyList<WidgetDisplayProfileMonitor> displays, string id) =>
        UI.Row(id, displays.OrderBy(display => display.X).ThenBy(display => display.Y).Take(4).Select((display, index) =>
            UI.Stack(id + "." + index,
                UI.Icon(WidgetIcon.PackageSvg("display.monitor", WidgetPackageIconColorMode.ThemeTint, WidgetGlyph.Fullscreen),
                    id + "." + index + ".icon", display.Name).Classes(display.IsPrimary ? ["display-monitor-icon", "display-monitor-primary"] : ["display-monitor-icon"]),
                UI.Text(display.IsPrimary ? "Primary" : display.Orientation, id + "." + index + ".label").Classes("display-monitor-label"))
                .Classes("display-monitor")).ToArray()).Classes("display-monitor-strip");
    private static string Summary(IReadOnlyList<WidgetDisplayProfileMonitor> displays) => string.Join(" · ",
        displays.Select(display => $"{display.Name} {display.Width}×{display.Height} {display.RefreshRate:0.###} Hz"));
}
