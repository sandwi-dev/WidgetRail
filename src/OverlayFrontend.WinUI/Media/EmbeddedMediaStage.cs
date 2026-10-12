using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>A resident XAML media plane. Fullscreen changes its transform, not the WebView's native attachment.</summary>
internal sealed partial class EmbeddedMediaStage : Grid, IDisposable
{
    internal Grid SurfaceHost { get; } = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly CompositeTransform placement = new();
    private Grid? slot;
    private double aspectRatio;
    private bool fullscreen, disposed, observing;
    internal bool HasSlot => slot is not null;
    internal bool CanPresent(Grid target) => XamlRoot is null || ReferenceEquals(XamlRoot, target.XamlRoot);
    internal Rect VideoBounds => SurfaceHost.TransformToVisual(this).TransformBounds(new(0, 0, SurfaceHost.ActualWidth, SurfaceHost.ActualHeight));

    internal EmbeddedMediaStage(double aspectRatio)
    {
        this.aspectRatio = aspectRatio;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        RenderTransform = placement;
        Children.Add(SurfaceHost);
        SizeChanged += (_, _) => Fit();
        LayoutUpdated += OnLayout;
    }

    internal void Configure(double aspect)
    {
        if (aspect == aspectRatio) return;
        aspectRatio = aspect;
        Fit();
    }

    internal void SetSlot(Grid? value)
    {
        var changed = !ReferenceEquals(slot, value) || fullscreen;
        slot = value;
        fullscreen = false;
        Observe(value is not null);
        if (changed) FollowOpacity();
        ApplyPlacement();
    }

    internal void EnterFullscreen()
    {
        if (fullscreen) return;
        fullscreen = true;
        Observe(false);
        var visual = ElementCompositionPreview.GetElementVisual(this);
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
        placement.ScaleX = placement.ScaleY = 1;
        placement.TranslateX = placement.TranslateY = 0;
        Clip = null;
        Visibility = Visibility.Visible;
    }

    private void Fit()
    {
        var rect = MediaTransitionGeometry.Fit(new(0, 0, ActualWidth, ActualHeight), aspectRatio);
        SurfaceHost.Width = rect.Width; SurfaceHost.Height = rect.Height;
        ApplyPlacement();
    }

    private void OnLayout(object? sender, object args) { if (!fullscreen) ApplyPlacement(); }
    private void Observe(bool active)
    {
        if (active == observing) return;
        observing = active;
        // Independent scrolling and widget motion do not necessarily run layout.
        if (active) CompositionTarget.Rendering += OnLayout;
        else CompositionTarget.Rendering -= OnLayout;
    }

    private void FollowOpacity()
    {
        var visual = ElementCompositionPreview.GetElementVisual(this);
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
        if (slot is null) return;
        using var animation = visual.Compositor.CreateExpressionAnimation();
        var terms = new List<string>();
        var sharedRoot = Parent is { } parent ? VisualTreeHelper.GetParent(parent) : null;
        for (DependencyObject? item = slot; item is UIElement element && !ReferenceEquals(item, sharedRoot); item = VisualTreeHelper.GetParent(item))
        {
            var name = "ancestor" + terms.Count;
            animation.SetReferenceParameter(name, ElementCompositionPreview.GetElementVisual(element));
            terms.Add(name + ".Opacity");
        }
        animation.Expression = terms.Count > 0 ? string.Join(" * ", terms) : "1.0";
        visual.StartAnimation("Opacity", animation);
    }

    private void ApplyPlacement()
    {
        if (disposed || fullscreen) return;
        if (slot is not { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } target ||
            Parent is not FrameworkElement root || target.XamlRoot != XamlRoot)
        { Visibility = Visibility.Collapsed; return; }
        var bounds = target.TransformToVisual(root).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
        var visible = bounds;
        for (DependencyObject? item = target; item is UIElement element; item = VisualTreeHelper.GetParent(item))
        {
            if (element.Visibility != Visibility.Visible) { Visibility = Visibility.Collapsed; return; }
            Rect? clip = element.Clip is RectangleGeometry rectangle ? rectangle.Rect :
                element is ScrollViewer or ScrollContentPresenter && element is FrameworkElement frame ? new(0, 0, frame.ActualWidth, frame.ActualHeight) : null;
            if (clip is { } value) visible.Intersect(element.TransformToVisual(root).TransformBounds(value));
        }
        if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0) { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var media = MediaTransitionGeometry.Fit(new(0, 0, ActualWidth, ActualHeight), aspectRatio);
        var pose = MediaTransitionGeometry.Match(media, bounds);
        placement.ScaleX = placement.ScaleY = pose.Scale.X;
        placement.TranslateX = pose.Translation.X; placement.TranslateY = pose.Translation.Y;
        var localClip = new Rect((visible.X - placement.TranslateX) / placement.ScaleX,
            (visible.Y - placement.TranslateY) / placement.ScaleY, visible.Width / placement.ScaleX, visible.Height / placement.ScaleY);
        if (Clip is not RectangleGeometry old || Math.Abs(old.Rect.X - localClip.X) > .01 || Math.Abs(old.Rect.Y - localClip.Y) > .01 ||
            Math.Abs(old.Rect.Width - localClip.Width) > .01 || Math.Abs(old.Rect.Height - localClip.Height) > .01)
            Clip = new RectangleGeometry { Rect = localClip };
    }

    public void Dispose()
    {
        disposed = true; slot = null;
        Observe(false);
        ElementCompositionPreview.GetElementVisual(this).StopAnimation("Opacity");
        LayoutUpdated -= OnLayout;
        if (Parent is Panel parent) parent.Children.Remove(this);
    }
}
