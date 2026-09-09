using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

internal readonly record struct SpotifyPlaylistSelectionKey(
    string PlaylistId,
    long Generation);

internal sealed record SpotifyPlaylistSelection(
    SpotifyPlaylistSelectionKey Key,
    SpotifyPlaylistSummary Playlist);

internal sealed record SpotifyPlaylistDetailPresentation(
    SpotifyPlaylistSelection Selection,
    SpotifyCursorPresentation<SpotifyMediaCollectionItem> Items);

/// <summary>
/// One cursor snapshot and its SDK-projected viewport shell from the same
/// resource revision. Presentation replaces only the immutable child rows.
/// </summary>
internal sealed record SpotifyCursorPresentation<TItem>(
    WidgetCursorResourceSnapshot<TItem> Snapshot,
    ScrollElement Viewport) where TItem : notnull
{
    internal ScrollElement Present(IReadOnlyList<WidgetElement> children) =>
        Viewport with { Children = children };

    internal static SpotifyCursorPresentation<TItem> Capture(
        WidgetCursorResource<TItem> resource,
        string operationKey,
        string scrollId)
    {
        const int maximumCaptureAttempts = 4;
        for (var attempt = 0; attempt < maximumCaptureAttempts; attempt++)
        {
            var snapshot = resource.Snapshot;
            var viewport = resource.Present(UI.VerticalScroll(scrollId, []));
            if (snapshot.Revision == resource.Snapshot.Revision)
                return new(snapshot, viewport);
        }

        // A provider completion can publish Loading -> Ready and busy-state
        // invalidations while the worker is capturing both responsive shells.
        // One unstable render safely omits virtual-window metadata rather than
        // turning ordinary collection churn into a fatal render request.
        var fallback = resource.Snapshot;
        return new(
            fallback,
            FallbackScroll(scrollId, operationKey, fallback));
    }

    private static ScrollElement FallbackScroll(
        string scrollId,
        string operationKey,
        WidgetCursorResourceSnapshot<TItem> snapshot)
    {
        var scroll = UI.VerticalScroll(scrollId, []) with
        {
            CollectionAnchorKey = snapshot.Anchor?.Value,
        };
        return snapshot.HasBefore || snapshot.HasAfter
            ? scroll.Paginate(
                snapshot.HasBefore ? operationKey + ".cursor.before" : null,
                snapshot.HasAfter ? operationKey + ".cursor.after" : null,
                SpotifyCollectionPolicy.PaginationThreshold)
            : scroll;
    }
}

internal readonly record struct SpotifyPresentationRevision(
    long CaptureSequence,
    long PlaylistRevision,
    long PlaylistItemRevision,
    long? PlaylistSelectionGeneration);

/// <summary>
/// One immutable render-facing Spotify revision. Rendering consumes only this
/// projection, never mutable route fields or live resource objects.
/// </summary>
internal sealed record SpotifyPresentationState(
    SpotifyPresentationRevision Revision,
    SpotifyWidgetViewState ViewState,
    SpotifyPlaybackSummary? Playback,
    SpotifyPlaybackOperation? PendingOperation,
    string Status,
    SpotifyRefreshWarning? RefreshWarning,
    long SetupViewGeneration,
    bool SetupBusy,
    WidgetNavigationSnapshot<SpotifyRoute> Navigation,
    WidgetCursorResourceSnapshot<SpotifyMediaCollectionItem> Queue,
    SpotifyCursorPresentation<SpotifyPlaylistCollectionItem> Playlists,
    SpotifyPlaylistDetailPresentation? PlaylistDetail,
    SpotifyDevicesSummary? Devices,
    SpotifyLocalPlaybackSummary? LocalPlayback,
    bool PageLoading,
    string? PageError);
