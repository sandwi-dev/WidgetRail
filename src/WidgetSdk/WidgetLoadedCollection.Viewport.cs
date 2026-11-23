using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Conservative state over the source's active host viewports. It is feedback, not atomic eviction authority.</summary>
public sealed record WidgetCollectionViewportState(int ActiveViews, bool FollowsEnd, bool Recovering)
{
    /// <summary>All active native viewports are physically at their loaded end, independently of inspected-row protection.</summary>
    public bool IsAtEnd { get; init; }
    public bool IsAtStart { get; init; }
    /// <summary>The last occurrence in the exact collection revision observed by
    /// every active viewport at its physical end. Null while refreshing/recovering,
    /// when any viewport is reading earlier items, or when the collection is empty.
    /// Unlike inspecting current data after an asynchronous callback, this cannot
    /// include a newly published tail the host has not yet observed.</summary>
    public WidgetCollectionItemKey? ObservedEndItemKey { get; init; }
}
public enum WidgetViewportRecoveryResult { Recovered, NotFound }
public sealed class WidgetCollectionBoundaryDemand
{
    internal WidgetCollectionBoundaryDemand(WidgetCursorDirection direction,IReadOnlyList<string>? keys)
    {Direction=direction;ProtectedKeys=Array.AsReadOnly((keys??[]).Select(key=>new WidgetCollectionItemKey(key)).ToArray());}
    public WidgetCursorDirection Direction { get; }
    public IReadOnlyList<WidgetCollectionItemKey> ProtectedKeys { get; }
}

/// <summary>One host-owned missing-anchor recovery. External reads happen before the bounded synchronous commit.</summary>
public sealed class WidgetViewportRecovery
{
    private readonly Func<Func<bool>, bool> commit;
    internal WidgetViewportRecovery(string key, IReadOnlyList<string>? neighbors, Func<Func<bool>, bool> commit)
    { AnchorKey = new(key); NeighborKeys = Array.AsReadOnly((neighbors ?? []).Select(value => new WidgetCollectionItemKey(value)).ToArray()); this.commit = commit; }
    public WidgetCollectionItemKey AnchorKey { get; }
    public IReadOnlyList<WidgetCollectionItemKey> NeighborKeys { get; }
    internal WidgetCollectionItemKey? ResolvedAnchor { get; private set; }
    /// <summary>Runs a bounded synchronous domain commit only while this recovery owns the active semantic source. Do not await or perform IO inside it.</summary>
    public bool TryCommit(Func<bool> update) { ArgumentNullException.ThrowIfNull(update); return commit(update); }
    /// <summary>Uses one declared surviving neighbor when the provider confirms the original anchor is gone.</summary>
    public bool TryCommit(WidgetCollectionItemKey anchor, Func<bool> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (anchor != AnchorKey && !NeighborKeys.Contains(anchor)) throw new ArgumentException("Recovery anchor is not a declared neighbor.", nameof(anchor));
        return TryCommit(() => { var accepted = update(); if (accepted) ResolvedAnchor = anchor; return accepted; });
    }
    /// <summary>Combines source admission with cursor recovery and bounded surviving-neighbor selection.</summary>
    public bool TryCommit<T>(WidgetCursorResource<T> resource, WidgetCursorRecoveryScope scope, WidgetCursorPage<T> page) where T : notnull =>
        TryCommit(() =>
        {
            var accepted = resource.TryRecover(scope, page, AnchorKey, NeighborKeys, out var restored);
            if (accepted) ResolvedAnchor = restored;
            return accepted;
        });
}

