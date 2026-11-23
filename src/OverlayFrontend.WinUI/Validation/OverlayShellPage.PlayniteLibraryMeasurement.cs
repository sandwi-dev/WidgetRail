using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Validation-only. Exercise the real shell, installed widget, local provider,
    // and artwork. The only dispatched widget action opens its Library tab.
    internal void EnablePlayniteLibraryMeasurement(string resultPath)
    {
        Loaded += Run;
        async void Run(object sender, RoutedEventArgs args)
        {
            Loaded -= Run;
            const string widget = "widgetrail.samples.playnite-library";
            const string browse = "playnite-library.browse.open";
            try
            {
                if (startup is not null) await startup;
                if (options.InitialWidgetId != widget || owner is null) throw new InvalidOperationException("Select Playnite Library for this measurement.");
                await SelectAsync(widget, true);
                await Wait(() => MainFocusEnabled && surface?.CurrentBinding is not null);
                var frame = surface!.CurrentBinding!.Frame;
                var tab = Nodes(frame.Snapshot.Root).FirstOrDefault(n => n.ActionId == browse);
                if (tab is not null)
                    await owner.Session.SendActionAsync(frame.Authority, new(browse, tab.Id, InputScopeId: frame.Snapshot.ActiveInputScopeId), lifetime.Token);
                GridView? grid = null;
                await Wait(() => (grid = ChoiceDescendants(surface).OfType<WidgetIndexedCollectionView>()
                    .FirstOrDefault(v => AutomationProperties.GetAutomationId(v) == "Widget.playnite-library.library.scroll")?.NativeView as GridView)?.Items.Count > 0);
                var viewport = ChoiceDescendants(grid!).OfType<ScrollViewer>().First();
                await Wait(() => grid!.ContainerFromIndex(0) is Control { IsLoaded: true, IsEnabled: true });
                if (!((Control)grid!.ContainerFromIndex(0)).Focus(FocusState.Keyboard)) throw new InvalidOperationException("Library row did not receive focus.");
                await Wait(() => viewport.ScrollableHeight > viewport.ViewportHeight && foreground);
                await File.WriteAllTextAsync(resultPath + ".ready", Environment.ProcessId.ToString());
                // Separate setup from allocation profiling. A controller observer
                // explicitly releases this gate once attached to this exact PID.
                await Wait(() => File.Exists(resultPath + ".start"));
                double min = viewport.VerticalOffset, max = min;
                var heights = new double[4];
                var bytes = GC.GetTotalAllocatedBytes();
                var generations = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                var elapsed = Stopwatch.StartNew();
                for (var leg = 0; leg < heights.Length; leg++)
                {
                    var sample = Sample();
                    sample.State.RightThumbY = leg % 2 == 0 ? (short)-28000 : (short)28000;
                    while (elapsed.Elapsed.TotalSeconds < (leg + 1) * 4)
                    {
                        if (!foreground || activeWidget != widget || !grid.IsLoaded) throw new InvalidOperationException("Library lost input ownership.");
                        Receive(sample);
                        await Task.Delay(15, lifetime.Token);
                        min = Math.Min(min, viewport.VerticalOffset);
                        max = Math.Max(max, viewport.VerticalOffset);
                    }
                    heights[leg] = viewport.VerticalOffset;
                }
                Receive(Sample());
                var allocation = GC.GetTotalAllocatedBytes() - bytes;
                var collections = generations.Select((n, i) => GC.CollectionCount(i) - n).ToArray();
                if (max - min < viewport.ViewportHeight || heights[0] <= heights[1] || heights[2] <= heights[3])
                    throw new InvalidOperationException("Library did not traverse both directions.");
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = true, items = grid.Items.Count,
                    width = surface.ActualWidth, height = surface.ActualHeight, extent = viewport.ScrollableHeight,
                    min, max, heights, allocation, collections, elapsedMs = elapsed.ElapsedMilliseconds }));
            }
            catch (Exception error) { await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = false, error = error.Message })); }
        }

        async Task Wait(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition())
            {
                if (timer.Elapsed.TotalSeconds > 60) throw new TimeoutException("Timed out waiting for the Playnite measurement.");
                await Task.Delay(100, lifetime.Token);
            }
        }
        static IEnumerable<ViewNode> Nodes(ViewNode n) => new[] { n }.Concat(n.Children.SelectMany(Nodes));
    }
}
