using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    [TestMethod]
    public async Task IndexedBrowseContentUpdatesKeepIdentityWithoutStalePresentation()
    {
        var query = new PlayniteLibraryBrowseQuery(BrowseCapture(BrowseGames(4), new(false)),
            PlayniteLibraryPrivateState.Empty, false);
        await using var widget = new IndexedBrowseFixture(query);
        await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "content.fixture");
        var declaration = Nodes(host.CurrentSnapshot.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection);
        using var original = await host.AcquireAsync(declaration.Id, 0, 4);
        widget.State = widget.State with { Status = "Updated" };
        var status = host.PublishSnapshot();
        Assert.AreEqual(declaration.IndexedCollection, Nodes(status.Root).Single(node => node.Id == declaration.Id).IndexedCollection,
            "Status-only publication must not replace query or content authority.");
        foreach (var busy in new[] { false, true, false })
        {
            widget.Source.UpdateContent(new(query, actionsEnabled: !busy, launchingSavedId: busy ? null : "game-1"));
            var updated = host.PublishSnapshot();
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(updated).Count);
            var current = Nodes(updated.Root).Single(node => node.Id == declaration.Id).IndexedCollection!;
            Assert.AreEqual(declaration.IndexedCollection!.QueryGeneration, current.QueryGeneration);
            using var lease = await host.AcquireAsync(declaration.Id, 0, 4);
            CollectionAssert.AreEqual(original.Range.Items.Select(row => row.Key).ToArray(), lease.Range.Items.Select(row => row.Key).ToArray());
            Assert.AreEqual(busy, lease.Range.Items[0].Root.IsDisabled == true);
            Assert.AreEqual(busy ? null : ControllerButton.X, lease.Range.Items[0].Root.ContextMenuButton);
            Assert.IsFalse(original.Range.Items[0].Root.IsDisabled == true, "Acquired snapshots stay immutable after content changes.");
            if (!busy)
                StringAssert.Contains(lease.Range.Items[1].Root.AccessibilityLabel!, "Pending");
        }
    }

    [TestMethod]
    public async Task IndexedBrowseReplacementRejectsOldLeasesAndPublishesExactReorderedRows()
    {
        var games = BrowseGames(4);
        var query = new PlayniteLibraryBrowseQuery(BrowseCapture(games, new(false)), PlayniteLibraryPrivateState.Empty, false);
        await using var widget = new IndexedBrowseFixture(query);
        await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "replacement.fixture");
        using var old = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 0, 4);
        var replacement = new PlayniteLibraryBrowseQuery(BrowseCapture(games.Reverse().ToArray(), new(false)), PlayniteLibraryPrivateState.Empty, false);
        widget.Source.PublishQuery(new(replacement), replacement.Count);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(host.PublishSnapshot()).Count);
        Assert.IsNull(old.RouteAction(old.Range.Items[0].Key, ControllerButton.A));
        using var current = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 0, 4);
        CollectionAssert.AreEqual(old.Range.Items.Reverse().Select(row => row.Key).ToArray(), current.Range.Items.Select(row => row.Key).ToArray());
        current.Dispose();
        Assert.IsNull(current.RouteAction(current.Range.Items[0].Key, ControllerButton.A), "Released ranges cannot regain action authority.");
    }
}
