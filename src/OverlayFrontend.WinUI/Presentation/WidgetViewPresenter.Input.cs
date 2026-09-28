using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    /// <summary>
    /// Routes an already-normalized button without reading hardware or introducing
    /// an action queue. Popups own input first; indexed rows preserve their semantic
    /// lease; ordinary shortcuts resolve through the existing worker input pipeline.
    /// Guide and overlay visibility remain owned by the host.
    /// </summary>
    internal async Task<bool> HandleControllerButtonAsync(ControllerButton button,
        ControllerEventPhase phase = ControllerEventPhase.Pressed,
        ControllerInputOrigin origin = ControllerInputOrigin.PhysicalController,
        CancellationToken cancellationToken = default)
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Route widget input on its WinUI dispatcher.");
        if (disposed || applying || presentationOnly || frame is null) return false;
        if (textEntryPopup is { } edit) { edit.Dialog.Handle(button, phase); return true; }
        if (button == ControllerButton.A) UpdateControllerPressedStyle(phase);
        if (HasTransientControl)
        {
            if (phase == ControllerEventPhase.Pressed)
            {
                if (button == ControllerButton.B) DismissTransientControl();
                else if (button == ControllerButton.A) { if (!ActivateContextMenu()) ActivateSelect(); }
            }
            // No parent shortcut is allowed through an open popup, including
            // release/repeat events from a previously pressed button.
            return true;
        }
        if (HandleSliderButton(button, phase)) return true;
        if (phase == ControllerEventPhase.Pressed && OpenContextMenu(button)) return true;
        if (button == ControllerButton.A)
        {
            if (phase == ControllerEventPhase.Pressed) ActivateFocused();
            return true;
        }
        if (FocusedBinding() is { Element: WidgetIndexedCollectionView collection } focused && Eligible(focused))
        {
            CancelGroupEntry();
            return await collection.InvokeFocusedInputAsync(button, phase);
        }
        if (Session is not { } session) return false;
        var displayed = frame;
        var authority = displayed.Authority;
        var input = new ControllerInputEvent(button, phase, ControllerInputContext.OpenWidget,
            FocusedBinding() is { } binding && Eligible(binding) ? binding.Identity.Id : null,
            ++actionSequence, Environment.TickCount64 * 1000, authority.ActiveInputScopeId,
            authority.SnapshotSequence, Origin: origin);
        try
        {
            if (!await AdmitInteractionAsync(authority, cancellationToken) || !IsInteractionCurrent(authority)) return true;
            if (DispatchActionAsync is not null && session.ResolveEmbeddedMediaFullscreenInput(displayed, input) is { } hostAction)
            { await DispatchActionAsync(new(displayed, hostAction)); return true; }
            return await session.SendControllerInputAsync(displayed, input, cancellationToken);
        }
        // A newer publication can retire the displayed input while it crosses IPC.
        // Drop it rather than replaying it against a different scope or game.
        catch (WidgetPresentationSessionException error) when
            (error.Code is "snapshot_stale" or "input_scope_stale" or "presentation_stale" or "ordinary_input_stale") { return true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || disposed) { return true; }
        catch (Exception error) { ReportFailure(error); return false; }
    }
}
