using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Immutable query values and item values must be safe to read concurrently.</summary>
public sealed record WidgetIndexedCollectionOptions<TQuery, TItem> where TQuery : notnull where TItem : notnull
{
    public required Func<TQuery, int, int, CancellationToken, ValueTask<IReadOnlyList<TItem>>> ReadRange { get; init; }
    public required Func<TItem, WidgetCollectionItemKey> ItemKey { get; init; }
    /// <summary>Pure item declaration, evaluated only for demanded rows using the captured query.</summary>
    public required Func<TQuery, TItem, WidgetIndexedItemContext, WidgetElement> RenderItem { get; init; }
    /// <summary>Handles a local row action using the immutable query/item captured for that row.</summary>
    public required Func<TQuery, TItem, WidgetActionEvent, CancellationToken, ValueTask> OnAction { get; init; }
    /// <summary>Resolves opaque artwork declared by this row; HTTPS images remain host-owned.</summary>
    public Func<TQuery, TItem, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>>? ResolveArtwork { get; init; }
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

public sealed class WidgetIndexedItemContext
{
    private readonly WidgetIdScope ids;
    public int Index { get; }
    public WidgetCollectionItemKey Key { get; }
    internal WidgetIndexedItemContext(string sourceId, string collectionId, int index, WidgetCollectionItemKey key)
    {
        Index = index; Key = key;
        // All components are validated identifiers, which cannot contain the separator.
        ids = WidgetIds.Scope(WidgetIds.Scope("indexed").KeyedId("item", sourceId + "|" + collectionId + "|" + key.Value));
    }
    /// <summary>Stable item-scoped ID, independent of its position or realization.</summary>
    public string Id(string name) => ids.Id(name);
}

internal interface IWidgetIndexedCollection
{
    IndexedCollectionDescriptor Descriptor { get; }
    ValueTask<WidgetIndexedRead> ReadAsync(IndexedCollectionRangeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// A registered exact-count source. Publishing a query changes membership/order;
/// updating content preserves all keys and their positions. Neither method renders
/// every item. Widget data/provider code stays in the worker process.
/// </summary>
public sealed class WidgetIndexedCollection<TQuery, TItem> : IWidgetIndexedCollection where TQuery : notnull where TItem : notnull
{
    private sealed record Query(TQuery Value, IndexedCollectionDescriptor Descriptor, CancellationTokenSource Lifetime);
    private readonly object gate = new();
    private readonly WidgetIndexedCollectionOptions<TQuery, TItem> options;
    private readonly Action changed;
    private readonly CancellationToken widgetLifetime;
    private Query current;
    private int activeLoads;

    internal WidgetIndexedCollection(string sourceId, TQuery query, int count,
        WidgetIndexedCollectionOptions<TQuery, TItem> options, Action changed, CancellationToken widgetLifetime)
    {
        StableIdentifier.Validate(sourceId, nameof(sourceId));
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ReadRange);
        ArgumentNullException.ThrowIfNull(options.ItemKey);
        ArgumentNullException.ThrowIfNull(options.RenderItem);
        ArgumentNullException.ThrowIfNull(options.OnAction);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (options.ReadTimeout < TimeSpan.FromMilliseconds(100) || options.ReadTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(options.ReadTimeout));
        this.options = options; this.changed = changed; this.widgetLifetime = widgetLifetime;
        current = new(query, new(sourceId, 1, 0, count), new());
    }

    public IndexedCollectionDescriptor Descriptor { get { lock (gate) return current.Descriptor; } }

    /// <summary>Creates a logical target without realizing a row. The host checks its occurrence key before focusing.</summary>
    public IndexedCollectionFocusTarget FocusTarget(string collectionId, WidgetCollectionItemKey key, int index)
    {
        var source = Descriptor;
        var target = new IndexedCollectionFocusTarget(collectionId, source.SourceId, source.QueryGeneration, key.Value, index);
        IndexedCollectionContract.ValidateFocusTarget(target, collectionId, source);
        return target;
    }

    /// <summary>Requests remembered/default collection entry, or an exact item in the current query.</summary>
    public FocusGroupEntryRequest Enter(string collectionId, long requestId, IndexedCollectionFocusTarget? item = null)
    {
        StableIdentifier.Validate(collectionId, nameof(collectionId));
        if (requestId is <= 0 or > ProtocolConstants.MaximumFocusGroupEntryRequestId) throw new ArgumentOutOfRangeException(nameof(requestId));
        if (item is not null) IndexedCollectionContract.ValidateFocusTarget(item, collectionId, Descriptor);
        return new() { RequestId = requestId, GroupId = collectionId, IndexedItem = item };
    }

    public void PublishQuery(TQuery query, int count)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Query previous;
        lock (gate)
        {
            widgetLifetime.ThrowIfCancellationRequested();
            previous = current;
            current = new(query, current.Descriptor with { QueryGeneration = checked(current.Descriptor.QueryGeneration + 1), ContentRevision = 0, Count = count }, new());
        }
        try { Retire(previous); }
        finally { changed(); }
    }

