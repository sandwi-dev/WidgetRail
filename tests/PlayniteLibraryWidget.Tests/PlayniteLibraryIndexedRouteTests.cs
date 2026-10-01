using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod]
    public async Task IndexedBrowseProductionRouteOpensDeepModalAndReturnsToExactOccurrence()
    {
        var provider = new FakeHost(120);
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "home"));
        await widget.WhenLibraryIdleAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "real-playnite");
        var declaration = Nodes(host.CurrentSnapshot.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection);
        Assert.AreEqual(120, declaration.IndexedCollection!.Count);
        Assert.AreEqual(0, declaration.Children.Count);
        Assert.IsFalse(Nodes(host.CurrentSnapshot.Root).Any(node => node.ActionId == PlayniteLibraryActions.DetailsOpen));
        IndexedCollectionFocusTarget target;
        using (var lease = await host.AcquireAsync(declaration.Id, 80, 1))
        {
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(lease.Range.Items[0].Key, ControllerButton.A));
            await WaitIndexed(() => widget.RenderState.Value.DetailsItem is not null);
            await widget.WhenDetailsIdleAsync();
            target = widget.RenderState.Value.IndexedBrowse.ModalReturn!;
            Assert.AreEqual(80, target.Index);
            Assert.AreEqual("saved-00080", widget.RenderState.Value.DetailsItem!.Value.SavedId);
            Assert.IsTrue(widget.RenderState.Value.DetailsCapturedTarget!.IsCurrent);
            host.PublishSnapshot();
            Assert.IsNull(lease.RouteAction(lease.Range.Items[0].Key, ControllerButton.A), "The modal blocks its retained parent row input.");
        }
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
        var returned = host.PublishSnapshot();
        Assert.AreEqual(target, returned.FocusGroupEntryRequest!.IndexedItem);
        Assert.AreEqual(declaration.IndexedCollection.QueryGeneration,
            Nodes(returned.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection).IndexedCollection!.QueryGeneration);
        await widget.OnActionAsync(new(PlayniteLibraryActions.SearchFocus, declaration.Id));
        var search = host.PublishSnapshot();
        Assert.AreEqual("playnite-library.search.group", search.FocusGroupEntryRequest!.GroupId,
            "A consumed modal return must not override a later RS search request.");
        Assert.IsNull(search.FocusGroupEntryRequest.IndexedItem);
        await Background(widget);
    }

    [TestMethod]
    public async Task IndexedBrowseFavoriteUpdatesContentWithoutReplacingLogicalMembership()
    {
        var provider = new FakeHost(120);
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "home"));
        await widget.WhenLibraryIdleAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "real-playnite");
        var declaration = Nodes(host.CurrentSnapshot.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection);
        var owner = widget.RenderState.Value.IndexedBrowse.Publication!.QueryOwner;
        using var old = await host.AcquireAsync(declaration.Id, 80, 1);
        var item = old.Range.Items[0];
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, old.RouteAction(item.Key, ControllerButton.A,
            contextActionOwnerId: item.Root.Id, contextActionId: PlayniteLibraryActions.Favorite));
        await WaitIndexed(() => provider.Authority.FavoriteGameIds.Contains("saved-00080") && !widget.RenderState.Value.OrganizationBusy);
        var updated = host.PublishSnapshot();
        var source = Nodes(updated.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection).IndexedCollection!;
        Assert.AreSame(owner, widget.RenderState.Value.IndexedBrowse.Publication!.QueryOwner);
        Assert.AreEqual(declaration.IndexedCollection!.QueryGeneration, source.QueryGeneration);
        Assert.IsGreaterThan(declaration.IndexedCollection.ContentRevision, source.ContentRevision);
        using var current = await host.AcquireAsync(declaration.Id, 80, 1);
        Assert.AreEqual("Remove favorite", current.Range.Items[0].Root.ContextActions.Single(action => action.ActionId == PlayniteLibraryActions.Favorite).Label);
        Assert.IsNull(old.RouteAction(item.Key, ControllerButton.A), "Old content bindings are retired without changing logical focus identity.");
        await Background(widget);
    }

    [TestMethod]
    public async Task IndexedBrowseSupersededCaptureCannotPublishOverNewSearch()
    {
        var provider = new FakeHost(3);
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "home"));
        await widget.WhenLibraryIdleAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLibraryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.QueryHandler = (request, _) =>
        {
            if (request.Query.SearchText == "00001") { started.TrySetResult(); return new(release.Task); }
            return ValueTask.FromResult(new WidgetAppLibraryPage([Item(2)], null, null, "new"));
        };
        await widget.OnActionAsync(new(PlayniteLibraryActions.SearchCommit, "playnite-library.search") { CommittedText = "00001" });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await widget.OnActionAsync(new(PlayniteLibraryActions.SearchCommit, "playnite-library.search") { CommittedText = "00002" });
        release.SetResult(new([Item(1)], null, null, "old"));
        await widget.WhenLibraryIdleAsync();
        var query = widget.RenderState.Value.IndexedBrowse.Publication!.Query;
        Assert.AreEqual("00002", query.Source.Query.SearchText);
        Assert.AreEqual("saved-00002", query.ReadRange(0, 1)[0].Item.Value.SavedId);
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.BrowseCollection.Status);
        await Background(widget);
    }

    [TestMethod]
    public async Task IndexedBrowseCancelledSemanticChangeRetriesOnReactivation()
    {
        var provider = new FakeHost(3);
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "home"));
        await widget.WhenLibraryIdleAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.QueryHandler = async (_, token) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new([], null, null, "never"); };
        await widget.OnActionAsync(new(PlayniteLibraryActions.SearchCommit, "playnite-library.search") { CommittedText = "00002" });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Background(widget);
        await widget.WhenLibraryIdleAsync();
        Assert.AreEqual(WidgetPagedResourceStatus.NotLoaded, widget.BrowseCollection.Status);
        provider.QueryHandler = (_, _) => ValueTask.FromResult(new WidgetAppLibraryPage([Item(2)], null, null, "retry"));
        await Interactive(widget);
        await widget.WhenWarmStateIdleAsync();
        await widget.WhenLibraryIdleAsync();
        Assert.AreEqual("00002", widget.RenderState.Value.IndexedBrowse.Publication!.Query.Source.Query.SearchText);
        Assert.AreEqual("saved-00002", BrowseItems(widget).Single().Value.SavedId);
        await Background(widget);
    }

    private static Task WaitIndexed(Func<bool> condition) => WaitIndexedCore(condition).WaitAsync(TimeSpan.FromSeconds(5));
    private static async Task WaitIndexedCore(Func<bool> condition)
    { while (!condition()) await Task.Delay(1); }

    [TestMethod]
    public async Task IndexedBrowseQueuedOldGameDoesNotRetargetAfterQueryReplacement()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeHost(120) { FavoriteCompletion = release.Task };
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "home"));
        await widget.WhenLibraryIdleAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "real-playnite");
        using var old = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 80, 2);
        var first = old.Range.Items[0];
        var second = old.Range.Items[1];
        // Admit the burst before the first action publishes its busy/disabled
        // content. Otherwise rejecting the second old binding is correct.
        lock (ModelGate(widget))
        {
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, old.RouteAction(first.Key, ControllerButton.A,
                contextActionOwnerId: first.Root.Id, contextActionId: PlayniteLibraryActions.Favorite));
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, old.RouteAction(second.Key, ControllerButton.A,
                contextActionOwnerId: second.Root.Id, contextActionId: PlayniteLibraryActions.Launch));
        }
        await WaitIndexed(() => widget.RenderState.Value.OrganizationBusy);
        // An asynchronous publication may replace a query while an already admitted
        // action waits in the serial queue. The queued row must keep its old owner.
        await widget.OnActionAsync(new(PlayniteLibraryActions.SearchCommit, "playnite-library.search") { CommittedText = "00002" });
        await widget.WhenLibraryIdleAsync();
        Assert.AreEqual("saved-00002", BrowseItems(widget).Single().Value.SavedId);
        host.PublishSnapshot();
        release.SetResult(true);
        await WaitIndexed(() => !widget.RenderState.Value.OrganizationBusy);
        host.PublishSnapshot();
        using var sentinel = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 0, 1);
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, sentinel.RouteAction(sentinel.Range.Items[0].Key, ControllerButton.RightStick));
        await WaitIndexed(() => widget.RenderState.Value.SearchFocusPending);
        Assert.AreEqual(0, provider.Launches.Count);
        await Background(widget);
    }

    [TestMethod]
    public async Task IndexedBrowseRefreshFailureKeepsExactSourceAndUsableRows()
    {
        var provider = new FakeHost(120);
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "home"));
        await widget.WhenLibraryIdleAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "real-playnite");
        var before = Nodes(host.CurrentSnapshot.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection).IndexedCollection;
        provider.QueryHandler = (_, _) => throw new WidgetCapabilityException("offline", "fixture");
        await widget.OnActionAsync(new(PlayniteLibraryActions.Refresh, "refresh"));
        await widget.WhenLibraryIdleAsync();
        var current = host.PublishSnapshot();
        Assert.AreEqual(WidgetPagedResourceStatus.Error, widget.BrowseCollection.Status);
        Assert.AreEqual(before, Nodes(current.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection).IndexedCollection);
        Assert.IsTrue(Nodes(current.Root).Any(node => node.Id == "playnite-library.retained-error"));
        using var lease = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 80, 1);
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(lease.Range.Items[0].Key, ControllerButton.A));
        await WaitIndexed(() => widget.RenderState.Value.DetailsItem?.Value.SavedId == "saved-00080");
        await Background(widget);
    }

    [TestMethod]
    public async Task IndexedBrowseFailedNewFilterDoesNotPresentOldQueryAsMatchingResults()
    {
        var provider = new FakeHost(120);
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "home"));
        await widget.WhenLibraryIdleAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "real-playnite");
        using var old = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 80, 1);
        provider.QueryHandler = (_, _) => throw new WidgetCapabilityException("offline", "fixture");
        await widget.OnActionAsync(new(PlayniteLibraryActions.SearchCommit, "playnite-library.search") { CommittedText = "new filter" });
        await widget.WhenLibraryIdleAsync();
        var current = host.PublishSnapshot();
        Assert.AreEqual(WidgetPagedResourceStatus.Error, widget.BrowseCollection.Status);
        Assert.IsNull(widget.RenderState.Value.IndexedBrowse.Publication);
        Assert.IsFalse(Nodes(current.Root).Any(node => node.Kind == ViewNodeKind.IndexedCollection));
        Assert.IsNull(old.RouteAction(old.Range.Items[0].Key, ControllerButton.A));
        Assert.AreEqual("new filter", widget.RenderState.Value.BrowseCollection.Query.SearchText);
        await Background(widget);
    }
}
