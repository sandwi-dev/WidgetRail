using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Bounded native cells, measured whole labels, and one stable-height guide slot.</summary>
internal sealed partial class ShellControllerGuide : ContentControl, IDisposable
{
    private sealed class GuideLayer
    {
        internal readonly GuidePanel Panel = new();
        internal readonly List<(Button Button, FontIcon Icon, TextBlock Text, SolidColorBrush Fill, SolidColorBrush Ink, SolidColorBrush Edge)> Cells = [];
    }
    private GuideLayer front = new(), back = new();
    private GuidePanel stage => front.Panel;
    private List<(Button Button, FontIcon Icon, TextBlock Text, SolidColorBrush Fill, SolidColorBrush Ink, SolidColorBrush Edge)> cells => front.Cells;
    private readonly Grid layers = new();
    private readonly Canvas outgoing = new() { IsHitTestVisible = false };
    private readonly List<FrameworkElement> typography = [];
    private IReadOnlyList<ControllerGuideHint> contextualHints = [];
    private IReadOnlyList<ControllerGuideHint> hostHints = ControllerGuideModel.TrayHints(false);
    private IReadOnlyList<ControllerGuideHint> current = [];
    private readonly ControllerGuidePublication stability = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer settleTimer;
    private object guideContext = new();
    private bool ready = true;
    private bool hidden, subscribed, disposed;
    private ShellChromePalette? palette;
    internal event Action? Changed;
    internal event Action<ControllerGuideHint>? Invoked;
    internal string HelpText { get; private set; } = string.Empty;
    internal bool ContextualReady => ready;
    internal bool HasPendingHints => !ready || stability.Remaining(Environment.TickCount64) > 0;
    internal FrameworkElement BackgroundSurface => stage;
    internal IReadOnlyList<FrameworkElement> Typography => typography;
    internal IReadOnlyList<ControllerGuideHint> DisplayedHints => stage.Selected.Where(i => i < current.Count).Select(i => current[i]).ToArray();

    internal ShellControllerGuide()
    {
        settleTimer = DispatcherQueue.CreateTimer();
        settleTimer.IsRepeating = false;
        settleTimer.Tick += (_, _) =>
        {
            if (disposed) return;
            if (stability.Commit(Environment.TickCount64)) Paint();
            ScheduleSettle();
        };
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = layers;
        layers.Children.Add(outgoing); layers.Children.Add(stage);
        InitializeLayer(front); InitializeLayer(back);
        SizeChanged += (_, _) => StopFade();
        Loaded += (_, _) => { if (!subscribed && !disposed) { subscribed = true; WidgetControllerPrompts.Changed += PromptsChanged; } stability.Reset(); Update(); };
        Unloaded += (_, _) =>
        {
            if (IsLoaded) return;
            StopFade(); settleTimer.Stop(); stability.Reset();
            if (subscribed) { WidgetControllerPrompts.Changed -= PromptsChanged; subscribed = false; }
        };
        Update();
    }