    /// <summary>
    /// Refreshes item content without changing count, keys or ordering. Use
    /// PublishQuery for membership changes. Query values must be immutable.
    /// </summary>
    public void UpdateContent(TQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Query previous;
        lock (gate)
        {
            widgetLifetime.ThrowIfCancellationRequested();
            previous = current;
            current = new(query, current.Descriptor with { ContentRevision = checked(current.Descriptor.ContentRevision + 1) }, new());
        }
        try { Retire(previous); }
        finally { changed(); }
    }

    private static void Retire(Query query)
    {
        try { query.Lifetime.Cancel(); }
        finally { query.Lifetime.Dispose(); }
    }

    async ValueTask<WidgetIndexedRead> IWidgetIndexedCollection.ReadAsync(IndexedCollectionRangeRequest request, CancellationToken cancellationToken)
    {
        IndexedCollectionContract.ValidateRequest(request);
        Query captured;
        CancellationTokenSource lifetime;
        lock (gate)
        {
            captured = current;
            if (request.Source != captured.Descriptor) throw new InvalidOperationException("The indexed query has changed.");
            if (activeLoads >= 4) throw new InvalidOperationException("The indexed source read limit has been reached.");
            cancellationToken.ThrowIfCancellationRequested();
            widgetLifetime.ThrowIfCancellationRequested();
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, widgetLifetime, captured.Lifetime.Token);
            lifetime.CancelAfter(options.ReadTimeout);
            ++activeLoads;
        }
        var token = lifetime.Token;
        // Keep the slot until the actual provider operation terminates, even if a
        // cancelled/timed-out caller has stopped waiting. Never accumulate unbounded
        // background work from providers that ignore cancellation.
        var load = Task.Run(async () => await options.ReadRange(captured.Value, request.StartIndex, request.Count, token).ConfigureAwait(false), token);
        _ = load.ContinueWith(task =>
        {
            _ = task.Exception;
            lock (gate) --activeLoads;
            lifetime.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        IReadOnlyList<TItem> values;
        try { values = await load.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !widgetLifetime.IsCancellationRequested)
        {
            lock (gate)
                if (!ReferenceEquals(current, captured)) throw new InvalidOperationException("The indexed query changed during loading.");
            throw new TimeoutException("The indexed source read timed out.");
        }
        if (values is null || values.Count != request.Count) throw new InvalidOperationException("An indexed reader must return exactly the requested range.");
        var copy = new TItem[request.Count];
        for (var index = 0; index < copy.Length; ++index) copy[index] = values[index];
        var result = new WidgetIndexedItemBinding[copy.Length];
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var nodeCount = 0;
        for (var index = 0; index < copy.Length; ++index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            widgetLifetime.ThrowIfCancellationRequested();
            lock (gate)
                if (!ReferenceEquals(current, captured)) throw new InvalidOperationException("The indexed query changed during item rendering.");
            var item = copy[index];
            ArgumentNullException.ThrowIfNull(item);
            var key = options.ItemKey(item);
            StableIdentifier.Validate(key.Value, nameof(options.ItemKey));
            if (!keys.Add(key.Value)) throw new InvalidOperationException("Indexed ranges require unique occurrence keys.");
            var context = new WidgetIndexedItemContext(captured.Descriptor.SourceId, request.CollectionId, request.StartIndex + index, key);
            var element = options.RenderItem(captured.Value, item, context)
                ?? throw new InvalidOperationException("The indexed renderer returned no element.");
            var node = element.ToProtocolNode();
            if (node.Kind is not (ViewNodeKind.Button or ViewNodeKind.ActionSurface) ||
                node.VisibleWhen is not (null or ResponsiveVisibility.Always) ||
                node.CollectionItemKey is { } declared && declared != key.Value)
                throw new InvalidOperationException("Indexed item roots must be buttons/action surfaces with matching occurrence keys.");
            var declaration = new IndexedCollectionItem(key.Value,
                WidgetDeclarationSnapshot.Freeze(node with { CollectionItemKey = key.Value }, 1, ref nodeCount));
            result[index] = new(declaration,
                (action, token) => options.OnAction(captured.Value, item, action, token),
                options.ResolveArtwork is { } resolver ? (handle, token) => resolver(captured.Value, item, handle, token) : null);
        }
        cancellationToken.ThrowIfCancellationRequested();
        widgetLifetime.ThrowIfCancellationRequested();
        lock (gate)
            if (!ReferenceEquals(current, captured)) throw new InvalidOperationException("The indexed query changed before publication.");
        return new(Array.AsReadOnly(result), nodeCount);
    }
}

/// <summary>Host-realized collection declaration. It never contains inline item trees.</summary>
public sealed record IndexedCollectionElement : ContainerElement
{
    internal IndexedCollectionElement(string id, IndexedCollectionDescriptor source, CollectionLayout layout,
        ScrollAxis axis, string accessibilityLabel) : base(id, [])
    {
        IndexedCollectionContract.ValidateDescriptor(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibilityLabel);
        ArgumentNullException.ThrowIfNull(layout);
        if (!Enum.IsDefined(axis)) throw new ArgumentOutOfRangeException(nameof(axis));
        if (!Enum.IsDefined(layout.Kind) || !double.IsFinite(layout.EstimatedItemExtent) ||
            layout.EstimatedItemExtent < ProtocolConstants.MinimumVirtualCollectionItemExtent ||
            layout.EstimatedItemExtent > ProtocolConstants.MaximumVirtualCollectionItemExtent)
            throw new ArgumentOutOfRangeException(nameof(layout));
        if (layout.Kind == CollectionLayoutKind.AdaptiveGrid &&
            (axis != ScrollAxis.Vertical || layout.MinimumColumnWidth is not { } width || !double.IsFinite(width) ||
             width < ProtocolConstants.MinimumGridColumnWidth || width > ProtocolConstants.MaximumGridColumnWidth ||
             layout.MaximumColumns is < 1 or > ProtocolConstants.MaximumGridColumns))
            throw new ArgumentOutOfRangeException(nameof(layout));
        Source = source; Layout = layout; Axis = axis; AccessibilityLabel = accessibilityLabel;
    }
    public IndexedCollectionDescriptor Source { get; }
    public CollectionLayout Layout { get; }
    public ScrollAxis Axis { get; }
    public string AccessibilityLabel { get; }
    public IReadOnlyList<IndexedCollectionGroup>? Groups { get; private init; }
    /// <summary>
    /// Adds display-only headings over contiguous portions of this vertical flat query.
    /// Counts must partition Source.Count exactly; zero-count groups are hidden by the host.
    /// Keys identify section occurrences, including repeated titles. Row identity, actions,
    /// focus scopes and source requests remain those of the original flat collection.
    /// </summary>
    public IndexedCollectionElement Grouped(params IndexedCollectionGroup[] groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Length > IndexedCollectionLimits.MaximumGroups)
            throw new ArgumentOutOfRangeException(nameof(groups));
        var frozen = Array.AsReadOnly(groups.ToArray());
        IndexedCollectionContract.ValidateGroups(frozen, Source, Axis);
        return this with { Groups = frozen };
    }
    internal override ViewNode ToProtocolNode() => ToContainerProtocolNode(ViewNodeKind.IndexedCollection) with
    {
        IndexedCollection = Source, IndexedGroups = Groups, CollectionLayout = Layout, ScrollAxis = Axis, AccessibilityLabel = AccessibilityLabel,
    };
}

public static partial class UI
{
    public static IndexedCollectionElement CollectionList<TQuery, TItem>(string id, WidgetIndexedCollection<TQuery, TItem> source,
        double estimatedItemExtent, string accessibilityLabel, ScrollAxis axis = ScrollAxis.Vertical) where TQuery : notnull where TItem : notnull =>
        new(id, (source ?? throw new ArgumentNullException(nameof(source))).Descriptor, new() { Kind = CollectionLayoutKind.List, EstimatedItemExtent = estimatedItemExtent }, axis, accessibilityLabel);

