using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public enum WidgetApplicationControl { None, Quit, Restart }

public sealed partial class WidgetPresentationSession
{
    public const string ReplayIntroductionAction = "application.introduction";
    public void ValidateIntroductionCommand(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            if (displayed.Authority.WidgetId != "settings" || action.ActionId != ReplayIntroductionAction ||
                action.SourceElementId != "settings.introduction" || action.InputScopeId != "settings-root" ||
                action.Phase != ControllerEventPhase.Pressed || action.RequestedValue is not null || action.CommittedText is not null ||
                ResolveDisplayedAction(displayed.Snapshot, action) is not { } declared || declared != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("Introduction requires the current Settings command.");
        }
    }

    public static bool IsApplicationControlAction(string action) => action is "application.quit" or "application.restart";

    /// <summary>Validates a currently displayed Settings command without queueing behind widget work.</summary>
    public WidgetApplicationControl ResolveApplicationControl(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            var restart = action.ActionId == "application.restart";
            if (displayed.Authority.WidgetId != "settings" || !IsApplicationControlAction(action.ActionId) ||
                action.InputScopeId != "settings-root" || action.SourceElementId != (restart ? "settings.restart" : "settings.quit") ||
                action.Phase != ControllerEventPhase.Pressed || action.RequestedValue is not null || action.CommittedText is not null ||
                ResolveDisplayedAction(displayed.Snapshot, action) != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("Application control requires the current Settings command.");
            return restart ? WidgetApplicationControl.Restart : WidgetApplicationControl.Quit;
        }
    }

    /// <summary>
    /// Consumes a trusted Settings application request once. Poll from the host
    /// control plane, independently of the visible widget or controller input.
    /// </summary>
    public async Task<WidgetApplicationControl> TakeApplicationControlAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(BridgeMessageTypes.ApplicationControl,
            new BridgeEmptyPayload(), BridgeMessageTypes.ApplicationControl, cancellationToken).ConfigureAwait(false);
        RequireObjectProperties(response.Payload, "action");
        var action = ReadInt64(response.Payload, "action", minimum: 0);
        return action switch
        {
            0 => WidgetApplicationControl.None,
            1 => WidgetApplicationControl.Quit,
            2 => WidgetApplicationControl.Restart,
            _ => throw new BridgeProtocolException("WidgetBridge returned an invalid application control request."),
        };
    }
}
