using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed class WindowCaptureCapabilityDomain(BrokerWidgetIdentity identity, CaptureLibraryStore? library = null) : IDisposable
{
    internal async Task<JsonElement> ExecuteAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if (!operation.StartsWith("capture-library.", StringComparison.Ordinal)) return Execute(operation, payload, token);
        if (library is null) throw new BrokerException("capture_library_unavailable", "Capture Library is unavailable.");
        var registry = WindowCaptureRegistry.Shared;
        try
        {
            switch (operation)
            {
                case "capture-library.list": return BrokerJson.ToElement(await library.ListAsync(BrokerJson.ParsePayload<CaptureLibraryListRequest>(payload).Offset, token).ConfigureAwait(false));
                case "capture-library.save":
                    var save = BrokerJson.ParsePayload<CaptureLibrarySaveRequest>(payload);
                    if (save.Attachment is null || !save.Attachment.IsWellFormed()) throw new BrokerException("invalid_payload", "Invalid capture reference.");
                    return BrokerJson.ToElement(await library.SaveAsync(registry.ResolveOwned(this, save.Attachment), token).ConfigureAwait(false));
                case "capture-library.open":
                    var open = BrokerJson.ParsePayload<CaptureLibraryOpenRequest>(payload);
                    await using (var media = await library.OpenAsync(open.Id, open.Resolution, token).ConfigureAwait(false))
                        return BrokerJson.ToElement(await registry.ImportAsync(this, identity, media, token).ConfigureAwait(false));
                case "capture-library.read": return BrokerJson.ToElement(registry.Read(this, BrokerJson.ParsePayload<CaptureReadRequest>(payload), PlatformCapabilities.CaptureLibraryReadV1));
                case "capture-library.release": registry.Discard(this, BrokerJson.ParsePayload<CaptureAttachmentRequest>(payload).AttachmentId, PlatformCapabilities.CaptureLibraryReadV1); return BrokerCapabilityDomains.Acknowledged();
                case "capture-library.delete": await library.DeleteAsync(BrokerJson.ParsePayload<CaptureLibraryReference>(payload).Id, token).ConfigureAwait(false); return BrokerCapabilityDomains.Acknowledged();
                case "capture-library.export": return BrokerJson.ToElement(await library.ExportAsync(BrokerJson.ParsePayload<CaptureLibraryReference>(payload).Id, token).ConfigureAwait(false));
                default: throw new BrokerException("unsupported_operation", "Capture Library operation is unsupported.");
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        { throw new BrokerException("capture_library_invalid_media", "Windows could not decode this saved capture."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { throw new BrokerException("capture_library_io_failed", "Capture Library could not read or write its files. Check storage and retry."); }
    }
    internal void Revoke(string capability) => WindowCaptureRegistry.Shared.Retire(this, capability);
    internal JsonElement Execute(string operation, JsonElement payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registry = WindowCaptureRegistry.Shared;
        switch (operation)
        {
            case "capture.capabilities":
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(registry.Capabilities);
            case "capture.validate":
                return BrokerJson.ToElement(registry.Capabilities.Validate(BrokerJson.ParsePayload<CaptureSessionRequest>(payload)));
            case "capture.start":
                return BrokerJson.ToElement(registry.EnqueueConfigured(this, identity, BrokerJson.ParsePayload<CaptureSessionRequest>(payload)));
            case "capture.stop":
                registry.Stop(this, BrokerJson.ParsePayload<WindowCaptureTicket>(payload).RequestId);
                return BrokerCapabilityDomains.Acknowledged();
            case "capture.request-original":
            case PlatformCapabilities.WindowCaptureRequest:
                var request = BrokerJson.ParsePayload<WindowCaptureRequest>(payload);
                if (operation == "capture.request-original" && (request.Kind != WindowCaptureKind.Screenshot || request.Resolution != CaptureImageResolution.Original)) throw new BrokerException("invalid_payload", "Original capture requires a screenshot.");
                if (!Enum.IsDefined(request.Kind)) throw new BrokerException("invalid_payload", "Capture kind is invalid.");
                return BrokerJson.ToElement(registry.Enqueue(this, identity, request.Kind, request.Resolution));
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
