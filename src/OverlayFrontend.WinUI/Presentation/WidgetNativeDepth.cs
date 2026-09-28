using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WidgetRail.OverlayFrontend.WinUI.Motion;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed record WidgetDepthStyle(Color Shadow, float Blur, Vector2 Offset, float Radius,
    Thickness Border, Color Top, Color Right, Color Bottom, Color Left, float Opacity);

/// <summary>Native paint-only resources; no geometry affects layout or input.</summary>
internal sealed class WidgetNativeDepth : IDisposable
{
    private readonly FrameworkElement element;
    private readonly (Grid Shadow, Grid Edges)? suppliedSlots;
    private (Grid Shadow, Grid Edges)? slots;
    private WidgetVisualAdornment? adornment;
    private WidgetOuterShadowMask? outerMask;
    private WidgetDepthStyle? style;
    private ContainerVisual? edges;
    private SpriteVisual? shadowVisual;
    private DropShadow? shadow;
    private ShapeVisual? maskVisual;
    private CompositionRoundedRectangleGeometry? maskGeometry;
    private CompositionSpriteShape? maskShape;
    private CompositionColorBrush? maskFill;
    private CompositionVisualSurface? maskSurface;
    private CompositionSurfaceBrush? mask;
    private CompositionGeometricClip? edgeClip;
    private readonly List<(SpriteVisual Visual, CompositionColorBrush Brush)> edgeVisuals = [];
    private bool disposed;
    internal DropShadow? NativeShadow => shadow;
    internal int NativeResourceCreates { get; private set; }
    internal int EdgeCount => edgeVisuals.Count;
    internal bool IsRealized => shadowVisual is not null;
    internal bool UsesOuterMask => outerMask is not null;
    internal float MaskPadding => outerMask?.Padding ?? 0;
    internal static bool HasTemplateSlots(FrameworkElement element) => FindSlots(element) is not null;

