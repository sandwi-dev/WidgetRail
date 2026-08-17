using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.GamesApps;

internal enum GamesAppsLibrarySaveStatus
{
    Saved,
    Rejected,
}

internal sealed record GamesAppsLibrarySaveResult(
    GamesAppsLibrarySaveStatus Status,
    GamesAppsLibraryState State,
    long Revision);

/// <summary>
/// Owns the bounded schema-v3 compare-and-swap transaction. It has no widget,
/// lifecycle, provider, render, or launch authority.
/// </summary>
internal static class GamesAppsLibraryStore
{
    internal static async Task<GamesAppsLibrarySaveResult> SaveAsync(
        Func<GamesAppsLibraryState, long, CancellationToken,
            ValueTask<WidgetPrivateStateMutation>> writeAsync,
        Func<CancellationToken,
            ValueTask<WidgetPrivateStateValue<GamesAppsLibraryState>>> readAsync,
        GamesAppsLibraryState baseline,
        GamesAppsLibraryState desired,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeAsync);
        ArgumentNullException.ThrowIfNull(readAsync);
        baseline = GamesAppsLibraryPolicy.Normalize(baseline);
        var attempted = GamesAppsLibraryPolicy.Normalize(desired);
        var revision = expectedRevision;
        for (var attempt = 0; attempt != 2; attempt++)
        {
            try
            {
                var mutation = await writeAsync(attempted, revision, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new GamesAppsLibrarySaveResult(
                    GamesAppsLibrarySaveStatus.Saved, attempted, mutation.Revision);
            }
            catch (WidgetCapabilityException exception) when (
                exception.ErrorCode == "state_conflict" && attempt == 0)
            {
                var latestSnapshot = await readAsync(cancellationToken).ConfigureAwait(false);
                var latest = GamesAppsLibraryPolicy.Normalize(
                    latestSnapshot.Exists ? latestSnapshot.Value : null);
                var merge = GamesAppsLibraryPolicy.Merge(baseline, attempted, latest);
                if (!merge.Accepted)
                    return new GamesAppsLibrarySaveResult(
                        GamesAppsLibrarySaveStatus.Rejected,
                        latest,
                        latestSnapshot.Revision);
                attempted = merge.State;
                revision = latestSnapshot.Revision;
            }
        }
        throw new InvalidOperationException("Private state retry bound was exceeded.");
    }
}
