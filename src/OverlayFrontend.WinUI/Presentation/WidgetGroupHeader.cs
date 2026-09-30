using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native group label using the same resolved section-title style as SDK sections.</summary>
public sealed class WidgetGroupHeader : ContentControl
{
    private readonly TextBlock label = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private NativeComputedStyleAdapter? style;
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(WidgetGroupHeader),
        new PropertyMetadata("", (owner, _) => ((WidgetGroupHeader)owner).Refresh()));
    public static readonly DependencyProperty HeaderStyleProperty = DependencyProperty.Register(nameof(HeaderStyle), typeof(object), typeof(WidgetGroupHeader),
        new PropertyMetadata(null, (owner, _) => ((WidgetGroupHeader)owner).Refresh()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    // Opaque to XAML metadata generation: the validated transport record has
    // required members and must never be constructed by the XAML activator.
    public object? HeaderStyle { get => GetValue(HeaderStyleProperty); set => SetValue(HeaderStyleProperty, value); }
    public WidgetGroupHeader()
    {
        Content = label; IsTabStop = false; HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Margin = new(6, 16, 6, 8);
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => { style?.Dispose(); style = null; };
    }
    private void Refresh()
    {
        WidgetTextStyleAdapter.SetSource(label, Text);
        if (!IsLoaded) return;
        style ??= new NativeComputedStyleAdapter(label, false);
        style.Update(HeaderStyle as BridgeNodeRenderStyles);
    }
}
