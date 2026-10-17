using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    // A widget-side traversal policy, not a claim that every Scroll overflows.
    // Nine 44-64 DIP rows span most of a panel before its header and footer.
    internal static bool NeedsQuickControls(int rows) => rows > 8;
    private readonly HashSet<string> quickControlTargets = new(StringComparer.Ordinal);
    private string? quickControlTarget;
    private bool BuildControlsAvailable => result is not null && !ManagementView && !busy &&
        pendingBefore is null && pendingImport is null && deleteBuildId is null && skillRemoval is null && featureReading is null && featureRemoval is null;

    private ContainerElement QuickControls(ContainerElement group, string initial, string destination,
        string label, bool enabled, bool preview = false)
    {
        group=group.RememberChildFocus(initial);
        if(!enabled || busy || pendingBefore is not null&&!preview)return group;
        quickControlTargets.Add(destination);
        return group.Shortcut(ControllerButton.LeftStick,"quick-controls."+destination,label);
    }
}
