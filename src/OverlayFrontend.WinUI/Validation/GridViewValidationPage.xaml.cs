using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetUi.State.Collections;
using Windows.Foundation;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>
/// Native GridView retention experiment. Mutations only update ItemsSource;
/// observation reads geometry and never moves focus, changes offsets or forces layout.
/// </summary>
public sealed partial class GridViewValidationPage : Page
{
    private readonly KeyedObservableCollection<ValidationCollectionItem> collection = new(new("validation", "gridview", "items"));
    private readonly List<KeyedCollectionItem<ValidationCollectionItem>> items = [];
    private long revision;
    private int before;
    private int after = 1000;

    public GridViewValidationPage()
    {
        InitializeComponent();
        items.AddRange(Enumerable.Range(0, after).Select(Item));
        CollectionView.ItemsSource = collection.Items;
        KeyDown += HandleKeyDown;
        Apply();
    }

    private static KeyedCollectionItem<ValidationCollectionItem> Item(int index) => new($"item.{index}", new($"Item {index}"));

    private void Apply()
    {
        collection.Apply(new(collection.Authority, 0, ++revision, items));
        StatusText.Text = $"Items: {collection.Items.Count}; revision: {revision}";
    }

    private void HandleKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.F5:
                if (collection.TryGetEntry("item.600", out var deep)) CollectionView.ScrollIntoView(deep, ScrollIntoViewAlignment.Leading);
                break;
            case VirtualKey.F6: Observe(); break;
            case VirtualKey.F7:
                before -= 30;
                items.InsertRange(0, Enumerable.Range(before, 30).Select(Item));
                Apply();
                break;
            case VirtualKey.F8:
                items.AddRange(Enumerable.Range(after, 30).Select(Item));
                after += 30;
                Apply();
                break;
            case VirtualKey.F9:
                var focusedIndex = items.FindIndex(item => item.Key == FocusedEntry()?.Key);
                if (focusedIndex >= 30) { items.RemoveRange(0, 30); Apply(); }
                else StatusText.Text = "Eviction requires focus beyond first 30 records";
                break;
            case VirtualKey.F10:
                var index = items.FindIndex(item => item.Key == FocusedEntry()?.Key);
                if (index >= 0)
                {
                    items[index] = items[index] with { Value = new($"Updated {items[index].Key}") };
                    Apply();
                }
                break;
            default: return;
        }
        args.Handled = true;
    }

    private ObservableCollectionEntry<ValidationCollectionItem>? FocusedEntry() =>
        (FocusManager.GetFocusedElement(XamlRoot) as GridViewItem)?.Content as ObservableCollectionEntry<ValidationCollectionItem>;

    private void ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is ObservableCollectionEntry<ValidationCollectionItem> entry)
        {
            AutomationProperties.SetAutomationId(args.ItemContainer, entry.Key);
            args.ItemContainer.SetBinding(AutomationProperties.NameProperty, new Binding
            {
                Source = entry, Path = new PropertyPath("Value.Title"), Mode = BindingMode.OneWay,
            });
        }
    }

    private void ItemClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is ObservableCollectionEntry<ValidationCollectionItem> entry)
            StatusText.Text = $"Invoked: {entry.Key}";
    }

    private void Observe()
    {
        var nodes = Descendants(CollectionView).ToArray();
        var viewer = nodes.OfType<ScrollViewer>().FirstOrDefault();
        var focus = FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement;
        var key = FocusedEntry()?.Key;
        var attached = focus is not null && nodes.Contains(focus);
        var bounds = attached && viewer is not null
            ? focus!.TransformToVisual(viewer).TransformBounds(new Rect(0, 0, focus.ActualWidth, focus.ActualHeight))
            : default;
        var visible = attached && viewer is not null && bounds.Width > 0 && bounds.Height > 0 &&
            bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= viewer.ViewportWidth + 1 && bounds.Bottom <= viewer.ViewportHeight + 1;
        ObservationText.Text = JsonSerializer.Serialize(new
        {
            revision, logical = collection.Items.Count,
            realized = nodes.OfType<GridViewItem>().Count(), focusKey = key, attached, fullyVisible = visible,
            x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height,
            offset = viewer?.VerticalOffset, viewportWidth = viewer?.ViewportWidth, viewportHeight = viewer?.ViewportHeight,
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            yield return node;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); ++index)
                pending.Push(VisualTreeHelper.GetChild(node, index));
        }
    }
}
