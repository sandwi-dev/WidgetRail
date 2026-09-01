using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteLibraryLaunchGenerationOwner
{
    private long _generation;

    internal long CurrentGeneration => Interlocked.Read(ref _generation);

    internal bool CompleteAdmission(
        WidgetOperationHandle handle,
        TaskCompletionSource<long> generationReady)
    {
        if (handle.Admission != WidgetOperationAdmission.Started)
        {
            generationReady.TrySetCanceled();
            return false;
        }
        generationReady.SetResult(Interlocked.Increment(ref _generation));
        return true;
    }

    internal void Invalidate() => Interlocked.Increment(ref _generation);

    internal bool IsCurrent(long generation) => generation == CurrentGeneration;
}
