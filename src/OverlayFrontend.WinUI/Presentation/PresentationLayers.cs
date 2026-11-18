using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

// Root-level ordering is a host contract. Local ZIndex values cannot escape a
// parent's stacking context; native resident surfaces and overlays use this
// common root instead of competing through unrelated local ordering values.
internal enum PresentationLayer
{
    Content = 0,
    ContentMedia = 100,
    ContentMediaChrome = 125,
    ContentPreview = 150,
    ContentPreviewMedia = 175,
    ContentPreviewMediaChrome = 180,
    WidgetDialog = 200,
    WidgetDialogMedia = 300,
    WidgetDialogMediaChrome = 325,
    WidgetDialogPreview = 350,
    WidgetDialogPreviewMedia = 375,
    WidgetDialogPreviewMediaChrome = 380,
    FullscreenBackdrop = 400,
    FullscreenMedia = 500,
    FullscreenChrome = 600,
    ShellOverlay = 700,
    Startup = 800,
}

internal sealed class PresentationLayers
{
    // Paint relocation does not change ownership. Semantic consumers (input,
    // theme, text scale, viewport policy) use this; geometry uses the visual tree.
    internal static DependencyObject? LogicalParent(DependencyObject element) => element switch
    {
        PresentationPortal portal => portal.Anchor,
        Media.MediaPresentationStage { PlacementAnchor: { } slot } => slot,
        _ => VisualTreeHelper.GetParent(element),
    };
    internal static Rect? LocalClip(UIElement element) => element.Clip is RectangleGeometry rectangle ? rectangle.Rect :
        element is ScrollViewer or ScrollContentPresenter or WidgetViewPresenter or WidgetModalLayer && element is FrameworkElement frame
            ? new(0, 0, frame.ActualWidth, frame.ActualHeight) : null;
    private static readonly DependencyProperty HostProperty = DependencyProperty.RegisterAttached(
        "PresentationLayers", typeof(PresentationLayers), typeof(PresentationLayers), new PropertyMetadata(null));
    internal Grid Root { get; }
    private PresentationLayers(Grid root) => Root = root;
    internal static PresentationLayers Attach(Grid root)
    {
        if (root.GetValue(HostProperty) is PresentationLayers existing) return existing;
        var host = new PresentationLayers(root);
        root.SetValue(HostProperty, host);
        return host;
    }
    internal static PresentationLayers? Find(DependencyObject? origin)
    {
        for (var item = origin; item is not null; item = VisualTreeHelper.GetParent(item))
            if (item.GetValue(HostProperty) is PresentationLayers host) return host;
        return null;
    }
    internal static void Order(UIElement element, PresentationLayer layer) => Canvas.SetZIndex(element, (int)layer);
    internal void Add(UIElement element, PresentationLayer layer)
    {
        Order(element, layer);
        Root.Children.Add(element);
    }
    internal static PresentationLayer MediaLayer(DependencyObject? slot)
    {
        for (var item = slot; item is not null; item = VisualTreeHelper.GetParent(item))
        {
            if (item is PresentationPortal { Layer: PresentationLayer.ContentPreview }) return PresentationLayer.ContentPreviewMedia;
            if (item is PresentationPortal { Layer: PresentationLayer.WidgetDialogPreview }) return PresentationLayer.WidgetDialogPreviewMedia;
            if (item is WidgetModalLayer) return PresentationLayer.WidgetDialogMedia;
        }
        return PresentationLayer.ContentMedia;
    }
    internal static PresentationLayer MediaChromeLayer(DependencyObject slot) => MediaLayer(slot) switch
    {
        PresentationLayer.ContentPreviewMedia => PresentationLayer.ContentPreviewMediaChrome,
        PresentationLayer.WidgetDialogMedia => PresentationLayer.WidgetDialogMediaChrome,
        PresentationLayer.WidgetDialogPreviewMedia => PresentationLayer.WidgetDialogPreviewMediaChrome,
        _ => PresentationLayer.ContentMediaChrome,
    };
}
