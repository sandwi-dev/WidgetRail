using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class WidgetCursorResourceTests
{
    private static WidgetCursorResourceOptions<Item> LiveOptions(
        Func<WidgetCollectionCursor?,WidgetCursorDirection?,int,CancellationToken,ValueTask<WidgetCursorPage<Item>>> load) =>
        Options(10,pageSize:2,maximumRetainedItems:4,@async:load) with
        { LiveUpdates=new((left,right)=>int.Parse(left.Id).CompareTo(int.Parse(right.Id)),item=>new(item.Id)) };
    private static WidgetCursorPage<Item> LivePage(string[] ids,string? before=null,string? after=null) =>
        new(ids.Select(id=>new Item(id,"Original")).ToArray(),before is null?null:new(before),after is null?null:new(after));
    private static WidgetCursorMutation<Item> Upsert(string id,string text="New")=>new(new(id),new(id,text));
    private static WidgetCursorMutation<Item> Remove(string id)=>new(new(id),null,true);
    private static string Keys(FixtureWidget widget)=>string.Join(",",widget.Resource.Snapshot.Items.Select(item=>item.Id));

    private static async Task LiveMutations()
    {
        var widget=await StartAsync(LiveOptions((_,_,_,_)=>ValueTask.FromResult(LivePage(["2","3"],"2"))));
        await widget.Resource.EnsureLoaded().Completion;
        var scope=widget.Resource.MutationScope;var reset=widget.Resource.Snapshot.ResetGeneration;
        widget.Resource.SelectAnchor(new("2"));
        var changed=widget.Resource.ApplyMutations(scope,[Upsert("2","Edited"),Remove("3"),Upsert("4")]);
        True(changed.Applied&&!changed.HasUnseenItems,"Live update was not admitted");
        Equal("2,4",Keys(widget));Equal("Edited",widget.Resource.Snapshot.Items[0].Text);
        Equal(reset,widget.Resource.Snapshot.ResetGeneration);Equal(new WidgetCollectionItemKey("2"),widget.Resource.Snapshot.Anchor!.Value);
        Equal<string?>(null,widget.Resource.Snapshot.RequestedFocusId);
        widget.Resource.ApplyMutations(scope,[Upsert("4"),Upsert("5"),Upsert("6")]);
        Equal("2,4,5,6",Keys(widget));
        var bounded=widget.Resource.ApplyMutations(scope,[Upsert("7")]);
        True(bounded.HasUnseenItems,"Capacity must preserve a reader rather than evict without authority");Equal("2,4,5,6",Keys(widget));
        var advanced=widget.Resource.ApplyMutations(scope,[],allowTailEviction:true);
        True(!advanced.HasUnseenItems,"Authorized tail advance remained pending");Equal("4,5,6,7",Keys(widget));
        Equal(new WidgetCollectionCursor("4"),widget.Resource.Snapshot.Before!.Value);
        widget.Resource.Reset();True(!widget.Resource.ApplyMutations(scope,[Upsert("8")]).Applied,"Old reset scope admitted a mutation");
        await StopAsync(widget);

        TaskCompletionSource<WidgetCursorPage<Item>> pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed=await StartAsync(LiveOptions((_,_,_,_)=>new(pending.Task)));
        var load=delayed.Resource.EnsureLoaded();
        delayed.Resource.ApplyMutations(delayed.Resource.MutationScope,[Upsert("2","Current"),Remove("3"),Upsert("4")]);
        pending.SetResult(LivePage(["2","3"],"2"));await load.Completion;
        Equal("2,4",Keys(delayed));Equal("Current",delayed.Resource.Snapshot.Items[0].Text);
        pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh=delayed.Resource.Refresh();
        delayed.Resource.ApplyMutations(delayed.Resource.MutationScope,[Upsert("2","While loading"),Remove("4")]);
        var background=WidgetTestHost.SetLifecycleStateAsync(delayed,WidgetLifecycleState.Background);
        // This fixture deliberately ignores cancellation, like a provider that finishes late.
        pending.SetResult(LivePage(["2","3"],"2"));await background;await refresh.Completion;
        Equal("2",Keys(delayed));Equal("While loading",delayed.Resource.Snapshot.Items[0].Text);
        True(!delayed.Resource.ApplyMutations(delayed.Resource.MutationScope,[Upsert("9")]).Applied,"Inactive live scope admitted paint");
        await WidgetTestHost.DestroyAsync(delayed);

        var older=await StartAsync(LiveOptions((_,_,_,_)=>ValueTask.FromResult(LivePage(["2","3"],"2","3"))));
        await older.Resource.EnsureLoaded().Completion;
        var oldResult=older.Resource.ApplyMutations(older.Resource.MutationScope,[Upsert("2","Edit old row"),Upsert("9")],true);
        Equal("2,3",Keys(older));True(oldResult.HasUnseenItems,"Older windows must not splice in a distant live tail");
        await StopAsync(older);

        var invalid=await StartAsync(LiveOptions((_,_,_,_)=>ValueTask.FromResult(LivePage(["2","3"],"2"))) with
        {LiveUpdates=new((_,_)=>throw new InvalidOperationException("Bad provider comparison"),item=>new(item.Id))});
        await invalid.Resource.EnsureLoaded().Completion;
        Throws<InvalidOperationException>(()=>invalid.Resource.ApplyMutations(invalid.Resource.MutationScope,[Upsert("4")]));
        Equal("2,3",Keys(invalid));await StopAsync(invalid);

        TaskCompletionSource<WidgetCursorPage<Item>> boundaryPage=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var boundary=await StartAsync(LiveOptions((cursor,_,_,_)=>cursor is null?
            ValueTask.FromResult(LivePage(["2","3"],"2","3")):new(boundaryPage.Task)));
        await boundary.Resource.EnsureLoaded().Completion;boundary.Resource.SelectAnchor(new("2"));
        var olderScope=boundary.Resource.MutationScope;
        var paging=boundary.Resource.Prefetch(WidgetCursorDirection.After,"items.list");
        boundary.Resource.ApplyMutations(olderScope,[Remove("2")],boundaries:new(new("2"),new("9")));
        boundaryPage.SetResult(LivePage(["4","5"],"4","5"));await paging.Completion;
        Equal("3",Keys(boundary));Equal(new WidgetCollectionItemKey("3"),boundary.Resource.Snapshot.Anchor!.Value);
        Equal(new WidgetCollectionCursor("9"),boundary.Resource.Snapshot.After!.Value);
        boundary.Resource.ApplyMutations(olderScope,[Remove("3")],boundaries:new(new("2"),new("9")));
        Equal("",Keys(boundary));Equal(new WidgetCollectionCursor("9"),boundary.Resource.Snapshot.After!.Value);
        await StopAsync(boundary);
        True(!boundary.Resource.ApplyMutations(olderScope,[Upsert("10")]).Applied,"Destroyed resource admitted a late mutation");

        var continuous=await StartAsync(LiveOptions((_,_,_,_)=>ValueTask.FromResult(LivePage(["0","1"]))));
        await continuous.Resource.EnsureLoaded().Completion;var continuousScope=continuous.Resource.MutationScope;
        for(var index=2;index<=1000;index++)
            True(continuous.Resource.ApplyMutations(continuousScope,[Upsert(index.ToString())],true).Applied,"Committed mutations leaked journal capacity");
        Equal("997,998,999,1000",Keys(continuous));Equal(4,continuous.Resource.RetainedItemCount);await StopAsync(continuous);

        var reading=await StartAsync(LiveOptions((_,_,_,_)=>ValueTask.FromResult(LivePage(["0","1"]))));
        await reading.Resource.EnsureLoaded().Completion;var readingScope=reading.Resource.MutationScope;
        for(var index=2;index<=1000;index++)
            True(reading.Resource.ApplyMutations(readingScope,[Upsert(index.ToString())]).Applied,"Deferred tail leaked journal capacity");
        Equal("0,1,2,3",Keys(reading));Equal(new WidgetCollectionCursor("3"),reading.Resource.Snapshot.After!.Value);
        True(!reading.Resource.ApplyMutations(readingScope,[],true).HasUnseenItems,"A gapped tail should use provider paging, not retain an unbounded local queue");
        Equal("0,1,2,3",Keys(reading));await StopAsync(reading);

        var focused=await StartAsync(LiveOptions((cursor,_,_,_)=>ValueTask.FromResult(cursor is null?
            LivePage(["2","3"],"2","3"):LivePage(["4","5"],"4"))));
        await focused.Resource.EnsureLoaded().Completion;
        await focused.Resource.Move(WidgetCursorDirection.After,"items.list").Completion;
        Equal("focus.4",focused.Resource.Snapshot.RequestedFocusId);
        focused.Resource.ApplyMutations(focused.Resource.MutationScope,[Remove("4")]);
        Equal<string?>(null,focused.Resource.Snapshot.RequestedFocusId);
        await StopAsync(focused);

        var unordered=await StartAsync(LiveOptions((_,_,_,_)=>ValueTask.FromResult(LivePage(["0","1"]))));
        await unordered.Resource.EnsureLoaded().Completion;var orderScope=unordered.Resource.MutationScope;
        unordered.Resource.ApplyMutations(orderScope,[Upsert("4")]);
        unordered.Resource.ApplyMutations(orderScope,[Upsert("3")]);
        Equal("0,1,3,4",Keys(unordered));
        True(unordered.Resource.ApplyMutations(orderScope,[Upsert("2")]).HasUnseenItems,"An out-of-order insertion at capacity must remain deferred");
        unordered.Resource.ApplyMutations(orderScope,[],true);
        Equal("1,2,3,4",Keys(unordered));await StopAsync(unordered);
    }
}
