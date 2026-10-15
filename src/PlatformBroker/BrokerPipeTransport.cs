using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace WidgetRail.PlatformBroker;

public enum BrokerHostEffectKind
{
    CloseOverlayAfterAppLaunch,
    ActivateTaskWindow,
}

public sealed record BrokerHostEffect(BrokerHostEffectKind Kind)
{
    public long InitiatedAtMilliseconds { get; init; }
    public string? WindowId { get; init; }
    public NativeWindowPreviewTarget? WindowTarget { get; init; }
}

internal readonly record struct BrokerCapabilityDiagnostic(
    string CapabilityId,
    string OperationId,
    string Stage,
    string? Code);

public sealed class BrokerPipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly BrokerWidgetIdentity _identity;
    private readonly HashSet<string> _declaredCapabilities;
    private readonly HashSet<string> _hostGrantedCapabilities;
    private readonly BrokerPipeTransportOptions _options;
    private readonly string? _isolatedClientAppContainerSid;
    private readonly PlatformCapabilityBroker _broker;
    private readonly ConsentChangeMonitor _consentMonitor;
    private readonly Action<BrokerHostEffect, long?, long?>? _hostEffectSink;
    private readonly Func<long, long?>? _actionExecutionAdmission;
    private readonly Action<BrokerCapabilityDiagnostic>? _diagnosticSink;
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
        string? isolatedClientAppContainerSid = null,
        IEnumerable<string>? hostGrantedCapabilities = null,
        Action<BrokerHostEffect>? hostEffectSink = null)
        : this(
            pipeName,
            authenticatedIdentity,
            declaredCapabilities,
            consentStore,
            backend,
            null,
            options,
            channelNonce,
            isolatedClientAppContainerSid,
            hostGrantedCapabilities,
            hostEffectSink,
            diagnosticSink: null,
            contextualHostEffectSink: null,
            actionExecutionAdmission: null)
    {
    }

    internal BrokerPipeServer(
        string pipeName,
        BrokerWidgetIdentity authenticatedIdentity,
        IEnumerable<string> declaredCapabilities,
        ConsentStore consentStore,
        IPlatformBrokerBackend backend,
        AppLibraryArtworkRegistry? appLibraryArtworkRegistry,
        BrokerPipeTransportOptions? options = null,
        string? channelNonce = null,
        string? isolatedClientAppContainerSid = null,
        IEnumerable<string>? hostGrantedCapabilities = null,
        Action<BrokerHostEffect>? hostEffectSink = null,
        Action<BrokerCapabilityDiagnostic>? diagnosticSink = null,
        Action<BrokerHostEffect, long?, long?>? contextualHostEffectSink = null,
        Func<long, long?>? actionExecutionAdmission = null,
        Func<WidgetRail.WidgetProtocol.MarketplaceCommand,CancellationToken,ValueTask<WidgetRail.WidgetProtocol.MarketplaceSnapshot>>? marketplace = null,
        Func<WidgetRail.WidgetProtocol.WidgetNotificationCommand, CancellationToken, ValueTask<WidgetRail.WidgetProtocol.WidgetNotificationReceipt>>? notifications = null,
        Func<WidgetRail.WidgetProtocol.WidgetActivity, CancellationToken, ValueTask<WidgetRail.WidgetProtocol.WidgetActivityReceipt>>? activity = null,
        Func<WidgetRail.WidgetProtocol.WebSessionCommand, CancellationToken, ValueTask<WidgetRail.WidgetProtocol.WebSessionReply>>? webSessions = null)
    {
        BrokerPipeNames.Validate(pipeName);
        BrokerPipeNames.ValidateAppContainerSid(isolatedClientAppContainerSid);
        _pipeName = pipeName;
        _isolatedClientAppContainerSid = isolatedClientAppContainerSid;
        _identity = authenticatedIdentity ?? throw new ArgumentNullException(nameof(authenticatedIdentity));
        _identity.Validate();
        ArgumentNullException.ThrowIfNull(declaredCapabilities);
        _declaredCapabilities = new HashSet<string>(declaredCapabilities, StringComparer.Ordinal);
        _hostGrantedCapabilities = new HashSet<string>(
            hostGrantedCapabilities ?? [], StringComparer.Ordinal);
        _hostEffectSink = contextualHostEffectSink ?? (hostEffectSink is null
            ? null
            : (effect, _, _) => hostEffectSink(effect));
        _actionExecutionAdmission = actionExecutionAdmission;
        _diagnosticSink = diagnosticSink;
        _options = options ?? new BrokerPipeTransportOptions();
        _options.Validate();
        ChannelNonce = channelNonce ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (ChannelNonce.Length is < 32 or > 128 ||
            !ChannelNonce.All(character => char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Broker channel nonce is invalid.", nameof(channelNonce));
        var artworkSession = appLibraryArtworkRegistry?.BeginSession(_identity, backend);
        try
        {
            _broker = new PlatformCapabilityBroker(
                _identity, _declaredCapabilities, consentStore, backend,
                _hostGrantedCapabilities, AppLibrarySavedIdIssuer.Shared, artworkSession, marketplace, notifications, activity, webSessions);
        }
        catch
        {
            artworkSession?.Dispose();
            throw;
        }
        _consentMonitor = new ConsentChangeMonitor(
            consentStore, _broker.RefreshConsentAsync, _broker.RevokeSubscriptions);
    }

    public string ChannelNonce { get; }
    public BrokerWidgetIdentity AuthenticatedIdentity => _identity;
    public IReadOnlySet<string> DeclaredCapabilities =>
        new HashSet<string>(_declaredCapabilities, StringComparer.Ordinal);
    public IReadOnlySet<string> HostGrantedCapabilities =>
        new HashSet<string>(_hostGrantedCapabilities, StringComparer.Ordinal);

    /// <summary>
    /// Binds an AppContainer broker endpoint to the worker process created by
    /// the trusted host. This must happen before <see cref="RunAsync"/>.
    /// </summary>
    public void BindExpectedIsolatedClientProcess(int processId)
    {
        if (_isolatedClientAppContainerSid is null) throw new InvalidOperationException("Endpoint is not isolated.");
        BindExpectedClientProcess(processId);
    }

    /// <summary>Bind a capability channel to the exact host-launched process before accepting it.</summary>
    public void BindExpectedClientProcess(int processId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        if (expectedIsolatedClientProcessId > 0)
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
        if (message.CorrelationId <= 0 ||
            message.ActionExecutionId is <= 0 ||
            _requests.Count >= _options.MaximumInFlightRequests)
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
        long? actionLifecycleGeneration = null;
        if (message.ActionExecutionId is { } executionId &&
            _actionExecutionAdmission is not null)
        {
            actionLifecycleGeneration = _actionExecutionAdmission(executionId);
            if (actionLifecycleGeneration is null)
            {
                _ = SendErrorSafeAsync(
                    message.CorrelationId, "stale_action_context", cancellationToken);
                return;
            }
        }
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        requestCancellation.CancelAfter(
            BrokerPipeRequestTimeoutPolicy.Resolve(_options, request));
        if (!_requests.TryAdd(message.CorrelationId, requestCancellation))
        {
            requestCancellation.Dispose();
            _ = SendErrorSafeAsync(message.CorrelationId, "duplicate_request", cancellationToken);
            return;
        }
        var taskId = Interlocked.Increment(ref _taskId);
        var task = RunRequestAsync(
            message.CorrelationId,
            requestBytes,
            message.ActionExecutionId,
            actionLifecycleGeneration,
            requestCancellation);
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
        long? actionExecutionId,
        long? actionLifecycleGeneration,
        CancellationTokenSource requestCancellation)
    {
        var request = BrokerJson.ParseRequest(requestBytes);
        var initiatedAt = Environment.TickCount64;
        try
        {
            var response = await _broker.HandleAsync(requestBytes, requestCancellation.Token)
                .ConfigureAwait(false);
            ReportDiagnostic(
                request.CapabilityId,
                request.Operation,
                "request",
                response.Succeeded ? null : response.ErrorCode ?? "broker_rejected");
            _ = BrokerJson.SerializeResponse(response);
            var effect = response.Succeeded
                ? ConfirmedHostEffect(requestBytes, response)
                : null;
            if (effect is not null) effect = effect with { InitiatedAtMilliseconds = initiatedAt };
            if (effect is not null && actionExecutionId is not null)
                PublishHostEffect(
                    effect, actionExecutionId, actionLifecycleGeneration);
            await SendAsync(BrokerPipeMessageTypes.Response, correlationId, response, _lifetime.Token)
                .ConfigureAwait(false);
            if (effect is not null && actionExecutionId is null)
                PublishHostEffect(effect, actionExecutionId, lifecycleGeneration: null);
        }
        catch (OperationCanceledException)
        {
            ReportDiagnostic(
                request.CapabilityId, request.Operation, "request", "request_canceled");
            await SendErrorSafeAsync(correlationId, "request_canceled", _lifetime.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _requests.TryRemove(correlationId, out _);
            requestCancellation.Dispose();
        }
    }

    private BrokerHostEffect? ConfirmedHostEffect(
        byte[] requestBytes,
        BrokerResponseEnvelope response)
    {
        var request = BrokerJson.ParseRequest(requestBytes);
        if (request.CapabilityId == PlatformCapabilities.TaskWindowsSwitchV1 &&
            request.Operation == PlatformCapabilities.TaskWindowsSwitch)
        {
            var window = BrokerJson.ParsePayload<TaskWindowRequest>(request.Payload);
            var target = WindowPreviewRegistry.Resolve(_identity, window.WindowId);
            return target is null ? null : new BrokerHostEffect(BrokerHostEffectKind.ActivateTaskWindow)
            {
                WindowId = window.WindowId,
                WindowTarget = target,
            };
        }
        if (request.Operation is not (PlatformCapabilities.AppLibraryLaunch or
            PlatformCapabilities.AppLibraryLaunchObserved)) return null;
        var launch = BrokerJson.ParsePayload<LaunchAppLibraryItemRequest>(request.Payload);
        if (!launch.CloseOverlayOnSuccess) return null;
        if (request.Operation == PlatformCapabilities.AppLibraryLaunchObserved)
        {
            if (response.Payload is not { } payload) return null;
            var observation = BrokerJson.ParsePayload<AppLibraryLaunchObservationSummary>(payload);
            if (observation.State == AppLibraryLaunchObservationState.RequestAccepted)
                return null;
        }
        return new BrokerHostEffect(BrokerHostEffectKind.CloseOverlayAfterAppLaunch);
    }

    private void PublishHostEffect(
        BrokerHostEffect effect,
        long? actionExecutionId,
        long? lifecycleGeneration)
    {
        if (_hostEffectSink is null) return;
        try
        {
            _hostEffectSink(effect, actionExecutionId, lifecycleGeneration);
        }
        catch (Exception)
        {
            // The privileged operation and its response already completed.
            // A host presentation callback must never poison the broker channel.
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
        BrokerEventSubscription subscription;
        try
        {
            subscription = await _broker.SubscribeAsync(
                request.CapabilityId, request.EventType, cancellationToken).ConfigureAwait(false);
            ReportDiagnostic(
                request.CapabilityId, request.EventType, "subscription-open", code: null);
        }
        catch (BrokerException exception)
        {
            ReportDiagnostic(
                request.CapabilityId, request.EventType, "subscription-open", exception.Code);
            throw;
        }
        var remote = new RemoteSubscription(
            request.SubscriptionId,
            request.CapabilityId,
            request.EventType,
            subscription,
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
                ReportDiagnostic(
                    remote.CapabilityId, remote.EventType, "subscription-read", code: null);
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
            ReportDiagnostic(
                remote.CapabilityId, remote.EventType, "subscription-read", exception.Code);
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
        catch (BrokerException exception)
        {
            ReportDiagnostic(
                remote.CapabilityId, remote.EventType, "subscription-read", exception.Code);
        }
        catch (IOException)
        {
            ReportDiagnostic(
                remote.CapabilityId, remote.EventType, "subscription-read", "channel_closed");
        }
        catch (OperationCanceledException) when (remote.Cancellation.IsCancellationRequested) { }
    }

    private void ReportDiagnostic(
        string capabilityId,
        string operationId,
        string stage,
        string? code)
    {
        if (_diagnosticSink is null) return;
        try
        {
            _diagnosticSink(new BrokerCapabilityDiagnostic(
                capabilityId, operationId, stage, code));
        }
        catch
        {
            // Diagnostics are advisory and cannot poison capability transport.
        }
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
        string capabilityId,
        string eventType,
        BrokerEventSubscription subscription,
        CancellationTokenSource cancellation) : IAsyncDisposable
    {
        public string Id { get; } = id;
        public string CapabilityId { get; } = capabilityId;
        public string EventType { get; } = eventType;
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
