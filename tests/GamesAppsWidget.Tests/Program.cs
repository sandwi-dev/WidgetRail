using GameBarAlternative.FirstPartyWidgets.GamesApps;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Visible lifecycle discovers trusted games before catalog browsing", LoadsFirstPage),
    ("Trusted games auto-curate idempotently with bounded feedback", AutoCuratesTrustedGames),
    ("Broker-shaped opaque IDs survive reconciliation and persistence", BrokerOpaqueIdsPersist),
    ("Automatic discovery walks bounded pages for trusted games", AutoCuratesGamesBeyondFirstPage),
    ("Refresh preserves order and focus while appending newly trusted games", RefreshAppendsGames),
    ("Disappearing games retain order and stable focus identity on reappearance", DisappearanceRetainsOrder),
    ("Explicit game exclusion survives disappearance and reappearance", ExclusionSurvivesReappearance),
    ("Identity replacement is a new game while applications remain opt-in", IdentityReplacementIsNewGame),
    ("Automatic provenance hides reclassified entries while explicit apps remain", ReclassificationPreservesOptIn),
    ("Current resolved classification authoritatively hides an automatic game", ResolvedReclassificationIsAuthoritative),
    ("Concurrent exclusion wins catalog reconciliation through bounded CAS", ConcurrentExclusionWins),
    ("Concurrent display removal wins background reconciliation through bounded CAS", ConcurrentDisplayRemovalWins),
    ("Library mutation reconciliation and CAS policy is render independent", LibraryPolicyIsRenderIndependent),
    ("Legacy unsupported and invalid schemas reset atomically before fresh reconciliation", LegacySchemasResetAtomically),
    ("Bounded exclusion storage refuses removal without losing membership", FullExclusionSetRefusesRemoval),
    ("Worst-case display projection remains inside private-state bounds", ProjectedStateIsBounded),
    ("Empty add catalog returns to the library without a dead end", EmptyCatalogReturnsToLibrary),
    ("Shared state surfaces keep loading empty and failure controller-safe", SharedStateSurfaces),
    ("Library starts curated and catalog is a bounded vertical controller picker", RendersControllerStrip),
    ("One saved app uses one full-width icon-led focus target", OneAppUsesCompactTile),
    ("Resolved application pixels replace the semantic fallback icon", ResolvedIconRenders),
    ("Many saved apps retain a compact vertical focus list", ManyAppsUseCompactRail),
    ("Maximum curated long names remain bounded and controller reachable", MaximumLongLibraryIsBounded),
    ("Library feedback uses a non-focusable lifecycle-bound toast", ToastFeedbackIsLifecycleBound),
    ("Catalog add remove and B navigation retain a user-owned library", CuratesLibrary),
    ("Catalog removal preserves unrelated rows through restart failure and CAS", CatalogRemovalPreservesLibraryContinuity),
    ("Failed durable removal rolls back the whole Library mutation", FailedRemovalRollsBack),
    ("Removing a focused app selects the nearest surviving row", RemovalSelectsNearestRow),
    ("Interactive A launches only the selected opaque app", LaunchesSelectedApp),
    ("Confirmed launches move the exact curated app to recent-first", SuccessfulLaunchOrdersRecentFirst),
    ("Failed launch keeps curated order and actionable focus", FailedLaunchKeepsOrder),
    ("Curated membership survives widget lifecycle reactivation", CurationSurvivesReactivation),
    ("Reactivation shows cached content while reconciling subsequent discovery", ReactivationReusesCachedLibrary),
    ("Opening Catalog cancels and drains cached background reconciliation", CatalogDrainsBackgroundReconciliation),
    ("Explicit Y refresh reconciles a cached worker library on demand", ExplicitRefreshReconcilesCachedLibrary),
    ("Fast cold load completes without publishing loading state", FastColdLoadDoesNotFlashLoading),
    ("Slow cold load publishes an honest delayed loading state", SlowColdLoadShowsDelayedLoading),
    ("Curated SavedIds survive a fresh widget worker instance", CurationSurvivesNewInstance),
    ("Current schema mutations order and selection survive a fresh worker", CurrentStateSurvivesRestart),
    ("A fresh worker renders persisted display before delayed authority resolves", WarmStartPrecedesAuthorityResolution),
    ("A failed fresh-worker refresh retains disabled last-good display", WarmStartSurvivesRefreshFailure),
    ("Background rejects a cancellation-ignoring fresh-worker resolution", WarmStartRejectsLateResolution),
    ("Catalog pages stay bounded and restore focus in both directions", LoadsMore),
    ("Catalog navigation policy owns bounded forward and reverse transitions", CatalogPolicyOwnsNavigation),
    ("Rapid repeated load more is one busy controller command", LoadMoreIsSingleFlight),
    ("Pagination stops exactly at the bounded catalog maximum", PaginationStopsAtMaximum),
    ("Rapid repeated launch cannot duplicate a Shell launch", LaunchIsSingleFlight),
    ("Leaving the widget cancels in-flight page work", BackgroundCancelsPageWork),
    ("Denied optional launch keeps the readable library usable", LaunchDenialKeepsLibrary),
    ("Permission and provider failures stay recoverable and sanitized", FailureStates),
    ("Subsequent discovery failure retains last-good order and focus", SubsequentFailureKeepsLastGood),
    ("Try again performs a fresh provider load and recovers transient failures", RetryRecoversTransientFailure),
    ("Leaving during retry cancels and drains the runtime-owned library load", BackgroundCancelsRetry),
    ("Manifest and GBSS package validate", PackageValidates),
    ("Games and Apps internals remain split by stable responsibility", ResponsibilitySplitContract),
    ("Pure presentation serializes identically for repeated immutable input", PurePresentationIsDeterministic),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}
if (failures != 0) Environment.Exit(1);
Console.WriteLine($"GamesAppsWidget.Tests passed ({tests.Length} tests)");

static async Task LoadsFirstPage()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("one", "One")], null) },
    };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    Assert.Equal(1, fake.PageRequests.Count);
    Assert.Equal(0, widget.Items.Count);
    await Interactive(widget);
    await OpenCatalog(widget);
    Assert.Equal(2, fake.PageRequests.Count);
    Assert.Equal((0, GamesAppsWidget.PageSize), fake.PageRequests[1]);
    Assert.Equal("one", widget.Items.Single().AppId);
    await Background(widget);
}

