namespace WidgetRail.WidgetProtocol;

/// <summary>Shared accounting for action layout and presentational rich-text runs.</summary>
internal struct ActionSurfaceContentBudget
{
    internal int LayoutNodes { get; private set; }
    internal int RichTextSpans { get; private set; }

    internal static bool IsInlineSpan(ViewNode node, ViewNodeKind? parentKind) =>
        parentKind == ViewNodeKind.RichText && node.Kind == ViewNodeKind.Text &&
        node.Children is { Count: 0 };

    internal void Add(ViewNode node, ViewNodeKind? parentKind)
    {
        if (IsInlineSpan(node, parentKind)) RichTextSpans++;
        else LayoutNodes++;
    }

    internal static bool RequiresExpandedBudget(ViewNode surface)
    {
        var pending = new Stack<(ViewNode Node, ViewNodeKind Parent, int Depth)>();
        foreach (var child in surface.Children ?? [])
            if (child is not null) pending.Push((child, surface.Kind, 1));
        var budget = new ActionSurfaceContentBudget();
        var visited = 0;
        while (pending.TryPop(out var entry) && ++visited <= ProtocolConstants.MaximumNodeCount)
        {
            budget.Add(entry.Node, entry.Parent);
            if (budget.RichTextSpans > 0 && budget.LayoutNodes + budget.RichTextSpans > ProtocolConstants.MaximumActionSurfaceDescendants ||
                IsInlineSpan(entry.Node, entry.Parent) && entry.Depth > ProtocolConstants.MaximumActionSurfaceRelativeDepth)
                return true;
            foreach (var child in entry.Node.Children ?? [])
                if (child is not null) pending.Push((child, entry.Node.Kind, entry.Depth + 1));
        }
        return false;
    }
}
