using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Power;

public sealed partial class PowerWidget
{
    private string[] ActionPermissions(WidgetActionEvent action)
    {
        return action.ActionId switch { "sleep" or "confirm.shutdown" or "confirm.restart" => ["system.power.control.v1"], "check" => ["system.power.read.v1"], _ => [] };
    }
    private void PermissionFeedback(string text) { lock (_gate) ShowToastLocked(text, ToastTone.Warning); Invalidate(); }
}
