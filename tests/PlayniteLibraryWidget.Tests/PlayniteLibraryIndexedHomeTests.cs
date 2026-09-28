using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod]
    public async Task IndexedHomeProductionRouteOpensDeepModalAndReturnsToExactOccurrence()
    {
        var provider = new FakeHost(120);
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
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
            target = widget.RenderState.Value.IndexedHome.ModalReturn!;
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
        await widget.OnActionAsync(new(PlayniteLibraryActions.HomeOpen, declaration.Id));
        var later = host.PublishSnapshot();
        Assert.AreEqual(declaration.Id, later.FocusGroupEntryRequest!.GroupId);
        Assert.IsNull(later.FocusGroupEntryRequest.IndexedItem, "A later ordinary content-entry request must not replay an old modal return.");
        await Background(widget);
    }

    [TestMethod]
    public async Task IndexedHomeFavoriteUpdatesContentWithoutReplacingLogicalMembership()
    {
        var provider = new FakeHost(120);
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "real-playnite");
        var declaration = Nodes(host.CurrentSnapshot.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection);
        var owner = widget.RenderState.Value.IndexedHome.Publication!.QueryOwner;
        using var old = await host.AcquireAsync(declaration.Id, 80, 1);
        var item = old.Range.Items[0];
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, old.RouteAction(item.Key, ControllerButton.A,
            contextActionOwnerId: item.Root.Id, contextActionId: PlayniteLibraryActions.Favorite));
        await WaitIndexed(() => provider.Authority.FavoriteGameIds.Contains("saved-00080") && !widget.RenderState.Value.OrganizationBusy);
        var updated = host.PublishSnapshot();
        var source = Nodes(updated.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection).IndexedCollection!;
        Assert.AreSame(owner, widget.RenderState.Value.IndexedHome.Publication!.QueryOwner);
        Assert.AreEqual(declaration.IndexedCollection!.QueryGeneration, source.QueryGeneration);
        Assert.IsGreaterThan(declaration.IndexedCollection.ContentRevision, source.ContentRevision);
        using var current = await host.AcquireAsync(declaration.Id, 80, 1);
        Assert.AreEqual("Remove favorite", current.Range.Items[0].Root.ContextActions.Single(action => action.ActionId == PlayniteLibraryActions.Favorite).Label);
        Assert.IsNull(old.RouteAction(item.Key, ControllerButton.A), "Old content bindings are retired without changing logical focus identity.");
        await Background(widget);
    }

    [TestMethod]
    public async Task IndexedHomeSupersededCaptureCannotPublishOverNewSearch()
    {
        var provider = new FakeHost(3);
        var widget = Create(provider);
        await Interactive(widget); await Ready(widget, provider);
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
        var query = widget.RenderState.Value.IndexedHome.Publication!.Query;
        Assert.AreEqual("00002", query.Source.Query.SearchText);
        Assert.AreEqual("saved-00002", query.ReadRange(0, 1)[0].Row.Current!.Value.SavedId);
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.HomeCollection.Status);
        await Background(widget);
    }

}