public sealed partial class WidgetLoadedCollection<TContext, TItem>
    where TContext : notnull where TItem : notnull
{
    private sealed record Viewport(IndexedCollectionRangeRequest Request, long Sequence, bool AtEnd, CancellationToken Activity, bool PhysicalAtEnd=false,bool PhysicalAtStart=false);
    private readonly Dictionary<string, Viewport> viewports = new(StringComparer.Ordinal);
    private readonly Func<CancellationToken> viewportActivity;
    private readonly Func<ViewSnapshot?> viewportPresentation;
    private readonly Action viewportInvalidated;
    private CancellationTokenSource? viewportRecovery;
    private CancellationTokenSource viewportControls=new();
    private long viewportGeneration;
    private const int MaximumViewports = 8;
    private int viewportCallbacksInFlight;

    public WidgetCollectionViewportState ViewportState { get { lock (gate) return CaptureViewportStateLocked(); } }

    private WidgetCollectionViewportState CaptureViewportStateLocked()
    {
        var parent = viewportPresentation();
        foreach (var pair in viewports.ToArray())
        {
            var valid = !pair.Value.Activity.IsCancellationRequested && !lifetime.IsCancellationRequested && parent is not null &&
                IndexedCollectionContract.SameViewportDomain(source.Descriptor, pair.Value.Request.Source);
            if (valid)
            {
                try { _ = IndexedCollectionContract.ResolveScope(parent!, pair.Value.Request); }
                catch (Exception error) when (error is ArgumentException or ProtocolValidationException) { valid = false; }
            }
            if (!valid) viewports.Remove(pair.Key);
        }
        return new(viewports.Count, viewportRecovery is null && viewports.Count > 0 && viewports.Values.All(view => view.AtEnd), viewportRecovery is not null)
            { IsAtEnd=viewportRecovery is null && viewports.Count>0 && viewports.Values.All(view=>view.PhysicalAtEnd),
                ObservedEndItemKey=viewportRecovery is null && viewports.Count>0 && current.Keys.Count>0 &&
                    viewports.Values.All(view=>view.PhysicalAtEnd&&view.Request.Source==source.Descriptor)
                        ?current.Keys[^1]:null,
                IsAtStart=viewportRecovery is null && viewports.Count>0 && viewports.Values.All(view=>view.PhysicalAtStart) };
    }

    private void ResetViewportLocked()
    {
        ++viewportGeneration;
        var controls=viewportControls;viewportControls=new();
        _=controls.CancelAsync().ContinueWith(done=>{_=done.Exception;controls.Dispose();},TaskScheduler.Default);
        viewports.Clear();
        if (viewportRecovery is { } previous)
        {
            viewportRecovery = null;
            // Cancel callbacks asynchronously, never while holding the source gate.
            _ = previous.CancelAsync().ContinueWith(completed => { _ = completed.Exception; }, TaskScheduler.Default);
        }
    }

    async ValueTask<IndexedViewportRecoveryResult?> IWidgetIndexedCollection.ControlViewportAsync(IndexedCollectionRangeRequest request, CancellationToken cancellationToken)
    {
        IndexedCollectionContract.ValidateRequest(request);
        CancellationToken domain;
        lock(gate)domain=viewportControls.Token;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime, viewportActivity(), cancellationToken,domain);
        operation.Token.ThrowIfCancellationRequested();
        TContext context;
        WidgetCollectionViewportState state;
        WidgetCollectionViewportState previousState;
        long generation;
        lock (gate)
        {
            previousState = CaptureViewportStateLocked();
            if (request.Source != source.Descriptor && !((request.Viewport is { Active: false } || request.Recovery is not null) &&
                IndexedCollectionContract.SameViewportDomain(source.Descriptor, request.Source)))
                throw new InvalidOperationException("Viewport request no longer names the current source.");
            context = current.Context; generation = viewportGeneration;
            if (request.Viewport is { } observation)
            {
                if (viewports.TryGetValue(observation.ViewportId, out var prior) && prior.Sequence >= observation.Sequence) return null;
                if (!observation.Active) viewports.Remove(observation.ViewportId);
                else
                {
                    if (!viewports.ContainsKey(observation.ViewportId) && viewports.Count >= MaximumViewports)
                        throw new InvalidOperationException("Viewport observation bound exceeded.");
                    viewports[observation.ViewportId] = new(request, observation.Sequence, observation.AtEnd, viewportActivity(),observation.PhysicalAtEnd??observation.AtEnd,observation.PhysicalAtStart==true);
                }
                state = CaptureViewportStateLocked();
            }
            else
            {
                var recovery = request.Recovery!;
                if (options.OnRecoverViewport is null || viewportRecovery is not null ||
                    viewports.TryGetValue(recovery.ViewportId, out var prior) && prior.Sequence >= recovery.Sequence)
                    return new(false, null);
                if (!viewports.ContainsKey(recovery.ViewportId) && viewports.Count >= MaximumViewports) return new(false, null);
                // Reader mode precedes the asynchronous provider read, so later
                // live events cannot repeatedly advance this reading window.
                viewports[recovery.ViewportId] = new(request, recovery.Sequence, false, viewportActivity());
                viewportRecovery = operation;
                state = CaptureViewportStateLocked();
            }
        }
        if (previousState != state) viewportInvalidated();
        if (request.Viewport is not null)
        {
            if (options.OnViewportChanged is { } changed)
            {
                var callback = StartViewportCallback(async () => { await changed(context, state, operation.Token).ConfigureAwait(false); return true; }, operation.Token);
                await callback.WaitAsync(operation.Token).ConfigureAwait(false);
            }
            if(request.Viewport.Boundary is {} edge && options.OnBoundaryDemand is {} adjacent)
            {
                operation.Token.ThrowIfCancellationRequested();
                IReadOnlyList<string>? protectedKeys;
                lock(gate)
                {
                    if(generation!=viewportGeneration || source.Descriptor!=request.Source)return null;
                    protectedKeys=request.Viewport.ProtectedKeys;
                    if(protectedKeys is not null && protectedKeys.Any(key=>!current.Keys.Contains(new WidgetCollectionItemKey(key))))
                        throw new InvalidOperationException("Boundary protection no longer names current occurrences.");
                    // A demand from one view cannot authorize eviction of another
                    // reader. Without the other view's exact anchor, protect the
                    // bounded resident window and let cursor capacity policy decide.
                    if(CaptureViewportStateLocked().ActiveViews>1)
                        protectedKeys=current.Keys.Select(key=>key.Value).ToArray();
                }
                var direction=edge==IndexedCollectionBoundary.Before?WidgetCursorDirection.Before:WidgetCursorDirection.After;
                var demand=new WidgetCollectionBoundaryDemand(direction,protectedKeys);
                var callback=StartViewportCallback(async()=>{await adjacent(context,demand,operation.Token).ConfigureAwait(false);return true;},operation.Token);
                await callback.WaitAsync(operation.Token).ConfigureAwait(false);
            }
            return null;
        }
        var attempted = false;
        var committed = false;
        var owned = new WidgetViewportRecovery(request.Recovery!.AnchorKey, request.Recovery.NeighborKeys, update =>
        {
            lock (gate)
            {
                if (attempted || operation.IsCancellationRequested || generation != viewportGeneration || !ReferenceEquals(viewportRecovery, operation) ||
                    !IndexedCollectionContract.SameViewportDomain(source.Descriptor, request.Source) || CaptureViewportStateLocked().ActiveViews == 0) return false;
                attempted = true;
                committed = update();
                return committed;
            }
        });
        try
        {
            var callback = StartViewportCallback(() => options.OnRecoverViewport!(context, owned, operation.Token), operation.Token);
            var result = await callback.WaitAsync(operation.Token).ConfigureAwait(false);
            if (result == WidgetViewportRecoveryResult.Recovered != committed)
                throw new InvalidOperationException("Viewport recovery result must match its admitted commit.");
            return new(committed, committed ? (owned.ResolvedAnchor ?? owned.AnchorKey).Value : null);
        }
        finally
        {
            lock (gate) if (ReferenceEquals(viewportRecovery, operation)) viewportRecovery = null;
        }
    }

    private Task<T> StartViewportCallback<T>(Func<ValueTask<T>> callback, CancellationToken token)
    {
        lock (gate)
        {
            if (viewportCallbacksInFlight >= 4) throw new InvalidOperationException("Viewport callbacks are still draining.");
            ++viewportCallbacksInFlight;
        }
        var task = Task.Run(async () => await callback().ConfigureAwait(false), token);
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            lock (gate) --viewportCallbacksInFlight;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }
}
