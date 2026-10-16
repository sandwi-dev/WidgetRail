using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public const string AddWebAppAction = "host.add-webapp";
    public const string ManageWebAppsAction = "host.manage-webapps";

    public void ValidateWebAppManagement(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            if (displayed.Authority.WidgetId != "settings" || action.InputScopeId != "installed.widgets" ||
                action.Phase != ControllerEventPhase.Pressed || action.RequestedValue is not null || action.CommittedText is not null ||
                !(action.ActionId == AddWebAppAction && action.SourceElementId == "installed.add-webapp" ||
                  action.ActionId == ManageWebAppsAction && action.SourceElementId == "installed.manage-webapps") ||
                ResolveDisplayedAction(displayed.Snapshot, action) != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("Webapps can only be managed from the current Settings command.");
        }
    }
}
