using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native command button exposing the value it edits in a separate native popup.</summary>
internal sealed partial class WidgetValueButton : Button
{
    private readonly TextBlock valueLabel = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon affordance = new() { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private string accessibleValue = string.Empty;
    private bool sensitive;
    private bool expanded;
    internal Action? ExpandRequested { get; set; }
    internal Action? CollapseRequested { get; set; }
    internal WidgetValueButton()
    {
        DefaultStyleKey = typeof(Button);
        Grid.SetColumn(affordance, 1);
        Content = new Grid { ColumnSpacing = 12,
            ColumnDefinitions = { new() { Width = new(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } },
            Children = { valueLabel, affordance } };
        affordance.SetBinding(FontIcon.ForegroundProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = this, Path = new PropertyPath(nameof(Foreground)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        affordance.SetBinding(FontIcon.FontSizeProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = this, Path = new PropertyPath(nameof(FontSize)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        AutomationProperties.SetAccessibilityView(affordance, AccessibilityView.Raw);
    }
    internal void SetLabel(string value)
    {
        WidgetTextStyleAdapter.SetSource(valueLabel, value);
        affordance.Glyph = ExpandRequested is null ? "\uE70F" : "\uE70D";
    }

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
