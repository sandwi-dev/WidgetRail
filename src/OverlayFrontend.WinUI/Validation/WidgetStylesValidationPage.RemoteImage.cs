using System.Text.Json;
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
    private async Task RemoteImageAsync(string output)
    {
        // Optional native/network qualification: public project artwork only,
        // never an authenticated attachment or a local user capture.
        const string url = "https://raw.githubusercontent.com/github/explore/main/topics/csharp/csharp.png";
        Exception? failure = null;
        var root = new Grid { Width = 800, Height = 600 };
        var media = new MediaPlayerView { Width = 640, Height = 420 };
        root.Children.Add(media); PresentationLayers.Attach(root);
        var fullscreen = new MediaFullscreenLayer(); fullscreen.Attach(root);
        host.Children.Clear(); host.Children.Add(status); host.Children.Add(root);
        App.Window.AppWindow.ResizeClient(new(1100, 900));
        var resident = MediaPlayerView.ResidentPlayers;
        var source = new MediaPlayerDefinition(MediaPlayerSource.WebImage(url), new());
        try
        {
            media.SetActive(true, true);
            media.Apply(_ => Task.FromResult(new HostMediaPlayerSource(source, url, IsImage: true)), source, () => true, "remote-image");
            await Wait(() => media.CanControl && media.IsImage);
            var viewport = Find<ScrollViewer>("MediaPlayer.Image.Viewport", root)!;
            var image = ((Grid)viewport.Content).Children.OfType<Image>().Single();
            Check(image.Source is BitmapImage { PixelWidth: > 0, PixelHeight: > 0 }, "Real HTTPS artwork decodes before entering the shared image viewer");
            Check(MediaPlayerView.ResidentPlayers == resident && !media.IsVideo, "Remote images do not consume a native video-player slot");
            Check(viewport.Focus(FocusState.Keyboard), "Remote image viewport accepts native controller focus");
            media.Handle(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.RightTrigger, ControllerEventPhase.Released);
            await Wait(() => viewport.ZoomFactor > 1);
            Check(true, "Controller trigger zoom uses the existing image viewport");
            media.Handle(ControllerButton.LeftBumper, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.LeftBumper, ControllerEventPhase.Released);
            await Wait(() => Math.Abs(viewport.ZoomFactor - 1) < .01);
            Check(true, "Fit resets remote-image zoom");
            media.Handle(ControllerButton.RightStick, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.RightStick, ControllerEventPhase.Released);
            await Wait(() => media.IsExpanded && !media.IsMovingPresentation);
            Check(true, "Remote images use the existing fullscreen presentation");
            media.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.B, ControllerEventPhase.Released);
            await Wait(() => !media.IsExpanded && !media.IsMovingPresentation);
            Check(media.CanControl, "B returns to the same decoded image");

            var delayed = new TaskCompletionSource<HostMediaPlayerSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            var old = source with { Revision = 2 };
            media.Apply(_ => delayed.Task, old, () => true, "remote-image");
            var latest = source with { Revision = 3 };
            media.Apply(_ => Task.FromResult(new HostMediaPlayerSource(latest, url, IsImage: true)), latest, () => true, "remote-image");
            await Wait(() => media.CanControl && media.Definition == latest);
            var pixels = image.Source;
            delayed.SetResult(new(old, url, IsImage: true));
            await Task.Delay(100);
            Check(media.Definition == latest && ReferenceEquals(image.Source, pixels), "Late resolution cannot overwrite the replacement image");

            var rejected = source with { Revision = 4 };
            media.Apply(_ => Task.FromResult(new HostMediaPlayerSource(rejected, "file:///C:/unexpected.png", IsImage: true)), rejected, () => true, "remote-image");
            await Wait(() => image.Source is null && !media.CanControl);
            Check(true, "A remote image cannot be resolved to an unexpected local-file authority");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            media.Dispose(); await media.PlaybackRetirement; host.Children.Remove(root);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        }
        if (failure is not null) throw failure;
    }
}
