using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Notifications;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class NotificationBannerStyleValidationPage
{
    private async Task VerifyTextFitAsync()
    {
        using var peer = new PinnedWidgetWindow("banner text validation", WinRT.Interop.WindowNative.GetWindowHandle(App.Window), PeerWindowLayer.Notification);
        var card = new NotificationCard((_, _, _) => Task.FromException<WidgetPresentationPackageIcon>(new InvalidOperationException()));
        using var chrome = new NotificationBannerChrome(card);
        var palette = ShellChromePalette.Resolve(ShellChromeValidationPage.Palette("builtin-mono-ink"), PlatformSettings.AppearanceSettings.Default);
        chrome.Apply(palette, 100, reducedTransparency: true, acrylicEffects: false);
        card.ApplyBannerPalette(palette);
        peer.SetContent(chrome);
        var descriptor = new BridgeWidgetDescriptor { Id = "capture-library", Name = "Capture Library", InstanceId = "fixture",
            RuntimeGeneration = "fixture", PresentationGeneration = "fixture", PackageContentDigest = "", Icon = WidgetGlyph.Fullscreen };
        var entry = new NotificationEntry(1, descriptor.Id, "widget", "", "", descriptor.Name, "", this, new(() => true, () => {}, () => {}, () => {}, () => {}));
        const string single = "1 captures moved to the Recycle Bin.";
        const string wrapped = "Preparing your gallery preview and grouping the newly saved screenshots by application.";
        foreach (var (zoom, textScale, message) in new[] { (.9, 1d, single), (.9, 1d, wrapped), (1d, 1.5, wrapped), (.75, 1d, single), (1.25, 1d, single) })
        {
            peer.InterfaceScale = zoom;
            entry.UpdateContent(new("capture-result", descriptor.Name) { Body = message });
            card.Update(entry, descriptor, textScale);
            await LayoutAsync();
            var label = FindBody(card, message) ?? throw new InvalidOperationException("Notification body was not realized.");
            var production = await InkHeightAsync(label);
            var padding = label.Padding;
            // Grow only the paint area below the last line. The same text, font,
            // width, DPI and zoom give us a reference with an unclipped descent.
            label.Padding = new(padding.Left, padding.Top, padding.Right, padding.Bottom + 12);
            await LayoutAsync();
            var reference = await InkHeightAsync(label);
            label.Padding = padding;
            if (production < 1 || reference < 1 || Math.Abs(production - reference) > 1)
                throw new InvalidOperationException($"Banner ink at zoom {zoom}, text {textScale}: {production}px, reference {reference}px.");
        }

        async Task LayoutAsync()
        {
            var scale = XamlRoot.RasterizationScale * peer.InterfaceScale;
            var width = (int)Math.Round(480 * scale);
            chrome.Measure(new(width / scale, double.PositiveInfinity));
            var height = (int)Math.Ceiling(Math.Clamp(chrome.DesiredSize.Height, 152, 420) * scale);
            peer.Place(new(200, 200, width, height));
            peer.Show();
            await Task.Delay(120);
            peer.AutomationRoot.UpdateLayout();
        }

        async Task<int> InkHeightAsync(TextBlock label)
        {
            var root = peer.AutomationRoot;
            var rect = label.TransformToVisual(root).TransformBounds(new(0, 0, label.ActualWidth, label.ActualHeight));
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(root);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            var dpi = bitmap.PixelWidth / root.ActualWidth;
            var left = Math.Clamp((int)Math.Floor(rect.Left * dpi), 0, bitmap.PixelWidth);
            var right = Math.Clamp((int)Math.Ceiling(rect.Right * dpi), left, bitmap.PixelWidth);
            var top = Math.Clamp((int)Math.Floor(rect.Top * dpi), 0, bitmap.PixelHeight);
            var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom * dpi), top, bitmap.PixelHeight);
            int first = bottom, last = -1;
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                    if (pixels[(y * bitmap.PixelWidth + x) * 4 + 2] > 100)
                    {
                        first = Math.Min(first, y);
                        last = y;
                        break;
                    }
            return last < first ? 0 : last - first + 1;
        }
    }

    private static TextBlock? FindBody(DependencyObject root, string text)
    {
        if (root is TextBlock label && label.Text == text) return label;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindBody(VisualTreeHelper.GetChild(root, i), text) is { } child) return child;
        return null;
    }
}
