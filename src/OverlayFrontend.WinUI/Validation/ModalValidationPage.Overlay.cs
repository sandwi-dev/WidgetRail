using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ModalValidationPage
{
    private async Task CheckOverlayPlacementAsync()
    {
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        App.Window.AppWindow.ResizeClient(new(1280,1000));
        await Until(() => ((Grid)Content).ActualHeight > 600 && ((Grid)Content).ActualWidth > 900);
        overlayPresentation = true; Apply();
        var panel = Find(modal!)!;
        await Until(() => panel.ActualWidth > widget.ActualWidth && Near(panel.ActualWidth / panel.ActualHeight, 16.0/9));
        var layer = AncestorLayer(panel);
        Check(Near(layer.ActualWidth, ((Grid)Content).ActualWidth) && Near(layer.ActualHeight, ((Grid)Content).ActualHeight),
            "Overlay modal scrim uses the shell viewport instead of the owning widget");
        var bounds = panel.TransformToVisual((Grid)Content).TransformPoint(new(0,0));
        Check(bounds.X >= 31 && bounds.Y >= 47 && panel.ActualWidth <= 1281,
            "Overlay modal preserves 16:9 with bounded width and outside-click space");
        ((Button)Find("second")!).Focus(FocusState.Keyboard);
        var width = panel.ActualWidth;
        widget.Width = 330; widget.Height = 180;
        await Task.Delay(120);
        Check(Near(panel.ActualWidth,width) && FocusedId == "Widget.second", "Compact widget resize does not shrink overlay media or steal focus");
        App.Window.AppWindow.ResizeClient(new(780,580));
        await Until(() => panel.ActualWidth < width && Near(panel.ActualWidth / panel.ActualHeight,16.0/9));
        Check(panel.ActualHeight <= ((Grid)Content).ActualHeight-95, "Short overlay reclamps proportionally without overflowing");
        App.Window.AppWindow.ResizeClient(new(1280,1000));
        await Until(() => ((Grid)Content).ActualWidth > 900);
        overlayPresentation = false; widget.Width = 600; widget.Height = 250; Apply();
        await CheckLocalBoundsAsync(panel,"Returning to widget-local mode restores original modal clipping");
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
    }
}
