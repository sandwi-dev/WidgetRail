using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

// Composition draws over the button face without affecting the focus target or its arranged bounds.
internal sealed class FavoriteButtonFeedback : IDisposable
{
    private readonly Button button;
    private readonly Compositor compositor;
    private ShapeVisual? visual;
    private CompositionRoundedRectangleGeometry? geometry;
    private CompositionLinearGradientBrush? moving;
    private ShellChromePalette? palette;
    private WidgetMotionOptions motion;
    private bool busy;
    internal bool IsBusy => busy;

    internal FavoriteButtonFeedback(Button button)
    {
        this.button = button;
        compositor = CompositionTarget.GetCompositorForCurrentThread();
        button.SizeChanged += SizeChanged;
        button.Unloaded += Unloaded;
    }

    internal void Press(WidgetMotionOptions options)
    {
        if (!button.IsLoaded) return;
        if (options.Reduced)
        {
            using var flash = compositor.CreateScalarKeyFrameAnimation();
            flash.Target = "Opacity";
            flash.InsertKeyFrame(0, .72f);
            flash.InsertKeyFrame(.99f, .72f);
            flash.InsertKeyFrame(1, 1);
            flash.Duration = TimeSpan.FromMilliseconds(120);
            button.StartAnimation(flash);
            return;
        }
        button.CenterPoint = new((float)button.ActualWidth / 2, (float)button.ActualHeight / 2, 0);
        using var press = compositor.CreateVector3KeyFrameAnimation();
        press.Target = "Scale";
        press.InsertExpressionKeyFrame(0, "this.StartingValue");
        press.InsertKeyFrame(.36f, new(.96f, .96f, 1));
        press.InsertKeyFrame(1, Vector3.One);
        press.Duration = options.Duration(190);
        button.StartAnimation(press);
    }

    internal void SetBusy(bool value, ShellChromePalette colors, WidgetMotionOptions options)
    {
        if (busy == value && (!value || palette == colors && motion == options)) return;
        Clear();
        busy = value;
        AutomationProperties.SetItemStatus(button, value ? "In progress" : "");
        if (!value) return;
        palette = colors;
        motion = options;
        visual = compositor.CreateShapeVisual();
        geometry = compositor.CreateRoundedRectangleGeometry();
        var outline = compositor.CreateSpriteShape(geometry);
        outline.StrokeThickness = colors.HighContrast || options.Reduced ? 3.5f : 3;
        if (colors.HighContrast || options.Reduced)
        {
            outline.StrokeBrush = compositor.CreateColorBrush(colors.HighContrast ? colors.Text : colors.Focus);
            var hairline = compositor.CreateSpriteShape(geometry);
            hairline.StrokeThickness = 1;
            hairline.StrokeBrush = compositor.CreateColorBrush(colors.Surface);
            visual.Shapes.Add(outline);
            visual.Shapes.Add(hairline);
        }
        else
        {
            moving = compositor.CreateLinearGradientBrush();
            moving.StartPoint = new(0, .5f);
            moving.EndPoint = new(1, .5f);
            moving.ColorStops.Add(compositor.CreateColorGradientStop(0, colors.Focus));
            moving.ColorStops.Add(compositor.CreateColorGradientStop(.45f, ShellChromePalette.Blend(colors.Focus, Microsoft.UI.Colors.MediumOrchid, .8)));
            moving.ColorStops.Add(compositor.CreateColorGradientStop(.83f, ShellChromePalette.Blend(colors.Focus, Microsoft.UI.Colors.Turquoise, .8)));
            moving.ColorStops.Add(compositor.CreateColorGradientStop(1, colors.Text));
            outline.StrokeBrush = moving;
            visual.Shapes.Add(outline);
            using var rotation = compositor.CreateScalarKeyFrameAnimation();
            rotation.InsertKeyFrame(0, 0);
            rotation.InsertKeyFrame(1, 360, compositor.CreateLinearEasingFunction());
            rotation.IterationBehavior = AnimationIterationBehavior.Forever;
            rotation.Duration = TimeSpan.FromSeconds(1.5);
            Resize();
            moving.StartAnimation("RotationAngleInDegrees", rotation);
        }
        Resize();
        ElementCompositionPreview.SetElementChildVisual(button, visual);
    }

    private void SizeChanged(object sender, SizeChangedEventArgs args) => Resize();
    private void Resize()
    {
        if (visual is null || geometry is null) return;
        visual.Size = new((float)button.ActualWidth, (float)button.ActualHeight);
        geometry.Offset = new(2);
        geometry.Size = Vector2.Max(Vector2.Zero, visual.Size - new Vector2(4));
        geometry.CornerRadius = new((float)Math.Max(0, button.CornerRadius.TopLeft - 2));
        if (moving is not null) moving.CenterPoint = new(.5f, .5f);
    }

    private void Clear()
    {
        if (visual is not null) ElementCompositionPreview.SetElementChildVisual(button, null);
        moving?.StopAnimation("RotationAngleInDegrees");
        visual?.Dispose();
        moving?.Dispose();
        geometry?.Dispose();
        visual = null;
        moving = null;
        geometry = null;
    }

    private void Unloaded(object sender, RoutedEventArgs args)
    {
        Clear();
        busy = false;
        AutomationProperties.SetItemStatus(button, "");
    }
    public void Dispose()
    {
        button.Unloaded -= Unloaded;
        button.SizeChanged -= SizeChanged;
        Clear();
    }
}
