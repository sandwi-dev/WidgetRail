using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AnimatedImageViewerAsync()
    {
        var media = new MediaPlayerView { Width = 380, Height = 280 };
        host.Children.Add(media);
        var staticBytes = await ArtworkPngAsync(160, 90);
        var reads = 0;
        media.ValidationImagePayload = uri => { reads++; return new(uri.AbsolutePath.EndsWith(".gif", StringComparison.Ordinal) ? TwoFrameGif() : staticBytes); };
        var definition = new MediaPlayerDefinition(MediaPlayerSource.WebImage("https://example.invalid/generated.gif"), new(AutoPlay: true, Muted: true));
        try
        {
            media.SetAnimationsAllowed(false); media.SetActive(true, true);
            Publish();
            await Wait(() => media.CanControl && media.HasImageAnimation);
            var viewport = Find<ScrollViewer>("MediaPlayer.Image.Viewport", media)!;
            var image = ((Grid)viewport.Content).Children.OfType<Image>().Single();
            var bitmap = (BitmapImage)image.Source;
            var play = Find<Button>("MediaPlayer.Image.Play", media)!;
            Check(!bitmap.IsPlaying && play.Visibility == Visibility.Visible, "Reduced motion keeps the decoded GIF paused with an explicit Play control");
            Check(play.Focus(FocusState.Keyboard), "GIF Play is a reachable native control");
            media.Handle(ControllerButton.A, ControllerEventPhase.Pressed); media.Handle(ControllerButton.A, ControllerEventPhase.Released);
            await Wait(() => bitmap.IsPlaying);
            var colors = new HashSet<string>();
            for (var i = 0; i < 10; i++) { await Task.Delay(110); colors.Add(await CenterColor(image)); }
            Check(colors.Contains("0000FFFF") && colors.Contains("FF0000FF"), "Explicit Play displays both generated GIF frames in the shared image viewer");
            media.Handle(ControllerButton.X, ControllerEventPhase.Pressed); media.Handle(ControllerButton.X, ControllerEventPhase.Released);
            Check(!bitmap.IsPlaying, "X pauses an animated image");
            media.SetAnimationsAllowed(true);
            Check(!bitmap.IsPlaying, "Changing motion policy does not override the user's paused state");
            media.Handle(ControllerButton.X, ControllerEventPhase.Pressed); media.Handle(ControllerButton.X, ControllerEventPhase.Released);
            await Wait(() => bitmap.IsPlaying);
            media.SetActive(false, false);
            Check(!bitmap.IsPlaying, "Hidden image viewers stop animation");
            media.SetActive(true, true);
            await Wait(() => bitmap.IsPlaying);
            Check(reads == 1, "Image viewer resume reuses the same decoded GIF");
            definition = new(MediaPlayerSource.WebImage("https://example.invalid/generated.png"), new(AutoPlay: true));
            Publish();
            await Wait(() => media.CanControl && !media.HasImageAnimation && !ReferenceEquals(image.Source, bitmap));
            Check(!bitmap.IsPlaying && play.Visibility == Visibility.Collapsed, "Static replacement retires GIF playback and removes its animation control");
        }
        finally { media.Dispose(); host.Children.Remove(media); }

        void Publish() => media.Apply(_ => Task.FromResult(new HostMediaPlayerSource(definition, definition.Source.Location!, IsImage: true)),
            definition, () => true, "animated-image-viewer");
    }
}
