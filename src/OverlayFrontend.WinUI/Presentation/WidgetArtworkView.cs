using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Native image measurement plus ImageBrush painting. Unlike Image's internal
/// centered brush, ImageBrush exposes the authored crop/letterbox alignment.
/// The measurement Image stays off-tree: one decoded source, one painted layer.
/// </summary>
internal sealed class WidgetArtworkView : Grid
{
    private readonly Image measurement = new() { Stretch = Stretch.Fill };
    private readonly ImageBrush brush = new();
    private WidgetArtworkOverlays? overlays;
    private NativeArtworkStyle style = new(Stretch.Fill);

    internal WidgetArtworkView() => Children.Add(new Border { Background = brush });

    internal ImageSource? Source
    {
        get => brush.ImageSource;
        set
        {
            if (ReferenceEquals(value, brush.ImageSource)) return;
            measurement.Source = brush.ImageSource = value;
            overlays?.Update(style, value is not null);
            InvalidateMeasure();
        }
    }

    internal Stretch Stretch => brush.Stretch;

    internal void ApplyStyle(NativeArtworkStyle value)
    {
        if (style.Stretch != value.Stretch || brush.Stretch != value.Stretch)
        {
            measurement.Stretch = brush.Stretch = value.Stretch;
            InvalidateMeasure();
        }
        brush.AlignmentX = value.AlignmentX; brush.AlignmentY = value.AlignmentY;
        style = value;
        if (overlays is null && (value.Tint is not null || value.Scrim is not null))
        { overlays = new(); Children.Add(overlays); }
        overlays?.Update(value, Source is not null);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Delegate intrinsic dimensions and aspect-ratio measurement to WinUI,
        // including one unconstrained axis. Paint uses the same BitmapImage.
        measurement.Measure(availableSize);
        var painted = base.MeasureOverride(availableSize);
        return new(Math.Max(painted.Width, measurement.DesiredSize.Width),
            Math.Max(painted.Height, measurement.DesiredSize.Height));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ImagePeer(this);
    private sealed class ImagePeer(WidgetArtworkView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(WidgetArtworkView);
        protected override List<AutomationPeer> GetChildrenCore() => [];
    }
}
