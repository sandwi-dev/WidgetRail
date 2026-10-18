using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>One author callback at a time, independent of the action lane, including callbacks that ignore cancellation.</summary>
internal sealed class WidgetTextSuggestionLane
{
    private readonly Widget widget;
    private readonly Func<RuntimeEnvelope, CancellationToken, Task> send;
    private readonly WidgetWorkerAsyncLane<string, TextSuggestionQuery> lane;

    internal WidgetTextSuggestionLane(Widget widget, Func<RuntimeEnvelope, CancellationToken, Task> send,
        Action<Exception> failed, CancellationToken session)
    {
        this.widget = widget; this.send = send;
        lane = new(1, RunAsync, failed, session);
    }

    internal bool TryRead(long id, TextSuggestionQuery query)
    {
        if (!TextSuggestionContract.IsValid(query)) throw new ArgumentException("Invalid suggestion query.");
        return lane.TryStart(id, query.SessionId + ":" + query.Revision, query) == WorkerAsyncAdmission.Accepted;
    }

    internal bool Cancel(TextSuggestionQuery query) => TextSuggestionContract.IsValid(query) &&
        lane.Cancel(query.SessionId + ":" + query.Revision, active => active == query);

    internal Task CloseAsync() => lane.CloseAsync();

    private async Task RunAsync(long id, TextSuggestionQuery query, CancellationToken demand, CancellationToken session)
    {
        using var providerCancellation = new CancellationTokenSource();
        Task cancellation = Task.CompletedTask;
        var cancellationGate = new object();
        void CancelProvider()
        {
            lock (cancellationGate)
                if (!providerCancellation.IsCancellationRequested) cancellation = providerCancellation.CancelAsync();
        }
        using var registration = demand.Register(CancelProvider);
        // The extra task also bounds callbacks that block before returning their ValueTask.
        var provider = Task.Run(async () =>
        {
            var authored = await widget.OnSuggestTextAsync(query, providerCancellation.Token).ConfigureAwait(false);
            if (authored?.Items is null) return TextSuggestionResult.NotAvailable;
            var count = authored.Items.Count;
            if (count < 0 || count > query.MaximumResults) return TextSuggestionResult.NotAvailable;
            var copy = new TextSuggestion[count];
            for (var i = 0; i < copy.Length; i++) copy[i] = authored.Items[i];
            var snapshot = authored with { Items = copy };
            return TextSuggestionContract.IsValid(snapshot, query.MaximumResults, query.MaximumValueLength) ? snapshot : TextSuggestionResult.NotAvailable;
        }, CancellationToken.None);
        TextSuggestionResult result;
        try
        {
            // The deadline must not depend on author cancellation callbacks returning.
            result = await provider.WaitAsync(TimeSpan.FromMilliseconds(query.TimeoutMilliseconds), demand).ConfigureAwait(false);
            if (!TextSuggestionContract.IsValid(result, query.MaximumResults, TextSuggestionContract.MaximumQueryLength))
                result = TextSuggestionResult.NotAvailable;
            else result = result with { Items = result.Items.ToArray() };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { CancelProvider(); result = TextSuggestionResult.NotAvailable; }
        try
        {
            if (!session.IsCancellationRequested)
                await send(new() { Type = MessageTypes.TextSuggestions, RequestId = id, Payload = RuntimeJson.ToElement(result) }, session).ConfigureAwait(false);
        }
        finally
        {
            // Reply promptly, but retain the capacity until the actual callback drains.
            // A broken provider therefore consumes at most one slot/thread per worker.
            try { await provider.ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { }
            // Retain capacity for blocking/throwing cancellation callbacks as well.
            registration.Dispose();
            Task drain;
            lock (cancellationGate) drain = cancellation;
            try { await drain.ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }
    }
}