    public static IndexedCollectionElement CollectionGrid<TQuery, TItem>(string id, WidgetIndexedCollection<TQuery, TItem> source,
        double minimumColumnWidth, double estimatedItemExtent, string accessibilityLabel, int? maximumColumns = null) where TQuery : notnull where TItem : notnull =>
        new(id, (source ?? throw new ArgumentNullException(nameof(source))).Descriptor, new() { Kind = CollectionLayoutKind.AdaptiveGrid, EstimatedItemExtent = estimatedItemExtent,
            MinimumColumnWidth = minimumColumnWidth, MaximumColumns = maximumColumns }, ScrollAxis.Vertical, accessibilityLabel);
}

public abstract partial class Widget
{
    private readonly object indexedSourcesGate = new();
    private readonly Dictionary<string, IWidgetIndexedCollection> indexedSources = new(StringComparer.Ordinal);

    protected WidgetIndexedCollection<TQuery, TItem> CreateIndexedCollection<TQuery, TItem>(string sourceId, TQuery initialQuery,
        int count, WidgetIndexedCollectionOptions<TQuery, TItem> options) where TQuery : notnull where TItem : notnull
    {
        var source = new WidgetIndexedCollection<TQuery, TItem>(sourceId, initialQuery, count, options,
            () => { RetireIndexedLeases(); Invalidate(); }, WidgetLifetimeToken);
        lock (indexedSourcesGate)
            if (!indexedSources.TryAdd(sourceId, source)) throw new ArgumentException("Indexed source IDs must be unique within a widget.", nameof(sourceId));
        return source;
    }

    internal async ValueTask<IndexedCollectionRange> ReadIndexedRangeAsync(IndexedCollectionRangeRequest request, CancellationToken cancellationToken)
    {
        var parent = Volatile.Read(ref _latestSnapshot) ?? throw new InvalidOperationException("No parent presentation has been published.");
        var scope = IndexedCollectionContract.ResolveScope(parent, request);
        IWidgetIndexedCollection source;
        lock (indexedSourcesGate)
            source = indexedSources.GetValueOrDefault(request.Source.SourceId) ?? throw new InvalidOperationException("Indexed source is not registered.");
        var read = await source.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        var items = Array.AsReadOnly(read.Items.Select(item => item.Declaration).ToArray());
        var result = new IndexedCollectionRange(parent.WidgetInstanceId, request.CollectionId, request.Source,
            scope, request.StartIndex, request.DemandId, items, request.PinnedLayoutId);
        parent = Volatile.Read(ref _latestSnapshot) ?? throw new InvalidOperationException("Parent presentation retired during range loading.");
        IndexedCollectionContract.ValidateRange(parent, request, result);
        cancellationToken.ThrowIfCancellationRequested();
        WidgetLifetimeToken.ThrowIfCancellationRequested();
        return result;
    }
}
