using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>The composed action body owns the transient highlight inside its focus border.</summary>
internal partial class WidgetActionPanel : Grid
{
    private SpriteVisual? highlight;
    private CompositionScopedBatch? batch;
    private bool wasFocused;
    private bool visible;
    private bool played;
    private NativeMotionVisibility? visibility;
    private DispatcherQueueTimer? rest;
#if ENABLE_WIDGET_VALIDATION
    internal bool HasRepeatTimer => rest is not null;
    internal Action CaptureRepeatTick() => rest is { } timer
        ? () => RestElapsed(timer, EventArgs.Empty) : throw new InvalidOperationException("No pending reflection");
#endif
    private readonly record struct Lighting(TimeSpan Duration, double Radius, bool Reflection, TimeSpan Delay, float Opacity, TimeSpan RepeatDelay);
    private Lighting lighting;
    private CompositionRoundedRectangleGeometry? geometry;

    internal void UpdateSheen(bool focused, TimeSpan duration, double radius,
        bool reflection = false, TimeSpan delay = default, float opacity = 1, TimeSpan repeatDelay = default)
    {
        if (!focused || duration == TimeSpan.Zero || opacity <= 0 || !IsLoaded)
        {
            EndLighting();
            return;
        }
        var next = new Lighting(duration, radius, reflection, delay, opacity, repeatDelay);
        if (wasFocused && next == lighting)
        {
            if (!visible) visibility?.Refresh();
            return;
        }
        EndLighting();
        wasFocused = true;
        lighting = next;
        Unloaded += LeftTree;
        if (repeatDelay == TimeSpan.Zero) Play(delay);
        else
        {
            visibility = new NativeMotionVisibility(this, OwnsVisibleFocus, SetVisible);
            visibility.Refresh();
        }
    }

    private bool OwnsVisibleFocus()
    {
        if (!wasFocused || XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not Control focused) return false;
        // Static actions own their Button. Virtualized actions own the enclosing SelectorItem.
        // Inspect only this focused face on a visibility event or the one-shot callback.
        var owns = false;
        for (DependencyObject? parent = this; parent is not null; parent = PresentationLayers.LogicalParent(parent))
        {
            if (ReferenceEquals(parent, focused) && parent is ButtonBase or SelectorItem or WidgetReadingSurface) owns = true;
            if (parent is WidgetViewPresenter presenter && !presenter.FocusEffectsActive) return false;
            if (parent is WidgetIndexedRowView row && row.Row is not Collections.WidgetIndexedRow { Lease.IsCurrent: true }) return false;
        }
        return owns;
    }

    private void SetVisible(bool value)
    {
        if (visible == value) return;
        visible = value;
        if (!value) { CancelRest(); Stop(); }
        else if (played) ScheduleRest();
        else Play(lighting.Delay);
    }

