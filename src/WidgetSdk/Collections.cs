using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static partial class UI
{
    /// <summary>
    /// A host-realized list of direct keyed buttons or action surfaces. The host
    /// measures visible content; estimatedItemExtent is only a starting estimate.
    /// Use ordinary Scroll for heterogeneous/nested layouts. Cursor Capture().Present()
    /// and PresentItem() apply normally; item keys must already exist at construction.
    /// </summary>
    public static ScrollElement CollectionList(string id, double estimatedItemExtent,
        ScrollAxis axis = ScrollAxis.Vertical, params WidgetElement[] items) =>
        CreateCollection(id, axis, new CollectionLayout
        {
            Kind = CollectionLayoutKind.List,
            EstimatedItemExtent = estimatedItemExtent,
        }, items);

    /// <summary>
    /// A vertical host-realized adaptive grid. Direct keyed items retain logical
    /// order through column changes. Main-axis estimates exclude the row gap.
    /// The collection owns row/column placement; item WRSS controls item contents.
    /// </summary>
    public static ScrollElement CollectionGrid(string id, double minimumColumnWidth,
        double estimatedItemExtent, int? maximumColumns = null, params WidgetElement[] items) =>
        CreateCollection(id, ScrollAxis.Vertical, new CollectionLayout
        {
            Kind = CollectionLayoutKind.AdaptiveGrid,
            EstimatedItemExtent = estimatedItemExtent,
            MinimumColumnWidth = minimumColumnWidth,
            MaximumColumns = maximumColumns,
        }, items);

    private static ScrollElement CreateCollection(string id, ScrollAxis axis,
        CollectionLayout layout, WidgetElement[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!double.IsFinite(layout.EstimatedItemExtent) ||
            layout.EstimatedItemExtent < ProtocolConstants.MinimumVirtualCollectionItemExtent ||
            layout.EstimatedItemExtent > ProtocolConstants.MaximumVirtualCollectionItemExtent)
            throw new ArgumentOutOfRangeException(nameof(layout), "Collection estimate is outside the supported DIP range.");
        if (layout.Kind == CollectionLayoutKind.AdaptiveGrid &&
            (layout.MinimumColumnWidth is not { } width || !double.IsFinite(width) ||
             width < ProtocolConstants.MinimumGridColumnWidth || width > ProtocolConstants.MaximumGridColumnWidth ||
             layout.MaximumColumns is < 1 or > ProtocolConstants.MaximumGridColumns))
            throw new ArgumentOutOfRangeException(nameof(layout), "Collection column limits are outside the supported range.");
        if (items.Length > ProtocolConstants.MaximumCursorCollectionItems)
            throw new ArgumentOutOfRangeException(nameof(items), "Collection exceeds the retained descriptor limit.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string? anchor = null;
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            var node = item.ToProtocolNode();
            if (node.Kind is not (ViewNodeKind.Button or ViewNodeKind.ActionSurface) ||
                node.CollectionItemKey is not { } key || !keys.Add(key) ||
                node.VisibleWhen is not (null or ResponsiveVisibility.Always))
                throw new ArgumentException("Collection items must be direct buttons or action surfaces with unique stable keys.", nameof(items));
            anchor ??= key;
        }
        return new ScrollElement(id, axis, items.ToArray())
        {
            CollectionLayout = layout,
            CollectionAnchorKey = anchor,
        };
    }
}
