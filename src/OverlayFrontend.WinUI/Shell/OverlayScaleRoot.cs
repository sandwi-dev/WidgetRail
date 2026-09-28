using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>
/// Layout-aware host zoom. Children receive the actual available design viewport,
/// then WinUI applies one matching presentation transform. Unlike a fixed canvas
/// stretched to fit, narrow windows still reflow and virtualize at their real size.
/// </summary>
public sealed class OverlayScaleRoot : Panel
{
    public static readonly DependencyProperty InterfaceScaleProperty = DependencyProperty.Register(nameof(InterfaceScale),
        typeof(double), typeof(OverlayScaleRoot), new PropertyMetadata(1d, (sender, _) => ((OverlayScaleRoot)sender).InvalidateMeasure()));
    public double InterfaceScale
    {
        get => (double)GetValue(InterfaceScaleProperty);
        set => SetValue(InterfaceScaleProperty, double.IsFinite(value) ? Math.Clamp(value,
            AppearanceSettings.MinimumInterfaceScale, AppearanceSettings.MaximumInterfaceScale) : 1d);
    }
    private readonly ScaleTransform transform = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return default;
        if (Children.Count != 1) throw new InvalidOperationException("The overlay scale root requires exactly one child.");
        var scale = InterfaceScale;
        var child = Children[0];
        child.RenderTransform = transform;
        transform.ScaleX = transform.ScaleY = scale;
        child.Measure(new(availableSize.Width / scale, availableSize.Height / scale));
        return new(child.DesiredSize.Width * scale, child.DesiredSize.Height * scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 1) Children[0].Arrange(new Rect(0, 0, finalSize.Width / InterfaceScale, finalSize.Height / InterfaceScale));
        return finalSize;
    }
}
