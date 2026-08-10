using System.Collections.Immutable;

namespace GameBarAlternative.Samples.YtMusicWidget;

internal static class YtMusicCompanionPolicy
{
    private const double ProgressDriftToleranceSeconds = 0.12;
    private const double SeekSnapThresholdSeconds = 3;
    private const double ForwardCorrectionRate = 0.35;

    internal static YtMusicPlaybackSnapshot ProjectForPresentation(
        YtMusicPresentationState presentation,
        long now,
        TimeProvider timeProvider) => ProjectProgressState(
            presentation.Snapshot,
            presentation.SnapshotTimestamp,
            presentation.PendingForwardCorrectionSeconds,
            now,
            timeProvider).Snapshot;

    internal static YtMusicOptimisticStart BeginOptimistic(
        YtMusicPresentationState presentation,
        YtMusicCommand command,
        string message,
        long now,
        YtMusicUpdatePolicy updatePolicy,
        TimeProvider timeProvider)
    {
        var beforeProjection = ProjectProgressState(
            presentation.Snapshot,
            presentation.SnapshotTimestamp,
            presentation.PendingForwardCorrectionSeconds,
            now,
            timeProvider);
        var before = beforeProjection.Snapshot;
        bool? toggleState = command switch
        {
            YtMusicCommand.Like => !before.IsLiked,
            YtMusicCommand.Dislike => !before.IsDisliked,
            _ => null,
        };
        var expected = command switch
        {
            YtMusicCommand.TogglePlayback => before with { IsPlaying = !before.IsPlaying },
            YtMusicCommand.Previous or YtMusicCommand.Next => before with { PositionSeconds = 0 },
            YtMusicCommand.Like => before with
            {
                IsLiked = toggleState!.Value,
                IsDisliked = toggleState.Value ? false : before.IsDisliked,
            },
            YtMusicCommand.Dislike => before with
            {
                IsLiked = toggleState!.Value ? false : before.IsLiked,
                IsDisliked = toggleState.Value,
            },
            YtMusicCommand.Shuffle => before with
            {
                IsShuffleEnabled = !(before.IsShuffleEnabled ?? false),
            },
            YtMusicCommand.Repeat => before with
            {
                RepeatMode = NextRepeatMode(before.RepeatMode ?? YtMusicRepeatMode.Off),
            },
            _ => before,
        };
        var confirmationWindow = command is YtMusicCommand.Previous or YtMusicCommand.Next
            ? updatePolicy.TransportConfirmationWindow
            : updatePolicy.OptimisticConfirmationWindow;
        var pending = new YtMusicPendingOptimisticState(
            command,
            before,
            expected,
            now + ToTimestampTicks(confirmationWindow, timeProvider),
            toggleState,
            now,
            beforeProjection.RemainingForwardCorrectionSeconds);
        var next = presentation with
        {
            Snapshot = expected,
            SnapshotTimestamp = now,
            PendingForwardCorrectionSeconds = command is
                YtMusicCommand.TogglePlayback or YtMusicCommand.Previous or YtMusicCommand.Next
                    ? 0
                    : beforeProjection.RemainingForwardCorrectionSeconds,
            HasProgressSnapshot = true,
            Status = message,
            PendingOptimistic = presentation.PendingOptimistic
                .Where(candidate => !SameStateFeature(candidate.Command, command))
                .Append(pending)
                .ToImmutableArray(),
        };
        return new(next, pending);
    }

    internal static YtMusicPresentationState ReconcileAuthoritative(
        YtMusicPresentationState presentation,
        YtMusicPlaybackSnapshot authoritative,
        long now,
        bool force,
        TimeProvider timeProvider)
    {
        authoritative = PreserveStableMetadata(presentation.Snapshot, authoritative);
        authoritative = PreserveUnavailableToggleState(
            presentation.Snapshot, authoritative);
        var pending = presentation.PendingOptimistic.ToBuilder();
        if (force)
        {
            pending.Clear();
        }
        else
        {
            for (var index = pending.Count - 1; index >= 0; index--)
            {
                var optimistic = pending[index];
                var withinConfirmationWindow =
                    timeProvider.GetElapsedTime(now, optimistic.DeadlineTimestamp) >
                    TimeSpan.Zero;
                if (Confirms(optimistic, authoritative) || !withinConfirmationWindow)
                    pending.RemoveAt(index);
                else
                    authoritative = MergeExpectedState(authoritative, optimistic);
            }
        }
        var progress = ReconcileProgress(
            presentation, authoritative, now, force, timeProvider);
        var nextPending = pending.ToImmutable();
        return presentation with
        {
            Snapshot = progress.Snapshot,
            SnapshotTimestamp = now,
            PendingForwardCorrectionSeconds = progress.PendingForwardCorrectionSeconds,
            HasProgressSnapshot = true,
            PendingOptimistic = nextPending,
            ConnectionState = YtMusicWidgetConnectionState.Connected,
            Status = nextPending.Length == 0
                ? YtMusicConnectionPolicy.ConnectedStatus(progress.Snapshot)
                : presentation.Status,
            PairingCode = null,
        };
    }

