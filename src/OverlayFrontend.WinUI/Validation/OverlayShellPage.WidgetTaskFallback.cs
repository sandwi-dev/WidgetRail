using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal async Task<HostTaskActivationOutcome> VerifyWidgetTaskHandoffAsync(TaskWindowTarget target)
    {
        var authority = surface?.CurrentBinding?.Frame.Authority ?? throw new InvalidOperationException("A displayed widget is required.");
        var effect = new WidgetPresentationHostEffect(new(authority.WidgetId, authority.WidgetInstanceId, authority.RuntimeGeneration, authority.SessionGeneration),
            WidgetHostEffectKind.ActivateTaskWindow, "activateTaskWindow", 1, Environment.TickCount64,
            new("test-peer", target.Handle, target.ProcessId, target.ProcessCreated, target.ClassName));
        HostTaskActivationOutcome? observed = null;
        var original = HostTaskActivationRequested ?? throw new InvalidOperationException("Missing host task handoff.");
        try
        {
            HostTaskActivationRequested = async request =>
            {
                if (!ReferenceEquals(request.WidgetEffect, effect)) throw new InvalidOperationException("Widget authority was lost.");
                var outcome = await original(request);
                observed = outcome;
                return outcome;
            };
            await RequestWidgetTaskActivationAsync(effect);
            return observed ?? throw new InvalidOperationException("The widget skipped the common task handoff.");
        }
        finally { HostTaskActivationRequested = original; }
    }
}
