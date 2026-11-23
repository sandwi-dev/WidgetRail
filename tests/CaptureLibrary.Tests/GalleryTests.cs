using System.Threading.Channels;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.FirstPartyWidgets.CaptureLibrary;

internal static partial class GalleryTests
{
    internal static async Task StreamlinedToolbarRetainsActions()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        await fixture.Until(()=>fixture.ReplayReads==1 && fixture.Node("capture-library.replay.start") is not null);
        Check(fixture.Node("capture-library.photo") is not null && fixture.Node("capture-library.video") is not null &&
            fixture.Node("capture-library.replay.start")?.ActionId=="capture-library.replay.start",
            "Screenshot, manual recording and replay must remain directly available in the header");
        CheckInlineBinding(fixture,"capture-library.photo",ControllerPrompt.Y,ControllerButton.Y,"capture-library.photo");
        CheckInlineBinding(fixture,"capture-library.video",ControllerPrompt.X,ControllerButton.X,"capture-library.video");
        var more = fixture.Node("capture-library.more");
        Check(more is { IsCommandMenu: true, ContextMenuButton: ControllerButton.Menu, Text: "Menu" } && more.SelectOptions.Count == 4 && more.SelectOptions.All(option => !option.IsSelected) &&
            more.SelectOptions.All(option => option.ActionId is not ("capture-library.save-recent" or "capture-library.replay.start")),
            "More must be an unselected native command dropdown");
        Check(fixture.Node("capture-library.filters") is null && fixture.Node("capture-library.selection.actions") is null,
            "Secondary controls clutter the initial gallery");
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count == 0,"Toolbar declaration invalid");
        var filterPreview = fixture.Node("capture-library.filters.preview");
        var filterContent = fixture.Node("capture-library.filters.toggle")?.FocusPresentation;
        Check(filterPreview?.FocusPreview is { EnterButton: ControllerButton.A, OnHover: true } && filterContent is not null,
            "Filters must use host-owned primary interactive preview entry");
        Check(filterContent!.Children.SelectMany(node => node.Children).Count(node => node.Kind == ViewNodeKind.Select) == 5 &&
            fixture.Node("capture-library.view.toggle")?.FocusPresentation?.Children.Count(node=>node.Kind==ViewNodeKind.Button)==4,
            "Filter and View panels lost filtering, sorting or thumbnail density choices");
        await fixture.Action("capture-library.selection.toggle");
        Check(!fixture.Nodes.Any(node=>node.ControllerPrompt is ControllerPrompt.Y or ControllerPrompt.X)&&
            !fixture.View().Root.Shortcuts.Any(shortcut=>shortcut.Button is ControllerButton.Y or ControllerButton.X),
            "Selection mode advertises inactive capture shortcuts");
        Check(fixture.Node("capture-library.selection.actions") is null,"Empty selection shows disabled batch commands");
        more = fixture.Node("capture-library.more");
        Check(more!.SelectOptions.Any(option => option.ActionId == "capture-library.settings") &&
            more.SelectOptions.Any(option => option.ActionId == "capture-library.sources.open") &&
            more.SelectOptions.Any(option => option.ActionId == "capture-library.selection.all"),"More lost management/selection commands");
        await fixture.Action("capture-library.selection.all");
        await fixture.Until(()=>fixture.Collection?.IndexedSelection?.Count == 3);
        Check(fixture.Node("capture-library.batch.copy") is not null,"Selection did not show contextual commands");
        Check(fixture.Node("capture-library.more")!.SelectOptions.Any(option => option.ActionId == "capture-library.batch.unfavorite" && !option.IsDisabled),
            "Remove favorite is unreachable");
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count == 0,"More menu declaration invalid");
    }

    internal static async Task FailedRecycleReleasesAndRecoversSamePreview()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget,"gallery-recycle-recovery",100); fixture.Host = host;
        using (var lease = await host.AcquireAsync("capture-library.entries",0,3))
            Check(lease.RouteAction(fixture.Items[0].Id,ControllerButton.A) is not null,"Preview action rejected");
        await fixture.Until(()=>fixture.Node("capture-library.preview") is not null && !fixture.Busy);
        fixture.FailRecycle = true;
        await fixture.Action("capture-library.delete");
        await fixture.Action("capture-library.delete.confirm");
        await fixture.Until(()=>fixture.RecycleAttempts == 1 && !fixture.Busy);
        Check(fixture.ReleasedBeforeRecycle && fixture.Opened == 2 && fixture.Items.Count == 3 && fixture.Node("capture-library.preview") is not null,
            "Failed recycle did not release the old lease then recover the unchanged preview");
        Check(fixture.Node("capture-library.confirm") is null,"Failed recycle trapped the confirmation");
    }

    internal static async Task LinkedRemovalKeepsProviderOriginals()
    {
        await using var fixture = new GalleryFixture();
        fixture.Items[0] = fixture.Items[0] with { SourceId = new string('b',32) };
        await fixture.Start();
        await fixture.Action("capture-library.selection.toggle");
        await fixture.Action("capture-library.selection.all");
        await fixture.Until(() => fixture.Collection?.IndexedSelection?.Count == 3);
        await fixture.Action("capture-library.batch.hide");
        await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.batch.confirmation") is not null);
        Check(fixture.Node("capture-library.batch.help")?.Text?.Contains("originals stay in place") == true &&
            fixture.Node("capture-library.batch.safety")?.Text?.Contains("2 WidgetRail originals cannot be hidden") == true,
            "Mixed-source hide must explain which captures can be hidden without deleting anything");
        await fixture.Action("capture-library.batch.confirm");
        await fixture.Until(() => !fixture.Busy && fixture.Items.Count == 2);
        Check(fixture.Unlinked == 1 && fixture.Recycled == 0 && fixture.HiddenItems.Count == 1,
            "Hide must never recycle an original, including in a mixed selection");
    }

    internal static async Task SourceSettingsConnectAndDisconnect()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        await fixture.Action("capture-library.settings");
        await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.sources.open") is not null);
        await fixture.Action("capture-library.sources.open");
        await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.sources.empty") is not null);
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count == 0, "Source modal invalid");
        await fixture.Action("capture-library.sources.add");
        await fixture.Until(() => !fixture.Busy && fixture.Sources.Count == 1);
        var id = fixture.Sources[0].Id;
        Check(fixture.Node("capture-library.sources." + id + ".path")?.Text == @"C:\Captures", "Chosen folder not shown");
        fixture.Sources[0] = fixture.Sources[0] with { LinkedCount = 8 }; fixture.Notify();
        await fixture.Until(() => fixture.Node("capture-library.sources."+id+".status")?.Text == "8 captures");
        await fixture.Action("capture-library.sources.remove." + id);
        Check(fixture.SourceRemovals == 0 && fixture.Node("capture-library.sources.confirm") is not null, "Disconnected before confirmation");
        await fixture.Action("capture-library.sources.cancel-remove");
        Check(fixture.SourceRemovals == 0 && fixture.Sources.Count == 1, "Cancel disconnected folder");
        await fixture.Action("capture-library.sources.remove." + id);
        await fixture.Action("capture-library.sources.confirm-remove");
        await fixture.Until(() => !fixture.Busy && fixture.Sources.Count == 0);
        Check(fixture.SourceRemovals == 1 && fixture.Recycled == 0 && fixture.Items.Count == 3, "Source disconnect touched original media");
        await fixture.Action("capture-library.sources.close");
        Check(fixture.Node("capture-library-sources") is null && fixture.Node("capture-library.settings.page") is not null,
            "Closing sources lost the settings route");
    }

    internal static async Task RelatedCapturesIntentAndContextLinks()
    {
        await using var fixture = new GalleryFixture();
        fixture.Items[0] = fixture.Items[0] with { ContextCount = 1 };
        await fixture.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-context", 100); fixture.Host = host;
        using (var lease = await host.AcquireAsync("capture-library.entries", 0, 3))
            Check(lease.RouteAction(fixture.Items[0].Id, ControllerButton.A) is not null, "Preview action rejected");
        await fixture.Until(() => fixture.Node("capture-library.preview") is not null && !fixture.Busy);
        await fixture.Action("capture-library.context.open");
        await fixture.Until(() => fixture.Nodes.Any(node => node.Intent?.ContractId == WidgetIntentContracts.OpenRelatedItem));
        var related = fixture.Nodes.Single(node => node.Intent?.ContractId == WidgetIntentContracts.OpenRelatedItem).Intent!;
        Check(related.Payload.GetProperty("packageId").GetString() == "widgetrail.tests.provider" &&
            related.Payload.GetProperty("kind").GetString() == "example.item" && related.Payload.GetProperty("id").GetString() == "opaque-id",
            "Related action did not preserve authenticated origin and opaque identity");
        Check(fixture.Nodes.Single(node => node.Text == "Metadata-only session").Intent is null,
            "Display-only metadata incorrectly offered a widget action");
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count == 0, "Context modal has invalid focus or declarations");
        await fixture.Action("capture-library.context.close");
        var request = WidgetIntentRequest.Create(WidgetIntentContracts.CaptureLibrary,
            JsonSerializer.SerializeToElement(new { originPackageId = "widgetrail.tests.provider", referenceKind = "example.item", referenceId = "opaque-id" }));
        Check(await fixture.Widget.OnIntentAsync(request) == WidgetIntentResult.Accepted, "Browse intent rejected");
        await fixture.Until(() => !fixture.Busy && fixture.Collection is not null && fixture.LastQuery?.Reference?.Id == "opaque-id");
        Check(fixture.LastQuery!.OriginPackageId == "widgetrail.tests.provider" && fixture.Node("capture-library.context.modal") is null &&
            fixture.Node("capture-library.preview") is null, "Browse failed to leave the previous preview and scope the query");
        Check(fixture.Node("capture-library.related-filter.label")?.Text == "Related captures" &&
            fixture.Node("capture-library.related-filter.clear") is not null && fixture.Node("capture-library.filters") is null,
            "Incoming related filter must be visible and removable even with collapsed filters and no related rows");
        await fixture.Action("capture-library.filter.screenshots");
        await fixture.Until(() => !fixture.Busy && fixture.LastQuery?.MediaKind == CaptureLibraryMediaKind.Screenshot);
        await fixture.Action("capture-library.related-filter.clear");
        await fixture.Until(() => !fixture.Busy && fixture.LastQuery?.Reference is null);
        Check(fixture.LastQuery!.OriginPackageId is null && fixture.LastQuery.MediaKind == CaptureLibraryMediaKind.Screenshot &&
            fixture.Node("capture-library.related-filter") is null && fixture.Node("capture-library.active.type") is not null,
            "Removing related filter must preserve and display explicit media filters");
        var invalid = WidgetIntentRequest.Create(WidgetIntentContracts.CaptureLibrary,
            JsonSerializer.SerializeToElement(new { originPackageId = "widgetrail.tests.provider", referenceKind = "bad kind", referenceId = "opaque-id" }));
        Check(await fixture.Widget.OnIntentAsync(invalid) == WidgetIntentResult.Rejected, "Malformed domain reference accepted");
    }

    internal static async Task ClosedContextDoesNotReopenOnLateResult()
    {
        await using var fixture = new GalleryFixture(); fixture.DelayContexts = true;
        fixture.Items[0] = fixture.Items[0] with { ContextCount = 1 }; await fixture.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-late-context", 100); fixture.Host = host;
        using (var lease = await host.AcquireAsync("capture-library.entries", 0, 3))
            Check(lease.RouteAction(fixture.Items[0].Id, ControllerButton.A) is not null, "Preview action rejected");
        await fixture.Until(() => fixture.Node("capture-library.preview") is not null && !fixture.Busy);
        await fixture.Action("capture-library.context.open");
        await fixture.ContextEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Action("capture-library.context.close");
        fixture.ContextRelease.TrySetResult();
        await Task.Delay(100);
        Check(fixture.Node("capture-library.context.modal") is null && fixture.Nodes.All(node => node.Intent?.ContractId != WidgetIntentContracts.OpenRelatedItem),
            "Closed context republished stale links");
    }

    internal static async Task RelatedDestinationUndoAndFocus()
    {
        var game = new CaptureLibraryDestination("widgetrail.tests.provider", "game", "silksong", "Silksong", true);
        var changes = new List<CaptureDestinationChange>();
        var linked = true;
        await using var fixture = new GalleryFixture((builder, state) => builder
            .WithHandler(WidgetCaptureLibraryCapabilities.ChangeDestination, (request, _) =>
            {
                changes.Add(request);
                if (changes.Count == 1) return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(false));
                linked = request.Undo;
                state.Items[0] = state.Items[0] with { ContextCount = linked ? 1 : 0 };
                return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
            }));
        fixture.Items[0] = fixture.Items[0] with { ContextCount = 1 };
        fixture.DestinationPage = () => new(linked ? [game] : [], 0, linked ? 1 : 0, linked ? "a" : "b");
        await fixture.Start();
        using var host = await OpenEditingSource(fixture);
        await fixture.Action("capture-library.context.open");
        await fixture.Until(() => fixture.Nodes.Any(node => node.Text == "Unlink"));
        var unlinkButton = fixture.Nodes.Single(node => node.Text == "Unlink");
        Check(unlinkButton.AccessibilityLabel == "Unlink from Silksong", "Unlink did not identify its destination to assistive input");
        var unlink = unlinkButton.ActionId!;
        var gameId = fixture.Nodes.Single(node => node.Intent?.ContractId == WidgetIntentContracts.OpenRelatedItem).Id;
        await fixture.Action(unlink);
        await fixture.Until(() => changes.Count == 1 && !fixture.Busy);
        Check(fixture.Node(gameId) is not null && fixture.Node("capture-library.context.undo") is null,
            "Cancelling Unlink removed the link or exposed an Undo");
        await fixture.Action(unlink);
        await fixture.Until(() => fixture.Node("capture-library.context.undo") is not null && !fixture.Busy);
        var removed = fixture.View();
        var undoId = "capture-library.context.undo";
        Check(changes.Count == 2 && changes[1].CaptureId == fixture.Items[0].Id && changes[1].DestinationId == game.Id &&
            changes[1].ContentRevision == fixture.Items[0].Revision && changes[1].ExpectedRevision == "a" &&
            fixture.Node("capture-library.context.empty") is not null && fixture.Node(gameId) is null &&
            removed.FocusGroupEntryRequest?.TargetId == undoId && ViewSnapshotValidator.Validate(removed).Count == 0,
            "Removing the last link did not leave a valid dialog focused on Undo");
        await fixture.Action("capture-library.context.close");
        Check(fixture.Node("capture-library.context.open") is { IsDisabled: not true }, "Closing the dialog made Undo unreachable");
        await fixture.Action("capture-library.context.open");
        await fixture.Until(() => fixture.Node("capture-library.context.empty") is not null);
        await fixture.Action(fixture.Node(undoId)!.ActionId!);
        await fixture.Until(() => fixture.Node(gameId) is not null && !fixture.Busy);
        var restored = fixture.View();
        Check(changes.Count == 3 && changes[2] == (changes[1] with { Undo = true }) && fixture.Node(undoId) is null &&
            restored.FocusGroupEntryRequest?.TargetId == gameId && ViewSnapshotValidator.Validate(restored).Count == 0,
            "Undo did not restore the original destination and request focus on its button");
    }

    internal static async Task CopyCanBeCancelledFromGallery()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        await fixture.Action("capture-library.selection.toggle");
        await fixture.Action("capture-library.selection.all");
        await fixture.Until(() => fixture.Collection?.IndexedSelection?.Count == 3);
        await fixture.Action("capture-library.batch.copy");
        await fixture.CopyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(fixture.Node("capture-library.copy.cancel") is { IsBusy: not true }, "Copy has no reachable cancel control");
        await fixture.Action("capture-library.batch.stop");
        await fixture.CopyCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.copy.cancel") is null);
        Check(fixture.LastFeedback?.Contains("Completed copies were kept", StringComparison.Ordinal) == true,
            "Cancellation incorrectly promises completed copies were removed");
    }

    internal static async Task SearchAndLiveUpdates()
    {
        await using var fixture = new GalleryFixture();
        await fixture.Start();
        await fixture.Action("capture-library.search", "Boss");
        await fixture.Until(()=>fixture.Collection?.IndexedCollection?.Count==1);
        await fixture.Action("capture-library.clear");
        await fixture.Until(()=>fixture.Collection?.IndexedCollection?.Count==3);
        fixture.Items.Add(fixture.Items[0] with{Id=new string('f',32),Title="Newest capture"});
        fixture.Notify();
        await fixture.Until(()=>fixture.Collection?.IndexedCollection?.Count==4);
        await fixture.Action("capture-library.search", "slow");
        await fixture.SlowEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Action("capture-library.search", "Boss");
        fixture.SlowRelease.TrySetResult();
        await fixture.Until(()=>fixture.Collection?.IndexedCollection?.Count==1);
        await Task.Delay(100);
        Check(fixture.Collection?.IndexedCollection?.Count==1&&fixture.Node("capture-library.search")?.TextEntryValue=="Boss","Late query replaced current search");
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count==0,"Filtered gallery declaration invalid");
    }

    internal static async Task SelectionBatches()
    {
        await using var fixture=new GalleryFixture();await fixture.Start();
        await fixture.Action("capture-library.selection.toggle");
        using var host=WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget,"gallery-selection",100);
        fixture.Host=host;
        using(var lease=await host.AcquireAsync("capture-library.entries",0,3))
        {
            var epoch=fixture.Collection!.IndexedSelection!.Epoch;
            Check(lease.RouteSelection(fixture.Items[0].Id,new(epoch,IndexedSelectionGesture.Toggle)) is not null,"Selection input rejected");
            await fixture.Until(()=>fixture.Collection?.IndexedSelection?.Count==1);
            host.PublishSnapshot();
            Check(lease.RouteSelection(fixture.Items[2].Id,new(epoch,IndexedSelectionGesture.Extend)) is not null,"Range input rejected");
            await fixture.Until(()=>fixture.Collection?.IndexedSelection?.Count==3);
        }
        await fixture.Action("capture-library.batch.favorite");
        await fixture.Until(()=>fixture.Batches==1&&!fixture.Busy);
        Check(fixture.Items.All(item=>item.IsFavorite),"Batch did not include selected offscreen occurrences");
        await fixture.Action("capture-library.selection.all");
        await fixture.Action("capture-library.batch.recycle");
        await fixture.Until(()=>fixture.Node("capture-library.batch.confirmation") is not null&&!fixture.Busy);
        Check(fixture.Recycled==0,"Recycled before confirmation");
        await fixture.Action("capture-library.batch.cancel");
        Check(fixture.Recycled==0&&fixture.Items.Count==3,"Cancel changed files");
        await fixture.Action("capture-library.batch.recycle");
        await fixture.Until(()=>fixture.Node("capture-library.batch.confirmation") is not null&&!fixture.Busy);
        await fixture.Action("capture-library.batch.confirm");
        await fixture.Until(()=>fixture.Recycled==3&&!fixture.Busy);
        Check(fixture.Items.Count==0&&fixture.Node("capture-library.empty") is not null,"Recycle did not refresh gallery");
    }

    internal static async Task HeldSelectionReconcilesPresentation()
    {
        await using var fixture=new GalleryFixture();await fixture.Start();
        using var host=WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget,"gallery-held-selection",100);
        fixture.Host=host;
        var before=fixture.Collection!.IndexedCollection!;
        using(var first=await host.AcquireAsync("capture-library.entries",0,1))
        {
            Check(first.Range.Items[0].Root.ContextActions.Any(action=>action.ActionId=="capture-library.item.favorite"&&!action.IsDisabled),
                "Normal gallery favorite action is available before framework selection");
            var epoch=fixture.Collection!.IndexedSelection!.Epoch;
            Check(first.RouteSelection(fixture.Items[0].Id,new(epoch,IndexedSelectionGesture.BeginAdd)) is not null,
                "Framework start-and-add was rejected");
            await fixture.Until(()=>fixture.Collection?.IndexedSelection is {IsActive:true,Count:1});
        }
        var selected=fixture.Collection!;
        Check(selected.IndexedCollection!.QueryGeneration==before.QueryGeneration && selected.IndexedCollection.ContentRevision>before.ContentRevision,
            "Host-started selection updates gallery presentation without replacing its query");
        using(var selectedRows=await host.AcquireAsync("capture-library.entries",0,3))
        {
            Check(selectedRows.Range.Items.All(item=>item.Root.FocusPresentation is null &&
                item.Root.ContextActions.Any(action=>action.ActionId=="capture-library.item.favorite"&&action.IsDisabled)),
                "Framework selection synchronizes gallery previews and per-item action policy");
            var epoch=selected.IndexedSelection!.Epoch;
            Check(selectedRows.RouteSelection(fixture.Items[2].Id,new(epoch,IndexedSelectionGesture.Add)) is not null,
                "Held additive selection was rejected after the presentation update");
            await fixture.Until(()=>fixture.Collection?.IndexedSelection?.Indices.SequenceEqual([0,2])==true);
        }
        Check(fixture.Node("capture-library.batch.favorite") is not null,"Host-selected rows expose the normal gallery batch commands");
        await fixture.Action("capture-library.selection.toggle");
        host.PublishSnapshot();
        using var restored=await host.AcquireAsync("capture-library.entries",0,1);
        Check(fixture.Collection?.IndexedSelection is {IsActive:false,Count:0} &&
            restored.Range.Items[0].Root.ContextActions.Any(action=>action.ActionId=="capture-library.item.favorite"&&!action.IsDisabled),
            "Ending framework selection restores normal gallery policy");
    }

    internal static async Task CancelledBatchReplyDoesNotClearNewConfirmation()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        await fixture.Action("capture-library.selection.toggle");
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-late-batch", 100); fixture.Host = host;
        using (var lease = await host.AcquireAsync("capture-library.entries", 0, 3))
        {
            var epoch = fixture.Collection!.IndexedSelection!.Epoch;
            Check(lease.RouteSelection(fixture.Items[0].Id, new(epoch, IndexedSelectionGesture.Toggle)) is not null, "Selection input rejected");
            await fixture.Until(() => fixture.Collection?.IndexedSelection?.Count == 1);
        }
        fixture.DelayRecycle = true;
        await fixture.Action("capture-library.batch.recycle");
        await fixture.Until(() => fixture.Node("capture-library.batch.confirmation") is not null && !fixture.Busy);
        await fixture.Action("capture-library.batch.confirm");
        await fixture.RecycleEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Action("capture-library.batch.stop");
        await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.batch.confirmation") is null);
        await fixture.Action("capture-library.selection.all");
        await fixture.Action("capture-library.batch.recycle");
        // The replacement preparation shares the serialized work lane. Release
        // the canceled reply first, then require the new confirmation to survive.
        fixture.DelayRecycle = false; fixture.RecycleRelease.TrySetResult();
        await fixture.Until(() => fixture.Node("capture-library.batch.confirmation") is not null && !fixture.Busy);
        await Task.Delay(100);
        Check(fixture.Node("capture-library.batch.confirmation") is not null,
            "A canceled older batch reply cleared the newer confirmation");
        await fixture.Action("capture-library.batch.cancel");
        Check(fixture.Items.Count == 3 && fixture.Recycled == 0, "Late failed recycle and canceled new confirmation must not remove captures");
    }

    internal static async Task InitialSnapshotCannotBeatPostSubscriptionRefresh()
    {
        var secondQueryReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queryCount = 0;
        var oldEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var media = new CaptureAttachment(new string('a',32), "image/png", 100, 100, 12, 0, long.MaxValue);
        var version = 1;
        var underlying = new WidgetTestHostServicesBuilder().WithPrivateState(new()).WithCaptureContinuity()
            .WithEvents(WidgetCaptureLibraryCapabilities.Changed, Array.Empty<CaptureLibraryChanged>())
            .WithResponse(WidgetCaptureLibraryCapabilities.Applications, new CaptureLibraryApplications([], false))
            .WithHandler(WidgetCaptureLibraryCapabilities.Folders,(request,_)=>ValueTask.FromResult(new CaptureLibraryFolders([new("", "WidgetRail captures", 0)],request.Revision ?? "a")))
            .WithHandler(WidgetCaptureLibraryCapabilities.Query, async (request, _) =>
            {
                if (Interlocked.Increment(ref queryCount) > 1) await secondQueryReady.Task;
                var count = version == 1 ? 3 : 4;
                var items = Enumerable.Range(1, count).Select(index => new CaptureLibraryItem(index.ToString("x32"), media, 1)).ToArray();
                return new CaptureLibraryQueryPage(items, 0, count, count * 12, version.ToString("x"));
            })
            .WithHandler(WidgetCaptureLibraryCapabilities.Groups, async (request, _) =>
            {
                // The old provider result was computed before the new data, but
                // its delivery ignores cancellation and arrives later.
                if (request.Revision == "1") { oldEntered.TrySetResult(); await oldRelease.Task; return new([new("unknown", 3)], 3, "1"); }
                newEntered.TrySetResult(); await newRelease.Task; return new([new("unknown", 4)], 4, "2");
            }).Build();
        var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), underlying);
        try
        {
            await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await oldEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            version = 2; secondQueryReady.TrySetResult();
            // The SDK latest-wins lane starts the replacement after the old read
            // retires; the former two independent writers could overlap here.
            await Task.Delay(75); oldRelease.TrySetResult();
            await newEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            newRelease.TrySetResult();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Find(widget.RenderSnapshot("initial-race", 1).Root)?.IndexedCollection?.Count != 4) await Task.Delay(10, timeout.Token);
            Check(Find(widget.RenderSnapshot("initial-race", 2).Root)?.IndexedCollection?.Count == 4,
                "Old initial snapshot displaced the newer post-subscription result");
        }
        finally { secondQueryReady.TrySetResult(); oldRelease.TrySetResult(); newRelease.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }
        static ViewNode? Find(ViewNode node) => node.Id == "capture-library.entries" ? node : node.Children.Select(Find).FirstOrDefault(value => value is not null);
    }

    internal static async Task MetadataEditDoesNotAuthorizeRecyclingChangedPreviewContent()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-preview-revision", 100); fixture.Host = host;
        using (var lease = await host.AcquireAsync("capture-library.entries", 0, 3))
            Check(lease.RouteAction(fixture.Items[0].Id, ControllerButton.A) is not null, "Preview action rejected");
        await fixture.Until(() => fixture.Node("capture-library.preview") is not null && !fixture.Busy);
        fixture.Items[0] = fixture.Items[0] with { Revision = 2 }; fixture.Notify();
        await fixture.Until(() => fixture.LastFeedback?.Contains("changed outside") == true);
        await fixture.Action("capture-library.favorite"); await fixture.Until(() => !fixture.Busy && fixture.Items[0].IsFavorite);
        await fixture.Action("capture-library.delete"); await fixture.Action("capture-library.delete.confirm");
        await fixture.Until(() => fixture.LastRecycleReferences.Count == 1 && !fixture.Busy);
        Check(fixture.LastRecycleReferences[0].ContentRevision == 0 && fixture.LastRecycleReferences[0].MetadataRevision == 1,
            "Editing metadata silently authorized recycling an unseen changed original");
        Check(fixture.Recycled == 0 && fixture.Items.Count == 3, "Changed original must survive stale preview recycle");
    }

    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private sealed class GalleryFixture:IAsyncDisposable
    {
        internal readonly List<CaptureLibraryItem> Items=Enumerable.Range(1,3).Select(i=>new CaptureLibraryItem(i.ToString("x32"),
            new(new string('a',32),"image/png",100,100,12,0,long.MaxValue),i){Title=i==1?"Boss fight":"Capture "+i}).ToList();
        private readonly Channel<CaptureLibraryChanged> changes=Channel.CreateUnbounded<CaptureLibraryChanged>();
        private readonly Channel<CaptureReplayStatus> replayChanges=Channel.CreateUnbounded<CaptureReplayStatus>();
        internal CaptureReplayStatus Replay=new(0,0,new(),CaptureReplayPhase.Off);
        internal readonly List<CaptureLibraryItem> HiddenItems = [];
        internal readonly System.Collections.Concurrent.ConcurrentQueue<WidgetNotification> Notifications = new();
        internal string? LastFeedback => Notifications.LastOrDefault()?.Body;
        internal int ReplayStarts,ReplayStops,ReplaySaves,ReplayReads,ReplayConfigurations,ReplaySubscriptions;
        internal CaptureReplayOutcome ReplayStartOutcome=CaptureReplayOutcome.Accepted;
        internal WidgetPermissionOutcome PermissionOutcome=WidgetPermissionOutcome.Granted;
        internal string? DeniedPermission;
        internal readonly List<string[]> PermissionRequests=[];
        internal TaskCompletionSource? ReplaySaveGate;
        internal void NotifyReplay(CaptureReplayStatus status){Replay=status;replayChanges.Writer.TryWrite(status);}
        private long revision=1;
        internal readonly TaskCompletionSource SlowEntered=new(TaskCreationOptions.RunContinuationsAsynchronously),SlowRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly CaptureLibraryWidget Widget;
        internal WidgetIndexedCollectionTestHost? Host;
        internal int Batches,Recycled,Unlinked,SourceRemovals,Opened,Released,RecycleAttempts;
        internal int? LoseRecycleReplyOnAttempt;
        internal bool FailRecycle,ReleasedBeforeRecycle;
        internal Func<CaptureLibraryOpenRequest,CancellationToken,ValueTask<CaptureAttachment>>? OpenOverride;
        internal Func<CaptureLibraryRecycleRequest,CancellationToken,ValueTask<CaptureLibraryBatchResult>>? RecycleLinkedOverride;
        internal Func<CaptureRecordingQuery,CancellationToken,ValueTask<CaptureCapabilities>>? EncodingOverride;
        internal readonly List<CaptureRecordingQuery> EncodingQueries=[];
        internal Func<CaptureLibraryGroupsRequest,CancellationToken,ValueTask<CaptureLibraryGroups>>? GroupsOverride;
        internal Func<CaptureLibraryReference,CancellationToken,ValueTask<CaptureLibraryItem>>? MetadataOverride;
        internal Func<CaptureAttachmentRequest,CancellationToken,ValueTask<WidgetCapabilityAcknowledgement>>? ReleaseOverride;
        internal Action<CaptureAttachmentRequest>? OnReleased;
        internal readonly List<CaptureLibrarySource> Sources = [];
        internal CaptureLibraryQuery? LastQuery;
        internal CaptureLibraryChooseSourceRequest? LastSourceRequest;
        internal readonly WidgetTestPrivateState PrivateState;
        internal bool DelayContexts;
        internal Func<CaptureLibraryDestinationsPage>? DestinationPage;
        internal readonly TaskCompletionSource ContextEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ContextRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IReadOnlyList<CaptureLibraryMutationReference> LastRecycleReferences = [];
        internal bool DelayRecycle;
        internal readonly TaskCompletionSource CopyEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), CopyCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RecycleEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), RecycleRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal GalleryFixture(Action<WidgetTestHostServicesBuilder, GalleryFixture>? configure = null, WidgetTestPrivateState? privateState = null)
        {
            PrivateState = privateState ?? new();
            var client=new WidgetTestHostServicesBuilder().WithPrivateState(PrivateState)
                .WithHandler(WidgetNotificationCapabilities.Publish,(request,_)=>
                {if(request.Notification is {} notification)Notifications.Enqueue(notification);return ValueTask.FromResult(new WidgetNotificationReceipt(true));})
                .WithEventStream(WidgetCaptureReplayCapabilities.EncodingChanged,token=>{ReplaySubscriptions++;return replayChanges.Reader.ReadAllAsync(token);})
                .WithHandler(WidgetCaptureReplayCapabilities.DetailedGet,(_,_)=>{ReplayReads++;Check(ReplaySubscriptions>0,"Replay read preceded subscription");return ValueTask.FromResult(Replay);})
                .WithHandler(WidgetCaptureReplayCapabilities.DetailedStart,(_,_)=>
                {ReplayStarts++;if(ReplayStartOutcome!=CaptureReplayOutcome.Accepted)return ValueTask.FromResult(new CaptureReplayControlResult(ReplayStartOutcome,Replay));Replay=Replay with{Revision=Replay.Revision+1,Settings=Replay.Settings with{Enabled=true},Phase=CaptureReplayPhase.Recording,SessionId=new string('a',32),TargetName="Game"};return ValueTask.FromResult(new CaptureReplayControlResult(CaptureReplayOutcome.Accepted,Replay));})
                .WithHandler(WidgetCaptureReplayCapabilities.Stop,(request,_)=>
                {Check(request.ExpectedSessionId==Replay.SessionId,"Stale Stop reached service");ReplayStops++;Replay=Replay with{Revision=Replay.Revision+1,Settings=Replay.Settings with{Enabled=false},SessionId=null,Phase=CaptureReplayPhase.Off};return ValueTask.FromResult(new CaptureReplayControlResult(CaptureReplayOutcome.Accepted,Replay));})
                .WithHandler(WidgetCaptureReplayCapabilities.Configure,(request,_)=>
                {Check(request.ExpectedRevision==Replay.SettingsRevision,"Stale settings reached service");ReplayConfigurations++;Replay=Replay with{Revision=Replay.Revision+1,SettingsRevision=Replay.SettingsRevision+1,Settings=request.Settings};return ValueTask.FromResult(new CaptureReplayControlResult(CaptureReplayOutcome.Accepted,Replay));})
                .WithHandler(WidgetCaptureReplayCapabilities.Save,async(request,token)=>
                {ReplaySaves++;if(ReplaySaveGate is {} gate)await gate.Task.WaitAsync(token);var saved=Items[0] with{Media=Items[0].Media with{ContentType="video/mp4",DurationSeconds=5,SourceApplication=new("Recorded game","Window title")}};return new(CaptureReplayOutcome.Accepted,request.RequestId,saved,5);})
                .WithEventStream(WidgetCaptureLibraryCapabilities.Changed,token=>changes.Reader.ReadAllAsync(token))
                .WithHandler(WidgetPermissionCapabilities.Request,(request,_)=>
                {PermissionRequests.Add(request.CapabilityIds.ToArray());return ValueTask.FromResult(new WidgetPermissionRequestState(
                    DeniedPermission is { } denied && request.CapabilityIds.Contains(denied) ? WidgetPermissionOutcome.Denied : PermissionOutcome));})
                .WithResponse(WidgetCaptureCapabilities.Capabilities, new CaptureCapabilities(true,[CaptureImageFormat.Png],
                    [new("1080p30-hardware",1920,1080,30,3_000_000,12_000_000,CaptureEncoderMode.Hardware)],[CaptureAudioMode.None],true,120))
                .WithHandler(WidgetCaptureCapabilities.RecordingCapabilities,async(request,token)=>
                {EncodingQueries.Add(request);return EncodingOverride is {} custom?await custom(request,token):RecordingCapabilities(request);})
                .WithHandler(WidgetCaptureLibrarySourceCapabilities.List,(_,_) => ValueTask.FromResult(new CaptureLibrarySources(Sources.ToArray())))
                .WithHandler(WidgetCaptureLibrarySourceCapabilities.Choose,(request,_) =>
                {
                    LastSourceRequest = request;
                    var source = new CaptureLibrarySource(new string('b',32),"Capture folder",true,0,0,@"C:\Captures");
                    Sources.Add(source); return ValueTask.FromResult(new CaptureLibrarySourceChoice(source));
                })
                .WithHandler(WidgetCaptureLibrarySourceCapabilities.Remove,(request,_) =>
                { SourceRemovals++; Sources.RemoveAll(source => source.Id == request.SourceId); return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); })
                .WithHandler(WidgetCaptureLibrarySourceCapabilities.RemoveLink,(request,_) =>
                {
                    var item = Items.Single(item => item.Id == request.Id);
                    if (item.SourceId is null) throw new Exception("Unlink received an owned original");
                    if(item.Revision!=request.ContentRevision||item.MetadataRevision!=request.MetadataRevision)
                        throw new WidgetCapabilityException("capture_library_metadata_conflict","Changed");
                    Items.Remove(item); HiddenItems.Add(item with{MetadataRevision=item.MetadataRevision+1}); Unlinked++; revision++;
                    return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
                })
                .WithHandler(WidgetCaptureLibrarySourceCapabilities.Unhide,(request,_)=>
                {
                    var item=HiddenItems.Single(value=>value.Id==request.Id);
                    if(item.Revision!=request.ContentRevision||item.MetadataRevision!=request.MetadataRevision)
                        throw new WidgetCapabilityException("capture_library_metadata_conflict","Changed");
                    HiddenItems.Remove(item);var restored=item with{MetadataRevision=item.MetadataRevision+1};Items.Add(restored);revision++;
                    return ValueTask.FromResult(restored);
                })
                .WithResponse(WidgetCaptureLibraryCapabilities.Applications,new CaptureLibraryApplications([],false))
                .WithHandler(WidgetCaptureLibraryCapabilities.Folders,(request,_)=>ValueTask.FromResult(new CaptureLibraryFolders(
                    new[] { new CaptureLibraryFolder("", "WidgetRail captures", Items.Count(item => item.SourceId is null)) }
                        .Concat(Sources.Select(source => new CaptureLibraryFolder(source.Id, source.Label, Items.Count(item => item.SourceId == source.Id)))).ToArray(),request.Revision ?? "a")))
                .WithHandler(WidgetCaptureLibraryCapabilities.Groups,(request,token)=>{if(GroupsOverride is not null)return GroupsOverride(request,token);var count=Read(request.Query).Length;return ValueTask.FromResult(new CaptureLibraryGroups(count==0?[]:[new("unknown",count)],count,revision.ToString("x")));})
                .WithHandler(WidgetCaptureLibraryCapabilities.Query,async(request,token)=>
                {
                    LastQuery = request.Query;
                    var value=Read(request.Query);
                    if(request.Query.Search=="slow"){SlowEntered.TrySetResult();await SlowRelease.Task;}
                    return new CaptureLibraryQueryPage(value.Skip(request.Offset).Take(request.Count).ToArray(),request.Offset,value.Length,value.Length*12,revision.ToString("x"));
                })
                .WithHandler(WidgetCaptureLibraryCapabilities.Find,(request,_)=>{
                    var value=Read(request.Query);var index=Array.FindIndex(value,item=>item.Id==request.Id);
                    return ValueTask.FromResult(new CaptureLibraryPosition(index<0?null:index,value.Length,revision.ToString("x")));})
                .WithHandler(WidgetCaptureLibraryCapabilities.GetMetadata, (request, token) => MetadataOverride?.Invoke(request, token) ?? ValueTask.FromResult(Items.Concat(HiddenItems).Single(item => item.Id == request.Id)))
                .WithHandler(WidgetCaptureLibraryCapabilities.Open, (request, token) => { Opened++; return OpenOverride?.Invoke(request, token) ?? ValueTask.FromResult(Items.Concat(HiddenItems).Single(item => item.Id == request.Id).Media with { Id = Guid.NewGuid().ToString("N"), Origin = CaptureAttachmentOrigin.SavedLibrary }); })
                .WithHandler(WidgetCaptureLibraryCapabilities.Release, (request, token) => { Released++; OnReleased?.Invoke(request); return ReleaseOverride?.Invoke(request, token) ?? ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); })
                .WithHandler(WidgetCaptureLibraryCapabilities.GetDestinations, async (request, token) =>
                {
                    ContextEntered.TrySetResult(); if (DelayContexts) await ContextRelease.Task;
                    if (DestinationPage is not null) return DestinationPage();
                    return new CaptureLibraryDestinationsPage([
                        new("widgetrail.tests.provider", "example.item", "opaque-id", "Related item", true),
                        new("widgetrail.tests.provider", "example.session", "session-id", "Metadata-only session", false)], 0, 2, "a");
                })
                .WithHandler(WidgetCaptureLibraryCapabilities.SaveCopy, async (request, token) =>
                {
                    CopyEntered.TrySetResult();
                    try { await Task.Delay(Timeout.Infinite, token); }
                    catch (OperationCanceledException) { CopyCancelled.TrySetResult(); throw; }
                    throw new InvalidOperationException("Copy cancellation fixture unexpectedly resumed");
                })
                .WithHandler(WidgetCaptureLibraryCapabilities.UpdateMetadata, (request, _) =>
                {
                    var values=Items.Any(item=>item.Id==request.Id)?Items:HiddenItems;
                    var index = values.FindIndex(item => item.Id == request.Id);
                    if (index < 0 || values[index].MetadataRevision != request.ExpectedRevision) throw new WidgetCapabilityException("capture_library_metadata_conflict", "Changed");
                    values[index] = values[index] with { Title = request.Title, IsFavorite = request.IsFavorite, MetadataRevision = values[index].MetadataRevision + 1 };
                    revision++; return ValueTask.FromResult(values[index]);
                })
                .WithHandler(WidgetCaptureLibraryCapabilities.UpdateMetadataBatch,(request,_)=>{
                    Batches++;var result=new List<CaptureLibraryMutationResult>();
                    foreach(var update in request.Items){var index=Items.FindIndex(item=>item.Id==update.Id);Items[index]=Items[index] with{Title=update.Title,IsFavorite=update.IsFavorite,MetadataRevision=Items[index].MetadataRevision+1};result.Add(new(update.Id,Items[index]));}
                    revision++;return ValueTask.FromResult(new CaptureLibraryBatchResult(result));})
                .WithHandler(WidgetCaptureLibraryCapabilities.Recycle,async(request,token)=>{
                    RecycleAttempts++; ReleasedBeforeRecycle = Released > 0;
                    if (FailRecycle) return new CaptureLibraryBatchResult(request.Items.Select(item=>new CaptureLibraryMutationResult(item.Id,ErrorCode:"capture_library_recycle_unavailable")).ToArray());
                    if (DelayRecycle)
                    {
                        RecycleEntered.TrySetResult(); await RecycleRelease.Task;
                        if (token.IsCancellationRequested)
                            return new CaptureLibraryBatchResult(request.Items.Select(item => new CaptureLibraryMutationResult(item.Id, ErrorCode: "capture_library_recycle_unavailable")).ToArray());
                    }
                    LastRecycleReferences = request.Items.ToArray();
                    var results = new List<CaptureLibraryMutationResult>();
                    foreach (var item in request.Items)
                    {
                        var existing = Items.FirstOrDefault(value => value.Id == item.Id);
                        if (existing is null || existing.Revision != item.ContentRevision || existing.MetadataRevision != item.MetadataRevision)
                            results.Add(new(item.Id, ErrorCode: "capture_library_metadata_conflict"));
                        else { Items.RemoveAll(value => value.Id == item.Id); Recycled++; results.Add(new(item.Id)); }
                    }
                    revision++;
                    if (LoseRecycleReplyOnAttempt == RecycleAttempts) throw new TimeoutException("Lost reply after commit");
                    return new CaptureLibraryBatchResult(results);})
                .WithHandler(WidgetCaptureLibraryCapabilities.RecycleLinked, (request, token) =>
                {
                    if (RecycleLinkedOverride is { } custom) return custom(request, token);
                    var results = new List<CaptureLibraryMutationResult>();
                    foreach (var reference in request.Items)
                    {
                        var item = Items.Concat(HiddenItems).FirstOrDefault(item => item.Id == reference.Id);
                        if (item?.SourceId is null || item.Revision != reference.ContentRevision || item.MetadataRevision != reference.MetadataRevision)
                            results.Add(new(reference.Id, ErrorCode: "capture_library_metadata_conflict"));
                        else if (FailRecycle)
                            results.Add(new(reference.Id, ErrorCode: "capture_library_recycle_unavailable"));
                        else
                        {
                            Items.Remove(item); HiddenItems.Remove(item); Recycled++;
                            results.Add(new(reference.Id));
                        }
                    }
                    revision++;
                    return ValueTask.FromResult(new CaptureLibraryBatchResult(results));
                });
            client.WithCaptureContinuity();
            configure?.Invoke(client, this);
            Widget=WidgetTestHost.Attach(new CaptureLibraryWidget(),client.Build());
        }
        private CaptureLibraryItem[] Read(CaptureLibraryQuery query)=>(query.HiddenOnly?HiddenItems:Items).Where(item=>(!query.FavoritesOnly||item.IsFavorite)&&
            (query.SourceId is null || (item.SourceId ?? "") == query.SourceId) &&
            (string.IsNullOrWhiteSpace(query.Search)||item.Title!.Contains(query.Search,StringComparison.OrdinalIgnoreCase))).ToArray();
        internal void Notify(){revision++;changes.Writer.TryWrite(new(revision));}
        internal ViewSnapshot View()=>Host?.PublishSnapshot() ?? Widget.RenderSnapshot("gallery",1);
        internal ViewNode? Collection=>Node("capture-library.entries");
        internal ViewNode? Node(string id)=>Walk(View().Root).FirstOrDefault(node=>node.Id==id);
        internal IEnumerable<ViewNode> Nodes => Walk(View().Root);
        internal bool Busy=>Walk(View().Root).Any(node=>node.IsBusy==true);
        private static IEnumerable<ViewNode> Walk(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Walk));
        internal async Task Start(){var count=Items.Count;await WidgetTestHost.InitializeAsync(Widget);await WidgetTestHost.SetLifecycleStateAsync(Widget,WidgetLifecycleState.Interactive);await Until(()=>Collection?.IndexedCollection?.Count==count&&!Busy);}
        internal async Task Action(string id,string? text=null){await Widget.OnActionAsync(new(id,id){CommittedText=text});}
        internal async Task Until(Func<bool> condition){using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!condition())await Task.Delay(10,deadline.Token);}
        public async ValueTask DisposeAsync(){SlowRelease.TrySetResult();RecycleRelease.TrySetResult();ContextRelease.TrySetResult();ReplaySaveGate?.TrySetResult();await WidgetTestHost.DestroyAsync(Widget);changes.Writer.TryComplete();replayChanges.Writer.TryComplete();}
    }
}
