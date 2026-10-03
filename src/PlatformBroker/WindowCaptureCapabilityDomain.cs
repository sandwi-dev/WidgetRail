using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed class WindowCaptureCapabilityDomain(BrokerWidgetIdentity identity) : IDisposable
{
    internal JsonElement Execute(string operation, JsonElement payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registry = WindowCaptureRegistry.Shared;
        switch (operation)
        {
            case PlatformCapabilities.WindowCaptureRequest:
                var request = BrokerJson.ParsePayload<WindowCaptureRequest>(payload);
                if (!Enum.IsDefined(request.Kind)) throw new BrokerException("invalid_payload", "Capture kind is invalid.");
                return BrokerJson.ToElement(registry.Enqueue(this, identity, request.Kind));
            case PlatformCapabilities.WindowCaptureStatus:
                return BrokerJson.ToElement(registry.Status(this, BrokerJson.ParsePayload<WindowCaptureTicket>(payload).RequestId));
            case PlatformCapabilities.WindowCaptureCancel:
                registry.Cancel(this, BrokerJson.ParsePayload<WindowCaptureTicket>(payload).RequestId);
                return BrokerCapabilityDomains.Acknowledged();
            case PlatformCapabilities.WindowCaptureDiscard:
                registry.Discard(this, BrokerJson.ParsePayload<CaptureAttachmentRequest>(payload).AttachmentId);
                return BrokerCapabilityDomains.Acknowledged();
            case PlatformCapabilities.WindowCaptureRead:
                return BrokerJson.ToElement(registry.Read(this, BrokerJson.ParsePayload<CaptureReadRequest>(payload)));
            default: throw new BrokerException("unsupported_operation", "Capture operation is unsupported.");
        }
    }
    internal HostCaptureAttachment ResolveForHost(CaptureAttachment attachment) => WindowCaptureRegistry.Shared.ResolveOwned(this, attachment);
    public void Dispose() => WindowCaptureRegistry.Shared.Retire(this);
}
