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
            pending.Completion.TrySetException(new WidgetRequestRejectedException(
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

internal sealed class WidgetRequestRejectedException : Exception
{
    internal WidgetRequestRejectedException(
        string requestType,
        string errorCode,
        string safeMessage) : base(Format(requestType, errorCode, safeMessage))
    {
        RequestType = ValidateToken(requestType, nameof(requestType));
        ErrorCode = ValidateToken(errorCode, nameof(errorCode));
        WorkerSafeMessage = ValidateMessage(safeMessage);
    }

    internal string RequestType { get; }
    internal string ErrorCode { get; }
    internal string WorkerSafeMessage { get; }

    private static string Format(string requestType, string errorCode, string safeMessage) =>
        $"Worker rejected request '{ValidateToken(requestType, nameof(requestType))}' " +
        $"({ValidateToken(errorCode, nameof(errorCode))}): {ValidateMessage(safeMessage)}";

    private static string ValidateToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.'))
            throw new WidgetProtocolViolationException(
                $"Worker error {parameterName} is invalid.");
        return value;
    }

    private static string ValidateMessage(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
            value.Any(character => char.IsControl(character) && character is not '\t'))
            throw new WidgetProtocolViolationException(
                "Worker error message is invalid.");
        return value;
    }
}
