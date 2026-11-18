using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>A reading surface exposes native activation only when its current preview declares it.</summary>
internal sealed partial class WidgetReadingSurface : ContentControl
{
    internal WidgetReadingSurface() => DefaultStyleKey = typeof(ContentControl);
    private bool primaryTemplate;
    internal void SetPrimaryPresentation(bool enabled)
    {
        if (primaryTemplate == enabled) return;
        primaryTemplate = enabled;
        if (enabled) Template = (ControlTemplate)Application.Current.Resources["WidgetReadingSurfaceTemplate"];
        else ClearValue(TemplateProperty);
    }
    internal Func<bool>? HasPrimary { get; set; }
    internal Func<bool>? PrimaryExpanded { get; set; }
    internal Action? PrimaryRequested { get; set; }
    internal Action? CollapseRequested { get; set; }
    protected override AutomationPeer OnCreateAutomationPeer() => new ReadingPeer(this);

    private sealed partial class ReadingPeer(WidgetReadingSurface owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider, IExpandCollapseProvider
    {
        protected override string GetClassNameCore() => nameof(ContentControl);
        protected override AutomationControlType GetAutomationControlTypeCore() => owner.HasPrimary?.Invoke() == true
            ? AutomationControlType.Button : AutomationControlType.Group;
        protected override object GetPatternCore(PatternInterface pattern) => pattern switch
        {
            PatternInterface.Invoke or PatternInterface.ExpandCollapse => owner.HasPrimary?.Invoke() == true ? this : null!,
            _ => base.GetPatternCore(pattern),
        };
        public void Invoke() { if (owner.IsEnabled && owner.HasPrimary?.Invoke() == true) owner.PrimaryRequested?.Invoke(); }
        public ExpandCollapseState ExpandCollapseState => owner.PrimaryExpanded?.Invoke() == true
            ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() => Invoke();
        public void Collapse() { if (owner.HasPrimary?.Invoke() == true) owner.CollapseRequested?.Invoke(); }
    }
}
