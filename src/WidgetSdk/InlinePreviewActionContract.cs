using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

// Private transport: no fragment, source path, or widget-authored callback crosses IPC.
internal sealed record InlinePreviewActionInput(long SnapshotSequence, WidgetActionEvent Action);
internal sealed record InlinePreviewActionBinding(string Scope, string Ancestry, string SourceId,
    string OwnerId, string ActionId, object Detail);

internal static class InlinePreviewActionContract
{
    internal static InlinePreviewActionBinding? Resolve(ViewSnapshot snapshot, WidgetActionEvent action)
        => Resolve(snapshot, action, out _);

    internal static InlinePreviewActionBinding? Resolve(ViewSnapshot snapshot, WidgetActionEvent action, out ViewNode? declaration)
    {
        declaration = null;
        var scope = Scope(snapshot.Root, snapshot.ActiveInputScopeId, true);
        if (scope is null) return null;
        var path = new List<ViewNode>();
        if (!Find(scope, action.SourceElementId, true, path)) return null;
        var edge = -1;
        for (var i = 0; i + 1 < path.Count; ++i)
            if (ReferenceEquals(path[i].FocusPresentation, path[i + 1]))
            { if (edge >= 0) throw Retired(); edge = i; }
        if (edge < 0) return null;
        var surface = path.Take(edge).LastOrDefault(node => node.Kind == ViewNodeKind.FocusPresentationSurface);
        if (surface?.FocusPreview?.EnterButton is null ||
            path.Any(node => !ControllerShortcutResolutionContract.OwnerAvailable(node)) ||
            action.InputScopeId != snapshot.ActiveInputScopeId || action.FocusedElementId != action.SourceElementId ||
            action.Phase != ControllerEventPhase.Pressed || action.CommittedText is not null ||
            action.FocusedCollectionItem is not null || action.VisibleCollectionKeys is not null || action.RetainedCollectionKeys is not null)
            throw Retired();
        var owner = path[^1];
        declaration = owner;
        object detail;
        if (owner.Kind == ViewNodeKind.Slider && owner.ValueChangedActionId == action.ActionId &&
            action.RequestedValue is { } value && owner.Minimum is { } min && owner.Maximum is { } max && owner.Step is { } step &&
            SliderMath.IsValidRequestedValue(value, min, max, step) &&
            action.ControllerButton is null or ControllerButton.DPadLeft or ControllerButton.DPadRight)
            detail = (owner.Kind, min, max, step);
        else if (action.RequestedValue is null && action.ControllerButton is null or ControllerButton.A)
        {
            if (owner.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface && owner.ActionId == action.ActionId)
                detail = owner.Kind;
            else if (owner.Kind == ViewNodeKind.Select && owner.SelectOptions.SingleOrDefault(option => option.ActionId == action.ActionId) is { IsDisabled: false, IsBusy: false } option)
                detail = (owner.Kind, owner.IsCommandMenu == true, option with { IsSelected = false });
            else throw Retired();
        }
        else throw Retired();
        // Include all source/fragment ancestors and occurrence keys, not just the child ID.
        // Labels, current values and layout sizing may update without replacing authority.
        var ancestry = JsonSerializer.Serialize(path.Select(node => (object)new
            { node.Id, node.Kind, node.CollectionItemKey, Entry = node.Kind == ViewNodeKind.FocusPresentationSurface ? node.FocusPreview?.EnterButton : null }).ToArray());
        return new(snapshot.ActiveInputScopeId, ancestry, path[edge].Id, owner.Id, action.ActionId, detail);
    }

    private static InvalidOperationException Retired() => new("Inline preview action retired.");
    private static ViewNode? Scope(ViewNode node, string id, bool root)
    {
        if ((root || node.InputScopeId is not null) && (node.InputScopeId ?? node.Id) == id) return node;
        foreach (var child in node.Children) if (Scope(child, id, false) is { } found) return found;
        return null;
    }
    private static bool Find(ViewNode node, string id, bool root, List<ViewNode> path)
    {
        if (!root && node.InputScopeId is not null) return false;
        path.Add(node);
        if (node.Id == id) return true;
        if (node.FocusPresentation is { } preview && Find(preview, id, false, path)) return true;
        foreach (var child in node.Children) if (Find(child, id, false, path)) return true;
        path.RemoveAt(path.Count - 1); return false;
    }
}

public abstract partial class Widget
{
    private sealed class InlinePreviewExecution(InlinePreviewActionBinding binding, WidgetActionEvent action)
    {
        internal InlinePreviewActionBinding Binding { get; } = binding;
        internal WidgetActionEvent Action { get; } = action;
        internal volatile bool Retired;
        internal bool Matches(ViewSnapshot? snapshot)
        {
            try { return !Retired && snapshot is not null && InlinePreviewActionContract.Resolve(snapshot, Action) == Binding; }
            catch (InvalidOperationException) { return false; }
        }
    }

    internal WidgetOperationAdmission AdmitInlinePreviewAction(InlinePreviewActionInput input)
    {
        var snapshot = Volatile.Read(ref _latestSnapshot);
        if (input.SnapshotSequence <= 0 || snapshot?.Sequence != input.SnapshotSequence)
            throw new InvalidOperationException("Inline preview origin retired.");
        var binding = InlinePreviewActionContract.Resolve(snapshot, input.Action) ?? throw new InvalidOperationException("Missing inline preview owner.");
        var captured = new InlinePreviewExecution(binding, input.Action);
        return AdmitAction(input.Action, execution: new(binding, async (action, token) =>
        {
            if (!captured.Matches(Volatile.Read(ref _latestSnapshot))) return;
            await OnActionAsync(action, token).ConfigureAwait(false);
        }) { InlinePreview = captured });
    }

    private void RetireInlinePreviewActions(ViewSnapshot snapshot)
    {
        lock (_actionQueueLock)
            if (_actionQueue is { } queue)
                foreach (var queued in queue.Pending)
                    if (queued.Execution?.InlinePreview is InlinePreviewExecution captured && !captured.Matches(snapshot)) captured.Retired = true;
    }
}
