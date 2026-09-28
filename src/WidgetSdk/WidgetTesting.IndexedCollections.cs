using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// Exercises indexed reads, captured actions and artwork through the real SDK
/// ingress. Owns its range leases, but never initializes or destroys the widget.
/// Do not combine this with another snapshot publisher for the same widget.
/// </summary>
public sealed class WidgetIndexedCollectionTestHost : IDisposable
{
    private readonly object gate = new();
    private readonly Widget widget;
    private readonly string widgetInstanceId;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<WidgetIndexedCollectionTestLease> leases = [];
    private ViewSnapshot snapshot;
    private bool disposed;

    internal WidgetIndexedCollectionTestHost(Widget widget, string widgetInstanceId, long initialSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetInstanceId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialSequence);
        this.widget = widget;
        this.widgetInstanceId = widgetInstanceId;
        snapshot = widget.RenderSnapshot(widgetInstanceId, initialSequence);
    }

    /// <summary>The latest exact parent snapshot; publishing is always explicit.</summary>
    public ViewSnapshot CurrentSnapshot { get { lock (gate) return snapshot; } }

    /// <summary>Publishes the next snapshot without altering widget lifecycle or awaiting queued actions.</summary>
    public ViewSnapshot PublishSnapshot()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return snapshot = widget.RenderSnapshot(widgetInstanceId, checked(snapshot.Sequence + 1));
        }
    }

    /// <summary>
    /// Acquires a bounded range from the current declared main or pinned collection.
    /// Reads obey the production provider, cancellation, timeout and retention limits.
    /// </summary>
    public async ValueTask<WidgetIndexedCollectionTestLease> AcquireAsync(
        string collectionId, int startIndex, int count, string? pinnedLayoutId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        IndexedCollectionRangeRequest request;
        CancellationTokenSource cancellation;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var root = pinnedLayoutId is null ? snapshot.Root : snapshot.PinnedLayouts
                .SingleOrDefault(layout => layout.Id == pinnedLayoutId)?.Root;
            var node = root is null ? null : Find(root, collectionId);
            if (node?.IndexedCollection is not { } source)
                throw new ArgumentException("The current projection does not declare this indexed collection.", nameof(collectionId));
            request = new(collectionId, source, startIndex, count, Guid.NewGuid().ToString("N"), pinnedLayoutId);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        }
        using (cancellation)
        {
            var acquired = await widget.AcquireIndexedRangeAsync(request, cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (disposed || cancellation.IsCancellationRequested)
                {
                    widget.ReleaseIndexedRange(acquired.LeaseId);
                    cancellation.Token.ThrowIfCancellationRequested();
                    throw new ObjectDisposedException(nameof(WidgetIndexedCollectionTestHost));
                }
                var lease = new WidgetIndexedCollectionTestLease(this, acquired);
                leases.Add(lease);
                return lease;
            }
        }
    }

    internal WidgetOperationAdmission? Route(WidgetIndexedCollectionTestLease lease, IndexedCollectionInputRequest input,
        IndexedCollectionInputContext? context)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return widget.AdmitIndexedInput(input, context ?? new(lease.Range.ScopeId, snapshot.Sequence));
        }
    }

    internal ValueTask<WidgetEncodedArtwork?> Artwork(IndexedCollectionItemReference item, string handle,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return widget.ResolveIndexedArtworkAsync(item, handle, cancellationToken);
        }
    }

    internal void Release(WidgetIndexedCollectionTestLease lease)
    {
        lock (gate)
            if (leases.Remove(lease)) widget.ReleaseIndexedRange(lease.LeaseId);
    }

    /// <summary>
    /// Releases owned data leases and cancels outstanding acquisitions. Callers must
    /// still observe their pending tasks. Already-admitted actions retain normal widget lifetime.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var lease in leases) widget.ReleaseIndexedRange(lease.LeaseId);
            leases.Clear();
        }
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private static ViewNode? Find(ViewNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }
}

/// <summary>A captured semantic range used by author tests, independent of visual realization.</summary>
public sealed class WidgetIndexedCollectionTestLease : IDisposable
{
    private readonly WidgetIndexedCollectionTestHost host;
    internal WidgetIndexedCollectionTestLease(WidgetIndexedCollectionTestHost host, IndexedCollectionLease lease)
    { this.host = host; LeaseId = lease.LeaseId; Range = lease.Range; }

    public string LeaseId { get; }
    public IndexedCollectionRange Range { get; }

    /// <summary>
    /// Routes through current ancestry and the normal serial action queue. Null means
    /// stale or unavailable authority; a non-null result is admission, not completion.
    /// Supply an explicit context to test stale snapshot or incorrect input-scope rejection.
    /// Released or query-retired leases are rejected by the production ingress.
    /// </summary>
    public WidgetOperationAdmission? RouteAction(string itemKey, ControllerButton button,
        ControllerEventPhase phase = ControllerEventPhase.Pressed,
        string? contextActionOwnerId = null, string? contextActionId = null,
        IndexedCollectionInputContext? context = null) =>
        host.Route(this, new(new(LeaseId, itemKey), button, phase, contextActionOwnerId, contextActionId), context);

    /// <summary>Resolves declared opaque artwork with production identity and encoded-image validation.</summary>
    public ValueTask<WidgetEncodedArtwork?> ResolveArtworkAsync(string itemKey, WidgetArtworkHandle handle,
        CancellationToken cancellationToken = default) =>
        host.Artwork(new(LeaseId, itemKey), handle.Value, cancellationToken);

    /// <summary>Releases retained data. Already-admitted actions keep their captured query/item.</summary>
    public void Dispose() => host.Release(this);
}
