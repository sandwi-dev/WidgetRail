using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public MediaPlayerDefinition ResolveMediaPlayer(WidgetPresentationFrame displayed, string elementId,
        WidgetPinnedSelection? selection = null, WidgetPinnedProjection? projection = null)
    {
        using (_gate.Enter())
        {
            var current = DemandPinnedFrameLocked(displayed);
            var origin = displayed.Snapshot; var latest = current.Snapshot;
            if (selection is not null || projection is not null)
            {
                if (selection is null || projection is null || !ReferenceEquals(projection.Frame, displayed)) throw MediaStale();
                latest = DemandPinnedInputLocked(selection, projection); origin = projection.Snapshot;
            }
            var before = FindMedia(origin.Root, elementId); var after = FindMedia(latest.Root, elementId);
            if (displayed.Authority.WorkerRun is null || displayed.Authority.WorkerRun != current.Authority.WorkerRun ||
                before is null || before != after || !before.IsWellFormed()) throw MediaStale();
            return before;
        }
    }
    private static MediaPlayerDefinition? FindMedia(ViewNode node, string id) => node.Id == id ? MediaPlayerDefinition.FromNode(node) :
        (node.FocusPresentation is { } preview ? FindMedia(preview, id) : null) ??
        node.Children.Select(child => FindMedia(child, id)).FirstOrDefault(value => value is not null);
    public Task<HostMediaPlayerSource> ResolveMediaPlayerSourceAsync(WidgetPresentationFrame displayed, string elementId,
        WidgetPinnedSelection? selection = null, WidgetPinnedProjection? projection = null, CancellationToken token = default) =>
        ResolveMediaPlayerSourceAsync(displayed, elementId, selection, projection, token, null);
    public Task<HostMediaPlayerSource> ResolveMediaPlayerSourceAsync(WidgetPresentationFrame displayed, string elementId,
        WidgetPinnedSelection? selection, WidgetPinnedProjection? projection, CancellationToken token, string? playbackLeaseId) =>
        ResolveMediaPlayerSourceAsync(displayed, elementId, selection, projection, token, playbackLeaseId, null);
    public async Task<HostMediaPlayerSource> ResolveMediaPlayerSourceAsync(WidgetPresentationFrame displayed, string elementId,
        WidgetPinnedSelection? selection, WidgetPinnedProjection? projection, CancellationToken token, string? playbackLeaseId, string? galleryItemId)
    {
        var media = ResolveMediaPlayer(displayed, elementId, selection, projection);
        if (!media.DeclaresGalleryItem(galleryItemId)) throw MediaStale();
        var selectedMedia = media.SelectGalleryItem(galleryItemId);
        var request = new BridgeMediaPlayerRequest(displayed.Authority.WidgetId, displayed.Authority.WorkerRun!,
            displayed.Authority.SnapshotSequence, elementId, media, selection?.LayoutId)
            { PlaybackLeaseId = playbackLeaseId, GalleryItemId = galleryItemId };
        var response = await RequestAsync(BridgeMessageTypes.ResolveMediaPlayer, request, BridgeMessageTypes.ResolveMediaPlayer, token).ConfigureAwait(false);
        var source = BridgeJson.FromElement<HostMediaPlayerSource>(response.Payload);
        if (ResolveMediaPlayer(displayed, elementId, selection, projection) != media || !ValidMediaSource(source, selectedMedia, playbackLeaseId)) throw MediaStale();
        return source;
    }
    public MediaPlayerDefinition ResolveIndexedMediaPlayer(WidgetPresentationIndexedLease lease, string itemKey, string elementId)
    {
        using (_gate.Enter())
        {
            DemandIndexedLeaseLocked(lease);
            var item = lease.Range.Items.SingleOrDefault(item => item.Key == itemKey) ?? throw MediaStale();
            if (lease.Request.Range.Kind != IndexedCollectionRequestKind.FocusPreview && FindBodyMediaNode(item.Root, elementId) is { } body &&
                MediaPlayerDefinition.FromNode(body) is { } bodyMedia && bodyMedia.IsWellFormed() &&
                (bodyMedia.Presentation == MediaPlayerPresentation.Animation ||
                    item.Root.IsReadingGroup && ReadingGroupContentContract.IsInlineControl(body)))
                return bodyMedia;
            var root = lease.Request.Range.Kind == IndexedCollectionRequestKind.FocusPreview ? item.Root : item.Root.FocusPresentation;
            return root is not null && FindMedia(root, elementId) is { } definition && definition.IsWellFormed() ? definition : throw MediaStale();
        }
    }
    private static ViewNode? FindBodyMediaNode(ViewNode node, string id) => node.Id == id ? node :
        node.Children.Select(child => FindBodyMediaNode(child, id)).FirstOrDefault(value => value is not null);
    public Task<HostMediaPlayerSource> ResolveIndexedMediaPlayerSourceAsync(WidgetPresentationIndexedLease lease,
        string itemKey, string elementId, CancellationToken token = default, string? playbackLeaseId = null) =>
        ResolveIndexedMediaPlayerSourceAsync(lease, itemKey, elementId, token, playbackLeaseId, null);
    public async Task<HostMediaPlayerSource> ResolveIndexedMediaPlayerSourceAsync(WidgetPresentationIndexedLease lease,
        string itemKey, string elementId, CancellationToken token, string? playbackLeaseId, string? galleryItemId)
    {
        var media = ResolveIndexedMediaPlayer(lease, itemKey, elementId);
        if (!media.DeclaresGalleryItem(galleryItemId)) throw MediaStale();
        var selectedMedia = media.SelectGalleryItem(galleryItemId);
        var authority = lease.Authority;
        var request = new BridgeMediaPlayerRequest(authority.WidgetId, authority.WorkerRun ?? throw MediaStale(), authority.SnapshotSequence,
            elementId, media, lease.Request.Range.PinnedLayoutId)
        {
            IndexedOrigin = new(authority.WidgetInstanceId, authority.RuntimeGeneration, authority.PresentationGeneration, new(lease.LeaseId, itemKey)),
            PlaybackLeaseId = playbackLeaseId, GalleryItemId = galleryItemId
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lease.Lifetime.Token);
        var response = await RequestAsync(BridgeMessageTypes.ResolveMediaPlayer, request, BridgeMessageTypes.ResolveMediaPlayer, cancellation.Token).ConfigureAwait(false);
        var source = BridgeJson.FromElement<HostMediaPlayerSource>(response.Payload);
        if (ResolveIndexedMediaPlayer(lease, itemKey, elementId) != media || !ValidMediaSource(source, selectedMedia, playbackLeaseId)) throw MediaStale();
        return source;
    }
    private static bool ValidMediaSource(HostMediaPlayerSource source, MediaPlayerDefinition media, string? playbackLeaseId) =>
        source.Media == media && source.PlaybackLeaseId == playbackLeaseId &&
        Uri.TryCreate(source.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "file" or "http" or "https" &&
        (!(media.Source.Kind is MediaPlayerSourceKind.WebUrl or MediaPlayerSourceKind.WebImage) || source.Uri == media.Source.Location) &&
        (media.Source.Kind != MediaPlayerSourceKind.WebImage || source.IsImage && uri.Scheme == "https") &&
        (!(media.Source.Kind is MediaPlayerSourceKind.SavedCapture or MediaPlayerSourceKind.Capture) || uri.IsFile);
    public async Task ReleaseMediaPlayerAsync(string playbackLeaseId, CancellationToken token = default)
    {
        if (!CaptureLimits.ValidToken(playbackLeaseId)) throw new ArgumentException("Invalid media playback lease.", nameof(playbackLeaseId));
        _ = await RequestAsync(BridgeMessageTypes.ReleaseMediaPlayer, new BridgeMediaPlayerRelease(playbackLeaseId),
            BridgeMessageTypes.ReleaseMediaPlayer, token).ConfigureAwait(false);
    }
    private static WidgetPresentationSessionException MediaStale() => new("media_player_stale", "The displayed media source retired or changed.");
}
