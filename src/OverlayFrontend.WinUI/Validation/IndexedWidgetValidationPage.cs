using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;
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
using WidgetRail.OverlayPlatformClient;
using Windows.Foundation;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Real bridge and worker rows rendered by the shared native presenter.</summary>
internal sealed class IndexedWidgetValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Connecting", MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly string pipe;
    private readonly CancellationTokenSource lifetime = new();
    private PresentationSession? session;
    private Task? startup;
    private long publication;
    private string? failure;
    private string navigation = "pending";
    private string logicalFocus = "not-run";
    private string groupedFocus = "not-run";
    private bool retired;

    public IndexedWidgetValidationPage(string pipe)
    {
        this.pipe = pipe;
        AutomationProperties.SetAutomationId(status, "IndexedWidget.Status");
        var layout = new Grid { RowSpacing = 8 };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        Grid.SetRow(presenter, 1);
        layout.Children.Add(status); layout.Children.Add(presenter);
        Content = layout;
        presenter.Failed = error => { failure = error.Message; Observe(); };
        presenter.DispatchActionAsync = async request =>
        {
            try { if (session is not null) await session.SendActionAsync(request.Authority, request.Action, lifetime.Token); }
            catch (Exception error) { failure = error.Message; Observe(); }
        };
        Loaded += (_, _) => startup ??= StartAsync();
        KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case VirtualKey.F1: _ = ProbeGroupedFocusAsync(); break;
                case VirtualKey.F2: _ = ProbeLogicalFocusAsync(); break;
                case VirtualKey.F5: if (View() is { } list) list.ScrollIntoView(list.Items[70], ScrollIntoViewAlignment.Leading); break;
                case VirtualKey.F4: _ = ProbeNavigationAsync(burst: true); break;
                case VirtualKey.F3:
                    presenter.MoveFocus(FocusNavigationDirection.Down);
                    Descendants(presenter).OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "Widget.parent").Focus(FocusState.Keyboard);
                    break;
                case VirtualKey.F6: Observe(); break;
                case VirtualKey.F7: presenter.ActivateFocused(); break;
                case VirtualKey.F8: presenter.InvokeIndexedShortcut(ControllerButton.X); break;
                case VirtualKey.F9: presenter.MoveFocus(FocusNavigationDirection.Down); break;
                case VirtualKey.F10: _ = ProbeNavigationAsync(); break;
                case VirtualKey.F11: _ = RefreshContentAsync(); break;
                case VirtualKey.F12: _ = ProbeNavigationAsync(reverse: true); break;
                default: return;
            }
            args.Handled = true;
        };
    }

    private async Task StartAsync()
    {
        try
        {
            session = await PresentationSession.ConnectAsync(pipe, cancellationToken: lifetime.Token);
            presenter.Session = session;
            session.PresentationChanged += Changed;
            await session.ListWidgetsAsync(lifetime.Token);
            await session.EstablishPresentationAsync(session.GetTarget("indexed-owned"), WidgetLifecycleState.Interactive, lifetime.Token);
        }
        catch (Exception error) { failure = error.Message; Observe(); }
    }
    private void Changed(object? sender, WidgetPresentationChangedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (retired || args.State.PublicationRevision <= publication) return;
            publication = args.State.PublicationRevision;
            try
            {
                if (args.State.Failure is { } error) throw new InvalidOperationException(error.Message);
                if (args.State.LastGood is { } frame) presenter.Apply(frame);
            }
            catch (Exception error) { failure = error.Message; }
            Observe();
        });
    }
    private ListViewBase? View() => Descendants(presenter).OfType<ListViewBase>().FirstOrDefault();
    private async Task RefreshContentAsync()
    {
        try
        {
            if (session?.GetState("indexed-owned")?.LastGood is { } frame)
                await session.SendActionAsync(frame.Authority, new("content", "content", InputScopeId: "root"), lifetime.Token);
        }
        catch (Exception error) { failure = error.Message; Observe(); }
    }
    private async Task ProbeNavigationAsync(bool reverse = false, bool burst = false)
    {
        try
        {
            if (View() is not { } view) return;
            navigation = "pending";
            var samples = await NativeIndexedFocusProbe.RunAsync(view, frame => presenter.MoveFocus(
                    frame.DpadNavigation.Direction == NavigationDirection.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down),
                startIndex: reverse ? 70 : 0, steps: reverse ? 20 : view is GridView ? 12 : 60,
                settleMilliseconds: burst ? 0 : 35, cancellationToken: lifetime.Token,
                direction: reverse ? NavigationDirection.Up : NavigationDirection.Down);
            navigation = $"last:{samples[^1].FocusedIndex};stalled:{samples.Zip(samples.Skip(1)).Count(pair => pair.First.FocusedIndex == pair.Second.FocusedIndex)}";
        }
        catch (Exception error) { navigation = "failed:" + error.Message; }
        Observe();
    }

    private async Task ProbeLogicalFocusAsync()
    {
        logicalFocus = "pending";
        try
        {
            Parent();
            await Send("focus-exact");
            await Until(() => FocusId() == "Widget.items.Item.75");
            Parent();
            await Send("content", "content");
            await Task.Delay(300, lifetime.Token);
            Check(FocusId() == "Widget.parent", "consumed request replayed on content update");
            await Send("focus-default");
            await Until(() => FocusId() == "Widget.items.Item.75");
            Parent();
            presenter.MoveFocus(FocusNavigationDirection.Down);
            await Until(() => FocusId() == "Widget.items.Item.75");
            Parent();
            await Send("focus-wrong");
            await Task.Delay(500, lifetime.Token);
            Check(FocusId() == "Widget.parent", "wrong key moved focus");
            await Send("focus-stale");
            await Task.Delay(500, lifetime.Token);
            Check(FocusId() == "Widget.parent", "stale query moved focus");
            await Send("focus-default");
            await Until(() => FocusId() == "Widget.items.Item.0");
            Parent();
            await Send("focus-delayed");
            await Until(() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single().IsEntryPending);
            presenter.MoveFocus(FocusNavigationDirection.Left);
            Parent();
            await Task.Delay(500, lifetime.Token);
            Check(FocusId() == "Widget.parent", "superseded request stole focus");
            await Send("focus-delayed");
            await Until(() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single().IsEntryPending);
            await Send("focus-clear");
            Parent();
            await Task.Delay(300, lifetime.Token);
            Check(FocusId() == "Widget.parent", "withdrawn request stole focus");
            await Send("focus-disabled");
            await Until(() => FocusId() == "Widget.items.Item.2");
            Parent();
            await Send("focus-delayed");
            await Until(() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single().IsEntryPending);
            await Send("grid", "grid");
            await Until(() => FocusId() == "Widget.items.Item.75");
            await Send("grid", "grid");
            Parent();
            await Send("focus-delayed");
            await Until(() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single().IsEntryPending);
            await Send("group-partition");
            await Until(() => FocusId() == "Widget.items.Item.75");
            await Send("groups", "groups");
            await Send("group-partition");
            await Send("groups", "groups");
            logicalFocus = "passed:12";
        }
        catch (Exception error) { logicalFocus = "failed:" + error.Message; }
        Observe();

        string FocusId() => XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject current
            ? "" : AutomationProperties.GetAutomationId(current);
        void Parent() => Descendants(presenter).OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "Widget.parent").Focus(FocusState.Keyboard);
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Until(Func<bool> ready)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!ready())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Logical focus did not settle: " + FocusId());
                await Task.Delay(20, lifetime.Token);
            }
        }
        async Task Send(string action, string owner = "root")
        {
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            var previous = publication;
            await session.SendActionAsync(frame.Authority, new(action, owner, InputScopeId: "root"), lifetime.Token);
            await Until(() => publication > previous);
        }
    }

    private async Task ProbeGroupedFocusAsync()
    {
        groupedFocus = "pending";
        Observe();
        try
        {
            if (View() is not GridView view || view.ItemsPanelRoot is not ItemsWrapGrid { MaximumRowsOrColumns: 3 })
                throw new InvalidOperationException("Grouped probe requires its three-column grid fixture.");
            foreach (var (start, direction, expected) in new[]
            {
                (2, NavigationDirection.Down, 4), // final partial row in first group
                (4, NavigationDirection.Down, 6), // same column across empty group
                (6, NavigationDirection.Up, 4),
                (5, NavigationDirection.Up, 3),
                (11, NavigationDirection.Down, 12),
                (12, NavigationDirection.Up, 11),
            })
            {
                var samples = await NativeIndexedFocusProbe.RunAsync(view,
                    frame => presenter.MoveFocus(frame.DpadNavigation.Direction == NavigationDirection.Up
                        ? FocusNavigationDirection.Up : FocusNavigationDirection.Down), start, 1, 150, lifetime.Token, direction);
                if (samples[^1].FocusedIndex != expected) throw new InvalidOperationException($"Grouped move from {start}: expected {expected}, got {samples[^1].FocusedIndex}.");
            }
            var previousSource = view.ItemsSource;
            var scroll = Descendants(view).OfType<ScrollViewer>().First();
            var offset = scroll.VerticalOffset;
            var focused = FocusManager.GetFocusedElement(XamlRoot);
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            var revision = publication;
            await session.SendActionAsync(frame.Authority, new("group-label", "root", InputScopeId: "root"), lifetime.Token);
            var deadline = Environment.TickCount64 + 5000;
            while (publication == revision)
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Header change was not published.");
                await Task.Delay(20, lifetime.Token);
            }
            await Task.Delay(150, lifetime.Token);
            if (!ReferenceEquals(previousSource, view.ItemsSource) || !ReferenceEquals(focused, FocusManager.GetFocusedElement(XamlRoot)) ||
                Math.Abs(offset - scroll.VerticalOffset) > 1) throw new InvalidOperationException("Header text update replaced focus/source or moved viewport.");
            groupedFocus = "passed:7";
        }
        catch (Exception error) { groupedFocus = "failed:" + error.Message; }
        Observe();
    }
    private void Observe()
    {
        if (retired) return;
        var nodes = Descendants(presenter).ToArray();
        var view = nodes.OfType<ListViewBase>().FirstOrDefault();
        var scroll = view is null ? null : Descendants(view).OfType<ScrollViewer>().FirstOrDefault();
        var focus = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement;
        var bounds = focus is null || scroll is null ? default : focus.TransformToVisual(scroll).TransformBounds(new Rect(0, 0, focus.ActualWidth, focus.ActualHeight));
        var frame = session?.GetState("indexed-owned")?.LastGood;
        status.Text = JsonSerializer.Serialize(new
        {
            failure, publication, count = view?.Items.Count, control = view?.GetType().Name,
            realized = nodes.Count(node => node is ListViewItem or GridViewItem), images = nodes.OfType<Image>().Count(image => image.Source is not null),
            imageWidth = nodes.OfType<Image>().Select(image => image.ActualWidth).DefaultIfEmpty().Max(),
            focus = focus is null ? null : AutomationProperties.GetAutomationId(focus), y = bounds.Y,
            offset = scroll?.VerticalOffset, viewport = scroll?.ViewportHeight, height = view?.ActualHeight,
            status = frame?.Snapshot.Root.Children.Single(node => node.Id == "status").Text,
            calls = frame?.Snapshot.Root.Children.Single(node => node.Id == "calls").Text,
            columns = view?.ItemsPanelRoot is ItemsWrapGrid wrap ? wrap.MaximumRowsOrColumns : 1,
            revision = frame?.Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection?.ContentRevision,
            navigation, logicalFocus, groupedFocus,
            groupCount = frame?.Snapshot.Root.Children.Single(node => node.Id == "items").IndexedGroups?.Count ?? 0,
        });
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    public async ValueTask DisposeAsync()
    {
        retired = true; lifetime.Cancel();
        if (startup is not null) await startup;
        if (session is not null) session.PresentationChanged -= Changed;
        try { await presenter.DisposeAsync(); }
        finally
        {
            try { if (session is not null) await session.DisposeAsync(); }
            finally { lifetime.Dispose(); }
        }
    }
}
