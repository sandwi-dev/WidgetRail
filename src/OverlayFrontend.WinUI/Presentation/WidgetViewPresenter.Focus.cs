using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record GroupMemory(WidgetElementIdentity Group, WidgetElementIdentity Child);
    private sealed record GroupEntry(long RequestId, WidgetElementIdentity Group);
    private readonly Dictionary<string, GroupMemory> groupMemory = new(StringComparer.Ordinal);
    private GroupEntry? pendingGroupEntry;
    private long lastGroupRequest;
    private bool redirectingGroupFocus;

    private void ResetGroupFocus()
    {
        groupMemory.Clear();
        pendingGroupEntry = null;
        lastGroupRequest = 0;
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
            pendingGroupEntry = null;
            return;
        }
        if (request.RequestId > lastGroupRequest)
        {
            lastGroupRequest = request.RequestId;
            pendingGroupEntry = bindings.TryGetValue(request.GroupId, out var group)
                ? new(request.RequestId, group.Identity) : null;
        }
        if (pendingGroupEntry is { } entry &&
            (!bindings.TryGetValue(entry.Group.Id, out var current) || current.Identity != entry.Group ||
             current.Identity.Scope != snapshot.ActiveInputScopeId)) pendingGroupEntry = null;
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
        if (frame is null || group.Identity.Scope != frame.Authority.ActiveInputScopeId ||
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
            group.Identity != entry.Group || GroupTarget(group) is not { Element: Control target }) return false;
        if (!target.Focus(FocusState.Keyboard)) return false;
        pendingGroupEntry = null;
        needsEntry = false;
        pendingRestore = null;
        return true;
    }

    private void OnGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (!applying && !redirectingGroupFocus && args.Direction != FocusNavigationDirection.None) pendingGroupEntry = null;
        // Pointer focus and explicit programmatic focus retain their exact target.
        // WinUI finds spatial candidates; group policy only chooses the declared
        // remembered/default child when a directional move enters that group.
        if (applying || redirectingGroupFocus || args.Direction is not (FocusNavigationDirection.Up or
            FocusNavigationDirection.Down or FocusNavigationDirection.Left or FocusNavigationDirection.Right) ||
            FindBinding(args.NewFocusedElement) is not { } incoming || !Eligible(incoming)) return;
        var outgoing = FindBinding(args.OldFocusedElement);
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
