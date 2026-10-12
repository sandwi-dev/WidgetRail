using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>One XAML presentation clock for pages and their independently resident media.
/// Placement and clipping are applied before the media owners reconcile the same frame.</summary>
internal sealed class WidgetPageMotion : IDisposable
{
    private sealed record Target(WidgetMotionHost Host, WidgetMotionRecipe Recipe,
        object Transform, double Opacity, RectangleGeometry? Clip, CompositeTransform Motion, RectangleGeometry MotionClip);
    private readonly List<Target> targets = [];
    private readonly Stopwatch clock = new();
    private readonly Action<WidgetMotionHost, WidgetMotionPose> synchronize;
    private readonly TaskCompletionSource<WidgetMotionOutcome> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;
    private static int remainingTraces = 256;
    private int frames;
    private double lastFrame, maxGap, maxWork;

    internal WidgetPageMotion(IEnumerable<(WidgetMotionHost Host, WidgetMotionRecipe Recipe)> items, Action<WidgetMotionHost, WidgetMotionPose> synchronize)
    {
        this.synchronize = synchronize;
        foreach (var (host, recipe) in items)
        {
            var transform = new CompositeTransform { CenterX = host.ActualWidth / 2, CenterY = host.ActualHeight / 2 };
            var clip = new RectangleGeometry();
            targets.Add(new(host, recipe, host.Layer.ReadLocalValue(UIElement.RenderTransformProperty), host.Layer.Opacity, host.Viewport.Clip, transform, clip));
            host.Viewport.Clip = clip;
            host.Layer.RenderTransform = transform;
        }
    }
    internal Task<WidgetMotionOutcome> PlayAsync()
    {
        Apply(0);
        CompositionTarget.Rendering += Render;
        return completion.Task;
    }
    private void Render(object? sender, object args)
    {
        if (disposed) return;
        // LayoutReady may run before expensive remaining layout/publication work.
        // Start at the first presentation tick, not partway through that work.
        if (!clock.IsRunning) clock.Restart();
        var elapsed = clock.Elapsed.TotalMilliseconds;
        maxGap = Math.Max(maxGap, elapsed - lastFrame); lastFrame = elapsed; ++frames;
        var started = Stopwatch.GetTimestamp();
        Apply(elapsed);
        maxWork = Math.Max(maxWork, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (targets.All(target => elapsed >= (target.Recipe.Duration + target.Recipe.Delay).TotalMilliseconds))
        {
            CompositionTarget.Rendering -= Render;
            completion.TrySetResult(WidgetMotionOutcome.Completed);
        }
    }
    private void Apply(double elapsed)
    {
        foreach (var target in targets)
        {
            var recipe = target.Recipe;
            var t = recipe.Duration == TimeSpan.Zero ? 1 : Math.Clamp((elapsed - recipe.Delay.TotalMilliseconds) / recipe.Duration.TotalMilliseconds, 0, 1);
            var progress = (float)(t * t * (3 - 2 * t));
            var translation = Vector3.Lerp(recipe.From.Translation, recipe.To.Translation, progress);
            var scale = Vector3.Lerp(recipe.From.Scale, recipe.To.Scale, progress);
            var inset = Vector4.Lerp(recipe.From.Insets, recipe.To.Insets, progress);
            target.Motion.TranslateX = translation.X; target.Motion.TranslateY = translation.Y;
            target.Motion.ScaleX = scale.X; target.Motion.ScaleY = scale.Y;
            target.Host.Layer.Opacity = target.Opacity * (recipe.From.Opacity + (recipe.To.Opacity - recipe.From.Opacity) * progress);
            var clip = new Windows.Foundation.Rect(inset.X, inset.Y,
                Math.Max(0, target.Host.ActualWidth - inset.X - inset.Z), Math.Max(0, target.Host.ActualHeight - inset.Y - inset.W));
            if (target.MotionClip.Rect != clip) target.MotionClip.Rect = clip;
            synchronize(target.Host, new(translation, scale, recipe.From.Opacity + (recipe.To.Opacity - recipe.From.Opacity) * progress, inset));
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (Environment.GetEnvironmentVariable("WIDGETRAIL_TRACE_PAGE_MOTION") == "1" && Interlocked.Decrement(ref remainingTraces) >= 0)
        {
            var message = $"frames={frames} elapsedMs={clock.Elapsed.TotalMilliseconds:F2} maxGapMs={maxGap:F2} maxWorkMs={maxWork:F2}";
            _ = Task.Run(() => Diagnostics.FrontendFailureLog.Current.Write("page-motion", null, message));
        }
        CompositionTarget.Rendering -= Render;
        foreach (var target in targets)
        {
            if (ReferenceEquals(target.Transform, DependencyProperty.UnsetValue)) target.Host.Layer.ClearValue(UIElement.RenderTransformProperty);
            else target.Host.Layer.SetValue(UIElement.RenderTransformProperty, target.Transform);
            target.Host.Layer.Opacity = target.Opacity;
            target.Host.Viewport.Clip = target.Clip;
        }
        completion.TrySetResult(WidgetMotionOutcome.Canceled);
    }
}
