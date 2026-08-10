using GameBarAlternative.FirstPartyWidgets.GamesApps;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Visible lifecycle discovers trusted games before catalog browsing", LoadsFirstPage),
    ("Trusted games auto-curate idempotently with bounded feedback", AutoCuratesTrustedGames),
    ("Automatic discovery walks bounded pages for trusted games", AutoCuratesGamesBeyondFirstPage),
    ("Refresh preserves order and focus while appending newly trusted games", RefreshAppendsGames),
    ("Disappearing games retain order and stable focus identity on reappearance", DisappearanceRetainsOrder),
    ("Explicit game exclusion survives disappearance and reappearance", ExclusionSurvivesReappearance),
    ("Identity replacement is a new game while applications remain opt-in", IdentityReplacementIsNewGame),
    ("Automatic provenance hides reclassified entries while explicit apps remain", ReclassificationPreservesOptIn),
    ("Current resolved classification authoritatively hides an automatic game", ResolvedReclassificationIsAuthoritative),
    ("Concurrent exclusion wins catalog reconciliation through bounded CAS", ConcurrentExclusionWins),
    ("Bounded exclusion storage refuses removal without losing membership", FullExclusionSetRefusesRemoval),
    ("Empty add catalog returns to the library without a dead end", EmptyCatalogReturnsToLibrary),
    ("Shared state surfaces keep loading empty and failure controller-safe", SharedStateSurfaces),
    ("Library starts curated and catalog is a bounded vertical controller picker", RendersControllerStrip),
    ("One saved app uses one full-width icon-led focus target", OneAppUsesCompactTile),
    ("Resolved application pixels replace the semantic fallback icon", ResolvedIconRenders),
    ("Many saved apps retain a compact vertical focus list", ManyAppsUseCompactRail),
    ("Maximum curated long names remain bounded and controller reachable", MaximumLongLibraryIsBounded),
    ("Library feedback uses a non-focusable lifecycle-bound toast", ToastFeedbackIsLifecycleBound),
    ("Catalog add remove and B navigation retain a user-owned library", CuratesLibrary),
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
    ("Catalog pages stay bounded and restore focus in both directions", LoadsMore),
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
        Assert.Equal(2, persisted.RootElement.GetProperty("Version").GetInt32());
        Assert.SequenceEqual(["saved-game-a", "saved-game-b"],
            persisted.RootElement.GetProperty("SavedIds").EnumerateArray()
                .Select(item => item.GetString()!));
        Assert.SequenceEqual(["saved-game-a", "saved-game-b"],
            persisted.RootElement.GetProperty("AutoGameSavedIds").EnumerateArray()
                .Select(item => item.GetString()!));
        Assert.Equal(0, persisted.RootElement.GetProperty("ExcludedGameSavedIds")
            .GetArrayLength());
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
    Assert.SequenceEqual(["fresh-a", "fresh-c"],
        widget.CuratedItems.Select(item => item.AppId));
    var absent = Snapshot(widget, 219);
    var alpha = ActionSurfaces(absent.Root).Single(tile => TileTitle(tile) == "Alpha");
    Assert.Equal(alpha.Id, absent.InitialFocusId);

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
    Assert.SequenceEqual(["replacement"], widget.CuratedItems.Select(item => item.AppId));
    Assert.False(widget.CuratedItems.Any(item => item.AppId is "application" or "unknown"));
    var replacementSnapshot = Snapshot(widget, 211);
    await widget.OnActionAsync(new("games.launch", staleElementId));
    Assert.Equal(0, fake.LaunchedIds.Count);
    var replacementElement = ActionSurfaces(replacementSnapshot.Root)
        .Single(tile => TileTitle(tile) == "Same title");
    await widget.OnActionAsync(new("games.launch", replacementElement.Id));
    Assert.SequenceEqual(["replacement"], fake.LaunchedIds);

    await AddFromCatalog(widget, "Utility");
    await BackToLibrary(widget);
    Assert.SequenceEqual(["replacement", "application"],
        widget.CuratedItems.Select(item => item.AppId));
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
                "{\"Version\":2,\"SavedIds\":[],\"SelectedSavedId\":null," +
                "\"AutoGameSavedIds\":[],\"ExcludedGameSavedIds\":[\"saved-game\"]}");
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

