using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Declarations over a complete loaded list. Context and item values must be immutable.</summary>
public sealed record WidgetLoadedCollectionOptions<TContext, TItem> where TContext : notnull where TItem : notnull
{
    /// <summary>Pure stable identity for an immutable item. Duplicate domain items require distinct occurrence keys.</summary>
    public required Func<TItem, WidgetCollectionItemKey> ItemKey { get; init; }
    public required Func<TContext, TItem, WidgetIndexedItemContext, WidgetElement> RenderItem { get; init; }
    public required Func<TContext, TItem, WidgetActionEvent, CancellationToken, ValueTask> OnAction { get; init; }
    public Func<TContext, TItem, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>>? ResolveArtwork { get; init; }
    public Func<TContext, TItem, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetArtworkSource?>>? ResolveArtworkSource { get; init; }
    /// <summary>Opt in to bounded host focus/viewport continuity across membership changes within one semantic identity.</summary>
    public bool PreserveOccurrences { get; init; }
    /// <summary>Keep an already-at-end viewport at the end across live publications within the same semantic identity. Requires PreserveOccurrences; never authorizes eviction of unread items.</summary>
    public bool FollowEnd { get; init; }
    /// <summary>Optional provider chronology for bounded live windows. Verifies forward tail replacement even when all previous rows were evicted; never compares opaque IDs itself.</summary>
    public Comparison<TItem>? LiveOrder { get; init; }
    /// <summary>Receives coalesced host viewport state. This is advisory; automatic eviction requires recovery for a reader who departs during delivery.</summary>
    public Func<TContext, WidgetCollectionViewportState, CancellationToken, ValueTask>? OnViewportChanged { get; init; }
    /// <summary>Loads one adjacent page on a coalesced host edge demand. Use the existing cursor resource's focus-neutral Prefetch and explicit Retry.</summary>
    public Func<TContext, WidgetCollectionBoundaryDemand, CancellationToken, ValueTask>? OnBoundaryDemand { get; init; }
    /// <summary>Prepares domain data around a missing occurrence; commit only through the request's captured admission.</summary>
    public Func<TContext, WidgetViewportRecovery, CancellationToken, ValueTask<WidgetViewportRecoveryResult>>? OnRecoverViewport { get; init; }
}

public enum WidgetLoadedCollectionChange { Unchanged, Content, Query }

