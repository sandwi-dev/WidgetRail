using System.Text.Json;

namespace WidgetRail.PlatformBroker;

internal enum BrokerCapabilityDomain
{
    Displays,
    Power,
    Audio,
    Network,
    AppLibrary,
    Media,
    Loopback,
    PrivateSecrets,
    PrivateState,
    WindowCapture,
    ProviderDocuments,
    Performance,
    Permissions,
    Marketplace,
    Notifications,
    Activity,
    WebSessions,
}

internal static class BrokerCapabilityDomains
{
    internal static BrokerCapabilityDomain Resolve(string capabilityId)
    {
        if (capabilityId == PlatformCapabilities.WebSessionsV1) return BrokerCapabilityDomain.WebSessions;
        if (capabilityId == PlatformCapabilities.WidgetActivityV1) return BrokerCapabilityDomain.Activity;
        if (capabilityId == PlatformCapabilities.WidgetNotificationsV1) return BrokerCapabilityDomain.Notifications;
        if (capabilityId == PlatformCapabilities.WidgetPermissionsV1) return BrokerCapabilityDomain.Permissions;
        if (capabilityId == PlatformCapabilities.WidgetMarketplaceV1) return BrokerCapabilityDomain.Marketplace;
        if (capabilityId is PlatformCapabilities.PerformanceReadV1 or PlatformCapabilities.PerformanceFpsControlV1)
            return BrokerCapabilityDomain.Performance;
        if (capabilityId == PlatformCapabilities.ProviderDocumentsV1) return BrokerCapabilityDomain.ProviderDocuments;
        if (capabilityId is PlatformCapabilities.WindowCaptureV1 or PlatformCapabilities.CaptureLibraryReadV1 or PlatformCapabilities.CaptureLibrarySaveV1 or PlatformCapabilities.CaptureLibraryManageV1) return BrokerCapabilityDomain.WindowCapture;
        if (capabilityId is PlatformCapabilities.DisplaysReadV1 or PlatformCapabilities.DisplaysControlV1)
            return BrokerCapabilityDomain.Displays;
        if (capabilityId is PlatformCapabilities.PowerReadV1 or PlatformCapabilities.PowerControlV1)
            return BrokerCapabilityDomain.Power;
        if (PlatformCapabilities.TryGetLoopbackPort(capabilityId, out _))
            return BrokerCapabilityDomain.Loopback;
        if (capabilityId.StartsWith("system.audio.", StringComparison.Ordinal))
            return BrokerCapabilityDomain.Audio;
        if (capabilityId.StartsWith("system.network.", StringComparison.Ordinal) ||
            capabilityId == PlatformCapabilities.RecentActivityReadV1)
            return BrokerCapabilityDomain.Network;
        if (capabilityId is PlatformCapabilities.TaskWindowsPreviewV1 or PlatformCapabilities.TaskWindowsReadV1 or
            PlatformCapabilities.TaskWindowsSwitchV1 or PlatformCapabilities.TaskWindowsCloseV1 or
            PlatformCapabilities.AppLibraryReadV1 or
            PlatformCapabilities.AppRunningReadV1 or
            PlatformCapabilities.AppRunningRegisterV1 or
            PlatformCapabilities.AppLibraryLaunchV1)
            return BrokerCapabilityDomain.AppLibrary;
        if (capabilityId.StartsWith("system.media.", StringComparison.Ordinal))
            return BrokerCapabilityDomain.Media;
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
