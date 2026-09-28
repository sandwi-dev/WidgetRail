using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetUi.State.Collections;
using Windows.Foundation;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

public sealed record IndexedValidationItem(string Title, string Subtitle);

/// <summary>Native range-callback and cache-retention proof; no worker or hardware.</summary>
public sealed partial class IndexedCollectionValidationPage : Page, IAsyncDisposable
{
    private readonly IndexedItemsSource<IndexedValidationItem> source;
    private int invokes;
    private volatile TaskCompletionSource? heldBuffer;
    public IndexedCollectionValidationPage()
    {
        InitializeComponent();
        source = new(new(new("validation", "indexed.instance", "indexed"), 1), 1_000_000, DispatcherQueue, ReadAsync);
        CollectionView.ItemsSource = source;
        source.StateChanged += (_, _) => Observe();
        KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case VirtualKey.F5: CollectionView.ScrollIntoView(source[600_000], ScrollIntoViewAlignment.Leading); break;
                case VirtualKey.F6: Observe(); break;
                case VirtualKey.F7: CollectionView.ScrollIntoView(source[0], ScrollIntoViewAlignment.Leading); break;
                case VirtualKey.F8: heldBuffer = new(TaskCreationOptions.RunContinuationsAsynchronously); break;
                case VirtualKey.F9: heldBuffer?.TrySetResult(); break;
                case VirtualKey.F10: source.RefreshContent(source.ContentRevision + 1); break;
                case VirtualKey.F11: _ = CheckLifetimesAsync(); break;
                default: return;
            }
            args.Handled = true;
        };
    }

    private async Task<IndexedRangeResult<IndexedValidationItem>> ReadAsync(IndexedRangeRequest request, CancellationToken cancellation)
    {
        await Task.Delay(200, cancellation);
        if (request.StartIndex == 599_968 && heldBuffer is { } hold) await hold.Task.WaitAsync(cancellation);
        return new(request.Query, request.RequestId, request.StartIndex, Enumerable.Range(request.StartIndex, request.Count)
            .Select(index => new KeyedCollectionItem<IndexedValidationItem>($"item.{index}",
                new($"Item {index}", $"Details for row {index}; revision {request.ContentRevision}"))).ToArray(),
            request.ContentRevision, new ProbeLifetime(this));
    }

    private int acquired;
    private string lifetimeResult = "pending";
    private bool lifetimeStarted;
    private async Task CheckLifetimesAsync()
    {
        if (lifetimeStarted) return;
        lifetimeStarted = true;
        try { lifetimeResult = $"passed:{await IndexedSourceLifetimeScenarios.RunAsync(DispatcherQueue)}"; }
        catch (Exception error) { lifetimeResult = $"failed:{error.Message}"; }
        Observe();
    }
    private int released;
    private int duplicateReleases;
    private sealed class ProbeLifetime : IAsyncDisposable
    {
        private readonly IndexedCollectionValidationPage owner;
        private int disposed;
        public ProbeLifetime(IndexedCollectionValidationPage owner)
        { this.owner = owner; Interlocked.Increment(ref owner.acquired); }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) Interlocked.Increment(ref owner.duplicateReleases);
            await Task.Delay(25);
            Interlocked.Increment(ref owner.released);
        }
    }

    private void ContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not IndexedItem<IndexedValidationItem> item) return;
        AutomationProperties.SetAutomationId(args.ItemContainer, $"indexed.{item.Index}");
        args.ItemContainer.SetBinding(AutomationProperties.NameProperty, new Binding
        {
            Source = item, Path = new PropertyPath("Value.Title"), Mode = BindingMode.OneWay,
            FallbackValue = $"Loading item {item.Index}", TargetNullValue = $"Loading item {item.Index}",
        });
    }
    private void ItemClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is IndexedItem<IndexedValidationItem> { HasValue: true }) ++invokes;
        Observe();
    }
    private void Observe()
    {
        var nodes = Descendants(CollectionView).ToArray();
        var viewer = nodes.OfType<ScrollViewer>().FirstOrDefault();
        var focused = FocusManager.GetFocusedElement(XamlRoot) as ListViewItem;
        var item = focused?.Content as IndexedItem<IndexedValidationItem>;
        var bounds = focused is not null && viewer is not null
            ? focused.TransformToVisual(viewer).TransformBounds(new Rect(0, 0, focused.ActualWidth, focused.ActualHeight)) : default;
        StatusText.Text = JsonSerializer.Serialize(new
        {
            count = source.Count, callbacks = source.RangeNotifications, reads = source.IndexReads, enumerations = source.EnumerationCalls,
            resident = source.ResidentSlots, peak = source.PeakResidentSlots, loads = source.CompletedLoads, cancelled = source.CancelledLoads,
            failures = source.FailedLoads, realized = nodes.OfType<ListViewItem>().Count(), focus = item?.Index, loaded = item?.HasValue,
            revision = source.ContentRevision, detail = item?.Value?.Subtitle,
            lifetimeResult,
            acquired = Volatile.Read(ref acquired), released = Volatile.Read(ref released), duplicateReleases = Volatile.Read(ref duplicateReleases),
            y = bounds.Y, height = bounds.Height, offset = viewer?.VerticalOffset, viewport = viewer?.ViewportHeight, invokes,
        });
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>(); pending.Push(root);
        while (pending.TryPop(out var node))
        {
            yield return node;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); ++index) pending.Push(VisualTreeHelper.GetChild(node, index));
        }
    }
    public ValueTask DisposeAsync()
    {
        CollectionView.ItemsSource = null;
        return source.DisposeAsync();
    }
}
