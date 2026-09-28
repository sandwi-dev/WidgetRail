using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private readonly Dictionary<string, IndexedDemand> _indexedDemands = new(StringComparer.Ordinal);
    private readonly HashSet<string> _indexedHiddenWidgets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _indexedLifecycleVersions = new(StringComparer.Ordinal);
    private sealed class IndexedDemand(WidgetPresentationAuthority authority, BridgeIndexedRangeRequest request,
        CancellationTokenSource lifetime, string scopeId)
    {
        internal readonly WidgetPresentationAuthority Authority = authority;
        internal readonly BridgeIndexedRangeRequest Request = request;
        internal readonly CancellationTokenSource Lifetime = lifetime;
        internal readonly string ScopeId = scopeId;
        internal readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? Retirement;
    }

    /// <summary>
    /// Fetches declarative rows against an exact current parent. Rows do not acquire
    /// action or artwork authority. Demand IDs are host-owned. Cancellation targets
    /// the captured worker and projection, never a replacement. Callers must discard
    /// returned ranges if their owner, query or projection changes before the UI
    /// dispatcher applies them. Unrelated parent revisions preserve query data.
    /// </summary>
    public async Task<IndexedCollectionRange> ReadIndexedRangeAsync(
        WidgetPresentationAuthority authority, string collectionId, IndexedCollectionDescriptor source,
        int startIndex, int count, string? pinnedLayoutId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var range = new IndexedCollectionRangeRequest(collectionId, source, startIndex, count,
            Guid.NewGuid().ToString("N"), pinnedLayoutId);
        IndexedCollectionContract.ValidateRequest(range);
        IndexedDemand operation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _ = ValidateAuthority(authority);
            if (_indexedHiddenWidgets.Contains(authority.WidgetId))
                throw new WidgetPresentationSessionException("indexed_surface_hidden", "The indexed surface is not active.");
            var scopeId = IndexedCollectionContract.ResolveScope(_states[authority.WidgetId].LastGood!.Snapshot, range);
            // Reserve space for each range's cancellation and at least one ordinary
            // action. Slow range providers must not occupy every transport slot.
            var capacity = Math.Min(_options.MaximumPendingIndexedRanges, (_options.MaximumPendingRequests - 1) / 2);
            if (_indexedDemands.Count >= capacity)
                throw new WidgetPresentationSessionException("indexed_saturated", "The indexed range request limit has been reached.");
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            lifetime.CancelAfter(_options.IndexedRangeTimeout);
            operation = new(authority, new(authority.WidgetId, authority.WidgetInstanceId,
                authority.RuntimeGeneration, authority.PresentationGeneration, range), lifetime, scopeId);
            _indexedDemands.Add(range.DemandId, operation);
        }
        Task<BridgeEnvelope>? reply = null;
        try
        {
            operation.Lifetime.Token.ThrowIfCancellationRequested();
            // Keep original correlation alive until either result or cancellation
            // completes. Cancelling the local await must not drop a late wire reply.
            reply = _transport.RequestWithWriteCompletionAsync(BridgeMessageTypes.ReadIndexedRange, operation.Request, operation.Written);
            ObserveIndexedReply(operation.Written.Task);
            ObserveIndexedReply(reply);
            var envelope = await reply.WaitAsync(operation.Lifetime.Token).ConfigureAwait(false);
            operation.Lifetime.Token.ThrowIfCancellationRequested();
            if (envelope.Type == BridgeMessageTypes.Error)
            {
                var failure = ReadRequestFailure(envelope.Payload);
                throw new WidgetPresentationSessionException(failure.Code, failure.Message);
            }
            if (envelope.Type != BridgeMessageTypes.IndexedRange)
                throw new BridgeProtocolException("WidgetBridge returned an unexpected indexed range response.");
            var response = BridgeJson.FromElement<BridgeIndexedRangeResponse>(envelope.Payload);
            if (response.WidgetId != authority.WidgetId || response.InstanceId != authority.WidgetInstanceId ||
                response.RuntimeGeneration != authority.RuntimeGeneration || response.PresentationGeneration != authority.PresentationGeneration)
                throw new BridgeProtocolException("WidgetBridge returned foreign indexed range authority.");
            lock (_gate)
            {
                operation.Lifetime.Token.ThrowIfCancellationRequested();
                if (!IsIndexedDemandCurrentLocked(operation))
                    throw new WidgetPresentationSessionException("indexed_retired", "The indexed parent or query retired.");
                IndexedCollectionContract.ValidateRange(_states[authority.WidgetId].LastGood!.Snapshot, range, response.Range);
                return response.Range;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            lock (_gate)
                if (operation.Retirement is not null)
                    throw new WidgetPresentationSessionException("indexed_retired", "The indexed parent or surface retired.");
            throw new WidgetPresentationSessionException("indexed_timeout", "The indexed range demand timed out.");
        }
        finally
        {
            try
            {
                if (reply is { IsCompleted: false })
                    await CancelIndexedDemandAsync(operation, reply).ConfigureAwait(false);
            }
            finally
            {
                Task? retirement;
                lock (_gate) { _indexedDemands.Remove(range.DemandId); retirement = operation.Retirement; }
                try { if (retirement is not null) await retirement.ConfigureAwait(false); }
                finally { operation.Lifetime.Dispose(); operation.Done.TrySetResult(); }
            }
        }
    }

    /// <summary>
    /// Retires reads admitted from one exact parent frame/projection. It cannot
    /// cancel replacement-frame work. A native surface should also pass its own
    /// lifetime token to every read and cancel that token when disposing.
    /// </summary>
    public void CancelIndexedRanges(WidgetPresentationAuthority authority, string? pinnedLayoutId = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        lock (_gate)
            foreach (var demand in _indexedDemands.Values)
                if (demand.Authority == authority && demand.Request.Range.PinnedLayoutId == pinnedLayoutId)
                    demand.Retirement ??= demand.Lifetime.CancelAsync();
    }

    private void RetireIndexedRangesLocked(string? widgetId = null, WidgetPresentationAuthority? retained = null)
    {
        foreach (var demand in _indexedDemands.Values)
            if ((widgetId is null || demand.Authority.WidgetId == widgetId) &&
                (retained is null || !IsIndexedDemandCurrentLocked(demand)))
                demand.Retirement ??= demand.Lifetime.CancelAsync();
    }

    private static bool SameIndexedOwner(WidgetPresentationAuthority first, WidgetPresentationAuthority second) =>
        first.WidgetId == second.WidgetId && first.WidgetInstanceId == second.WidgetInstanceId &&
        first.RuntimeGeneration == second.RuntimeGeneration && first.PresentationGeneration == second.PresentationGeneration &&
        first.SessionGeneration == second.SessionGeneration;

    // A data demand belongs to its query and projection, not to every unrelated
    // parent snapshot revision or the currently interactive modal scope.
    private bool IsIndexedDemandCurrentLocked(IndexedDemand demand)
    {
        if (_disposed || _terminalFailure is not null || _indexedHiddenWidgets.Contains(demand.Authority.WidgetId) ||
            !_descriptors.TryGetValue(demand.Authority.WidgetId, out var descriptor) ||
            descriptor.InstanceId != demand.Authority.WidgetInstanceId || descriptor.RuntimeGeneration != demand.Authority.RuntimeGeneration ||
            descriptor.PresentationGeneration != demand.Authority.PresentationGeneration ||
            _sessionGenerations.GetValueOrDefault(demand.Authority.WidgetId) != demand.Authority.SessionGeneration ||
            !_states.TryGetValue(demand.Authority.WidgetId, out var state) || state.LastGood is not { } parent ||
            !SameIndexedOwner(demand.Authority, parent.Authority)) return false;
        try { return IndexedCollectionContract.ResolveScope(parent.Snapshot, demand.Request.Range) == demand.ScopeId; }
        catch (Exception exception) when (exception is ArgumentException or ProtocolValidationException) { return false; }
    }

    private async Task CancelIndexedDemandAsync(IndexedDemand operation, Task<BridgeEnvelope> original)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            // Do not race a cancellation frame ahead of a read still waiting for
            // write/capacity admission. The bridge can cancel only admitted work.
            await operation.Written.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            var cancellation = _transport.RequestAsync(BridgeMessageTypes.CancelIndexedRange, operation.Request, CancellationToken.None);
            ObserveIndexedReply(cancellation);
            var response = await cancellation.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (response.Type != BridgeMessageTypes.Acknowledged)
                throw new BridgeProtocolException("WidgetBridge did not acknowledge indexed cancellation.");
            RequireObjectProperties(response.Payload, "cancelled");
            if (response.Payload.GetProperty("cancelled").ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                throw new BridgeProtocolException("WidgetBridge returned invalid indexed cancellation status.");
            // Cancellation is terminal only when the old correlated read also ends.
            _ = await original.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Without an acknowledged terminal boundary we cannot permit unlimited
            // orphan ranges to consume ordinary-action capacity in a live connection.
            FailTerminal(new WidgetPresentationSessionException("indexed_cancel_failed", "Indexed cancellation could not be confirmed."));
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private long BeginIndexedLifecycle(WidgetPresentationTarget target, WidgetRail.WidgetSdk.WidgetLifecycleState state)
    {
        lock (_gate)
        {
            _ = ValidateTarget(target);
            var version = _indexedLifecycleVersions.GetValueOrDefault(target.Descriptor.Id) + 1;
            _indexedLifecycleVersions[target.Descriptor.Id] = version;
            if (state == WidgetRail.WidgetSdk.WidgetLifecycleState.Background)
            {
                _indexedHiddenWidgets.Add(target.Descriptor.Id);
                RetireIndexedRangesLocked(target.Descriptor.Id);
            }
            return version;
        }
    }

    private void CompleteIndexedLifecycle(WidgetPresentationTarget target, long generation, long version,
        WidgetRail.WidgetSdk.WidgetLifecycleState state)
    {
        lock (_gate)
            if (state != WidgetRail.WidgetSdk.WidgetLifecycleState.Background &&
                _sessionGenerations.GetValueOrDefault(target.Descriptor.Id) == generation &&
                _indexedLifecycleVersions.GetValueOrDefault(target.Descriptor.Id) == version)
                _indexedHiddenWidgets.Remove(target.Descriptor.Id);
    }

    private static void ObserveIndexedReply(Task task) =>
        _ = task.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
