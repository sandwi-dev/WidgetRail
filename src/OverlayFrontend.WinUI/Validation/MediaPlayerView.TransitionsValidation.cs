using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal async Task ValidateNativeTransitionsAsync(string mp4, string result)
    {
        var checks = new List<string>();
        var frames = new List<object>();
        var clock = Stopwatch.StartNew();
        var phase = "loading";
        var unloads = 0;
        var strict = Environment.GetCommandLineArgs().Contains("--assert-resident-video");
        DependencyObject? retainedParent = null;
        double renderWidth = 0, renderHeight = 0;
        var renderChanged = false;
        video.Unloaded += OnUnload;
        CompositionTarget.Rendering += OnFrame;
        try
        {
            SetActive(true, true);
            var definition = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(mp4), new(Muted: true, Loop: true));
            Apply(_ => Task.FromResult(new HostMediaPlayerSource(definition, new Uri(mp4).AbsoluteUri)), definition, () => true);
            await Until(() => player?.PlaybackSession.NaturalVideoWidth > 0);
            await Task.Delay(500);
            var parent = video.Parent;
            var width = video.ActualWidth; var height = video.ActualHeight;
            var host = MediaFullscreenLayer.Find(this)!;
            var origin = host.BoundsOf(video)!.Value;
            var retained = player;
            unloads = 0; // Initial placement is complete; measure fullscreen handoffs only.
            retainedParent = parent; renderWidth = width; renderHeight = height;
            speed.Focus(FocusState.Keyboard);
            await File.WriteAllTextAsync(result + ".ready", "ready");
            await Until(() => File.Exists(result + ".go"));
            for (var cycle = 0; cycle < 4; cycle++)
            {
                var playing = cycle % 2 == 1;
                if (playing) player!.Play(); else player!.Pause();
                await Until(() => IsPlaying == playing);
                phase = $"cycle-{cycle}-inline"; await Task.Delay(600);
                phase = $"cycle-{cycle}-enter";
                var completion = ToggleExpandedAsync();
                await Until(() => IsExpanded && !host.IsTransitioning && !changingPlacement);
                phase = $"cycle-{cycle}-expanded"; await Task.Delay(600);
                phase = $"cycle-{cycle}-exit"; host.Close(); await completion;
                phase = $"cycle-{cycle}-returned"; await Task.Delay(600);
                if (!ReferenceEquals(player, retained) || IsPlaying != playing || !ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), speed))
                    throw new InvalidOperationException("Playback or focused control changed during transition");
                checks.Add($"Cycle {cycle}: playback and focus lifetime settle inline");
                if (strict && (unloads != 0 || renderChanged))
                    throw new InvalidOperationException("Native video attachment or render size changed");
                var returned = host.BoundsOf(video)!.Value;
                if (Math.Abs(returned.X - origin.X) > 1 || Math.Abs(returned.Y - origin.Y) > 1 || Math.Abs(returned.Width - origin.Width) > 1 || Math.Abs(returned.Height - origin.Height) > 1)
                    throw new InvalidOperationException("Returned native video does not match its original bounds");
                checks.Add($"Cycle {cycle}: exact inline bounds restored");
            }
            if (strict) checks.Add("Native video retains one attachment and render size across every sampled frame");
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = true, checks, unloads, frames }));
        }
        catch (Exception error) { await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = false, checks, unloads, frames, error = error.ToString() })); }
        finally { CompositionTarget.Rendering -= OnFrame; video.Unloaded -= OnUnload; }

        void OnUnload(object sender, RoutedEventArgs args) => unloads++;
        void OnFrame(object? sender, object args)
        {
            if (!video.IsLoaded || XamlRoot is null) return;
            if (retainedParent is not null && (!ReferenceEquals(video.Parent, retainedParent) || Math.Abs(video.ActualWidth - renderWidth) > .1 || Math.Abs(video.ActualHeight - renderHeight) > .1))
                renderChanged = true;
            var bounds = video.TransformToVisual((UIElement)XamlRoot.Content).TransformBounds(new(0, 0, video.ActualWidth, video.ActualHeight));
            frames.Add(new { ms = clock.ElapsedMilliseconds, phase, width = video.ActualWidth, height = video.ActualHeight,
                x = bounds.X, y = bounds.Y, renderedWidth = bounds.Width, renderedHeight = bounds.Height, playing = IsPlaying });
        }
        static async Task Until(Func<bool> condition)
        {
            var until = Environment.TickCount64 + 20000;
            while (!condition()) { if (Environment.TickCount64 >= until) throw new TimeoutException("Native transition probe timed out"); await Task.Delay(20); }
        }
    }
}
