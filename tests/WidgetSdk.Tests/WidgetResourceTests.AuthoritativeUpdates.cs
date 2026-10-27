using WidgetRail.WidgetSdk;

internal static partial class WidgetResourceTests
{
    private static async Task AuthoritativeUpdatesPreserveAdmissionAsync()
    {
        var entered=Signal();var release=new TaskCompletionSource<TestValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled=false;var mapped=0;
        var widget=await StartAsync(Options(async token=>
        {
            using var registration=token.Register(()=>canceled=true);
            entered.TrySetResult();return await release.Task;
        }));
        try
        {
            Throws<ArgumentNullException>(()=>widget.Resource.TryPublishUpdate(null!));
            False(widget.Resource.TryPublishUpdate(value=>{mapped++;return(true,value);}),"Missing resource invoked update mapper.");
            var load=widget.Resource.EnsureLoaded();await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            False(widget.Resource.TryPublishUpdate(value=>{mapped++;return(true,value);}),"Loading-only resource accepted an update.");
            Equal(0,mapped);False(canceled,"Missing-value update canceled the existing read.");
            release.TrySetResult(new("loaded"));await load.Completion;
            var original=widget.Resource.Snapshot;var invalidations=0;
            widget.Invalidated+=(_,_)=>invalidations++;
            False(widget.Resource.TryPublishUpdate(_=>(false,null!)),"Declined update was accepted.");
            Throws<InvalidOperationException>(()=>widget.Resource.TryPublishUpdate(_=>throw new InvalidOperationException("mapper")));
            Throws<ArgumentNullException>(()=>widget.Resource.TryPublishUpdate(_=>(true,null!)));
            Equal(original,widget.Resource.Snapshot);Equal(0,invalidations);
            True(widget.Resource.TryPublishUpdate(value=>(true,value)),"Equal authoritative update was rejected.");
            Equal(original,widget.Resource.Snapshot);Equal(0,invalidations);
            True(widget.Resource.TryPublishUpdate(_=>(true,new("changed")),invalidate:false),"Silent update was rejected.");
            Equal("changed",widget.Resource.Snapshot.Value!.Name);Equal(0,invalidations);
        }
        finally{release.TrySetResult(new("cleanup"));await StopAsync(widget);}

        entered=Signal();release=new(TaskCreationOptions.RunContinuationsAsynchronously);canceled=false;
        widget=await StartAsync(Options(async token=>
        {
            using var registration=token.Register(()=>canceled=true);
            entered.TrySetResult();return await release.Task;
        }));
        try
        {
            widget.Resource.Publish(new("account-a"));
            var read=widget.Resource.Refresh();await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var before=widget.Resource.Snapshot;
            False(widget.Resource.TryPublishUpdate(value=>(false,new("wrong-owner"))),"Rejected domain update was published.");
            Equal(before,widget.Resource.Snapshot);False(canceled,"Rejected domain update canceled a valid read.");
            True(widget.Resource.TryPublishUpdate(value=>(true,value with{Name="confirmed"})),"Confirmed update was rejected.");
            True(canceled,"Confirmed update did not cancel the old read.");
            release.TrySetResult(new("stale"));await read.Completion;
            Equal("confirmed",widget.Resource.Snapshot.Value!.Name);
            Equal(WidgetOperationAdmission.Completed,widget.Resource.EnsureLoaded().Admission);
        }
        finally{release.TrySetResult(new("cleanup"));await StopAsync(widget);}
    }

    private static async Task AuthoritativeUpdatesAreAtomicAsync()
    {
        var widget=new PatchResourceWidget<PatchState>(new("account-a",0,0));
        await WidgetTestHost.InitializeAsync(widget);
        try
        {
            widget.Resource.Publish(new("account-a",0,7));
            var updates=Enumerable.Range(0,40).Select(_=>Task.Run(()=>widget.Resource.TryPublishUpdate(current=>
                (current.Account=="account-a",current with{Count=current.Count+1})))).ToArray();
            await Task.WhenAll(updates).WaitAsync(TimeSpan.FromSeconds(5));
            True(updates.All(update=>update.Result),"An accepted atomic increment was lost.");
            Equal(new PatchState("account-a",40,7),widget.Resource.Snapshot.Value);
            widget.Resource.Publish(new("account-b",100,9));
            False(widget.Resource.TryPublishUpdate(current=>(current.Account=="account-a",current with{Count=999})),
                "An old-owner mapper overwrote the latest publication.");
            Equal(new PatchState("account-b",100,9),widget.Resource.Snapshot.Value);
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
        var numbers=new PatchResourceWidget<int>(0);
        await WidgetTestHost.InitializeAsync(numbers);
        try
        {
            numbers.Resource.Publish(0);
            True(numbers.Resource.TryPublishUpdate(value=>(true,value+1)),"A loaded default value type was treated as absent.");
            Equal(1,numbers.Resource.Snapshot.Value);
            False(numbers.Resource.TryPublishUpdate(value=>(false,0)),"Value-type update rejection was ignored.");
            Equal(1,numbers.Resource.Snapshot.Value);
        }
        finally{await WidgetTestHost.DestroyAsync(numbers);}
    }

    private sealed record PatchState(string Account,int Count,int Unrelated);
    private sealed class PatchResourceWidget<TValue>:Widget where TValue:notnull
    {
        internal WidgetResource<TValue> Resource{get;}
        internal PatchResourceWidget(TValue initial)=>Resource=CreateResource("test.patch",new WidgetResourceOptions<TValue>
        {Load=_=>ValueTask.FromResult(initial),MapError=_=>WidgetResourceError.Unexpected});
        public override WidgetView Render()=>new(UI.Text("Patch fixture","root"));
    }
}
