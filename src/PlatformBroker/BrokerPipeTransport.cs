using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace GameBarAlternative.PlatformBroker;

public sealed record BrokerPipeTransportOptions
{
    public int MaximumFrameBytes { get; init; } = 64 * 1024;
    public TimeSpan AcceptTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public int MaximumInFlightRequests { get; init; } = 16;
    public int MaximumSubscriptions { get; init; } = 8;

    internal void Validate()
    {
        if (MaximumFrameBytes is < 1024 or > 256 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumFrameBytes));
        ValidateTimeout(AcceptTimeout, nameof(AcceptTimeout));
        ValidateTimeout(HandshakeTimeout, nameof(HandshakeTimeout));
        ValidateTimeout(RequestTimeout, nameof(RequestTimeout));
        if (MaximumInFlightRequests is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaximumInFlightRequests));
        if (MaximumSubscriptions is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(MaximumSubscriptions));
    }

    private static void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(name);
    }
}

internal static class BrokerPipeMessageTypes
{
    public const string Hello = "hello";
    public const string HelloAccepted = "helloAccepted";
    public const string Request = "request";
    public const string Response = "response";
    public const string Lifecycle = "lifecycle";
    public const string Subscribe = "subscribe";
    public const string Unsubscribe = "unsubscribe";
    public const string Cancel = "cancel";
    public const string Acknowledged = "acknowledged";
    public const string Error = "error";
    public const string Event = "event";
    public const string SubscriptionRevoked = "subscriptionRevoked";
}

internal sealed record BrokerPipeEnvelope
{
    [JsonRequired] public required int ProtocolVersion { get; init; }
    [JsonRequired] public required string Type { get; init; }
    [JsonRequired] public required long CorrelationId { get; init; }
    [JsonRequired] public required JsonElement Payload { get; init; }
}

internal sealed record BrokerPipeHello(
    [property: JsonRequired] string Nonce,
    [property: JsonRequired] BrokerWidgetIdentity Widget);
internal sealed record BrokerPipeLifecycle(
    [property: JsonRequired] BrokerLifecycleState State);
internal sealed record BrokerPipeSubscriptionRequest(
    [property: JsonRequired] string SubscriptionId,
    [property: JsonRequired] string CapabilityId,
    [property: JsonRequired] string EventType);
internal sealed record BrokerPipeUnsubscribe(
    [property: JsonRequired] string SubscriptionId);
internal sealed record BrokerPipeCancel(
    [property: JsonRequired] long RequestCorrelationId);
internal sealed record BrokerPipeError(
    [property: JsonRequired] string Code);
internal sealed record BrokerPipeEvent(
    [property: JsonRequired] string SubscriptionId,
    [property: JsonRequired] BrokerEventEnvelope Event);
internal sealed record BrokerPipeSubscriptionRevoked(
    [property: JsonRequired] string SubscriptionId,
    [property: JsonRequired] string Code);

internal sealed class BrokerPipeFrameChannel(Stream stream, int maximumFrameBytes)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly int _maximumFrameBytes = maximumFrameBytes is >= 1024 and <= 256 * 1024
        ? maximumFrameBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));

    public async ValueTask WriteAsync(BrokerPipeEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = BrokerPipeJson.Serialize(envelope);
        if (payload.Length == 0 || payload.Length > _maximumFrameBytes)
            throw new BrokerException("frame_too_large", "Broker transport frame exceeds its bound.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await _stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BrokerPipeEnvelope> ReadAsync(CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        await ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > _maximumFrameBytes)
            throw new BrokerException("frame_too_large", "Broker transport frame length is invalid.");
        var payload = GC.AllocateUninitializedArray<byte>(length);
        await ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return BrokerPipeJson.Parse(payload);
    }

    private async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Broker transport closed mid-frame.");
            offset += read;
        }
    }
}

