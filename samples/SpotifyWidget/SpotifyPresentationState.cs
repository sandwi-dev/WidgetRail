using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

internal readonly record struct SpotifyPlaylistSelectionKey(
    string PlaylistId,
    long Generation);

internal sealed record SpotifyPlaylistSelection(
    SpotifyPlaylistSelectionKey Key,
    WidgetSpotifyPlaylistSummary Playlist,
    string Mode,
    string ReturnFocusId);

internal sealed record SpotifyPlaylistDetailPresentation(
    SpotifyPlaylistSelection Selection,
    WidgetPagedResourceSnapshot<WidgetSpotifyMediaItemSummary> Items);

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
    WidgetSpotifyPlaybackSummary? Playback,
    WidgetSpotifyPlaybackOperation? PendingOperation,
    string Status,
    bool ShowSetup,
    long SetupViewGeneration,
    SpotifyDestination Destination,
    WidgetSpotifyQueueSummary? Queue,
    WidgetPagedResourceSnapshot<WidgetSpotifyPlaylistSummary> Playlists,
    SpotifyPlaylistDetailPresentation? PlaylistDetail,
    WidgetSpotifyDevicesSummary? Devices,
    WidgetSpotifyLocalPlaybackSummary? LocalPlayback,
    bool PageLoading,
    string? PageError,
    string? ReadyInitialFocusId);
