using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>A resident XAML media plane shared by native and embedded video.
/// Fullscreen changes its transform, not the renderer's attachment or viewport.</summary>
internal sealed partial class MediaPresentationStage : Grid, IDisposable
{
    internal Grid SurfaceHost { get; } = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly CompositeTransform placement = new();
    private readonly Border fadeCover = new() { IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Visibility = Visibility.Collapsed };
    private float pageOpacity = 1;
    private Grid? slot;
    private double aspectRatio;
    private bool fullscreen, disposed, observing, covered, pageMotion;
    internal bool HasSlot => slot is not null;
    internal bool CanPresent(Grid target) => XamlRoot is null || ReferenceEquals(XamlRoot, target.XamlRoot);
    internal Rect VideoBounds => SurfaceHost.TransformToVisual(this).TransformBounds(new(0, 0, SurfaceHost.ActualWidth, SurfaceHost.ActualHeight));

    internal MediaPresentationStage(double aspectRatio)
    {
        this.aspectRatio = aspectRatio;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        RenderTransform = placement;
        Children.Add(SurfaceHost); Children.Add(fadeCover);
        SizeChanged += (_, _) => Fit();
        LayoutUpdated += OnLayout;
    }

    internal void Configure(double aspect)
    {
        if (aspect == aspectRatio) return;
        aspectRatio = aspect;
        Fit();
    }

    internal void RefreshPlacement(bool ancestryChanged = false)
    {
        if (ancestryChanged && !fullscreen && !pageMotion) FollowOpacity();
        ApplyPlacement();
    }

    internal void ApplyPagePose(FrameworkElement layer, FrameworkElement viewport, Motion.WidgetMotionPose pose)
    {
        if (disposed || fullscreen || slot is not { IsLoaded: true } target || Parent is not FrameworkElement root) return;
        if (!pageMotion)
        {
            pageMotion = true; Observe(false);
            var visual = ElementCompositionPreview.GetElementVisual(this);
            visual.StopAnimation("Opacity"); visual.Opacity = 1;
        }
        // Native swap-chain opacity can expose the desktop through the XAML
        // beneath it. Fade through the page color instead; retire the opaque
        // video once fully covered, before the incoming page starts its fade.
        pageOpacity = pose.Opacity;
        fadeCover.Width = SurfaceHost.Width; fadeCover.Height = SurfaceHost.Height;
        fadeCover.Opacity = 1 - pageOpacity;
        fadeCover.Visibility = pageOpacity < 1 ? Visibility.Visible : Visibility.Collapsed;
        var toRoot = viewport.TransformToVisual(root);
        var local = target.TransformToVisual(layer).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
        Rect Map(Rect rect) => toRoot.TransformBounds(new(
            (rect.X - layer.ActualWidth / 2) * pose.Scale.X + layer.ActualWidth / 2 + pose.Translation.X,
            (rect.Y - layer.ActualHeight / 2) * pose.Scale.Y + layer.ActualHeight / 2 + pose.Translation.Y,
            rect.Width * pose.Scale.X, rect.Height * pose.Scale.Y));
        var bounds = Map(local);
        var visible = bounds;
        visible.Intersect(toRoot.TransformBounds(new(pose.Insets.X, pose.Insets.Y,
            Math.Max(0, viewport.ActualWidth - pose.Insets.X - pose.Insets.Z),
            Math.Max(0, viewport.ActualHeight - pose.Insets.Y - pose.Insets.W))));
        for (DependencyObject? item = target; item is UIElement element && !ReferenceEquals(item, layer); item = VisualTreeHelper.GetParent(item))
        {
            if (element.Visibility != Visibility.Visible) { Visibility = Visibility.Collapsed; return; }
            Rect? clip = element.Clip is RectangleGeometry rectangle ? rectangle.Rect :
                element is ScrollViewer or ScrollContentPresenter && element is FrameworkElement frame ? new(0, 0, frame.ActualWidth, frame.ActualHeight) : null;
            if (clip is { } value) visible.Intersect(Map(element.TransformToVisual(layer).TransformBounds(value)));
        }
        for (DependencyObject? item = viewport; item is UIElement element; item = VisualTreeHelper.GetParent(item))
        {
            if (element.Visibility != Visibility.Visible) { Visibility = Visibility.Collapsed; return; }
            if (ReferenceEquals(item, viewport)) continue;
            Rect? clip = element.Clip is RectangleGeometry rectangle ? rectangle.Rect :
                element is ScrollViewer or ScrollContentPresenter && element is FrameworkElement frame ? new(0, 0, frame.ActualWidth, frame.ActualHeight) : null;
            if (clip is { } value) visible.Intersect(element.TransformToVisual(root).TransformBounds(value));
        }
        visible.Intersect(new(0, 0, root.ActualWidth, root.ActualHeight));
        Place(bounds, visible);
    }

