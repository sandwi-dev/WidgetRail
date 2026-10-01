using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.YouTubeWidget.Tests;

public sealed partial class YouTubeWidgetTests
{
    [TestMethod]
    public async Task SearchStickShortcutReturnsFromLazyRowToOnlyTheSearchField()
    {
        var widget = await CreateSearchFixture(new SearchFixture((_, _, _) => new(Items(0, 3), null, 3)));
        try
        {
            await SubmitSearch(widget, "search shortcut");
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "search-stick");
            using var rows = await host.AcquireAsync(YouTubeVideoWidget.SearchScrollId, 1, 1);
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, rows.RouteAction("youtube-video-" + ItemId(1), ControllerButton.RightStick));
            ViewSnapshot returned = host.CurrentSnapshot;
            for (var attempt = 0; attempt < 200; ++attempt)
            {
                returned = widget.RenderSnapshot("search-stick", attempt + 20);
                if (returned.FocusGroupEntryRequest?.GroupId == YouTubeVideoWidget.SearchFieldGroupId) break;
                await Task.Delay(10);
            }
            Assert.IsEmpty(ViewSnapshotValidator.Validate(returned));
            Assert.AreEqual("youtube.search.query", returned.InitialFocusId);
            Assert.AreEqual(YouTubeVideoWidget.SearchFieldGroupId, returned.FocusGroupEntryRequest?.GroupId);
            var field = Find(returned.Root, YouTubeVideoWidget.SearchFieldGroupId);
            Assert.HasCount(1, field.Children);
            Assert.AreEqual("youtube.search.query", field.Children[0].Id);
            Assert.IsNull(returned.FocusGroupEntryRequest?.IndexedItem);
            var request = returned.FocusGroupEntryRequest!.RequestId;
            await widget.OnActionAsync(new(YouTubeVideoWidget.SearchFocusActionId, "youtube.root"));
            Assert.IsGreaterThan(request, widget.RenderSnapshot("search-stick", 500).FocusGroupEntryRequest!.RequestId);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task SettingsReturnFromLazySearchItemUsesCollectionIdentityAndKeepsMedia()
    {
        var widget = await CreateSearchFixture(new SearchFixture((_, _, _) => new(Items(0, 3), null, 3)));
        try
        {
            await widget.OnActionAsync(new("youtube.link.open", "youtube.link.open"));
            await CommitAsync(widget, "https://youtu.be/" + VideoId);
            var loaded = widget.RenderSnapshot("settings-search", 1);
            await ObserveAsync(widget, loaded.EmbeddedMediaSession!.PendingCommand!, EmbeddedMediaPlaybackState.Playing, 1);
            await widget.OnActionAsync(new("youtube.search.open-route", "youtube.root"));
            await SubmitSearch(widget, "settings");
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "settings-search");
            using var rows = await host.AcquireAsync(YouTubeVideoWidget.SearchScrollId, 1, 1);
            var before = host.CurrentSnapshot;
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, rows.RouteAction("youtube-video-" + ItemId(1), ControllerButton.Y));
            ViewSnapshot settings = before;
            for (var attempt = 0; attempt < 200; ++attempt)
            {
                settings = widget.RenderSnapshot("settings-search", attempt + 20);
                if (TryFind(settings.Root, "youtube.setup.key") is not null) break;
                await Task.Delay(10);
            }
            Assert.IsNotNull(TryFind(settings.Root, "youtube.setup.key"));
            await widget.OnActionAsync(new("youtube.setup.back", "youtube.setup.back"));
            var returned = widget.RenderSnapshot("settings-search", 500);
            Assert.IsEmpty(ViewSnapshotValidator.Validate(returned));
            Assert.AreEqual(YouTubeVideoWidget.SearchScrollId, returned.InitialFocusId);
            Assert.AreEqual("youtube-video-" + ItemId(1), returned.FocusGroupEntryRequest?.IndexedItem?.ItemKey);
            Assert.AreEqual(1, returned.FocusGroupEntryRequest?.IndexedItem?.Index);
            Assert.AreEqual(before.EmbeddedMediaSession!.Id, settings.EmbeddedMediaSession!.Id);
            Assert.AreEqual(before.EmbeddedMediaSession.Id, returned.EmbeddedMediaSession!.Id);
            Assert.IsNull(returned.EmbeddedMediaSession.PendingCommand);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task EmptySearchRetainsContentSizingContract()
    {
        var widget = await CreateSearchFixture(new SearchFixture((_, _, _) => new([], null, 0)));
        try
        {
            var snapshot = widget.RenderSnapshot("search-sizing", 1);
            Assert.AreEqual(WidgetSurfaceAxisMode.Content, snapshot.Surface!.HeightMode);
            WidgetRail.Tests.RendererFixtureExporter.WriteCollectionFixture("youtube-empty-search", snapshot);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task EmptySearchTotalIsNotAnInvalidFiniteExtent()
    {
        var provider = new SearchFixture((_, _, _) => new([], null, 0));
        var widget = await CreateSearchFixture(provider);
        try
        {
            await SubmitSearch(widget, "nothing");
            var snapshot = await SearchSettled(widget);
            var source = Find(snapshot.Root, YouTubeVideoWidget.SearchScrollId).IndexedCollection!;
            Assert.AreEqual(0, source.Count);
            Assert.IsFalse(source.Discovery!.HasMore);
            Assert.AreEqual(DiscoveredCollectionStatus.Ready, source.Discovery.Status);
            Assert.IsEmpty(ViewSnapshotValidator.Validate(snapshot));
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task FilteredPagesAndChangingReportedTotalsRemainOpaqueContinuations()
    {
        var provider = new SearchFixture((_, token, _) => token is null
            ? new(Items(0, 2), "page-2", 10000)
            : new(Items(2, 1), null, 1));
        var widget = await CreateSearchFixture(provider);
        try
        {
            await SubmitSearch(widget, "filtered");
            var first = Find((await SearchSettled(widget)).Root, YouTubeVideoWidget.SearchScrollId);
            Assert.IsEmpty(first.Children);
            Assert.AreEqual(2, first.IndexedCollection!.Count);
            Assert.IsTrue(first.IndexedCollection.Discovery!.HasMore);
            Assert.IsNull(first.VirtualCollectionWindow);
            await NextSearchPage(widget);
            var next = Find((await SearchSettled(widget)).Root, YouTubeVideoWidget.SearchScrollId);
            Assert.IsEmpty(next.Children);
            Assert.AreEqual(3, next.IndexedCollection!.Count);
            Assert.IsFalse(next.IndexedCollection.Discovery!.HasMore);
            Assert.AreEqual(first.IndexedCollection.QueryGeneration, next.IndexedCollection.QueryGeneration);
            Assert.IsNotNull(next.IndexedCollection.Discovery, "Exhaustion must preserve discovered semantics and exact occurrence authority.");
            CollectionAssert.AreEqual(new string?[] { null, "page-2" }, provider.Tokens.ToArray());
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task ForwardSearchRetainsAllDiscoveredHistoryBeyondTheOldCursorWindow()
    {
        var provider = new SearchFixture((_, token, _) =>
        {
            var page = token is null ? 0 : int.Parse(token.AsSpan(5));
            return new(Items(page * 12, 12), page == 7 ? null : "page-" + (page + 1), 1000);
        });
        var widget = await CreateSearchFixture(provider);
        try
        {
            await SubmitSearch(widget, "deep");
            for (var page = 0; page < 8; ++page)
            {
                var snapshot = await SearchSettled(widget);
                Assert.IsEmpty(ViewSnapshotValidator.Validate(snapshot));
                var scroll = Find(snapshot.Root, YouTubeVideoWidget.SearchScrollId);
                Assert.IsEmpty(scroll.Children);
                Assert.AreEqual((page + 1) * 12, scroll.IndexedCollection!.Count);
                Assert.IsNull(scroll.CollectionStartIndex);
                Assert.IsNull(scroll.VirtualCollectionWindow);
                using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "search-range");
                using var range = await host.AcquireAsync(YouTubeVideoWidget.SearchScrollId, page * 12 + 11, 1);
                Assert.AreEqual("youtube.result." + ItemId(page * 12 + 11), range.Range.Items[0].Root.Id);
                Assert.AreEqual(page != 7, scroll.IndexedCollection.Discovery!.HasMore);
                if (page != 7) await NextSearchPage(widget);
            }
            Assert.AreEqual(8, provider.Tokens.Count);
            using var history = WidgetTestHost.CreateIndexedCollectionHost(widget, "search-history");
            using var first = await history.AcquireAsync(YouTubeVideoWidget.SearchScrollId, 0, 1);
            Assert.AreEqual("youtube.result." + ItemId(0), first.Range.Items[0].Root.Id);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task SearchContinuationKeepsEstablishedMediaParkedWithoutAnotherPlaybackCommand()
    {
        var provider = new SearchFixture((_, token, _) => token is null
            ? new(Items(0, 2), "page-2", 500) : new(Items(2, 2), null, 2));
        var widget = await CreateSearchFixture(provider);
        try
        {
            await widget.OnActionAsync(new("youtube.link.open", "youtube.link.open"));
            await CommitAsync(widget, "https://youtu.be/" + VideoId);
            var initial = widget.RenderSnapshot("search-media", 1);
            await ObserveAsync(widget, initial.EmbeddedMediaSession!.PendingCommand!, EmbeddedMediaPlaybackState.Ready, 1);
            await widget.OnActionAsync(new("youtube.search.open-route", "youtube.search.open-route"));
            await SubmitSearch(widget, "media");
            var first = await SearchSettled(widget);
            await NextSearchPage(widget);
            var second = await SearchSettled(widget);
            Assert.IsNotNull(second.EmbeddedMediaSession);
            Assert.IsNull(second.EmbeddedMediaSession.PendingCommand);
            Assert.IsNull(TryFind(second.Root, "youtube.viewport"));
            Assert.AreEqual(initial.EmbeddedMediaSession.Id, second.EmbeddedMediaSession.Id);
            Assert.AreEqual(initial.EmbeddedMediaSession.EntryAsset, second.EmbeddedMediaSession.EntryAsset);
            CollectionAssert.AreEqual(initial.EmbeddedMediaSession.Resources.ToArray(), second.EmbeddedMediaSession.Resources.ToArray());
            Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(first.EmbeddedMediaSession),
                System.Text.Json.JsonSerializer.Serialize(second.EmbeddedMediaSession));
            await widget.OnActionAsync(new("youtube.player.return", "youtube.player.return"));
            var player = widget.RenderSnapshot("search-media", 10);
            Assert.IsNotNull(TryFind(player.Root, "youtube.viewport"));
            Assert.IsNull(player.EmbeddedMediaSession!.PendingCommand);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private static string ItemId(int index) => "v" + index.ToString("D10");

    [TestMethod]
    public async Task AppendedSearchKeepsCapturedActionAndExactKeyedReturnThenRetiresOnReplacement()
    {
        var provider = new SearchFixture((_, token, _) => token is null
            ? new(Items(0, 2), "next", 100) : new(Items(1, 2), null, 100));
        var widget = await CreateSearchFixture(provider);
        try
        {
            await SubmitSearch(widget, "captured");
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "captured-search");
            using var prefix = await host.AcquireAsync(YouTubeVideoWidget.SearchScrollId, 1, 1);
            var source = Find(host.CurrentSnapshot.Root, YouTubeVideoWidget.SearchScrollId).IndexedCollection!;
            await host.ContinueAsync(YouTubeVideoWidget.SearchScrollId); host.PublishSnapshot();
            Assert.AreEqual(3, Find(host.CurrentSnapshot.Root, YouTubeVideoWidget.SearchScrollId).IndexedCollection!.Count,
                "The explicit unique-video policy must retain history and omit a repeated video across pages.");
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, prefix.RouteAction("youtube-video-" + ItemId(1), ControllerButton.A));
            ViewSnapshot? selected = null;
            for (var attempt = 0; attempt < 200; ++attempt)
            {
                selected = widget.RenderSnapshot("captured-search", attempt + 50);
                if (selected.EmbeddedMediaSession?.PendingCommand?.MediaKey == ItemId(1)) break;
                await Task.Delay(10);
            }
            Assert.AreEqual(ItemId(1), selected!.EmbeddedMediaSession?.PendingCommand?.MediaKey);
            await widget.OnActionAsync(new("youtube.section.previous", "youtube.root"));
            var returned = widget.RenderSnapshot("captured-search", 500);
            var target = returned.FocusGroupEntryRequest?.IndexedItem;
            Assert.IsNotNull(target);
            Assert.AreEqual(source.QueryGeneration, target.QueryGeneration);
            Assert.AreEqual("youtube-video-" + ItemId(1), target.ItemKey);
            Assert.AreEqual(1, target.Index);
            await SubmitSearch(widget, "replacement"); host.PublishSnapshot();
            Assert.IsNull(prefix.RouteAction("youtube-video-" + ItemId(1), ControllerButton.A));
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }
    private static YouTubeSearchItem[] Items(int start, int count) => Enumerable.Range(start, count)
        .Select(index => new YouTubeSearchItem(ItemId(index), "Video " + index, "Channel", "1:00",
            "https://i.ytimg.com/vi/" + ItemId(index) + "/mqdefault.jpg")).ToArray();

    private static async Task<YouTubeVideoWidget> CreateSearchFixture(SearchFixture provider)
    {
        var widget = WidgetTestHost.Attach(new YouTubeVideoWidget(provider), new WidgetTestHostServicesBuilder().Build());
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        for (var attempt = 0; attempt < 200; ++attempt)
        {
            if (TryFind(widget.RenderSnapshot("search-fixture", 1).Root, "youtube.search.query") is not null) return widget;
            await Task.Delay(10);
        }
        throw new TimeoutException("Search configuration did not settle.");
    }
    private static async Task SubmitSearch(YouTubeVideoWidget widget, string query)
    {
        await widget.OnActionAsync(new WidgetActionEvent("youtube.search.query.commit", "youtube.search.query") { CommittedText = query });
        await widget.OnActionAsync(new("youtube.search.submit", "youtube.search.submit"));
        await NextSearchPage(widget);
    }
    private static async Task NextSearchPage(YouTubeVideoWidget widget)
    {
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "search-demand");
        await host.ContinueAsync(YouTubeVideoWidget.SearchScrollId);
        host.PublishSnapshot();
    }
    private static async Task<ViewSnapshot> SearchSettled(YouTubeVideoWidget widget)
    {
        for (var attempt = 0; attempt < 300; ++attempt)
        {
            var snapshot = widget.RenderSnapshot("search-fixture", 2 + attempt);
            var scroll = TryFind(snapshot.Root, YouTubeVideoWidget.SearchScrollId);
            if (scroll?.IndexedCollection?.Discovery is { Status: not DiscoveredCollectionStatus.Loading }) return snapshot;
            await Task.Delay(10);
        }
        throw new TimeoutException("Search continuation did not settle.");
    }

    private sealed class SearchFixture(Func<string, string?, int, YouTubeSearchPage> search) : IYouTubeApplicationService
    {
        internal List<string?> Tokens { get; } = [];
        public ValueTask<YouTubeConfigurationSummary> GetConfigurationAsync(CancellationToken token) => ValueTask.FromResult(new YouTubeConfigurationSummary(true));
        public ValueTask ConfigureApiKeyAsync(string key, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DeleteApiKeyAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask OpenGoogleCloudConsoleAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask OpenVideoInYouTubeAsync(string id, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<YouTubeSearchPage> SearchAsync(string query, string? pageToken, int pageSize, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Tokens.Add(pageToken); return ValueTask.FromResult(search(query, pageToken, pageSize)); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
