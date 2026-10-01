using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    private sealed record IntentTicket(string SourceId, string SourceFingerprint, BridgeWorkerRun SourceRun,
        long CreatedAt, WidgetIntentRequest Request, WidgetIntentLaunchKind Kind,
        IReadOnlyDictionary<string, string> Targets);
    private readonly Dictionary<string, IntentTicket> _intentTickets = new(StringComparer.Ordinal);
    private sealed record ActiveIntentDelivery(string SourceId, CancellationTokenSource Cancellation);
    private readonly Dictionary<string, ActiveIntentDelivery> _activeIntentDeliveries = new(StringComparer.Ordinal);
    internal Func<long> IntentNow { get; init; } = () => Environment.TickCount64;
    private static bool IntentFresh(long created, long now) => now >= created && now - created <= 60_000;

    internal async Task<WidgetIntentPreparation> PrepareIntentAsync(BridgeIntentPrepareRequest request, CancellationToken cancellationToken)
    {
        ClientRegistration source;
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
                DemandCurrentInputRegistration(source);
                if (request.WorkerRun is null || source.HostLifecycle != WidgetLifecycleState.Interactive ||
                    !source.HasCurrentSnapshotWorker || source.CachedWorkerRun != request.WorkerRun ||
                    source.FindInputOriginSnapshot(request.SnapshotSequence) is not { } origin ||
                    source.CachedSnapshot is not { } current || request.Action is null ||
                    IntentActionAuthority.Revalidate(origin, current, request.Action) is not { } intent) return Rejected();

                var declaration = source.Configured.Intents?.Requests.SingleOrDefault(item => item.Id == intent.ContractId && item.Version == intent.Version);
                if (declaration is null) return Rejected();
                var contract = CompiledWidgetIntentContract.Create(declaration);
                if (contract.SchemaDigest != intent.SchemaDigest || !contract.Accepts(intent.Payload)) return Rejected();
                var candidates = _catalog.IntentWidgets.SelectMany(widget => (widget.Intents?.Handles ?? [])
                    .Where(item => item.Id == intent.ContractId && item.Version == intent.Version)
                    .Select(item => new IntentHandlerCandidate(widget.Id, _catalogRevision + 1,
                        CompiledWidgetIntentContract.Create(item), true, item.SupportsPassiveDelivery))).ToArray();
                var resolution = IntentResolutionPolicy.Resolve(contract, intent.Payload, candidates);
                var kind = resolution.Kind switch
                {
                    IntentResolutionKind.Widget => WidgetIntentLaunchKind.Widget,
                    IntentResolutionKind.ChooseHandler => WidgetIntentLaunchKind.ChooseHandler,
                    IntentResolutionKind.ExternalBrowser => WidgetIntentLaunchKind.ExternalBrowser,
                    IntentResolutionKind.Unavailable => WidgetIntentLaunchKind.Unavailable,
                    _ => WidgetIntentLaunchKind.Rejected,
                };
                if (kind is WidgetIntentLaunchKind.Unavailable or WidgetIntentLaunchKind.Rejected)
                    return new(request.WidgetId, null, kind, []);
                var now = IntentNow();
                foreach (var key in _intentTickets.Where(pair => !IntentFresh(pair.Value.CreatedAt, now)).Select(pair => pair.Key).ToArray())
                    _intentTickets.Remove(key);
                if (_intentTickets.Count + _activeIntentDeliveries.Count >= 16) return Rejected();
                var destinations = resolution.Candidates.Select(candidate => _catalog.GetConfigured(candidate.WidgetId)).ToArray();
                var ticketId = Guid.NewGuid().ToString("N");
                _intentTickets.Add(ticketId, new(request.WidgetId, source.Configured.WorkerFingerprint, request.WorkerRun,
                    now, intent with { Payload = intent.Payload.Clone() }, kind,
                    destinations.ToDictionary(widget => widget.Id, widget => widget.CatalogFingerprint, StringComparer.Ordinal)));
                return new(request.WidgetId, ticketId, kind,
                    resolution.Candidates.Select(candidate => new WidgetIntentDestination(candidate.WidgetId,
                        _catalog.GetConfigured(candidate.WidgetId).Name, candidate.SupportsPassiveDelivery)).ToArray(), intent.Presentation);
            }
        }
        finally { source.OperationGate.Release(); }
        WidgetIntentPreparation Rejected() => new(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, []);
    }

    internal async Task<WidgetIntentCompletion> CommitIntentAsync(BridgeIntentCommitRequest request, CancellationToken cancellationToken)
    {
        ClientRegistration target;
        IntentTicket ticket;
        CancellationTokenSource operation;
        BridgeClientPublication<ClientRegistration> held;
        lock (_gate)
        {
            DemandNotDisposed();
            if (!_intentTickets.Remove(request.TicketId, out ticket!) || ticket.SourceId != request.WidgetId ||
                !IntentFresh(ticket.CreatedAt, IntentNow()) || !IntentSourceCurrent(ticket)) return new(false);
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
                        var result = await ExecuteClientOperationAsync(target,
                            (client, token) => client.DeliverIntentAsync(ticket.Request, request.TargetWorkerRun!.StartOrdinal, token), operation.Token)
                            .ConfigureAwait(false);
                        operation.Token.ThrowIfCancellationRequested();
                        if (!Enum.IsDefined(result)) return new(false);
                        var accepted = result != WidgetIntentResult.Rejected;
                        return new(accepted, BrowserFallbackUrl: accepted ? null : BrowserFallback(ticket.Request),
                            RequiresInteraction: result == WidgetIntentResult.InteractionRequired);
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
                return new(false);
            }
            finally { lock (_gate) _activeIntentDeliveries.Remove(request.TicketId); }
        }
    }

    internal async Task CancelIntentAsync(BridgeIntentCancelRequest request)
    {
        Task cancellation = Task.CompletedTask;
        lock (_gate)
        {
            if (_intentTickets.TryGetValue(request.TicketId, out var pending) && pending.SourceId == request.WidgetId)
                _intentTickets.Remove(request.TicketId);
            // The authenticated frontend owns all session tickets. The opaque ID
            // addresses only one admitted operation; cancellation cannot redirect it.
            if (_activeIntentDeliveries.TryGetValue(request.TicketId, out var active) && active.SourceId == request.WidgetId)
                cancellation = active.Cancellation.CancelAsync();
        }
        await cancellation.ConfigureAwait(false);
    }

    private bool IntentSourceCurrent(IntentTicket ticket) => !_disposed &&
        _clients.TryGetValue(ticket.SourceId, out var source) && IsCurrentLocked(source) &&
        source.Configured.WorkerFingerprint == ticket.SourceFingerprint && source.HasCurrentSnapshotWorker &&
        source.CachedWorkerRun == ticket.SourceRun;

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
