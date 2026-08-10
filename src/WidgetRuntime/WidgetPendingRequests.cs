using System.Collections.Concurrent;

namespace GameBarAlternative.WidgetRuntime;

internal sealed class WidgetPendingRequests
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<RuntimeEnvelope>> _pending = [];
    private long _nextRequestId;

    internal int Count => _pending.Count;

    internal WidgetPendingRequest Register()
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<RuntimeEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
            throw new InvalidOperationException("Duplicate request ID.");
        return new WidgetPendingRequest(this, requestId, completion.Task);
    }

    internal bool TryComplete(RuntimeEnvelope response)
    {
        if (response.RequestId <= 0 ||
            !_pending.TryRemove(response.RequestId, out var completion))
            return false;

        if (response.Type == MessageTypes.Error)
        {
            var error = RuntimeJson.FromElement<ErrorPayload>(response.Payload);
            completion.TrySetException(new WidgetProcessException(
                $"Worker rejected the request ({error.Code}): {error.Message}"));
        }
        else
        {
            completion.TrySetResult(response);
        }
        return true;
    }

    internal void FailAll(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var entry in _pending.ToArray())
        {
            if (_pending.TryRemove(entry.Key, out var completion))
                completion.TrySetException(exception);
        }
    }

    private void Remove(long requestId) => _pending.TryRemove(requestId, out _);

    internal sealed class WidgetPendingRequest(
        WidgetPendingRequests owner,
        long requestId,
        Task<RuntimeEnvelope> response) : IDisposable
    {
        private WidgetPendingRequests? _owner = owner;

        internal long RequestId { get; } = requestId;
        internal Task<RuntimeEnvelope> Response { get; } = response;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Remove(RequestId);
    }
}
