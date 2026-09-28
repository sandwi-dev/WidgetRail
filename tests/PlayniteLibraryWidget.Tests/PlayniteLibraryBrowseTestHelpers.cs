using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using LauncherWidget = WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    // Membership assertions inspect the frozen logical query even while Home is
    // active. Presentation/action tests explicitly acquire SDK ranges instead.
    private static IReadOnlyList<PlayniteLibraryItem> BrowseItems(LauncherWidget widget)
    {
        var query = widget.RenderState.Value.IndexedBrowse.Publication?.Query;
        if (query is null) return [];
        var result = new List<PlayniteLibraryItem>();
        for (var start = 0; start < query.Count; start += 64)
            result.AddRange(query.ReadRange(start, Math.Min(64, query.Count - start)).Select(row => row.Item));
        return result;
    }

    private static IReadOnlyList<PlayniteLibraryItem> CurrentItems(LauncherWidget widget) =>
        Nodes(widget.Render().CreateSnapshot("query-probe", 1).Root).Any(node => node.Kind == ViewNodeKind.IndexedCollection)
            ? BrowseItems(widget) : widget.Collection.Items;

    private static IReadOnlyList<ViewNode> BrowseNodes(LauncherWidget widget)
    {
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "browse-author-test");
        var declaration = Nodes(host.CurrentSnapshot.Root).SingleOrDefault(node => node.Kind == ViewNodeKind.IndexedCollection);
        if (declaration?.IndexedCollection is not { } source) return [];
        var result = new List<ViewNode>();
        for (var start = 0; start < source.Count; start += 64)
        {
            using var lease = host.AcquireAsync(declaration.Id, start, Math.Min(64, source.Count - start)).AsTask().GetAwaiter().GetResult();
            result.AddRange(lease.Range.Items.SelectMany(row => Nodes(row.Root)));
        }
        return result;
    }
}
