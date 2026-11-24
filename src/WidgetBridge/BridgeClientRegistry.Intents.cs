using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    private sealed record IntentTicket(string SourceId, string SourceFingerprint, BridgeWorkerRun? SourceRun,
        long CreatedAt, WidgetIntentRequest Request, WidgetIntentLaunchKind Kind,
        IReadOnlyDictionary<string, string> Targets, BridgeIntentPrepareRequest? Origin, WidgetIntentRequest Original,
        string? NotificationToken = null, bool HostSettings = false, string? ManagedWidgetId = null, string? ManagedFingerprint = null,
        string? PreviewProof = null, WebNotificationIntentPayload? WebNotification = null);
    private readonly Dictionary<string, IntentTicket> _intentTickets = new(StringComparer.Ordinal);
    private sealed record ActiveIntentDelivery(string SourceId, CancellationTokenSource Cancellation);
    private readonly Dictionary<string, ActiveIntentDelivery> _activeIntentDeliveries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Source, long Time)> _cancelledIntentPreparations = new(StringComparer.Ordinal);
    internal Func<long> IntentNow { get; init; } = () => Environment.TickCount64;
    internal WidgetSummaryExchange? Summaries { get; init; }
    private static bool IntentFresh(long created, long now) => now >= created && now - created <= 60_000;

    internal async Task<WidgetIntentPreparation> PrepareIntentAsync(BridgeIntentPrepareRequest request, CancellationToken cancellationToken)
    {
        if (request.PreparationId is null) return await PrepareIntentCoreAsync(request, cancellationToken).ConfigureAwait(false);
        if (!Guid.TryParseExact(request.PreparationId, "N", out _)) return new(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, []);
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        preparation.CancelAfter(TimeSpan.FromSeconds(65));
        lock (_gate)
        {
            DemandNotDisposed();
            if (_cancelledIntentPreparations.TryGetValue(request.PreparationId, out var cancelled) && IntentFresh(cancelled.Time, IntentNow()))
                return new(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, [], PreparationFailed: true);
            if (_activeIntentDeliveries.Count >= 16 || !_activeIntentDeliveries.TryAdd(request.PreparationId, new(request.WidgetId, preparation)))
                return new(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, []);
        }
        try { return await PrepareIntentCoreAsync(request, preparation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { return new(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, [], PreparationFailed: true); }
        finally
        {
            lock (_gate)
            {
                _activeIntentDeliveries.Remove(request.PreparationId);
                RememberIntentPreparation(request.PreparationId, request.WidgetId);
            }
        }
    }

    private void RememberIntentPreparation(string id, string source)
    {
        foreach (var key in _cancelledIntentPreparations.Where(p => !IntentFresh(p.Value.Time, IntentNow())).Select(p => p.Key).ToArray())
            _cancelledIntentPreparations.Remove(key);
        if (_cancelledIntentPreparations.Count >= 64) _cancelledIntentPreparations.Remove(_cancelledIntentPreparations.MinBy(p => p.Value.Time).Key);
        _cancelledIntentPreparations[id] = (source, IntentNow());
    }

    private async Task<WidgetIntentPreparation> PrepareIntentCoreAsync(BridgeIntentPrepareRequest request, CancellationToken cancellationToken)
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
                    ResolveIntentSourceLocked(source, origin, current, request, out var previewProof) is not { } intent ||
                    !Declared(source, intent) || intent.Fallback is { } fallback && !Declared(source, fallback)) return Rejected();
                ticket = new(request.WidgetId, source.Configured.WorkerFingerprint, request.WorkerRun, IntentNow(), intent,
                    WidgetIntentLaunchKind.Unavailable, new Dictionary<string, string>(), request, intent, PreviewProof: previewProof);
            }
        }
        finally { source.OperationGate.Release(); }
        // No source operation gate is held across another worker's readiness callback.
        try
        {
            if (ticket.Request.PreparationKey is not null)
            {
                if (request.PreparationId is null) return Rejected();
                // Author code runs on its bounded async lane, without holding
                // the source operation gate needed by cancellation/lifecycle.
                var resolved = await ExecuteClientOperationAsync(source, (client, ct) => client.PrepareOutgoingIntentAsync(
                    ticket.Request, request.WorkerRun!.StartOrdinal, ct), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (resolved is null) return new(request.WidgetId, null, WidgetIntentLaunchKind.Rejected, [], PreparationFailed: true);
                lock (_gate)
                {
                    var expected = ticket.Request with { Payload = resolved.Payload, PreparationKey = null };
                    if (!IntentActionCurrent(ticket) || !expected.Matches(resolved) || !Declared(source, resolved)) return Rejected();
                    ticket = ticket with { Request = resolved, CreatedAt = IntentNow() };
                }
            }
            return await ResolveIntentAsync(ticket, cancellationToken).ConfigureAwait(false);
        }
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
        if (intent.ContractId == WidgetIntentContracts.OpenWebNotification || declaration is null || !intent.IsWellFormed()) return false;
        var contract = CompiledWidgetIntentContract.Create(declaration);
        return contract.SchemaDigest == intent.SchemaDigest && contract.Accepts(intent.Payload);
    }

    private bool IntentActionCurrent(IntentTicket ticket)
    {
        if (!IntentSourceCurrent(ticket)) return false;
        if (ticket.PreviewProof is not null) return IntentPreviewCurrent(ticket);
        if (ticket.Origin is null) return ticket.HostSettings || ticket.WebNotification is not null && WebNotificationIntentCurrent(ticket) || NotificationIntentCurrent(ticket);
        var source = _clients[ticket.SourceId];
        return source.FindInputOriginSnapshot(ticket.Origin.SnapshotSequence) is { } origin && source.CachedSnapshot is { } current &&
            ticket.Original.Matches(ResolveIntentSourceLocked(source, origin, current, ticket.Origin)) && Declared(source, ticket.Request);
    }

    private async Task<WidgetIntentPreparation> ResolveIntentAsync(IntentTicket ticket, CancellationToken token)
    {
        var intent = ticket.Request;
        if (intent.PreparationKey is not null) return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
        List<(IntentHandlerCandidate Candidate, string Fingerprint, bool Dynamic)> candidates;
        lock (_gate)
        {
            if (!IntentActionCurrent(ticket)) return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
            if (_intentTickets.Count + _activeIntentDeliveries.Count >= 16 && _intentTickets.Values.All(item => IntentFresh(item.CreatedAt, IntentNow())))
                return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
            var summaryAction = intent.ContractId == WidgetIntentContracts.InvokeSummaryAction;
            var destination = summaryAction ? SummaryDestination(ticket) : null;
            if (summaryAction && destination is null) return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
            var opensProvider = intent.ContractId == WidgetIntentContracts.OpenSummaryProvider;
            var package = opensProvider ? intent.Payload.GetProperty("packageId").GetString() : null;
            if (opensProvider && (intent.Presentation == WidgetIntentPresentation.PreferExistingSurface || !WidgetManifestValidator.IsValidPackageIdentity(package)))
                return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
            candidates = intent.Routing == WidgetIntentRouting.Windows ? [] : _catalog.IntentWidgets
                .Where(widget => (opensProvider ? widget.PackageId == package : !widget.IsStopped) && (!ticket.HostSettings || widget.Id == "settings") &&
                    (destination is null || MatchesDestination(widget, destination)) &&
                    (ticket.WebNotification is null || widget.Id == ticket.SourceId)).SelectMany(widget =>
                (widget.Intents?.Handles ?? []).Where(handler => handler.Id == intent.ContractId && handler.Version == intent.Version &&
                    (intent.Presentation != WidgetIntentPresentation.StayInSource || handler.SupportsBackgroundDelivery))
                .Select(handler => (new IntentHandlerCandidate(widget.Id, _catalogRevision + 1, CompiledWidgetIntentContract.Create(handler),
                    true, handler.SupportsPassiveDelivery), widget.CatalogFingerprint, handler.HasDynamicAvailability))).ToList();
        }
        if (candidates.Count > IntentResolutionPolicy.MaximumCandidates)
            return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
        // File sharing discovers declared destinations without waking every
        // potential receiver. The chosen target is presented before delivery;
        // its optional dynamic check and admission guards still run there.
        var discoverFromCatalog = intent.Presentation == WidgetIntentPresentation.StayInSource || intent.ContractId is WidgetIntentContracts.FileShare or WidgetIntentContracts.OpenSummaryProvider;
        var ready = new List<IntentHandlerCandidate>();
        foreach (var candidate in candidates)
        {
            if (!discoverFromCatalog && candidate.Dynamic && candidate.Candidate.Contract.SchemaDigest == intent.SchemaDigest)
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
            var declaration = ticket.WebNotification is not null ? WidgetIntentContracts.WebNotification :
                ticket.ManagedWidgetId is not null ? WidgetIntentContracts.WidgetSettings :
                _clients[ticket.SourceId].Configured.Intents!.Requests.Single(item => item.Id == intent.ContractId && item.Version == intent.Version);
            var resolution = IntentResolutionPolicy.Resolve(CompiledWidgetIntentContract.Create(declaration), intent.Payload, ready.ToArray(), routing: intent.Routing);
            var kind = resolution.Kind switch
            {
                IntentResolutionKind.Widget => WidgetIntentLaunchKind.Widget,
                IntentResolutionKind.ChooseHandler => WidgetIntentLaunchKind.ChooseHandler,
                IntentResolutionKind.ExternalBrowser => WidgetIntentLaunchKind.ExternalBrowser,
                IntentResolutionKind.Unavailable => WidgetIntentLaunchKind.Unavailable,
                _ => WidgetIntentLaunchKind.Rejected,
            };
            if (intent.Presentation == WidgetIntentPresentation.StayInSource && kind == WidgetIntentLaunchKind.ExternalBrowser)
                kind = WidgetIntentLaunchKind.Unavailable;
            prepared = new(ticket.SourceId, null, kind, [], intent.Presentation) { SourceReportsResult = intent.ReportsResult };
            if (kind is not (WidgetIntentLaunchKind.Unavailable or WidgetIntentLaunchKind.Rejected))
            {
                var now = IntentNow();
                foreach (var key in _intentTickets.Where(pair => !IntentFresh(pair.Value.CreatedAt, now)).Select(pair => pair.Key).ToArray())
                    _intentTickets.Remove(key);
                if (_intentTickets.Count + _activeIntentDeliveries.Count >= 16) return new(ticket.SourceId, null, WidgetIntentLaunchKind.Rejected, []);
                var targets = resolution.Candidates.ToDictionary(item => item.WidgetId, item => _catalog.GetConfigured(item.WidgetId).CatalogFingerprint, StringComparer.Ordinal);
                _intentTickets.Add(id, ticket with { Kind = kind, Targets = targets });
                return new(ticket.SourceId, id, kind, resolution.Candidates.Select(item => new WidgetIntentDestination(item.WidgetId,
                    _catalog.GetConfigured(item.WidgetId).Name, item.SupportsPassiveDelivery,
                    intent.ContractId == WidgetIntentContracts.OpenSummaryProvider && _catalog.GetConfigured(item.WidgetId).IsStopped)).ToArray(), intent.Presentation) { SourceReportsResult = intent.ReportsResult };
            }
        }
        var followUp = await ReportIntentAsync(id, ticket, prepared.Kind == WidgetIntentLaunchKind.Unavailable
            ? WidgetIntentStatus.Unavailable : WidgetIntentStatus.Rejected, token).ConfigureAwait(false);
        return followUp ?? prepared;
    }

    private async Task<WidgetIntentPreparation?> ReportIntentAsync(string id, IntentTicket ticket, WidgetIntentStatus status, CancellationToken token)
    {
        if (!ticket.Request.ReportsResult || ticket.SourceRun is null) return null;
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
        BrokerWidgetIdentity? fileShareSource = null;
        lock (_gate)
        {
            DemandNotDisposed();
            if (!IntentFresh(ticket.CreatedAt, IntentNow()) || !IntentSourceCurrent(ticket) || !IntentPreviewCurrent(ticket) || ticket.NotificationToken is not null && !NotificationIntentCurrent(ticket)) return new(false);
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
            if (ticket.Request.ContractId == WidgetIntentContracts.FileShare && ticket.Request.Version == 1)
            {
                var configured = _clients[ticket.SourceId].Configured;
                fileShareSource = new(configured.PackageId, configured.PublisherId, configured.InstanceId);
            }
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
                        if (!IntentSourceCurrent(ticket) || !IntentPreviewCurrent(ticket) || ticket.NotificationToken is not null && !NotificationIntentCurrent(ticket) || !IsCurrentLocked(target) || target.CachedWorkerRun != request.TargetWorkerRun ||
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
                        lock (_gate) if (!IntentSourceCurrent(ticket) || !IntentPreviewCurrent(ticket) ||
                            ticket.Request.ContractId == WidgetIntentContracts.InvokeSummaryAction &&
                            (SummaryDestination(ticket) is not { } destination || !MatchesDestination(target.Configured, destination))) return new(false);
                        // Only trusted, selected-target intent delivery can turn
                        // an opaque offer into recipient-scoped file references.
                        // Existing source/run/presentation checks remain in charge.
                        using var files = fileShareSource is null ? null : FileReferenceRegistry.Shared.AuthorizeShare(
                            fileShareSource, new(target.Configured.PackageId, target.Configured.PublisherId, target.Configured.InstanceId),
                            ticket.Request.Payload.GetProperty("offerId").GetString()!);
                        var wantsPin = ticket.Request.ContractId == WidgetIntentContracts.InvokeSummaryAction &&
                            ticket.Request.Payload.GetProperty("pin").GetBoolean();
                        WidgetSummaryExchange.ActionDelivery? deliveryGrant = null;
                        var wantsMicrophone = ticket.Request.ContractId == WidgetIntentContracts.InvokeSummaryAction &&
                            ticket.Request.Version == 2 && ticket.Request.Presentation == WidgetIntentPresentation.StayInSource &&
                            ticket.Request.Payload.GetProperty("microphone").GetBoolean();
                        if (wantsPin && wantsMicrophone) return new(false, Status: WidgetIntentStatus.Rejected);
                        if (wantsPin || wantsMicrophone)
                        {
                            if (request.PinnedTarget is not null || ticket.Request.Presentation == WidgetIntentPresentation.PreferExistingSurface)
                                return new(false, Status: WidgetIntentStatus.Rejected);
                            lock (_gate)
                            {
                                if (!IntentSourceCurrent(ticket) || wantsPin && !target.Configured.PinningSupported) return new(false);
                                var from = _clients[ticket.SourceId].Configured;
                                deliveryGrant = Summaries?.BeginDelivery(new(from.PackageId, from.PublisherId, from.InstanceId),
                                    new(target.Configured.PackageId, target.Configured.PublisherId, target.Configured.InstanceId),
                                    ticket.Request.Payload.GetProperty("reader").GetString()!, ticket.Request.Payload.GetProperty("token").GetString()!, wantsPin, request.TicketId);
                            }
                            if (deliveryGrant is null) return new(false);
                        }
                        using var delivery = deliveryGrant;
                        var result = await ExecuteClientOperationAsync(target,
                            (client, token) => client.DeliverIntentAsync(ticket.Request, request.TargetWorkerRun!.StartOrdinal, token), operation.Token)
                            .ConfigureAwait(false);
                        operation.Token.ThrowIfCancellationRequested();
                        if (!Enum.IsDefined(result)) return new(false);
                        var accepted = result == WidgetIntentResult.Accepted || result == WidgetIntentResult.InteractionRequired &&
                            ticket.Request.Presentation != WidgetIntentPresentation.StayInSource;
                        if (files is not null) accepted = files.Complete(accepted);
                        var requestedPin = accepted && wantsPin ? delivery?.Complete() : null;
                        if (wantsPin && requestedPin is null) return new(false, Status: WidgetIntentStatus.Rejected);
                        return new(accepted, RequestedPinLayout: requestedPin, BrowserFallbackUrl: accepted ? null : BrowserFallback(ticket.Request),
                            RequiresInteraction: accepted && result == WidgetIntentResult.InteractionRequired, Status: accepted ? WidgetIntentStatus.Accepted : WidgetIntentStatus.Rejected);
                    }
                    catch (BridgeWidgetRequestException)
                    { return new(false, BrowserFallbackUrl: BrowserFallback(ticket.Request)); }
                    catch (BrokerException error) when (fileShareSource is not null && error.Code == "file_limit")
                    { return new(false, Status: WidgetIntentStatus.Rejected, Failure: WidgetIntentFailure.FileCapacity); }
                    catch (BrokerException)
                    { return new(false, Status: WidgetIntentStatus.Rejected); }
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
            else if (cancelled is null && Guid.TryParseExact(request.TicketId, "N", out _))
            {
                RememberIntentPreparation(request.TicketId, request.WidgetId);
            }
        }
        await cancellation.ConfigureAwait(false);
        if (cancelled is not null)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await ReportIntentAsync(request.TicketId, cancelled, WidgetIntentStatus.Cancelled, cleanup.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    // Called under the registry gate both before and after waiting for the receiver.
    private void DemandBackgroundIntent(string ticketId, ConfiguredWidget target)
    {
        if (!_intentTickets.TryGetValue(ticketId, out var ticket) || !IntentFresh(ticket.CreatedAt, IntentNow()) ||
            ticket.Request.Presentation != WidgetIntentPresentation.StayInSource || !IntentActionCurrent(ticket) ||
            !ticket.Targets.TryGetValue(target.Id, out var fingerprint) || fingerprint != target.CatalogFingerprint ||
            target.IsStopped || target.Intents?.Handles.Any(h => h.SupportsBackgroundDelivery &&
                h.Id == ticket.Request.ContractId && h.Version == ticket.Request.Version) != true)
            throw new BridgeStaleControllerInputAuthorityException("The background action has expired.");
    }

    private static bool MatchesDestination(ConfiguredWidget widget, BrokerWidgetIdentity destination) =>
        widget.PackageId == destination.PackageId && widget.PublisherId == destination.PublisherId && widget.InstanceId == destination.InstanceId;

    private BrokerWidgetIdentity? SummaryDestination(IntentTicket ticket) =>
        _clients.TryGetValue(ticket.SourceId, out var source) && ticket.Request.Payload.TryGetProperty("reader", out var reader) &&
        reader.ValueKind == System.Text.Json.JsonValueKind.String && reader.GetString() is { } proof ?
            Summaries?.ActionDestination(new(source.Configured.PackageId, source.Configured.PublisherId, source.Configured.InstanceId), proof) : null;

    private bool IntentSourceCurrent(IntentTicket ticket) => !_disposed &&
        (ticket.Request.Presentation != WidgetIntentPresentation.StayInSource ||
            _clients.GetValueOrDefault(ticket.SourceId)?.HostLifecycle == WidgetLifecycleState.Interactive) && ManagedTargetCurrent(ticket) && WebNotificationIntentCurrent(ticket) &&
        _clients.TryGetValue(ticket.SourceId, out var source) && IsCurrentLocked(source) &&
        source.Configured.WorkerFingerprint == ticket.SourceFingerprint &&
        (ticket.HostSettings || ticket.NotificationToken is not null || ticket.WebNotification is not null
            ? ticket.HostSettings || source.Client.IsRunning && new BridgeWorkerRun(source.Generation, source.Client.Starts) == ticket.SourceRun
            : source.HasCurrentSnapshotWorker && source.CachedWorkerRun == ticket.SourceRun);

    private WidgetIntentRequest? ResolveIntentSourceLocked(ClientRegistration source, ViewSnapshot origin,
        ViewSnapshot current, BridgeIntentPrepareRequest request)
        => ResolveIntentSourceLocked(source, origin, current, request, out _);

    private bool IntentPreviewCurrent(IntentTicket ticket)
    {
        if (ticket.Request.Presentation == WidgetIntentPresentation.StayInSource && ticket.Request.ContractId != WidgetIntentContracts.OpenSummaryProvider && ticket.Origin is { } origin)
        {
            if (!_clients.TryGetValue(ticket.SourceId, out var caller) || caller.CachedSnapshot is not { } currentFrame) return false;
            var currentAction = ResolveIntentSourceLocked(caller, currentFrame, currentFrame, origin, out var currentProof);
            return currentProof == ticket.PreviewProof && ticket.Original.Matches(currentAction) && Declared(caller, ticket.Request);
        }
        if (ticket.PreviewProof is null) return true; // Preserve ordinary prepared-intent source-hide semantics.
        if (ticket.Origin is not { } request || !_clients.TryGetValue(ticket.SourceId, out var source) || source.CachedSnapshot is not { } current) return false;
        // Hold the admitted semantic proof, not an obsolete native preview or a
        // bounded snapshot-history slot. Unrelated publications may advance.
        var intent = ResolveIntentSourceLocked(source, current, current, request, out var proof);
        return proof == ticket.PreviewProof && ticket.Original.Matches(intent) && Declared(source, ticket.Request);
    }

    private WidgetIntentRequest? ResolveIntentSourceLocked(ClientRegistration source, ViewSnapshot origin,
        ViewSnapshot current, BridgeIntentPrepareRequest request, out string? previewProof)
    {
        previewProof = null;
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
                if (item is null) return null;
                var currentOwners = ResolveIndexedInputOwners(current, lease);
                var intent = IntentActionAuthority.RevalidateIndexed(
                    ResolveIndexedInputOwners(origin, lease), currentOwners, item.Root, request.Action, reference);
                if (intent is not null && (lease.Request.Range.Kind == IndexedCollectionRequestKind.FocusPreview || item.Root.Id != request.Action.SourceElementId))
                    previewProof = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        reference.LeaseId, reference.ItemKey,
                        Owners = currentOwners.Select(node => new { node.Id, node.Kind, node.InputScopeId, Entry = node.FocusPreview?.EnterButton }).ToArray(),
                    });
                return intent;
            }
            if (request.PinnedLayoutId is { } layout)
            {
                if (!source.Configured.PinningSupported ||
                    layout == PinnedSurfaceContract.FullWidgetLayoutId && !source.Configured.FullWidgetPinningSupported) return null;
                origin = PinnedActionContract.Project(origin, layout, inheritRootless: true);
                current = PinnedActionContract.Project(current, layout, inheritRootless: true);
            }
            var resolved = IntentActionAuthority.Revalidate(origin, current, request.Action);
            if (resolved is not null && InlinePreviewActionContract.Resolve(current, request.Action) is { } preview)
                previewProof = "inline:" + preview.Ancestry;
            return resolved;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or BridgeProtocolException or BridgeStaleIndexedInputAuthorityException)
        { return null; }
    }

    private static bool IntentTargetPresented(ClientRegistration target, IntentTicket ticket, BridgeIntentPinnedTarget? pin)
    {
        if (ticket.Request.Presentation == WidgetIntentPresentation.StayInSource)
            return pin is null && !target.Configured.IsStopped && target.Configured.Intents?.Handles.Any(h =>
                h.Id == ticket.Request.ContractId && h.Version == ticket.Request.Version && h.SupportsBackgroundDelivery) == true;
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
        request.Presentation != WidgetIntentPresentation.StayInSource && request.ContractId == WidgetIntentContracts.OpenWebPage && request.Version is 1 or 2
            ? request.Payload.GetProperty("url").GetString() : null;
}
