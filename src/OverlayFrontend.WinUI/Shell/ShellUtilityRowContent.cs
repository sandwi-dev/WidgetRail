using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Stable row layout with a decorative prompt; the ListViewItem owns input.</summary>
internal sealed partial class ShellUtilityRowContent : Grid
{
    private readonly ContentPresenter body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly FontIcon glyph = new();
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel hint = new() { Orientation = Orientation.Horizontal, Spacing = 4,
        VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 0, 0), IsHitTestVisible = false, Opacity = 0 };
    internal UIElement Body { get => (UIElement)body.Content; set => body.Content = value; }
    internal bool HintShown => hint.Visibility == Visibility.Visible && hint.Opacity > 0;
    internal string? DismissLabel { get; private set; }

    internal ShellUtilityRowContent(UIElement content, string? dismissLabel)
    {
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Children.Add(body); Children.Add(hint); SetColumn(hint, 1);
        hint.Children.Add(glyph); hint.Children.Add(label);
        foreach (var element in new FrameworkElement[] { hint, glyph, label })
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
        Body = content; SetAction(dismissLabel); UpdatePrompt();
    }
    internal void SetAction(string? text)
    {
        DismissLabel = text; label.Text = text ?? "";
        hint.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }
    internal void SetFocused(bool focused) => hint.Opacity = focused ? 1 : 0;
    internal void UpdatePrompt() => WidgetGlyphs.Apply(glyph, new() { Id = "utility.dismiss", Kind = ViewNodeKind.ControllerGlyph,
        ControllerPrompt = ControllerPrompt.X }, WidgetControllerPrompts.PlayStation);
    internal void Apply(ShellChromePalette palette, double scale)
    {
        glyph.FontSize = 20 * scale; label.FontSize = 13 * scale;
        glyph.Foreground = label.Foreground = new SolidColorBrush(palette.Text);
        if (Body is ShellTaskWindowRow task) task.Apply(palette, scale);
        if (Body is ShellUtilityActionRow action) action.Apply(palette, scale);
        if (Body is NotificationCard card) card.ApplyBannerPalette(palette);
    }
}
