using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetWorkerHost;

/// <summary>
/// Worker-owned adapter. Normal widget code receives only the transport-neutral
/// SDK interface. This is API encapsulation, not a sandbox boundary: arbitrary
/// same-process code can inspect its launch environment until AppContainer
/// isolation is implemented.
/// </summary>
internal sealed class BrokerWidgetCapabilityClient(BrokerPipeClient client)
    : IWidgetCapabilityClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly BrokerPipeClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public bool IsAvailable => true;

    public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(request);
        BrokerResponseEnvelope response;
        try
        {
            response = await _client.RequestAsync(
                operation.CapabilityId, operation.OperationId, request, cancellationToken)
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

    public async IAsyncEnumerable<TPayload> SubscribeAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platformEvent);
        await using var subscription = await OpenSubscriptionAsync(
            platformEvent.CapabilityId, platformEvent.EventType, cancellationToken)
            .ConfigureAwait(false);
        while (true)
        {
            var envelope = await ReadEventAsync(subscription, cancellationToken).ConfigureAwait(false);
            if (envelope.ProtocolVersion != BrokerJson.ProtocolVersion ||
                envelope.CapabilityId != platformEvent.CapabilityId ||
                envelope.EventType != platformEvent.EventType || envelope.Sequence <= 0)
                throw CapabilityFailure("malformed_event");
            yield return Deserialize<TPayload>(envelope.Payload, "malformed_event");
        }
    }

    private async Task<BrokerPipeEventSubscription> OpenSubscriptionAsync(
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
