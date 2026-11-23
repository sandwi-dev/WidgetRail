using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class CollectionBoundaryTests
{
    internal static async Task Run()
    {
        var widget=new Fixture();await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        try
        {
            var intent=widget.NewFocusRequest();
            var before=widget.Source.Descriptor;
            var deferred=intent.DeferCollectionReveal(widget.Source,"items");
            var declaration=widget.Render() with{ScrollRevealRequest=deferred};
            var snapshot=declaration.CreateSnapshot("boundary.defer",1);
            Check(snapshot.ProtocolVersion==ProtocolConstants.CurrentVersion && deferred.IndexedItem is null,
                "Deferred reveal publishes its admission before a target exists");
            Check(ViewSnapshotValidator.Validate(snapshot with{ProtocolVersion=ProtocolConstants.TargetedCollectionPresentationVersion-1})
                .Any(error=>error.Code=="feature_requires_version"&&error.Path=="$.scrollRevealRequest.deferredSource"),
                "Deferred reveal still requires its introduction version while indexed templates advertise current SDK capabilities");
            widget.Source.Publish("initial","account",[1,2,3]);
            var target=intent.RevealCollection(widget.Source,"items",widget.Source.FocusTarget("items",new("item-3"),2));
            Check(target.RequestId==deferred.RequestId && IndexedCollectionContract.SameViewportDomain(widget.Source.Descriptor,before),
                "First data can complete the same intent within its source continuity domain");
            Check(ViewSnapshotValidator.Validate(snapshot with{ScrollRevealRequest=deferred with{IndexedItem=target.IndexedItem}})
                .Any(error=>error.Code=="invalid_deferred_reveal"),"A deferred reveal cannot also claim a resolved target");
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"boundaries");host.PublishSnapshot();
            var id=Guid.NewGuid().ToString("N");
            var request=new IndexedViewportObservation(id,1,true,false){PhysicalAtEnd=true,Boundary=IndexedCollectionBoundary.Before,ProtectedKeys=["item-1"]};
            await host.ObserveViewportAsync("items",request);
            Check(widget.Demands==1 && widget.Last is {Direction:WidgetCursorDirection.Before,ProtectedKeys.Count:1},"Boundary keeps direction and captured occurrence protection");
            Check(widget.Source.ViewportState is {IsAtEnd:true,FollowsEnd:false},"Physical end does not authorize eviction of an inspected reader");
            await host.ObserveViewportAsync("items",request);
            Check(widget.Demands==1,"Repeated host sequence does not duplicate a boundary callback");
            var other=Guid.NewGuid().ToString("N");
            await host.ObserveViewportAsync("items",new(other,1,true,false));
            await host.ObserveViewportAsync("items",request with{Sequence=2});
            Check(widget.Last!.ProtectedKeys.Select(key=>key.Value).SequenceEqual(new[]{"item-1","item-2","item-3"}),
                "A second active reader protects the entire bounded resident window from another view's page eviction");
            await host.ObserveViewportAsync("items",new(other,2,false,false));
            var current=widget.Source.Descriptor;
            try{widget.Source.Publish("too-large","account",Enumerable.Range(0,IndexedCollectionLimits.MaximumContinuityItems+1).ToArray());
                throw new InvalidOperationException("Oversized boundary window was admitted");}
            catch(ArgumentException){}
            Check(widget.Source.Descriptor==current && widget.Source.Items.Count==3,
                "Oversized boundary publication fails before changing the active query or items");
            widget.Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending=host.ObserveViewportAsync("items",request with{Sequence=3,Boundary=IndexedCollectionBoundary.After});
            await widget.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            widget.Source.Publish("replacement","new-account",[9],forceReset:true);
            try{await pending;throw new InvalidOperationException("Old boundary survived semantic replacement");}
            catch(OperationCanceledException){}
            Check(widget.Completed==2 && widget.Source.ViewportState.ActiveViews==0,"Semantic replacement cancels pending adjacency and revokes old reader feedback");
        }
        finally{widget.Gate?.TrySetResult();await WidgetTestHost.DestroyAsync(widget);}
    }
    private sealed class Fixture:Widget
    {
        internal WidgetLoadedCollection<string,int> Source;
        internal int Demands,Completed;
        internal WidgetCollectionBoundaryDemand? Last;
        internal TaskCompletionSource? Gate;
        internal TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal WidgetFocusRequest NewFocusRequest()=>CreateFocusRequest();
        internal Fixture()=>Source=CreateLoadedCollection<string,int>("boundary.rows","initial","account",[1,2],new()
        {
            PreserveOccurrences=true,FollowEnd=true,ItemKey=value=>new("item-"+value),
            RenderItem=(_,value,ids)=>UI.Button(value.ToString(),"open",ids.Id("row")),OnAction=(_,_,_,_)=>ValueTask.CompletedTask,
            OnBoundaryDemand=async(_,demand,token)=>{Demands++;Last=demand;if(Gate is {} gate){Started.TrySetResult();await gate.Task.WaitAsync(token);}Completed++;}
        });
        public override WidgetView Render()=>new(UI.Stack("root",UI.CollectionList("items",Source,44,"Items").AdjacentPages(true,true)),"items",ActiveInputScopeId:"root");
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
