using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PlaynitePerformanceValidationPage
{
    private sealed record ScrollPosition(double Seconds, string Event, int Step, double Offset,
        double Extent, double Viewport, int? Focused, double CellWidth, double CellHeight,
        double? FocusY, double? FocusHeight, double PanelY, int? Anchor, double? AnchorY,
        double? AnchorMargin, double? AnchorHeight, float? AnchorScale, bool AnchorHasContent);
    private sealed record ScrollRequest(double Seconds, int Step, double Horizontal, double Vertical);

    private async Task CheckScrollDirectionAsync()
    {
        var view = View()!;
        var scroll = Descendants(view).OfType<ScrollViewer>().First();
        var collection = Descendants(presenter).OfType<WidgetIndexedCollectionView>().First();
        var results = new List<object>();
        var reversed = false;
        foreach (var mode in new[] { "held-down", "burst-down", "burst-up", "half-down" })
        {
            var upward = mode.EndsWith("up", StringComparison.Ordinal);
            var columns = ((ItemsWrapGrid)view.ItemsPanelRoot).MaximumRowsOrColumns;
            var start = upward ? 100 * columns : 0;
            view.ScrollIntoView(view.Items[start], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(start) is Control { IsLoaded: true, IsEnabled: true, IsTabStop: true });
            ((Control)view.ContainerFromIndex(start)).Focus(FocusState.Keyboard);
            await Task.Delay(1000, lifetime.Token);
            var positions = new List<ScrollPosition>();
            var navigation = new List<object>();
            var initial = (GridViewItem)view.ContainerFromIndex(start);
            navigation.Add(new { message = "transitions",
                items = view.ItemContainerTransitions?.Select(value => value.GetType().Name).ToArray(),
                container = initial.Transitions?.Select(value => value.GetType().Name).ToArray(),
                content = initial.ContentTransitions?.Select(value => value.GetType().Name).ToArray() });
            var step = 0;
            int? anchor = null;
            var started = elapsed.Elapsed.TotalSeconds;
            var sampler = new Shell.RightStickScroll();
            var requests = new List<ScrollRequest>();
            presenter.ScrollRequestTrace = (x, y) => requests.Add(new(elapsed.Elapsed.TotalSeconds - started, step, x, y));
            collection.NavigationTrace = message => navigation.Add(new { seconds = elapsed.Elapsed.TotalSeconds - started, step, message });
            scroll.ViewChanged += Changed;
            scroll.BringIntoViewRequested += BringRequested;
            CompositionTarget.Rendering += Rendered;
            try
            {
                Capture("start");
                while (elapsed.Elapsed.TotalSeconds - started < 8)
                {
                    ++step;
                    var magnitude = mode.StartsWith("half", StringComparison.Ordinal) ? 20000 : 32767;
                    var delta = sampler.Sample(0, (short)(upward ? magnitude : -magnitude), Environment.TickCount64);
                    navigation.Add(new { seconds = elapsed.Elapsed.TotalSeconds - started, step, message = "scroll-input",
                        delta = delta.Y, reported = scroll.VerticalOffset, requested = scroll.VerticalOffset + delta.Y });
                    presenter.ScrollBy(delta.X, delta.Y);
                    Capture("input");
                    if (mode.StartsWith("burst", StringComparison.Ordinal))
                        for (var packet = 0; packet < 3; packet++)
                        {
                            // The native pump drains multiple queued input packets in
                            // one UI dispatch. Allow its clock to advance without
                            // yielding to layout, just as native poll/dispatch work does.
                            Thread.Sleep(1);
                            delta = sampler.Sample(0, (short)(upward ? magnitude : -magnitude), Environment.TickCount64);
                            if (delta.Y != 0) presenter.ScrollBy(delta.X, delta.Y);
                            Capture("queued-input");
                        }
                    await Task.Delay(16, lifetime.Token);
                }
                Capture("release");
                sampler.Sample(0, 0, Environment.TickCount64);
                await Task.Delay(150, lifetime.Token);
                if (sampler.ShouldSettle(Environment.TickCount64)) await Until(presenter.SettleScrollFocus);
                await Task.Delay(1500, lifetime.Token);
                Capture("settled");
            }
            finally
            {
                scroll.ViewChanged -= Changed;
                scroll.BringIntoViewRequested -= BringRequested;
                CompositionTarget.Rendering -= Rendered;
                collection.NavigationTrace = null;
                presenter.ScrollRequestTrace = null;
            }
            var releasedAt = positions.First(position => position.Event == "release").Seconds;
            var backwards = positions.Zip(positions.Skip(1))
                .Select(pair => new { previous = pair.First, current = pair.Second,
                    distance = (pair.Second.Offset - pair.First.Offset) * (upward ? 1 : -1) })
                .Where(change => change.distance > 1 / XamlRoot.RasterizationScale).ToArray();
            var visualBackwards = positions.Zip(positions.Skip(1))
                .Where(pair => pair.First.Anchor is not null && pair.First.Anchor == pair.Second.Anchor)
                .Select(pair => new { previous = pair.First, current = pair.Second,
                    distance = (pair.Second.AnchorY!.Value - pair.First.AnchorY!.Value) * (upward ? -1 : 1) })
                .Where(change => change.distance > 1 / XamlRoot.RasterizationScale).ToArray();
            var requestBackwards = requests.Zip(requests.Skip(1))
                .Select(pair => new { previous = pair.First, current = pair.Second,
                    distance = (pair.Second.Vertical - pair.First.Vertical) * (upward ? 1 : -1) })
                .Where(change => change.distance > 1 / XamlRoot.RasterizationScale).ToArray();
            var heldBackwards = backwards.Where(change => change.current.Seconds < releasedAt).ToArray();
            var heldArtworkBackwards = visualBackwards.Where(change => change.current.Seconds < releasedAt &&
                change.previous.AnchorHasContent && change.current.AnchorHasContent).ToArray();
            reversed |= backwards.Length != 0 || requestBackwards.Length != 0 || heldArtworkBackwards.Length != 0;
            results.Add(new { mode, heldBackwards = heldBackwards.Length, heldArtworkBackwards = heldArtworkBackwards.Length,
                releaseBackwards = backwards.Length - heldBackwards.Length,
                placeholderOrReleaseMovements = visualBackwards.Length - heldArtworkBackwards.Length,
                requestBackwards = requestBackwards.Length, maximumRequestBackward = requestBackwards.Select(change => change.distance).DefaultIfEmpty().Max() });
            await File.WriteAllTextAsync(Path.Combine(output, mode + ".json"),
                JsonSerializer.Serialize(new { mode, positions, navigation, backwards, visualBackwards, requests, requestBackwards }), lifetime.Token);
            Sample(mode);

            void Capture(string reason)
            {
                if (positions.Count >= 20000) return;
                var focused = FocusedIndex(view);
                var item = focused is { } index ? view.ContainerFromIndex(index) as FrameworkElement : null;
                var grid = (ItemsWrapGrid)view.ItemsPanelRoot;
                var anchorItem = anchor is { } known ? view.ContainerFromIndex(known) as FrameworkElement : null;
                if (anchorItem is null || !anchorItem.IsLoaded || !Visible(anchorItem))
                {
                    anchorItem = grid.Children.OfType<GridViewItem>().FirstOrDefault(candidate =>
                        candidate.IsLoaded && view.IndexFromContainer(candidate) != focused && Visible(candidate));
                    anchor = anchorItem is null ? null : view.IndexFromContainer(anchorItem);
                }
                positions.Add(new(elapsed.Elapsed.TotalSeconds - started, reason, step,
                    scroll.VerticalOffset, scroll.ScrollableHeight, scroll.ViewportHeight, focused,
                    grid.ItemWidth, double.IsFinite(grid.ItemHeight) ? grid.ItemHeight : -1,
                    item?.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y, item?.ActualHeight,
                    grid.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y, anchor,
                    anchorItem?.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y,
                    anchorItem?.Margin.Top, anchorItem?.ActualHeight, anchorItem?.Scale.Y,
                    anchorItem is not null && Descendants(anchorItem).OfType<WidgetArtworkView>().Any(image => image.Source is not null)));
                bool Visible(FrameworkElement candidate)
                {
                    var y = candidate.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y;
                    return y < scroll.ViewportHeight && y + candidate.ActualHeight > 0;
                }
            }
            void Changed(object? sender, ScrollViewerViewChangedEventArgs args) => Capture(args.IsIntermediate ? "intermediate" : "final");
            void Rendered(object? sender, object args) => Capture("render");
            void BringRequested(UIElement sender, BringIntoViewRequestedEventArgs args) =>
                navigation.Add(new { seconds = elapsed.Elapsed.TotalSeconds - started, step, message = "bring-into-view",
                    animation = args.AnimationDesired, target = (args.TargetElement as FrameworkElement)?.GetType().Name,
                    rectangle = args.TargetRect.ToString() });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "direction-summary.json"), JsonSerializer.Serialize(results), lifetime.Token);
        if (reversed) throw new InvalidOperationException("Held scrolling or release produced a backward scroll destination or offset.");
    }
}
