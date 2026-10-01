using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WinUiPresentationContractTests
{
    internal static Task LegacyDeclarations()
    {
        var legacy = new WidgetView(UI.CollectionList("songs", 72,
            items: [UI.Button("Song", "play", "song").CollectionItem(new WidgetCollectionItemKey("song-key"))]), "song")
            .CreateSnapshot("contract", 1);
        Check(ViewSnapshotValidator.Validate(legacy).Count == 0, "legacy declaration remains valid transport data");
        var errors = WidgetTestHost.ValidateWinUiPresentation(legacy);
        Check(errors.Any(error => error.Path == "$.root.collectionLayout" && error.Code == "winui_legacy_collection_layout" &&
            error.Message.Contains("indexed/discovered", StringComparison.Ordinal) && error.Message.Contains("'songs'", StringComparison.Ordinal)),
            "author preflight names the unsupported field, safe element identity and replacement");
        Check(errors.Any(error => error.Path == "$.root.collectionAnchorKey"), "preflight reports every unsupported declaration on the node");
        foreach (var (field, node) in new (string, ViewNode)[] {
            ("virtualCollectionWindow", legacy.Root with { CollectionLayout = null, CollectionAnchorKey = null,
                VirtualCollectionWindow = new() { RequestGeneration = 1, Change = default, HasBefore = false, HasAfter = false, EstimatedItemExtent = 72 } }),
            ("collectionGeneration", legacy.Root with { CollectionLayout = null, CollectionAnchorKey = null, CollectionGeneration = 1 }),
            ("collectionResetGeneration", legacy.Root with { CollectionLayout = null, CollectionAnchorKey = null, CollectionResetGeneration = 1 }),
            ("scrollNearStartActionId", legacy.Root with { CollectionLayout = null, CollectionAnchorKey = null, ScrollNearStartActionId = "start" }),
            ("scrollNearEndActionId", legacy.Root with { CollectionLayout = null, CollectionAnchorKey = null, ScrollNearEndActionId = "end" }) })
            Check(WinUiPresentationContract.ValidateNode(node, "$.fixture").Any(error => error.Path == "$.fixture." + field),
                "node admission reports " + field);
        return Task.CompletedTask;
    }

    internal static Task NativeDeclarations()
    {
        var view = new WidgetView(UI.Stack("native", UI.Text("Heading", "heading"), UI.Button("Open", "open", "open")), "open")
            .CreateSnapshot("contract", 1);
        Check(WidgetTestHost.ValidateWinUiPresentation(view).Count == 0, "ordinary native controls pass preflight");
        foreach (var kind in Enum.GetValues<ViewNodeKind>())
            Check(WinUiPresentationContract.ValidateNode(new() { Id = "kind", Kind = kind }, "$.node").Count == 0,
                "known WinUI node mapping " + kind);
        var unknown = WinUiPresentationContract.ValidateNode(new() { Id = "unknown", Kind = (ViewNodeKind)int.MaxValue }, "$.node");
        Check(unknown.Count == 1 && unknown[0].Code == "winui_node_kind", "unknown node kinds cannot bypass native admission");
        var invalid = view with { InitialFocusId = "missing" };
        Check(WidgetTestHost.ValidateWinUiPresentation(invalid).SequenceEqual(ViewSnapshotValidator.Validate(invalid)),
            "protocol errors retain their existing diagnostics before frontend admission");
        return Task.CompletedTask;
    }

    internal static Task NestedAndPinned()
    {
        var bad = new ViewNode { Id = "edge", Kind = ViewNodeKind.Scroll, ScrollNearEndActionId = "more" };
        var root = new ViewNode { Id = "root", Kind = ViewNodeKind.Stack, Children = [bad],
            FocusPresentation = bad with { Id = "focus" }, DefaultFocusPresentation = bad with { Id = "fallback" } };
        var errors = WinUiPresentationContract.ValidateSubtree(root);
        Check(errors.Count == 3 && errors.Any(error => error.Path == "$.root.children[0].scrollNearEndActionId") &&
            errors.Any(error => error.Path == "$.root.focusPresentation.scrollNearEndActionId") &&
            errors.Any(error => error.Path == "$.root.defaultFocusPresentation.scrollNearEndActionId"),
            "subtree preflight traverses deferred fragments and reports structural paths without executing them");
        var legacy = new WidgetView(UI.CollectionList("pinned-list", 72,
            items: [UI.Button("Song", "play", "pinned-song").CollectionItem(new WidgetCollectionItemKey("key"))]), "pinned-song")
            .CreateSnapshot("contract", 1);
        var parent = new WidgetView(UI.Button("Open", "open", "open"), "open").CreateSnapshot("contract", 1);
        var pinned = parent with { ProtocolVersion = ProtocolConstants.CurrentVersion, PinnedLayouts = [new()
            { Id = "pin", Name = "Pinned", Surface = new(), Root = legacy.Root, ActiveInputScopeId = legacy.ActiveInputScopeId,
                InitialFocusId = legacy.InitialFocusId }] };
        var protocol = ViewSnapshotValidator.Validate(pinned);
        Check(protocol.Count == 0, "pinned fixture protocol is valid: " + string.Join(",", protocol.Select(error => error.Code)));
        Check(WidgetTestHost.ValidateWinUiPresentation(pinned).Any(error => error.Path == "$.pinnedLayouts[0].root.collectionLayout"),
            "parent author tests include declared pinned trees");
        return Task.CompletedTask;
    }

    internal static Task SafeAndBounded()
    {
        const string secret = "https://private.invalid/art?token=secret";
        var node = new ViewNode { Id = secret, Kind = ViewNodeKind.Scroll, Text = secret, ImageSource = secret, ScrollNearEndActionId = secret };
        var error = WinUiPresentationContract.ValidateNode(node, "$.root").Single();
        Check(!error.Message.Contains(secret, StringComparison.Ordinal), "diagnostics omit unsafe identity and authored values");
        var root = new ViewNode { Id = "root", Kind = ViewNodeKind.Stack,
            Children = Enumerable.Range(0, 200).Select(index => node with { Id = "item." + index }).ToArray() };
        Check(WinUiPresentationContract.ValidateSubtree(root).Count == 64, "unsupported declarations have a bounded diagnostic budget");
        for (var depth = 0; depth < ProtocolConstants.MaximumTreeDepth + 2; ++depth)
            node = new() { Id = "deep", Kind = ViewNodeKind.Stack, Children = [node] };
        Check(WinUiPresentationContract.ValidateSubtree(node).Any(value => value.Code == "winui_tree_limit"),
            "author-supplied excessive depth terminates without recursive stack traversal");
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
