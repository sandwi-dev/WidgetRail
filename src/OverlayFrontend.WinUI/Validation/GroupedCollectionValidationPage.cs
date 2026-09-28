using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetUi.State.Collections;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>
/// Isolated native grouped-source probe. No worker, custom layout or range forwarding
/// adapter hides whether CollectionViewSource itself forwards native demand.
/// </summary>
internal sealed class GroupedCollectionValidationPage : Page, IAsyncDisposable
{
    private readonly GridView view;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Height = 180 };
    private readonly CollectionViewSource grouped = new() { IsSourceGrouped = true };
    private readonly ObservableCollection<ProbeGroup> groups = [];
    private readonly bool flat;
    private readonly bool adapted;
    private readonly NativeGroupedRangeView? adaptedView;
    private bool disposed;
    private string focusResult = "not-requested";
    private readonly int contractChecks;

    public GroupedCollectionValidationPage(bool flatBaseline = false, bool useRangeAdapter = false)
    {
        flat = flatBaseline;
        adapted = useRangeAdapter;
        if (adapted) contractChecks = NativeGroupedRangeViewScenarios.Run();
        // Validation-only trusted static XAML: keeps this probe isolated in one source file.
        view = (GridView)XamlReader.Load("""
            <GridView xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      SelectionMode="None" IsItemClickEnabled="True"
                      ScrollViewer.HorizontalScrollMode="Disabled"
                      ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                      ScrollViewer.VerticalScrollMode="Enabled"
                      ScrollViewer.VerticalScrollBarVisibility="Auto">
                <GridView.ItemsPanel>
                    <ItemsPanelTemplate>
                        <ItemsWrapGrid Orientation="Horizontal" ItemWidth="150" ItemHeight="190"
                                       MaximumRowsOrColumns="4" GroupHeaderPlacement="Top" />
                    </ItemsPanelTemplate>
                </GridView.ItemsPanel>
                <GridView.GroupStyle>
                    <GroupStyle HidesIfEmpty="True">
                        <GroupStyle.HeaderTemplate>
                            <DataTemplate><TextBlock Text="{Binding}" FontSize="20" Margin="8,12" /></DataTemplate>
                        </GroupStyle.HeaderTemplate>
                    </GroupStyle>
                </GridView.GroupStyle>
                <GridView.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Width="130" Height="170" Spacing="8">
                            <TextBlock Text="{Binding Index}" />
                            <TextBlock Text="{Binding Value.Title, Mode=OneWay, FallbackValue='Awaiting native range', TargetNullValue='Awaiting native range'}"
                                       TextWrapping="Wrap" />
                            <TextBlock Text="{Binding Value.Subtitle, Mode=OneWay}" TextWrapping="Wrap" />
                        </StackPanel>
                    </DataTemplate>
                </GridView.ItemTemplate>
            </GridView>
            """);
        AutomationProperties.SetAutomationId(view, "Grouped.View");
        AutomationProperties.SetAutomationId(status, "Grouped.Status");
        for (var index = 0; index < 3; ++index)
        {
            var group = index;
            var source = adapted && group > 0 ? groups[0].Source : new IndexedItemsSource<IndexedValidationItem>(
                new(new("grouped-probe", "grouped.instance", $"group.{group}"), 1),
                adapted ? 30_000 : 10_000, DispatcherQueue, async (request, token) =>
                {
                    await Task.Delay(80, token);
                    return new(request.Query, request.RequestId, request.StartIndex,
                        Enumerable.Range(request.StartIndex, request.Count).Select(item =>
                            new KeyedCollectionItem<IndexedValidationItem>($"group.{group}.item.{item}",
                                new($"Group {(adapted ? item / 10000 : group)}, item {(adapted ? item % 10000 : item)}", $"Revision {request.ContentRevision}"))).ToArray(),
                        request.ContentRevision);
                });
            groups.Add(new($"Section {group}", source, adapted ? group * 10_000 : 0, 10_000));
        }
        grouped.Source = groups;
        if (adapted) adaptedView = new(grouped.View, groups[0].Source);
        view.ItemsSource = flat ? groups[0] : adaptedView is not null ? adaptedView : grouped.View;
        view.ContainerContentChanging += (_, args) =>
        {
            if (!args.InRecycleQueue && args.Item is IndexedItem<IndexedValidationItem> item)
                AutomationProperties.SetAutomationId(args.ItemContainer, $"Grouped.Item.{view.Items.IndexOf(item)}");
        };
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(new TextBlock
        {
            Text = $"{(flat ? "Flat baseline" : "Native grouped source")}: F5 deep; F6 inspect; F7 start; F8 focus target; F9 refresh",
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(view, 1); root.Children.Add(view);
        Grid.SetRow(status, 2); root.Children.Add(status);
        Content = root;
        Loaded += (_, _) => { view.Focus(FocusState.Keyboard); Observe(); };
        KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case VirtualKey.F5: view.ScrollIntoView(Target, ScrollIntoViewAlignment.Leading); break;
                case VirtualKey.F6: break;
                case VirtualKey.F7: view.ScrollIntoView(groups[0][0], ScrollIntoViewAlignment.Leading); break;
                case VirtualKey.F8:
                    focusResult = view.ContainerFromItem(Target) is Control control
                        ? control.Focus(FocusState.Keyboard).ToString() : "target-unrealized";
                    break;
                case VirtualKey.F9:
                    foreach (var source in groups.Select(group => group.Source).Distinct()) source.RefreshContent(source.ContentRevision + 1);
                    break;
                default: return;
            }
            args.Handled = true;
            Observe();
        };
    }

    private object Target => groups[flat ? 0 : 1][6000]!;

    private void Observe()
    {
        var nodes = Descendants(view).ToArray();
        var scrollers = nodes.OfType<ScrollViewer>().ToArray();
        var scroller = scrollers.FirstOrDefault();
        var focus = FocusManager.GetFocusedElement(XamlRoot) as GridViewItem;
        status.Text = JsonSerializer.Serialize(new
        {
            mode = flat ? "flat" : adapted ? "adapted" : "grouped",
            contractChecks,
            directSourceSupportsRanges = view.ItemsSource is IItemsRangeInfo,
            count = view.Items.Count,
            realized = nodes.OfType<GridViewItem>().Count(),
            scrollers = scrollers.Length,
            offset = scroller?.VerticalOffset,
            viewport = scroller?.ViewportHeight,
            columns = (view.ItemsPanelRoot as ItemsWrapGrid)?.MaximumRowsOrColumns,
            focus = focus?.Content is { } item ? view.Items.IndexOf(item) : -1,
            focusResult,
            groupEnumerationCalls = groups.Sum(group => group.EnumerationCalls),
            groups = groups.DistinctBy(group => group.Source).Select(group => new
            {
                group.Title, group.Source.RangeNotifications, group.Source.IndexReads,
                group.Source.EnumerationCalls, group.Source.ResidentSlots, group.Source.PeakResidentSlots,
                group.Source.CompletedLoads, group.Source.FailedLoads,
            }),
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        view.ItemsSource = null;
        adaptedView?.Dispose();
        grouped.Source = null;
        foreach (var source in groups.Select(group => group.Source).Distinct()) await source.DisposeAsync();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        yield return node;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); ++index)
            foreach (var descendant in Descendants(VisualTreeHelper.GetChild(node, index))) yield return descendant;
    }

    // Each group is itself the observable IList, avoiding ItemsPath/property-binding
    // ambiguity. Range calls, if WinUI makes them, reach the existing proven adapter.
    private sealed class ProbeGroup(string title, IndexedItemsSource<IndexedValidationItem> source, int offset, int count)
        : IList, INotifyCollectionChanged, IItemsRangeInfo
    {
        public string Title => title;
        public IndexedItemsSource<IndexedValidationItem> Source => source;
        public override string ToString() => title;
        public event NotifyCollectionChangedEventHandler? CollectionChanged
        { add => source.CollectionChanged += value; remove => source.CollectionChanged -= value; }
        public int EnumerationCalls { get; private set; }
        public int Count => count;
        public bool IsReadOnly => true;
        public bool IsFixedSize => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public object? this[int index]
        {
            get { if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index)); return source[offset + index]; }
            set => throw new NotSupportedException();
        }
        public int IndexOf(object? value) => source.IndexOf(value) - offset is var index && index >= 0 && index < count ? index : -1;
        public bool Contains(object? value) => IndexOf(value) >= 0;
        public IEnumerator GetEnumerator()
        {
            ++EnumerationCalls;
            return Enumerate().GetEnumerator();
            IEnumerable<object?> Enumerate() { for (var index = 0; index < count; ++index) yield return this[index]; }
        }
        public void CopyTo(Array array, int index) { for (var item = 0; item < count; ++item) array.SetValue(this[item], index + item); }
        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
        public void RangesChanged(ItemIndexRange visibleRange, IReadOnlyList<ItemIndexRange> trackedItems) =>
            source.RangesChanged(visibleRange, trackedItems);
        public void Dispose() => source.Dispose();
    }
}
