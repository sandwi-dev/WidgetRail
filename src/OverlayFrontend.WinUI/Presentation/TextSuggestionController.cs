using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed record TextSuggestionView(long Revision, string Text, IReadOnlyList<TextSuggestion> Items,
    bool Loading = false, bool Unavailable = false, bool Queried = false);

/// <summary>One active query and one replaceable pending value. No widget can change host pacing or focus.</summary>
internal sealed class TextSuggestionController : IDisposable
{
    private sealed record Work(long Revision, string Text, long ChangedAt);
    private readonly object gate = new();
    private readonly TextSuggestionSource source;
    private readonly int maximumLength, debounce, interval, timeout;
    private readonly Func<TextSuggestionQuery, CancellationToken, Task<TextSuggestionResult>> query;
    private Action<TextSuggestionView>? publish;
    private readonly string fieldId, sessionId = Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? demand;
    private Work? pending;
    private TextSuggestionView view = new(0, "", []);
    private long revision, lastStart;
    private bool running, disposed;

    internal TextSuggestionController(string fieldId, TextSuggestionSource source, int maximumLength,
        Func<TextSuggestionQuery, CancellationToken, Task<TextSuggestionResult>> query, Action<TextSuggestionView> publish,
        int debounce = TextSuggestionContract.DebounceMilliseconds,
        int interval = TextSuggestionContract.MinimumRequestIntervalMilliseconds,
        int timeout = TextSuggestionContract.TimeoutMilliseconds + 1000)
    {
        this.fieldId = fieldId; this.source = source; this.maximumLength = maximumLength;
        this.query = query; this.publish = publish; this.debounce = debounce; this.interval = interval; this.timeout = timeout;
    }

    internal bool IsCurrent(long value) { lock (gate) return !disposed && revision == value; }

    internal void Update(string text)
    {
        TextSuggestionView state;
        lock (gate)
        {
            if (disposed || revision > 0 && view.Text == text) return;
            CancelDemand();
            state = view = new(++revision, text, []);
            pending = text.Length >= source.MinimumLength && text.Length <= Math.Min(maximumLength, TextSuggestionContract.MaximumQueryLength)
                ? new(revision, text, Environment.TickCount64) : null;
            if (!running && pending is not null) { running = true; _ = Task.Run(PumpAsync); }
        }
        Publish(state);
    }

    internal string? Accept(long expectedRevision, string id)
    {
        TextSuggestionView state;
        string value;
        lock (gate)
        {
            if (disposed || expectedRevision != revision || view.Loading || view.Items.FirstOrDefault(item => item.Id == id) is not { } item) return null;
            value = item.Value;
            CancelDemand(); pending = null;
            state = view = new(++revision, value, []);
        }
        Publish(state);
        return value;
    }

    private async Task PumpAsync()
    {
        try
        {
            while (true)
            {
                Work work;
                int wait;
                lock (gate)
                {
                    if (disposed || pending is null) return;
                    work = pending;
                    wait = (int)Math.Max(0, Math.Max(work.ChangedAt + debounce, lastStart + interval) - Environment.TickCount64);
                }
                if (wait > 0) await Task.Delay(wait, lifetime.Token).ConfigureAwait(false);
                CancellationTokenSource request;
                TextSuggestionView loading;
                lock (gate)
                {
                    if (disposed) return;
                    if (pending != work || revision != work.Revision) continue;
                    pending = null; lastStart = Environment.TickCount64;
                    demand = request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    loading = view = new(work.Revision, work.Text, [], Loading: true);
                }
                Publish(loading);
                Task<TextSuggestionResult>? operation = null;
                try
                {
                    operation = query(new(fieldId, source.Key, sessionId, work.Revision, work.Text, source.MaximumResults, Math.Min(maximumLength, TextSuggestionContract.MaximumQueryLength)), request.Token);
                    var result = await operation.WaitAsync(TimeSpan.FromMilliseconds(timeout), lifetime.Token).ConfigureAwait(false);
                    if (!TextSuggestionContract.IsValid(result, source.MaximumResults, maximumLength)) result = TextSuggestionResult.NotAvailable;
                    Complete(work, result);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    lock (gate) CancelDemand();
                    Complete(work, TextSuggestionResult.NotAvailable);
                }
                finally
                {
                    // A misbehaving callback cannot create an unbounded trail of abandoned queries.
                    if (operation is not null)
                        try { await operation.WaitAsync(lifetime.Token).ConfigureAwait(false); }
                        catch (Exception error) when (error is not OutOfMemoryException)
                        {
                            _ = operation.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        }
                    lock (gate) { if (ReferenceEquals(demand, request)) demand = null; }
                    request.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            lock (gate)
            {
                running = false;
                if (!disposed && pending is not null) { running = true; _ = Task.Run(PumpAsync); }
                if (disposed) lifetime.Dispose();
            }
        }
    }

    private void Complete(Work work, TextSuggestionResult result)
    {
        TextSuggestionView state;
        lock (gate)
        {
            if (disposed || work.Revision != revision) return;
            state = view = new(work.Revision, work.Text, result.Items.ToArray(), Unavailable: result.Unavailable, Queried: true);
        }
        Publish(state);
    }

    private void Publish(TextSuggestionView state)
    {
        Action<TextSuggestionView>? callback;
        lock (gate) callback = !disposed && revision == state.Revision ? publish : null;
        callback?.Invoke(state);
    }

    private void CancelDemand()
    {
        if (demand is { IsCancellationRequested: false }) ObserveCancellation(demand.CancelAsync());
    }
    private static void ObserveCancellation(Task task) => _ = task.ContinueWith(done => { _ = done.Exception; },
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; revision++; pending = null; publish = null;
            CancelDemand(); ObserveCancellation(lifetime.CancelAsync());
            if (!running) lifetime.Dispose();
        }
    }
}
