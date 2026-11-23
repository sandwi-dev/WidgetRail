using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.Samples.PlayniteLibrary;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod]
    public async Task ShelfOptionsHideRestoreAndPersistAcrossWorkerRestart()
    {
        var host = new FakeHost(2);
        var widget = Create(host, out var app);
        var changes = Channel.CreateUnbounded<PlayniteEventBatch>();
        var game = Item(0).SavedId;
        var batch = new PlayniteEventBatch("profile", "instance", true, 1, [], [game]);
        var card = "playnite.playing." + game + ".resume";
        async Task Open()
        {
            app.EventsHandler = async (_, token) => await changes.Reader.ReadAsync(token);
            await Interactive(widget);
            await Ready(widget, host);
            await changes.Writer.WriteAsync(batch);
            await WaitFor(() => widget.RenderState.Value.RunningGames?.Contains(game) == true, "running snapshot");
        }
        ViewNode? Find(string id) => Nodes(Snapshot(widget, 5).Root).FirstOrDefault(n => n.Id == id);
        const string prefs = "playnite.card-preferences";
        async Task Set(string kind, bool show)
        {
            var row = Find(prefs + ".row." + kind)!;
            await widget.OnActionAsync(new(prefs + (show ? ".on." : ".off.") + kind, row.Id));
        }
        await Open();
        var tile = Find(card)!;
        Assert.AreEqual(ControllerButton.X, tile.ContextMenuButton);
        await widget.OnActionAsync(new(tile.ContextActions[0].ActionId, tile.Id));
        Assert.IsNull(Find(card));
        await widget.OnActionAsync(new(WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget.ManageCards, "playnite-library.library.menu"));
        foreach (var kind in new[] { "Help", "News", "Sessions", "Achievements", "Capture" }) await Set(kind, false);
        await widget.OnActionAsync(new(prefs + ".close", prefs + ".back"));
        Assert.IsNull(Find("playnite-library.home.playing.sections"));
        await Background(widget);
        await WidgetTestHost.DestroyAsync(widget);
        widget = Create(host, out app);
        await Open();
        Assert.IsNull(Find("playnite-library.home.playing.sections"));
        await widget.OnActionAsync(new(WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget.ManageCards, "playnite-library.library.menu"));
        await Set("Details", true);
        await widget.OnActionAsync(new(prefs + ".close", prefs + ".back"));
        Assert.IsNotNull(Find(card));
        await Background(widget);
    }

    [TestMethod]
    public void ShelfKeepsOneNewestUnlockAndSkipsLockedSecrets()
    {
        var now = DateTimeOffset.UtcNow;
        PlayniteAchievement Earned(int days) => new($"Unlock {days}", "", true, now.AddDays(-days), null, null, false)
            { Icon = $@"C:\fixture\badge{days}.png" };
        var secret = Earned(0) with { Hidden = true, Unlocked = false, Progress = new(99, 100) };
        var visible = Earned(5) with { Unlocked = false, Progress = new(3, 5), Percent = 60 };
        var data = new PlayniteAchievements(true, 6, 4, [Earned(4), secret, Earned(3), Earned(1), visible, Earned(2)]);
        var awards = PlayingAwards.From("game", data);
        CollectionAssert.AreEqual(new[] { "Unlock 1" }, awards.Badges.Where(b => b.Latest).Select(b => b.Award.Name).ToArray());
        Assert.AreEqual(2, awards.Badges.Select(b => b.LocalArtwork).Distinct().Count());
        var locked = PlayingAwards.From("game", data with { Unlocked = 0, Items = [secret, visible] });
        Assert.AreEqual(visible, locked.Badges.Single().Award);
    }

    [TestMethod]
    public void TrackedLatestUnlockAppearsOnceEvenWithOlderUnlocks()
    {
        var latest = new PlayniteAchievement("Latest", "Finish the run", true, DateTimeOffset.UtcNow,
            10, null, false) { Id = "latest" };
        var older = latest with { Id = "older", UnlockedAt = latest.UnlockedAt!.Value.AddDays(-1) };
        var data = new PlayniteAchievements(true, 2, 2, [older, latest]) { ConnectionId = "profile" };
        var tracked = new TrackedAchievement("game", "profile", data.Provider, "latest");
        var badge = PlayingAwards.From("game", data, tracked).Badges.Single();
        Assert.AreEqual("Tracked", badge.Group);
        Assert.AreSame(latest, badge.Award);
        Assert.IsTrue(badge.Latest);
    }

    [TestMethod]
    public void MostEarnedUsesHighestKnownLockedPercentAndShowsTheTrackedChoiceOnce()
    {
        var items = Enumerable.Range(1, 8).Select(i => new PlayniteAchievement("Award " + i, "Task", false, null,
            i * 10, 50, false) { Id = "id" + i }).ToArray();
        var secret = items[0] with { Id = "secret", Percent = 99, Hidden = true };
        var unknown = items[0] with { Id = "unknown", Percent = null };
        var data = new PlayniteAchievements(true, 10, 0, [.. items, secret, unknown]) { ConnectionId = "profile" };
        var choice = new TrackedAchievement("game", "profile", data.Provider, items[6].Id!);
        var awards = PlayingAwards.From("game", data, choice);
        CollectionAssert.AreEqual(new[] { "id7", "id8", "id6", "id5", "id4" }, awards.Badges.Select(b => b.Award.Id).ToArray());
        Assert.AreEqual("Tracked", awards.Badges[0].Group);
        Assert.IsTrue(awards.Badges.Skip(1).All(b => b.Group == "Next activity"));
    }

    [TestMethod]
    public void StoryExcerptSkipsRepeatedHeadingsAndPreservesVersionNumbers()
    {
        var story = new SteamAnnouncement("1", "Update 1.2", DateTimeOffset.UtcNow,
            "Update 1.2\nPatch Notes:\n- Version 1.2 adds cloud saves. This sentence belongs in the reader.", null);
        Assert.AreEqual("Version 1.2 adds cloud saves.", story.Excerpt);
        Assert.AreEqual("", new SteamAnnouncement("2", "A", story.Published, "", null).Excerpt);
    }

    [TestMethod, Timeout(30000)]
    public async Task PlayingAchievementsUseGameTotalsWithoutARecentUnlockAndRefreshOnProviderEvents()
    {
        var host = new FakeHost(2);
        var widget = Create(host, out var app);
        var changes = Channel.CreateUnbounded<PlayniteEventBatch>();
        app.EventsHandler = async (_, token) => await changes.Reader.ReadAsync(token);
        var id = Item(0).SavedId;
        var secret = new PlayniteAchievement("Secret ending", "", false, null, null, null, true)
            { Icon = @"C:\fixture\secret.png", Progress = new(9, 10) };
        var data = new PlayniteAchievements(true, 1, 0, [secret]);
        var reads = 0;
        app.AchievementsHandler = (_, _) => { Interlocked.Increment(ref reads); return ValueTask.FromResult(data); };
        var key = "playnite.playing." + id + ".achievement";
        bool Has(string text) => Nodes(Snapshot(widget, 4).Root).Any(n => n.Text?.TrimStart() == text);
        var batch = new PlayniteEventBatch("profile", "instance", true, 1, [], [id]);
        await Interactive(widget);
        await Ready(widget, host);
        await changes.Writer.WriteAsync(batch);
        await WaitFor(() => Has("0 of 1 unlocked") && Has("No unlocks yet"), "zero-unlock shelf card");
        if (Environment.GetEnvironmentVariable("WIDGETRAIL_PLAYNITE_AWARD_FIXTURE") is { Length: > 0 } path)
        {
            var snapshot = Snapshot(widget, 4);
            RendererFixtureExporter.Write(path, snapshot,
                RendererFixtureExporter.CompileTheme(Path.Combine(AppContext.BaseDirectory, "styles"), "default.wrss"));
        }
        Assert.IsFalse(Has(secret.Name));
        Assert.AreEqual(1, reads);
        data = data with { Unlocked = 1, Items = [secret with { Unlocked = true, UnlockedAt = DateTimeOffset.UtcNow }] };
        await changes.Writer.WriteAsync(batch with { ResetRequired = false, NextSequence = 2,
            Changes = [new(2, "achievement-unlocked", id, "ending", 1000)] });
        await WaitFor(() => Has("1 of 1 unlocked") && Has(secret.Name), "new unlock shown without reopening Home");
        key = Nodes(Snapshot(widget, 5).Root).Single(n => n.ActionId?.StartsWith(key + ".", StringComparison.Ordinal) == true).Id;
        var handle = Nodes(Snapshot(widget, 5).Root).Single(node => node.Id == key + ".art").ArtworkHandle!;
        Assert.IsInstanceOfType<WidgetFileArtwork>(await widget.OnResolveArtworkSourceAsync(new(handle)));
        await widget.OnActionAsync(new(key, key));
        Assert.IsNotNull(widget.RenderState.Value.DetailsItem);
        Assert.IsInstanceOfType<WidgetFileArtwork>(await widget.OnResolveArtworkSourceAsync(new(handle)),
            "The retained Home background still owns its declared artwork while Game Details is open.");
        await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "playnite-library.details.back"));
        await changes.Writer.WriteAsync(batch with { NextSequence = 3, RunningGames = [] });
        await WaitFor(() => !Nodes(Snapshot(widget, 6).Root).Any(n => n.Id == key), "game exit removes card");
        Assert.IsNull(await widget.OnResolveArtworkSourceAsync(new(handle)));
        await Background(widget);
    }

    [TestMethod, Timeout(30000)]
    public async Task OpeningShelfCanFocusBeforeDetailsAndReusesSameGamesReadOnReopen()
    {
        var host = new FakeHost(2);
        var widget = Create(host, out var app);
        var feed = Channel.CreateUnbounded<PlayniteEventBatch>();
        app.EventsHandler = async (_, token) => await feed.Reader.ReadAsync(token);
        var details = new TaskCompletionSource<PlayniteGameDetails?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var news = new TaskCompletionSource<SteamAnnouncements>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.NewsHandler = (_, token) => { newsStarted.TrySetResult(); return new(news.Task.WaitAsync(token)); };
        var reads = 0;
        app.DetailsHandler = (_, token) => { Interlocked.Increment(ref reads); return new(details.Task.WaitAsync(token)); };
        var game = Item(0);
        var batch = new PlayniteEventBatch("profile", "instance", true, 7, [], [game.SavedId]);
        var entry = "playnite.playing." + game.SavedId + ".resume";
        await Interactive(widget);
        await Ready(widget, host);
        await feed.Writer.WriteAsync(batch);
        await WaitFor(() => Snapshot(widget, 1).FocusGroupEntryRequest?.TargetId == entry, "entry while metadata is pending");
        Assert.IsTrue(Nodes(Snapshot(widget, 2).Root).Any(n => n.Id == entry));
        Assert.IsTrue(Nodes(Snapshot(widget, 2).Root).Any(n => n.Id == entry + ".icon"));
        Assert.IsFalse(Nodes(Snapshot(widget, 2).Root).Any(n => n.Id == entry + ".metadata"));
        var request = Snapshot(widget, 2).FocusGroupEntryRequest;
        var artwork = new WidgetAppLibraryArtworkSet([
            new(WidgetAppLibraryArtworkRole.Tile, "cover", "1", WidgetAppLibraryArtworkFallback.Game),
            new(WidgetAppLibraryArtworkRole.Hero, "wallpaper", "1", WidgetAppLibraryArtworkFallback.Game)]);
        details.SetResult(new(game with { Presentation = game.Presentation with { Artwork = artwork } },
            new(game.SavedId, "Test", "Steam", true, false, false, null, [], ["Adventure"], [], 0, null)
            { SteamAppId = 123, Developers = ["Team Cherry"], ReleaseDate = "2025-09-04" }));
        await Bounded(newsStarted.Task, "details available before remote news");
        var profile = Nodes(Snapshot(widget, 2).Root).Single(n => n.Id == entry);
        Assert.AreEqual("wallpaper", profile.FocusBackgroundArtworkHandle);
        var portrait = Nodes(profile).Single(n => n.Id == entry + ".art");
        Assert.AreEqual("cover", portrait.ArtworkHandle);
        Assert.AreEqual(ImageFit.Contain, portrait.ImageFit);
        Assert.IsTrue(Nodes(profile).Any(n => n.Text == "Team Cherry"));
        Assert.IsTrue(Nodes(profile).Any(n => n.Id == entry + ".metadata" && n.Text == "Adventure · 2025"));
        Assert.IsTrue(Nodes(profile).Any(n => n.Id == entry + ".title" && n.Text == "Resume playing"));
        var cards = Nodes(Snapshot(widget, 2).Root).Single(n => n.Id.EndsWith(game.SavedId + ".cards")).Children.Select(c => c.Id).ToArray();
        news.SetResult(new(123, [new("1", "Patch notes", DateTimeOffset.UtcNow, "", null)]));
        await WaitFor(() => Nodes(Snapshot(widget, 2).Root).Any(n => n.Id == entry.Replace(".resume", ".news")), "append news card");
        var updated = Snapshot(widget, 2);
        CollectionAssert.AreEqual(cards, Nodes(updated.Root).Single(n => n.Id.EndsWith(game.SavedId + ".cards"))
            .Children.Where(c => c.Id != entry.Replace(".resume", ".news")).Select(c => c.Id).ToArray());
        var ordered = Nodes(updated.Root).Single(n => n.Id.EndsWith(game.SavedId + ".cards")).Children;
        Assert.IsTrue(ordered.TakeWhile(c => c.Id != entry.Replace(".resume", ".news")).All(c => !c.Id.EndsWith(".help") && !c.Id.EndsWith(".sessions")));
        Assert.AreEqual(request, updated.FocusGroupEntryRequest);
        await widget.OnActionAsync(new(entry, entry));
        CollectionAssert.AreEqual(new[] { game.SavedId }, app.ReturnedGames);
        Assert.IsNull(widget.RenderState.Value.DetailsItem);
        await Background(widget);
        Assert.IsTrue(widget.RenderState.Value.RunningGames!.Contains(game.SavedId));
        await Interactive(widget);
        await feed.Writer.WriteAsync(batch);
        await WaitFor(() => Snapshot(widget, 3).FocusGroupEntryRequest?.TargetId == entry, "warm shelf entry");
        Assert.AreEqual(1, reads);
        await feed.Writer.WriteAsync(batch with { RunningGames = [] });
        await WaitFor(() => widget.RenderState.Value.RunningGames?.Count == 0, "exit reconciliation");
        Assert.IsFalse(Nodes(Snapshot(widget, 4).Root).Any(n => n.Id == entry));
        await Background(widget);
    }

    [TestMethod, Timeout(30000)]
    public async Task PlayingShelfTracksTheSelectedRunningGameAndRetiresClosedActions()
    {
        var host = new FakeHost(3);
        var widget = Create(host, out var app);
        var changes = Channel.CreateUnbounded<PlayniteEventBatch>();
        app.EventsHandler = async (_, token) => await changes.Reader.ReadAsync(token);
        var first = Item(1).SavedId;
        var second = Item(0).SavedId;
        long sequence = 0;
        const string prefix = "playnite.playing.";
        var processKeys = new Dictionary<string, string> { [first] = new('a', 64), [second] = new('b', 64) };
        async Task Publish(params string[] games) => await changes.Writer.WriteAsync(new("profile", "process", true, ++sequence, [], games,
            RunningProcesses: games.ToDictionary(game => game, game => processKeys[game])));
        bool Has(string action) => Nodes(Snapshot(widget, 30).Root).Any(n => n.ActionId == action);

        await Interactive(widget); await Ready(widget, host);
        Assert.IsFalse(Has(prefix + first + ".resume"));
        await Publish(first);
        await WaitFor(() => Has(prefix + first + ".resume"), "first running game");
        await widget.OnActionAsync(new(prefix + first + ".resume", prefix + first + ".resume"));
        CollectionAssert.AreEqual(new[] { first }, app.ReturnedGames);
        Assert.IsEmpty(host.Launches);
        Assert.AreEqual(processKeys[first], Nodes(Snapshot(widget, 30).Root).Single(n => n.Id == "playnite-library.cinematic").ForegroundBackgroundProcessKey);
        var home = Nodes(Snapshot(widget, 30).Root).ToArray();
        Assert.IsTrue(home.Single(n => n.Id == "playnite-library.home.actions").StyleClasses.Contains("playing-foreground-header"));
        Assert.AreEqual(home.Single(n => n.Kind == ViewNodeKind.Button && n.ActionId == PlayniteLibraryActions.HomeOpen).Id,
            home.Single(n => n.ActionId == prefix + first + ".resume").Focus?.Up);
        Assert.IsTrue(home.SelectMany(n => n.Shortcuts).Any(s => s.Button == ControllerButton.RightBumper && s.ActionId == PlayniteLibraryActions.BrowseOpen));
        Assert.IsFalse(Has(prefix + first + ".news"), "An unmapped game has no Steam feed.");
        await Publish(second, first);
        await WaitFor(() => Nodes(Snapshot(widget, 31).Root).Any(n => n.Id == prefix + first + ".games"), "running game chooser");
        await widget.OnActionAsync(new(prefix + second + ".switch", prefix + first + ".games"));
        Assert.IsTrue(Has(prefix + second + ".resume"));
        Assert.AreEqual(processKeys[second], Nodes(Snapshot(widget, 30).Root).Single(n => n.ActionId == prefix + second + ".resume").ForegroundBackgroundProcessKey);
        await Publish(first);
        await WaitFor(() => Has(prefix + first + ".resume"), "restore remaining game");
        await widget.OnActionAsync(new(prefix + second + ".resume", prefix + second + ".resume"));
        CollectionAssert.AreEqual(new[] { first }, app.ReturnedGames);
        Assert.IsEmpty(host.Launches);
        await Publish();
        await WaitFor(() => !Has(prefix + first + ".resume"), "remove shelf after exit");
        await Background(widget);
    }
}
