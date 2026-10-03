using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    private sealed record IntentTicket(string SourceId, string SourceFingerprint, BridgeWorkerRun SourceRun,
        long CreatedAt, WidgetIntentRequest Request, WidgetIntentLaunchKind Kind,
        IReadOnlyDictionary<string, string> Targets, BridgeIntentPrepareRequest Origin, WidgetIntentRequest Original);
    private readonly Dictionary<string, IntentTicket> _intentTickets = new(StringComparer.Ordinal);
    private sealed record ActiveIntentDelivery(string SourceId, CancellationTokenSource Cancellation);
    private readonly Dictionary<string, ActiveIntentDelivery> _activeIntentDeliveries = new(StringComparer.Ordinal);
    internal Func<long> IntentNow { get; init; } = () => Environment.TickCount64;
    private static bool IntentFresh(long created, long now) => now >= created && now - created <= 60_000;

    internal async Task<WidgetIntentPreparation> PrepareIntentAsync(BridgeIntentPrepareRequest request, CancellationToken cancellationToken)
    {
        ClientRegistration source;
        IntentTicket ticket;
        lock (_gate)
        {
            DemandNotDisposed();
            if (!_clients.TryGetValue(request.WidgetId, out source!) || !IsCurrentLocked(source)) return Rejected();
        }
        using var held = AdmitInputPublication(source, source);
        await source.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (request.WorkerRun is null || !IsCurrentLocked(source) || source.HostLifecycle != WidgetLifecycleState.Interactive ||
                    !source.HasCurrentSnapshotWorker || source.CachedWorkerRun != request.WorkerRun ||
                    source.FindInputOriginSnapshot(request.SnapshotSequence) is not { } origin ||
                    source.CachedSnapshot is not { } current || request.Action is null ||
                    ResolveIntentSourceLocked(source, origin, current, request) is not { } intent ||
                    !Declared(source, intent) || intent.Fallback is { } fallback && !Declared(source, fallback)) return Rejected();
                ticket = new(request.WidgetId, source.Configured.WorkerFingerprint, request.WorkerRun, IntentNow(), intent,
                    WidgetIntentLaunchKind.Unavailable, new Dictionary<string, string>(), request, intent);
            }
        }
        finally { source.OperationGate.Release(); }
        // No source operation gate is held across another worker's readiness callback.
        try { return await ResolveIntentAsync(ticket, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or BridgeWidgetRequestException or BridgeStaleControllerInputAuthorityException)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await ReportIntentAsync(Guid.NewGuid().ToString("N"), ticket, error is OperationCanceledException
                ? WidgetIntentStatus.Cancelled : WidgetIntentStatus.Failed, cleanup.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            throw;
        }
        WidgetIntentPreparation Rejected() => new(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, []);
    }

    private static bool Declared(ClientRegistration source, WidgetIntentRequest intent)
    {
        var declaration = source.Configured.Intents?.Requests.SingleOrDefault(item => item.Id == intent.ContractId && item.Version == intent.Version);
        if (declaration is null || !intent.IsWellFormed()) return false;
        var contract = CompiledWidgetIntentContract.Create(declaration);
        return contract.SchemaDigest == intent.SchemaDigest && contract.Accepts(intent.Payload);
    }

    private bool IntentActionCurrent(IntentTicket ticket)
    {
        if (!IntentSourceCurrent(ticket)) return false;
        var source = _clients[ticket.SourceId];
        return source.FindInputOriginSnapshot(ticket.Origin.SnapshotSequence) is { } origin && source.CachedSnapshot is { } current &&
            ticket.Original.Matches(ResolveIntentSourceLocked(source, origin, current, ticket.Origin)) && Declared(source, ticket.Request);
    }

    private async Task<WidgetIntentPreparation> ResolveIntentAsync(IntentTicket ticket, CancellationToken token)
    {
        var intent = ticket.Request;
        List<(IntentHandlerCandidate Candidate, string Fingerprint, bool Dynamic)> candidates;
        lock (_gate)
        {
            if (!IntentActionCurrent(ticket)) return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
            if (_intentTickets.Count + _activeIntentDeliveries.Count >= 16 && _intentTickets.Values.All(item => IntentFresh(item.CreatedAt, IntentNow())))
                return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
            candidates = intent.Routing == WidgetIntentRouting.Windows ? [] : _catalog.IntentWidgets.SelectMany(widget =>
                (widget.Intents?.Handles ?? []).Where(handler => handler.Id == intent.ContractId && handler.Version == intent.Version)
                .Select(handler => (new IntentHandlerCandidate(widget.Id, _catalogRevision + 1, CompiledWidgetIntentContract.Create(handler),
                    true, handler.SupportsPassiveDelivery), widget.CatalogFingerprint, handler.HasDynamicAvailability))).ToList();
        }
        if (candidates.Count > IntentResolutionPolicy.MaximumCandidates)
            return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
        var ready = new List<IntentHandlerCandidate>();
        foreach (var candidate in candidates)
        {
            if (candidate.Dynamic && candidate.Candidate.Contract.SchemaDigest == intent.SchemaDigest)
            {
                var target = await GetOrCreateAsync(candidate.Candidate.WidgetId, token).ConfigureAwait(false);
                using var held = AdmitInputPublication(target, target);
                await target.OperationGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    lock (_gate)
                        if (!IsCurrentLocked(target) || target.Configured.CatalogFingerprint != candidate.Fingerprint) continue;
                    target.CancelIdleUnload();
                    if (!await ExecuteClientOperationAsync(target, (client, ct) => client.QueryIntentAsync(intent, null, null, ct), token).ConfigureAwait(false)) continue;
                }
                finally { ScheduleIdleUnload(target, CancellationToken.None); target.OperationGate.Release(); }
            }
            ready.Add(candidate.Candidate);
        }
        WidgetIntentPreparation prepared;
        var id = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            if (!IntentActionCurrent(ticket)) return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
            // Discard catalog entries changed while probing another worker.
            ready.RemoveAll(candidate => !candidates.Any(before => before.Candidate.WidgetId == candidate.WidgetId &&
                _catalog.IntentWidgets.Any(now => now.Id == candidate.WidgetId && now.CatalogFingerprint == before.Fingerprint)));
            var declaration = _clients[ticket.SourceId].Configured.Intents!.Requests.Single(item => item.Id == intent.ContractId && item.Version == intent.Version);
            var resolution = IntentResolutionPolicy.Resolve(CompiledWidgetIntentContract.Create(declaration), intent.Payload, ready.ToArray(), routing: intent.Routing);
            var kind = resolution.Kind switch
            {
                IntentResolutionKind.Widget => WidgetIntentLaunchKind.Widget,
                IntentResolutionKind.ChooseHandler => WidgetIntentLaunchKind.ChooseHandler,
                IntentResolutionKind.ExternalBrowser => WidgetIntentLaunchKind.ExternalBrowser,
                IntentResolutionKind.Unavailable => WidgetIntentLaunchKind.Unavailable,
                _ => WidgetIntentLaunchKind.Rejected,
            };
            prepared = new(ticket.SourceId, null, kind, []);
            if (kind is not (WidgetIntentLaunchKind.Unavailable or WidgetIntentLaunchKind.Rejected))
            {
                var now = IntentNow();
                foreach (var key in _intentTickets.Where(pair => !IntentFresh(pair.Value.CreatedAt, now)).Select(pair => pair.Key).ToArray())
                    _intentTickets.Remove(key);
                if (_intentTickets.Count + _activeIntentDeliveries.Count >= 16) return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
                var targets = resolution.Candidates.ToDictionary(item => item.WidgetId, item => _catalog.GetConfigured(item.WidgetId).CatalogFingerprint, StringComparer.Ordinal);
                _intentTickets.Add(id, ticket with { Kind = kind, Targets = targets });
                return new(ticket.SourceId, id, kind, resolution.Candidates.Select(item => new WidgetIntentDestination(item.WidgetId,
                    _catalog.GetConfigured(item.WidgetId).Name, item.SupportsPassiveDelivery)).ToArray(), intent.Presentation);
            }
        }
        var followUp = await ReportIntentAsync(id, ticket, prepared.Kind == WidgetIntentLaunchKind.Unavailable
            ? WidgetIntentStatus.Unavailable : WidgetIntentStatus.Rejected, token).ConfigureAwait(false);
        return followUp ?? prepared;
    }

    private async Task<WidgetIntentPreparation?> ReportIntentAsync(string id, IntentTicket ticket, WidgetIntentStatus status, CancellationToken token)
    {
        if (!ticket.Request.ReportsResult) return null;
        ClientRegistration source;
        lock (_gate)
        {
            if (!IntentSourceCurrent(ticket)) return null;
            source = _clients[ticket.SourceId];
        }
        bool fallback;
        using (var held = AdmitInputPublication(source, source))
        {
            await source.OperationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (_gate) if (!IntentSourceCurrent(ticket)) return null;
                fallback = await ExecuteClientOperationAsync(source, (client, ct) => client.QueryIntentAsync(ticket.Request,
                    new(id, ticket.Request, status), ticket.SourceRun.StartOrdinal, ct), token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is BridgeWidgetRequestException or BridgeStaleControllerInputAuthorityException) { return null; }
            finally { source.OperationGate.Release(); }
        }
        if (!fallback || status is not (WidgetIntentStatus.Unavailable or WidgetIntentStatus.Rejected) || ticket.Request.Fallback is not { } next) return null;
        lock (_gate) if (!IntentActionCurrent(ticket)) return null;
        return await ResolveIntentAsync(ticket with { Request = next }, token).ConfigureAwait(false);
    }

    internal async Task<WidgetIntentCompletion> CommitIntentAsync(BridgeIntentCommitRequest request, CancellationToken cancellationToken)
    {
        IntentTicket ticket;
        lock (_gate)
        {
            DemandNotDisposed();
            if (!_intentTickets.TryGetValue(request.TicketId, out ticket!) || ticket.SourceId != request.WidgetId) return new(false);
            _intentTickets.Remove(request.TicketId);
        }
        WidgetIntentCompletion completion;
        try { completion = await CommitIntentCoreAsync(request, ticket, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await ReportIntentAsync(request.TicketId, ticket, WidgetIntentStatus.Cancelled, cleanup.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            throw;
        }
        catch (BridgeStaleControllerInputAuthorityException) { completion = new(false); }
        var followUp = await ReportIntentAsync(request.TicketId, ticket, completion.Accepted ? WidgetIntentStatus.Accepted : completion.Status,
            cancellationToken).ConfigureAwait(false);
        return completion with { FollowUp = followUp, Status = completion.Accepted ? WidgetIntentStatus.Accepted : completion.Status };
    }

    private async Task<WidgetIntentCompletion> CommitIntentCoreAsync(BridgeIntentCommitRequest request, IntentTicket ticket, CancellationToken cancellationToken)
    {
        ClientRegistration target;
        CancellationTokenSource operation;
        BridgeClientPublication<ClientRegistration> held;
        lock (_gate)
        {
            DemandNotDisposed();
            if (!IntentFresh(ticket.CreatedAt, IntentNow()) || !IntentSourceCurrent(ticket)) return new(false);
            if (ticket.Kind == WidgetIntentLaunchKind.ExternalBrowser)
                return request.TargetWidgetId is null && request.TargetWorkerRun is null && request.PinnedTarget is null
                    ? new(true, ticket.Request.Payload.GetProperty("url").GetString()) : new(false);
            if (request.TargetWidgetId is null || request.TargetWorkerRun is null ||
                !ticket.Targets.TryGetValue(request.TargetWidgetId, out var fingerprint) ||
                !_clients.TryGetValue(request.TargetWidgetId, out target!) || !IsCurrentLocked(target) ||
                target.Configured.CatalogFingerprint != fingerprint || !IntentTargetPresented(target, ticket, request.PinnedTarget) ||
                !target.HasCurrentSnapshotWorker || target.CachedWorkerRun != request.TargetWorkerRun) return new(false);
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeIntentDeliveries.Add(request.TicketId, new(request.WidgetId, operation));
            held = AdmitPublicationLocked(target, target);
        }
        using (held)
        using (operation)
        {
            try
            {
                await target.OperationGate.WaitAsync(operation.Token).ConfigureAwait(false);
                try
                {
                    lock (_gate)
                    {
                        if (!IntentSourceCurrent(ticket) || !IsCurrentLocked(target) || target.CachedWorkerRun != request.TargetWorkerRun ||
                            !target.HasCurrentSnapshotWorker || !IntentTargetPresented(target, ticket, request.PinnedTarget) ||
                            !ticket.Targets.TryGetValue(target.Configured.Id, out var fingerprint) || target.Configured.CatalogFingerprint != fingerprint)
                            return new(false);
                    }
                    try
                    {
                        var handler = target.Configured.Intents!.Handles.Single(item => item.Id == ticket.Request.ContractId && item.Version == ticket.Request.Version);
                        if (handler.HasDynamicAvailability && !await ExecuteClientOperationAsync(target,
                            (client, token) => client.QueryIntentAsync(ticket.Request, null, request.TargetWorkerRun!.StartOrdinal, token), operation.Token).ConfigureAwait(false))
                            return new(false, Status: WidgetIntentStatus.Unavailable);
                        var result = await ExecuteClientOperationAsync(target,
                            (client, token) => client.DeliverIntentAsync(ticket.Request, request.TargetWorkerRun!.StartOrdinal, token), operation.Token)
                            .ConfigureAwait(false);
                        operation.Token.ThrowIfCancellationRequested();
                        if (!Enum.IsDefined(result)) return new(false);
                        var accepted = result != WidgetIntentResult.Rejected;
                        return new(accepted, BrowserFallbackUrl: accepted ? null : BrowserFallback(ticket.Request),
                            RequiresInteraction: result == WidgetIntentResult.InteractionRequired, Status: accepted ? WidgetIntentStatus.Accepted : WidgetIntentStatus.Rejected);
                    }
                    catch (BridgeWidgetRequestException)
                    { return new(false, BrowserFallbackUrl: BrowserFallback(ticket.Request)); }
                }
                finally { target.OperationGate.Release(); }
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Intent cancellation is not Bridge/session cancellation. Publish
                // a terminal result so the reply remains correlated and usable.
                return new(false, Status: WidgetIntentStatus.Cancelled);
            }
            finally { lock (_gate) _activeIntentDeliveries.Remove(request.TicketId); }
        }
    }

    internal async Task CancelIntentAsync(BridgeIntentCancelRequest request)
    {
        Task cancellation = Task.CompletedTask;
        IntentTicket? cancelled = null;
        lock (_gate)
        {
            if (_intentTickets.TryGetValue(request.TicketId, out var pending) && pending.SourceId == request.WidgetId)
                { _intentTickets.Remove(request.TicketId); cancelled = pending; }
            // The authenticated frontend owns all session tickets. The opaque ID
            // addresses only one admitted operation; cancellation cannot redirect it.
            if (_activeIntentDeliveries.TryGetValue(request.TicketId, out var active) && active.SourceId == request.WidgetId)
                cancellation = active.Cancellation.CancelAsync();
        }
        await cancellation.ConfigureAwait(false);
        if (cancelled is not null)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await ReportIntentAsync(request.TicketId, cancelled, WidgetIntentStatus.Cancelled, cleanup.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private bool IntentSourceCurrent(IntentTicket ticket) => !_disposed &&
        _clients.TryGetValue(ticket.SourceId, out var source) && IsCurrentLocked(source) &&
        source.Configured.WorkerFingerprint == ticket.SourceFingerprint && source.HasCurrentSnapshotWorker &&
        source.CachedWorkerRun == ticket.SourceRun;

    private WidgetIntentRequest? ResolveIntentSourceLocked(ClientRegistration source, ViewSnapshot origin,
        ViewSnapshot current, BridgeIntentPrepareRequest request)
    {
        try
        {
            if (request.IndexedItem is { } reference)
            {
                IndexedCollectionInputContract.ValidateReference(reference);
                var descriptor = source.Configured.PublicDescriptor();
                var lease = FindIndexedOwnerLocked(request.WidgetId, descriptor.InstanceId, descriptor.RuntimeGeneration,
                    descriptor.PresentationGeneration, reference.LeaseId);
                if (lease is null || !ReferenceEquals(lease.Registration, source) ||
                    lease.Request.Range.PinnedLayoutId != request.PinnedLayoutId ||
                    lease.Runtime.Lease.Range.ScopeId != request.Action.InputScopeId) return null;
                _ = DemandIndexedInputOwnerLocked(lease);
                var item = lease.Runtime.Lease.Range.Items.SingleOrDefault(item => item.Key == reference.ItemKey);
                return item is null ? null : IntentActionAuthority.RevalidateIndexed(
                    ResolveIndexedInputOwners(origin, lease), ResolveIndexedInputOwners(current, lease), item.Root, request.Action);
            }
            if (request.PinnedLayoutId is { } layout)
            {
                if (!source.Configured.PinningSupported ||
                    layout == PinnedSurfaceContract.FullWidgetLayoutId && !source.Configured.FullWidgetPinningSupported) return null;
                origin = PinnedActionContract.Project(origin, layout, inheritRootless: true);
                current = PinnedActionContract.Project(current, layout, inheritRootless: true);
            }
            return IntentActionAuthority.Revalidate(origin, current, request.Action);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or BridgeProtocolException or BridgeStaleIndexedInputAuthorityException)
        { return null; }
    }

    private static bool IntentTargetPresented(ClientRegistration target, IntentTicket ticket, BridgeIntentPinnedTarget? pin)
    {
        if (pin is null) return target.HostLifecycle == WidgetLifecycleState.Interactive;
        // The authenticated presentation session owns the live selection. Never
        // infer it from saved pin preferences; revalidate its exact displayed
        // layout and worker here before permitting noninteractive delivery.
        if (ticket.Request.Presentation != WidgetIntentPresentation.PreferExistingSurface ||
            target.HostLifecycle is not (WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive) ||
            !target.Configured.PinningSupported ||
            pin.LayoutId == PinnedSurfaceContract.FullWidgetLayoutId && !target.Configured.FullWidgetPinningSupported ||
            !ProtocolValidationIdentifierContext.IsSafeIdentifier(pin.LayoutId) || pin.SnapshotSequence <= 0 ||
            target.Configured.Intents?.Handles.SingleOrDefault(item => item.Id == ticket.Request.ContractId &&
                item.Version == ticket.Request.Version) is not { SupportsPassiveDelivery: true } ||
            target.FindInputOriginSnapshot(pin.SnapshotSequence) is not { } origin || target.CachedSnapshot is not { } current)
            return false;
        if (pin.MediaSessionId is { } mediaId)
        {
            return pin.LayoutId == "host.embedded-media.compact" && origin.ActiveInputScopeId == current.ActiveInputScopeId &&
                origin.EmbeddedMediaSession is { } before && current.EmbeddedMediaSession is { } after &&
                before.Id == mediaId && after.Id == mediaId && before.EntryAsset == after.EntryAsset &&
                before.Resources.SequenceEqual(after.Resources) &&
                before.AllowedFrameOrigins.SequenceEqual(after.AllowedFrameOrigins, StringComparer.Ordinal) &&
                before.AllowedFrameDomainFamilies.SequenceEqual(after.AllowedFrameDomainFamilies, StringComparer.Ordinal) &&
                before.SupportedPresentations.Contains(MediaPresentationKind.CompactPinned) &&
                after.SupportedPresentations.Contains(MediaPresentationKind.CompactPinned);
        }
        try
        {
            _ = PinnedActionContract.Project(origin, pin.LayoutId, inheritRootless: true);
            _ = PinnedActionContract.Project(current, pin.LayoutId, inheritRootless: true);
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    // An offer for explicit user confirmation, never permission to auto-launch
    // a second handler after an uncertain or unsuccessful delivery.
    private static string? BrowserFallback(WidgetIntentRequest request) =>
        request.ContractId == WidgetIntentContracts.OpenWebPage && request.Version == 1
            ? request.Payload.GetProperty("url").GetString() : null;
}
