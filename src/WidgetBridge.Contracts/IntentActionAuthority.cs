using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

/// <summary>Resolves only the primary intent actually shown in the admitted input scope.</summary>
internal static class IntentActionAuthority
{
    internal static WidgetIntentRequest? RevalidateIndexed(IReadOnlyList<ViewNode> originOwners,
        IReadOnlyList<ViewNode> currentOwners, ViewNode item, WidgetActionEvent action)
    {
        if (action.Phase != ControllerEventPhase.Pressed || action.ControllerButton is not (null or ControllerButton.A) ||
            action.RequestedValue is not null || action.CommittedText is not null ||
            action.VisibleCollectionKeys is not null || action.RetainedCollectionKeys is not null ||
            item.Kind is not (ViewNodeKind.Button or ViewNodeKind.ActionSurface) || item.ActionId != action.ActionId ||
            item.Id != action.SourceElementId || item.IsDisabled == true || item.IsBusy == true ||
            originOwners.Concat(currentOwners).Any(node => node.IsDisabled == true || node.IsBusy == true) ||
            item.Intent is not { } intent || !intent.IsWellFormed()) return null;
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
