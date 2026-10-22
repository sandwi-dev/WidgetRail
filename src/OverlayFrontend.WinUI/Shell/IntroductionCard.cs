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

    internal void Present(string step, bool notifications, string openingLabel, ControllerPrompt openingPrompt, bool f1)
    {
        Step = step;
        appearance = null;
        glyphs.Clear(); labels.Clear(); rows.Children.Clear();
        counter.Text = step == "welcome" ? "WELCOME" : $"QUICK TOUR  ·  {Math.Clamp(Array.IndexOf(IntroductionProgress.Steps, step), 1, 5)} / 5";
        (title.Text, body.Text) = step switch
        {
            "welcome" => ("Welcome to WidgetRail", "Thanks for installing! Find this tour anytime in Settings → Introduction."),
            "guide" => ("Control guide", "The guide shows controls for your current focus. Its action hints are clickable."),
            "commands" => ("Widget commands", "Select a widget in the tray for pinning and other commands."),
            "utilities" => ("Notifications and Task Switcher", "From the tray, open notifications when available, otherwise Task Switcher."),
            "catalogue" => ("WidgetRail Catalogue", "Find and install more widgets here."),
            "appearance" => ("Appearance", "Next opens Settings → Appearance."),
            _ => ("Appearance", "Choose a theme and adjust Interface size here in Settings → Appearance."),
        };
        if (step == "welcome")
        {
            Row(ControllerPrompt.LeftStickMove, "Move focus", "D-pad or left stick · keyboard arrows");
            Row(ControllerPrompt.A, "Select", "Enter or mouse click");
            Row(ControllerPrompt.B, "Go back", "Escape");
            Row(openingPrompt, openingLabel, f1 ? "F1 also toggles the overlay" : "F1 can be enabled in Settings → Controllers",
                openingPrompt == ControllerPrompt.View ? ControllerPrompt.Menu : null);
        }
        else if (step == "commands") Row(ControllerPrompt.Menu, "Commands", "Right-click a tray item or click Commands in the guide");
        else if (step == "utilities") Row(ControllerPrompt.Y, notifications ? "Hold for Notifications" : "Hold for Task Switcher", "Mouse: click its icon · keyboard: arrows, then Enter");
        ButtonLabel(primary, ControllerPrompt.X, step == "welcome" ? "Show me around" : step == "appearance-details" ? "Finish" : "Continue");
        ButtonLabel(skip, ControllerPrompt.B, "Close tour");
        rows.Children.Add(new TextBlock { Text = "Keyboard: F8 to continue · Esc to close", TextWrapping = TextWrapping.Wrap });
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
