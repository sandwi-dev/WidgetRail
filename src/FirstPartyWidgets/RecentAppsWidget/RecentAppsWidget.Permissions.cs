using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.RecentApps;

public sealed partial class RecentAppsWidget
{
    private string[] ActionPermissions(WidgetActionEvent action)
    {
        return action.ActionId == "retry" ? ["system.activity.recent.read.v1"] : [];
    }
    private void PermissionFeedback(string text) { lock (_gate) _status = text; Invalidate(); }
}
