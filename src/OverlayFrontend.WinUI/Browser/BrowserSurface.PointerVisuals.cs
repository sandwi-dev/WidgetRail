using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Input;
using Windows.UI.ViewManagement;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private sealed record PointerTrailSample(Border Element, Visual Visual)
    {
        internal Vector2 Position;
        internal long Started;
    }

    private readonly UISettings pointerUiSettings = new();
    private readonly AccessibilitySettings pointerAccessibility = new();
    private Visual? pointerVisual;
    private Canvas? pointerTrailLayer;
    private readonly List<PointerTrailSample> pointerTrail = [];
    private readonly RectangleGeometry pointerTrailClip = new();
    private CompositionScopedBatch? pointerGlideBatch;
    private Windows.Foundation.TypedEventHandler<object, CompositionBatchCompletedEventArgs>? pointerGlideCompleted;
    private Vector2 pointerVisualTarget;
    private Vector2 pointerTrailOrigin;
    private long pointerTrailSampleTime;
    private int pointerTrailIndex;
    private bool pointerVisualPositioned;
    private bool pointerMotionReduced;

    // The presenter may additionally suppress motion for its own reduced-motion
    // policy. The system preference is always honored, even without a presenter.
    internal void SetPointerMotionReduced(bool reduced)
    {
        pointerMotionReduced = reduced;
        if (reduced) FinishPointerVisual();
    }

    private bool PointerMotionEnabled => !pointerMotionReduced && pointerUiSettings.AnimationsEnabled;

    private void InitializePointerVisual()
    {
        if (pointerVisual is not null) return;
        cursor.Margin = new(0);
        ElementCompositionPreview.SetIsTranslationEnabled(cursor, true);
        pointerVisual = ElementCompositionPreview.GetElementVisual(cursor);
        pointerTrailLayer = new Canvas { IsHitTestVisible = false, Clip = pointerTrailClip };
        // Behind the crisp pointer, above the page. These noninteractive pooled
        // decorations never participate in controller focus or DOM hit testing.
        viewport.Children.Insert(viewport.Children.IndexOf(cursor), pointerTrailLayer);
        for (var index = 0; index < PointerMotionOptions.TrailCapacity; ++index)
        {
            var dot = new Border
            {
                Width = PointerMotionOptions.TrailDiameter, Height = PointerMotionOptions.TrailDiameter,
                CornerRadius = new(PointerMotionOptions.TrailDiameter / 2), IsHitTestVisible = false,
            };
            pointerTrailLayer.Children.Add(dot);
            ElementCompositionPreview.SetIsTranslationEnabled(dot, true);
            var visual = ElementCompositionPreview.GetElementVisual(dot);
            visual.Opacity = 0;
            pointerTrail.Add(new(dot, visual));
        }
    }

    private Vector2 PointerVisualDestination() => new(
        (float)Math.Max(0, viewport.ActualWidth * cursorX - ControllerPointerAppearance.Diameter / 2),
        (float)Math.Max(0, viewport.ActualHeight * cursorY - ControllerPointerAppearance.Diameter / 2));

    private void DrawPointerVisual(bool animateJump = false, bool freeMovement = false)
    {
        if (cursor.Visibility != Visibility.Visible || retired || viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0)
        { ResetPointerVisual(); return; }
        InitializePointerVisual();
        var target = PointerVisualDestination();
        var motion = PointerMotionEnabled;
        if (!pointerVisualPositioned || !motion)
        {
            pointerVisualTarget = target; pointerVisualPositioned = true;
            FinishPointerVisual();
            return;
        }

        if (animateJump)
        {
            ClearPointerTrail();
            if (target == pointerVisualTarget) return;
            pointerVisualTarget = target;
            // StartingValue is sampled on the compositor, including when a new
            // jump replaces an unfinished glide. There is no managed frame loop.
            RetirePointerGlide(stop: false);
            var compositor = pointerVisual!.Compositor;
            using var animation = compositor.CreateVector3KeyFrameAnimation();
            using var easing = compositor.CreateCubicBezierEasingFunction(new(.2f, .8f), new(.2f, 1));
            animation.Duration = PointerMotionOptions.JumpDuration;
            animation.InsertExpressionKeyFrame(0, "this.StartingValue");
            animation.InsertKeyFrame(1, new Vector3(target, 0), easing);
            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            pointerGlideBatch = batch;
            pointerGlideCompleted = (_, _) =>
            {
                if (!ReferenceEquals(pointerGlideBatch, batch)) return;
                RetirePointerGlide(stop: true);
                pointerVisual.Properties.InsertVector3("Translation", new(pointerVisualTarget, 0));
            };
            batch.Completed += pointerGlideCompleted;
            pointerVisual.Properties.StartAnimation("Translation", animation);
            batch.End();
            return;
        }

        if (freeMovement)
        {
            // An analog takeover is immediate; do not leave old jump particles
            // or interpolate a trail from an unrelated pre-glide screen position.
            if (pointerGlideBatch is not null) ClearPointerTrail();
            RetirePointerGlide(stop: true);
            if (!pointerAccessibility.HighContrast) DrawPointerTrail(target);
            else ClearPointerTrail();
            pointerVisualTarget = target;
            pointerVisual!.Properties.InsertVector3("Translation", new(target, 0));
            return;
        }

        // Routine DrawCursor calls must not cut a still-running glide short.
        // Actual layout/position changes settle immediately in the new viewport.
        if (target != pointerVisualTarget)
        { pointerVisualTarget = target; FinishPointerVisual(); }
    }

    private void DrawPointerTrail(Vector2 target)
    {
        var now = Stopwatch.GetTimestamp();
        pointerTrailClip.Rect = new(0, 0, viewport.ActualWidth, viewport.ActualHeight);
        foreach (var sample in pointerTrail)
            if (sample.Started != 0 && Vector2.Distance(sample.Position, target) > PointerMotionOptions.TrailMaximumLength)
            { sample.Visual.StopAnimation(nameof(Visual.Opacity)); sample.Visual.Opacity = 0; sample.Started = 0; }

        if (pointerTrailSampleTime == 0)
        { pointerTrailOrigin = target; pointerTrailSampleTime = now; return; }
        var elapsed = Stopwatch.GetElapsedTime(pointerTrailSampleTime, now);
        var distance = Vector2.Distance(pointerTrailOrigin, target);
        if (elapsed.TotalMilliseconds < PointerMotionOptions.TrailSampleMilliseconds) return;
        if (elapsed > PointerMotionOptions.TrailLifetime || distance < PointerMotionOptions.TrailMinimumStep)
        { pointerTrailOrigin = target; pointerTrailSampleTime = now; return; }
        var speed = distance / (float)elapsed.TotalSeconds;
        var strength = Math.Clamp(speed / PointerMotionOptions.TrailFullStrengthSpeed, 0, 1);
        var position = distance <= PointerMotionOptions.TrailMaximumLength
            ? pointerTrailOrigin : target + Vector2.Normalize(pointerTrailOrigin - target) * PointerMotionOptions.TrailMaximumLength;
        var dot = pointerTrail[pointerTrailIndex];
        pointerTrailIndex = (pointerTrailIndex + 1) % pointerTrail.Count;
        dot.Position = position; dot.Started = now; dot.Element.Background = cursor.Background;
        dot.Visual.StopAnimation(nameof(Visual.Opacity));
        dot.Visual.Properties.InsertVector3("Translation", new(position + new Vector2((float)(ControllerPointerAppearance.Diameter - PointerMotionOptions.TrailDiameter) / 2), 0));
        dot.Visual.Scale = new(.55f + strength * .45f, .55f + strength * .45f, 1);
        dot.Visual.CenterPoint = new(PointerMotionOptions.TrailDiameter / 2, PointerMotionOptions.TrailDiameter / 2, 0);
        using var fade = dot.Visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = PointerMotionOptions.TrailLifetime;
        fade.InsertKeyFrame(0, PointerMotionOptions.TrailMaximumOpacity * strength);
        fade.InsertKeyFrame(1, 0);
        dot.Visual.StartAnimation(nameof(Visual.Opacity), fade);
        pointerTrailOrigin = target; pointerTrailSampleTime = now;
    }

    // Called before clicks: the displayed circle and native click must agree.
    private void FinishPointerVisual()
    {
        RetirePointerGlide(stop: true); ClearPointerTrail();
        if (pointerVisual is null) return;
        pointerVisualTarget = PointerVisualDestination();
        pointerVisual.Properties.InsertVector3("Translation", new(pointerVisualTarget, 0));
    }

    private void ResetPointerVisual()
    {
        RetirePointerGlide(stop: true); ClearPointerTrail();
        pointerVisualPositioned = false;
    }

    private void RetirePointerGlide(bool stop)
    {
        if (pointerGlideBatch is { } batch)
        {
            pointerGlideBatch = null;
            batch.Completed -= pointerGlideCompleted;
            pointerGlideCompleted = null; batch.Dispose();
        }
        if (stop) pointerVisual?.Properties.StopAnimation("Translation");
    }

    private void ClearPointerTrail()
    {
        pointerTrailSampleTime = 0;
        foreach (var sample in pointerTrail)
        { sample.Visual.StopAnimation(nameof(Visual.Opacity)); sample.Visual.Opacity = 0; sample.Started = 0; }
    }

#if ENABLE_WIDGET_VALIDATION || ENABLE_TRIMMED_CONTROL_VALIDATION
    internal bool FixturePointerMotionEnabled => PointerMotionEnabled && !pointerAccessibility.HighContrast;
    internal bool FixturePointerGliding => pointerGlideBatch is not null;
    internal Vector2 FixturePointerVisualTarget => pointerVisualTarget;
    internal bool FixturePointerReducedMotion { set => SetPointerMotionReduced(value); }
    internal int FixturePointerTrailCount => pointerTrail.Count(sample => sample.Started != 0 &&
        Stopwatch.GetElapsedTime(sample.Started) < PointerMotionOptions.TrailLifetime);
#endif
}
