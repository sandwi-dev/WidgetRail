using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>Native layout slot for browser pixels. SDK media viewports are deliberately not focus targets.</summary>
internal sealed partial class WidgetMediaViewport : ContentControl
{
    internal Grid SurfaceHost { get; } = new();
    private readonly ProgressRing loading = new() { Width = 32, Height = 32, IsActive = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock pinnedNotice = new() { Text = "Media is playing in the pinned window", TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    internal Action? PlacementChanged { get; set; }
    internal EmbeddedMediaSession? Declaration { get; private set; }
    internal bool ScopeActive { get; set; }
    internal Func<bool>? CanAcceptInput { get; set; }
    internal bool AcceptsInput => ScopeActive && (CanAcceptInput?.Invoke() ?? false);
    internal bool Retired { get; private set; }

    public WidgetMediaViewport()
    {
        var layers = new Grid(); layers.Children.Add(SurfaceHost); layers.Children.Add(loading); layers.Children.Add(pinnedNotice); Content = layers;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(loading, "Starting media player");
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsHitTestVisible = false;
        Loaded += (_, _) => PlacementChanged?.Invoke();
        Unloaded += (_, _) => PlacementChanged?.Invoke();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => PlacementChanged?.Invoke());
        SizeChanged += (_, _) =>
        {
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)) };
            PlacementChanged?.Invoke();
        };
    }

    internal void SetMediaPinned(bool pinned)
    {
        pinnedNotice.Foreground = Foreground; pinnedNotice.FontSize = FontSize;
        pinnedNotice.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;
        if (pinned) SetMediaLoading(false);
    }

    internal void SetMediaLoading(bool active)
    {
        loading.Foreground = Foreground;
        loading.IsActive = active;
        loading.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Configure(EmbeddedMediaSession? declaration, bool scopeActive)
    {
        Declaration = declaration;
        ScopeActive = scopeActive;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var preferred = Declaration?.Surface;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : preferred?.PreferredWidth ?? 640;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : width / (Declaration?.AspectRatio ?? 16d / 9);
        base.MeasureOverride(new(Math.Max(0, width), Math.Max(0, height)));
        return new(Math.Max(0, width), Math.Max(0, height));
    }

    internal void Retire()
    {
        if (Retired) return;
        Retired = true;
        SetMediaLoading(false);
        PlacementChanged?.Invoke();
        PlacementChanged = null;
        CanAcceptInput = null;
    }
}