    internal static bool TryRollback(
        YtMusicPresentationState presentation,
        YtMusicPendingOptimisticState optimistic,
        string message,
        long now,
        TimeProvider timeProvider,
        out YtMusicPresentationState next)
    {
        var index = -1;
        for (var candidateIndex = 0;
             candidateIndex < presentation.PendingOptimistic.Length;
             candidateIndex++)
        {
            if (!ReferenceEquals(
                    presentation.PendingOptimistic[candidateIndex], optimistic))
                continue;
            index = candidateIndex;
            break;
        }
        if (index < 0)
        {
            next = presentation;
            return false;
        }
        var currentProjection = ProjectProgressState(
            presentation.Snapshot,
            presentation.SnapshotTimestamp,
            presentation.PendingForwardCorrectionSeconds,
            now,
            timeProvider);
        var beforeProjection = ProjectProgressState(
            optimistic.Before,
            optimistic.StartedTimestamp,
            optimistic.BeforeForwardCorrectionSeconds,
            now,
            timeProvider);
        next = presentation with
        {
            Snapshot = RestorePreviousState(
                currentProjection.Snapshot,
                optimistic,
                beforeProjection.Snapshot),
            PendingForwardCorrectionSeconds = optimistic.Command is
                YtMusicCommand.TogglePlayback or YtMusicCommand.Previous or YtMusicCommand.Next
                    ? beforeProjection.RemainingForwardCorrectionSeconds
                    : currentProjection.RemainingForwardCorrectionSeconds,
            SnapshotTimestamp = now,
            PendingOptimistic = presentation.PendingOptimistic.RemoveAt(index),
            ConnectionState = YtMusicWidgetConnectionState.Connected,
            Status = message,
        };
        return true;
    }

    internal static bool TransportTransitionResolved(
        YtMusicPresentationState presentation,
        YtMusicCommand command)
    {
        if (presentation.PendingOptimistic.Any(candidate =>
                SameStateFeature(candidate.Command, command)))
            return false;
        return command == YtMusicCommand.TogglePlayback ||
               presentation.Snapshot.HasCompleteMetadata &&
               MetadataMatchesState(presentation.Snapshot);
    }

    internal static bool SameStateFeature(
        YtMusicCommand left,
        YtMusicCommand right) =>
        left == right ||
        left is YtMusicCommand.Like or YtMusicCommand.Dislike &&
        right is YtMusicCommand.Like or YtMusicCommand.Dislike;

    private static bool Confirms(
        YtMusicPendingOptimisticState pending,
        YtMusicPlaybackSnapshot authoritative) => pending.Command switch
        {
            YtMusicCommand.Next =>
                !string.Equals(authoritative.TrackId, pending.Before.TrackId,
                    StringComparison.Ordinal) ||
                !string.Equals(authoritative.Title, pending.Before.Title,
                    StringComparison.Ordinal) ||
                !string.Equals(authoritative.Artist, pending.Before.Artist,
                    StringComparison.Ordinal),
            YtMusicCommand.Previous =>
                authoritative.PositionSeconds <= 1.5 ||
                !string.Equals(authoritative.TrackId, pending.Before.TrackId,
                    StringComparison.Ordinal) ||
                !string.Equals(authoritative.Title, pending.Before.Title,
                    StringComparison.Ordinal) ||
                !string.Equals(authoritative.Artist, pending.Before.Artist,
                    StringComparison.Ordinal),
            YtMusicCommand.TogglePlayback =>
                authoritative.IsPlaying == pending.Expected.IsPlaying,
            YtMusicCommand.Like or YtMusicCommand.Dislike =>
                TrackChanged(pending.Before, authoritative) ||
                authoritative.IsLiked == pending.Expected.IsLiked &&
                authoritative.IsDisliked == pending.Expected.IsDisliked,
            YtMusicCommand.Shuffle =>
                authoritative.IsShuffleEnabled == pending.Expected.IsShuffleEnabled,
            YtMusicCommand.Repeat =>
                authoritative.RepeatMode == pending.Expected.RepeatMode,
            _ => true,
        };

