using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

// Private host/worker input contract, independent of the unchanged view schema.
internal sealed record PinnedActionInput(int Version, string LayoutId, long SnapshotSequence, WidgetActionEvent Action);
internal sealed record PinnedActionResult(
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] WidgetOperationAdmission? Admission);
internal sealed record PinnedActionNodeIdentity(string Id, ViewNodeKind Kind, string Occurrence, bool Disabled, bool Busy);
internal sealed record PinnedActionBinding(string Role, PinnedActionNodeIdentity Owner,
    PinnedActionNodeIdentity? Focus, string ActionId, object Detail);

internal static class PinnedActionContract
{
    internal const int Version = 1;
    internal static WidgetOperationAdmission? ValidateAdmission(WidgetOperationAdmission? admission) => admission switch
    {
        null or WidgetOperationAdmission.Enqueued or WidgetOperationAdmission.Joined or WidgetOperationAdmission.Replaced or
            WidgetOperationAdmission.RejectedInactive or WidgetOperationAdmission.RejectedCapacity => admission,
        _ => throw new ArgumentException("Invalid pinned action admission."),
    };
    internal static void Validate(PinnedActionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Action);
        var action = input.Action;
        if (input.Version != Version || input.SnapshotSequence <= 0 ||
            !Safe(input.LayoutId) || !Safe(action.ActionId) || !Safe(action.SourceElementId) || !Safe(action.InputScopeId) ||
            action.Phase != ControllerEventPhase.Pressed || action.ControllerButton is { } button && !Enum.IsDefined(button) ||
            action.Sequence < 0 || action.MonotonicTimestampMicroseconds < 0 ||
            action.FocusedElementId is { } focus && !Safe(focus) ||
            action.RequestedValue is not null || action.FocusedCollectionItem is not null ||
            action.VisibleCollectionKeys is not null || action.RetainedCollectionKeys is not null ||
            action.CommittedText is { } text && (text.Length > ProtocolConstants.MaximumExtendedTextEntryLength || text.Any(char.IsControl)))
            throw new ArgumentException("Invalid pinned action contract.");
    }

    internal static PinnedActionBinding Resolve(ViewSnapshot snapshot, PinnedActionInput input)
    {
        Validate(input);
        var action = input.Action;
        var projected = Project(snapshot, input.LayoutId);
        if (action.InputScopeId != projected.ActiveInputScopeId) throw new InvalidOperationException("Pinned action scope retired.");
        var scope = FindScope(projected.Root, projected.ActiveInputScopeId, true)
            ?? throw new InvalidOperationException("Pinned action scope is missing.");
        var path = new List<ViewNode>();
        if (!FindOwner(scope, action.SourceElementId, true, path)) throw new InvalidOperationException("Pinned action owner is missing.");
        var node = path[^1];
        if (path.Any(item => !ControllerShortcutResolutionContract.OwnerAvailable(item))) throw new InvalidOperationException("Pinned action owner is unavailable.");
        var owner = Identity(path);
        PinnedActionNodeIdentity? focused = null;
        if (action.FocusedElementId is { } focusId)
        {
            var focusPath = new List<ViewNode>();
            if (!FindOwner(scope, focusId, true, focusPath)) throw new InvalidOperationException("Pinned focus owner retired.");
            focused = Identity(focusPath);
        }
        if (action.CommittedText is { } text)
        {
            if (node.Kind != ViewNodeKind.TextEntry || action.ActionId != node.ActionId ||
                text.Length > (node.TextEntryMaximumLength ?? ProtocolConstants.MaximumTextEntryLength) ||
                action.ControllerButton is not (null or ControllerButton.A) || focused is not null && focused.Id != node.Id)
                throw new InvalidOperationException("Pinned text binding retired.");
            return new("text", owner, focused, action.ActionId,
                (node.TextEntryInputKind, node.TextEntryMaximumLength));
        }
        if (node.ContextActions.SingleOrDefault(item => item.ActionId == action.ActionId) is not { IsDisabled: false, IsBusy: false } context)
            throw new InvalidOperationException("Pinned context action binding retired.");
        var trigger = node.ContextMenuButton ?? (node.Kind == ViewNodeKind.ActionSurface ? ControllerButton.Menu : (ControllerButton?)null);
        if (action.ControllerButton is { } supplied && supplied != trigger ||
            node.Kind == ViewNodeKind.ActionSurface && focused is not null && focused.Id != node.Id)
            throw new InvalidOperationException("Pinned context trigger or focus retired.");
        return new("context", owner, focused, action.ActionId, (node.ContextMenuButton, context));
    }

    internal static ViewSnapshot Project(ViewSnapshot snapshot, string layoutId, bool inheritRootless = false)
    {
        if (layoutId == PinnedSurfaceContract.FullWidgetLayoutId) return PinnedSurfaceContract.WithoutModal(snapshot);
        var layout = snapshot.PinnedLayouts.SingleOrDefault(item => item.Id == layoutId);
        if (inheritRootless && layout is { Root: null }) return PinnedSurfaceContract.WithoutModal(snapshot);
        if (layout?.Root is not { } root || layout.ActiveInputScopeId is not { } scope)
            throw new InvalidOperationException("Pinned layout retired or has no independent root.");
        return snapshot with { Root = root, ActiveInputScopeId = scope, PinnedLayouts = [], QuickActions = [] };
    }

    private static PinnedActionNodeIdentity Identity(IReadOnlyList<ViewNode> path) => new(path[^1].Id, path[^1].Kind,
        string.Concat(path.Where(item => item.CollectionItemKey is not null)
            .Select(item => item.CollectionItemKey!.Length + ":" + item.CollectionItemKey)),
        path[^1].IsDisabled == true, path[^1].IsBusy == true);

    private static bool Safe(string? value) => value is not null && ProtocolValidationIdentifierContext.IsSafeIdentifier(value);
    private static ViewNode? FindScope(ViewNode node, string scope, bool root)
    {
        if ((root || node.InputScopeId is not null) && (node.InputScopeId ?? node.Id) == scope) return node;
        foreach (var child in node.Children) if (FindScope(child, scope, false) is { } found) return found;
        return null;
    }
    private static bool FindOwner(ViewNode node, string id, bool root, List<ViewNode> path)
    {
        if (!root && node.InputScopeId is not null) return false;
        path.Add(node);
        if (node.Id == id) return true;
        foreach (var child in node.Children) if (FindOwner(child, id, false, path)) return true;
        path.RemoveAt(path.Count - 1); return false;
    }
}

public abstract partial class Widget
{
    internal WidgetOperationAdmission? AdmitPinnedAction(PinnedActionInput input)
    {
        PinnedActionContract.Validate(input);
        var snapshot = Volatile.Read(ref _latestSnapshot);
        if (snapshot is null || snapshot.Sequence != input.SnapshotSequence) return null;
        try { _ = PinnedActionContract.Resolve(snapshot, input); }
        catch (InvalidOperationException) { return null; }
        // Worker requests are serial. Capture this exact declared action into
        // the existing bounded queue; a later deselection cannot retract it.
        return AdmitAction(input.Action);
    }
}
