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
            case BridgeMessageTypes.TakeCapture:
                var pending = captures.Take();
                var (catalog, _) = _registry.CatalogSnapshot();
                var widget = pending is null ? null : catalog.Widgets.FirstOrDefault(item => item.InstanceId == pending.Widget.InstanceId);
                if (pending is not null && (widget is null || !await CaptureAllowedAsync(pending.Widget, token).ConfigureAwait(false)))
                { captures.Complete(new(pending.RequestId, 0, 0, 0, "permission_denied")); pending = null; }
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureTake(pending, widget?.Id), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CheckCapture:
                var check = BridgeJson.FromElement<BridgeCaptureReference>(request.Payload);
                var live = captures.IsCurrent(check.RequestId);
                var current = live ? captures.Take() : null;
                live = live && current?.RequestId == check.RequestId && await CaptureAllowedAsync(current.Widget, token).ConfigureAwait(false);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(live), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.RecordingCapture:
                captures.Recording(BridgeJson.FromElement<BridgeCaptureReference>(request.Payload).RequestId);
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(true), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.CompleteCapture:
                var completion = BridgeJson.FromElement<HostCaptureCompletion>(request.Payload);
                var accepted = captures.IsCurrent(completion.RequestId);
                if (accepted)
                {
                    // Names come from the same provider as Task Switcher. The capture's
                    // identity remains fixed even if foreground order changes afterward.
                    var source = completion.ErrorCode is null && completion.Target is { } captured
                        ? await ResolveCaptureApplicationAsync(captured, token).ConfigureAwait(false) : null;
                    accepted = captures.IsCurrent(completion.RequestId);
                    if (accepted) captures.Complete(completion with { SourceApplication = source });
                }
                await ReplyAsync(request.Type, request.RequestId, new BridgeCaptureCurrent(accepted), token).ConfigureAwait(false);
                break;
            case BridgeMessageTypes.ResolveCaptureAttachment:
                var attachment = BridgeJson.FromElement<BridgeCaptureAttachmentRequest>(request.Payload);
                if (!_registry.IsCaptureWorkerCurrent(attachment.WidgetId, attachment.WorkerRun)) throw new BrokerException("capture_unavailable", "Capture worker retired.");
                var (widgets, _) = _registry.CatalogSnapshot();
                var configured = widgets.GetConfigured(attachment.WidgetId);
                var identity = new BrokerWidgetIdentity(configured.PackageId, configured.PublisherId, configured.InstanceId);
                if (!await CaptureAllowedAsync(identity, token).ConfigureAwait(false)) throw new BrokerException("permission_denied", "Capture permission is unavailable.");
                if (!_registry.IsCaptureWorkerCurrent(attachment.WidgetId, attachment.WorkerRun)) throw new BrokerException("capture_unavailable", "Capture worker retired.");
                await ReplyAsync(request.Type, request.RequestId, captures.Resolve(identity, attachment.AttachmentId), token).ConfigureAwait(false);
                break;
        }
    }
    private async Task<CaptureApplicationContext?> ResolveCaptureApplicationAsync(NativeWindowPreviewTarget captured, CancellationToken token)
    {
        if (_platformBackend is null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var windows = await _platformBackend.GetTaskWindowsAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return windows is null ? null : CaptureApplicationMetadata.Resolve(captured, windows);
        }
        catch (Exception error) when (error is not OutOfMemoryException && !token.IsCancellationRequested)
        { return null; } // Missing metadata must not discard a successful capture.
    }

    private async Task<bool> CaptureAllowedAsync(BrokerWidgetIdentity identity, CancellationToken token)
    {
        var (catalog, _) = _registry.CatalogSnapshot();
        var descriptor = catalog.Widgets.FirstOrDefault(item => item.InstanceId == identity.InstanceId);
        if (descriptor is null || _consentStore is null) return false;
        var configured = catalog.GetConfigured(descriptor.Id);
        return configured.PackageId == identity.PackageId && configured.PublisherId == identity.PublisherId &&
            configured.DeclaredCapabilities.Contains(PlatformCapabilities.WindowCaptureV1) &&
            await _consentStore.GetDecisionAsync(identity, PlatformCapabilities.WindowCaptureV1, token).ConfigureAwait(false) == ConsentDecision.Grant;
    }
}
