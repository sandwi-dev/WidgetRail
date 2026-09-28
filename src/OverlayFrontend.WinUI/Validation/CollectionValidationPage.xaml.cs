using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

public sealed record ValidationCollectionItem(string Title);

/// <summary>Real WinUI collection control over the logical collection model.</summary>
public sealed partial class CollectionValidationPage : Page
{
    private readonly KeyedObservableCollection<ValidationCollectionItem> collection = new(new("validation", "collection", "items"));
    private readonly List<KeyedCollectionItem<ValidationCollectionItem>> items = [];
    private long revision;
    private int before;
    private int after = 1000;

    public CollectionValidationPage()
    {
        InitializeComponent();
        items.AddRange(Enumerable.Range(0, after).Select(Item));
        CollectionView.ItemsSource = collection.Items;
        // Replay page admission without moving focus to a toolbar command.
        KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.F7) { PrependClicked(this, new()); args.Handled = true; }
            else if (args.Key == Windows.System.VirtualKey.F8) { AppendClicked(this, new()); args.Handled = true; }
        };
        Apply();
    }
    private static KeyedCollectionItem<ValidationCollectionItem> Item(int index) => new($"item.{index}", new($"Item {index}"));
    private void Apply()
    {
        collection.Apply(new(collection.Authority, 0, ++revision, items));
        StatusText.Text = $"Items: {collection.Items.Count}; revision: {revision}";
    }
    private void AppendClicked(object sender, RoutedEventArgs e)
    {
        items.AddRange(Enumerable.Range(after, 30).Select(Item)); after += 30; Apply();
    }
    private void PrependClicked(object sender, RoutedEventArgs e)
    {
        before -= 30; items.InsertRange(0, Enumerable.Range(before, 30).Select(Item)); Apply();
    }
    private void RefreshClicked(object sender, RoutedEventArgs e)
    {
        items[0] = items[0] with { Value = new($"Updated {items[0].Key}") }; Apply();
    }
    private void ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ObservableCollectionEntry<ValidationCollectionItem> entry)
            StatusText.Text = $"Invoked: {entry.Key}";
    }
    private void InspectClicked(object sender, RoutedEventArgs e)
    {
        var count = 0;
        var pending = new Stack<DependencyObject>();
        pending.Push(CollectionView);
        while (pending.TryPop(out var node))
        {
            if (node is ItemContainer) ++count;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); ++i)
                pending.Push(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
        }
        RealizedText.Text = $"Realized: {count}; logical: {collection.Items.Count}";
    }
}
