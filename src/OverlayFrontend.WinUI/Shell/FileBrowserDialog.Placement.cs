using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class FileBrowserDialog
{
    private Size? popupArea;
    internal override bool PositionsOwnPopup => popupArea is not null;

    private async Task ShowPickerAsync(FrameworkElement origin)
    {
        var root = XamlRoot;
        if (root.Size.Width >= 440 && root.Size.Height >= 440)
        {
            await ShowAsync();
            return;
        }

        // Like the host keyboard, keep the requesting XAML root and input owner.
        // A small pin can borrow work-area space without resizing its window.
        var windowId = root.ContentIslandEnvironment.AppWindowId;
        var window = AppWindow.GetFromWindowId(windowId);
        var work = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest).WorkArea;
        var dpi = root.RasterizationScale;
        var width = Math.Min(620, Math.Max(1, work.Width / dpi - 32));
        var height = Math.Min(640, Math.Max(1, work.Height / dpi - 32));
        var scaleOrigin = origin.TransformToVisual(root.Content).TransformBounds(new Rect(0, 0, origin.ActualWidth, origin.ActualHeight));
        var left = (work.X - window.Position.X) / dpi + 16;
        var top = (work.Y - window.Position.Y) / dpi + 16;
        var stage = new Grid { Width = width, Height = height };
        var popup = new Popup
        {
            XamlRoot = root,
            Child = stage,
            ShouldConstrainToRootBounds = false,
            HorizontalOffset = Math.Clamp(scaleOrigin.X + (scaleOrigin.Width - width) / 2, left, Math.Max(left, left + work.Width / dpi - width - 32)),
            VerticalOffset = Math.Clamp(scaleOrigin.Y + (scaleOrigin.Height - height) / 2, top, Math.Max(top, top + work.Height / dpi - height - 32))
        };
        popupArea = new Size(width, height);
        try
        {
            stage.Children.Add(this);
            popup.IsOpen = true;
            await ShowAsync(ContentDialogPlacement.InPlace);
        }
        finally
        {
            popup.IsOpen = false;
            stage.Children.Remove(this);
            popup.Child = null;
            popupArea = null;
        }
    }
}
