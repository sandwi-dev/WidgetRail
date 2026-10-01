using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native theme defaults; resolved widget styles can override these properties.</summary>
internal sealed partial class WidgetModalPanel : Grid
{
    public WidgetModalPanel() => InitializeComponent();
    protected override AutomationPeer OnCreateAutomationPeer() => new ModalPeer(this);
    private sealed partial class ModalPeer(WidgetModalPanel owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(WidgetModalPanel);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override bool IsControlElementCore() => true;
    }
}
