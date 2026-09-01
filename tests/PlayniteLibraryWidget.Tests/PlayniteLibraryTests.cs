using System.Text.Json;
using System.Text;
using System.Globalization;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LauncherWidget = WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget;

namespace WidgetRail.Tests.PlayniteLibrary;

[TestClass]
public sealed class PlayniteLibraryTests
{




    [TestMethod]
    public void CategoryPolicyBoundsResetOnlyCategoriesAndCasReplayExactDelta()
    {
        var a = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var b = new PlayniteLibraryDisplayItem("saved-b", "B", "Windows");
        var favorite = new[] { a.SavedId };
        var state = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [a, b])
        {
            FavoriteSavedIds = favorite,
            Categories = Enumerable.Range(0, PlayniteLibraryPrivateState.MaximumCategories + 1)
                .Select(_ => new PlayniteLibraryCategory(
                    PlayniteLibraryCategoryPolicy.NewId(), "Category " + Guid.NewGuid().ToString("N")[..8],
                    Array.Empty<string>()))
                .ToArray(),
        };
        var normalized = PlayniteLibraryOrganizationPolicy.Normalize(state);
        Assert.AreEqual(0, normalized.Categories.Count);
        CollectionAssert.AreEqual(favorite, normalized.FavoriteSavedIds.ToArray());
        Assert.AreEqual(2, normalized.Items.Count);

        var valid = PlayniteLibraryOrganizationPolicy.Normalize(state with
        {
            Categories = [new(PlayniteLibraryCategoryPolicy.NewId(), "Arcade", [a.SavedId])],
        });
        var json = JsonSerializer.Serialize(valid);
        Assert.IsTrue(json.Length < 64 * 1024,
            "Bounded category state exceeded the private-state limit.");

