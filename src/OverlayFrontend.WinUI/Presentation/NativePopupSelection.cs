using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Paint-only selection rim and leading marker from the native shell menu.
/// MenuFlyout retains its standard template, checked state, accessibility and input.</summary>
internal sealed class NativePopupSelection : IDisposable
{
    private readonly Control control;
    private WidgetVisualAdornment? owner;
    private ShapeVisual? visual;
    private CompositionRoundedRectangleGeometry? rimGeometry, markerGeometry;
    private CompositionSpriteShape? rim, marker;
    private CompositionColorBrush? rimBrush, markerBrush;
    private bool active, contrast;
    private double zoom = 1, radius;
    private Color selection, accent;

    internal NativePopupSelection(Control control)
    {
        this.control = control;
        control.Loaded += Loaded; control.SizeChanged += Resized; control.Unloaded += Unloaded;
    }
    internal void Apply(bool active, bool contrast, double zoom, double radius, Color selection, Color accent)
    {
        this.active = active; this.contrast = contrast; this.zoom = zoom; this.radius = radius;
        this.selection = selection; this.accent = accent; Update();
    }
    private void Loaded(object sender, RoutedEventArgs args) => Update();
    private void Resized(object sender, SizeChangedEventArgs args) => Update();
    private void Unloaded(object sender, RoutedEventArgs args) { if (!control.IsLoaded) Retire(); }
    private void Update()
    {
        if (!control.IsLoaded || control.ActualWidth <= 0 || control.ActualHeight <= 0) return;
        owner ??= WidgetVisualAdornment.Acquire(control, WidgetVisualAdornment.Layer.Focus);
        if (owner is null) return;
        if (visual is null)
        {
            var compositor = owner.Visual.Compositor;
            visual = compositor.CreateShapeVisual();
            rimGeometry = compositor.CreateRoundedRectangleGeometry(); markerGeometry = compositor.CreateRoundedRectangleGeometry();
            rim = compositor.CreateSpriteShape(rimGeometry); marker = compositor.CreateSpriteShape(markerGeometry);
            rimBrush = compositor.CreateColorBrush(); markerBrush = compositor.CreateColorBrush();
            rim.StrokeBrush = rimBrush; marker.FillBrush = markerBrush;
            visual.Shapes.Add(rim); visual.Shapes.Add(marker); owner.Visual.Children.InsertAtTop(visual);
        }
        visual.Opacity = active ? 1 : 0;
        visual.Size = new((float)control.ActualWidth, (float)control.ActualHeight);
        var scale = (float)zoom;
        var inset = scale * .5f;
        rimGeometry!.Offset = new(inset); rimGeometry.Size = Vector2.Max(Vector2.Zero, visual.Size - new Vector2(inset * 2));
        rimGeometry.CornerRadius = new((float)radius * scale);
        rim!.StrokeThickness = contrast ? 0 : scale;
        byte Shade(byte value) => (byte)Math.Clamp(Math.Round(value + (255 - value) * .22), 0, 255);
        rimBrush!.Color = Color.FromArgb((byte)Math.Round(selection.A * .35), Shade(selection.R), Shade(selection.G), Shade(selection.B));
        var height = Math.Min(20 * scale, visual.Size.Y * .5f);
        markerGeometry!.Offset = new(4.5f * scale, (visual.Size.Y - height) * .5f);
        markerGeometry.Size = new(3 * scale, height); markerGeometry.CornerRadius = new(Math.Min((float)radius, 1.5f) * scale);
        markerBrush!.Color = accent;
    }
    private void Retire()
    {
        visual?.Shapes.Clear(); rim?.Dispose(); marker?.Dispose(); rimGeometry?.Dispose(); markerGeometry?.Dispose();
        rimBrush?.Dispose(); markerBrush?.Dispose(); owner?.Dispose(); visual?.Dispose();
        owner = null; visual = null; rim = marker = null; rimGeometry = markerGeometry = null; rimBrush = markerBrush = null;
    }
    public void Dispose()
    {
        control.Loaded -= Loaded; control.SizeChanged -= Resized; control.Unloaded -= Unloaded; Retire();
    }
}
