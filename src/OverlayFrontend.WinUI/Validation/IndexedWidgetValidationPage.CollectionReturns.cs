using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private sealed record ReturnVisibleRow(int Index, double Y, double Height);
    private async Task ValidateCollectionReturnsAsync(string output)
    {
        var checks = new List<string>();
        Exception? error = null;
        var liveSession = session!;
        var horizontal = false;
        var traces = new Queue<string>();
        presenter.FocusTrace = line => { traces.Enqueue(line); if (traces.Count > 1000) traces.Dequeue(); };
        string? FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused
            ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused) : null;
        async Task Send(string action)
        {
            var displayed = liveSession.GetState("indexed-owned")!.LastGood!;
            await liveSession.SendActionAsync(displayed.Authority, new(action, action, InputScopeId: "root"), lifetime.Token);
            await Until(() => Descendants(presenter).OfType<TextBlock>().Any(text => text.Text == action), "native publication of " + action);
        }
        async Task Until(Func<bool> predicate, string label)
        {
            var deadline = Environment.TickCount64 + 10000;
            while (!predicate())
            {
                if (failure is not null) throw new InvalidOperationException(failure);
                if (Environment.TickCount64 > deadline)
                {
                    var source = View()?.ItemsSource as Collections.IndexedItemsSource<Collections.WidgetIndexedRow>;
                    var slot = source is { Count: > 75 } ? (Collections.IndexedItem<Collections.WidgetIndexedRow>)source[75] : null;
                    throw new TimeoutException(label + ": " + JsonSerializer.Serialize(new { source?.CompletedLoads, source?.FailedLoads,
                        source?.CancelledLoads, slot?.Failed, disabled = slot?.Value?.Item.Root.IsDisabled, current = slot?.Value?.Lease.IsCurrent,
                        presenter.HasPendingMemoryRestore, entries = Descendants(presenter).OfType<WidgetIndexedCollectionView>().Select(collection => collection.NavigationDiagnosticState()).ToArray(),
                        presenter = presenter.FocusDiagnostics() }));
                }
                await Task.Delay(20, lifetime.Token);
            }
        }
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        ScrollViewer Scroll() => Descendants(View()!).OfType<ScrollViewer>().First();
        double Y()
        {
            var point = ((FrameworkElement)View()!.ContainerFromIndex(75)).TransformToVisual(Scroll()).TransformPoint(default);
            return horizontal ? point.X : point.Y;
        }
        async Task Settle()
        {
            await Until(() => !presenter.HasPendingMemoryRestore && Descendants(presenter).OfType<WidgetIndexedCollectionView>().All(view => !view.IsEntryPending), "return settles");
            await Task.Delay(250, lifetime.Token);
        }
        async Task<double> Open(bool horizontalList = false)
        {
            horizontal = horizontalList;
            await Send("return-mode");
            if (horizontal) await Send("return-horizontal");
            await Until(() => View()?.Items.Count == 100, "list ready");
            await Send("return-exact");
            await Until(() => FocusId() == "Widget.items.Item.75", "deep exact focus");
            await Settle();
            var scroll = Scroll();
            var row = (FrameworkElement)View()!.ContainerFromIndex(75);
            var viewport = horizontal ? scroll.ViewportWidth : scroll.ViewportHeight;
            var extent = horizontal ? row.ActualWidth : row.ActualHeight;
            var desiredY = Math.Min(100, Math.Max(0, viewport - extent));
            var desiredOffset = Math.Max(0, (horizontal ? scroll.HorizontalOffset : scroll.VerticalOffset) + Y() - desiredY);
            scroll.ChangeView(horizontal ? desiredOffset : null, horizontal ? null : desiredOffset, null, true);
            await Task.Delay(300, lifetime.Token);
            var y = Y();
            Check(y >= -1 && y + extent <= viewport + 1, "return opener is visible before leaving");
            await Send("return-open");
            await Until(() => View() is null, "detail replaces list");
            Check(presenter.CapturePresentationState()!.CollectionReturns.Any(memory => memory.Element.Id == "items" && memory.Index == 75),
                "departure captures exact return viewport");
            return y;
        }
        async Task Pending()
        {
            await Send("return-hold");
            await Send("return-back");
            await Until(() => presenter.HasPendingMemoryRestore && View() is not null, "held return admitted");
        }
        try
        {
            checks.Add($"indexed source lifetime/reentrancy: {await IndexedSourceLifetimeScenarios.RunAsync(DispatcherQueue)} checks");
            if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--collection-return-start") is { } startFile)
            {
                await Until(() => Descendants(presenter).OfType<Button>().Any(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "Widget.return-mode"), "fixture ready for foreground admission");
                await File.WriteAllTextAsync(startFile + ".ready", "ready");
                await Until(() => File.Exists(startFile), "external foreground admission");
            }
            await Open(); await Pending();
            Check(presenter.FocusHostTarget("return-other"), "programmatic departure focuses another control");
            Check(!presenter.HasPendingMemoryRestore, "programmatic departure cancels pending return geometry");
            await Send("return-release");
            await Task.Delay(500, lifetime.Token);
            Check(FocusId() == "Widget.return-other", "late rows do not steal programmatic focus");

            await Open(); await Pending();
            var other = Descendants(presenter).OfType<Button>().Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "Widget.return-other");
            Check(other.Focus(FocusState.Programmatic), "direct native focus departure succeeds");
            Check(presenter.CapturePresentationState()!.PendingCollectionReturn is null,
                "immediate capture cannot resurrect a directly canceled native return");
            await Send("return-release"); await Task.Delay(300, lifetime.Token);
            Check(FocusId() == "Widget.return-other", "late data preserves direct native focus departure");

            await Open(); await Pending();
            presenter.SetAutomaticFocusEnabled(false);
            other = Descendants(presenter).OfType<Button>().Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "Widget.return-other");
            Check(other.Focus(FocusState.Programmatic), "explicit native focus is accepted while automatic focus is disabled");
            presenter.SetAutomaticFocusEnabled(true);
            await Send("return-release"); await Task.Delay(300, lifetime.Token);
            Check(FocusId() == "Widget.return-other" && !presenter.HasPendingMemoryRestore,
                "re-enabling automatic focus does not resurrect a return canceled by native focus");

            await Open(); await Send("return-disabled"); await Send("return-back");
            await Settle();
            Check(FocusId() != "Widget.items.Item.75" && !presenter.HasPendingMemoryRestore,
                "disabled return target retires geometry without receiving focus");
            await Send("return-disabled");

            await Open(); await Pending();
            await Send("return-replace");
            await Until(() => !presenter.HasPendingMemoryRestore, "query replacement retires return");
            await Send("return-release"); await Task.Delay(400, lifetime.Token);
            Check(!presenter.HasPendingMemoryRestore && FocusId() != "Widget.items.Item.75",
                "replacement query never receives the old return position");

            var retainedY = await Open(); await Pending();
            presenter.SetAutomaticFocusEnabled(false);
            presenter.SetPresentationInputEnabled(false);
            await presenter.SetPresentationActiveAsync(false);
            await liveSession.SetLifecycleAsync(liveSession.GetTarget("indexed-owned"), WidgetLifecycleState.Background, lifetime.Token);
            await liveSession.EstablishPresentationAsync(liveSession.GetTarget("indexed-owned"), WidgetLifecycleState.Interactive, lifetime.Token);
            await presenter.SetPresentationActiveAsync(true);
            presenter.SetPresentationInputEnabled(true);
            presenter.SetAutomaticFocusEnabled(true);
            await Until(() => presenter.HasPendingMemoryRestore, "retained hide/show resumes unfinished return");
            await Send("return-release");
            await Until(() => FocusId() == "Widget.items.Item.75", "retained return completes");
            await Settle();
            Check(Math.Abs(Y() - retainedY) < 2, "retained hide/show restores unfinished return geometry");

            var originalY = await Open(); await Pending();
            var memory = presenter.CapturePresentationState()!;
            Check(memory.PendingCollectionReturn is not null, "unfinished return is captured separately from transient viewport geometry");
            var old = presenter;
            await old.SetPresentationActiveAsync(false);
            await liveSession.SetLifecycleAsync(liveSession.GetTarget("indexed-owned"), WidgetLifecycleState.Background, lifetime.Token);
            var layout = (Grid)Content; layout.Children.Remove(old);
            await old.DisposeAsync();
            presenter = new() { Session = liveSession, Failed = old.Failed, DispatchActionAsync = old.DispatchActionAsync, FocusTrace = old.FocusTrace };
            await presenter.SetPresentationActiveAsync(false); presenter.SetAutomaticFocusEnabled(false);
            Grid.SetRow(presenter, 1); layout.Children.Add(presenter);
            await liveSession.EstablishPresentationAsync(liveSession.GetTarget("indexed-owned"), WidgetLifecycleState.Interactive, lifetime.Token);
            AdoptCurrentPresentation();
            await Until(() => presenter.IsLoaded, "new presenter loaded");
            Check(presenter.RestorePresentationState(memory), "same-owner unfinished return survives native recreation");
            await presenter.SetPresentationActiveAsync(true); presenter.SetAutomaticFocusEnabled(true);
            await Until(() => presenter.HasPendingMemoryRestore, "recreated return waits for its row");
            await Send("return-release");
            await Until(() => FocusId() == "Widget.items.Item.75", "recreated return focuses exact row");
            await Settle();
            Check(Math.Abs(Y() - originalY) < 2, "unfinished return restores intended geometry after recreation");

            var horizontalPosition = await Open(horizontalList: true);
            await Send("return-back");
            await Until(() => FocusId() == "Widget.items.Item.75", "horizontal return focuses row");
            await Settle();
            Check(Math.Abs(Y() - horizontalPosition) < 2, "horizontal return preserves the opener's screen position");

            var rowPosition = await Open();
            await Send("return-tall"); await Send("return-back");
            await Until(() => FocusId() == "Widget.items.Item.75", "larger row return focuses row");
            await Settle();
            Check(((FrameworkElement)View()!.ContainerFromIndex(75)).ActualHeight >= 80 && Math.Abs(Y() - rowPosition) < 2,
                "changed row height preserves screen position instead of obsolete pixel scroll offset");
            await Send("return-open"); await Until(() => View() is null, "detail before resize");
            presenter.Height = 300;
            await Send("return-back");
            await Until(() => FocusId() == "Widget.items.Item.75", "resized return focuses row");
            await Settle();
            var contentViewport = Descendants(Scroll()).OfType<ScrollContentPresenter>().First();
            var resizedRow = (FrameworkElement)View()!.ContainerFromIndex(75);
            var resizedY = resizedRow.TransformToVisual(contentViewport).TransformPoint(default).Y;
            Check(resizedY >= -1 && resizedY + resizedRow.ActualHeight <= contentViewport.ActualHeight + 1,
                "smaller viewport clamps return geometry to keep the focused row visible");
            presenter.Height = double.NaN;

            var repeatedY = await Open(); await Pending();
            for (var attempt = 0; attempt < 8; ++attempt)
            {
                presenter.SetAutomaticFocusEnabled(false);
                presenter.SetAutomaticFocusEnabled(true);
                await Task.Delay(40, lifetime.Token);
                Check(presenter.PendingCollectionReturnCount == 1, "suspended return remains one pending viewport after resume " + attempt);
            }
            await Send("return-release");
            await Until(() => FocusId() == "Widget.items.Item.75", "repeatedly resumed return focuses its row");
            await Settle();
            Check(Math.Abs(Y() - repeatedY) < 2, "repeated suspension retains the original return geometry");

            await Open(); await Pending();
            App.Window.Activate();
            var wheelEvents = 0;
            var wheelScroll = Scroll();
            PointerEventHandler wheel = (_, _) => ++wheelEvents;
            wheelScroll.AddHandler(UIElement.PointerWheelChangedEvent, wheel, true);
            try
            {
                using var input = new NativeWheelProbe(wheelScroll);
                traces.Enqueue(JsonSerializer.Serialize(new { phase = "wheel-target", position = input.Position }));
                input.MoveToTarget();
                // Allow the real pointer movement to reach WinUI hit testing
                // before emitting the distinct wheel input, as a user would.
                await Task.Delay(100, lifetime.Token);
                input.Send(-120);
                await Until(() => wheelEvents > 0, "native wheel delivered");
                Check(!presenter.HasPendingMemoryRestore, "native wheel cancels pending return geometry");
                await Task.Delay(500, lifetime.Token);
                var offset = wheelScroll.VerticalOffset;
                var visibleBefore = WheelVisibleRows();
                await Send("return-release"); await Task.Delay(500, lifetime.Token);
                var visibleAfter = WheelVisibleRows();
                traces.Enqueue(JsonSerializer.Serialize(new { phase = "wheel-after-data", before = offset, after = wheelScroll.VerticalOffset, visibleBefore, visibleAfter, focus = FocusId() }));
                // Native anchoring legitimately changes the pixel offset when
                // 48px placeholders acquire 44px rows. The user's visible anchor
                // must stay fixed, and the canceled return must not steal focus.
                Check(!presenter.HasPendingMemoryRestore && visibleBefore.Length > 0 && visibleAfter.Length > 0 &&
                    visibleBefore[0].Index == visibleAfter[0].Index && Math.Abs(visibleBefore[0].Y - visibleAfter[0].Y) < 2 &&
                    FocusId() == "Widget.return-back",
                    "late rows do not rewind a wheel-owned viewport");

                ReturnVisibleRow[] WheelVisibleRows() => Descendants(View()!).OfType<ListViewItem>().Select(row => new ReturnVisibleRow(
                    View()!.IndexFromContainer(row), row.TransformToVisual(wheelScroll).TransformPoint(default).Y, row.ActualHeight))
                    .Where(row => row.Y + row.Height > 0 && row.Y < wheelScroll.ViewportHeight).OrderBy(row => row.Y).ToArray();
            }
            finally { wheelScroll.RemoveHandler(UIElement.PointerWheelChangedEvent, wheel); }
        }
        catch (Exception exception) { error = exception; }
        finally
        {
            try { await Send("return-release"); } catch { }
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = error is null, checks, trace = traces.ToArray(), error = error?.ToString() }));
        }
        if (error is not null) throw error;
    }
}
