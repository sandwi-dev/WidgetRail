using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>Exact indexed query identity. Payload eviction never changes Count.</summary>
public sealed record IndexedCollectionDescriptor(string SourceId, long QueryGeneration, long ContentRevision, int Count)
{
    /// <summary>Earliest query in an explicit same-filter refresh chain; normal query replacement clears this proof.</summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public long? RefreshFromQuery { get; init; }
    /// <summary>Null means a complete finite query. Otherwise Count is only the discovered, addressable prefix.</summary>
    public DiscoveredCollectionState? Discovery { get; init; }
    /// <summary>Opt-in query-bound key lookup for viewport continuity; grants no item action authority.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanLocateItems { get; init; }
    /// <summary>Opt-in viewport continuity domain. Equal epochs permit the host to keep an already-at-end viewport at the end; they grant no mutation or input authority.</summary>
    public long? EndFollowingEpoch { get; init; }
    /// <summary>Earliest preceding query in an uninterrupted verified forward-tail chain within EndFollowingEpoch. Permits coalescing intermediate publications.</summary>
    public long? EndFollowingPreviousQuery { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanRecoverViewport { get; init; }
    /// <summary>Finite loaded snapshots may append an unchanged immutable prefix without retiring its exact leases. Membership removal, edits and context changes still invalidate them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RetainsAppendedPrefix { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ObservesBoundaries { get; init; }

}

public enum DiscoveredCollectionStatus { Ready, Loading, Failed, LimitReached }
public sealed record DiscoveredCollectionState(long Revision, bool HasMore, DiscoveredCollectionStatus Status,
    int MaximumItems, string? ErrorCode = null, string? ErrorMessage = null);
public enum IndexedCollectionRequestKind { Range, Continue, Retry, FocusPreview, Locate, ObserveViewport, RecoverViewport }

public enum IndexedCollectionBoundary { Before, After }
public sealed record IndexedCollectionBoundaries(bool Before, bool After);

/// <summary>Entry from another control through directional navigation; explicit focus requests retain their own policy.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IndexedDirectionalEntryMode>))]
public enum IndexedDirectionalEntryMode { Remembered = 0, VisibleEdge = 1 }
public sealed record IndexedViewportObservation(string ViewportId, long Sequence, bool Active, bool AtEnd)
{
    public IndexedCollectionBoundary? Boundary { get; init; }
    public bool? PhysicalAtEnd { get; init; }
    public bool? PhysicalAtStart { get; init; }
    public IReadOnlyList<string>? ProtectedKeys { get; init; }
}
public sealed record IndexedViewportRecovery(string ViewportId, long Sequence, string AnchorKey)
{
    public IReadOnlyList<string>? NeighborKeys { get; init; }
}
public sealed record IndexedViewportRecoveryResult(bool Recovered, string? AnchorKey);

/// <summary>One contiguous display-only heading and item run in an existing flat indexed query.</summary>
public sealed record IndexedCollectionGroup(string Key, string Header, int Count);

/// <summary>A logical occurrence in an immutable query; content refresh does not change its identity.</summary>
public sealed record IndexedCollectionFocusTarget(string CollectionId, string SourceId, long QueryGeneration, string ItemKey, int Index);

public sealed record IndexedCollectionRangeRequest(
    string CollectionId, IndexedCollectionDescriptor Source, int StartIndex, int Count,
    string DemandId, string? PinnedLayoutId = null)
{
    /// <summary>Continuation control demands have zero rows and run on the bounded range lane.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IndexedCollectionRequestKind Kind { get; init; }
    public string? PreviewKey { get; init; }
    public string? LocateKey { get; init; }
    public IndexedViewportObservation? Viewport { get; init; }
    public IndexedViewportRecovery? Recovery { get; init; }
}

public sealed record IndexedCollectionItem(string Key, ViewNode Root);

public sealed record IndexedCollectionRange(
    string WidgetInstanceId, string CollectionId, IndexedCollectionDescriptor Source,
    string ScopeId, int StartIndex, string DemandId, IReadOnlyList<IndexedCollectionItem> Items,
    string? PinnedLayoutId = null)
{
    public int? LocatedIndex { get; init; }
    public bool? ViewportRecovered { get; init; }
    public string? ViewportAnchorKey { get; init; }
}

public static class IndexedCollectionLimits
{
    public const int MaximumGroups = 256;
    public const int MaximumContinuityItems = 512;
    public const int MaximumRangeItems = 64;
    public const int MaximumRangeNodes = ProtocolConstants.MaximumNodeCount;
}

/// <summary>
/// Shared validation for worker and host. Parent declarations remain authoritative;
/// an indexed response is not an independent page or input scope.
/// </summary>
public static class IndexedCollectionContract
{
    /// <summary>Optional complete occurrence order for bounded local collections. It grants no action authority.</summary>
    public static void ValidateOccurrenceKeys(IReadOnlyList<string> keys, IndexedCollectionDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ValidateDescriptor(source);
        if (source.Discovery is not null || keys.Count != source.Count || keys.Count > IndexedCollectionLimits.MaximumContinuityItems)
            throw new ArgumentException("Continuity requires a complete bounded occurrence order.", nameof(keys));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            RequireId(key);
            if (!seen.Add(key)) throw new ArgumentException("Continuity keys must identify unique occurrences.", nameof(keys));
        }
    }

