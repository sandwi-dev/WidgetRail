using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task VideoAnimationAsync(string output, string clip)
    {
        Exception? failure = null;
        var panel = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var scroll = new ScrollViewer { Width = 660, Height = 240, Content = panel };
        var players = Enumerable.Range(0, 3).Select(_ => new MediaPlayerView { Width = 210, Height = 180 }).ToArray();
        var definition = new MediaPlayerDefinition(MediaPlayerSource.WebUrl("https://example.invalid/animation.mp4"),
            new(AutoPlay: true, Loop: true, Muted: true) { IsAnimation = true }) { Presentation = MediaPlayerPresentation.Animation };
        var current = true;
        var reads = 0;
        try
        {
            foreach (var media in players) row.Children.Add(media);
            panel.Children.Add(row); panel.Children.Add(new Border { Height = 800 }); host.Children.Add(scroll);
            foreach (var media in players)
            {
                media.SetAnimationsAllowed(false); media.SetActive(true, true);
                media.Apply(_ => { reads++; return Task.FromResult(new HostMediaPlayerSource(definition, new Uri(clip).AbsoluteUri)); },
                    definition, () => current, "video-animation");
            }
            await Wait(() => players.All(media => media.IsLoaded && media.ActualWidth > 0));
            await Task.Delay(300);
            Check(reads == 0 && MediaPlayerView.ResidentAnimations == 0, "Reduced motion creates no passive video demand or decoder");
            Check(players.All(media => !media.IsTabStop && !media.IsHitTestVisible), "Inline animations do not take keyboard or pointer input from their row");
            foreach (var media in players) media.SetAnimationsAllowed(true);
            await Wait(() => players.Count(media => media.IsPlaying) == 2);
            Check(MediaPlayerView.ResidentAnimations == 2 && MediaPlayerView.ResidentPlayers == 0,
                "Visible animations use at most two separate decoders and no interactive player slots");
            scroll.ChangeView(null, 500, null, true);
            await Wait(() => MediaPlayerView.ResidentAnimations == 0);
            Check(players.All(media => !media.IsPlaying), "Scrolling animations offscreen stops playback and releases decoders");
            scroll.ChangeView(null, 0, null, true);
            await Wait(() => players.Count(media => media.IsPlaying) == 2);
            Check(MediaPlayerView.ResidentAnimations == 2, "Visible rows resume bounded animation playback");
            foreach (var media in players) media.SetActive(false, false);
            Check(MediaPlayerView.ResidentAnimations == 0, "Hiding a presentation releases passive decoders synchronously");
            foreach (var media in players) media.SetActive(true, false);
            await Wait(() => players.Count(media => media.IsPlaying) == 2);
            players[0].SetActive(false, false); players[1].SetActive(false, false);
            await Wait(() => players[2].IsPlaying);
            Check(MediaPlayerView.ResidentAnimations == 1, "A resumed overflow animation starts when another visible player releases its slot");
            current = false;
            await Wait(() => MediaPlayerView.ResidentAnimations == 0);
            Check(players.All(media => !media.IsPlaying), "Retired row authority stops every animation");
            foreach (var media in players) media.Dispose();
            host.Children.Remove(scroll);
            var viewer = new MediaPlayerView { Width = 420, Height = 280 };
            host.Children.Add(viewer);
            try
            {
                var interactive = definition with { Presentation = MediaPlayerPresentation.Interactive };
                viewer.SetAnimationsAllowed(false); viewer.SetActive(true, true);
                viewer.Apply(_ => Task.FromResult(new HostMediaPlayerSource(interactive, new Uri(clip).AbsoluteUri)), interactive, () => true);
                await Wait(() => viewer.CanControl); await Task.Delay(350);
                Check(!viewer.IsPlaying, "Larger preview respects reduced-motion autoplay");
                viewer.Toggle(); await Wait(() => viewer.IsPlaying);
                Check(viewer.IsPlaying, "Explicit Play overrides reduced motion in the interactive preview");
                viewer.Toggle(); viewer.SetAnimationsAllowed(true); await Task.Delay(300);
                Check(!viewer.IsPlaying, "Motion preference changes preserve explicit Pause");
                viewer.SetActive(false, false);
                viewer.SetActive(true, true);
                viewer.Apply(_ => Task.FromResult(new HostMediaPlayerSource(interactive, new Uri(clip).AbsoluteUri)), interactive, () => true);
                await Wait(() => viewer.CanControl); await Task.Delay(300);
                Check(!viewer.IsPlaying, "Explicit Pause survives same-source preview hide and reopen");
            }
            finally { viewer.Dispose(); host.Children.Remove(viewer); }
            if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--animation-pipe") is { } pipe)
                await IndexedAnimationAsync(pipe, clip);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            foreach (var media in players) media.Dispose();
            host.Children.Remove(scroll);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null, checks, reads,
                residentAnimations = MediaPlayerView.ResidentAnimations, residentPlayers = MediaPlayerView.ResidentPlayers, error = failure?.ToString() }));
        }
        if (failure is not null) throw failure;
    }
}
