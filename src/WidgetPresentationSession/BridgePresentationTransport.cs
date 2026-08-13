using System.IO.Pipes;
using GameBarAlternative.WidgetBridge;

namespace GameBarAlternative.WidgetPresentationSession;

internal sealed class BridgePresentationTransport : IAsyncDisposable
{
    private static readonly TimeSpan StopDeadline = TimeSpan.FromSeconds(2);
    private readonly NamedPipeClientStream _pipe;
    private readonly BridgeFrameChannel _channel;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _pendingCapacity;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Dictionary<long, TaskCompletionSource<BridgeEnvelope>> _pending = [];
    private Task? _receiveLoop;
    private long _requestId;
    private Exception? _terminalFailure;
    private bool _disposed;

    private BridgePresentationTransport(
        NamedPipeClientStream pipe,
        BridgeFrameChannel channel,
        int maximumPendingRequests,
        long initialRequestId)
    {
        _pipe = pipe;
        _channel = channel;
        _requestId = initialRequestId;
        _pendingCapacity = new SemaphoreSlim(
            maximumPendingRequests, maximumPendingRequests);
    }

    internal event Action<BridgeEnvelope>? EventReceived;
    internal event Action<Exception>? Failed;

    internal static async Task<BridgePresentationTransport> ConnectAsync(
        string pipeName,
        WidgetPresentationSessionOptions options,
        CancellationToken cancellationToken)
    {
        ValidatePipeName(pipeName);
        var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(options.ConnectTimeout);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            var channel = new BridgeFrameChannel(pipe, options.MaximumMessageBytes);
            const long helloId = 1;
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.Hello,
                RequestId = helloId,
                Payload = BridgeJson.ToElement(new BridgeHello(options.ClientName)),
            }, deadline.Token).ConfigureAwait(false);
            var hello = await channel.ReadAsync(deadline.Token).ConfigureAwait(false);
            if (hello.RequestId != helloId || hello.Type != BridgeMessageTypes.HelloAccepted ||
                hello.Payload.ValueKind != System.Text.Json.JsonValueKind.Object ||
                hello.Payload.EnumerateObject().Any())
                throw new BridgeProtocolException(
                    "WidgetBridge rejected the presentation-session handshake.");
            return new BridgePresentationTransport(
                pipe, channel, options.MaximumPendingRequests, helloId);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal void Start()
    {
        if (_receiveLoop is not null)
            throw new InvalidOperationException("The bridge receive loop is already running.");
        _receiveLoop = ReceiveLoopAsync();
    }

    internal async Task<BridgeEnvelope> RequestAsync<T>(
        string type,
        T payload,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _pendingCapacity.WaitAsync(cancellationToken).ConfigureAwait(false);
        var requestId = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<BridgeEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            lock (_gate)
            {
                ThrowIfTerminalLocked();
                _pending.Add(requestId, completion);
            }
        }
        catch
        {
            _pendingCapacity.Release();
            throw;
        }

        try
        {
            await _writeGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                await _channel.WriteAsync(new BridgeEnvelope
                {
                    Type = type,
                    RequestId = requestId,
                    Payload = BridgeJson.ToElement(payload),
                }, _lifetime.Token).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailTerminal(exception, notify: true);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_terminalFailure is null)
        {
            try
            {
                using var stop = new CancellationTokenSource(StopDeadline);
                var response = await RequestAsync(
                    BridgeMessageTypes.Stop,
                    new { },
                    stop.Token).ConfigureAwait(false);
                if (response.Type != BridgeMessageTypes.Acknowledged)
                    throw new BridgeProtocolException(
                        "WidgetBridge returned an invalid stop response.");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failed?.Invoke(new WidgetPresentationSessionException(
                    "stop_incomplete", "The WidgetBridge stop handshake did not complete.", exception));
            }
        }

        _disposed = true;
        _lifetime.Cancel();
        await _pipe.DisposeAsync().ConfigureAwait(false);
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
        FailTerminal(new ObjectDisposedException(nameof(BridgePresentationTransport)), notify: false);
        _lifetime.Dispose();
        _writeGate.Dispose();
        _pendingCapacity.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var envelope = await _channel.ReadAsync(_lifetime.Token).ConfigureAwait(false);
                if (envelope.RequestId == 0)
                {
                    EventReceived?.Invoke(envelope);
                    continue;
                }
                TaskCompletionSource<BridgeEnvelope>? completion;
                lock (_gate)
                {
                    if (_pending.Remove(envelope.RequestId, out completion))
                        _pendingCapacity.Release();
                }
                if (completion is null || !completion.TrySetResult(envelope))
                    throw new BridgeProtocolException(
                        "WidgetBridge returned an unknown or duplicate response.");
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailTerminal(exception, notify: true);
        }
    }

    private void FailTerminal(Exception exception, bool notify)
    {
        TaskCompletionSource<BridgeEnvelope>[] pending;
        lock (_gate)
        {
            if (_terminalFailure is not null) return;
            _terminalFailure = exception;
            pending = _pending.Values.ToArray();
            _pending.Clear();
        }
        var failure = new WidgetPresentationSessionException(
            "transport_closed", "The WidgetBridge presentation transport closed.", exception);
        foreach (var completion in pending)
        {
            _pendingCapacity.Release();
            completion.TrySetException(failure);
        }
        if (notify) Failed?.Invoke(exception);
    }

    private void ThrowIfTerminalLocked()
    {
        if (_terminalFailure is not null)
            throw new WidgetPresentationSessionException(
                "transport_closed", "The WidgetBridge presentation transport is closed.",
                _terminalFailure);
    }

    private static void ValidatePipeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Contains('\\'))
            throw new ArgumentException("Bridge pipe name is invalid.", nameof(value));
    }
}
