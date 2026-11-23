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
    private async Task NestedScopeArtworkMotionAsync()
    {
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
        var state = new Dictionary<string, WidgetRail.WidgetBridge.BridgeComputedStyleValue>
        {
            ["artwork-motion"] = new() { Kind = WidgetRail.WidgetStyling.WrssValueKind.Keyword, Text = "pan-zoom" },
            ["artwork-transition"] = new() { Kind = WidgetRail.WidgetStyling.WrssValueKind.Keyword, Text = "directional-fade" }
        };
        ViewNode Button(string id) => new() { Id = id, Kind = ViewNodeKind.Button, Text = id, ActionId = id, FocusBackgroundArtworkHandle = id };
        Present(new ViewSnapshot
        {
            WidgetInstanceId = "surface.instance", Sequence = ++sequence, ActiveInputScopeId = "navigation", InitialFocusId = "a",
            Root = new() { Id = "background", Kind = ViewNodeKind.BackgroundSurface, UsesFocusedDescendantArtwork = true,
                Children = (ViewNode[])[new() { Id = "navigation", InputScopeId = "navigation", Kind = ViewNodeKind.Row,
                    Children = (ViewNode[])[Button("a"), Button("b")] }] }
        }, styles: new Dictionary<string, WidgetRail.WidgetBridge.BridgeNodeRenderStyles>
        {
            ["background"] = new() { Base = state, Focused = state, Pressed = state }
        });
        await Focus("a");
        var motion = BackgroundSurface()!.ArtworkMotion;
        await Until(() => motion.HasDrift);
        presenter.MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Right);
        await Until(() => motion.LastTravel.X > 0);
        Check(true, "a shared wallpaper outside the active navigation scope follows the actual directional focus move");
    }

    private async Task ArtworkMotionAsync()
    {
        var settings = AppearanceSettings.Default with { Motion = MotionPreference.Full, PageAnimation = WidgetPageAnimation.None,
            WidgetAnimationSpeed = 2, Contrast = ContrastPreference.Standard };
        WidgetPresentationSurface.SetMotionAppearance(settings, true);
        var panel = (StackPanel)Content;
        var surface = new WidgetPresentationSurface(ViewNodeKind.BackgroundSurface) { Width = 320, Height = 140 };
        surface.ConfigureEntrance(true);
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
            await Until(() => surface.EntranceForValidation?.Starts == 1);
            var entrance = surface.EntranceForValidation!.Playback!;
            surface.ConfigureEntrance(true);
            Check(ReferenceEquals(surface.EntranceForValidation.Playback, entrance), "Refreshing a mounted surface does not restart its entrance");
            await entrance;
            Check(surface.EntranceForValidation.Starts == 1, "A surface entrance is independent of page navigation animation");
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
            WidgetPresentationSurface.SetMotionAppearance(settings with { WidgetAnimationSpeed = .5 }, true);
            surface.SetArtwork(red, ImageFit.Cover);
            await Until(() => motion.IncomingSource is not null);
            var resizingPlayback = motion.Playback!;
            var resizingStarts = motion.StartedCount;
            surface.SetArtwork(green, ImageFit.Cover);
            surface.Width = 300;
            surface.Height = 180;
            surface.UpdateLayout();
            Check(surface.ArtworkMotion.View.ActualWidth == 300 && surface.ArtworkMotion.View.ActualHeight == 180 &&
                ReferenceEquals(motion.Playback, resizingPlayback) && !resizingPlayback.IsCompleted &&
                motion.StartedCount == resizingStarts && ReferenceEquals(motion.IncomingSource, red) &&
                ReferenceEquals(motion.OutgoingSource, last),
                "native resize rearranges background layers without snapping or restarting their active blend");
            Check(ReferenceEquals(surface.ArtworkSource, green) && motion.RetainedImageCount == 3,
                "resize preserves the latest pending artwork without prematurely painting it");
            Check(await resizingPlayback == WidgetRail.OverlayFrontend.WinUI.Motion.WidgetMotionOutcome.Completed,
                "resized artwork finishes its original compositor batch normally");
            await Until(() => motion.StartedCount == resizingStarts + 1 && motion.IncomingSource is null);
            Check(motion.RetainedImageCount == 1 && ReferenceEquals(surface.ArtworkSource, green) && motion.Failure is null,
                "pending artwork blends after resize and releases both older decoded images");
            WidgetPresentationSurface.SetMotionAppearance(settings, true);
            motion.Configure(WidgetRail.OverlayFrontend.WinUI.Motion.WallpaperMotionStyle.Default with
            {
                Transition = "directional-fade", FadeMs = 300, PanZoom = true
            });
            motion.SetSource(last, Microsoft.UI.Xaml.Media.Stretch.UniformToFill, Microsoft.UI.Xaml.Input.FocusNavigationDirection.Right, "game-a");
            await Until(() => motion.IncomingSource == last);
            Check(motion.LastTravel.X > 0 && motion.LastTravel.Y == 0 && motion.HasDrift,
                "rightward navigation starts the incoming artwork on the right with independent camera movement");
            await Until(() => motion.IncomingSource is null);
            var priorBlends = motion.StartedCount;
            motion.SetSource(blue, Microsoft.UI.Xaml.Media.Stretch.UniformToFill, Microsoft.UI.Xaml.Input.FocusNavigationDirection.Left, "game-a");
            await Task.Delay(250);
            Check(motion.StartedCount == priorBlends && motion.HasDrift && motion.Source == blue,
                "refreshing the decoded pixels of the same artwork retains the current motion");
            motion.SetSource(red, Microsoft.UI.Xaml.Media.Stretch.UniformToFill, Microsoft.UI.Xaml.Input.FocusNavigationDirection.Left, "game-b");
            await Until(() => motion.IncomingSource == red);
            Check(motion.LastTravel.X < 0, "return navigation mirrors the incoming artwork direction");
            WidgetPresentationSurface.SetMotionAppearance(settings with { Motion = MotionPreference.Reduced }, true);
            Check(!motion.HasDrift && motion.IncomingSource is null, "reducing motion during a directional blend retires both effects");
            motion.Configure(WidgetRail.OverlayFrontend.WinUI.Motion.WallpaperMotionStyle.Default);
            WidgetPresentationSurface.SetMotionAppearance(settings, true);
            surface.SetArtwork(blue, ImageFit.Cover);
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
