using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetRichTextView
{
    protected override AutomationPeer OnCreateAutomationPeer() => new NativeTextAutomationPeer(this, Document);
}