internal static class BrokerPipeJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = BrokerJson.MaximumDepth,
    };

    static BrokerPipeJson() => Options.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static BrokerPipeEnvelope Parse(ReadOnlySpan<byte> payload)
    {
        try
        {
            BrokerJson.RejectDuplicateProperties(payload);
            var envelope = JsonSerializer.Deserialize<BrokerPipeEnvelope>(payload, Options)
                ?? throw new JsonException("Transport envelope was null.");
            if (envelope.ProtocolVersion != BrokerJson.ProtocolVersion)
                throw new BrokerException("unsupported_protocol", "Broker transport protocol is unsupported.");
            if (string.IsNullOrEmpty(envelope.Type) || envelope.Type.Length > 64 ||
                !envelope.Type.All(character => char.IsAsciiLetter(character)))
                throw new BrokerException("malformed_frame", "Broker transport message type is invalid.");
            if (envelope.CorrelationId < 0 || envelope.CorrelationId > 9_007_199_254_740_991L)
                throw new BrokerException("malformed_frame", "Broker transport correlation ID is invalid.");
            BrokerJson.ValidateElement(envelope.Payload, "malformed_frame");
            return envelope with { Payload = envelope.Payload.Clone() };
        }
        catch (BrokerException) { throw; }
        catch (JsonException exception)
        {
            throw new BrokerException("malformed_frame", "Broker transport JSON is invalid.", exception);
        }
    }

    public static T Payload<T>(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<T>(Options)
                ?? throw new JsonException("Transport payload was null.");
        }
        catch (JsonException exception)
        {
            throw new BrokerException("malformed_frame", "Broker transport payload is invalid.", exception);
        }
    }

    public static JsonElement Element<T>(T value) =>
        JsonSerializer.SerializeToElement(value, Options);
}

