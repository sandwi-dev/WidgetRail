using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
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
    private readonly List<TextBlock> labels = [];
    private readonly Button back;
    private Button? remembered;
    private double aspectRatio = 16d / 9;
    private readonly bool compact;
    private Button? previousMedia, nextMedia;
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, IsHitTestVisible = false, Height = 3 };
    internal event Action? ExitRequested;
    internal event Action<EmbeddedMediaHostCommand>? CommandRequested;
    internal event Action<EmbeddedMediaCommand>? NavigationRequested;

    internal MediaFullscreenView(bool compact = false)
    {
        this.compact = compact;
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        // Chrome overlays the aspect-fit video; it never reserves video space.
        Children.Add(area); area.Children.Add(SurfaceHost);
        title.VerticalAlignment = VerticalAlignment.Top;
        title.Margin = new Thickness(16);
        Children.Add(title);
        commands.VerticalAlignment = VerticalAlignment.Bottom;
        commands.HorizontalAlignment = HorizontalAlignment.Center;
        commands.Margin = new Thickness(12);
        commands.Padding = new Thickness(8);
        commands.CornerRadius = new CornerRadius(12);
        commands.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(210, 20, 20, 24));
        commands.Children.Add(Command("Play / Pause", "Toggle", ControllerPrompt.X, () => CommandRequested?.Invoke(EmbeddedMediaHostCommand.TogglePlayback)));
        commands.Children.Add(Command("Rewind", "Rewind", ControllerPrompt.LeftTrigger, () => CommandRequested?.Invoke(EmbeddedMediaHostCommand.SeekBackward)));
        commands.Children.Add(Command("Forward", "Forward", ControllerPrompt.RightTrigger, () => CommandRequested?.Invoke(EmbeddedMediaHostCommand.SeekForward)));
        commands.Children.Add(back = Command("Back", "Back", ControllerPrompt.B, () => ExitRequested?.Invoke()));
        if (compact)
        {
            commands.Children.Add(previousMedia = Command("Previous", "Previous", ControllerPrompt.LeftBumper, () => NavigationRequested?.Invoke(EmbeddedMediaCommand.Previous)));
            commands.Children.Add(nextMedia = Command("Next", "Next", ControllerPrompt.RightBumper, () => NavigationRequested?.Invoke(EmbeddedMediaCommand.Next)));
            commands.ColumnSpacing = 2;
            commands.Margin = new Thickness(6);
            commands.Padding = new Thickness(4);
            AutomationProperties.SetAutomationId(this, "Overlay.MediaCompact");
            AutomationProperties.SetAutomationId(SurfaceHost, "Overlay.MediaCompact.Viewport");
        }
        Children.Add(commands);
        progress.VerticalAlignment = VerticalAlignment.Bottom;
        Children.Add(progress);
        InitializeAutoHide();
        TabFocusNavigation = KeyboardNavigationMode.Cycle;
        AutomationProperties.SetAutomationId(this, compact ? "Overlay.MediaCompact" : "Overlay.MediaFullscreen");
        AutomationProperties.SetAutomationId(SurfaceHost, compact ? "Overlay.MediaCompact.Viewport" : "Overlay.MediaFullscreen.Viewport");
        AutomationProperties.SetAutomationId(title, "Overlay.MediaFullscreen.Title");
        area.SizeChanged += (_, _) => Fit();
        SizeChanged += (_, _) => ReflowCommands();
        commands.SizeChanged += (_, _) => ReflowCommands();
        Loaded += (_, _) => { WidgetControllerPrompts.Changed += RefreshPrompts; RefreshPrompts(); };
        Unloaded += (_, _) => { WidgetControllerPrompts.Changed -= RefreshPrompts; controlsTimer.Stop(); };
        KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; ExitRequested?.Invoke(); } };
        GotFocus += (_, _) => { if (FocusManager.GetFocusedElement(XamlRoot) is Button button && actions.ContainsKey(button)) remembered = button; };
        ReflowCommands();
    }

    internal void Show(EmbeddedMediaSession definition)
    {
        title.Text = definition.AccessibleName;
        if (previousMedia is not null) previousMedia.IsEnabled = definition.Commands.Contains(EmbeddedMediaCommand.Previous);
        if (nextMedia is not null) nextMedia.IsEnabled = definition.Commands.Contains(EmbeddedMediaCommand.Next);
        var changed = aspectRatio != definition.AspectRatio || Visibility != Visibility.Visible;
        aspectRatio = definition.AspectRatio;
        AutomationProperties.SetName(this, definition.AccessibleName + (compact ? " pinned media" : " fullscreen"));
        Visibility = Visibility.Visible;
        if (changed) { Fit(); if (!compact) RevealControls(); }
    }
    internal void Enter() { RevealControls(); FocusControls(); }
    internal void SetInteraction(bool enabled)
    {
        if (!compact) return;
        controlsInteractive = enabled;
        title.Visibility = Visibility.Collapsed;
        if (enabled) RevealControls(); else HideControls();
    }
    internal void UpdatePlayback(EmbeddedMediaPlaybackEvent? playback)
    {
        if (playback is null) return;
        progress.Value = playback.DurationSeconds > 0 ? Math.Clamp(playback.PositionSeconds / playback.DurationSeconds, 0, 1) : 0;
    }
    internal void ActivateFocused()
    {
        if (commands.Visibility != Visibility.Visible || XamlRoot is null ||
            FocusManager.GetFocusedElement(XamlRoot) is not Button focused || !actions.ContainsKey(focused))
        { Enter(); return; }
        RevealControls();
        if (focused.IsEnabled) actions[focused]();
    }
    internal void Hide() { controlsTimer.Stop(); pointerOverControls = false; Visibility = Visibility.Collapsed; }
    internal void RegisterChrome(ShellChromeStyles styles)
    {
        // This surface lives outside the widget declaration, but remains inside
        // the shell scale root. Use the same theme/accessibility adapters once;
        // do not multiply font or glyph sizes by interface zoom a second time.
        styles.Register(title, "title");
        foreach (var label in labels) styles.Register(label, "body");
        foreach (var (icon, _) in prompts) styles.Register(icon, "controller-glyph");
        styles.Register(commands, "tray");
        foreach (var button in actions.Keys) styles.Register(button, "tray-item");
        styles.Register(progress, "body");
    }
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
        var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Visibility = compact ? Visibility.Collapsed : Visibility.Visible };
        labels.Add(text);
        Grid.SetColumn(text, 1); content.Children.Add(icon); content.Children.Add(text);
        var button = new Button { Content = content, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(compact ? 7 : 12, 6, compact ? 7 : 12, 6) };
        NativePopupTheme.SetToolTip(button, label);
        AutomationProperties.SetAutomationId(button, (compact ? "Overlay.MediaCompact." : "Overlay.MediaFullscreen.") + id);
        AutomationProperties.SetName(button, id switch { "Toggle" => "Toggle playback", "Rewind" => "Seek backward", "Forward" => "Seek forward", "Back" => "Return to widget", _ => label });
        button.Click += (_, _) => { RevealControls(); action(); };
        actions.Add(button, action);
        return button;
    }
    private void ReflowCommands()
    {
        var compactButtonWidth = Math.Max(36, prompts.Max(value => value.Icon.FontSize) +
            actions.Keys.Max(button => button.Padding.Left + button.Padding.Right) + 8);
        var compactWidth = ActualWidth - commands.Margin.Left - commands.Margin.Right - commands.Padding.Left - commands.Padding.Right;
        var count = compact ? compactWidth >= 6 * compactButtonWidth + 5 * commands.ColumnSpacing ? 6 : 3
            : ActualWidth >= 600 ? 4 : 2;
        if (count == commandColumns) return;
        commandColumns = count;
        commands.ColumnDefinitions.Clear(); commands.RowDefinitions.Clear();
        for (var index = 0; index < count; ++index) commands.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < (commands.Children.Count + count - 1) / count; ++index) commands.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < commands.Children.Count; ++index)
        { Grid.SetColumn((FrameworkElement)commands.Children[index], index % count); Grid.SetRow((FrameworkElement)commands.Children[index], index / count); }
    }
    private void RefreshPrompts()
    {
        foreach (var (icon, prompt) in prompts)
            WidgetGlyphs.Apply(icon, new() { Id = "fullscreen-prompt", Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = prompt }, WidgetControllerPrompts.PlayStation);
    }
}
