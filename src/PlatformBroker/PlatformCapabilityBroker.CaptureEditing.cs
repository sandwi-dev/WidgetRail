using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class PlatformCapabilityBroker
{
    private async Task<JsonElement> ExecuteCaptureDomainAsync(BrokerRequestEnvelope request, RequestLease primary)
    {
        var token = primary.Token;
        if (primary.CaptureCommand is { } command)
        {
            if (request.Operation is "capture.save-screenshot" or "capture.record-clip" or "capture.replay.save")
            {
                var savePermission = await AuthorizeAsync(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save", token).ConfigureAwait(false);
                using var saving = RetainSaveGrant(primary, savePermission);
                return await _captureDomain.ExecuteAsync(request.Operation, request.Payload, saving.Token, command, request.GestureInputSequence, primary.HostCompletion).ConfigureAwait(false);
            }
            return await _captureDomain.ExecuteAsync(request.Operation, request.Payload, token, command).ConfigureAwait(false);
        }
        var libraryRecording=request.Operation=="capture.start" && BrokerJson.ParsePayload<CaptureSessionRequest>(request.Payload).OutputPurpose==CaptureOutputPurpose.LibraryRecording;
        var savingDraft=request.Operation=="capture-library.save-current" && BrokerJson.ParsePayload<CaptureLibrarySaveRequest>(request.Payload).Attachment?.Origin==CaptureAttachmentOrigin.LibraryRecordingDraft;
        if(libraryRecording || savingDraft || request.Operation is "capture.save-screenshot" or "capture.record-clip")
        {
            var required=savingDraft?PlatformCapabilities.WindowCaptureV1:PlatformCapabilities.CaptureLibrarySaveV1;
            var secondaryOperation=savingDraft?PlatformCapabilities.WindowCaptureRead:"capture-library.save";
            var permission=await AuthorizeAsync(required,secondaryOperation,token).ConfigureAwait(false);
            using var secondary=CreateRequestLease(permission,request with{CapabilityId=required,Operation=secondaryOperation},token);
            try
            {
                secondary.ThrowIfBrokerCanceled();
                return await _captureDomain.ExecuteAsync(request.Operation,request.Payload,secondary.Token).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(secondary.CancellationCode is {})
            {throw new BrokerException(secondary.CancellationCode!,"Recording draft permission was revoked.");}
        }
        if (request.Operation == "capture.replay.save")
        {
            var permission = await AuthorizeAsync(PlatformCapabilities.CaptureLibrarySaveV1, "capture-library.save", token).ConfigureAwait(false);
            // This secondary grant belongs to the already-admitted replay operation.
            // Keep its completion across focus loss while retaining normal initial
            // lifecycle admission, consent revocation and channel teardown checks.
            using var saving = CreateRequestLease(permission, request with { CapabilityId = PlatformCapabilities.CaptureLibrarySaveV1, Operation = "capture-library.save" }, token,
                continueAdmittedOperation: true);
            try
            {
                saving.ThrowIfBrokerCanceled();
                return await _captureDomain.ExecuteAsync(request.Operation, request.Payload, saving.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (saving.CancellationCode is { })
            { throw new BrokerException(saving.CancellationCode!, "Library save permission was revoked."); }
        }
        if (request.Operation != "capture-library.edit-copy")
            return await _captureDomain.ExecuteAsync(request.Operation, request.Payload, token).ConfigureAwait(false);
        var edit = BrokerJson.ParsePayload<CaptureLibraryEditRequest>(request.Payload);
        if (!edit.IsWellFormed()) throw new BrokerException("invalid_payload", "Choose one editor source.");
        // Retain BOTH consent leases through encoding/commit. Host polling supplements this;
        // it is not the authority for revoking source read access.
        var source = edit.Saved is not null ? PlatformCapabilities.CaptureLibraryReadV1 : PlatformCapabilities.WindowCaptureV1;
        var operation = edit.Saved is not null ? "capture-library.open" : PlatformCapabilities.WindowCaptureRead;
        var capability = await AuthorizeAsync(source, operation, token).ConfigureAwait(false);
        using var lease = CreateRequestLease(capability, request with { CapabilityId = source, Operation = operation }, token);
        try
        {
            lease.ThrowIfBrokerCanceled();
            return await _captureDomain.ExecuteAsync(request.Operation, request.Payload, lease.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lease.CancellationCode is { })
        { throw new BrokerException(lease.CancellationCode!, "Source permission was revoked while editing."); }
    }

    // The primary lease admits the exact capture operation. This second lease
    // observes save-consent revocation without granting a second background command.
    private RequestLease RetainSaveGrant(RequestLease primary, BrokerCapabilityDefinition permission)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            primary.ThrowIfBrokerCanceled();
            if (_revokedCapabilities.Contains(permission.Id)) throw new BrokerException("capability_revoked", "Save permission was revoked.");
            var lease = new RequestLease(this, permission.Id, BrokerCapabilityKind.Control, false, true, false, primary.Token);
            _requestLeases.Add(lease);
            return lease;
        }
    }
}
