using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>One bounded append; continuation is worker-private and null means exhausted.</summary>
public sealed record WidgetDiscoveredPage<TItem>(IReadOnlyList<TItem> Items, string? Continuation) where TItem : notnull;
public enum WidgetDiscoveredDuplicatePolicy { Reject, KeepFirst }

public sealed record WidgetDiscoveredCollectionOptions<TQuery, TItem> where TQuery : notnull where TItem : notnull
{
    public int PageSize { get; init; } = 24;
    /// <summary>Hard retained metadata bound. Reaching it stops discovery visibly; existing history is never evicted.</summary>
    public int MaximumItems { get; init; } = 1024;
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>KeepFirst is an explicit unique-result policy; repeated occurrences otherwise require distinct keys.</summary>
    public WidgetDiscoveredDuplicatePolicy DuplicatePolicy { get; init; }
    public required Func<TQuery, string?, int, CancellationToken, ValueTask<WidgetDiscoveredPage<TItem>>> LoadNext { get; init; }
    public required Func<TItem, WidgetCollectionItemKey> ItemKey { get; init; }
    public required Func<TQuery, TItem, WidgetIndexedItemContext, WidgetElement> RenderItem { get; init; }
    public required Func<TQuery, TItem, WidgetActionEvent, CancellationToken, ValueTask> OnAction { get; init; }
    public Func<TQuery, TItem, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>>? ResolveArtwork { get; init; }
    public Func<Exception, WidgetResourceError>? MapError { get; init; }
}

/// <summary>
/// A forward-discovered prefix over opaque provider continuations. Count is never a remote
/// total. Each successful append preserves old row/action leases; ReplaceQuery retires them.
/// Captured queries/items must be immutable. Metadata is retained up to MaximumItems;
/// native realized rows and artwork retain the existing independent cache/lease budgets.
/// </summary>
public sealed class WidgetDiscoveredCollection<TQuery, TItem> : IWidgetIndexedCollection where TQuery : notnull where TItem : notnull
{
    private sealed class Epoch(TQuery value)
    {
        internal TQuery Value { get; } = value;
        internal List<TItem> Items { get; } = [];
        internal HashSet<string> Keys { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> Tokens { get; } = new(StringComparer.Ordinal);
        internal string? Continuation;
        internal CancellationTokenSource Lifetime { get; } = new();
        internal bool ProviderActive;
    }
    private readonly object gate = new();
    private readonly WidgetDiscoveredCollectionOptions<TQuery, TItem> options;
    private readonly WidgetIndexedCollection<Epoch, TItem> indexed;
    private readonly CancellationToken widgetLifetime;
    private Epoch epoch;
    private Task? loading;
    private Epoch? loadingEpoch;
    private int activeProvider;
    private long revision;

    internal WidgetDiscoveredCollection(string id, TQuery query, WidgetDiscoveredCollectionOptions<TQuery, TItem> options,
        Action changed, CancellationToken widgetLifetime)
    {
        ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.LoadNext); ArgumentNullException.ThrowIfNull(options.ItemKey);
        ArgumentNullException.ThrowIfNull(options.RenderItem); ArgumentNullException.ThrowIfNull(options.OnAction);
        if (options.PageSize is < 1 or > IndexedCollectionLimits.MaximumRangeItems || options.MaximumItems is < 1 or > 4096 ||
            options.PageSize > options.MaximumItems || !Enum.IsDefined(options.DuplicatePolicy) || options.ReadTimeout < TimeSpan.FromMilliseconds(100) || options.ReadTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(options));
        this.options = options; this.widgetLifetime = widgetLifetime; epoch = new(query);
        indexed = new(id, epoch, 0, new()
        {
            ReadRange = (captured, start, count, token) =>
            {
                token.ThrowIfCancellationRequested();
                lock (gate) return ValueTask.FromResult<IReadOnlyList<TItem>>(captured.Items.GetRange(start, count).ToArray());
            },
            ItemKey = options.ItemKey,
            RenderItem = (captured, item, context) => options.RenderItem(captured.Value, item, context),
            OnAction = (captured, item, action, token) => options.OnAction(captured.Value, item, action, token),
            ResolveArtwork = options.ResolveArtwork is { } resolver ? (captured, item, handle, token) => resolver(captured.Value, item, handle, token) : null,
        }, changed, widgetLifetime);
        indexed.AppendPrefix(0, new(++revision, true, DiscoveredCollectionStatus.Ready, options.MaximumItems));
    }

