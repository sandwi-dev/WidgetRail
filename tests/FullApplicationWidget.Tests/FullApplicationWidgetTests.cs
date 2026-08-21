using WidgetRail.Samples.FullApplicationWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WidgetRail.Tests.FullApplicationWidget;

[TestClass]
public sealed class FullApplicationWidgetTests
{
    [TestMethod, Timeout(30_000)]
    public async Task ApplicationScaleModelProjectsBoundedCursorWindowAndDetails()
    {
        var widget = new FullApplicationReferenceWidget();
        await Interactive(widget);
        await widget.WhenDocumentsIdleAsync();
        Assert.AreEqual(ReferenceLibrary.DocumentCount, widget.PrivateDocumentCount);
        Assert.IsTrue(widget.Documents.Items.Count <= FullApplicationReferenceWidget.MaximumRetainedItems);
        var view = Snapshot(widget, 1);
        Assert.AreEqual(ProtocolConstants.VirtualCollectionWindowVersion, view.ProtocolVersion);
        var scroll = Nodes(view.Root).Single(node => node.Id == "full-app.document-list");
        Assert.AreEqual(ReferenceLibrary.DocumentCount,
            scroll.VirtualCollectionWindow?.TotalItemCount);
        Assert.AreEqual(0L, scroll.VirtualCollectionWindow?.FirstItemIndex);
        Assert.IsTrue(Nodes(view.Root).Count() <=
            FullApplicationReferenceWidget.MaximumRetainedItems + 8);
        var first = Nodes(view.Root).First(node => node.ActionId == "full-app.open");
        await widget.OnActionAsync(new(first.ActionId!, first.Id));
        var details = Snapshot(widget, 2);
        StringAssert.Contains(Nodes(details.Root).Single(node =>
            node.Id == "full-app.details.summary").Text!, "Deterministic private record");
        await widget.OnActionAsync(new(
            Nodes(details.Root).Single(node => node.Id == "full-app.details.back").ActionId!,
            "full-app.details.back", ControllerButton.B,
            InputScopeId: details.ActiveInputScopeId));
        Assert.AreEqual(first.Id, Snapshot(widget, 3).InitialFocusId);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AdjacentPagingRetainsOnlyBoundedHostProjection()
    {
        var widget = new FullApplicationReferenceWidget();
        await Interactive(widget);
        await widget.WhenDocumentsIdleAsync();
        for (var page = 0; page < 8; page++)
        {
            await widget.OnActionAsync(new(
                "full-app.documents.cursor.after", "full-app.document-list"));
            await widget.WhenDocumentsIdleAsync();
        }
        Assert.AreEqual(ReferenceLibrary.DocumentCount, widget.PrivateDocumentCount);
        Assert.AreEqual(FullApplicationReferenceWidget.MaximumRetainedItems,
            widget.Documents.Items.Count);
        Assert.IsTrue(widget.Documents.After is not null);
        var forward = Snapshot(widget, 4);
        var forwardWindow = Nodes(forward.Root).Single(node =>
            node.Id == "full-app.document-list").VirtualCollectionWindow;
        Assert.IsNotNull(forwardWindow);
        Assert.AreEqual(VirtualCollectionWindowChange.Append, forwardWindow.Change);
        Assert.IsTrue(forwardWindow.FirstItemIndex > 0);
        Assert.AreEqual(ReferenceLibrary.DocumentCount, forwardWindow.TotalItemCount);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(forward).Count);
        var beforeFirst = forwardWindow.FirstItemIndex;
        await widget.OnActionAsync(new(
            "full-app.documents.cursor.before", "full-app.document-list"));
        await widget.WhenDocumentsIdleAsync();
        var backward = Snapshot(widget, 5);
        var backwardWindow = Nodes(backward.Root).Single(node =>
            node.Id == "full-app.document-list").VirtualCollectionWindow;
        Assert.AreEqual(VirtualCollectionWindowChange.Prepend, backwardWindow?.Change);
        Assert.IsTrue(backwardWindow?.FirstItemIndex < beforeFirst);
        Assert.IsTrue(Nodes(backward.Root).Count() <=
            FullApplicationReferenceWidget.MaximumRetainedItems + 8);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task FailureRetainsSafeErrorAndRetryRecovers()
    {
        var source = new ReferenceLibrary();
        var widget = new FullApplicationReferenceWidget(source);
        source.FailNext();
        await Interactive(widget);
        await widget.WhenDocumentsIdleAsync();
        var failed = Snapshot(widget, 5);
        Assert.IsTrue(Nodes(failed.Root).Any(node => node.Id == "full-app.retry"));
        Assert.AreEqual("full-app.retry", failed.InitialFocusId);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(failed).Count);
        await widget.OnActionAsync(new("full-app.retry", "full-app.retry"));
        await widget.WhenDocumentsIdleAsync();
        var ready = Snapshot(widget, 6);
        Assert.IsTrue(Nodes(ready.Root).Any(node => node.ActionId == "full-app.open"));
        Assert.IsNotNull(ready.InitialFocusId);
        Assert.IsTrue(Nodes(ready.Root).Any(node => node.Id == ready.InitialFocusId));
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(ready).Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ActiveLifetimeCancelsAndDrainsBlockedLoad()
    {
        var source = new ReferenceLibrary();
        var loadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.BeforeNextLoad(async token =>
        {
            loadStarted.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        });
        var widget = new FullApplicationReferenceWidget(source);
        await Interactive(widget);
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var loading = Snapshot(widget, 7);
        Assert.IsNull(loading.InitialFocusId);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(loading).Count);
        await Background(widget);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await widget.WhenDocumentsIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, widget.Documents.Items.Count);
        var reset = Snapshot(widget, 8);
        Assert.IsNull(reset.InitialFocusId);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(reset).Count);
    }

    private static ValueTask Interactive(Widget widget) =>
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    private static ValueTask Background(Widget widget) =>
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    private static ViewSnapshot Snapshot(Widget widget, long sequence) =>
        widget.Render().CreateSnapshot("full-app.test", sequence);
    private static IEnumerable<ViewNode> Nodes(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var nested in Nodes(child)) yield return nested;
    }
}
