using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.TaskSwitcher;

public sealed partial class TaskSwitcherWidget
{
    private string[] ActionPermissions(WidgetActionEvent action)
    {
        if (action.ActionId.StartsWith("switch.", StringComparison.Ordinal)) return ["system.apps.windows.switch.v1"];
        if (action.ActionId.StartsWith("close.", StringComparison.Ordinal)) return ["system.apps.windows.close.v1"];
        return action.ActionId == "refresh" ? ["system.apps.windows.read.v1", "system.apps.windows.preview.v1"] : [];
    }
    private void PermissionFeedback(string text) { lock (_gate) SetToastLocked(text, ToastTone.Warning); Invalidate(); }
}
