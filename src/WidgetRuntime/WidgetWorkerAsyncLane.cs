namespace WidgetRail.WidgetRuntime;

internal enum WorkerAsyncAdmission { Accepted, Duplicate, Full, Closed }

/// <summary>Owns bounded asynchronous worker reads, exact cancellation and shutdown drain.</summary>
internal sealed class WidgetWorkerAsyncLane<TKey, TRequest>(int capacity,
    Func<long, TRequest, CancellationToken, CancellationToken, Task> execute,
    Action<Exception> failed, CancellationToken sessionToken) where TKey : notnull
{
    private sealed class Operation(TRequest request, CancellationToken sessionToken)
    {
        internal readonly TRequest Request = request;
        internal readonly CancellationTokenSource Cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? CancellationCompletion;
        internal bool Completing;
    }
    private readonly object gate = new();
    private readonly Dictionary<TKey, Operation> operations = [];
    private Task? closeTask;
    private bool closed;

    internal WorkerAsyncAdmission TryStart(long requestId, TKey key, TRequest request)
    {
        lock (gate)
        {
            if (closed) return WorkerAsyncAdmission.Closed;
            if (operations.ContainsKey(key)) return WorkerAsyncAdmission.Duplicate;
            if (operations.Count >= capacity) return WorkerAsyncAdmission.Full;
            var operation = new Operation(request, sessionToken);
            operations.Add(key, operation);
            _ = Task.Run(() => RunAsync(requestId, key, operation));
            return WorkerAsyncAdmission.Accepted;
        }
    }

    internal bool Cancel(TKey key, Func<TRequest, bool> matches)
    {
        lock (gate)
        {
            if (!operations.TryGetValue(key, out var operation) || operation.Completing || !matches(operation.Request)) return false;
            CancelLocked(operation);
            return true;
        }
    }

    private static void CancelLocked(Operation operation)
    {
        if (!operation.Completing) operation.CancellationCompletion ??= operation.Cancellation.CancelAsync();
    }

    internal async Task<bool> CancelAndDrainAsync(TKey key)
    {
        Task finished;
        lock (gate)
        {
            if (!operations.TryGetValue(key, out var operation)) return false;
            CancelLocked(operation);
            finished = operation.Finished.Task;
        }
        await finished.ConfigureAwait(false);
        return true;
    }

    internal Task CloseAsync()
    {
        lock (gate)
        {
            if (closeTask is not null) return closeTask;
            closed = true;
            foreach (var operation in operations.Values) CancelLocked(operation);
            return closeTask = Task.WhenAll(operations.Values.Select(operation => operation.Finished.Task)).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private async Task RunAsync(long requestId, TKey key, Operation operation)
    {
        try { await execute(requestId, operation.Request, operation.Cancellation.Token, sessionToken).ConfigureAwait(false); }
        catch (Exception error) { if (!sessionToken.IsCancellationRequested) failed(error); }
        finally
        {
            Task? cancellation;
            lock (gate) { operation.Completing = true; cancellation = operation.CancellationCompletion; }
            try { if (cancellation is not null) await cancellation.ConfigureAwait(false); }
            catch (Exception error) { if (!sessionToken.IsCancellationRequested) failed(error); }
            finally
            {
                lock (gate)
                {
                    operation.Cancellation.Dispose();
                    operations.Remove(key);
                    operation.Finished.TrySetResult();
                }
            }
        }
    }
}