    public IndexedCollectionDescriptor Descriptor => indexed.Descriptor;
    public void ReplaceQuery(TQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Epoch previous;
        lock (gate)
        {
            widgetLifetime.ThrowIfCancellationRequested();
            previous = epoch; epoch = new(query);
            indexed.PublishQuery(epoch, 0, new(++revision, true, DiscoveredCollectionStatus.Ready, options.MaximumItems));
        }
        previous.Lifetime.Cancel(); previous.Lifetime.Dispose();
    }
    public IndexedCollectionFocusTarget FocusTarget(string collectionId, WidgetCollectionItemKey key, int index) => indexed.FocusTarget(collectionId, key, index);
    public FocusGroupEntryRequest Enter(string collectionId, long requestId, IndexedCollectionFocusTarget? item = null) => indexed.Enter(collectionId, requestId, item);

    ValueTask<WidgetIndexedRead> IWidgetIndexedCollection.ReadAsync(IndexedCollectionRangeRequest request, CancellationToken token) =>
        ((IWidgetIndexedCollection)indexed).ReadAsync(request, token);

    async ValueTask IWidgetIndexedCollection.ContinueAsync(IndexedCollectionRangeRequest request, CancellationToken cancellationToken)
    {
        Task operation;
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested(); widgetLifetime.ThrowIfCancellationRequested();
            var source = Descriptor;
            if (request.Source != source) return; // stale boundary demand cannot consume another token
            var discovery = source.Discovery!;
            if (!discovery.HasMore || discovery.Status == DiscoveredCollectionStatus.LimitReached ||
                discovery.Status == DiscoveredCollectionStatus.Failed && request.Kind != IndexedCollectionRequestKind.Retry) return;
            if (epoch.ProviderActive && loading is not { IsCompleted: false } || activeProvider >= 4)
            {
                Publish(epoch, true, DiscoveredCollectionStatus.Failed, new("discovery_busy", "The previous load is still stopping. Retry shortly."));
                return;
            }
            if (ReferenceEquals(loadingEpoch, epoch) && loading is { IsCompleted: false }) { operation = loading; }
            else
            {
                var captured = epoch;
                var remaining = options.MaximumItems - captured.Items.Count;
                if (remaining == 0 || captured.Tokens.Count >= 4095)
                { Publish(captured, true, DiscoveredCollectionStatus.LimitReached); return; }
                var count = Math.Min(options.PageSize, remaining);
                var token = captured.Continuation;
                var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, widgetLifetime, captured.Lifetime.Token);
                lifetime.CancelAfter(options.ReadTimeout);
                Publish(captured, true, DiscoveredCollectionStatus.Loading);
                // The provider's synchronous prefix cannot block worker input/render.
                ++activeProvider;
                captured.ProviderActive = true;
                loadingEpoch = captured;
                loading = operation = Task.Run(() => LoadAsync(captured, token, count, lifetime, cancellationToken), CancellationToken.None);
            }
        }
        await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadAsync(Epoch captured, string? continuation, int count, CancellationTokenSource lifetime, CancellationToken caller)
    {
        var token = lifetime.Token;
        var provider = Task.Run(async () => await options.LoadNext(captured.Value, continuation, count, token).ConfigureAwait(false), CancellationToken.None);
        _ = provider.ContinueWith(task =>
        {
            _ = task.Exception;
            lock (gate) { --activeProvider; captured.ProviderActive = false; }
            lifetime.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        {
            try
            {
                var page = await provider.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (page?.Items is null || page.Items.Count > count || page.Continuation is { Length: > 1024 } ||
                    page.Continuation is { Length: 0 } || page.Continuation?.Any(char.IsControl) == true)
                    throw new InvalidOperationException("The provider returned an invalid discovery page.");
                var items = new List<TItem>();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in page.Items)
                {
                    ArgumentNullException.ThrowIfNull(item);
                    var key = options.ItemKey(item).Value; StableIdentifier.Validate(key, nameof(options.ItemKey));
                    if (!keys.Add(key))
                    {
                        if (options.DuplicatePolicy == WidgetDiscoveredDuplicatePolicy.KeepFirst) continue;
                        throw new InvalidOperationException("Discovered occurrences require unique keys.");
                    }
                    items.Add(item);
                }
                lock (gate)
                {
                    token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(epoch, captured)) return;
                    if (options.DuplicatePolicy == WidgetDiscoveredDuplicatePolicy.KeepFirst)
                    {
                        items.RemoveAll(item => captured.Keys.Contains(options.ItemKey(item).Value));
                        keys.ExceptWith(captured.Keys);
                    }
                    if (keys.Overlaps(captured.Keys) || page.Continuation is { } next &&
                        (next == continuation || captured.Tokens.Contains(next)))
                        throw new InvalidOperationException("The provider repeated a discovered occurrence or continuation.");
                    // Empty filtered pages still consume token history, bounded separately.
                    if (continuation is not null) captured.Tokens.Add(continuation);
                    captured.Items.AddRange(items); captured.Keys.UnionWith(keys); captured.Continuation = page.Continuation;
                    Publish(captured, page.Continuation is not null, (captured.Items.Count == options.MaximumItems || captured.Tokens.Count >= 4095) && page.Continuation is not null
                        ? DiscoveredCollectionStatus.LimitReached : DiscoveredCollectionStatus.Ready);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                lock (gate) if (ReferenceEquals(epoch, captured) && !widgetLifetime.IsCancellationRequested)
                    Publish(captured, true, caller.IsCancellationRequested ? DiscoveredCollectionStatus.Ready : DiscoveredCollectionStatus.Failed,
                        caller.IsCancellationRequested ? null : new("discovery_timeout", "Loading more results timed out. Retry to continue."));
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                lock (gate) if (ReferenceEquals(epoch, captured) && !widgetLifetime.IsCancellationRequested)
                {
                    WidgetResourceError failure;
                    try
                    {
                        failure = options.MapError?.Invoke(error) ?? new("discovery_failed", "More results could not be loaded. Retry to continue.");
                        StableIdentifier.Validate(failure.Code, nameof(options.MapError));
                        if (failure.Code.Length > 128 || string.IsNullOrWhiteSpace(failure.Message) ||
                            failure.Message.Length > 256 || failure.Message.Any(char.IsControl))
                            throw new ArgumentException("Mapped discovery errors must be bounded visible text.");
                    }
                    catch { failure = new("discovery_failed", "More results could not be loaded. Retry to continue."); }
                    Publish(captured, true, DiscoveredCollectionStatus.Failed, failure);
                }
            }
        }
    }

    private void Publish(Epoch captured, bool hasMore, DiscoveredCollectionStatus status, WidgetResourceError? error = null) =>
        indexed.AppendPrefix(captured.Items.Count, new(++revision, hasMore, status, options.MaximumItems, error?.Code, error?.Message));
}

