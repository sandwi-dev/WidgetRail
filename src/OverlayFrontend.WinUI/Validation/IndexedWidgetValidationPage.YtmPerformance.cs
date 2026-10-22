using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    // Diagnostic comparison of the shipping widget before/after the loaded-list
    // migration. Provider data is synthetic; transport and native rendering are real.
    private async Task ValidateYtmPerformanceAsync(string output)
    {
        var liveSession = session ?? throw new InvalidOperationException("A live presentation session is required.");
        var samples = new List<object>();
        var passes = new List<object>();
        var frames = new List<double>();
        var reopen = new List<double>();
        long previousFrame = 0;
        var measuring = false;
        var maxRealized = 0;
        Exception? error = null;
        ScrollViewer Scroll() => Descendants(View()!).OfType<ScrollViewer>().First();
        async Task Until(Func<bool> ready)
        {
            var timeout = Stopwatch.StartNew();
            while (!ready())
            {
                if (failure is not null) throw new InvalidOperationException(failure);
                if (timeout.Elapsed.TotalSeconds > 15) throw new TimeoutException("Performance fixture did not become ready.");
                await Task.Delay(20, lifetime.Token);
            }
        }
        void Sample(string phase)
        {
            using var process = Process.GetCurrentProcess();
            var realized = Descendants(presenter).Count(node => node is ListViewItem or GridViewItem);
            maxRealized = Math.Max(maxRealized, realized);
            samples.Add(new { phase, realized, offset = View() is null ? 0 : Scroll().VerticalOffset,
                privateBytes = process.PrivateMemorySize64, workingSet = process.WorkingSet64,
                managedBytes = GC.GetTotalMemory(false), allocatedBytes = GC.GetTotalAllocatedBytes() });
        }
        void Rendered(object? sender, object args)
        {
            if (!measuring) return;
            var now = Stopwatch.GetTimestamp();
            if (previousFrame != 0) frames.Add(Stopwatch.GetElapsedTime(previousFrame, now).TotalMilliseconds);
            previousFrame = now;
        }
        try
        {
            await Until(() => View()?.Items.Count == 10000 && Descendants(presenter).OfType<TextBlock>().Any(text => text.Text == "Artist 0"));
            await Task.Delay(500, lifetime.Token);
            Sample("ready");
            CompositionTarget.Rendering += Rendered;
            for (var pass = 0; pass < 3; ++pass)
            {
                frames.Clear(); previousFrame = 0; measuring = true;
                var watch = Stopwatch.StartNew();
                var nextSample = 0d;
                while (watch.Elapsed.TotalSeconds < 6)
                {
                    var fraction = watch.Elapsed.TotalSeconds / 6;
                    var scroll = Scroll();
                    scroll.ChangeView(null, scroll.ScrollableHeight * (pass % 2 == 0 ? fraction : 1 - fraction), null, true);
                    if (watch.Elapsed.TotalSeconds >= nextSample) { Sample("scroll-" + pass); nextSample += 1; }
                    await Task.Delay(16, lifetime.Token);
                }
                measuring = false;
                var sorted = frames.Order().ToArray();
                if (sorted.Length == 0) throw new InvalidOperationException("No native composition frames observed.");
                passes.Add(new { pass, frameCount = sorted.Length, p50Ms = sorted[sorted.Length / 2],
                    p95Ms = sorted[(int)(sorted.Length * .95)], maxMs = sorted[^1], over50Ms = sorted.Count(value => value > 50) });
                await Task.Delay(500, lifetime.Token);
                Sample("settled-" + pass);
            }
            for (var attempt = 0; attempt < 3; ++attempt)
            {
                await presenter.SetPresentationActiveAsync(false);
                await liveSession.SetLifecycleAsync(liveSession.GetTarget("indexed-owned"), WidgetLifecycleState.Background, lifetime.Token);
                await Task.Delay(250, lifetime.Token);
                var watch = Stopwatch.StartNew();
                await presenter.SetPresentationActiveAsync(true);
                await liveSession.EstablishPresentationAsync(liveSession.GetTarget("indexed-owned"), WidgetLifecycleState.Interactive, lifetime.Token);
                await Until(() => View()?.Items.Count == 10000 && Descendants(View()!).OfType<TextBlock>().Any(text => text.Text.StartsWith("Artist ", StringComparison.Ordinal)));
                reopen.Add(watch.Elapsed.TotalMilliseconds);
            }
            Sample("reopened");
            // Explicit diagnostic full GC happens only after timing, in this isolated fixture.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Sample("diagnostic-retained");
            if (maxRealized >= 100) throw new InvalidOperationException("Native realization exceeded the fixture bound.");
        }
        catch (Exception exception) { error = exception; }
        finally { CompositionTarget.Rendering -= Rendered; }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = error is null,
            scope = "Synthetic 10,000-item shipping YTM Home; real worker/bridge/native renderer; no artwork/network/playback. Reopen measures retained-view readiness, not cold process startup.",
            maxRealized, samples, passes, reopenMilliseconds = reopen, error = error?.ToString() }));
        if (error is not null) throw error;
    }
}
