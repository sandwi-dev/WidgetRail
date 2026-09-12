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
        var capture = resource.Capture();
        return new(capture.Snapshot, capture.Present(UI.VerticalScroll(scrollId, [])));
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
    bool LocalPlaybackBusy,
    string? LocalPlaybackFeedback,
    bool PageLoading,
    string? PageError,
    SpotifySearchPresentation? Search = null,
    ToastElement? ActionToast = null);
