using System.Text.Json;
using GameBarAlternative.FirstPartyWidgets.GameLauncher;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LauncherWidget = GameBarAlternative.FirstPartyWidgets.GameLauncher.GameLauncherWidget;

namespace GameBarAlternative.Tests.GameLauncher;

[TestClass]
public sealed class GameLauncherTests
{
    [TestMethod, Timeout(30_000)]
    public async Task QueryControlsMapExactBoundedCriteriaAndClear()
    {
        var persisted = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion,
            [new("saved-00000", "Game 00000", "Steam")])
        {
            FavoriteSavedIds = ["saved-00000"],
        };
        var host = new FakeHost(2, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "query organization load");

        await widget.OnActionAsync(new(
            "game-launcher.filter.favorites", "game-launcher.filter.favorites"));
        await Bounded(widget.WhenLibraryIdleAsync(), "favorite query");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());

        await widget.OnActionAsync(new(
            "game-launcher.filter.source", "game-launcher.filter.source"));
        await Bounded(widget.WhenLibraryIdleAsync(), "source query");
        Assert.AreEqual("Steam", host.Queries[^1].Query.SourceAttribution);

        await widget.OnActionAsync(new(
            "game-launcher.filter.sort", "game-launcher.filter.sort"));
        await Bounded(widget.WhenLibraryIdleAsync(), "sort query");
        Assert.AreEqual(WidgetAppLibrarySortOrder.DisplayNameDescending,
            host.Queries[^1].Query.Sort);

        await widget.OnActionAsync(new(
            "game-launcher.query.clear", "game-launcher.query.clear"));
        await Bounded(widget.WhenLibraryIdleAsync(), "cleared query");
        var cleared = host.Queries[^1].Query;
        Assert.IsNull(cleared.SearchText);
        Assert.IsNull(cleared.SourceAttribution);
        Assert.AreEqual(0, cleared.FavoriteSavedIds.Count);
        Assert.AreEqual(WidgetAppLibrarySortOrder.DisplayName, cleared.Sort);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task EveryTopControlKeepsPendingAndCommittedSnapshotsValid()
    {
        var displays = Enumerable.Range(0, 3)
            .Select(index => new GameLauncherDisplayItem(
                $"saved-{index:D5}", $"Game {index:D5}",
                index % 2 == 0 ? "Steam" : "Windows"))
            .ToArray();
        var persisted = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, displays)
        {
            FavoriteSavedIds = [displays[1].SavedId],
            RecentSavedIds = [displays[2].SavedId],
            ExcludedSavedIds = [displays[0].SavedId],
        };
        var host = new FakeHost(3, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "top-control warm state");
        long sequence = 700;

        foreach (var actionId in new[]
                 {
                     "game-launcher.filter.favorites",
                     "game-launcher.filter.recent",
                     "game-launcher.filter.source",
                     "game-launcher.filter.sort",
                     "game-launcher.query.clear",
                 })
        {
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<WidgetAppLibraryPage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var before = host.Queries.Count;
            host.QueryHandler = (request, token) =>
            {
                started.TrySetResult();
                return new(release.Task.WaitAsync(token));
            };

            await widget.OnActionAsync(new(actionId, actionId));
            await Bounded(started.Task, actionId + " admission");
            Assert.AreEqual(before + 1, host.Queries.Count,
                actionId + " must admit one replacement query.");
            AssertValidCollectionAnchor(Snapshot(widget, sequence++));

            release.TrySetResult(new(
                [Item(0), Item(1), Item(2)], null, null, actionId));
            await Bounded(widget.WhenLibraryIdleAsync(), actionId + " drain");
            AssertValidCollectionAnchor(Snapshot(widget, sequence++));
        }

        host.QueryHandler = null;
        await AssertRoute("game-launcher.add.open", GameLauncherRoute.AddGames);
        await AssertRoute("game-launcher.running.open", GameLauncherRoute.Running);
        await AssertRoute("game-launcher.hidden.open", GameLauncherRoute.Hidden);
        await Background(widget);

        async Task AssertRoute(string actionId, GameLauncherRoute expected)
        {
            var beforeQueries = host.Queries.Count;
            var beforeRunning = host.RunningObservationCount;
            await widget.OnActionAsync(new(actionId, actionId));
            await Bounded(widget.WhenLibraryIdleAsync(), actionId + " route load");
            var route = Snapshot(widget, sequence++);
            var expectedTitle = expected switch
            {
                GameLauncherRoute.AddGames => "Add games",
                GameLauncherRoute.Running => "Add running app",
                _ => "Hidden games",
            };
            Assert.AreEqual(expectedTitle, Nodes(route.Root).Single(node =>
                node.Id == "game-launcher.compact.title").Text);
            if (expected == GameLauncherRoute.Running)
                Assert.AreEqual(beforeRunning + 1, host.RunningObservationCount);
            else
                Assert.AreEqual(beforeQueries + 1, host.Queries.Count);

            var backId = expected switch
            {
                GameLauncherRoute.AddGames => "game-launcher.add.back",
                GameLauncherRoute.Running => "game-launcher.running.back",
                _ => "game-launcher.hidden.back",
            };
            await widget.OnActionAsync(new(backId, backId));
            await Bounded(widget.WhenLibraryIdleAsync(), backId + " route load");
            AssertValidCollectionAnchor(Snapshot(widget, sequence++));
        }
    }

    [TestMethod, Timeout(30_000)]
    [DataRow(1)]
    [DataRow(65)]
    [DataRow(130)]
    public async Task LastGameRowContinuesExactlyAcrossAvailableCursorPages(int total)
    {
        var host = new FakeHost(total);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var expectedQueries = 1;
        var expectedPages = (total + LauncherWidget.PageSize - 1) /
            LauncherWidget.PageSize;

        for (var page = 1; page < expectedPages; page++)
        {
            var before = Snapshot(widget, 800 + page);
            var scroll = Nodes(before.Root).Single(node =>
                node.Id == GameLauncherPresentation.ScrollId);
            Assert.IsNotNull(scroll.ScrollNearEndActionId,
                "A non-terminal last row must expose one managed continuation action.");
            var lastGame = Nodes(scroll).Last(node => node.CollectionItemKey is not null);
            Assert.IsFalse(lastGame.Id.StartsWith("game-launcher.previous",
                StringComparison.Ordinal));

            var action = new WidgetActionEvent(
                scroll.ScrollNearEndActionId!, scroll.Id,
                ControllerButton.DPadDown, ControllerEventPhase.Pressed,
                InputScopeId: before.ActiveInputScopeId);
            await widget.OnActionAsync(action);
            await Bounded(widget.WhenLibraryIdleAsync(), "last-row cursor continuation");
            expectedQueries++;
            Assert.AreEqual(expectedQueries, host.Queries.Count,
                "One edge input must load the next cursor page exactly once.");
            Assert.IsNotNull(widget.Collection.RequestedFocusId);
            Assert.IsTrue(widget.Collection.RequestedFocusId!.StartsWith(
                "game-launcher.item.grid.", StringComparison.Ordinal));
            Assert.IsTrue(Nodes(Snapshot(widget, 850 + page).Root).Any(node =>
                node.Id == widget.Collection.RequestedFocusId &&
                node.ActionId == "game-launcher.launch"),
                "Continuation focus must land on a game rather than page/footer controls.");
        }

        var final = Snapshot(widget, 900 + total);
        var finalScroll = Nodes(final.Root).Single(node =>
            node.Id == GameLauncherPresentation.ScrollId);
        Assert.IsNull(finalScroll.ScrollNearEndActionId,
            "A final partial row must not expose a looping continuation.");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task FocusedShortcutLegendMatchesExactActionability()
    {
        var launchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLaunch = new TaskCompletionSource<WidgetAppLaunchObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(2)
        {
            LaunchHandler = (_, _) =>
            {
                launchStarted.TrySetResult();
                return new(releaseLaunch.Task);
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var ready = Snapshot(widget, 950);
        var tile = Nodes(ready.Root).First(node =>
            node.ActionId == "game-launcher.launch");
        var shortcuts = tile.Shortcuts.ToDictionary(shortcut => shortcut.Button);
        Assert.AreEqual("game-launcher.details.open",
            shortcuts[ControllerButton.View].ActionId);
        Assert.AreEqual("game-launcher.favorite",
            shortcuts[ControllerButton.X].ActionId);
        Assert.AreEqual("game-launcher.hide",
            shortcuts[ControllerButton.Y].ActionId);
        Assert.AreEqual("game-launcher.variant",
            shortcuts[ControllerButton.LeftBumper].ActionId);
        Assert.AreEqual("game-launcher.prefer",
            shortcuts[ControllerButton.RightBumper].ActionId);
        foreach (var hintId in new[]
                 {
                     "game-launcher.hint.details",
                     "game-launcher.hint.favorite",
                     "game-launcher.hint.hide",
                     "game-launcher.hint.variant",
                     "game-launcher.hint.prefer",
                 })
            Assert.IsTrue(Nodes(ready.Root).Any(node => node.Id == hintId));

        var launch = widget.OnActionAsync(new(
            "game-launcher.launch", tile.Id)).AsTask();
        await Bounded(launchStarted.Task, "shortcut busy admission");
        var busy = Snapshot(widget, 951);
        var busyTile = Nodes(busy.Root).Single(node => node.Id == tile.Id);
        Assert.IsTrue(busyTile.IsBusy);
        Assert.AreEqual("Pending", Nodes(busy.Root).Single(node =>
            node.Id == "game-launcher.hero.state").Text);
        Assert.AreEqual(0, busyTile.Shortcuts.Count,
            "A busy game must not advertise shortcuts it cannot dispatch.");
        Assert.IsFalse(Nodes(busy.Root).Any(node =>
            node.Id == "game-launcher.organization.hints"),
            "The static legend must not outlive current focused-game actionability.");

        releaseLaunch.TrySetResult(new(
            WidgetAppLaunchObservationState.LauncherStarted, false, false));
        await Bounded(launch, "shortcut busy completion");
        await Bounded(widget.WhenLibraryIdleAsync(), "shortcut recent refresh");
        var completed = Snapshot(widget, 952);
        Assert.AreEqual(5, Nodes(completed.Root).Single(node =>
            node.Id == tile.Id).Shortcuts.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task CommittedQueryReplacesGenerationAndStaleCompletionCannotPublish()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var staleStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.QueryHandler = (request, _) =>
        {
            if (request.Query.SearchText == "Alpha")
            {
                staleStarted.TrySetResult();
                return new ValueTask<WidgetAppLibraryPage>(releaseStale.Task);
            }
            var item = WithPresentation(
                Item(1), displayName: request.Query.SearchText ?? "All");
            return ValueTask.FromResult(new WidgetAppLibraryPage(
                [item], null, null, "query-" + (request.Query.SearchText ?? "all")));
        };

        await widget.OnActionAsync(new WidgetActionEvent(
            "game-launcher.search.commit", "game-launcher.search")
            { CommittedText = "  Alpha  " });
        await Bounded(staleStarted.Task, "stale query admission");
        await widget.OnActionAsync(new WidgetActionEvent(
            "game-launcher.search.commit", "game-launcher.search")
            { CommittedText = "Beta" });
        releaseStale.TrySetResult(new([WithPresentation(Item(0), displayName: "Alpha")],
            null, null, "query-alpha"));
        await Bounded(widget.WhenLibraryIdleAsync(), "replacement query drain");
        Assert.AreEqual("Beta",
            widget.Collection.Items.Single().Presentation.DisplayName);

        var snapshot = Snapshot(widget, 99);
        var search = Nodes(snapshot.Root).Single(node => node.Id == "game-launcher.search");
        Assert.AreEqual(ViewNodeKind.TextEntry, search.Kind);
        Assert.AreEqual("Beta", search.TextEntryValue);
        await Background(widget);
    }
    [TestMethod, Timeout(30_000)]
    [DataRow(2_000)]
    [DataRow(10_000)]
    public async Task LargeLibrariesTraverseInBoundedCursorWindow(int total)
    {
        var host = new FakeHost(total);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var pages = (total + LauncherWidget.PageSize - 1) / LauncherWidget.PageSize;
        for (var page = 1; page < pages; page++)
        {
            var revision = widget.Collection.Revision;
            await widget.OnActionAsync(new("game-launcher.next", "game-launcher.next"));
            await Bounded(widget.WhenLibraryIdleAsync(), "forward page drain");
            Assert.IsGreaterThan(revision, widget.Collection.Revision);
            Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
            Assert.IsLessThanOrEqualTo(LauncherWidget.MaximumRetainedItems,
                widget.Collection.Items.Count);
        }

        Assert.AreEqual(total, host.MaximumObservedIndex + 1);
        Assert.AreEqual(LauncherWidget.PageSize, host.MaximumRequestedLimit);
        Assert.IsLessThanOrEqualTo(LauncherWidget.MaximumRetainedItems,
            widget.Collection.Items.Count);
        Assert.AreEqual($"Game {total - 1:D5}",
            widget.Collection.Items[^1].Presentation.DisplayName);
        var serialized = Snapshot(widget, total);
        Assert.AreEqual(widget.Collection.Items.Count, Nodes(serialized.Root).Count(node =>
            node.ActionId == "game-launcher.launch"));
        Assert.IsLessThanOrEqualTo(
            WidgetCursorResource<GameLauncherItem>.MaximumCursorHistory,
            widget.RetainedCursorCount);
        var beforeRevision = widget.Collection.Revision;
        await widget.OnActionAsync(new("game-launcher.previous", "game-launcher.previous"));
        await Bounded(widget.WhenLibraryIdleAsync(), "reverse page drain");
        Assert.IsGreaterThan(beforeRevision, widget.Collection.Revision);
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
        var finalPageStart = (pages - 1) * LauncherWidget.PageSize;
        var reversePageStart = finalPageStart - LauncherWidget.MaximumRetainedItems;
        Assert.AreEqual($"Game {reversePageStart:D5}",
            widget.Collection.Items[0].Presentation.DisplayName);
        Assert.AreEqual(
            GameLauncherIdentity.FocusId("grid", widget.Collection.Items[63].Key),
            widget.Collection.RequestedFocusId);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ControllerBumpersAreScopedAndBoundaryExactAcrossPages()
    {
        var host = new FakeHost(260)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var first = Snapshot(widget, 201);
        var firstTile = Nodes(first.Root).First(node =>
            node.ActionId == "game-launcher.launch");
        await widget.OnActionAsync(new("game-launcher.launch", firstTile.Id));
        await WaitUntil(() => widget.Organization.RecentSavedIds.Count == 1);
        AssertShortcutMap(first, before: false, after: true);
        Assert.IsFalse(await Route(widget, first, ControllerButton.LeftBumper, firstTile.Id));
        Assert.IsFalse(await Route(widget, first, ControllerButton.RightBumper, firstTile.Id,
            ControllerEventPhase.Repeated));
        Assert.IsTrue(await Route(widget, first, ControllerButton.RightBumper, firstTile.Id));
        await WaitUntil(() => host.Queries.Count == 2);
        await Bounded(widget.WhenLibraryIdleAsync(), "first bumper page");

        var retained = Snapshot(widget, 202);
        AssertShortcutMap(retained, before: false, after: true);
        Assert.IsTrue(Nodes(retained.Root).Any(node => node.Id == firstTile.Id),
            "The exact focused SavedId should remain available in the retained window.");
        Assert.IsTrue(await Route(widget, retained, ControllerButton.RightBumper, firstTile.Id));
        await WaitUntil(() => host.Queries.Count == 3);
        await Bounded(widget.WhenLibraryIdleAsync(), "second retained bumper page");

        var beforeEviction = Snapshot(widget, 203);
        AssertShortcutMap(beforeEviction, before: false, after: true);
        Assert.IsTrue(await Route(
            widget, beforeEviction, ControllerButton.RightBumper, firstTile.Id));
        await WaitUntil(() => host.Queries.Count == 4);
        await Bounded(widget.WhenLibraryIdleAsync(), "evicting bumper page");

        var middle = Snapshot(widget, 204);
        AssertShortcutMap(middle, before: true, after: true);
        Assert.IsFalse(Nodes(middle.Root).Any(node => node.Id == firstTile.Id));
        var nearest = GameLauncherIdentity.FocusId(
            "grid", GameLauncherIdentity.Key("saved-00192"));
        Assert.AreEqual(nearest, widget.Collection.RequestedFocusId);
        Assert.IsTrue(Nodes(middle.Root).Any(node => node.Id == nearest));
        Assert.IsTrue(await Route(widget, middle, ControllerButton.RightBumper, nearest));
        await WaitUntil(() => host.Queries.Count == 5);
        await Bounded(widget.WhenLibraryIdleAsync(), "final bumper page");

        var final = Snapshot(widget, 205);
        AssertShortcutMap(final, before: true, after: false);
        var finalTile = Nodes(final.Root).First(node =>
            node.ActionId == "game-launcher.launch");
        Assert.IsFalse(await Route(widget, final, ControllerButton.RightBumper, finalTile.Id));
        Assert.IsTrue(await Route(widget, final, ControllerButton.LeftBumper, finalTile.Id));
        await WaitUntil(() => host.Queries.Count == 6);
        await Bounded(widget.WhenLibraryIdleAsync(), "reverse bumper page");

        var reverse = Snapshot(widget, 206);
        AssertShortcutMap(reverse, before: true, after: true);
        Assert.IsFalse(await Route(
            widget, reverse, ControllerButton.RightBumper, "game-launcher.refresh"),
            "Bumpers outside the results scroll must keep their existing ownership.");

        await widget.OnActionAsync(new(
            "game-launcher.filter.recent", "game-launcher.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "recent-first reload");
        var fixedSnapshot = Snapshot(widget, 207);
        var fixedTile = Nodes(fixedSnapshot.Root).First(node =>
            node.ActionId == "game-launcher.launch" &&
            node.CollectionItemKey is null);
        Assert.IsTrue(await Route(
            widget, fixedSnapshot, ControllerButton.RightBumper, fixedTile.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "fixed-section bumper page");
        Assert.IsLessThanOrEqualTo(
            LauncherWidget.MaximumRetainedItems, widget.Collection.Items.Count);
        await Background(widget);

        var singleHost = new FakeHost(2);
        var single = Create(singleHost);
        await Interactive(single);
        await Ready(single, singleHost);
        var singleSnapshot = Snapshot(single, 208);
        AssertShortcutMap(singleSnapshot, before: false, after: false);
        var singleTile = Nodes(singleSnapshot.Root).First(node =>
            node.ActionId == "game-launcher.launch");
        Assert.AreEqual("game-launcher.variant", singleTile.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.LeftBumper).ActionId);
        Assert.AreEqual("game-launcher.prefer", singleTile.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.RightBumper).ActionId);
        await Background(single);
    }

    [TestMethod, Timeout(30_000)]
    public async Task BumperPagingRejectsBusyAndReplacementStaleCompletion()
    {
        var host = new FakeHost(130);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var staleStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.QueryHandler = (request, _) =>
        {
            if (request.Query.SearchText == "Current")
                return ValueTask.FromResult(new WidgetAppLibraryPage(
                    [WithPresentation(Item(1), displayName: "Current")], null, null,
                    "current-revision"));
            staleStarted.TrySetResult();
            return new(releaseStale.Task);
        };

        var admitted = Snapshot(widget, 211);
        var focused = Nodes(admitted.Root).First(node =>
            node.ActionId == "game-launcher.launch").Id;
        Assert.IsTrue(await Route(
            widget, admitted, ControllerButton.RightBumper, focused));
        await Bounded(staleStarted.Task, "bumper page admission");
        var busy = Snapshot(widget, 212);
        Assert.AreEqual(WidgetPagedResourceStatus.LoadingAdjacent, widget.Collection.Status);
        AssertShortcutMap(busy, before: false, after: false);
        Assert.IsFalse(await Route(widget, busy, ControllerButton.RightBumper, focused));

        await widget.OnActionAsync(new WidgetActionEvent(
            "game-launcher.search.commit", "game-launcher.search")
            { CommittedText = "Current" });
        releaseStale.TrySetResult(new(
            [WithPresentation(Item(64), displayName: "Stale")], "cursor.0", "cursor.65",
            "stale-revision"));
        await Bounded(widget.WhenLibraryIdleAsync(), "bumper replacement drain");
        Assert.AreEqual("Current",
            widget.Collection.Items.Single().Presentation.DisplayName);
        Assert.IsFalse(widget.Collection.Items.Any(item =>
            item.Presentation.DisplayName == "Stale"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task WarmProjectionIsVisibleButCannotAuthorizeLaunch()
    {
        var warm = new GameLauncherPrivateState(GameLauncherPrivateState.CurrentVersion,
            [new("saved-warm", "Warm game", "Steam")]);
        var state = new WidgetTestPrivateState(JsonSerializer.Serialize(warm), 1);
        var pending = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(0, state)
        {
            QueryHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
                pending.Task.WaitAsync(token)),
        };
        var widget = Create(host);
        await Visible(widget);
        await Bounded(host.FirstQueryStarted.Task, "warm projection query admission");
        await Bounded(widget.WhenWarmStateIdleAsync(), "warm state load");
        Assert.AreEqual(1, widget.WarmItems.Count);

        var snapshot = Snapshot(widget, 1);
        var tile = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "game-launcher.launch");
        Assert.IsTrue(tile.IsDisabled);
        await widget.OnActionAsync(new("game-launcher.launch", tile.Id));
        Assert.AreEqual(0, host.Launches.Count);

        await Background(widget);
        pending.TrySetResult(new([], null, null, "rev-1"));
    }

    [TestMethod]
    public void PrivateProjectionIsBoundedAndContainsNoLaunchAuthority()
    {
        var items = Enumerable.Range(0, GameLauncherPrivateState.MaximumItems)
            .Select(index => new GameLauncherDisplayItem(
                $"saved-{index:D3}-" + new string('s', 114),
                new string((char)('a' + index % 26), 96),
                new string((char)('A' + index % 26), 64)))
            .ToArray();
        var organized = items.Take(GameLauncherPrivateState.MaximumOrganizedItems).ToArray();
        var groups = organized.Chunk(GameLauncherPrivateState.MaximumVariantsPerGroup)
            .Select((members, index) => new GameLauncherVariantGroup(
                "variant." + index.ToString("x20"),
                members.Select(item => item.SavedId).ToArray(),
                members[^1].SavedId)).ToArray();
        var state = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, items)
        {
            FavoriteSavedIds = organized.Select(item => item.SavedId).ToArray(),
            VariantGroups = groups,
            RecentSavedIds = items.Skip(GameLauncherPrivateState.MaximumOrganizedItems)
                .Take(GameLauncherPrivateState.MaximumRecentItems)
                .Reverse().Select(item => item.SavedId).ToArray(),
            ManualSavedIds = items.Skip(GameLauncherPrivateState.MaximumOrganizedItems +
                    GameLauncherPrivateState.MaximumRecentItems)
                .Take(GameLauncherPrivateState.MaximumManualItems)
                .Select(item => item.SavedId).ToArray(),
            ExcludedSavedIds = items.Skip(GameLauncherPrivateState.MaximumOrganizedItems +
                    GameLauncherPrivateState.MaximumRecentItems +
                    GameLauncherPrivateState.MaximumManualItems)
                .Take(GameLauncherPrivateState.MaximumExcludedItems)
                .Select(item => item.SavedId).ToArray(),
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(state);

        Assert.IsLessThanOrEqualTo(64 * 1024, json.Length);
        Assert.AreEqual(items.Length, GameLauncherOrganizationPolicy.Normalize(state).Items.Count);
        Assert.AreEqual(0, GameLauncherOrganizationPolicy.Normalize(
            state with { Items = [.. items, items[0] with { SavedId = "saved-overflow" }] })
            .Items.Count);
        var text = System.Text.Encoding.UTF8.GetString(json);
        Assert.IsFalse(text.Contains("AppId", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("Artwork", StringComparison.Ordinal));
    }

    [TestMethod, Timeout(30_000)]
    public async Task HideSurvivesRestartAndRestoreNeverAuthorizesLaunch()
    {
        var privateState = new WidgetTestPrivateState();
        var host = new FakeHost(3, privateState);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var first = Nodes(Snapshot(widget, 301).Root).First(node =>
            node.ActionId == "game-launcher.launch");
        await widget.OnActionAsync(new("game-launcher.favorite", first.Id));
        await widget.OnActionAsync(new("game-launcher.hide", first.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "hidden library refresh");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            widget.Organization.ExcludedSavedIds.ToArray());
        Assert.IsFalse(Nodes(Snapshot(widget, 302).Root).Any(node =>
            node.ActionId == "game-launcher.launch" && node.Id == first.Id));
        var afterHide = Snapshot(widget, 3021);
        Assert.IsNotNull(afterHide.InitialFocusId);
        Assert.IsTrue(Nodes(afterHide.Root).Any(node =>
            node.Id == afterHide.InitialFocusId &&
            node.ActionId == "game-launcher.launch" && node.IsDisabled is not true));
        await Background(widget);

        var restartedHost = new FakeHost(3, privateState);
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        Assert.IsFalse(Nodes(Snapshot(restarted, 303).Root).Any(node =>
            node.ActionId == "game-launcher.launch" && node.Id == first.Id));
        await restarted.OnActionAsync(new(
            "game-launcher.hidden.open", "game-launcher.hidden.open"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "hidden route load");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restartedHost.Queries[^1].Query.FavoriteSavedIds.ToArray());
        var hidden = Nodes(Snapshot(restarted, 304).Root).Single(node =>
            node.ActionId == "game-launcher.restore");
        StringAssert.Contains(hidden.AccessibilityLabel!, "Hidden · Restore");
        await restarted.OnActionAsync(new WidgetActionEvent(
            "game-launcher.search.commit", "game-launcher.search")
            { CommittedText = "No such hidden game" });
        await Bounded(restarted.WhenLibraryIdleAsync(), "hidden query filter");
        Assert.IsTrue(Nodes(Snapshot(restarted, 3041).Root).Any(node =>
            node.Id == "game-launcher.hidden.empty.action"));
        await restarted.OnActionAsync(new(
            "game-launcher.query.clear", "game-launcher.query.clear"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "hidden query clear");
        hidden = Nodes(Snapshot(restarted, 3042).Root).Single(node =>
            node.ActionId == "game-launcher.restore");
        await restarted.OnActionAsync(new("game-launcher.launch", hidden.Id));
        Assert.AreEqual(0, restartedHost.Launches.Count,
            "A display-only hidden row must not authorize launch.");
        await restarted.OnActionAsync(new("game-launcher.restore", hidden.Id));
        Assert.AreEqual(0, restarted.Organization.ExcludedSavedIds.Count);
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restarted.Organization.FavoriteSavedIds.ToArray());
        Assert.IsTrue(Nodes(Snapshot(restarted, 305).Root).Any(node =>
            node.Id == "game-launcher.hidden.empty.action"));
        await restarted.OnActionAsync(new(
            "game-launcher.hidden.back", "game-launcher.hidden.empty.action"));
        var restoredLibrary = Snapshot(restarted, 306);
        Assert.IsTrue(Nodes(restoredLibrary.Root).Any(node =>
            node.ActionId == "game-launcher.launch" && node.Id == first.Id &&
            node.IsDisabled is not true));
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RestoreSupersedesCancellationIgnoringHiddenLoadBeforeBack()
    {
        var display = new GameLauncherDisplayItem("saved-00000", "Game 00000", "Steam");
        var persisted = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, [display])
        {
            ExcludedSavedIds = [display.SavedId],
        };
        var host = new FakeHost(1, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var hiddenLoadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHiddenLoad = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var queryCount = 0;
        host.QueryHandler = (_, _) =>
        {
            if (Interlocked.Increment(ref queryCount) == 1)
            {
                hiddenLoadStarted.TrySetResult();
                return new(releaseHiddenLoad.Task);
            }
            return ValueTask.FromResult(new WidgetAppLibraryPage(
                [Item(0)], null, null, "current-library"));
        };

        await widget.OnActionAsync(new(
            "game-launcher.hidden.open", "game-launcher.hidden.open"));
        await Bounded(hiddenLoadStarted.Task, "cancellation-ignoring hidden load admission");
        var hidden = Nodes(Snapshot(widget, 307).Root).Single(node =>
            node.ActionId == "game-launcher.restore");
        var restore = widget.OnActionAsync(new(
            "game-launcher.restore", hidden.Id)).AsTask();
        Assert.IsFalse(restore.IsCompleted,
            "Restore must drain the replaced Hidden generation before reporting readiness.");
        releaseHiddenLoad.TrySetResult(new(
            [Item(0)], null, null, "late-hidden"));
        await Bounded(restore, "cancellation-ignoring Hidden replacement drain");
        Assert.IsTrue(Nodes(Snapshot(widget, 3071).Root).Any(node =>
            node.Id == "game-launcher.hidden.empty.action"));

        using var canceledRoute = new CancellationTokenSource();
        canceledRoute.Cancel();
        await widget.OnActionAsync(new(
            "game-launcher.hidden.back", "game-launcher.hidden.empty.action"),
            canceledRoute.Token);
        var current = Snapshot(widget, 308);
        var launch = Nodes(current.Root).Single(node =>
            node.ActionId == "game-launcher.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                display.DisplayName, StringComparison.Ordinal));
        Assert.IsTrue(launch.IsDisabled is not true);
        Assert.IsNotNull(current.InitialFocusId);
        Assert.IsTrue(Nodes(current.Root).Any(node =>
            node.Id == current.InitialFocusId &&
            node.ActionId == "game-launcher.launch" && node.IsDisabled is not true));

        var afterLateCompletion = Snapshot(widget, 309);
        Assert.IsTrue(Nodes(afterLateCompletion.Root).Any(node =>
            node.Id == launch.Id && node.ActionId == "game-launcher.launch" &&
            node.IsDisabled is not true));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingAndReplacementRowsRemainIndependentFromHiddenIdentity()
    {
        var hidden = new GameLauncherDisplayItem("saved-00000", "Shared title", "Steam");
        var persisted = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, [hidden])
        {
            ExcludedSavedIds = [hidden.SavedId],
        };
        var privateState = new WidgetTestPrivateState(JsonSerializer.Serialize(persisted), 1);
        var host = new FakeHost(1, privateState)
        {
            ItemFactory = _ => WithPresentation(Item(1), displayName: hidden.DisplayName),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Nodes(Snapshot(widget, 311).Root).Where(node =>
            node.ActionId == "game-launcher.launch").ToArray();
        Assert.AreEqual(1, library.Length);
        StringAssert.Contains(library[0].AccessibilityLabel!, hidden.DisplayName);
        await widget.OnActionAsync(new(
            "game-launcher.hidden.open", "game-launcher.hidden.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "missing hidden route");
        var unavailable = Nodes(Snapshot(widget, 312).Root).Single(node =>
            node.ActionId == "game-launcher.restore");
        StringAssert.Contains(unavailable.AccessibilityLabel!, "Unavailable · Restore");
        Assert.AreEqual("game-launcher.item.hidden." +
            GameLauncherIdentity.Key(hidden.SavedId).Value, unavailable.Id);
        await Background(widget);

        var reclassifiedHost = new FakeHost(1, privateState)
        {
            ItemFactory = _ => WithPresentation(
                Item(0), kind: WidgetAppLibraryKind.Application),
        };
        var reclassified = Create(reclassifiedHost);
        await Interactive(reclassified);
        await Ready(reclassified, reclassifiedHost);
        Assert.IsFalse(Nodes(Snapshot(reclassified, 313).Root).Any(node =>
            node.ActionId == "game-launcher.launch"));
        await reclassified.OnActionAsync(new(
            "game-launcher.hidden.open", "game-launcher.hidden.open"));
        await Bounded(reclassified.WhenLibraryIdleAsync(), "reclassified hidden route");
        var current = Nodes(Snapshot(reclassified, 314).Root).Single(node =>
            node.ActionId == "game-launcher.restore");
        StringAssert.Contains(current.AccessibilityLabel!, "Hidden · Restore");
        await Background(reclassified);
    }

    [TestMethod]
    public async Task HiddenBoundAndCasReplayPreserveConcurrentOrganization()
    {
        var displays = Enumerable.Range(0, GameLauncherPrivateState.MaximumExcludedItems + 2)
            .Select(index => new GameLauncherDisplayItem(
                $"saved-{index:D5}", $"Game {index}", "Steam"))
            .ToArray();
        var state = GameLauncherPrivateState.Empty;
        for (var index = 0; index < GameLauncherPrivateState.MaximumExcludedItems; index++)
        {
            var mutation = GameLauncherOrganizationPolicy.SetExcluded(
                state, displays[index], excluded: true);
            Assert.IsTrue(mutation.Accepted);
            state = mutation.State;
        }
        var overflow = GameLauncherOrganizationPolicy.SetExcluded(
            state, displays[^1], excluded: true);
        Assert.IsFalse(overflow.Accepted);
        Assert.AreEqual(GameLauncherPrivateState.MaximumExcludedItems,
            overflow.State.ExcludedSavedIds.Count);

        var concurrent = state with
        {
            Items = [displays[^2], .. state.Items],
            FavoriteSavedIds = [displays[^2].SavedId],
            VariantGroups =
            [
                new(GameLauncherIdentity.GroupId(
                        displays[^2].SavedId, displays[31].SavedId),
                    [displays[^2].SavedId, displays[31].SavedId],
                    displays[^2].SavedId),
            ],
            RecentSavedIds = [displays[^2].SavedId],
            ManualSavedIds = [displays[^2].SavedId],
        };
        GameLauncherPrivateState? written = null;
        var attempts = 0;
        var restored = displays[0];
        var result = await GameLauncherStateStore.SaveAsync(
            current => GameLauncherOrganizationPolicy.SetExcluded(
                current, restored, excluded: false),
            (candidate, _, _) =>
            {
                if (attempts++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "fixture"));
                written = candidate;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<GameLauncherPrivateState>(
                true, concurrent, 2)), state, 1, CancellationToken.None);
        Assert.IsTrue(result.Saved);
        Assert.IsFalse(written!.ExcludedSavedIds.Contains(restored.SavedId));
        Assert.AreEqual(GameLauncherPrivateState.MaximumExcludedItems - 1,
            written.ExcludedSavedIds.Count);
        CollectionAssert.AreEqual(new[] { displays[^2].SavedId },
            written.FavoriteSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { displays[^2].SavedId },
            written.RecentSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { displays[^2].SavedId },
            written.ManualSavedIds.ToArray());
        Assert.AreEqual(displays[^2].SavedId,
            written.VariantGroups.Single().PreferredSavedId);

        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await GameLauncherStateStore.SaveAsync(
                current => GameLauncherOrganizationPolicy.SetExcluded(
                    current, displays[1], excluded: false),
                (_, _, _) => ValueTask.FromException<WidgetPrivateStateMutation>(
                    new IOException("fixture")),
                _ => ValueTask.FromResult(new WidgetPrivateStateValue<GameLauncherPrivateState>(
                    true, state, 1)), state, 1, CancellationToken.None));
        Assert.IsTrue(state.ExcludedSavedIds.Contains(displays[1].SavedId));
    }

    [TestMethod, Timeout(30_000)]
    public async Task FavoritesAndExplicitVariantsSurviveRestartAndDisappearance()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(2, state)
        {
            ItemFactory = index => WithPresentation(Item(index), displayName: "Shared title"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 10).Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();

        await widget.OnActionAsync(new("game-launcher.favorite", tiles[0].Id));
        await widget.OnActionAsync(new("game-launcher.variant", tiles[0].Id));
        await widget.OnActionAsync(new("game-launcher.variant", tiles[1].Id));
        await widget.OnActionAsync(new("game-launcher.prefer", tiles[1].Id));
        Assert.IsTrue(widget.Organization.FavoriteSavedIds.Contains("saved-00000"));
        Assert.AreEqual(1, widget.Organization.VariantGroups.Count);
        var group = widget.Organization.VariantGroups.Single();
        Assert.AreEqual("saved-00001", group.PreferredSavedId);
        await Background(widget);

        var missingHost = new FakeHost(0, state);
        var missing = Create(missingHost);
        await Interactive(missing);
        await Ready(missing, missingHost);
        var missingSnapshot = Snapshot(missing, 11);
        Assert.IsTrue(Nodes(missingSnapshot.Root).Any(node =>
            node.ActionId == "game-launcher.launch" && node.IsDisabled == true &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Preferred variant", StringComparison.Ordinal)));
        Assert.AreEqual(1, missing.Organization.VariantGroups.Count);
        Assert.AreEqual("saved-00001",
            missing.Organization.VariantGroups.Single().PreferredSavedId);
        await Background(missing);

        var restoredHost = new FakeHost(2, state);
        var restored = Create(restoredHost);
        await Interactive(restored);
        await Ready(restored, restoredHost);
        var restoredPreferred = Nodes(Snapshot(restored, 12).Root).Single(node =>
            node.ActionId == "game-launcher.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Preferred variant", StringComparison.Ordinal));
        Assert.IsTrue(restoredPreferred.IsDisabled != true);
        var restoredTiles = Nodes(Snapshot(restored, 13).Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();
        await restored.OnActionAsync(new("game-launcher.variant", restoredTiles[0].Id));
        await restored.OnActionAsync(new("game-launcher.variant", restoredTiles[1].Id));
        Assert.AreEqual(0, restored.Organization.VariantGroups.Count);
        Assert.IsTrue(restored.Organization.FavoriteSavedIds.Contains("saved-00000"));
        await restored.OnActionAsync(new(
            "game-launcher.organization.reset", "game-launcher.organization.reset"));
        Assert.AreEqual(0, restored.Organization.FavoriteSavedIds.Count);
        await Background(restored);
    }

    [TestMethod]
    public async Task CasConflictReappliesOnlyRequestedFavoriteDelta()
    {
        var a = new GameLauncherDisplayItem("saved-a", "A", "Steam");
        var b = new GameLauncherDisplayItem("saved-b", "B", "Steam");
        var c = new GameLauncherDisplayItem("saved-c", "C", "Windows");
        var d = new GameLauncherDisplayItem("saved-d", "D", "Windows");
        var baseline = new GameLauncherPrivateState(GameLauncherPrivateState.CurrentVersion,
            [a, b, c, d])
        {
            FavoriteSavedIds = [a.SavedId],
        };
        var latest = baseline with
        {
            FavoriteSavedIds = [a.SavedId, c.SavedId],
            VariantGroups =
            [
                new(GameLauncherIdentity.GroupId(c.SavedId, d.SavedId),
                    [c.SavedId, d.SavedId], d.SavedId),
            ],
        };
        GameLauncherPrivateState? written = null;
        var writes = 0;

        var result = await GameLauncherStateStore.SaveAsync(
            state => GameLauncherOrganizationPolicy.SetFavorite(state, b, favorite: true),
            (state, _, _) =>
            {
                if (writes++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "conflict"));
                written = state;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<GameLauncherPrivateState>(
                true, latest, 2)),
            baseline, 1, CancellationToken.None);

        Assert.IsTrue(result.Saved);
        CollectionAssert.AreEqual(
            new[] { a.SavedId, c.SavedId, b.SavedId },
            written!.FavoriteSavedIds.ToArray());
        Assert.AreEqual(1, written.VariantGroups.Count);
        CollectionAssert.AreEqual(new[] { c.SavedId, d.SavedId },
            written.VariantGroups[0].SavedIds.ToArray());
    }

    [TestMethod]
    public void SourceRevisionReplacementUsesOnlyExactSavedIdentity()
    {
        var old = new GameLauncherDisplayItem("saved-a", "Old title", "Steam");
        var state = new GameLauncherPrivateState(GameLauncherPrivateState.CurrentVersion, [old])
        {
            FavoriteSavedIds = [old.SavedId],
        };
        var sameIdentity = GameLauncherItem.From(InstalledItem(
            "app-new", old.SavedId, "Updated title", WidgetAppLibraryKind.Game,
            "source-steam", "Steam"));
        var refreshed = GameLauncherOrganizationPolicy.ProjectPage(state, [sameIdentity]);
        Assert.AreEqual("Updated title",
            refreshed.Items.Single(item => item.SavedId == old.SavedId).DisplayName);
        Assert.IsTrue(refreshed.FavoriteSavedIds.Contains(old.SavedId));

        var replacement = GameLauncherItem.From(InstalledItem(
            "app-replacement", "saved-replacement", "Updated title",
            WidgetAppLibraryKind.Game, "source-steam", "Steam"));
        var replaced = GameLauncherOrganizationPolicy.ProjectPage(refreshed, [replacement]);
        Assert.IsTrue(replaced.FavoriteSavedIds.Contains(old.SavedId));
        Assert.IsFalse(replaced.FavoriteSavedIds.Contains("saved-replacement"));
        Assert.IsTrue(replaced.Items.Any(item => item.SavedId == old.SavedId));
        Assert.IsTrue(replaced.Items.Any(item => item.SavedId == "saved-replacement"));
    }

    [TestMethod, Timeout(30_000)]
    public async Task IncompatibleStateResetsWholeSchemaBeforeReconciliation()
    {
        var legacyJson = """
            {"Version":4,"Items":[{"SavedId":"saved-stale","DisplayName":"Stale","SourceAttribution":"Old"}],"FavoriteSavedIds":["saved-stale"],"RecentSavedIds":["saved-stale"],"ManualSavedIds":["saved-stale"],"ExcludedSavedIds":["saved-stale"]}
            """;
        var state = new WidgetTestPrivateState(legacyJson, 1);
        var page = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(1, state)
        {
            QueryHandler = (_, token) => new(page.Task.WaitAsync(token)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Bounded(host.FirstQueryStarted.Task, "legacy reset query admission");
        await Bounded(widget.WhenWarmStateIdleAsync(), "legacy whole-state reset");

        var reset = JsonSerializer.Deserialize<GameLauncherPrivateState>(state.Json!);
        Assert.AreEqual(GameLauncherPrivateState.CurrentVersion, reset!.Version);
        Assert.AreEqual(0, reset.Items.Count);
        Assert.AreEqual(0, reset.FavoriteSavedIds.Count);
        Assert.AreEqual(0, reset.RecentSavedIds.Count);
        Assert.AreEqual(0, reset.ManualSavedIds.Count);
        Assert.AreEqual(0, reset.ExcludedSavedIds.Count);
        Assert.IsFalse(Nodes(Snapshot(widget, 13).Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Stale", StringComparison.Ordinal)));

        page.SetResult(new([Item(0)], null, null, "revision-1"));
        await Bounded(widget.WhenLibraryIdleAsync(), "authoritative reconciliation");

        Assert.AreEqual(GameLauncherPrivateState.CurrentVersion, widget.Organization.Version);
        Assert.IsFalse(widget.Organization.Items.Any(item => item.SavedId == "saved-stale"));
        Assert.AreEqual(0, widget.Organization.FavoriteSavedIds.Count);
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        Assert.AreEqual(0, widget.Organization.RecentSavedIds.Count);
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count);
        Assert.AreEqual(0, widget.Organization.ExcludedSavedIds.Count);
        Assert.IsFalse(Nodes(Snapshot(widget, 14).Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Stale", StringComparison.Ordinal)));
        await Background(widget);
    }

    [TestMethod]
    public async Task FailedPersistenceLeavesCommittedOrganizationUnchanged()
    {
        var display = new GameLauncherDisplayItem("saved-a", "A", "Steam");
        var baseline = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, [display]);
        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await GameLauncherStateStore.SaveAsync(
                state => GameLauncherOrganizationPolicy.SetFavorite(
                    state, display, favorite: true),
                (_, _, _) => ValueTask.FromException<WidgetPrivateStateMutation>(
                    new IOException("fixture")),
                _ => ValueTask.FromResult(new WidgetPrivateStateValue<GameLauncherPrivateState>(
                    true, baseline, 1)),
                baseline, 1, CancellationToken.None));
        Assert.AreEqual(0, baseline.FavoriteSavedIds.Count);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AddGamesRouteShowsAllTrustedKindsAndRestoresLibraryFocus()
    {
        var host = new FakeHost(3)
        {
            ItemFactory = index => WithPresentation(Item(index), kind: index switch
                {
                    0 => WidgetAppLibraryKind.Game,
                    1 => WidgetAppLibraryKind.Application,
                    _ => WidgetAppLibraryKind.Unknown,
                }),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        await widget.OnActionAsync(new("game-launcher.add.open", "game-launcher.add.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "add-games route load");
        Assert.IsNull(host.Queries[^1].Query.Kind);
        var addSnapshot = Snapshot(widget, 70);
        var automatic = Nodes(addSnapshot.Root).Single(node =>
            node.ActionId == "game-launcher.manual.included");
        Assert.IsTrue(automatic.IsDisabled);
        StringAssert.Contains(automatic.AccessibilityLabel!, "Game");
        var addTiles = Nodes(addSnapshot.Root)
            .Where(node => node.ActionId == "game-launcher.manual.toggle").ToArray();
        Assert.AreEqual(2, addTiles.Length);
        StringAssert.Contains(addTiles[0].AccessibilityLabel!, "Application");
        StringAssert.Contains(addTiles[1].AccessibilityLabel!, "Unknown");

        await widget.OnActionAsync(new("game-launcher.manual.toggle", addTiles[0].Id));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            widget.Organization.ManualSavedIds.ToArray());
        await widget.OnActionAsync(new("game-launcher.manual.toggle", addTiles[0].Id));
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count);

        await widget.OnActionAsync(new("game-launcher.add.back", "game-launcher.add.back"));
        await Bounded(widget.WhenLibraryIdleAsync(), "library route restore");
        Assert.AreEqual("game-launcher.add.open", Snapshot(widget, 71).InitialFocusId);
        Assert.AreEqual(WidgetAppLibraryKind.Game, host.Queries[^1].Query.Kind);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RunningRouteConfirmsExactObservationBeforeManualAdd()
    {
        var host = new FakeHost(1)
        {
            RunningObservation = new([
                new("saved-00000", "Automatic game", WidgetAppLibraryKind.Game, "Steam"),
                new("saved-running", "Visible app", WidgetAppLibraryKind.Application,
                    "Windows"),
            ], "running-revision"),
            ConfirmRunningHandler = request => request.Revision == "running-revision"
                ? InstalledItem(
                    "app-current-running", request.SavedId, "Visible app",
                    WidgetAppLibraryKind.Application, "source-windows", "Windows")
                : null,
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        await widget.OnActionAsync(new(
            "game-launcher.running.open", "game-launcher.running.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "running route load");
        var snapshot = Snapshot(widget, 700);
        var automatic = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "game-launcher.manual.included");
        Assert.IsTrue(automatic.IsDisabled);
        var available = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "game-launcher.manual.toggle");
        await widget.OnActionAsync(new("game-launcher.manual.toggle", available.Id));

        Assert.AreEqual(1, host.RunningConfirmations.Count);
        Assert.AreEqual("saved-running", host.RunningConfirmations[0].SavedId);
        Assert.AreEqual("running-revision", host.RunningConfirmations[0].Revision);
        CollectionAssert.AreEqual(new[] { "saved-running" },
            widget.Organization.ManualSavedIds.ToArray());

        host.RunningObservation = new([], "replacement-revision");
        await widget.OnActionAsync(new("game-launcher.refresh", "game-launcher.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "running route refresh");
        Assert.AreEqual(0, widget.Collection.Items.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task MalformedRunningConfirmationCannotMutateOrganization()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(1, state)
        {
            RunningObservation = new([
                new("saved-running", "Visible app", WidgetAppLibraryKind.Application,
                    "Windows"),
            ], "running-revision"),
            ConfirmRunningHandler = request => WithPresentation(
                InstalledItem(
                    "app-current-running", request.SavedId, "Visible app",
                    WidgetAppLibraryKind.Application, "source-windows", "Windows"),
                source: "bad\nsource"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var revision = state.Revision;
        var retained = widget.Organization.Items
            .Select(item => item.SavedId).ToArray();

        await widget.OnActionAsync(new(
            "game-launcher.running.open", "game-launcher.running.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "running route load");
        var available = Nodes(Snapshot(widget, 700).Root).Single(node =>
            node.ActionId == "game-launcher.manual.toggle");
        WidgetCapabilityException? failure = null;
        try
        {
            await widget.OnActionAsync(new(
                "game-launcher.manual.toggle", available.Id));
        }
        catch (WidgetCapabilityException exception)
        {
            failure = exception;
        }

        Assert.AreEqual("malformed_response", failure?.ErrorCode);
        Assert.AreEqual(revision, state.Revision);
        CollectionAssert.AreEqual(retained,
            widget.Organization.Items.Select(item => item.SavedId).ToArray());
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ManualEntrySurvivesRestartButLaunchStillRevalidatesExactSavedId()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(2, state)
        {
            ItemFactory = index => WithPresentation(Item(index), kind: index == 1
                    ? WidgetAppLibraryKind.Application
                    : WidgetAppLibraryKind.Game),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await widget.OnActionAsync(new("game-launcher.add.open", "game-launcher.add.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "manual add route");
        var application = Nodes(Snapshot(widget, 72).Root)
            .Single(node => node.ActionId == "game-launcher.manual.toggle" &&
                node.AccessibilityLabel!.Contains("Application", StringComparison.Ordinal));
        await widget.OnActionAsync(new("game-launcher.manual.toggle", application.Id));
        await Background(widget);

        var restartedHost = new FakeHost(1, state)
        {
            ResolveHandler = request => request.SavedIds.Select(_ =>
                WithPresentation(Item(1) with { AppId = "app-fresh-manual" },
                    kind: WidgetAppLibraryKind.Application)).ToArray(),
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        var manualTile = Nodes(Snapshot(restarted, 73).Root)
            .Single(node => node.ActionId == "game-launcher.launch" &&
                node.AccessibilityLabel!.Contains("Game 00001", StringComparison.Ordinal));
        await restarted.OnActionAsync(new("game-launcher.launch", manualTile.Id));

        CollectionAssert.AreEqual(new[] { "saved-00001" },
            restartedHost.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-fresh-manual" },
            restartedHost.Launches.ToArray());
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task FullProviderPageAndMaximumFixedSlicesStayIndependentlyBounded()
    {
        var recentIds = Enumerable.Range(64, GameLauncherPrivateState.MaximumRecentItems)
            .Reverse().Select(index => $"saved-{index:D5}").ToArray();
        var manualIds = Enumerable.Range(96, GameLauncherPrivateState.MaximumManualItems)
            .Select(index => $"saved-{index:D5}").ToArray();
        var displays = recentIds.Concat(manualIds).Select(savedId =>
        {
            var index = int.Parse(savedId.AsSpan(savedId.LastIndexOf('-') + 1));
            return new GameLauncherDisplayItem(savedId, $"Game {index:D5}",
                index % 2 == 0 ? "Steam" : "Windows");
        }).ToArray();
        var persisted = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, displays)
        {
            RecentSavedIds = recentIds,
            ManualSavedIds = manualIds,
            FavoriteSavedIds = [recentIds[0], manualIds[0]],
        };
        var host = new FakeHost(LauncherWidget.PageSize, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1))
        {
            ItemFactory = index => WithPresentation(Item(index), kind: index >= 96
                    ? WidgetAppLibraryKind.Application
                    : WidgetAppLibraryKind.Game),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        Assert.AreEqual(LauncherWidget.PageSize, widget.Collection.Items.Count,
            "fixed rows must not enter the cursor page");
        await widget.OnActionAsync(new(
            "game-launcher.filter.recent", "game-launcher.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "maximum fixed-slice reload");
        Assert.AreEqual(LauncherWidget.PageSize, widget.Collection.Items.Count);
        Assert.AreEqual(LauncherWidget.PageSize, host.MaximumRequestedLimit);
        CollectionAssert.AreEquivalent(recentIds.Concat(manualIds).ToArray(),
            host.ResolveRequests[^1].ToArray());

        var snapshot = Snapshot(widget, 75);
        var launches = Nodes(snapshot.Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();
        Assert.AreEqual(LauncherWidget.PageSize +
            GameLauncherPrivateState.MaximumRecentItems +
            GameLauncherPrivateState.MaximumManualItems, launches.Length);
        Assert.AreEqual(LauncherWidget.PageSize,
            launches.Count(node => node.CollectionItemKey is not null),
            "only provider rows participate in cursor anchor accounting");
        Assert.AreEqual(GameLauncherIdentity.FocusId("grid",
            GameLauncherIdentity.Key(recentIds[0])), launches[0].Id);
        Assert.AreEqual(launches.Length,
            launches.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count());

        await widget.OnActionAsync(new(
            "game-launcher.filter.sort", "game-launcher.filter.sort"));
        await Bounded(widget.WhenLibraryIdleAsync(), "fixed-slice descending sort");
        var sortedFixed = Nodes(Snapshot(widget, 751).Root)
            .Where(node => node.ActionId == "game-launcher.launch" &&
                node.CollectionItemKey is null).ToArray();
        Assert.AreEqual(GameLauncherIdentity.FocusId("grid",
            GameLauncherIdentity.Key(recentIds[0])), sortedFixed[0].Id,
            "recent order remains exact instead of following catalog sort");
        Assert.AreEqual(GameLauncherIdentity.FocusId("grid",
            GameLauncherIdentity.Key(manualIds[^1])),
            sortedFixed[GameLauncherPrivateState.MaximumRecentItems].Id,
            "manual fixed rows follow the selected display sort");

        await widget.OnActionAsync(new(
            "game-launcher.filter.source", "game-launcher.filter.source"));
        await Bounded(widget.WhenLibraryIdleAsync(), "fixed-slice source filter");
        var sourceFixed = Nodes(Snapshot(widget, 752).Root)
            .Where(node => node.ActionId == "game-launcher.launch" &&
                node.CollectionItemKey is null).ToArray();
        Assert.AreEqual(32, sourceFixed.Length);
        Assert.IsTrue(sourceFixed.All(node =>
            node.AccessibilityLabel!.Contains("Steam", StringComparison.Ordinal)));

        await widget.OnActionAsync(new(
            "game-launcher.filter.favorites", "game-launcher.filter.favorites"));
        await Bounded(widget.WhenLibraryIdleAsync(), "fixed-slice favorite intersection");
        var favoriteFixed = Nodes(Snapshot(widget, 753).Root)
            .Where(node => node.ActionId == "game-launcher.launch" &&
                node.CollectionItemKey is null).ToArray();
        Assert.AreEqual(1, favoriteFixed.Length);
        Assert.AreEqual(GameLauncherIdentity.FocusId("grid",
            GameLauncherIdentity.Key(manualIds[0])), favoriteFixed[0].Id);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task LaterPageRecentLeadsColdFirstPageAndDeduplicatesTraversal()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(130, state)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await widget.OnActionAsync(new("game-launcher.next", "game-launcher.next"));
        await Bounded(widget.WhenLibraryIdleAsync(), "later catalog page");
        var laterId = GameLauncherIdentity.FocusId(
            "grid", GameLauncherIdentity.Key("saved-00100"));
        await widget.OnActionAsync(new("game-launcher.launch", laterId));
        CollectionAssert.AreEqual(new[] { "saved-00100" },
            widget.Organization.RecentSavedIds.ToArray());
        await Background(widget);

        var restartedHost = new FakeHost(130, state)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        Assert.IsFalse(restarted.Collection.Items.Any(item =>
            item.Value.SavedId == "saved-00100"));
        await restarted.OnActionAsync(new(
            "game-launcher.filter.recent", "game-launcher.filter.recent"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "cold recent-first resolution");
        var first = Nodes(Snapshot(restarted, 76).Root)
            .First(node => node.ActionId == "game-launcher.launch");
        Assert.AreEqual(laterId, first.Id);
        await restarted.OnActionAsync(new("game-launcher.launch", first.Id));
        CollectionAssert.AreEqual(new[] { "saved-00100" },
            restartedHost.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00100" }, restartedHost.Launches.ToArray());

        await restarted.OnActionAsync(new("game-launcher.next", "game-launcher.next"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "recent catalog overlap traversal");
        Assert.AreEqual(1, Nodes(Snapshot(restarted, 77).Root).Count(node =>
            node.ActionId == "game-launcher.launch" && node.Id == laterId));
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AutomaticGamesAreIncludedAndCannotRetainManualMembership()
    {
        var display = new GameLauncherDisplayItem(
            "saved-00000", "Game 00000", "Steam");
        var persisted = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, [display])
        {
            ManualSavedIds = [display.SavedId],
            FavoriteSavedIds = [display.SavedId],
            RecentSavedIds = [display.SavedId],
        };
        var host = new FakeHost(1, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count,
            "fresh classification removes obsolete manual Game membership");
        CollectionAssert.AreEqual(new[] { display.SavedId },
            widget.Organization.FavoriteSavedIds.ToArray(),
            "automatic membership cleanup must preserve unrelated favorites");
        CollectionAssert.AreEqual(new[] { display.SavedId },
            widget.Organization.RecentSavedIds.ToArray(),
            "automatic membership cleanup must preserve unrelated recents");

        await widget.OnActionAsync(new("game-launcher.add.open", "game-launcher.add.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "automatic game add route");
        var automatic = Nodes(Snapshot(widget, 78).Root).Single(node =>
            node.ActionId == "game-launcher.manual.included");
        Assert.IsTrue(automatic.IsDisabled);
        StringAssert.Contains(automatic.AccessibilityLabel!, "Included automatically");
        await widget.OnActionAsync(new("game-launcher.manual.toggle", automatic.Id));
        Assert.AreEqual(0, widget.Organization.ManualSavedIds.Count);
        await Background(widget);
    }

    [TestMethod]
    public async Task ManualCasReplayPreservesFavoritesGroupsAndRecentOrder()
    {
        var a = new GameLauncherDisplayItem("saved-a", "A", "Steam");
        var b = new GameLauncherDisplayItem("saved-b", "B", "Windows");
        var c = new GameLauncherDisplayItem("saved-c", "C", "Xbox");
        var baseline = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, [a, b])
        {
            FavoriteSavedIds = [a.SavedId],
            RecentSavedIds = [b.SavedId],
        };
        var latest = baseline with
        {
            Items = [c, a, b],
            RecentSavedIds = [c.SavedId, b.SavedId],
        };
        GameLauncherPrivateState? written = null;
        var attempt = 0;
        await GameLauncherStateStore.SaveAsync(
            state => GameLauncherOrganizationPolicy.SetManual(state, a, true),
            (value, _, _) =>
            {
                if (attempt++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "fixture"));
                written = value;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<GameLauncherPrivateState>(
                true, latest, 2)), baseline, 1, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { a.SavedId }, written!.ManualSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { a.SavedId }, written.FavoriteSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { c.SavedId, b.SavedId },
            written.RecentSavedIds.ToArray());
    }

    [TestMethod]
    public void ManualMembershipIsBoundedAndReplacementIdentityIsIndependent()
    {
        var state = GameLauncherPrivateState.Empty;
        for (var index = 0; index < GameLauncherPrivateState.MaximumManualItems; index++)
        {
            var display = new GameLauncherDisplayItem(
                $"saved-{index:D5}", $"Game {index}", "Fixture");
            var mutation = GameLauncherOrganizationPolicy.SetManual(state, display, true);
            Assert.IsTrue(mutation.Accepted);
            state = mutation.State;
        }
        var overflow = GameLauncherOrganizationPolicy.SetManual(state,
            new("saved-overflow", "Overflow", "Fixture"), true);
        Assert.IsFalse(overflow.Accepted);
        Assert.AreEqual(GameLauncherPrivateState.MaximumManualItems,
            overflow.State.ManualSavedIds.Count);

        var replacement = GameLauncherOrganizationPolicy.ProjectPage(state,
            [GameLauncherItem.From(Item(0) with { SavedId = "saved-replacement" })]);
        Assert.IsFalse(replacement.ManualSavedIds.Contains(
            "saved-replacement", StringComparer.Ordinal));
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingManualEntryStaysVisibleButCannotAuthorizeLaunch()
    {
        var display = new GameLauncherDisplayItem(
            "saved-00999", "Unavailable manual game", "Fixture");
        var persisted = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, [display])
        {
            ManualSavedIds = [display.SavedId],
        };
        var state = new WidgetTestPrivateState(JsonSerializer.Serialize(persisted), 1);
        var host = new FakeHost(1, state) { ResolveHandler = _ => [] };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var unavailable = Nodes(Snapshot(widget, 74).Root).Single(node =>
            node.ActionId == "game-launcher.launch" &&
            node.AccessibilityLabel!.Contains("Unavailable manual game",
                StringComparison.Ordinal));
        Assert.IsTrue(unavailable.IsDisabled);
        await widget.OnActionAsync(new("game-launcher.launch", unavailable.Id));
        Assert.AreEqual(0, host.Launches.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ExactLaunchRevalidatesSavedIdentity()
    {
        var host = new FakeHost(1)
        {
            ResolveHandler = request =>
                [Item(0) with { AppId = "app-current" }],
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(widget.RenderSnapshot("launcher.test", 2).Root)
            .Single(node => node.ActionId == "game-launcher.launch");

        await widget.OnActionAsync(new("game-launcher.launch", tile.Id));

        CollectionAssert.AreEqual(new[] { "saved-00000" }, host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-current" }, host.Launches.ToArray());
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            widget.Organization.RecentSavedIds.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AcceptedLaunchesDriveBoundedRecentOrderingAndFilter()
    {
        var host = new FakeHost(40)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        for (var index = 0; index < 35; index++)
        {
            var tileId = GameLauncherIdentity.FocusId("grid",
                GameLauncherIdentity.Key($"saved-{index:D5}"));
            await widget.OnActionAsync(new("game-launcher.launch", tileId));
        }

        Assert.AreEqual(GameLauncherPrivateState.MaximumRecentItems,
            widget.Organization.RecentSavedIds.Count);
        Assert.AreEqual("saved-00034", widget.Organization.RecentSavedIds[0]);
        Assert.AreEqual("saved-00003", widget.Organization.RecentSavedIds[^1]);

        await widget.OnActionAsync(new("game-launcher.filter.recent",
            "game-launcher.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "recent-first reload");
        var first = Nodes(Snapshot(widget, 60).Root)
            .First(node => node.ActionId == "game-launcher.launch");
        Assert.AreEqual(GameLauncherIdentity.FocusId("grid",
            GameLauncherIdentity.Key("saved-00034")), first.Id);

        await widget.OnActionAsync(new("game-launcher.filter.recent",
            "game-launcher.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "recent-only reload");
        CollectionAssert.AreEqual(widget.Organization.RecentSavedIds.ToArray(),
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ImmediateRecentPromotionRetainsCurrentItemAndRelaunchesExactly()
    {
        const string artwork = "library.art.0123456789abcdef0123456789abcdef";
        var resolveGeneration = 0;
        var host = new FakeHost(3)
        {
            ItemFactory = index => WithPresentation(Item(index),
                displayName: $"Current game {index}", source: "Current catalog",
                artworkHandle: artwork),
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        host.ResolveHandler = request => request.SavedIds.Select(savedId =>
        {
            var index = int.Parse(savedId.AsSpan(savedId.LastIndexOf('-') + 1));
            return WithPresentation(
                host.ItemFactory(index) with
                    { AppId = $"fresh-app-{++resolveGeneration}" },
                displayName: $"Resolved game {index}", source: "Resolved catalog");
        }).ToArray();
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await widget.OnActionAsync(new(
            "game-launcher.filter.recent", "game-launcher.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "empty recent-first reload");
        var queriesBeforeLaunch = host.Queries.Count;
        var promotedId = GameLauncherIdentity.FocusId(
            "grid", GameLauncherIdentity.Key("saved-00001"));

        await widget.OnActionAsync(new("game-launcher.launch", promotedId));

        var promotedSnapshot = Snapshot(widget, 601);
        var launchTiles = Nodes(promotedSnapshot.Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();
        Assert.AreEqual(3, launchTiles.Length);
        Assert.AreEqual(promotedId, launchTiles[0].Id);
        Assert.AreEqual(1, launchTiles.Count(node => node.Id == promotedId),
            "the promoted identity must not remain duplicated in the catalog section");
        Assert.IsFalse(launchTiles[0].IsDisabled ?? false);
        Assert.IsNull(launchTiles[0].CollectionItemKey,
            "the promoted fixed row must stay outside cursor anchor accounting");
        Assert.AreEqual(artwork,
            Nodes(launchTiles[0]).Single(node => node.ArtworkHandle is not null).ArtworkHandle);
        StringAssert.Contains(launchTiles[0].AccessibilityLabel!, "Current game 1");
        StringAssert.Contains(launchTiles[0].AccessibilityLabel!, "Current catalog");
        Assert.AreEqual(queriesBeforeLaunch, host.Queries.Count,
            "promotion must not reload the provider page");

        await widget.OnActionAsync(new("game-launcher.launch", promotedId));

        Assert.AreEqual(2, host.ResolveRequests.Count);
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[0].ToArray());
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[1].ToArray());
        CollectionAssert.AreEqual(new[] { "fresh-app-1", "fresh-app-2" },
            host.Launches.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task SourceHealthRetainsPartialRowsRejectsStaleAndRecovers()
    {
        var staleStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var currentReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var host = new FakeHost(2)
        {
            QueryHandler = (_, _) => Interlocked.Increment(ref calls) switch
            {
                1 => ValueTask.FromResult(SourcePage(
                    [Source("source-windows", "Windows",
                        WidgetAppLibrarySourceHealth.Healthy, 1, "healthy"),
                     Source("source-steam", "Steam",
                        WidgetAppLibrarySourceHealth.Degraded, 1,
                        "source_degraded")], "revision-1")),
                2 => WaitForStaleSourcePage(),
                3 => ReturnRecoveredPage(),
                4 => ValueTask.FromResult(SourcePage(
                    [Source("source-windows", "Windows",
                        WidgetAppLibrarySourceHealth.Healthy, 3, "healthy")],
                    "revision-3")),
                _ => throw new InvalidOperationException("Unexpected source query."),
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var partial = Snapshot(widget, 602);
        StringAssert.Contains(Nodes(partial.Root).Single(node =>
            node.Id == "game-launcher.source.source-windows").Text!, "Healthy");
        StringAssert.Contains(Nodes(partial.Root).Single(node =>
            node.Id == "game-launcher.source.source-steam").Text!, "Degraded");
        Assert.IsTrue(Nodes(partial.Root).Where(node =>
            node.ActionId == "game-launcher.launch").All(node =>
                !(node.IsDisabled ?? false)),
            "partial source health must not suppress usable games");
        var initialFocus = partial.InitialFocusId;

        await widget.OnActionAsync(new("game-launcher.refresh", "game-launcher.refresh"));
        await Bounded(staleStarted.Task, "stale source refresh admission");
        var refreshing = Snapshot(widget, 603);
        Assert.AreEqual(2, Nodes(refreshing.Root).Count(node =>
            node.Id.StartsWith("game-launcher.source.", StringComparison.Ordinal) &&
            (node.Text ?? string.Empty).Contains("Refreshing", StringComparison.Ordinal)));

        await widget.OnActionAsync(new(
            "game-launcher.filter.source", "game-launcher.filter.source"));
        releaseStale.TrySetResult(SourcePage(
            [Source("source-windows", "Windows",
                WidgetAppLibrarySourceHealth.Healthy, 2, "healthy"),
             Source("source-steam", "Steam",
                WidgetAppLibrarySourceHealth.Unavailable, 2,
                "source_unavailable")], "revision-stale"));
        await Bounded(currentReturned.Task, "recovered source revision");
        await Bounded(widget.WhenLibraryIdleAsync(), "stale source drain");
        var recovered = Snapshot(widget, 604);
        StringAssert.Contains(Nodes(recovered.Root).Single(node =>
            node.Id == "game-launcher.source.source-steam").Text!, "Healthy");
        Assert.IsFalse(Nodes(recovered.Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Unavailable", StringComparison.Ordinal)));
        Assert.AreEqual(initialFocus, recovered.InitialFocusId);

        await widget.OnActionAsync(new("game-launcher.refresh", "game-launcher.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "source disappearance refresh");
        var disappeared = Snapshot(widget, 605);
        Assert.IsFalse(Nodes(disappeared.Root).Any(node =>
            node.Id == "game-launcher.source.source-steam"));
        StringAssert.Contains(Nodes(disappeared.Root).Single(node =>
            node.Id == "game-launcher.source.source-windows").Text!, "Healthy");
        await Background(widget);

        ValueTask<WidgetAppLibraryPage> WaitForStaleSourcePage()
        {
            staleStarted.TrySetResult();
            return new(releaseStale.Task);
        }

        ValueTask<WidgetAppLibraryPage> ReturnRecoveredPage()
        {
            currentReturned.TrySetResult();
            return ValueTask.FromResult(SourcePage(
                [Source("source-windows", "Windows",
                    WidgetAppLibrarySourceHealth.Healthy, 2, "healthy"),
                 Source("source-steam", "Steam",
                    WidgetAppLibrarySourceHealth.Healthy, 2, "healthy")],
                "revision-2"));
        }

        WidgetAppLibraryPage SourcePage(
            IReadOnlyList<WidgetAppLibrarySource> sources,
            string revision) => new(
                [Item(0), Item(1)], null, null, revision)
            {
                Sources = sources,
            };

        static WidgetAppLibrarySource Source(
            string id, string label, WidgetAppLibrarySourceHealth health,
            long revision, string statusCode) =>
            new(id, label, health, revision, statusCode);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RecentMutationConflictPreservesOrganizationAndConcurrentOrder()
    {
        var a = new GameLauncherDisplayItem("saved-a", "A", "Steam");
        var b = new GameLauncherDisplayItem("saved-b", "B", "Steam");
        var c = new GameLauncherDisplayItem("saved-c", "C", "Windows");
        var d = new GameLauncherDisplayItem("saved-d", "D", "Windows");
        var e = new GameLauncherDisplayItem("saved-e", "E", "Xbox");
        var baseline = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, [a, b, c, d])
        {
            FavoriteSavedIds = [c.SavedId],
            VariantGroups =
            [
                new(GameLauncherIdentity.GroupId(c.SavedId, d.SavedId),
                    [c.SavedId, d.SavedId], d.SavedId),
            ],
            RecentSavedIds = [a.SavedId, b.SavedId],
        };
        var latest = baseline with
        {
            Items = [e, a, b, c, d],
            RecentSavedIds = [e.SavedId, a.SavedId, b.SavedId],
        };
        GameLauncherPrivateState? written = null;
        var attempts = 0;

        var result = await GameLauncherStateStore.SaveAsync(
            state => GameLauncherOrganizationPolicy.RecordRecent(state, b),
            (state, _, _) =>
            {
                if (attempts++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "fixture"));
                written = state;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<GameLauncherPrivateState>(
                true, latest, 2)), baseline, 1, CancellationToken.None);

        Assert.IsTrue(result.Saved);
        CollectionAssert.AreEqual(new[] { b.SavedId, e.SavedId, a.SavedId },
            written!.RecentSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { c.SavedId },
            written.FavoriteSavedIds.ToArray());
        Assert.AreEqual(d.SavedId, written.VariantGroups.Single().PreferredSavedId);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RecentHistorySurvivesRestartAndClearsWithoutOrganizationLoss()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(2, state)
        {
            LaunchHandler = (_, _) => ValueTask.FromResult(new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var first = Nodes(Snapshot(widget, 63).Root)
            .First(node => node.ActionId == "game-launcher.launch");
        await widget.OnActionAsync(new("game-launcher.favorite", first.Id));
        await widget.OnActionAsync(new("game-launcher.launch", first.Id));
        await Background(widget);

        var restartedHost = new FakeHost(2, state);
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        await Bounded(restarted.WhenWarmStateIdleAsync(), "recent warm-state restart");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restarted.Organization.RecentSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restarted.Organization.FavoriteSavedIds.ToArray());

        await restarted.OnActionAsync(new("game-launcher.recent.clear",
            "game-launcher.recent.clear"));
        Assert.AreEqual(0, restarted.Organization.RecentSavedIds.Count);
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restarted.Organization.FavoriteSavedIds.ToArray());
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task FailedStaleAndCanceledLaunchesNeverRecordRecentHistory()
    {
        var missing = new FakeHost(1) { ResolveHandler = _ => [] };
        var missingWidget = Create(missing);
        await Interactive(missingWidget);
        await Ready(missingWidget, missing);
        var missingTile = Nodes(Snapshot(missingWidget, 61).Root)
            .Single(node => node.ActionId == "game-launcher.launch");
        await missingWidget.OnActionAsync(new("game-launcher.launch", missingTile.Id));
        Assert.AreEqual(0, missingWidget.Organization.RecentSavedIds.Count);
        await Background(missingWidget);

        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLaunchObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = new FakeHost(1)
        {
            LaunchHandler = (_, _) =>
            {
                admitted.TrySetResult();
                return new ValueTask<WidgetAppLaunchObservation>(release.Task);
            },
        };
        var staleWidget = Create(stale);
        await Interactive(staleWidget);
        await Ready(staleWidget, stale);
        var staleTile = Nodes(Snapshot(staleWidget, 62).Root)
            .Single(node => node.ActionId == "game-launcher.launch");
        var launch = staleWidget.OnActionAsync(
            new("game-launcher.launch", staleTile.Id)).AsTask();
        await Bounded(admitted.Task, "stale recent admission");
        await staleWidget.OnActionAsync(new("game-launcher.refresh", "game-launcher.refresh"));
        await Bounded(staleWidget.WhenLibraryIdleAsync(), "stale recent replacement");
        release.TrySetResult(new(WidgetAppLaunchObservationState.Running, true, false));
        await Bounded(launch, "stale recent completion");
        Assert.AreEqual(0, staleWidget.Organization.RecentSavedIds.Count);
        await Background(staleWidget);
    }

    [TestMethod]
    public void MissingAndReplacementIdentitiesDoNotInheritRecentHistory()
    {
        var old = new GameLauncherDisplayItem("saved-old", "Game", "Steam");
        var state = new GameLauncherPrivateState(
            GameLauncherPrivateState.CurrentVersion, [old])
        {
            RecentSavedIds = [old.SavedId],
        };
        var replacement = GameLauncherItem.From(WithPresentation(
            Item(0) with { SavedId = "saved-new" }, displayName: old.DisplayName));

        var projected = GameLauncherOrganizationPolicy.ProjectPage(state, [replacement]);

        CollectionAssert.AreEqual(new[] { old.SavedId },
            projected.RecentSavedIds.ToArray());
        Assert.IsTrue(projected.Items.Any(item => item.SavedId == old.SavedId));
        Assert.IsTrue(projected.Items.Any(item => item.SavedId == "saved-new"));
    }

    [TestMethod, Timeout(30_000)]
    public async Task AdapterEvidenceProjectsExactLifecyclePerSavedIdentity()
    {
        var host = new FakeHost(3)
        {
            LaunchHandler = (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                var state = request.AppId.EndsWith("0", StringComparison.Ordinal)
                    ? WidgetAppLaunchObservationState.Running
                    : request.AppId.EndsWith("1", StringComparison.Ordinal)
                        ? WidgetAppLaunchObservationState.Ended
                        : WidgetAppLaunchObservationState.LauncherStarted;
                return ValueTask.FromResult(new WidgetAppLaunchObservation(
                    state, SupportsRunning: true, SupportsEnded: true));
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var first = Nodes(Snapshot(widget, 20).Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();

        await widget.OnActionAsync(new("game-launcher.launch", first[0].Id));
        await widget.OnActionAsync(new("game-launcher.launch", first[1].Id));
        await widget.OnActionAsync(new("game-launcher.launch", first[2].Id));

        var tiles = Nodes(Snapshot(widget, 21).Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();
        StringAssert.Contains(tiles[0].AccessibilityLabel!, "Running");
        StringAssert.Contains(tiles[1].AccessibilityLabel!, "Ended");
        StringAssert.Contains(tiles[2].AccessibilityLabel!, "Launcher started");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task UnsupportedAdapterUsesRequestAcceptedAndDoesNotClaimRunning()
    {
        var host = new FakeHost(1);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(Snapshot(widget, 22).Root)
            .Single(node => node.ActionId == "game-launcher.launch");

        await widget.OnActionAsync(new("game-launcher.launch", tile.Id));

        var rendered = Nodes(Snapshot(widget, 23).Root)
            .Single(node => node.ActionId == "game-launcher.launch");
        StringAssert.Contains(rendered.AccessibilityLabel!, "Request accepted");
        Assert.IsFalse(rendered.AccessibilityLabel!.Contains("Running", StringComparison.Ordinal));
        Assert.AreEqual(0, widget.Organization.RecentSavedIds.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task LateCanceledEvidenceCannotPublishAfterDeactivation()
    {
        var admitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLaunchObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(1)
        {
            LaunchHandler = (_, _) =>
            {
                admitted.TrySetResult();
                return new ValueTask<WidgetAppLaunchObservation>(release.Task);
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(Snapshot(widget, 24).Root)
            .Single(node => node.ActionId == "game-launcher.launch");
        var launch = widget.OnActionAsync(new("game-launcher.launch", tile.Id)).AsTask();
        await Bounded(admitted.Task, "launch evidence admission");
        StringAssert.Contains(
            Nodes(Snapshot(widget, 25).Root)
                .Single(node => node.ActionId == "game-launcher.launch")
                .AccessibilityLabel!,
            "Pending");

        var background = Background(widget);
        Assert.IsFalse(background.IsCompleted);
        release.TrySetResult(new(WidgetAppLaunchObservationState.Running, true, false));
        await Bounded(background, "launch lifecycle drain");
        await Bounded(launch, "late launch completion");

        Assert.IsFalse(Nodes(Snapshot(widget, 26).Root).Any(node =>
            node.AccessibilityLabel?.Contains("Running", StringComparison.Ordinal) == true));
    }

    [TestMethod, Timeout(30_000)]
    public async Task RefreshGenerationRejectsLateLaunchEvidence()
    {
        var admitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLaunchObservation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(1)
        {
            LaunchHandler = (_, _) =>
            {
                admitted.TrySetResult();
                return new ValueTask<WidgetAppLaunchObservation>(release.Task);
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(Snapshot(widget, 28).Root)
            .Single(node => node.ActionId == "game-launcher.launch");
        var launch = widget.OnActionAsync(new("game-launcher.launch", tile.Id)).AsTask();
        await Bounded(admitted.Task, "stale launch admission");

        var revision = widget.Collection.Revision;
        await widget.OnActionAsync(new("game-launcher.refresh", "game-launcher.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "collection refresh");
        Assert.IsGreaterThan(revision, widget.Collection.Revision);
        release.TrySetResult(new(WidgetAppLaunchObservationState.Running, true, false));
        await Bounded(launch, "stale launch completion");

        Assert.IsFalse(Nodes(Snapshot(widget, 29).Root).Any(node =>
            node.AccessibilityLabel?.Contains("Running", StringComparison.Ordinal) == true));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingCurrentIdentityCannotLaunch()
    {
        var host = new FakeHost(1) { ResolveHandler = _ => [] };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(widget.RenderSnapshot("launcher.test", 3).Root)
            .Single(node => node.ActionId == "game-launcher.launch");

        await widget.OnActionAsync(new("game-launcher.launch", tile.Id));

        Assert.AreEqual(0, host.Launches.Count);
        Assert.AreEqual("Failed · The selected game is no longer installed",
            Nodes(widget.RenderSnapshot("launcher.test", 4).Root)
                .Single(node => node.Id == "game-launcher.status").Text);
        StringAssert.Contains(
            Nodes(widget.RenderSnapshot("launcher.test", 5).Root)
                .Single(node => node.ActionId == "game-launcher.launch")
                .AccessibilityLabel!, "Failed");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task LaunchStateRetentionIsBounded()
    {
        var host = new FakeHost(LauncherWidget.MaximumRetainedLaunchStates + 4);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 27).Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();

        foreach (var tile in tiles)
            await widget.OnActionAsync(new("game-launcher.launch", tile.Id));

        Assert.AreEqual(LauncherWidget.MaximumRetainedLaunchStates,
            widget.RetainedLaunchStateCount);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task SameTitleVariantsKeepDistinctIdentityAndExactRouting()
    {
        var host = new FakeHost(2)
        {
            ItemFactory = index => WithPresentation(Item(index),
                displayName: "Shared title", source: index == 0 ? "Steam" : "Windows"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 5).Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();

        Assert.AreEqual(2, tiles.Length);
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        Assert.AreNotEqual(tiles[0].Id, tiles[1].Id);
        StringAssert.Contains(tiles[0].AccessibilityLabel ?? string.Empty, "Steam");
        StringAssert.Contains(tiles[1].AccessibilityLabel ?? string.Empty, "Windows");
        await widget.OnActionAsync(new("game-launcher.launch", tiles[1].Id));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ViewOpensExactDetailsAndBackRestoresOriginTile()
    {
        var host = new FakeHost(2)
        {
            ItemFactory = index => WithPresentation(Item(index),
                displayName: "Shared title", source: index == 0 ? "Steam" : "Windows"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 40);
        var tiles = Nodes(library.Root)
            .Where(node => node.ActionId == "game-launcher.launch").ToArray();

        Assert.IsTrue(tiles[1].Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.View &&
            shortcut.ActionId == "game-launcher.details.open"));
        await widget.OnActionAsync(new("game-launcher.details.open", tiles[1].Id));
        var details = Snapshot(widget, 41);
        Assert.AreEqual("Shared title", Nodes(details.Root).Single(node =>
            node.Id == "game-launcher.details.title").Text);
        StringAssert.Contains(Nodes(details.Root).Single(node =>
            node.Id == "game-launcher.details.source").Text!, "Windows");
        Assert.AreEqual("game-launcher.details.launch", details.InitialFocusId);

        await widget.OnActionAsync(new("game-launcher.favorite",
            "game-launcher.details.favorite"));
        Assert.IsTrue(widget.Organization.FavoriteSavedIds.Contains(
            "saved-00001", StringComparer.Ordinal));

        await widget.OnActionAsync(new("game-launcher.launch",
            "game-launcher.details.launch"));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());

        var afterLaunch = Snapshot(widget, 42);
        var collectionRevision = widget.Collection.Revision;
        var collectionAnchor = widget.Collection.Anchor;
        var back = afterLaunch.Root.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.B);
        await widget.OnActionAsync(new(back.ActionId, "game-launcher.details.launch",
            ControllerButton.B, ControllerEventPhase.Pressed,
            InputScopeId: afterLaunch.ActiveInputScopeId));
        var returned = Snapshot(widget, 43);
        Assert.AreEqual(tiles[1].Id, returned.InitialFocusId);
        Assert.IsTrue(Nodes(returned.Root).Any(node => node.Id == tiles[1].Id));
        Assert.AreEqual("Windows", Nodes(returned.Root).Single(node =>
            node.Id == "game-launcher.hero.source").Text);
        Assert.AreEqual(collectionRevision, widget.Collection.Revision);
        Assert.AreEqual(collectionAnchor, widget.Collection.Anchor);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsPreserveContextShortcutsAndFailClosedWhenIdentityDisappears()
    {
        var host = new FakeHost(1);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 44);
        var tile = Nodes(library.Root).Single(node =>
            node.ActionId == "game-launcher.launch");
        Assert.IsTrue(tile.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.View &&
            shortcut.ActionId == "game-launcher.details.open"));

        await widget.OnActionAsync(new("game-launcher.details.open", tile.Id));
        var details = Snapshot(widget, 45);
        var detailScroll = Nodes(details.Root).Single(node =>
            node.Id == "game-launcher.details.scroll");
        Assert.IsTrue(detailScroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.X &&
            shortcut.ActionId == "game-launcher.favorite"));
        Assert.IsTrue(detailScroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.Y &&
            shortcut.ActionId == "game-launcher.hide"));
        Assert.IsTrue(detailScroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.LeftBumper &&
            shortcut.ActionId == "game-launcher.variant"));
        Assert.IsTrue(detailScroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.RightBumper &&
            shortcut.ActionId == "game-launcher.prefer"));

        host.QueryHandler = (_, _) => ValueTask.FromResult(
            new WidgetAppLibraryPage([], null, null, "removed"));
        await widget.OnActionAsync(new("game-launcher.refresh", "game-launcher.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "details disappearance refresh");
        var unavailable = Snapshot(widget, 46);
        StringAssert.Contains(Nodes(unavailable.Root).Single(node =>
            node.Id == "game-launcher.details.availability").Text!, "Unavailable");
        Assert.IsTrue(Nodes(unavailable.Root).Single(node =>
            node.Id == "game-launcher.details.launch").IsDisabled);
        await widget.OnActionAsync(new("game-launcher.launch",
            "game-launcher.details.launch"));
        Assert.AreEqual(0, host.Launches.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task PagedDetailsDoNotReplaceLibraryBumperSemantics()
    {
        var host = new FakeHost(LauncherWidget.PageSize + 1);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 47);
        var tile = Nodes(library.Root).First(node =>
            node.ActionId == "game-launcher.launch");

        await widget.OnActionAsync(new("game-launcher.details.open", tile.Id));
        var details = Snapshot(widget, 48);
        var scroll = Nodes(details.Root).Single(node =>
            node.Id == "game-launcher.details.scroll");
        Assert.IsFalse(scroll.Shortcuts.Any(shortcut => shortcut.Button is
            ControllerButton.LeftBumper or ControllerButton.RightBumper));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsVariantSelectionNamesEachExactStepAndReturnsForSecondChoice()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var library = Snapshot(widget, 49);
        var tiles = Nodes(library.Root).Where(node =>
            node.ActionId == "game-launcher.launch").ToArray();

        await widget.OnActionAsync(new("game-launcher.details.open", tiles[0].Id));
        var firstDetails = Snapshot(widget, 50);
        Assert.AreEqual("Choose another variant", Nodes(firstDetails.Root).Single(node =>
            node.Id == "game-launcher.details.variant").Text);
        await widget.OnActionAsync(new("game-launcher.variant",
            "game-launcher.details.variant"));

        var returned = Snapshot(widget, 51);
        Assert.IsFalse(Nodes(returned.Root).Any(node =>
            node.Id == "game-launcher.details.root"));
        Assert.AreEqual(tiles[0].Id, returned.InitialFocusId);
        StringAssert.Contains(Nodes(returned.Root).Single(node =>
            node.Id == "game-launcher.status").Text!,
            "Variant selection started with Game 00000");

        await widget.OnActionAsync(new("game-launcher.details.open", tiles[1].Id));
        var secondDetails = Snapshot(widget, 52);
        Assert.AreEqual("Group with selected game", Nodes(secondDetails.Root).Single(node =>
            node.Id == "game-launcher.details.variant").Text);
        await widget.OnActionAsync(new("game-launcher.variant",
            "game-launcher.details.variant"));

        var group = widget.Organization.VariantGroups.Single();
        CollectionAssert.AreEquivalent(new[] { "saved-00000", "saved-00001" },
            group.SavedIds.ToArray());
        StringAssert.Contains(Nodes(Snapshot(widget, 53).Root).Single(node =>
            node.Id == "game-launcher.details.feedback").Text!,
            "Grouped Game 00000 with Game 00001");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsVariantRemovalAndSameIdentityRemainExplicit()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 54).Root).Where(node =>
            node.ActionId == "game-launcher.launch").ToArray();
        await widget.OnActionAsync(new("game-launcher.variant", tiles[0].Id));
        await widget.OnActionAsync(new("game-launcher.variant", tiles[1].Id));
        Assert.AreEqual(1, widget.Organization.VariantGroups.Count);

        await widget.OnActionAsync(new("game-launcher.variant", tiles[0].Id));
        await widget.OnActionAsync(new("game-launcher.details.open", tiles[1].Id));
        var removal = Snapshot(widget, 55);
        Assert.AreEqual("Remove from variant group", Nodes(removal.Root).Single(node =>
            node.Id == "game-launcher.details.variant").Text);
        await widget.OnActionAsync(new("game-launcher.variant",
            "game-launcher.details.variant"));
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        StringAssert.Contains(Nodes(Snapshot(widget, 56).Root).Single(node =>
            node.Id == "game-launcher.details.feedback").Text!,
            "Removed Game 00001 from its variant group");

        var details = Snapshot(widget, 57);
        var back = details.Root.Shortcuts.Single(shortcut =>
            shortcut.Button == ControllerButton.B);
        await widget.OnActionAsync(new(back.ActionId, "game-launcher.details.variant",
            ControllerButton.B, ControllerEventPhase.Pressed,
            InputScopeId: details.ActiveInputScopeId));
        await widget.OnActionAsync(new("game-launcher.variant", tiles[0].Id));
        await widget.OnActionAsync(new("game-launcher.details.open", tiles[0].Id));
        var same = Snapshot(widget, 58);
        var sameAction = Nodes(same.Root).Single(node =>
            node.Id == "game-launcher.details.variant");
        Assert.AreEqual("Choose a different game", sameAction.Text);
        Assert.IsTrue(sameAction.IsDisabled);
        await widget.OnActionAsync(new("game-launcher.variant", sameAction.Id));
        StringAssert.Contains(Nodes(Snapshot(widget, 59).Root).Single(node =>
            node.Id == "game-launcher.details.feedback").Text!,
            "Choose a different game");
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task StaleVariantSeedFailsClosedAndHideClosesOnlyAfterCommit()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 60).Root).Where(node =>
            node.ActionId == "game-launcher.launch").ToArray();
        await widget.OnActionAsync(new("game-launcher.variant", tiles[0].Id));
        host.QueryHandler = (_, _) => ValueTask.FromResult(new WidgetAppLibraryPage(
            [Item(1)], null, null, "second-only"));
        await widget.OnActionAsync(new("game-launcher.refresh", "game-launcher.refresh"));
        await Bounded(widget.WhenLibraryIdleAsync(), "stale seed refresh");
        var second = Nodes(Snapshot(widget, 61).Root).Single(node =>
            node.ActionId == "game-launcher.launch");
        await widget.OnActionAsync(new("game-launcher.details.open", second.Id));
        await widget.OnActionAsync(new("game-launcher.variant",
            "game-launcher.details.variant"));
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        StringAssert.Contains(Nodes(Snapshot(widget, 62).Root).Single(node =>
            node.Id == "game-launcher.details.feedback").Text!,
            "first selected game is no longer available");

        await widget.OnActionAsync(new("game-launcher.hide", "game-launcher.details.hide"));
        Assert.IsTrue(widget.Organization.ExcludedSavedIds.Contains(
            "saved-00001", StringComparer.Ordinal));
        Assert.IsFalse(Nodes(Snapshot(widget, 63).Root).Any(node =>
            node.Id == "game-launcher.details.root"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DetailsVariantConflictReplaysExactPairAndPreservesConcurrentState()
    {
        var privateState = new WidgetTestPrivateState();
        var host = new FakeHost(3, privateState);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 64).Root).Where(node =>
            node.ActionId == "game-launcher.launch").ToArray();
        await widget.OnActionAsync(new("game-launcher.variant", tiles[0].Id));
        await widget.OnActionAsync(new("game-launcher.details.open", tiles[1].Id));

        var concurrent = widget.Organization with
        {
            FavoriteSavedIds = ["saved-00002"],
        };
        privateState.SimulateExternalWriteJson(JsonSerializer.Serialize(concurrent));
        await widget.OnActionAsync(new("game-launcher.variant",
            "game-launcher.details.variant"));

        Assert.IsTrue(widget.Organization.FavoriteSavedIds.Contains(
            "saved-00002", StringComparer.Ordinal));
        CollectionAssert.AreEquivalent(new[] { "saved-00000", "saved-00001" },
            widget.Organization.VariantGroups.Single().SavedIds.ToArray());
        StringAssert.Contains(Nodes(Snapshot(widget, 65).Root).Single(node =>
            node.Id == "game-launcher.details.feedback").Text!, "Grouped");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task CanceledDetailsVariantDoesNotClaimACommittedGroup()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tiles = Nodes(Snapshot(widget, 66).Root).Where(node =>
            node.ActionId == "game-launcher.launch").ToArray();
        await widget.OnActionAsync(new("game-launcher.variant", tiles[0].Id));
        await widget.OnActionAsync(new("game-launcher.details.open", tiles[1].Id));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await widget.OnActionAsync(new("game-launcher.variant",
                "game-launcher.details.variant"), canceled.Token));

        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        StringAssert.Contains(Nodes(Snapshot(widget, 67).Root).Single(node =>
            node.Id == "game-launcher.details.feedback").Text!,
            "Organization change was not saved");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AdjacentFailureRetainsLastGoodWindow()
    {
        var host = new FakeHost(300);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        host.FailAfterOffset = LauncherWidget.PageSize;
        await widget.OnActionAsync(new("game-launcher.next", "game-launcher.next"));
        await Bounded(widget.WhenLibraryIdleAsync(), "adjacent failure drain");

        Assert.AreEqual(LauncherWidget.PageSize, widget.Collection.Items.Count);
        Assert.IsNotNull(widget.Collection.Error);
        Assert.IsTrue(Nodes(widget.RenderSnapshot("launcher.test", 5).Root)
            .Any(node => node.Id == "game-launcher.retained-error"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ArtworkAndHeroRailRemainSemanticAndBounded()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = index => WithPresentation(Item(index), artworkHandle:
                "library.art.0123456789abcdef0123456789abcdef"),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var snapshot = widget.RenderSnapshot("launcher.test", 6);

        Assert.AreEqual(WidgetSurfaceMode.Wide, snapshot.Surface?.Mode);
        var rail = Nodes(snapshot.Root).Single(node =>
            node.Id == GameLauncherPresentation.ScrollId);
        Assert.AreEqual(ViewNodeKind.Scroll, rail.Kind);
        Assert.AreEqual(ScrollAxis.Horizontal, rail.ScrollAxis);
        Assert.AreEqual(2, Nodes(snapshot.Root).Count(node =>
            node.ArtworkHandle == "library.art.0123456789abcdef0123456789abcdef"));
        Assert.AreEqual("Game 00000", Nodes(snapshot.Root).Single(node =>
            node.Id == "game-launcher.hero.title").Text);
        Assert.IsLessThan(100, Nodes(snapshot.Root).Count());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RailFocusUpdatesHeroWithoutLaunchAndActionsRemainExact()
    {
        var host = new FakeHost(20);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var first = Snapshot(widget, 9_000);
        var tiles = Nodes(first.Root).Where(node =>
                node.ActionId == "game-launcher.launch")
            .ToArray();
        Assert.AreEqual("Game 00000", Nodes(first.Root).Single(node =>
            node.Id == "game-launcher.hero.title").Text);

        Assert.IsFalse(await Route(
            widget, first, ControllerButton.DPadRight, tiles[0].Id));
        var moved = Snapshot(widget, 9_001);
        Assert.AreEqual("Game 00001", Nodes(moved.Root).Single(node =>
            node.Id == "game-launcher.hero.title").Text);
        Assert.AreEqual(0, host.Launches.Count,
            "Focus movement must never authorize launch.");

        var movedSecond = Nodes(moved.Root).Where(node =>
                node.ActionId == "game-launcher.launch")
            .ElementAt(1);
        Assert.IsTrue(await Route(
            widget, moved, ControllerButton.A, movedSecond.Id));
        await WaitUntil(() => host.Launches.Count == 1);
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task DeactivationDrainsCancellationIgnoringPage()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(0)
        {
            QueryHandler = async (_, token) =>
            {
                started.TrySetResult();
                return await release.Task.ConfigureAwait(false);
            },
        };
        var widget = Create(host);
        await Visible(widget);
        await Bounded(started.Task, "cancellation-ignoring query admission");
        var transition = WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Background).AsTask();
        release.TrySetResult(new([Item(0)], null, null, "late"));
        await Bounded(transition, "cancellation-ignoring lifecycle drain");

        Assert.AreEqual(0, widget.Collection.Items.Count);
        Assert.AreEqual(WidgetPagedResourceStatus.NotLoaded, widget.Collection.Status);
    }

    private static LauncherWidget Create(FakeHost host) =>
        WidgetTestHost.Attach(new LauncherWidget(), host.Services());

    private static Task Visible(LauncherWidget widget) => Bounded(
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible).AsTask(),
        "visible lifecycle");

    private static Task Interactive(LauncherWidget widget) => Bounded(
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive).AsTask(),
        "interactive lifecycle");

    private static Task Background(LauncherWidget widget) => Bounded(
        WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background).AsTask(),
        "background lifecycle");

    private static async Task Ready(LauncherWidget widget, FakeHost host)
    {
        await Bounded(host.FirstQueryStarted.Task, "first provider query");
        await Bounded(widget.WhenLibraryIdleAsync(), "initial page drain");
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
    }

    private static async Task Bounded(Task task, string phase)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Timed out during {phase}.", exception);
        }
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 20_000; attempt++)
        {
            if (predicate()) return;
            await Task.Yield();
        }
        Assert.Fail("Condition did not become true.");
    }

    private static ValueTask<bool> Route(
        LauncherWidget widget,
        ViewSnapshot snapshot,
        ControllerButton button,
        string focusedElementId,
        ControllerEventPhase phase = ControllerEventPhase.Pressed) =>
        widget.OnControllerInputAsync(new ControllerInputEvent(
            button, phase, ControllerInputContext.OpenWidget,
            FocusedElementId: focusedElementId,
            Sequence: snapshot.Sequence,
            ActiveInputScopeId: snapshot.ActiveInputScopeId,
            SnapshotSequence: snapshot.Sequence));

    private static void AssertShortcutMap(
        ViewSnapshot snapshot,
        bool before,
        bool after)
    {
        var scroll = Nodes(snapshot.Root).Single(node =>
            node.Id == GameLauncherPresentation.ScrollId);
        Assert.AreEqual(before, scroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.LeftBumper &&
            shortcut.ActionId == "game-launcher.previous"));
        Assert.AreEqual(after, scroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.RightBumper &&
            shortcut.ActionId == "game-launcher.next"));
    }

    private static void AssertValidCollectionAnchor(ViewSnapshot snapshot)
    {
        var scroll = Nodes(snapshot.Root).SingleOrDefault(node =>
            node.Id == GameLauncherPresentation.ScrollId);
        if (scroll is null) return;
        var keys = Nodes(scroll).Where(node => node.CollectionItemKey is not null)
            .Select(node => node.CollectionItemKey!).ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0)
            Assert.IsNull(scroll.CollectionAnchorKey);
        else
            Assert.IsTrue(keys.Contains(scroll.CollectionAnchorKey ?? string.Empty),
                "The collection anchor must identify one currently rendered keyed row.");
    }

    private static IEnumerable<ViewNode> Nodes(ViewNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var node in Nodes(child)) yield return node;
    }

    private static ViewSnapshot Snapshot(LauncherWidget widget, long sequence)
    {
        try { return widget.RenderSnapshot("launcher.test", sequence); }
        catch (ProtocolValidationException exception)
        {
            Assert.Fail(string.Join(Environment.NewLine, exception.Errors.Select(error =>
                $"{error.Path}: {error.Code}: {error.Message}")));
            throw;
        }
    }

    private static WidgetAppLibraryItem Item(int index) =>
        InstalledItem(
            $"app-{index:D5}", $"saved-{index:D5}", $"Game {index:D5}",
            WidgetAppLibraryKind.Game,
            index % 2 == 0 ? "source-steam" : "source-windows",
            index % 2 == 0 ? "Steam" : "Windows");

    private static WidgetAppLibraryItem InstalledItem(
        string appId,
        string savedId,
        string displayName,
        WidgetAppLibraryKind kind,
        string sourceId,
        string sourceDisplayName) =>
        new(appId, savedId, new(
            displayName,
            kind,
            new(sourceId, sourceDisplayName),
            new(WidgetAppLibraryAvailabilityState.Installed, true, "installed"),
            new([]),
            Metadata: null,
            new([WidgetAppLibraryAction.Launch]),
            ActiveOperation: null));

    private static WidgetAppLibraryItem WithPresentation(
        WidgetAppLibraryItem item,
        string? displayName = null,
        WidgetAppLibraryKind? kind = null,
        string? source = null,
        string? artworkHandle = null)
    {
        var presentation = item.Presentation;
        var updatedKind = kind ?? presentation.Kind;
        WidgetAppLibraryArtworkSet artwork = artworkHandle is null
            ? presentation.Artwork
            : new([
                new(WidgetAppLibraryArtworkRole.Tile, artworkHandle, "fixture",
                    updatedKind == WidgetAppLibraryKind.Game
                        ? WidgetAppLibraryArtworkFallback.Game
                        : WidgetAppLibraryArtworkFallback.Application),
                new(WidgetAppLibraryArtworkRole.Hero, artworkHandle, "fixture",
                    updatedKind == WidgetAppLibraryKind.Game
                        ? WidgetAppLibraryArtworkFallback.Game
                        : WidgetAppLibraryArtworkFallback.Application),
            ]);
        return item with
        {
            Presentation = presentation with
            {
                DisplayName = displayName ?? presentation.DisplayName,
                Kind = updatedKind,
                Source = source is null ? presentation.Source :
                    presentation.Source with { DisplayName = source },
                Artwork = artwork,
            },
        };
    }

    private sealed class FakeHost
    {
        private readonly int _count;
        private readonly WidgetTestPrivateState _state;
        internal WidgetTestPrivateState State => _state;
        internal int MaximumObservedIndex { get; private set; } = -1;
        internal int MaximumRequestedLimit { get; private set; }
        internal int RunningObservationCount { get; private set; }
        internal int? FailAfterOffset { get; set; }
        internal Func<int, WidgetAppLibraryItem> ItemFactory { get; set; } = Item;
        internal Func<WidgetAppLibraryCursorRequest, CancellationToken,
            ValueTask<WidgetAppLibraryPage>>? QueryHandler { get; set; }
        internal Func<ResolveSavedWidgetAppLibraryItemsRequest,
            IReadOnlyList<WidgetAppLibraryItem>>? ResolveHandler { get; set; }
        internal List<IReadOnlyList<string>> ResolveRequests { get; } = [];
        internal List<string> Launches { get; } = [];
        internal List<WidgetAppLibraryCursorRequest> Queries { get; } = [];
        internal Func<LaunchWidgetAppLibraryItemRequest, CancellationToken,
            ValueTask<WidgetAppLaunchObservation>>? LaunchHandler { get; set; }
        internal WidgetRunningAppObservation RunningObservation { get; set; } =
            new([], "running-empty");
        internal Func<ConfirmWidgetRunningAppRequest, WidgetAppLibraryItem?>?
            ConfirmRunningHandler { get; set; }
        internal List<ConfirmWidgetRunningAppRequest> RunningConfirmations { get; } = [];
        internal TaskCompletionSource FirstQueryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal FakeHost(int count, WidgetTestPrivateState? state = null)
        {
            _count = count;
            _state = state ?? new WidgetTestPrivateState();
        }

        internal WidgetHostServices Services() => new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetAppLibraryCapabilities.GetPage, Query)
            .WithHandler(WidgetAppLibraryCapabilities.ResolveSaved, Resolve)
            .WithHandler(WidgetAppLibraryCapabilities.Launch, Launch)
            .WithHandler(WidgetAppLibraryCapabilities.LaunchObserved, LaunchObserved)
            .WithHandler(WidgetAppLibraryCapabilities.ObserveRunning,
                (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    RunningObservationCount++;
                    return ValueTask.FromResult(RunningObservation);
                })
            .WithHandler(WidgetAppLibraryCapabilities.ConfirmRunning,
                (request, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    RunningConfirmations.Add(request);
                    return ValueTask.FromResult(new ConfirmWidgetRunningAppResponse(
                        ConfirmRunningHandler?.Invoke(request)));
                })
            .WithPrivateState(_state)
            .Build();

        private ValueTask<WidgetAppLibraryPage> Query(
            WidgetAppLibraryCursorRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FirstQueryStarted.TrySetResult();
            Queries.Add(request);
            if (QueryHandler is not null) return QueryHandler(request, token);
            var offset = request.Cursor is null ? 0 : int.Parse(
                request.Cursor.AsSpan(request.Cursor.LastIndexOf('.') + 1),
                System.Globalization.CultureInfo.InvariantCulture);
            if (FailAfterOffset == offset)
                return ValueTask.FromException<WidgetAppLibraryPage>(
                    new WidgetCapabilityException("platform_unavailable", "private"));
            MaximumRequestedLimit = Math.Max(MaximumRequestedLimit, request.Limit);
            var count = Math.Min(request.Limit, Math.Max(0, _count - offset));
            if (count != 0) MaximumObservedIndex = Math.Max(MaximumObservedIndex, offset + count - 1);
            var items = Enumerable.Range(offset, count).Select(ItemFactory).ToArray();
            var before = offset == 0 ? null : $"cursor.{Math.Max(0, offset - request.Limit)}";
            var after = offset + count < _count ? $"cursor.{offset + count}" : null;
            return ValueTask.FromResult(new WidgetAppLibraryPage(items, before, after, "revision-1"));
        }

        private ValueTask<ResolveSavedWidgetAppLibraryItemsResponse> Resolve(
            ResolveSavedWidgetAppLibraryItemsRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ResolveRequests.Add(request.SavedIds.ToArray());
            var values = ResolveHandler?.Invoke(request) ?? request.SavedIds.Select(saved =>
            {
                var index = int.Parse(saved.AsSpan(saved.LastIndexOf('-') + 1),
                    System.Globalization.CultureInfo.InvariantCulture);
                return ItemFactory(index);
            }).ToArray();
            return ValueTask.FromResult(new ResolveSavedWidgetAppLibraryItemsResponse(values));
        }

        private ValueTask<WidgetCapabilityAcknowledgement> Launch(
            LaunchWidgetAppLibraryItemRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Launches.Add(request.AppId);
            return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
        }

        private ValueTask<WidgetAppLaunchObservation> LaunchObserved(
            LaunchWidgetAppLibraryItemRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Launches.Add(request.AppId);
            return LaunchHandler?.Invoke(request, token) ??
                ValueTask.FromResult(new WidgetAppLaunchObservation(
                    WidgetAppLaunchObservationState.RequestAccepted, false, false));
        }
    }
}
