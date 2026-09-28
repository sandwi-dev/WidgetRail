using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.YouTubeWidget.Tests;

public sealed partial class YouTubeWidgetTests
{
    [TestMethod]
    public async Task EmptySearchTotalIsNotAnInvalidFiniteExtent()
    {
        var provider = new SearchFixture((_, _, _) => new([], null, 0));
        var widget = await CreateSearchFixture(provider);
        try
        {
            await SubmitSearch(widget, "nothing");
            var snapshot = await SearchSettled(widget);
            Assert.IsNotNull(TryFind(snapshot.Root, "youtube.search.no-results"));
            Assert.IsNull(TryFind(snapshot.Root, "youtube.search.error"));
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
            Assert.HasCount(2, first.Children);
            Assert.IsTrue(first.VirtualCollectionWindow!.HasAfter);
            Assert.IsNull(first.VirtualCollectionWindow.FirstItemIndex);
            Assert.IsNull(first.VirtualCollectionWindow.TotalItemCount);
            await NextSearchPage(widget);
            var next = Find((await SearchSettled(widget)).Root, YouTubeVideoWidget.SearchScrollId);
            Assert.HasCount(3, next.Children);
            Assert.IsFalse(next.VirtualCollectionWindow!.HasAfter);
            Assert.IsNull(next.VirtualCollectionWindow.TotalItemCount);
            Assert.IsNull(next.IndexedCollection, "An exhausted retained cursor must not silently become a finite indexed source.");
            CollectionAssert.AreEqual(new string?[] { null, "page-2" }, provider.Tokens.ToArray());
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task ForwardSearchSurvivesEvictionWithoutReusingRetainedCountAsAbsoluteIndex()
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
                Assert.AreEqual(Math.Min(48, (page + 1) * 12), scroll.Children.Count);
                Assert.AreEqual(Math.Max(0, (page + 1) * 12 - 48), scroll.CollectionStartIndex);
                Assert.IsNull(scroll.VirtualCollectionWindow!.FirstItemIndex);
                Assert.IsNull(scroll.VirtualCollectionWindow.TotalItemCount);
                Assert.IsNotNull(TryFind(scroll, "youtube.result." + ItemId(page * 12 + 11)));
                Assert.AreEqual(page != 7, scroll.VirtualCollectionWindow.HasAfter);
                if (page != 7) await NextSearchPage(widget);
            }
            Assert.AreEqual(8, provider.Tokens.Count);
            Assert.IsNull(TryFind(widget.RenderSnapshot("search-deep", 100).Root, "youtube.result." + ItemId(0)));
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
    }
    private static async Task NextSearchPage(YouTubeVideoWidget widget) => await widget.OnActionAsync(
        new("youtube.search.cursor.after", YouTubeVideoWidget.SearchScrollId));
    private static async Task<ViewSnapshot> SearchSettled(YouTubeVideoWidget widget)
    {
        for (var attempt = 0; attempt < 300; ++attempt)
        {
            var snapshot = widget.RenderSnapshot("search-fixture", 2 + attempt);
            var scroll = TryFind(snapshot.Root, YouTubeVideoWidget.SearchScrollId);
            if (scroll is not null && scroll.CollectionLoading is null or CollectionLoadingState.Idle ||
                TryFind(snapshot.Root, "youtube.search.no-results") is not null || TryFind(snapshot.Root, "youtube.search.error") is not null) return snapshot;
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
