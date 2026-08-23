using System.Collections.Concurrent;

namespace WidgetRail.WidgetRuntime;

internal sealed class WidgetPendingRequests
{
    private readonly ConcurrentDictionary<long, PendingOperation> _pending = [];
    private long _nextRequestId;

    internal int Count => _pending.Count;

    internal WidgetPendingRequest Register(string requestType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestType);
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<RuntimeEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, new(requestType, completion)))
            throw new InvalidOperationException("Duplicate request ID.");
        return new WidgetPendingRequest(this, requestId, completion.Task);
    }

    internal bool TryComplete(RuntimeEnvelope response)
    {
        if (response.RequestId <= 0 ||
            !_pending.TryRemove(response.RequestId, out var pending))
            return false;

        if (response.Type == MessageTypes.Error)
        {
            var error = RuntimeJson.FromElement<ErrorPayload>(response.Payload);
            pending.Completion.TrySetException(new WidgetProcessException(
                pending.RequestType, error.Code, error.Message));
        }
        else
        {
            pending.Completion.TrySetResult(response);
        }
        return true;
    }

    internal void FailAll(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var entry in _pending.ToArray())
        {
            if (_pending.TryRemove(entry.Key, out var pending))
                pending.Completion.TrySetException(exception);
        }
    }

    private void Remove(long requestId) => _pending.TryRemove(requestId, out _);

    private sealed record PendingOperation(
        string RequestType,
        TaskCompletionSource<RuntimeEnvelope> Completion);

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
