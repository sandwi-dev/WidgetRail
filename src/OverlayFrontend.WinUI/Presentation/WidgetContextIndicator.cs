using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Decorative, zero-measure context prompt inside the native control's
/// scaled visual tree. It never owns focus, hit testing or command dispatch.</summary>
internal sealed partial class WidgetContextIndicator : Panel, IDisposable
{
    private readonly Control owner;
    private readonly Border badge;
    private readonly FontIcon glyph = new() { FontSize = 20 };
    private Panel? parent;
    private Rect? viewport;
    private Rect bounds;
    private ControllerButton button;
    private bool requested, contentInset, observingLayout, disposed;
    internal bool IsShown => badge.Visibility == Visibility.Visible;
    internal FontIcon Glyph => glyph;
    internal Rect BadgeBounds => bounds;

    internal WidgetContextIndicator(Control owner)
    {
        this.owner = owner;
        IsHitTestVisible = false;
        badge = new() { Width = 26, Height = 26, Padding = new Thickness(3), CornerRadius = new CornerRadius(6),
            Child = glyph, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        foreach (var element in new FrameworkElement[] { this, badge, glyph })
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
        Children.Add(badge);
        EffectiveViewportChanged += ViewportChanged;
        Unloaded += UnloadedHint;
        WidgetControllerPrompts.Changed += UpdateGlyph;
    }

    internal static ControllerButton? Resolve(ViewNode node) => node.Kind == ViewNodeKind.ActionSurface &&
        node.IsDisabled != true && node.IsBusy != true && node.ContextActions.Any(action => !action.IsDisabled && !action.IsBusy)
            ? node.ContextMenuButton ?? ControllerButton.Menu : null;

    internal void Update(ControllerButton? prompt, Panel? contentHost = null)
    {
        if (disposed) return;
        requested = prompt is not null;
        ObserveLayout(requested && contentHost is not null);
        if (prompt is { } next) { button = next; UpdateGlyph(); }
        if (!requested) { ShowIfVisible(); return; }
        var host = contentHost;
        contentInset = contentHost is not null;
        if (host is null && owner is Button)
        {
            owner.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(owner) > 0 && VisualTreeHelper.GetChild(owner, 0) is Grid { Name: "WidgetDepthRoot" } root)
                host = root;
        }
        if (!ReferenceEquals(host, parent))
        {
            parent?.Children.Remove(this); parent = host; viewport = null;
            parent?.Children.Add(this);
        }
        badge.Background = BackgroundFor(owner);
        glyph.Foreground = owner.Foreground;
        InvalidateArrange(); ShowIfVisible();
    }
    private static Brush? BackgroundFor(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var brush = current switch { Control control => control.Background, Panel panel => panel.Background, Border border => border.Background, _ => null };
            if (brush is SolidColorBrush { Color.A: > 0 } || brush is GradientBrush gradient && gradient.GradientStops.Any(stop => stop.Color.A > 0)) return brush;
        }
        return Application.Current.Resources.TryGetValue("ButtonBackground", out var fallback) ? fallback as Brush : null;
    }
    private void UpdateGlyph()
    {
        if (disposed) return;
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(UpdateGlyph); return; }
        var prompt = button switch { ControllerButton.X => ControllerPrompt.X, ControllerButton.Y => ControllerPrompt.Y, _ => ControllerPrompt.Menu };
        WidgetGlyphs.Apply(glyph, new() { Id = "context-indicator", Kind = ViewNodeKind.ControllerGlyph,
            ControllerPrompt = prompt, AccessibilityLabel = "Options" }, WidgetControllerPrompts.PlayStation);
    }
    private void ViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    { if (!disposed) { viewport = args.EffectiveViewport; ShowIfVisible(); } }
    private void UnloadedHint(object sender, RoutedEventArgs args)
    { if (!IsLoaded) { viewport = null; badge.Visibility = Visibility.Collapsed; } }
    private void ShowIfVisible()
    {
        var visible = requested && !disposed && parent is not null && viewport is { } clip && bounds.Width > 0 &&
            clip.Left <= bounds.Left + .5 && clip.Top <= bounds.Top + .5 && clip.Right >= bounds.Right - .5 && clip.Bottom >= bounds.Bottom - .5;
        badge.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
    protected override Size MeasureOverride(Size availableSize)
    { badge.Measure(new Size(26, 26)); return new Size(); }
    protected override Size ArrangeOverride(Size finalSize)
    {
        bounds = IndicatorBounds(finalSize.Width);
        ShowIfVisible(); badge.Arrange(bounds); return finalSize;
    }
    private Rect IndicatorBounds(double width)
    {
        // Indexed content can be centered or padded by the native presenter.
        // Use the actual control origin, rather than guessing its content inset.
        var origin = contentInset && parent is not null && IsLoaded
            ? owner.TransformToVisual(this).TransformPoint(new(owner.ActualWidth - 31, 5))
            : new Point(width - 31, 5);
        return new(origin.X, origin.Y, 26, 26);
    }
    private void ObserveLayout(bool value)
    {
        if (observingLayout == value) return;
        observingLayout = value;
        if (value) LayoutUpdated += LayoutChanged;
        else LayoutUpdated -= LayoutChanged;
    }
    private void LayoutChanged(object? sender, object args)
    {
        if (!disposed && IsLoaded && IndicatorBounds(ActualWidth) != bounds) InvalidateArrange();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        badge.Visibility = Visibility.Collapsed;
        ObserveLayout(false);
        WidgetControllerPrompts.Changed -= UpdateGlyph;
        EffectiveViewportChanged -= ViewportChanged; Unloaded -= UnloadedHint;
        parent?.Children.Remove(this); parent = null; Children.Clear();
    }
}
