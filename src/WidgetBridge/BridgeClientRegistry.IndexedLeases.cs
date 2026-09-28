using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

internal interface IBridgeIndexedLease : IAsyncDisposable
{
    IndexedCollectionLease Lease
    {
        get;
    }
    Task<WidgetOperationAdmission?> AdmitInputAsync(IndexedCollectionInputRequest input, IndexedCollectionInputContext correlation, CancellationToken cancellationToken);
    Task<WidgetEncodedArtwork?> ResolveArtworkAsync(string itemKey, string artworkHandle, CancellationToken cancellationToken);
}

internal sealed class WidgetProcessBridgeIndexedLease(WidgetProcessIndexedLease lease) : IBridgeIndexedLease
{
    public IndexedCollectionLease Lease => lease.Lease;
    public Task<WidgetOperationAdmission?> AdmitInputAsync(IndexedCollectionInputRequest input, IndexedCollectionInputContext correlation, CancellationToken cancellationToken) =>
        lease.AdmitInputAsync(input, correlation, cancellationToken);
    public Task<WidgetEncodedArtwork?> ResolveArtworkAsync(string itemKey, string artworkHandle, CancellationToken cancellationToken) =>
        lease.ResolveArtworkAsync(itemKey, artworkHandle, cancellationToken);
    public ValueTask DisposeAsync() => lease.DisposeAsync();
}

internal sealed partial class BridgeClientRegistry
{
    private const int MaximumGlobalIndexedLeases = 128;
    private const int MaximumGlobalIndexedLeaseItems = 4096;
    private const int MaximumGlobalIndexedLeaseNodes = 131072;
    private const int MaximumLocalIndexedLeaseNodes = 32768;
    private readonly Dictionary<(string Widget, string Lease), IndexedOwner> indexedOwners = [];
    private readonly HashSet<BridgeIndexedRangeRequest> indexedAcquisitions = [];
    private readonly Dictionary<BridgeIndexedArtworkRequest, IndexedArtworkOperation> indexedArtwork = [];

