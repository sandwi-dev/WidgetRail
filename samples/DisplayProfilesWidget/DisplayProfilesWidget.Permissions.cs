using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DisplayProfiles;

public sealed partial class DisplayProfilesWidget
{
    private string[] ActionPermissions(WidgetActionEvent action)
    {
        if (action.ActionId.StartsWith("apply.", StringComparison.Ordinal) || action.ActionId is "commit-name" or "confirm") return ["system.displays.read.v1", "system.displays.control.v1"];
        // Never interpose a consent dialog in a running restore's timed Keep/Revert flow.
        return action.ActionId == "retry" ? ["system.displays.read.v1"] : [];
    }
    private void PermissionFeedback(string text) { lock (_gate) Toast(text, ToastTone.Warning); Invalidate(); }
}
