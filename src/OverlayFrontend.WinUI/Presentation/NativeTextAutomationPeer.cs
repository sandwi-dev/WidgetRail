using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Projects native text semantics with provider-owned text lifetimes.</summary>
internal sealed partial class NativeTextAutomationPeer(FrameworkElement owner, FrameworkElement document) : FrameworkElementAutomationPeer(owner)
{
    private static readonly ConditionalWeakTable<FrameworkElement, NativeTextAutomationPeer> peers = new();
    private AutomationPeer Native => CreatePeerForElement(document);
    internal static IList<AutomationPeer> ProtectChildren(IList<AutomationPeer>? children) => children?.Select(peer =>
        peer is FrameworkElementAutomationPeer { Owner: TextBlock or RichTextBlock } element
            ? (AutomationPeer)peers.GetValue((FrameworkElement)element.Owner, text => new(text, text)) : peer).ToList() ?? [];
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    protected override string GetClassNameCore() => Native.GetClassName();
    protected override string GetNameCore() => Native.GetName();
    protected override string GetAutomationIdCore() => AutomationProperties.GetAutomationId(Owner) is { Length: > 0 } id
        ? id : AutomationProperties.GetAutomationId(document);
    protected override IList<AutomationPeer> GetChildrenCore() => ProtectChildren(Native.GetChildren());
    protected override object GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Text && Native.GetPattern(pattern) is ITextProvider text
        ? new NativeTextProviderLifetime(document, text, () => ProviderFromPeer(this))
        : base.GetPatternCore(pattern);
}
