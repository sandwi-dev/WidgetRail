using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    private const string AchievementHubId = "playnite-library.achievement-hub";
    [TestMethod]
    public async Task AchievementHeaderExitsToCaptureSettingsAndReturnsToItsTab()
    {
        var provider = new FakeHost(2); var widget = Create(provider, out TestApplicationService app);
        var game = new PlayniteAchievementGame(provider.ItemFactory(0).SavedId, "Stardew Valley", "Steam", true, 10, 1, 1, null, null);
        app.OverviewHandler = _ => ValueTask.FromResult(new PlayniteAchievementOverview("ready", "profile", "one", DateTimeOffset.UtcNow, [game], []));
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.AchievementsOpen, "navigation"));
        await PrivateField<WidgetResource<PlayniteAchievementOverview>>(widget, "achievementHub").WhenIdleAsync();
        var snapshot = Snapshot(widget, 240000);
        var tab = Nodes(snapshot.Root).Single(node => node.ActionId == PlayniteLibraryActions.AchievementsOpen);
        var capture = Nodes(snapshot.Root).Single(node => node.Text == "Capture settings");
        var settings = Nodes(snapshot.Root).Single(node => node.Id == AchievementHubId + ".settings");
        Assert.IsFalse(Nodes(snapshot.Root).Any(node => node.ActionId == AchievementHubId + ".refresh"));
        var search = Nodes(snapshot.Root).Single(node => node.Id == AchievementHubId + ".search");
        Assert.AreEqual(tab.Id, search.Focus?.Up);
        foreach (var field in new[] { "clear", "filter", "sort" })
            Assert.AreEqual(tab.Id, Nodes(snapshot.Root).Single(n=>n.Id==AchievementHubId+"."+field).Focus?.Up);

        var query = new TextSuggestionQuery(search.Id, search.TextEntrySuggestions!.Key, Guid.NewGuid().ToString("N"), 1, "Val", MaximumValueLength:160);
        Assert.AreEqual(game.Name, (await widget.OnSuggestTextAsync(query, default)).Items.Single().Value);
        Assert.IsTrue((await widget.OnSuggestTextAsync(query with {SourceKey="stale"}, default)).Unavailable);
        Assert.AreEqual(capture.Id, tab.Focus?.Right);
        Assert.AreEqual(tab.Id, capture.Focus?.Left);
        Assert.AreEqual(settings.Id, capture.Focus?.Right);
        Assert.AreEqual(capture.Id, settings.Focus?.Left);
        Assert.AreEqual(0, WidgetTestHost.ValidateWinUiPresentation(snapshot).Count);
        if (Environment.GetEnvironmentVariable("WIDGETRAIL_PLAYNITE_HEADER_FIXTURE") is { Length: > 0 } path)
        {
            // This standalone renderer has no indexed item provider. The full shell covers populated rows.
            await widget.OnActionAsync(new(AchievementHubId+".search", search.Id) { CommittedText="No matching game" });
            RendererFixtureExporter.Write(path, Snapshot(widget,240006),
                RendererFixtureExporter.CompileTheme(Path.Combine(AppContext.BaseDirectory, "styles"), "default.wrss"));
            await widget.OnActionAsync(new(AchievementHubId+".clear", AchievementHubId+".clear"));
        }
        var recent = new PlayniteRecentAchievement(game.Id, game.Name, "first", "First unlock", DateTimeOffset.UtcNow, null);
        app.OverviewHandler = _ => ValueTask.FromResult(new PlayniteAchievementOverview("ready", "profile", "two", DateTimeOffset.UtcNow, [game], [recent]));
        await widget.OnActionAsync(new(PlayniteLibraryActions.AchievementsOpen, "navigation"));
        await PrivateField<WidgetResource<PlayniteAchievementOverview>>(widget, "achievementHub").WhenIdleAsync();
        var populated = Snapshot(widget,240005);
        foreach (var field in new[] { "search", "clear", "filter", "sort" })
            Assert.AreEqual(AchievementHubId+".recent", Nodes(populated.Root).Single(n=>n.Id==AchievementHubId+"."+field).Focus?.Up);
        if (Environment.GetEnvironmentVariable("WIDGETRAIL_PLAYNITE_HEADER_FIXTURE") is { Length: > 0 } expandedPath)
            RendererFixtureExporter.Write(expandedPath+".recent.json", populated,
                RendererFixtureExporter.CompileTheme(Path.Combine(AppContext.BaseDirectory,"styles"),"default.wrss"));
        await WidgetTestHost.DestroyAsync(widget);
    }
    [TestMethod]
    [DataRow("missing", "Add PlayniteAchievements")]
    [DataRow("unsupported", "Update PlayniteAchievements")]
    [DataRow("update-required", "Update WidgetRail for Playnite")]
    [DataRow("unavailable", "Achievements unavailable")]
    public async Task AchievementDestinationExplainsUnavailableProviderWithoutBreakingNavigation(string availability, string title)
    {
        var provider = new FakeHost(2); var widget = Create(provider, out TestApplicationService app);
        app.OverviewHandler = _ => ValueTask.FromResult(PlayniteAchievementOverview.Unavailable with { Availability = availability });
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.AchievementsOpen, "navigation"));
        await PrivateField<WidgetResource<PlayniteAchievementOverview>>(widget, "achievementHub").WhenIdleAsync();
        var snapshot = Snapshot(widget, 240001);
        Assert.IsTrue(Nodes(snapshot.Root).Any(node => node.Text == title));
        Assert.IsTrue(Nodes(snapshot.Root).Any(node => node.ActionId == PlayniteLibraryActions.HomeOpen));
        Assert.IsTrue(Nodes(snapshot.Root).Any(node => node.ActionId == PlayniteLibraryActions.BrowseOpen));
        Assert.AreEqual(0, WidgetTestHost.ValidateWinUiPresentation(snapshot).Count);
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "navigation"));
        Assert.AreEqual(PlayniteLibraryRoute.Browse, PrivateField<WidgetNavigator<PlayniteLibraryRoute>>(widget, "_navigation").Value.Route);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task AchievementOverviewFollowsEventsAndRefreshesOnReturnWithoutPollingWhileAway()
    {
        var provider = new FakeHost(1); var widget = Create(provider, out TestApplicationService app);
        var changes = System.Threading.Channels.Channel.CreateUnbounded<PlayniteEventBatch>();
        var polls = 0; var reads = 0;
        var game = new PlayniteAchievementGame(provider.ItemFactory(0).SavedId, "Game", "Steam", true, 10, 1, 1, null, null);
        var data = new PlayniteAchievementOverview("ready", "profile", "one", DateTimeOffset.UtcNow, [game], []);
        app.OverviewHandler = _ => { Interlocked.Increment(ref reads); return ValueTask.FromResult(data); };
        app.EventsHandler = async (after, token) =>
        {
            if (after < 0) return new("profile", "process", true, 0, []);
            Interlocked.Increment(ref polls);
            return await changes.Reader.ReadAsync(token);
        };
        await Interactive(widget); await Ready(widget, provider); await WaitIndexed(() => polls == 1);
        await widget.OnActionAsync(new(PlayniteLibraryActions.AchievementsOpen, "navigation"));
        var resource = PrivateField<WidgetResource<PlayniteAchievementOverview>>(widget, "achievementHub");
        await resource.WhenIdleAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "achievement-events");
        host.PublishSnapshot();
        using var before = await host.AcquireAsync(AchievementHubId + ".games", 0, 1);
        data = data with { Revision = "two", Games = [game with { Unlocked = 2 }] };
        await changes.Writer.WriteAsync(new("profile", "process", false, 1, [new(1, "achievements-changed", game.Id, null, 1)]));
        await WaitIndexed(() => resource.Snapshot.Value?.Revision == "two");
        host.PublishSnapshot();
        using var after = await host.AcquireAsync(AchievementHubId + ".games", 0, 1);
        Assert.IsTrue(Nodes(after.Range.Items[0].Root).Any(node => node.Text?.Contains("2 / 10 unlocked") == true));
        await widget.OnActionAsync(new(PlayniteLibraryActions.BrowseOpen, "navigation"));
        var priorReads = reads; var priorPolls = polls;
        data = data with { Revision = "three", Games = [game with { Unlocked = 3 }] };
        await changes.Writer.WriteAsync(new("profile", "process", false, 2, [new(2, "achievements-changed", game.Id, null, 2)]));
        await WaitIndexed(() => polls > priorPolls);
        Assert.AreEqual(priorReads, reads, "An inactive destination must not fetch the overview for every event.");
        await widget.OnActionAsync(new(PlayniteLibraryActions.AchievementsOpen, "navigation"));
        await resource.WhenIdleAsync();
        Assert.AreEqual("three", resource.Snapshot.Value?.Revision, "Returning must not reuse a snapshot invalidated while away.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task AchievementEventSupersedesAnOlderInFlightOverview()
    {
        var provider = new FakeHost(1); var widget = Create(provider, out TestApplicationService app);
        var changes = System.Threading.Channels.Channel.CreateUnbounded<PlayniteEventBatch>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var polls = 0; var delayNext = 0;
        var game = new PlayniteAchievementGame(provider.ItemFactory(0).SavedId, "Game", "Steam", true, 10, 1, 1, null, null);
        var data = new PlayniteAchievementOverview("ready", "profile", "one", DateTimeOffset.UtcNow, [game], []);
        app.OverviewHandler = async _ =>
        {
            var captured = data;
            if (Interlocked.Exchange(ref delayNext, 0) == 1) { started.SetResult(); await release.Task; }
            return captured; // Deliberately uncooperative old provider read.
        };
        app.EventsHandler = async (after, token) =>
        {
            if (after < 0) return new("profile", "process", true, 0, []);
            Interlocked.Increment(ref polls); return await changes.Reader.ReadAsync(token);
        };
        await Interactive(widget); await Ready(widget, provider); await WaitIndexed(() => polls == 1);
        await widget.OnActionAsync(new(PlayniteLibraryActions.AchievementsOpen, "navigation"));
        var resource = PrivateField<WidgetResource<PlayniteAchievementOverview>>(widget, "achievementHub");
        await resource.WhenIdleAsync();
        delayNext = 1; data = data with { Revision = "two" };
        await changes.Writer.WriteAsync(new("profile", "process", false, 1, [new(1, "achievements-changed", game.Id, null, 1)]));
        await Bounded(started.Task, "old overview started");
        data = data with { Revision = "three", Games = [game with { Unlocked = 3 }] };
        await changes.Writer.WriteAsync(new("profile", "process", false, 2, [new(2, "achievements-changed", game.Id, null, 2)]));
        await WaitIndexed(() => polls >= 3); release.SetResult();
        await resource.WhenIdleAsync();
        Assert.AreEqual("three", resource.Snapshot.Value?.Revision);
        Assert.AreEqual(3, resource.Snapshot.Value?.Unlocked);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task AchievementDestinationLoadsOnceAndFiltersDemandedRows()
    {
        var provider = new FakeHost(3); var widget = Create(provider, out TestApplicationService app); var reads = 0;
        var games = Enumerable.Range(0, 3).Select(index => new PlayniteAchievementGame(provider.ItemFactory(index).SavedId,
            "Game " + index, "Steam", true, 10, index * 5, index == 0 ? 1 : 0, null, null)).ToArray();
        var data = new PlayniteAchievementOverview("ready", "profile", "revision", DateTimeOffset.UtcNow, games, []);
        app.OverviewHandler = _ => { reads++; return ValueTask.FromResult(data); };
        await Interactive(widget); await Ready(widget, provider);
        Assert.AreEqual(0, reads, "Home must not eagerly scan achievement data.");
        await widget.OnActionAsync(new(PlayniteLibraryActions.AchievementsOpen, "navigation"));
        await PrivateField<WidgetResource<PlayniteAchievementOverview>>(widget, "achievementHub").WhenIdleAsync();
        var snapshot = Snapshot(widget, 240002);
        Assert.AreEqual(1, reads);
        Assert.AreEqual(3, Nodes(snapshot.Root).Single(node => node.Id == AchievementHubId + ".games").IndexedCollection!.Count);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "achievement-hub");
        using var old = await host.AcquireAsync(AchievementHubId + ".games", 0, 3);
        Assert.AreEqual(3, old.Range.Items.Count);
        await widget.OnActionAsync(new(AchievementHubId + ".filter.completed", "filter"));
        snapshot = host.PublishSnapshot();
        Assert.AreEqual(1, Nodes(snapshot.Root).Single(node => node.Id == AchievementHubId + ".games").IndexedCollection!.Count);
        Assert.AreEqual(1, reads, "Filtering must use the loaded snapshot rather than re-reading provider data.");
        Assert.AreNotEqual(WidgetOperationAdmission.Enqueued, old.RouteAction(old.Range.Items[0].Key, ControllerButton.A));
        using var current = await host.AcquireAsync(AchievementHubId + ".games", 0, 1);
        Assert.AreEqual(games[2].Id, current.Range.Items[0].Key);
        Assert.AreEqual(0, WidgetTestHost.ValidateWinUiPresentation(snapshot).Count);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    [DataRow("reorder")]
    [DataRow("remove")]
    [DataRow("profile")]
    public async Task AchievementReturnTracksDomainIdentityAcrossOverviewChanges(string change)
    {
        var provider = new FakeHost(2); var widget = Create(provider, out TestApplicationService app);
        var games = Enumerable.Range(0, 2).Select(index => new PlayniteAchievementGame(provider.ItemFactory(index).SavedId,
            "Game " + index, "Steam", true, 10, 4, 1, null, null)).ToArray();
        var data = new PlayniteAchievementOverview("ready", "profile", "one", DateTimeOffset.UtcNow, games, []);
        app.OverviewHandler = _ => ValueTask.FromResult(data);
        app.AchievementsHandler = (_, _) => ValueTask.FromResult(new PlayniteAchievements(true, 1, 0,
            [new("Target", "", false, null, null, null, false) { Id = "exact", Progress = new(2, 10) }])
            { ConnectionId = "profile", Provider = PlayniteAchievementProvider.PlayniteAchievements });
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.AchievementsOpen, "navigation"));
        var resource = PrivateField<WidgetResource<PlayniteAchievementOverview>>(widget, "achievementHub");
        await resource.WhenIdleAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "achievement-return-" + change);
        using var row = await host.AcquireAsync(AchievementHubId + ".games", 1, 1);
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, row.RouteAction(row.Range.Items[0].Key, ControllerButton.A));
        await WaitIndexed(() => widget.RenderState.Value.DetailsItem is not null);
        data = change switch
        {
            "reorder" => data with { Revision = "two", Games = [games[0], games[1] with { LastUnlockedAt = DateTimeOffset.UtcNow }] },
            "remove" => data with { Revision = "two", Games = [games[0]] },
            _ => data with { Revision = "two", ConnectionId = "another-profile" },
        };
        await resource.Refresh().Completion;
        await Bounded(widget.WhenDetailsIdleAsync(), "achievement details");
        host.PublishSnapshot();
        var query = PrivateField<PlayniteAchievementBrowse>(widget, "achievementBrowse");
        var track = widget.GetType().GetMethod("PinAchievementAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (ValueTask)track.Invoke(widget, [query, query.All.Single(), CancellationToken.None])!;
        Assert.AreEqual(change == "reorder", widget.RenderState.Value.Progress.Target is not null,
            "Tracking follows the current game/profile authority, not the old modal's visible text.");
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
        var snapshot = host.PublishSnapshot();
        Assert.AreEqual(0, WidgetTestHost.ValidateWinUiPresentation(snapshot).Count);
        if (change == "reorder")
        {
            Assert.AreEqual(games[1].Id, snapshot.FocusGroupEntryRequest?.IndexedItem?.ItemKey);
            Assert.AreEqual(0, snapshot.FocusGroupEntryRequest?.IndexedItem?.Index);
        }
        else Assert.AreEqual(AchievementHubId + ".search.group", snapshot.FocusGroupEntryRequest?.GroupId);
        await WidgetTestHost.DestroyAsync(widget);
    }

    [TestMethod]
    public async Task AchievementGameOpensExistingDetailsAndReturnsToItsExactRow()
    {
        var provider = new FakeHost(2); var widget = Create(provider, out TestApplicationService app);
        var games = Enumerable.Range(0, 2).Select(index => new PlayniteAchievementGame(provider.ItemFactory(index).SavedId,
            "Game " + index, "Steam", true, 10, 4, 1, null, null)).ToArray();
        app.OverviewHandler = _ => ValueTask.FromResult(new PlayniteAchievementOverview("ready", "profile", "revision", DateTimeOffset.UtcNow, games, []));
        app.AchievementsHandler = (_, _) => ValueTask.FromResult(new PlayniteAchievements(true, 0, 0, []));
        await Interactive(widget); await Ready(widget, provider);
        await widget.OnActionAsync(new(PlayniteLibraryActions.AchievementsOpen, "navigation"));
        await PrivateField<WidgetResource<PlayniteAchievementOverview>>(widget, "achievementHub").WhenIdleAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "achievement-hub-open");
        using var row = await host.AcquireAsync(AchievementHubId + ".games", 1, 1);
        Assert.AreEqual(WidgetOperationAdmission.Enqueued, row.RouteAction(row.Range.Items[0].Key, ControllerButton.A));
        await WaitIndexed(() => widget.RenderState.Value.DetailsItem is not null);
        Assert.AreEqual(games[1].Id, widget.RenderState.Value.DetailsItem!.Value.SavedId);
        Assert.AreEqual(PlayniteDetailsTab.Achievements, widget.RenderState.Value.DetailsExtras.Tab);
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
        var snapshot = host.PublishSnapshot();
        Assert.AreEqual(AchievementHubId + ".games", snapshot.FocusGroupEntryRequest?.GroupId);
        Assert.AreEqual(0, WidgetTestHost.ValidateWinUiPresentation(snapshot).Count);
        await WidgetTestHost.DestroyAsync(widget);
    }
}
