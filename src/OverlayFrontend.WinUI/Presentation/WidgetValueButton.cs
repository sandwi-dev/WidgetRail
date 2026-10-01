using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native command button exposing the value it edits in a separate native popup.</summary>
internal sealed partial class WidgetValueButton : Button
{
    private string accessibleValue = string.Empty;
    private bool sensitive;
    private bool expanded;
    internal Action? ExpandRequested { get; set; }
    internal Action? CollapseRequested { get; set; }
    internal WidgetValueButton() => DefaultStyleKey = typeof(Button);

    internal void SetAccessibleValue(string? value, bool isSensitive)
    {
        var previous = accessibleValue;
        sensitive = isSensitive;
        accessibleValue = isSensitive ? string.Empty : value ?? string.Empty;
        if (previous != accessibleValue && FrameworkElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previous, accessibleValue);
    }

    internal void SetExpanded(bool value)
    {
        if (expanded == value) return;
        var previous = State;
        expanded = value;
        FrameworkElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, previous, State);
    }
    private ExpandCollapseState State => expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    protected override AutomationPeer OnCreateAutomationPeer() => new ValuePeer(this);

    private sealed partial class ValuePeer(WidgetValueButton owner) : ButtonAutomationPeer(owner), IValueProvider, IExpandCollapseProvider
    {
        protected override object GetPatternCore(PatternInterface pattern) => pattern switch
        {
            PatternInterface.Value => owner.sensitive ? null! : this,
            PatternInterface.ExpandCollapse => owner.ExpandRequested is null ? null! : this,
            _ => base.GetPatternCore(pattern),
        };
        protected override bool IsPasswordCore() => owner.sensitive;
        public bool IsReadOnly => true;
        public string Value => owner.sensitive ? string.Empty : owner.accessibleValue;
        public void SetValue(string value) => throw new InvalidOperationException("Invoke the control to edit its value.");
        public ExpandCollapseState ExpandCollapseState => owner.State;
        public void Expand() { if (owner.IsEnabled) owner.ExpandRequested?.Invoke(); }
        public void Collapse() => owner.CollapseRequested?.Invoke();
    }
}
