using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Root-level paint placement with widget-local geometry and ownership.
/// It neither owns navigation nor grants input. The source presenter still owns
/// the semantic subtree, and revokes input/visibility through this placement.</summary>
internal sealed partial class PresentationPortal : Grid, IDisposable
{
    private readonly FrameworkElement anchor;
    private readonly Grid root;
    private readonly CompositeTransform placement = new();
    private readonly RectangleGeometry clip = new();
    private readonly Grid roundedLayer = new();
    private readonly IDisposable? input;
    private readonly WidgetViewPresenter? owner;
    private readonly Microsoft.UI.Composition.CompositionRoundedRectangleGeometry rounded;
    private readonly Microsoft.UI.Composition.CompositionGeometricClip roundedClip;
    private bool disposed;
    private bool paintOnly;
    private readonly bool followComposition;
    internal FrameworkElement Content { get; }
    internal FrameworkElement Anchor => anchor;

    internal PresentationPortal(PresentationLayers layers, FrameworkElement anchor, FrameworkElement content,
        PresentationLayer layer = PresentationLayer.WidgetDialog, bool passive = false, bool followComposition = true, bool attachInput = true)
    {
        this.anchor = anchor; root = layers.Root; Content = content;
        this.followComposition = followComposition; paintOnly = passive;
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Top;
        RenderTransform = placement; Clip = clip;
        for (DependencyObject? item = anchor; item is not null; item = VisualTreeHelper.GetParent(item))
            if (item is WidgetViewPresenter presenter) { owner = presenter; if (!passive && attachInput) input = presenter.AttachPresentationInput(content); break; }
        // XAML owns the outer rectangular clip/transform. A separate visual
        // carries the rounded compositor clip; putting both on one visual makes
        // WinUI attempt to treat a geometric clip as an inset clip (E_NOINTERFACE).
        var visual = ElementCompositionPreview.GetElementVisual(roundedLayer);
        rounded = visual.Compositor.CreateRoundedRectangleGeometry();
        roundedClip = visual.Compositor.CreateGeometricClip(rounded); visual.Clip = roundedClip;
        roundedLayer.Children.Add(content); Children.Add(roundedLayer);
        GettingFocus += (_, args) => { if (paintOnly) args.TryCancel(); };
        layers.Add(this, layer);
        FollowOpacity();
        anchor.LayoutUpdated += Refresh;
        if (followComposition) CompositionTarget.Rendering += Refresh;
        Refresh(null, EventArgs.Empty);
    }

    private void FollowOpacity()
    {
        var visual = ElementCompositionPreview.GetElementVisual(this);
        using var expression = visual.Compositor.CreateExpressionAnimation();
        var terms = new List<string>();
        for (DependencyObject? item = anchor; item is UIElement element && !ReferenceEquals(item, root); item = VisualTreeHelper.GetParent(item))
        {
            var name = "ancestor" + terms.Count;
            expression.SetReferenceParameter(name, ElementCompositionPreview.GetElementVisual(element));
            terms.Add(name + ".Opacity");
        }
        expression.Expression = terms.Count == 0 ? "1.0" : string.Join(" * ", terms);
        visual.StartAnimation("Opacity", expression);
    }

    private void Refresh(object? sender, object args)
    {
        if (disposed) return;
        if (!anchor.IsLoaded || anchor.ActualWidth <= 0 || anchor.ActualHeight <= 0 || anchor.XamlRoot != root.XamlRoot)
        { Visibility = Visibility.Collapsed; return; }
        var bounds = anchor.TransformToVisual(root).TransformBounds(new(0, 0, anchor.ActualWidth, anchor.ActualHeight));
        var visible = bounds;
        var acceptsInput = true;
        for (DependencyObject? item = anchor; item is UIElement element; item = VisualTreeHelper.GetParent(item))
        {
            if (element.Visibility != Visibility.Visible) { Visibility = Visibility.Collapsed; return; }
            acceptsInput &= element.IsHitTestVisible && (element is not Control control || control.IsEnabled);
            var ancestorClip = PresentationLayers.LocalClip(element);
            if (ancestorClip is { } value) visible.Intersect(element.TransformToVisual(root).TransformBounds(value));
        }
        visible.Intersect(new(0, 0, root.ActualWidth, root.ActualHeight));
        if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        { Visibility = Visibility.Collapsed; return; }
        Width = anchor.ActualWidth; Height = anchor.ActualHeight; FlowDirection = anchor.FlowDirection;
        var ownerBounds = owner is null ? new Rect(0, 0, Width, Height) :
            owner.TransformToVisual(anchor).TransformBounds(new(0, 0, owner.ActualWidth, owner.ActualHeight));
        rounded.Offset = new((float)ownerBounds.X, (float)ownerBounds.Y);
        rounded.Size = new((float)ownerBounds.Width, (float)ownerBounds.Height);
        rounded.CornerRadius = new((float)Math.Min(owner?.SurfaceCornerRadius ?? 0, Math.Min(ownerBounds.Width, ownerBounds.Height) / 2));
        placement.ScaleX = bounds.Width / Width; placement.ScaleY = bounds.Height / Height;
        placement.TranslateX = bounds.X; placement.TranslateY = bounds.Y;
        var localClip = new Rect((visible.X - bounds.X) / placement.ScaleX, (visible.Y - bounds.Y) / placement.ScaleY,
            visible.Width / placement.ScaleX, visible.Height / placement.ScaleY);
        if (clip.Rect != localClip) clip.Rect = localClip;
        IsHitTestVisible = acceptsInput && !paintOnly;
        Visibility = Visibility.Visible;
    }
    internal void RetainForExit() { paintOnly = true; IsHitTestVisible = false; input?.Dispose(); }

    // The presenter's stage exposes the modal peer once, in its logical tree.
    // Root-level paint relocation must not duplicate accessibility descendants.
    protected override AutomationPeer OnCreateAutomationPeer() => new PaintPeer(this);
    private sealed partial class PaintPeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
        protected override List<AutomationPeer> GetChildrenCore() => [];
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Visibility = Visibility.Collapsed;
        anchor.LayoutUpdated -= Refresh; if (followComposition) CompositionTarget.Rendering -= Refresh;
        ElementCompositionPreview.GetElementVisual(this).StopAnimation("Opacity");
        ElementCompositionPreview.GetElementVisual(roundedLayer).Clip = null;
        roundedClip.Dispose(); rounded.Dispose();
        input?.Dispose(); roundedLayer.Children.Clear(); Children.Clear(); root.Children.Remove(this);
    }
}
