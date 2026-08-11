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
            widget.Collection.Items[^1].Value.DisplayName);
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
            widget.Collection.Items[0].Value.DisplayName);
        Assert.AreEqual(
            GameLauncherIdentity.FocusId("grid", widget.Collection.Items[63].Key),
            widget.Collection.RequestedFocusId);
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
                new string((char)('a' + index % 26), 120),
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
    public async Task FavoritesAndExplicitVariantsSurviveRestartAndDisappearance()
    {
        var state = new WidgetTestPrivateState();
        var host = new FakeHost(2, state)
        {
            ItemFactory = index => Item(index) with { DisplayName = "Shared title" },
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
        var baseline = new GameLauncherPrivateState(2, [a, b, c, d])
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
        var state = new GameLauncherPrivateState(2, [old])
        {
            FavoriteSavedIds = [old.SavedId],
        };
        var sameIdentity = GameLauncherItem.From(new WidgetAppLibraryItem(
            "app-new", "Updated title", WidgetAppLibraryKind.Game)
        {
            SavedId = old.SavedId,
            SourceAttribution = "Steam",
        });
        var refreshed = GameLauncherOrganizationPolicy.ProjectPage(state, [sameIdentity]);
        Assert.AreEqual("Updated title",
            refreshed.Items.Single(item => item.SavedId == old.SavedId).DisplayName);
        Assert.IsTrue(refreshed.FavoriteSavedIds.Contains(old.SavedId));

        var replacement = GameLauncherItem.From(new WidgetAppLibraryItem(
            "app-replacement", "Updated title", WidgetAppLibraryKind.Game)
        {
            SavedId = "saved-replacement",
            SourceAttribution = "Steam",
        });
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
            {"Version":1,"Items":[{"SavedId":"saved-stale","DisplayName":"Stale","SourceAttribution":"Old"}],"FavoriteSavedIds":["saved-stale"]}
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
        Assert.IsFalse(Nodes(Snapshot(widget, 13).Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Stale", StringComparison.Ordinal)));

        page.SetResult(new([Item(0)], null, null, "revision-1"));
        await Bounded(widget.WhenLibraryIdleAsync(), "authoritative reconciliation");

        Assert.AreEqual(GameLauncherPrivateState.CurrentVersion, widget.Organization.Version);
        Assert.IsFalse(widget.Organization.Items.Any(item => item.SavedId == "saved-stale"));
        Assert.AreEqual(0, widget.Organization.FavoriteSavedIds.Count);
        Assert.AreEqual(0, widget.Organization.VariantGroups.Count);
        Assert.IsFalse(Nodes(Snapshot(widget, 14).Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Stale", StringComparison.Ordinal)));
        await Background(widget);
    }

    [TestMethod]
    public async Task FailedPersistenceLeavesCommittedOrganizationUnchanged()
    {
        var display = new GameLauncherDisplayItem("saved-a", "A", "Steam");
        var baseline = new GameLauncherPrivateState(2, [display]);
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
    public async Task ExactLaunchRevalidatesSavedIdentity()
    {
        var host = new FakeHost(1)
        {
            ResolveHandler = request =>
                [Item(0) with { AppId = "app-current" }],
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var tile = Nodes(widget.RenderSnapshot("launcher.test", 2).Root)
            .Single(node => node.ActionId == "game-launcher.launch");

        await widget.OnActionAsync(new("game-launcher.launch", tile.Id));

        CollectionAssert.AreEqual(new[] { "saved-00000" }, host.ResolveRequests[^1].ToArray());
        CollectionAssert.AreEqual(new[] { "app-current" }, host.Launches.ToArray());
        await Background(widget);
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
            ItemFactory = index => Item(index) with
            {
                DisplayName = "Shared title",
                SourceAttribution = index == 0 ? "Steam" : "Windows",
            },
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
    public async Task ArtworkAndResponsiveGridRemainSemanticAndBounded()
    {
        var host = new FakeHost(1)
        {
            ItemFactory = index => Item(index) with
            {
                ArtworkHandle = "library.art.0123456789abcdef0123456789abcdef",
            },
        };
        var widget = Create(host);
        await Interactive(widget);
        await Ready(widget, host);
        var snapshot = widget.RenderSnapshot("launcher.test", 6);

        Assert.AreEqual(WidgetSurfaceMode.Wide, snapshot.Surface?.Mode);
        var grid = Nodes(snapshot.Root).Single(node => node.Id == "game-launcher.library.grid");
        Assert.AreEqual(ViewNodeKind.Grid, grid.Kind);
        Assert.AreEqual(5, grid.GridMaximumColumns);
        Assert.AreEqual("library.art.0123456789abcdef0123456789abcdef",
            Nodes(snapshot.Root).Single(node => node.ArtworkHandle is not null).ArtworkHandle);
        Assert.IsLessThan(100, Nodes(snapshot.Root).Count());
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

    private static WidgetAppLibraryItem Item(int index) => new(
        $"app-{index:D5}", $"Game {index:D5}", WidgetAppLibraryKind.Game)
    {
        SavedId = $"saved-{index:D5}",
        SourceAttribution = index % 2 == 0 ? "Steam" : "Windows",
    };

    private sealed class FakeHost
    {
        private readonly int _count;
        private readonly WidgetTestPrivateState _state;
        internal WidgetTestPrivateState State => _state;
        internal int MaximumObservedIndex { get; private set; } = -1;
        internal int MaximumRequestedLimit { get; private set; }
        internal int? FailAfterOffset { get; set; }
        internal Func<int, WidgetAppLibraryItem> ItemFactory { get; set; } = Item;
        internal Func<WidgetAppLibraryCursorRequest, CancellationToken,
            ValueTask<WidgetAppLibraryPage>>? QueryHandler { get; set; }
        internal Func<ResolveSavedWidgetAppLibraryItemsRequest,
            IReadOnlyList<WidgetAppLibraryItem>>? ResolveHandler { get; set; }
        internal List<IReadOnlyList<string>> ResolveRequests { get; } = [];
        internal List<string> Launches { get; } = [];
        internal Func<LaunchWidgetAppLibraryItemRequest, CancellationToken,
            ValueTask<WidgetAppLaunchObservation>>? LaunchHandler { get; set; }
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
            .WithPrivateState(_state)
            .Build();

        private ValueTask<WidgetAppLibraryPage> Query(
            WidgetAppLibraryCursorRequest request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FirstQueryStarted.TrySetResult();
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