    internal void EndPageMotion()
    {
        if (!pageMotion) return;
        pageMotion = false; fadeCover.Visibility = Visibility.Collapsed; FollowOpacity();
        Observe(!fullscreen && slot is not null); ApplyPlacement();
    }

    internal void SetSlot(Grid? value)
    {
        var changed = !ReferenceEquals(slot, value) || fullscreen;
        slot = value;
        fullscreen = false;
        Observe(value is not null);
        if (changed && !pageMotion) FollowOpacity();
        ApplyPlacement();
    }

    internal void EnterFullscreen()
    {
        if (fullscreen) return;
        EndPageMotion();
        fullscreen = true;
        Observe(false);
        var visual = ElementCompositionPreview.GetElementVisual(this);
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
        placement.ScaleX = placement.ScaleY = 1;
        placement.TranslateX = placement.TranslateY = 0;
        Clip = null;
        Visibility = Visibility.Visible;
    }

    internal void SetCovered(bool value)
    {
        covered = value;
        if (value) Visibility = Visibility.Collapsed;
        else if (fullscreen) Visibility = Visibility.Visible;
        else ApplyPlacement();
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
        if (disposed || fullscreen || pageMotion) return;
        if (covered) { Visibility = Visibility.Collapsed; return; }
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
        Place(bounds, visible);
    }

    private void Place(Rect bounds, Rect visible)
    {
        if (covered || pageMotion && pageOpacity <= 0 || visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0) { Visibility = Visibility.Collapsed; return; }
        if (ActualWidth <= 0 || ActualHeight <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            // Measure the plane without allowing its decoder to paint at the
            // default full-window transform while layout is still pending.
            SurfaceHost.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Visible;
            return;
        }
        var media = MediaTransitionGeometry.Fit(new(0, 0, ActualWidth, ActualHeight), aspectRatio);
        var pose = MediaTransitionGeometry.Match(media, bounds);
        placement.ScaleX = placement.ScaleY = pose.Scale.X;
        placement.TranslateX = pose.Translation.X; placement.TranslateY = pose.Translation.Y;
        var localClip = new Rect((visible.X - placement.TranslateX) / placement.ScaleX,
            (visible.Y - placement.TranslateY) / placement.ScaleY, visible.Width / placement.ScaleX, visible.Height / placement.ScaleY);
        if (Clip is not RectangleGeometry old || Math.Abs(old.Rect.X - localClip.X) > .01 || Math.Abs(old.Rect.Y - localClip.Y) > .01 ||
            Math.Abs(old.Rect.Width - localClip.Width) > .01 || Math.Abs(old.Rect.Height - localClip.Height) > .01)
        {
            if (Clip is RectangleGeometry geometry) geometry.Rect = localClip;
            else Clip = new RectangleGeometry { Rect = localClip };
        }
        // Never expose an unpositioned full-window media surface, even on its
        // first frame while the source and native layout are arriving separately.
        Visibility = Visibility.Visible;
        SurfaceHost.Visibility = Visibility.Visible;
    }

    public void Dispose()
    {
        Visibility = Visibility.Collapsed;
        disposed = true; slot = null;
        Observe(false);
        ElementCompositionPreview.GetElementVisual(this).StopAnimation("Opacity");
        LayoutUpdated -= OnLayout;
        if (Parent is Panel parent) parent.Children.Remove(this);
    }

    internal void SetPageBackground(Brush? brush)
    {
        if (brush is SolidColorBrush solid)
        {
            var color = solid.Color; color.A = 255;
            fadeCover.Background = new SolidColorBrush(color);
        }
    }
}