    private void InitializeLayer(GuideLayer layer)
    {
        // Maximum is the finite normalized button set plus host Back and Close.
        for (var i = 0; i < 16; ++i)
        {
            var icon = new FontIcon { MinWidth = 24, MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
            WidgetGlyphs.Apply(icon, new() { Id = "shell.guide", Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = ControllerPrompt.A }, WidgetControllerPrompts.PlayStation);
            var text = new TextBlock { TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(icon); content.Children.Add(text);
            var button = new Button { Content = content, IsTabStop = false, AllowFocusOnInteraction = false,
                Padding = new Thickness(10, 4, 10, 4), BorderThickness = new Thickness(1), CornerRadius = new(8),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var fill = new SolidColorBrush(); var ink = new SolidColorBrush(); var edge = new SolidColorBrush();
            button.Background = fill; button.Foreground = ink; button.BorderBrush = edge;
            foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
            {
                button.Resources["ButtonBackground" + state] = fill;
                button.Resources["ButtonForeground" + state] = ink;
                button.Resources["ButtonBorderBrush" + state] = edge;
            }
            var index = i;
            button.Click += (_, _) =>
            {
                // Retained labels never dispatch an action that disappeared or
                // changed meaning while the replacement guide is settling.
                if (ReferenceEquals(layer, front) && !hidden && index < current.Count && stability.Latest.Contains(current[index])) Invoked?.Invoke(current[index]);
            };
            AutomationProperties.SetAutomationId(button, "Overlay.Guide." + i);
            AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
            AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw);
            layer.Cells.Add((button, icon, text, fill, ink, edge)); typography.Add(icon); typography.Add(text); layer.Panel.Children.Add(button);
        }
        layer.Panel.Fitted += () => { if (ReferenceEquals(layer, front)) UpdateHelp(); };
    }

    private void PromptsChanged()
    {
        StopFade(); Paint();
        for (var i = 0; i < back.Cells.Count; ++i)
            WidgetGlyphs.Apply(back.Cells[i].Icon, new() { Id = "shell.guide", Kind = ViewNodeKind.ControllerGlyph,
                ControllerPrompt = i < back.Panel.Hints.Count ? back.Panel.Hints[i].Prompt : ControllerPrompt.A }, WidgetControllerPrompts.PlayStation);
    }

    internal void Present(object context, bool contextualReady, IReadOnlyList<ControllerGuideHint> contextual,
        IReadOnlyList<ControllerGuideHint> host, bool hide = false)
    {
        guideContext = context; ready = contextualReady; contextualHints = contextual; hostHints = host;
        var visibilityChanged = hidden != hide;
        hidden = hide;
        Update();
        if (visibilityChanged) Paint();
    }
    internal void ApplyAppearance(IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles, AppearanceSettings appearance, bool animationsEnabled = true)
    {
        var nextPalette = ShellChromePalette.Resolve(styles, appearance);
        if (motionAppearance != appearance || systemAnimations != animationsEnabled || palette != nextPalette) StopFade();
        motionAppearance = appearance; systemAnimations = animationsEnabled; palette = nextPalette;
        stage.Anchor = back.Panel.Anchor = appearance.OverlayPosition switch { OverlayPosition.BottomLeft => 0, OverlayPosition.BottomRight => 1, _ => .5 };
        ApplyPalette(); stage.InvalidateArrange();
    }

    private void ApplyPalette()
    {
        if (palette is not { } colors) return;
        for (var i = 0; i < cells.Count; i++)
        {
            var primary = i < current.Count && current[i].Prompt == ControllerPrompt.A;
            var cell = cells[i];
            cell.Fill.Color = primary ? colors.Selected : colors.Item;
            cell.Ink.Color = primary ? colors.SelectedText : colors.Text;
            var edge = cell.Ink.Color; edge.A = colors.HighContrast ? byte.MaxValue : (byte)64;
            cell.Edge.Color = edge;
            cell.Text.Foreground = cell.Ink; cell.Icon.Foreground = cell.Ink;
        }
    }

    private void Update()
    {
        if (disposed) return;
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(Update); return; }
        if (stability.Update(guideContext, ready, contextualHints, hostHints, Environment.TickCount64)) Paint();
        ScheduleSettle();
    }

    private void ScheduleSettle()
    {
        settleTimer.Stop();
        var remaining = stability.Remaining(Environment.TickCount64);
        if (disposed || !IsLoaded || remaining == 0) return;
        settleTimer.Interval = TimeSpan.FromMilliseconds(remaining);
        settleTimer.Start();
    }

    private void Paint()
    {
        if (disposed) return;
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(Paint); return; }
        var next = stability.Displayed;
        var animate = !current.SequenceEqual(next) && current.Count > 0 && next.Count > 0 &&
            !hidden && IsLoaded && stage.ActualWidth > 0 && stage.ActualHeight > 0 && FadeRecipe.Incoming.Duration > TimeSpan.Zero;
        if (animate) PrepareFade();
        else if (hidden || !current.SequenceEqual(next)) StopFade();
        current = next;
        for (var i = 0; i < cells.Count; ++i)
        {
            var cell = cells[i];
            var hint = i < current.Count ? current[i] : new ControllerGuideHint(ControllerPrompt.A, string.Empty);
            cell.Text.Text = hint.Label;
            WidgetGlyphs.Apply(cell.Icon, new() { Id = "shell.guide", Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = hint.Prompt }, WidgetControllerPrompts.PlayStation);
            AutomationProperties.SetName(cell.Button, WidgetGlyphs.AccessibleName(hint.Prompt, WidgetControllerPrompts.PlayStation) + ": " + hint.Label);
        }
        stage.Hints = current;
        ApplyPalette();
        stage.InvalidateMeasure();
        Opacity = hidden ? 0 : 1;
        IsHitTestVisible = Opacity > 0;
        UpdateHelp();
        if (animate) StartFade();
    }