public static partial class UI
{
    public static IndexedCollectionElement CollectionList<TQuery, TItem>(string id, WidgetDiscoveredCollection<TQuery, TItem> source,
        double estimatedItemExtent, string accessibilityLabel, ScrollAxis axis = ScrollAxis.Vertical) where TQuery : notnull where TItem : notnull =>
        new(id, (source ?? throw new ArgumentNullException(nameof(source))).Descriptor, new() { Kind = CollectionLayoutKind.List, EstimatedItemExtent = estimatedItemExtent }, axis, accessibilityLabel);
    public static IndexedCollectionElement CollectionGrid<TQuery, TItem>(string id, WidgetDiscoveredCollection<TQuery, TItem> source,
        double minimumColumnWidth, double estimatedItemExtent, string accessibilityLabel, int? maximumColumns = null) where TQuery : notnull where TItem : notnull =>
        new(id, (source ?? throw new ArgumentNullException(nameof(source))).Descriptor, new() { Kind = CollectionLayoutKind.AdaptiveGrid, EstimatedItemExtent = estimatedItemExtent,
            MinimumColumnWidth = minimumColumnWidth, MaximumColumns = maximumColumns }, ScrollAxis.Vertical, accessibilityLabel);
}

public abstract partial class Widget
{
    protected WidgetDiscoveredCollection<TQuery, TItem> CreateDiscoveredCollection<TQuery, TItem>(string sourceId, TQuery initialQuery,
        WidgetDiscoveredCollectionOptions<TQuery, TItem> options) where TQuery : notnull where TItem : notnull
    {
        var source = new WidgetDiscoveredCollection<TQuery, TItem>(sourceId, initialQuery, options,
            () => { RetireIndexedLeases(); Invalidate(); }, WidgetLifetimeToken);
        lock (indexedSourcesGate)
            if (!indexedSources.TryAdd(sourceId, source)) throw new ArgumentException("Indexed source IDs must be unique within a widget.", nameof(sourceId));
        return source;
    }
}