public sealed class BrokerPipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly BrokerWidgetIdentity _identity;
    private readonly HashSet<string> _declaredCapabilities;
    private readonly BrokerPipeTransportOptions _options;
    private readonly string? _isolatedClientAppContainerSid;
    private readonly PlatformCapabilityBroker _broker;
    private readonly ConsentChangeMonitor _consentMonitor;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _requests = new();
    private readonly ConcurrentDictionary<string, RemoteSubscription> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, Task> _requestTasks = new();
    private NamedPipeServerStream? _pipe;
    private BrokerPipeFrameChannel? _channel;
    private int _runStarted;
    private int _expectedIsolatedClientProcessId;
    private long _taskId;
    private bool _disposed;

    public BrokerPipeServer(
        string pipeName,
        BrokerWidgetIdentity authenticatedIdentity,
        IEnumerable<string> declaredCapabilities,
        ConsentStore consentStore,
        IPlatformBrokerBackend backend,
        BrokerPipeTransportOptions? options = null,
        string? channelNonce = null,
        string? isolatedClientAppContainerSid = null)
    {
        BrokerPipeNames.Validate(pipeName);
        BrokerPipeNames.ValidateAppContainerSid(isolatedClientAppContainerSid);
        _pipeName = pipeName;
        _isolatedClientAppContainerSid = isolatedClientAppContainerSid;
        _identity = authenticatedIdentity ?? throw new ArgumentNullException(nameof(authenticatedIdentity));
        _identity.Validate();
        ArgumentNullException.ThrowIfNull(declaredCapabilities);
        _declaredCapabilities = new HashSet<string>(declaredCapabilities, StringComparer.Ordinal);
        _options = options ?? new BrokerPipeTransportOptions();
        _options.Validate();
        ChannelNonce = channelNonce ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (ChannelNonce.Length is < 32 or > 128 ||
            !ChannelNonce.All(character => char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Broker channel nonce is invalid.", nameof(channelNonce));
        _broker = new PlatformCapabilityBroker(
            _identity, _declaredCapabilities, consentStore, backend);
        _consentMonitor = new ConsentChangeMonitor(
            consentStore, _broker.RefreshConsentAsync, _broker.RevokeSubscriptions);
    }

    public string ChannelNonce { get; }
    public BrokerWidgetIdentity AuthenticatedIdentity => _identity;
    public IReadOnlySet<string> DeclaredCapabilities =>
        new HashSet<string>(_declaredCapabilities, StringComparer.Ordinal);

    /// <summary>
    /// Binds an AppContainer broker endpoint to the worker process created by
    /// the trusted host. This must happen before <see cref="RunAsync"/>.
    /// </summary>
    public void BindExpectedIsolatedClientProcess(int processId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isolatedClientAppContainerSid is null)
            throw new InvalidOperationException(
                "Only an isolated broker endpoint accepts a worker process binding.");
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (Volatile.Read(ref _runStarted) != 0 ||
            Interlocked.CompareExchange(
                ref _expectedIsolatedClientProcessId,
                processId,
                0) != 0)
            throw new InvalidOperationException(
                "The isolated broker worker process is already bound or running.");
    }

    /// <summary>
    /// Host-owned lifecycle gate. The connected worker cannot elevate its own
    /// capability access by sending transport messages.
    /// </summary>
    public void SetLifecycle(BrokerLifecycleState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _broker.SetLifecycle(lifecycle);
    }

    /// <summary>
    /// Trusted host companion path. There is intentionally no corresponding
    /// broker-pipe message a widget worker could send.
    /// </summary>
    public void GrantDashboardGestureAuthority(
        string capabilityId,
        string operationId,
        long inputSequence,
        long snapshotSequence,
        TimeSpan validFor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _broker.GrantDashboardGestureAuthority(
            capabilityId, operationId, inputSequence, snapshotSequence, validFor);
    }

    public void RevokeDashboardGestureAuthority(long inputSequence)
    {
        if (_disposed) return;
        _broker.RevokeDashboardGestureAuthority(inputSequence);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var expectedIsolatedClientProcessId =
            Volatile.Read(ref _expectedIsolatedClientProcessId);
        if (_isolatedClientAppContainerSid is not null &&
            expectedIsolatedClientProcessId <= 0)
            throw new InvalidOperationException(
                "An isolated broker endpoint requires an expected worker process before it runs.");
        if (Interlocked.Exchange(ref _runStarted, 1) != 0)
            throw new InvalidOperationException("A broker pipe server accepts exactly one client.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        _pipe = BrokerPipeNames.CreateServer(_pipeName, _isolatedClientAppContainerSid);
        using (var accept = CancellationTokenSource.CreateLinkedTokenSource(linked.Token))
        {
            accept.CancelAfter(_options.AcceptTimeout);
            await _pipe.WaitForConnectionAsync(accept.Token).ConfigureAwait(false);
        }
        if (_isolatedClientAppContainerSid is not null)
            WindowsIsolatedPipeFactory.VerifyClientProcess(
                _pipe,
                expectedIsolatedClientProcessId);
        _channel = new BrokerPipeFrameChannel(_pipe, _options.MaximumFrameBytes);
        await HandshakeAsync(linked.Token).ConfigureAwait(false);

        try
        {
            while (!linked.IsCancellationRequested)
            {
                var message = await _channel.ReadAsync(linked.Token).ConfigureAwait(false);
                try { await DispatchAsync(message, linked.Token).ConfigureAwait(false); }
                catch (BrokerException exception)
                {
                    await SendErrorSafeAsync(message.CorrelationId, exception.Code, linked.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (EndOfStreamException) { }
        catch (IOException) when (linked.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (linked.IsCancellationRequested) { }
        finally
        {
            _lifetime.Cancel();
            foreach (var request in _requests.Values) CancelRequest(request);
            await DisposeSubscriptionsAsync().ConfigureAwait(false);
            var tasks = _requestTasks.Values.ToArray();
            if (tasks.Length != 0)
            {
                try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (Exception exception) when (exception is TimeoutException or OperationCanceledException) { }
            }
            await _consentMonitor.DisposeAsync().ConfigureAwait(false);
            await _broker.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.HandshakeTimeout);
        var message = await _channel!.ReadAsync(timeout.Token).ConfigureAwait(false);
        if (message.Type != BrokerPipeMessageTypes.Hello || message.CorrelationId <= 0)
            throw new BrokerException("handshake_required", "The first broker message must be hello.");
        var hello = BrokerPipeJson.Payload<BrokerPipeHello>(message.Payload);
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(hello.Nonce),
                System.Text.Encoding.UTF8.GetBytes(ChannelNonce)) ||
            hello.Widget != _identity)
            throw new BrokerException("authentication_failed", "Broker channel authentication failed.");
        await SendAsync(BrokerPipeMessageTypes.HelloAccepted, message.CorrelationId, new { }, timeout.Token)
            .ConfigureAwait(false);
    }

    private async Task DispatchAsync(BrokerPipeEnvelope message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case BrokerPipeMessageTypes.Request:
                StartRequest(message, cancellationToken);
                break;
            case BrokerPipeMessageTypes.Cancel:
                var cancel = BrokerPipeJson.Payload<BrokerPipeCancel>(message.Payload);
                if (_requests.TryGetValue(cancel.RequestCorrelationId, out var request)) CancelRequest(request);
                break;
            case BrokerPipeMessageTypes.Subscribe:
                await SubscribeAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            case BrokerPipeMessageTypes.Unsubscribe:
                await UnsubscribeAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new BrokerException("unexpected_message", "Broker transport message is not allowed.");
        }
    }

    private void StartRequest(BrokerPipeEnvelope message, CancellationToken cancellationToken)
    {
        if (message.CorrelationId <= 0 || _requests.Count >= _options.MaximumInFlightRequests)
        {
            _ = SendErrorSafeAsync(message.CorrelationId, "request_limit", cancellationToken);
            return;
        }
        var requestBytes = BrokerPipeJson.Serialize(message.Payload);
        BrokerRequestEnvelope request;
        try { request = BrokerJson.ParseRequest(requestBytes); }
        catch (BrokerException exception)
        {
            _ = SendErrorSafeAsync(message.CorrelationId, exception.Code, cancellationToken);
            return;
        }
        if (request.RequestId != message.CorrelationId || request.Widget != _identity)
        {
            _ = SendErrorSafeAsync(message.CorrelationId, "identity_mismatch", cancellationToken);
            return;
        }
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        requestCancellation.CancelAfter(_options.RequestTimeout);
        if (!_requests.TryAdd(message.CorrelationId, requestCancellation))
        {
            requestCancellation.Dispose();
            _ = SendErrorSafeAsync(message.CorrelationId, "duplicate_request", cancellationToken);
            return;
        }
        var taskId = Interlocked.Increment(ref _taskId);
        var task = RunRequestAsync(message.CorrelationId, requestBytes, requestCancellation);
        _requestTasks[taskId] = task;
        _ = task.ContinueWith(completedTask =>
        {
            _ = completedTask.Exception;
            _requestTasks.TryRemove(taskId, out var removedTask);
            _ = removedTask;
        },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task RunRequestAsync(
        long correlationId,
        byte[] requestBytes,
        CancellationTokenSource requestCancellation)
    {
        try
        {
            var response = await _broker.HandleAsync(requestBytes, requestCancellation.Token)
                .ConfigureAwait(false);
            await SendAsync(BrokerPipeMessageTypes.Response, correlationId, response, _lifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SendErrorSafeAsync(correlationId, "request_canceled", _lifetime.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _requests.TryRemove(correlationId, out _);
            requestCancellation.Dispose();
        }
    }

    private async Task SubscribeAsync(BrokerPipeEnvelope message, CancellationToken cancellationToken)
    {
        if (_subscriptions.Count >= _options.MaximumSubscriptions)
        {
            await SendErrorSafeAsync(message.CorrelationId, "subscription_limit", cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        var request = BrokerPipeJson.Payload<BrokerPipeSubscriptionRequest>(message.Payload);
        ValidateSubscriptionId(request.SubscriptionId);
        var subscription = await _broker.SubscribeAsync(
            request.CapabilityId, request.EventType, cancellationToken).ConfigureAwait(false);
        var remote = new RemoteSubscription(request.SubscriptionId, subscription,
            CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));
        if (!_subscriptions.TryAdd(request.SubscriptionId, remote))
        {
            await remote.DisposeAsync().ConfigureAwait(false);
            await SendErrorSafeAsync(message.CorrelationId, "duplicate_subscription", cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        remote.ForwardTask = ForwardEventsAsync(remote);
        await SendAsync(BrokerPipeMessageTypes.Acknowledged, message.CorrelationId, new { }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UnsubscribeAsync(BrokerPipeEnvelope message, CancellationToken cancellationToken)
    {
        var request = BrokerPipeJson.Payload<BrokerPipeUnsubscribe>(message.Payload);
        ValidateSubscriptionId(request.SubscriptionId);
        if (_subscriptions.TryRemove(request.SubscriptionId, out var subscription))
            await subscription.DisposeAsync().ConfigureAwait(false);
        await SendAsync(BrokerPipeMessageTypes.Acknowledged, message.CorrelationId, new { }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ForwardEventsAsync(RemoteSubscription remote)
    {
        try
        {
            while (!remote.Cancellation.IsCancellationRequested)
            {
                var brokerEvent = await remote.Subscription.ReadAsync(remote.Cancellation.Token)
                    .ConfigureAwait(false);
                // The watcher provides prompt cross-process revocation. Recheck
                // again at the event boundary so a dropped filesystem signal
                // cannot preserve a stale subscription grant indefinitely.
                await _broker.RefreshConsentAsync(remote.Cancellation.Token).ConfigureAwait(false);
                if (remote.Subscription.IsRevoked)
                    throw new BrokerException(
                        "capability_revoked", "Capability subscription was revoked.");
                await SendAsync(BrokerPipeMessageTypes.Event, 0,
                        new BrokerPipeEvent(remote.Id, brokerEvent), remote.Cancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (BrokerException exception) when (exception.Code == "capability_revoked")
        {
            if (_subscriptions.TryRemove(remote.Id, out _))
            {
                try
                {
                    await SendAsync(BrokerPipeMessageTypes.SubscriptionRevoked, 0,
                            new BrokerPipeSubscriptionRevoked(remote.Id, exception.Code),
                            _lifetime.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception sendException) when (sendException is IOException or
                    OperationCanceledException or ObjectDisposedException) { }
                remote.Cancellation.Cancel();
                await remote.Subscription.DisposeAsync().ConfigureAwait(false);
                remote.Cancellation.Dispose();
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or BrokerException or IOException) { }
    }

    private async Task SendErrorSafeAsync(long correlationId, string code, CancellationToken cancellationToken)
    {
        try { await SendAsync(BrokerPipeMessageTypes.Error, correlationId, new BrokerPipeError(code), cancellationToken)
                .ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task SendAsync<T>(
        string type, long correlationId, T payload, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException("Broker channel is not connected.");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await channel.WriteAsync(new BrokerPipeEnvelope
            {
                ProtocolVersion = BrokerJson.ProtocolVersion,
                Type = type,
                CorrelationId = correlationId,
                Payload = BrokerPipeJson.Element(payload),
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
    }

    private async Task DisposeSubscriptionsAsync()
    {
        foreach (var entry in _subscriptions.ToArray())
            if (_subscriptions.TryRemove(entry.Key, out var subscription))
                await subscription.DisposeAsync().ConfigureAwait(false);
    }

    private static void ValidateSubscriptionId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
            throw new BrokerException("invalid_subscription", "Subscription ID is invalid.");
    }

    private static void CancelRequest(CancellationTokenSource request)
    {
        try { request.Cancel(); }
        catch (ObjectDisposedException)
        {
            // ConcurrentDictionary enumeration and TryGetValue may retain a value
            // after its request task has removed and disposed the source.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _pipe?.Dispose();
        foreach (var request in _requests.Values) CancelRequest(request);
        await DisposeSubscriptionsAsync().ConfigureAwait(false);
        var requests = _requestTasks.Values.ToArray();
        if (requests.Length != 0)
        {
            try { await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException) { }
        }
        await _consentMonitor.DisposeAsync().ConfigureAwait(false);
        await _broker.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class RemoteSubscription(
        string id,
        BrokerEventSubscription subscription,
        CancellationTokenSource cancellation) : IAsyncDisposable
    {
        public string Id { get; } = id;
        public BrokerEventSubscription Subscription { get; } = subscription;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task ForwardTask { get; set; } = Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            Cancellation.Cancel();
            await Subscription.DisposeAsync().ConfigureAwait(false);
            try { await ForwardTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or BrokerException) { }
            Cancellation.Dispose();
        }
    }
}

public sealed class BrokerPipeClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly BrokerWidgetIdentity _identity;
    private readonly string _nonce;
    private readonly BrokerPipeTransportOptions _options;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<BrokerPipeEnvelope>> _pending = new();
    private readonly ConcurrentDictionary<string, Channel<BrokerEventEnvelope>> _events = new(StringComparer.Ordinal);
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
        BrokerPipeNames.Validate(pipeName);
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
        SendRequestEnvelopeAsync(new BrokerRequestEnvelope(
            BrokerJson.ProtocolVersion,
            0,
            _identity,
            capabilityId,
            operation,
            BrokerPipeJson.Element(payload),
            gestureInputSequence,
            gestureSnapshotSequence), cancellationToken);

    internal async Task<BrokerResponseEnvelope> SendRequestEnvelopeAsync(
        BrokerRequestEnvelope source,
        CancellationToken cancellationToken = default)
    {
        var correlation = NextCorrelation();
        var request = source with { RequestId = correlation };
        var response = await CommandAsync(
            BrokerPipeMessageTypes.Request,
            request,
            cancellationToken,
            sendCancellation: true).ConfigureAwait(false);
        if (response.Type == BrokerPipeMessageTypes.Error)
        {
            var error = BrokerPipeJson.Payload<BrokerPipeError>(response.Payload);
            return new BrokerResponseEnvelope(BrokerJson.ProtocolVersion, correlation, false, null, error.Code);
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
        bool sendCancellation = false)
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
            await WriteAsync(type, correlation, payload, cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
            timeout.CancelAfter(_options.RequestTimeout);
            try { return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            {
                if (sendCancellation) await SendCancellationSafeAsync(correlation).ConfigureAwait(false);
                throw new TimeoutException("Broker transport request timed out.");
            }
            catch (OperationCanceledException)
            {
                if (sendCancellation) await SendCancellationSafeAsync(correlation).ConfigureAwait(false);
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
                if (message.CorrelationId <= 0 ||
                    !_pending.TryRemove(message.CorrelationId, out var completion))
                    throw new BrokerException("protocol_violation", "Broker response correlation is unknown.");
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
        string type, long correlationId, T payload, CancellationToken cancellationToken)
    {
        var channel = _channel ?? throw new InvalidOperationException("Broker client is not connected.");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await channel.WriteAsync(new BrokerPipeEnvelope
            {
                ProtocolVersion = BrokerJson.ProtocolVersion,
                Type = type,
                CorrelationId = correlationId,
                Payload = BrokerPipeJson.Element(payload),
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
