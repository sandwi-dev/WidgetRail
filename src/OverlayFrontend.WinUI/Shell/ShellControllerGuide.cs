using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Bounded native cells, measured whole labels, and one stable-height guide slot.</summary>
internal sealed class ShellControllerGuide : ContentControl, IDisposable
{
    private readonly GuidePanel stage = new();
    private readonly List<(Button Button, FontIcon Icon, TextBlock Text)> cells = [];
    private readonly List<FrameworkElement> typography = [];
    private IReadOnlyList<ControllerGuideHint>? widgetHints;
    private IReadOnlyList<ControllerGuideHint> current = [];
    private bool reordering, hidden, subscribed, disposed;
    internal event Action? Changed;
    internal event Action<ControllerGuideHint>? Invoked;
    internal string HelpText { get; private set; } = string.Empty;
    internal FrameworkElement BackgroundSurface => stage;
    internal IReadOnlyList<FrameworkElement> Typography => typography;
    internal IReadOnlyList<ControllerGuideHint> DisplayedHints => stage.Selected.Where(i => i < current.Count).Select(i => current[i]).ToArray();

    internal ShellControllerGuide()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = stage;
        // Maximum is the finite normalized button set plus host Back and Close.
        for (var i = 0; i < 16; ++i)
        {
            var icon = new FontIcon { MinWidth = 24, MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(icon); content.Children.Add(text);
            var button = new Button { Content = content, IsTabStop = false, AllowFocusOnInteraction = false,
                Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = null,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var index = i;
            button.Click += (_, _) => { if (index < current.Count) Invoked?.Invoke(current[index]); };
            AutomationProperties.SetAutomationId(button, "Overlay.Guide." + i);
            AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
            AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw);
            cells.Add((button, icon, text)); typography.Add(icon); typography.Add(text); stage.Children.Add(button);
        }
        stage.Fitted += UpdateHelp;
        Loaded += (_, _) => { if (!subscribed && !disposed) { subscribed = true; WidgetControllerPrompts.Changed += Update; } Update(); };
        Unloaded += (_, _) => { if (!IsLoaded && subscribed) { WidgetControllerPrompts.Changed -= Update; subscribed = false; } };
        Update();
    }

    internal void SetWidgetHints(IReadOnlyList<ControllerGuideHint>? hints)
    {
        if (ReferenceEquals(widgetHints, hints) || widgetHints is not null && hints is not null && widgetHints.SequenceEqual(hints)) return;
        widgetHints = hints; Update();
    }
    internal void SetState(bool reorder, bool inactive)
    { if (reordering == reorder && hidden == inactive) return; reordering = reorder; hidden = inactive; Update(); }

    private void Update()
    {
        if (disposed) return;
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(Update); return; }
        current = widgetHints is { } hints ? ControllerGuideModel.WithHost(hints) : reordering
            ? [new(ControllerPrompt.DPadHorizontal, "Move widget", Required: true), new(ControllerPrompt.A, "Done", ControllerButton.A),
                new(ControllerPrompt.B, "Done", ControllerButton.B, Required: true), new(ControllerPrompt.Y, "Done", ControllerButton.Y)]
            : [new(ControllerPrompt.A, "Open widget", ControllerButton.A, Required: true),
                new(ControllerPrompt.Y, "Reorder · hold to restart", ControllerButton.Y),
                new(ControllerPrompt.Menu, "Commands", ControllerButton.Menu), new(ControllerPrompt.B, "Close", ControllerButton.B, Required: true)];
        for (var i = 0; i < cells.Count; ++i)
        {
            var cell = cells[i];
            var hint = i < current.Count ? current[i] : new ControllerGuideHint(ControllerPrompt.A, string.Empty);
            cell.Text.Text = hint.Label;
            WidgetGlyphs.Apply(cell.Icon, new() { Id = "shell.guide", Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = hint.Prompt }, WidgetControllerPrompts.PlayStation);
            AutomationProperties.SetName(cell.Button, WidgetGlyphs.AccessibleName(hint.Prompt, WidgetControllerPrompts.PlayStation) + ": " + hint.Label);
        }
        stage.Hints = current;
        stage.InvalidateMeasure();
        Opacity = hidden && widgetHints is null ? 0 : 1;
        IsHitTestVisible = Opacity > 0;
        UpdateHelp();
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
        if (subscribed) WidgetControllerPrompts.Changed -= Update;
        subscribed = false;
    }

    private sealed class GuidePanel : Panel
    {
        internal IReadOnlyList<ControllerGuideHint> Hints { get; set; } = [];
        internal IReadOnlyList<int> Selected { get; private set; } = [];
        internal event Action? Fitted;
        private const double Gap = 16;
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
            var x = Math.Max(0, (finalSize.Width - width) / 2);
            for (var i = 0; i < Children.Count; ++i)
            {
                var shown = Selected.Contains(i);
                var child = Children[i];
                var actionable = shown && (Hints[i].Button is not null || Hints[i].Prompt == ControllerPrompt.Guide);
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
