using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class TextEntryValidationPage
{
    private async Task VerifyPopupScalingAsync()
    {
        sensitive = false; value = "ab"; Apply(); await Open();
        var dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        var editor = (TextBox)Editor();
        var key = (Button)FindDialog("Widget.TextEntry.Key.10")!;
        var commit = (Button)FindDialog("Widget.TextEntry.Commit")!;
        var body = (StackPanel)((ScrollViewer)dialog.Content).Content;
        var keys = (Grid)body.Children[1];
        var title = (TextBlock)dialog.Title;
        var focused = FocusedId;
        var count = actions.Count;
        foreach (var zoom in new[] { .5, 1.25, 1d })
        {
            popupTheme.Update(null, AppearanceSettings.Default with { InterfaceScale = zoom });
            await Task.Delay(100);
            Check(Near(key.FontSize, 16 * zoom) && Near(editor.FontSize, key.FontSize) &&
                Near(title.FontSize, 20 * zoom) && Near(key.MinHeight, 36 * zoom) && Near(keys.ColumnSpacing, 4 * zoom),
                "native keyboard, editor and heading use interface scale once at " + zoom);
            var expectedWidth = Math.Max(1, Math.Min(640 * zoom, XamlRoot.Size.Width - 120 * zoom));
            Check(Near(body.ActualWidth, expectedWidth) && Near(body.Spacing, 12 * zoom),
                "native keyboard width and spacing follow the bounded popup viewport at " + zoom);
            var prompt = (StackPanel)commit.Content;
            Check(Near(((FontIcon)prompt.Children[0]).FontSize, 18 * zoom) &&
                Near(((TextBlock)prompt.Children[1]).FontSize, key.FontSize),
                "controller key labels and glyphs follow interface scale at " + zoom);
            foreach (var button in keys.Children.OfType<Button>())
            {
                var bounds = button.TransformToVisual(body).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                if (bounds.Left < -1 || bounds.Right > body.ActualWidth + 1)
                    throw new InvalidOperationException("Keyboard key escaped the scaled viewport");
            }
            Check(ReferenceEquals(FindDialog("Widget.TextEntry.Dialog"), dialog) && ReferenceEquals(Editor(), editor) &&
                editor.Text == "ab" && FocusedId == focused && actions.Count == count,
                "live interface scaling retains edit value, focus and native dialog at " + zoom);
            await CapturePopupAsync(dialog, "text-entry-scale-" + zoom.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        popupTheme.Update(null, AppearanceSettings.Default with { InterfaceScale = 1.25, TextScale = 1.5, BoldText = true });
        await Task.Delay(100);
        Check(Near(key.FontSize, 30) && Near(title.FontSize, 37.5) && key.FontWeight.Weight >= 600 &&
            Near(keys.ColumnSpacing, 5) && Near(((FontIcon)((StackPanel)commit.Content).Children[0]).FontSize, 22.5),
            "text accessibility scales typography independently from keyboard geometry and controller glyphs");
        await CapturePopupAsync(dialog, "text-entry-scale-accessibility");
        popupTheme.Update(null, AppearanceSettings.Default with { BoldText = false });
        await Task.Delay(100);
        Check(Near(key.FontSize, 16) && Near(title.FontSize, 20) && key.FontWeight.Weight == 400 &&
            Near(key.MinHeight, 36) && Near(keys.ColumnSpacing, 4) && editor.Text == "ab" && FocusedId == focused,
            "removing scale and bold preferences restores the same native edit session");
        await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();
        Check(actions.Count == count, "scaling and dismissing the keyboard never dispatches an edit");
        popupTheme.Update(null, AppearanceSettings.Default);
        static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1;
    }

    private static async Task CapturePopupAsync(WidgetTextEntryDialog dialog, string name)
    {
        var raster = new RenderTargetBitmap();
        await raster.RenderAsync(dialog);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(directory);
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync(name + ".png", CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)raster.PixelWidth, (uint)raster.PixelHeight,
            96, 96, (await raster.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
