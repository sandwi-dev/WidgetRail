using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record GroupMemory(WidgetElementIdentity Group, WidgetElementIdentity Child);
    private sealed record GroupEntry(long RequestId, WidgetElementIdentity Group, IndexedCollectionFocusTarget? IndexedItem);
    private sealed record CollectionMemoryKey(WidgetElementIdentity Identity, string SourceId, long QueryGeneration);
    private readonly OrderedDictionary<CollectionMemoryKey, IndexedCollectionFocusTarget> collectionMemory = [];
    private readonly Dictionary<string, GroupMemory> groupMemory = new(StringComparer.Ordinal);
    private GroupEntry? pendingGroupEntry;
    private long lastGroupRequest;
    private long? declaredGroupRequest;
    private long entryIntentVersion;
    private bool redirectingGroupFocus;

    private void ResetGroupFocus()
    {
        groupMemory.Clear();
        collectionMemory.Clear();
        pendingGroupEntry = null;
        lastGroupRequest = 0;
        declaredGroupRequest = null;
        ++entryIntentVersion;
    }

    private void UpdateFocusPolicy(ViewSnapshot snapshot)
    {
        foreach (var id in groupMemory.Keys.ToArray())
        {
            var memory = groupMemory[id];
            if (!bindings.TryGetValue(id, out var group) || group.Identity != memory.Group ||
                !bindings.TryGetValue(memory.Child.Id, out var child) || child.Identity != memory.Child ||
                !IsDescendant(child.Identity.Id, id)) groupMemory.Remove(id);
        }
        UpdateNativeNeighbors();
        if (snapshot.FocusGroupEntryRequest is not { } request)
        {
            if (declaredGroupRequest is not null) CancelGroupEntry();
            declaredGroupRequest = null;
            return;
        }
        declaredGroupRequest = request.RequestId;
        if (request.RequestId > lastGroupRequest)
        {
            CancelGroupEntry();
            lastGroupRequest = request.RequestId;
            pendingGroupEntry = bindings.TryGetValue(request.GroupId, out var group)
                ? new(request.RequestId, group.Identity, request.IndexedItem) : null;
        }
        if (pendingGroupEntry is { } entry &&
            (!bindings.TryGetValue(entry.Group.Id, out var current) || current.Identity != entry.Group ||
             current.Identity.Scope != snapshot.ActiveInputScopeId)) pendingGroupEntry = null;
    }

    private void CancelGroupEntry()
    {
        CancelMemoryRestoration();
        ++entryIntentVersion;
        pendingGroupEntry = null;
        foreach (var binding in bindings.Values)
            if (binding.Element is WidgetIndexedCollectionView collection) collection.CancelEntry();
    }

    private void RememberCollectionFocus(WidgetElementIdentity identity, IndexedCollectionFocusTarget item)
    {
        var key = new CollectionMemoryKey(identity, item.SourceId, item.QueryGeneration);
        // Retain only lightweight identities across page removal, with a bounded
        // least-recently-focused history; never retain item leases or controls.
        collectionMemory.Remove(key);
        collectionMemory[key] = item;
        if (collectionMemory.Count > 64) collectionMemory.Remove(collectionMemory.Keys.First());
    }

    private bool FocusBinding(Binding binding)
    {
        if (binding.Element is WidgetIndexedCollectionView collection)
        {
            var query = declarations[binding.Identity.Id].Node.IndexedCollection!;
            collectionMemory.TryGetValue(new(binding.Identity, query.SourceId, query.QueryGeneration), out var target);
            return collection.Enter(target, allowFallback: true);
        }
        return binding.Element is Control control && control.Focus(FocusState.Keyboard);
    }

    private void UpdateNativeNeighbors()
    {
        foreach (var binding in bindings.Values)
        {
            if (binding.Element is not Control control) continue;
            var neighbors = declarations[binding.Identity.Id].Node.Focus;
            control.XYFocusUp = Neighbor(neighbors?.Up, binding.Identity.Scope);
            control.XYFocusDown = Neighbor(neighbors?.Down, binding.Identity.Scope);
            control.XYFocusLeft = Neighbor(neighbors?.Left, binding.Identity.Scope);
            control.XYFocusRight = Neighbor(neighbors?.Right, binding.Identity.Scope);
        }
    }

    private DependencyObject? Neighbor(string? id, string scope)
    {
        if (id is null || !bindings.TryGetValue(id, out var target) || target.Identity.Scope != scope) return null;
        return Eligible(target) ? target.Element : GroupTarget(target)?.Element;
    }

    private bool IsDescendant(string child, string ancestor)
    {
        for (var current = declarations.GetValueOrDefault(child); current is not null;
             current = current.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
            if (current.Node.Id == ancestor) return true;
        return false;
    }

    private void RememberGroupFocus(Binding child)
    {
        for (var declaration = declarations.GetValueOrDefault(child.Identity.Id); declaration is not null;
             declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
        {
            if (declaration.Identity.Scope != child.Identity.Scope) break;
            if (declaration.Node.InitialChildFocusId is not null)
                groupMemory[declaration.Node.Id] = new(declaration.Identity, child.Identity);
        }
    }

    private Binding? GroupTarget(Binding group)
    {
        if (frame is null || group.Identity.Scope != activeScope ||
            declarations[group.Identity.Id].Node.InitialChildFocusId is not { } initial) return null;
        if (groupMemory.TryGetValue(group.Identity.Id, out var memory) && memory.Group == group.Identity &&
            bindings.TryGetValue(memory.Child.Id, out var rememberedChild) && rememberedChild.Identity == memory.Child &&
            Eligible(rememberedChild) && IsDescendant(rememberedChild.Identity.Id, group.Identity.Id)) return rememberedChild;
        if (bindings.TryGetValue(initial, out var defaultChild) && Eligible(defaultChild)
            && IsDescendant(defaultChild.Identity.Id, group.Identity.Id)) return defaultChild;
        return bindings.Values.FirstOrDefault(candidate => Eligible(candidate) && IsDescendant(candidate.Identity.Id, group.Identity.Id));
    }

    private bool TryRestoreGroupEntry()
    {
        if (pendingGroupEntry is not { } entry || !bindings.TryGetValue(entry.Group.Id, out var group) ||
            group.Identity != entry.Group) return false;
        if (group.Element is WidgetIndexedCollectionView collection)
        {
            if (entry.IndexedItem is { } item) collection.Enter(item);
            else FocusBinding(group);
            // Even a stale exact target is consumed. It must not fall through
            // to unrelated initial focus or replay on a later content update.
        }
        else if (GroupTarget(group) is not { } target || !FocusBinding(target)) return false;
        pendingGroupEntry = null;
        needsEntry = false;
        pendingRestore = null;
        return true;
    }

    private void OnGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (RejectInactiveFocus(args)) return;
        if (!applying && !redirectingGroupFocus && args.Direction != FocusNavigationDirection.None) CancelGroupEntry();
        // Pointer focus and explicit programmatic focus retain their exact target.
        // WinUI finds spatial candidates; group policy only chooses the declared
        // remembered/default child when a directional move enters that group.
        if (applying || redirectingGroupFocus || args.Direction == FocusNavigationDirection.None ||
            FindBinding(args.NewFocusedElement) is not { } incoming || !Eligible(incoming)) return;
        var outgoing = FindBinding(args.OldFocusedElement);
        if (incoming.Element is WidgetIndexedCollectionView && !ReferenceEquals(incoming, outgoing))
        {
            // The native neighbor points to the collection. Enter its logical
            // child after this focus transaction so realization can complete.
            args.TryCancel();
            var intent = entryIntentVersion;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!disposed && intent == entryIntentVersion && bindings.TryGetValue(incoming.Identity.Id, out var current) &&
                    ReferenceEquals(current, incoming) && Eligible(current)) FocusBinding(current);
            });
            return;
        }
        var path = new List<Declaration>();
        for (var declaration = declarations.GetValueOrDefault(incoming.Identity.Id); declaration is not null;
             declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
        {
            if (declaration.Identity.Scope != incoming.Identity.Scope) break;
            path.Add(declaration);
        }
        for (var index = path.Count - 1; index >= 0; --index)
        {
            var declaration = path[index];
            if (declaration.Node.InitialChildFocusId is null ||
                (outgoing is not null && IsDescendant(outgoing.Identity.Id, declaration.Node.Id))) continue;
            if (GroupTarget(bindings[declaration.Node.Id]) is not { } target || ReferenceEquals(target, incoming)) return;
            redirectingGroupFocus = true;
            try { args.TrySetNewFocusedElement(target.Element); }
            finally { redirectingGroupFocus = false; }
            return;
        }
    }
}
