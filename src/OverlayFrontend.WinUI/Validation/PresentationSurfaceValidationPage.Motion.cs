using Microsoft.UI.Xaml;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PresentationSurfaceValidationPage
{
    private async Task ArtworkMotionAsync()
    {
        var settings = AppearanceSettings.Default with { Motion = MotionPreference.Full, WidgetAnimationSpeed = 2, Contrast = ContrastPreference.Standard };
        WidgetPresentationSurface.SetMotionAppearance(settings, true);
        var panel = (StackPanel)Content;
        var surface = new WidgetPresentationSurface(ViewNodeKind.BackgroundSurface) { Width = 320, Height = 140 };
        var label = new TextBlock { Text = "Content remains independent" };
        surface.ContentPanel.Children.Add(label);
        panel.Children.Add(surface);
        var motion = surface.ArtworkMotion;
        // Distinct decoded objects: the rendering helper must never replace or decode them.
        var red = Pixels(220, 40, 40);
        var green = Pixels(40, 200, 80);
        var blue = Pixels(40, 80, 220);
        var last = Pixels(220, 180, 40);
        try
        {
            await Until(() => surface.IsLoaded && surface.ActualWidth == 320);
            surface.SetArtwork(red, ImageFit.Cover);
            Check(ReferenceEquals(surface.ArtworkSource, red) && motion.RetainedImageCount == 1,
                "first decoded artwork uses its existing source without an entrance buffer");
            surface.SetArtwork(green, ImageFit.Cover);
            await Until(() => motion.StartedCount == 1);
            Check(ReferenceEquals(motion.OutgoingSource, red) && ReferenceEquals(motion.IncomingSource, green),
                "native artwork batch paints the previous and current decoded objects");
            Check(label.Opacity == 1 && label.Translation == System.Numerics.Vector3.Zero && label.Scale == System.Numerics.Vector3.One,
                "artwork blending does not fade or transform foreground widget content");
            surface.SetArtwork(blue, ImageFit.Cover);
            surface.SetArtwork(last, ImageFit.Cover);
            Check(ReferenceEquals(surface.ArtworkSource, last) && motion.RetainedImageCount <= 3,
                "rapid focus coalesces into one latest proposal without resetting the active blend");
            await Until(() => motion.StartedCount == 2 && motion.IncomingSource is null);
            Check(ReferenceEquals(surface.ArtworkSource, last) && motion.RetainedImageCount == 1 && motion.Failure is null,
                "superseded artwork completions do not overwrite the latest source or retain older images");
            var count = motion.StartedCount;
            surface.SetArtwork(last, ImageFit.Contain);
            WidgetPresentationSurface.SetMotionAppearance(settings with { BoldText = !settings.BoldText }, true);
            await Task.Delay(100);
            Check(motion.StartedCount == count && ReferenceEquals(surface.ArtworkSource, last),
                "same artwork and non-image preferences reuse decoded pixels without replay");
            surface.SetArtwork(red, ImageFit.Cover);
            await Until(() => motion.IncomingSource is not null);
            surface.SetArtwork(null, ImageFit.Cover);
            Check(surface.ArtworkSource is null && motion.RetainedImageCount == 0,
                "missing artwork revokes current, outgoing and pending image references immediately");
            await Task.Delay(300);
            Check(surface.ArtworkSource is null && motion.RetainedImageCount == 0,
                "canceled native completion cannot resurrect cleared artwork");
            surface.SetArtwork(red, ImageFit.Cover);
            WidgetPresentationSurface.SetMotionAppearance(settings with { Motion = MotionPreference.Reduced }, true);
            surface.SetArtwork(green, ImageFit.Cover);
            Check(motion.RetainedImageCount == 1 && motion.IncomingSource is null,
                "reduced motion applies new artwork without a fade");
            WidgetPresentationSurface.SetMotionAppearance(settings with { Transparency = TransparencyPreference.Reduced }, true);
            surface.SetArtwork(blue, ImageFit.Cover);
            Check(motion.RetainedImageCount == 1 && motion.IncomingSource is null,
                "reduced transparency applies artwork without intermediate layer opacity");
            WidgetPresentationSurface.SetMotionAppearance(settings with { Contrast = ContrastPreference.System }, true);
            WidgetPresentationSurface.SetSystemHighContrast(true);
            surface.SetArtwork(last, ImageFit.Cover);
            Check(motion.RetainedImageCount == 1 && motion.IncomingSource is null,
                "system high contrast suppresses decorative artwork blending");
            WidgetPresentationSurface.SetSystemHighContrast(false);
            WidgetPresentationSurface.SetMotionAppearance(settings, true);
            surface.SetArtwork(red, ImageFit.Cover);
            await Until(() => motion.IncomingSource is not null);
            surface.Width = 300;
            await Until(() => motion.IncomingSource is null);
            Check(motion.RetainedImageCount == 1 && ReferenceEquals(surface.ArtworkSource, red),
                "resizing settles to the latest artwork and retires old geometry");
            surface.SetArtwork(green, ImageFit.Cover);
            await Until(() => motion.IncomingSource is not null);
            panel.Children.Remove(surface);
            await Until(() => !surface.IsLoaded && motion.IncomingSource is null);
            Check(motion.RetainedImageCount == 1 && motion.IncomingSource is null,
                "unloading cancels motion while retaining only the logical current artwork");
            await surface.DisposeAsync();
            Check(motion.RetainedImageCount == 0, "surface teardown releases all decoded image references and compositor owners");
        }
        finally
        {
            panel.Children.Remove(surface); await surface.DisposeAsync();
            WidgetPresentationSurface.SetSystemHighContrast(false);
            WidgetPresentationSurface.SetMotionAppearance(AppearanceSettings.Default, true);
        }

        static WriteableBitmap Pixels(byte red, byte green, byte blue)
        {
            var bitmap = new WriteableBitmap(2, 2);
            using var pixels = bitmap.PixelBuffer.AsStream();
            for (var index = 0; index < 4; ++index) pixels.Write([blue, green, red, 255]);
            bitmap.Invalidate();
            return bitmap;
        }
    }
}
