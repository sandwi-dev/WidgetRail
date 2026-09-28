namespace WidgetRail.WidgetProtocol;

/// <summary>A bounded worker-owned semantic range, independent of native container realization.</summary>
public sealed record IndexedCollectionLease(string LeaseId, IndexedCollectionRange Range);
public sealed record IndexedCollectionItemReference(string LeaseId, string ItemKey);
public sealed record IndexedCollectionInputContext(string InputScopeId, long SnapshotSequence, long Sequence = 0, long MonotonicTimestampMicroseconds = 0);
public sealed record IndexedCollectionInputRequest(IndexedCollectionItemReference Item,
    ControllerButton Button, ControllerEventPhase Phase = ControllerEventPhase.Pressed,
    string? ContextActionOwnerId = null, string? ContextActionId = null);

internal sealed record IndexedInputBinding(string ActionId, string OwnerId, ViewNodeKind OwnerKind, bool IsItem);

/// <summary>Logical ancestry shared by trusted host admission and worker revalidation.</summary>
public static class IndexedCollectionInputContract
{
    public static void ValidateReference(IndexedCollectionItemReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!Guid.TryParseExact(reference.LeaseId, "N", out _) || reference.ItemKey is null ||
            !ProtocolValidationIdentifierContext.IsSafeIdentifier(reference.ItemKey))
            throw new ArgumentException("Indexed item reference is invalid.", nameof(reference));
    }

    public static void ValidateContext(IndexedCollectionInputContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.InputScopeId is null || !ProtocolValidationIdentifierContext.IsSafeIdentifier(context.InputScopeId) ||
            context.SnapshotSequence <= 0 || context.Sequence < 0 || context.MonotonicTimestampMicroseconds < 0)
            throw new ArgumentException("Indexed input context is invalid.", nameof(context));
    }

    public static void ValidateInput(IndexedCollectionInputRequest request)
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

    internal static IReadOnlyList<ViewNode> ResolveOwnerPath(ViewSnapshot parent, IndexedCollectionRangeRequest request)
    {
        var binding = IndexedCollectionContract.Resolve(parent, request);
        if (binding.Projection.ActiveInputScopeId != binding.Scope)
            throw new InvalidOperationException("The indexed collection is outside the active input scope.");
        var path = new List<ViewNode>();
        if (!Find(binding.Projection.Root)) throw new InvalidOperationException("Indexed ancestry is unavailable.");
        var scopeStart = path.FindLastIndex(node => node.InputScopeId is not null);
        if (scopeStart > 0) path.RemoveRange(0, scopeStart);
        // The path is used only during admission. Leases retain item data, not
        // an obsolete parent tree or its page-level shortcut definitions.
        return path.AsReadOnly();

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