    private sealed class IndexedOwner(BridgeIndexedRangeRequest request, ClientRegistration registration,
        int workerStart, IBridgeIndexedLease lease, BridgeClientPublication<ClientRegistration> lifetime)
    {
        internal readonly BridgeIndexedRangeRequest Request = request;
        internal readonly ClientRegistration Registration = registration;
        internal readonly int WorkerStart = workerStart;
        internal readonly IBridgeIndexedLease Runtime = lease;
        internal readonly int NodeCount = lease.Lease.Range.Items.Sum(item => CountIndexedNodes(item.Root));
        internal readonly BridgeClientPublication<ClientRegistration> Lifetime = lifetime;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly TaskCompletionSource Idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Retiring;
        internal int Active;
    }
    private sealed class IndexedArtworkOperation(IndexedOwner owner, CancellationToken cancellationToken)
    {
        internal readonly IndexedOwner Owner = owner;
        internal readonly CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner.Cancellation.Token);
        internal Task? CancellationCompletion;
        internal bool Completing;
        internal void Cancel()
        {
            if (!Completing)
                CancellationCompletion ??= Cancellation.CancelAsync();
        }
    }

    internal async Task<BridgeClientPublication<BridgeIndexedLeaseResponse>> AcquireIndexedRangeAsync(
        BridgeIndexedRangeRequest request, CancellationToken cancellationToken,
        Func<ConfiguredWidget, IndexedCollectionRange, BridgeResolvedStyleSnapshot>? resolveStyles = null)
    {
        BridgeIndexedRangeValidation.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        ClientRegistration registration;
        IndexedReadOperation operation;
        BridgeClientPublication<ClientRegistration>? lifetime;
        lock (_gate)
        {
            DemandNotDisposed();
            if (!_clients.TryGetValue(request.WidgetId, out registration!) || !IsCurrentLocked(registration) || !registration.Client.IsRunning)
                throw new BridgeProtocolException("Indexed acquisition requires a running presentation.");
            if (indexedReads.Count >= MaximumIndexedReads ||
                indexedReads.Values.Count(read => ReferenceEquals(read.Registration, registration)) >= MaximumIndexedReadsPerWidget)
                throw new BridgeProtocolException("Indexed read admission is full.");
            if (indexedReads.Keys.Concat(indexedOwners.Values.Select(owner => owner.Request)).Any(existing => SameIndexedDemand(existing, request)))
                throw new BridgeProtocolException("Indexed demand is already owned.");
            var owners = indexedOwners.Values.ToArray();
            var local = owners.Where(owner => ReferenceEquals(owner.Registration, registration)).ToArray();
            var localReservations = indexedAcquisitions.Where(candidate => candidate.WidgetId == request.WidgetId).ToArray();
            if (owners.Length + indexedAcquisitions.Count >= MaximumGlobalIndexedLeases ||
                owners.Sum(owner => owner.Request.Range.Count) + indexedAcquisitions.Sum(candidate => candidate.Range.Count) + request.Range.Count > MaximumGlobalIndexedLeaseItems ||
                local.Length + localReservations.Length >= Widget.MaximumIndexedLeases ||
                local.Sum(owner => owner.Request.Range.Count) + localReservations.Sum(candidate => candidate.Range.Count) + request.Range.Count > Widget.MaximumIndexedLeaseItems ||
                owners.Sum(owner => owner.NodeCount) + (indexedAcquisitions.Count + 1) * IndexedCollectionLimits.MaximumRangeNodes > MaximumGlobalIndexedLeaseNodes ||
                local.Sum(owner => owner.NodeCount) + (localReservations.Length + 1) * IndexedCollectionLimits.MaximumRangeNodes > MaximumLocalIndexedLeaseNodes)
                throw new BridgeProtocolException("Indexed semantic lease budget is full.");
            lifetime = AdmitPublicationLocked(registration, registration);
            operation = new(registration, cancellationToken);
            indexedReads.Add(request, operation);
            indexedAcquisitions.Add(request);
        }
        IBridgeIndexedLease? acquired = null;
        IndexedOwner? admitted = null;
        var delivered = false;
        try
        {
            var token = operation.Cancellation.Token;
            int workerStart;
            await registration.OperationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_gate)
                    DemandIndexedAuthorityLocked(registration, request);
                workerStart = registration.Client.Starts;
            }
            finally { registration.OperationGate.Release(); }
            acquired = await registration.Client.AcquireIndexedRangeAsync(request.Range, workerStart, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await registration.OperationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ConfiguredWidget configured;
                ViewSnapshot parent;
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    parent = DemandIndexedAuthorityLocked(registration, request);
                    configured = registration.Configured;
                }
                IndexedCollectionContract.ValidateRange(parent, request.Range, acquired.Lease.Range);
                // Resolve only bounded range roots. Compilation never holds the global
                // registry lock; the final identity check rejects concurrent catalog changes.
                var styleSnapshot = resolveStyles is null
                    ? new(0, BridgeRenderStyleResolver.ResolveRange(acquired.Lease.Range, configured.CompiledTheme))
                    : resolveStyles(configured, acquired.Lease.Range);
                if (styleSnapshot.Revision < 0) throw new BridgeProtocolException("Invalid appearance revision.");
                var styles = BridgeRenderStyleContract.ValidateAndFreeze(styleSnapshot.RenderStyles,
                    BridgeRenderStyleContract.RangeNodeIds(acquired.Lease.Range), requireComplete: true);
                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    _ = DemandIndexedAuthorityLocked(registration, request);
                    if (!ReferenceEquals(registration.Configured, configured))
                        throw new BridgeProtocolException("Indexed style configuration changed before publication.");
                    if (registration.Client.Starts != workerStart || !Guid.TryParseExact(acquired.Lease.LeaseId, "N", out _))
                        throw new BridgeProtocolException("Indexed acquisition worker authority changed.");
                    var key = (request.WidgetId, acquired.Lease.LeaseId);
                    if (indexedOwners.ContainsKey(key))
                        throw new BridgeProtocolException("Worker reused indexed lease identity.");
                    admitted = new(request, registration, workerStart, acquired, lifetime);
                    indexedOwners.Add(key, admitted);
                    indexedAcquisitions.Remove(request);
                    lifetime = null;
                    acquired = null;
                    var response = new BridgeIndexedLeaseResponse(request.WidgetId, request.InstanceId, request.RuntimeGeneration,
                        request.PresentationGeneration, admitted.Runtime.Lease, styles, styleSnapshot.Revision);
                    var publication = AdmitPublicationLocked(registration, response);
                    delivered = true;
                    return publication;
                }
            }
            finally { registration.OperationGate.Release(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            if (exception is BridgeProtocolException)
                throw;
            throw new BridgeProtocolException("Indexed acquisition was cancelled or changed authority.", exception);
        }
        finally
        {
            Task? cancellation;
            lock (_gate)
            {
                operation.Completing = true;
                cancellation = operation.CancellationCompletion;
            }
            try
            {
                if (cancellation is not null)
                    await cancellation.ConfigureAwait(false);
                if (acquired is not null)
                    await acquired.DisposeAsync().ConfigureAwait(false);
                if (!delivered && admitted is not null)
                {
                    lock (_gate)
                        StartIndexedReleaseLocked(admitted);
                    await admitted.Released.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_gate)
                {
                    indexedReads.Remove(request);
                    indexedAcquisitions.Remove(request);
                }
                operation.Cancellation.Dispose();
                lifetime?.Dispose();
            }
        }
    }

    internal BridgeClientPublication<BridgeIndexedStylesResponse> RefreshIndexedStyles(BridgeIndexedLeaseRequest request,
        Func<ConfiguredWidget, IndexedCollectionRange, BridgeResolvedStyleSnapshot> resolveStyles, CancellationToken cancellationToken)
    {
        IndexedOwner owner;
        ConfiguredWidget configured;
        lock (_gate)
        {
            DemandNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            owner = FindIndexedOwnerLocked(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration, request.LeaseId)
                ?? throw new BridgeProtocolException("Indexed style lease is unavailable.");
            _ = DemandIndexedOwnerLocked(owner);
            if (owner.Active >= Widget.ActionQueueCapacity) throw new BridgeProtocolException("Indexed lease admission is full.");
            ++owner.Active;
            configured = owner.Registration.Configured;
        }
        try
        {
            // Compile bounded retained declarations, never ask the worker/provider
            // for new semantics or change the lease's input/artwork authority.
            var snapshot = resolveStyles(configured, owner.Runtime.Lease.Range);
            if (snapshot.Revision < 0) throw new BridgeProtocolException("Invalid appearance revision.");
            var styles = BridgeRenderStyleContract.ValidateAndFreeze(snapshot.RenderStyles,
                BridgeRenderStyleContract.RangeNodeIds(owner.Runtime.Lease.Range), requireComplete: true);
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = DemandIndexedOwnerLocked(owner);
                if (!ReferenceEquals(configured, owner.Registration.Configured))
                    throw new BridgeProtocolException("Indexed style configuration changed before publication.");
                return AdmitPublicationLocked(owner.Registration, new BridgeIndexedStylesResponse(request.WidgetId,
                    request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration, request.LeaseId, snapshot.Revision, styles));
            }
        }
        finally { lock (_gate) EndIndexedOperationLocked(owner); }
    }

    internal async Task<bool> ReleaseIndexedLeaseAsync(BridgeIndexedLeaseRequest request)
    {
        IndexedOwner? owner;
        lock (_gate)
        {
            owner = FindIndexedOwnerLocked(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration, request.LeaseId, includeRetiring: true);
            if (owner is null)
                return false;
            StartIndexedReleaseLocked(owner);
        }
        await owner.Released.Task.ConfigureAwait(false);
        return true;
    }

    internal async Task<WidgetOperationAdmission?> AdmitIndexedInputAsync(BridgeIndexedInputRequest request, CancellationToken cancellationToken)
    {
        IndexedCollectionInputContract.ValidateInput(request.Input);
        IndexedCollectionInputContract.ValidateContext(request.Context);
        cancellationToken.ThrowIfCancellationRequested();
        IndexedOwner owner;
        lock (_gate)
        {
            owner = FindIndexedOwnerLocked(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration, request.Input.Item.LeaseId)
                ?? throw new BridgeProtocolException("Indexed input lease is unavailable.");
            if (owner.Active >= Widget.ActionQueueCapacity)
                throw new BridgeProtocolException("Indexed input admission is full.");
            owner.Active++;
        }
        var registration = owner.Registration;
        try
        {
            await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IndexedCollectionInputContext currentContext;
                lock (_gate)
                {
                    var current = DemandIndexedOwnerLocked(owner);
                    DemandInteractionAllowed(registration);
                    var origin = registration.FindInputOriginSnapshot(request.Context.SnapshotSequence)
                        ?? throw new BridgeProtocolException("Indexed input origin is unavailable.");
                    var item = owner.Runtime.Lease.Range.Items.SingleOrDefault(item => item.Key == request.Input.Item.ItemKey)
                        ?? throw new BridgeProtocolException("Indexed input item is outside the lease.");
                    var expectedScope = owner.Runtime.Lease.Range.ScopeId;
                    if (request.Context.InputScopeId != expectedScope)
                        throw new BridgeProtocolException("Indexed input scope does not match its data lease.");
                    var originOwners = IndexedCollectionInputContract.ResolveOwnerPath(origin, owner.Request.Range);
                    var currentOwners = IndexedCollectionInputContract.ResolveOwnerPath(current, owner.Request.Range);
                    var originBinding = IndexedCollectionInputContract.Resolve(originOwners, item.Root, request.Input);
                    var currentBinding = IndexedCollectionInputContract.Resolve(currentOwners, item.Root, request.Input);
                    if (originBinding != currentBinding)
                        throw new BridgeProtocolException("Indexed input binding changed after presentation.");
                    if (currentBinding is null)
                        return null;
                    currentContext = request.Context with
                    {
                        SnapshotSequence = current.Sequence
                    };
                }
                // Once admitted, the runtime exchange owns cancellation and drains its
                // correlation. Keep this operation alive until that acknowledgement.
                return await owner.Runtime.AdmitInputAsync(request.Input, currentContext, CancellationToken.None).ConfigureAwait(false);
            }
            finally { registration.OperationGate.Release(); }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or ProtocolValidationException)
        {
            throw new BridgeProtocolException("Indexed input authority changed.");
        }
        catch (Exception exception) when (IsWidgetRuntimeFailure(exception))
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            throw new BridgeWidgetRequestException(registration.Configured.Id, ClassifyWidgetRuntimeFailure(exception), exception);
        }
        finally { lock (_gate) EndIndexedOperationLocked(owner); }
    }

    internal async Task<WidgetEncodedArtwork?> ResolveIndexedArtworkAsync(BridgeIndexedArtworkRequest request, CancellationToken cancellationToken,
        Func<ConfiguredWidget, string, CancellationToken, Task<WidgetEncodedArtwork?>>? resolveBrokerArtwork = null)
    {
        IndexedCollectionInputContract.ValidateReference(request.Item);
        if (!BridgeRequestKey.IsBoundedIdentifier(request.DemandId) || !BridgeRequestKey.IsBoundedIdentifier(request.ArtworkHandle))
            throw new BridgeProtocolException("Indexed artwork demand is invalid.");
        IndexedOwner owner;
        IndexedArtworkOperation operation;
        ConfiguredWidget configured;
        lock (_gate)
        {
            owner = FindIndexedOwnerLocked(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration, request.Item.LeaseId)
                ?? throw new BridgeProtocolException("Indexed artwork lease is unavailable.");
            _ = DemandIndexedOwnerLocked(owner);
            configured = owner.Registration.Configured;
            var item = owner.Runtime.Lease.Range.Items.SingleOrDefault(item => item.Key == request.Item.ItemKey);
            if (item is null || !ContainsIndexedArtwork(item.Root, request.ArtworkHandle))
                return null;
            if (indexedArtwork.Count >= 8 || indexedArtwork.Values.Count(value => ReferenceEquals(value.Owner.Registration, owner.Registration)) >= 4 ||
                indexedArtwork.ContainsKey(request))
                throw new BridgeProtocolException("Indexed artwork admission is full or duplicate.");
            operation = new(owner, cancellationToken);
            indexedArtwork.Add(request, operation);
            owner.Active++;
        }
        try
        {
            // Broker handles are resolved only by their host registry, never by a
            // widget callback. The exact row lease guards both request and completion.
            var artwork = WidgetRail.PlatformBroker.AppLibraryArtworkRegistry.IsHandle(request.ArtworkHandle)
                ? resolveBrokerArtwork is null ? null : await resolveBrokerArtwork(configured, request.ArtworkHandle, operation.Cancellation.Token).ConfigureAwait(false)
                : await owner.Runtime.ResolveArtworkAsync(request.Item.ItemKey, request.ArtworkHandle, operation.Cancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                operation.Cancellation.Token.ThrowIfCancellationRequested();
                _ = DemandIndexedOwnerLocked(owner);
                if (artwork is not null && !WidgetEncodedArtworkContract.IsValid(artwork))
                    throw new BridgeProtocolException("Indexed artwork is invalid.");
                return artwork;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            if (exception is BridgeProtocolException)
                throw;
            throw new BridgeProtocolException("Indexed artwork demand was cancelled or changed authority.");
        }
        finally
        {
            Task? cancellation;
            lock (_gate)
            {
                operation.Completing = true;
                cancellation = operation.CancellationCompletion;
            }
            try
            {
                if (cancellation is not null)
                    await cancellation.ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    indexedArtwork.Remove(request);
                    EndIndexedOperationLocked(owner);
                }
                operation.Cancellation.Dispose();
            }
        }
    }

    internal Task<bool> CancelIndexedArtworkAsync(BridgeIndexedArtworkRequest request)
    {
        lock (_gate)
        {
            if (!indexedArtwork.TryGetValue(request, out var operation) || operation.Completing)
                return Task.FromResult(false);
            operation.Cancel();
            return Task.FromResult(true);
        }
    }

    private static bool SameIndexedDemand(BridgeIndexedRangeRequest left, BridgeIndexedRangeRequest right) =>
        left.WidgetId == right.WidgetId && left.Range.CollectionId == right.Range.CollectionId && left.Range.Source == right.Range.Source &&
        left.Range.DemandId == right.Range.DemandId && left.Range.PinnedLayoutId == right.Range.PinnedLayoutId;

    private IndexedOwner? FindIndexedOwnerLocked(string widget, string instance, string runtime, string presentation, string lease, bool includeRetiring = false)
    {
        if (!indexedOwners.TryGetValue((widget, lease), out var owner) || !includeRetiring && owner.Retiring ||
            owner.Request.InstanceId != instance || owner.Request.RuntimeGeneration != runtime || owner.Request.PresentationGeneration != presentation)
            return null;
        return owner;
    }
    private ViewSnapshot DemandIndexedOwnerLocked(IndexedOwner owner)
    {
        if (owner.Retiring || owner.Registration.Client.Starts != owner.WorkerStart)
            throw new BridgeProtocolException("Indexed lease retired.");
        var parent = DemandIndexedAuthorityLocked(owner.Registration, owner.Request);
        if (IndexedCollectionContract.ResolveScope(parent, owner.Request.Range) != owner.Runtime.Lease.Range.ScopeId)
            throw new BridgeProtocolException("Indexed lease logical scope changed.");
        return parent;
    }
    private void EndIndexedOperationLocked(IndexedOwner owner)
    {
        if (--owner.Active < 0)
            throw new InvalidOperationException("Indexed operation released twice.");
        if (owner.Retiring && owner.Active == 0)
            owner.Idle.TrySetResult();
    }
    private Task<bool> CancelDeliveredIndexedDemandLocked(BridgeIndexedRangeRequest request)
    {
        var owned = indexedOwners.Values.Where(owner => owner.Request == request).ToArray();
        foreach (var owner in owned)
            StartIndexedReleaseLocked(owner);
        return Task.FromResult(owned.Length != 0);
    }
    private void RetireIndexedOwnersLocked(ClientRegistration registration, bool validate)
    {
        foreach (var owner in indexedOwners.Values.Where(owner => ReferenceEquals(owner.Registration, registration)).ToArray())
        {
            if (!validate)
            {
                StartIndexedReleaseLocked(owner);
                continue;
            }
            try
            {
                _ = DemandIndexedOwnerLocked(owner);
            }
            catch (Exception error) when (error is ArgumentException or BridgeProtocolException or ProtocolValidationException) { StartIndexedReleaseLocked(owner); }
        }
    }
    private void StartIndexedReleaseLocked(IndexedOwner owner)
    {
        if (owner.Retiring)
            return;
        owner.Retiring = true;
        if (owner.Active == 0)
            owner.Idle.TrySetResult();
        _ = Task.Run(async () =>
        {
            try
            {
                await owner.Cancellation.CancelAsync().ConfigureAwait(false);
                await owner.Idle.Task.ConfigureAwait(false);
                await owner.Runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) { RecordTerminalFailure(error); }
            finally
            {
                lock (_gate)
                    indexedOwners.Remove((owner.Request.WidgetId, owner.Runtime.Lease.LeaseId));
                owner.Cancellation.Dispose();
                owner.Lifetime.Dispose();
                owner.Released.TrySetResult();
            }
        });
    }
    private static int CountIndexedNodes(ViewNode node) => 1 + node.Children.Sum(CountIndexedNodes) +
        (node.FocusPresentation is { } focus ? CountIndexedNodes(focus) : 0) +
        (node.DefaultFocusPresentation is { } fallback ? CountIndexedNodes(fallback) : 0);
    private static bool ContainsIndexedArtwork(ViewNode node, string handle) => node.ArtworkHandle == handle || node.FocusBackgroundArtworkHandle == handle ||
        node.FocusPresentation is { } focus && ContainsIndexedArtwork(focus, handle) || node.DefaultFocusPresentation is { } fallback && ContainsIndexedArtwork(fallback, handle) ||
        node.Children.Any(child => ContainsIndexedArtwork(child, handle));
}
