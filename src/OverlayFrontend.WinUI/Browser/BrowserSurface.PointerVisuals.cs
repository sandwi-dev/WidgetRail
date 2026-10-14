using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Input;
using Windows.UI.ViewManagement;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private readonly UISettings pointerUiSettings = new();
    private Visual? pointerVisual;
    private CompositionScopedBatch? pointerGlideBatch;
    private Windows.Foundation.TypedEventHandler<object, CompositionBatchCompletedEventArgs>? pointerGlideCompleted;
    private Vector2 pointerVisualTarget;
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
            // Analog input always takes over immediately, including mid-glide.
            RetirePointerGlide(stop: true);
            pointerVisualTarget = target;
            pointerVisual!.Properties.InsertVector3("Translation", new(target, 0));
            return;
        }

        // Routine DrawCursor calls must not cut a still-running glide short.
        // Actual layout/position changes settle immediately in the new viewport.
        if (target != pointerVisualTarget)
        { pointerVisualTarget = target; FinishPointerVisual(); }
    }

    // Called before clicks: the displayed circle and native click must agree.
    private void FinishPointerVisual()
    {
        RetirePointerGlide(stop: true);
        if (pointerVisual is null) return;
        pointerVisualTarget = PointerVisualDestination();
        pointerVisual.Properties.InsertVector3("Translation", new(pointerVisualTarget, 0));
    }

    private void ResetPointerVisual()
    {
        RetirePointerGlide(stop: true);
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

#if ENABLE_WIDGET_VALIDATION || ENABLE_TRIMMED_CONTROL_VALIDATION
    internal bool FixturePointerMotionEnabled => PointerMotionEnabled;
    internal bool FixturePointerGliding => pointerGlideBatch is not null;
    internal Vector2 FixturePointerVisualTarget => pointerVisualTarget;
    internal bool FixturePointerReducedMotion { set => SetPointerMotionReduced(value); }
#endif
}
