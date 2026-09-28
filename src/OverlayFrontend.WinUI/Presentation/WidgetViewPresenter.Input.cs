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
        if (HasTransientControl)
        {
            if (phase == ControllerEventPhase.Pressed)
            {
                if (button == ControllerButton.B) DismissTransientControl();
                else if (button == ControllerButton.A) ActivateSelect();
            }
            // No parent shortcut is allowed through an open popup, including
            // release/repeat events from a previously pressed button.
            return true;
        }
        if (button == ControllerButton.A)
        {
            if (phase == ControllerEventPhase.Pressed) ActivateFocused();
            return true;
        }
        if (FocusedBinding() is { Element: WidgetIndexedCollectionView collection } focused && Eligible(focused))
        {
            CancelGroupEntry();
            return collection.InvokeFocused(button, phase);
        }
        if (Session is not { } session) return false;
        var authority = frame.Authority;
        var input = new ControllerInputEvent(button, phase, ControllerInputContext.OpenWidget,
            FocusedBinding() is { } binding && Eligible(binding) ? binding.Identity.Id : null,
            ++actionSequence, Environment.TickCount64 * 1000, authority.ActiveInputScopeId,
            authority.SnapshotSequence, Origin: origin);
        try { return await session.SendControllerInputAsync(authority, input, cancellationToken); }
        // A newer publication can retire the displayed input while it crosses IPC.
        // Drop it rather than replaying it against a different scope or game.
        catch (WidgetPresentationSessionException error) when
            (error.Code is "snapshot_stale" or "input_scope_stale" or "presentation_stale") { return true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || disposed) { return true; }
        catch (Exception error) { ReportFailure(error); return false; }
    }
}
