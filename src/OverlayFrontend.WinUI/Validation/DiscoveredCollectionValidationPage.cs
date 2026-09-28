using System.Collections.Specialized;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed class DiscoveredCollectionValidationPage(string pipe) : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Connecting discovered collection", TextWrapping = TextWrapping.Wrap };
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<string> checks = [];
    private readonly List<string> failures = [];
    private readonly List<NotifyCollectionChangedAction> notifications = [];
    private PresentationSession? session;
    private Task? running;
    private long publication;
    private bool retired;

    internal void Initialize()
    {
        AutomationProperties.SetAutomationId(status, "Discovered.Status");
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.Children.Add(status); root.Children.Add(presenter); Grid.SetRow(presenter, 1); Content = root;
        presenter.Failed = error => failures.Add(error.Message);
        presenter.DispatchActionAsync = request => session!.SendActionAsync(request.Displayed, request.Action, lifetime.Token);
        Loaded += (_, _) => running ??= RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            session = await PresentationSession.ConnectAsync(pipe, cancellationToken: lifetime.Token);
            presenter.Session = session;
            session.PresentationChanged += Changed;
            await session.ListWidgetsAsync(lifetime.Token);
            await session.EstablishPresentationAsync(session.GetTarget("discovered"), WidgetLifecycleState.Interactive, lifetime.Token);
            await Until(() => Native()?.ItemsSource is IndexedItemsSource<WidgetIndexedRow> { Count: >= 16 });
            var view = Native()!;
            var footerInk = new SolidColorBrush(Microsoft.UI.Colors.Coral);
            view.Foreground = footerInk;
            var footer = (StackPanel)view.Footer;
            Check(footer.Children.OfType<TextBlock>().All(item => ReferenceEquals(item.Foreground, footerInk)) &&
                footer.Children.OfType<Control>().All(item => ReferenceEquals(item.Foreground, footerInk)),
                "discovery footer follows the styled native collection foreground");
            view.ClearValue(Control.ForegroundProperty);
            var items = (IndexedItemsSource<WidgetIndexedRow>)view.ItemsSource;
            items.CollectionChanged += CollectionChanged;
            Check(items is Microsoft.UI.Xaml.Data.ISupportIncrementalLoading, "native ItemsSource implements platform incremental loading");
            Check(items.Count >= 16, "empty native list continues through an empty first provider page");
            await Until(() => view.ContainerFromIndex(0) is ListViewItem && ((IndexedItem<WidgetIndexedRow>)items[0]).Value is not null);
            await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), view.ContainerFromIndex(0)));
            Check(true, "initial entry intent survives zero discovered rows and reaches the first result");
            var slot = (IndexedItem<WidgetIndexedRow>)items[0];
            var oldLease = slot.Value!.Lease;
            ((Control)view.ContainerFromIndex(0)).Focus(FocusState.Keyboard);
            var focused = FocusManager.GetFocusedElement(XamlRoot);
            var scroller = Descendants(view).OfType<ScrollViewer>().First();
            var offset = scroller.VerticalOffset;
            var count = items.Count;
            await Until(() => items.HasMoreItems);
            await items.LoadMoreItemsAsync(16);
            await Until(() => items.Count > count);
            Check(ReferenceEquals(items, view.ItemsSource), "append preserves native ItemsSource identity");
            Check(ReferenceEquals(slot, items[0]), "append preserves prefix slot identity");
            Check(slot.Key == "item-0" && slot.Value!.Lease.IsCurrent, "the retained prefix slot has current captured row authority after partial-range expansion");
            Check(ReferenceEquals(focused, FocusManager.GetFocusedElement(XamlRoot)), "append preserves actual native focus");
            Check(Math.Abs(scroller.VerticalOffset - offset) < 1, "append preserves the native viewport without offset correction");
            Check(notifications.Count != 0 && notifications.All(action => action == NotifyCollectionChangedAction.Add), "append emits tail Add notifications and no Reset");

            await Send("fail-next");
            await Until(() => SessionFrame().Snapshot.Root.Children[0].Text == "fail-next");
            await Until(() => items.HasMoreItems);
            await items.LoadMoreItemsAsync(16);
            await Until(() => Descendants(view).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "Widget.items.RetryContinuation" && button.Visibility == Visibility.Visible));
            var retry = Descendants(view).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Widget.items.RetryContinuation");
            Check(!items.HasMoreItems && slot.Value!.Lease.IsCurrent, "provider failure stops automatic demand without retiring the prefix");
            retry.Focus(FocusState.Keyboard);
            count = items.Count;
            presenter.ActivateFocused();
            await Until(() => items.Count > count);
            Check(ReferenceEquals(items, view.ItemsSource), "normalized A on native Retry extends the existing source");

            await Send("delay-next");
            await Until(() => SessionFrame().Snapshot.Root.Children[0].Text == "delay-next");
            var tail = items.Count - 1;
            view.ScrollIntoView(items[tail], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(tail) is Control && ((IndexedItem<WidgetIndexedRow>)items[tail]).Value is not null);
            ((Control)view.ContainerFromIndex(tail)).Focus(FocusState.Keyboard);
            for (var press = 0; press < 8; ++press)
                Check(presenter.MoveFocus(FocusNavigationDirection.Down), "normalized forward input is contained at the undiscovered tail " + press);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), view.ContainerFromIndex(tail)), "held forward input does not leave the discovered row while loading");
            Check(presenter.MoveFocus(FocusNavigationDirection.Up), "reversal remains available during continuation loading");
            await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), view.ContainerFromIndex(tail - 1)));
            await Until(() => items.Count > tail + 1);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), view.ContainerFromIndex(tail - 1)), "page arrival does not replay prior held movement after reversal");

            await Until(() => items.HasMoreItems || SessionFrame().Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!.Discovery is { HasMore: false });
            while (items.HasMoreItems) await items.LoadMoreItemsAsync(16);
            Check(items.Count == 96 && !items.HasMoreItems, "provider exhaustion declares the exact discovered count without a fabricated total");
            view.ScrollIntoView(items[95], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(95) is Control && ((IndexedItem<WidgetIndexedRow>)items[95]).Value is not null);
            var realized = Enumerable.Range(0, items.Count).Count(index => view.ContainerFromIndex(index) is not null);
            Check(realized > 0 && realized < items.Count / 2, "deep native realization remains bounded below half of discovered metadata history");
            view.ScrollIntoView(items[0], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(0) is Control && ((IndexedItem<WidgetIndexedRow>)items[0]).Value is not null);
            Check(((IndexedItem<WidgetIndexedRow>)items[0]).Key == "item-0", "reverse navigation can realize the original discovered prefix");
            items.CollectionChanged -= CollectionChanged;
            await Send("replace");
            await Until(() => Native()?.ItemsSource is IndexedItemsSource<WidgetIndexedRow> replacement && !ReferenceEquals(replacement, items) && replacement.Count >= 16);
            Check(!oldLease.IsCurrent, "query replacement retires prefix leases and starts a new native source");
            var replacementView = Native()!;
            await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), replacementView.ContainerFromIndex(0)) && replacementView.ContainerFromIndex(0) is not null);
            Check(true, "explicit replacement entry reaches the first discovered result");
            await Send("replace");
            await Until(() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().FirstOrDefault() is { IsEntryPending: true, NativeView.ItemsSource: IndexedItemsSource<WidgetIndexedRow> { Count: 0 } });
            presenter.MoveFocus(FocusNavigationDirection.Up);
            var header = Descendants(presenter).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Widget.replace");
            header.Focus(FocusState.Keyboard);
            await Until(() => Native()?.ItemsSource is IndexedItemsSource<WidgetIndexedRow> { Count: >= 16 });
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), header), "new user navigation cancels empty-source entry before results arrive");

            var beforeGap = Native()!.ItemsSource;
            await Send("duplicate-gap");
            await Until(() => Native()?.ItemsSource is IndexedItemsSource<WidgetIndexedRow> { Count: >= 16 } gap && !ReferenceEquals(gap, beforeGap));
            var gapView = Native()!;
            var gapItems = (IndexedItemsSource<WidgetIndexedRow>)gapView.ItemsSource;
            await Until(() => gapView.ContainerFromIndex(0) is not null &&
                ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), gapView.ContainerFromIndex(0)) &&
                !Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single().IsEntryPending);
            gapView.ScrollIntoView(gapItems[15], ScrollIntoViewAlignment.Leading);
            await Until(() => gapItems.Count >= 32);
            Check(true, "native tail demand automatically crosses a duplicate-only page with a fresh continuation");
            await Send("empty-pages");
            await Until(() => Descendants(presenter).OfType<Button>().Any(button => Equals(button.Content, "Load more") && button.Visibility == Visibility.Visible));
            var paused = (IndexedItemsSource<WidgetIndexedRow>)Native()!.ItemsSource;
            Check(paused.Count == 0 && !paused.HasMoreItems, "four empty pages pause automatic demand without fabricating rows or exhaustion");
            var pausedRevision = SessionFrame().Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!.Discovery!.Revision;
            await Task.Delay(250, lifetime.Token);
            Check(SessionFrame().Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!.Discovery!.Revision == pausedRevision,
                "paused empty discovery does not continue spending provider quota in the background");
            var more = Descendants(presenter).OfType<Button>().Single(button => Equals(button.Content, "Load more"));
            await Until(() => more.IsLoaded && more.ActualWidth > 0 && more.ActualHeight > 0);
            more.Focus(FocusState.Keyboard); presenter.ActivateFocused();
            await Until(() => SessionFrame().Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!.Discovery!.Revision > pausedRevision &&
                Descendants(presenter).OfType<Button>().Any(button => Equals(button.Content, "Load more") && button.Visibility == Visibility.Visible));
            Check(ReferenceEquals(paused, Native()!.ItemsSource) && paused.Count == 0, "explicit Load more resumes a bounded empty-page burst on the same source");
            var oldFooter = Native()!.Footer;
            await Send("layout");
            await Until(() => Native() is GridView grid && ReferenceEquals(grid.Footer, oldFooter));
            Check(ReferenceEquals(paused, Native()!.ItemsSource), "list-to-grid layout replacement retains the query and transfers its footer");
            await Send("layout");
            await Until(() => Native() is ListView list && ReferenceEquals(list.Footer, oldFooter));
            Check(ReferenceEquals(paused, Native()!.ItemsSource), "grid-to-list layout replacement transfers the same native footer without double parenting");
            await Until(() => oldFooter is FrameworkElement { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } &&
                Descendants(Native()!).Contains((DependencyObject)oldFooter));
            var footerBounds = ((FrameworkElement)oldFooter).TransformToVisual(presenter).TransformBounds(
                new(0, 0, ((FrameworkElement)oldFooter).ActualWidth, ((FrameworkElement)oldFooter).ActualHeight));
            Check(footerBounds.Top >= 0 && footerBounds.Bottom <= presenter.ActualHeight + 1,
                "empty-source footer is realized inside the viewport after native layout replacement");
            status.Text = "Discovered collection checks passed: " + checks.Count;
            Write(new { passed = true, checks, failures, notifications, count = 96, footerBounds,
                foreground = (Native()!.Foreground as SolidColorBrush)?.Color.ToString(),
                style = SessionFrame().RenderStyles.GetValueOrDefault("items")?.Base.GetValueOrDefault("color")?.Text });
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error)
        {
            status.Text = "Discovered check failed: " + error.Message;
            Write(new { passed = false, checks, failures, error = error.ToString(),
                descriptor = session?.GetState("discovered")?.LastGood?.Snapshot.Root.Children.FirstOrDefault(node => node.Id == "items")?.IndexedCollection,
                focus = XamlRoot is null ? null : (FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement)?.GetType().Name,
                offset = Native() is { } native ? Descendants(native).OfType<ScrollViewer>().FirstOrDefault()?.VerticalOffset : null });
        }
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => notifications.Add(args.Action);
    private WidgetPresentationFrame SessionFrame() => session!.GetState("discovered")!.LastGood!;
    private Task Send(string action) => session!.SendActionAsync(SessionFrame().Authority, new(action, action, InputScopeId: "root"), lifetime.Token);
    private ListViewBase? Native() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().FirstOrDefault()?.NativeView;
    private void Changed(object? sender, WidgetPresentationChangedEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (retired || args.State.PublicationRevision <= publication) return;
        publication = args.State.PublicationRevision;
        try { if (args.State.LastGood is { } frame) presenter.Apply(frame); if (args.State.Failure is { } error) failures.Add(error.Message); }
        catch (Exception error) { failures.Add(error.Message); }
    });
    private async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 1000; ++i)
        {
            if (failures.Count != 0) throw new InvalidOperationException(failures[^1]);
            lifetime.Token.ThrowIfCancellationRequested(); if (condition()) return;
            await Task.Delay(20, lifetime.Token);
        }
        throw new TimeoutException("Native discovered condition did not settle.");
    }
    private void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static void Write<T>(T result)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "discovered-result.json"), JsonSerializer.Serialize(result));
    }
    public async ValueTask DisposeAsync()
    {
        if (retired) return; retired = true; lifetime.Cancel();
        if (session is not null) session.PresentationChanged -= Changed;
        if (running is not null) await running;
        await presenter.DisposeAsync(); if (session is not null) await session.DisposeAsync(); lifetime.Dispose();
    }
}
