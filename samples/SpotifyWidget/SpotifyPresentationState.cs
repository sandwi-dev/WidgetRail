using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

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
    WidgetCursorResourceSnapshot<SpotifyMediaCollectionItem> Items);

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
    SpotifyDestination Destination,
    WidgetCursorResourceSnapshot<SpotifyMediaCollectionItem> Queue,
    WidgetCursorResourceSnapshot<SpotifyPlaylistCollectionItem> Playlists,
    SpotifyPlaylistDetailPresentation? PlaylistDetail,
    SpotifyDevicesSummary? Devices,
    SpotifyLocalPlaybackSummary? LocalPlayback,
    bool PageLoading,
    string? PageError,
    string? ReadyInitialFocusId);
