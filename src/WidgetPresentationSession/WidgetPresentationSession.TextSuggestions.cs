using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public async Task<TextSuggestionResult> SuggestTextAsync(WidgetPresentationFrame displayed, TextSuggestionQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TextSuggestionContract.IsValid(query)) throw new ArgumentException("Invalid text suggestion query.");
        void Demand()
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, displayed.Snapshot.ActiveInputScopeId);
            var oldPath = OrdinaryPath(OrdinaryScope(displayed.Snapshot), query.FieldId);
            var newPath = OrdinaryPath(OrdinaryScope(current.Snapshot), query.FieldId);
            var original = oldPath[^1]; var latest = newPath[^1];
            if (oldPath.Concat(newPath).Any(node => node.IsDisabled == true || node.IsBusy == true) ||
                !TextSuggestionContract.Matches(original, query) || !TextSuggestionContract.Matches(latest, query) ||
                Identity(oldPath) != Identity(newPath) || original.ActionId != latest.ActionId ||
                original.TextEntryValue != latest.TextEntryValue || original.TextEntryMaximumLength != latest.TextEntryMaximumLength ||
                original.TextEntrySuggestions != latest.TextEntrySuggestions)
                throw OrdinaryInputStale("The text suggestion field changed.");
        }
        using (_gate.Enter()) Demand();
        var request = new BridgeTextSuggestionsRequest(displayed.Authority.WidgetId, displayed.Authority.RuntimeGeneration,
            displayed.Authority.PresentationGeneration, query);
        BridgeEnvelope response;
        try { response = await RequestAsync(BridgeMessageTypes.SuggestText, request, BridgeMessageTypes.TextSuggestions, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var cancelDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await RequestAsync(BridgeMessageTypes.CancelTextSuggestions, request, BridgeMessageTypes.Acknowledged, cancelDeadline.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { }
            throw;
        }
        using (_gate.Enter()) Demand();
        var result = BridgeJson.FromElement<TextSuggestionResult>(response.Payload);
        return TextSuggestionContract.IsValid(result, query.MaximumResults, TextSuggestionContract.MaximumQueryLength)
            ? result : TextSuggestionResult.NotAvailable;
    }
}
