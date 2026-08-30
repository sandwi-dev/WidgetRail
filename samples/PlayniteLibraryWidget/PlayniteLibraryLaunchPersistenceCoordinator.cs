using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteLibraryLaunchPersistenceCoordinator
{
    private long _generation;

    internal long CurrentGeneration => Interlocked.Read(ref _generation);

    internal bool CompleteAdmission(
        WidgetOperationHandle handle,
        TaskCompletionSource<long> generationReady)
    {
        if (!handle.IsAccepted)
        {
            generationReady.TrySetCanceled();
            return false;
        }
        generationReady.SetResult(Interlocked.Increment(ref _generation));
        return true;
    }

    internal void Invalidate() => Interlocked.Increment(ref _generation);

    internal bool IsCurrent(long generation) => generation == CurrentGeneration;

    internal async Task CommitRecentAsync(
        long generation,
        WidgetAppLaunchObservationState observation,
        PlayniteLibraryDisplayItem display,
        Func<Func<PlayniteLibraryPrivateState, PlayniteLibraryStateMutation>,
            CancellationToken, Task<bool>> save,
        Func<IReadOnlyList<string>> captureCurrentRecent,
        CancellationToken cancellationToken)
    {
        if (observation == WidgetAppLaunchObservationState.RequestAccepted ||
            !IsCurrent(generation)) return;
        try
        {
            await save(
                state => PlayniteLibraryOrganizationPolicy.RecordRecent(state, display),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!IsCurrent(generation))
            {
                var desiredRecent = captureCurrentRecent();
                await save(
                    state => PlayniteLibraryOrganizationPolicy.RestoreRecent(
                        state, desiredRecent),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
