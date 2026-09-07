using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>
/// Keeps broker transport details behind the transport-neutral SDK contract.
/// Widget code receives only <see cref="IWidgetCapabilityClient"/>.
/// </summary>
internal sealed class BrokerWidgetCapabilityClient :
    IWidgetCapabilityClient,
    IDashboardGestureActivatingCapabilityClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly BrokerPipeClient _client;
    private Func<WidgetCapabilityGestureContext, string, string, CancellationToken, ValueTask<bool>>?
        _dashboardGestureActivator;

    internal BrokerWidgetCapabilityClient(BrokerPipeClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    void IDashboardGestureActivatingCapabilityClient.SetDashboardGestureActivator(
        Func<WidgetCapabilityGestureContext, string, string, CancellationToken, ValueTask<bool>>
            activator) =>
        _dashboardGestureActivator = activator ?? throw new ArgumentNullException(nameof(activator));

    public bool IsAvailable => true;

    public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(request);
        var actionContext = WidgetActionInvocationContext.Current;
        var closeOnLaunch = IsCloseOnLaunch(operation, request);
        if (closeOnLaunch && actionContext is { IsActive: false })
            throw CapabilityFailure("stale_action_context");
        using var closeRequest = closeOnLaunch
            ? actionContext?.TryBeginCloseRequest()
            : null;
        if (actionContext is not null && closeOnLaunch &&
            closeRequest is null)
            throw CapabilityFailure("stale_action_context");
        BrokerResponseEnvelope response;
        try
        {
            var gesture = WidgetCapabilityInvocationContext.Current is { IsActive: true } active
                ? active
                : null;
            WidgetCapabilityGestureContext? activatedGesture = null;
            if (gesture is not null && _dashboardGestureActivator is not null)
            {
                var activated = await _dashboardGestureActivator(
                        gesture,
                        operation.CapabilityId,
                        operation.OperationId,
                        cancellationToken)
                    .ConfigureAwait(false);
                // The request was admitted while the exact physical gesture was
                // active.  Activation crosses the worker/host pipe, so the
                // widget action scope can legitimately finish before the host
                // returns the grant.  Rechecking the scope here would discard a
                // grant that was already bound to this invocation and downgrade
                // the request to ordinary lifecycle authority.
                if (activated) activatedGesture = gesture;
            }
            if (closeOnLaunch && actionContext is { IsActive: false })
                throw CapabilityFailure("stale_action_context");
            response = await _client.RequestWithActionAsync(
                operation.CapabilityId,
                operation.OperationId,
                request,
                activatedGesture?.InputSequence,
                activatedGesture?.SnapshotSequence,
                closeRequest?.ExecutionId,
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BrokerException exception)
        {
            throw CapabilityFailure(exception.Code);
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or
            ObjectDisposedException or ChannelClosedException)
        {
            throw CapabilityFailure("channel_closed");
        }

        if (response.ProtocolVersion != BrokerJson.ProtocolVersion)
            throw CapabilityFailure("unsupported_protocol");
        if (!response.Succeeded)
            throw CapabilityFailure(response.ErrorCode ?? "broker_rejected");
        if (response.Payload is not JsonElement payload)
            throw CapabilityFailure("malformed_response");
        return Deserialize<TResponse>(payload, "malformed_response");
    }

    private static bool IsCloseOnLaunch<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request) =>
        request is LaunchWidgetAppLibraryItemRequest { CloseOverlayOnSuccess: true } &&
        operation.CapabilityId == PlatformCapabilities.AppLibraryLaunchV1 &&
        operation.OperationId is (PlatformCapabilities.AppLibraryLaunch or
            PlatformCapabilities.AppLibraryLaunchObserved);

    public async ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platformEvent);
        var subscription = await OpenBrokerSubscriptionAsync(
            platformEvent.CapabilityId, platformEvent.EventType, cancellationToken)
            .ConfigureAwait(false);
        return new BrokerCapabilitySubscription<TPayload>(subscription, platformEvent);
    }

    private async Task<BrokerPipeEventSubscription> OpenBrokerSubscriptionAsync(
        string capabilityId, string eventType, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.SubscribeAsync(capabilityId, eventType, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BrokerException exception)
        {
            throw CapabilityFailure(exception.Code);
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or
            ObjectDisposedException or ChannelClosedException)
        {
            throw CapabilityFailure("channel_closed");
        }
    }

    private static async ValueTask<BrokerEventEnvelope> ReadEventAsync(
        BrokerPipeEventSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            return await subscription.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BrokerException exception)
        {
            throw CapabilityFailure(exception.Code);
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or
            ObjectDisposedException or ChannelClosedException)
        {
            throw CapabilityFailure("channel_closed");
        }
    }

    private static T Deserialize<T>(JsonElement payload, string errorCode)
    {
        try
        {
            return payload.Deserialize<T>(JsonOptions)
                ?? throw CapabilityFailure(errorCode);
        }
        catch (WidgetCapabilityException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw CapabilityFailure(errorCode);
        }
    }

    private sealed class BrokerCapabilitySubscription<TPayload>(
        BrokerPipeEventSubscription subscription,
        WidgetCapabilityEvent<TPayload> platformEvent)
        : IWidgetCapabilitySubscription<TPayload>
    {
        private readonly BrokerPipeEventSubscription _subscription = subscription;
        private readonly WidgetCapabilityEvent<TPayload> _platformEvent = platformEvent;
        private int _readerStarted;
        private int _disposed;

        public IAsyncEnumerable<TPayload> ReadAllAsync(
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Interlocked.Exchange(ref _readerStarted, 1) != 0)
                throw new InvalidOperationException("A capability subscription supports one event reader.");
            return ReadAllCoreAsync(cancellationToken);
        }

        private async IAsyncEnumerable<TPayload> ReadAllCoreAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (true)
            {
                var envelope = await ReadEventAsync(_subscription, cancellationToken).ConfigureAwait(false);
                if (envelope.ProtocolVersion != BrokerJson.ProtocolVersion ||
                    envelope.CapabilityId != _platformEvent.CapabilityId ||
                    envelope.EventType != _platformEvent.EventType || envelope.Sequence <= 0)
                    throw CapabilityFailure("malformed_event");
                yield return Deserialize<TPayload>(envelope.Payload, "malformed_event");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            await _subscription.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static WidgetCapabilityException CapabilityFailure(string errorCode) =>
        new(errorCode, $"The platform capability request failed ({errorCode}).");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = BrokerJson.MaximumDepth,
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
