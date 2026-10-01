using System.IO.Pipes;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession;

internal sealed class BridgePresentationTransport : IAsyncDisposable
{
    private static readonly TimeSpan StopDeadline = TimeSpan.FromSeconds(2);
    private readonly NamedPipeClientStream _pipe;
    private readonly BridgeFrameChannel _channel;
    private readonly Action<string>? _beforeInputWrite;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    // Bridge dispatcher admits at most 16 concurrent requests. Leave one slot
    // for reply finalization/shutdown and reserve provider
    // cancellation slots independently from ordinary controls and lease release.
    internal const int MaximumWireRequests = 15;
    private readonly SemaphoreSlim _pendingCapacity;
    private readonly SemaphoreSlim _providerCapacity;
    private readonly SemaphoreSlim _cancellationCapacity;
    private readonly SemaphoreSlim _controlCapacity;
    private sealed record PendingResponse(TaskCompletionSource<BridgeEnvelope> Completion, SemaphoreSlim Lane);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Dictionary<long, PendingResponse> _pending = [];
    private Task? _receiveLoop;
    private long _requestId;
    private Exception? _terminalFailure;
    private bool _disposed;
    private bool _stopping;
    private int _activeRequests;
    private TaskCompletionSource? _requestsDrained;
    private Task? _disposal;

