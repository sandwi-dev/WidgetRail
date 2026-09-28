using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>Host-only controls and aspect-fit native media placement. No widget view or browser is recreated.</summary>
internal sealed partial class MediaFullscreenView : Grid
{
    internal Grid SurfaceHost { get; } = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid area = new();
    private readonly Grid commands = new() { ColumnSpacing = 12, RowSpacing = 8 };
    private int commandColumns;
    private readonly TextBlock title = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly List<(FontIcon Icon, ControllerPrompt Prompt)> prompts = [];
    private readonly Dictionary<Button, Action> actions = [];
    private readonly Button back;
    private Button? remembered;
    private double aspectRatio = 16d / 9;
    internal event Action? ExitRequested;
    internal event Action<EmbeddedMediaHostCommand>? CommandRequested;

    internal MediaFullscreenView()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        RowSpacing = 12; Padding = new Thickness(16);
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        Children.Add(title); Grid.SetRow(area, 1); Children.Add(area); area.Children.Add(SurfaceHost);
        commands.Children.Add(Command("Play / Pause", "Toggle", ControllerPrompt.X, () => CommandRequested?.Invoke(EmbeddedMediaHostCommand.TogglePlayback)));
        commands.Children.Add(Command("Rewind", "Rewind", ControllerPrompt.LeftTrigger, () => CommandRequested?.Invoke(EmbeddedMediaHostCommand.SeekBackward)));
        commands.Children.Add(Command("Forward", "Forward", ControllerPrompt.RightTrigger, () => CommandRequested?.Invoke(EmbeddedMediaHostCommand.SeekForward)));
        commands.Children.Add(back = Command("Back", "Back", ControllerPrompt.B, () => ExitRequested?.Invoke()));
        Grid.SetRow(commands, 2); Children.Add(commands);
        TabFocusNavigation = KeyboardNavigationMode.Cycle;
        AutomationProperties.SetAutomationId(this, "Overlay.MediaFullscreen");
        AutomationProperties.SetAutomationId(SurfaceHost, "Overlay.MediaFullscreen.Viewport");
        AutomationProperties.SetAutomationId(title, "Overlay.MediaFullscreen.Title");
        area.SizeChanged += (_, _) => Fit();
        SizeChanged += (_, _) => ReflowCommands();
        Loaded += (_, _) => { WidgetControllerPrompts.Changed += RefreshPrompts; RefreshPrompts(); };
        Unloaded += (_, _) => WidgetControllerPrompts.Changed -= RefreshPrompts;
        KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; ExitRequested?.Invoke(); } };
        GotFocus += (_, _) => { if (FocusManager.GetFocusedElement(XamlRoot) is Button button && actions.ContainsKey(button)) remembered = button; };
        ReflowCommands();
    }

    internal void Show(EmbeddedMediaSession definition)
    {
        title.Text = definition.AccessibleName;
        var changed = aspectRatio != definition.AspectRatio || Visibility != Visibility.Visible;
        aspectRatio = definition.AspectRatio;
        AutomationProperties.SetName(this, definition.AccessibleName + " fullscreen");
        Visibility = Visibility.Visible;
        if (changed) Fit();
    }
    internal void Enter() => (remembered ?? back).Focus(FocusState.Keyboard);
    internal void ActivateFocused()
    {
        if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button { IsEnabled: true } button && actions.TryGetValue(button, out var action)) action();
    }
    internal void Hide() => Visibility = Visibility.Collapsed;
    private void Fit()
    {
        var width = Math.Min(area.ActualWidth, area.ActualHeight * aspectRatio);
        SurfaceHost.Width = Math.Max(0, width);
        SurfaceHost.Height = Math.Max(0, width / aspectRatio);
    }
    private Button Command(string label, string id, ControllerPrompt prompt, Action action)
    {
        var icon = new FontIcon(); prompts.Add((icon, prompt));
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1); content.Children.Add(icon); content.Children.Add(text);
        var button = new Button { Content = content, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(button, "Overlay.MediaFullscreen." + id);
        AutomationProperties.SetName(button, id switch { "Toggle" => "Toggle playback", "Rewind" => "Seek backward", "Forward" => "Seek forward", _ => "Return to widget" });
        button.Click += (_, _) => action();
        actions.Add(button, action);
        return button;
    }
    private void ReflowCommands()
    {
        var count = ActualWidth >= 600 ? 4 : 2;
        if (count == commandColumns) return;
        commandColumns = count;
        commands.ColumnDefinitions.Clear(); commands.RowDefinitions.Clear();
        for (var index = 0; index < count; ++index) commands.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < 4 / count; ++index) commands.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < commands.Children.Count; ++index)
        { Grid.SetColumn((FrameworkElement)commands.Children[index], index % count); Grid.SetRow((FrameworkElement)commands.Children[index], index / count); }
    }
    private void RefreshPrompts()
    {
        foreach (var (icon, prompt) in prompts)
            WidgetGlyphs.Apply(icon, new() { Id = "fullscreen-prompt", Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = prompt }, WidgetControllerPrompts.PlayStation);
    }
}
