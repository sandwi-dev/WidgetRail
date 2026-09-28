using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>One native icon visual; changing foreground edits only a compositor brush.</summary>
internal sealed class NativePackageIconTint : IDisposable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageIcon, NativePackageIconTint> Owners = new();
    internal static NativePackageIconTint? For(ImageIcon icon) => Owners.TryGetValue(icon, out var value) ? value : null;
    private readonly ImageIcon icon;
    private NativePackageIconTintCache.Lease lease;
    private readonly NativePackageIconTintCache cache;
    private readonly WidgetPresentationPackageIcon payload;
    private readonly XamlRoot root;
    private readonly CancellationTokenSource lifetime = new();
    private int rasterPixels = 128;
    private bool resizing;
    private double requestedExtent = 20;
    private readonly SpriteVisual visual;
    private readonly CompositionSurfaceBrush alpha;
    private readonly CompositionColorBrush color;
    private readonly CompositionMaskBrush mask;
    private readonly long foregroundToken;
    private SolidColorBrush? foreground;
    private long colorToken, opacityToken;
    private bool disposed;
    internal Color CurrentColor => color.Color;
    internal NativePackageIconTint(ImageIcon icon, NativePackageIconTintCache.Lease lease, NativePackageIconTintCache cache, WidgetPresentationPackageIcon payload, XamlRoot root)
    {
        this.icon = icon; this.lease = lease; this.cache = cache; this.payload = payload; this.root = root; Owners.Add(icon, this);
        root.Changed += RootChanged;
        var compositor = ElementCompositionPreview.GetElementVisual(icon).Compositor;
        alpha = compositor.CreateSurfaceBrush(lease.Surface); alpha.Stretch = CompositionStretch.Uniform;
        color = compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);
        mask = compositor.CreateMaskBrush(); mask.Mask = alpha; mask.Source = color;
        visual = compositor.CreateSpriteVisual(); visual.Brush = mask;
        ElementCompositionPreview.SetElementChildVisual(icon, visual);
        foregroundToken = icon.RegisterPropertyChangedCallback(IconElement.ForegroundProperty, (_, _) => UpdateForeground());
        icon.SizeChanged += SizeChanged; icon.Loaded += Loaded; icon.ActualThemeChanged += ThemeChanged;
        UpdateSize(); UpdateForeground();
    }
    private void Loaded(object sender, RoutedEventArgs args) { UpdateSize(); UpdateForeground(); }
    private void ThemeChanged(FrameworkElement sender, object args) => UpdateForeground();
    private void SizeChanged(object sender, SizeChangedEventArgs args) => UpdateSize();
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateSize();
    internal void RefreshSize(double extent) { requestedExtent = extent; UpdateSize(); }
    private void UpdateSize()
    {
        if (disposed) return;
        visual.Size = new Vector2((float)(icon.ActualWidth > 0 ? icon.ActualWidth : icon.Width),
            (float)(icon.ActualHeight > 0 ? icon.ActualHeight : icon.Height));
        if (!resizing && DesiredPixels() > rasterPixels) _ = ResizeAsync();
    }
    private int DesiredPixels()
    {
        var size = visual.Size;
        if (icon.IsLoaded && root.Content is UIElement target)
        {
            var bounds = icon.TransformToVisual(target).TransformBounds(new Windows.Foundation.Rect(0, 0, size.X, size.Y));
            size = new((float)bounds.Width, (float)bounds.Height);
        }
        var needed = Math.Max(requestedExtent, Math.Min(size.X, size.Y)) * root.RasterizationScale;
        var bucket = 128;
        while (bucket < needed && bucket < 512) bucket *= 2;
        return bucket;
    }
    private async Task ResizeAsync()
    {
        resizing = true;
        try
        {
            while (!disposed && DesiredPixels() > rasterPixels)
            {
                var nextPixels = DesiredPixels();
                var next = await cache.AcquireAsync(payload, nextPixels, lifetime.Token);
                if (disposed) { next.Dispose(); return; }
                var previous = lease; lease = next; rasterPixels = nextPixels;
                alpha.Surface = lease.Surface; previous.Dispose();
            }
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception) { System.Diagnostics.Trace.TraceWarning("Native icon retained its current raster after resize preparation failed."); }
        finally { resizing = false; }
    }
    private void UpdateForeground()
    {
        if (disposed) return;
        var next = icon.Foreground as SolidColorBrush;
        if (!ReferenceEquals(foreground, next))
        {
            DetachForeground(); foreground = next;
            if (next is not null)
            {
                colorToken = next.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => UpdateColor());
                opacityToken = next.RegisterPropertyChangedCallback(Brush.OpacityProperty, (_, _) => UpdateColor());
            }
        }
        UpdateColor();
    }
    private void UpdateColor()
    {
        if (disposed) return;
        if (foreground is not { } brush) { color.Color = Microsoft.UI.Colors.Transparent; return; }
        var value = brush.Color; value.A = (byte)Math.Clamp(Math.Round(value.A * brush.Opacity), 0, 255); color.Color = value;
    }
    private void DetachForeground()
    {
        if (foreground is null) return;
        foreground.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, colorToken);
        foreground.UnregisterPropertyChangedCallback(Brush.OpacityProperty, opacityToken);
        foreground = null;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; Owners.Remove(icon);
        icon.UnregisterPropertyChangedCallback(IconElement.ForegroundProperty, foregroundToken);
        lifetime.Cancel(); root.Changed -= RootChanged;
        icon.SizeChanged -= SizeChanged; icon.Loaded -= Loaded; icon.ActualThemeChanged -= ThemeChanged;
        DetachForeground(); ElementCompositionPreview.SetElementChildVisual(icon, null);
        visual.Dispose(); mask.Dispose(); color.Dispose(); alpha.Dispose(); lease.Dispose(); lifetime.Dispose();
    }
}
