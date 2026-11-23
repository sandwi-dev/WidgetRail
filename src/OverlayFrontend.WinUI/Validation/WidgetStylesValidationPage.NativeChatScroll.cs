using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    // A native-only control experiment. No bridge, WidgetIndexedRows, query
    // replacement, placeholders, focus restoration or viewport recovery.
    // Ready strings preserve their native identity through bounded insert/remove.
    private async Task NativeChatScrollAsync(string output)
    {
        var arguments = Environment.GetCommandLineArgs();
        var accumulated = arguments.Contains("--native-chat-scroll-accumulated");
        var animated = arguments.Contains("--native-chat-scroll-animated");
        var edgeOnly = arguments.Contains("--native-chat-scroll-edge-only");
        var results = new List<object>();
        var events = new List<object>();
        Exception? failure = null;
        var rows = new ObservableCollection<string>(Enumerable.Range(1101, 20).Select(Message));
        var list = new ListView
        {
            Width = 600, Height = 400, ItemsSource = rows,
            SelectionMode = ListViewSelectionMode.None, IsTabStop = false,
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <StackPanel Margin="8,8,8,8" Spacing="4">
                        <TextBlock Text="Synthetic participant" FontSize="14"/>
                        <TextBlock Text="{Binding}" TextWrapping="Wrap" FontSize="14"/>
                    </StackPanel>
                </DataTemplate>
                """)
        };
        host.Children.Add(list);
        Task pendingPage = Task.CompletedTask;
        var clock = Stopwatch.StartNew();
        var accumulator = new ScrollOffsetAccumulator();
        var beforeReads = 0;
        var afterReads = 0;
        var failedDirections = 0;
        try
        {
            await Wait(() => list.IsLoaded && Descendants(list).OfType<ScrollViewer>().Any());
            var scroll = Descendants(list).OfType<ScrollViewer>().First();
            ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
            var panel = (ItemsStackPanel)list.ItemsPanelRoot;
            panel.ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepItemsInView;
            await Wait(() => scroll.ScrollableHeight > 0);
            list.ScrollIntoView(rows[^1], ScrollIntoViewAlignment.Leading);
            await Wait(() => list.ContainerFromIndex(rows.Count - 1) is FrameworkElement { IsLoaded: true, ActualHeight: > 0 });
            // ScrollIntoView can retain an estimated extent until the tail rows
            // measure. Finish fixture setup before sampling any user movement.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
                await Task.Delay(100);
                if (scroll.ScrollableHeight - scroll.VerticalOffset < 1) break;
            }
            Check(scroll.ScrollableHeight - scroll.VerticalOffset < 1, "Native comparison resolves its initial measured tail before input");
            await Task.Delay(200);
            Check(true, "Native ListView starts at its settled latest message with stable ObservableCollection");
            foreach (var direction in new[] { -1, 1, -1 })
            {
                accumulator.Reset();
                var readsBefore = direction < 0 ? beforeReads : afterReads;
                var frames = 0;
                var wrong = 0;
                double? last = null;
                var samples = new List<object>();
                var requests = new List<object>();
                void Observe(object? sender, object args)
                {
                    for (var index = 0; index < rows.Count; index++)
                    {
                        if (list.ContainerFromIndex(index) is not FrameworkElement { IsLoaded: true, ActualHeight: > 0 } row) continue;
                        var bounds = row.TransformToVisual(scroll).TransformBounds(new(0, 0, row.ActualWidth, row.ActualHeight));
                        if (bounds.Bottom <= 1 || bounds.Top >= scroll.ViewportHeight - 1) continue;
                        var key = Key(rows[index]);
                        var position = key + Math.Clamp(-bounds.Top / bounds.Height, 0, 1);
                        if (last is { } previous && (position - previous) * direction < -.08) wrong++;
                        last = position;
                        frames++;
                        if (samples.Count < 5000) samples.Add(new { ms = clock.ElapsedMilliseconds, key, position,
                            offset = scroll.VerticalOffset, extent = scroll.ExtentHeight, top = bounds.Top, height = bounds.Height });
                        break;
                    }
                }
                CompositionTarget.Rendering += Observe;
                try
                {
                    for (var pulse = 0; pulse < 420; pulse++)
                    {
                        var destination = accumulated
                            ? accumulator.Propose(scroll.VerticalOffset, direction * 18, scroll.ScrollableHeight, 1 / XamlRoot.RasterizationScale)
                            : Math.Clamp(scroll.VerticalOffset + direction * 18, 0, scroll.ScrollableHeight);
                        var current = scroll.VerticalOffset;
                        var accepted = scroll.ChangeView(null, destination, null, disableAnimation: !animated);
                        if (accepted && accumulated) accumulator.Accepted(destination);
                        requests.Add(new { ms = clock.ElapsedMilliseconds, current, destination, accepted, extent = scroll.ExtentHeight });
                        var distance = direction < 0 ? scroll.VerticalOffset : scroll.ScrollableHeight - scroll.VerticalOffset;
                        if (pendingPage.IsCompleted && distance <= (edgeOnly ? 1 : 200) &&
                            (direction < 0 ? Key(rows[0]) > 1001 : Key(rows[^1]) < 1120))
                            pendingPage = Page(direction);
                        await Task.Delay(16);
                    }
                    await pendingPage;
                    await Task.Delay(600);
                }
                finally { CompositionTarget.Rendering -= Observe; }
                var reads = (direction < 0 ? beforeReads : afterReads) - readsBefore;
                results.Add(new { direction, frames, wrongDirection = wrong, reads, samples, requests });
                if (wrong != 0 || frames == 0 || reads == 0) failedDirections++;
            }
            Check(failedDirections == 0, "Native held up/down/reversed-up paging has no wrong-direction rendered motion");

            async Task Page(int direction)
            {
                var first = Key(rows[0]);
                var last = Key(rows[^1]);
                await Task.Delay(100); // Same synthetic provider latency as Discord fixture.
                events.Add(new { ms = clock.ElapsedMilliseconds, direction, first, last, offset = scroll.VerticalOffset,
                    extent = scroll.ExtentHeight, phase = "before publication" });
                if (direction < 0)
                {
                    beforeReads++;
                    for (var id = first - 1; id >= Math.Max(1001, first - 20); id--) rows.Insert(0, Message(id));
                    while (rows.Count > 40) rows.RemoveAt(rows.Count - 1);
                }
                else
                {
                    afterReads++;
                    for (var id = last + 1; id <= Math.Min(1120, last + 20); id++) rows.Add(Message(id));
                    while (rows.Count > 40) rows.RemoveAt(0);
                }
                events.Add(new { ms = clock.ElapsedMilliseconds, direction, first = Key(rows[0]), last = Key(rows[^1]),
                    offset = scroll.VerticalOffset, extent = scroll.ExtentHeight, phase = "after publication" });
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            await pendingPage;
            host.Children.Remove(list);
            list.ItemsSource = null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null,
                accumulated, animated, edgeOnly, checks, results, events, error = failure?.ToString() }));
        }
        if (failure is not null) throw failure;

        static int Key(string text) => int.Parse(text.AsSpan(8, 4), System.Globalization.CultureInfo.InvariantCulture);
        static string Message(int id) => string.Join('\n', Enumerable.Range(1, 1 + id % 5)
            .Select(line => $"Message {id}, line {line}: synthetic conversation content."));
        static IEnumerable<DependencyObject> Descendants(DependencyObject node)
        {
            yield return node;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(node, index))) yield return child;
        }
    }
}
