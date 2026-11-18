using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class WidgetCursorResourceTests
{
    private static async Task ViewportRecovery()
    {
        var paged = await StartAsync(LiveOptions((cursor,direction,_,_) => ValueTask.FromResult(cursor is null
            ? LivePage(["6","7"],"6") : direction==WidgetCursorDirection.Before
                ? LivePage(["2","3"],"2","3") : LivePage(["6","7"],"6"))));
        await paged.Resource.EnsureLoaded().Completion;
        using(var around=paged.Resource.CaptureRecovery())
            True(paged.Resource.TryRecover(around,LivePage(["4","5","6","7"],"4"),new("4")),"Full recovery window was rejected");
        await paged.Resource.Prefetch(new WidgetCollectionBoundaryDemand(WidgetCursorDirection.Before,["4"]),"items.list").Completion;
        True(paged.Resource.Snapshot.Error is null,"Paging from a recovered full window rejected a protected surviving row");
        Equal("2,3,4,5",Keys(paged));
        await paged.Resource.Prefetch(new WidgetCollectionBoundaryDemand(WidgetCursorDirection.After,["5"]),"items.list").Completion;
        True(paged.Resource.Snapshot.Error is null,"Reverse paging from a recovered window failed");
        Equal("4,5,6,7",Keys(paged));
        Equal(4,paged.Resource.RetainedItemCount);
        await StopAsync(paged);
        var widget = await StartAsync(LiveOptions((_, _, _, _) => ValueTask.FromResult(LivePage(["8", "9"], "8"))));
        await widget.Resource.EnsureLoaded().Completion;
        using (var fullPage = widget.Resource.CaptureRecovery())
            True(widget.Resource.TryRecover(fullPage, LivePage(["6", "7", "8", "9"], "6"), new("8")), "Recovery rejected a bounded window larger than one normal page");
        var original = widget.Resource.Snapshot;
        var recovery = widget.Resource.CaptureRecovery();
        widget.Resource.ApplyMutations(widget.Resource.MutationScope, [Upsert("3", "Edited during recovery"), Upsert("10")]);
        True(widget.Resource.TryRecover(recovery, LivePage(["2", "3"], "2", "3"), new("3")), "Current around-page was rejected");
        Equal("2,3", Keys(widget));
        Equal("Edited during recovery", widget.Resource.Snapshot.Items[1].Text);
        Equal(original.ResetGeneration, widget.Resource.Snapshot.ResetGeneration);
        True(widget.Resource.Snapshot.WindowGeneration > original.WindowGeneration, "Recovered paint reused window generation");
        Equal<string?>(null, widget.Resource.Snapshot.RequestedFocusId);
        Equal(new WidgetCollectionItemKey("3"), widget.Resource.Snapshot.Anchor!.Value);
        True(!widget.Resource.TryRecover(recovery, LivePage(["2", "3"]), new("3")), "Recovery scope committed twice");
        widget.Resource.ApplyMutations(widget.Resource.MutationScope, [Upsert("11")], true);
        Equal("2,3", Keys(widget));

        var missing = widget.Resource.CaptureRecovery();
        True(!widget.Resource.TryRecover(missing, LivePage(["4", "5"]), new("3")), "Missing anchor replaced a reader");
        var deleted = widget.Resource.CaptureRecovery();
        widget.Resource.ApplyMutations(widget.Resource.MutationScope, [Remove("3")]);
        True(!widget.Resource.TryRecover(deleted, LivePage(["2", "3"], "2", "3"), new("3")), "Late around response resurrected deleted anchor");

        var navigated = widget.Resource.CaptureRecovery();
        await widget.Resource.Refresh().Completion;
        True(!widget.Resource.TryRecover(navigated, LivePage(["2", "3"]), new("3")), "Recovery overwrote a newer navigation");
        var reset = widget.Resource.CaptureRecovery();
        widget.Resource.Reset();
        True(!widget.Resource.TryRecover(reset, LivePage(["2", "3"]), new("3")), "Recovery crossed a semantic reset");
        await widget.Resource.EnsureLoaded().Completion;
        var inactive = widget.Resource.CaptureRecovery();
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        True(!widget.Resource.TryRecover(inactive, LivePage(["2", "3"]), new("3")), "Recovery published after lifecycle exit");
        await WidgetTestHost.DestroyAsync(widget);

        var bounded = await StartAsync(LiveOptions((_, _, _, _) => ValueTask.FromResult(LivePage(["8", "9"], "8"))));
        await bounded.Resource.EnsureLoaded().Completion;
        using var overflow = bounded.Resource.CaptureRecovery();
        var mutationScope = bounded.Resource.MutationScope;
        bounded.Resource.ApplyMutations(mutationScope, Enumerable.Range(100, 512).Select(id => Upsert(id.ToString())).ToArray(), true);
        True(bounded.Resource.ApplyMutations(mutationScope, [Upsert("612")], true).RequiresRefresh,
            "Overflowed recovery journal did not request a bounded refresh");
        True(!bounded.Resource.TryRecover(overflow, LivePage(["2", "3"]), new("3")), "Recovery committed after losing a live event");
        await StopAsync(bounded);

        var deletedNeighbor = await StartAsync(LiveOptions((_, _, _, _) => ValueTask.FromResult(LivePage(["8", "9"], "8"))));
        await deletedNeighbor.Resource.EnsureLoaded().Completion;
        using (var around = deletedNeighbor.Resource.CaptureRecovery())
        {
            deletedNeighbor.Resource.ApplyMutations(deletedNeighbor.Resource.MutationScope, [Remove("3")]);
            True(deletedNeighbor.Resource.TryRecover(around, LivePage(["2", "3", "4"], "2", "4"), new("3"),
                [new("4"), new("2")], out var restored), "Deleted anchor discarded available old neighbors");
            Equal(new WidgetCollectionItemKey("4"), restored!.Value);
            Equal("2,4", Keys(deletedNeighbor));
            Equal(new WidgetCollectionItemKey("4"), deletedNeighbor.Resource.Snapshot.Anchor!.Value);
        }
        using (var around = deletedNeighbor.Resource.CaptureRecovery())
        {
            True(deletedNeighbor.Resource.TryRecover(around, LivePage(["1", "2"], "1", "2"), new("3"),
                [new("4"), new("2")], out var restored), "Previous surviving neighbor was not considered");
            Equal(new WidgetCollectionItemKey("2"), restored!.Value);
        }
        using (var around = deletedNeighbor.Resource.CaptureRecovery())
            True(!deletedNeighbor.Resource.TryRecover(around, LivePage(["7", "8"]), new("3"),
                [new("4"), new("2")], out _), "Unrelated around page changed the reader without a surviving candidate");
        await StopAsync(deletedNeighbor);
    }
}
