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
        object Transform, double Opacity, RectangleGeometry? Clip, CompositeTransform Motion);
    private readonly List<Target> targets = [];
    private readonly Stopwatch clock = new();
    private readonly Action<WidgetMotionHost, WidgetMotionPose> synchronize;
    private readonly TaskCompletionSource<WidgetMotionOutcome> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;

    internal WidgetPageMotion(IEnumerable<(WidgetMotionHost Host, WidgetMotionRecipe Recipe)> items, Action<WidgetMotionHost, WidgetMotionPose> synchronize)
    {
        this.synchronize = synchronize;
        foreach (var (host, recipe) in items)
        {
            var transform = new CompositeTransform { CenterX = host.ActualWidth / 2, CenterY = host.ActualHeight / 2 };
            targets.Add(new(host, recipe, host.Layer.ReadLocalValue(UIElement.RenderTransformProperty), host.Layer.Opacity, host.Viewport.Clip, transform));
            host.Layer.RenderTransform = transform;
        }
    }
    internal Task<WidgetMotionOutcome> PlayAsync()
    {
        Apply(0);
        clock.Restart();
        CompositionTarget.Rendering += Render;
        return completion.Task;
    }
    private void Render(object? sender, object args)
    {
        if (disposed) return;
        var elapsed = clock.Elapsed.TotalMilliseconds;
        Apply(elapsed);
        if (targets.All(target => elapsed >= target.Recipe.Duration.TotalMilliseconds))
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
            var t = recipe.Duration == TimeSpan.Zero ? 1 : Math.Clamp(elapsed / recipe.Duration.TotalMilliseconds, 0, 1);
            var progress = (float)(t * t * (3 - 2 * t));
            var translation = Vector3.Lerp(recipe.From.Translation, recipe.To.Translation, progress);
            var scale = Vector3.Lerp(recipe.From.Scale, recipe.To.Scale, progress);
            var inset = Vector4.Lerp(recipe.From.Insets, recipe.To.Insets, progress);
            target.Motion.TranslateX = translation.X; target.Motion.TranslateY = translation.Y;
            target.Motion.ScaleX = scale.X; target.Motion.ScaleY = scale.Y;
            target.Host.Layer.Opacity = target.Opacity * (recipe.From.Opacity + (recipe.To.Opacity - recipe.From.Opacity) * progress);
            target.Host.Viewport.Clip = new RectangleGeometry { Rect = new(inset.X, inset.Y,
                Math.Max(0, target.Host.ActualWidth - inset.X - inset.Z), Math.Max(0, target.Host.ActualHeight - inset.Y - inset.W)) };
            synchronize(target.Host, new(translation, scale, recipe.From.Opacity + (recipe.To.Opacity - recipe.From.Opacity) * progress, inset));
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
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
