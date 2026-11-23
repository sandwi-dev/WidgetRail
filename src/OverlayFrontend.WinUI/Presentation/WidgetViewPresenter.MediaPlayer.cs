using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetBridge;
namespace WidgetRail.OverlayFrontend.WinUI.Presentation;
internal sealed partial class WidgetViewPresenter
{
    private WidgetPresentationIndexedLease? previewMediaLease;
    private string? previewMediaItem;
    private object? previewMediaOwner;
    private bool previewMediaAllowed;
    private Func<bool>? previewMediaCurrent;
    private WidgetPresentationIndexedLease? rowMediaLease;
    private string? rowMediaItem;
    private Func<bool>? rowMediaCurrent;
    internal void SetIndexedBodyMedia(WidgetPresentationIndexedLease lease, string item, Func<bool> current)
    { rowMediaLease = lease; rowMediaItem = item; rowMediaCurrent = current; }
    private Func<string, Microsoft.UI.Xaml.Media.ImageSource?>? BorrowPreviewArtwork;
    internal Action<object, string, bool>? NativeAudioChanged { get; set; }
    private int mediaEditorSuspensions;
    // A host editor temporarily owns playback; keep the widget's presentation and capability work alive.
    internal IDisposable PauseMediaPlayersForEditor()
    {
        if (disposed) return new MediaEditorSuspension(null);
        ++mediaEditorSuspensions;
        RefreshMediaPlayers();
        return new MediaEditorSuspension(this);
    }
    private sealed class MediaEditorSuspension(WidgetViewPresenter? owner) : IDisposable
    {
        private WidgetViewPresenter? current = owner;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref current, null) is not { } presenter) return;
            --presenter.mediaEditorSuspensions;
            if (!presenter.disposed) presenter.RefreshMediaPlayers();
        }
    }
    internal bool PanMediaImage(short x, short y) => FocusedMediaPlayer?.PanImage(x, y) == true;
    private MediaPlayerView? FocusedMediaPlayer => bindings.Values.Select(binding => binding.Element).OfType<MediaPlayerView>().FirstOrDefault(media => media.IsExpanded || media.IsMovingPresentation || media.HasOptionsMenu) ??
        (FocusedBinding() is { Element: MediaPlayerView media } binding && Navigable(binding) ? media : null);
    private void UpdateMediaPlayer(MediaPlayerView media, ViewNode node, string scope)
    {
        media.AudioPreferenceWidgetId = presentation?.Frame.Authority.WidgetId;
        media.ThumbnailAnchor = node.DirectionalAnchor;
        media.SetAnimationsAllowed(!Motion.WidgetMotionOptions.From(appearance, systemAnimationsEnabled).Reduced);
        media.InteractionChanged = NotifyControllerGuideChanged;
        if (presentation is { } activityOwner)
            media.AudioChanged = playing => NativeAudioChanged?.Invoke(media, activityOwner.Frame.Authority.WidgetId, playing);
        var pageReady = activeTransition?.Settled.Task ?? Task.CompletedTask;
        var previewReady = previewOwner?.previewMotion?.Ready ?? Task.CompletedTask;
        // Resolve the existing source/lease while the poster enters, but create
        // no native video plane before its preview frame has settled.
        media.PresentationReady = pageReady.IsCompletedSuccessfully ? previewReady :
            previewReady.IsCompletedSuccessfully ? pageReady : Task.WhenAll(pageReady, previewReady);
        media.SetPreviewPlayback(previewMediaAllowed);
        media.RequestPreviewInteraction = previewMediaAllowed && previewOwner is { } parent ? () => parent.EnterFocusPreview(focusFirst: false) : null;
        media.SetEditorPreviewPaused(mediaEditorSuspensions != 0);
        media.SetActive(presentationActive && !disposed, presentationInputEnabled && scope == activeScope && node.IsDisabled != true && !HasTransientControl &&
            (!previewMediaAllowed || previewOwner?.PreviewInputCurrent(this) == true));
#if ENABLE_WIDGET_VALIDATION
        if (TryApplyValidationMedia(media, node)) return;
#endif
        // Row media resolves through the exact indexed body lease. Host controls
        // do not become widget-authored actions or ordinary parent-tree sources.
        var bodyMedia = rowMediaLease is not null && (node.MediaPlayer?.Presentation is MediaPlayerPresentation.Animation or MediaPlayerPresentation.Inline ||
            ReadingGroupContentContract.IsSavedPreview(node.MediaPlayer) && fragmentRootId is { } root && declarations.GetValueOrDefault(root)?.Node.IsReadingGroup == true);
        if (presentationActive && !disposed && (!presentationOnly || bodyMedia) && Session is { } session && presentation is { } binding && MediaPlayerDefinition.FromNode(node) is { } definition)
        {
            var lease = bodyMedia ? rowMediaLease : previewMediaLease;
            media.ConfigureEmbedded(lease is null && !presentationOnly && scope == activeScope ? MediaOwner : null,
                binding.Frame.Authority, node.Id, effectiveView?.EmbeddedMediaSession);
            var item = bodyMedia ? rowMediaItem : previewMediaItem;
            var preview = previewMediaOwner;
            Task<HostMediaPlayerSource> Resolve(CancellationToken token, string? owned = null, string? galleryItem = null) => lease is not null && item is not null
                ? session.ResolveIndexedMediaPlayerSourceAsync(lease, item, node.Id, token, owned, galleryItem)
                : session.ResolveMediaPlayerSourceAsync(binding.Frame, node.Id, binding.Selection, binding.Projection, token, owned, galleryItem);
            media.Apply(token => Resolve(token), definition,
                () =>
                {
                    if (disposed || presentation?.SameSurface(binding) != true || !declarations.TryGetValue(node.Id, out var current) || MediaPlayerDefinition.FromNode(current.Node) != definition) return false;
                    if (preview is not null && (!Equals(previewMediaOwner, preview) || previewMediaCurrent?.Invoke() != true)) return false;
                    if (bodyMedia && (!ReferenceEquals(rowMediaLease, lease) || rowMediaItem != item || rowMediaCurrent?.Invoke() != true)) return false;
                    try { return (lease is not null && item is not null ? session.ResolveIndexedMediaPlayer(lease, item, node.Id) :
                        session.ResolveMediaPlayer(binding.Frame, node.Id, binding.Selection, binding.Projection)) == definition; }
                    catch (Exception error) when (error is WidgetPresentationSessionException or ObjectDisposedException) { return false; }
                }, (binding.Frame.Authority.WidgetId, binding.Frame.Authority.RuntimeGeneration,
                    binding.Frame.Authority.SessionGeneration, binding.Frame.Authority.WorkerRun, binding.Selection, preview,
                    bodyMedia ? lease?.LeaseId : null, bodyMedia ? item : null),
                (owned, token) => Resolve(token, owned), async owned =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await session.ReleaseMediaPlayerAsync(owned, timeout.Token);
                }, initialPoster: node.ArtworkHandle is { } handle ? BorrowPreviewArtwork?.Invoke(handle) : null,
                resolveGalleryItem: (key, owned, token) => Resolve(token, owned, key));
        }
    }
    private void RefreshMediaPlayers()
    {
        foreach (var binding in bindings.Values)
            if (binding.Element is MediaPlayerView media && declarations.TryGetValue(binding.Identity.Id, out var declaration))
                UpdateMediaPlayer(media, declaration.Node, binding.Identity.Scope);
    }
}