    private void Play(TimeSpan delay)
    {
        if (highlight is not null || !IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;
        played = true;
        var (duration, radius, reflection, _, opacity, _) = lighting;
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var brush = compositor.CreateLinearGradientBrush();
        var width = (float)ActualWidth;
        var height = (float)ActualHeight;
        var size = new Vector2(width, height);
        var tint = reflection ? Color.FromArgb(255, 222, 232, 255) : Color.FromArgb(255, 255, 251, 244);
        foreach (var (position, alpha) in reflection ? ReflectionStops : SweepStops)
            brush.ColorStops.Add(compositor.CreateColorGradientStop(position, Color.FromArgb(alpha, tint.R, tint.G, tint.B)));
        var axis = Vector2.One;
        var span = .48f;
        if (reflection)
        {
            // Project the normalized card onto a diagonal. Weight the two axes independently
            // of aspect ratio so the pool of light also fills wide news and activity cards.
            brush.MappingMode = CompositionMappingMode.Absolute;
            var normal = new Vector2(.64f / width, .36f / height);
            axis = normal / normal.LengthSquared();
            span = 2.7f;
        }
        highlight = compositor.CreateSpriteVisual();
        highlight.Opacity = opacity;
        highlight.Size = size;
        highlight.Brush = brush;
        geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = size;
        geometry.CornerRadius = new((float)Math.Clamp(radius, 0, Math.Min(width, height) / 2));
        highlight.Clip = compositor.CreateGeometricClip(geometry);
        ElementCompositionPreview.SetElementChildVisual(this, highlight);
        using var sweep = compositor.CreateVector2KeyFrameAnimation();
        using CompositionEasingFunction easing = reflection ? compositor.CreateLinearEasingFunction() :
            compositor.CreateCubicBezierEasingFunction(new(.28f, .12f), new(.35f, .92f));
        sweep.Duration = duration;
        sweep.DelayTime = delay;
        batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += Completed;
        brush.StartPoint = -axis * span;
        brush.EndPoint = Vector2.Zero;
        sweep.InsertKeyFrame(0, brush.StartPoint);
        sweep.InsertKeyFrame(1, axis, easing);
        brush.StartAnimation(nameof(brush.StartPoint), sweep);
        sweep.InsertKeyFrame(0, brush.EndPoint);
        sweep.InsertKeyFrame(1, axis * (1 + span), easing);
        brush.StartAnimation(nameof(brush.EndPoint), sweep);
        batch.End();
    }

    private static readonly (float Position, byte Alpha)[] SweepStops =
        [(0, 0), (.19f, 17), (.37f, 81), (.49f, 226), (.54f, 255), (.64f, 133), (.82f, 29), (1, 0)];
    private static readonly (float Position, byte Alpha)[] ReflectionStops =
        [(0, 0), (.10f, 7), (.20f, 63), (.30f, 162), (.40f, 235), (.50f, 255),
         (.60f, 245), (.70f, 181), (.80f, 79), (.90f, 10), (1, 0)];

    private void LeftTree(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (!IsLoaded) EndLighting();
    }

    private void Completed(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (!ReferenceEquals(sender, batch)) return;
        Stop();
        if (lighting.RepeatDelay > TimeSpan.Zero && visible && OwnsVisibleFocus()) ScheduleRest();
        else if (visibility is null) Unloaded -= LeftTree;
    }

    private void ScheduleRest()
    {
        if (!wasFocused || !visible || rest is not null || highlight is not null || lighting.RepeatDelay <= TimeSpan.Zero) return;
        rest = DispatcherQueue.CreateTimer();
        rest.IsRepeating = false;
        rest.Interval = lighting.RepeatDelay;
        rest.Tick += RestElapsed;
        rest.Start();
    }

    private void RestElapsed(DispatcherQueueTimer sender, object args)
    {
        if (!ReferenceEquals(sender, rest)) return;
        CancelRest();
        visibility?.Refresh();
        if (visible && OwnsVisibleFocus()) Play(TimeSpan.Zero);
    }

    private void CancelRest()
    {
        if (rest is not { } timer) return;
        rest = null;
        timer.Tick -= RestElapsed;
        timer.Stop();
    }

    internal void EndLighting()
    {
        if (!wasFocused) return;
        wasFocused = false;
        Unloaded -= LeftTree;
        CancelRest();
        visibility?.Dispose();
        visibility = null;
        visible = played = false;
        Stop();
    }

    private void Stop()
    {
        if (highlight is not { } old) return;
        var finishedBatch = batch;
        var finishedGeometry = geometry;
        highlight = null;
        batch = null;
        geometry = null;
        if (finishedBatch is not null) { finishedBatch.Completed -= Completed; finishedBatch.Dispose(); }
        if (ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(this), old))
            ElementCompositionPreview.SetElementChildVisual(this, null);
        old.Clip?.Dispose();
        finishedGeometry?.Dispose();
        old.Brush?.Dispose();
        old.Dispose();
    }
}
