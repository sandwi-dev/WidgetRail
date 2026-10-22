using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Authored-state disclosure header; commands never mutate expansion locally.</summary>
internal sealed partial class WidgetDisclosureButton : Button
{
    private bool expanded;
    private readonly ContentPresenter content = new();
    internal object? DisplayContent => content.Content;
    private readonly FontIcon chevron = new() { Glyph = "\uE70D", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
        IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid body = new() { ColumnSpacing = 12,
        ColumnDefinitions = { new() { Width = new(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
    internal WidgetDisclosureButton()
    {
        DefaultStyleKey = typeof(Button);
        Grid.SetColumn(chevron, 1);
        body.Children.Add(content); body.Children.Add(chevron);
        chevron.SetBinding(FontIcon.ForegroundProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = this, Path = new PropertyPath(nameof(Foreground)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        chevron.SetBinding(FontIcon.FontSizeProperty, new Microsoft.UI.Xaml.Data.Binding
            { Source = this, Path = new PropertyPath(nameof(FontSize)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        AutomationProperties.SetAccessibilityView(chevron, AccessibilityView.Raw);
    }
    internal void SetLabel(string value)
    {
        if (content.Content is not TextBlock label) content.Content = label = new TextBlock { TextWrapping = TextWrapping.Wrap };
        WidgetTextStyleAdapter.SetSource(label, value);
        Content = body;
    }
    internal void SetDisplayContent(UIElement value)
    {
        content.Content = value;
        Content = body;
    }
    internal void SetExpanded(bool value)
    {
        if (expanded == value) return;
        var previous = State;
        expanded = value;
        chevron.Glyph = value ? "\uE70E" : "\uE70D";
        FrameworkElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, previous, State);
    }
    private ExpandCollapseState State => expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    private void Request(bool value)
    {
        if (expanded == value || !IsEnabled || Command?.CanExecute(CommandParameter) != true) return;
        // The ordinary presenter command validates current owner, scope, busy state,
        // and action authority, including calls from a peer retained after removal.
        Command.Execute(CommandParameter);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new DisclosurePeer(this);
    private sealed partial class DisclosurePeer(WidgetDisclosureButton owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        protected override object GetPatternCore(PatternInterface pattern) =>
            pattern == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(pattern);
        public ExpandCollapseState ExpandCollapseState => owner.State;
        public void Expand() => owner.Request(true);
        public void Collapse() => owner.Request(false);
    }
}
