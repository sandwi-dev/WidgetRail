using System.Numerics;
using Microsoft.UI.Composition;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

internal readonly record struct SettledOutlineStyle(string? Effect, TimeSpan Duration, float Intensity, TimeSpan Rest, Color? Highlight = null)
{
    internal bool Enabled => Effect is not null && Duration > TimeSpan.Zero && Intensity > 0;
}

internal sealed partial class WidgetFocusDecoration
{
    private SettledOutlineStyle settledStyle;
    private NativeMotionVisibility? settledVisibility;
    private CompositionSpriteShape? highlight;
    private CompositionBrush? highlightBrush;

    private void ArmSettledEffect()
    {
        DisarmSettledEffect();
        if (!focused || !settledStyle.Enabled || target is null) return;
        var observation = new NativeMotionVisibility(control, CanAnimate, SetSettledVisible);
        settledVisibility = observation;
        if (Playback is { IsCompleted: false } entry) _ = AfterEntryAsync(entry, observation);
        else observation.Refresh();
    }

    internal void RefreshSettledVisibility() => settledVisibility?.Refresh();

    private bool CanAnimate()
    {
        if (!focused || !control.IsHitTestVisible || !DialogChoiceNavigation.ContainsFocus(control)) return false;
        for (Microsoft.UI.Xaml.DependencyObject? parent = control; parent is not null; parent = PresentationLayers.LogicalParent(parent))
            if (parent is WidgetViewPresenter presenter && !presenter.FocusEffectsActive) return false;
        return true;
    }

    private async Task AfterEntryAsync(Task<WidgetMotionOutcome> entry, NativeMotionVisibility observation)
    {
        if (await entry == WidgetMotionOutcome.Completed && ReferenceEquals(observation, settledVisibility))
            observation.Refresh();
    }

    private void SetSettledVisible(bool visible)
    {
        if (!visible) { RemoveHighlight(); return; }
        if (highlight is not null || Playback is { IsCompleted: false } || !focused || disposed) return;
        var compositor = visual.Compositor;
        var color = brush.Color;
        // A very pale focus color still needs a visible variation on light themes.
        var channel = color.R > 225 && color.G > 225 && color.B > 225 ? (byte)80 : (byte)255;
        var tint = settledStyle.Highlight ?? Color.FromArgb(255, channel, channel, channel);
        var peak = Color.FromArgb((byte)Math.Round(tint.A * settledStyle.Intensity), tint.R, tint.G, tint.B);
        var clear = Color.FromArgb(0, tint.R, tint.G, tint.B);
        var cycle = settledStyle.Duration + settledStyle.Rest;
        var end = (float)(settledStyle.Duration.TotalSeconds / cycle.TotalSeconds);
        var repeat = settledStyle.Rest > TimeSpan.Zero ? AnimationIterationBehavior.Forever : AnimationIterationBehavior.Count;
        var shape = compositor.CreateSpriteShape(geometry);
        shape.StrokeThickness = width;
        if (settledStyle.Effect == "edge-glint")
        {
            // A broader moving crest and a shaded shoulder give the glint
            // contrast even when the steady outline is already almost white.
            shape.StrokeThickness += Math.Min(2f, width * .65f) * settledStyle.Intensity;
            var shoulder = Color.FromArgb((byte)Math.Round(peak.A * .55),
                (byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2));
            var gradient = compositor.CreateLinearGradientBrush();
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(0, clear));
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(.24f, shoulder));
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(.46f, peak));
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(.62f, peak));
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(1, clear));
            using var animation = compositor.CreateVector2KeyFrameAnimation();
            using var linear = compositor.CreateLinearEasingFunction();
            animation.Duration = cycle; animation.IterationBehavior = repeat;
            void Sweep(string property, float from, float to)
            {
                animation.InsertKeyFrame(0, new Vector2(from), linear);
                animation.InsertKeyFrame(end, new Vector2(to), linear);
                if (end < 1) animation.InsertKeyFrame(1, new Vector2(to), linear);
                gradient.StartAnimation(property, animation);
            }
            Sweep(nameof(gradient.StartPoint), -.65f, 1.05f);
            Sweep(nameof(gradient.EndPoint), -.05f, 1.65f);
            highlightBrush = gradient;
        }
        else
        {
            var pulse = compositor.CreateColorBrush(clear);
            using var animation = compositor.CreateColorKeyFrameAnimation();
            animation.Duration = cycle; animation.IterationBehavior = repeat;
            animation.InsertKeyFrame(0, clear);
            animation.InsertKeyFrame(end / 2, peak);
            animation.InsertKeyFrame(end, clear);
            if (end < 1) animation.InsertKeyFrame(1, clear);
            pulse.StartAnimation(nameof(pulse.Color), animation);
            highlightBrush = pulse;
        }
        shape.StrokeBrush = highlightBrush;
        visual.Shapes.Add(shape);
        highlight = shape;
    }

    private void RemoveHighlight()
    {
        if (highlight is null) return;
        visual.Shapes.Remove(highlight);
        highlight.Dispose(); highlight = null;
        highlightBrush?.Dispose(); highlightBrush = null;
    }

    private void DisarmSettledEffect()
    {
        settledVisibility?.Dispose(); settledVisibility = null;
        RemoveHighlight();
    }
}
