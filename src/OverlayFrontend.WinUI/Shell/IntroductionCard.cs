using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Ordinary modeless content: only its actual buttons own input.</summary>
internal sealed partial class IntroductionCard : UserControl, IDisposable
{
    private readonly WidgetPreviewFrame frame = new();
    private readonly TextBlock title = new() { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock body = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock counter = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly StackPanel rows = new() { Spacing = 10 };
    private readonly Button primary = new(), skip = new();
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly List<(FontIcon Icon, ControllerPrompt Prompt)> glyphs = [];
    private readonly List<TextBlock> labels = [];
    private (ShellChromePalette Palette, PreviewFrameStyle Style, double Scale, double Height)? appearance;
    internal event Action<string>? Action;
    internal string Step { get; private set; } = "";
    internal bool Focused => XamlRoot is not null && Contains(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject);

    internal IntroductionCard()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Bottom;
        AutomationProperties.SetAutomationId(this, "Introduction.Card");
        AutomationProperties.SetName(this, "WidgetRail introduction");
        var copy = new StackPanel { Spacing = 12, Children = { counter, title, body, rows, status } };
        scroll.Content = copy;
        var buttons = new Grid { ColumnSpacing = 8 };
        foreach (var button in new[] { primary, skip })
        {
            var column = buttons.ColumnDefinitions.Count;
            buttons.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            buttons.Children.Add(button); Grid.SetColumn(button, column);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Padding = new(10, 8, 10, 8);
            button.IsTabStop = false;
            button.AllowFocusOnInteraction = false;
        }
        var layout = new Grid { RowSpacing = 16 };
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.Children.Add(scroll); layout.Children.Add(buttons); Grid.SetRow(buttons, 1);
        frame.Surface.Child = layout;
        Content = frame;
        Wire(primary, "next"); Wire(skip, "close");
        WidgetControllerPrompts.Changed += PromptsChanged;
    }

    private void Wire(Button button, string action)
    {
        AutomationProperties.SetAutomationId(button, "Introduction." + action);
        button.Click += (_, _) => Action?.Invoke(action);
    }

    internal void Present(string step, bool notifications, string openingLabel, ControllerPrompt openingPrompt, string? keyboardShortcut,
        string[]? visibleSteps = null, bool radial = false)
    {
        Step = step;
        appearance = null;
        glyphs.Clear(); labels.Clear(); rows.Children.Clear();
        var steps = visibleSteps ?? IntroductionProgress.TourSteps;
        var ordinal = Array.IndexOf(steps, step == "appearance" ? "appearance-details" : step) + 1;
        counter.Text = step == "welcome" ? "WELCOME" : ordinal > 0 ? $"QUICK TOUR  ·  {ordinal} / {steps.Length}" : "QUICK TOUR";
        (title.Text, body.Text) = step switch
        {
            "welcome" => ("Welcome to WidgetRail", "Start at the tray to open a widget. You can revisit the tour in Settings → Introduction."),
            "guide" => ("Inside a widget", "Open a widget and try moving between its controls. The guide follows your focus. You can click its hints, too."),
            "commands" => ("Tray quick actions", radial
                ? "Use the left stick to choose a widget. Try a D-pad direction to see what its shortcut does."
                : "Open Quick actions beside Manage pins. During this step, choosing a shortcut shows what it does."),
            "utilities" => ("Notifications and Task Switcher", "From the tray, open notifications when available, otherwise Task Switcher."),
            "catalogue" => ("WidgetRail Catalogue", "Find and install more widgets here."),
            "appearance" => ("Appearance", "Next opens Settings → Appearance."),
            _ => ("Appearance", "Choose a theme and adjust Interface size here in Settings → Appearance."),
        };
        if (step != "welcome" && ordinal == 0)
            body.Text = "This destination is unavailable. Continue to move on.";
        if (step == "welcome")
        {
            Row(ControllerPrompt.LeftStickMove, "Choose a widget", "Use the left stick in the radial tray or the arrow keys on the rail");
            Row(ControllerPrompt.A, "Select", "Enter or mouse click");
            Row(ControllerPrompt.B, "Go back", "Escape");
            Row(openingPrompt, openingLabel, keyboardShortcut is not null ? keyboardShortcut + " also toggles the overlay" : "Set a keyboard shortcut in Settings → Controllers",
                openingPrompt == ControllerPrompt.View ? ControllerPrompt.Menu : null);
        }
        else if (step == "guide")
        {
            Row(ControllerPrompt.DPad, "Move focus", "D-pad, left stick or arrow keys");
            Row(ControllerPrompt.RightStickMove, "Scroll", "Use the right stick to read through a list");
        }
        else if (step == "commands")
        {
            if (radial) Row(ControllerPrompt.DPad, "Use a shortcut", "After the tour, an assigned direction runs its action. An empty direction opens the chooser.");
            Row(ControllerPrompt.Menu, "Change a shortcut or pin", "Open Commands on the tray widget or right-click it");
        }
        else if (step == "utilities")
        {
            Row(ControllerPrompt.Y, "Tap to reorder widgets", "Use the tray to choose which widget to move");
            Row(ControllerPrompt.Y, notifications ? "Hold for Notifications" : "Hold for Task Switcher", "On the rail, click either icon or use the arrow keys and Enter");
        }
        ButtonLabel(primary, ControllerPrompt.X, step == "welcome" ? "Show me around" : step == steps[^1] ? "Finish" : "Continue");
        ButtonLabel(skip, ControllerPrompt.B, "Close tour");
        rows.Children.Add(new TextBlock { Text = "F8 to continue · Esc to close", TextWrapping = TextWrapping.Wrap });
        status.Visibility = Visibility.Collapsed;
        RefreshGlyphs();
    }

