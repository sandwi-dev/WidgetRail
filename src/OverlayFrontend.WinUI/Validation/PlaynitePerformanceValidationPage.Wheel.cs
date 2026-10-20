using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PlaynitePerformanceValidationPage
{
    private async Task CompareWheelAsync(string mode)
    {
        if (mode is not ("stick" or "wheel-input"))
            throw new ArgumentException("Unknown comparison mode.");
        var view = View()!;
        var scroll = Descendants(view).OfType<ScrollViewer>().First();
        view.ScrollIntoView(view.Items[0], ScrollIntoViewAlignment.Leading);
        await Until(() => view.ContainerFromIndex(0) is Control { IsLoaded: true, IsEnabled: true });
        ((Control)view.ContainerFromIndex(0)).Focus(FocusState.Keyboard);
        App.Window.Activate();
        await Task.Delay(500, lifetime.Token);
        using var input = new NativeWheelProbe(scroll);
        var positions = new List<object>();
        var wheelEvents = 0;
        var wheelDelta = 0L;
        scroll.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Wheel), true);
        double pixelsPerNotch = 48;
        try
        {
            if (mode.StartsWith("wheel", StringComparison.Ordinal))
            {
                var before = scroll.VerticalOffset;
                input.Send(-120);
                await Task.Delay(1000, lifetime.Token);
                pixelsPerNotch = scroll.VerticalOffset - before;
                await File.WriteAllTextAsync(Path.Combine(output, "wheel-admission.json"),
                    JsonSerializer.Serialize(new { mode, pixelsPerNotch, wheelEvents, wheelDelta, position = input.Position }), lifetime.Token);
                if (pixelsPerNotch < 1) throw new InvalidOperationException("Targeted wheel input did not scroll the native viewport.");
            }
            scroll.ChangeView(null, 0, null, true);
            await Until(() => scroll.VerticalOffset < 1);
            await Task.Delay(500, lifetime.Token);
            Sample("comparison-ready");
            await File.WriteAllTextAsync(Path.Combine(output, "comparison-ready.json"),
                JsonSerializer.Serialize(new { mode, pid = Environment.ProcessId, position = input.Position,
                    viewport = scroll.TransformToVisual(null).TransformBounds(new(0, 0, scroll.ActualWidth, scroll.ActualHeight)),
                    scale = XamlRoot.RasterizationScale, windowWidth = App.Window.AppWindow.Size.Width / XamlRoot.RasterizationScale,
                    pixelsPerNotch }), lifetime.Token);
            if (Environment.GetCommandLineArgs().Contains("--playnite-comparison-capture"))
                await Until(() => File.Exists(Path.Combine(output, "capture-started")));
            var timer = Stopwatch.StartNew();
            var sampler = new Shell.RightStickScroll();
            var fractionalWheel = 0d;
            frames.Clear(); ticks.Clear(); lastFrame = 0; scrolling = true;
            CompositionTarget.Rendering += Rendering;
            CompositionTarget.Rendering += Observe;
            scroll.ViewChanged += Changed;
            try
            {
                phase = "comparison";
                while (timer.Elapsed.TotalSeconds < 8)
                {
                    var delta = sampler.Sample(0, short.MinValue, Environment.TickCount64).Y;
                    if (mode == "stick") presenter.ScrollBy(0, delta);
                    else
                    {
                        fractionalWheel += -delta * 120 / pixelsPerNotch;
                        var units = (int)fractionalWheel;
                        fractionalWheel -= units;
                        if (units != 0) input.Send(units);
                    }
                    if (failure is not null) throw failure;
                    await Task.Delay(15, lifetime.Token);
                }
                scrolling = false;
                Sample("comparison-end");
                await Task.Delay(1000, lifetime.Token);
                Sample("comparison-settled");
            }
            finally
            {
                CompositionTarget.Rendering -= Rendering;
                CompositionTarget.Rendering -= Observe;
                scroll.ViewChanged -= Changed;
            }
            await File.WriteAllTextAsync(Path.Combine(output, "wheel-comparison.json"),
                JsonSerializer.Serialize(new { mode, pixelsPerNotch, wheelEvents, wheelDelta, frames, positions }), lifetime.Token);
            if (mode == "wheel-input")
            {
                // Outside the timed comparison: wheel ownership must retire
                // controller settlement without stopping the newly admitted wheel.
                var before = scroll.VerticalOffset;
                var focused = FocusedIndex(view);
                presenter.ScrollBy(0, 400);
                await Until(() => scroll.VerticalOffset > before + 5);
                var admitted = wheelEvents;
                input.Send(-120);
                await Until(() => wheelEvents > admitted);
                if (!presenter.SettleScrollFocus() || FocusedIndex(view) != focused)
                    throw new InvalidOperationException("Wheel input inherited controller focus settlement.");
                await Task.Delay(800, lifetime.Token);
                if (scroll.VerticalOffset <= before + 10)
                    throw new InvalidOperationException("Retiring controller ownership cancelled native wheel motion.");
                navigationChecks.Add("Native wheel takes over controller motion without controller focus settlement");
            }

            void Observe(object? sender, object args) => Position("render");
            void Changed(object? sender, ScrollViewerViewChangedEventArgs args) => Position(args.IsIntermediate ? "intermediate" : "final");
            void Position(string reason) => positions.Add(new { seconds = timer.Elapsed.TotalSeconds, reason,
                offset = scroll.VerticalOffset, extent = scroll.ScrollableHeight, focused = FocusedIndex(view) });
        }
        finally
        {
            scrolling = false;
            scroll.RemoveHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Wheel));
        }
        void Wheel(object sender, PointerRoutedEventArgs args)
        {
            ++wheelEvents;
            wheelDelta += args.GetCurrentPoint(scroll).Properties.MouseWheelDelta;
        }
    }
}
