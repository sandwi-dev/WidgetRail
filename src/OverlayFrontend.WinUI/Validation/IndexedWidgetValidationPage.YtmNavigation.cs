using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private async Task ValidateYtmNavigationAsync(string output)
    {
        var checks = new List<string>();
        var failures = new List<string>();
        var observations = new List<object>();
        var traces = new Queue<string>();
        presenter.FocusTrace = line => { traces.Enqueue(line); if (traces.Count > 1000) traces.Dequeue(); };
        Exception? error = null;
        var mode = session!.GetTarget("indexed-owned").Descriptor.InstanceId["ytm-navigation-".Length..];
        ViewSnapshot Snapshot() => session.GetState("indexed-owned")!.LastGood!.Snapshot;
        IEnumerable<ViewNode> Nodes(ViewNode root) => new[] { root }.Concat(root.Children.SelectMany(Nodes));
        ViewNode? Collection() => Nodes(Snapshot().Root).FirstOrDefault(node => node.IndexedCollection is not null);
        bool Shown(string text) => Descendants(presenter).OfType<TextBlock>().Any(node => node.IsLoaded && node.Text == text);
        ScrollViewer Scroll() => Descendants(View()!).OfType<ScrollViewer>().First();
        double RowY(int index) => ((FrameworkElement)View()!.ContainerFromIndex(index)).TransformToVisual(View()!).TransformPoint(default).Y;
        int FocusIndex()
        {
            for (DependencyObject? node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is ListViewItem or GridViewItem) return View()?.IndexFromContainer(node) ?? -1;
            return -1;
        }
        async Task Until(Func<bool> predicate, string label)
        {
            var deadline = Environment.TickCount64 + 8000;
            while (!predicate())
            {
                if (failure is not null) throw new InvalidOperationException(failure);
                if (Environment.TickCount64 > deadline) throw new TimeoutException(label + "; focus=" + FocusIndex() + "; collection=" + Collection()?.Id + "; native=" + presenter.FocusDiagnostics() + "; entries=" + System.Text.Json.JsonSerializer.Serialize(Descendants(presenter).OfType<Presentation.WidgetIndexedCollectionView>().Select(item => item.NavigationDiagnosticState())));
                await Task.Delay(20, lifetime.Token);
            }
        }
        void Check(bool pass, string label) { if (pass) checks.Add(label); else failures.Add(label); }
        async Task SettleViewport()
        {
            await Until(() => !presenter.HasPendingMemoryRestore &&
                Descendants(presenter).OfType<Presentation.WidgetIndexedCollectionView>().All(collection => !collection.IsEntryPending),
                "host viewport restoration completed");
            var offset = Scroll().VerticalOffset;
            var stable = 0;
            await Until(() =>
            {
                var current = Scroll().VerticalOffset;
                stable = Math.Abs(current - offset) < .1 ? stable + 1 : 0;
                offset = current;
                return stable >= 10;
            }, "viewport settled");
        }
        async Task FocusRow(int index)
        {
            await Until(() => FocusIndex() >= 0, "initial collection focus");
            while (FocusIndex() < index)
            {
                var previous = FocusIndex();
                presenter.MoveFocus(FocusNavigationDirection.Down);
                await Until(() => FocusIndex() > previous, "controller row navigation");
            }
            Check(FocusIndex() == index, "controller navigation reaches deep row " + index);
            await SettleViewport();
        }
        try
        {
            await Until(() => Collection()?.Id == "music.scroll.home" && Shown("Artists") && View()?.Items.Count == 100, "home loaded");
            await FocusRow(60);
            var homeQuery = Collection()!.IndexedCollection;
            var homeOffset = Scroll().VerticalOffset;
            var homeY = RowY(60);
            Check(Descendants(presenter).Count(node => node is ListViewItem or GridViewItem) < 50, "home realizes a bounded subset of the real widget's rows");
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await Until(() => Shown("Artist albums 1") && View()?.Items.Count == 100, "artist loaded");
            await FocusRow(75);
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await Until(() => Shown("Album songs") && View()?.Items.Count == 1, "album loaded");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            await Until(() => Shown("Artist albums 2") && View()?.Items.Count == 100 && FocusIndex() >= 0, "artist returned");
            var expectedIndex = mode == "reordered" ? 24 : 0;
            await Until(() => FocusIndex() == expectedIndex, "return target after " + mode);
            Check(FocusIndex() == expectedIndex, mode == "reordered" ? "nested Back follows the unique album identity after provider reorder" : "missing or ambiguous album identity uses valid collection entry instead of a stale row index");
            Check(Descendants(presenter).Count(node => node is ListViewItem or GridViewItem) < 50, "nested return keeps native realization bounded");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);
            await Until(() => Collection()?.Id == "music.scroll.home" && Shown("Artists") && FocusIndex() == 60, "home return");
            await SettleViewport();
            observations.Add(new { phase = "home-return", before = homeOffset, after = Scroll().VerticalOffset, beforeY = homeY, afterY = RowY(60) });
            Check(Math.Abs(RowY(60) - homeY) < 2, "Home Back restores the opener's native screen position");
            Check(Collection()!.IndexedCollection == homeQuery, "Home Back retains the original query identity");
            Check(Math.Abs(Scroll().VerticalOffset - homeOffset) < 2, "Home Back restores the original viewport offset");
            var settingsOffset = Scroll().VerticalOffset;
            var settingsY = RowY(60);
            await presenter.HandleControllerButtonAsync(ControllerButton.Y);
            await Until(() => Descendants(presenter).OfType<Button>().Any(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "Widget.panel.back"), "settings panel");
            var back = Descendants(presenter).OfType<Button>().Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "Widget.panel.back");
            back.Focus(FocusState.Keyboard); presenter.ActivateFocused();
            await Until(() => Collection()?.Id == "music.scroll.home" && Shown("Artists") && FocusIndex() == 60, "visible Settings Back");
            await SettleViewport();
            observations.Add(new { phase = "settings-return", before = settingsOffset, after = Scroll().VerticalOffset, beforeY = settingsY, afterY = RowY(60) });
            Check(Math.Abs(RowY(60) - settingsY) < 2, "visible Settings Back restores the opener's native screen position");
            Check(Math.Abs(Scroll().VerticalOffset - settingsOffset) < 2, "visible Settings Back restores the exact opener and viewport");
            var recreationY = RowY(60);
            await presenter.HandleControllerButtonAsync(ControllerButton.Y);
            await Until(() => Shown("YouTube Music · Settings"), "settings before recreation");
            var memory = presenter.CapturePresentationState()!;
            Check(memory.CollectionReturns.Any(item => item.Element.Id == "music.scroll.home" && item.Index == 60),
                "host memento includes value-only return geometry while the original page is absent");
            var old = presenter;
            await old.SetPresentationActiveAsync(false);
            await session!.SetLifecycleAsync(session.GetTarget("indexed-owned"), WidgetRail.WidgetSdk.WidgetLifecycleState.Background, lifetime.Token);
            var layout = (Grid)Content;
            layout.Children.Remove(old);
            await old.DisposeAsync();
            presenter = new() { Session = session, Failed = old.Failed, DispatchActionAsync = old.DispatchActionAsync, FocusTrace = old.FocusTrace };
            await presenter.SetPresentationActiveAsync(false);
            presenter.SetAutomaticFocusEnabled(false);
            Grid.SetRow(presenter, 1); layout.Children.Add(presenter);
            await session.EstablishPresentationAsync(session.GetTarget("indexed-owned"), WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive, lifetime.Token);
            AdoptCurrentPresentation();
            await Until(() => presenter.IsLoaded && Shown("YouTube Music · Settings"), "recreated settings");
            Check(presenter.RestorePresentationState(memory), "same-owner recreation accepts return memory");
            Check(!presenter.RestorePresentationState(memory with { Owner = memory.Owner with { Runtime = "other" } }),
                "another runtime cannot supply return memory");
            await presenter.SetPresentationActiveAsync(true);
            presenter.SetAutomaticFocusEnabled(true);
            await Until(() => presenter.FocusHostTarget("panel.back"), "recreated Back focus");
            presenter.ActivateFocused();
            await Until(() => Shown("Artists") && FocusIndex() == 60, "return after native recreation");
            await SettleViewport();
            observations.Add(new { phase = "recreated-return", beforeY = recreationY, afterY = RowY(60) });
            Check(Math.Abs(RowY(60) - recreationY) < 2, "return after native recreation preserves the opener's screen position");
        }
        catch (Exception exception) { error = exception; }
        await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(new { mode, passed = error is null && failures.Count == 0, checks, failures, observations, trace = traces.ToArray(), error = error?.ToString() }));
        if (error is not null) throw error;
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures));
    }
}