    /// <summary>Find the same surviving occurrence, falling back to the next old neighbour then the preceding one.</summary>
    public static int RebaseOccurrence(IReadOnlyList<string> previous, IReadOnlyList<string> next, string key)
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < next.Count; ++i) positions.Add(next[i], i);
        if (positions.TryGetValue(key, out var exact)) return exact;
        var old = -1;
        for (var i = 0; i < previous.Count; ++i) if (previous[i] == key) { old = i; break; }
        if (old < 0) return -1;
        for (var i = old + 1; i < previous.Count; ++i) if (positions.TryGetValue(previous[i], out var after)) return after;
        for (var i = old - 1; i >= 0; --i) if (positions.TryGetValue(previous[i], out var before)) return before;
        return -1;
    }

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
        if(source.RefreshFromQuery is {} refresh && (refresh<0 || refresh>=source.QueryGeneration || source.Discovery is not null))
            throw new ArgumentException("Refresh provenance requires an earlier finite query.",nameof(source));
        RequireId(source.SourceId);
        ArgumentOutOfRangeException.ThrowIfNegative(source.QueryGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(source.ContentRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(source.Count);
        if (source.EndFollowingEpoch is <= 0 || source.EndFollowingEpoch is not null && source.Discovery is not null)
            throw new ArgumentException("End following requires a positive finite-query continuity epoch.", nameof(source));
        if (source.EndFollowingPreviousQuery is { } previous && (source.EndFollowingEpoch is null || previous < 0 || previous >= source.QueryGeneration))
            throw new ArgumentException("End following append provenance must name an earlier query.", nameof(source));
        if (source.RetainsAppendedPrefix && (source.EndFollowingEpoch is null || source.Discovery is not null || source.Count > IndexedCollectionLimits.MaximumContinuityItems))
            throw new ArgumentException("Finite prefix retention requires a bounded end-following query.", nameof(source));
        if (source.CanRecoverViewport && source.EndFollowingEpoch is null) throw new ArgumentException("Viewport recovery requires an end-following domain.", nameof(source));
        if (source.ObservesBoundaries && (source.EndFollowingEpoch is null || source.Count>IndexedCollectionLimits.MaximumContinuityItems))
            throw new ArgumentException("Boundary observation requires a bounded finite continuity domain.", nameof(source));
        if (source.CanLocateItems && source.Discovery is not null) throw new ArgumentException("Key lookup requires a finite query.", nameof(source));
        if (source.Discovery is { } discovery && (discovery.Revision < 0 || !Enum.IsDefined(discovery.Status) ||
            discovery.MaximumItems is < 1 or > 4096 || source.Count > discovery.MaximumItems ||
            discovery.ErrorCode is { } code && !ProtocolValidationIdentifierContext.IsSafeIdentifier(code) ||
            discovery.ErrorMessage is { Length: > 256 } || discovery.ErrorMessage?.Any(char.IsControl) == true ||
            discovery.Status == DiscoveredCollectionStatus.Failed && (discovery.ErrorCode is null || string.IsNullOrWhiteSpace(discovery.ErrorMessage)) ||
            discovery.Status == DiscoveredCollectionStatus.LimitReached && !discovery.HasMore))
            throw new ArgumentException("Invalid discovered collection state.", nameof(source));
    }

    /// <summary>Old prefixes retain exact data/action authority only within an unchanged append-only query.</summary>
    public static bool RetainsPrefix(IndexedCollectionDescriptor? current, IndexedCollectionDescriptor previous) =>
        current == previous || current is not null &&
        current.SourceId == previous.SourceId && current.QueryGeneration == previous.QueryGeneration &&
        current.ContentRevision == previous.ContentRevision && current.Count >= previous.Count &&
        (current.Discovery is { } next && previous.Discovery is { } prior &&
            next.MaximumItems == prior.MaximumItems && next.Revision >= prior.Revision ||
         current.RetainsAppendedPrefix && previous.RetainsAppendedPrefix &&
            current.EndFollowingEpoch == previous.EndFollowingEpoch && current.CanRecoverViewport == previous.CanRecoverViewport &&
            current.CanLocateItems == previous.CanLocateItems && current.ObservesBoundaries==previous.ObservesBoundaries);

    public static void ValidateRequest(IndexedCollectionRangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDescriptor(request.Source);
        RequireId(request.CollectionId);
        RequireId(request.DemandId);
        if (request.PinnedLayoutId is { } pinned) RequireId(pinned);
        if (!Enum.IsDefined(request.Kind)) throw new ArgumentException("Unknown indexed request kind.", nameof(request));
        if (request.Kind is IndexedCollectionRequestKind.ObserveViewport or IndexedCollectionRequestKind.RecoverViewport)
        {
            if (request.Source.EndFollowingEpoch is null || request.StartIndex != 0 || request.Count != 0 || request.PreviewKey is not null || request.LocateKey is not null)
                throw new ArgumentException("Viewport control requires an opted-in finite query and no rows.", nameof(request));
            if (request.Kind == IndexedCollectionRequestKind.ObserveViewport)
            {
                if (request.Viewport is not { } observation || request.Recovery is not null || observation.Sequence <= 0 ||
                    !Guid.TryParseExact(observation.ViewportId, "N", out _) || !observation.Active && observation.AtEnd)
                    throw new ArgumentException("Invalid viewport observation.", nameof(request));
                if ((observation.Boundary is not null || observation.PhysicalAtEnd is not null || observation.PhysicalAtStart is not null) && !request.Source.ObservesBoundaries ||
                    observation.Boundary is { } boundary && (!Enum.IsDefined(boundary) || !observation.Active) ||
                    (observation.PhysicalAtEnd==true || observation.PhysicalAtStart==true) && !observation.Active)
                    throw new ArgumentException("Invalid boundary observation.", nameof(request));
                if(observation.ProtectedKeys is {} protectedKeys && (observation.Boundary is null || protectedKeys.Count>IndexedCollectionLimits.MaximumContinuityItems ||
                    protectedKeys.Any(key=>!ProtocolValidationIdentifierContext.IsSafeIdentifier(key)) || protectedKeys.Distinct(StringComparer.Ordinal).Count()!=protectedKeys.Count))
                    throw new ArgumentException("Invalid protected boundary occurrences.",nameof(request));
            }
            else if (!request.Source.CanRecoverViewport || request.Recovery is not { } recovery || request.Viewport is not null || recovery.Sequence <= 0 ||
                !Guid.TryParseExact(recovery.ViewportId, "N", out _) || !ProtocolValidationIdentifierContext.IsSafeIdentifier(recovery.AnchorKey))
                throw new ArgumentException("Invalid viewport recovery.", nameof(request));
            if (request.Recovery?.NeighborKeys is { } neighbors && (neighbors.Count > IndexedCollectionLimits.MaximumContinuityItems ||
                neighbors.Any(key => !ProtocolValidationIdentifierContext.IsSafeIdentifier(key) || key == request.Recovery.AnchorKey) ||
                neighbors.Distinct(StringComparer.Ordinal).Count() != neighbors.Count)) throw new ArgumentException("Invalid viewport recovery neighbors.", nameof(request));
            return;
        }
        if (request.Viewport is not null || request.Recovery is not null) throw new ArgumentException("Only viewport controls carry viewport data.", nameof(request));
        if (request.Kind == IndexedCollectionRequestKind.Locate)
        {
            RequireId(request.LocateKey!);
            if (!request.Source.CanLocateItems || request.Source.Discovery is not null || request.StartIndex != 0 || request.Count != 0 || request.PreviewKey is not null)
                throw new ArgumentException("Lookup requires an opted-in finite query and one key.", nameof(request));
            return;
        }
        if (request.LocateKey is not null) throw new ArgumentException("Only locate requests carry a lookup key.", nameof(request));
        if (request.Kind == IndexedCollectionRequestKind.FocusPreview)
        {
            RequireId(request.PreviewKey!);
            if (request.Source.Count != 1 || request.Source.Discovery is not null || request.StartIndex != 0 || request.Count != 1)
                throw new ArgumentException("A preview demand resolves one content key.", nameof(request));
            return;
        }
        if (request.PreviewKey is not null) throw new ArgumentException("Only preview demands carry a preview key.", nameof(request));
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
        if (request.Kind is IndexedCollectionRequestKind.ObserveViewport or IndexedCollectionRequestKind.RecoverViewport)
        {
            if (range.LocatedIndex is not null || request.Kind == IndexedCollectionRequestKind.RecoverViewport != (range.ViewportRecovered is not null))
                throw new ArgumentException("Invalid viewport control response.", nameof(range));
            if (range.ViewportRecovered == true ? range.ViewportAnchorKey is null || !ProtocolValidationIdentifierContext.IsSafeIdentifier(range.ViewportAnchorKey) : range.ViewportAnchorKey is not null)
                throw new ArgumentException("Invalid viewport recovery anchor.", nameof(range));
            if (range.ViewportRecovered == true && range.ViewportAnchorKey != request.Recovery!.AnchorKey &&
                request.Recovery.NeighborKeys?.Contains(range.ViewportAnchorKey!) != true)
                throw new ArgumentException("Recovery chose an undeclared neighbor.", nameof(range));
            return;
        }
        if (range.ViewportRecovered is not null || range.ViewportAnchorKey is not null) throw new ArgumentException("Only recovery responses report recovery.", nameof(range));
        if (request.Kind == IndexedCollectionRequestKind.Locate)
        {
            if (range.LocatedIndex is { } found && (found < 0 || found >= request.Source.Count))
                throw new ArgumentException("Located index is outside the declared query.", nameof(range));
            return;
        }
        if (range.LocatedIndex is not null) throw new ArgumentException("Only a lookup response contains a located index.", nameof(range));
        if (request.Kind == IndexedCollectionRequestKind.FocusPreview)
        {
            var item=range.Items[0];
            ArgumentNullException.ThrowIfNull(item);
            if (item.Key != request.PreviewKey) throw new ArgumentException("Preview key changed during resolution.");
            FocusPreviewContentContract.Validate(item.Root,binding.Node.FocusPreview?.EnterButton is not null, binding.Node.FocusPreview?.AllowMedia == true);
            return;
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var nodes = 0;
        var itemIndex = request.StartIndex;
        foreach (var item in range.Items)
        {
            ArgumentNullException.ThrowIfNull(item);
            RequireId(item.Key);
            if (binding.Node.IndexedOccurrenceKeys is { } order && order[itemIndex] != item.Key)
                throw new ArgumentException("Indexed range does not match declared occurrence order.", nameof(range));
            ++itemIndex;
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
                        InitialFocusId = layout.InitialFocusId, FocusGroupEntryRequest = null, ScrollRevealRequest = null, QuickActions = [], PinnedLayouts = [] };
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
                if (request.Kind == IndexedCollectionRequestKind.FocusPreview)
                {
                    if (entry.Node.Kind != ViewNodeKind.FocusPresentationSurface || entry.Node.FocusPreview is null || entry.Node.FocusPreviewSource != request.Source || scope != projection.ActiveInputScopeId)
                        throw new ArgumentException("Preview source or input scope has retired.", nameof(request));
                    return (entry.Node,scope,projection);
                }
                var viewportControl = request.Kind is IndexedCollectionRequestKind.ObserveViewport or IndexedCollectionRequestKind.RecoverViewport;
                if (entry.Node.Kind != ViewNodeKind.IndexedCollection || !(viewportControl
                    ? SameViewportDomain(entry.Node.IndexedCollection, request.Source) && scope == projection.ActiveInputScopeId
                    : RetainsPrefix(entry.Node.IndexedCollection, request.Source)))
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

    /// <summary>Control acknowledgments may follow their own publication, but never a replacement semantic domain. This does not admit range reads or row actions.</summary>
    public static bool SameViewportDomain(IndexedCollectionDescriptor? current, IndexedCollectionDescriptor previous) =>
        current is not null && current.SourceId == previous.SourceId && current.QueryGeneration >= previous.QueryGeneration && previous.EndFollowingEpoch is { } epoch && current.EndFollowingEpoch == epoch &&
        current.CanRecoverViewport == previous.CanRecoverViewport && current.ObservesBoundaries==previous.ObservesBoundaries;
}
