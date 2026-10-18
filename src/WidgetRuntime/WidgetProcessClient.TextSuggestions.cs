using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetRuntime;

public sealed partial class WidgetProcessClient
{
    internal async Task<TextSuggestionResult> SuggestTextAsync(TextSuggestionQuery query, int expectedStartOrdinal,
        CancellationToken cancellationToken)
    {
        if (!TextSuggestionContract.IsValid(query)) throw new ArgumentException("Invalid suggestion query.");
        var session = Volatile.Read(ref _session);
        if (session is not { IsRunning: true } || _disposed || _stopping || Starts != expectedStartOrdinal)
            return TextSuggestionResult.NotAvailable;
        // Never start/restart a worker for an optional suggestion request.
        using var pending = session.PendingRequests.Register(MessageTypes.SuggestText);
        using (var write = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken))
        {
            write.CancelAfter(_options.RequestTimeout);
            await session.WriteAsync(new() { Type = MessageTypes.SuggestText, RequestId = pending.RequestId, Payload = RuntimeJson.ToElement(query) }, write.Token).ConfigureAwait(false);
        }
        RuntimeEnvelope response;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.CancellationToken);
        deadline.CancelAfter(_options.RequestTimeout);
        try { response = await pending.Response.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            using var cancelDeadline = CancellationTokenSource.CreateLinkedTokenSource(session.CancellationToken);
            cancelDeadline.CancelAfter(TimeSpan.FromSeconds(3));
            using var cancel = session.PendingRequests.Register(MessageTypes.CancelTextSuggestions);
            try
            {
                await session.WriteAsync(new() { Type = MessageTypes.CancelTextSuggestions, RequestId = cancel.RequestId, Payload = RuntimeJson.ToElement(query) }, cancelDeadline.Token).ConfigureAwait(false);
                await cancel.Response.WaitAsync(cancelDeadline.Token).ConfigureAwait(false);
                // Preserve correlation until the worker's bounded terminal reply, even if its author callback is quarantined.
                await pending.Response.WaitAsync(cancelDeadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { session.Terminate(); }
            throw;
        }
        if (!ReferenceEquals(Volatile.Read(ref _session), session) || Starts != expectedStartOrdinal ||
            response.Type != MessageTypes.TextSuggestions) return TextSuggestionResult.NotAvailable;
        var result = RuntimeJson.FromElement<TextSuggestionResult>(response.Payload);
        return TextSuggestionContract.IsValid(result, query.MaximumResults, TextSuggestionContract.MaximumQueryLength)
            ? result : TextSuggestionResult.NotAvailable;
    }
}
