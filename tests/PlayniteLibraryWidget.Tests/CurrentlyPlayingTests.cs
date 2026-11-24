using System.Text.Json;
using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.Samples.PlayniteLibrary;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod, Timeout(30000)]
    public async Task DiscordStartKeepsCardUntilProviderRespondsAndLetsUserRetry()
    {
        var providerEvents = Channel.CreateUnbounded<WidgetCapabilityAcknowledgement>();
        var gameEvents = Channel.CreateUnbounded<PlayniteEventBatch>();
        var reply = new WidgetSummaryValue(null);
        var host = new FakeHost(1) { ConfigureServices = builder => builder
            .WithEventStream(WidgetSummaryCapabilities.ChangedFrom("widgetrail.samples.discord-controls"),
                token => providerEvents.Reader.ReadAllAsync(token))
            .WithHandler(WidgetSummaryCapabilities.ReadFrom("widgetrail.samples.discord-controls"),
                (_, _) => ValueTask.FromResult(reply)) };
        var widget = Create(host, out var app);
        var game = Item(0).SavedId;
        var id = "playnite.playing." + game + ".discord";
        var sequence = 50;
        ViewNode Card() => Nodes(Snapshot(widget, ++sequence).Root).Single(n => n.Id == id);
        app.EventsHandler = async (_, token) => await gameEvents.Reader.ReadAsync(token);
        await Interactive(widget);
        try
        {
            await Ready(widget, host);
            gameEvents.Writer.TryWrite(new("profile", "instance", true, 1, [], [game]));
            await WaitFor(() => widget.RenderState.Value.DiscordSummaryChecked, "stopped Discord");
            Assert.AreEqual("Start Discord", Card().AccessibilityLabel);
            var start = Card().Intent!;
            Assert.IsTrue(start.ReportsResult);
            foreach (var status in new[] { WidgetIntentStatus.Cancelled, WidgetIntentStatus.Failed, WidgetIntentStatus.Accepted })
            {
                Assert.IsNotNull(await widget.OnPrepareIntentAsync(start.PreparationKey!, start));
                var pending = Card();
                Assert.AreEqual("Connecting to Discord", pending.AccessibilityLabel);
                Assert.IsTrue(start.Matches(pending.Intent));
                await widget.OnIntentCompletedAsync(new(Guid.NewGuid().ToString("N"), start with { PreparationKey = null }, status));
                Assert.AreEqual(status == WidgetIntentStatus.Accepted ? "Connecting to Discord" : "Retry Discord connection",
                    Card().AccessibilityLabel);
            }
            reply = new(null) { ProviderRunning = true };
            providerEvents.Writer.TryWrite(new(true));
            await WaitFor(() => widget.RenderState.Value.DiscordProviderRunning, "worker started");
            Assert.AreEqual("Connecting to Discord", Card().AccessibilityLabel);
            reply = new(JsonSerializer.SerializeToElement(new { Session = Guid.NewGuid().ToString("N"), Mode = "friends",
                Title = "Friends active", Count = 0, Truncated = false, People = Array.Empty<object>() }), new string('f', 32))
                { ProviderRunning = true };
            providerEvents.Writer.TryWrite(new(true));
            await WaitFor(() => widget.RenderState.Value.DiscordSummary?.Mode == "friends", "activity ready");
            Assert.AreEqual("Discord, Friends active", Card().AccessibilityLabel);
            Assert.IsNull(Card().Intent);
            reply = new(JsonSerializer.SerializeToElement(new { Session = Guid.NewGuid().ToString("N"), Mode = "unavailable",
                Title = "Discord unavailable", Count = 0, Truncated = false, ConnectionFailed = true, People = Array.Empty<object>() }),
                new string('e', 32)) { ProviderRunning = true };
            providerEvents.Writer.TryWrite(new(true));
            await WaitFor(() => widget.RenderState.Value.DiscordSummary?.ConnectionFailed == true, "provider failure");
            Assert.AreEqual("Retry Discord connection", Card().AccessibilityLabel);
            var retry = Card().Intent!;
            Assert.IsNotNull(await widget.OnPrepareIntentAsync(retry.PreparationKey!, retry));
            Assert.AreEqual("Connecting to Discord", Card().AccessibilityLabel);
            await widget.OnIntentCompletedAsync(new(Guid.NewGuid().ToString("N"), retry, WidgetIntentStatus.Rejected));
            Assert.AreEqual("Retry Discord connection", Card().AccessibilityLabel);
        }
        finally { await Background(widget); }
    }

    [TestMethod, Timeout(30000)]
    public async Task DiscordShelfResizesInPlaceAndClosesPanelWhenAuthorityChanges()
    {
        var events = Channel.CreateUnbounded<WidgetCapabilityAcknowledgement>();
        var gameEvents = Channel.CreateUnbounded<PlayniteEventBatch>();
        var session = Guid.NewGuid().ToString("N");
        var count = 2;
        var everyAvatar = false;
        var mode = "friends";
        var names = new[] { "Alex", "Maya", "Jules", "Nora", "Sam", "Ellis" };
        var statuses = new[] { "Speaking", "Muted", "Listening", "Deafened", "Listening", "Muted for you" };
        var host = new FakeHost(2) { ConfigureServices = builder => builder
            .WithEventStream(WidgetSummaryCapabilities.ChangedFrom("widgetrail.samples.discord-controls"), token => events.Reader.ReadAllAsync(token))
            .WithHandler(WidgetSummaryCapabilities.ReadFrom("widgetrail.samples.discord-controls"), (_, _) => ValueTask.FromResult(new WidgetSummaryValue(
                JsonSerializer.SerializeToElement(new { Session = session, Mode = mode, Title = mode switch
                    {
                        "call" => "Game Night",
                        "setup" => "Sign in from the Discord widget",
                        _ => "Friends active"
                    },
                    Count = count, Truncated = false, People = Enumerable.Range(0, Math.Min(6, count)).Select(i => new
                    {
                        Key = (100 + i).ToString(), Name = names[i], Detail = mode == "call" ? statuses[i] :
                            i == 0 ? "Streaming 007 First Light" : "Playing Hades II",
                        Avatar = everyAvatar || i is 1 or 4 ? $"https://cdn.discordapp.com/embed/avatars/{i}.png" : null, Streaming = i == 0,
                        Action = i == 0 ? (int?)WidgetRail.Samples.Integrations.DiscordSummaryAction.WatchAndPin : null,
                        Token = i == 0 ? new string('a', 32) : null
                    }).ToArray() }), new string('b', 32)))) };
        var widget = Create(host, out var app);
        var game = Item(0).SavedId;
        var card = "playnite.playing." + game + ".discord";
        app.EventsHandler = async (_, token) => await gameEvents.Reader.ReadAsync(token);
        await Interactive(widget);
        try
        {
            await Ready(widget, host);
            await gameEvents.Writer.WriteAsync(new("profile", "instance", true, 1, [], [game],
                new Dictionary<string,long> { [game] = DateTimeOffset.UtcNow.AddMinutes(-25).ToUnixTimeMilliseconds() }));
            await WaitFor(() => widget.RenderState.Value.DiscordSummary?.Count == 2, "initial Discord subscription");
            ViewSnapshot Check(string name, int size, bool wide)
            {
                var snapshot = Snapshot(widget, 100 + size);
                var tile = Nodes(snapshot.Root).Single(n => n.Id == card);
                Assert.IsTrue(tile.StyleClasses.Contains(wide ? "playing-discord-wide" : "playing-discord-compact"));
                Assert.AreEqual(Math.Min(size, 4), Nodes(tile).Count(n => n.Id.EndsWith(".name", StringComparison.Ordinal)));
                Assert.AreEqual(ControllerButton.X, tile.ContextMenuButton);
                Export(name, snapshot);
                return snapshot;
            }
            void Export(string name, ViewSnapshot snapshot)
            {
                if (Environment.GetEnvironmentVariable("WRAIL_DISCORD_SHELF_FIXTURES") is not { Length: > 0 } output) return;
                Directory.CreateDirectory(output);
                RendererFixtureExporter.Write(Path.Combine(output, name + ".json"), snapshot,
                    RendererFixtureExporter.CompileTheme(Path.Combine(AppContext.BaseDirectory, "styles"), "default.wrss"));
            }
            Check("two", 2, false);
            foreach (var size in new[] { 3, 2, 6 })
            {
                count = size;
                if (size == 6) mode = "call";
                events.Writer.TryWrite(new(true));
                await WaitFor(() => widget.RenderState.Value.DiscordSummary?.Count == size, "updated roster");
                Check(size == 6 ? "call" : size.ToString(), size, size > 2);
            }
            await widget.OnActionAsync(new(card, card));
            var panel = Snapshot(widget, 201);
            Assert.IsTrue(Nodes(panel.Root).Any(n => n.Id == card + ".panel"));
            var intent = Nodes(panel.Root).Single(n => n.Intent?.ContractId == "widgetrail.summary.action").Intent!;
            Assert.AreEqual(WidgetIntentPresentation.StayInSource, intent.Presentation);
            Assert.IsTrue(intent.ReportsResult && intent.Payload.GetProperty("pin").GetBoolean() && !intent.Payload.GetProperty("microphone").GetBoolean());
            await widget.OnIntentCompletedAsync(new("test", intent, WidgetIntentStatus.Rejected));
            Assert.IsTrue(Nodes(Snapshot(widget, 202).Root).Any(n => n.Text == "This stream could not be pinned."));
            Export("panel", panel);
            await widget.OnActionAsync(new(card + ".close", card + ".close"));
            Assert.IsNull(widget.RenderState.Value.DiscordPanelGame);
            await widget.OnActionAsync(new(card, card));
            var replacement = session = Guid.NewGuid().ToString("N");
            events.Writer.TryWrite(new(true));
            await WaitFor(() => widget.RenderState.Value.DiscordSummary?.Session == replacement, "replacement session");
            Assert.IsNull(widget.RenderState.Value.DiscordPanelGame);
            mode = "friends";
            count = 0;
            events.Writer.TryWrite(new(true));
            await WaitFor(() => widget.RenderState.Value.DiscordSummary?.Count == 0, "empty activities");
            Check("empty", 0, false);
            await widget.OnActionAsync(new(card, card));
            var empty = Snapshot(widget, 203);
            Assert.AreEqual(card + ".close", empty.InitialFocusId);
            Export("empty-panel", empty);
            names[0] = "Alexandra With A Very Long Display Name";
            count = 4;
            events.Writer.TryWrite(new(true));
            await WaitFor(() => widget.RenderState.Value.DiscordSummary?.Count == 4, "updated open panel");
            var longPanel = Snapshot(widget, 204);
            Assert.IsTrue(Nodes(longPanel.Root).Any(n => n.Text == names[0]));
            Export("long-panel", longPanel);
            everyAvatar = true; count = 6;
            await widget.OnActionAsync(new(card + ".close", card + ".close"));
            events.Writer.TryWrite(new(true));
            await WaitFor(() => widget.RenderState.Value.DiscordSummary?.Count == 6, "all avatars");
            var complete = Check("portraits", 6, true);
            Assert.AreEqual(6, Nodes(Nodes(complete.Root).Single(n => n.Id == card)).Count(n => n.ImageIsPublicMedia));
            await widget.OnActionAsync(new(card, card));
            mode = "setup"; count = 0;
            events.Writer.TryWrite(new(true));
            await WaitFor(() => widget.RenderState.Value.DiscordSummary?.Mode == "setup", "sign-in required");
            Assert.IsNull(widget.RenderState.Value.DiscordPanelGame);
            var signedOut = Nodes(Snapshot(widget, 205).Root).Single(n => n.Id == card);
            Assert.AreEqual("Sign in from the Discord widget", signedOut.AccessibilityLabel);
            Assert.IsNull(signedOut.Intent);
            await widget.OnActionAsync(new(card, card));
            Assert.IsNull(widget.RenderState.Value.DiscordPanelGame);
            session = Guid.NewGuid().ToString("N");
            mode = "friends"; count = 2;
            events.Writer.TryWrite(new(true));
            await WaitFor(() => widget.RenderState.Value.DiscordSummary?.Session == session, "new account after sign-in");
            Check("signed-back-in", 2, false);
        }
        finally { await Background(widget); }
    }

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
        foreach (var kind in new[] { "Help", "News", "Sessions", "Achievements", "Capture", "Discord" }) await Set(kind, false);
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
    public void NextActivityUsesHighestKnownLockedPercentAndShowsTheTrackedChoiceOnce()
    {
        var items = Enumerable.Range(1, 8).Select(i => new PlayniteAchievement("Award " + i, "Task", false, null,
            i * 10, 50, false) { Id = "id" + i }).ToArray();
        var secret = items[0] with { Id = "secret", Percent = 99, Hidden = true };
        var unknown = items[0] with { Id = "unknown", Percent = null };
        var data = new PlayniteAchievements(true, 10, 0, [.. items, secret, unknown]) { ConnectionId = "profile" };
        var choice = new TrackedAchievement("game", "profile", data.Provider, items[7].Id!);
        var awards = PlayingAwards.From("game", data, choice);
        CollectionAssert.AreEqual(new[] { "id8", "id7", "id6", "id5", "id4", "id3" }, awards.Badges.Select(b => b.Award.Id).ToArray());
        Assert.AreEqual("Tracked", awards.Badges[0].Group);
        Assert.IsTrue(awards.Badges.Skip(1).All(b => b.Group == "Next activity"));
        CollectionAssert.AreEqual(new[] { "id8", "id7" }, awards.LimitNextActivity(1).Badges.Select(b => b.Award.Id).ToArray());
        Assert.AreEqual("id8", awards.LimitNextActivity(0).Badges.Single().Award.Id);
    }

    [TestMethod, Timeout(30000)]
    public async Task NextActivitySettingLimitsTheShelfAndPersistsAcrossWorkerRestart()
    {
        var host = new FakeHost(2);
        var game = Item(0).SavedId;
        var suggested = Enumerable.Range(1, 5).Select(i => new PlayniteAchievement("Candidate " + i, "",
            false, null, 100 - i, null, false) { Id = "candidate" + i }).ToArray();
        var unlocked = suggested[0] with { Id = "earned", Name = "Earned", Unlocked = true, UnlockedAt = DateTimeOffset.UtcNow };
        var data = new PlayniteAchievements(true, 6, 1, [unlocked, .. suggested]);
        const string settingId = "playnite.card-preferences.next-activity";
        var previousState = JsonSerializer.Deserialize<PlayniteLibraryPrivateState>(
            "{\"Version\":5,\"Items\":[]}");
        Assert.AreEqual(1, PlayniteLibraryOrganizationPolicy.Normalize(previousState).NextActivityCount);
        var reads = 0;
        var widget = Create(host, out var app);

        async Task OpenShelf()
        {
            var feed = Channel.CreateUnbounded<PlayniteEventBatch>();
            feed.Writer.TryWrite(new("profile", "instance", true, 1, [], [game]));
            app.EventsHandler = async (_, token) => await feed.Reader.ReadAsync(token);
            app.AchievementsHandler = (_, _) =>
            {
                Interlocked.Increment(ref reads);
                return ValueTask.FromResult(data);
            };
            await Interactive(widget);
            await Ready(widget, host);
            await WaitFor(() => ShelfCards("Recent unlock").Length == 1, "recent achievement");
        }

        ViewNode[] ShelfCards(string label) => Nodes(Snapshot(widget, 1).Root).Where(node =>
            node.Kind == ViewNodeKind.ActionSurface &&
            node.AccessibilityLabel?.StartsWith(label + ". ", StringComparison.Ordinal) == true).ToArray();

        async Task SetCount(string label, string previous, int expectedCount)
        {
            await widget.OnActionAsync(new(WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget.SettingsOpen, "playnite-library.library.menu"));
            var snapshot = Snapshot(widget, 2);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
            var control = Nodes(snapshot.Root).Single(node => node.Id == settingId);
            Assert.AreEqual(previous, control.SelectOptions.Single(option => option.IsSelected).Label);
            if (previous == "1" && Environment.GetEnvironmentVariable("WIDGETRAIL_PLAYNITE_NEXT_ACTIVITY_FIXTURE") is { Length: > 0 } path)
                RendererFixtureExporter.Write(path, snapshot,
                    RendererFixtureExporter.CompileTheme(Path.Combine(AppContext.BaseDirectory, "styles"), "default.wrss"));
            var choice = control.SelectOptions.Single(option => option.Label == label);
            await widget.OnActionAsync(new(choice.ActionId, control.Id, InputScopeId: snapshot.ActiveInputScopeId));
            var saved = Snapshot(widget, 3);
            Assert.AreEqual(label, Nodes(saved.Root).Single(node => node.Id == settingId).AccessibilityValue);
            var back = Nodes(saved.Root).Single(node => node.Id == "playnite.settings.back");
            await widget.OnActionAsync(new(back.ActionId!, back.Id, InputScopeId: saved.ActiveInputScopeId));
            Assert.AreEqual(expectedCount, ShelfCards("Next activity").Length);
            Assert.AreEqual(1, ShelfCards("Recent unlock").Length);
        }

        try
        {
            await OpenShelf();
            StringAssert.Contains(ShelfCards("Next activity").Single().AccessibilityLabel!, "Candidate 1");
            var initialReads = Volatile.Read(ref reads);
            await SetCount("3", "1", 3);
            await SetCount("Off", "3", 0);
            Assert.AreEqual(initialReads, Volatile.Read(ref reads), "Changing the display limit must reuse the cached achievements.");
            await WidgetTestHost.DestroyAsync(widget);
            widget = Create(host, out app);
            await OpenShelf();
            Assert.AreEqual(0, ShelfCards("Next activity").Length);
            await SetCount("1", "Off", 1);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
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
