using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class IndexedWidgetValidationPage
{
    private bool suspensionRunning;
    private async Task ProbeSuspensionAsync()
    {
        if (suspensionRunning || session is null) return;
        suspensionRunning = true;
        var result = new TextBlock { MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        AutomationProperties.SetAutomationId(result, "IndexedWidget.SuspensionResult");
        var layout = (Grid)Content;
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(result, layout.RowDefinitions.Count - 1); layout.Children.Add(result);
        try
        {
            var ownershipChecks = await IndexedSourceLifetimeScenarios.RunAsync(DispatcherQueue);
            var view = View() ?? throw new InvalidOperationException("Collection missing.");
            var source = (IndexedItemsSource<WidgetIndexedRow>)view.ItemsSource;
            view.ScrollIntoView(source[70], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(70) is Control { IsLoaded: true } &&
                ((IndexedItem<WidgetIndexedRow>)source[70]).Value?.Lease.IsCurrent == true);
            ((Control)view.ContainerFromIndex(70)).Focus(FocusState.Keyboard);
            await Task.Delay(100);
            var scroller = Descendants(view).OfType<ScrollViewer>().First();
            var offset = scroller.VerticalOffset;
            var slot = (IndexedItem<WidgetIndexedRow>)source[70];
            var lease = slot.Value!.Lease;
            var revision = source.ContentRevision;
            var count = source.Count;
            var structural = 0;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => ++structural;
            source.CollectionChanged += changed;
            try
            {
                var stop = presenter.SetPresentationActiveAsync(false);
                Check(!presenter.IsPresentationActive && !presenter.IsInteractionCurrent(slot.Value!.Owner.Frame.Authority), "suspension gates input synchronously");
                await stop;
                await session.SetLifecycleAsync(session.GetTarget("indexed-owned"), WidgetLifecycleState.Background, lifetime.Token);
                presenter.Visibility = Visibility.Collapsed;
                var loaded = source.CompletedLoads;
                await Task.Delay(200);
                Check(!lease.IsCurrent && source.CompletedLoads == loaded, "hidden collection releases authority and stops range demand");
                await session.EstablishPresentationAsync(session.GetTarget("indexed-owned"), WidgetLifecycleState.Interactive, lifetime.Token);
                if (session.GetState("indexed-owned")?.LastGood is { } next) presenter.Apply(next);
                presenter.Visibility = Visibility.Visible;
                await presenter.SetPresentationActiveAsync(true);
                presenter.Enter();
                await Until(() => slot.Value?.Lease.IsCurrent == true && !ReferenceEquals(slot.Value.Lease, lease));
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is FrameworkElement focused &&
                    AutomationProperties.GetAutomationId(focused) == "Widget.items.Item.70");
                Check(ReferenceEquals(View(), view) && ReferenceEquals(view.ItemsSource, source) && ReferenceEquals(source[70], slot),
                    "native view source and logical slot retain identity");
                Check(source.ContentRevision == revision && source.Count == count && structural == 0,
                    "resume reacquires same revision without structural reset");
                Check(Math.Abs(scroller.VerticalOffset - offset) < 2, "collapse/show preserves deep native viewport");
                result.Text = $"Passed presentation suspension; ownership checks:{ownershipChecks}; index:70; offset:{offset:F2}->{scroller.VerticalOffset:F2}";
            }
            finally { source.CollectionChanged -= changed; }
        }
        catch (Exception error) { result.Text = "Failed presentation suspension: " + error; }
        finally { suspensionRunning = false; }

        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        static async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 8000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Suspension scenario did not settle.");
                await Task.Delay(20);
            }
        }
    }
}
