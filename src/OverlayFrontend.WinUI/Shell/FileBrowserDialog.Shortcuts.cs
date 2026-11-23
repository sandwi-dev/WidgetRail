using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class FileBrowserDialog
{
    private readonly Grid shortcutStrip = new() { ColumnSpacing = 14, RowSpacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<(FontIcon Icon, ViewNode Prompt)> shortcutGlyphs = [];
    private int shortcutColumns;

    private StackPanel ShortcutLabel(ControllerPrompt prompt, string label)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var icon = new FontIcon { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        group.Children.Add(icon);
        group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        shortcutGlyphs.Add((icon, new ViewNode { Id = "files.hint." + prompt, Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = prompt }));
        return group;
    }

    private void BuildShortcutStrip()
    {
        if (request.Mode == FileBrowserMode.Multiple)
            shortcutStrip.Children.Add(ShortcutLabel(ControllerPrompt.LeftTrigger, "Hold to select"));
        shortcutStrip.Children.Add(ShortcutLabel(ControllerPrompt.Y, "Search"));
        shortcutStrip.Children.Add(ShortcutLabel(ControllerPrompt.B, "Back"));
        shortcutStrip.Children.Add(ShortcutLabel(ControllerPrompt.X, request.CommitLabel));
        shortcutStrip.Children.Add(ShortcutLabel(ControllerPrompt.Menu, "Cancel"));
    }

    private void LayoutShortcuts(bool compact)
    {
        var columns = compact ? (body.Width < 360 ? 2 : 3) : shortcutStrip.Children.Count;
        if (shortcutColumns == columns)
            return;
        shortcutColumns = columns;
        shortcutStrip.ColumnDefinitions.Clear();
        shortcutStrip.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++)
            shortcutStrip.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        for (var i = 0; i < shortcutStrip.Children.Count; i++)
        {
            if (i % columns == 0)
                shortcutStrip.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow((FrameworkElement)shortcutStrip.Children[i], i / columns);
            Grid.SetColumn((FrameworkElement)shortcutStrip.Children[i], i % columns);
        }
    }

    private void RefreshShortcutGlyphs()
    {
        foreach (var (icon, prompt) in shortcutGlyphs)
            WidgetGlyphs.Apply(icon, prompt, WidgetControllerPrompts.PlayStation);
    }
}
