namespace WidgetRail.WidgetProtocol;

/// <summary>A bounded worker-owned semantic range, independent of native container realization.</summary>
public sealed record IndexedCollectionLease(string LeaseId, IndexedCollectionRange Range);
public sealed record IndexedCollectionItemReference(string LeaseId, string ItemKey);
public sealed record IndexedCollectionInputRequest(IndexedCollectionItemReference Item,
    ControllerButton Button, ControllerEventPhase Phase = ControllerEventPhase.Pressed,
    string? ContextActionOwnerId = null, string? ContextActionId = null);

internal sealed record IndexedInputBinding(string ActionId, string OwnerId, ViewNodeKind OwnerKind, bool IsItem);

/// <summary>Logical ancestry shared by trusted host admission and worker revalidation.</summary>
internal static class IndexedCollectionInputContract
{
    internal static void ValidateReference(IndexedCollectionItemReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!Guid.TryParseExact(reference.LeaseId, "N", out _) || reference.ItemKey is null ||
            !ProtocolValidationIdentifierContext.IsSafeIdentifier(reference.ItemKey))
            throw new ArgumentException("Indexed item reference is invalid.", nameof(reference));
    }

    internal static void ValidateInput(IndexedCollectionInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateReference(request.Item);
        if (!Enum.IsDefined(request.Button) || !Enum.IsDefined(request.Phase))
            throw new ArgumentException("Indexed input button or phase is invalid.", nameof(request));
        if ((request.ContextActionId is null) != (request.ContextActionOwnerId is null) ||
            request.ContextActionId is { } action && !ProtocolValidationIdentifierContext.IsSafeIdentifier(action) ||
            request.ContextActionOwnerId is { } owner && !ProtocolValidationIdentifierContext.IsSafeIdentifier(owner))
            throw new ArgumentException("Indexed context selection identity is invalid.", nameof(request));
    }

    internal static IReadOnlyList<ViewNode> CaptureOwners(ViewSnapshot parent, IndexedCollectionRangeRequest request,
        bool requireActiveScope)
    {
        var binding = IndexedCollectionContract.Resolve(parent, request);
        if (requireActiveScope && binding.Projection.ActiveInputScopeId != binding.Scope)
            throw new InvalidOperationException("The indexed collection is outside the active input scope.");
        var path = new List<ViewNode>();
        if (!Find(binding.Projection.Root)) throw new InvalidOperationException("Indexed ancestry is unavailable.");
        var scopeStart = path.FindLastIndex(node => node.InputScopeId is not null);
        if (scopeStart > 0) path.RemoveRange(0, scopeStart);
        // Retain only input ownership, not the entire parent tree through Children.
        return Array.AsReadOnly(path.Select(node => new ViewNode
        {
            Id = node.Id, Kind = node.Kind, InputScopeId = node.InputScopeId,
            IsDisabled = node.IsDisabled, IsBusy = node.IsBusy,
            Shortcuts = Array.AsReadOnly(node.Shortcuts.ToArray()),
            ContextMenuButton = node.ContextMenuButton,
            ContextActions = Array.AsReadOnly(node.ContextActions.ToArray()),
        }).ToArray());

        bool Find(ViewNode node)
        {
            path.Add(node);
            if (ReferenceEquals(node, binding.Node)) return true;
            foreach (var child in node.Children) if (Find(child)) return true;
            path.RemoveAt(path.Count - 1);
            return false;
        }
    }

    internal static IndexedInputBinding? Resolve(IReadOnlyList<ViewNode> owners, ViewNode item,
        IndexedCollectionInputRequest request)
    {
        ValidateInput(request);
        var path = owners.Append(item).ToArray();
        var collectionAvailable = owners.Where(node => node.Kind == ViewNodeKind.IndexedCollection)
            .All(ControllerShortcutResolutionContract.OwnerAvailable);
        if (request.ContextActionId is not null || request.ContextActionOwnerId is not null)
        {
            if (request.ContextActionId is null || request.ContextActionOwnerId is null || request.Phase != ControllerEventPhase.Pressed)
                throw new ArgumentException("Context selection requires an exact owner, action and pressed phase.", nameof(request));
            var owner = path.SingleOrDefault(node => node.Id == request.ContextActionOwnerId);
            var action = owner?.ContextActions.SingleOrDefault(candidate => candidate.ActionId == request.ContextActionId);
            return owner is not null && ControllerShortcutResolutionContract.OwnerAvailable(owner) &&
                action is { IsDisabled: false, IsBusy: false } && (!ReferenceEquals(owner, item) || collectionAvailable)
                ? new(action.ActionId, owner.Id, owner.Kind, ReferenceEquals(owner, item)) : null;
        }
        if (request.Button == ControllerButton.A)
            return request.Phase == ControllerEventPhase.Pressed && item.ActionId is { } action &&
                collectionAvailable && ControllerShortcutResolutionContract.OwnerAvailable(item) ? new(action, item.Id, item.Kind, true) : null;
        var shortcut = ControllerShortcutResolver.ResolvePath(path, request.Button, request.Phase);
        return shortcut.Status == ControllerShortcutResolutionStatus.Resolved && (!ReferenceEquals(shortcut.Owner, item) || collectionAvailable)
            ? new(shortcut.Shortcut!.ActionId, shortcut.Owner!.Id, shortcut.Owner.Kind, ReferenceEquals(shortcut.Owner, item)) : null;
    }
}
