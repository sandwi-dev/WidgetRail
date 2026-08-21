using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

internal readonly record struct SpotifyPlaylistSelectionKey(
    string PlaylistId,
    long Generation);

internal sealed record SpotifyPlaylistSelection(
    SpotifyPlaylistSelectionKey Key,
    SpotifyPlaylistSummary Playlist,
    string Mode,
    string ReturnFocusId);

internal sealed record SpotifyPlaylistDetailPresentation(
    SpotifyPlaylistSelection Selection,
    SpotifyCursorPresentation<SpotifyMediaCollectionItem> Items);

/// <summary>
/// One cursor snapshot and the two SDK-projected viewport shells from the same
/// resource revision. Presentation replaces only the immutable child rows.
/// </summary>
internal sealed record SpotifyCursorPresentation<TItem>(
    WidgetCursorResourceSnapshot<TItem> Snapshot,
    ScrollElement Wide,
    ScrollElement Compact) where TItem : notnull
{
    internal ScrollElement Scroll(string mode, IReadOnlyList<WidgetElement> children) =>
        (string.Equals(mode, "compact", StringComparison.Ordinal) ? Compact : Wide) with
        {
            Children = children,
        };

    internal static SpotifyCursorPresentation<TItem> Capture(
        WidgetCursorResource<TItem> resource,
        string operationKey,
        string wideScrollId,
        string compactScrollId)
    {
        const int maximumCaptureAttempts = 4;
        for (var attempt = 0; attempt < maximumCaptureAttempts; attempt++)
        {
            var snapshot = resource.Snapshot;
            var wide = resource.Present(UI.VerticalScroll(wideScrollId, []));
            var compact = resource.Present(UI.VerticalScroll(compactScrollId, []));
            if (snapshot.Revision == resource.Snapshot.Revision)
                return new(snapshot, wide, compact);
        }

        // A provider completion can publish Loading -> Ready and busy-state
        // invalidations while the worker is capturing both responsive shells.
        // One unstable render safely omits virtual-window metadata rather than
        // turning ordinary collection churn into a fatal render request.
        var fallback = resource.Snapshot;
        return new(
            fallback,
            FallbackScroll(wideScrollId, operationKey, fallback),
            FallbackScroll(compactScrollId, operationKey, fallback));
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
    bool ShowSetup,
    long SetupViewGeneration,
    bool SetupBusy,
    SpotifyDestination Destination,
    WidgetCursorResourceSnapshot<SpotifyMediaCollectionItem> Queue,
    SpotifyCursorPresentation<SpotifyPlaylistCollectionItem> Playlists,
    SpotifyPlaylistDetailPresentation? PlaylistDetail,
    SpotifyDevicesSummary? Devices,
    SpotifyLocalPlaybackSummary? LocalPlayback,
    bool PageLoading,
    string? PageError,
    string? ReadyInitialFocusId);
