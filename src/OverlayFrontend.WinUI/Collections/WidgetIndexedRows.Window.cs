using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

internal sealed partial class WidgetIndexedRows
{
    internal bool IsPreparedQueryCurrent(WidgetPresentationBinding binding,ViewNode collection)
    {
        var frame=session.GetState(binding.Frame.Authority.WidgetId)?.LastGood;
        if (frame is null || !binding.IsCurrent || !SameOwner(frame.Authority,binding.Frame.Authority) ||
            frame.Authority.WorkerRun != binding.Frame.Authority.WorkerRun) return false;
        try
        {
            _=IndexedCollectionContract.ResolveScope(frame.Snapshot,new(collection.Id,collection.IndexedCollection!,0,1,
                Guid.NewGuid().ToString("N"),binding.PinnedLayoutId));
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    internal async Task<PreparedIndexedWindow<WidgetIndexedRow>> PrepareWindowAsync(
        WidgetPresentationBinding binding, ViewNode collection, CancellationToken token)
    {
        if (!CanRebaseWindow(binding,collection) || collection.IndexedCollection is not { ObservesBoundaries: true } next)
            throw new InvalidOperationException("Only a bounded paged successor can be prepared.");
        var current = new SourcePresentation(binding,collection);
        var query = Items.Query with { Generation = next.QueryGeneration };
        var pageSize = Items.PageSize;
        var ranges = new List<IndexedRangeResult<WidgetIndexedRow>>();
        var prepared = new PreparedIndexedWindow<WidgetIndexedRow>(ranges);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token,activity.Token,lifetime.Token);
        try
        {
            // One successor and one request at a time. The shared provider lane
            // and session lease budget remain authoritative.
            for (var start = 0; start < next.Count; start += pageSize)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var request = new IndexedRangeRequest(query,start / pageSize + 1,start,
                    Math.Min(pageSize,next.Count-start),next.ContentRevision);
                ranges.Add(await ReadAsync(current,request,cancellation.Token).ConfigureAwait(false));
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (ranges.Any(range => range.Items.Any(item => !item.Value.Lease.IsCurrent)))
                throw new OperationCanceledException("Prepared row authority retired.");
            prepared.Validate(query,next.ContentRevision,collection.IndexedOccurrenceKeys!,pageSize);
            return prepared;
        }
        catch { await prepared.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