/// <summary>
/// Adapts a complete loaded list to the existing indexed source. Query identity
/// is explicit domain policy; equal keys never imply an equal account/build/filter.
/// Items are copied on publication, declarations are still built only on demand.
/// </summary>
public sealed partial class WidgetLoadedCollection<TContext, TItem> : IWidgetIndexedCollection
    where TContext : notnull where TItem : notnull
{
    private sealed record Snapshot(string Identity, TContext Context, TItem[] Values,
        IReadOnlyList<WidgetCollectionItemKey> Keys, bool Continuity)
    {
        internal IReadOnlyList<TItem> Items { get; } = Array.AsReadOnly(Values);
    }
    private readonly object gate = new();
    private readonly WidgetIndexedCollection<Snapshot, TItem> source;
    private readonly WidgetLoadedCollectionOptions<TContext, TItem> options;
    private readonly CancellationToken lifetime;
    private Snapshot current;

    internal WidgetLoadedCollection(string sourceId, string queryIdentity, TContext context, IReadOnlyList<TItem> values,
        WidgetLoadedCollectionOptions<TContext, TItem> options, Action changed, CancellationToken lifetime,
        Func<CancellationToken>? activity = null, Func<ViewSnapshot?>? presentation = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ItemKey);
        ArgumentNullException.ThrowIfNull(options.RenderItem);
        ArgumentNullException.ThrowIfNull(options.OnAction);
        if (options.FollowEnd && !options.PreserveOccurrences) throw new ArgumentException("End following requires occurrence continuity.", nameof(options));
        if (!options.FollowEnd && (options.OnViewportChanged is not null || options.OnRecoverViewport is not null || options.OnBoundaryDemand is not null))
            throw new ArgumentException("Viewport feedback requires end following.", nameof(options));
        viewportActivity = activity ?? (() => lifetime); viewportPresentation = presentation ?? (() => null);
        viewportInvalidated = changed;
        this.options = options;
        this.lifetime = lifetime;
        current = Freeze(queryIdentity, context, values);
        source = new(sourceId, current, current.Items.Count, new()
        {
            ReadRange = (snapshot, start, count, token) =>
            {
                token.ThrowIfCancellationRequested();
                var range = new TItem[count];
                for (var i = 0; i < count; ++i) range[i] = snapshot.Values[start + i];
                return ValueTask.FromResult<IReadOnlyList<TItem>>(range);
            },
            ItemKey = options.ItemKey,
            FollowEnd = options.FollowEnd,
            SupportsViewportRecovery = options.OnRecoverViewport is not null,
            ObservesBoundaries = options.OnBoundaryDemand is not null,
            RetainsAppendedPrefix = options.FollowEnd,
            OccurrenceKeys = snapshot => snapshot.Continuity && snapshot.Keys.Count <= IndexedCollectionLimits.MaximumContinuityItems ? snapshot.Keys : null,
            RenderItem = (snapshot, item, ids) => options.RenderItem(snapshot.Context, item, ids),
            OnAction = (snapshot, item, action, token) => options.OnAction(snapshot.Context, item, action, token),
            ResolveArtwork = options.ResolveArtwork is { } artwork ? (snapshot, item, handle, token) => artwork(snapshot.Context, item, handle, token) : null,
            ResolveArtworkSource = options.ResolveArtworkSource is { } artworkSource ? (snapshot, item, handle, token) => artworkSource(snapshot.Context, item, handle, token) : null,
        }, changed, lifetime);
    }

    public IndexedCollectionDescriptor Descriptor => source.Descriptor;
    public IReadOnlyList<TItem> Items { get { lock (gate) return current.Items; } }
    public TContext Context { get { lock (gate) return current.Context; } }

    /// <summary>
    /// Classifies a new immutable list. Equal identity/context/items is a no-op;
    /// identical ordered occurrence keys update content; other changes retire the
    /// query. forceReset explicitly retires even otherwise identical input.
    /// </summary>
    public WidgetLoadedCollectionChange Publish(string queryIdentity, TContext context, IReadOnlyList<TItem> values, bool forceReset = false)
    {
        ValidateInput(queryIdentity, context, values);
        lock (gate)
        {
            lifetime.ThrowIfCancellationRequested();
            var equalItems = ReferenceEquals(current.Items, values) || current.Values.SequenceEqual(values);
            var sameIdentity = current.Identity == queryIdentity;
            if (!forceReset && sameIdentity && equalItems && EqualityComparer<TContext>.Default.Equals(current.Context, context))
                return WidgetLoadedCollectionChange.Unchanged;
            var next = equalItems ? current with { Identity = queryIdentity, Context = context } : Freeze(queryIdentity, context, values, current);
            var sameKeys = sameIdentity && !forceReset && (ReferenceEquals(current.Keys, next.Keys) || current.Keys.SequenceEqual(next.Keys));
            var continuity = sameKeys ? current.Continuity : options.PreserveOccurrences && sameIdentity && !forceReset;
            var appended = continuity && options.FollowEnd && (next.Keys.Count > current.Keys.Count && current.Keys.SequenceEqual(next.Keys.Take(current.Keys.Count)) ||
                current.Items.Count > 0 && next.Items.Count > 0 && options.LiveOrder is { } compare &&
                compare(current.Items[^1], next.Items[^1]) < 0 && compare(current.Items[0], next.Items[0]) <= 0 &&
                Ordered(current.Items, compare) && Ordered(next.Items, compare));
            var unchangedPrefix = continuity && options.FollowEnd && source.Descriptor.RetainsAppendedPrefix &&
                next.Values.Length > current.Values.Length && next.Values.Length <= IndexedCollectionLimits.MaximumContinuityItems &&
                EqualityComparer<TContext>.Default.Equals(current.Context, context) &&
                current.Keys.SequenceEqual(next.Keys.Take(current.Keys.Count)) &&
                current.Values.SequenceEqual(next.Values.Take(current.Values.Length));
            if (next.Continuity != continuity) next = next with { Continuity = continuity };
            if (!sameIdentity || forceReset) ResetViewportLocked();
            current = next;
            if (sameKeys) source.UpdateContent(next);
            else if (unchangedPrefix) source.AppendUnchangedPrefix(next, next.Items.Count);
            else source.PublishQuery(next, next.Items.Count, null, continuity, appended);
            return sameKeys ? WidgetLoadedCollectionChange.Content : WidgetLoadedCollectionChange.Query;
        }
    }

    private Snapshot Freeze(string identity, TContext context, IReadOnlyList<TItem> values, Snapshot? previous = null)
    {
        ValidateInput(identity, context, values);
        var frozen = values.ToArray();
        WidgetCollectionItemKey[]? keys = previous?.Keys.Count == frozen.Length ? null : new WidgetCollectionItemKey[frozen.Length];
        for (var i = 0; i < frozen.Length; ++i)
        {
            ArgumentNullException.ThrowIfNull(frozen[i]);
            var key = previous?.Values.Length == frozen.Length && EqualityComparer<TItem>.Default.Equals(previous.Values[i], frozen[i])
                ? previous.Keys[i] : options.ItemKey(frozen[i]);
            if (keys is null && key == previous!.Keys[i]) continue;
            StableIdentifier.Validate(key.Value, nameof(values));
            if (keys is null)
            {
                keys = new WidgetCollectionItemKey[frozen.Length];
                for (var old = 0; old < i; ++old) keys[old] = previous!.Keys[old];
            }
            keys[i] = key;
        }
        if (keys is not null && new HashSet<WidgetCollectionItemKey>(keys).Count != keys.Length)
            throw new ArgumentException("Loaded items require unique occurrence keys, including duplicates of one domain item.", nameof(values));
        return new(identity, context, frozen, keys is null ? previous!.Keys : Array.AsReadOnly(keys), options.PreserveOccurrences);
    }

    private static bool Ordered(IReadOnlyList<TItem> items, Comparison<TItem> compare)
    {
        for (var index = 1; index < items.Count; index++) if (compare(items[index - 1], items[index]) >= 0) return false;
        return true;
    }

    private void ValidateInput(string identity, TContext context, IReadOnlyList<TItem> values)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(values);
        if(options.OnBoundaryDemand is not null && values.Count>IndexedCollectionLimits.MaximumContinuityItems)
            throw new ArgumentException("Boundary paging requires a bounded resident window.",nameof(values));
    }

    public IndexedCollectionFocusTarget FocusTarget(string collectionId, WidgetCollectionItemKey key, int index) => source.FocusTarget(collectionId, key, index);
    public FocusGroupEntryRequest Enter(string collectionId, long requestId, IndexedCollectionFocusTarget? item = null) => source.Enter(collectionId, requestId, item);
    public ScrollRevealRequest Reveal(string collectionId, long requestId, IndexedCollectionFocusTarget item) => source.Reveal(collectionId, requestId, item);
    internal (IndexedCollectionDescriptor Source, IReadOnlyList<string>? Keys) CaptureDeclaration() => source.CaptureDeclaration();
    ValueTask<WidgetIndexedRead> IWidgetIndexedCollection.ReadAsync(IndexedCollectionRangeRequest request, CancellationToken cancellationToken) =>
        ((IWidgetIndexedCollection)source).ReadAsync(request, cancellationToken);
}

