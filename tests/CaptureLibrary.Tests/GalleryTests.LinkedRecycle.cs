using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task RemovedDetailReturnsToNeighbor()
    {
        foreach (var (count, index) in new[] { (3, 0), (3, 1), (3, 2), (1, 0) })
        {
            await using var fixture = new GalleryFixture();
            fixture.Items.RemoveRange(count, fixture.Items.Count - count);
            await fixture.Start();
            using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "delete-focus", 100);
            fixture.Host = host;
            using (var lease = await host.AcquireAsync("capture-library.entries", 0, count))
                Check(lease.RouteAction(fixture.Items[0].Id, ControllerButton.A) is not null, "Could not open capture");
            await fixture.Until(() => Preview(fixture) is not null && !fixture.Busy);
            for (int step = 0; step < index; step++)
            {
                await fixture.Action("capture-library.viewer.next");
                await fixture.Until(() => !fixture.Busy);
            }
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.GroupsOverride = async (request, token) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                var remaining = fixture.Items.Count;
                return new(remaining == 0 ? [] : [new("unknown", remaining)], remaining, request.Revision!);
            };
            await fixture.Action("capture-library.delete");
            await fixture.Action("capture-library.delete.confirm");
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(fixture.View().FocusGroupEntryRequest is null, "Published stale focus before the post-delete query completed");
            }
            finally { release.TrySetResult(); }
            await fixture.Until(() => !fixture.Busy);
            var focus = fixture.View().FocusGroupEntryRequest;
            if (count == 1)
                Check(focus?.TargetId == "capture-library.search", "Empty gallery lost toolbar focus");
            else
            {
                var nearest = Math.Min(index, count - 2);
                Check(focus?.IndexedItem is { } item && item.Index == nearest && item.ItemKey == fixture.Items[nearest].Id,
                    "Removal did not focus the surviving neighbor of the capture on screen");
            }
        }
    }

    internal static async Task BackPublishesScopeWithPage()
    {
        await using var fixture = new GalleryFixture();
        fixture.Replay = fixture.Replay with { Phase = CaptureReplayPhase.Error, Settings = fixture.Replay.Settings with { Enabled = true } };
        await fixture.Start();
        await fixture.Until(() => fixture.Node("capture-library.replay.stop.hint") is not null);
        var home = fixture.View();
        using var host = await OpenEditingSource(fixture);
        var detail = fixture.View();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ReleaseOverride = async (_, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return new(true);
        };
        var refreshing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.GroupsOverride = async (request, token) =>
        {
            refreshing.TrySetResult();
            await finishRefresh.Task.WaitAsync(token);
            return new([new("unknown", fixture.Items.Count)], fixture.Items.Count, request.Revision!);
        };
        await fixture.Action("capture-library.back");
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var waiting = fixture.View();
            Check(waiting.Root.Transition?.Key == detail.Root.Transition?.Key &&
                waiting.ActiveInputScopeId == detail.ActiveInputScopeId, "Pending release changed the displayed detail's scope");
        }
        finally { release.TrySetResult(); }
        try
        {
            await refreshing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!fixture.Busy && fixture.Node("capture-library.replay.stop.hint")?.ControllerPrompt == ControllerPrompt.LeftBumper,
                "Back published an intermediate busy gallery without its replay hint");
        }
        finally { finishRefresh.TrySetResult(); }
        var back = fixture.View();
        Check(back.ActiveInputScopeId == home.ActiveInputScopeId && back.Root.Transition?.Key == home.Root.Transition?.Key &&
            back.FocusGroupEntryRequest is { RestoreViewport: true, IndexedItem: { } row } && row.ItemKey == fixture.Items[0].Id,
            "Back lost the home page, original capture, or saved viewport");
    }

    internal static async Task LinkedOriginalRecycleIsSeparateFromRemoval()
    {
        foreach(var recycle in new[]{false,true})
        {
            var calls=0;CaptureLibraryRecycleRequest? request=null;
            await using var fixture=new GalleryFixture();
            fixture.RecycleLinkedOverride=(value,_)=>
                {calls++;request=value;fixture.Items.RemoveAll(item=>item.Id==value.Items[0].Id);fixture.Notify();return ValueTask.FromResult(new CaptureLibraryBatchResult([new(value.Items[0].Id)]));};
            fixture.Items[0]=fixture.Items[0] with{SourceId=new string('b',32),Revision=7,MetadataRevision=9};
            var original=fixture.Items[0];await fixture.Start();using var host=await OpenEditingSource(fixture);
            Check(fixture.Node("capture-library.delete")?.Text=="Hide from library"&&
                fixture.Node("capture-library.recycle-original") is {IsDisabled:not true},"Linked detail does not offer both independent actions");
            await fixture.Action("capture-library.recycle-original");
            Check(fixture.View().InitialFocusId=="capture-library.delete.cancel"&&calls==0&&fixture.PermissionRequests.Count==0,
                "Linked recycle must wait for explicit confirmation with safe Cancel focus");
            await fixture.Action("capture-library.delete.cancel");
            Check(calls==0&&fixture.Released==0,"Cancel recycled a file or retired its preview");
            await fixture.Action(recycle?"capture-library.recycle-original":"capture-library.delete");
            await fixture.Action("capture-library.delete.confirm");
            await fixture.Until(()=>!fixture.Busy&&fixture.Collection is not null&&fixture.Items.Count==2);
            Check(recycle?calls==1&&fixture.Unlinked==0: calls==0&&fixture.Unlinked==1,"Recycle and metadata removal used the same operation");
            Check(fixture.PermissionRequests.Any(ids=>ids.SequenceEqual(new[]{recycle?WidgetCaptureLibraryCapabilities.RecycleLinkedPermission:WidgetCaptureLibraryCapabilities.ManagePermission})),
                "Explicit action requested the wrong permission");
            if(recycle)Check(request!.Items.Single()==new CaptureLibraryMutationReference(original.Id,7,9),"Linked recycle did not retain exact displayed revisions");
            Check(fixture.Released==1&&fixture.Recycled==0,"Linked action failed to release its preview or used legacy owned recycling");
        }
    }

    internal static async Task LinkedRecycleFailureKeepsExactPreview()
    {
        foreach(var permission in new[]{WidgetPermissionOutcome.Denied,WidgetPermissionOutcome.Granted})
        {
            var attempts=0;
            await using var fixture=new GalleryFixture();
            fixture.RecycleLinkedOverride=(request,_)=>
                {attempts++;return ValueTask.FromResult(new CaptureLibraryBatchResult([new(request.Items[0].Id,ErrorCode:"capture_library_recycle_unavailable")]));};
            fixture.Items[0]=fixture.Items[0] with{SourceId=new string('b',32)};
            await fixture.Start();using var host=await OpenEditingSource(fixture);var original=Preview(fixture);
            fixture.PermissionOutcome=permission;
            await fixture.Action("capture-library.recycle-original");await fixture.Action("capture-library.delete.confirm");
            await fixture.Until(()=>!fixture.Busy);
            Check(fixture.Items.Count==3&&fixture.Unlinked==0&&fixture.Recycled==0,"Failed linked recycle removed metadata or another original");
            if(permission==WidgetPermissionOutcome.Denied)
                Check(attempts==0&&Preview(fixture)==original&&fixture.Released==0,"Denied permission disturbed the original preview");
            else Check(attempts==1&&Preview(fixture) is not null&&fixture.Released==1&&fixture.Opened==2,
                "Failed linked recycle did not recover the unchanged saved preview");
        }
    }
}
