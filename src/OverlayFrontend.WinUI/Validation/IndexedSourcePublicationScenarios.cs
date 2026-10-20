using Microsoft.UI.Dispatching;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static class IndexedSourcePublicationScenarios
{
    private sealed class Lifetime : IAsyncDisposable
    {
        internal int Releases;
        public ValueTask DisposeAsync(){Interlocked.Increment(ref Releases);return ValueTask.CompletedTask;}
    }

    internal static async Task<int> RunAsync(DispatcherQueue dispatcher)
    {
        var checks=0;
        foreach(var mode in new[]{"yield","suspend","dispose","refresh"})
        {
            var original=new Lifetime();var replacement=new Lifetime();
            await using var source=new IndexedItemsSource<string>(new(new("publication",mode,"rows"),1),32,dispatcher,
                (request,_)=>Task.FromResult(new IndexedRangeResult<string>(request.Query,request.RequestId,request.StartIndex,
                    Enumerable.Range(request.StartIndex,request.Count).Select(index=>
                        new KeyedCollectionItem<string>(index.ToString(),request.ContentRevision+":"+index)).ToArray(),
                    request.ContentRevision,request.ContentRevision==0?original:replacement)));
            var slots=Enumerable.Range(0,32).Select(index=>(IndexedItem<string>)source[index]).ToArray();
            var interrupted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? retirement=null;
            var queued=false;var changedOwner=false;var stale=false;var earlyCount=0;
            foreach(var slot in slots)
                slot.StateChanged+=(_,_)=>{if(changedOwner&&slot.Value?.StartsWith("0:",StringComparison.Ordinal)==true)stale=true;};
            slots[0].StateChanged+=(_,_) =>
            {
                if(queued)return;
                queued=true;
                dispatcher.TryEnqueue(DispatcherQueuePriority.High,()=>
                {
                    earlyCount=slots.Count(slot=>slot.HasValue);
                    changedOwner=mode!="yield";
                    switch(mode)
                    {
                        case "suspend":retirement=source.SetPresentationActiveAsync(false);break;
                        case "dispose":retirement=source.DisposeAsync().AsTask();break;
                        case "refresh":source.RefreshContent(1);break;
                    }
                    interrupted.TrySetResult();
                });
            };
            source.RangesChanged(new(0,32),[]);
            await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(earlyCount is >0 and <=8,mode+": native input opportunity precedes the rest of a large page");
            if(retirement is not null)await retirement.WaitAsync(TimeSpan.FromSeconds(5));
            if(mode=="yield")await Until(()=>source.CompletedLoads==1);
            if(mode=="refresh")await Until(()=>source.CompletedLoads==2);
            Check(!stale,mode+": superseded publication cannot update the remaining slots");
            if(mode is "yield" or "refresh")
                Check(slots.All(slot=>slot.Value==(mode=="yield"?"0:":"1:")+slot.Index),mode+": every admitted row eventually publishes in order");
            else Check(!slots[^1].HasValue,mode+": retirement drains without finishing the old page");
            await source.DisposeAsync();
            Check(original.Releases==1&&replacement.Releases==(mode=="refresh"?1:0),mode+": each delivered lease releases exactly once");
        }
        return checks;
        void Check(bool passed,string message){if(!passed)throw new InvalidOperationException(message);checks++;}
        static async Task Until(Func<bool> condition)
        {
            var deadline=Environment.TickCount64+5000;
            while(!condition()){if(Environment.TickCount64>=deadline)throw new TimeoutException("Publication did not settle.");await Task.Delay(10);}
        }
    }
}
