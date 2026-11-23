using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private async Task HandleCaptureAsync(BridgeEnvelope request, CancellationToken token)
    {
        var captures = WindowCaptureRegistry.Shared;
        switch (request.Type)
        {
            case BridgeMessageTypes.CaptureCapabilities:
                var capabilities = BridgeJson.FromElement<BridgeCaptureCapabilities>(request.Payload);
                if (capabilities.DiscoveryId is { } discoveryId) captures.SetRecordingCapabilities(discoveryId, capabilities.Capabilities);
                else captures.SetCapabilities(capabilities.Capabilities, capabilities.EncodingProbed);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(true), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.TakeCapture:
                var pending = captures.Take();
                var (catalog, _) = _registry.CatalogSnapshot();
                var replay = _platformBackend?.CaptureReplay;
                var picker = replay?.PendingApproval;
                var pickerWidget = picker is null ? null : catalog.Widgets.FirstOrDefault(item => item.InstanceId == picker.Widget.InstanceId);
                if (picker is not null && (pickerWidget is null || !await CaptureAllowedAsync(picker.Widget, token).ConfigureAwait(false)))
                { replay!.CompleteApprovalPicker(picker.Id, null); picker = null; }
                var edit = CaptureLibraryEditRequests.Pending;
                var editWidget = edit is null ? null : catalog.Widgets.FirstOrDefault(item => item.InstanceId == edit.Widget.InstanceId);
                if (edit is not null && (editWidget is null || !await CaptureEditAllowedAsync(edit, token).ConfigureAwait(false)))
                { CaptureLibraryEditRequests.Cancel(edit.Id); edit = null; }
                var sourceFolder = CaptureLibrarySourceRequests.Pending;
                var sourceWidget = sourceFolder is null ? null : catalog.Widgets.FirstOrDefault(item => item.InstanceId == sourceFolder.Widget.InstanceId);
                if (sourceFolder is not null && (sourceWidget is null || !await CaptureAllowedAsync(sourceFolder.Widget, token, PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false)))
                { CaptureLibrarySourceRequests.Complete(sourceFolder.Id, null); sourceFolder = null; }
                var copy = CaptureLibraryCopyRequests.Pending;
                var copyWidget = copy is null ? null : catalog.Widgets.FirstOrDefault(item => item.InstanceId == copy.Widget.InstanceId);
                if (copy is not null && (copyWidget is null || !await CaptureAllowedAsync(copy.Widget, token, PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false)))
                { CaptureLibraryCopyRequests.Complete(copy.Id, null); copy = null; }
                var review = CaptureDestinationReviews.Pending;
                var reviewer = review is null ? null : catalog.Widgets.FirstOrDefault(item => item.InstanceId == review.Widget.InstanceId);
                if (review is not null && (reviewer is null || !await CaptureAllowedAsync(review.Widget, token, PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false)))
                { CaptureDestinationReviews.Complete(review.Id, false); review = null; }
                var folder = CaptureLibraryFolderRequests.Pending;
                var folderWidget = folder is null ? null : catalog.Widgets.FirstOrDefault(item=>item.InstanceId==folder.Widget.InstanceId);
                if(folder is not null && (folderWidget is null || !await CaptureAllowedAsync(folder.Widget,token,PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false)))
                { CaptureLibraryFolderRequests.Complete(folder.Id,false); folder=null; }
                var widget = pending is null ? null : catalog.Widgets.FirstOrDefault(item => item.InstanceId == pending.Widget.InstanceId);
                if (pending is not null && (widget is null || !await CaptureRequestAllowedAsync(pending, token).ConfigureAwait(false)))
                { await captures.FinishAsync(new(pending.RequestId, 0, 0, 0, "permission_denied"), token).ConfigureAwait(false); pending = null; }
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureTake(pending, widget?.Id) {
                    ApprovalPicker = picker, ApprovalWidgetId = pickerWidget?.Id,
                    DestinationReview = review, DestinationWidgetId = reviewer?.Id,
                    NeedsCapabilities = captures.NeedsCapabilities, NeedsEncodingCapabilities = captures.NeedsEncodingCapabilities,
                    Edit = edit, EditWidgetId = editWidget?.Id,
                    SourceFolder = sourceFolder, SourceWidgetId = sourceWidget?.Id, RecordingDiscoveries = captures.RecordingDiscoveries, CopyDestination = copy, CopyWidgetId = copyWidget?.Id, OpenFolder=folder, FolderWidgetId=folderWidget?.Id }, token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.DecideCaptureDestination:
                var decision = BridgeJson.FromElement<BridgeCaptureDestinationDecision>(request.Payload);
                var outstanding = CaptureDestinationReviews.Pending;
                if (outstanding?.Id == decision.Id)
                    CaptureDestinationReviews.Complete(decision.Id, decision.Approved &&
                        await CaptureAllowedAsync(outstanding.Widget, token, PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false));
                await ReplyAsync(request.Type, request.RequestId, new BridgeEmptyPayload(), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CheckCaptureEdit:
                var editCheck = BridgeJson.FromElement<BridgeCaptureReference>(request.Payload);
                var checkingEdit = CaptureLibraryEditRequests.ActiveRequest(editCheck.RequestId);
                if (checkingEdit?.Id == editCheck.RequestId && !await CaptureEditAllowedAsync(checkingEdit, token).ConfigureAwait(false))
                    CaptureLibraryEditRequests.Cancel(editCheck.RequestId);
                await ReplyAsync(request.Type, request.RequestId, CaptureLibraryEditRequests.Status(editCheck.RequestId), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CompleteCaptureEdit:
                var editComplete = BridgeJson.FromElement<BridgeCaptureEditCompletion>(request.Payload);
                var completingEdit = CaptureLibraryEditRequests.ActiveRequest(editComplete.Id);
                var editAllowed = completingEdit?.Id == editComplete.Id && await CaptureEditAllowedAsync(completingEdit, token).ConfigureAwait(false);
                var acceptedEdit = editAllowed && CaptureLibraryEditRequests.Complete(editComplete.Id, editComplete.Recipe);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(acceptedEdit), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CancelCaptureEdit:
                var editCancel = BridgeJson.FromElement<BridgeCaptureReference>(request.Payload);
                await ReplyAsync(request.Type, request.RequestId, CaptureLibraryEditRequests.Cancel(editCancel.RequestId), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CompleteReplayApproval:
                var approval = BridgeJson.FromElement<BridgeReplayApprovalChoice>(request.Payload);
                var approvalAccepted = _platformBackend?.CaptureReplay?.CompleteApprovalPicker(approval.Id, approval.Selected) == true;
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(approvalAccepted), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CompleteCaptureSource:
                var sourceResult = BridgeJson.FromElement<BridgeCaptureCopyDestination>(request.Payload);
                var sourcePending = CaptureLibrarySourceRequests.Pending;
                var sourceAllowed = sourcePending?.Id == sourceResult.Id && await CaptureAllowedAsync(sourcePending.Widget, token, PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false);
                CaptureLibrarySourceRequests.Complete(sourceResult.Id, sourceAllowed ? sourceResult.Destination : null);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(sourceAllowed), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CheckCaptureSource:
                var sourceCheck = BridgeJson.FromElement<BridgeCaptureReference>(request.Payload);
                var sourceChoosing = CaptureLibrarySourceRequests.Pending;
                var sourceCurrent = sourceChoosing?.Id == sourceCheck.RequestId && await CaptureAllowedAsync(sourceChoosing.Widget, token, PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false);
                if (!sourceCurrent && sourceChoosing?.Id == sourceCheck.RequestId) CaptureLibrarySourceRequests.Complete(sourceCheck.RequestId, null);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(sourceCurrent), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CompleteCaptureCopy:
                var copyResult = BridgeJson.FromElement<BridgeCaptureCopyDestination>(request.Payload);
                var copyPending = CaptureLibraryCopyRequests.Pending;
                var copyAllowed = copyPending?.Id == copyResult.Id && await CaptureAllowedAsync(copyPending.Widget, token, PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false);
                CaptureLibraryCopyRequests.Complete(copyResult.Id, copyAllowed ? copyResult.Destination : null);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(copyAllowed), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CheckCaptureCopy:
                var copyCheck = BridgeJson.FromElement<BridgeCaptureReference>(request.Payload);
                var choosing = CaptureLibraryCopyRequests.Pending;
                var copyCurrent = choosing?.Id == copyCheck.RequestId && await CaptureAllowedAsync(choosing.Widget, token, PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false);
                if (!copyCurrent && choosing?.Id == copyCheck.RequestId) CaptureLibraryCopyRequests.Complete(copyCheck.RequestId, null);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(copyCurrent), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CompleteCaptureFolder:
                var folderResult=BridgeJson.FromElement<BridgeCaptureFolderResult>(request.Payload);
                CaptureLibraryFolderRequests.Complete(folderResult.Id,folderResult.Opened);
                await ReplyAsync(request.Type,request.RequestId,new BridgeCaptureCurrent(true),token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CheckCaptureFolder:
                var folderCheck=BridgeJson.FromElement<BridgeCaptureReference>(request.Payload);
                var folderPending=CaptureLibraryFolderRequests.Pending;
                var folderCurrent=folderPending is not null && folderPending.Id==folderCheck.RequestId &&
                    await CaptureAllowedAsync(folderPending.Widget,token,PlatformCapabilities.CaptureLibraryManageV1).ConfigureAwait(false);
                await ReplyAsync(request.Type,request.RequestId,new BridgeCaptureCurrent(folderCurrent),token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CheckCapture:
                var check = BridgeJson.FromElement<BridgeCaptureReference>(request.Payload);
                var live = captures.IsCurrent(check.RequestId);
                var current = live ? captures.Take() : null;
                live = live && current?.RequestId == check.RequestId && await CaptureRequestAllowedAsync(current, token).ConfigureAwait(false);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(live) { StopRequested = live && captures.IsStopRequested(check.RequestId) }, token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.RecordingCapture:
                captures.Recording(BridgeJson.FromElement<BridgeCaptureReference>(request.Payload).RequestId);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(true), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CompleteCapture:
                var completion = BridgeJson.FromElement<HostCaptureCompletion>(request.Payload);
                var accepted = captures.IsCurrent(completion.RequestId);
                string? savedApplication = null;
                if (accepted)
                {
                    // Names come from the same provider as Task Switcher. The capture's
                    // identity remains fixed even if foreground order changes afterward.
                    var source = completion.ErrorCode is null && completion.Target is { } captured
                        ? await ResolveCaptureApplicationAsync(captured, token).ConfigureAwait(false) : null;
                    var finishing = captures.Take();
                    accepted = finishing?.RequestId == completion.RequestId;
                    if (accepted && finishing is { SaveToLibrary: true } && completion.ErrorCode is null && !await CaptureRequestAllowedAsync(finishing, token).ConfigureAwait(false))
                    {
                        await captures.FinishAsync(completion with { ErrorCode = "permission_denied" }, token).ConfigureAwait(false);
                        accepted = false;
                    }
                    else if (accepted)
                    {
                        accepted = await captures.FinishAsync(completion with { SourceApplication = source }, token, CaptureRequestAllowedAsync).ConfigureAwait(false);
                        if (accepted && finishing?.SaveToLibrary == true) savedApplication = source?.ApplicationName;
                    }
                }
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(accepted) { ApplicationName = savedApplication }, token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.ResolveCaptureAttachment:
                var attachment = BridgeJson.FromElement<BridgeCaptureAttachmentRequest>(request.Payload);
                if (!_registry.IsCaptureWorkerCurrent(attachment.WidgetId, attachment.WorkerRun)) throw new BrokerException("capture_unavailable", "Capture worker retired.");
                var (widgets, _) = _registry.CatalogSnapshot();
                var configured = widgets.GetConfigured(attachment.WidgetId);
                var identity = new BrokerWidgetIdentity(configured.PackageId, configured.PublisherId, configured.InstanceId);
                var resolved = captures.Resolve(identity, attachment.AttachmentId);
                if (!await CaptureAllowedAsync(identity, token, resolved.RequiredCapability).ConfigureAwait(false)) throw new BrokerException("permission_denied", "Capture permission is unavailable.");
                if (!_registry.IsCaptureWorkerCurrent(attachment.WidgetId, attachment.WorkerRun)) throw new BrokerException("capture_unavailable", "Capture worker retired.");
                await ReplyAsync(request.Type, request.RequestId, captures.Resolve(identity, attachment.AttachmentId), token).ConfigureAwait(false);
                break;
        }
    }
    private async Task<CaptureApplicationContext?> ResolveCaptureApplicationAsync(NativeWindowPreviewTarget captured, CancellationToken token)
    {
        var processName = CaptureApplicationMetadata.ResolveProcessName(captured);
        var fallback = processName is null ? null : new CaptureApplicationContext(processName, "") { ProcessName = processName };
        if (_platformBackend is null) return fallback;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var windows = await _platformBackend.GetTaskWindowsAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return windows is not null && CaptureApplicationMetadata.Resolve(captured, windows) is { } application
                ? application with { ProcessName = processName } : fallback;
        }
        catch (Exception error) when (error is not OutOfMemoryException && !token.IsCancellationRequested)
        { return fallback; } // A slow catalog must not discard a successful capture.
    }

    private async Task<bool> CaptureAllowedAsync(BrokerWidgetIdentity identity, CancellationToken token, string capability = PlatformCapabilities.WindowCaptureV1)
    {
        if (capability is not (PlatformCapabilities.WindowCaptureV1 or PlatformCapabilities.CaptureLibraryReadV1 or PlatformCapabilities.CaptureLibrarySaveV1 or PlatformCapabilities.CaptureLibraryManageV1)) return false;
        var (catalog, _) = _registry.CatalogSnapshot();
        var descriptor = catalog.Widgets.FirstOrDefault(item => item.InstanceId == identity.InstanceId);
        if (descriptor is null || _consentStore is null) return false;
        var configured = catalog.GetConfigured(descriptor.Id);
        return configured.PackageId == identity.PackageId && configured.PublisherId == identity.PublisherId &&
            configured.DeclaredCapabilities.Contains(capability) &&
            await _consentStore.GetDecisionAsync(identity, capability, token).ConfigureAwait(false) == ConsentDecision.Grant;
    }

    private async Task<bool> CaptureRequestAllowedAsync(HostWindowCapture request, CancellationToken token) =>
        await CaptureAllowedAsync(request.Widget, token).ConfigureAwait(false) && (!request.SaveToLibrary ||
        await CaptureAllowedAsync(request.Widget, token, PlatformCapabilities.CaptureLibrarySaveV1).ConfigureAwait(false));

    private async Task<bool> CaptureEditAllowedAsync(HostCaptureEditRequest edit, CancellationToken token) =>
        CaptureLibraryEditRequests.SourcePermission(edit.Id) is { } source &&
        await CaptureAllowedAsync(edit.Widget, token, PlatformCapabilities.CaptureLibrarySaveV1).ConfigureAwait(false) &&
        await CaptureAllowedAsync(edit.Widget, token, source).ConfigureAwait(false);
}
