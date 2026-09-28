using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Windows.Foundation;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Real worker, real indexed data and native XAML modal lifetime regression.</summary>
internal static class IndexedModalValidation
{
    internal sealed record Result(string ResultCode, IReadOnlyList<string> Checks, string? Error);

    internal static async Task<Result> RunAsync(WidgetViewPresenter presenter, PresentationSession session,
        CancellationToken cancellationToken)
    {
        var checks = new List<string>();
        var originalWidth = presenter.Width;
        var originalHeight = presenter.Height;
        var originalTransform = presenter.RenderTransform;
        Result result;
        try
        {
            await Send("modal-mode", "root");
            await Until(() => FocusId() == "Widget.items.Item.75" && Summary() == "Summary 200:75");
            var collection = Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single();
            var view = collection.NativeView;
            var scroll = Descendants(view).OfType<ScrollViewer>().First();
            await Until(() => ((IndexedItem<WidgetIndexedRow>)view.Items[75]).Value is not null && Artwork() is not null);
            // Focus and artwork readiness precede completion of native deep
            // ScrollIntoView. Capture the baseline after that setup has settled.
            await SettleViewport(scroll);
            var originalRow = ((IndexedItem<WidgetIndexedRow>)view.Items[75]).Value!;
            var container = (Control)view.ContainerFromIndex(75);
            var retainedArtwork = Artwork();
            var source = Node("items")!.IndexedCollection!;
            var target = new IndexedCollectionFocusTarget("items", source.SourceId, source.QueryGeneration, "item.75", 75);
            var originalItems = view.ItemsSource;
            var originalBounds = Bounds(container, scroll);
            var offset = scroll.VerticalOffset;
            var viewport = scroll.ViewportHeight;
            var initialCalls = Calls();
            Check(offset > 0 && container.IsLoaded && originalRow.Lease.IsCurrent, "deep native row and semantic lease are ready");

            await Button(ControllerButton.A);
            await Until(() => InModal() && FocusId() == "Widget.modal-play");
            await Task.Delay(100, cancellationToken);
            Check(Calls() == initialCalls + 1 && Node("status")?.Text == "row:200:75:open", "row A opens exact captured game once through worker queue");
            Check(ReferenceEquals(collection.NativeView, view) && ReferenceEquals(view.ItemsSource, originalItems) &&
                ReferenceEquals(view.ContainerFromIndex(75), container), "modal reparent retains indexed view source and container identity");
            Check(Near(scroll.VerticalOffset, offset) && Near(scroll.ViewportHeight, viewport) && Near(Bounds(container, scroll).Y, originalBounds.Y),
                "opening preserves deep parent viewport and row position");
            Check(originalRow.Lease.IsCurrent && ReferenceEquals(retainedArtwork, Artwork()) && Summary() == "Summary 200:75",
                "inactive parent keeps live lease and exact retained background and summary");
            Check(view.IsEnabled && !view.IsHitTestVisible && !container.IsTabStop && !view.IsItemClickEnabled,
                "inactive native collection blocks input without disabled-state dimming");
            container.Focus(FocusState.Keyboard);
            Check(FocusId() == "Widget.modal-play" && !collection.Enter(target) &&
                !collection.MoveFocus(FocusNavigationDirection.Up) && !collection.InvokeFocused(ControllerButton.A),
                "inactive indexed focus entry navigation and invocation are rejected");
            var rejectedScope = false;
            try { await originalRow.Lease.AdmitInputAsync(Frame().Authority, originalRow.Item.Key, ControllerButton.A, cancellationToken: cancellationToken); }
            catch (WidgetPresentationSessionException error) when (error.Code == "input_scope_stale") { rejectedScope = true; }
            Check(rejectedScope && Calls() == initialCalls + 1, "session rejects parent lease input while its data remains current");
            await Button(ControllerButton.X);
            await Task.Delay(100, cancellationToken);
            Check(Calls() == initialCalls + 1 && InModal(), "parent indexed shortcut cannot leak through modal scope");

            await Button(ControllerButton.A);
            await Until(() => Node("status")?.Text == "modal-play");
            Check(Calls() == initialCalls + 2 && FocusId() == "Widget.modal-play", "modal A dispatches exactly once in active scope");
            var update = Find<Button>("modal-update");
            update.Focus(FocusState.Keyboard);
            await Button(ControllerButton.A);
            await Until(() => Node("modal-status")?.Text == "Item 75; revision 1" && Summary() == "Summary 201:75");
            Check(ReferenceEquals(update, Find<Button>("modal-update")) && FocusId() == "Widget.modal-update" &&
                Near(scroll.VerticalOffset, offset), "worker content refresh preserves modal control and parent anchor");
            await Until(() => RowText(container).Contains("Item 75", StringComparison.Ordinal) && HasRowArtwork(container));
            Check(true, "reparented indexed row restores text and artwork after refresh");

            await Button(ControllerButton.B);
            await Until(() => !InModal() && FocusId() == "Widget.items.Item.75");
            Check(Calls() == initialCalls + 4 && Node("status")?.Text == "modal-dismiss", "modal B closes once without invoking parent B shortcut");
            Check(ReferenceEquals(container, view.ContainerFromIndex(75)) && Near(scroll.VerticalOffset, offset) &&
                Near(Bounds(container, scroll).Y, originalBounds.Y), $"modal close restores exact indexed row and viewport [sameContainer={ReferenceEquals(container, view.ContainerFromIndex(75))}; beforeOffset={offset:R}; afterOffset={scroll.VerticalOffset:R}; beforeBounds={originalBounds}; afterBounds={Bounds(container, scroll)}; beforeViewport={viewport:R}; afterViewport={scroll.ViewportHeight:R}]");
            Check(view.IsHitTestVisible && container.IsTabStop && view.IsItemClickEnabled, "parent native input reactivates on close");

            // Exercise repeated unload/reload without hiding stale failures behind a fresh query.
            for (var repetition = 0; repetition < 3; ++repetition)
            {
                await Button(ControllerButton.A);
                await Until(() => InModal() && FocusId() == "Widget.modal-play");
                await Send("modal-update", "modal-update");
                await Until(() => Node("modal-status")?.Text == "Item 75; revision 1");
                await Button(ControllerButton.B);
                await Until(() => !InModal() && FocusId() == "Widget.items.Item.75");
                await Until(() => HasRowArtwork((DependencyObject)view.ContainerFromIndex(75)));
                Check(Near(scroll.VerticalOffset, offset) && RowText(container).Contains("Item 75", StringComparison.Ordinal),
                    $"repeated modal cycle {repetition + 1} preserves parent row content and viewport");
            }

            // Smaller native viewport plus a non-default presentation transform.
            // This is not a claim of monitor-DPI coverage.
            presenter.Width = Math.Min(600, presenter.ActualWidth - 40);
            presenter.Height = Math.Min(340, presenter.ActualHeight - 30);
            presenter.RenderTransform = new ScaleTransform { ScaleX = 1.15, ScaleY = 1.15 };
            await Task.Delay(120, cancellationToken);
            view.ScrollIntoView(view.Items[75], ScrollIntoViewAlignment.Default);
            await Until(() => view.ContainerFromIndex(75) is Control { IsLoaded: true });
            ((Control)view.ContainerFromIndex(75)).Focus(FocusState.Keyboard);
            // The row may already be realized. That does not mean the explicit
            // ScrollIntoView/focus reveal above has finished moving its viewport.
            // Measure the modal's effect only after that setup movement settles.
            await SettleViewport(scroll);
            var smallOffset = scroll.VerticalOffset;
            await Button(ControllerButton.A);
            await Until(() => InModal() && FocusId() == "Widget.modal-play");
            var layer = Descendants(presenter).OfType<WidgetModalLayer>().Single();
            await Until(() => layer.OpeningMotion is not null);
            await layer.OpeningMotion!.WaitAsync(cancellationToken);
            var panel = Descendants(presenter).OfType<WidgetModalPanel>().Single();
            var panelBounds = Bounds(panel, presenter);
            Check(panelBounds.X >= 15 && panelBounds.Y >= 15 && panelBounds.Right <= presenter.ActualWidth - 15 &&
                panelBounds.Bottom <= presenter.ActualHeight - 15 && Near(scroll.VerticalOffset, smallOffset),
                $"scaled smaller viewport keeps modal inside widget and parent offset unchanged [panel={panelBounds}; widget={presenter.ActualWidth}x{presenter.ActualHeight}; oldOffset={smallOffset}; offset={scroll.VerticalOffset}]");
            await Button(ControllerButton.B);
            await Until(() => !InModal() && FocusId() == "Widget.items.Item.75");
            Check(Near(scroll.VerticalOffset, smallOffset), $"scaled modal dismissal preserves indexed viewport [beforeOffset={smallOffset:R}; afterOffset={scroll.VerticalOffset:R}; viewport={scroll.ViewportHeight:R}; bounds={Bounds((FrameworkElement)view.ContainerFromIndex(75), scroll)}]");

            await Button(ControllerButton.A);
            await Until(() => InModal() && FocusId() == "Widget.modal-play");
            var retiredRow = ((IndexedItem<WidgetIndexedRow>)view.Items[75]).Value!;
            var oldGeneration = Node("items")!.IndexedCollection!.QueryGeneration;
            await Send("modal-replace", "modal-replace");
            await Until(() => Node("items")!.IndexedCollection!.QueryGeneration > oldGeneration && !retiredRow.Lease.IsCurrent);
            await Until(() => Summary() is null && Artwork() is null);
            Check(InModal() && FocusId() == "Widget.modal-play", "query replacement retires parent lease and presentation without stealing modal focus");
            var rejectedRetired = false;
            try { await retiredRow.Lease.AdmitInputAsync(Frame().Authority, retiredRow.Item.Key, ControllerButton.A, cancellationToken: cancellationToken); }
            catch (WidgetPresentationSessionException error) when (error.Code.Contains("stale", StringComparison.Ordinal) || error.Code.Contains("retired", StringComparison.Ordinal)) { rejectedRetired = true; }
            Check(rejectedRetired, "retired query lease cannot dispatch through replacement parent");
            await Button(ControllerButton.B);
            await Until(() => !InModal() && FocusId() == "Widget.items.Item.0" && Summary() == "Summary 210:0");
            Check(true, "changed query returns to valid new-query fallback rather than stale row target");
            await Button(ControllerButton.A);
            await Until(() => InModal() && Node("status")?.Text == "row:210:0:open");
            Check(true, "replacement row retains current action authority after modal lifecycle");
            await Button(ControllerButton.B);
            await Until(() => !InModal());
            await Until(() => view.ContainerFromIndex(0) is DependencyObject last &&
                RowText(last).Contains("Item 0", StringComparison.Ordinal) &&
                HasRowArtwork(last));
            Check(true, "final query row restores rendered text and artwork after modal close");
            result = new("passed", checks, null);
        }
        catch (Exception error) { result = new("failed", checks, error.ToString()); }
        finally
        {
            presenter.Width = originalWidth; presenter.Height = originalHeight; presenter.RenderTransform = originalTransform;
        }
        var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "indexed-modal-result.json"), JsonSerializer.Serialize(result));
        return result;

        WidgetPresentationFrame Frame() => session.GetState("indexed-owned")?.LastGood ?? throw new InvalidOperationException("Missing indexed frame");
        ViewNode? Node(string id) => FindNode(Frame().Snapshot.Root, id);
        bool InModal() => Frame().Snapshot.Root.Kind == ViewNodeKind.ModalLayer;
        int Calls() => int.Parse(Node("calls")!.Text!["Calls: ".Length..], System.Globalization.CultureInfo.InvariantCulture);
        string FocusId() => FocusManager.GetFocusedElement(presenter.XamlRoot) is DependencyObject current ? AutomationProperties.GetAutomationId(current) : string.Empty;
        string? Summary() => Descendants(presenter).OfType<TextBlock>().Select(text => text.Text).FirstOrDefault(text => text.StartsWith("Summary ", StringComparison.Ordinal));
        ImageSource? Artwork() => Descendants(presenter).OfType<WidgetPresentationSurface>().Select(surface => surface.ArtworkSource).FirstOrDefault(image => image is not null);
        T Find<T>(string id) where T : FrameworkElement => Descendants(presenter).OfType<T>().Single(element => AutomationProperties.GetAutomationId(element) == "Widget." + id);
        async Task Send(string action, string source)
        {
            var frame = Frame();
            await session.SendActionAsync(frame.Authority, new(action, source, InputScopeId: frame.Authority.ActiveInputScopeId), cancellationToken);
        }
        Task<bool> Button(ControllerButton button) => presenter.HandleControllerButtonAsync(button,
            origin: ControllerInputOrigin.AccessibilityAutomation, cancellationToken: cancellationToken);
        async Task Until(Func<bool> condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? waitingFor = null)
        {
            var deadline = Environment.TickCount64 + 7000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException($"Indexed modal did not settle: waiting={waitingFor}, focus={FocusId()}, status={Node("status")?.Text}, summary={Summary()}, calls={Calls()}, row={DescribeRow()}");
                await Task.Delay(20, cancellationToken);
            }
        }
        string DescribeRow()
        {
            var view = Descendants(presenter).OfType<ListViewBase>().FirstOrDefault();
            var focus = FocusId();
            var index = int.TryParse(focus[(focus.LastIndexOf('.') + 1)..], out var focusedIndex) ? focusedIndex : 0;
            if (view?.ContainerFromIndex(index) is not DependencyObject container) return "unrealized";
            return JsonSerializer.Serialize(Descendants(container).OfType<WidgetIndexedRowView>().Select(row => new
            {
                row.IsLoaded, row.ActualWidth, row.ActualHeight,
                key = (row.Row as WidgetIndexedRow)?.Item.Key,
                lease = (row.Row as WidgetIndexedRow)?.Lease.IsCurrent,
                content = row.Content?.GetType().Name,
                texts = Descendants(row).OfType<TextBlock>().Select(text => text.Text).ToArray(),
                images = Descendants(row).OfType<Image>().Select(image => new { identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(image), image.IsLoaded, source = image.Source?.GetType().Name, parent = VisualTreeHelper.GetParent(image)?.GetType().Name }).ToArray(),
            }));
        }
        async Task SettleViewport(ScrollViewer scroll)
        {
            var stable = 0;
            var previous = (scroll.VerticalOffset, scroll.ViewportHeight);
            var deadline = Environment.TickCount64 + 3000;
            while (stable < 4)
            {
                await Task.Delay(20, cancellationToken);
                var current = (scroll.VerticalOffset, scroll.ViewportHeight);
                stable = current == previous ? stable + 1 : 0;
                previous = current;
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Native setup viewport did not settle before modal opening");
            }
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
    }

    private static bool Near(double first, double second) => Math.Abs(first - second) <= 1;
    private static Rect Bounds(FrameworkElement element, UIElement relativeTo) =>
        element.TransformToVisual(relativeTo).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
    private static string RowText(DependencyObject row) => string.Join(' ', Descendants(row).OfType<TextBlock>().Select(text => text.Text));
    // The worker's cover is an 8x8 contrasting checker. Parent backgrounds use
    // different tiny swatches, so those cannot accidentally satisfy this check.
    private static bool HasRowArtwork(DependencyObject row) => Descendants(row).OfType<Image>().Any(image =>
        image.IsLoaded && image.ActualWidth > 0 && image.ActualHeight > 0 &&
        image.Source is BitmapImage { PixelWidth: 8, PixelHeight: 8 });
    private static ViewNode? FindNode(ViewNode node, string id) => node.Id == id ? node :
        node.Children.Select(child => FindNode(child, id)).FirstOrDefault(found => found is not null);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject element)
    {
        yield return element;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); ++index)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(element, index))) yield return child;
    }
}
