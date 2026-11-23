using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private sealed class HeldSelection(WidgetIndexedRows source,long epoch)
    {
        internal readonly WidgetIndexedRows Source=source;
        internal readonly CancellationTokenSource Cancellation=new();
        internal readonly Queue<(int Index,string Key)> Pending=[];
        internal readonly HashSet<string> Visited=new(StringComparer.Ordinal);
        internal TaskCompletionSource Changed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long Epoch=epoch;
        internal bool Starting;
        internal bool Released;
        internal void Wake(){var old=Changed;Changed=new(TaskCreationOptions.RunContinuationsAsynchronously);old.TrySetResult();}
    }
    private HeldSelection? heldSelection;
    partial void TraceHeldSelection(string phase);
    internal bool CanStartHeldSelection=>CanReceiveInput && source?.Declaration.IndexedSelection?.SupportsHeldSelection==true &&
        FocusedRow() is {Lease.IsCurrent:true} && FocusedIndex() is not null;

    internal bool BeginHeldSelection()
    {
        if(!CanStartHeldSelection || source is not {} captured || FocusedIndex() is not {} index || FocusedRow() is not {} row)return false;
        // A fresh press can continue the same released gesture while its
        // already-visited queue drains, without dropping those earlier choices.
        if(heldSelection is {Released:true} draining && HeldSelectionCurrent(draining))
        {draining.Released=false;QueueHeldSelection(index,row.Item.Key);return true;}
        CancelHeldSelection();
        var gesture=new HeldSelection(captured,captured.Declaration.IndexedSelection!.Epoch);
        heldSelection=gesture;
        TraceHeldSelection($"begin index={index} epoch={gesture.Epoch}");
        QueueHeldSelection(index,row.Item.Key);
        TrackRetirement(PumpHeldSelectionAsync(gesture));
        return true;
    }

    internal void CancelHeldSelection()
    {
        var previous=heldSelection;heldSelection=null;
        if(previous is not null)TraceHeldSelection($"cancel epoch={previous.Epoch} active={CanReceiveInput}");
        previous?.Cancellation.Cancel();
    }

    internal void ReleaseHeldSelection()
    {
        if(heldSelection is not {} gesture)return;
        // Finish already-visited choices, but never add a row whose pending
        // navigation completes after release. Ownership loss cancels both.
        gesture.Released=true;gesture.Wake();
    }

    // Called only after deliberate directional navigation actually focuses a
    // current row. Scrolling/recycling/hover and pending unloaded targets do not
    // count as visited items. Retain only metadata here, never native containers.
    private void QueueHeldSelection(int index,string key)
    {
        if(heldSelection is not {} gesture || gesture.Released || !HeldSelectionCurrent(gesture))return;
        if(gesture.Visited.Count>IndexedCollectionSelection.MaximumItems || !gesture.Visited.Add(key))return;
        gesture.Pending.Enqueue((index,key));gesture.Wake();
    }

    private bool HeldSelectionCurrent(HeldSelection gesture)=>ReferenceEquals(heldSelection,gesture)&&!disposed&&CanReceiveInput&&
        ReferenceEquals(source,gesture.Source)&&source.Declaration.IndexedSelection is {SupportsHeldSelection:true} selection&&
        (selection.Epoch==gesture.Epoch || gesture.Starting&&selection.Epoch==gesture.Epoch-1);

    private void RefreshHeldSelection()
    {
        if(heldSelection is not {} gesture)return;
        if(!HeldSelectionCurrent(gesture)){CancelHeldSelection();return;}
        gesture.Wake();
    }

    private async Task PumpHeldSelectionAsync(HeldSelection gesture)
    {
        var token=gesture.Cancellation.Token;
        try
        {
            while(HeldSelectionCurrent(gesture))
            {
                if(!gesture.Pending.TryDequeue(out var target))
                {if(gesture.Released)break;await gesture.Changed.Task.WaitAsync(token);continue;}
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
                TraceHeldSelection($"target index={target.Index} epoch={gesture.Epoch}");
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                var itemToken=deadline.Token;
                // Uses the existing bounded range reader. Queued visited items
                // may have scrolled offscreen; no visual realization is needed.
                using var retained=gesture.Source.Items.Retain(target.Index);
                void Changed(object? sender,EventArgs args)=>gesture.Wake();
                retained.Slot.StateChanged+=Changed;
                try
                {
                    await WaitUntil(()=>retained.Slot.Value is {Lease.IsCurrent:true},itemToken);
                    var selection=gesture.Source.Declaration.IndexedSelection!;
                    if(selection.IsActive && selection.Indices.Contains(target.Index))continue;
                    var start=!selection.IsActive;
                    var originalEpoch=selection.Epoch;
                    if(start){gesture.Epoch=checked(originalEpoch+1);gesture.Starting=true;}
                    for(var attempt=0;attempt<2;attempt++)
                    {
                        await WaitUntil(()=>retained.Slot.Value is {Lease.IsCurrent:true},itemToken);
                        var row=retained.Slot.Value!;
                        if(row.Item.Key!=target.Key)return;
                        var binding=gesture.Source.Presentation;
                        TraceHeldSelection($"admit index={target.Index} epoch={originalEpoch} start={start} current={HeldSelectionCurrent(gesture)}");
                        if(EnsureInteractionAsync is {} admit && !await admit(binding.Frame.Authority,itemToken))return;
                        itemToken.ThrowIfCancellationRequested();
                        if(!HeldSelectionCurrent(gesture))return;
                        try
                        {
                            var result=await row.Lease.AdmitSelectionAsync(binding.Frame,target.Key,
                                new(originalEpoch,start?IndexedSelectionGesture.BeginAdd:IndexedSelectionGesture.Add),
                                ++sequence,Environment.TickCount64*1000,itemToken);
                            TraceHeldSelection($"admitted result={result}");
                            // A rejected operation did not enter the worker queue.
                            // Do not wait for a publication it cannot produce, or
                            // replay a capacity/lifecycle rejection as stale input.
                            if(result is WidgetOperationAdmission.RejectedInactive or WidgetOperationAdmission.RejectedCapacity)return;
                            if(result is not null)break;
                        }
                        catch(WidgetPresentationSessionException error) when(WidgetInputFailure.IsStale(error)) { TraceHeldSelection($"stale {error.Message}"); }
                        // Add is idempotent, and retry is limited to the same
                        // source/query/key after a newer publication arrives.
                        if(attempt==1)return;
                        session.RequestInputRefresh(binding.Frame);
                        await WaitUntil(()=>gesture.Source.Presentation.Frame.Authority.SnapshotSequence>binding.Frame.Authority.SnapshotSequence,itemToken);
                    }
                    await WaitUntil(()=>gesture.Source.Declaration.IndexedSelection is {IsActive:true} current && current.Epoch==gesture.Epoch &&
                        (current.Indices.Contains(target.Index)||current.LimitReached),itemToken);
                    gesture.Starting=false;
                }
                finally {retained.Slot.StateChanged-=Changed;}
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(OperationCanceledException)
        {if(HeldSelectionCurrent(gesture))failed(new TimeoutException("Selection did not complete. Release the trigger and try again."));}
        catch(Exception error)
        {if(HeldSelectionCurrent(gesture))failed(error);}
        finally
        {
            if(ReferenceEquals(heldSelection,gesture))heldSelection=null;
            gesture.Pending.Clear();gesture.Cancellation.Dispose();
        }

        async Task WaitUntil(Func<bool> ready,CancellationToken cancellation)
        {
            while(true)
            {
                cancellation.ThrowIfCancellationRequested();
                if(!HeldSelectionCurrent(gesture))throw new OperationCanceledException(token);
                if(ready())return;
                await gesture.Changed.Task.WaitAsync(cancellation);
            }
        }
    }
}
