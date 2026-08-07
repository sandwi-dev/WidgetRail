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

    IAsyncEnumerable<TPayload> SubscribeAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default);
}

public sealed class WidgetHostServices
{
    internal WidgetHostServices(IWidgetCapabilityClient capabilityClient)
    {
        Capabilities = capabilityClient ?? throw new ArgumentNullException(nameof(capabilityClient));
        Audio = new WidgetAudioService(capabilityClient);
        Network = new WidgetNetworkService(capabilityClient);
    }

    public IWidgetCapabilityClient Capabilities { get; }
    public WidgetAudioService Audio { get; }
    public WidgetNetworkService Network { get; }

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

    public async IAsyncEnumerable<TPayload> SubscribeAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new WidgetCapabilityUnavailableException(
            "This worker was not given an authenticated platform capability channel.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
