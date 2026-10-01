using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>Compositor-only resize of a ready widget and its shell fill on one timeline.</summary>
internal sealed class WidgetSurfaceResizeMotion : IDisposable
{
    private readonly FrameworkElement element;
    private readonly List<(Visual Visual, Vector3 Scale, Vector3 Center)> targets = [];
    private CompositionScopedBatch? batch;
    private TaskCompletionSource<WidgetMotionOutcome>? pending;
    private EventHandler<object>? layoutReady;
    private Vector2 from, destination;
    private long started;
    private TimeSpan duration;
    private bool disposed;
    internal Task<WidgetMotionOutcome>? Playback { get; private set; }
    internal int Starts { get; private set; }

    internal WidgetSurfaceResizeMotion(FrameworkElement element)
    {
        this.element = element;
        element.Unloaded += Unloaded;
        element.SizeChanged += Resized;
    }

    // Sample only when publishing a replacement. No managed per-frame work.
    internal Vector2 PresentedSize(Vector2 fallback)
    {
        if (pending is null) return fallback;
        if (layoutReady is not null) return from;
        var t = (float)Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        return Vector2.Lerp(from, destination, t * t * (3 - 2 * t));
    }

    internal void Play(AppearanceSettings appearance, bool systemAnimationsEnabled, Vector2 previousSize,
        FrameworkElement background, WidgetResizeReason reason = WidgetResizeReason.WidgetSwitch)
    {
        if (disposed) return;
        Cancel();
        destination = new((float)element.Width, (float)element.Height);
        var recipe = WidgetMotionPolicy.WidgetResize(appearance, systemAnimationsEnabled, previousSize, destination,
            new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast, reason);
        if (recipe.Duration == TimeSpan.Zero) { Playback = Task.FromResult(WidgetMotionOutcome.Completed); return; }
        from = previousSize;
        duration = recipe.Duration;
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Playback = pending.Task;
        // Seed both transforms before publication can render. Delay the timeline
        // until native arrange has caught up with the newly committed dimensions.
        var anchor = appearance.OverlayPosition switch
        { OverlayPosition.BottomLeft => 0f, OverlayPosition.BottomRight => 1f, _ => .5f };
        foreach (var layer in new[] { element, background })
        {
            var visual = ElementCompositionPreview.GetElementVisual(layer);
            targets.Add((visual, visual.Scale, visual.CenterPoint));
            visual.CenterPoint = new(destination.X * anchor, destination.Y, 0);
            visual.Scale *= recipe.From.Scale;
        }
        layoutReady = (_, _) =>
        {
            if (!element.IsLoaded || Math.Abs(element.ActualWidth - destination.X) > 1 ||
                Math.Abs(element.ActualHeight - destination.Y) > 1) return;
            ClearLayoutWait();
            var compositor = targets[0].Visual.Compositor;
            batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            batch.Completed += Completed;
            using var easing = compositor.CreateCubicBezierEasingFunction(new(1f / 3, 0), new(2f / 3, 1));
            started = Stopwatch.GetTimestamp();
            foreach (var target in targets)
            {
                using var animation = compositor.CreateVector3KeyFrameAnimation();
                animation.Duration = duration;
                animation.InsertKeyFrame(0, target.Scale * recipe.From.Scale);
                animation.InsertKeyFrame(1, target.Scale, easing);
                target.Visual.StartAnimation(nameof(Visual.Scale), animation);
            }
            ++Starts;
            batch.End();
        };
        element.LayoutUpdated += layoutReady;
        element.InvalidateArrange();
    }

    private void Completed(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (ReferenceEquals(sender, batch)) Finish(WidgetMotionOutcome.Completed);
    }
    private void ClearLayoutWait()
    {
        if (layoutReady is null) return;
        element.LayoutUpdated -= layoutReady;
        layoutReady = null;
    }
    internal void Cancel() => Finish(WidgetMotionOutcome.Canceled);
    private void Finish(WidgetMotionOutcome outcome)
    {
        ClearLayoutWait();
        if (batch is not null) { batch.Completed -= Completed; batch.Dispose(); batch = null; }
        foreach (var target in targets)
        {
            target.Visual.StopAnimation(nameof(Visual.Scale));
            target.Visual.Scale = target.Scale;
            target.Visual.CenterPoint = target.Center;
        }
        targets.Clear();
        pending?.TrySetResult(outcome); pending = null;
    }
    private void Unloaded(object sender, RoutedEventArgs args) { if (!element.IsLoaded) Cancel(); }
    private void Resized(object sender, SizeChangedEventArgs args) { if (layoutReady is null) Cancel(); }
    public void Dispose()
    {
        if (disposed) return;
        Cancel();
        element.Unloaded -= Unloaded;
        element.SizeChanged -= Resized;
        disposed = true;
    }
}
