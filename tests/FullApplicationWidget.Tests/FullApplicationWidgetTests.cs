using WidgetRail.Samples.FullApplicationWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WidgetRail.Tests.FullApplicationWidget;

[TestClass]
public sealed class FullApplicationWidgetTests
{
    private const string Collection = FullApplicationReferenceWidget.CollectionId;

    [TestMethod, Timeout(30_000)]
    public async Task CompletePrivateModelPublishesEmptyLazyShellAndDeepCapturedDetails()
    {
        var library = new ReferenceLibrary();
        var widget = new FullApplicationReferenceWidget(library);
        await Interactive(widget);
        try
        {
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "full-app.test");
            var collection = Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == Collection);
            Assert.AreEqual(10_000, widget.PrivateDocumentCount);
            Assert.AreEqual(10_000, collection.IndexedCollection!.Count);
            Assert.IsNull(collection.IndexedCollection.Discovery);
            Assert.AreEqual(0, collection.Children.Count); Assert.AreEqual(0, library.LoadCount);
            Assert.AreEqual(Collection, host.CurrentSnapshot.InitialFocusId);
            AssertSupported(host.CurrentSnapshot);
            using var lease = await host.AcquireAsync(Collection, 9_999, 1);
            Assert.AreEqual(1, library.LoadCount);
            var row = lease.Range.Items.Single();
            Assert.AreEqual("document-09999", row.Key);
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(row.Key, ControllerButton.A));
            await Until(() => widget.Render().ActiveInputScopeId != host.CurrentSnapshot.ActiveInputScopeId);
            host.PublishSnapshot();
            Assert.AreEqual("Deterministic private record 09999.", Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == "full-app.details.summary").Text);
            await Back(widget, host);
            var target = host.CurrentSnapshot.FocusGroupEntryRequest?.IndexedItem;
            Assert.IsNotNull(target); Assert.AreEqual(9_999, target.Index); Assert.AreEqual(row.Key, target.ItemKey);
            Assert.AreEqual(collection.IndexedCollection.QueryGeneration, target.QueryGeneration);
            var request = host.CurrentSnapshot.FocusGroupEntryRequest!.RequestId;
            Assert.AreEqual(request, host.PublishSnapshot().FocusGroupEntryRequest!.RequestId);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod, Timeout(30_000)]
    public async Task BoundedArbitraryRangesAndReverseReadsDoNotInventCursorWindows()
    {
        var library = new ReferenceLibrary(); var widget = new FullApplicationReferenceWidget(library);
        await Interactive(widget);
        try
        {
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "full-app.test");
            var parentNodes = Nodes(host.CurrentSnapshot.Root).Count();
            foreach (var start in new[] { 0, 512, 9_936, 8_000, 32, 0 })
            {
                using var range = await host.AcquireAsync(Collection, start, 64);
                Assert.AreEqual(64, range.Range.Items.Count);
                Assert.AreEqual($"document-{start:D5}", range.Range.Items[0].Key);
                Assert.AreEqual($"document-{start + 63:D5}", range.Range.Items[^1].Key);
                Assert.AreEqual(parentNodes, Nodes(host.PublishSnapshot().Root).Count());
                AssertSupported(host.CurrentSnapshot);
            }
            Assert.AreEqual(6, library.LoadCount);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => { using var _ = await host.AcquireAsync(Collection, 0, 65); });
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod, Timeout(30_000)]
    public async Task FailedRangeKeepsCollectionAndSafeRetryRecovers()
    {
        var library = new ReferenceLibrary(); var widget = new FullApplicationReferenceWidget(library);
        await Interactive(widget);
        try
        {
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "full-app.test");
            library.FailNext();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var _ = await host.AcquireAsync(Collection, 500, 32); });
            Assert.AreEqual("The private library could not be loaded. Try again.", error.Message);
            host.PublishSnapshot();
            var retry = Nodes(host.CurrentSnapshot.Root).Single(node => node.ActionId == "full-app.retry");
            Assert.IsFalse(Nodes(host.CurrentSnapshot.Root).Any(node => node.Text?.Contains("Deterministic reference failure") == true));
            Assert.AreEqual(10_000, Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == Collection).IndexedCollection!.Count);
            await widget.OnActionAsync(new(retry.ActionId!, retry.Id, InputScopeId: host.CurrentSnapshot.ActiveInputScopeId));
            host.PublishSnapshot();
            using var ready = await host.AcquireAsync(Collection, 500, 32);
            Assert.AreEqual("document-00500", ready.Range.Items[0].Key);
            Assert.IsFalse(Nodes(host.CurrentSnapshot.Root).Any(node => node.ActionId == "full-app.retry"));
            AssertSupported(host.CurrentSnapshot);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod, Timeout(30_000)]
    public async Task RefreshRetiresOldActionsButPreservesMembershipAndKeys()
    {
        var widget = new FullApplicationReferenceWidget(); await Interactive(widget);
        try
        {
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "full-app.test");
            using var old = await host.AcquireAsync(Collection, 75, 1);
            var before = Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == Collection).IndexedCollection!;
            await widget.OnActionAsync(new("full-app.open", old.Range.Items[0].Root.Id));
            Assert.AreEqual(host.CurrentSnapshot.ActiveInputScopeId, widget.Render().ActiveInputScopeId);
            await widget.OnActionAsync(new("full-app.refresh", "full-app.refresh")); host.PublishSnapshot();
            var after = Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == Collection).IndexedCollection!;
            Assert.AreEqual(before.QueryGeneration, after.QueryGeneration); Assert.AreEqual(before.Count, after.Count);
            Assert.IsTrue(after.ContentRevision > before.ContentRevision);
            Assert.IsNull(old.RouteAction(old.Range.Items[0].Key, ControllerButton.A));
            using var fresh = await host.AcquireAsync(Collection, 75, 1);
            Assert.AreEqual(old.Range.Items[0].Key, fresh.Range.Items[0].Key);
            Assert.AreEqual(old.Range.Items[0].Root.Id, fresh.Range.Items[0].Root.Id);
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, fresh.RouteAction(fresh.Range.Items[0].Key, ControllerButton.A));
            await Until(() => widget.Render().ActiveInputScopeId != host.CurrentSnapshot.ActiveInputScopeId);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod, Timeout(30_000)]
    public async Task BackgroundCancelsRangeAndReopenKeepsLogicalQuery()
    {
        var library = new ReferenceLibrary(); var widget = new FullApplicationReferenceWidget(library);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        library.BeforeNextLoad(async token =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled.SetResult(); throw; }
        });
        await Interactive(widget);
        try
        {
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "full-app.test");
            var generation = Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == Collection).IndexedCollection!.QueryGeneration;
            var read = host.AcquireAsync(Collection, 300, 32).AsTask();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var _ = await read; });
            await Interactive(widget); host.PublishSnapshot();
            Assert.AreEqual(generation, Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == Collection).IndexedCollection!.QueryGeneration);
            using var fresh = await host.AcquireAsync(Collection, 300, 32);
            Assert.AreEqual("document-00300", fresh.Range.Items[0].Key);
            AssertSupported(host.CurrentSnapshot);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private static async Task Back(FullApplicationReferenceWidget widget, WidgetIndexedCollectionTestHost host)
    {
        var back = Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == "full-app.details.back");
        await widget.OnActionAsync(new(back.ActionId!, back.Id, ControllerButton.B, InputScopeId: host.CurrentSnapshot.ActiveInputScopeId));
        host.PublishSnapshot();
    }
    private static void AssertSupported(ViewSnapshot snapshot)
    {
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        Assert.IsTrue(Nodes(snapshot.Root).All(node => node.VirtualCollectionWindow is null && node.CollectionAnchorKey is null &&
            node.CollectionGeneration is null && node.CollectionResetGeneration is null && node.ScrollNearStartActionId is null &&
            node.ScrollNearEndActionId is null && (node.CollectionLayout is null || node.Kind == ViewNodeKind.IndexedCollection)));
    }
    private static ValueTask Interactive(Widget widget) => WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 100; ++i) { if (ready()) return; await Task.Delay(10); } Assert.Fail("Captured action did not complete."); }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)
    { yield return node; foreach (var child in node.Children) foreach (var nested in Nodes(child)) yield return nested; }
}
