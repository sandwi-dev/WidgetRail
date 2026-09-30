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
        if (!presentationInputEnabled || !presentationActive || disposed || applying || presentationOnly || frame is null) return false;
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
        var displayed = presentation!;
        var authority = displayed.Frame.Authority;
        var input = new ControllerInputEvent(button, phase, displayed.Selection is null ? ControllerInputContext.OpenWidget : ControllerInputContext.PinnedSurface,
            FocusedBinding() is { } binding && Navigable(binding) ? binding.Identity.Id : null,
            ++actionSequence, Environment.TickCount64 * 1000, displayed.Scope,
            authority.SnapshotSequence, Origin: origin) { PinnedLayoutId = displayed.PinnedLayoutId };
        try
        {
            if (!await AdmitBindingAsync(displayed, cancellationToken)) return true;
            if (displayed.Selection is { } selection && displayed.Projection is { } projection)
                return await session.SendPinnedControllerInputAsync(selection, projection, input, cancellationToken);
            if (DispatchActionAsync is not null && session.ResolveEmbeddedMediaHostInput(displayed.Frame, input) is { } hostAction)
            { await DispatchActionAsync(new(displayed.Frame, hostAction)); return true; }
            return await session.SendControllerInputAsync(displayed.Frame, input, cancellationToken);
        }
        // A newer publication can retire the displayed input while it crosses IPC.
        // Drop it rather than replaying it against a different scope or game.
        catch (WidgetPresentationSessionException error) when
            (IsRetiredInput(error)) { session.RequestInputRefresh(displayed.Frame); return true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || disposed) { return true; }
        catch (Exception error) { ReportFailure(error, IsBindingCurrent(displayed)); return true; }
    }
}
