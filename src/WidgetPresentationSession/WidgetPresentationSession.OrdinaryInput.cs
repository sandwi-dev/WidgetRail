using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public async Task<bool> SendDashboardInputAsync(WidgetPresentationFrame displayed, ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateControllerPayload(input);
        if (input.Context != ControllerInputContext.DashboardQuickAction || input.SnapshotSequence != displayed.Authority.SnapshotSequence ||
            input.FocusedElementId is not null || input.RequestedValue is not null || input.PinnedLayoutId is not null ||
            input.IsPinnedLayoutSelected is not null || input.Phase is not (ControllerEventPhase.Pressed or ControllerEventPhase.Repeated))
            throw OrdinaryInputStale("Dashboard input origin is invalid.");
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, input.ActiveInputScopeId);
            var original = displayed.Snapshot.QuickActions.SingleOrDefault(action => action.Button == input.Button);
            var latest = current.Snapshot.QuickActions.SingleOrDefault(action => action.Button == input.Button);
            if (original is null || latest is null || original.ActionId != latest.ActionId || original.Capability != latest.Capability ||
                original.RepeatPolicy != latest.RepeatPolicy || input.Phase == ControllerEventPhase.Repeated && latest.RepeatPolicy != ControllerActionRepeatPolicy.WhileHeld)
                throw OrdinaryInputStale("Dashboard command changed after presentation.");
        }
        var reply = await RequestAsync(BridgeMessageTypes.ControllerInput,
            new BridgeControllerInputRequest(displayed.Authority.WidgetId, input, displayed.Authority.RuntimeGeneration,
                WorkerRun: displayed.Authority.WorkerRun), BridgeMessageTypes.ControllerInputResult, cancellationToken).ConfigureAwait(false);
        RequireObjectProperties(reply.Payload, "handled");
        return reply.Payload.GetProperty("handled").GetBoolean();
    }

    /// <summary>
    /// Admit the exact action captured from a frame actually published by this session.
    /// Unrelated publications may advance while lifecycle activation is acknowledged;
    /// the original and current semantic binding must still match. Payload is never
    /// reconstructed from new focus or rebased onto a different action.
    /// </summary>
    public async Task<WidgetOperationAdmission> SendActionAsync(WidgetPresentationFrame displayed,
        WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            var originBinding = ResolveDisplayedAction(displayed.Snapshot, action);
            var currentBinding = ResolveDisplayedAction(current.Snapshot, action);
            if (originBinding != currentBinding) throw OrdinaryInputStale("The displayed action binding changed.");
        }
        var response = await RequestAsync(BridgeMessageTypes.Action,
            new BridgeActionRequest(displayed.Authority.WidgetId, action, displayed.Authority.WorkerRun), BridgeMessageTypes.Acknowledged,
            cancellationToken).ConfigureAwait(false);
        return ReadAdmission(response.Payload);
    }

    /// <summary>
    /// Preserve the origin snapshot sequence and focused element when dispatching
    /// delayed open-widget controller input. Both this session and the bridge compare
    /// its origin/current binding. The bridge's bounded history remains authoritative;
    /// a rejected old origin is dropped, never retried against the current snapshot.
    /// </summary>
    public async Task<bool> SendControllerInputAsync(WidgetPresentationFrame displayed,
        ControllerInputEvent input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDisplayedControllerOrigin(displayed, input);
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, input.ActiveInputScopeId);
            if (ResolveDisplayedController(displayed.Snapshot, input) != ResolveDisplayedController(current.Snapshot, input))
                throw OrdinaryInputStale("The displayed controller binding changed.");
        }
        var response = await RequestAsync(BridgeMessageTypes.ControllerInput,
            new BridgeControllerInputRequest(displayed.Authority.WidgetId, input, displayed.Authority.RuntimeGeneration, WorkerRun: displayed.Authority.WorkerRun),
            BridgeMessageTypes.ControllerInputResult, cancellationToken).ConfigureAwait(false);
        RequireObjectProperties(response.Payload, "handled");
        return response.Payload.GetProperty("handled").GetBoolean();
    }

    private static void ValidateDisplayedControllerOrigin(WidgetPresentationFrame displayed, ControllerInputEvent input)
    {
        ValidateControllerPayload(input);
        if (input.Context != ControllerInputContext.OpenWidget || input.SnapshotSequence != displayed.Authority.SnapshotSequence ||
            input.PinnedLayoutId is not null || input.IsPinnedLayoutSelected is not null)
            throw OrdinaryInputStale("The displayed controller input has invalid origin authority.");
    }

    private static void ValidateControllerPayload(ControllerInputEvent input)
    {
        if (!Enum.IsDefined(input.Button) || !Enum.IsDefined(input.Phase) || !Enum.IsDefined(input.Origin) || !Enum.IsDefined(input.Context) ||
            input.Sequence < 0 || input.MonotonicTimestampMicroseconds < 0 || input.RequestedValue is { } value && !double.IsFinite(value))
            throw new BridgeProtocolException("The controller input payload is invalid.");
    }

    private WidgetPresentationFrame ValidateDisplayedOrdinaryFrameLocked(WidgetPresentationFrame origin, string? scope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfTerminalLocked();
        if (!_publishedInputFrames.TryGetValue(origin, out _) ||
            _states.GetValueOrDefault(origin.Authority.WidgetId)?.LastGood is not { } current ||
            !SameIndexedOwner(origin.Authority, current.Authority) ||
            origin.Authority.SnapshotSequence > current.Authority.SnapshotSequence)
            throw OrdinaryInputStale("The input frame was not published by the active session.");
        if (scope != origin.Authority.ActiveInputScopeId || scope != current.Authority.ActiveInputScopeId)
            throw OrdinaryInputStale("The displayed input scope has retired.");
        return current;
    }

    private sealed record OrdinaryNodeIdentity(string Id, ViewNodeKind Kind, string OccurrencePath);
    private sealed record OrdinaryInputBinding(string Role, string ActionId, OrdinaryNodeIdentity Owner,
        OrdinaryNodeIdentity? Focus, object? Detail = null);

    private static OrdinaryInputBinding ResolveDisplayedAction(ViewSnapshot snapshot, WidgetActionEvent action)
    {
        if (!SafeActionName(action.ActionId) || !Safe(action.SourceElementId) || !Enum.IsDefined(action.Phase) ||
            action.ControllerButton is { } button && !Enum.IsDefined(button) || action.Sequence < 0 || action.MonotonicTimestampMicroseconds < 0 ||
            action.FocusedCollectionItem is not null || action.RequestedValue is { } value && !double.IsFinite(value) ||
            action.CommittedText is { } committed && (committed.Any(char.IsControl) || action.RequestedValue is not null || action.Phase != ControllerEventPhase.Pressed))
            throw new BridgeProtocolException("The displayed action payload is invalid.");
        var scope = OrdinaryScope(snapshot);
        var path = OrdinaryPath(scope, action.SourceElementId);
        var node = path[^1];
        var owner = Identity(path);
        var focus = action.FocusedElementId is { } focused ? Identity(OrdinaryPath(scope, focused)) : null;
        if (!ControllerShortcutResolutionContract.OwnerAvailable(node)) throw OrdinaryInputStale("The action owner is unavailable.");
        if (action.CommittedText is { } text)
        {
            if (node.Kind != ViewNodeKind.TextEntry || action.ActionId != node.ActionId || action.RequestedValue is not null ||
                action.Phase != ControllerEventPhase.Pressed || text.Length > (node.TextEntryMaximumLength ?? ProtocolConstants.MaximumTextEntryLength) ||
                text.Any(char.IsControl)) throw OrdinaryInputStale("The text commit binding changed.");
            return new("text", action.ActionId, owner, focus, (node.TextEntryInputKind, node.TextEntryMaximumLength));
        }
        if (action.RequestedValue is { } requested)
        {
            if (node.Kind != ViewNodeKind.Slider || action.ActionId != node.ValueChangedActionId ||
                node.Minimum is not { } min || node.Maximum is not { } max || node.Step is not { } step ||
                !SliderMath.IsValidRequestedValue(requested, min, max, step))
                throw OrdinaryInputStale("The slider commit binding changed.");
            return new("slider", action.ActionId, owner, focus, (min, max, step));
        }
        if (action.Phase == ControllerEventPhase.Pressed)
        {
            if (node.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface or ViewNodeKind.Slider && node.ActionId == action.ActionId)
                return new("primary", action.ActionId, owner, focus);
            if (node.Kind == ViewNodeKind.Select && node.SelectOptions.SingleOrDefault(option => option.ActionId == action.ActionId) is { IsDisabled: false, IsBusy: false } option)
                return new("select", action.ActionId, owner, focus, option with { IsSelected = false });
            if (node.ContextActions.SingleOrDefault(candidate => candidate.ActionId == action.ActionId) is { IsDisabled: false, IsBusy: false } context)
                return new("context", action.ActionId, owner, focus, (node.ContextMenuButton, context));
        }
        if (action.ControllerButton is { } trigger)
        {
            var shortcut = ControllerShortcutResolver.Resolve(scope, action.FocusedElementId, trigger, action.Phase);
            if (shortcut.Status == ControllerShortcutResolutionStatus.Resolved && shortcut.Owner!.Id == action.SourceElementId &&
                shortcut.Shortcut!.ActionId == action.ActionId)
                return new("shortcut", action.ActionId, owner, focus, shortcut.Shortcut);
        }
        throw OrdinaryInputStale("The captured action is not declared by its displayed owner.");
    }

    private static OrdinaryInputBinding ResolveDisplayedController(ViewSnapshot snapshot, ControllerInputEvent input)
    {
        var scope = OrdinaryScope(snapshot);
        var path = input.FocusedElementId is { } id ? OrdinaryPath(scope, id) : new List<ViewNode> { scope };
        var focused = input.FocusedElementId is null ? null : path[^1];
        var focusIdentity = focused is null ? null : Identity(path);
        if (input.Button == ControllerButton.A && focused is not null)
        {
            if (!ControllerShortcutResolutionContract.OwnerAvailable(focused)) throw OrdinaryInputStale("The controller owner is unavailable.");
            if (input.Phase == ControllerEventPhase.Pressed && focused.Kind is ViewNodeKind.Button or ViewNodeKind.Slider or ViewNodeKind.ActionSurface && focused.ActionId is { } action)
                return new("primary", action, Identity(path), focusIdentity);
        }
        if (focused is { Kind: ViewNodeKind.Slider, ValueChangedActionId: { } sliderAction } && input.Button is ControllerButton.DPadLeft or ControllerButton.DPadRight)
        {
            if (!ControllerShortcutResolutionContract.OwnerAvailable(focused)) throw OrdinaryInputStale("The slider owner is unavailable.");
            return new("slider", sliderAction, Identity(path), focusIdentity, (focused.Minimum, focused.Maximum, focused.Step));
        }
        var resolution = ControllerShortcutResolver.ResolvePath(path, input.Button, input.Phase);
        if (resolution.Status == ControllerShortcutResolutionStatus.OwnerUnavailable) throw OrdinaryInputStale("The shortcut owner is unavailable.");
        if (resolution.Status == ControllerShortcutResolutionStatus.Resolved)
            return new("shortcut", resolution.Shortcut!.ActionId, Identity(OrdinaryPath(scope, resolution.Owner!.Id)), focusIdentity, resolution.Shortcut);
        // Match the existing bridge/SDK raw open-widget fallback. It is still tied
        // to this exact focus occurrence/kind and disabled/busy state.
        return new("raw", string.Empty, Identity(path), focusIdentity,
            (focused?.IsDisabled is true, focused?.IsBusy is true));
    }

    private static bool Safe(string? value) => value is not null && ProtocolValidationIdentifierContext.IsSafeIdentifier(value);
    // Action names are opaque worker commands, not element IDs. The worker's
    // bounded action contract permits qualifiers such as Settings' @display-id;
    // exact origin/current declaration matching below supplies their authority.
    private static bool SafeActionName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 && !value.Any(char.IsControl);
    private static WidgetPresentationSessionException OrdinaryInputStale(string message) => new("ordinary_input_stale", message);
    private static ViewNode OrdinaryScope(ViewSnapshot snapshot)
    {
        return Find(snapshot.Root, true) ?? throw OrdinaryInputStale("The input scope no longer exists.");
        ViewNode? Find(ViewNode node, bool root)
        {
            if ((root || node.InputScopeId is not null) && (node.InputScopeId ?? node.Id) == snapshot.ActiveInputScopeId) return node;
            foreach (var child in node.Children) if (Find(child, false) is { } found) return found;
            return null;
        }
    }
    private static List<ViewNode> OrdinaryPath(ViewNode scope, string id)
    {
        var path = new List<ViewNode>();
        if (!Find(scope, true)) throw OrdinaryInputStale("The displayed input owner no longer exists in its scope.");
        return path;
        bool Find(ViewNode node, bool root)
        {
            if (!root && node.InputScopeId is not null) return false;
            path.Add(node);
            if (node.Id == id) return true;
            foreach (var child in node.Children) if (Find(child, false)) return true;
            path.RemoveAt(path.Count - 1); return false;
        }
    }
    private static OrdinaryNodeIdentity Identity(IReadOnlyList<ViewNode> path) => new(path[^1].Id, path[^1].Kind,
        string.Concat(path.Where(node => node.CollectionItemKey is not null).Select(node => node.CollectionItemKey!.Length + ":" + node.CollectionItemKey)));
}