public abstract partial class Widget
{
    protected WidgetLoadedCollection<TContext, TItem> CreateLoadedCollection<TContext, TItem>(string sourceId,
        string queryIdentity, TContext context, IReadOnlyList<TItem> items, WidgetLoadedCollectionOptions<TContext, TItem> options)
        where TContext : notnull where TItem : notnull
    {
        var source = new WidgetLoadedCollection<TContext, TItem>(sourceId, queryIdentity, context, items, options,
            () => { RetireIndexedLeases(); Invalidate(); }, WidgetLifetimeToken, () => ActiveCancellationToken, () => Volatile.Read(ref _latestSnapshot));
        lock (indexedSourcesGate)
            if (!indexedSources.TryAdd(sourceId, source)) throw new ArgumentException("Indexed source IDs must be unique within a widget.", nameof(sourceId));
        return source;
    }
}

public static partial class UI
{
    public static IndexedCollectionElement CollectionList<TContext, TItem>(string id, WidgetLoadedCollection<TContext, TItem> source,
        double estimatedItemExtent, string accessibilityLabel, ScrollAxis axis = ScrollAxis.Vertical)
        where TContext : notnull where TItem : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        var captured = source.CaptureDeclaration();
        return new(id, captured.Source, new() { Kind = CollectionLayoutKind.List, EstimatedItemExtent = estimatedItemExtent }, axis, accessibilityLabel, captured.Keys);
    }

    public static IndexedCollectionElement CollectionGrid<TContext, TItem>(string id, WidgetLoadedCollection<TContext, TItem> source,
        double minimumColumnWidth, double estimatedItemExtent, string accessibilityLabel, int? maximumColumns = null)
        where TContext : notnull where TItem : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        var captured = source.CaptureDeclaration();
        return new(id, captured.Source, new() { Kind = CollectionLayoutKind.AdaptiveGrid, EstimatedItemExtent = estimatedItemExtent,
            MinimumColumnWidth = minimumColumnWidth, MaximumColumns = maximumColumns }, ScrollAxis.Vertical, accessibilityLabel, captured.Keys);
    }
}
