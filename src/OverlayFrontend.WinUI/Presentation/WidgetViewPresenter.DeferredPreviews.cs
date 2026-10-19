using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record DeferredPreviewEntry(string OwnerId,object OwnerToken,IndexedCollectionDescriptor Provider,WidgetPresentationIndexedLease Lease);
    private readonly OrderedDictionary<object,DeferredPreviewEntry> deferredPreviews=[];
    private CancellationTokenSource? deferredPreviewCancellation;
    private Task? deferredPreviewRead;
    private object? deferredPreviewRequestKey;
    private object? deferredPreviewFailure;
    private static object DeferredCacheKey(PreviewSource source)=>(source.Owner.Token,source.Provider!,source.ContentKey!);

    private PreviewSource? ResolveDeferredPreview(PreviewSource source)
    {
        if(source.Provider is null)return source;
        PruneDeferredPreviews();
        var key=DeferredCacheKey(source);
        if(deferredPreviews.Remove(key,out var entry))
        {
            deferredPreviews.Add(key,entry);
            return source with{Content=entry.Lease.Range.Items[0].Root,ContentLease=entry.Lease};
        }
        if(deferredPreviewFailure?.Equals(source.Key)==true)return null;
        if(deferredPreviewRead is not null)
        {
            if(deferredPreviewRequestKey?.Equals(source.Key)!=true)deferredPreviewCancellation?.Cancel();
            return null;
        }
        if(Session is null||presentation is null)return null;
        var cancellation=new CancellationTokenSource();deferredPreviewCancellation=cancellation;deferredPreviewRequestKey=source.Key;
        // The initial yield installs the task before an immediately completed provider can finish.
        deferredPreviewRead=ReadDeferredPreviewAsync(source,presentation,cancellation);
        return null;
    }
    private async Task ReadDeferredPreviewAsync(PreviewSource source,WidgetPresentationBinding origin,CancellationTokenSource cancellation)
    {
        await Task.Yield();
        WidgetPresentationIndexedLease? lease=null;
        try
        {
            var session=Session!;var admission=Collections.WidgetIndexedRows.ProviderAdmission(session);
            await admission.WaitAsync(cancellation.Token);
            try
            {
                // Data may use a newer unchanged source; commands still carry the displayed frame.
                var current=session.GetState(origin.Frame.Authority.WidgetId)?.LastGood??origin.Frame;
                if(!SameOwner(origin.Frame.Authority,current.Authority)||!origin.IsCurrent)throw new OperationCanceledException("Preview owner retired.");
                lease=await session.AcquireFocusPreviewAsync(current.Authority,source.Owner.Identity.Id,source.Provider!,source.ContentKey!,origin.PinnedLayoutId,cancellation.Token);
            }
            finally{admission.Release();}
            if(disposed||cancellation.IsCancellationRequested||!origin.SameInput(presentation)||CurrentPreviewSource()?.Key.Equals(source.Key)!=true||!lease.IsCurrent)return;
            var key=DeferredCacheKey(source);
            deferredPreviews.Add(key,new(source.Owner.Identity.Id,source.Owner.Token,source.Provider!,lease));lease=null;
            while(deferredPreviews.Count>4)
            {
                var oldest=deferredPreviews.First();deferredPreviews.Remove(oldest.Key);TrackRetirement(oldest.Value.Lease.DisposeAsync().AsTask());
            }
        }
        catch(Exception error)when(error is not OutOfMemoryException)
        {
            // Optional preview failure must not turn a usable page into a rendering failure.
            // Never publish provider exception text or retry continuously while focus stays put.
            if(!cancellation.IsCancellationRequested&&CurrentPreviewSource()?.Key.Equals(source.Key)==true)deferredPreviewFailure=source.Key;
        }
        finally
        {
            if(lease is not null)TrackRetirement(lease.DisposeAsync().AsTask());
            cancellation.Dispose();deferredPreviewCancellation=null;deferredPreviewRead=null;deferredPreviewRequestKey=null;
            if(!disposed)QueueSurfaceUpdate();
        }
    }
    private void CancelDeferredPreviewRead()
    {
        deferredPreviewCancellation?.Cancel();deferredPreviewFailure=null;
    }
    private void PruneDeferredPreviews()
    {
        foreach(var pair in deferredPreviews.ToArray())
            if(!pair.Value.Lease.IsCurrent||bindings.GetValueOrDefault(pair.Value.OwnerId)?.Token!=pair.Value.OwnerToken||
                declarations.GetValueOrDefault(pair.Value.OwnerId)?.Node.FocusPreviewSource!=pair.Value.Provider)
            {deferredPreviews.Remove(pair.Key);TrackRetirement(pair.Value.Lease.DisposeAsync().AsTask());}
    }
    private void ClearDeferredPreviews()
    {
        CancelDeferredPreviewRead();
        if(deferredPreviewRead is { } read)TrackRetirement(read);
        foreach(var entry in deferredPreviews.Values)TrackRetirement(entry.Lease.DisposeAsync().AsTask());
        deferredPreviews.Clear();
    }
}
