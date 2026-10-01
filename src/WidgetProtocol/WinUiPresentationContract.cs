namespace WidgetRail.WidgetProtocol;

/// <summary>
/// WinUI presentation admission shared by the native frontend and author tests.
/// This checks declaration support, not theme mapping, provider behavior or pixels.
/// It never acquires a lazy collection range or executes widget code.
/// </summary>
public static class WinUiPresentationContract
{
    /// <summary>Checks protocol validity, then all declared main, pinned and presentation-fragment trees.</summary>
    public static IReadOnlyList<ProtocolValidationError> Validate(ViewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var protocol = ViewSnapshotValidator.Validate(snapshot);
        if (protocol.Count != 0) return protocol;
        var errors = new List<ProtocolValidationError>();
        Collect(snapshot.Root, "$.root", errors);
        for (var index = 0; index < snapshot.PinnedLayouts.Count && errors.Count < 64; ++index)
            if (snapshot.PinnedLayouts[index].Root is { } root)
                Collect(root, $"$.pinnedLayouts[{index}].root", errors);
        return errors;
    }

    /// <summary>
    /// Checks a subtree, including focus fragments. Use on lazily acquired row
    /// roots as well as parent snapshots; this is not structural protocol validation.
    /// </summary>
    public static IReadOnlyList<ProtocolValidationError> ValidateSubtree(ViewNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var errors = new List<ProtocolValidationError>();
        Collect(root, "$.root", errors);
        return errors;
    }

    /// <summary>Checks one already protocol-validated node at its structural path.</summary>
    public static IReadOnlyList<ProtocolValidationError> ValidateNode(ViewNode node, string path)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        List<ProtocolValidationError>? errors = null;
        if (node.Kind is not (ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Grid or ViewNodeKind.Scroll or
            ViewNodeKind.Button or ViewNodeKind.ActionSurface or ViewNodeKind.Slider or ViewNodeKind.ModalLayer or
            ViewNodeKind.TextEntry or ViewNodeKind.Select or ViewNodeKind.Text or ViewNodeKind.Progress or
            ViewNodeKind.LoadingIndicator or ViewNodeKind.Spacer or ViewNodeKind.IndexedCollection or ViewNodeKind.Image or
            ViewNodeKind.BackgroundSurface or ViewNodeKind.FocusPresentationSurface or ViewNodeKind.ControllerGlyph or
            ViewNodeKind.Icon or ViewNodeKind.MediaViewport or ViewNodeKind.WindowPreview))
            Add("kind", "winui_node_kind", "This node kind has no WinUI control mapping.");
        if (node.CollectionLayout is not null && node.Kind != ViewNodeKind.IndexedCollection)
            Add("collectionLayout", "winui_legacy_collection_layout", "Use an indexed/discovered collection for virtualized rows, or UI.ResponsiveGrid for a small static grid.");
        if (node.VirtualCollectionWindow is not null)
            Add("virtualCollectionWindow", "winui_legacy_collection_window", "Publish an indexed/discovered collection; WinUI owns realization and visible ranges.");
        if (node.CollectionAnchorKey is not null)
            Add("collectionAnchorKey", "winui_legacy_collection_anchor", "Use an IndexedCollectionFocusTarget for keyed entry; WinUI retains scroll position.");
        if (node.CollectionGeneration is not null)
            Add("collectionGeneration", "winui_legacy_collection_generation", "Use PublishQuery for membership changes and UpdateContent for unchanged indexed membership.");
        if (node.CollectionResetGeneration is not null)
            Add("collectionResetGeneration", "winui_legacy_collection_reset", "Use PublishQuery and an explicit keyed collection entry when resetting navigation.");
        if (node.ScrollNearStartActionId is not null)
            Add("scrollNearStartActionId", "winui_legacy_edge_action", "Use indexed range reads or a discovered collection continuation instead of viewport-edge actions.");
        if (node.ScrollNearEndActionId is not null)
            Add("scrollNearEndActionId", "winui_legacy_edge_action", "Use indexed range reads or a discovered collection continuation instead of viewport-edge actions.");
        return errors is null ? Array.Empty<ProtocolValidationError>() : errors;

        void Add(string property, string code, string message)
        {
            // Report only safe structural identity, never text, action payload,
            // image URL or the arbitrary value of an unsupported property.
            var identity = node.Id is not null && ProtocolValidationIdentifierContext.IsSafeIdentifier(node.Id) ? $" Element '{node.Id}'." : string.Empty;
            (errors ??= []).Add(new(path + "." + property, code, message + identity));
        }
    }

    private static void Collect(ViewNode root, string rootPath, List<ProtocolValidationError> errors)
    {
        var pending = new Stack<(ViewNode Node, string Path, int Depth, int FragmentDepth)>();
        pending.Push((root, rootPath, 0, 0));
        var count = 0;
        while (pending.TryPop(out var entry) && errors.Count < 64)
        {
            if (++count > ProtocolConstants.MaximumNodeCount || entry.Depth > ProtocolConstants.MaximumTreeDepth ||
                entry.FragmentDepth > ProtocolConstants.MaximumFocusPresentationDepth ||
                entry.Node.Children.Count > ProtocolConstants.MaximumNodeCount)
            { errors.Add(new(entry.Path, "winui_tree_limit", "Presentation subtree exceeds the protocol's bounded traversal limits.")); break; }
            foreach (var error in ValidateNode(entry.Node, entry.Path))
            { if (errors.Count == 64) break; errors.Add(error); }
            if (entry.Node.DefaultFocusPresentation is { } fallback)
                pending.Push((fallback, entry.Path + ".defaultFocusPresentation", 0, entry.FragmentDepth + 1));
            if (entry.Node.FocusPresentation is { } focus)
                pending.Push((focus, entry.Path + ".focusPresentation", 0, entry.FragmentDepth + 1));
            for (var index = entry.Node.Children.Count - 1; index >= 0; --index)
                pending.Push((entry.Node.Children[index], $"{entry.Path}.children[{index}]", entry.Depth + 1, entry.FragmentDepth));
        }
    }
}
