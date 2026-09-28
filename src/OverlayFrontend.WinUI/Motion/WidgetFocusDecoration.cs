using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>
/// Host-only focus outline in a dedicated child visual. Its motion transforms
/// no XAML content and inherits the control's independent authored scale.
/// </summary>
internal sealed class WidgetFocusDecoration : IDisposable
{
    private readonly Control control;
    private readonly WidgetVisualAdornment adornment;
    private readonly bool nativeFocusVisuals;
    private readonly ContainerVisual viewport;
    private readonly ShapeVisual visual;
    private readonly CompositionRoundedRectangleGeometry geometry;
    private readonly CompositionSpriteShape outline;
    private readonly CompositionColorBrush brush;
    private WidgetCompositionTarget? target;
    private WidgetCompositionMotion? motion;
    private WidgetMotionOptions options;
    private Vector2 size;
    private float width;
    private float radius;
    private float offset;
    private bool focused;
    private bool applied;
    private bool disposed;
    internal bool IsVisible => visual.Opacity > 0;
    internal Task<WidgetMotionOutcome>? Playback { get; private set; }
    internal Visual DecorationVisual => visual;

    internal static WidgetFocusDecoration? Create(Control control)
    {
        // Share only our adornment owner, never a media/third-party child slot.
        // Native system focus remains the fallback for foreign visual owners.
        var adornment = WidgetVisualAdornment.Acquire(control, WidgetVisualAdornment.Layer.Focus);
        return adornment is null ? null : new(control, adornment);
    }
    private WidgetFocusDecoration(Control control, WidgetVisualAdornment adornment)
    {
        this.control = control; this.adornment = adornment;
        nativeFocusVisuals = control.UseSystemFocusVisuals;
        // Do not obtain the control's backing visual: WinUI facade Scale and
        // ScaleTransition prohibit that interop path. Only our child visuals
        // are owned here, using the current XAML compositor.
        var compositor = Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread();
        viewport = compositor.CreateContainerVisual();
        visual = compositor.CreateShapeVisual();
        geometry = compositor.CreateRoundedRectangleGeometry();
        outline = compositor.CreateSpriteShape(geometry);
        brush = compositor.CreateColorBrush();
        outline.StrokeBrush = brush;
        visual.Shapes.Add(outline);
        viewport.Children.InsertAtTop(visual);
        adornment.Visual.Children.InsertAtTop(viewport);
        control.UseSystemFocusVisuals = false;
        control.SizeChanged += Resized; control.Loaded += Loaded; control.Unloaded += Unloaded;
    }
    internal void Apply(Color color, float thickness, float cornerRadius, float outlineOffset,
        bool isFocused, WidgetMotionOptions policy)
    {
        if (disposed) return;
        brush.Color = color;
        width = Math.Clamp(thickness, 0, 16);
        radius = Math.Max(0, cornerRadius);
        offset = outlineOffset;
        var initial = !applied;
        var policyChanged = options != policy;
        var focusChanged = focused != isFocused;
        options = policy; focused = isFocused; applied = true;
        var rebuilt = UpdateGeometry();
        if (target is null || motion is null) return;
        if (initial || policyChanged || rebuilt)
        {
            motion.Cancel(); target.Set(WidgetMotionPose.Identity with { Opacity = focused ? 1 : 0 });
        }
        else if (focusChanged)
            Playback = motion.PlayAsync((WidgetMotionPlayback[])[new(target, WidgetMotionPolicy.Focus(options, size, focused))]);
    }
    private bool UpdateGeometry()
    {
        var next = new Vector2((float)control.ActualWidth, (float)control.ActualHeight);
        if (!control.IsLoaded || next.X <= 0 || next.Y <= 0) return false;
        var rebuilt = target is null || size != next;
        if (rebuilt)
        {
            RetireTargets(); size = next;
            visual.Size = viewport.Size = size;
            target = new(visual, viewport, size);
            motion = new(visual.Compositor, control.DispatcherQueue);
        }
        var inset = Math.Clamp(Math.Max(width / 2, -offset), 0, Math.Min(size.X, size.Y) / 2);
        geometry.Offset = new(inset);
        geometry.Size = Vector2.Max(Vector2.Zero, size - new Vector2(2 * inset));
        geometry.CornerRadius = new(Math.Min(radius, Math.Min(geometry.Size.X, geometry.Size.Y) / 2));
        outline.StrokeThickness = width;
        return rebuilt;
    }
    private void Loaded(object sender, RoutedEventArgs args)
    { if (UpdateGeometry()) target?.Set(WidgetMotionPose.Identity with { Opacity = focused ? 1 : 0 }); }
    private void Resized(object sender, SizeChangedEventArgs args) => Loaded(sender, args);
    private void Unloaded(object sender, RoutedEventArgs args) { if (!control.IsLoaded) RetireTargets(); }
    private void RetireTargets()
    { motion?.Dispose(); motion = null; target?.Dispose(); target = null; }
    public void Dispose()
    {
        if (disposed) return;
        RetireTargets();
        control.SizeChanged -= Resized; control.Loaded -= Loaded; control.Unloaded -= Unloaded;
        adornment.Dispose();
        control.UseSystemFocusVisuals = nativeFocusVisuals;
        viewport.Children.RemoveAll(); visual.Shapes.Clear();
        outline.Dispose(); geometry.Dispose(); brush.Dispose(); visual.Dispose(); viewport.Dispose(); disposed = true;
    }
}
