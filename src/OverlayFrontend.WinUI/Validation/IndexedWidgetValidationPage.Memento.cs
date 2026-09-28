using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetUi.State.Collections;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private bool mementoRunning;
    private TextBlock? mementoStatus;
    private async Task ProbeMementoAsync()
    {
        if (mementoRunning) return;
        if (session is null) return;
        mementoRunning = true;
        var layout = (Grid)Content;
        if (mementoStatus is null)
        {
            mementoStatus = new() { MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
            AutomationProperties.SetAutomationId(mementoStatus, "IndexedWidget.MementoResult");
            layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(mementoStatus, layout.RowDefinitions.Count - 1); layout.Children.Add(mementoStatus);
        }
        var result = mementoStatus;
        result.Text = "Memento checks pending";
        var checks = 0;
        try
        {
            await CheckInactiveInitialDemand();
            var initial = session.GetState("indexed-owned")!.LastGood!;
            await session.SendActionAsync(initial.Authority, new("focus-exact", "root", InputScopeId: "root"), lifetime.Token);
            await Until(() => FocusId() == "Widget.items.Item.75");
            var view = View()!;
            var source = (IndexedItemsSource<WidgetIndexedRow>)view.ItemsSource;
            view.ScrollIntoView(source[70], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(70) is Control { IsLoaded: true } &&
                ((IndexedItem<WidgetIndexedRow>)source[70]).Value?.Lease.IsCurrent == true);
            ((Control)view.ContainerFromIndex(70)).Focus(FocusState.Keyboard);
            await Task.Delay(80);
            var oldOffset = Descendants(view).OfType<ScrollViewer>().First().VerticalOffset;
            var oldLease = ((IndexedItem<WidgetIndexedRow>)source[70]).Value!.Lease;
            var memory = presenter.CapturePresentationState()!;
            Check(memory.IndexedViewports.Count == 1 && memory.IndexedViewports[0].Index > 50, "deep semantic anchor captured");
            var originalBounds = AnchorBounds(view, memory.IndexedViewports.Single().Index);
            await Inspect("Inspect original memory viewport");
            await Recreate(memory, previewFirst: true);
            await Until(() => !presenter.HasPendingMemoryRestore && FocusId() == "Widget.items.Item.70");
            Check(FocusId() == "Widget.items.Item.70", "consumed author focus request does not replay after recreation");
            var nextView = View()!;
            var nextSource = (IndexedItemsSource<WidgetIndexedRow>)nextView.ItemsSource;
            var nextOffset = Descendants(nextView).OfType<ScrollViewer>().First().VerticalOffset;
            Check(!ReferenceEquals(nextView, view) && !ReferenceEquals(nextSource, source), "native view was actually evicted and recreated");
            Check(!oldLease.IsCurrent && ((IndexedItem<WidgetIndexedRow>)nextSource[70]).Value is { Lease.IsCurrent: true }, "restored focus uses fresh row authority");
            var oldAnchor = memory.IndexedViewports.Single();
            var restoredAnchor = presenter.CapturePresentationState()!.IndexedViewports.Single();
            Check(restoredAnchor.Key == oldAnchor.Key && Math.Abs(restoredAnchor.ClippedFraction - oldAnchor.ClippedFraction) < .04,
                $"semantic viewport restored [{oldAnchor.Index}:{oldAnchor.ClippedFraction:F3}->{restoredAnchor.Index}:{restoredAnchor.ClippedFraction:F3}; offset {oldOffset:F2}->{nextOffset:F2}]");
            var restoredBounds = AnchorBounds(nextView, restoredAnchor.Index);
            Check(Math.Abs(originalBounds.Y - restoredBounds.Y) < 2 && Math.Abs(originalBounds.Height - restoredBounds.Height) < 2,
                $"same-size native anchor screen position remains stable [{originalBounds.Y:F2}->{restoredBounds.Y:F2}]");
            await Inspect("Inspect restored memory viewport");
            Check(!presenter.RestorePresentationState(memory with { Owner = memory.Owner with { Runtime = "replaced" } }), "runtime replacement rejects old memory");

            // A changed query must not pull the new source to a previous query's coordinates.
            var invalid = memory with { IndexedViewports = memory.IndexedViewports.Select(x => x with { Query = x.Query + 1 }).ToArray(),
                IndexedFocus = memory.IndexedFocus.Select(x => x with { Target = x.Target with { QueryGeneration = x.Target.QueryGeneration + 1 } }).ToArray() };
            await Recreate(invalid);
            await Until(() => !presenter.HasPendingMemoryRestore && FocusId() == "Widget.items.Item.0");
            Check(Descendants(View()!).OfType<ScrollViewer>().First().VerticalOffset < 10, "query mismatch keeps native initial viewport");

            await Recreate(memory, cancel: true);
            await Until(() => !presenter.HasPendingMemoryRestore);
            Check(FocusId() == "Widget.parent", "explicit input cancels deferred memory restoration");
            var ordinary = presenter.CapturePresentationState()!;
            await Recreate(ordinary);
            await Until(() => !presenter.HasPendingMemoryRestore && FocusId() == "Widget.parent");
            Check(FocusId() == "Widget.parent", "ordinary semantic focus survives native recreation");

            await Recreate(memory, resized: true);
            await Until(() => !presenter.HasPendingMemoryRestore && FocusId() == "Widget.items.Item.70");
            Check(presenter.CapturePresentationState()!.IndexedViewports.Single().Index > 50,
                "resized recreation restores a semantic deep anchor using current native layout");

            var beforeQuery = publication;
            // Use the row's declared Y shortcut. The fixture's context-replace
            // command requires context-mode surfaces and is not a valid query
            // transition from the ordinary list/grid fixture.
            await presenter.HandleControllerButtonAsync(ControllerButton.Y, origin: ControllerInputOrigin.AccessibilityAutomation);
            await Until(() => publication > beforeQuery);
            await Recreate(memory);
            await Until(() => !presenter.HasPendingMemoryRestore && FocusId() == "Widget.items.Item.0");
            Check(Descendants(View()!).OfType<ScrollViewer>().First().VerticalOffset < 10,
                "actual worker query replacement rejects the evicted query viewport");
            result.Text = $"Passed memento recreation checks:{checks}; deep offset:{oldOffset:F2}->{nextOffset:F2}";
        }
        catch (Exception error)
        {
            var items = View()?.ItemsSource as IndexedItemsSource<WidgetIndexedRow>;
            result.Text = $"Failed memento recreation after {checks} checks; focus:{FocusId()}; pending:{presenter.HasPendingMemoryRestore}; active:{items?.IsPresentationActive}; ranges:{items?.RangeNotifications}; loads:{items?.CompletedLoads}; failures:{items?.FailedLoads}: " + error;
        }
        finally { mementoRunning = false; }

        async Task Recreate(WidgetPresentationMemento state, bool cancel = false, bool resized = false, bool previewFirst = false)
        {
            var old = presenter;
            result.Text = "Recreating: suspending old view";
            await old.SetPresentationActiveAsync(false).WaitAsync(TimeSpan.FromSeconds(8));
            result.Text = "Recreating: background worker";
            await session.SetLifecycleAsync(session.GetTarget("indexed-owned"), WidgetLifecycleState.Background, lifetime.Token);
            layout.Children.Remove(old);
            result.Text = "Recreating: disposing old view";
            await old.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8));
            presenter = new() { Session = session, Failed = old.Failed, DispatchActionAsync = old.DispatchActionAsync };
            if (resized) presenter.Width = 500;
            await presenter.SetPresentationActiveAsync(false);
            presenter.SetAutomaticFocusEnabled(false);
            Grid.SetRow(presenter, 1); layout.Children.Add(presenter);
            var frame = await session.EstablishPresentationAsync(session.GetTarget("indexed-owned"), WidgetLifecycleState.Interactive, lifetime.Token);
            presenter.Apply(frame);
            result.Text = "Recreating: waiting for layout";
            await Until(() => presenter.IsLoaded && Descendants(presenter).OfType<Button>().Any(x => x.ActualWidth > 0 && AutomationProperties.GetAutomationId(x) == "Widget.parent"));
            Check(presenter.RestorePresentationState(state), "matching owner admits bounded memory");
            await presenter.SetPresentationActiveAsync(true);
            if (previewFirst)
            {
                // Tray previews restore scroll before any focus entry. Entry used
                // to mask a missing grid measurement-item demand in this test.
                await Until(() => !presenter.HasPendingMemoryRestore);
                var preview = View()!;
                var restored = presenter.CapturePresentationState()!.IndexedViewports.Single();
                var expected = state.IndexedViewports.Single();
                Check(restored.Key == expected.Key && Math.Abs(restored.ClippedFraction - expected.ClippedFraction) < .04,
                    "deep preview restores semantic viewport before focus entry");
                if (preview is GridView)
                {
                    var previewSource = (IndexedItemsSource<WidgetIndexedRow>)preview.ItemsSource;
                    Check(((IndexedItem<WidgetIndexedRow>)previewSource[0]).Value is { Lease.IsCurrent: true } &&
                        previewSource.RetainedIndices <= IndexedItemsSource<WidgetIndexedRow>.MaximumRetainedIndices,
                        "native grid measurement item remains current within the source retention budget");
                }
            }
            presenter.SetAutomaticFocusEnabled(true);
            if (cancel)
            {
                presenter.MoveFocus(FocusNavigationDirection.Up);
                Descendants(presenter).OfType<Button>().First(x => AutomationProperties.GetAutomationId(x) == "Widget.parent").Focus(FocusState.Keyboard);
            }
            else presenter.Enter();
        }
        string FocusId() => FocusManager.GetFocusedElement(XamlRoot) is FrameworkElement focused ? AutomationProperties.GetAutomationId(focused) : string.Empty;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); ++checks; }
        static async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 8000;
            while (!condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Memento scenario did not settle."); await Task.Delay(20); }
        }
        static Rect AnchorBounds(ListViewBase view, int index)
        {
            var container = (FrameworkElement)view.ContainerFromIndex(index);
            return container.TransformToVisual(view).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        }
        async Task Inspect(string caption)
        {
            result.Text = caption;
            await Task.Delay(1500, lifetime.Token); // Bounded window for the scripted desktop pixel capture.
        }
        async Task CheckInactiveInitialDemand()
        {
            var reads = 0;
            await using var initialSource = new IndexedItemsSource<string>(new(new("memory", "inactive", "initial"), 1), 64, DispatcherQueue,
                (request, token) =>
                {
                    Interlocked.Increment(ref reads);
                    return Task.FromResult(new IndexedRangeResult<string>(request.Query, request.RequestId, request.StartIndex,
                        Enumerable.Range(request.StartIndex, request.Count).Select(index => new KeyedCollectionItem<string>($"item.{index}", $"{index}")).ToArray(), request.ContentRevision));
                });
            await initialSource.SetPresentationActiveAsync(false);
            using var measure = initialSource.Retain(0);
            initialSource.RangesChanged(new(32, 4), []);
            await Task.Delay(30);
            Check(reads == 0, "new inactive source records first viewport without fetching");
            await initialSource.SetPresentationActiveAsync(true);
            await Until(() => initialSource.CompletedLoads == 2);
            Check(((IndexedItem<string>)initialSource[32]).Value == "32", "resume serves recorded first native viewport without another range callback");
            Check(measure.Slot.Value == "0", "measurement retention does not replace the inactive source's first native viewport demand");
        }
    }
}
