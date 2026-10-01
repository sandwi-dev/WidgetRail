using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>One semantic production root, independent of temporary visible status text.</summary>
public sealed partial class ProductionShellRoot : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new RootPeer(this);
    private sealed partial class RootPeer(ProductionShellRoot root) : FrameworkElementAutomationPeer(root)
    {
        protected override string GetClassNameCore() => nameof(ProductionShellRoot);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override bool IsControlElementCore() => true;
    }
}