static async Task AutoCuratesTrustedGames()
{
    var state = new WidgetTestPrivateState();
    var fake = new FakeAppLibraryHost
    {
        PrivateState = state,
        Pages =
        {
            [0] = Page([
                App("game-a", "Game A", WidgetAppLibraryKind.Game),
                App("application", "Application"),
                App("unknown", "Unknown", WidgetAppLibraryKind.Unknown),
                App("game-b", "Game B", WidgetAppLibraryKind.Game),
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    Assert.SequenceEqual(["game-a", "game-b"],
        widget.CuratedItems.Select(item => item.AppId));
    Assert.Equal(1, fake.PageRequests.Count);
    Assert.Equal(1L, state.Revision);
    using (var persisted = System.Text.Json.JsonDocument.Parse(state.Json!))
    {
        Assert.Equal(3, persisted.RootElement.GetProperty("Version").GetInt32());
        Assert.SequenceEqual(["saved-game-a", "saved-game-b"],
            persisted.RootElement.GetProperty("SavedIds").EnumerateArray()
                .Select(item => item.GetString()!));
        Assert.SequenceEqual(["saved-game-a", "saved-game-b"],
            persisted.RootElement.GetProperty("AutoGameSavedIds").EnumerateArray()
                .Select(item => item.GetString()!));
        Assert.Equal(0, persisted.RootElement.GetProperty("ExcludedGameSavedIds")
            .GetArrayLength());
        Assert.SequenceEqual(["Game A", "Game B"],
            persisted.RootElement.GetProperty("DisplayItems").EnumerateArray()
                .Select(item => item.GetProperty("DisplayName").GetString()!));
        Assert.False(state.Json!.Contains("AppId", StringComparison.Ordinal));
        Assert.False(state.Json.Contains("IconPngBase64", StringComparison.Ordinal));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(state.Json) < 64 * 1024);
    }
    var notice = Snapshot(widget, 200);
    Assert.Contains("Added 2 trusted games", Text(notice.Root, "games.toast.message").Text!);
    Assert.False(Nodes(notice.Root).Single(node => node.Id == "games.toast").IsFocusable);

    await widget.OnActionAsync(new("games.retry", "games.root"));
    Assert.Equal(2, fake.PageRequests.Count);
    Assert.Equal(1L, state.Revision);
    Assert.SequenceEqual(["game-a", "game-b"],
        widget.CuratedItems.Select(item => item.AppId));
    Assert.False(Nodes(Snapshot(widget, 201).Root).Any(node => node.Id == "games.toast"));
    await Background(widget);
}

static async Task BrokerOpaqueIdsPersist()
{
    const string savedId =
        "saved-BV00eBPpCiWbbAk0BBblMHlF3FZN0YLt4WefLk5QVLw";
    var state = new WidgetTestPrivateState();
    var fake = new FakeAppLibraryHost
    {
        PrivateState = state,
        Pages =
        {
            [0] = Page([
                App(
                    "app-01fab6bcf34b4aa5b3aa510430962e4e",
                    "Conformance Trusted Game",
                    WidgetAppLibraryKind.Game) with { SavedId = savedId },
                App(
                    "app-b9356a17164b4dc0b1a62192cf27ac7a",
                    "Conformance Library App",
                    WidgetAppLibraryKind.Application) with
                {
                    SavedId =
                        "saved-y8KENyEDtl9p_Iv1pdINsLHfCbcXObRK1yn3YmJHKCo",
                },
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    Assert.SequenceEqual([savedId], widget.CuratedItems.Select(item => item.SavedId));
    using var persisted = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.SequenceEqual([savedId], persisted.RootElement.GetProperty("SavedIds")
        .EnumerateArray().Select(item => item.GetString()!));
    Assert.Equal("Conformance Trusted", persisted.RootElement
        .GetProperty("DisplayItems")[0].GetProperty("DisplayName").GetString());
    await Background(widget);
}

static async Task AutoCuratesGamesBeyondFirstPage()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([App("application", "Application")], 32),
            [32] = Page([App("second-page-game", "Second page game",
                WidgetAppLibraryKind.Game)], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 1);

    Assert.SequenceEqual([0, 32], fake.PageRequests.Select(request => request.Offset));
    Assert.SequenceEqual(["second-page-game"],
        widget.CuratedItems.Select(item => item.AppId));
    await Background(widget);
}

static async Task RefreshAppendsGames()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([
                App("a", "Alpha", WidgetAppLibraryKind.Game),
                App("b", "Beta", WidgetAppLibraryKind.Game),
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 2);
    var beta = ActionSurfaces(Snapshot(widget, 202).Root)
        .Single(tile => TileTitle(tile) == "Beta");
    var betaElementId = beta.Id;
    await widget.OnActionAsync(new("games.launch", beta.Id));
    Assert.SequenceEqual(["b", "a"], widget.CuratedItems.Select(item => item.AppId));

    fake.Pages[0] = Page([
        App("fresh-a", "Alpha", WidgetAppLibraryKind.Game) with { SavedId = "saved-a" },
        App("fresh-b", "Beta", WidgetAppLibraryKind.Game) with { SavedId = "saved-b" },
        App("fresh-c", "Gamma", WidgetAppLibraryKind.Game) with { SavedId = "saved-c" },
    ], null);
    await widget.OnActionAsync(new("games.retry", "games.root"));

    Assert.SequenceEqual(["fresh-b", "fresh-a", "fresh-c"],
        widget.CuratedItems.Select(item => item.AppId));
    Assert.Equal("fresh-b", widget.SelectedAppId);
    Assert.Equal(betaElementId, ActionSurfaces(Snapshot(widget, 203).Root)
        .Single(tile => TileTitle(tile) == "Beta").Id);
    Assert.Equal(betaElementId, Snapshot(widget, 204).InitialFocusId);
    Assert.Contains("Added 1 trusted game",
        Text(Snapshot(widget, 205).Root, "games.toast.message").Text!);
    await Background(widget);
}

static async Task DisappearanceRetainsOrder()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([
                App("a", "Alpha", WidgetAppLibraryKind.Game),
                App("b", "Beta", WidgetAppLibraryKind.Game),
                App("c", "Gamma", WidgetAppLibraryKind.Game),
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 3);
    var beta = ActionSurfaces(Snapshot(widget, 208).Root)
        .Single(tile => TileTitle(tile) == "Beta");
    await widget.OnActionAsync(new("games.launch", beta.Id));

    fake.Pages[0] = Page([
        App("fresh-a", "Alpha", WidgetAppLibraryKind.Game) with { SavedId = "saved-a" },
        App("fresh-c", "Gamma", WidgetAppLibraryKind.Game) with { SavedId = "saved-c" },
    ], null);
    await widget.OnActionAsync(new("games.retry", "games.root"));
    Assert.SequenceEqual(["Beta", "Alpha", "Gamma"],
        widget.CuratedItems.Select(item => item.DisplayName));
    var absent = Snapshot(widget, 219);
    var staleBeta = ActionSurfaces(absent.Root).Single(tile => TileTitle(tile) == "Beta");
    Assert.True(staleBeta.IsDisabled == true);
    Assert.Equal("Checking…", Text(absent.Root, staleBeta.Id + ".state").Text);
    Assert.Equal(beta.Id, staleBeta.Id);
    Assert.Equal(beta.Id, absent.InitialFocusId);

    fake.Pages[0] = Page([
        App("fresh-a", "Alpha", WidgetAppLibraryKind.Game) with { SavedId = "saved-a" },
        App("fresh-b", "Beta", WidgetAppLibraryKind.Game) with { SavedId = "saved-b" },
        App("fresh-c", "Gamma", WidgetAppLibraryKind.Game) with { SavedId = "saved-c" },
    ], null);
    await widget.OnActionAsync(new("games.retry", "games.root"));
    Assert.SequenceEqual(["fresh-b", "fresh-a", "fresh-c"],
        widget.CuratedItems.Select(item => item.AppId));
    Assert.Equal(beta.Id, ActionSurfaces(Snapshot(widget, 209).Root)
        .Single(tile => TileTitle(tile) == "Beta").Id);
    await Background(widget);
}

static async Task ExclusionSurvivesReappearance()
{
    var state = new WidgetTestPrivateState();
    var firstHost = new FakeAppLibraryHost
    {
        PrivateState = state,
        Pages = { [0] = Page([App("game", "Game", WidgetAppLibraryKind.Game)], null) },
    };
    var first = Create(firstHost);
    await Interactive(first);
    await WaitUntil(() => first.CuratedItems.Count == 1);
    var tile = ActionSurfaces(Snapshot(first, 206).Root)
        .Single(candidate => candidate.ActionId == "games.launch");
    await first.OnActionAsync(new("games.remove", tile.Id));
    await Background(first);

    using (var persisted = System.Text.Json.JsonDocument.Parse(state.Json!))
        Assert.SequenceEqual(["saved-game"],
            persisted.RootElement.GetProperty("ExcludedGameSavedIds")
                .EnumerateArray().Select(item => item.GetString()!));

    var secondHost = new FakeAppLibraryHost
    {
        PrivateState = state,
        Pages = { [0] = Page([], null) },
    };
    var second = Create(secondHost);
    await Interactive(second);
    await WaitUntil(() => second.ViewState == GamesAppsViewState.Ready);
    Assert.Equal(0, second.CuratedItems.Count);

    secondHost.Pages[0] = Page([
        App("fresh-game", "Game", WidgetAppLibraryKind.Game) with
        {
            SavedId = "saved-game",
        },
    ], null);
    await second.OnActionAsync(new("games.retry", "games.root"));
    Assert.Equal(0, second.CuratedItems.Count);
    Assert.False(Nodes(Snapshot(second, 207).Root).Any(node => node.Id == "games.toast"));
    await AddFromCatalog(second, "Game");
    await BackToLibrary(second);
    Assert.SequenceEqual(["fresh-game"], second.CuratedItems.Select(item => item.AppId));
    using (var persisted = System.Text.Json.JsonDocument.Parse(state.Json!))
        Assert.Equal(0, persisted.RootElement.GetProperty("ExcludedGameSavedIds")
            .GetArrayLength());
    await Background(second);
}

static async Task IdentityReplacementIsNewGame()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([
                App("old", "Same title", WidgetAppLibraryKind.Game),
                App("application", "Utility"),
                App("unknown", "Mystery", WidgetAppLibraryKind.Unknown),
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 1);
    var staleElementId = ActionSurfaces(Snapshot(widget, 210).Root)
        .Single(tile => TileTitle(tile) == "Same title").Id;
    Assert.SequenceEqual(["old"], widget.CuratedItems.Select(item => item.AppId));

    fake.Pages[0] = Page([
        App("replacement", "Same title", WidgetAppLibraryKind.Game),
        App("application", "Utility"),
        App("unknown", "Mystery", WidgetAppLibraryKind.Unknown),
    ], null);
    await widget.OnActionAsync(new("games.retry", "games.root"));
    Assert.SequenceEqual(["Same title", "Same title"],
        widget.CuratedItems.Select(item => item.DisplayName));
    Assert.False(widget.CuratedItems.Any(item => item.AppId is "application" or "unknown"));
    var replacementSnapshot = Snapshot(widget, 211);
    await widget.OnActionAsync(new("games.launch", staleElementId));
    Assert.Equal(0, fake.LaunchedIds.Count);
    var replacementElement = ActionSurfaces(replacementSnapshot.Root)
        .Single(tile => TileTitle(tile) == "Same title" && tile.Id != staleElementId);
    await widget.OnActionAsync(new("games.launch", replacementElement.Id));
    Assert.SequenceEqual(["replacement"], fake.LaunchedIds);

    await AddFromCatalog(widget, "Utility");
    await BackToLibrary(widget);
    Assert.SequenceEqual(["replacement", "Same title", "application"],
        widget.CuratedItems.Select(item => item.AppId.StartsWith("pending.", StringComparison.Ordinal)
            ? item.DisplayName
            : item.AppId));
    await Background(widget);
}

static async Task ReclassificationPreservesOptIn()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([
                App("game", "Game", WidgetAppLibraryKind.Game),
                App("application", "Application"),
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 1);
    await AddFromCatalog(widget, "Application");
    await BackToLibrary(widget);

    fake.Pages[0] = Page([
        App("game-now-app", "Game", WidgetAppLibraryKind.Application) with
        {
            SavedId = "saved-game",
        },
        App("application-now-unknown", "Application", WidgetAppLibraryKind.Unknown) with
        {
            SavedId = "saved-application",
        },
    ], null);
    await widget.OnActionAsync(new("games.retry", "games.root"));
    Assert.SequenceEqual(["application-now-unknown"],
        widget.CuratedItems.Select(item => item.AppId));

    await AddFromCatalog(widget, "Game");
    await BackToLibrary(widget);
    Assert.SequenceEqual(["game-now-app", "application-now-unknown"],
        widget.CuratedItems.Select(item => item.AppId));

    fake.Pages[0] = Page([
        App("game-again", "Game", WidgetAppLibraryKind.Unknown) with { SavedId = "saved-game" },
        App("application-now-unknown", "Application", WidgetAppLibraryKind.Unknown) with
        {
            SavedId = "saved-application",
        },
    ], null);
    await widget.OnActionAsync(new("games.retry", "games.root"));
    Assert.SequenceEqual(["game-again", "application-now-unknown"],
        widget.CuratedItems.Select(item => item.AppId));
    await Background(widget);
}

static async Task ResolvedReclassificationIsAuthoritative()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([App("game", "Game", WidgetAppLibraryKind.Game)], null),
        },
        ResolveHandler = (request, _) => ValueTask.FromResult(
            new ResolveSavedWidgetAppLibraryItemsResponse([
                App("game-now-app", "Game", WidgetAppLibraryKind.Application) with
                {
                    SavedId = request.SavedIds.Single(),
                },
            ])),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    Assert.Equal(0, widget.CuratedItems.Count);
    Assert.Equal(1L, fake.PrivateState.Revision);
    using (var persisted = System.Text.Json.JsonDocument.Parse(fake.PrivateState.Json!))
        Assert.Equal(0, persisted.RootElement.GetProperty("SavedIds").GetArrayLength());
    Assert.False(Nodes(Snapshot(widget, 220).Root).Any(node => node.Id == "games.toast"));
    await Background(widget);
}

static async Task ConcurrentExclusionWins()
{
    var state = new WidgetTestPrivateState();
    var injected = false;
    var fake = new FakeAppLibraryHost { PrivateState = state };
    fake.ReadHandler = (_, _) =>
    {
        if (!injected)
        {
            injected = true;
            state.SimulateExternalWriteJson(
                "{\"Version\":3,\"SavedIds\":[],\"SelectedSavedId\":null," +
                "\"AutoGameSavedIds\":[],\"ExcludedGameSavedIds\":[\"saved-game\"]," +
                "\"DisplayItems\":[]}");
        }
        return ValueTask.FromResult(Page([
            App("game", "Game", WidgetAppLibraryKind.Game),
        ], null));
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    Assert.Equal(0, widget.CuratedItems.Count);
    Assert.False(Nodes(Snapshot(widget, 212).Root).Any(node => node.Id == "games.toast"));
    using var persisted = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.SequenceEqual(["saved-game"],
        persisted.RootElement.GetProperty("ExcludedGameSavedIds")
            .EnumerateArray().Select(item => item.GetString()!));
    Assert.Equal(0, persisted.RootElement.GetProperty("SavedIds").GetArrayLength());
    await Background(widget);
}

static async Task ConcurrentDisplayRemovalWins()
{
    var state = ProjectedState(
        selectedSavedId: "saved-game",
        ("saved-game", "Game", WidgetAppLibraryKind.Game));
    var injected = false;
    var fake = new FakeAppLibraryHost
    {
        PrivateState = state,
        ReadHandler = (_, _) =>
        {
            if (!injected)
            {
                injected = true;
                state.SimulateExternalWriteJson(
                    "{\"Version\":3,\"SavedIds\":[\"saved-game\"]," +
                    "\"SelectedSavedId\":null,\"AutoGameSavedIds\":[]," +
                    "\"ExcludedGameSavedIds\":[],\"DisplayItems\":[]}");
            }
            return ValueTask.FromResult(Page([
                App("fresh-game", "Game") with { SavedId = "saved-game" },
            ], null));
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Any(item => item.AppId == "fresh-game"));

    using var persisted = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.SequenceEqual(["saved-game"], persisted.RootElement
        .GetProperty("SavedIds").EnumerateArray().Select(item => item.GetString()!));
    Assert.Equal(0, persisted.RootElement.GetProperty("DisplayItems").GetArrayLength());
    await Background(widget);
}

static async Task LegacySchemasResetAtomically()
{
    foreach (var (version, invalidCurrent) in new[]
             {
                 (Version: 1, InvalidCurrent: false),
                 (Version: 2, InvalidCurrent: false),
                 (Version: 99, InvalidCurrent: false),
                 (Version: 3, InvalidCurrent: true),
             })
    {
        var state = new WidgetTestPrivateState(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                Version = version,
                SavedIds = invalidCurrent
                    ? new[] { "saved-stale", "saved-stale" }
                    : ["saved-stale"],
                SelectedSavedId = "saved-stale",
                AutoGameSavedIds = new[] { "saved-stale" },
                ExcludedGameSavedIds = new[] { "saved-fresh" },
                DisplayItems = new[]
                {
                    new
                    {
                        SavedId = "saved-stale",
                        DisplayName = "Legacy stale app",
                        Kind = WidgetAppLibraryKind.Game,
                    },
                },
            }), 1);
        var fake = new FakeAppLibraryHost
        {
            PrivateState = state,
            Pages =
            {
                [0] = Page([App($"fresh-{version}", "Fresh game",
                    WidgetAppLibraryKind.Game) with { SavedId = "saved-fresh" }], null),
            },
        };
        var widget = Create(fake);
        await Interactive(widget);
        await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                              state.Revision == 2);

        Assert.SequenceEqual([$"fresh-{version}"],
            widget.CuratedItems.Select(item => item.AppId));
        var snapshot = Snapshot(widget, 230 + version);
        Assert.False(Nodes(snapshot.Root).Any(node =>
            string.Equals(node.Text, "Legacy stale app", StringComparison.Ordinal)));
        await widget.OnActionAsync(new("games.launch", LibraryElementId("saved-stale")));
        Assert.Equal(0, fake.LaunchedIds.Count);
        var fresh = ActionSurfaces(snapshot.Root).Single(tile =>
            tile.ActionId == "games.launch" && TileTitle(tile) == "Fresh game");
        await widget.OnActionAsync(new("games.launch", fresh.Id));
        Assert.SequenceEqual([$"fresh-{version}"], fake.LaunchedIds);

        using var persisted = System.Text.Json.JsonDocument.Parse(state.Json!);
        Assert.Equal(3, persisted.RootElement.GetProperty("Version").GetInt32());
        Assert.SequenceEqual(["saved-fresh"], persisted.RootElement
            .GetProperty("SavedIds").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal(0, persisted.RootElement.GetProperty("ExcludedGameSavedIds")
            .GetArrayLength());
        Assert.False(state.Json!.Contains("saved-stale", StringComparison.Ordinal));
        Assert.False(state.Json.Contains("Legacy stale app", StringComparison.Ordinal));
        await Background(widget);
    }
}

static async Task FullExclusionSetRefusesRemoval()
{
    var exclusions = Enumerable.Range(0, 128)
        .Select(index => $"saved-excluded-{index}").ToArray();
    var state = new WidgetTestPrivateState(System.Text.Json.JsonSerializer.Serialize(new
    {
        Version = 3,
        SavedIds = Array.Empty<string>(),
        SelectedSavedId = (string?)null,
        AutoGameSavedIds = Array.Empty<string>(),
        ExcludedGameSavedIds = exclusions,
        DisplayItems = Array.Empty<object>(),
    }), 1);
    var fake = new FakeAppLibraryHost
    {
        PrivateState = state,
        Pages = { [0] = Page([App("new-game", "New game", WidgetAppLibraryKind.Game)], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 1);
    var revision = state.Revision;
    var tile = ActionSurfaces(Snapshot(widget, 213).Root)
        .Single(candidate => candidate.ActionId == "games.launch");
    await widget.OnActionAsync(new("games.remove", tile.Id));

    Assert.Equal(revision, state.Revision);
    Assert.SequenceEqual(["new-game"], widget.CuratedItems.Select(item => item.AppId));
    var snapshot = Snapshot(widget, 214);
    Assert.Contains("previously excluded", Text(snapshot.Root, "games.toast.message").Text!);
    Assert.True(Nodes(snapshot.Root).Single(node => node.Id == "games.toast")
        .StyleClasses.Contains("gbar-toast--warning", StringComparer.Ordinal));
    await Background(widget);
}

static Task ProjectedStateIsBounded()
{
    var savedIds = Enumerable.Range(0, 64)
        .Select(index => $"s{index:D2}" + new string('x', 125))
        .ToArray();
    var excludedIds = Enumerable.Range(0, 128)
        .Select(index => $"e{index:D3}" + new string('y', 124))
        .ToArray();
    var worstEscapedName = string.Concat(Enumerable.Repeat("😀", 20));
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        Version = 3,
        SavedIds = savedIds,
        SelectedSavedId = savedIds[0],
        AutoGameSavedIds = savedIds,
        ExcludedGameSavedIds = excludedIds,
        DisplayItems = savedIds.Select(savedId => new
        {
            SavedId = savedId,
            DisplayName = worstEscapedName,
            Kind = WidgetAppLibraryKind.Game,
        }).ToArray(),
    });
    Assert.True(
        System.Text.Encoding.UTF8.GetByteCount(json) <=
        WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes,
        "The worst-case schema-v3 display projection exceeded private-state bounds.");
    return Task.CompletedTask;
}

static async Task EmptyCatalogReturnsToLibrary()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await widget.OnActionAsync(new("games.open-catalog", "games.open-catalog"));
    await WaitUntil(() => widget.Page == GamesAppsPage.Library &&
        widget.ViewState == GamesAppsViewState.Ready && fake.PageRequests.Count == 2);

    var snapshot = Snapshot(widget, 2);
    Assert.True(Buttons(snapshot.Root).Any(button => button.ActionId == "games.open-catalog"));
    Assert.False(Nodes(snapshot.Root).Any(node => node.Id == "games.catalog"));
    Assert.Contains("No additional", Text(snapshot.Root, "games.status").Text!);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task SharedStateSurfaces()
{
    var loadingPage = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var loadingHost = new FakeAppLibraryHost
    {
        ReadHandler = (_, cancellationToken) => new ValueTask<WidgetAppLibraryPage>(
            loadingPage.Task.WaitAsync(cancellationToken)),
    };
    var loadingWidget = Create(loadingHost);
    var initial = Snapshot(loadingWidget, 230);
    Assert.True(Nodes(initial.Root).Single(node => node.Id == "games.state")
        .StyleClasses.Contains("gbar-card", StringComparer.Ordinal));
    Assert.Equal<string?>(null, initial.InitialFocusId);

    await Interactive(loadingWidget);
    await WaitUntil(() => loadingWidget.ViewState == GamesAppsViewState.Loading);
    var loading = Snapshot(loadingWidget, 231);
    Assert.True(Nodes(loading.Root).Any(node =>
        node.Kind == ViewNodeKind.LoadingIndicator && node.Id == "games.state.loading"));
    Assert.False(Nodes(loading.Root).Any(node => node.IsFocusable));
    loadingPage.TrySetResult(Page([], null));
    await WaitUntil(() => loadingWidget.ViewState == GamesAppsViewState.Ready);
    var empty = Snapshot(loadingWidget, 232);
    var emptySurface = Nodes(empty.Root).Single(node => node.Id == "games.state");
    Assert.True(emptySurface.StyleClasses.Contains("gbar-empty-state", StringComparer.Ordinal));
    var add = Buttons(emptySurface).Single(button => button.ActionId == "games.open-catalog");
    Assert.Equal(add.Id, empty.InitialFocusId);
    Assert.Equal("Add applications", add.Text);
    Assert.Valid(empty);
    await Background(loadingWidget);

    var failureHost = new FakeAppLibraryHost
    {
        ReadException = new WidgetCapabilityException(
            "platform_unavailable", "private provider detail"),
    };
    var failureWidget = Create(failureHost);
    await Interactive(failureWidget);
    await WaitUntil(() => failureWidget.ViewState == GamesAppsViewState.ServiceUnavailable);
    var failure = Snapshot(failureWidget, 233);
    var alert = Nodes(failure.Root).Single(node => node.Id == "games.state");
    Assert.True(alert.StyleClasses.Contains("gbar-alert", StringComparer.Ordinal));
    var retry = Buttons(alert).Single(button => button.ActionId == "games.retry");
    Assert.Equal(retry.Id, failure.InitialFocusId);
    Assert.False(System.Text.Json.JsonSerializer.Serialize(failure)
        .Contains("private provider detail", StringComparison.Ordinal));
    Assert.Valid(failure);
    await Background(failureWidget);
}

static async Task RendersControllerStrip()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([
                App("private-one", "Alpha"),
                App("private-two", "Beta"),
                App("private-three", "Gamma"),
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    var snapshot = Snapshot(widget, 3);
    Assert.Equal(WidgetSurfaceMode.Standard, snapshot.Surface!.Mode);
    Assert.Equal(420d, snapshot.Surface.MinimumWidth);
    Assert.Equal(280d, snapshot.Surface.PreferredHeight);
    Assert.Equal(250d, snapshot.Surface.MinimumHeight);
    Assert.True(snapshot.Surface.PreferredWidth > snapshot.Surface.MinimumWidth);
    Assert.True(snapshot.Surface.PreferredHeight > snapshot.Surface.MinimumHeight);
    Assert.Equal("games-apps", snapshot.ActiveInputScopeId);
    Assert.True(Buttons(snapshot.Root).Any(button => button.ActionId == "games.open-catalog"));
    Assert.False(ActionSurfaces(snapshot.Root).Any(tile => tile.ActionId == "games.launch"));

    await OpenCatalog(widget);
    snapshot = Snapshot(widget, 4);
    Assert.Equal(600d, snapshot.Surface!.PreferredHeight);
    Assert.Equal(320d, snapshot.Surface.MinimumHeight);
    Assert.Equal("games.catalog", snapshot.ActiveInputScopeId);
    var catalogScope = Nodes(snapshot.Root).Single(node => node.Id == "games.catalog");
    Assert.True(catalogScope.Shortcuts.Any(shortcut =>
        shortcut.Button == ControllerButton.B && shortcut.ActionId == "back"));
    var scroll = Nodes(snapshot.Root).Single(node => node.Id == "games.library.scroll");
    Assert.Equal(ViewNodeKind.Scroll, scroll.Kind);
    Assert.Equal(ScrollAxis.Vertical, scroll.ScrollAxis);
    var tiles = ActionSurfaces(scroll)
        .Where(tile => tile.ActionId == "games.toggle-curation").ToArray();
    Assert.Equal(3, tiles.Length);
    Assert.Equal(tiles[0].Id, tiles[0].Focus!.Left);
    Assert.Equal(tiles[0].Id, tiles[0].Focus!.Right);
    Assert.Equal(tiles[1].Id, tiles[0].Focus!.Down);
    Assert.Equal(tiles[0].Id, snapshot.InitialFocusId);
    var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
    Assert.False(json.Contains("private-one", StringComparison.Ordinal));
    Assert.False(json.Contains(".lnk", StringComparison.OrdinalIgnoreCase));
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task OneAppUsesCompactTile()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await AddFromCatalog(widget, "Alpha");
    await BackToLibrary(widget);

    var snapshot = Snapshot(widget, 62);
    Assert.Equal(600d, snapshot.Surface!.PreferredHeight);
    Assert.True(snapshot.Surface.PreferredHeight >= 430d + (2d * 78d),
        "The preferred Library surface did not add two normal row pitches.");
    Assert.Equal(300d, snapshot.Surface.MinimumHeight);
    var launch = ActionSurfaces(snapshot.Root).Single(tile => tile.ActionId == "games.launch");
    Assert.Equal(ViewNodeKind.ActionSurface, launch.Kind);
    var artwork = Nodes(launch).Single(node => node.Id == launch.Id + ".artwork");
    Assert.Equal(WidgetGlyph.Play, artwork.Glyph);
    Assert.True(artwork.ImageSource is null);
    Assert.Equal(launch.Id, launch.Focus!.Left);
    Assert.Equal(launch.Id, launch.Focus.Right);
    Assert.Equal("games.open-catalog", launch.Focus.Down);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task ResolvedIconRenders()
{
    const string handle = "library.art.0123456789abcdef0123456789abcdef";
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha", artworkHandle: handle)], null) },
        PrivateState = SavedState("saved-opaque-a"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    var snapshot = Snapshot(widget, 64);
    var launch = ActionSurfaces(snapshot.Root).Single(tile => tile.ActionId == "games.launch");
    Assert.Equal(ViewNodeKind.ActionSurface, launch.Kind);
    var artwork = Nodes(launch).Single(node => node.Id == launch.Id + ".artwork");
    Assert.Equal(handle, artwork.ArtworkHandle);
    Assert.True(artwork.ImageSource is null);
    Assert.Equal(ImageFit.Contain, artwork.ImageFit);
    Assert.True(artwork.Glyph is null);
    Assert.Equal(ProtocolConstants.CursorCollectionVersion, snapshot.ProtocolVersion);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task ManyAppsUseCompactRail()
{
    var apps = Enumerable.Range(0, 8)
        .Select(index => App($"opaque-{index}", $"Application {index}"))
        .ToArray();
    var fake = new FakeAppLibraryHost { Pages = { [0] = Page(apps, null) } };
    var widget = Create(fake);
    await Interactive(widget);
    await OpenCatalog(widget);
    foreach (var app in apps)
        await AddFromOpenCatalog(widget, app.DisplayName);
    await BackToLibrary(widget);

    var snapshot = Snapshot(widget, 63);
    Assert.Equal(600d, snapshot.Surface!.PreferredHeight);
    Assert.Equal(300d, snapshot.Surface.MinimumHeight);
    var scroll = Nodes(snapshot.Root).Single(node => node.Id == "games.library.scroll");
    var launches = ActionSurfaces(scroll).Where(tile => tile.ActionId == "games.launch").ToArray();
    Assert.Equal(8, launches.Length);
    Assert.True(launches.All(launch => Nodes(launch).Single(node =>
        node.Id == launch.Id + ".artwork").Glyph == WidgetGlyph.Play));
    for (var index = 1; index < launches.Length; index++)
        Assert.Equal(launches[index - 1].Id, launches[index].Focus!.Up);
    Assert.Equal("games.open-catalog", launches[^1].Focus!.Down);
    Assert.Equal(ScrollAxis.Vertical, scroll.ScrollAxis);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task MaximumLongLibraryIsBounded()
{
    var longPrefix = new string('L', 130);
    var items = Enumerable.Range(0, 64)
        .Select(index => App(
            $"game-{index:D2}",
            $"{longPrefix} {index:D2} launch",
            WidgetAppLibraryKind.Game))
        .ToArray();
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page(items[..32], 32),
            [32] = Page(items[32..], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 64);

    var snapshot = Snapshot(widget, 239);
    var launches = ActionSurfaces(snapshot.Root)
        .Where(tile => tile.ActionId == "games.launch").ToArray();
    Assert.Equal(64, launches.Length);
    Assert.True(launches.All(tile => TileTitle(tile)!.Length == 120));
    Assert.True(Nodes(snapshot.Root).Count() < ProtocolConstants.MaximumNodeCount);
    Assert.Equal(launches[0].Id, snapshot.InitialFocusId);
    Assert.Equal("games.open-catalog", launches[^1].Focus!.Down);
    Assert.True(Buttons(snapshot.Root).Any(button =>
        button.ActionId == "games.open-catalog"));
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task ToastFeedbackIsLifecycleBound()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await AddFromCatalog(widget, "Alpha");

    var feedback = Snapshot(widget, 66);
    var toast = Nodes(feedback.Root).Single(node => node.Id == "games.toast");
    Assert.Equal(ViewNodeKind.Row, toast.Kind);
    Assert.False(Nodes(toast).Any(node => node.IsFocusable));
    var alpha = ActionSurfaces(feedback.Root).Single(tile => TileTitle(tile) == "Alpha");
    Assert.Equal(alpha.Id, feedback.InitialFocusId);
    Assert.Contains("Added Alpha", Text(feedback.Root, "games.toast.message").Text!);

    await Background(widget);
    Assert.False(Nodes(Snapshot(widget, 67).Root).Any(node => node.Id == "games.toast"));
}

static async Task CuratesLibrary()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha"), App("opaque-b", "Beta")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    Assert.Equal(0, widget.CuratedItems.Count);

    await OpenCatalog(widget);
    var catalog = Snapshot(widget, 40);
    var beta = ActionSurfaces(catalog.Root).Single(tile => TileTitle(tile) == "Beta");
    await widget.OnActionAsync(new("games.toggle-curation", beta.Id));
    Assert.SequenceEqual(["opaque-b"], widget.CuratedItems.Select(item => item.AppId));
    Assert.True(ActionSurfaces(Snapshot(widget, 41).Root)
        .Single(tile => TileTitle(tile) == "Beta")
        .IsSelected == true);

    await widget.OnActionAsync(new("back", "games.catalog"));
    Assert.Equal(GamesAppsPage.Library, widget.Page);
    var library = Snapshot(widget, 42);
    Assert.Equal("games-apps", library.ActiveInputScopeId);
    Assert.False(Nodes(library.Root).Single(node => node.Id == "games.root").Shortcuts
        .Any(shortcut => shortcut.Button == ControllerButton.B));
    var savedBeta = ActionSurfaces(library.Root).Single(tile => TileTitle(tile) == "Beta");
    Assert.Equal("games.launch", savedBeta.ActionId);
    Assert.True(savedBeta.Shortcuts.Any(shortcut =>
        shortcut.Button == ControllerButton.X && shortcut.ActionId == "games.remove"));

    await widget.OnActionAsync(new("games.toggle-curation", beta.Id));
    Assert.Equal(1, widget.CuratedItems.Count);

    await widget.OnActionAsync(new("games.remove", savedBeta.Id));
    Assert.Equal(0, widget.CuratedItems.Count);
    var removed = Snapshot(widget, 43);
    Assert.True(Buttons(removed.Root)
        .Any(button => button.ActionId == "games.open-catalog"));
    Assert.Contains("Removed Beta", Text(removed.Root, "games.toast.message").Text!);
    await Background(widget);
}

static async Task CatalogRemovalPreservesLibraryContinuity()
{
    var sharedState = new WidgetTestPrivateState();
    var catalog = new[]
    {
        App("game", "Trusted Game", WidgetAppLibraryKind.Game),
        App("alpha", "Alpha", WidgetAppLibraryKind.Application),
        App("beta", "Beta", WidgetAppLibraryKind.Application),
    };
    var firstHost = new FakeAppLibraryHost
    {
        PrivateState = sharedState,
        Pages = { [0] = Page(catalog, null) },
    };
    var first = Create(firstHost);
    await Interactive(first);
    await WaitUntil(() => first.CuratedItems.Count == 1);
    await OpenCatalog(first);
    await AddFromOpenCatalog(first, "Alpha");
    await AddFromOpenCatalog(first, "Beta");
    var invalidations = 0;
    first.Invalidated += (_, _) => invalidations++;
    var game = ActionSurfaces(Snapshot(first, 340).Root).Single(tile =>
        tile.ActionId == "games.toggle-curation" && TileTitle(tile) == "Trusted Game");
    await first.OnActionAsync(new("games.toggle-curation", game.Id));
    await BackToLibrary(first);

    var afterRemoval = AssertReadyLibrary(
        first, 341, "Alpha", "Beta");
    Assert.True(invalidations >= 2,
        "The committed catalog mutation and Back transition did not invalidate.");
    Assert.True(ActionSurfaces(afterRemoval.Root)
        .Where(tile => tile.ActionId == "games.launch")
        .All(tile => tile.IsDisabled != true));
    using (var persisted = System.Text.Json.JsonDocument.Parse(sharedState.Json!))
    {
        Assert.SequenceEqual(["saved-alpha", "saved-beta"], persisted.RootElement
            .GetProperty("SavedIds").EnumerateArray().Select(item => item.GetString()!));
        Assert.SequenceEqual(["saved-game"], persisted.RootElement
            .GetProperty("ExcludedGameSavedIds").EnumerateArray()
            .Select(item => item.GetString()!));
        Assert.SequenceEqual(["Alpha", "Beta"], persisted.RootElement
            .GetProperty("DisplayItems").EnumerateArray()
            .Select(item => item.GetProperty("DisplayName").GetString()!));
    }
    await Background(first);

    var delayed = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var freshCatalog = catalog.Select(item => item with
    {
        AppId = "fresh-" + item.AppId,
    }).ToArray();
    var delayedHost = new FakeAppLibraryHost
    {
        PrivateState = sharedState,
        Pages = { [0] = Page(freshCatalog, null) },
        ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
            delayed.Task.WaitAsync(token)),
    };
    var delayedWorker = Create(delayedHost);
    await Interactive(delayedWorker);
    await WaitUntil(() => delayedWorker.CuratedItems.Count == 2);
    var warm = AssertReadyLibrary(delayedWorker, 342, "Alpha", "Beta");
    Assert.True(ActionSurfaces(warm.Root)
        .Where(tile => tile.ActionId == "games.launch")
        .All(tile => tile.IsDisabled == true));
    delayed.SetResult(Page(freshCatalog, null));
    await WaitUntil(() => delayedWorker.CuratedItems.All(item =>
        item.AppId.StartsWith("fresh-", StringComparison.Ordinal)));
    var resolved = AssertReadyLibrary(delayedWorker, 343, "Alpha", "Beta");
    Assert.True(ActionSurfaces(resolved.Root)
        .Where(tile => tile.ActionId == "games.launch")
        .All(tile => tile.IsDisabled != true));
    await Background(delayedWorker);

    var failedHost = new FakeAppLibraryHost
    {
        PrivateState = sharedState,
        ReadException = new WidgetCapabilityException(
            "platform_unavailable", "private catalog failure"),
        ResolveException = new WidgetCapabilityException(
            "platform_unavailable", "private resolution failure"),
    };
    var failedWorker = Create(failedHost);
    await Interactive(failedWorker);
    await WaitUntil(() => Text(Snapshot(failedWorker, 344).Root, "games.status").Text!
        .Contains("refresh unavailable", StringComparison.Ordinal));
    var failed = AssertReadyLibrary(failedWorker, 345, "Alpha", "Beta");
    Assert.True(ActionSurfaces(failed.Root)
        .Where(tile => tile.ActionId == "games.launch")
        .All(tile => tile.IsDisabled == true));
    Assert.False(System.Text.Json.JsonSerializer.Serialize(failed)
        .Contains("private", StringComparison.Ordinal));
    await Background(failedWorker);

    var conflictHost = new FakeAppLibraryHost
    {
        PrivateState = sharedState,
        Pages = { [0] = Page(freshCatalog, null) },
    };
    var conflictWorker = Create(conflictHost);
    await Interactive(conflictWorker);
    await WaitUntil(() => conflictWorker.CuratedItems.Count == 2 &&
                          conflictWorker.CuratedItems.All(item =>
                              item.AppId.StartsWith("fresh-", StringComparison.Ordinal)));
    await OpenCatalog(conflictWorker);
    sharedState.SimulateExternalWriteJson(sharedState.Json!);
    var alpha = ActionSurfaces(Snapshot(conflictWorker, 346).Root).Single(tile =>
        tile.ActionId == "games.toggle-curation" && TileTitle(tile) == "Alpha");
    await conflictWorker.OnActionAsync(new("games.toggle-curation", alpha.Id));
    await BackToLibrary(conflictWorker);
    var afterConflict = AssertReadyLibrary(conflictWorker, 347, "Beta");
    var beta = ActionSurfaces(afterConflict.Root).Single(tile =>
        tile.ActionId == "games.launch" && TileTitle(tile) == "Beta");
    Assert.True(beta.IsDisabled != true);
    await conflictWorker.OnActionAsync(new("games.launch", beta.Id));
    Assert.SequenceEqual(["fresh-beta"], conflictHost.LaunchedIds);
    await Background(conflictWorker);
}

static async Task FailedRemovalRollsBack()
{
    var initial = ProjectedState(
        selectedSavedId: "saved-beta",
        ("saved-alpha", "Alpha", WidgetAppLibraryKind.Application),
        ("saved-beta", "Beta", WidgetAppLibraryKind.Application));
    var state = new WidgetTestPrivateState(initial.Json, long.MaxValue);
    var fake = new FakeAppLibraryHost
    {
        PrivateState = state,
        Pages =
        {
            [0] = Page([
                App("alpha", "Alpha") with { SavedId = "saved-alpha" },
                App("beta", "Beta") with { SavedId = "saved-beta" },
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 2 &&
                          widget.CuratedItems.All(item =>
                              !item.AppId.StartsWith("pending.", StringComparison.Ordinal)));
    var beforeJson = state.Json;
    await OpenCatalog(widget);
    var alpha = ActionSurfaces(Snapshot(widget, 348).Root).Single(tile =>
        tile.ActionId == "games.toggle-curation" && TileTitle(tile) == "Alpha");
    await widget.OnActionAsync(new("games.toggle-curation", alpha.Id));
    await BackToLibrary(widget);

    var rolledBack = AssertReadyLibrary(widget, 349, "Alpha", "Beta");
    Assert.Equal(beforeJson, state.Json);
    Assert.True(ActionSurfaces(rolledBack.Root)
        .Where(tile => tile.ActionId == "games.launch")
        .All(tile => tile.IsDisabled != true));
    Assert.Contains("durable library could not be updated",
        Text(rolledBack.Root, "games.toast.message").Text!);
    await Background(widget);
}

static async Task RemovalSelectsNearestRow()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([
                App("opaque-a", "Alpha"),
                App("opaque-b", "Beta"),
                App("opaque-c", "Gamma"),
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await OpenCatalog(widget);
    await AddFromOpenCatalog(widget, "Alpha");
    await AddFromOpenCatalog(widget, "Beta");
    await AddFromOpenCatalog(widget, "Gamma");
    await BackToLibrary(widget);

    var library = Snapshot(widget, 234);
    Assert.Equal(ActionSurfaces(library.Root).Single(tile =>
        TileTitle(tile) == "Gamma").Id, library.InitialFocusId);
    var beta = ActionSurfaces(library.Root)
        .Single(tile => TileTitle(tile) == "Beta");
    await widget.OnActionAsync(new("games.remove", beta.Id));
    var afterBeta = Snapshot(widget, 235);
    Assert.Equal(ActionSurfaces(afterBeta.Root).Single(tile =>
        TileTitle(tile) == "Gamma").Id, afterBeta.InitialFocusId);

    var gamma = ActionSurfaces(afterBeta.Root).Single(tile => TileTitle(tile) == "Gamma");
    await widget.OnActionAsync(new("games.remove", gamma.Id));
    var afterGamma = Snapshot(widget, 236);
    Assert.Equal(ActionSurfaces(afterGamma.Root).Single(tile =>
        TileTitle(tile) == "Alpha").Id, afterGamma.InitialFocusId);
    Assert.Valid(afterGamma);
    await Background(widget);
}

static async Task LaunchesSelectedApp()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha"), App("opaque-b", "Beta")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await AddFromCatalog(widget, "Beta");
    await BackToLibrary(widget);
    var snapshot = Snapshot(widget, 44);
    var beta = ActionSurfaces(snapshot.Root).Single(tile => TileTitle(tile) == "Beta");
    await widget.OnActionAsync(new("games.launch", beta.Id));
    Assert.SequenceEqual(["opaque-b"], fake.LaunchedIds);
    Assert.Equal("opaque-b", widget.SelectedAppId);
    var opened = Snapshot(widget, 5);
    Assert.Contains("Opened Beta", Text(opened.Root, "games.status").Text!);
    Assert.Contains("Opened Beta", Text(opened.Root, "games.toast.message").Text!);
    await Background(widget);
}

static async Task SuccessfulLaunchOrdersRecentFirst()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha"), App("opaque-b", "Beta")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await OpenCatalog(widget);
    await AddFromOpenCatalog(widget, "Alpha");
    await AddFromOpenCatalog(widget, "Beta");
    await BackToLibrary(widget);
    Assert.SequenceEqual(["opaque-a", "opaque-b"],
        widget.CuratedItems.Select(item => item.AppId));

    var beta = ActionSurfaces(Snapshot(widget, 45).Root)
        .Single(tile => TileTitle(tile) == "Beta");
    await widget.OnActionAsync(new("games.launch", beta.Id));
    Assert.SequenceEqual(["opaque-b", "opaque-a"],
        widget.CuratedItems.Select(item => item.AppId));
    Assert.Equal(beta.Id, Snapshot(widget, 46).InitialFocusId);
    await Background(widget);
}

static async Task FailedLaunchKeepsOrder()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha"), App("opaque-b", "Beta")], null) },
        LaunchException = new WidgetCapabilityException("platform_unavailable", "private failure"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await OpenCatalog(widget);
    await AddFromOpenCatalog(widget, "Alpha");
    await AddFromOpenCatalog(widget, "Beta");
    await BackToLibrary(widget);
    var beta = ActionSurfaces(Snapshot(widget, 47).Root)
        .Single(tile => TileTitle(tile) == "Beta");
    await widget.OnActionAsync(new("games.launch", beta.Id));

    Assert.SequenceEqual(["opaque-a", "opaque-b"],
        widget.CuratedItems.Select(item => item.AppId));
    var failed = Snapshot(widget, 48);
    Assert.Equal(beta.Id, failed.InitialFocusId);
    Assert.Contains("App library unavailable", Text(failed.Root, "games.status").Text!);
    Assert.True(Nodes(failed.Root).Single(node => node.Id == "games.toast")
        .StyleClasses.Contains("gbar-toast--danger", StringComparer.Ordinal));
    await Background(widget);
}

static async Task CurationSurvivesReactivation()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await AddFromCatalog(widget, "Alpha");
    await BackToLibrary(widget);
    await Background(widget);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Any(item => item.AppId == "opaque-a"));
    Assert.SequenceEqual(["opaque-a"], widget.CuratedItems.Select(item => item.AppId));
    Assert.True(ActionSurfaces(Snapshot(widget, 49).Root)
        .Any(tile => tile.ActionId == "games.launch" && TileTitle(tile) == "Alpha"));
    await Background(widget);
}

static async Task ReactivationReusesCachedLibrary()
{
    var reactivation = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await AddFromCatalog(widget, "Alpha");
    await BackToLibrary(widget);
    var focusBefore = Snapshot(widget, 51).InitialFocusId;
    var resolvesBefore = fake.ResolveRequests.Count;
    fake.ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
        reactivation.Task.WaitAsync(token));
    await Background(widget);

    await Interactive(widget);
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    var cached = Snapshot(widget, 52);
    Assert.Equal(focusBefore, cached.InitialFocusId);
    var checking = ActionSurfaces(cached.Root).Single(tile =>
        tile.ActionId == "games.launch" && TileTitle(tile) == "Alpha");
    Assert.True(checking.IsDisabled == true);
    Assert.Equal("Checking…", Text(cached.Root, checking.Id + ".state").Text);
    await widget.OnActionAsync(new("games.launch", checking.Id));
    Assert.Equal(0, fake.LaunchedIds.Count);
    await Task.Delay(GamesAppsWidget.ColdLoadingDelayMilliseconds + 75);
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    var freshPage = Page([App("fresh-a", "Alpha") with
        { SavedId = "saved-opaque-a" }], null);
    fake.Pages[0] = freshPage;
    reactivation.SetResult(freshPage);
    await WaitUntil(() => fake.ResolveRequests.Count == resolvesBefore + 1);
    await WaitUntil(() => widget.CuratedItems.Any(item => item.AppId == "fresh-a"));
    Assert.Equal(focusBefore, Snapshot(widget, 53).InitialFocusId);
    await Background(widget);
}

static async Task CatalogDrainsBackgroundReconciliation()
{
    var backgroundStarted = NewSignal();
    var backgroundCanceled = NewSignal();
    var requestNumber = 0;
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([App("game", "Game", WidgetAppLibraryKind.Game)], null),
        },
        ReadHandler = (_, cancellationToken) => Interlocked.Increment(ref requestNumber) switch
        {
            1 => ValueTask.FromResult(Page([
                App("game", "Game", WidgetAppLibraryKind.Game),
            ], null)),
            2 => new ValueTask<WidgetAppLibraryPage>(
                WaitForCancellation(backgroundStarted, backgroundCanceled, cancellationToken)),
            _ => ValueTask.FromResult(Page([
                App("application", "Application"),
            ], null)),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 1);
    await Background(widget);

    await Interactive(widget);
    await backgroundStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await OpenCatalog(widget);
    await backgroundCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Assert.Equal(GamesAppsPage.Catalog, widget.Page);
    Assert.SequenceEqual(["application"], widget.Items.Select(item => item.AppId));
    Assert.True(ActionSurfaces(Snapshot(widget, 221).Root).Any(tile =>
        tile.ActionId == "games.toggle-curation" && TileTitle(tile) == "Application"));
    await Background(widget);
}

static async Task ExplicitRefreshReconcilesCachedLibrary()
{
    var refreshStarted = NewSignal();
    var refreshPage = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await AddFromCatalog(widget, "Alpha");
    await BackToLibrary(widget);
    var before = fake.ResolveRequests.Count;
    var pageRequestsBefore = fake.PageRequests.Count;
    var root = Nodes(Snapshot(widget, 64).Root).Single(node => node.Id == "games.root");
    Assert.True(root.Shortcuts.Any(shortcut =>
        shortcut.Button == ControllerButton.Y && shortcut.ActionId == "games.retry"));

    fake.ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
        AwaitPage(refreshPage.Task, refreshStarted, token));
    var refresh = widget.OnActionAsync(new("games.retry", "games.root")).AsTask();
    await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var during = AssertReadyLibrary(widget, 350, "Alpha");
    Assert.False(Nodes(during.Root).Any(node =>
        node.Kind == ViewNodeKind.LoadingIndicator));
    refreshPage.SetResult(Page([App("opaque-a", "Alpha")], null));
    await refresh;

    Assert.Equal(before + 1, fake.ResolveRequests.Count);
    Assert.Equal(pageRequestsBefore + 1, fake.PageRequests.Count);
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    Assert.True(ActionSurfaces(Snapshot(widget, 65).Root).Any(tile =>
        tile.ActionId == "games.launch" && TileTitle(tile) == "Alpha"));
    await Background(widget);
}

static async Task FastColdLoadDoesNotFlashLoading()
{
    var fake = new FakeAppLibraryHost();
    var widget = Create(fake);
    var observed = new List<GamesAppsViewState>();
    widget.Invalidated += (_, _) => observed.Add(widget.ViewState);

    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await Task.Delay(GamesAppsWidget.ColdLoadingDelayMilliseconds + 75);

    Assert.False(observed.Contains(GamesAppsViewState.Loading));
    Assert.False(Nodes(Snapshot(widget, 53).Root).Any(node =>
        node.Kind == ViewNodeKind.LoadingIndicator));
    Assert.False(Nodes(Snapshot(widget, 53).Root).Any(node => node.Id == "games.retry"));
    await Background(widget);
}

static async Task SlowColdLoadShowsDelayedLoading()
{
    var discoveryStarted = NewSignal();
    var discovery = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeAppLibraryHost
    {
        PrivateState = SavedState("saved-opaque-a"),
        ReadHandler = async (_, cancellationToken) =>
        {
            discoveryStarted.TrySetResult();
            return await discovery.Task.WaitAsync(cancellationToken);
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await discoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(GamesAppsViewState.Initial, widget.ViewState);
    Assert.False(Nodes(Snapshot(widget, 54).Root).Any(node => node.Id == "games.retry"));
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Loading);
    var loading = Snapshot(widget, 55);
    Assert.Contains("Loading", Text(loading.Root, "games.state.title").Text!);
    Assert.True(Nodes(loading.Root).Any(node =>
        node.Kind == ViewNodeKind.LoadingIndicator && node.Id == "games.state.loading"));

    discovery.TrySetResult(Page([App("opaque-a", "Alpha")], null));
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await Background(widget);
}

static async Task CurationSurvivesNewInstance()
{
    var sharedState = new WidgetTestPrivateState();
    var firstHost = new FakeAppLibraryHost
    {
        PrivateState = sharedState,
        Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
    };
    var first = Create(firstHost);
    await Interactive(first);
    await AddFromCatalog(first, "Alpha");
    await Background(first);
    Assert.True(sharedState.Json is not null);

    var restartedHost = new FakeAppLibraryHost
    {
        PrivateState = sharedState,
        Pages = { [0] = Page([App("fresh-a", "Alpha") with
            { SavedId = "saved-opaque-a" }], null) },
    };
    var restarted = Create(restartedHost);
    await Interactive(restarted);
    await WaitUntil(() => restarted.ViewState == GamesAppsViewState.Ready);
    Assert.Equal(1, restartedHost.PageRequests.Count);
    Assert.SequenceEqual(["fresh-a"], restarted.CuratedItems.Select(item => item.AppId));
    Assert.True(ActionSurfaces(Snapshot(restarted, 50).Root).Any(tile =>
        tile.ActionId == "games.launch" && TileTitle(tile) == "Alpha"));
    await Background(restarted);
}

static async Task CurrentStateSurvivesRestart()
{
    var sharedState = new WidgetTestPrivateState();
    var firstHost = new FakeAppLibraryHost
    {
        PrivateState = sharedState,
        Pages =
        {
            [0] = Page([
                App("alpha", "Alpha", WidgetAppLibraryKind.Game),
                App("beta", "Beta", WidgetAppLibraryKind.Game),
                App("gamma", "Gamma", WidgetAppLibraryKind.Application),
            ], null),
        },
    };
    var first = Create(firstHost);
    await Interactive(first);
    await WaitUntil(() => first.CuratedItems.Count == 2);
    await AddFromCatalog(first, "Gamma");
    await BackToLibrary(first);
    var alpha = ActionSurfaces(Snapshot(first, 320).Root)
        .Single(tile => TileTitle(tile) == "Alpha");
    await first.OnActionAsync(new("games.remove", alpha.Id));
    var gamma = ActionSurfaces(Snapshot(first, 321).Root)
        .Single(tile => TileTitle(tile) == "Gamma");
    await first.OnActionAsync(new("games.launch", gamma.Id));
    Assert.SequenceEqual(["gamma", "beta"],
        first.CuratedItems.Select(item => item.AppId));
    await Background(first);

    var discovery = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var freshItems = new[]
    {
        App("fresh-alpha", "Alpha", WidgetAppLibraryKind.Game) with
            { SavedId = "saved-alpha" },
        App("fresh-beta", "Beta", WidgetAppLibraryKind.Game) with
            { SavedId = "saved-beta" },
        App("fresh-gamma", "Gamma", WidgetAppLibraryKind.Application) with
            { SavedId = "saved-gamma" },
    };
    var restartedHost = new FakeAppLibraryHost
    {
        PrivateState = sharedState,
        Pages = { [0] = Page(freshItems, null) },
        ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
            discovery.Task.WaitAsync(token)),
    };
    var restarted = Create(restartedHost);
    await Interactive(restarted);
    await WaitUntil(() => restarted.CuratedItems.Count == 2);

    var warm = Snapshot(restarted, 322);
    Assert.SequenceEqual(["Gamma", "Beta"],
        restarted.CuratedItems.Select(item => item.DisplayName));
    Assert.True(restarted.CuratedItems.All(item =>
        item.AppId.StartsWith("pending.", StringComparison.Ordinal)));
    Assert.Equal(LibraryElementId("saved-gamma"), warm.InitialFocusId);
    Assert.False(ActionSurfaces(warm.Root).Any(tile => TileTitle(tile) == "Alpha"));
    using (var persisted = System.Text.Json.JsonDocument.Parse(sharedState.Json!))
        Assert.SequenceEqual(["saved-alpha"], persisted.RootElement
            .GetProperty("ExcludedGameSavedIds").EnumerateArray()
            .Select(item => item.GetString()!));

    discovery.SetResult(Page(freshItems, null));
    await WaitUntil(() => restarted.CuratedItems.All(item =>
        !item.AppId.StartsWith("pending.", StringComparison.Ordinal)));
    Assert.SequenceEqual(["fresh-gamma", "fresh-beta"],
        restarted.CuratedItems.Select(item => item.AppId));
    Assert.Equal(LibraryElementId("saved-gamma"), Snapshot(restarted, 323).InitialFocusId);
    await Background(restarted);
}

static async Task WarmStartPrecedesAuthorityResolution()
{
    var state = ProjectedState(
        selectedSavedId: "saved-b",
        ("saved-b", "Beta", WidgetAppLibraryKind.Game),
        ("saved-a", "Alpha", WidgetAppLibraryKind.Application));
    var discovery = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var resolvedItems = new[]
    {
        App("fresh-a", "Alpha", WidgetAppLibraryKind.Application) with
            { SavedId = "saved-a" },
        App("fresh-b", "Beta", WidgetAppLibraryKind.Game) with
            { SavedId = "saved-b" },
    };
    var fake = new FakeAppLibraryHost
    {
        PrivateState = state,
        Pages = { [0] = Page(resolvedItems, null) },
        ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
            discovery.Task.WaitAsync(token)),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Count == 2);

    var warm = Snapshot(widget, 301);
    Assert.SequenceEqual(["Beta", "Alpha"],
        widget.CuratedItems.Select(item => item.DisplayName));
    Assert.True(widget.CuratedItems.All(item =>
        item.AppId.StartsWith("pending.", StringComparison.Ordinal)));
    var warmBeta = ActionSurfaces(warm.Root).Single(tile => TileTitle(tile) == "Beta");
    Assert.True(warmBeta.IsDisabled == true);
    Assert.Equal("Checking…", Text(warm.Root, warmBeta.Id + ".state").Text);
    Assert.Equal(warmBeta.Id, warm.InitialFocusId);
    Assert.Contains("checking 2 launch entries", Text(warm.Root, "games.status").Text!);
    await widget.OnActionAsync(new("games.launch", warmBeta.Id));
    Assert.Equal(0, fake.LaunchedIds.Count);

    discovery.SetResult(Page(resolvedItems, null));
    await WaitUntil(() => widget.CuratedItems.All(item =>
        !item.AppId.StartsWith("pending.", StringComparison.Ordinal)));
    var resolved = Snapshot(widget, 302);
    var resolvedBeta = ActionSurfaces(resolved.Root).Single(tile => TileTitle(tile) == "Beta");
    Assert.Equal(warmBeta.Id, resolvedBeta.Id);
    Assert.Equal(warmBeta.Id, resolved.InitialFocusId);
    Assert.True(resolvedBeta.IsDisabled != true);
    await widget.OnActionAsync(new("games.launch", resolvedBeta.Id));
    Assert.SequenceEqual(["fresh-b"], fake.LaunchedIds);
    await Background(widget);
}

static async Task WarmStartSurvivesRefreshFailure()
{
    var fake = new FakeAppLibraryHost
    {
        PrivateState = ProjectedState(
            selectedSavedId: "saved-a",
            ("saved-a", "Alpha", WidgetAppLibraryKind.Application)),
        ReadException = new WidgetCapabilityException(
            "platform_unavailable", "private catalog detail"),
        ResolveException = new WidgetCapabilityException(
            "platform_unavailable", "private resolver detail"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => Text(Snapshot(widget, 303).Root, "games.status").Text!
        .Contains("refresh unavailable", StringComparison.Ordinal));

    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    Assert.SequenceEqual(["Alpha"], widget.CuratedItems.Select(item => item.DisplayName));
    var snapshot = Snapshot(widget, 304);
    var alpha = ActionSurfaces(snapshot.Root).Single(tile => TileTitle(tile) == "Alpha");
    Assert.True(alpha.IsDisabled == true);
    Assert.False(System.Text.Json.JsonSerializer.Serialize(snapshot)
        .Contains("private", StringComparison.Ordinal));
    await Background(widget);
}

static async Task WarmStartRejectsLateResolution()
{
    var cancellationObserved = NewSignal();
    var discovery = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeAppLibraryHost
    {
        PrivateState = ProjectedState(
            selectedSavedId: "saved-a",
            ("saved-a", "Alpha", WidgetAppLibraryKind.Application)),
        Pages = { [0] = Page([App("fresh-a", "Changed") with
            { SavedId = "saved-a" }], null) },
        ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
            IgnorePageCancellation(token, cancellationObserved, discovery.Task)),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 1);

    var backgrounding = Background(widget);
    await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
    discovery.SetResult(Page([App("fresh-a", "Changed") with
        { SavedId = "saved-a" }], null));
    await backgrounding.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.SequenceEqual(["Alpha"], widget.CuratedItems.Select(item => item.DisplayName));
    Assert.True(widget.CuratedItems.Single().AppId.StartsWith(
        "pending.", StringComparison.Ordinal));
}

static async Task LoadsMore()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([App("one", "One"), App("two", "Two")], 32),
            [32] = Page([App("three", "Three"), App("four", "Four")], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await OpenCatalog(widget);
    Assert.True(Buttons(Snapshot(widget, 6).Root).Any(button => button.Id == "games.load-more"));
    await widget.OnActionAsync(new("games.load-more", "games.load-more"));
    Assert.Equal(2, widget.Items.Count);
    Assert.Equal("three", widget.SelectedAppId);
    Assert.Equal<int?>(null, widget.NextOffset);
    var snapshot = Snapshot(widget, 7);
    Assert.True(!Buttons(snapshot.Root).Any(button => button.Id == "games.load-more"));
    Assert.Equal(2, ActionSurfaces(snapshot.Root)
        .Count(tile => tile.ActionId == "games.toggle-curation"));
    var previous = Buttons(snapshot.Root).Single(button => button.Id == "games.previous-page");
    Assert.Equal(ActionSurfaces(snapshot.Root).First(tile =>
        tile.ActionId == "games.toggle-curation").Id, previous.Focus!.Down);
    Assert.Equal(ActionSurfaces(snapshot.Root).Single(tile => TileTitle(tile) == "Three").Id,
        snapshot.InitialFocusId);
    Assert.Valid(snapshot);

    await widget.OnActionAsync(new("games.previous-page", previous.Id));
    var restored = Snapshot(widget, 237);
    Assert.Equal(2, widget.Items.Count);
    Assert.Equal(ActionSurfaces(restored.Root).Single(tile => TileTitle(tile) == "One").Id,
        restored.InitialFocusId);
    Assert.True(Buttons(restored.Root).Any(button => button.Id == "games.load-more"));
    Assert.False(Buttons(restored.Root).Any(button => button.Id == "games.previous-page"));
    Assert.Valid(restored);
    await Background(widget);
}

static async Task LoadMoreIsSingleFlight()
{
    var started = NewSignal();
    var release = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var browsing = false;
    var fake = new FakeAppLibraryHost
    {
        ReadHandler = (request, cancellationToken) => !browsing
            ? ValueTask.FromResult(Page([], null))
            : request.Offset == 0
            ? ValueTask.FromResult(Page([App("one", "One")], 32))
            : new ValueTask<WidgetAppLibraryPage>(
                AwaitPage(release.Task, started, cancellationToken)),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    browsing = true;
    await OpenCatalog(widget);

    var first = widget.OnActionAsync(new("games.load-more", "games.load-more")).AsTask();
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var busy = Snapshot(widget, 9);
    var loadMore = Buttons(busy.Root).Single(button => button.Id == "games.load-more");
    Assert.True(loadMore.IsBusy == true);
    Assert.True(loadMore.IsDisabled == true);
    Assert.True(ActionSurfaces(busy.Root)
        .Where(tile => tile.ActionId == "games.toggle-curation")
        .All(tile => tile.IsDisabled == true));

    await widget.OnActionAsync(new("games.load-more", "games.load-more"));
    Assert.Equal(1, fake.PageRequests.Count(request => request.Offset == 32));
    release.TrySetResult(Page([App("two", "Two")], null));
    await first;
    Assert.Equal(1, widget.Items.Count);
    await Background(widget);
}

static async Task PaginationStopsAtMaximum()
{
    var browsing = false;
    var fake = new FakeAppLibraryHost
    {
        ReadHandler = (request, _) =>
        {
            if (!browsing) return ValueTask.FromResult(Page([], null));
            var items = Enumerable.Range(request.Offset, GamesAppsWidget.PageSize)
                .Select(index => App($"opaque-{index}", $"Application {index}"))
                .ToArray();
            return ValueTask.FromResult(Page(items, request.Offset + GamesAppsWidget.PageSize));
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    browsing = true;
    fake.PageRequests.Clear();
    await OpenCatalog(widget);
    while (widget.NextOffset is not null)
        await widget.OnActionAsync(new("games.load-more", "games.load-more"));

    Assert.Equal(GamesAppsWidget.PageSize, widget.Items.Count);
    Assert.Equal(16, fake.PageRequests.Count);
    Assert.Equal<int?>(null, widget.NextOffset);
    var maximumPage = Snapshot(widget, 238);
    Assert.Equal(GamesAppsWidget.PageSize, ActionSurfaces(maximumPage.Root)
        .Count(tile => tile.ActionId == "games.toggle-curation"));
    Assert.True(Nodes(maximumPage.Root).Count() < ProtocolConstants.MaximumNodeCount);
    Assert.True(Buttons(maximumPage.Root).Any(button => button.Id == "games.previous-page"));
    Assert.Valid(maximumPage);
    await Background(widget);
}

static async Task LaunchIsSingleFlight()
{
    var started = NewSignal();
    var release = new TaskCompletionSource<WidgetCapabilityAcknowledgement>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha"), App("opaque-b", "Beta")], null) },
        LaunchHandler = (_, cancellationToken) => new ValueTask<WidgetCapabilityAcknowledgement>(
            AwaitLaunch(release.Task, started, cancellationToken)),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await OpenCatalog(widget);
    await AddFromOpenCatalog(widget, "Alpha");
    await AddFromOpenCatalog(widget, "Beta");
    await BackToLibrary(widget);
    var initial = Snapshot(widget, 10);
    var alpha = ActionSurfaces(initial.Root).Single(tile => TileTitle(tile) == "Alpha");
    var beta = ActionSurfaces(initial.Root).Single(tile => TileTitle(tile) == "Beta");

    var first = widget.OnActionAsync(new("games.launch", alpha.Id)).AsTask();
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var busy = Snapshot(widget, 11);
    Assert.True(ActionSurfaces(busy.Root)
        .Single(tile => TileTitle(tile) == "Alpha").IsBusy == true);
    Assert.True(ActionSurfaces(busy.Root).Where(tile => tile.ActionId == "games.launch")
        .All(tile => tile.IsDisabled == true));

    await widget.OnActionAsync(new("games.launch", beta.Id));
    Assert.SequenceEqual(["opaque-a"], fake.LaunchedIds);
    Assert.Equal("opaque-a", widget.SelectedAppId);
    release.TrySetResult(new WidgetCapabilityAcknowledgement(true));
    await first;
    Assert.Contains("Opened Alpha", Text(Snapshot(widget, 12).Root, "games.status").Text!);
    await Background(widget);
}

static async Task BackgroundCancelsPageWork()
{
    var started = NewSignal();
    var canceled = NewSignal();
    var fake = new FakeAppLibraryHost
    {
        ReadHandler = (request, cancellationToken) => request.Offset == 0
            ? ValueTask.FromResult(Page([App("one", "One")], 32))
            : new ValueTask<WidgetAppLibraryPage>(
                WaitForCancellation(started, canceled, cancellationToken)),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await OpenCatalog(widget);

    var command = widget.OnActionAsync(new("games.load-more", "games.load-more")).AsTask();
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Background(widget);
    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await command;
    Assert.Equal(1, widget.Items.Count);
}

static async Task LaunchDenialKeepsLibrary()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
        LaunchException = new WidgetCapabilityException("permission_denied", "private detail"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await AddFromCatalog(widget, "Alpha");
    await BackToLibrary(widget);
    var alpha = ActionSurfaces(Snapshot(widget, 13).Root)
        .Single(tile => TileTitle(tile) == "Alpha");
    await widget.OnActionAsync(new("games.launch", alpha.Id));

    var denied = Snapshot(widget, 14);
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    Assert.Equal(1, widget.Items.Count);
    Assert.Contains("Allow Games & Apps access in Settings",
        Text(denied.Root, "games.status").Text!);
    Assert.False(System.Text.Json.JsonSerializer.Serialize(denied)
        .Contains("private detail", StringComparison.Ordinal));
    await Background(widget);
}

static async Task FailureStates()
{
    foreach (var (exception, expectedStatus) in new (Exception, string)[]
    {
        (new WidgetCapabilityException("permission_denied", "denied"),
            "Allow Games & Apps access in Settings"),
        (new WidgetCapabilityException("lifecycle_denied", "paused"),
            "App library is paused"),
        (new WidgetCapabilityException("platform_unavailable", "private path"),
            "App library unavailable"),
    })
    {
        var fake = new FakeAppLibraryHost { ReadException = exception };
        var widget = Create(fake);
        await Interactive(widget);
        await WaitUntil(() => widget.ViewState != GamesAppsViewState.Initial &&
            widget.ViewState != GamesAppsViewState.Loading);
        Assert.Equal(GamesAppsPage.Library, widget.Page);
        Assert.Equal(1, fake.PageRequests.Count);
        var snapshot = Snapshot(widget, 8);
        Assert.Contains(expectedStatus, Text(snapshot.Root, "games.status").Text!);
        Assert.True(Buttons(snapshot.Root).Any(button =>
            button.ActionId == "games.retry"));
        Assert.False(System.Text.Json.JsonSerializer.Serialize(snapshot)
            .Contains("private path", StringComparison.Ordinal));
        Assert.Valid(snapshot);
        await Background(widget);
    }
}

static async Task SubsequentFailureKeepsLastGood()
{
    var state = new WidgetTestPrivateState();
    var fake = new FakeAppLibraryHost
    {
        PrivateState = state,
        Pages =
        {
            [0] = Page([
                App("a", "Alpha", WidgetAppLibraryKind.Game),
                App("b", "Beta", WidgetAppLibraryKind.Game),
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 2);
    var beta = ActionSurfaces(Snapshot(widget, 215).Root)
        .Single(tile => TileTitle(tile) == "Beta");
    await widget.OnActionAsync(new("games.launch", beta.Id));
    var revision = state.Revision;

    fake.ReadException = new WidgetCapabilityException(
        "platform_unavailable", "private catalog detail");
    fake.ResolveException = new WidgetCapabilityException(
        "platform_unavailable", "private resolver detail");
    await widget.OnActionAsync(new("games.retry", "games.root"));

    Assert.True(widget.CuratedItems.All(item =>
        item.AppId.StartsWith("pending.", StringComparison.Ordinal)));
    var snapshot = Snapshot(widget, 216);
    Assert.Equal(beta.Id, snapshot.InitialFocusId);
    Assert.Contains("refresh unavailable", Text(snapshot.Root, "games.status").Text!);
    var betaAfterFailure = ActionSurfaces(snapshot.Root)
        .Single(tile => TileTitle(tile) == "Beta");
    Assert.True(betaAfterFailure.IsDisabled == true);
    await widget.OnActionAsync(new("games.launch", betaAfterFailure.Id));
    Assert.SequenceEqual(["b"], fake.LaunchedIds);
    Assert.Equal(revision, state.Revision);
    var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
    Assert.False(json.Contains("private catalog", StringComparison.Ordinal));
    Assert.False(json.Contains("private resolver", StringComparison.Ordinal));
    await Background(widget);
}

static async Task RetryRecoversTransientFailure()
{
    var fake = new FakeAppLibraryHost
    {
        PrivateState = SavedState("saved-opaque-a"),
        Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
        ReadException = new WidgetCapabilityException(
            "platform_unavailable", "private provider detail"),
        ResolveException = new WidgetCapabilityException(
            "platform_unavailable", "private provider detail"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.ServiceUnavailable);
    var failed = Snapshot(widget, 60);
    var retry = Buttons(failed.Root).Single(button => button.ActionId == "games.retry");
    Assert.Equal("games.retry", retry.ActionId);
    Assert.True(retry.IsDisabled != true);
    Assert.False(System.Text.Json.JsonSerializer.Serialize(failed)
        .Contains("private provider detail", StringComparison.Ordinal));

    fake.ReadException = null;
    fake.ResolveException = null;
    await widget.OnActionAsync(new(retry.ActionId!, retry.Id));

    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    Assert.SequenceEqual(["opaque-a"], widget.CuratedItems.Select(item => item.AppId));
    var recovered = Snapshot(widget, 61);
    Assert.True(ActionSurfaces(recovered.Root).Any(tile =>
        tile.ActionId == "games.launch" && TileTitle(tile) == "Alpha"));
    Assert.Valid(recovered);
    await Background(widget);
}

static async Task BackgroundCancelsRetry()
{
    var started = NewSignal();
    var canceled = NewSignal();
    var fake = new FakeAppLibraryHost
    {
        PrivateState = SavedState("saved-opaque-a"),
        ReadException = new WidgetCapabilityException(
            "platform_unavailable", "private provider detail"),
        ResolveException = new WidgetCapabilityException(
            "platform_unavailable", "private provider detail"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.ServiceUnavailable);
    fake.ReadException = null;
    fake.ResolveException = null;
    fake.ReadHandler = async (_, token) =>
    {
        started.TrySetResult();
        using var registration = token.Register(() => canceled.TrySetResult());
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return Page([], null);
    };

    var retry = widget.OnActionAsync(new("games.retry", "games.retry")).AsTask();
    await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await Background(widget).WaitAsync(TimeSpan.FromSeconds(1));
    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await retry.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal(GamesAppsPage.Library, widget.Page);
}

static async Task LibraryPolicyIsRenderIndependent()
{
    var alpha = App("alpha", "Alpha");
    var beta = App("beta", "Beta");
    var game = App("game", "Game", WidgetAppLibraryKind.Game);
    var baseline = new GamesAppsLibraryState(
        3,
        [alpha.SavedId, beta.SavedId],
        alpha.SavedId)
    {
        DisplayItems =
        [
            GamesAppsLibraryPolicy.ToDisplayItem(alpha),
            GamesAppsLibraryPolicy.ToDisplayItem(beta),
        ],
    };

    var removal = GamesAppsLibraryPolicy.Remove(
        baseline, alpha, [alpha.SavedId, beta.SavedId]);
    Assert.True(removal.Accepted);
    Assert.SequenceEqual([beta.SavedId], removal.State.SavedIds);
    Assert.Equal(beta.SavedId, removal.State.SelectedSavedId);
    Assert.SequenceEqual([beta.SavedId],
        removal.State.DisplayItems.Select(item => item.SavedId));

    var reconciliation = GamesAppsLibraryPolicy.Reconcile(
        removal.State, [beta, game], removal.State.SelectedSavedId);
    Assert.SequenceEqual([beta.SavedId, game.SavedId], reconciliation.State.SavedIds);
    Assert.SequenceEqual([game.SavedId], reconciliation.State.AutoGameSavedIds);

    var latest = new GamesAppsLibraryState(
        3,
        [alpha.SavedId, beta.SavedId, game.SavedId],
        alpha.SavedId)
    {
        AutoGameSavedIds = [game.SavedId],
        DisplayItems =
        [
            GamesAppsLibraryPolicy.ToDisplayItem(alpha),
            GamesAppsLibraryPolicy.ToDisplayItem(beta),
            GamesAppsLibraryPolicy.ToDisplayItem(game),
        ],
    };
    var writes = 0;
    GamesAppsLibraryState? committed = null;
    async ValueTask<WidgetPrivateStateMutation> Write(
        GamesAppsLibraryState state,
        long revision,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        writes++;
        if (writes == 1)
            throw new WidgetCapabilityException(
                "state_conflict", "The test state changed concurrently.");
        Assert.Equal(9L, revision);
        committed = state;
        return new WidgetPrivateStateMutation(10);
    }
    ValueTask<WidgetPrivateStateValue<GamesAppsLibraryState>> Read(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new WidgetPrivateStateValue<GamesAppsLibraryState>(true, latest, 9));
    }

    var saved = await GamesAppsLibraryStore.SaveAsync(
        Write, Read, baseline, removal.State, expectedRevision: 4, CancellationToken.None);
    Assert.Equal(GamesAppsLibrarySaveStatus.Saved, saved.Status);
    Assert.Equal(2, writes);
    Assert.True(committed is not null);
    Assert.SequenceEqual([beta.SavedId, game.SavedId], committed!.SavedIds);
    Assert.False(committed.SavedIds.Contains(alpha.SavedId, StringComparer.Ordinal));
}

static Task CatalogPolicyOwnsNavigation()
{
    var first = Enumerable.Range(0, GamesAppsWidget.PageSize)
        .Select(index => App($"first-{index}", $"First {index}"))
        .ToArray();
    var second = Enumerable.Range(0, GamesAppsWidget.PageSize)
        .Select(index => App($"second-{index}", $"Second {index}"))
        .ToArray();
    var initial = GamesAppsCatalogPolicy.ApplyPage(
        GamesAppsCatalogState.Empty,
        Page(first, GamesAppsWidget.PageSize),
        GamesAppsCatalogPageTransition.Initial,
        0,
        GamesAppsWidget.PageSize,
        GamesAppsWidget.MaximumItems);
    Assert.False(initial.EmptyInitial);
    Assert.False(initial.State.CanLoadPrevious);
    Assert.Equal(GamesAppsWidget.PageSize, initial.State.NextOffset);

    var next = GamesAppsCatalogPolicy.ApplyPage(
        initial.State,
        Page(second, GamesAppsWidget.PageSize * 2),
        GamesAppsCatalogPageTransition.Next,
        GamesAppsWidget.PageSize,
        GamesAppsWidget.PageSize,
        GamesAppsWidget.MaximumItems);
    Assert.True(next.State.CanLoadPrevious);
    Assert.SequenceEqual([0], next.State.BackOffsets);
    Assert.Equal("saved-second-0", next.State.Items[0].SavedId);

    var previous = GamesAppsCatalogPolicy.ApplyPage(
        next.State,
        Page(first, GamesAppsWidget.PageSize),
        GamesAppsCatalogPageTransition.Previous,
        0,
        GamesAppsWidget.PageSize,
        GamesAppsWidget.MaximumItems);
    Assert.False(previous.State.CanLoadPrevious);
    Assert.Equal("saved-first-0", previous.State.Items[0].SavedId);
    return Task.CompletedTask;
}

static Task ResponsibilitySplitContract()
{
    var sourceRoot = Path.Combine(AppContext.BaseDirectory, "source");
    var orchestration = File.ReadAllText(Path.Combine(sourceRoot, "GamesAppsWidget.cs"));
    var presentation = File.ReadAllText(Path.Combine(sourceRoot, "GamesAppsPresentation.cs"));
    var catalog = File.ReadAllText(Path.Combine(sourceRoot, "GamesAppsCatalogPolicy.cs"));
    var policy = File.ReadAllText(Path.Combine(sourceRoot, "GamesAppsLibraryState.cs"));
    var store = File.ReadAllText(Path.Combine(sourceRoot, "GamesAppsLibraryStore.cs"));

    AssertSourceContains(orchestration, "OnActivatedAsync");
    AssertSourceContains(orchestration, "OnActionAsync");
    AssertSourceContains(orchestration, "private readonly object _gate");
    AssertSourceContains(orchestration, "private readonly SemaphoreSlim _commandGate");
    Assert.True(!orchestration.Contains("RenderLibrary(", StringComparison.Ordinal),
        "Lifecycle/action orchestration regained Library composition.");
    Assert.True(!orchestration.Contains("state_conflict", StringComparison.Ordinal),
        "Lifecycle/action orchestration regained bounded CAS policy.");

    AssertSourceContains(presentation, "GamesAppsPresentationState state");
    AssertSourceContains(presentation, "RenderLibrary");
    AssertSourceContains(presentation, "RenderCatalog");
    Assert.True(!presentation.Contains("HostServices", StringComparison.Ordinal),
        "Pure presentation acquired provider ownership.");
    Assert.True(!presentation.Contains("lock (", StringComparison.Ordinal),
        "Pure presentation reads mutable widget state.");

    AssertSourceContains(catalog, "GamesAppsCatalogPageResult");
    Assert.True(!catalog.Contains("HostServices", StringComparison.Ordinal),
        "Catalog navigation acquired provider ownership.");
    Assert.True(!catalog.Contains("GamesAppsLibraryState", StringComparison.Ordinal),
        "Catalog navigation acquired persistence knowledge.");

    AssertSourceContains(policy, "GamesAppsLibraryMutation Remove");
    AssertSourceContains(policy, "GamesAppsLibraryReconciliation Reconcile");
    AssertSourceContains(policy, "GamesAppsLibraryProjection Project");
    Assert.True(!policy.Contains("WidgetView", StringComparison.Ordinal),
        "Library policy acquired rendering ownership.");
    Assert.True(!policy.Contains("HostServices", StringComparison.Ordinal),
        "Library policy acquired ambient host-service ownership.");

    AssertSourceContains(store, "GamesAppsLibraryStore");
    AssertSourceContains(store, "state_conflict");
    Assert.True(!store.Contains("GamesAppsWidget", StringComparison.Ordinal),
        "CAS storage acquired widget ownership.");
    Assert.True(!store.Contains("WidgetView", StringComparison.Ordinal),
        "CAS storage acquired rendering ownership.");

    var alpha = App("presented", "Presented");
    var view = GamesAppsPresentation.Render(new GamesAppsPresentationState(
        GamesAppsViewState.Ready,
        GamesAppsPage.Library,
        "1 saved",
        [alpha],
        [alpha.SavedId],
        new HashSet<string>([alpha.SavedId], StringComparer.Ordinal),
        alpha.AppId,
        LaunchingAppId: null,
        LoadingMore: false,
        LibraryMutationBusy: false,
        NextOffset: null,
        CanLoadPrevious: false,
        WidgetLifecycleState.Interactive,
        Toast: null));
    Assert.True(view.Root is StackElement);
    return Task.CompletedTask;
}

static Task PurePresentationIsDeterministic()
{
    var alpha = App("deterministic", "Deterministic");
    var presentation = new GamesAppsPresentationState(
        GamesAppsViewState.Ready,
        GamesAppsPage.Library,
        "1 saved",
        [alpha],
        [alpha.SavedId],
        new HashSet<string>([alpha.SavedId], StringComparer.Ordinal),
        alpha.AppId,
        LaunchingAppId: null,
        LoadingMore: false,
        LibraryMutationBusy: false,
        NextOffset: null,
        CanLoadPrevious: false,
        WidgetLifecycleState.Interactive,
        Toast: null);
    var widget = new GamesAppsPresentationProbeWidget(presentation);
    var first = widget.RenderSnapshot("games.presenter", 101) with { Sequence = 0 };
    var second = widget.RenderSnapshot("games.presenter", 102) with { Sequence = 0 };
    var firstJson = System.Text.Json.JsonSerializer.Serialize(first);
    var secondJson = System.Text.Json.JsonSerializer.Serialize(second);
    Assert.Equal(firstJson, secondJson);
    Assert.Valid(first);
    Assert.Valid(second);
    return Task.CompletedTask;
}

static void AssertSourceContains(string source, string value) =>
    Assert.True(source.Contains(value, StringComparison.Ordinal),
        $"Expected source boundary to contain '{value}'.");

static Task PackageValidates()
{
    var root = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
    Assert.SequenceEqual(["system.apps.library.read.v1"], manifest.Permissions);
    Assert.SequenceEqual(["system.apps.library.launch.v1"], manifest.OptionalPermissions);
    var package = GbssPackageLoader.Load("styles/default.gbss", new GbssFileSourceProvider(root));
    var compiled = GbssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    var stateTitle = compiled.Theme!.Resolve(new GbssElement(
        "text", StyleClasses: new HashSet<string>(["games-state-title"])))!;
    var stateHelp = compiled.Theme.Resolve(new GbssElement(
        "text", StyleClasses: new HashSet<string>(["games-state-help"])))!;
    var rootStyle = compiled.Theme.Resolve(new GbssElement(
        "stack", StyleClasses: new HashSet<string>(["games-apps-widget"])))!;
    var contentStyle = compiled.Theme.Resolve(new GbssElement(
        "stack", StyleClasses: new HashSet<string>(["games-content"])))!;
    var scrollStyle = compiled.Theme.Resolve(new GbssElement(
        "scroll", StyleClasses: new HashSet<string>(["games-library-scroll"])))!;
    Assert.Equal("0", stateTitle.Get("flex-shrink")?.Text);
    Assert.Equal("center", stateTitle.Get("text-align")?.Text);
    Assert.Equal("0", stateHelp.Get("flex-shrink")?.Text);
    Assert.Equal("100vh", rootStyle.Get("height")?.Text);
    Assert.Equal("0px", rootStyle.Get("min-height")?.Text);
    Assert.Equal("0px", contentStyle.Get("min-height")?.Text);
    Assert.Equal("1", contentStyle.Get("flex-grow")?.Text);
    Assert.Equal("0px", scrollStyle.Get("min-height")?.Text);
    Assert.Equal("1", scrollStyle.Get("flex-grow")?.Text);
    return Task.CompletedTask;
}

static WidgetAppLibraryItem App(
    string id,
    string name,
    WidgetAppLibraryKind kind = WidgetAppLibraryKind.Application,
    string? artworkHandle = null) =>
    new(id, name, kind)
    {
        SavedId = "saved-" + id,
        ArtworkHandle = artworkHandle,
    };

static WidgetAppLibraryPage Page(IReadOnlyList<WidgetAppLibraryItem> items, int? next) =>
    new(items, next);

static WidgetTestPrivateState SavedState(params string[] savedIds)
{
    var ids = string.Join(',', savedIds.Select(id => $"\"{id}\""));
    var selected = savedIds.FirstOrDefault();
    return new WidgetTestPrivateState(
        $"{{\"Version\":3,\"SavedIds\":[{ids}],\"SelectedSavedId\":\"{selected}\"," +
        "\"AutoGameSavedIds\":[],\"ExcludedGameSavedIds\":[],\"DisplayItems\":[]}", 1);
}

static WidgetTestPrivateState ProjectedState(
    string selectedSavedId,
    params (string SavedId, string DisplayName, WidgetAppLibraryKind Kind)[] items)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        Version = 3,
        SavedIds = items.Select(item => item.SavedId).ToArray(),
        SelectedSavedId = selectedSavedId,
        AutoGameSavedIds = items
            .Where(item => item.Kind == WidgetAppLibraryKind.Game)
            .Select(item => item.SavedId)
            .ToArray(),
        ExcludedGameSavedIds = Array.Empty<string>(),
        DisplayItems = items.Select(item => new
        {
            item.SavedId,
            item.DisplayName,
            item.Kind,
        }).ToArray(),
    });
    return new WidgetTestPrivateState(json, 1);
}

static GamesAppsWidget Create(FakeAppLibraryHost fake) =>
    WidgetTestHost.Attach(new GamesAppsWidget(), fake.Build());

static async Task OpenCatalog(GamesAppsWidget widget)
{
    await widget.OnActionAsync(new("games.open-catalog", "games.open-catalog"));
    await WaitUntil(() => widget.Page == GamesAppsPage.Catalog &&
        widget.ViewState is GamesAppsViewState.Ready or GamesAppsViewState.Empty);
    Assert.Equal(GamesAppsPage.Catalog, widget.Page);
}

static async Task AddFromCatalog(GamesAppsWidget widget, string displayName)
{
    await OpenCatalog(widget);
    await AddFromOpenCatalog(widget, displayName);
}

static async Task AddFromOpenCatalog(GamesAppsWidget widget, string displayName)
{
    var tile = ActionSurfaces(Snapshot(widget, 100).Root).Single(candidate =>
        candidate.ActionId == "games.toggle-curation" && TileTitle(candidate) == displayName);
    await widget.OnActionAsync(new("games.toggle-curation", tile.Id));
}

static async Task BackToLibrary(GamesAppsWidget widget)
{
    await widget.OnActionAsync(new("back", "games.catalog"));
    Assert.Equal(GamesAppsPage.Library, widget.Page);
}

static async Task Visible(GamesAppsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

static async Task Interactive(GamesAppsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

static async Task Background(GamesAppsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

static ViewSnapshot Snapshot(GamesAppsWidget widget, long sequence) =>
    widget.RenderSnapshot("games.test", sequence);

static ViewSnapshot AssertReadyLibrary(
    GamesAppsWidget widget,
    long sequence,
    params string[] expectedTitles)
{
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    Assert.Equal(GamesAppsPage.Library, widget.Page);
    var snapshot = Snapshot(widget, sequence);
    Assert.SequenceEqual(expectedTitles, ActionSurfaces(snapshot.Root)
        .Where(tile => tile.ActionId == "games.launch")
        .Select(tile => TileTitle(tile)!));
    Assert.Equal(1, Buttons(snapshot.Root).Count(button =>
        button.ActionId == "games.open-catalog"));
    Assert.Valid(snapshot);
    return snapshot;
}

static string LibraryElementId(string savedId)
{
    var hash = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(savedId));
    return "games.item." + Convert.ToHexString(hash.AsSpan(0, 10)).ToLowerInvariant();
}

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Nodes(child)) yield return descendant;
}

static IEnumerable<ViewNode> Buttons(ViewNode node) =>
    Nodes(node).Where(candidate => candidate.Kind == ViewNodeKind.Button);

static IEnumerable<ViewNode> ActionSurfaces(ViewNode node) =>
    Nodes(node).Where(candidate => candidate.Kind == ViewNodeKind.ActionSurface);

static string? TileTitle(ViewNode tile) =>
    Nodes(tile).Single(node => node.Id == tile.Id + ".title").Text;

static ViewNode Text(ViewNode node, string id) => Nodes(node).Single(candidate => candidate.Id == id);

static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 3000)
{
    var deadline = Environment.TickCount64 + timeoutMilliseconds;
    while (!condition())
    {
        if (Environment.TickCount64 >= deadline) throw new TimeoutException();
        await Task.Delay(10);
    }
}

static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

static async Task<WidgetAppLibraryPage> AwaitPage(
    Task<WidgetAppLibraryPage> page,
    TaskCompletionSource started,
    CancellationToken cancellationToken)
{
    started.TrySetResult();
    return await page.WaitAsync(cancellationToken);
}

static async Task<WidgetCapabilityAcknowledgement> AwaitLaunch(
    Task<WidgetCapabilityAcknowledgement> acknowledgement,
    TaskCompletionSource started,
    CancellationToken cancellationToken)
{
    started.TrySetResult();
    return await acknowledgement.WaitAsync(cancellationToken);
}

static async Task<WidgetAppLibraryPage> WaitForCancellation(
    TaskCompletionSource started,
    TaskCompletionSource canceled,
    CancellationToken cancellationToken)
{
    started.TrySetResult();
    using var registration = cancellationToken.Register(() => canceled.TrySetResult());
    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    throw new InvalidOperationException("Cancellation should end the delayed page request.");
}

static async Task<WidgetAppLibraryPage> IgnorePageCancellation(
    CancellationToken cancellationToken,
    TaskCompletionSource cancellationObserved,
    Task<WidgetAppLibraryPage> completion)
{
    using var registration = cancellationToken.Register(
        () => cancellationObserved.TrySetResult());
    return await completion.ConfigureAwait(false);
}

static string ProjectDirectory()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName,
            "src", "FirstPartyWidgets", "GamesAppsWidget");
        if (Directory.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException();
}

file sealed class GamesAppsPresentationProbeWidget(
    GamesAppsPresentationState presentation) : Widget
{
    public override WidgetView Render() => GamesAppsPresentation.Render(presentation);
}

file sealed class FakeAppLibraryHost
{
    public Dictionary<int, WidgetAppLibraryPage> Pages { get; } = [];
    public List<(int Offset, int Limit)> PageRequests { get; } = [];
    public List<IReadOnlyList<string>> ResolveRequests { get; } = [];
    public List<string> LaunchedIds { get; } = [];
    public Exception? ReadException { get; set; }
    public Exception? ResolveException { get; set; }
    public Exception? LaunchException { get; set; }
    public Func<WidgetAppLibraryPageRequest, CancellationToken,
        ValueTask<WidgetAppLibraryPage>>? ReadHandler { get; set; }
    public Func<ResolveSavedWidgetAppLibraryItemsRequest, CancellationToken,
        ValueTask<ResolveSavedWidgetAppLibraryItemsResponse>>? ResolveHandler { get; set; }
    public Func<LaunchWidgetAppLibraryItemRequest, CancellationToken,
        ValueTask<WidgetCapabilityAcknowledgement>>? LaunchHandler { get; set; }
    public WidgetTestPrivateState PrivateState { get; init; } = new();

    public WidgetHostServices Build() => new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetAppLibraryCapabilities.GetPage, GetPage)
        .WithHandler(WidgetAppLibraryCapabilities.ResolveSaved, ResolveSaved)
        .WithHandler(WidgetAppLibraryCapabilities.Launch, Launch)
        .WithPrivateState(PrivateState)
        .Build();

    private ValueTask<ResolveSavedWidgetAppLibraryItemsResponse> ResolveSaved(
        ResolveSavedWidgetAppLibraryItemsRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResolveRequests.Add(request.SavedIds.ToArray());
        if (ResolveException is not null)
            return ValueTask.FromException<ResolveSavedWidgetAppLibraryItemsResponse>(
                ResolveException);
        if (ResolveHandler is not null) return ResolveHandler(request, cancellationToken);
        var bySaved = Pages.Values.SelectMany(page => page.Items)
            .GroupBy(item => item.SavedId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return ValueTask.FromResult(new ResolveSavedWidgetAppLibraryItemsResponse(
            request.SavedIds.Where(bySaved.ContainsKey).Select(id => bySaved[id]).ToArray()));
    }

    private ValueTask<WidgetAppLibraryPage> GetPage(
        WidgetAppLibraryPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PageRequests.Add((request.Offset, request.Limit));
        if (ReadException is not null)
            return ValueTask.FromException<WidgetAppLibraryPage>(ReadException);
        if (ReadHandler is not null) return ReadHandler(request, cancellationToken);
        return ValueTask.FromResult(Pages.TryGetValue(request.Offset, out var page)
            ? page
            : new WidgetAppLibraryPage([], null));
    }

    private ValueTask<WidgetCapabilityAcknowledgement> Launch(
        LaunchWidgetAppLibraryItemRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LaunchException is not null)
            return ValueTask.FromException<WidgetCapabilityAcknowledgement>(LaunchException);
        LaunchedIds.Add(request.AppId);
        if (LaunchHandler is not null) return LaunchHandler(request, cancellationToken);
        return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
    }
}

file static class Assert
{
    public static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void False(bool value) => True(!value);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        var expectedItems = expected.ToArray();
        var actualItems = actual.ToArray();
        if (!expectedItems.SequenceEqual(actualItems))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expectedItems)}]; " +
                $"actual [{string.Join(", ", actualItems)}].");
    }

    public static void Valid(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(" | ", errors));
    }
}