    private static YtMusicPlaybackSnapshot MergeExpectedState(
        YtMusicPlaybackSnapshot authoritative,
        YtMusicPendingOptimisticState pending) => pending.Command switch
        {
            YtMusicCommand.TogglePlayback => authoritative with
            {
                IsPlaying = pending.Expected.IsPlaying,
            },
            YtMusicCommand.Previous or YtMusicCommand.Next => authoritative with
            {
                PositionSeconds = pending.Expected.PositionSeconds,
            },
            YtMusicCommand.Like or YtMusicCommand.Dislike => authoritative with
            {
                IsLiked = pending.Expected.IsLiked,
                IsDisliked = pending.Expected.IsDisliked,
            },
            YtMusicCommand.Shuffle => authoritative with
            {
                IsShuffleEnabled = pending.Expected.IsShuffleEnabled,
            },
            YtMusicCommand.Repeat => authoritative with
            {
                RepeatMode = pending.Expected.RepeatMode,
            },
            _ => authoritative,
        };

    private static YtMusicPlaybackSnapshot RestorePreviousState(
        YtMusicPlaybackSnapshot current,
        YtMusicPendingOptimisticState pending,
        YtMusicPlaybackSnapshot projectedBefore) => pending.Command switch
        {
            YtMusicCommand.TogglePlayback => current with
            {
                IsPlaying = pending.Before.IsPlaying,
                PositionSeconds = projectedBefore.PositionSeconds,
            },
            YtMusicCommand.Previous or YtMusicCommand.Next => current with
            {
                PositionSeconds = projectedBefore.PositionSeconds,
            },
            YtMusicCommand.Like or YtMusicCommand.Dislike => current with
            {
                IsLiked = pending.Before.IsLiked,
                IsDisliked = pending.Before.IsDisliked,
            },
            YtMusicCommand.Shuffle => current with
            {
                IsShuffleEnabled = pending.Before.IsShuffleEnabled,
            },
            YtMusicCommand.Repeat => current with
            {
                RepeatMode = pending.Before.RepeatMode,
            },
            _ => current,
        };

    private static bool TrackChanged(
        YtMusicPlaybackSnapshot before,
        YtMusicPlaybackSnapshot authoritative) =>
        !string.IsNullOrWhiteSpace(authoritative.TrackId) &&
        !string.IsNullOrWhiteSpace(before.TrackId) &&
        !string.Equals(authoritative.TrackId, before.TrackId,
            StringComparison.Ordinal);

    private static YtMusicPlaybackSnapshot PreserveUnavailableToggleState(
        YtMusicPlaybackSnapshot current,
        YtMusicPlaybackSnapshot authoritative) => authoritative with
        {
            IsShuffleEnabled = authoritative.IsShuffleEnabled ?? current.IsShuffleEnabled,
            RepeatMode = authoritative.RepeatMode ?? current.RepeatMode,
        };

    private static YtMusicPlaybackSnapshot PreserveStableMetadata(
        YtMusicPlaybackSnapshot current,
        YtMusicPlaybackSnapshot authoritative)
    {
        if (authoritative.HasCompleteMetadata && MetadataMatchesState(authoritative))
            return authoritative;
        if (!current.HasCompleteMetadata)
        {
            return authoritative with
            {
                Title = authoritative.IsPlaying ? "Loading track…" : "YouTube Music",
                Artist = authoritative.IsPlaying
                    ? "Waiting for YTMDesktop2 metadata"
                    : "No track loaded in YTMDesktop2",
                Album = string.Empty,
                ArtworkUrl = string.Empty,
                MetadataTrackId = string.Empty,
                HasCompleteMetadata = false,
            };
        }
        return authoritative with
        {
            Title = current.Title,
            Artist = current.Artist,
            Album = current.Album,
            ArtworkUrl = current.ArtworkUrl,
            MetadataTrackId = MetadataIdentity(current),
            HasCompleteMetadata = true,
        };
    }

