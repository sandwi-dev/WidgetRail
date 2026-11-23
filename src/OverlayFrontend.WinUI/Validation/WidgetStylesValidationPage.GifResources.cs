using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    // Generated local bytes only. The external runner independently enforces a
    // process-memory/time ceiling; observations are not a universal codec bound.
    private async Task GifResourcesAsync(string output, string input, int terminalMilliseconds = 0)
    {
        var samples = new List<GifResourceSample>();
        var bytes = await File.ReadAllBytesAsync(input);
        var weakImages = new List<WeakReference>();
        var weakViews = new List<WeakReference>();
        var visibilityObservations = new List<object>();
        object? endurance = null;
        var cleanupErrors = new List<string>();
        Exception? failure = null;
        var clock = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        var lastBeat = clock.Elapsed.TotalMilliseconds;
        var longestBeat = 0d;
        var longestPlaybackBeat = 0d;
        var samplingPlayback = false;
        var beats = 0;
        heartbeat.Tick += (_, _) =>
        {
            var now = clock.Elapsed.TotalMilliseconds;
            longestBeat = Math.Max(longestBeat, now - lastBeat);
            if (samplingPlayback) longestPlaybackBeat = Math.Max(longestPlaybackBeat, now - lastBeat);
            lastBeat = now; beats++;
        };
        try
        {
            await CollectAsync();
            Sample("baseline");
            heartbeat.Start();
            for (var cycle = 0; cycle < 6; cycle++)
            {
                await ExerciseAsync(cycle);
                await CollectAsync();
                Sample($"released-{cycle}");
            }
            Check(weakImages.All(image => !image.IsAlive), "All six retired GIF projections become collectible");
            Check(weakViews.All(view => !view.IsAlive), "Viewport observation does not retain retired artwork elements");
            Check(beats > 0, "The dispatcher services input-time work while GIFs play");
        }
        catch (Exception error) { failure ??= error; }
        finally
        {
            heartbeat.Stop();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
            {
                passed = failure is null, checks, encodedBytes = bytes.Length,
                samples, visibilityObservations, endurance, cleanupErrors, heartbeatCount = beats, longestHeartbeatMilliseconds = longestBeat,
                longestPlaybackHeartbeatMilliseconds = longestPlaybackBeat,
                collectedImages = weakImages.Count(image => !image.IsAlive),
                collectedViews = weakViews.Count(view => !view.IsAlive),
                error = failure?.ToString(),
                scope = "Generated GIF through production public decode and artwork. Private bytes/CPU are process-wide; no claim about unmeasured GPU allocations or all possible GIFs."
            }));
        }
        if (failure is not null) throw failure;

        async Task ExerciseAsync(int cycle)
        {
            var artwork = new WidgetArtworkView { Width = 256, Height = 256 };
            weakViews.Add(new(artwork));
            artwork.ApplyStyle(new(Stretch.Fill));
            // Match chat/gallery artwork: a real viewport defines visibility.
            var scroll = new ScrollViewer { Width = 280, Height = 280, Content = artwork };
            host.Children.Add(scroll);
            NativeArtworkDecode? decoded = null;
            var active = true;
            var pending = new List<Task>();
            Exception? decodeFailure = null;
            using var lifetime = new CancellationTokenSource(TimeSpan.FromMilliseconds(30_000 + (cycle == 0 ? terminalMilliseconds : 0)));
            var started = clock.Elapsed.TotalMilliseconds;
            using var demand = new NativeArtworkDemand(artwork, ImageFit.Fill, () => active,
                _ => Task.FromResult<NativeArtworkPayload?>(new(bytes, IsPublicMedia: true)),
                image => artwork.Source = image, _ => { }, pending.Add, error => decodeFailure = error, lifetime.Token);
            try
            {
                await Wait(() => decodeFailure is not null || demand.LastDecode is not null);
                if (decodeFailure is not null) throw decodeFailure;
                decoded = demand.LastDecode;
                Check(decoded?.Image.IsAnimatedBitmap == true, $"Cycle{cycle}: generated GIF decodes as animation");
                weakImages.Add(new(decoded!.Image));
                try { await Wait(() => decoded.Image.IsPlaying); }
                catch (TimeoutException)
                {
                    visibilityObservations.Add(new { cycle, kind = "production-demand-visibility-timeout", decoded.Image.IsPlaying,
                        artwork.IsLoaded, hostVisible = artwork.XamlRoot?.IsHostVisible, artwork.ActualWidth, artwork.ActualHeight });
                    throw;
                }
                Sample($"decoded-{cycle}", clock.Elapsed.TotalMilliseconds - started);
                samplingPlayback = true;
                var playbackStarted = clock.Elapsed.TotalMilliseconds;
                var colors = new HashSet<string>();
                for (var sample = 0; sample < 10; sample++)
                {
                    await Task.Delay(110);
                    colors.Add(await CenterColor(artwork));
                    Sample($"playing-{cycle}-{sample}");
                }
                Check(colors.Count > 1, $"Cycle{cycle}: actual rendered pixels advance");
                if (cycle == 0 && terminalMilliseconds > 0)
                {
                    var color = await CenterColor(artwork);
                    var sample = 0;
                    while (color != "00FF00FF" && clock.Elapsed.TotalMilliseconds - playbackStarted < terminalMilliseconds + 15_000)
                    {
                        await Task.Delay(250, lifetime.Token);
                        color = await CenterColor(artwork);
                        if (++sample % 4 == 0) Sample($"endurance-{sample / 4}");
                    }
                    var elapsed = clock.Elapsed.TotalMilliseconds - playbackStarted;
                    endurance = new { expectedTerminalMilliseconds = terminalMilliseconds, observedTerminalMilliseconds = elapsed,
                        terminalColor = color, observations = sample };
                    Check(color == "00FF00FF", "Finite animation reaches its distinct final green frame within the deadline");
                    Check(elapsed >= terminalMilliseconds * .8, "Terminal pixels follow sustained playback rather than a static last-frame projection");
                    await Task.Delay(350);
                    Check(await CenterColor(artwork) == color, "Finite animation retains its final frame without restarting");
                    Sample("terminal-frame");
                }
                samplingPlayback = false;
                active = false; demand.RefreshPlayback();
                Check(!decoded.Image.IsPlaying, $"Cycle{cycle}: presentation suspension stops animation");
                await Task.Delay(250);
                var stopped = await CenterColor(artwork);
                await Task.Delay(350);
                Check(await CenterColor(artwork) == stopped, $"Cycle{cycle}: suspended pixels remain stable");
            }
            catch (Exception error) { failure ??= error; throw; }
            finally
            {
                samplingPlayback = false;
                // Retain the originating failure even if a torn-down native
                // object also rejects cleanup. Continue retiring other owners.
                Cleanup(lifetime.Cancel); Cleanup(demand.Dispose);
                Cleanup(() => decoded?.Image.Stop());
                try { await Task.WhenAll(pending); }
                catch (Exception error) { failure ??= error; cleanupErrors.Add(error.ToString()); }
                Cleanup(() => artwork.Source = null);
                Cleanup(() => host.Children.Remove(scroll));
                Cleanup(() => scroll.Content = null);
            }
        }

        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error) { failure ??= error; cleanupErrors.Add(error.ToString()); }
        }

        void Sample(string phase, double? decodeMilliseconds = null)
        {
            process.Refresh();
            samples.Add(new(phase, clock.Elapsed.TotalMilliseconds, process.PrivateMemorySize64,
                process.WorkingSet64, process.TotalProcessorTime.TotalMilliseconds,
                GC.GetTotalMemory(false), decodeMilliseconds));
        }

        static async Task CollectAsync()
        {
            // Yield before and after finalization so XAML/COM dispatcher releases
            // can drain. This is a teardown probe, not a production GC policy.
            await Task.Delay(150);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Task.Delay(150);
        }
    }

    private sealed record GifResourceSample(string Phase, double Milliseconds, long PrivateBytes,
        long WorkingSetBytes, double CpuMilliseconds, long ManagedBytes, double? DecodeMilliseconds);
}
