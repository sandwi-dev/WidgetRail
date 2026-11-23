using WidgetRail.WidgetSdk;

internal static partial class WidgetCursorResourceTests
{
    private static async Task PreparedPagesStaySeparateAndBounded()
    {
        var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var widget = await StartAsync(Options(48, pageSize:4, maximumRetainedItems:8, @async:(cursor, direction, limit, token) =>
        {
            requests.Enqueue(cursor?.Value ?? "initial");
            return ValueTask.FromResult(Page(cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1)), limit,48));
        }) with { PrepareAdjacentPages=true });
        await widget.Resource.EnsureLoaded().Completion;
        await Until(()=>requests.Contains("c4"));
        var first=widget.Resource.Snapshot;
        Equal(4,first.Items.Count);
        Equal(WidgetPagedResourceStatus.Ready,first.Status);
        for(var index=1;index<12;index++)
        {
            var next=$"c{index*4}";
            await Until(()=>requests.Contains(next));
            var before=requests.Count(value=>value==next);
            var result=await widget.Resource.Prefetch(WidgetCursorDirection.After,"items.list").Completion;
            Equal(WidgetOperationStatus.Succeeded,result.Status);
            Equal(before,requests.Count(value=>value==next));
            True(widget.Resource.RetainedItemCount<=8,"Speculation expanded the admitted window");
            Equal<string?>(null,widget.Resource.Snapshot.RequestedFocusId);
        }
        True(!widget.Resource.Snapshot.HasAfter,"Terminal edge should stop preparation");
        for(var index=0;index<10;index++)
        {
            Equal(WidgetOperationStatus.Succeeded,(await widget.Resource.Prefetch(WidgetCursorDirection.Before,"items.list").Completion).Status);
            True(widget.Resource.RetainedItemCount<=8,"Reverse traversal exceeded capacity");
        }
        True(!widget.Resource.Snapshot.HasBefore,"Reverse traversal should reach the first page");
        await StopAsync(widget);
    }

    private static async Task PreparedDemandJoinsAndRejectsStaleResults()
    {
        var started=Signal(); var release=new TaskCompletionSource<WidgetCursorPage<Item>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls=0; var hold=true;
        var widget=await StartAsync(Options(20,pageSize:4,maximumRetainedItems:8,@async:async(cursor,_,limit,token)=>
        {
            if(cursor?.Value=="c4")
            {
                Interlocked.Increment(ref calls);
                if(hold){started.TrySetResult();return await release.Task;}
            }
            return Page(cursor is null?0:int.Parse(cursor.Value.Value.AsSpan(1)),limit,20);
        }) with{PrepareAdjacentPages=true});
        await widget.Resource.EnsureLoaded().Completion; await started.Task;
        var demand=widget.Resource.Prefetch(WidgetCursorDirection.After,"items.list");
        var duplicate=widget.Resource.Prefetch(WidgetCursorDirection.After,"items.list");
        Equal(WidgetOperationAdmission.Joined,duplicate.Admission); Equal(1,calls);
        widget.Resource.Reset(); hold=false;
        await widget.Resource.EnsureLoaded().Completion;
        release.SetResult(Page(4,4,20) with{Items=[new("stale.1"),new("stale.2"),new("stale.3"),new("stale.4")]});
        await demand.Completion;
        await widget.Resource.Prefetch(WidgetCursorDirection.After,"items.list").Completion;
        True(widget.Resource.Snapshot.Items.All(item=>!item.Id.StartsWith("stale")),"Old query preparation committed after reset");
        await StopAsync(widget);
    }

    private static async Task PreparedFailuresStayQuietAndDemandRetries()
    {
        var fail=true;var calls=0;
        var widget=await StartAsync(Options(12,pageSize:4,maximumRetainedItems:8,@async:(cursor,_,limit,token)=>
        {
            if(cursor is not null){Interlocked.Increment(ref calls);if(fail)throw new InvalidOperationException("Speculative failure");}
            return ValueTask.FromResult(Page(cursor is null?0:int.Parse(cursor.Value.Value.AsSpan(1)),limit,12));
        }) with{PrepareAdjacentPages=true});
        await widget.Resource.EnsureLoaded().Completion;await Until(()=>calls>0);
        Equal(WidgetPagedResourceStatus.Ready,widget.Resource.Snapshot.Status);
        Equal<WidgetResourceError?>(null,widget.Resource.Snapshot.Error);
        fail=false;
        Equal(WidgetOperationStatus.Succeeded,(await widget.Resource.Prefetch(WidgetCursorDirection.After,"items.list").Completion).Status);
        Equal(8,widget.Resource.Snapshot.Items.Count);
        await StopAsync(widget);
    }

    private static async Task PreparedPagesRespectVisibilityAndProtectedEviction()
    {
        var widget=await StartAsync(Options(24,pageSize:4,maximumRetainedItems:8) with{PrepareAdjacentPages=true});
        await widget.Resource.EnsureLoaded().Completion;
        await widget.Resource.Prefetch(WidgetCursorDirection.After,"items.list").Completion;
        await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Background);
        await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Visible);
        var demand=new WidgetCollectionBoundaryDemand(WidgetCursorDirection.After,[new("item.0")]);
        var result=await widget.Resource.Prefetch(demand,"items.list").Completion;
        Equal(WidgetOperationStatus.Succeeded,result.Status);
        Equal(WidgetPagedResourceStatus.Ready,widget.Resource.Snapshot.Status);
        Equal(8,widget.Resource.RetainedItemCount);
        True(widget.Resource.Snapshot.Items.Any(item=>item.Id=="item.0"),"Prepared page evicted protected content");
        var readyWindow=widget.Resource.Snapshot.WindowGeneration;
        await widget.Resource.Prefetch(new WidgetCollectionBoundaryDemand(WidgetCursorDirection.After,["item.4"]),"items.list").Completion;
        True(widget.Resource.Snapshot.WindowGeneration>readyWindow && !widget.Resource.Snapshot.Items.Any(item=>item.Id=="item.0"),
            "Deferred early demand must commit when the opposite page leaves the reader's viewport");
        await StopAsync(widget);
    }

    private static async Task PreparedDeferralRetainsBothDirections()
    {
        foreach (var direction in new[] { WidgetCursorDirection.Before, WidgetCursorDirection.After })
        {
            var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var initial = direction == WidgetCursorDirection.Before ? 8 : 0;
            var next = direction == WidgetCursorDirection.Before ? "c0" : "c8";
            var protectedKey = direction == WidgetCursorDirection.Before ? "item.8" : "item.0";
            var widget = await StartAsync(Options(24, pageSize: 4, maximumRetainedItems: 8,
                @async: (cursor, _, limit, _) =>
                {
                    requests.Enqueue(cursor?.Value ?? "initial");
                    return ValueTask.FromResult(VirtualPage(cursor is null ? initial : int.Parse(cursor.Value.Value.AsSpan(1)), limit, 24));
                }) with { PrepareAdjacentPages = true });
            try
            {
                await widget.Resource.EnsureLoaded().Completion;
                await widget.Resource.Prefetch(direction, "items.list").Completion;
                await Until(() => requests.Contains(next));
                var before = widget.Resource.Snapshot;
                var nextReads = requests.Count(value => value == next);
                for (var repeat = 0; repeat < 5; repeat++)
                {
                    var result = await widget.Resource.Prefetch(
                        new WidgetCollectionBoundaryDemand(direction, [new(protectedKey)]), "items.list").Completion;
                    Equal(WidgetOperationStatus.Succeeded, result.Status);
                    Equal(WidgetPagedResourceStatus.Ready, widget.Resource.Snapshot.Status);
                    Equal<WidgetResourceError?>(null, widget.Resource.Snapshot.Error);
                    Equal(before.WindowGeneration, widget.Resource.Snapshot.WindowGeneration);
                    Equal(before.Anchor, widget.Resource.Snapshot.Anchor);
                    Equal(string.Join(",", before.Items.Select(item => item.Id)), Keys(widget));
                    Equal(nextReads, requests.Count(value => value == next));
                }
                await widget.Resource.Prefetch(new WidgetCollectionBoundaryDemand(direction, ["item.4"]), "items.list").Completion;
                Equal(direction == WidgetCursorDirection.Before
                    ? "item.0,item.1,item.2,item.3,item.4,item.5,item.6,item.7"
                    : "item.4,item.5,item.6,item.7,item.8,item.9,item.10,item.11", Keys(widget));
                Equal(nextReads, requests.Count(value => value == next));
                Equal<string?>(null, widget.Resource.Snapshot.RequestedFocusId);
            }
            finally { await StopAsync(widget); }
        }
    }

    private static async Task PreparedDeferralStillValidatesIncomingWindow()
    {
        foreach (var malformedExtent in new[] { false, true })
        {
            var widget = await StartAsync(Options(24, pageSize: 4, maximumRetainedItems: 8,
                @async: (cursor, _, limit, _) =>
                {
                    var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
                    var page = VirtualPage(start, limit, 24);
                    if (start == 8) page = malformedExtent
                        ? page with { FirstItemIndex = 9 }
                        : page with { Items = [new("item.0"), new("item.9"), new("item.10"), new("item.11")] };
                    return ValueTask.FromResult(page);
                }) with { PrepareAdjacentPages = true });
            try
            {
                await widget.Resource.EnsureLoaded().Completion;
                await widget.Resource.Prefetch(WidgetCursorDirection.After, "items.list").Completion;
                var before = Keys(widget);
                await widget.Resource.Prefetch(new WidgetCollectionBoundaryDemand(WidgetCursorDirection.After, ["item.0"]), "items.list").Completion;
                Equal(WidgetPagedResourceStatus.Error, widget.Resource.Snapshot.Status);
                Equal(WidgetResourceError.InvalidPage, widget.Resource.Snapshot.Error);
                Equal(before, Keys(widget));
            }
            finally { await StopAsync(widget); }
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while(!condition())await Task.Delay(5,timeout.Token);
    }

    private static async Task PreparedMalformedAndHiddenResults()
    {
        var calls=0;
        var widget=await StartAsync(Options(12,pageSize:4,maximumRetainedItems:8,@async:(cursor,_,limit,token)=>
        {
            if(cursor is null)return ValueTask.FromResult(Page(0,limit,12));
            if(Interlocked.Increment(ref calls)==1)return ValueTask.FromResult(new WidgetCursorPage<Item>([new("duplicate"),new("duplicate")],null,null));
            return ValueTask.FromResult(new WidgetCursorPage<Item>([],new("c0"),null));
        }) with{PrepareAdjacentPages=true});
        await widget.Resource.EnsureLoaded().Completion;await Until(()=>calls>0);
        Equal(WidgetPagedResourceStatus.Ready,widget.Resource.Snapshot.Status);
        Equal(WidgetOperationStatus.Succeeded,(await widget.Resource.Prefetch(WidgetCursorDirection.After,"items.list").Completion).Status);
        Equal(4,widget.Resource.RetainedItemCount);
        True(!widget.Resource.Snapshot.HasAfter,"An empty terminal page should close the edge");
        await StopAsync(widget);

        var started=Signal();var release=new TaskCompletionSource<WidgetCursorPage<Item>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelObserved=Signal();var heldCalls=0;
        var hidden=await StartAsync(Options(12,pageSize:4,maximumRetainedItems:8,@async:async(cursor,_,limit,token)=>
        {
            if(cursor is null)return Page(0,limit,12);
            if(Interlocked.Increment(ref heldCalls)==1)
            {
                using var registration=token.Register(()=>cancelObserved.TrySetResult());
                started.TrySetResult();return await release.Task;
            }
            return Page(4,limit,12) with{Items=[new("fresh.1"),new("fresh.2"),new("fresh.3"),new("fresh.4")]};
        }) with{PrepareAdjacentPages=true});
        await hidden.Resource.EnsureLoaded().Completion;await started.Task;
        var retirement=WidgetTestHost.SetLifecycleStateAsync(hidden,WidgetLifecycleState.Background);
        await cancelObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult(Page(4,4,12));await retirement;
        Equal(4,hidden.Resource.RetainedItemCount);
        await WidgetTestHost.SetLifecycleStateAsync(hidden,WidgetLifecycleState.Visible);
        await hidden.Resource.Prefetch(WidgetCursorDirection.After,"items.list").Completion;
        True(hidden.Resource.Snapshot.Items.Any(item=>item.Id=="fresh.1"),"Hidden-lifetime preparation leaked into the new visible lifetime");
        await StopAsync(hidden);
    }

    private static async Task PreparedLiveChangesAndReversal()
    {
        var delayed=new TaskCompletionSource<WidgetCursorPage<Item>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls=0;var started=Signal();
        var widget=await StartAsync(LiveOptions(async(cursor,direction,_,token)=>
        {
            if(cursor is null)return LivePage(["4","5"],"4","5");
            if(direction==WidgetCursorDirection.After)return LivePage(["6","7"],"6");
            if(Interlocked.Increment(ref calls)==1){started.TrySetResult();return await delayed.Task;}
            return LivePage(["2","3"],null,"3") with{Items=[new("2","Fresh"),new("3")]};
        }) with{PrepareAdjacentPages=true});
        await widget.Resource.EnsureLoaded().Completion;await started.Task;
        var older=widget.Resource.Prefetch(WidgetCursorDirection.Before,"items.list");
        // A live edit during preparation invalidates the unpublished response;
        // the later demand must not resurrect a deleted or edited attachment.
        widget.Resource.ApplyMutations(widget.Resource.MutationScope,[Upsert("4","Changed")]);
        delayed.SetResult(LivePage(["2","3"],null,"3"));
        Equal(WidgetOperationStatus.Succeeded,(await older.Completion).Status);
        Equal("Fresh",widget.Resource.Snapshot.Items[0].Text);
        True(widget.Resource.Snapshot.Items.Any(item=>item.Id=="4"&&item.Text=="Changed"),"Live edits were lost by preparation");
        await StopAsync(widget);

        var beforeStarted=Signal();var beforeGate=new TaskCompletionSource<WidgetCursorPage<Item>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reverse=await StartAsync(LiveOptions(async(cursor,direction,_,token)=>
        {
            if(cursor is null)return LivePage(["4","5"],"4","5");
            if(direction==WidgetCursorDirection.Before){beforeStarted.TrySetResult();return await beforeGate.Task;}
            return LivePage(["6","7"],"6");
        }) with{PrepareAdjacentPages=true});
        await reverse.Resource.EnsureLoaded().Completion;await beforeStarted.Task;
        var movingBefore=reverse.Resource.Prefetch(WidgetCursorDirection.Before,"items.list");
        var movingAfter=reverse.Resource.Prefetch(WidgetCursorDirection.After,"items.list");
        Equal(WidgetOperationStatus.Succeeded,(await movingAfter.Completion).Status);
        beforeGate.SetResult(LivePage(["2","3"],null,"3"));
        await movingBefore.Completion;
        Equal("4,5,6,7",Keys(reverse));
        await StopAsync(reverse);
    }
}
