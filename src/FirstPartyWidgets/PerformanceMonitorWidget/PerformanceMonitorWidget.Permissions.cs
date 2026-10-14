using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.PerformanceMonitor;

public sealed partial class PerformanceMonitorWidget
{
    private string[] ActionPermissions(WidgetActionEvent action)
    {
        return action.ActionId switch { "performance.fps" => ["system.performance.read.v1", "system.performance.fps.control.v1"], "performance.refresh" => ["system.performance.read.v1"], _ => [] };
    }
    private void PermissionFeedback(string text) { lock (_gate) _error = text; Invalidate(); }
}
