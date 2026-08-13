using System.Collections.Immutable;

namespace GameBarAlternative.Samples.YtMusicWidget;

internal sealed record YtMusicPendingOptimisticState(
    YtMusicCommand Command,
    YtMusicPlaybackSnapshot Before,
    YtMusicPlaybackSnapshot Expected,
    long DeadlineTimestamp,
    bool? ToggleState,
    long StartedTimestamp,
    double BeforeForwardCorrectionSeconds);

internal sealed record YtMusicPresentationState(
    YtMusicWidgetConnectionState ConnectionState,
    YtMusicPlaybackSnapshot Snapshot,
    long SnapshotTimestamp,
    double PendingForwardCorrectionSeconds,
    bool HasProgressSnapshot,
    ImmutableArray<YtMusicPendingOptimisticState> PendingOptimistic,
    string Status,
    string? PairingCode)
{
    internal bool HasCurrentPlayback =>
        ConnectionState == YtMusicWidgetConnectionState.Connected &&
        !string.IsNullOrWhiteSpace(Snapshot.TrackId);

    internal static YtMusicPresentationState Initial(long timestamp) => new(
        YtMusicWidgetConnectionState.Disconnected,
        YtMusicPlaybackSnapshot.Empty,
        timestamp,
        0,
        false,
        [],
        "Connect to YTMDesktop2 to begin",
        null);

    internal static YtMusicPresentationState Connected(
        YtMusicPlaybackSnapshot snapshot,
        long timestamp,
        string status = "Connected to YTMDesktop2") => new(
            YtMusicWidgetConnectionState.Connected,
            snapshot,
            timestamp,
            0,
            true,
            [],
            status,
            null);
}

internal readonly record struct YtMusicOptimisticStart(
    YtMusicPresentationState Presentation,
    YtMusicPendingOptimisticState Pending);

internal readonly record struct YtMusicProgressProjection(
    YtMusicPlaybackSnapshot Snapshot,
    double RemainingForwardCorrectionSeconds);

internal readonly record struct YtMusicProgressReconciliation(
    YtMusicPlaybackSnapshot Snapshot,
    double PendingForwardCorrectionSeconds);
