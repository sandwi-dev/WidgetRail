namespace WidgetRail.WidgetProtocol;

/// <summary>Exact indexed query identity. Payload eviction never changes Count.</summary>
public sealed record IndexedCollectionDescriptor(string SourceId, long QueryGeneration, long ContentRevision, int Count)
{
    /// <summary>Null means a complete finite query. Otherwise Count is only the discovered, addressable prefix.</summary>
    public DiscoveredCollectionState? Discovery { get; init; }
}

public enum DiscoveredCollectionStatus { Ready, Loading, Failed, LimitReached }
public sealed record DiscoveredCollectionState(long Revision, bool HasMore, DiscoveredCollectionStatus Status,
    int MaximumItems, string? ErrorCode = null, string? ErrorMessage = null);
public enum IndexedCollectionRequestKind { Range, Continue, Retry }

/// <summary>One contiguous display-only heading and item run in an existing flat indexed query.</summary>
public sealed record IndexedCollectionGroup(string Key, string Header, int Count);

/// <summary>A logical occurrence in an immutable query; content refresh does not change its identity.</summary>
public sealed record IndexedCollectionFocusTarget(string CollectionId, string SourceId, long QueryGeneration, string ItemKey, int Index);

public sealed record IndexedCollectionRangeRequest(
    string CollectionId, IndexedCollectionDescriptor Source, int StartIndex, int Count,
    string DemandId, string? PinnedLayoutId = null)
{
    /// <summary>Continuation control demands have zero rows and run on the bounded range lane.</summary>
    public IndexedCollectionRequestKind Kind { get; init; }
}

public sealed record IndexedCollectionItem(string Key, ViewNode Root);

public sealed record IndexedCollectionRange(
    string WidgetInstanceId, string CollectionId, IndexedCollectionDescriptor Source,
    string ScopeId, int StartIndex, string DemandId, IReadOnlyList<IndexedCollectionItem> Items,
    string? PinnedLayoutId = null);

public static class IndexedCollectionLimits
{
    public const int MaximumGroups = 256;
    public const int MaximumRangeItems = 64;
    public const int MaximumRangeNodes = ProtocolConstants.MaximumNodeCount;
}

/// <summary>
/// Shared validation for worker and host. Parent declarations remain authoritative;
/// an indexed response is not an independent page or input scope.
/// </summary>
public static class IndexedCollectionContract
{
    /// <summary>Groups partition one vertical flat source; they do not create scopes or data sources.</summary>
    public static void ValidateGroups(IReadOnlyList<IndexedCollectionGroup> groups,
        IndexedCollectionDescriptor source, ScrollAxis axis)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ValidateDescriptor(source);
        if (source.Discovery is not null) throw new ArgumentException("Discovered prefixes do not support static group partitions.", nameof(source));
        if (axis != ScrollAxis.Vertical)
            throw new ArgumentException("Indexed grouping requires a vertical collection.", nameof(axis));
        if (groups.Count > IndexedCollectionLimits.MaximumGroups)
            throw new ArgumentOutOfRangeException(nameof(groups), "Indexed grouping exceeds its group limit.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        long count = 0;
        foreach (var group in groups)
        {
            ArgumentNullException.ThrowIfNull(group);
            RequireId(group.Key);
            if (!keys.Add(group.Key)) throw new ArgumentException("Indexed group keys must be unique.", nameof(groups));
            if (string.IsNullOrWhiteSpace(group.Header) || group.Header.Length > ProtocolConstants.MaximumStringLength || group.Header.Any(char.IsControl))
                throw new ArgumentException("Indexed group headings must be nonempty bounded visible text.", nameof(groups));
            ArgumentOutOfRangeException.ThrowIfNegative(group.Count);
            count += group.Count;
        }
        if (count != source.Count)
            throw new ArgumentException("Indexed group counts must partition the exact flat query count.", nameof(groups));
    }

    public static void ValidateFocusTarget(IndexedCollectionFocusTarget target, string collectionId, IndexedCollectionDescriptor source)
    {
        ValidateFocusTargetShape(target, collectionId);
        ValidateDescriptor(source);
        if (target.CollectionId != collectionId || target.SourceId != source.SourceId || target.QueryGeneration != source.QueryGeneration ||
            target.Index < 0 || target.Index >= source.Count)
            throw new ArgumentException("Indexed focus target does not belong to the declared collection query.", nameof(target));
    }

    internal static void ValidateFocusTargetShape(IndexedCollectionFocusTarget target, string collectionId)
    {
        ArgumentNullException.ThrowIfNull(target);
        RequireId(target.CollectionId); RequireId(target.SourceId); RequireId(target.ItemKey);
        if (target.CollectionId != collectionId || target.QueryGeneration < 0 || target.Index < 0)
            throw new ArgumentException("Indexed focus target identity is invalid.", nameof(target));
    }

    public static void ValidateDescriptor(IndexedCollectionDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireId(source.SourceId);
        ArgumentOutOfRangeException.ThrowIfNegative(source.QueryGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(source.ContentRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(source.Count);
        if (source.Discovery is { } discovery && (discovery.Revision < 0 || !Enum.IsDefined(discovery.Status) ||
            discovery.MaximumItems is < 1 or > 4096 || source.Count > discovery.MaximumItems ||
            discovery.ErrorCode is { } code && !ProtocolValidationIdentifierContext.IsSafeIdentifier(code) ||
            discovery.ErrorMessage is { Length: > 256 } || discovery.ErrorMessage?.Any(char.IsControl) == true ||
            discovery.Status == DiscoveredCollectionStatus.Failed && (discovery.ErrorCode is null || string.IsNullOrWhiteSpace(discovery.ErrorMessage)) ||
            discovery.Status == DiscoveredCollectionStatus.LimitReached && !discovery.HasMore))
            throw new ArgumentException("Invalid discovered collection state.", nameof(source));
    }

    /// <summary>Old prefixes retain data/action authority only for append-only discovered queries.</summary>
    public static bool RetainsPrefix(IndexedCollectionDescriptor? current, IndexedCollectionDescriptor previous) =>
        current == previous || current is { Discovery: { } next } && previous.Discovery is { } prior &&
        current.SourceId == previous.SourceId && current.QueryGeneration == previous.QueryGeneration &&
        current.ContentRevision == previous.ContentRevision && current.Count >= previous.Count &&
        next.MaximumItems == prior.MaximumItems && next.Revision >= prior.Revision;

    public static void ValidateRequest(IndexedCollectionRangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDescriptor(request.Source);
        RequireId(request.CollectionId);
        RequireId(request.DemandId);
        if (request.PinnedLayoutId is { } pinned) RequireId(pinned);
        if (!Enum.IsDefined(request.Kind)) throw new ArgumentException("Unknown indexed request kind.", nameof(request));
        if (request.Kind != IndexedCollectionRequestKind.Range)
        {
            if (request.Source.Discovery is null || request.Count != 0 || request.StartIndex != request.Source.Count)
                throw new ArgumentException("Continuation demand must name the exact discovered tail.", nameof(request));
            return;
        }
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
                if (entry.Node.Kind != ViewNodeKind.IndexedCollection || !RetainsPrefix(entry.Node.IndexedCollection, request.Source))
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