    private static bool MetadataMatchesState(YtMusicPlaybackSnapshot snapshot)
    {
        if (!snapshot.HasCompleteMetadata) return false;
        var metadataTrackId = MetadataIdentity(snapshot);
        return string.IsNullOrWhiteSpace(snapshot.TrackId) ||
               string.IsNullOrWhiteSpace(metadataTrackId) ||
               string.Equals(snapshot.TrackId, metadataTrackId,
                   StringComparison.Ordinal);
    }

    private static string MetadataIdentity(YtMusicPlaybackSnapshot snapshot) =>
        string.IsNullOrWhiteSpace(snapshot.MetadataTrackId)
            ? snapshot.TrackId
            : snapshot.MetadataTrackId;

    private static YtMusicProgressReconciliation ReconcileProgress(
        YtMusicPresentationState presentation,
        YtMusicPlaybackSnapshot authoritative,
        long now,
        bool force,
        TimeProvider timeProvider)
    {
        authoritative = ClampProgress(authoritative);
        var currentProjection = ProjectProgressState(
            presentation.Snapshot,
            presentation.SnapshotTimestamp,
            presentation.PendingForwardCorrectionSeconds,
            now,
            timeProvider);
        var current = currentProjection.Snapshot;
        var trackChanged = presentation.HasProgressSnapshot &&
                           !string.IsNullOrWhiteSpace(current.TrackId) &&
                           !string.IsNullOrWhiteSpace(authoritative.TrackId) &&
                           !string.Equals(current.TrackId, authoritative.TrackId,
                               StringComparison.Ordinal);
        var drift = authoritative.PositionSeconds - current.PositionSeconds;
        var mustAnchor = force ||
                         !presentation.HasProgressSnapshot ||
                         trackChanged ||
                         !authoritative.IsPlaying ||
                         !current.IsPlaying ||
                         Math.Abs(drift) >= SeekSnapThresholdSeconds;
        if (mustAnchor) return new(authoritative, 0);

        double correction;
        if (drift > ProgressDriftToleranceSeconds)
            correction = Math.Max(
                currentProjection.RemainingForwardCorrectionSeconds, drift);
        else if (drift < -ProgressDriftToleranceSeconds)
            correction = 0;
        else
            correction = currentProjection.RemainingForwardCorrectionSeconds;
        return new(
            authoritative with
            {
                PositionSeconds = Math.Clamp(
                    current.PositionSeconds,
                    0,
                    Math.Max(0, authoritative.DurationSeconds)),
            },
            correction);
    }

    private static YtMusicProgressProjection ProjectProgressState(
        YtMusicPlaybackSnapshot snapshot,
        long observedTimestamp,
        double forwardCorrectionSeconds,
        long now,
        TimeProvider timeProvider)
    {
        snapshot = ClampProgress(snapshot);
        if (!snapshot.IsPlaying || snapshot.DurationSeconds <= 0)
            return new(snapshot, 0);
        var elapsed = timeProvider.GetElapsedTime(observedTimestamp, now).TotalSeconds;
        if (!double.IsFinite(elapsed) || elapsed <= 0)
            return new(snapshot, Math.Max(0, forwardCorrectionSeconds));
        var correction = Math.Min(
            Math.Max(0, forwardCorrectionSeconds),
            elapsed * ForwardCorrectionRate);
        return new(
            snapshot with
            {
                PositionSeconds = Math.Clamp(
                    snapshot.PositionSeconds + elapsed + correction,
                    0,
                    snapshot.DurationSeconds),
            },
            Math.Max(0, forwardCorrectionSeconds - correction));
    }

    private static YtMusicPlaybackSnapshot ClampProgress(
        YtMusicPlaybackSnapshot snapshot)
    {
        var duration = double.IsFinite(snapshot.DurationSeconds)
            ? Math.Max(0, snapshot.DurationSeconds)
            : 0;
        var position = double.IsFinite(snapshot.PositionSeconds)
            ? Math.Clamp(snapshot.PositionSeconds, 0, duration)
            : 0;
        return snapshot with
        {
            PositionSeconds = position,
            DurationSeconds = duration,
        };
    }

    private static YtMusicRepeatMode NextRepeatMode(YtMusicRepeatMode mode) => mode switch
    {
        YtMusicRepeatMode.Off => YtMusicRepeatMode.All,
        YtMusicRepeatMode.All => YtMusicRepeatMode.One,
        _ => YtMusicRepeatMode.Off,
    };

    private static long ToTimestampTicks(
        TimeSpan interval,
        TimeProvider timeProvider) =>
        (long)Math.Ceiling(interval.TotalSeconds * timeProvider.TimestampFrequency);
}
