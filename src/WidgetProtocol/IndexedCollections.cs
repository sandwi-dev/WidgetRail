namespace WidgetRail.WidgetProtocol;

/// <summary>Exact indexed query identity. Payload eviction never changes Count.</summary>
public sealed record IndexedCollectionDescriptor(string SourceId, long QueryGeneration, long ContentRevision, int Count);

public sealed record IndexedCollectionRangeRequest(
    string CollectionId, IndexedCollectionDescriptor Source, int StartIndex, int Count,
    string DemandId, string? PinnedLayoutId = null);

public sealed record IndexedCollectionItem(string Key, ViewNode Root);

public sealed record IndexedCollectionRange(
    string WidgetInstanceId, string CollectionId, IndexedCollectionDescriptor Source,
    string ScopeId, int StartIndex, string DemandId, IReadOnlyList<IndexedCollectionItem> Items,
    string? PinnedLayoutId = null);

public static class IndexedCollectionLimits
{
    public const int MaximumRangeItems = 64;
    public const int MaximumRangeNodes = ProtocolConstants.MaximumNodeCount;
}

/// <summary>
/// Shared validation for worker and host. Parent declarations remain authoritative;
/// an indexed response is not an independent page or input scope.
/// </summary>
public static class IndexedCollectionContract
{
    public static void ValidateDescriptor(IndexedCollectionDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireId(source.SourceId);
        ArgumentOutOfRangeException.ThrowIfNegative(source.QueryGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(source.ContentRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(source.Count);
    }

    public static void ValidateRequest(IndexedCollectionRangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDescriptor(request.Source);
        RequireId(request.CollectionId);
        RequireId(request.DemandId);
        if (request.PinnedLayoutId is { } pinned) RequireId(pinned);
        if (request.StartIndex < 0 || request.Count is < 1 or > IndexedCollectionLimits.MaximumRangeItems
            || (long)request.StartIndex + request.Count > request.Source.Count)
            throw new ArgumentOutOfRangeException(nameof(request), "Indexed range is outside the declared query.");
    }

    public static string ResolveScope(ViewSnapshot parent, IndexedCollectionRangeRequest request) => Resolve(parent, request).Scope;

    /// <summary>Returns the active input scope of the request's main or pinned projection.</summary>
    public static string ResolveActiveInputScope(ViewSnapshot parent, IndexedCollectionRangeRequest request) =>
        Resolve(parent, request).Projection.ActiveInputScopeId;

    public static void ValidateRange(ViewSnapshot parent, IndexedCollectionRangeRequest request, IndexedCollectionRange range)
    {
        var binding = Resolve(parent, request);
        ArgumentNullException.ThrowIfNull(range);
        if (range.WidgetInstanceId != parent.WidgetInstanceId || range.CollectionId != request.CollectionId || range.Source != request.Source
            || range.ScopeId != binding.Scope || range.StartIndex != request.StartIndex || range.DemandId != request.DemandId
            || range.PinnedLayoutId != request.PinnedLayoutId || range.Items is null || range.Items.Count != request.Count)
            throw new ArgumentException("Indexed response authority does not match its request.", nameof(range));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var nodes = 0;
        foreach (var item in range.Items)
        {
            ArgumentNullException.ThrowIfNull(item);
            RequireId(item.Key);
            if (!keys.Add(item.Key) || item.Root is null || item.Root.CollectionItemKey != item.Key
                || item.Root.Kind is not (ViewNodeKind.Button or ViewNodeKind.ActionSurface))
                throw new ArgumentException("Indexed items require unique keys and keyed button/action-surface roots.", nameof(range));
            var pending = new Stack<ViewNode>(); pending.Push(item.Root);
            while (pending.TryPop(out var node))
            {
                if (node is null || node.Children is null || ++nodes > IndexedCollectionLimits.MaximumRangeNodes)
                    throw new ArgumentException("Indexed range exceeds its node budget.", nameof(range));
                if (node.Children.Count > IndexedCollectionLimits.MaximumRangeNodes - nodes - pending.Count)
                    throw new ArgumentException("Indexed range exceeds its node budget.", nameof(range));
                foreach (var child in node.Children) pending.Push(child);
                if (node.FocusPresentation is { } focused) pending.Push(focused);
                if (node.DefaultFocusPresentation is { } fallback) pending.Push(fallback);
            }
        }
        // Validate item declarations in their real parent context. This preserves
        // static focus neighbors and presentation-slot ownership without publishing
        // a materialized collection tree or weakening normal snapshot validation.
        ViewNode Project(ViewNode node) => ReferenceEquals(node, binding.Node)
            ? node with { Children = range.Items.Select(item => item.Root).ToArray() }
            : node with { Children = node.Children.Select(Project).ToArray() };
        var projection = binding.Projection with { Root = Project(binding.Projection.Root), PinnedLayouts = [] };
        var errors = ViewSnapshotValidator.Validate(projection, ProtocolVersionRequirements.Calculate(projection), materializedIndexedCollections: true);
        if (errors.Count != 0) throw new ProtocolValidationException(errors);
    }

    internal static (ViewNode Node, string Scope, ViewSnapshot Projection) Resolve(ViewSnapshot parent, IndexedCollectionRangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var parentErrors = ViewSnapshotValidator.Validate(parent);
        if (parentErrors.Count != 0) throw new ProtocolValidationException(parentErrors);
        ValidateRequest(request);
        var projection = parent;
        if (request.PinnedLayoutId is { } id)
        {
            if (id == PinnedSurfaceContract.FullWidgetLayoutId) projection = PinnedSurfaceContract.WithoutModal(parent);
            else
            {
                var layout = parent.PinnedLayouts.SingleOrDefault(layout => layout.Id == id)
                    ?? throw new ArgumentException("Indexed request refers to an undeclared pinned projection.", nameof(request));
                if (layout.Root is { } root)
                    projection = parent with { Root = root, ActiveInputScopeId = layout.ActiveInputScopeId ?? root.InputScopeId ?? root.Id,
                        InitialFocusId = layout.InitialFocusId, FocusGroupEntryRequest = null, QuickActions = [], PinnedLayouts = [] };
                else projection = PinnedSurfaceContract.WithoutModal(parent);
            }
        }
        var pending = new Stack<(ViewNode Node, string Scope)>();
        pending.Push((projection.Root, projection.Root.InputScopeId ?? projection.Root.Id));
        while (pending.TryPop(out var entry))
        {
            var scope = entry.Node.InputScopeId ?? entry.Scope;
            if (entry.Node.Id == request.CollectionId)
            {
                if (entry.Node.Kind != ViewNodeKind.IndexedCollection || entry.Node.IndexedCollection != request.Source)
                    throw new ArgumentException("Indexed request does not match the current parent declaration.", nameof(request));
                return (entry.Node, scope, projection);
            }
            foreach (var child in entry.Node.Children) pending.Push((child, scope));
        }
        throw new ArgumentException("Indexed collection is not declared in the current parent presentation.", nameof(request));
    }

    private static void RequireId(string value)
    {
        if (value is null || !ProtocolValidationIdentifierContext.IsSafeIdentifier(value))
            throw new ArgumentException("Indexed collection identity is invalid.");
    }
}