    internal WidgetNativeDepth(FrameworkElement element, (Grid Shadow, Grid Edges)? suppliedSlots)
    {
        this.element = element; this.suppliedSlots = suppliedSlots;
        element.Loaded += Loaded; element.Unloaded += Unloaded; element.SizeChanged += Resized;
    }
    internal void Apply(WidgetDepthStyle value)
    { style = value; Update(); }
    private void Loaded(object sender, RoutedEventArgs args) => Update();
    private void Resized(object sender, SizeChangedEventArgs args) => Update();
    private void Unloaded(object sender, RoutedEventArgs args) { if (!element.IsLoaded) Retire(); }
    private void Update()
    {
        if (disposed || style is not { } value || !element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        slots ??= suppliedSlots ?? FindSlots(element);
        if (slots is null && element is SelectorItem)
            adornment ??= WidgetVisualAdornment.Acquire(element, WidgetVisualAdornment.Layer.Depth);
        if (slots is null && adornment is null) return;
        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        if (shadowVisual is null)
        {
            // These are dedicated slots. Never steal another visual owner's slot.
            if (slots is { } host && (ElementCompositionPreview.GetElementChildVisual(host.Shadow) is not null ||
                ElementCompositionPreview.GetElementChildVisual(host.Edges) is not null)) return;
            shadowVisual = compositor.CreateSpriteVisual();
            shadow = compositor.CreateDropShadow(); shadowVisual.Shadow = shadow;
            maskGeometry = compositor.CreateRoundedRectangleGeometry();
            maskShape = compositor.CreateSpriteShape(maskGeometry);
            maskFill = compositor.CreateColorBrush(Microsoft.UI.Colors.White); maskShape.FillBrush = maskFill;
            maskVisual = compositor.CreateShapeVisual(); maskVisual.Shapes.Add(maskShape);
            maskSurface = compositor.CreateVisualSurface(); maskSurface.SourceVisual = maskVisual;
            mask = compositor.CreateSurfaceBrush(maskSurface); shadow.Mask = mask;
            edges = compositor.CreateContainerVisual(); edgeClip = compositor.CreateGeometricClip(maskGeometry); edges.Clip = edgeClip;
            for (var index = 0; index < 4; ++index)
            {
                var visual = compositor.CreateSpriteVisual(); var brush = compositor.CreateColorBrush();
                visual.Brush = brush; edges.Children.InsertAtTop(visual); edgeVisuals.Add((visual, brush));
            }
            if (slots is { } target)
            {
                ElementCompositionPreview.SetElementChildVisual(target.Shadow, shadowVisual);
                ElementCompositionPreview.SetElementChildVisual(target.Edges, edges);
            }
            else
            {
                outerMask = new(shadowVisual);
                adornment!.Visual.Children.InsertAtBottom(outerMask.Visual);
                adornment.Visual.Children.InsertAtTop(edges);
            }
            ++NativeResourceCreates;
        }
        var size = new Vector2((float)element.ActualWidth, (float)element.ActualHeight);
        maskVisual!.Size = maskSurface!.SourceSize = maskGeometry!.Size = shadowVisual.Size = edges!.Size = size;
        maskGeometry.CornerRadius = new(Math.Clamp(value.Radius, 0, Math.Min(size.X, size.Y) / 2));
        shadow!.Color = Color.FromArgb(255, value.Shadow.R, value.Shadow.G, value.Shadow.B);
        shadow.Opacity = value.Shadow.A / 255f;
        shadow.BlurRadius = value.Blur; shadow.Offset = new(value.Offset, 0);
        shadowVisual.Opacity = edges.Opacity = value.Opacity;
        outerMask?.Resize(size, value.Radius, value.Blur, value.Offset);
        var top = Math.Min((float)value.Border.Top, size.Y);
        var bottom = Math.Min((float)value.Border.Bottom, size.Y - top);
        var left = Math.Min((float)value.Border.Left, size.X);
        var right = Math.Min((float)value.Border.Right, size.X - left);
        Edge(0, Vector2.Zero, new(size.X, top), value.Top);
        Edge(1, new(size.X - right, top), new(right, Math.Max(0, size.Y - top - bottom)), value.Right);
        Edge(2, new(0, size.Y - bottom), new(size.X, bottom), value.Bottom);
        Edge(3, new(0, top), new(left, Math.Max(0, size.Y - top - bottom)), value.Left);
        void Edge(int index, Vector2 offset, Vector2 extent, Color color)
        { var edge = edgeVisuals[index]; edge.Visual.Offset = new(offset, 0); edge.Visual.Size = extent; edge.Brush.Color = color; }
    }

    private static (Grid Shadow, Grid Edges)? FindSlots(FrameworkElement element)
    {
        if (element is Control control) control.ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(element) == 0 || VisualTreeHelper.GetChild(element, 0) is not Grid root) return null;
        var shadow = root.Children.OfType<Grid>().FirstOrDefault(child => child.Name == "WidgetDepthShadow");
        var edges = root.Children.OfType<Grid>().FirstOrDefault(child => child.Name == "WidgetDepthEdges");
        return shadow is not null && edges is not null ? (shadow, edges) : null;
    }
    private void Retire()
    {
        adornment?.Dispose(); adornment = null;
        outerMask?.Dispose(); outerMask = null;
        if (slots is { } host)
        {
            if (ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(host.Shadow), shadowVisual)) ElementCompositionPreview.SetElementChildVisual(host.Shadow, null);
            if (ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(host.Edges), edges)) ElementCompositionPreview.SetElementChildVisual(host.Edges, null);
        }
        if (shadowVisual is not null) shadowVisual.Shadow = null;
        if (shadow is not null) shadow.Mask = null;
        if (maskSurface is not null) maskSurface.SourceVisual = null;
        edges?.Children.RemoveAll(); if (edges is not null) edges.Clip = null;
        foreach (var edge in edgeVisuals) { edge.Visual.Dispose(); edge.Brush.Dispose(); } edgeVisuals.Clear();
        edgeClip?.Dispose(); edgeClip = null; edges?.Dispose(); edges = null;
        maskVisual?.Shapes.Clear(); maskShape?.Dispose(); maskShape = null; maskFill?.Dispose(); maskFill = null;
        mask?.Dispose(); mask = null; maskSurface?.Dispose(); maskSurface = null;
        maskVisual?.Dispose(); maskVisual = null; maskGeometry?.Dispose(); maskGeometry = null;
        shadow?.Dispose(); shadow = null; shadowVisual?.Dispose(); shadowVisual = null;
        slots = null;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        element.Loaded -= Loaded; element.Unloaded -= Unloaded; element.SizeChanged -= Resized;
        Retire(); style = null;
    }
}
