using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed record TextSuggestionView(long Revision, string Text, IReadOnlyList<TextSuggestion> Items,
    bool Loading = false, bool Unavailable = false, bool Queried = false, bool Stale = false)
{
    internal bool Selectable => Queried && !Loading && !Unavailable && !Stale;
}

/// <summary>One active query and one replaceable pending value, with bounded field timing and host-owned focus.</summary>
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
    private CancellationTokenSource? pendingDeadline;
    private Task? pump;
    private Work? pending;
    private TextSuggestionView view = new(0, "", []);
    private long revision, lastStart;
    private bool running, disposed;

    internal TextSuggestionController(string fieldId, TextSuggestionSource source, int maximumLength,
        Func<TextSuggestionQuery, CancellationToken, Task<TextSuggestionResult>> query, Action<TextSuggestionView> publish,
        int? debounce = null, int? interval = null, int? timeout = null)
    {
        if (!TextSuggestionContract.IsValid(source)) throw new ArgumentException("Invalid suggestion options.", nameof(source));
        this.fieldId = fieldId; this.source = source; this.maximumLength = maximumLength;
        this.query = query; this.publish = publish;
        this.debounce = debounce ?? source.DebounceMilliseconds;
        this.interval = interval ?? source.MinimumRequestIntervalMilliseconds;
        this.timeout = timeout ?? source.TimeoutMilliseconds + TextSuggestionContract.TransportGraceMilliseconds;
    }

    internal bool IsCurrent(TextSuggestionView state) { lock (gate) return !disposed && ReferenceEquals(view, state); }
    internal Task Completion { get { lock (gate) return pump ?? Task.CompletedTask; } }

    internal void Update(string text, bool force = false)
    {
        TextSuggestionView state;
        lock (gate)
        {
            if (disposed || !force && revision > 0 && view.Text == text) return;
            CancelDemand();
            var retained = view.Items;
            ++revision;
            pending = text.Length >= source.MinimumLength && text.Length <= Math.Min(maximumLength, TextSuggestionContract.MaximumQueryLength)
                ? new(revision, text, Environment.TickCount64) : null;
            state = view = new(revision, text, pending is null ? [] : retained, Stale: pending is not null && retained.Count > 0);
            RetireDeadline();
            if (pending is not null)
            {
                pendingDeadline = new();
                _ = ExpirePendingAsync(pending, pendingDeadline.Token);
            }
            if (!running && pending is not null) { running = true; pump = Task.Run(PumpAsync); }
        }
        Publish(state);
    }

    internal string? Accept(long expectedRevision, string id)
    {
        TextSuggestionView state;
        string value;
        lock (gate)
        {
            if (disposed || expectedRevision != revision || !view.Selectable || view.Items.FirstOrDefault(item => item.Id == id) is not { } item) return null;
            value = item.Value;
            CancelDemand(); pending = null;
            RetireDeadline();
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
                    RetireDeadline(); // Once dispatched, the request has its own full provider deadline.
                    demand = request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    loading = view = new(work.Revision, work.Text, view.Items, Loading: true, Stale: view.Items.Count > 0);
                }
                Publish(loading);
                Task<TextSuggestionResult>? operation = null;
                try
                {
                    operation = query(new(fieldId, source.Key, sessionId, work.Revision, work.Text, source.MaximumResults, Math.Min(maximumLength, TextSuggestionContract.MaximumQueryLength))
                        { TimeoutMilliseconds = source.TimeoutMilliseconds }, request.Token);
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
                if (!disposed && pending is not null) { running = true; pump = Task.Run(PumpAsync); }
                if (disposed) lifetime.Dispose();
            }
        }
    }

    private void Complete(Work work, TextSuggestionResult result)
    {
        TextSuggestionView state;
        lock (gate)
        {
            if (disposed || work.Revision != revision || view.Queried) return;
            RetireDeadline();
            state = view = new(work.Revision, work.Text, result.Items.ToArray(), Unavailable: result.Unavailable, Queried: true);
        }
        Publish(state);
    }

    private void Publish(TextSuggestionView state)
    {
        // The host callback only posts/applies a bounded UI update. Serialize it
        // with retirement so Dispose cannot return before a new callback starts.
        lock (gate)
            if (!disposed && ReferenceEquals(view, state)) publish?.Invoke(state);
    }

    private void CancelDemand()
    {
        if (demand is { IsCancellationRequested: false }) ObserveCancellation(demand.CancelAsync());
    }

    // A provider ignoring cancellation can occupy the one query slot. Bound
    // the latest pending display too, without spawning more provider calls.
    private async Task ExpirePendingAsync(Work work, CancellationToken token)
    {
        try { await Task.Delay(Math.Max(debounce, interval) + timeout, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        TextSuggestionView state;
        lock (gate)
        {
            if (disposed || token.IsCancellationRequested || work.Revision != revision || view.Queried) return;
            CancelDemand();
            state = view = new(revision, work.Text, [], Unavailable: true, Queried: true);
            RetireDeadline();
        }
        Publish(state);
    }

    private void RetireDeadline()
    {
        pendingDeadline?.Cancel(); pendingDeadline?.Dispose(); pendingDeadline = null;
    }
    private static void ObserveCancellation(Task task) => _ = task.ContinueWith(done => { _ = done.Exception; },
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; revision++; pending = null; publish = null;
            RetireDeadline();
            CancelDemand(); ObserveCancellation(lifetime.CancelAsync());
            if (!running) lifetime.Dispose();
        }
    }
}