        var baseline = PlayniteLibraryPrivateState.Empty with
        {
            Items = [a, b],
            FavoriteSavedIds = [a.SavedId],
            Categories = [new(PlayniteLibraryCategoryPolicy.NewId(), "Arcade", [a.SavedId])],
        };
        var concurrent = baseline with
        {
            FavoriteSavedIds = [a.SavedId, b.SavedId],
        };
        PlayniteLibraryPrivateState? written = null;
        var writes = 0;
        var result = PlayniteLibraryStateStore.SaveAsync(
            candidate => PlayniteLibraryCategoryPolicy.SetMembership(
                candidate, candidate.Categories[0].Id, b, included: true),
            (candidate, _, _) =>
            {
                writes++;
                if (writes == 1)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "conflict"));
                written = candidate;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                true, concurrent, 2)),
            baseline, 1, default).GetAwaiter().GetResult();
        Assert.IsTrue(result.Saved);
        CollectionAssert.AreEqual(new[] { a.SavedId, b.SavedId },
            written!.FavoriteSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { a.SavedId, b.SavedId },
            written.Categories[0].SavedIds.ToArray());
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingCategoryMemberRetainsDisplayWithoutLaunchAuthority()
    {
        var display = new PlayniteLibraryDisplayItem(
            "saved-00001", "Temporarily missing", "Steam");
        var category = new PlayniteLibraryCategory(
            PlayniteLibraryCategoryPolicy.NewId(), "Offline", [display.SavedId]);
        var stored = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display])
        {
            Categories = [category],
        };
        var host = new FakeHost(0, new WidgetTestPrivateState(
            JsonSerializer.Serialize(stored), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await widget.OnActionAsync(new("playnite-library.categories.open",
            "playnite-library.categories.open"));
        await widget.OnActionAsync(new("playnite-library.category.open." + category.Id,
            "playnite-library.category.open-button." + category.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "missing category member load");
        var snapshot = Snapshot(widget, 20);
        var tile = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        StringAssert.Contains(tile.AccessibilityLabel!, "Temporarily missing");
        StringAssert.Contains(tile.AccessibilityLabel!, "Play unavailable");
        Assert.IsTrue(tile.IsDisabled);
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual(0, host.ResolveRequests.Count);
        Assert.AreEqual(0, host.Launches.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task UnavailableAndStaleResolvedRowsNeverAuthorizeLaunch()
    {
        foreach (var state in new[]
                 {
                     WidgetAppLibraryAvailabilityState.Unavailable,
                     WidgetAppLibraryAvailabilityState.StaleSource,
                 })
        {
            var host = new FakeHost(1);
            host.ResolveHandler = request => request.SavedIds.Select(_ =>
            {
                var current = host.ItemFactory(0);
                return current with
                {
                    Presentation = current.Presentation with
                    {
                        Availability = new(state, false,
                            state == WidgetAppLibraryAvailabilityState.Unavailable
                                ? "unavailable" : "stale"),
                        Capabilities = new([]),
                    },
                };
            }).ToArray();
            var widget = Create(host);
            await Interactive(widget);
            await Ready(widget, host);
            var tile = Nodes(Snapshot(widget, 1).Root).Single(node =>
                node.ActionId == "playnite-library.launch");

            await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

            Assert.AreEqual(0, host.Launches.Count);
            await Background(widget);
        }
    }

    [TestMethod, Timeout(30_000)]
    public async Task EpicGameProjectsExactSourceAndLaunchIdentity()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = _ => InstalledItem(
                "app-epic", "saved-epic", "Epic Game",
                WidgetAppLibraryKind.Game,
                "source-epic-installed", "Epic"),
        };
        host.ResolveHandler = _ => [host.ItemFactory(0)];
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var item = widget.Collection.Items.Single();
        Assert.AreEqual("Epic", item.Presentation.Source.DisplayName);
        var tile = Nodes(Snapshot(widget, 2).Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        StringAssert.Contains(tile.AccessibilityLabel!, "Epic");
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual("app-epic", host.Launches.Single());
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task GogGameProjectsExactSourceWithoutLaunchAuthority()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = _ => InstalledItem(
                "app-gog", "saved-gog", "GOG Game",
                WidgetAppLibraryKind.Game,
                "source-gog-installed", "GOG") with
            {
                Presentation = InstalledItem(
                    "app-gog", "saved-gog", "GOG Game",
                    WidgetAppLibraryKind.Game,
                    "source-gog-installed", "GOG").Presentation with
                {
                    Availability = new(
                        WidgetAppLibraryAvailabilityState.Installed,
                        false, "play_unavailable"),
                    Capabilities = new([]),
                },
            },
        };
        host.ResolveHandler = _ => [host.ItemFactory(0)];
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var item = widget.Collection.Items.Single();
        Assert.AreEqual("GOG", item.Presentation.Source.DisplayName);
        var tile = Nodes(Snapshot(widget, 3).Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        StringAssert.Contains(tile.AccessibilityLabel!, "GOG");
        StringAssert.Contains(tile.AccessibilityLabel!, "Play unavailable");
        Assert.IsTrue(tile.IsDisabled);
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual(0, host.Launches.Count);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task WindowsPackageGameProjectsExactSourceAndLaunchIdentity()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = _ => InstalledItem(
                "app-xbox", "saved-xbox", "Package Game",
                WidgetAppLibraryKind.Game,
                "source-microsoft-games-installed", "Xbox / Microsoft Store"),
        };
        host.ResolveHandler = _ => [host.ItemFactory(0)];
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var item = widget.Collection.Items.Single();
        Assert.AreEqual("Xbox / Microsoft Store",
            item.Presentation.Source.DisplayName);
        var tile = Nodes(Snapshot(widget, 3).Root).Single(node =>
            node.ActionId == "playnite-library.launch");
        StringAssert.Contains(tile.AccessibilityLabel!, "Xbox / Microsoft Store");
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual("app-xbox", host.Launches.Single());
        await Background(widget);
    }


    [TestMethod, Timeout(30_000)]
    public async Task EveryTopControlKeepsPendingAndCommittedSnapshotsValid()
    {
        var displays = Enumerable.Range(0, 32)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D5}", $"Game {index:D5}",
                index % 2 == 0 ? "Steam" : "Windows"))
            .ToArray();
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, displays)
        {
            FavoriteSavedIds = [displays[1].SavedId],
            RecentSavedIds = [displays[2].SavedId],
            ExcludedSavedIds = [displays[0].SavedId],
        };
        var host = new FakeHost(32, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "top-control warm state");
        long sequence = 700;

        foreach (var actionId in new[]
                 {
                     "playnite-library.filter.favorites",
                     "playnite-library.filter.recent",
                     "playnite-library.filter.source",
                     "playnite-library.filter.sort",
                     "playnite-library.query.clear",
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
        await AssertRoute("playnite-library.categories.open", PlayniteLibraryRoute.Categories);
        await AssertRoute("playnite-library.hidden.open", PlayniteLibraryRoute.Hidden);
        var unpublished = new[]
        {
            "playnite-library.add.open", "playnite-library.add.back",
            "playnite-library.running.open", "playnite-library.running.back",
        };
        var beforeRoute = widget.RenderState.Value.Collection;
        foreach (var actionId in unpublished)
            await widget.OnActionAsync(new(actionId, actionId));
        Assert.AreEqual(beforeRoute, widget.RenderState.Value.Collection,
            "Unpublished retired routes changed the widget model.");
        var current = Snapshot(widget, sequence++);
        foreach (var actionId in unpublished)
            Assert.IsFalse(Nodes(current.Root).Any(node => node.ActionId == actionId),
                $"Retired action '{actionId}' was still published.");
        await Background(widget);

        async Task AssertRoute(string actionId, PlayniteLibraryRoute expected)
        {
            var beforeQueries = host.Queries.Count;
            var beforeRunning = host.RunningObservationCount;
            await widget.OnActionAsync(new(actionId, actionId));
            await Bounded(widget.WhenLibraryIdleAsync(), actionId + " route load");
            var route = Snapshot(widget, sequence++);
            var expectedTitle = expected switch
            {
                PlayniteLibraryRoute.Categories => "Categories",
                _ => "Hidden games",
            };
            Assert.AreEqual(expectedTitle, Nodes(route.Root).Single(node =>
                node.Id == "playnite-library.compact.title").Text);
            if (expected != PlayniteLibraryRoute.Categories)
                Assert.AreEqual(beforeQueries + 1, host.Queries.Count);
            else
            {
                Assert.AreEqual(beforeQueries, host.Queries.Count);
                Assert.AreEqual(
                    "Categories are read from Playnite. Creating or changing " +
                    "membership uses the exact current Playnite game identity.",
                    Nodes(route.Root).Single(node =>
                        node.Id == "playnite-library.categories.help").Text);
                Assert.AreEqual(ViewNodeKind.TextEntry, Nodes(route.Root).Single(node =>
                    node.Id == "playnite-library.category.create").Kind);
            }

            var backId = expected switch
            {
                PlayniteLibraryRoute.Categories => "playnite-library.categories.back",
                _ => "playnite-library.hidden.back",
            };
            await widget.OnActionAsync(new(backId, backId));
            await Bounded(widget.WhenLibraryIdleAsync(), backId + " route load");
            AssertValidCollectionAnchor(Snapshot(widget, sequence++));
        }
    }

    [TestMethod, Timeout(30_000)]
    public async Task SecondaryRouteBackRestoresAnAnchoredHomePosterFocus()
    {
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion,
            [new("saved-00000", "Game 00000", "Steam")])
        {
            ExcludedSavedIds = ["saved-00000"],
            Categories = [new("category.focus", "Focus", ["saved-00000"])],
        };
        var host = new FakeHost(2, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        await Bounded(widget.WhenWarmStateIdleAsync(), "secondary focus warm state");

        foreach (var (open, back) in new[]
                 {
                     ("playnite-library.categories.open", "playnite-library.categories.back"),
                     ("playnite-library.hidden.open", "playnite-library.hidden.back"),
                 })
        {
            await widget.OnActionAsync(new(open, open));
            await Bounded(widget.WhenLibraryIdleAsync(), open + " route load");
            await widget.OnActionAsync(new(back, back));
            await Bounded(widget.WhenLibraryIdleAsync(), back + " route return");
            var home = Snapshot(widget, host.Queries.Count + 10_000);
            Assert.IsTrue(home.InitialFocusId is not null && Nodes(home.Root).Any(node =>
                node.Id == home.InitialFocusId && node.ActionId == "playnite-library.launch"),
                back + " must restore a current Home poster focus.");
            Assert.IsNotNull(widget.Collection.Anchor,
                back + " must preserve the collection anchor.");
        }
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task HomeLibraryNavigationPrimaryActionOpensBrowseAndReactivationRetainsLastGoodPosters()
    {
        var displays = Enumerable.Range(0, 3).Select(index =>
            new PlayniteLibraryDisplayItem(
                $"saved-{index:D5}", $"Game {index:D5}", "Steam")).ToArray();
        var category = new PlayniteLibraryCategory(
            PlayniteLibraryCategoryPolicy.NewId(), "Lifecycle", [displays[0].SavedId]);
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, displays)
        {
            FavoriteSavedIds = [displays[0].SavedId],
            ExcludedSavedIds = [displays[2].SavedId],
            Categories = [category],
        };
        var host = new FakeHost(3, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        const string reconstructedBackground =
            "library.background.reconstructed-home";
        host.ItemFactory = index => WithPresentation(
            Item(index), artworkHandle: reconstructedBackground);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var home = Snapshot(widget, 40_001);
        var libraryNavigation = Nodes(home.Root).Single(node =>
            node.Id == "playnite-library.library.menu");
        var libraryAction = libraryNavigation.ActionId;
        Assert.IsNotNull(libraryAction);
        Assert.AreEqual("playnite-library.browse.open", libraryAction);
        await widget.OnActionAsync(new(libraryAction, libraryNavigation.Id));
        var browse = Snapshot(widget, 40_002);
        Assert.IsTrue(Nodes(browse.Root).Any(node =>
            node.Id == "playnite-library.browse.grid"),
            "The Home library-navigation primary action must enter Browse.");

        var returnStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var returnRelease = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.QueryHandler = (_, token) =>
        {
            returnStarted.TrySetResult();
            return new(returnRelease.Task.WaitAsync(token));
        };
        var browseBack = widget.OnActionAsync(new("playnite-library.browse.back",
            "playnite-library.browse.back")).AsTask();
        await Bounded(returnStarted.Task, "browse return refresh admission");
        var refreshingHome = Snapshot(widget, 40_006);
        var refreshingNodes = Nodes(refreshingHome.Root).ToArray();
        Assert.AreEqual(WidgetPagedResourceStatus.Refreshing, widget.Collection.Status,
            "Browse Back must refresh while retaining the admitted Home page.");
        Assert.IsTrue(refreshingNodes.Any(node =>
                node.ActionId == "playnite-library.launch"),
            "Browse Back must retain current posters while the replacement query is pending.");
        Assert.IsFalse(refreshingNodes.Any(node =>
                node.Id == "playnite-library.loading" ||
                (node.Text ?? string.Empty).Contains(
                    "Checking current availability", StringComparison.OrdinalIgnoreCase) ||
                (node.Text ?? string.Empty).Contains(
                    "Checking installed games", StringComparison.OrdinalIgnoreCase)),
            "Browse Back must not flash the initial warm/loading page.");
        returnRelease.TrySetResult(new(
            [host.ItemFactory(0), host.ItemFactory(1), host.ItemFactory(2)],
            null, null, "browse-return"));
        await Bounded(browseBack, "browse return action");
        await Bounded(widget.WhenLibraryIdleAsync(), "browse return");
        host.QueryHandler = null;
        var expectedAnchor = widget.Collection.Anchor ?? widget.Collection.Items[0].Key;
        var expectedFocus = PlayniteLibraryIdentity.FocusId("grid", expectedAnchor);
        var expectedPosterCount = widget.Collection.Items.Count;
        Assert.IsGreaterThan(0, expectedPosterCount);
        var reconstructedHome = Snapshot(widget, 40_000);
        var reconstructedHomeBackground = Nodes(reconstructedHome.Root).Single(node =>
            node.Id == "playnite-library.cinematic");
        Assert.AreEqual(reconstructedBackground,
            reconstructedHomeBackground.ArtworkHandle,
            "Browse Back must immediately expose the selected current game's default artwork.");
        Assert.AreEqual(ImageFit.Cover, reconstructedHomeBackground.ImageFit);
        Assert.AreEqual(true,
            reconstructedHomeBackground.UsesFocusedDescendantArtwork);
        var expectedStatus = widget.RenderState.Value.Status;

        void AssertRetainedHome(ViewSnapshot snapshot, string phase)
        {
            var nodes = Nodes(snapshot.Root).ToArray();
            var posters = nodes.Where(node =>
                node.ActionId == "playnite-library.launch").ToArray();
            Assert.AreEqual(expectedPosterCount, posters.Length,
                phase + " must retain every current Home poster.");
            Assert.IsTrue(posters.All(node => node.IsDisabled != true),
                phase + " must not dim a provider-authorized game.");
            foreach (var poster in posters)
            {
                CollectionAssert.AreEqual(new[]
                {
                    "playnite-library.favorite",
                    "playnite-library.hide",
                    "playnite-library.refresh-source",
                    "playnite-library.category." + category.Id,
                }, poster.ContextActions.Select(action => action.ActionId).ToArray(),
                    phase + " must retain the exact game Menu declarations.");
                Assert.IsFalse((poster.AccessibilityLabel ?? string.Empty).Contains(
                    "Ready", StringComparison.Ordinal));
                Assert.IsFalse((poster.AccessibilityLabel ?? string.Empty).Contains(
                    "Paused", StringComparison.Ordinal));
            }
            var rail = nodes.Single(node =>
                node.Id == PlayniteLibraryPresentation.HomeRailId);
            Assert.AreEqual(expectedAnchor.Value, rail.CollectionAnchorKey,
                phase + " must retain the exact collection anchor.");
            Assert.AreEqual(expectedFocus, snapshot.InitialFocusId,
                phase + " must restore the exact retained current poster.");
            Assert.AreEqual(expectedStatus, widget.RenderState.Value.Status,
                phase + " must preserve the last-good status authority.");
            var favoritePoster = posters.Single(node =>
                (node.AccessibilityLabel ?? string.Empty).StartsWith(
                    displays[0].DisplayName, StringComparison.Ordinal));
            CollectionAssert.Contains(favoritePoster.ContextActions.Select(action =>
                    action.Label).ToArray(), "Remove favorite",
                phase + " must preserve current favorite presentation metadata.");
            CollectionAssert.Contains(favoritePoster.ContextActions.Select(action =>
                    action.Label).ToArray(), "Remove from Lifecycle",
                phase + " must preserve current category presentation metadata.");
            Assert.IsFalse(posters.Any(node =>
                    (node.AccessibilityLabel ?? string.Empty).StartsWith(
                        displays[2].DisplayName, StringComparison.Ordinal)),
                phase + " must preserve current hidden presentation metadata.");
            var menu = nodes.Single(node => node.Id == "playnite-library.library.menu");
            Assert.IsFalse(menu.ContextActions.Single(action =>
                action.ActionId == "playnite-library.filter.favorites").IsDisabled);
            Assert.IsFalse(menu.ContextActions.Single(action =>
                action.ActionId == "playnite-library.hidden.open").IsDisabled);
        }

        var staleStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var staleRelease = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var currentStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var currentRelease = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = 0;
        host.QueryHandler = (_, token) =>
        {
            if (Interlocked.Increment(ref probe) == 1)
            {
                staleStarted.TrySetResult();
                return new(staleRelease.Task);
            }
            currentStarted.TrySetResult();
            return new(currentRelease.Task.WaitAsync(token));
        };
        await widget.OnActionAsync(new("playnite-library.refresh",
            "playnite-library.refresh"));
        await Bounded(staleStarted.Task, "stale refresh admission");
        var backgroundTransition = WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Background).AsTask();
        staleRelease.TrySetResult(new([Item(99)], null, null, "retired"));
        await Bounded(backgroundTransition, "retired refresh drain");
        await Visible(widget);
        await Bounded(currentStarted.Task, "reactivation query admission");
        var visible = Snapshot(widget, 40_003);
        AssertRetainedHome(visible, "Visible reactivation");
        await Interactive(widget);
        var retained = Snapshot(widget, 40_004);
        Assert.IsFalse(Nodes(retained.Root).Any(node =>
            node.Id == "playnite-library.loading"),
            "Reactivation must retain last-good posters instead of flashing a loading page.");
        AssertRetainedHome(retained, "Interactive reactivation");

        currentRelease.TrySetResult(new([Item(0), Item(1), Item(2)], null, null,
            "reactivated"));
        await Bounded(widget.WhenLibraryIdleAsync(), "reactivation query drain");
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
        AssertRetainedHome(Snapshot(widget, 40_005), "Settled reactivation");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task BrowseSearchFinalWidgetViewRetiresFilteredNavigationFocus()
    {
        var host = new FakeHost(3);
        host.QueryHandler = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            var items = Enumerable.Range(0, 3).Select(host.ItemFactory);
            if (request.Query.SearchText is { } search)
                items = items.Where(item => item.Presentation.DisplayName.Contains(
                    search, StringComparison.OrdinalIgnoreCase));
            return ValueTask.FromResult(new WidgetAppLibraryPage(
                items.ToArray(), null, null, "search-focus"));
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        await widget.OnActionAsync(new("playnite-library.browse.open",
            "playnite-library.library.menu"));
        var firstBrowse = AssertValidSnapshot(50_001, "initial Browse");
        var restoredTile = Nodes(firstBrowse.Root).Where(node =>
            node.ActionId == "playnite-library.launch").Skip(1).First();
        await widget.OnActionAsync(new("playnite-library.browse.back", restoredTile.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "seed Browse return focus");
        _ = AssertValidSnapshot(50_002, "seeded Home return");

        await widget.OnActionAsync(new("playnite-library.browse.open",
            "playnite-library.library.menu"));
        await CommitSearch("Game", "retain all Browse results");
        var restoredBrowse = AssertValidSnapshot(50_003, "navigator-owned restored Browse");
        Assert.AreEqual(restoredTile.Id, restoredBrowse.InitialFocusId,
            "A distinct still-rendered tile inside the Browse background must retain navigator-owned return focus after presentation preference retires.");

        await CommitSearch("Game 00000", "matching search");
        var matching = AssertValidSnapshot(50_004, "matching search");
        Assert.AreNotEqual(restoredTile.Id, matching.InitialFocusId,
            "A filtered-out navigator target must not overwrite presentation focus.");
        Assert.AreEqual("Game 00000", Nodes(matching.Root).Single(node =>
            node.Id == matching.InitialFocusId).AccessibilityLabel?.Split(',')[0]);

        await CommitSearch("No matching game", "zero-result search");
        var empty = AssertValidSnapshot(50_005, "zero-result search");
        Assert.AreEqual("playnite-library.search", empty.InitialFocusId);

        await widget.OnActionAsync(new("playnite-library.query.clear",
            "playnite-library.query.clear"));
        await Bounded(widget.WhenLibraryIdleAsync(), "Browse query clear");
        var cleared = AssertValidSnapshot(50_006, "cleared Browse");
        Assert.IsTrue(Nodes(cleared.Root).Any(node =>
            node.Id == cleared.InitialFocusId && node.IsDisabled is not true));

        await widget.OnActionAsync(new("playnite-library.browse.back",
            cleared.InitialFocusId!));
        await Bounded(widget.WhenLibraryIdleAsync(), "final Browse return");
        _ = AssertValidSnapshot(50_007, "final Home return");
        await Background(widget);

        async Task CommitSearch(string value, string phase)
        {
            await widget.OnActionAsync(new WidgetActionEvent(
                "playnite-library.search.commit", "playnite-library.search")
                { CommittedText = value });
            await Bounded(widget.WhenLibraryIdleAsync(), phase);
        }

        ViewSnapshot AssertValidSnapshot(long sequence, string phase)
        {
            var snapshot = Snapshot(widget, sequence);
            var errors = ViewSnapshotValidator.Validate(snapshot);
            Assert.AreEqual(0, errors.Count,
                phase + Environment.NewLine + string.Join(Environment.NewLine,
                    errors.Select(error => $"{error.Path}: {error.Code}: {error.Message}")));
            Assert.IsNotNull(snapshot.InitialFocusId, phase + " must publish initial focus.");
            var focus = Nodes(snapshot.Root).Single(node =>
                node.Id == snapshot.InitialFocusId);
            Assert.IsTrue(focus.IsFocusable,
                phase + " must publish a protocol-focusable initial target.");
            return snapshot;
        }
    }

    [TestMethod, Timeout(30_000)]
    public async Task BrowseFinalViewPreservesDisabledFocusableNavigationTarget()
    {
        var host = new FakeHost(3);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        await widget.OnActionAsync(new("playnite-library.browse.open",
            "playnite-library.library.menu"));
        const string disabledTarget = "playnite-library.filter.favorites";
        var browse = Snapshot(widget, 50_101);
        Assert.IsTrue(Nodes(browse.Root).Single(node => node.Id == disabledTarget).IsDisabled,
            "The empty Favorites filter is the deterministic disabled-but-focusable target.");

        await widget.OnActionAsync(new("playnite-library.browse.back", disabledTarget));
        await Bounded(widget.WhenLibraryIdleAsync(), "seed disabled Browse return focus");
        _ = Snapshot(widget, 50_102);
        await widget.OnActionAsync(new("playnite-library.browse.open",
            "playnite-library.library.menu"));
        await widget.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search")
            { CommittedText = "Game" });
        await Bounded(widget.WhenLibraryIdleAsync(), "retire Browse content preference");

        var restored = Snapshot(widget, 50_103);
        var errors = ViewSnapshotValidator.Validate(restored);
        Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine,
            errors.Select(error => $"{error.Path}: {error.Code}: {error.Message}")));
        Assert.AreEqual(disabledTarget, restored.InitialFocusId,
            "Disabled controls remain valid protocol focus targets and must retain navigation focus.");
        var restoredTarget = Nodes(restored.Root).Single(node => node.Id == disabledTarget);
        Assert.IsTrue(restoredTarget.IsFocusable);
        Assert.IsTrue(restoredTarget.IsDisabled);
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task ScopedBrowseHiddenAndCategoryQueriesSurviveLifecycleReactivation()
    {
        var displays = Enumerable.Range(0, 3).Select(index =>
            new PlayniteLibraryDisplayItem(
                $"saved-{index:D5}", $"Game {index:D5}", "Steam")).ToArray();
        var category = new PlayniteLibraryCategory(
            PlayniteLibraryCategoryPolicy.NewId(), "Lifecycle", [displays[1].SavedId]);
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, displays)
        {
            FavoriteSavedIds = [displays[0].SavedId],
            ExcludedSavedIds = [displays[2].SavedId],
            Categories = [category],
        };
        var host = new FakeHost(3, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        await widget.OnActionAsync(new("playnite-library.filter.favorites",
            "playnite-library.library.menu"));
        await Bounded(widget.WhenLibraryIdleAsync(), "favorite Browse load");
        CollectionAssert.AreEqual(new[] { displays[0].SavedId },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());
        Assert.AreEqual(PlayniteLibraryQueryScope.Library, host.QueryContexts[^1].Scope);
        Assert.AreEqual(1, Nodes(Snapshot(widget, 40_101).Root).Count(node =>
            node.ActionId == "playnite-library.launch"));

        var browseQueries = host.Queries.Count;
        await Background(widget);
        await Visible(widget);
        await WaitUntil(() => host.Queries.Count > browseQueries);
        await Bounded(widget.WhenLibraryIdleAsync(), "favorite Browse reactivation");
        await Interactive(widget);
        CollectionAssert.AreEqual(new[] { displays[0].SavedId },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray(),
            "Browse Favorites must remain an exact scoped query after reactivation.");

        await widget.OnActionAsync(new("playnite-library.browse.back",
            "playnite-library.browse.back"));
        await Bounded(widget.WhenLibraryIdleAsync(), "Browse return");
        await widget.OnActionAsync(new("playnite-library.hidden.open",
            "playnite-library.library.menu"));
        await Bounded(widget.WhenLibraryIdleAsync(), "Hidden load");
        Assert.AreEqual(PlayniteLibraryQueryScope.Hidden, host.QueryContexts[^1].Scope);
        CollectionAssert.AreEqual(new[] { displays[2].SavedId },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());
        Assert.IsTrue(Nodes(Snapshot(widget, 40_102).Root).Any(node =>
            node.ActionId == "playnite-library.restore"));

        var hiddenQueries = host.Queries.Count;
        await Background(widget);
        await Visible(widget);
        await WaitUntil(() => host.Queries.Count > hiddenQueries);
        await Bounded(widget.WhenLibraryIdleAsync(), "Hidden reactivation");
        await Interactive(widget);
        Assert.AreEqual(PlayniteLibraryQueryScope.Hidden, host.QueryContexts[^1].Scope);
        Assert.IsTrue(Nodes(Snapshot(widget, 40_103).Root).Any(node =>
            node.ActionId == "playnite-library.restore"));

        await widget.OnActionAsync(new("playnite-library.hidden.back",
            "playnite-library.hidden.back"));
        await Bounded(widget.WhenLibraryIdleAsync(), "Hidden return");
        await widget.OnActionAsync(new("playnite-library.categories.open",
            "playnite-library.library.menu"));
        await widget.OnActionAsync(new("playnite-library.category.open." + category.Id,
            "playnite-library.category.open-button." + category.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "Category load");
        Assert.AreEqual(PlayniteLibraryQueryScope.Category, host.QueryContexts[^1].Scope);
        Assert.AreEqual(category.Name, host.QueryContexts[^1].CategoryName);
        CollectionAssert.AreEqual(new[] { displays[1].SavedId },
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());

        var categoryQueries = host.Queries.Count;
        await Background(widget);
        await Visible(widget);
        await WaitUntil(() => host.Queries.Count > categoryQueries);
        await Bounded(widget.WhenLibraryIdleAsync(), "Category reactivation");
        await Interactive(widget);
        Assert.AreEqual(PlayniteLibraryQueryScope.Category, host.QueryContexts[^1].Scope);
        Assert.AreEqual(category.Name, host.QueryContexts[^1].CategoryName);
        Assert.IsTrue(Nodes(Snapshot(widget, 40_104).Root).Any(node =>
            node.ActionId == "playnite-library.launch" && node.IsDisabled != true));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task LifecycleRetirementBarrierDrainsStaleQueryBeforeCurrentAdmission()
    {
        var host = new FakeHost(2);
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);

        var retiredStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retiredRelease = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var currentStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var currentRelease = new TaskCompletionSource<WidgetAppLibraryPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        host.QueryHandler = (_, token) =>
        {
            if (Interlocked.Increment(ref call) == 1)
            {
                retiredStarted.TrySetResult();
                return new(retiredRelease.Task);
            }
            currentStarted.TrySetResult();
            return new(currentRelease.Task.WaitAsync(token));
        };

        await widget.OnActionAsync(new("playnite-library.refresh",
            "playnite-library.refresh"));
        await Bounded(retiredStarted.Task, "retired query admission");
        var backgroundTransition = WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Background).AsTask();
        await Task.Yield();
        Assert.IsFalse(currentStarted.Task.IsCompleted,
            "WidgetOperations.RunLatest must not overlap a current cursor query with its " +
            "cancellation-ignoring predecessor.");
        retiredRelease.TrySetResult(new([Item(99)], null, null, "retired"));
        await Bounded(backgroundTransition, "retired query lifecycle drain");
        CollectionAssert.AreEqual(new[] { "saved-00000", "saved-00001" },
            widget.Collection.Items.Select(item => item.Value.SavedId).ToArray(),
            "The lifecycle-retired result must not publish into retained presentation.");
        Assert.IsFalse(Nodes(Snapshot(widget, 40_105).Root).Any(node =>
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Game 00099", StringComparison.Ordinal)));

        await Visible(widget);
        await Bounded(currentStarted.Task, "post-barrier current query admission");
        CollectionAssert.AreEqual(new[] { "saved-00000", "saved-00001" },
            widget.Collection.Items.Select(item => item.Value.SavedId).ToArray(),
            "Visible must retain the last-good page until its current successor publishes.");
        currentRelease.TrySetResult(new([Item(1)], null, null, "current"));
        await Bounded(widget.WhenLibraryIdleAsync(), "current query publication");
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            widget.Collection.Items.Select(item => item.Value.SavedId).ToArray(),
            "Only the post-retirement current query may replace presentation.");
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task AdjacentPageCannotRegressNewerFavoriteAndCategoryAuthority()
    {
        var display = new PlayniteLibraryDisplayItem(
            "saved-00000", "Game 00000", "Steam");
        var category = new PlayniteLibraryCategory(
            PlayniteLibraryCategoryPolicy.NewId(), "Race", []);
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display])
        {
            Categories = [category],
        };
        var host = new FakeHost(65, new WidgetTestPrivateState(
            JsonSerializer.Serialize(persisted), 1));
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var first = Nodes(Snapshot(widget, 40_201).Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).StartsWith(
                display.DisplayName, StringComparison.Ordinal));

        var adjacentReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var persistStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var persistRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        host.QueryHandler = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            adjacentReturned.TrySetResult();
            return ValueTask.FromResult(new WidgetAppLibraryPage(
                [Item(64)], "cursor.0", null, "adjacent-old-authority"));
        };
        host.StateWriteHandler = async (request, token) =>
        {
            persistStarted.TrySetResult();
            await persistRelease.Task.WaitAsync(token).ConfigureAwait(false);
            return await host.State.WriteAsync(request, token).ConfigureAwait(false);
        };

        await widget.OnActionAsync(new("playnite-library.next",
            PlayniteLibraryPresentation.ScrollId));
        await Bounded(adjacentReturned.Task, "adjacent provider return");
        await Bounded(persistStarted.Task, "adjacent pre-publication persistence");
        await widget.OnActionAsync(new("playnite-library.favorite", first.Id));
        await widget.OnActionAsync(new("playnite-library.category." + category.Id, first.Id));
        CollectionAssert.AreEqual(new[] { display.SavedId },
            host.Authority.FavoriteGameIds.ToArray());
        CollectionAssert.AreEqual(new[] { display.SavedId },
            host.Authority.Categories.Single().SavedIds.ToArray());

        persistRelease.TrySetResult();
        await Bounded(widget.WhenLibraryIdleAsync(), "adjacent page publication");
        var retained = Nodes(Snapshot(widget, 40_202).Root).Single(node => node.Id == first.Id);
        CollectionAssert.Contains(retained.ContextActions.Select(action =>
            action.Label).ToArray(), "Remove favorite");
        CollectionAssert.Contains(retained.ContextActions.Select(action =>
            action.Label).ToArray(), "Remove from Race");
        Assert.AreEqual(65, widget.Collection.Items.Count,
            "The current adjacent page must still merge after its stale authority is fenced.");
        await Background(widget);
    }





    [TestMethod]
    public void RenderStateHasOneModelOwnerAndNoRetiredScalarOwners()
    {
        var fields = typeof(LauncherWidget).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        var models = fields.Where(field => field.FieldType.IsGenericType &&
            field.FieldType.GetGenericTypeDefinition() == typeof(WidgetModel<>)).ToArray();

        Assert.AreEqual(1, models.Length,
            "Render-facing state must have one WidgetModel owner.");
        Assert.AreEqual(typeof(PlayniteLibraryRenderState),
            models[0].FieldType.GetGenericArguments()[0]);
        CollectionAssert.AreEquivalent(new[]
        {
            "_application", "_gate", "_launchPersistence", "_launchStateRecency",
            "_launchStates", "_library", "_model", "_navigation", "_organization",
            "_livePlayniteAuthority", "_presentationAuthority", "_queryAuthorityGeneration",
            "_authorityRevision", "_hasLivePlayniteAuthority", "_playniteClient",
            "_playniteGeneration",
            "_stateGate", "_stateRevision",
        }, fields.Select(field => field.Name).ToArray(),
            "A mutable presentation owner was added outside the model boundary.");
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
            await widget.OnActionAsync(new("playnite-library.next",
                PlayniteLibraryPresentation.HomeRailId));
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
            node.ActionId == "playnite-library.launch"));
        Assert.IsLessThanOrEqualTo(
            WidgetCursorResource<PlayniteLibraryItem>.MaximumCursorHistory,
            widget.RetainedCursorCount);
        var beforeRevision = widget.Collection.Revision;
        await widget.OnActionAsync(new("playnite-library.previous",
            PlayniteLibraryPresentation.HomeRailId));
        await Bounded(widget.WhenLibraryIdleAsync(), "reverse page drain");
        Assert.IsGreaterThan(beforeRevision, widget.Collection.Revision);
        Assert.AreEqual(WidgetPagedResourceStatus.Ready, widget.Collection.Status);
        var finalPageStart = (pages - 1) * LauncherWidget.PageSize;
        var reversePageStart = finalPageStart - LauncherWidget.MaximumRetainedItems;
        Assert.AreEqual($"Game {reversePageStart:D5}",
            widget.Collection.Items[0].Presentation.DisplayName);
        Assert.AreEqual(
            PlayniteLibraryIdentity.FocusId("grid", widget.Collection.Items[63].Key),
            widget.Collection.RequestedFocusId);
        await Background(widget);
    }



    [TestMethod, Timeout(30_000)]
    public async Task WarmProjectionIsVisibleButCannotAuthorizeLaunch()
    {
        var warm = new PlayniteLibraryPrivateState(PlayniteLibraryPrivateState.CurrentVersion,
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
            node.ActionId == "playnite-library.launch");
        Assert.IsTrue(tile.IsDisabled);
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        Assert.AreEqual(0, host.Launches.Count);

        await Background(widget);
        pending.TrySetResult(new([], null, null, "rev-1"));
    }

    [TestMethod]
    public void PrivateProjectionIsBoundedAndContainsNoLaunchAuthority()
    {
        var items = Enumerable.Range(0, 128)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D3}-" + new string('s', 114),
                new string((char)('a' + index % 26), 96),
                new string((char)('A' + index % 26), 64)))
            .ToArray();
        var organized = items.Take(PlayniteLibraryPrivateState.MaximumOrganizedItems).ToArray();
        var groups = organized.Chunk(PlayniteLibraryPrivateState.MaximumVariantsPerGroup)
            .Select((members, index) => new PlayniteLibraryVariantGroup(
                "variant." + index.ToString("x20"),
                members.Select(item => item.SavedId).ToArray(),
                members[^1].SavedId)).ToArray();
        var state = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, items)
        {
            FavoriteSavedIds = organized.Select(item => item.SavedId).ToArray(),
            VariantGroups = groups,
            RecentSavedIds = items.Skip(PlayniteLibraryPrivateState.MaximumOrganizedItems)
                .Take(PlayniteLibraryPrivateState.MaximumRecentItems)
                .Reverse().Select(item => item.SavedId).ToArray(),
            ManualSavedIds = items.Skip(PlayniteLibraryPrivateState.MaximumOrganizedItems +
                    PlayniteLibraryPrivateState.MaximumRecentItems)
                .Take(PlayniteLibraryPrivateState.MaximumManualItems)
                .Select(item => item.SavedId).ToArray(),
            ExcludedSavedIds = items.Skip(PlayniteLibraryPrivateState.MaximumOrganizedItems +
                    PlayniteLibraryPrivateState.MaximumRecentItems +
                    PlayniteLibraryPrivateState.MaximumManualItems)
                .Take(PlayniteLibraryPrivateState.MaximumExcludedItems)
                .Select(item => item.SavedId).ToArray(),
            Categories = Enumerable.Range(0, PlayniteLibraryPrivateState.MaximumCategories)
                .Select(index => new PlayniteLibraryCategory(
                    "category." + index.ToString("x32"),
                    new string((char)('A' + index),
                        PlayniteLibraryPrivateState.MaximumCategoryNameLength),
                    items.Skip(index * 2).Take(2)
                        .Select(item => item.SavedId).ToArray()))
                .ToArray(),
            ProvenSources = Enumerable.Range(
                    0, PlayniteLibraryPrivateState.MaximumProvenSources)
                .Select(index => $"Source {index:D2} " + new string('S', 54))
                .ToArray(),
        };
        var normalizedState = PlayniteLibraryOrganizationPolicy.Normalize(state);
        var json = JsonSerializer.SerializeToUtf8Bytes(normalizedState);
        var baseBytes = JsonSerializer.SerializeToUtf8Bytes(state with { Categories = [] });

        Assert.IsLessThanOrEqualTo(64 * 1024, json.Length,
            $"Base={baseBytes.Length}, categories={json.Length - baseBytes.Length}");
        Assert.AreEqual(0, normalizedState.Categories.Count,
            "Over-budget categories were not reset atomically.");
        Assert.AreEqual(items.Length, normalizedState.Items.Count);
        Assert.AreEqual(0, normalizedState.ProvenSources.Count,
            "Byte pressure did not reset only the non-authorizing source catalog.");
        var maximumSources = state.ProvenSources;
        var sourcesOnly = PlayniteLibraryOrganizationPolicy.Normalize(
            PlayniteLibraryPrivateState.Empty with { ProvenSources = maximumSources });
        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumProvenSources,
            sourcesOnly.ProvenSources.Count);
        Assert.IsLessThanOrEqualTo(64 * 1024,
            JsonSerializer.SerializeToUtf8Bytes(sourcesOnly).Length);
        var invalidSources = PlayniteLibraryOrganizationPolicy.Normalize(state with
        {
            Categories = [],
            ProvenSources = Enumerable.Range(
                    0, PlayniteLibraryPrivateState.MaximumProvenSources + 1)
                .Select(index => $"Source {index:D2}").ToArray(),
        });
        Assert.AreEqual(0, invalidSources.ProvenSources.Count,
            "Invalid source evidence was not reset as one affected field.");
        Assert.AreEqual(items.Length, invalidSources.Items.Count,
            "Invalid source evidence reset unrelated organization state.");
        var validationOverflow = Enumerable.Range(
                0, PlayniteLibraryPrivateState.MaximumItems + 1)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-validation-{index:D4}", $"Game {index:D4}", "Local"))
            .ToArray();
        Assert.AreEqual(0, PlayniteLibraryOrganizationPolicy.Normalize(
            PlayniteLibraryPrivateState.Empty with { Items = validationOverflow }).Items.Count);
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
            node.ActionId == "playnite-library.launch");
        await widget.OnActionAsync(new("playnite-library.favorite", first.Id));
        await widget.OnActionAsync(new("playnite-library.hide", first.Id));
        await Bounded(widget.WhenLibraryIdleAsync(), "hidden library refresh");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            host.Authority.HiddenGameIds.ToArray());
        Assert.IsFalse(Nodes(Snapshot(widget, 302).Root).Any(node =>
            node.ActionId == "playnite-library.launch" && node.Id == first.Id));
        var afterHide = Snapshot(widget, 3021);
        Assert.IsNotNull(afterHide.InitialFocusId);
        Assert.IsTrue(Nodes(afterHide.Root).Any(node =>
            node.Id == afterHide.InitialFocusId &&
            node.ActionId == "playnite-library.launch" && node.IsDisabled is not true));
        await Background(widget);

        var restartedHost = new FakeHost(3, privateState);
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        Assert.IsFalse(Nodes(Snapshot(restarted, 303).Root).Any(node =>
            node.ActionId == "playnite-library.launch" && node.Id == first.Id));
        await restarted.OnActionAsync(new(
            "playnite-library.hidden.open", "playnite-library.hidden.open"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "hidden route load");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restartedHost.Queries[^1].Query.FavoriteSavedIds.ToArray());
        var hidden = Nodes(Snapshot(restarted, 304).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        StringAssert.Contains(hidden.AccessibilityLabel!, "Hidden · Restore");
        await restarted.OnActionAsync(new WidgetActionEvent(
            "playnite-library.search.commit", "playnite-library.search")
            { CommittedText = "No such hidden game" });
        await Bounded(restarted.WhenLibraryIdleAsync(), "hidden query filter");
        Assert.IsTrue(Nodes(Snapshot(restarted, 3041).Root).Any(node =>
            node.Id == "playnite-library.hidden.empty.action"));
        await restarted.OnActionAsync(new(
            "playnite-library.query.clear", "playnite-library.query.clear"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "hidden query clear");
        hidden = Nodes(Snapshot(restarted, 3042).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        await restarted.OnActionAsync(new("playnite-library.launch", hidden.Id));
        Assert.AreEqual(0, restartedHost.Launches.Count,
            "A display-only hidden row must not authorize launch.");
        await restarted.OnActionAsync(new("playnite-library.restore", hidden.Id));
        Assert.AreEqual(0, restartedHost.Authority.HiddenGameIds.Count);
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restartedHost.Authority.FavoriteGameIds.ToArray());
        Assert.IsTrue(Nodes(Snapshot(restarted, 305).Root).Any(node =>
            node.Id == "playnite-library.hidden.empty.action"));
        await restarted.OnActionAsync(new(
            "playnite-library.hidden.back", "playnite-library.hidden.empty.action"));
        var restoredLibrary = Snapshot(restarted, 306);
        Assert.IsTrue(Nodes(restoredLibrary.Root).Any(node =>
            node.ActionId == "playnite-library.launch" && node.Id == first.Id &&
            node.IsDisabled is not true));
        await Background(restarted);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RestoreSupersedesCancellationIgnoringHiddenLoadBeforeBack()
    {
        var display = new PlayniteLibraryDisplayItem("saved-00000", "Game 00000", "Steam");
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display])
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
            "playnite-library.hidden.open", "playnite-library.hidden.open"));
        await Bounded(hiddenLoadStarted.Task, "cancellation-ignoring hidden load admission");
        var hidden = Nodes(Snapshot(widget, 307).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        var restore = widget.OnActionAsync(new(
            "playnite-library.restore", hidden.Id)).AsTask();
        Assert.IsFalse(restore.IsCompleted,
            "Restore must drain the replaced Hidden generation before reporting readiness.");
        releaseHiddenLoad.TrySetResult(new(
            [Item(0)], null, null, "late-hidden"));
        await Bounded(restore, "cancellation-ignoring Hidden replacement drain");
        Assert.IsTrue(Nodes(Snapshot(widget, 3071).Root).Any(node =>
            node.Id == "playnite-library.hidden.empty.action"));

        using var canceledRoute = new CancellationTokenSource();
        canceledRoute.Cancel();
        await widget.OnActionAsync(new(
            "playnite-library.hidden.back", "playnite-library.hidden.empty.action"),
            canceledRoute.Token);
        var current = Snapshot(widget, 308);
        var launch = Nodes(current.Root).Single(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                display.DisplayName, StringComparison.Ordinal));
        Assert.IsTrue(launch.IsDisabled is not true);
        Assert.IsNotNull(current.InitialFocusId);
        Assert.IsTrue(Nodes(current.Root).Any(node =>
            node.Id == current.InitialFocusId &&
            node.ActionId == "playnite-library.launch" && node.IsDisabled is not true));

        var afterLateCompletion = Snapshot(widget, 309);
        Assert.IsTrue(Nodes(afterLateCompletion.Root).Any(node =>
            node.Id == launch.Id && node.ActionId == "playnite-library.launch" &&
            node.IsDisabled is not true));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task MissingAndReplacementRowsRemainIndependentFromHiddenIdentity()
    {
        var hidden = new PlayniteLibraryDisplayItem("saved-00000", "Shared title", "Steam");
        var persisted = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [hidden])
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
            node.ActionId == "playnite-library.launch").ToArray();
        Assert.AreEqual(1, library.Length);
        StringAssert.Contains(library[0].AccessibilityLabel!, hidden.DisplayName);
        await widget.OnActionAsync(new(
            "playnite-library.hidden.open", "playnite-library.hidden.open"));
        await Bounded(widget.WhenLibraryIdleAsync(), "missing hidden route");
        var unavailable = Nodes(Snapshot(widget, 312).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        StringAssert.Contains(unavailable.AccessibilityLabel!, "Unavailable · Restore");
        Assert.AreEqual("playnite-library.item.hidden." +
            PlayniteLibraryIdentity.Key(hidden.SavedId).Value, unavailable.Id);
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
            node.ActionId == "playnite-library.launch"));
        await reclassified.OnActionAsync(new(
            "playnite-library.hidden.open", "playnite-library.hidden.open"));
        await Bounded(reclassified.WhenLibraryIdleAsync(), "reclassified hidden route");
        var current = Nodes(Snapshot(reclassified, 314).Root).Single(node =>
            node.ActionId == "playnite-library.restore");
        StringAssert.Contains(current.AccessibilityLabel!,
            "Game 00000, Steam, Hidden · Restore");
        await Background(reclassified);
    }

    [TestMethod]
    public async Task HiddenBoundAndCasReplayPreserveConcurrentOrganization()
    {
        var displays = Enumerable.Range(0, PlayniteLibraryPrivateState.MaximumExcludedItems + 2)
            .Select(index => new PlayniteLibraryDisplayItem(
                $"saved-{index:D5}", $"Game {index}", "Steam"))
            .ToArray();
        var state = PlayniteLibraryPrivateState.Empty;
        for (var index = 0; index < PlayniteLibraryPrivateState.MaximumExcludedItems; index++)
        {
            var mutation = PlayniteLibraryOrganizationPolicy.SetExcluded(
                state, displays[index], excluded: true);
            Assert.IsTrue(mutation.Accepted);
            state = mutation.State;
        }
        var overflow = PlayniteLibraryOrganizationPolicy.SetExcluded(
            state, displays[^1], excluded: true);
        Assert.IsFalse(overflow.Accepted);
        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumExcludedItems,
            overflow.State.ExcludedSavedIds.Count);

        var concurrent = state with
        {
            Items = [displays[^2], .. state.Items],
            FavoriteSavedIds = [displays[^2].SavedId],
            VariantGroups =
            [
                new(PlayniteLibraryIdentity.GroupId(
                        displays[^2].SavedId, displays[31].SavedId),
                    [displays[^2].SavedId, displays[31].SavedId],
                    displays[^2].SavedId),
            ],
            RecentSavedIds = [displays[^2].SavedId],
            ManualSavedIds = [displays[^2].SavedId],
        };
        PlayniteLibraryPrivateState? written = null;
        var attempts = 0;
        var restored = displays[0];
        var result = await PlayniteLibraryStateStore.SaveAsync(
            current => PlayniteLibraryOrganizationPolicy.SetExcluded(
                current, restored, excluded: false),
            (candidate, _, _) =>
            {
                if (attempts++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "fixture"));
                written = candidate;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                true, concurrent, 2)), state, 1, CancellationToken.None);
        Assert.IsTrue(result.Saved);
        Assert.IsFalse(written!.ExcludedSavedIds.Contains(restored.SavedId));
        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumExcludedItems - 1,
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
            await PlayniteLibraryStateStore.SaveAsync(
                current => PlayniteLibraryOrganizationPolicy.SetExcluded(
                    current, displays[1], excluded: false),
                (_, _, _) => ValueTask.FromException<WidgetPrivateStateMutation>(
                    new IOException("fixture")),
                _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                    true, state, 1)), state, 1, CancellationToken.None));
        Assert.IsTrue(state.ExcludedSavedIds.Contains(displays[1].SavedId));
    }

    [TestMethod, Timeout(30_000)]
    public async Task FavoritesPersistWhileVariantActionsRemainUnpublished()
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
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        await widget.OnActionAsync(new("playnite-library.favorite", tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.variant", tiles[0].Id));
        await widget.OnActionAsync(new("playnite-library.variant", tiles[1].Id));
        await widget.OnActionAsync(new("playnite-library.prefer", tiles[1].Id));
        Assert.IsTrue(host.Authority.FavoriteGameIds.Contains("saved-00000"));
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        Assert.IsFalse(Nodes(Snapshot(widget, 10).Root).Any(node =>
            node.ActionId is "playnite-library.variant" or "playnite-library.prefer"));
        await Background(widget);

        var missingHost = new FakeHost(0, state);
        var missing = Create(missingHost);
        await Interactive(missing);
        await Ready(missing, missingHost);
        var missingSnapshot = Snapshot(missing, 11);
        Assert.IsFalse(Nodes(missingSnapshot.Root).Any(node =>
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "variant", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, missing.Organization.VariantGroups.Count);
        await Background(missing);

        var restoredHost = new FakeHost(2, state);
        var restored = Create(restoredHost);
        await Interactive(restored);
        await Ready(restored, restoredHost);
        Assert.IsFalse(Nodes(Snapshot(restored, 12).Root).Any(node =>
            node.ActionId is "playnite-library.variant" or "playnite-library.prefer"));
        var restoredTiles = Nodes(Snapshot(restored, 13).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();
        await restored.OnActionAsync(new("playnite-library.variant", restoredTiles[0].Id));
        await restored.OnActionAsync(new("playnite-library.prefer", restoredTiles[1].Id));
        Assert.AreEqual(0, restored.Organization.VariantGroups.Count);
        Assert.IsTrue(restoredHost.Authority.FavoriteGameIds.Contains("saved-00000"));
        await restored.OnActionAsync(new(
            "playnite-library.organization.reset", "playnite-library.organization.reset"));
        Assert.IsTrue(restoredHost.Authority.FavoriteGameIds.Contains("saved-00000"));
        Assert.IsTrue(Nodes(Snapshot(restored, 14).Root).Any(node =>
            node.ActionId == "playnite-library.launch" &&
            (node.AccessibilityLabel ?? string.Empty).Contains(
                "Favorite", StringComparison.Ordinal)));
        await Background(restored);
    }

    [TestMethod]
    public async Task CasConflictReappliesOnlyRequestedFavoriteDelta()
    {
        var a = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var b = new PlayniteLibraryDisplayItem("saved-b", "B", "Steam");
        var c = new PlayniteLibraryDisplayItem("saved-c", "C", "Windows");
        var d = new PlayniteLibraryDisplayItem("saved-d", "D", "Windows");
        var baseline = new PlayniteLibraryPrivateState(PlayniteLibraryPrivateState.CurrentVersion,
            [a, b, c, d])
        {
            FavoriteSavedIds = [a.SavedId],
        };
        var latest = baseline with
        {
            FavoriteSavedIds = [a.SavedId, c.SavedId],
            VariantGroups =
            [
                new(PlayniteLibraryIdentity.GroupId(c.SavedId, d.SavedId),
                    [c.SavedId, d.SavedId], d.SavedId),
            ],
        };
        PlayniteLibraryPrivateState? written = null;
        var writes = 0;

        var result = await PlayniteLibraryStateStore.SaveAsync(
            state => PlayniteLibraryOrganizationPolicy.SetFavorite(state, b, favorite: true),
            (state, _, _) =>
            {
                if (writes++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "conflict"));
                written = state;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
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
        var old = new PlayniteLibraryDisplayItem("saved-a", "Old title", "Steam");
        var state = new PlayniteLibraryPrivateState(PlayniteLibraryPrivateState.CurrentVersion, [old])
        {
            FavoriteSavedIds = [old.SavedId],
        };
        var sameIdentity = PlayniteLibraryItem.From(InstalledItem(
            "app-new", old.SavedId, "Updated title", WidgetAppLibraryKind.Game,
            "source-steam", "Steam"));
        var refreshed = PlayniteLibraryOrganizationPolicy.ProjectPage(state, [sameIdentity]);
        Assert.AreEqual("Updated title",
            refreshed.Items.Single(item => item.SavedId == old.SavedId).DisplayName);
        Assert.IsTrue(refreshed.FavoriteSavedIds.Contains(old.SavedId));

        var replacement = PlayniteLibraryItem.From(InstalledItem(
            "app-replacement", "saved-replacement", "Updated title",
            WidgetAppLibraryKind.Game, "source-steam", "Steam"));
        var replaced = PlayniteLibraryOrganizationPolicy.ProjectPage(refreshed, [replacement]);
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

        var reset = JsonSerializer.Deserialize<PlayniteLibraryPrivateState>(state.Json!);
        Assert.AreEqual(PlayniteLibraryPrivateState.CurrentVersion, reset!.Version);
        Assert.AreEqual(0, reset.Items.Count);
        Assert.AreEqual(0, reset.FavoriteSavedIds.Count);
        Assert.AreEqual(0, reset.RecentSavedIds.Count);
        Assert.AreEqual(0, reset.ManualSavedIds.Count);
        Assert.AreEqual(0, reset.ExcludedSavedIds.Count);
        Assert.IsFalse(Nodes(Snapshot(widget, 13).Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Stale", StringComparison.Ordinal)));

        page.SetResult(new([Item(0)], null, null, "revision-1"));
        await Bounded(widget.WhenLibraryIdleAsync(), "authoritative reconciliation");

        Assert.AreEqual(PlayniteLibraryPrivateState.CurrentVersion, widget.Organization.Version);
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
        var display = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var baseline = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [display]);
        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await PlayniteLibraryStateStore.SaveAsync(
                state => PlayniteLibraryOrganizationPolicy.SetFavorite(
                    state, display, favorite: true),
                (_, _, _) => ValueTask.FromException<WidgetPrivateStateMutation>(
                    new IOException("fixture")),
                _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                    true, baseline, 1)),
                baseline, 1, CancellationToken.None));
        Assert.AreEqual(0, baseline.FavoriteSavedIds.Count);
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
        await widget.OnActionAsync(new("playnite-library.next",
            PlayniteLibraryPresentation.HomeRailId));
        await Bounded(widget.WhenLibraryIdleAsync(), "later catalog page");
        var laterId = PlayniteLibraryIdentity.FocusId(
            "grid", PlayniteLibraryIdentity.Key("saved-00100"));
        await widget.OnActionAsync(new("playnite-library.launch", laterId));
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
            "playnite-library.filter.recent", "playnite-library.filter.recent"));
        await Bounded(restarted.WhenLibraryIdleAsync(), "cold recent-first resolution");
        var first = Nodes(Snapshot(restarted, 76).Root)
            .First(node => node.ActionId == "playnite-library.launch");
        Assert.AreEqual(laterId, first.Id);
        await restarted.OnActionAsync(new("playnite-library.launch", first.Id));
        CollectionAssert.AreEqual(new[] { "saved-00100" },
            restartedHost.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00100" }, restartedHost.Launches.ToArray());

        await restarted.OnActionAsync(new("playnite-library.next",
            PlayniteLibraryPresentation.HomeRailId));
        await Bounded(restarted.WhenLibraryIdleAsync(), "recent catalog overlap traversal");
        Assert.AreEqual(1, Nodes(Snapshot(restarted, 77).Root).Count(node =>
            node.ActionId == "playnite-library.launch" && node.Id == laterId));
        await Background(restarted);
    }


    [TestMethod]
    public async Task ManualCasReplayPreservesFavoritesGroupsAndRecentOrder()
    {
        var a = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var b = new PlayniteLibraryDisplayItem("saved-b", "B", "Windows");
        var c = new PlayniteLibraryDisplayItem("saved-c", "C", "Xbox");
        var baseline = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [a, b])
        {
            FavoriteSavedIds = [a.SavedId],
            RecentSavedIds = [b.SavedId],
        };
        var latest = baseline with
        {
            Items = [c, a, b],
            RecentSavedIds = [c.SavedId, b.SavedId],
        };
        PlayniteLibraryPrivateState? written = null;
        var attempt = 0;
        await PlayniteLibraryStateStore.SaveAsync(
            state => PlayniteLibraryOrganizationPolicy.SetManual(state, a, true),
            (value, _, _) =>
            {
                if (attempt++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "fixture"));
                written = value;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
                true, latest, 2)), baseline, 1, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { a.SavedId }, written!.ManualSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { a.SavedId }, written.FavoriteSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { c.SavedId, b.SavedId },
            written.RecentSavedIds.ToArray());
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
            .Single(node => node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

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
            var tileId = PlayniteLibraryIdentity.FocusId("grid",
                PlayniteLibraryIdentity.Key($"saved-{index:D5}"));
            await widget.OnActionAsync(new("playnite-library.launch", tileId));
        }

        Assert.AreEqual(PlayniteLibraryPrivateState.MaximumRecentItems,
            widget.Organization.RecentSavedIds.Count);
        Assert.AreEqual("saved-00034", widget.Organization.RecentSavedIds[0]);
        Assert.AreEqual("saved-00003", widget.Organization.RecentSavedIds[^1]);

        await widget.OnActionAsync(new("playnite-library.filter.recent",
            "playnite-library.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "recent collection reload");
        var first = Nodes(Snapshot(widget, 60).Root)
            .First(node => node.ActionId == "playnite-library.launch");
        Assert.AreEqual(PlayniteLibraryIdentity.FocusId("grid",
            PlayniteLibraryIdentity.Key("saved-00034")), first.Id);
        CollectionAssert.AreEqual(widget.Organization.RecentSavedIds.ToArray(),
            host.Queries[^1].Query.FavoriteSavedIds.ToArray());

        await widget.OnActionAsync(new("playnite-library.filter.recent",
            "playnite-library.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "all collection reload");
        Assert.AreEqual(0, host.Queries[^1].Query.FavoriteSavedIds.Count);
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
            "playnite-library.filter.recent", "playnite-library.filter.recent"));
        await Bounded(widget.WhenLibraryIdleAsync(), "empty recent-first reload");
        var queriesBeforeLaunch = host.Queries.Count;
        var promotedId = PlayniteLibraryIdentity.FocusId(
            "grid", PlayniteLibraryIdentity.Key("saved-00001"));

        await widget.OnActionAsync(new("playnite-library.launch", promotedId));

        var promotedSnapshot = Snapshot(widget, 601);
        var launchTiles = Nodes(promotedSnapshot.Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();
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

        await widget.OnActionAsync(new("playnite-library.launch", promotedId));

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
    public async Task PermissionDeniedIsNotRenderedAsEmptyOrOffline()
    {
        var host = new FakeHost(0)
        {
            QueryHandler = (_, _) => ValueTask.FromException<WidgetAppLibraryPage>(
                new WidgetCapabilityException("permission_denied", "private")),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Bounded(widget.WhenLibraryIdleAsync(), "permission-denied load");
        var denied = Snapshot(widget, 653);
        Assert.IsTrue(Nodes(denied.Root).Any(node =>
            (node.Text ?? string.Empty).Contains("permission denied",
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(Nodes(denied.Root).Any(node =>
            node.ActionId == "playnite-library.retry"));
        Assert.IsFalse(Nodes(denied.Root).Any(node => node.Id == "playnite-library.empty"));
        await Background(widget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task RecentMutationConflictPreservesOrganizationAndConcurrentOrder()
    {
        var a = new PlayniteLibraryDisplayItem("saved-a", "A", "Steam");
        var b = new PlayniteLibraryDisplayItem("saved-b", "B", "Steam");
        var c = new PlayniteLibraryDisplayItem("saved-c", "C", "Windows");
        var d = new PlayniteLibraryDisplayItem("saved-d", "D", "Windows");
        var e = new PlayniteLibraryDisplayItem("saved-e", "E", "Xbox");
        var baseline = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [a, b, c, d])
        {
            FavoriteSavedIds = [c.SavedId],
            VariantGroups =
            [
                new(PlayniteLibraryIdentity.GroupId(c.SavedId, d.SavedId),
                    [c.SavedId, d.SavedId], d.SavedId),
            ],
            RecentSavedIds = [a.SavedId, b.SavedId],
        };
        var latest = baseline with
        {
            Items = [e, a, b, c, d],
            RecentSavedIds = [e.SavedId, a.SavedId, b.SavedId],
        };
        PlayniteLibraryPrivateState? written = null;
        var attempts = 0;

        var result = await PlayniteLibraryStateStore.SaveAsync(
            state => PlayniteLibraryOrganizationPolicy.RecordRecent(state, b),
            (state, _, _) =>
            {
                if (attempts++ == 0)
                    return ValueTask.FromException<WidgetPrivateStateMutation>(
                        new WidgetCapabilityException("state_conflict", "fixture"));
                written = state;
                return ValueTask.FromResult(new WidgetPrivateStateMutation(3));
            },
            _ => ValueTask.FromResult(new WidgetPrivateStateValue<PlayniteLibraryPrivateState>(
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
            .First(node => node.ActionId == "playnite-library.launch");
        await widget.OnActionAsync(new("playnite-library.favorite", first.Id));
        await widget.OnActionAsync(new("playnite-library.launch", first.Id));
        await Background(widget);

        var restartedHost = new FakeHost(2, state);
        var restarted = Create(restartedHost);
        await Interactive(restarted);
        await Ready(restarted, restartedHost);
        await Bounded(restarted.WhenWarmStateIdleAsync(), "recent warm-state restart");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restarted.Organization.RecentSavedIds.ToArray());
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restartedHost.Authority.FavoriteGameIds.ToArray());

        await restarted.OnActionAsync(new("playnite-library.recent.clear",
            "playnite-library.recent.clear"));
        Assert.AreEqual(0, restarted.Organization.RecentSavedIds.Count);
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            restartedHost.Authority.FavoriteGameIds.ToArray());
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
            .Single(node => node.ActionId == "playnite-library.launch");
        await missingWidget.OnActionAsync(new("playnite-library.launch", missingTile.Id));
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
            .Single(node => node.ActionId == "playnite-library.launch");
        var launch = staleWidget.OnActionAsync(
            new("playnite-library.launch", staleTile.Id)).AsTask();
        await Bounded(admitted.Task, "stale recent admission");
        await staleWidget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
        await Bounded(staleWidget.WhenLibraryIdleAsync(), "stale recent replacement");
        release.TrySetResult(new(WidgetAppLaunchObservationState.Running, true, false));
        await Bounded(launch, "stale recent completion");
        Assert.AreEqual(0, staleWidget.Organization.RecentSavedIds.Count);
        await Background(staleWidget);
    }

    [TestMethod, Timeout(30_000)]
    public async Task NewerLaunchMakesCancellationIgnoringCompletionStale()
    {
        var firstWriteStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new FakeHost(2)
        {
            LaunchHandler = (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (request.AppId.EndsWith("1", StringComparison.Ordinal))
                    secondStarted.TrySetResult();
                return ValueTask.FromResult(new WidgetAppLaunchObservation(
                    WidgetAppLaunchObservationState.LauncherStarted, false, false));
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var blockFirstRecent = true;
        host.StateWriteHandler = async (request, _) =>
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(
                request.CanonicalJsonBase64));
            var state = JsonSerializer.Deserialize<PlayniteLibraryPrivateState>(json)!;
            if (blockFirstRecent && state.RecentSavedIds.SequenceEqual(
                    ["saved-00000"], StringComparer.Ordinal))
            {
                blockFirstRecent = false;
                firstWriteStarted.TrySetResult();
                await releaseFirstWrite.Task.ConfigureAwait(false);
            }
            return await host.State.WriteAsync(request, CancellationToken.None)
                .ConfigureAwait(false);
        };
        var tiles = Nodes(Snapshot(widget, 64).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        var first = widget.OnActionAsync(new("playnite-library.launch", tiles[0].Id)).AsTask();
        await Bounded(firstWriteStarted.Task, "accepted first launch Recent write");
        var second = widget.OnActionAsync(new("playnite-library.launch", tiles[1].Id)).AsTask();
        releaseFirstWrite.TrySetResult();
        await Bounded(secondStarted.Task, "replacement launch admission");
        await Bounded(Task.WhenAll(first, second), "replacement launch completion");

        CollectionAssert.AreEqual(new[] { "saved-00001" },
            widget.Organization.RecentSavedIds.ToArray());
        var durable = JsonSerializer.Deserialize<PlayniteLibraryPrivateState>(host.State.Json!)!;
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            durable.RecentSavedIds.ToArray());
        var rendered = Nodes(Snapshot(widget, 65).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();
        Assert.IsFalse(rendered[0].AccessibilityLabel!.Contains(
            "Running", StringComparison.Ordinal));
        StringAssert.Contains(rendered[1].AccessibilityLabel!, "Launcher started");
        await Background(widget);
    }

    [TestMethod]
    public void RejectedLaunchAdmissionCannotReserveGeneration()
    {
        var coordinator = new PlayniteLibraryLaunchPersistenceCoordinator();
        foreach (var admission in new[]
                 {
                     WidgetOperationAdmission.RejectedInactive,
                     WidgetOperationAdmission.RejectedCapacity,
                 })
        {
            var ready = new TaskCompletionSource<long>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = new WidgetOperationHandle(admission,
                Task.FromResult(new WidgetOperationResult(
                    WidgetOperationStatus.Rejected)));

            Assert.IsFalse(coordinator.CompleteAdmission(handle, ready));
            Assert.AreEqual(0L, coordinator.CurrentGeneration,
                $"{admission} invalidated the active launch generation.");
            Assert.IsTrue(ready.Task.IsCanceled);
        }
    }

    [TestMethod]
    public async Task LaunchPersistenceCoordinatorRestoresCurrentRecentAfterReplacement()
    {
        var coordinator = new PlayniteLibraryLaunchPersistenceCoordinator();
        var ready = new TaskCompletionSource<long>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = new WidgetOperationHandle(
            WidgetOperationAdmission.Started,
            Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Succeeded)));
        Assert.IsTrue(coordinator.CompleteAdmission(handle, ready));
        var generation = await ready.Task;
        var first = new PlayniteLibraryDisplayItem("saved-first", "First", "Steam");
        var current = new PlayniteLibraryDisplayItem("saved-current", "Current", "Xbox");
        var durable = PlayniteLibraryPrivateState.Empty with
        {
            Items = [first, current],
            RecentSavedIds = [current.SavedId],
        };
        var writes = 0;

        async Task<bool> Save(
            Func<PlayniteLibraryPrivateState, PlayniteLibraryStateMutation> apply,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mutation = apply(durable);
            Assert.IsTrue(mutation.Accepted);
            durable = mutation.State;
            if (++writes == 1) coordinator.Invalidate();
            await Task.CompletedTask;
            return true;
        }

        await coordinator.CommitRecentAsync(
            generation,
            WidgetAppLaunchObservationState.LauncherStarted,
            first,
            Save,
            () => [current.SavedId],
            CancellationToken.None);

        Assert.AreEqual(2, writes);
        CollectionAssert.AreEqual(new[] { current.SavedId },
            durable.RecentSavedIds.ToArray());
    }

    [TestMethod, Timeout(30_000)]
    public async Task FailedLaunchRestoresExactFocusAndRetriesSameGame()
    {
        var attempts = 0;
        var host = new FakeHost(1)
        {
            LaunchHandler = (_, _) => ++attempts == 1
                ? ValueTask.FromException<WidgetAppLaunchObservation>(
                    new WidgetCapabilityException("launch_failed", "fixture"))
                : ValueTask.FromResult(new WidgetAppLaunchObservation(
                    WidgetAppLaunchObservationState.LauncherStarted, false, false)),
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(Snapshot(widget, 66).Root)
            .Single(node => node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        var failed = Snapshot(widget, 67);
        Assert.AreEqual(tile.Id, failed.InitialFocusId);
        StringAssert.Contains(Nodes(failed.Root).Single(node => node.Id == tile.Id)
            .AccessibilityLabel!, "Failed");
        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));
        var retried = Snapshot(widget, 68);
        Assert.AreEqual(tile.Id, retried.InitialFocusId);
        StringAssert.Contains(Nodes(retried.Root).Single(node => node.Id == tile.Id)
            .AccessibilityLabel!, "Launcher started");
        CollectionAssert.AreEqual(new[] { "saved-00000" },
            widget.Organization.RecentSavedIds.ToArray());
        await Background(widget);
    }

    [TestMethod]
    public void MissingAndReplacementIdentitiesDoNotInheritRecentHistory()
    {
        var old = new PlayniteLibraryDisplayItem("saved-old", "Game", "Steam");
        var state = new PlayniteLibraryPrivateState(
            PlayniteLibraryPrivateState.CurrentVersion, [old])
        {
            RecentSavedIds = [old.SavedId],
        };
        var replacement = PlayniteLibraryItem.From(WithPresentation(
            Item(0) with { SavedId = "saved-new" }, displayName: old.DisplayName));

        var projected = PlayniteLibraryOrganizationPolicy.ProjectPage(state, [replacement]);

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
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        await widget.OnActionAsync(new("playnite-library.launch", first[0].Id));
        await widget.OnActionAsync(new("playnite-library.launch", first[1].Id));
        await widget.OnActionAsync(new("playnite-library.launch", first[2].Id));

        var tiles = Nodes(Snapshot(widget, 21).Root)
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();
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
            .Single(node => node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

        var rendered = Nodes(Snapshot(widget, 23).Root)
            .Single(node => node.ActionId == "playnite-library.launch");
        StringAssert.Contains(rendered.AccessibilityLabel!, "Request accepted");
        Assert.IsFalse(rendered.AccessibilityLabel!.Contains("Running", StringComparison.Ordinal));
        Assert.AreEqual(0, widget.Organization.RecentSavedIds.Count);
        await Background(widget);
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
            .Single(node => node.ActionId == "playnite-library.launch");
        var launch = widget.OnActionAsync(new("playnite-library.launch", tile.Id)).AsTask();
        await Bounded(admitted.Task, "stale launch admission");

        var revision = widget.Collection.Revision;
        await widget.OnActionAsync(new("playnite-library.refresh", "playnite-library.refresh"));
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
            .Single(node => node.ActionId == "playnite-library.launch");

        await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

        Assert.AreEqual(0, host.Launches.Count);
        Assert.AreEqual("Failed · The selected game is no longer installed",
            widget.RenderState.Value.Status);
        StringAssert.Contains(
            Nodes(widget.RenderSnapshot("launcher.test", 5).Root)
                .Single(node => node.ActionId == "playnite-library.launch")
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
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        foreach (var tile in tiles)
            await widget.OnActionAsync(new("playnite-library.launch", tile.Id));

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
            .Where(node => node.ActionId == "playnite-library.launch").ToArray();

        Assert.AreEqual(2, tiles.Length);
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        Assert.AreNotEqual(tiles[0].Id, tiles[1].Id);
        StringAssert.Contains(tiles[0].AccessibilityLabel ?? string.Empty, "Steam");
        StringAssert.Contains(tiles[1].AccessibilityLabel ?? string.Empty, "Windows");
        await widget.OnActionAsync(new("playnite-library.launch", tiles[1].Id));
        CollectionAssert.AreEqual(new[] { "saved-00001" },
            host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-00001" }, host.Launches.ToArray());
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
        await widget.OnActionAsync(new("playnite-library.next",
            PlayniteLibraryPresentation.HomeRailId));
        await Bounded(widget.WhenLibraryIdleAsync(), "adjacent failure drain");

        Assert.AreEqual(LauncherWidget.PageSize, widget.Collection.Items.Count);
        Assert.IsNotNull(widget.Collection.Error);
        Assert.IsTrue(Nodes(widget.RenderSnapshot("launcher.test", 5).Root)
            .Any(node => node.Id == "playnite-library.retained-error"));
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

    private static LauncherWidget Create(FakeHost host)
    {
        var services = host.Services();
        return WidgetTestHost.Attach(
            new LauncherWidget(new TestApplicationService(services, host)), services);
    }

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
            node.Id == PlayniteLibraryPresentation.ScrollId);
        Assert.AreEqual(before, scroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.LeftBumper &&
            shortcut.ActionId == "playnite-library.previous"));
        Assert.AreEqual(after, scroll.Shortcuts.Any(shortcut =>
            shortcut.Button == ControllerButton.RightBumper &&
            shortcut.ActionId == "playnite-library.next"));
    }

    private static void AssertValidCollectionAnchor(ViewSnapshot snapshot)
    {
        var scroll = Nodes(snapshot.Root).SingleOrDefault(node =>
            node.Id == PlayniteLibraryPresentation.ScrollId);
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

    private sealed class FakeConnectionClient : IPlayniteBridgeClient
    {
        internal int ProbeCalls { get; private set; }

        public ValueTask<PlayniteBridgeConnectionResult> ProbeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCalls++;
            return ValueTask.FromResult(new PlayniteBridgeConnectionResult(
                PlayniteBridgeConnectionKind.NotConfigured, "credential_missing"));
        }

        public ValueTask SaveCredentialAsync(
            string token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Credential mutation is outside this fixture.");

        public ValueTask DeleteCredentialAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Credential mutation is outside this fixture.");

        public void Dispose() { }
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

    private sealed class TestApplicationService(
        WidgetHostServices services,
        FakeHost host) :
        IPlayniteLibraryApplicationService
    {
        public bool OwnsArtworkContent => false;

        public ValueTask<WidgetAppLibraryPage> QueryAsync(
            WidgetAppLibraryQuery query, WidgetCollectionCursor? cursor,
            WidgetCursorDirection? direction, int limit, bool refresh,
            CancellationToken cancellationToken) => services.AppLibrary.QueryAsync(
                query, cursor, direction, limit, refresh, cancellationToken);

        public async ValueTask<PlayniteLibraryQueryResult> QueryWithAuthorityAsync(
            WidgetAppLibraryQuery query,
            PlayniteLibraryQueryContext context,
            WidgetCollectionCursor? cursor,
            WidgetCursorDirection? direction,
            int limit,
            bool refresh,
            CancellationToken cancellationToken)
        {
            return await host.QueryWithCurrentAuthorityAsync(
                query, context, cursor, direction, limit, refresh, cancellationToken);
        }

        public ValueTask<IReadOnlyList<WidgetAppLibraryItem>> ResolveSavedAsync(
            IReadOnlyList<string> savedIds, CancellationToken cancellationToken) =>
            services.AppLibrary.ResolveSavedAsync(savedIds, cancellationToken);

        public ValueTask<WidgetRunningAppObservation> ObserveRunningAsync(
            CancellationToken cancellationToken) =>
            services.AppLibrary.ObserveRunningAsync(cancellationToken);

        public ValueTask<WidgetAppLibraryItem?> ConfirmRunningAsync(
            string savedId, string revision, CancellationToken cancellationToken) =>
            services.AppLibrary.ConfirmRunningAsync(
                savedId, revision, cancellationToken);

        public ValueTask<WidgetAppLaunchObservation> LaunchObservedAsync(
            string appId, WidgetAppLaunchOverlayBehavior overlayBehavior,
            CancellationToken cancellationToken) =>
            services.AppLibrary.LaunchObservedAsync(
                appId, overlayBehavior, cancellationToken);

        public ValueTask<WidgetEncodedArtwork?> ResolveArtworkAsync(
            WidgetArtworkHandle handle, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<WidgetEncodedArtwork?>(null);
        }

        public async ValueTask<WidgetAppLibraryItem?> SetFavoriteAsync(
            string gameId, bool favorite, CancellationToken cancellationToken)
        {
            host.SetFavorite(gameId, favorite);
            return (await ResolveSavedAsync([gameId], cancellationToken)).SingleOrDefault();
        }

        public async ValueTask<WidgetAppLibraryItem?> SetHiddenAsync(
            string gameId, bool hidden, CancellationToken cancellationToken)
        {
            host.SetHidden(gameId, hidden);
            return (await ResolveSavedAsync([gameId], cancellationToken)).SingleOrDefault();
        }

        public async ValueTask<WidgetAppLibraryItem?> SetCategoriesAsync(
            string gameId, IReadOnlyList<string> categories,
            CancellationToken cancellationToken)
        {
            host.SetCategories(gameId, categories);
            return (await ResolveSavedAsync([gameId], cancellationToken)).SingleOrDefault();
        }

        public async ValueTask<WidgetAppLibraryItem?> SetCompletionStatusAsync(
            string gameId, string completionStatus, CancellationToken cancellationToken)
        {
            host.SetCompletionStatus(gameId, completionStatus);
            return (await ResolveSavedAsync([gameId], cancellationToken)).SingleOrDefault();
        }

        public ValueTask<PlayniteLibraryCategory?> CreateCategoryAsync(
            string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(host.CreateCategory(name));
        }

        public ValueTask<IReadOnlyList<string>> GetCompletionStatusesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<string>>(
                ["Not Played", "Playing", "Completed"]);
        }

        public ValueTask<WidgetPrivateStateValue<PlayniteLibraryPrivateState>> ReadStateAsync(
            CancellationToken cancellationToken) =>
            services.PrivateState.ReadAsync<PlayniteLibraryPrivateState>(
                cancellationToken: cancellationToken);

        public ValueTask<WidgetPrivateStateMutation> WriteStateAsync(
            PlayniteLibraryPrivateState state, long? expectedRevision,
            CancellationToken cancellationToken) => services.PrivateState.WriteAsync(
                state, expectedRevision, cancellationToken: cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeHost
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            WidgetTestPrivateState, AuthorityBox> Authorities = new();
        private readonly int _count;
        private readonly WidgetTestPrivateState _state;
        private readonly AuthorityBox _authority;
        internal WidgetTestPrivateState State => _state;
        internal PlayniteLibraryAuthorityProjection Authority => _authority.Value;
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
        internal List<PlayniteLibraryQueryContext> QueryContexts { get; } = [];
        internal Func<LaunchWidgetAppLibraryItemRequest, CancellationToken,
            ValueTask<WidgetAppLaunchObservation>>? LaunchHandler { get; set; }
        internal Func<WriteWidgetPrivateStateTransportRequest, CancellationToken,
            ValueTask<WidgetPrivateStateTransportMutation>>? StateWriteHandler { get; set; }
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
            _authority = Authorities.GetValue(_state, CreateAuthority);
        }

        internal async ValueTask<PlayniteLibraryQueryResult> QueryWithCurrentAuthorityAsync(
            WidgetAppLibraryQuery query,
            PlayniteLibraryQueryContext context,
            WidgetCollectionCursor? cursor,
            WidgetCursorDirection? direction,
            int limit,
            bool refresh,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new WidgetAppLibraryCursorRequest(
                query, cursor?.Value, direction, limit, refresh);
            QueryContexts.Add(context);
            if (QueryHandler is not null)
            {
                var custom = await Query(request, cancellationToken);
                return new(FilterPage(custom, query, context), _authority.Value);
            }

            FirstQueryStarted.TrySetResult();
            Queries.Add(request);
            var source = Enumerable.Range(0, _count)
                .Select(index => (Index: index, Item: ItemFactory(index)));
            var authority = _authority.Value;
            source = Filter(source, query, context, authority);
            var values = source.ToArray();
            var offset = request.Cursor is null ? 0 : int.Parse(
                request.Cursor.AsSpan(request.Cursor.LastIndexOf('.') + 1),
                System.Globalization.CultureInfo.InvariantCulture);
            if (FailAfterOffset == offset)
                throw new WidgetCapabilityException("platform_unavailable", "private");
            MaximumRequestedLimit = Math.Max(MaximumRequestedLimit, limit);
            var pageValues = values.Skip(offset).Take(limit).ToArray();
            if (pageValues.Length != 0)
                MaximumObservedIndex = Math.Max(
                    MaximumObservedIndex, pageValues.Max(value => value.Index));
            var before = offset == 0 ? null :
                $"cursor.{Math.Max(0, offset - limit)}";
            var after = offset + pageValues.Length < values.Length
                ? $"cursor.{offset + pageValues.Length}"
                : null;
            var page = new WidgetAppLibraryPage(
                pageValues.Select(value => value.Item).ToArray(),
                before, after, "revision-1");
            return new(page, authority);
        }

        private WidgetAppLibraryPage FilterPage(
            WidgetAppLibraryPage page,
            WidgetAppLibraryQuery query,
            PlayniteLibraryQueryContext context)
        {
            var indexed = page.Items.Select((item, index) => (Index: index, Item: item));
            var filtered = Filter(indexed, query, context, _authority.Value)
                .Select(value => value.Item).ToArray();
            return new(filtered, page.Before, page.After, page.Revision)
            {
                Sources = page.Sources,
            };
        }

        private static IEnumerable<(int Index, WidgetAppLibraryItem Item)> Filter(
            IEnumerable<(int Index, WidgetAppLibraryItem Item)> source,
            WidgetAppLibraryQuery query,
            PlayniteLibraryQueryContext context,
            PlayniteLibraryAuthorityProjection authority)
        {
            var items = source;
            if (context.Scope == PlayniteLibraryQueryScope.Hidden)
                items = items.Where(value => authority.HiddenGameIds.Contains(
                    value.Item.SavedId, StringComparer.Ordinal));
            else
                items = items.Where(value => !authority.HiddenGameIds.Contains(
                    value.Item.SavedId, StringComparer.Ordinal));
            if (context.Scope == PlayniteLibraryQueryScope.Category)
            {
                var members = authority.Categories.FirstOrDefault(category =>
                    string.Equals(category.Name, context.CategoryName,
                        StringComparison.OrdinalIgnoreCase))?.SavedIds ?? [];
                items = items.Where(value => members.Contains(
                    value.Item.SavedId, StringComparer.Ordinal));
            }
            if (query.FavoriteSavedIds.Count != 0)
                items = items.Where(value => query.FavoriteSavedIds.Contains(
                    value.Item.SavedId, StringComparer.Ordinal));
            return items;
        }

        internal void SetFavorite(string gameId, bool favorite)
        {
            var values = _authority.Value.FavoriteGameIds
                .Where(value => value != gameId).ToList();
            if (favorite) values.Add(gameId);
            _authority.Value = _authority.Value with { FavoriteGameIds = values };
        }

        internal void SetHidden(string gameId, bool hidden)
        {
            var values = _authority.Value.HiddenGameIds
                .Where(value => value != gameId).ToList();
            if (hidden) values.Add(gameId);
            _authority.Value = _authority.Value with { HiddenGameIds = values };
        }

        internal void SetCategories(string gameId, IReadOnlyList<string> names)
        {
            _authority.Value = _authority.Value with
            {
                Categories = _authority.Value.Categories.Select(category => category with
                {
                    SavedIds = names.Contains(category.Name,
                            StringComparer.OrdinalIgnoreCase)
                        ? category.SavedIds.Append(gameId).Distinct(StringComparer.Ordinal).ToArray()
                        : category.SavedIds.Where(value => value != gameId).ToArray(),
                }).ToArray(),
            };
        }

        internal void SetCompletionStatus(string gameId, string completionStatus)
        {
            var values = new Dictionary<string, string?>(
                _authority.Value.CompletionStatuses, StringComparer.Ordinal)
            {
                [gameId] = completionStatus,
            };
            _authority.Value = _authority.Value with { CompletionStatuses = values };
        }

        internal PlayniteLibraryCategory? CreateCategory(string name)
        {
            if (_authority.Value.Categories.Any(category => string.Equals(
                    category.Name, name, StringComparison.OrdinalIgnoreCase))) return null;
            var created = new PlayniteLibraryCategory(
                "category." + (_authority.Value.Categories.Count + 1).ToString("x32"),
                name, []);
            _authority.Value = _authority.Value with
            {
                Categories = _authority.Value.Categories.Append(created).ToArray(),
            };
            return created;
        }

        private static AuthorityBox CreateAuthority(WidgetTestPrivateState state)
        {
            PlayniteLibraryPrivateState? persisted = null;
            if (state.Json is { } json)
                try { persisted = JsonSerializer.Deserialize<PlayniteLibraryPrivateState>(json); }
                catch (JsonException) { }
            persisted = PlayniteLibraryOrganizationPolicy.Normalize(persisted);
            return new(new(
                persisted.FavoriteSavedIds.ToArray(),
                persisted.ExcludedSavedIds.ToArray(),
                persisted.Categories.ToArray(),
                new Dictionary<string, string?>(StringComparer.Ordinal)));
        }

        private sealed class AuthorityBox(PlayniteLibraryAuthorityProjection value)
        {
            internal PlayniteLibraryAuthorityProjection Value { get; set; } = value;
        }

        internal WidgetHostServices Services()
        {
            var builder = new WidgetTestHostServicesBuilder()
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
            .WithHandler(WidgetPrivateStateCapabilities.Read, _state.ReadAsync)
            .WithHandler(WidgetPrivateStateCapabilities.Write,
                (request, token) => StateWriteHandler is null
                    ? _state.WriteAsync(request, token)
                    : StateWriteHandler(request, token))
            .WithHandler(WidgetPrivateStateCapabilities.Clear, _state.ClearAsync);
            return builder.Build();
        }

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
