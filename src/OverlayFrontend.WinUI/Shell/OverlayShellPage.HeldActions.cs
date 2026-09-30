using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly HeldActionRepeat heldAction = new();
    private ControllerButton heldActionButton;
    private Task? heldActionDispatch;

    private (object Identity, bool Available)? CaptureHeldAction(ControllerButton button)
    {
        if (retired || !visible || !foreground && !PinnedInputActive || switching || pinnedAdjustment is not null || savingPinnedAdjustment || trayMenu is not null)
            return null;
        if (PinnedInputActive && pinned is { Media: not null } pin && pin.IsCurrent && mediaOwner?.CompactState is { } compact)
            return ((pin, compact.Authority.WidgetInstanceId, compact.Authority.RuntimeGeneration, compact.Declaration.Id, button), !mediaOwner.CompactCommandPending);
        if (IsMediaFullscreen && mediaOwner?.FullscreenState is { } full)
            return ((fullscreenView, full.Authority.WidgetId, full.Authority.WidgetInstanceId, full.Authority.RuntimeGeneration, full.Declaration.Id, button), !mediaOwner.FullscreenCommandPending);
        if (PinnedInputActive) return pinned?.Presenter?.CaptureHeldAction(button);
        if (interactive) return RecoveryVisible ? null : surface?.CaptureHeldAction(button);
        if (!reordering && FocusedTrayWidget() is { } widget && DashboardFrame(widget) is { } frame &&
            frame.Snapshot.QuickActions.FirstOrDefault(action => action.Button == button && action.RepeatPolicy == ControllerActionRepeatPolicy.WhileHeld) is { } action)
            return ((widget.Id, widget.InstanceId, widget.RuntimeGeneration, widget.PresentationGeneration,
                selectionVersion, frame.Authority.SessionGeneration, frame.Authority.ActiveInputScopeId, action.ActionId, button), true);
        return null;
    }

    private void BeginTrigger(ControllerButton button)
    {
        heldAction.Reset();
        heldActionButton = button;
        if (CaptureHeldAction(button) is { Available: true } target)
            heldAction.Begin(target.Identity, Environment.TickCount64);
        heldActionDispatch = RouteButtonAsync(button, ControllerEventPhase.Pressed);
    }

    private void PumpHeldAction(ControllerFrame frame)
    {
        if (!heldAction.Active) return;
        var down = heldActionButton == ControllerButton.LeftTrigger ? frame.State.LeftTrigger >= 30 : frame.State.RightTrigger >= 30;
        var target = CaptureHeldAction(heldActionButton);
        if (heldAction.Tick(target?.Identity, down, target?.Available == true && heldActionDispatch?.IsCompleted != false, Environment.TickCount64))
            heldActionDispatch = RouteButtonAsync(heldActionButton, ControllerEventPhase.Repeated);
    }
}
