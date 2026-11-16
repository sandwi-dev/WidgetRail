using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

internal sealed class WindowCaptureCapabilityDomain(BrokerWidgetIdentity identity, CaptureLibraryStore? library = null,
    WebSessionRegistry.Owner? webSessionOwner = null) : IDisposable
{
    internal async Task<JsonElement> ExecuteAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if(operation is "capture.web-prepare" or "capture.web-prepare-constrained")
        {
            var preparation=BrokerJson.ParsePayload<WebCaptureStreamRequest>(payload);
            if((preparation.AllowedQualities is not null)!=(operation=="capture.web-prepare-constrained"))
                throw new BrokerException("invalid_payload","Capture quality restrictions require the constrained operation.");
            return BrokerJson.ToElement(await WebSessionRegistry.Shared.PrepareWebCaptureAsync(this,identity,
                preparation,token,SessionOwner()).ConfigureAwait(false));
        }
        if(operation=="capture.web-status")
            return BrokerJson.ToElement(WebSessionRegistry.Shared.WebCaptureStatus(this,identity,BrokerJson.ParsePayload<WebCaptureStreamTicket>(payload),SessionOwner()));
        if(operation=="capture.web-stop")
        {
            await WebSessionRegistry.Shared.StopWebCaptureAsync(this,identity,BrokerJson.ParsePayload<WebCaptureStreamTicket>(payload),token,SessionOwner()).ConfigureAwait(false);
            return BrokerCapabilityDomains.Acknowledged();
        }
        if (operation == "capture.recording-capabilities")
        {
            return BrokerJson.ToElement(await WindowCaptureRegistry.Shared.GetRecordingCapabilitiesAsync(
                BrokerJson.ParsePayload<CaptureRecordingQuery>(payload), token).ConfigureAwait(false));
        }
        if (operation == "capture.capabilities")
        {
            BrokerCapabilityDomains.DemandEmptyPayload(payload);
            await WindowCaptureRegistry.Shared.GetCapabilitiesAsync(token).ConfigureAwait(false);
        }
        if (operation is "capture.validate" or "capture.start")
        {
            var options = BrokerJson.ParsePayload<CaptureSessionRequest>(payload);
            if (!options.IsWellFormed())
            {
                if (operation == "capture.validate") return BrokerJson.ToElement(new CaptureConfigurationValidation(false, "invalid_capture_options"));
                throw new BrokerException("invalid_capture_options", "Invalid capture options.");
            }
            var captureRegistry = WindowCaptureRegistry.Shared;
            var available = options.Recording is { } video
                ? await captureRegistry.GetRecordingCapabilitiesAsync(new(video), token).ConfigureAwait(false)
                : await captureRegistry.GetCapabilitiesAsync(token, includeEncoding: false).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return operation == "capture.validate" ? BrokerJson.ToElement(available.Validate(options)) :
                BrokerJson.ToElement(captureRegistry.EnqueueConfigured(this, identity, options, available));
        }
        if (!operation.StartsWith("capture-library.", StringComparison.Ordinal)) return Execute(operation, payload, token);
        if (library is null) throw new BrokerException("capture_library_unavailable", "Capture Library is unavailable.");
        var registry = WindowCaptureRegistry.Shared;
        try
        {
            switch (operation)
            {
                case "capture-library.sources":
                    BrokerCapabilityDomains.DemandEmptyPayload(payload);
                    return BrokerJson.ToElement(await library.SourcesAsync(identity, token).ConfigureAwait(false));
                case "capture-library.choose-source":
                    var sourceRequest = BrokerJson.ParsePayload<CaptureLibraryChooseSourceRequest>(payload);
                    if (!CaptureLibrarySourceLimits.ValidLabel(sourceRequest.Label)) throw new BrokerException("invalid_payload", "Invalid capture source label.");
                    var sourceFolder = await CaptureLibrarySourceRequests.ChooseAsync(identity, sourceRequest.Label, token).ConfigureAwait(false);
                    return BrokerJson.ToElement(new CaptureLibrarySourceChoice(sourceFolder is null ? null : await library.AddSourceAsync(sourceFolder, sourceRequest.Label, identity, token).ConfigureAwait(false)));
                case "capture-library.remove-source":
                    await library.RemoveSourceAsync(BrokerJson.ParsePayload<CaptureLibrarySourceReference>(payload).SourceId, identity, token).ConfigureAwait(false);
                    return BrokerCapabilityDomains.Acknowledged();
                case "capture-library.link":
                    return BrokerJson.ToElement(await library.LinkAsync(BrokerJson.ParsePayload<CaptureLibraryLinkRequest>(payload), identity, token).ConfigureAwait(false));
                case "capture-library.remove-link":
                    await library.RemoveLinkAsync(BrokerJson.ParsePayload<CaptureLibraryMutationReference>(payload), token).ConfigureAwait(false);
                    return BrokerCapabilityDomains.Acknowledged();
                case "capture-library.copy":
                    var copy = BrokerJson.ParsePayload<CaptureLibraryCopyRequest>(payload);
                    var copyItems = CaptureLibraryStore.ValidateCopy(copy);
                    var destination = await CaptureLibraryCopyRequests.ChooseAsync(identity, copyItems.Length, token).ConfigureAwait(false);
                    return BrokerJson.ToElement(destination is null
                        ? new CaptureLibraryBatchResult(copyItems.Select(item => new CaptureLibraryMutationResult(item.Id, ErrorCode: "cancelled")).ToArray())
                        : await library.CopyAsync(new(copyItems), destination, token).ConfigureAwait(false));
                case "capture-library.remove-unavailable":
                    await library.RemoveUnavailableAsync(BrokerJson.ParsePayload<CaptureLibraryMutationReference>(payload), token).ConfigureAwait(false);
                    return BrokerCapabilityDomains.Acknowledged();
                case "capture-library.metadata-batch":
                    return BrokerJson.ToElement(await library.UpdateMetadataBatchAsync(BrokerJson.ParsePayload<CaptureLibraryBatchMetadataRequest>(payload), token).ConfigureAwait(false));
                case "capture-library.recycle":
                    return BrokerJson.ToElement(await library.RecycleAsync(BrokerJson.ParsePayload<CaptureLibraryRecycleRequest>(payload), token).ConfigureAwait(false));
                case "capture-library.reveal":
                    var reveal = await library.RevealPathAsync(BrokerJson.ParsePayload<CaptureLibraryReference>(payload).Id, token).ConfigureAwait(false);
                    if (!await CaptureLibraryFolderRequests.OpenAsync(identity, Path.GetDirectoryName(reveal)!, token, Path.GetFileName(reveal)).ConfigureAwait(false))
                        throw new BrokerException("capture_library_folder_unavailable", "Windows could not reveal the capture.");
                    return BrokerCapabilityDomains.Acknowledged();
                case "capture-library.groups":
                    return BrokerJson.ToElement(await library.GroupsAsync(BrokerJson.ParsePayload<CaptureLibraryGroupsRequest>(payload), token).ConfigureAwait(false));
                case "capture-library.applications":
                    return BrokerJson.ToElement(await library.ApplicationsAsync(BrokerJson.ParsePayload<CaptureLibraryApplicationsRequest>(payload), token).ConfigureAwait(false));
                case "capture-library.get-metadata":
                    return BrokerJson.ToElement(await library.GetMetadataAsync(BrokerJson.ParsePayload<CaptureLibraryReference>(payload).Id, token).ConfigureAwait(false));
                case "capture-library.query":
                    return BrokerJson.ToElement(await library.QueryAsync(BrokerJson.ParsePayload<CaptureLibraryQueryRequest>(payload), token).ConfigureAwait(false));
                case "capture-library.find":
                    return BrokerJson.ToElement(await library.FindAsync(BrokerJson.ParsePayload<CaptureLibraryFindRequest>(payload), token).ConfigureAwait(false));
                case "capture-library.metadata":
                    return BrokerJson.ToElement(await library.UpdateMetadataAsync(BrokerJson.ParsePayload<CaptureLibraryMetadataRequest>(payload), token).ConfigureAwait(false));
                case "capture-library.list":
                    var list = BrokerJson.ParsePayload<CaptureLibraryListRequest>(payload);
                    var legacyPage = await library.ListAsync(list.Offset, token, list.Refresh).ConfigureAwait(false);
                    return BrokerJson.ToElement(new { Items = legacyPage.Items.Select(LegacyItem).ToArray(), legacyPage.Offset, legacyPage.TotalCount, legacyPage.StoredBytes });
                case "capture-library.open-folder":
                    BrokerCapabilityDomains.DemandEmptyPayload(payload);
                    if (!await CaptureLibraryFolderRequests.OpenAsync(identity,await library.FolderAsync(token).ConfigureAwait(false),token).ConfigureAwait(false))
                        throw new BrokerException("capture_library_folder_unavailable","Windows could not open the captures folder.");
                    return BrokerCapabilityDomains.Acknowledged();
                case "capture-library.thumbnail":
                    var thumbnailRequest = BrokerJson.ParsePayload<CaptureLibraryThumbnailRequest>(payload);
                    if (thumbnailRequest.Offset is < 0 or >= CaptureLibraryLimits.MaximumThumbnailBytes) throw new BrokerException("invalid_payload", "Invalid thumbnail offset.");
                    var thumbnail = await library.ThumbnailAsync(thumbnailRequest.Id, token).ConfigureAwait(false);
                    if (thumbnailRequest.Offset >= thumbnail.Bytes.Length) throw new BrokerException("invalid_payload", "Invalid thumbnail offset.");
                    return BrokerJson.ToElement(new CaptureLibraryThumbnailChunk(thumbnail.Width, thumbnail.Height, thumbnail.Bytes.Length, thumbnailRequest.Offset,
                        thumbnail.Bytes.AsSpan(thumbnailRequest.Offset, Math.Min(CaptureLimits.ChunkBytes, thumbnail.Bytes.Length - thumbnailRequest.Offset)).ToArray()));
                case "capture-library.contexts":
                    return BrokerJson.ToElement(await library.GetContextsAsync(BrokerJson.ParsePayload<CaptureLibraryContextsRequest>(payload), token).ConfigureAwait(false));
                case "capture-library.save-context":
                    var contextualSave = BrokerJson.ParsePayload<CaptureLibrarySaveWithContextRequest>(payload);
                    if (contextualSave.Attachment?.IsWellFormed() != true || contextualSave.Context?.IsWellFormed() != true) throw new BrokerException("invalid_payload", "Invalid capture context.");
                    var receipt = await library.FindSavedContextAsync(identity, contextualSave.Context, token).ConfigureAwait(false);
                    return BrokerJson.ToElement(receipt ?? await library.SaveWithContextAsync(registry.ResolveOwned(this, contextualSave.Attachment), identity, contextualSave.Context, token).ConfigureAwait(false));
                case "capture-library.save":
                case "capture-library.save-current":
                    var save = BrokerJson.ParsePayload<CaptureLibrarySaveRequest>(payload);
                    if (save.Attachment is null || !save.Attachment.IsWellFormed()) throw new BrokerException("invalid_payload", "Invalid capture reference.");
                    var saved = await library.SaveAsync(registry.ResolveOwned(this, save.Attachment), identity, token).ConfigureAwait(false);
                    return operation == "capture-library.save" ? BrokerJson.ToElement(LegacyItem(saved)) : BrokerJson.ToElement(saved);
                case "capture-library.open":
                    var open = BrokerJson.ParsePayload<CaptureLibraryOpenRequest>(payload);
                    var media = await library.OpenAsync(open.Id, open.Resolution, token).ConfigureAwait(false);
                    if (media.Media.ContentType == "video/mp4")
                    {
                        try { return BrokerJson.ToElement(registry.AdoptLibraryVideo(this, identity, media, token)); }
                        catch { await media.DisposeAsync().ConfigureAwait(false); throw; }
                    }
                    await using (media.ConfigureAwait(false))
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
        catch (IOException error) when ((error.HResult & 0xffff) is 39 or 112)
        { throw new BrokerException("capture_library_disk_full", "There is not enough disk space to save the capture."); }
        catch (Microsoft.Data.Sqlite.SqliteException error)
        { throw new BrokerException(error.SqliteErrorCode == 13 ? "capture_library_disk_full" : "capture_library_io_failed", "Capture Library could not update its index."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { throw new BrokerException("capture_library_io_failed", "Capture Library could not read or write its files. Check storage and retry."); }
    }
    // Independently packaged older clients reject unknown response members.
    // Enriched metadata belongs only to the new query/get/metadata operations.
    internal static object LegacyItem(CaptureLibraryItem item)
    {
        var media = item.Media;
        if (media.ContentType == "video/mp4" &&
            (media.ByteLength > CaptureLimits.MaximumAttachmentBytes || media.DurationSeconds > CaptureLimits.MaximumRecordingSeconds + .25 ||
             media.Properties is null && (media.Width > 1280 || media.Height > 720 || media.ByteLength > CaptureLimits.MaximumContextBytes || media.DurationSeconds > CaptureLimits.VideoSeconds + .25)))
            throw new BrokerException("capture_library_client_upgrade_required", "This recording needs an updated capture library client.");
        return new { item.Id, item.Media, item.SavedAtUnixMilliseconds, item.Available, item.Revision };
    }

    internal void Revoke(string capability)
    {
        WindowCaptureRegistry.Shared.Retire(this, capability);
        if(capability==PlatformCapabilities.WindowCaptureV1)WebSessionRegistry.Shared.RetireWebCaptures(this);
    }
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
    private WebSessionRegistry.Owner SessionOwner()=>webSessionOwner??throw new BrokerException("platform_unavailable","This worker has no hosted web session authority.");
    public void Dispose()
    { WindowCaptureRegistry.Shared.Retire(this);WebSessionRegistry.Shared.RetireWebCaptures(this); }
}
