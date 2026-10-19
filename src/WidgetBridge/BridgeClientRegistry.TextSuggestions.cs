using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

internal sealed partial class BridgeClientRegistry
{
    private sealed class SuggestionRead(BridgeTextSuggestionsRequest request, CancellationToken token)
    {
        internal readonly BridgeTextSuggestionsRequest Request = request;
        internal readonly CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        internal Task? CancellationCompletion;
        internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly Dictionary<ClientRegistration, SuggestionRead> suggestionReads = [];

    internal async Task CancelTextSuggestionsAsync(BridgeTextSuggestionsRequest request)
    {
        SuggestionRead? active;
        Task? cancellation;
        lock (_gate)
        {
            active = suggestionReads.Values.FirstOrDefault(read => read.Request == request);
            cancellation = active is null ? null : active.CancellationCompletion ??= active.Cancellation.CancelAsync();
        }
        if (active is null) return;
        if (cancellation is not null) await cancellation.ConfigureAwait(false);
        await active.Finished.Task.ConfigureAwait(false);
    }

    internal async Task<BridgeClientPublication<TextSuggestionResult>> SuggestTextAsync(BridgeTextSuggestionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!BridgeRequestKey.IsBoundedIdentifier(request.WidgetId) || !TextSuggestionContract.IsValid(request.Query) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.RuntimeGeneration) || !BridgeRequestKey.IsBoundedIdentifier(request.PresentationGeneration))
            throw new BridgeProtocolException("Invalid text suggestion request.");
        ClientRegistration registration;
        BridgeClientPublication<ClientRegistration>? lease;
        int start;
        ViewNode declaration;
        SuggestionRead operation;
        lock (_gate)
        {
            DemandNotDisposed();
            if (!_clients.TryGetValue(request.WidgetId, out registration!)) throw new BridgeProtocolException("Suggestion owner is unavailable.");
            declaration = DemandSuggestionOwnerLocked(registration, request);
            if (suggestionReads.Count >= 8 || suggestionReads.ContainsKey(registration))
                return AdmitPublicationLocked(registration, TextSuggestionResult.NotAvailable);
            start = registration.Client.Starts;
            lease = AdmitPublicationLocked(registration, registration);
            operation = new(request, cancellationToken);
            suggestionReads.Add(registration, operation);
        }
        try
        {
            var result = await registration.Client.SuggestTextAsync(request.Query, start, operation.Cancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                var current = DemandSuggestionOwnerLocked(registration, request);
                if (registration.Client.Starts != start || current.TextEntryValue != declaration.TextEntryValue ||
                    current.ActionId != declaration.ActionId || current.TextEntryMaximumLength != declaration.TextEntryMaximumLength ||
                    current.TextEntrySuggestions != declaration.TextEntrySuggestions)
                    throw new BridgeProtocolException("Suggestion owner changed.");
                if (!TextSuggestionContract.IsValid(result, request.Query.MaximumResults, current.TextEntryMaximumLength!.Value))
                    result = TextSuggestionResult.NotAvailable;
                var publication = lease.Map(_ => result);
                lease = null;
                return publication;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Superseded typing and the worker RPC's own deadline cancel only
            // this optional read. Never let them become a fatal session fault.
            throw new BridgeProtocolException("Text suggestion request was cancelled or timed out.");
        }
        finally
        {
            Task? cancellation;
            lock (_gate) { suggestionReads.Remove(registration); cancellation = operation.CancellationCompletion; }
            try { if (cancellation is not null) await cancellation.ConfigureAwait(false); }
            finally
            {
                lease?.Dispose();
                operation.Cancellation.Dispose();
                operation.Finished.TrySetResult();
            }
        }
    }

    private ViewNode DemandSuggestionOwnerLocked(ClientRegistration registration, BridgeTextSuggestionsRequest request)
    {
        if (!IsCurrentLocked(registration) || !registration.Client.IsRunning || !registration.HasCurrentSnapshotWorker ||
            registration.HostLifecycle != WidgetLifecycleState.Interactive ||
            !MatchesArtworkOrigin(registration.Configured, request.RuntimeGeneration, request.PresentationGeneration) ||
            registration.CachedSnapshot is not { } snapshot)
            throw new BridgeProtocolException("Suggestion presentation retired.");
        var node = Find(snapshot.Root, false);
        if (!TextSuggestionContract.Matches(node, request.Query)) throw new BridgeProtocolException("Suggestion field is unavailable.");
        return node!;
        ViewNode? Find(ViewNode current, bool active)
        {
            if (current.IsDisabled == true || current.IsBusy == true) return null;
            if (current.InputScopeId is { } scope) active = scope == snapshot.ActiveInputScopeId;
            else if (ReferenceEquals(current, snapshot.Root)) active = current.Id == snapshot.ActiveInputScopeId;
            if (active && current.Id == request.Query.FieldId) return current;
            foreach (var child in current.Children) if (Find(child, active) is { } found) return found;
            return null;
        }
    }
}
