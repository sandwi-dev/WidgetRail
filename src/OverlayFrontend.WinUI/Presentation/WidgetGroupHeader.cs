using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Data;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native group label using the same resolved section-title style as SDK sections.</summary>
public sealed partial class WidgetGroupHeader : ContentControl
{
    private readonly TextBlock label = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly Grid row = new();
    private NativeComputedStyleAdapter? style;
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(WidgetGroupHeader),
        new PropertyMetadata("", (owner, _) => ((WidgetGroupHeader)owner).Refresh()));
    public static readonly DependencyProperty ShowRuleProperty = DependencyProperty.Register(nameof(ShowRule), typeof(bool), typeof(WidgetGroupHeader),
        new PropertyMetadata(false, (owner, _) => ((WidgetGroupHeader)owner).UpdateLayoutContent()));
    public bool ShowRule { get => (bool)GetValue(ShowRuleProperty); set => SetValue(ShowRuleProperty, value); }
    public static readonly DependencyProperty HeaderStyleProperty = DependencyProperty.Register(nameof(HeaderStyle), typeof(object), typeof(WidgetGroupHeader),
        new PropertyMetadata(null, (owner, _) => ((WidgetGroupHeader)owner).Refresh()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    // Opaque to XAML metadata generation: the validated transport record has
    // required members and must never be constructed by the XAML activator.
    public object? HeaderStyle { get => GetValue(HeaderStyleProperty); set => SetValue(HeaderStyleProperty, value); }
    public WidgetGroupHeader()
    {
        row.Children.Add(label);
        Content = row; IsTabStop = false; HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Margin = new(6, 16, 6, 8);
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => { style?.Dispose(); style = null; };
    }
    private void UpdateLayoutContent()
    {
        row.ColumnDefinitions.Clear();
        if (row.Children.Count > 1) row.Children.RemoveAt(1);
        if (!ShowRule)
        {
            Margin = new(6, 16, 6, 8);
            return;
        }
        Margin = new(0, 16, 8, 8);
        row.ColumnSpacing = 12;
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Opacity = .54, IsHitTestVisible = false };
        rule.SetBinding(Border.BackgroundProperty, new Binding { Source = label, Path = new PropertyPath("Foreground") });
        AutomationProperties.SetAccessibilityView(rule, AccessibilityView.Raw);
        Grid.SetColumn(rule, 1);
        row.Children.Add(rule);
    }
    private void Refresh()
    {
        WidgetTextStyleAdapter.SetSource(label, Text);
        if (!IsLoaded) return;
        style ??= new NativeComputedStyleAdapter(label, false);
        style.Update(HeaderStyle as BridgeNodeRenderStyles);
    }
}
