namespace WidgetRail.WidgetProtocol;

/// <summary>Shared accounting for action layout and presentational rich-text runs.</summary>
internal struct ActionSurfaceContentBudget
{
    internal int LayoutNodes { get; private set; }
    internal int RichTextSpans { get; private set; }
    internal int InlineActions { get; private set; }
    internal int SavedPreviews { get; private set; }

    internal static bool IsInlineSpan(ViewNode node, ViewNodeKind? parentKind) =>
        parentKind == ViewNodeKind.RichText && node.Kind == ViewNodeKind.Text &&
        node.Children is { Count: 0 };

    // A reading group's admitted child action is a complete surface with its
    // own four-level layout budget. Keep counting every descendant against the
    // outer node budget, and never restart at arbitrary layout containers.
    internal static int ChildDepth(ViewNode child, ViewNode parent, int depth, bool inlineAction) =>
        (inlineAction ? 0 : depth) + (IsInlineSpan(child, parent.Kind) ? 0 : 1);

    internal void Add(ViewNode node, ViewNodeKind? parentKind)
    {
        if (ReadingGroupContentContract.IsSavedPreview(node.MediaPlayer)) SavedPreviews++;
        if (node.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface ||
            node.Kind == ViewNodeKind.MediaPlayer && ReadingGroupContentContract.IsInteractivePlayer(node.MediaPlayer)) InlineActions++;
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
