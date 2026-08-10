using System.Text.Json;

namespace GameBarAlternative.PlatformBroker;

internal enum BrokerCapabilityDomain
{
    Audio,
    Network,
    AppLibrary,
    MediaSpotify,
    Loopback,
    PrivateSecrets,
    PrivateState,
}

internal static class BrokerCapabilityDomains
{
    internal static BrokerCapabilityDomain Resolve(string capabilityId)
    {
        if (PlatformCapabilities.TryGetLoopbackPort(capabilityId, out _))
            return BrokerCapabilityDomain.Loopback;
        if (capabilityId.StartsWith("system.audio.", StringComparison.Ordinal))
            return BrokerCapabilityDomain.Audio;
        if (capabilityId.StartsWith("system.network.", StringComparison.Ordinal) ||
            capabilityId == PlatformCapabilities.RecentActivityReadV1)
            return BrokerCapabilityDomain.Network;
        if (capabilityId is PlatformCapabilities.AppLibraryReadV1 or
            PlatformCapabilities.AppLibraryLaunchV1)
            return BrokerCapabilityDomain.AppLibrary;
        if (capabilityId.StartsWith("system.media.", StringComparison.Ordinal) ||
            capabilityId.StartsWith("external.spotify.", StringComparison.Ordinal))
            return BrokerCapabilityDomain.MediaSpotify;
        if (capabilityId == PlatformCapabilities.PrivateSecretsV1)
            return BrokerCapabilityDomain.PrivateSecrets;
        if (capabilityId == PlatformCapabilities.PrivateStateV1)
            return BrokerCapabilityDomain.PrivateState;
        throw new BrokerException(
            "unsupported_operation", "Broker capability domain is unsupported.");
    }

    internal static void DemandEmptyPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Any())
            throw new BrokerException(
                "invalid_payload", "Operation payload must be an empty object.");
    }

    internal static JsonElement Acknowledged() =>
        BrokerJson.ToElement(new { acknowledged = true });
}
