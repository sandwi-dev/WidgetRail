using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>A resident XAML plane for embedded browser media.
/// Fullscreen changes its transform, not the renderer's attachment or viewport.</summary>
internal sealed partial class MediaPresentationStage : Grid, IDisposable
{
    internal Grid SurfaceHost { get; } = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly CompositeTransform placement = new();
    private Grid? slot;
    private CompositionOpacityBinding? opacityBinding;
    private double aspectRatio;
    private bool fullscreen, disposed, observing, covered;
    internal bool HasSlot => slot is not null;
    internal Grid? PlacementAnchor => slot;
    internal Action? Released { get; set; }
    internal bool CanPresent(Grid target) => XamlRoot is null || ReferenceEquals(XamlRoot, target.XamlRoot);
    internal Rect VideoBounds => SurfaceHost.TransformToVisual(this).TransformBounds(new(0, 0, SurfaceHost.ActualWidth, SurfaceHost.ActualHeight));

    internal MediaPresentationStage(double aspectRatio)
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

    internal void RefreshPlacement(bool ancestryChanged = false)
    {
        if (ancestryChanged && !fullscreen) FollowOpacity();
        ApplyPlacement();
    }

    internal void SetSlot(Grid? value)
    {
        var changed = !ReferenceEquals(slot, value) || fullscreen;
        slot = value;
        fullscreen = false;
        IsHitTestVisible = false;
        PresentationLayers.Order(this, PresentationLayers.MediaLayer(value));
        Observe(value is not null);
        if (changed) FollowOpacity();
        ApplyPlacement();
    }

    internal void EnterFullscreen()
    {
        if (fullscreen) return;
        fullscreen = true;
        PresentationLayers.Order(this, PresentationLayer.FullscreenMedia);
        Observe(false);
        opacityBinding?.Dispose(); opacityBinding = null;
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
        if (fullscreen) Visibility = value ? Visibility.Collapsed : Visibility.Visible;
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
        opacityBinding?.Dispose(); opacityBinding = null;
        var visual = ElementCompositionPreview.GetElementVisual(this);
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
        if (slot is null) return;
        // Both paint and logical content already inherit this root's opacity.
        opacityBinding = new CompositionOpacityBinding(visual, slot, Parent);
    }

    private void ApplyPlacement()
    {
        if (disposed || fullscreen) return;
        if (slot is not { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } target ||
            Parent is not FrameworkElement root || target.XamlRoot != XamlRoot)
        { Visibility = Visibility.Collapsed; return; }
        // ContentControl can finish restoring a collapsed fullscreen card's
        // visual ancestry after SetSlot. Reconcile the same owner policy when
        // its real inline placement is ready, without another lifetime/timer.
        var layer = PresentationLayers.MediaLayer(target);
        // A gallery moves its viewport into fullscreen together with its controls.
        // Its resident browser follows that slot above the fullscreen backdrop.
        if (covered && layer != PresentationLayer.FullscreenMedia) { Visibility = Visibility.Collapsed; return; }
        if (Canvas.GetZIndex(this) != (int)layer) PresentationLayers.Order(this, layer);
        var bounds = target.TransformToVisual(root).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
        var visible = bounds;
        for (DependencyObject? item = target; item is UIElement element; item = VisualTreeHelper.GetParent(item))
        {
            if (element.Visibility != Visibility.Visible) { Visibility = Visibility.Collapsed; return; }
            var clip = PresentationLayers.LocalClip(element);
            if (clip is { } value) visible.Intersect(element.TransformToVisual(root).TransformBounds(value));
        }
        Place(bounds, visible);
    }

    private void Place(Rect bounds, Rect visible)
    {
        if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0) { Visibility = Visibility.Collapsed; return; }
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
        if (disposed) return;
        Visibility = Visibility.Collapsed;
        disposed = true; slot = null;
        Observe(false);
        opacityBinding?.Dispose(); opacityBinding = null;
        ElementCompositionPreview.GetElementVisual(this).StopAnimation("Opacity");
        LayoutUpdated -= OnLayout;
        if (Parent is Panel parent) parent.Children.Remove(this);
        Released?.Invoke(); Released = null;
    }

}
