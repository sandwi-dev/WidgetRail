using System.Threading.Channels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    private static readonly TimeSpan IndexedLimit = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task IndexedBrowseUsesProductionPageAndOnlyProjectsDemandedPosters()
    {
        var projected = new List<string>();
        var games = BrowseGames(10_000);
        var query = new PlayniteLibraryBrowseQuery(BrowseCapture(games, new(false), game => projected.Add(game.Id)),
            PlayniteLibraryPrivateState.Empty, false);
        await using var widget = new IndexedBrowseFixture(query);
        await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "playnite-indexed");
        var snapshot = host.CurrentSnapshot;
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var nodes = Nodes(snapshot.Root).ToArray();
        var collection = nodes.Single(node => node.Id == PlayniteLibraryPresentation.ScrollId);
        Assert.AreEqual(ViewNodeKind.IndexedCollection, collection.Kind);
        Assert.AreEqual(10_000, collection.IndexedCollection!.Count);
        Assert.AreEqual(0, collection.Children.Count);
        Assert.AreEqual(0, projected.Count, "Rendering the real page must not prepare even the first poster eagerly.");
        Assert.AreEqual("10000 games", nodes.Single(node => node.Id == "playnite-library.status").Text);
        Assert.AreEqual(PlayniteLibraryPresentation.ScrollId, snapshot.InitialFocusId);
        Assert.AreEqual(PlayniteLibraryPresentation.ScrollId, snapshot.FocusGroupEntryRequest?.GroupId);
        Assert.IsTrue(nodes.Any(node => node.Id == "playnite-library.hint.options"));
        Assert.IsTrue(nodes.Any(node => node.Kind == ViewNodeKind.BackgroundSurface));

        using var lease = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 9_000, 4);
        Assert.AreEqual(4, projected.Count);
        for (var offset = 0; offset < 4; ++offset)
        {
            var row = lease.Range.Items[offset];
            var expected = query.ReadRange(9_000 + offset, 1)[0];
            Assert.AreEqual(expected.Key.Value, row.Key);
            Assert.AreEqual(PlayniteLibraryActions.DetailsOpen, row.Root.ActionId);
            Assert.AreEqual(ControllerButton.X, row.Root.ContextMenuButton);
            CollectionAssert.AreEqual(PlayniteLibraryGameOptions.Create(expected.Item, false, []).ToArray(),
                row.Root.ContextActions.ToArray());
            Assert.IsNotNull(row.Root.FocusBackgroundArtworkHandle);
            Assert.IsTrue(row.Root.StyleClasses.Contains("playnite-library-browse-tile"));
            var artwork = Nodes(row.Root).Single(node => node.Kind == ViewNodeKind.Image);
            Assert.AreEqual(ImageFit.Cover, artwork.ImageFit);
        }
    }

    [TestMethod]
    public async Task IndexedBrowseAdmittedActionsKeepCapturedGameAndParentShortcutsStayOnParent()
    {
        var query = new PlayniteLibraryBrowseQuery(BrowseCapture(BrowseGames(100), new(false)),
            PlayniteLibraryPrivateState.Empty, false);
        await using var widget = new IndexedBrowseFixture(query);
        await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "playnite-indexed");
        using var lease = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 80, 2);
        var first = lease.Range.Items[0];
        var second = lease.Range.Items[1];
        widget.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(first.Key, ControllerButton.A));
        await widget.Entered.Task.WaitAsync(IndexedLimit);
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(second.Key, ControllerButton.A,
            contextActionOwnerId: second.Root.Id, contextActionId: PlayniteLibraryActions.Favorite));
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(second.Key, ControllerButton.RightStick));
        lease.Dispose();
        var replacement = new PlayniteLibraryBrowseQuery(BrowseCapture(BrowseGames(2), new(false)),
            PlayniteLibraryPrivateState.Empty, false);
        widget.Source.PublishQuery(new(replacement), replacement.Count);
        host.PublishSnapshot();
        widget.Block.SetResult();
        var opened = await widget.Next();
        Assert.AreSame(query, opened.Item!.Query);
        Assert.AreEqual("game-80", opened.Item.Item.Value.AppId);
        Assert.AreEqual(80, opened.Action.FocusedCollectionItem!.Index);
        Assert.AreEqual(first.Key, opened.Action.FocusedCollectionItem.ItemKey);
        var favorite = await widget.Next();
        Assert.AreSame(query, favorite.Item!.Query);
        Assert.AreEqual("game-81", favorite.Item.Item.Value.AppId);
        Assert.AreEqual(PlayniteLibraryActions.Favorite, favorite.Action.ActionId);
        var search = await widget.Next();
        Assert.IsNull(search.Item);
        Assert.AreEqual(PlayniteLibraryActions.SearchFocus, search.Action.ActionId);
        Assert.IsNull(lease.RouteAction(first.Key, ControllerButton.A));
    }

    [TestMethod]
    public async Task IndexedBrowseArtworkUsesExactCapturedGameAndReleaseRejectsMoreDemand()
    {
        var requested = new List<string>();
        var query = new PlayniteLibraryBrowseQuery(BrowseCapture(BrowseGames(100), new(false),
            artwork: (game, _, _) =>
            {
                requested.Add(game.Id);
                return ValueTask.FromResult<WidgetEncodedArtwork?>(IndexedBrowseFixture.Png);
            }), PlayniteLibraryPrivateState.Empty, false);
        await using var widget = new IndexedBrowseFixture(query);
        await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "playnite-indexed");
        using var lease = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 77, 1);
        var row = lease.Range.Items.Single();
        Assert.IsNotNull(await lease.ResolveArtworkAsync(row.Key, new("art-game-77")));
        CollectionAssert.AreEqual(new[] { "game-77" }, requested);
        Assert.IsNull(await lease.ResolveArtworkAsync(row.Key, new("art-game-76")));
        lease.Dispose();
        Assert.IsNull(await lease.ResolveArtworkAsync(row.Key, new("art-game-77")));
        Assert.AreEqual(1, requested.Count);
    }

    [TestMethod]
    public async Task IndexedBrowseLogicalReturnSurvivesContentButNotQueryReplacement()
    {
        var query = new PlayniteLibraryBrowseQuery(BrowseCapture(BrowseGames(100), new(false)),
            PlayniteLibraryPrivateState.Empty, false);
        await using var widget = new IndexedBrowseFixture(query);
        await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "playnite-indexed");
        IndexedCollectionFocusTarget target;
        using (var lease = await host.AcquireAsync(PlayniteLibraryPresentation.ScrollId, 90, 1))
        {
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, lease.RouteAction(lease.Range.Items[0].Key, ControllerButton.A));
            target = (await widget.Next()).Action.FocusedCollectionItem!;
        }
        widget.Source.UpdateContent(new(query, launchingSavedId: "game-90"));
        widget.ReturnTarget = target;
        var current = host.PublishSnapshot();
        Assert.AreEqual(target, current.FocusGroupEntryRequest?.IndexedItem);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(current).Count);
        widget.ReturnTarget = null;
        widget.Source.PublishQuery(new(query), query.Count);
        host.PublishSnapshot();
        Assert.ThrowsExactly<ArgumentException>(() => widget.Source.Enter(PlayniteLibraryPresentation.ScrollId, 3, target));
    }

    [TestMethod]
    public async Task IndexedBrowseEmptyResultsDoNotFallBackToRetainedCursorRows()
    {
        var query = new PlayniteLibraryBrowseQuery(BrowseCapture([], new(false)), PlayniteLibraryPrivateState.Empty, false);
        await using var widget = new IndexedBrowseFixture(query);
        widget.State = widget.State with { Collection = Snapshot(WidgetPagedResourceStatus.Ready, [BrowseItem(BrowseGames(1)[0])]) };
        await widget.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "playnite-indexed");
        var nodes = Nodes(host.CurrentSnapshot.Root).ToArray();
        Assert.IsFalse(nodes.Any(node => node.ActionId == PlayniteLibraryActions.DetailsOpen));
        Assert.IsFalse(nodes.Any(node => node.Kind == ViewNodeKind.IndexedCollection));
        Assert.AreEqual("0 games", nodes.Single(node => node.Id == "playnite-library.status").Text);
        Assert.IsFalse(nodes.Any(node => node.Id == "playnite-library.hint.options"));
    }

    private sealed class IndexedBrowseFixture : Widget, IAsyncDisposable
    {
        internal static readonly WidgetEncodedArtwork Png = new(WidgetArtworkContentType.Png,
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII="));
        internal readonly WidgetIndexedCollection<PlayniteLibraryBrowseContent, PlayniteLibraryBrowseItem> Source;
        internal PlayniteLibraryPresentationState State;
        internal TaskCompletionSource? Block;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IndexedCollectionFocusTarget? ReturnTarget;
        private readonly Channel<(PlayniteLibraryBrowseItem? Item, WidgetActionEvent Action)> events = Channel.CreateUnbounded<(PlayniteLibraryBrowseItem?, WidgetActionEvent)>();
        internal IndexedBrowseFixture(PlayniteLibraryBrowseQuery query)
        {
            State = PlayniteLibraryLayoutTests.State(Snapshot(WidgetPagedResourceStatus.Ready, []),
                PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Browse, []) with { ContentEntryRequestId = 1 };
            Source = CreateIndexedCollection("playnite.browse", new PlayniteLibraryBrowseContent(query), query.Count,
                PlayniteLibraryIndexedBrowse.Options(async (_, item, action, token) =>
                {
                    if (Block is { } block) { Entered.TrySetResult(); await block.Task.WaitAsync(token); }
                    await events.Writer.WriteAsync((item, action), token);
                }));
        }
        internal async Task Start()
        {
            await WidgetTestHost.InitializeAsync(this);
            await WidgetTestHost.SetLifecycleStateAsync(this, WidgetLifecycleState.Visible);
        }
        internal Task<(PlayniteLibraryBrowseItem? Item, WidgetActionEvent Action)> Next() =>
            events.Reader.ReadAsync().AsTask().WaitAsync(IndexedLimit);
        public override WidgetView Render()
        {
            var view = PlayniteLibraryPresentation.Render(State, indexedBrowse: Source);
            return view with { Root = ((ContainerElement)view.Root).InputScope("browse.scope"),
                ActiveInputScopeId = "browse.scope", FocusGroupEntryRequest = ReturnTarget is { } target
                    ? Source.Enter(PlayniteLibraryPresentation.ScrollId, 2, target) : view.FocusGroupEntryRequest };
        }
        public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default) =>
            events.Writer.WriteAsync((null, action), cancellationToken);
        public ValueTask DisposeAsync() => WidgetTestHost.DestroyAsync(this);
    }
}
