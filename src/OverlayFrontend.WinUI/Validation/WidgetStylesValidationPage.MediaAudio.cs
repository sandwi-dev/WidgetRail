using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task MediaAudioAsync(string output, string clip)
    {
        var root = new Grid { Width = 680, Height = 440 };
        host.Children.Add(root);
        var saved = ShellPreferences.Empty;
        var writes = 0;
        new NativeMediaAudioPreferences(id => saved.MediaAudio.GetValueOrDefault(id), (id, value) =>
        { saved = saved.RememberAudio(id, value); writes++; }).Attach(root);
        MediaPlayerView? media = null;
        Exception? failure = null;
        var baseline = MediaPlayerView.ResidentPlayers;
        try
        {
            await Open("widget.one");
            Check(media!.PreviewMutedForValidation, "A widget's first native video starts muted despite an unmuted authored default");
            media.PreviewVolumeForValidation.Value = 35;
            Check(!media.PreviewMutedForValidation && saved.MediaAudio["widget.one"] == new MediaAudioPreference(false, .35),
                "Explicit volume input remembers volume and unmute for this widget");
            await Open("widget.two");
            Check(media!.PreviewMutedForValidation && !saved.MediaAudio.ContainsKey("widget.two"),
                "A different widget keeps its independent muted default without storing a synthetic user choice");
            await Open("widget.one");
            Check(!media!.PreviewMutedForValidation && Math.Abs(media.PreviewVolumeForValidation.Value - 35) < .01,
                "A fresh player restores the first widget's audio after the previous player and playback memory were destroyed");
            var mute = Find<Button>("MediaPlayer.Mute", root)!;
            Check(mute.Focus(FocusState.Keyboard), "Mute is reachable through the native player controls");
            media.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
            media.Handle(ControllerButton.A, ControllerEventPhase.Released);
            Check(media.PreviewMutedForValidation && saved.MediaAudio["widget.one"].Muted,
                "Controller mute updates the same per-widget preference");
            await Open("widget.one");
            Check(media!.PreviewMutedForValidation && Math.Abs(media.PreviewVolumeForValidation.Value - 35) < .01,
                "Explicit mute persists without discarding the chosen volume");
            Check(writes == 2, "Loading and replacing players do not write audio preferences");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (media is not null) { media.Dispose(); await media.PlaybackRetirement; }
            host.Children.Remove(root);
            if (failure is null) Check(MediaPlayerView.ResidentPlayers == baseline, "Audio-preference tests release every native decoder");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null, checks, writes, error = failure?.ToString() }));
        }
        if (failure is not null) throw failure;

        async Task Open(string widget)
        {
            if (media is not null) { media.Dispose(); await media.PlaybackRetirement; root.Children.Remove(media); }
            media = new MediaPlayerView { Width = 640, Height = 400, AudioPreferenceWidgetId = widget };
            root.Children.Add(media);
            var definition = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(clip), new(AutoPlay: false, Muted: false));
            media.SetActive(true, true);
            media.Apply(_ => Task.FromResult(new HostMediaPlayerSource(definition, new Uri(clip).AbsoluteUri)), definition, () => true, new object());
            await Wait(() => media.IsLoaded && media.CanControl);
            root.UpdateLayout();
        }
    }
}
