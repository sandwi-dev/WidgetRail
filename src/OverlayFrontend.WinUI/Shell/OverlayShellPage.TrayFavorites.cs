using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private sealed record Favorite(string Label, string? Command = null, WidgetPresentationFrame? Frame = null,
        WidgetQuickAction? Action = null, string? FallbackFor = null, BridgeWidgetDescriptor? Prepare = null,
        TrayCaptureCommand? ExpectedCommand = null, string? Reason = null);
    private sealed record Assignable(string Group, TrayActionBinding Binding, TrayActionDefinition Definition);
    private static readonly TrayDirection[] Directions = Enum.GetValues<TrayDirection>();
    private readonly Favorite?[] displayedFavorites = new Favorite?[4];
    private readonly Dictionary<string, BridgeWidgetDescriptor> favoriteProviders = new(StringComparer.Ordinal);
    private sealed class FavoriteInvocation(TrayActionBinding binding, string label)
    {
        internal TrayActionBinding Binding { get; } = binding;
        internal string Label { get; } = label;
        internal bool ShowProgress { get; set; }
    }
    private readonly Dictionary<TrayDirection, FavoriteInvocation> runningFavorites = [];
    private ushort favoriteHeld;

    private Favorite? ResolveFavorite(TrayDirection direction, WidgetPresentationFrame? prepared = null)
    {
        if (preferences.TrayActions.Get(direction) is not { } binding)
            return null;
        if (!favoriteProviders.TryGetValue(binding.Provider, out var descriptor) || descriptor.PublisherId != binding.Publisher || descriptor.PackageId != binding.Package)
            return new(binding.Label ?? "Widget unavailable", Reason: "The widget assigned to this shortcut is no longer available.");
        var definition = descriptor.AvailableTrayActions.FirstOrDefault(item => item.Id == binding.Action);
        if (definition is null)
            return new(binding.Label ?? "Action unavailable", Reason: $"{descriptor.Name} no longer provides this action.");
        if (descriptor.IsStopped)
            return new(definition.Label, Reason: $"Start {descriptor.Name} to use this action.");
        var alternate = definition.FallbackId == binding.Fallback && binding.Fallback is not null
            ? descriptor.AvailableTrayActions.FirstOrDefault(item => item.Id == binding.Fallback) : null;
        if (prepared is null && definition.StatusOperation is not null &&
            (alternate is null || alternate.StatusOperation is not null))
        {
            var recorder = ReplayStatusForHost;
            var status = TrayReplayState.From(recorder);
            if (status.CommandFor(definition.StatusOperation) is { } primary)
                return new(definition.Label, definition.Id, Prepare: descriptor, ExpectedCommand: primary);
            if (alternate is not null && status.CommandFor(alternate.StatusOperation) is { } fallbackCommand)
                return new(alternate.Label, alternate.Id, FallbackFor: binding.Action, Prepare: descriptor, ExpectedCommand: fallbackCommand);
            return new(definition.Label, Reason: ReplayUnavailableReason(recorder, definition.StatusOperation));
        }
        // A background preparation authorizes the press that requested it. A hidden
        // worker can finish work or unload without publishing another visible frame.
        var frame = prepared;
        if (frame is null && (!definition.WakeOnInvoke || LifecycleFor(descriptor.Id) != WidgetLifecycleState.Background))
            frame = owner?.Session.GetState(descriptor.Id) is { Failure: null } state ? state.LastGood : null;
        if (frame is not null && SameSurfaceOwner(frame.Descriptor, descriptor))
        {
            var actions = frame.Snapshot.QuickActions;
            var action = actions.FirstOrDefault(item => item.TrayOffer?.Id == binding.Action);
            if (action is null && binding.Fallback is { } fallback && definition.FallbackId == fallback)
                action = actions.FirstOrDefault(item => item.TrayOffer is { } offer && offer.Id == fallback && offer.AvailableAsFallbackFor == binding.Action);
            if (action?.TrayOffer is { } offer && (offer.EmbeddedSessionId is null ||
                offer.EmbeddedSessionId == frame.Snapshot.EmbeddedMediaSession?.Id && mediaOwner?.IsReady(descriptor.Id) == true))
                return new(action.Label, offer.Id, Frame: frame, Action: action, FallbackFor: offer.Id != binding.Action ? binding.Action : null);
            if (action?.TrayOffer?.EmbeddedSessionId is not null)
                return new(definition.Label, Reason: $"Open the player in {descriptor.Name} and wait for it to load.");
            if (prepared is not null || !definition.WakeOnInvoke)
            {
                var reasons = frame.Snapshot.UnavailableTrayActions;
                var reason = reasons?.GetValueOrDefault(definition.Id) ??
                    (alternate is null ? null : reasons?.GetValueOrDefault(alternate.Id));
                return new(definition.Label, Reason: reason ?? $"{descriptor.Name} cannot use this action right now.");
            }
        }
        if (!definition.WakeOnInvoke) return new(definition.Label, Reason: $"Open {descriptor.Name} to make its controls available.");
        var label = alternate is null ? definition.Label : definition.Label + " / " + alternate.Label;
        return new(label, definition.Id, Prepare: descriptor);
    }

    private static string ReplayUnavailableReason(CaptureReplayStatus? recorder, string operation) => recorder switch
    {
        null or { Issue: CaptureReplayIssue.HostUnavailable or CaptureReplayIssue.FeatureRemoved } => "Replay recording is unavailable.",
        { Saving: true } when operation == "capture.replay.save" => "A replay is already being saved.",
        { Phase: CaptureReplayPhase.Stopping } => "Replay recording is stopping.",
        { Phase: CaptureReplayPhase.Waiting } => "Replay is waiting for an approved application.",
        { Phase: CaptureReplayPhase.Starting } => "Replay recording is starting.",
        { Issue: CaptureReplayIssue.PermissionRevoked } => "Open Capture Library to grant recording access.",
        { Phase: CaptureReplayPhase.Error } => "Open Capture Library to check the recorder error.",
        { Phase: CaptureReplayPhase.Paused } when operation == "capture.replay.save" => "Resume the recording application to fill the replay buffer.",
        { Phase: CaptureReplayPhase.Recording } when operation == "capture.replay.save" => "The replay buffer is still filling.",
        _ when operation == "capture.replay.start" => "Replay recording is already enabled.",
        _ when operation == "capture.replay.save" => "Start replay recording before saving a replay.",
        _ => "Replay recording is off."
    };

    private void RefreshFavoriteHints(string? changedProvider = null)
    {
        RefreshFavoriteProgress();
        if (!RadialOpen || radialView is null) return;
        foreach (var direction in Directions)
        {
            if (changedProvider is not null && preferences.TrayActions.Get(direction)?.Provider != changedProvider)
                continue;
            var favorite = !reordering ? ResolveFavorite(direction) : null;
            displayedFavorites[(int)direction] = favorite;
            var work = runningFavorites.GetValueOrDefault(direction);
            var running = !reordering && work is not null && work.Binding == preferences.TrayActions.Get(direction);
            radialView.SetFavorite(direction, running ? work!.Label : favorite?.Label, favorite?.Command is not null);
        }
    }

    private async Task InvokeFavoriteAsync(TrayDirection direction, Favorite? captured, ControllerInputOrigin origin)
    {
        if (IntroductionRunning && introduction.Step == "commands" && foreground && visible && !retired)
        {
            introductionDirectionHint = captured is null ? $"{direction} is empty. After the tour this opens the chooser." :
                $"{direction} is assigned to {captured.Label}.";
            RefreshIntroduction();
            return;
        }
        if (runningFavorites.TryGetValue(direction, out var currentWork) && currentWork.Binding == preferences.TrayActions.Get(direction)) return;
        if (interactive || !foreground || !visible || retired || reordering || HostChoiceActive)
            return;
        FavoriteInvocation? invocation = null;
        long? captureSequence = null;
        try
        {
            if (captured is null && preferences.TrayActions.Get(direction) is null)
            {
                await AssignFavoriteAsync(direction);
                return;
            }
            if (owner is null)
                return;
            var latest = ResolveFavorite(direction);
            if (captured?.Command is null || latest?.Command != captured.Command || latest.Action != captured.Action ||
                latest.FallbackFor != captured.FallbackFor || latest.Prepare != captured.Prepare || latest.ExpectedCommand != captured.ExpectedCommand ||
                captured.Frame is { } original && (latest.Frame is not { } live || !SameSurfaceOwner(original.Descriptor, live.Descriptor) ||
                    original.Authority.WorkerRun != live.Authority.WorkerRun))
            {
                await ShowFavoriteNoticeAsync(latest?.Reason ?? "This shortcut changed. Press it again to use the current action.");
                return;
            }
            var slot = preferences.TrayActions.Get(direction)!;
            invocation = new(slot, captured.Label);
            runningFavorites[direction] = invocation;
            if (RadialOpen) radialView?.PressFavorite(direction, FavoriteMotion);
            else DesktopFeedback.Press(FavoriteMotion);
            _ = RevealFavoriteProgressAsync(direction, invocation);
            var frame = captured.Frame;
            var action = captured.Action;
            var fallbackFor = captured.FallbackFor;
            var provider = captured.Prepare ?? frame?.Descriptor;
            if (provider?.AvailableTrayActions.Any(def => def.Id == slot.Action && def.WakeOnInvoke) == true)
            {
                var selection = selectionVersion;
                var visibility = visibleSince;
                frame = await owner.Session.PrepareTrayActionAsync(owner.Session.GetTarget(provider.Id), slot.Action, lifetime.Token);
                if (retired || !visible || !foreground || interactive || HostChoiceActive || reordering || selection != selectionVersion ||
                    visibility != visibleSince || preferences.TrayActions.Get(direction) != slot || !SameSurfaceOwner(provider, frame.Descriptor)) return;
                var current = ResolveFavorite(direction, frame);
                if (current?.Action?.TrayOffer is { NeedsPermission: true } && (current.Command == captured.Command || current.Command == slot.Action))
                {
                    runningFavorites.Remove(direction);
                    RefreshFavoriteProgress();
                    var choice = await ShowHostChoiceAsync("Permission needed", $"Open {provider.Name} to review access for {current.Label}.",
                        (HostDialogChoice[])[new("open", "Open " + provider.Name)], lifetime.Token, directDecisionButtons: true);
                    if (choice == "open" && visible && foreground && visibleSince == visibility && selectionVersion == selection &&
                        preferences.TrayActions.Get(direction) == slot && favoriteProviders.TryGetValue(provider.Id, out var installed) &&
                        SameSurfaceOwner(provider, installed) && !installed.IsStopped)
                        await SelectAsync(provider.Id);
                    return;
                }
                // A cold slot displays both labels when the user explicitly assigned a fallback.
                var offeredFallback = captured.Prepare is not null && captured.ExpectedCommand is null && current?.FallbackFor == slot.Action && current.Command == slot.Fallback;
                if (current?.Action is null || (current.Command != captured.Command && !offeredFallback) ||
                    captured.ExpectedCommand is { } expected && (current.Action.TrayOffer?.CaptureCommand != expected || current.FallbackFor != captured.FallbackFor) ||
                    action is not null && current.Action != action && action.TrayOffer?.NeedsPermission != true)
                {
                    await ShowFavoriteNoticeAsync(current?.Reason ?? "The target changed. Press the shortcut again.");
                    return;
                }
                action = current.Action;
                fallbackFor = current.FallbackFor;
            }
            var sequence = ++dashboardInputSequence;
            if (frame is not null && action?.TrayOffer?.CaptureCommand?.Operation is "capture.save-screenshot" or "capture.record-clip")
            {
                captureSequence = sequence;
                pendingTrayCaptures[sequence] = (frame.Descriptor.Id, visibleSince, selectionVersion);
            }
            var result = frame is null || action is null ? default :
                await owner.Session.RunTrayActionAsync(frame, action, sequence, origin, lifetime.Token, fallbackFor);
            if (!result.Handled && !retired && preferences.TrayActions.Get(direction) == slot)
                await ShowFavoriteNoticeAsync(result.Reason ?? "The action changed. Try again with the current target.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Diagnostics.FrontendFailureLog.Current.Write("tray-quick-action", error);
            await ShowFavoriteNoticeAsync("The quick action could not complete.");
        }
        finally
        {
            if (captureSequence is { } sequence) pendingTrayCaptures.Remove(sequence);
            if (invocation is not null && runningFavorites.GetValueOrDefault(direction) == invocation)
                runningFavorites.Remove(direction);
            if (!retired) RefreshFavoriteHints();
        }
    }

    private Task ShowFavoriteNoticeAsync(string text) => ShowReplayNoticeAsync(text, "tray-action", "Quick actions");

    private async Task EditFavoriteSlotsAsync()
    {
        while (!retired && visible && foreground)
        {
            var rows = Directions.Select(d => new HostDialogChoice(d.ToString(), d.ToString(),
                Value: ResolveFavorite(d)?.Label ?? "Assign action…")).ToList();
            var selected = await ShowHostChoiceAsync("Assign quick action", "", rows, lifetime.Token,
                configure: dialog => dialog.ConfigurePreferenceList());
            if (!Enum.TryParse<TrayDirection>(selected, out var direction) || await AssignFavoriteAsync(direction)) return;
        }
    }

    private Task<string?> FavoriteChoiceAsync(string title, IReadOnlyList<HostDialogChoice> choices, string? current = null) =>
        ShowHostChoiceAsync(title, "", choices, lifetime.Token, configure: dialog =>
        {
            dialog.ConfigurePreferenceList(current);
            dialog.SetListHeight(Math.Clamp(XamlRoot.Size.Height - 240, 144, 440));
        });

    private async Task<bool> AssignFavoriteAsync(TrayDirection direction)
    {
        var current = preferences.TrayActions.Get(direction);
        var choices = favoriteProviders.Values.SelectMany(provider => provider.AvailableTrayActions.Select(def =>
                new Assignable(provider.Name, new(provider.Id, def.Id, provider.PublisherId, Package: provider.PackageId, Label: def.Label), def)))
            .OrderBy(item => item.Group, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Binding.Provider, StringComparer.Ordinal)
            .ThenBy(item => item.Definition.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        // Page by provider so a disclosure never divides one widget's actions across pages.
        const int providersPerPage = 24;
        var slots = preferences.TrayActions;
        if (choices.Length == 0)
        {
            if (current is null)
            {
                await ShowHostChoiceAsync("Assign " + direction, "Install a widget that offers quick actions to assign this direction.",
                    Array.Empty<HostDialogChoice>(), lifetime.Token);
                return false;
            }
            if (await FavoriteChoiceAsync("Assign " + direction, (HostDialogChoice[])[new("clear", "Clear assignment")]) != "clear" ||
                preferences.TrayActions != slots) return false;
            preferences = preferences with { TrayActions = slots.Assign(direction, null) };
            RefreshRadialChooser();
            await SavePreferencesAsync();
            return true;
        }
        var pageStarts = choices.Select((choice, index) => (choice, index)).GroupBy(x => x.choice.Binding.Provider)
            .Select(group => group.First().index).Where((_, index) => index % providersPerPage == 0).Append(choices.Length).ToArray();
        var focusIndex = Array.FindIndex(choices, choice => choice.Binding.SameAction(current));
        var page = Math.Max(0, Array.FindLastIndex(pageStarts, first => first <= focusIndex));
        while (true)
        {
            var start = pageStarts[page];
            var end = pageStarts[page + 1];
            var rows = new List<HostDialogChoice>();
            for (var i = start; i < end; i++)
            {
                var item = choices[i];
                var assigned = string.Join(", ", Directions.Where(slot => item.Binding.SameAction(slots.Get(slot))));
                rows.Add(new(i.ToString(System.Globalization.CultureInfo.InvariantCulture), item.Definition.Label,
                    Section: new(item.Binding.Provider, item.Group),
                    Value: assigned.Length == 0 ? null : assigned));
            }
            if (start != 0)
                rows.Add(new("previous", "Previous widgets"));
            if (end < choices.Length)
                rows.Add(new("next", "More widgets"));
            if (current is not null)
                rows.Add(new("clear", "Clear assignment"));
            var selected = focusIndex >= start && focusIndex < end ? focusIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            var result = await FavoriteChoiceAsync("Assign " + direction, rows, selected);
            if (result == "previous")
            {
                page--;
                continue;
            }
            if (result == "next")
            {
                page++;
                continue;
            }
            if (result is null)
                return false;
            TrayActionBinding? binding = null;
            TrayDirection? moveFrom = null;
            if (result != "clear")
            {
                if (!int.TryParse(result, out var index) || (uint)index >= choices.Length)
                    return false;
                var choice = choices[index];
                focusIndex = index;
                binding = choice.Binding;
                if (!binding.SameAction(current))
                {
                    var existing = Directions.Where(slot => binding.SameAction(slots.Get(slot))).ToArray();
                    if (existing.Length > 0)
                    {
                        var operations = existing.Select(slot => new HostDialogChoice(slot.ToString(), $"Move from {slot}")).ToList();
                        operations.Add(new("copy", $"Also assign to {direction}"));
                        var operation = await FavoriteChoiceAsync(choice.Definition.Label, operations);
                        if (operation is null)
                            continue;
                        if (Enum.TryParse<TrayDirection>(operation, out var source))
                            moveFrom = source;
                    }
                }
                if (choice.Definition.FallbackId is { } fallback)
                {
                    var alternate = choices.First(c => c.Binding.Provider == binding.Provider && c.Binding.Action == fallback);
                    var mode = await FavoriteChoiceAsync("When " + choice.Definition.Label.ToLowerInvariant() + " is unavailable",
                        (HostDialogChoice[])[new("fallback", alternate.Definition.Label), new("none", "Keep unavailable")]);
                    if (mode is null)
                        continue;
                    if (mode == "fallback")
                        binding = binding with
                        {
                            Fallback = fallback
                        };
                }
            }
            if (preferences.TrayActions != slots)
                return false;
            var updated = moveFrom is { } previous ? slots.Assign(previous, null) : slots;
            preferences = preferences with
            {
                TrayActions = updated.Assign(direction, binding)
            };
            RefreshRadialChooser();
            await SavePreferencesAsync();
            return true;
        }
    }
}
