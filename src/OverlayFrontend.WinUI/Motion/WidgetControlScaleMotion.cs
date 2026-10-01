using System.Globalization;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

/// <summary>
/// Owns only a native control's presentation scale. UIElement.StartAnimation keeps
/// XAML/composition interop in the platform; layout, focus and actions are unchanged.
/// </summary>
internal sealed class WidgetControlScaleMotion : IDisposable
{
    private readonly FrameworkElement element;
    private readonly Vector3 originalScale;
    private readonly Vector3 originalCenter;
    private readonly Vector3Transition? originalTransition;
    private readonly Compositor compositor;
    private Vector3KeyFrameAnimation? animation;
    private readonly Dictionary<string, Vector3KeyFrameAnimation> animations = [];
    private CompositionScopedBatch? batch;
    private Windows.Foundation.TypedEventHandler<object, CompositionBatchCompletedEventArgs>? completed;
    private WidgetMotionOptions? policy;
    private float target = 1;
    private bool applied;
    private bool disposed;
    internal int Starts { get; private set; }
    internal bool IsAnimating => batch is not null;

    internal WidgetControlScaleMotion(FrameworkElement element)
    {
        this.element = element;
        originalScale = element.Scale; originalCenter = element.CenterPoint;
        originalTransition = element.ScaleTransition;
        element.ScaleTransition = null;
        compositor = CompositionTarget.GetCompositorForCurrentThread();
        element.SizeChanged += Resized;
        element.Loaded += Loaded; element.Unloaded += Unloaded;
        Center();
    }

    internal void Apply(float scale, double milliseconds, string? easing, WidgetMotionOptions options)
    {
        if (disposed) return;
        scale = float.IsFinite(scale) ? Math.Clamp(scale, .5f, 2f) : 1;
        var changedPolicy = policy != options;
        var initial = !applied;
        policy = options;
        applied = true;
        if (!initial && !changedPolicy && target == scale) return;
        target = scale;
        var duration = options.Duration(double.IsFinite(milliseconds) ? Math.Clamp(milliseconds, 0, 2000) : 0);
        if (initial || changedPolicy || !element.IsLoaded || duration == TimeSpan.Zero)
        { Settle(); return; }

        // Do not stop the current channel before replacing it. StartingValue is
        // sampled by the compositor from the displayed scale during interruption.
        RetireBatch(stop: false);
        var key = easing is "spring" or "linear" or "ease-in-out" ? easing : "ease-out";
        if (!animations.TryGetValue(key, out var next))
            animations.Add(key, next = compositor.CreateVector3KeyFrameAnimation());
        animation = next;
        next.Target = "Scale";
        next.Duration = duration;
        next.InsertExpressionKeyFrame(0, "this.StartingValue");
        var destination = originalScale * new Vector3(target, target, 1);
        CompositionEasingFunction curve;
        if (easing == "spring")
        {
            // Preserve WRSS's bounded normalized critical response; it is not an
            // unbounded physical spring. All samples are submitted once to GPU.
            next.SetVector3Parameter("Destination", destination);
            curve = compositor.CreateLinearEasingFunction();
            for (var index = 1; index < 24; ++index)
            {
                var progress = index / 24f;
                var response = (1 - (1 + 6 * progress) * MathF.Exp(-6 * progress)) / (1 - 7 * MathF.Exp(-6));
                next.InsertExpressionKeyFrame(progress,
                    "this.StartingValue + (Destination - this.StartingValue) * " + response.ToString("R", CultureInfo.InvariantCulture), curve);
            }
            next.InsertKeyFrame(1, destination, curve);
        }
        else
        {
            curve = easing switch
            {
                "linear" => compositor.CreateLinearEasingFunction(),
                "ease-in-out" => compositor.CreateCubicBezierEasingFunction(new(1f / 3, 0), new(2f / 3, 1)),
                _ => compositor.CreateCubicBezierEasingFunction(new(1f / 3, 1), new(2f / 3, 1)),
            };
            next.InsertKeyFrame(1, destination, curve);
        }
        var run = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch = run;
        completed = (_, _) => { if (ReferenceEquals(batch, run)) Settle(); };
        run.Completed += completed;
        element.StartAnimation(next);
        ++Starts;
        run.End();
        curve.Dispose();
    }

    private void Center() => element.CenterPoint = new((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, originalCenter.Z);
    private void Resized(object sender, SizeChangedEventArgs args) { Center(); Settle(); }
    private void Loaded(object sender, RoutedEventArgs args) { Center(); Settle(); }
    private void Unloaded(object sender, RoutedEventArgs args)
    {
        if (element.IsLoaded) return;
        RetireBatch(stop: true);
        element.Scale = originalScale;
    }
    private void Settle()
    { RetireBatch(stop: true); element.Scale = originalScale * new Vector3(target, target, 1); }
    private void RetireBatch(bool stop)
    {
        if (batch is { } prior) { batch = null; prior.Completed -= completed; completed = null; prior.Dispose(); }
        if (animation is { } priorAnimation)
        {
            animation = null;
            if (stop) element.StopAnimation(priorAnimation);
            // WinUI's animation facade retains the animation object and reads
            // its Target when replacing an active run. Keep four bounded curve
            // instances alive until this control's owner is retired.
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        RetireBatch(stop: true);
        element.SizeChanged -= Resized; element.Loaded -= Loaded; element.Unloaded -= Unloaded;
        element.Scale = originalScale; element.CenterPoint = originalCenter;
        element.ScaleTransition = originalTransition;
        foreach (var value in animations.Values) value.Dispose();
        animations.Clear();
        disposed = true;
    }
}