    private BridgePresentationTransport(
        NamedPipeClientStream pipe,
        BridgeFrameChannel channel,
        int maximumPendingRequests,
        long initialRequestId,
        Action<string>? beforeInputWrite)
    {
        _pipe = pipe;
        _channel = channel;
        _beforeInputWrite = beforeInputWrite;
        _requestId = initialRequestId;
        var total = Math.Min(maximumPendingRequests, MaximumWireRequests);
        var provider = Math.Min(4, (total - 1) / 2);
        var control = total - 2 * provider;
        _pendingCapacity = new(total, total);
        _providerCapacity = new(provider, Math.Max(1, provider));
        _cancellationCapacity = new(provider, Math.Max(1, provider));
        _controlCapacity = new(control, control);
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
                Payload = BridgeJson.ToElement(new BridgeHello(options.ClientName, options.WindowPreviews,
                    options.ExclusiveControllerControl, options.HeldDpadScroll, options.StartupRegistration)),
            }, deadline.Token).ConfigureAwait(false);
            var hello = await channel.ReadAsync(deadline.Token).ConfigureAwait(false);
            if (hello.RequestId != helloId || hello.Type != BridgeMessageTypes.HelloAccepted ||
                hello.Payload.ValueKind != System.Text.Json.JsonValueKind.Object ||
                hello.Payload.EnumerateObject().Any())
                throw new BridgeProtocolException(
                    "WidgetBridge rejected the presentation-session handshake.");
            return new BridgePresentationTransport(
                pipe, channel, options.MaximumPendingRequests, helloId, options.BeforeInputWrite);
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

    internal Task<BridgeEnvelope> RequestAsync<T>(
        string type,
        T payload,
        CancellationToken cancellationToken) => RequestCoreAsync(type, payload, cancellationToken, stopping: false);

    internal async Task<BridgeEnvelope> RequestWithWriteCompletionAsync<T>(string type, T payload,
        TaskCompletionSource written)
    {
        try { return await RequestCoreAsync(type, payload, CancellationToken.None, stopping: false, written).ConfigureAwait(false); }
        catch (Exception error) { written.TrySetException(error); throw; }
    }

    private async Task<BridgeEnvelope> RequestCoreAsync<T>(
        string type, T payload, CancellationToken cancellationToken, bool stopping, TaskCompletionSource? written = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed || (_stopping && !stopping), this);
            ThrowIfTerminalLocked();
            ++_activeRequests;
        }
        try { return await SendRequestAsync(type, payload, cancellationToken, written).ConfigureAwait(false); }
        finally
        {
            lock (_gate)
                if (--_activeRequests == 0) _requestsDrained?.TrySetResult();
        }
    }

    private async Task<BridgeEnvelope> SendRequestAsync<T>(
        string type, T payload, CancellationToken cancellationToken, TaskCompletionSource? written)
    {
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var lane = type switch
        {
            BridgeMessageTypes.ReadIndexedRange or BridgeMessageTypes.AcquireIndexedRange or BridgeMessageTypes.ResolveIndexedArtwork => _providerCapacity,
            BridgeMessageTypes.CancelIntent or BridgeMessageTypes.CancelIndexedRange or BridgeMessageTypes.CancelIndexedArtwork => _cancellationCapacity,
            _ => _controlCapacity,
        };
        await lane.WaitAsync(admission.Token).ConfigureAwait(false);
        try { await _pendingCapacity.WaitAsync(admission.Token).ConfigureAwait(false); }
        catch { lane.Release(); throw; }
        var requestId = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<BridgeEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // A caller may cancel after its frame was sent. Keep the response slot for
        // correlation, but observe a later terminal failure even without that caller.
        _ = completion.Task.ContinueWith(static task => { _ = task.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            lock (_gate)
            {
                ThrowIfTerminalLocked();
                _pending.Add(requestId, new(completion, lane));
            }
        }
        catch
        {
            _pendingCapacity.Release();
            lane.Release();
            throw;
        }

        try
        {
            await _writeGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                var envelope = new BridgeEnvelope
                {
                    Type = type,
                    RequestId = requestId,
                    Payload = BridgeJson.ToElement(payload),
                };
                // Run after waiting for transport capacity/the write gate, so
                // foreground eligibility cannot be captured from an earlier UI
                // event while this input waits behind another request.
                if (type is BridgeMessageTypes.Action or BridgeMessageTypes.PinnedAction or
                    BridgeMessageTypes.QuickAction or BridgeMessageTypes.ControllerInput or BridgeMessageTypes.IndexedInput)
                    _beforeInputWrite?.Invoke(type);
                await _channel.WriteAsync(envelope, _lifetime.Token).ConfigureAwait(false);
                written?.TrySetResult();
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

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            return new(_disposal ??= StopAsync());
        }
    }

    private async Task StopAsync()
    {
        try
        {
            if (_terminalFailure is null)
            {
                using var stop = new CancellationTokenSource(StopDeadline);
                // The deadline must interrupt existing writers and gate waiters,
                // not merely stop waiting for the shutdown response.
                using var abort = stop.Token.Register(() => _lifetime.Cancel());
                try
                {
                    var response = await RequestCoreAsync(BridgeMessageTypes.Stop,
                        new BridgeEmptyPayload(), stop.Token, stopping: true).ConfigureAwait(false);
                    if (response.Type != BridgeMessageTypes.Acknowledged)
                        throw new BridgeProtocolException("WidgetBridge returned an invalid stop response.");
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Failed?.Invoke(new WidgetPresentationSessionException(
                        "stop_incomplete", "The WidgetBridge stop handshake did not complete.", exception));
                }
            }
        }
        finally
        {
            _lifetime.Cancel();
            await _pipe.DisposeAsync().ConfigureAwait(false);
            if (_receiveLoop is not null)
            {
                try { await _receiveLoop.ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { }
            }
            FailTerminal(new ObjectDisposedException(nameof(BridgePresentationTransport)), notify: false);
            Task drained;
            lock (_gate)
            {
                _disposed = true;
                drained = _activeRequests == 0 ? Task.CompletedTask :
                    (_requestsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
            await drained.ConfigureAwait(false);
            _lifetime.Dispose();
            _writeGate.Dispose();
            _pendingCapacity.Dispose();
            _providerCapacity.Dispose();
            _cancellationCapacity.Dispose();
            _controlCapacity.Dispose();
        }
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
                PendingResponse? pending;
                lock (_gate)
                {
                    if (_pending.Remove(envelope.RequestId, out pending))
                    {
                        _pendingCapacity.Release();
                        pending.Lane.Release();
                    }
                }
                if (pending is null || !pending.Completion.TrySetResult(envelope))
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
        PendingResponse[] pending;
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
            completion.Lane.Release();
            completion.Completion.TrySetException(failure);
        }
        _lifetime.Cancel();
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