static async Task FullExclusionSetRefusesRemoval()
{
    var exclusions = Enumerable.Range(0, 320)
        .Select(index => $"saved-excluded-{index}").ToArray();
    var state = new WidgetTestPrivateState(System.Text.Json.JsonSerializer.Serialize(new
    {
        Version = 2,
        SavedIds = Array.Empty<string>(),
        SelectedSavedId = (string?)null,
        AutoGameSavedIds = Array.Empty<string>(),
        ExcludedGameSavedIds = exclusions,
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
    Assert.Equal(450d, snapshot.Surface!.PreferredHeight);
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
    Assert.Equal(430d, snapshot.Surface!.PreferredHeight);
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
    const string png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
        "AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha", iconPngBase64: png)], null) },
        PrivateState = SavedState("saved-opaque-a"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    var snapshot = Snapshot(widget, 64);
    var launch = ActionSurfaces(snapshot.Root).Single(tile => tile.ActionId == "games.launch");
    Assert.Equal(ViewNodeKind.ActionSurface, launch.Kind);
    var artwork = Nodes(launch).Single(node => node.Id == launch.Id + ".artwork");
    Assert.True(artwork.ImageSource!.StartsWith("data:image/png;base64,", StringComparison.Ordinal));
    Assert.Equal(ImageFit.Contain, artwork.ImageFit);
    Assert.True(artwork.Glyph is null);
    Assert.Equal(ProtocolConstants.ActionSurfaceVersion, snapshot.ProtocolVersion);
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
    Assert.Equal(430d, snapshot.Surface!.PreferredHeight);
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
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    Assert.SequenceEqual(["opaque-a"], widget.CuratedItems.Select(item => item.AppId));
    Assert.True(ActionSurfaces(Snapshot(widget, 49).Root)
        .Any(tile => tile.ActionId == "games.launch" && TileTitle(tile) == "Alpha"));
    await Background(widget);
}

static async Task ReactivationReusesCachedLibrary()
{
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
    await Background(widget);

    await Interactive(widget);
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    var cached = Snapshot(widget, 52);
    Assert.Equal(focusBefore, cached.InitialFocusId);
    Assert.True(ActionSurfaces(cached.Root).Any(tile =>
        tile.ActionId == "games.launch" && TileTitle(tile) == "Alpha"));
    await Task.Delay(GamesAppsWidget.ColdLoadingDelayMilliseconds + 75);
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    await WaitUntil(() => fake.ResolveRequests.Count == resolvesBefore + 1);
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

    await widget.OnActionAsync(new("games.retry", "games.root"));

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

    Assert.SequenceEqual(["b", "a"], widget.CuratedItems.Select(item => item.AppId));
    var snapshot = Snapshot(widget, 216);
    Assert.Equal(beta.Id, snapshot.InitialFocusId);
    Assert.Contains("refresh unavailable", Text(snapshot.Root, "games.status").Text!);
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
    string? iconPngBase64 = null) =>
    new(id, name, kind)
    {
        SavedId = "saved-" + id,
        IconPngBase64 = iconPngBase64,
    };

static WidgetAppLibraryPage Page(IReadOnlyList<WidgetAppLibraryItem> items, int? next) =>
    new(items, next);

static WidgetTestPrivateState SavedState(params string[] savedIds)
{
    var ids = string.Join(',', savedIds.Select(id => $"\"{id}\""));
    var selected = savedIds.FirstOrDefault();
    return new WidgetTestPrivateState(
        $"{{\"Version\":1,\"SavedIds\":[{ids}],\"SelectedSavedId\":\"{selected}\"}}", 1);
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
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Sequences differ.");
    }

    public static void Valid(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(" | ", errors));
    }
}