    private void UpdateHelp()
    {
        var displayed = stage.Selected.Count == 0 ? Enumerable.Range(0, current.Count) : stage.Selected;
        HelpText = string.Join(". ", displayed.Where(index => index < current.Count).Select(index =>
            WidgetGlyphs.AccessibleName(current[index].Prompt, WidgetControllerPrompts.PlayStation) + ": " + current[index].Label)) + ".";
        AutomationProperties.SetName(this, HelpText);
        AutomationProperties.SetAccessibilityView(this, Opacity == 0 ? AccessibilityView.Raw : AccessibilityView.Content);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        StopFade(); settleTimer.Stop(); stability.Reset();
        if (subscribed) WidgetControllerPrompts.Changed -= PromptsChanged;
        subscribed = false;
    }

    private sealed class GuidePanel : Panel
    {
        internal IReadOnlyList<ControllerGuideHint> Hints { get; set; } = [];
        internal IReadOnlyList<int> Selected { get; private set; } = [];
        internal event Action? Fitted;
        internal double Anchor { get; set; } = .5;
        internal bool Interactive { get; set; } = true;
        private const double Gap = 10;
        protected override Size MeasureOverride(Size availableSize)
        {
            // Every cell remains measured so navigation never changes guide height.
            foreach (var child in Children) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Selected = ControllerGuideModel.Fit(Hints, Children.Select(child => child.DesiredSize.Width).ToArray(), availableSize.Width, Gap);
            var width = Selected.Sum(i => Children[i].DesiredSize.Width) + Math.Max(0, Selected.Count - 1) * Gap;
            return new Size(double.IsFinite(availableSize.Width) ? Math.Min(width, availableSize.Width) : width,
                Math.Max(40, Children.Max(child => child.DesiredSize.Height)));
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            Selected = ControllerGuideModel.Fit(Hints, Children.Select(child => child.DesiredSize.Width).ToArray(), finalSize.Width, Gap);
            var width = Selected.Sum(i => Children[i].DesiredSize.Width) + Math.Max(0, Selected.Count - 1) * Gap;
            var x = Math.Max(0, (finalSize.Width - width) * Anchor);
            for (var i = 0; i < Children.Count; ++i)
            {
                var shown = Selected.Contains(i);
                var child = Children[i];
                var actionable = Interactive && shown && (Hints[i].Button is not null || Hints[i].Prompt == ControllerPrompt.Guide);
                child.Opacity = shown ? 1 : 0; child.IsHitTestVisible = actionable;
                AutomationProperties.SetAccessibilityView(child, actionable ? AccessibilityView.Content : AccessibilityView.Raw);
                child.Arrange(new Rect(shown ? x : 0, 0, child.DesiredSize.Width, finalSize.Height));
                if (shown) x += child.DesiredSize.Width + Gap;
            }
            Fitted?.Invoke();
            return finalSize;
        }
    }
}
