using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private readonly HostMediaPlaybackLeases mediaPlaybackLeases = new();
    private async Task HandleMediaPlayerAsync(BridgeEnvelope envelope, CancellationToken token)
    {
        if (envelope.Type == BridgeMessageTypes.ReleaseMediaPlayer)
        {
            await mediaPlaybackLeases.ReleaseAsync(BridgeJson.FromElement<BridgeMediaPlayerRelease>(envelope.Payload).PlaybackLeaseId).ConfigureAwait(false);
            await ReplyAsync(envelope.Type, envelope.RequestId, new BridgeCaptureCurrent(true), token).ConfigureAwait(false);
            return;
        }
        var request = BridgeJson.FromElement<BridgeMediaPlayerRequest>(envelope.Payload);
        using var admission = _registry.AdmitMediaPlayer(request);
        var widget = admission.Widget;
        HostMediaPlayerSource resolved;
        if (request.Media.Source.Kind == MediaPlayerSourceKind.SavedCapture)
        {
            var identity = new BrokerWidgetIdentity(widget.PackageId, widget.PublisherId, widget.InstanceId);
            if (!await CaptureAllowedAsync(identity, token, PlatformCapabilities.CaptureLibraryReadV1).ConfigureAwait(false))
            { if (request.PlaybackLeaseId is { } rejected) mediaPlaybackLeases.Release(rejected); throw new BrokerException("permission_denied", "Capture Library read permission is unavailable."); }
            resolved = await mediaPlaybackLeases.ResolveAsync(request, admission,
                _platformBackend?.CaptureLibrary ?? throw new BrokerException("capture_library_unavailable", "Capture Library is unavailable."), token).ConfigureAwait(false);
            if (!await CaptureAllowedAsync(identity, token, PlatformCapabilities.CaptureLibraryReadV1).ConfigureAwait(false))
            { mediaPlaybackLeases.Release(request.PlaybackLeaseId!); throw new BrokerException("permission_denied", "Capture Library read permission was revoked."); }
        }
        else if (request.Media.Source.Attachment is { } attachment)
        {
            var identity = new BrokerWidgetIdentity(widget.PackageId, widget.PublisherId, widget.InstanceId);
            var capture = WindowCaptureRegistry.Shared.Resolve(identity, attachment.Id);
            if (!await CaptureAllowedAsync(identity, token, capture.RequiredCapability).ConfigureAwait(false)) throw new BrokerException("permission_denied", "Capture permission is unavailable.");
            capture = WindowCaptureRegistry.Shared.Resolve(identity, attachment.Id);
            if (capture.Attachment != attachment) throw new BrokerException("capture_unavailable", "Capture metadata changed.");
            resolved = new(request.Media, new Uri(capture.Path).AbsoluteUri, attachment.ContentType == "image/png",
                attachment.Width, attachment.Height, attachment.DurationSeconds, capture.LeaseExpiresAtUnixMilliseconds ?? attachment.ExpiresAtUnixMilliseconds, RequiresRevalidation: true);
        }
        else resolved = await MediaPlayerSourceResolver.ResolveAsync(widget, request.Media, token).ConfigureAwait(false);
        if (!admission.IsCurrent)
        {
            if (request.PlaybackLeaseId is { } retired) mediaPlaybackLeases.Release(retired);
            throw new BridgeProtocolException("Media package changed during resolution.");
        }
        await ReplyAsync(envelope.Type, envelope.RequestId, resolved, token).ConfigureAwait(false);
    }
}
