using System.Collections.ObjectModel;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

/// <summary>Validates and freezes styles received across the trusted host/bridge transport.</summary>
internal static class BridgeRenderStyleContract
{
    internal static IReadOnlyDictionary<string, BridgeNodeRenderStyles> ValidateAndFreeze(
        IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles, IReadOnlySet<string> nodeIds, bool requireComplete)
    {
        if (styles is null || styles.Count > BridgeRenderStyleLimits.MaximumNodes ||
            requireComplete && styles.Count != nodeIds.Count)
            throw new BridgeProtocolException("Computed style map has an invalid node count.");
        var frozen = new Dictionary<string, BridgeNodeRenderStyles>(StringComparer.Ordinal);
        var total = 0;
        foreach (var (id, style) in styles)
        {
            if (!nodeIds.Contains(id) || style is null)
                throw new BridgeProtocolException("Computed style map contains a foreign or null node.");
            frozen.Add(id, new() { Base = Freeze(style.Base), Focused = Freeze(style.Focused), Pressed = Freeze(style.Pressed),
                GroupHeader = style.GroupHeader is null ? null : Freeze(style.GroupHeader) });
        }
        return new ReadOnlyDictionary<string, BridgeNodeRenderStyles>(frozen);

        IReadOnlyDictionary<string, BridgeComputedStyleValue> Freeze(IReadOnlyDictionary<string, BridgeComputedStyleValue>? state)
        {
            if (state is null || state.Count > BridgeRenderStyleLimits.MaximumPropertiesPerState ||
                (total += state.Count) > BridgeRenderStyleLimits.MaximumTotalProperties)
                throw new BridgeProtocolException("Computed style map exceeds its property budget.");
            var values = new Dictionary<string, BridgeComputedStyleValue>(StringComparer.Ordinal);
            foreach (var (name, value) in state)
            {
                if (name.Length is < 1 or > BridgeRenderStyleLimits.MaximumPropertyNameLength || value is null ||
                    !Enum.IsDefined(value.Kind) || value.Text is null || value.Text.Length > BridgeRenderStyleLimits.MaximumValueTextLength ||
                    value.Unit is { Length: > 16 } || value.Number is { } number && !double.IsFinite(number))
                    throw new BridgeProtocolException("Computed style property is invalid.");
                values.Add(name, value with { });
            }
            return new ReadOnlyDictionary<string, BridgeComputedStyleValue>(values);
        }
    }

    internal static IReadOnlySet<string> RangeNodeIds(IndexedCollectionRange range) =>
        NodeIds(range.Items.Select(item => (item.Root, string.Empty)));

    internal static IReadOnlySet<string> SnapshotNodeIds(ViewSnapshot snapshot) =>
        NodeIds(new[] { (snapshot.Root, string.Empty) }.Concat(snapshot.PinnedLayouts
            .Where(layout => layout.Root is not null).Select(layout => (layout.Root!, layout.Id + "/"))));

    private static IReadOnlySet<string> NodeIds(IEnumerable<(ViewNode Root, string Prefix)> roots)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(ViewNode Node, string Prefix)>();
        foreach (var (root, prefix) in roots) pending.Push((root, prefix));
        while (pending.TryPop(out var item))
        {
            var node = item.Node;
            if (node is null || ids.Count >= BridgeRenderStyleLimits.MaximumNodes || !ids.Add(item.Prefix + node.Id))
                throw new BridgeProtocolException("Computed style node identity is duplicate or oversized.");
            foreach (var child in node.Children) pending.Push((child, item.Prefix));
            if (node.FocusPresentation is { } focused) pending.Push((focused, item.Prefix));
            if (node.DefaultFocusPresentation is { } fallback) pending.Push((fallback, item.Prefix));
        }
        return ids;
    }
}
