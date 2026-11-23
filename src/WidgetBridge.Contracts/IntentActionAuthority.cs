using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

/// <summary>Resolves the exact intent declaration shown in the admitted input scope.</summary>
internal static class IntentActionAuthority
{
    internal static WidgetIntentRequest? RevalidateIndexed(IReadOnlyList<ViewNode> originOwners,
        IReadOnlyList<ViewNode> currentOwners, ViewNode item, WidgetActionEvent action,
        IndexedCollectionItemReference? reference = null)
    {
        if (action.Phase != ControllerEventPhase.Pressed || action.ControllerButton is not (null or ControllerButton.A) ||
            action.RequestedValue is not null || action.CommittedText is not null ||
            action.VisibleCollectionKeys is not null || action.RetainedCollectionKeys is not null ||
            originOwners.Concat(currentOwners).Any(node => node.IsDisabled == true || node.IsBusy == true)) return null;
        ViewNode? declaration = item;
        if (reference is not null)
        {
            var preview = action.SourceElementId != item.Id ||
                originOwners.LastOrDefault() is { Kind: ViewNodeKind.FocusPresentationSurface, FocusPreviewSource: not null };
            var inline = item.IsReadingGroup && action.SourceElementId != item.Id;
            var request = new IndexedCollectionInputRequest(reference, ControllerButton.A)
            {
                InlineAction = inline ? new(action.SourceElementId,action.ActionId) : null,
                PreviewAction = preview && !inline ? new(action.SourceElementId, action.ActionId) : null
            };
            var before = IndexedCollectionInputContract.Resolve(originOwners, item, request, out var original);
            var after = IndexedCollectionInputContract.Resolve(currentOwners, item, request, out declaration);
            if (before is null || before != after || !ReferenceEquals(original, declaration)) return null;
        }
        if (declaration is not { Kind: ViewNodeKind.Button or ViewNodeKind.ActionSurface } ||
            declaration.ActionId != action.ActionId || declaration.Id != action.SourceElementId ||
            declaration.IsDisabled == true || declaration.IsBusy == true || declaration.Intent is not { } intent || !intent.IsWellFormed()) return null;
        // The host's live semantic lease owns this immutable item; parent paths
        // have independently passed the lease's query and active-scope checks.
        return intent;
    }

    internal static WidgetIntentRequest? Revalidate(ViewSnapshot origin, ViewSnapshot current, WidgetActionEvent action)
    {
        if (origin.WidgetInstanceId != current.WidgetInstanceId || origin.ActiveInputScopeId != current.ActiveInputScopeId ||
            action.InputScopeId != origin.ActiveInputScopeId || action.Phase != ControllerEventPhase.Pressed ||
            action.ControllerButton is not (null or ControllerButton.A) || action.RequestedValue is not null ||
            action.CommittedText is not null || action.VisibleCollectionKeys is not null || action.RetainedCollectionKeys is not null)
            return null;
        var original = Resolve(origin, action);
        var latest = Resolve(current, action);
        if (original is null || latest is null || original.Value.Identity != latest.Value.Identity ||
            !original.Value.Intent.Matches(latest.Value.Intent)) return null;
        return latest.Value.Intent;
    }

    private static (string Identity, WidgetIntentRequest Intent)? Resolve(ViewSnapshot snapshot, WidgetActionEvent action)
    {
        // The same proof used for ordinary preview actions owns interactive
        // ancestry, occurrence identity and disabled/busy ancestors. Do not
        // invent a second traversal for intent buttons inside inline fragments.
        try
        {
            if (InlinePreviewActionContract.Resolve(snapshot, action, out var preview) is { } proof)
                return preview is { Kind: ViewNodeKind.Button or ViewNodeKind.ActionSurface, Intent: { } previewIntent } && previewIntent.IsWellFormed()
                    ? ("preview:" + proof.Ancestry, previewIntent) : null;
        }
        catch (InvalidOperationException) { return null; }
        var scope = FindScope(snapshot.Root, snapshot.ActiveInputScopeId, true);
        if (scope is null) return null;
        var path = new List<ViewNode>();
        if (!Find(scope, action.SourceElementId, path, true)) return null;
        var node = path[^1];
        if (node.Kind is not (ViewNodeKind.Button or ViewNodeKind.ActionSurface) || node.ActionId != action.ActionId ||
            node.Intent is not { } intent || !intent.IsWellFormed() || path.Any(n => n.IsDisabled == true || n.IsBusy == true)) return null;
        // Collection occurrences can reuse local element IDs. Their logical keys
        // must remain identical across snapshot replacement.
        var identity = node.Kind + ":" + node.Id + ":" + string.Concat(path.Where(n => n.CollectionItemKey is not null)
            .Select(n => n.CollectionItemKey!.Length + ":" + n.CollectionItemKey));
        return (identity, intent);
    }

    private static ViewNode? FindScope(ViewNode node, string id, bool root)
    {
        if ((root || node.InputScopeId is not null) && (node.InputScopeId ?? node.Id) == id) return node;
        foreach (var child in node.Children)
            if (FindScope(child, id, false) is { } scope) return scope;
        return null;
    }

    private static bool Find(ViewNode node, string id, List<ViewNode> path, bool root)
    {
        if (!root && node.InputScopeId is not null) return false;
        path.Add(node);
        if (node.Id == id) return true;
        foreach (var child in node.Children) if (Find(child, id, path, false)) return true;
        path.RemoveAt(path.Count - 1);
        return false;
    }
}
