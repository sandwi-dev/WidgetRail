using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetBridge;

public sealed partial class WidgetBridgeServer
{
    private async Task HandleMediaPlayerAsync(BridgeEnvelope envelope, CancellationToken token)
    {
        var request = BridgeJson.FromElement<BridgeMediaPlayerRequest>(envelope.Payload);
        var widget = _registry.DemandMediaPlayer(request);
        HostMediaPlayerSource resolved;
        if (request.Media.Source.Attachment is { } attachment)
        {
            var identity = new BrokerWidgetIdentity(widget.PackageId, widget.PublisherId, widget.InstanceId);
            if (!await CaptureAllowedAsync(identity, token).ConfigureAwait(false)) throw new BrokerException("permission_denied", "Capture permission is unavailable.");
            var capture = WindowCaptureRegistry.Shared.Resolve(identity, attachment.Id);
            if (capture.Attachment != attachment) throw new BrokerException("capture_unavailable", "Capture metadata changed.");
            resolved = new(request.Media, new Uri(capture.Path).AbsoluteUri, attachment.ContentType == "image/png",
                attachment.Width, attachment.Height, attachment.DurationSeconds, attachment.ExpiresAtUnixMilliseconds, RequiresRevalidation: true);
        }
        else resolved = await MediaPlayerSourceResolver.ResolveAsync(widget, request.Media, token).ConfigureAwait(false);
        if (_registry.DemandMediaPlayer(request).CatalogFingerprint != widget.CatalogFingerprint)
            throw new BridgeProtocolException("Media package changed during resolution.");
        await ReplyAsync(envelope.Type, envelope.RequestId, resolved, token).ConfigureAwait(false);
    }
}
