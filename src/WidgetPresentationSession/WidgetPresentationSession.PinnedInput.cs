using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public Task<bool> SendPinnedControllerInputAsync(WidgetPinnedSelection selection, WidgetPinnedProjection projection,
        ControllerInputEvent input, CancellationToken cancellationToken = default) =>
        SendPinnedInputCoreAsync(selection, projection, input, null, cancellationToken);

    /// <summary>Commits a captured Select option through the bridge's exact pinned Select binding route.</summary>
    public Task<bool> SendPinnedSelectOptionAsync(WidgetPinnedSelection selection, WidgetPinnedProjection projection,
        ControllerInputEvent input, string optionActionId, CancellationToken cancellationToken = default) =>
        SendPinnedInputCoreAsync(selection, projection, input, optionActionId, cancellationToken);

    private async Task<bool> SendPinnedInputCoreAsync(WidgetPinnedSelection selection, WidgetPinnedProjection projection,
        ControllerInputEvent input, string? optionActionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(projection); ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        using var dispatch = await AcquirePinnedDispatchAsync(cancellationToken).ConfigureAwait(false);
        string? expectedAction;
        using (_gate.Enter())
        {
            var current = DemandPinnedInputLocked(selection, projection);
            ValidatePinnedInputOrigin(projection, input);
            if (input.ActiveInputScopeId != current.ActiveInputScopeId) throw PinnedStale("The pinned input scope changed.");
            OrdinaryInputBinding? originBinding, currentBinding;
            if (optionActionId is not null)
            {
                if (!Safe(optionActionId) || input.Button != ControllerButton.A || input.Phase != ControllerEventPhase.Pressed || input.RequestedValue is not null)
                    throw PinnedStale("The pinned Select commitment is invalid.");
                var action = new WidgetActionEvent(optionActionId, input.FocusedElementId!, ControllerButton.A,
                    InputScopeId: input.ActiveInputScopeId) { FocusedElementId = input.FocusedElementId };
                originBinding = ResolveDisplayedAction(projection.Snapshot, action);
                currentBinding = ResolveDisplayedAction(current, action);
                if (originBinding.Role != "select" || currentBinding.Role != "select") throw PinnedStale("The pinned opener is not a Select.");
            }
            else
            {
                originBinding = ResolvePinnedController(projection.Snapshot, input);
                currentBinding = ResolvePinnedController(current, input);
            }
            if (originBinding != currentBinding) throw PinnedStale("The pinned input binding changed after display.");
            // Pinned surfaces have no raw override fallback. Preserve native
            // not-handled behavior instead of forwarding unrelated input.
            if (originBinding is null) return false;
            expectedAction = optionActionId is null ? originBinding.ActionId : null;
        }
        return await SendPinnedWireAsync(projection.Frame, input, expectedAction, optionActionId, dispatch, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Routes supported pinned commits through exact-layout controller authority.
    /// Context actions and text commits use the versioned pinned action contract;
    /// they never fall back to the ordinary Action endpoint.
    /// </summary>
    public Task<bool> SendPinnedActionAsync(WidgetPinnedSelection selection, WidgetPinnedProjection projection,
        WidgetActionEvent action, ControllerInputOrigin origin = ControllerInputOrigin.AccessibilityAutomation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(projection); ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        OrdinaryInputBinding binding;
        using (_gate.Enter())
        {
            var current = DemandPinnedInputLocked(selection, projection);
            if (action.InputScopeId != projection.Snapshot.ActiveInputScopeId || action.InputScopeId != current.ActiveInputScopeId)
                throw PinnedStale("The pinned action scope changed.");
            binding = ResolveDisplayedAction(projection.Snapshot, action);
            if (binding != ResolveDisplayedAction(current, action)) throw PinnedStale("The pinned action binding changed.");
            if (action.VisibleCollectionKeys is not null || action.RetainedCollectionKeys is not null || !Enum.IsDefined(origin))
                throw PinnedStale("The pinned action payload is invalid.");
        }
        if (binding.Role is "context" or "text")
            return SendPinnedDeclaredActionAsync(selection, projection, action, cancellationToken);
        var button = binding.Role == "slider" ? action.ControllerButton ?? ControllerButton.DPadRight
            : binding.Role == "shortcut" ? action.ControllerButton!.Value : ControllerButton.A;
        if (binding.Role is "primary" or "select" && action.ControllerButton is { } supplied && supplied != ControllerButton.A ||
            binding.Role == "slider" && button is not (ControllerButton.DPadLeft or ControllerButton.DPadRight))
            throw PinnedStale("The pinned action trigger does not match its declaration.");
        var focus = binding.Role == "shortcut" ? action.FocusedElementId ?? action.SourceElementId : action.SourceElementId;
        if (binding.Role != "shortcut" && action.FocusedElementId is { } focused && focused != action.SourceElementId)
            throw PinnedStale("The pinned action lost its focused owner.");
        var input = new ControllerInputEvent(button, action.Phase, ControllerInputContext.PinnedSurface, focus,
            action.Sequence, action.MonotonicTimestampMicroseconds, action.InputScopeId,
            projection.Frame.Authority.SnapshotSequence, action.RequestedValue, origin) { PinnedLayoutId = projection.LayoutId };
        return SendPinnedInputCoreAsync(selection, projection, input, binding.Role == "select" ? action.ActionId : null, cancellationToken);
    }

    private async Task<bool> SendPinnedDeclaredActionAsync(WidgetPinnedSelection selection, WidgetPinnedProjection projection,
        WidgetActionEvent action, CancellationToken cancellationToken)
    {
        using var dispatch = await AcquirePinnedDispatchAsync(cancellationToken).ConfigureAwait(false);
        var input = new PinnedActionInput(PinnedActionContract.Version, projection.LayoutId, projection.Frame.Authority.SnapshotSequence, action);
        using (_gate.Enter())
        {
            var current = DemandPinnedInputLocked(selection, projection);
            try
            {
                if (PinnedActionContract.Resolve(projection.Frame.Snapshot, input) !=
                    PinnedActionContract.Resolve(_states[selection.WidgetId].LastGood!.Snapshot, input))
                    throw PinnedStale("The pinned action binding changed.");
                if (current.ActiveInputScopeId != action.InputScopeId) throw PinnedStale("The pinned action scope changed.");
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            { throw PinnedStale("The pinned action origin or binding is invalid."); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var authority = projection.Frame.Authority;
        var exchange = RequestAsync(BridgeMessageTypes.PinnedAction,
            new BridgePinnedActionRequest(authority.WidgetId, authority.WidgetInstanceId, authority.RuntimeGeneration,
                authority.PresentationGeneration, input, authority.WorkerRun), BridgeMessageTypes.Acknowledged, CancellationToken.None);
        dispatch.Track(exchange);
        var reply = await exchange.WaitAsync(cancellationToken).ConfigureAwait(false);
        RequireObjectProperties(reply.Payload, "admission");
        WidgetOperationAdmission? admission;
        try { admission = PinnedActionContract.ValidateAdmission(BridgeJson.FromElement<PinnedActionResult>(reply.Payload).Admission); }
        catch (ArgumentException error) { throw new BridgeProtocolException("Invalid pinned action result.", error); }
        return admission is WidgetOperationAdmission.Enqueued or WidgetOperationAdmission.Joined or WidgetOperationAdmission.Replaced;
    }

    private static void ValidatePinnedInputOrigin(WidgetPinnedProjection projection, ControllerInputEvent input)
    {
        if (input.Context != ControllerInputContext.PinnedSurface || input.PinnedLayoutId != projection.LayoutId ||
            input.IsPinnedLayoutSelected is not null || input.SnapshotSequence != projection.Frame.Authority.SnapshotSequence ||
            input.ActiveInputScopeId != projection.Snapshot.ActiveInputScopeId || !Safe(input.FocusedElementId) ||
            !Enum.IsDefined(input.Button) || !Enum.IsDefined(input.Phase) || !Enum.IsDefined(input.Origin) ||
            input.RequestedValue is { } value && !double.IsFinite(value)) throw PinnedStale("The pinned controller origin is invalid.");
        ValidatePinnedClock(input.Sequence, input.MonotonicTimestampMicroseconds);
    }
    private static OrdinaryInputBinding? ResolvePinnedController(ViewSnapshot snapshot, ControllerInputEvent input)
    {
        var scope = OrdinaryScope(snapshot);
        var path = OrdinaryPath(scope, input.FocusedElementId!);
        var focused = path[^1];
        if (input.Button == ControllerButton.A)
        {
            if (!ControllerShortcutResolutionContract.OwnerAvailable(focused)) throw PinnedStale("The pinned input owner is unavailable.");
            if (input.RequestedValue is not null) throw PinnedStale("Pinned activation cannot contain a slider value.");
            return input.Phase == ControllerEventPhase.Pressed && focused.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface or ViewNodeKind.Slider && focused.ActionId is { } action
                ? new("primary", action, Identity(path), Identity(path)) : null;
        }
        if (input.RequestedValue is { } requested && (focused.Kind != ViewNodeKind.Slider ||
            input.Button is not (ControllerButton.DPadLeft or ControllerButton.DPadRight) || focused.Minimum is not { } min ||
            focused.Maximum is not { } max || focused.Step is not { } step || !SliderMath.IsValidRequestedValue(requested, min, max, step)))
            throw PinnedStale("The pinned slider value is invalid.");
        var binding = ResolveDisplayedController(snapshot, input);
        return binding.Role == "raw" ? null : binding;
    }
}
