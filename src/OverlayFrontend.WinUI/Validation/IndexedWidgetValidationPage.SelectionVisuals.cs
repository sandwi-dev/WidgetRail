using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidateSelectionCheckboxVisualsAsync(CheckBox checkbox, string mode, string output, Action<bool, string> check, Func<bool, Task> select)
    {
        var theme = checkbox.RequestedTheme; var selected = checkbox.IsChecked;
        try
        {
            foreach (var requested in new[] { ElementTheme.Light, ElementTheme.Dark })
            foreach (var value in new[] { false, true })
            {
                checkbox.RequestedTheme = requested;
                await select(value); checkbox.UpdateLayout();
                await Task.Delay(400, lifetime.Token);
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(checkbox, 64, 64);
                var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                check(Math.Abs(checkbox.ActualWidth - 32) < .1 && Math.Abs(checkbox.ActualHeight - 32) < .1, mode + ": checkbox keeps its 32-DIP hit area");
                var width = bitmap.PixelWidth; var height = bitmap.PixelHeight;
                // The native 20-DIP indicator must not paint the full 32-DIP target.
                var outer = Enumerable.Range(height / 8, height * 3 / 4).Max(y => pixels[(y * width + width - 2) * 4 + 3]);
                check(outer == 0, $"{mode} {requested} {(value ? "checked" : "unchecked")}: no rectangular outer fill");
                if (mode != "selection-list" && !value)
                {
                    var center = ((height / 2) * width + width * 10 / 32) * 4;
                    check(pixels[center + 3] >= 150, $"{requested}: on-image indicator has sufficient fill over bright and dark thumbnails");
                }
                var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, $"{mode}-{requested}-{(value ? "checked" : "unchecked")}.png");
                using var stream = File.Create(file).AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 192, 192, pixels);
                await encoder.FlushAsync();
            }
        }
        finally { checkbox.RequestedTheme = theme; await select(selected == true); }
    }
}
