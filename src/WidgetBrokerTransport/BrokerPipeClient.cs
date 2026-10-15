using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace WidgetRail.PlatformBroker;

public sealed class BrokerPipeClient : IAsyncDisposable
{
    private const int MaximumCanceledRequestCorrelations = 128;
    private readonly string _pipeName;
    private readonly BrokerWidgetIdentity _identity;
    private readonly string _nonce;
    private readonly BrokerPipeTransportOptions _options;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<BrokerPipeEnvelope>> _pending = new();
    private readonly ConcurrentDictionary<string, Channel<BrokerEventEnvelope>> _events = new(StringComparer.Ordinal);
    private readonly object _canceledRequestsGate = new();
    private readonly Queue<long> _canceledRequestOrder = [];
    private readonly HashSet<long> _canceledRequests = [];
    private NamedPipeClientStream? _pipe;
    private BrokerPipeFrameChannel? _channel;
    private Task? _reader;
    private long _correlationId;
    private bool _disposed;

    public BrokerPipeClient(
        string pipeName,
        BrokerWidgetIdentity identity,
        string channelNonce,
        BrokerPipeTransportOptions? options = null)
    {
        BrokerPipeNameValidation.Validate(pipeName);
        _pipeName = pipeName;
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _identity.Validate();
        _nonce = channelNonce ?? throw new ArgumentNullException(nameof(channelNonce));
        _options = options ?? new BrokerPipeTransportOptions();
        _options.Validate();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pipe is not null) throw new InvalidOperationException("Broker client is already connected.");
        _pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(_options.AcceptTimeout);
        await _pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        _channel = new BrokerPipeFrameChannel(_pipe, _options.MaximumFrameBytes);
        var correlation = NextCorrelation();
        await WriteAsync(BrokerPipeMessageTypes.Hello, correlation,
            new BrokerPipeHello(_nonce, _identity), timeout.Token).ConfigureAwait(false);
        var response = await _channel.ReadAsync(timeout.Token).ConfigureAwait(false);
        if (response.Type != BrokerPipeMessageTypes.HelloAccepted || response.CorrelationId != correlation)
            throw new BrokerException("authentication_failed", "Broker handshake was rejected.");
        _reader = ReadLoopAsync(_lifetime.Token);
    }

    public Task<BrokerResponseEnvelope> RequestAsync<T>(
        string capabilityId,
        string operation,
        T payload,
        CancellationToken cancellationToken = default) =>
        RequestWithGestureAsync(
            capabilityId, operation, payload, null, null, cancellationToken);

    public Task<BrokerResponseEnvelope> RequestWithGestureAsync<T>(
        string capabilityId,
        string operation,
        T payload,
        long? gestureInputSequence,
        long? gestureSnapshotSequence,
        CancellationToken cancellationToken = default) =>
        RequestWithActionAsync(
            capabilityId,
            operation,
            payload,
            gestureInputSequence,
            gestureSnapshotSequence,
            actionExecutionId: null,
            cancellationToken);

    internal Task<BrokerResponseEnvelope> RequestWithActionAsync<T>(
        string capabilityId,
        string operation,
        T payload,
        long? gestureInputSequence,
        long? gestureSnapshotSequence,
        long? actionExecutionId,
        CancellationToken cancellationToken = default) =>
        SendRequestEnvelopeAsync(new BrokerRequestEnvelope(
            BrokerWireJson.ProtocolVersion,
            0,
            _identity,
            capabilityId,
            operation,
            BrokerPipeJson.Element(payload),
            gestureInputSequence,
            gestureSnapshotSequence), cancellationToken, actionExecutionId);

    internal async Task<BrokerResponseEnvelope> SendRequestEnvelopeAsync(
        BrokerRequestEnvelope source,
        CancellationToken cancellationToken = default,
        long? actionExecutionId = null)
    {
        var correlation = NextCorrelation();
        var request = source with { RequestId = correlation };
        var response = await CommandAsync(
            BrokerPipeMessageTypes.Request,
            request,
            cancellationToken,
            sendCancellation: true,
            requestTimeout: BrokerPipeRequestTimeoutPolicy.Resolve(_options, request),
            actionExecutionId: actionExecutionId)
            .ConfigureAwait(false);
        if (response.Type == BrokerPipeMessageTypes.Error)
        {
            var error = BrokerPipeJson.Payload<BrokerPipeError>(response.Payload);
            return new BrokerResponseEnvelope(BrokerWireJson.ProtocolVersion, correlation, false, null, error.Code);
        }
        if (response.Type != BrokerPipeMessageTypes.Response)
            throw new BrokerException("protocol_violation", "Broker response type is invalid.");
        var brokerResponse = BrokerPipeJson.Payload<BrokerResponseEnvelope>(response.Payload);
        if (brokerResponse.RequestId != correlation)
            throw new BrokerException("protocol_violation", "Broker response correlation is invalid.");
        return brokerResponse;
    }

    public async Task<BrokerPipeEventSubscription> SubscribeAsync(
        string capabilityId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        var id = $"subscription-{NextCorrelation()}";
        var channel = Channel.CreateBounded<BrokerEventEnvelope>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
        if (!_events.TryAdd(id, channel)) throw new InvalidOperationException("Duplicate subscription ID.");
        try
        {
            var response = await CommandAsync(BrokerPipeMessageTypes.Subscribe,
                new BrokerPipeSubscriptionRequest(id, capabilityId, eventType), cancellationToken)
                .ConfigureAwait(false);
            DemandAcknowledged(response);
            return new BrokerPipeEventSubscription(this, id, channel);
        }
        catch
        {
            _events.TryRemove(id, out _);
            channel.Writer.TryComplete();
            throw;
        }
    }

    internal async ValueTask UnsubscribeAsync(string id)
    {
        if (!_events.TryRemove(id, out var channel)) return;
        channel.Writer.TryComplete();
        try
        {
            var response = await CommandAsync(BrokerPipeMessageTypes.Unsubscribe,
                new BrokerPipeUnsubscribe(id), CancellationToken.None).ConfigureAwait(false);
            DemandAcknowledged(response);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or
            OperationCanceledException or BrokerException) { }
    }

    private async Task<BrokerPipeEnvelope> CommandAsync<T>(
        string type,
        T payload,
        CancellationToken cancellationToken,
        bool sendCancellation = false,
        TimeSpan? requestTimeout = null,
        long? actionExecutionId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var correlation = type == BrokerPipeMessageTypes.Request && payload is BrokerRequestEnvelope request
            ? request.RequestId
            : NextCorrelation();
        var completion = new TaskCompletionSource<BrokerPipeEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(correlation, completion))
            throw new InvalidOperationException("Duplicate broker correlation ID.");
        try
        {
            await WriteAsync(
                type, correlation, payload, cancellationToken, actionExecutionId)
                .ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
            timeout.CancelAfter(requestTimeout ?? _options.RequestTimeout);
            try { return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            {
                if (sendCancellation)
                {
                    RecordCanceledRequest(correlation);
                    await SendCancellationSafeAsync(correlation).ConfigureAwait(false);
                }
                throw new TimeoutException("Broker transport request timed out.");
            }
            catch (OperationCanceledException)
            {
                if (sendCancellation)
                {
                    RecordCanceledRequest(correlation);
                    await SendCancellationSafeAsync(correlation).ConfigureAwait(false);
                }
                throw;
            }
        }
        finally { _pending.TryRemove(correlation, out _); }
    }

    private async Task SendCancellationSafeAsync(long requestCorrelation)
    {
        try
        {
            var cancellationCorrelation = NextCorrelation();
            await WriteAsync(BrokerPipeMessageTypes.Cancel, cancellationCorrelation,
                new BrokerPipeCancel(requestCorrelation), _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await _channel!.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (message.Type == BrokerPipeMessageTypes.Event)
                {
                    if (message.CorrelationId != 0)
                        throw new BrokerException("protocol_violation", "Broker event cannot be correlated.");
                    var brokerEvent = BrokerPipeJson.Payload<BrokerPipeEvent>(message.Payload);
                    if (_events.TryGetValue(brokerEvent.SubscriptionId, out var channel))
                        channel.Writer.TryWrite(brokerEvent.Event);
                    continue;
                }
                if (message.Type == BrokerPipeMessageTypes.SubscriptionRevoked)
                {
                    if (message.CorrelationId != 0)
                        throw new BrokerException("protocol_violation", "Broker revocation cannot be correlated.");
                    var revoked = BrokerPipeJson.Payload<BrokerPipeSubscriptionRevoked>(message.Payload);
                    ValidateSubscriptionRevocation(revoked);
                    if (_events.TryRemove(revoked.SubscriptionId, out var channel))
                        channel.Writer.TryComplete(new BrokerException(
                            "capability_revoked", "Capability subscription was revoked."));
                    continue;
                }
                if (message.CorrelationId <= 0)
                    throw new BrokerException("protocol_violation", "Broker response correlation is unknown.");
                if (!_pending.TryRemove(message.CorrelationId, out var completion))
                {
                    if (TryConsumeCanceledRequest(message.CorrelationId)) continue;
                    throw new BrokerException(
                        "protocol_violation", "Broker response correlation is unknown.");
                }
                _ = TryConsumeCanceledRequest(message.CorrelationId);
                completion.TrySetResult(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or BrokerException)
        {
            FailPending(exception);
            _lifetime.Cancel();
        }
    }

    private async Task WriteAsync<T>(
        string type,
        long correlationId,
        T payload,
        CancellationToken cancellationToken,
        long? actionExecutionId = null)
    {
        var channel = _channel ?? throw new InvalidOperationException("Broker client is not connected.");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await channel.WriteAsync(new BrokerPipeEnvelope
            {
                ProtocolVersion = BrokerWireJson.ProtocolVersion,
                Type = type,
                CorrelationId = correlationId,
                Payload = BrokerPipeJson.Element(payload),
                ActionExecutionId = actionExecutionId,
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
    }

    private static void DemandAcknowledged(BrokerPipeEnvelope response)
    {
        if (response.Type == BrokerPipeMessageTypes.Error)
            throw new BrokerException(BrokerPipeJson.Payload<BrokerPipeError>(response.Payload).Code,
                "Broker transport command failed.");
        if (response.Type != BrokerPipeMessageTypes.Acknowledged)
            throw new BrokerException("protocol_violation", "Broker acknowledgement is invalid.");
    }

    private static void ValidateSubscriptionRevocation(BrokerPipeSubscriptionRevoked revoked)
    {
        if (string.IsNullOrEmpty(revoked.SubscriptionId) || revoked.SubscriptionId.Length > 128 ||
            !revoked.SubscriptionId.All(character => char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '-' or '_') ||
            revoked.Code != "capability_revoked")
            throw new BrokerException("protocol_violation", "Broker subscription revocation is invalid.");
    }

    private long NextCorrelation() => Interlocked.Increment(ref _correlationId);

    private void RecordCanceledRequest(long correlation)
    {
        lock (_canceledRequestsGate)
        {
            if (!_canceledRequests.Add(correlation)) return;
            _canceledRequestOrder.Enqueue(correlation);
            while (_canceledRequestOrder.Count > MaximumCanceledRequestCorrelations)
                _canceledRequests.Remove(_canceledRequestOrder.Dequeue());
        }
    }

    private bool TryConsumeCanceledRequest(long correlation)
    {
        lock (_canceledRequestsGate) return _canceledRequests.Remove(correlation);
    }

    private void FailPending(Exception exception)
    {
        foreach (var entry in _pending.ToArray())
            if (_pending.TryRemove(entry.Key, out var completion))
                completion.TrySetException(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _pipe?.Dispose();
        if (_reader is not null)
        {
            try { await _reader.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException) { }
        }
        FailPending(new ObjectDisposedException(nameof(BrokerPipeClient)));
        foreach (var channel in _events.Values) channel.Writer.TryComplete();
        _events.Clear();
        lock (_canceledRequestsGate)
        {
            _canceledRequests.Clear();
            _canceledRequestOrder.Clear();
        }
    }
}

public sealed class BrokerPipeEventSubscription : IAsyncDisposable
{
    private readonly BrokerPipeClient _client;
    private readonly Channel<BrokerEventEnvelope> _events;
    private int _disposed;

    internal BrokerPipeEventSubscription(
        BrokerPipeClient client,
        string id,
        Channel<BrokerEventEnvelope> events)
    {
        _client = client;
        Id = id;
        _events = events;
    }

    public string Id { get; }

    public async ValueTask<BrokerEventEnvelope> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        try { return await _events.Reader.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (ChannelClosedException exception) when (exception.InnerException is BrokerException broker)
        {
            throw broker;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _client.UnsubscribeAsync(Id).ConfigureAwait(false);
    }
}