    private void Row(ControllerPrompt prompt, string text, string alternate, ControllerPrompt? chord = null)
    {
        var icon = new FontIcon { Width = 30, FontSize = 24, VerticalAlignment = VerticalAlignment.Top };
        glyphs.Add((icon, prompt));
        var heading = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var detail = new TextBlock { Text = alternate, TextWrapping = TextWrapping.Wrap };
        labels.Add(heading); labels.Add(detail);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var copy = new StackPanel { Spacing = 2, Children = { heading, detail } };
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { icon } };
        if (chord is { } other)
        {
            var second = new FontIcon { Width = 30, FontSize = 24 };
            glyphs.Add((second, other));
            icons.Children.Add(new TextBlock { Text = "+", VerticalAlignment = VerticalAlignment.Center });
            icons.Children.Add(second);
        }
        row.Children.Add(icons); row.Children.Add(copy); Grid.SetColumn(copy, 1);
        rows.Children.Add(row);
    }

    private void ButtonLabel(Button button, ControllerPrompt prompt, string label)
    {
        var icon = new FontIcon { FontSize = 20 };
        glyphs.Add((icon, prompt));
        button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icon, new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap } } };
        AutomationProperties.SetName(button, label);
    }
    private void PromptsChanged() { if (DispatcherQueue.HasThreadAccess) RefreshGlyphs(); else DispatcherQueue.TryEnqueue(RefreshGlyphs); }
    private void RefreshGlyphs()
    {
        foreach (var (icon, prompt) in glyphs)
            WidgetGlyphs.Apply(icon, new() { Id = "introduction.glyph", Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = prompt }, WidgetControllerPrompts.PlayStation);
    }
    internal void Apply(ShellChromePalette palette, PreviewFrameStyle style, double scale, double height)
    {
        if (appearance == (palette, style, scale, height)) return;
        appearance = (palette, style, scale, height);
        frame.Apply(style);
        frame.Surface.Padding = new(20);
        MaxHeight = height;
        scroll.MaxHeight = Math.Max(60, height - 105);
        title.FontSize = 24 * scale; body.FontSize = 15 * scale; counter.FontSize = 12 * scale;
        Foreground = new SolidColorBrush(palette.Text);
        counter.Foreground = new SolidColorBrush(palette.Muted);
        foreach (var label in labels) label.FontSize = 14 * scale;
        foreach (var button in new[] { primary, skip })
        {
            button.FontSize = 14 * scale;
            button.Foreground = new SolidColorBrush(palette.Text);
            button.Background = new SolidColorBrush(palette.Item);
            button.FocusVisualPrimaryBrush = new SolidColorBrush(palette.Focus);
        }
    }
    internal void ShowStatus(string? message) { status.Text = message ?? ""; status.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible; }
    internal bool Contains(DependencyObject? node)
    {
        for (; node is not null; node = VisualTreeHelper.GetParent(node)) if (ReferenceEquals(node, this)) return true;
        return false;
    }
    public void Dispose() { WidgetControllerPrompts.Changed -= PromptsChanged; frame.Dispose(); }
}
