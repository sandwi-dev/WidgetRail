using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.SpotifyWidget;

internal readonly record struct SpotifyPlaylistSelectionKey(
    string PlaylistId,
    long Generation);

internal sealed record SpotifyPlaylistSelection(
    SpotifyPlaylistSelectionKey Key,
    SpotifyPlaylistSummary Playlist);

internal sealed record SpotifyPlaylistDetailPresentation(
    SpotifyPlaylistSelection Selection,
    SpotifyDiscoveredPresentation Items);

/// <summary>
/// One immutable discovered descriptor and its lazy native collection shell.
/// Rendering never reads a mutable viewport window or materializes all rows.
/// </summary>
internal sealed record SpotifyDiscoveredPresentation(IndexedCollectionElement Collection)
{
    internal IndexedCollectionDescriptor Source => Collection.Source;
    internal DiscoveredCollectionState State => Source.Discovery!;
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
    SpotifyDiscoveredPresentation Playlists,
    SpotifyPlaylistDetailPresentation? PlaylistDetail,
    SpotifyDevicesSummary? Devices,
    SpotifyLocalPlaybackSummary? LocalPlayback,
    bool LocalPlaybackBusy,
    string? LocalPlaybackFeedback,
    bool PageLoading,
    string? PageError,
    SpotifySearchPresentation? Search = null,
    ToastElement? ActionToast = null)
{
    internal IndexedCollectionElement? IndexedQueue { get; init; }
    internal FocusGroupEntryRequest? CollectionEntry { get; init; }
}
