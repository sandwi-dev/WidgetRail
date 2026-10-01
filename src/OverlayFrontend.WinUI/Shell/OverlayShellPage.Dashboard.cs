using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private long dashboardInputSequence;

    private WidgetPresentationFrame? DashboardFrame(BridgeWidgetDescriptor widget) =>
        !retired && visible && foreground && !interactive && !switching && activeWidget == widget.Id && requestedWidget == widget.Id &&
        surface?.CurrentBinding is { IsCurrent: true } binding && SameSurfaceOwner(widget, binding.Frame.Descriptor)
            ? binding.Frame : null;

    private IReadOnlyList<ControllerGuideHint> DashboardGuideHints()
    {
        if (FocusedTrayWidget() is not { } widget || DashboardFrame(widget) is not { } displayed) return [];
        return displayed.Snapshot.QuickActions.Select(action =>
            new ControllerGuideHint(ControllerGuideModel.Prompt(action.Button), action.Label, action.Button)).ToArray();
    }

    private async Task<bool> InvokeDashboardButtonAsync(BridgeWidgetDescriptor widget, ControllerButton button, ControllerInputOrigin origin,
        ControllerEventPhase phase = ControllerEventPhase.Pressed)
    {
        if (owner is null || DashboardFrame(widget) is not { } displayed ||
            !displayed.Snapshot.QuickActions.Any(action => action.Button == button &&
                (phase == ControllerEventPhase.Pressed || phase == ControllerEventPhase.Repeated && action.RepeatPolicy == ControllerActionRepeatPolicy.WhileHeld))) return false;
        var sequence = ++dashboardInputSequence;
        var timestamp = checked(Environment.TickCount64 * 1000);
        var failureCurrent = CapturePresentationFailureGuard();
        var capturedSelection = selectionVersion;
        bool Current() => capturedSelection == selectionVersion && DashboardFrame(widget) is { } current &&
            WidgetPresentationBinding.ForMain(displayed).SameInput(WidgetPresentationBinding.ForMain(current));
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                // Current-view dashboard actions need Visible lifecycle authority;
                // they never enter the widget or reuse its focused control's route.
                if (!Current()) return true;
                await owner.Session.SetLifecycleAsync(owner.Session.GetTarget(widget.Id), WidgetLifecycleState.Visible, lifetime.Token);
                if (!Current()) return true;
                await owner.Session.SendDashboardInputAsync(displayed,
                    new(button, phase, ControllerInputContext.DashboardQuickAction,
                        Sequence: sequence, MonotonicTimestampMicroseconds: timestamp,
                        ActiveInputScopeId: displayed.Authority.ActiveInputScopeId,
                        SnapshotSequence: displayed.Authority.SnapshotSequence, Origin: origin), lifetime.Token);
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error))
        { if (failureCurrent()) owner?.Session.RequestInputRefresh(displayed); }
        catch (WidgetPresentationSessionException error) when (error.Code is "catalog_stale" or "unknown_widget") { }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
        return true;
    }
}
