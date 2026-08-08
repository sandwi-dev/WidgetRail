namespace GameBarAlternative.WidgetSdk;

/// <summary>
/// A typed, transport-neutral capability operation. Platform provider packages
/// publish reusable instances; widget authors should not construct ad-hoc IDs.
/// </summary>
public sealed record WidgetCapabilityOperation<TRequest, TResponse>(
    string CapabilityId,
    string OperationId);

/// <summary>A typed, coalesced platform event exposed by a provider package.</summary>
public sealed record WidgetCapabilityEvent<TPayload>(
    string CapabilityId,
    string EventType);

/// <summary>
/// An acknowledged platform-event subscription. A successful open means the
/// host has registered the subscription before returning, so callers may fetch
/// a current snapshot without losing events in the snapshot/subscription gap.
/// The event stream is single-consumer and coalescing semantics are defined by
/// the provider contract.
/// </summary>
public interface IWidgetCapabilitySubscription<TPayload> : IAsyncDisposable
{
    IAsyncEnumerable<TPayload> ReadAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The only OS-capability boundary visible to widget code. The implementation
/// lives in the worker bootstrap and communicates with an identity-bound host
/// broker. Transport credentials and OS objects are not exposed by this API;
/// process sandboxing is still required to prevent hostile same-process code
/// from inspecting its own launch environment.
/// </summary>
public interface IWidgetCapabilityClient
{
    bool IsAvailable { get; }

    ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens and acknowledges a subscription before returning. Widgets that
    /// combine current state with events should open first, fetch second, then
    /// drain events so changes during the fetch are reconciled afterward.
    /// </summary>
    ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compatibility stream for event-only consumers. Snapshot-plus-event
    /// consumers should prefer <see cref="OpenSubscriptionAsync{TPayload}"/>.
    /// </summary>
    IAsyncEnumerable<TPayload> SubscribeAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default) =>
        WidgetCapabilitySubscriptionCompatibility.ReadAsync(
            this, platformEvent, cancellationToken);
}

internal static class WidgetCapabilitySubscriptionCompatibility
{
    internal static async IAsyncEnumerable<TPayload> ReadAsync<TPayload>(
        IWidgetCapabilityClient client,
        WidgetCapabilityEvent<TPayload> platformEvent,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await using var subscription = await client.OpenSubscriptionAsync(
            platformEvent, cancellationToken).ConfigureAwait(false);
        await foreach (var item in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return item;
    }
}

public sealed class WidgetHostServices
{
    internal WidgetHostServices(IWidgetCapabilityClient capabilityClient)
    {
        Capabilities = capabilityClient ?? throw new ArgumentNullException(nameof(capabilityClient));
        Audio = new WidgetAudioService(capabilityClient);
        Network = new WidgetNetworkService(capabilityClient);
        RecentActivity = new WidgetRecentActivityService(capabilityClient);
    }

    public IWidgetCapabilityClient Capabilities { get; }
    public WidgetAudioService Audio { get; }
    public WidgetNetworkService Network { get; }
    public WidgetRecentActivityService RecentActivity { get; }

    internal static WidgetHostServices Unavailable { get; } =
        new(UnavailableWidgetCapabilityClient.Instance);
}

public sealed class WidgetCapabilityUnavailableException(string message) : Exception(message);

/// <summary>
/// A capability request was rejected by the authenticated host broker. Error
/// codes are stable machine-readable values; messages never contain broker
/// connection details or host filesystem paths.
/// </summary>
public sealed class WidgetCapabilityException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = string.IsNullOrWhiteSpace(errorCode)
        ? throw new ArgumentException("A capability error code is required.", nameof(errorCode))
        : errorCode;
}

internal sealed class UnavailableWidgetCapabilityClient : IWidgetCapabilityClient
{
    internal static UnavailableWidgetCapabilityClient Instance { get; } = new();
    public bool IsAvailable => false;

    public ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<TResponse>(new WidgetCapabilityUnavailableException(
            "This worker was not given an authenticated platform capability channel."));

    public ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<IWidgetCapabilitySubscription<TPayload>>(
            new WidgetCapabilityUnavailableException(
                "This worker was not given an authenticated platform capability channel."));
}
