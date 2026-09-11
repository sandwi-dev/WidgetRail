using WidgetRail.FirstPartyWidgets.GamesApps;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Visible lifecycle discovers trusted games before catalog browsing", LoadsFirstPage),
    ("Trusted games auto-curate idempotently with bounded feedback", AutoCuratesTrustedGames),
    ("Windows package games auto-curate with exact source truth",
        WindowsPackageGameProjectsTruthfully),
    ("Epic games auto-curate with exact source truth", EpicGameProjectsTruthfully),
    ("GOG games auto-curate through normalized source truth", GogGameProjectsTruthfully),
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
    ("Registration CAS capacity rejects without losing exact recovery ownership",
        RegistrationMergeCapacityRetainsRecoveryOwnership),
    ("Legacy unsupported and invalid schemas reset atomically before fresh reconciliation", LegacySchemasResetAtomically),
    ("Bounded exclusion storage refuses removal without losing membership", FullExclusionSetRefusesRemoval),
    ("Worst-case display projection remains inside private-state bounds", ProjectedStateIsBounded),
    ("Empty add catalog remains an actionable selected section", EmptyCatalogRemainsSelected),
    ("Shared state surfaces keep loading empty and failure controller-safe", SharedStateSurfaces),
    ("Library starts curated and catalog is a bounded responsive controller grid", RendersControllerStrip),
    ("One saved app remains one icon-led focus target", OneAppUsesCompactTile),
    ("Resolved application pixels replace the semantic fallback icon", ResolvedIconRenders),
    ("Game and application artwork survives refresh and warm restart", ArtworkSurvivesRefreshAndWarmRestart),
    ("Many saved apps retain one bounded responsive focus grid", ManyAppsUseCompactRail),
    ("Maximum curated long names remain bounded and controller reachable", MaximumLongLibraryIsBounded),
    ("Library feedback uses a non-focusable lifecycle-bound toast", ToastFeedbackIsLifecycleBound),
    ("Library feedback expiry is fake-time latest-wins", ToastFeedbackExpiryIsLatestWins),
    ("Catalog add remove and section navigation retain a user-owned library", CuratesLibrary),
    ("Running app route confirms current opaque identity before durable add",
        RunningAppRouteConfirmsCurrentIdentity),
    ("Portable running add persists intent before one registration and finalizes ownership",
        PortableRunningAddIsOrderedAndSingleFlight),
    ("Already-registered portable running app finalizes one ownership receipt",
        AlreadyRegisteredPortableFinalizesOnce),
    ("A stale portable registration attempt retains its durable recovery intent",
        StaleRegistrationRetainsPendingIntent),
    ("Resolved pending registration finalizes without destructive cleanup",
        ResolvedPendingRegistrationFinalizes),
    ("Unresolved pending registration is forgotten before its intent clears",
        UnresolvedPendingRegistrationForgetsBeforeClear),
    ("Pending removal cleanup does not depend on saved-item resolution",
        PendingRemovalIgnoresResolveFailure),
    ("Denied pending cleanup remains retryable across a fresh widget instance",
        DeniedPendingCleanupSurvivesRestart),
    ("Leaving during portable registration retains its durable recovery intent",
        BackgroundDuringRegistrationRetainsPendingIntent),
    ("Portable library removal forgets registration before private-state removal",
        PortableRemovalForgetsBeforeStateRemoval),
    ("Denied portable removal retains the exact saved row and ownership receipt",
        DeniedPortableRemovalRetainsState),
    ("Forgotten portable registration with failed state save becomes a disabled cleanup row",
        PortableRemovalSaveFailureWithdrawsLaunch),
    ("Malformed running confirmation cannot mutate the durable library",
        MalformedRunningConfirmationPreservesLibrary),
    ("Catalog removal preserves unrelated rows through restart failure and CAS", CatalogRemovalPreservesLibraryContinuity),
    ("Failed durable removal rolls back the whole Library mutation", FailedRemovalRollsBack),
    ("Removing a focused app selects the nearest surviving row", RemovalSelectsNearestRow),
    ("Interactive A launches only the selected opaque app", LaunchesSelectedApp),
    ("Launch revalidates a rotated opaque app ID from the selected SavedId", LaunchRevalidatesRotatedAppId),
    ("Unavailable and stale resolutions never authorize launch",
        UnavailableAndStaleNeverLaunch),
    ("Confirmed launches move the exact curated app to recent-first", SuccessfulLaunchOrdersRecentFirst),
    ("Accepted launch recency survives external foreground closure", LaunchRecencySurvivesBackground),
    ("Confirmed launch with failed recents save stays open and truthful", LaunchSaveFailureIsTruthful),
    ("Failed launch keeps curated order and actionable focus", FailedLaunchKeepsOrder),
    ("Elevation launch outcomes keep the library and explain the result",
        ElevationLaunchOutcomesAreExplained),
    ("Curated membership survives widget lifecycle reactivation", CurationSurvivesReactivation),
    ("First activation performs one saved-library session reconciliation", InitialActivationReconcilesOnce),
    ("Persisted later selection keeps first curated row as entry focus",
        PersistedLaterSelectionKeepsFirstEntryFocus),
    ("Reactivation retains the ready session snapshot without broker work", ReactivationReusesCachedLibrary),
    ("Top Refresh exclusively requests full reconciliation and retains last-good", ExplicitRefreshReconcilesCachedLibrary),
    ("Cross-route load publishes unavailable content and a one-second large spinner",
        CrossRouteLoadingIsBounded),
    ("Cross-route failure keeps the same one-second loading contract",
        CrossRouteFailureIsBounded),
    ("Header A and bumper selection enter the same remembered content group",
        HeaderAndBumperEnterRememberedContent),
    ("Newer route selection rejects a canceled stale page completion",
        RouteSwitchRejectsStaleCompletion),
    ("Warm Catalog and Running roots retain Ready content during refresh",
        WarmActiveRootsRetainReadyContent),
    ("Warm later Catalog page retains its exact bounded window",
        WarmLaterCatalogPageIsRetained),
    ("Fast cold load completes without publishing loading state", FastColdLoadDoesNotFlashLoading),
    ("Slow cold load publishes an honest delayed loading state", SlowColdLoadShowsDelayedLoading),
    ("Curated SavedIds survive a fresh widget worker instance", CurationSurvivesNewInstance),
    ("Current schema mutations order and selection survive a fresh worker", CurrentStateSurvivesRestart),
    ("A fresh worker resolves saved entries before delayed catalog completion",
        WarmStartPrecedesAuthorityResolution),
    ("A failed fresh-worker refresh retains disabled last-good display", WarmStartSurvivesRefreshFailure),
    ("Background rejects a cancellation-ignoring fresh-worker resolution", WarmStartRejectsLateResolution),
    ("Catalog pages stay bounded and restore focus in both directions", LoadsMore),
    ("Catalog navigation policy owns bounded forward and reverse transitions", CatalogPolicyOwnsNavigation),
    ("Load more publishes one busy disabled controller command", LoadMoreIsSingleFlight),
    ("Large cursor catalogs retain only one bounded semantic page", LargeCatalogRetainsOneBoundedPage),
    ("Rapid repeated launch cannot duplicate a Shell launch", LaunchIsSingleFlight),
    ("Leaving the widget cancels in-flight page work", BackgroundCancelsPageWork),
    ("Denied optional launch keeps the readable library usable", LaunchDenialKeepsLibrary),
    ("Permission and provider failures stay recoverable and sanitized", FailureStates),
    ("Manual refresh failure retains last-good order authority and focus", SubsequentFailureKeepsLastGood),
    ("Try again performs a fresh provider load and recovers transient failures", RetryRecoversTransientFailure),
    ("Leaving during retry cancels and drains the runtime-owned library load", BackgroundCancelsRetry),
    ("Manifest and WRSS package validate", PackageValidates),
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

static async Task RunningAppRouteConfirmsCurrentIdentity()
{
    var fake = new FakeAppLibraryHost
    {
        RunningObservation = new([
            new("saved-running", "Visible app", WidgetAppLibraryKind.Application,
                "Windows")
            {
                Artwork = new([new(WidgetAppLibraryArtworkRole.Tile, "artwork-running", "revision-running", WidgetAppLibraryArtworkFallback.Application)]),
            },
        ], "running-revision"),
        ConfirmRunningHandler = request => request.Revision == "running-revision"
            ? InstalledItem(
                "app-current", request.SavedId, "Visible app",
                WidgetAppLibraryKind.Application, "source-windows", "Windows")
            : null,
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    await widget.OnActionAsync(new("games.open-running", "games.open-running"));
    await WaitUntil(() => widget.Page == GamesAppsPage.Running &&
        widget.ViewState == GamesAppsViewState.Ready);
    var tile = ActionSurfaces(Snapshot(widget, 900).Root).Single(candidate =>
        candidate.ActionId == "games.toggle-curation");
    Assert.Equal("artwork-running", widget.Items.Single().Presentation.Artwork.Items.Single().Handle);
    Assert.Equal(0, fake.RunningRegistrations.Count);
    await widget.OnActionAsync(new("games.toggle-curation", tile.Id));

    Assert.Equal(1, fake.RunningConfirmations.Count);
    Assert.Equal("saved-running", fake.RunningConfirmations[0].SavedId);
    Assert.Equal("running-revision", fake.RunningConfirmations[0].Revision);
    await WaitUntil(() => widget.CuratedItems.Any(item =>
        item.SavedId == "saved-running"));
    Assert.Equal("app-current", widget.CuratedItems.Single(item =>
        item.SavedId == "saved-running").AppId);
    Assert.Equal(0, fake.RunningRegistrations.Count);
    Assert.Equal(0, fake.ForgottenRunningApps.Count);
    Assert.Contains("Added Visible app to your library",
        Text(Snapshot(widget, 919).Root, "games.toast.message").Text!);
    using (var persisted = System.Text.Json.JsonDocument.Parse(fake.PrivateState.Json!))
    {
        Assert.Equal(0, persisted.RootElement
            .GetProperty("RunningRegistrationSavedIds").GetArrayLength());
        Assert.Equal(0, persisted.RootElement
            .GetProperty("PendingRunningRegistrationSavedIds").GetArrayLength());
    }
    await Background(widget);
}

static async Task PortableRunningAddIsOrderedAndSingleFlight()
{
    const string artworkHandle = "library.art.99999999999999999999999999999999";
    var registrationStarted = NewSignal();
    var registration = new TaskCompletionSource<RegisterWidgetRunningAppResponse>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var portable = InstalledItem(
        "portable-current", "saved-portable", "Portable app",
        WidgetAppLibraryKind.Application, "source-portable", "Portable",
        artworkHandle);
    var fake = new FakeAppLibraryHost
    {
        RunningObservation = new([
            new("saved-portable", "Portable app", WidgetAppLibraryKind.Application,
                "Portable"),
        ], "running-revision"),
        ConfirmRunningHandler = _ => null,
    };
    fake.RegisterRunningHandler = (request, cancellationToken) =>
    {
        using var persisted = System.Text.Json.JsonDocument.Parse(fake.PrivateState.Json!);
        Assert.SequenceEqual([request.SavedId], persisted.RootElement
            .GetProperty("PendingRunningRegistrationSavedIds").EnumerateArray()
            .Select(item => item.GetString()!));
        Assert.Equal(0, persisted.RootElement.GetProperty("SavedIds").GetArrayLength());
        registrationStarted.TrySetResult();
        return new ValueTask<RegisterWidgetRunningAppResponse>(
            registration.Task.WaitAsync(cancellationToken));
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await widget.OnActionAsync(new("games.open-running", "games.open-running"));
    await WaitUntil(() => widget.Page == GamesAppsPage.Running &&
                          widget.ViewState == GamesAppsViewState.Ready);
    var tile = ActionSurfaces(Snapshot(widget, 920).Root).Single(candidate =>
        candidate.ActionId == "games.toggle-curation");
    var first = widget.OnActionAsync(new("games.toggle-curation", tile.Id)).AsTask();
    await registrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await widget.OnActionAsync(new("games.toggle-curation", tile.Id));
    Assert.Equal(1, fake.RunningRegistrations.Count);
    registration.SetResult(new RegisterWidgetRunningAppResponse(portable, false));
    await first.WaitAsync(TimeSpan.FromSeconds(2));
    await WaitUntil(() => widget.CuratedItems.Any(item =>
        item.SavedId == portable.SavedId));

    using var committed = System.Text.Json.JsonDocument.Parse(fake.PrivateState.Json!);
    Assert.SequenceEqual([portable.SavedId], committed.RootElement
        .GetProperty("SavedIds").EnumerateArray().Select(item => item.GetString()!));
    Assert.SequenceEqual([portable.SavedId], committed.RootElement
        .GetProperty("RunningRegistrationSavedIds").EnumerateArray()
        .Select(item => item.GetString()!));
    Assert.Equal(0, committed.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").GetArrayLength());
    Assert.Equal("running-revision", fake.RunningRegistrations.Single().Revision);
    await BackToLibrary(widget);
    var committedTile = ActionSurfaces(Snapshot(widget, 921).Root).Single(candidate =>
        candidate.ActionId == "games.launch" && TileTitle(candidate) == "Portable app");
    Assert.Equal(artworkHandle, Nodes(committedTile).Single(node =>
        node.Id == committedTile.Id + ".artwork").ArtworkHandle);
    await Background(widget);
}

static async Task UnresolvedPendingRegistrationForgetsBeforeClear()
{
    var state = RunningRegistrationState("saved-pending", pending: true);
    var fake = new FakeAppLibraryHost { PrivateState = state };
    fake.ForgetRunningHandler = (request, _) =>
    {
        using var persisted = System.Text.Json.JsonDocument.Parse(state.Json!);
        Assert.True(persisted.RootElement
            .GetProperty("PendingRunningRegistrationSavedIds").EnumerateArray()
            .Any(item => item.GetString() == request.SavedId));
        return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    Assert.SequenceEqual(["saved-pending"],
        fake.ForgottenRunningApps.Select(request => request.SavedId));
    using var recovered = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.Equal(0, recovered.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").GetArrayLength());
    await Background(widget);
}

static async Task PendingRemovalIgnoresResolveFailure()
{
    var item = InstalledItem(
        "portable-current", "saved-portable", "Portable app",
        WidgetAppLibraryKind.Application, "source-portable", "Portable");
    var state = RunningRegistrationState(
        item.SavedId, pending: false, item, removalPending: true);
    var fake = new FakeAppLibraryHost
    {
        PrivateState = state,
        ResolveException = new WidgetCapabilityException(
            "platform_unavailable", "resolution unavailable"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          fake.ForgottenRunningApps.Count == 1);

    Assert.Equal(1, fake.ResolveRequests.Count);
    Assert.SequenceEqual(["saved-portable"],
        fake.ForgottenRunningApps.Select(request => request.SavedId));
    Assert.Equal(0, fake.RunningRegistrations.Count);
    using var cleaned = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.Equal(0, cleaned.RootElement.GetProperty("SavedIds").GetArrayLength());
    Assert.Equal(0, cleaned.RootElement
        .GetProperty("RunningRegistrationSavedIds").GetArrayLength());
    Assert.Equal(0, cleaned.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").GetArrayLength());
    await Background(widget);
}

static async Task AlreadyRegisteredPortableFinalizesOnce()
{
    var item = InstalledItem(
        "portable-current", "saved-portable", "Portable app",
        WidgetAppLibraryKind.Application, "source-portable", "Portable");
    var fake = PortableRunningHost(item);
    fake.RegisterRunningHandler = (_, _) => ValueTask.FromResult(
        new RegisterWidgetRunningAppResponse(item, AlreadyRegistered: true));
    var widget = Create(fake);
    await AddFirstRunningTile(widget, 924);

    Assert.Equal(1, fake.RunningRegistrations.Count);
    using var persisted = System.Text.Json.JsonDocument.Parse(fake.PrivateState.Json!);
    Assert.SequenceEqual([item.SavedId], persisted.RootElement
        .GetProperty("RunningRegistrationSavedIds").EnumerateArray()
        .Select(value => value.GetString()!));
    Assert.Equal(0, persisted.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").GetArrayLength());
    await Background(widget);
}

static async Task StaleRegistrationRetainsPendingIntent()
{
    var item = InstalledItem(
        "portable-current", "saved-portable", "Portable app",
        WidgetAppLibraryKind.Application, "source-portable", "Portable");
    var fake = PortableRunningHost(item);
    fake.RegisterRunningHandler = (_, _) =>
        ValueTask.FromException<RegisterWidgetRunningAppResponse>(
            new WidgetCapabilityException("stale_observation", "stale"));
    var widget = Create(fake);
    await AddFirstRunningTile(widget, 925);

    using var persisted = System.Text.Json.JsonDocument.Parse(fake.PrivateState.Json!);
    Assert.SequenceEqual([item.SavedId], persisted.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").EnumerateArray()
        .Select(value => value.GetString()!));
    Assert.Equal(0, persisted.RootElement.GetProperty("SavedIds").GetArrayLength());
    Assert.Equal(1, fake.RunningRegistrations.Count);
    await Background(widget);
}

static async Task ResolvedPendingRegistrationFinalizes()
{
    var item = InstalledItem(
        "portable-current", "saved-portable", "Portable app",
        WidgetAppLibraryKind.Application, "source-portable", "Portable");
    var state = RunningRegistrationState(item.SavedId, pending: true);
    var fake = new FakeAppLibraryHost { PrivateState = state };
    fake.ResolveHandler = (request, _) => ValueTask.FromResult(
        new ResolveSavedWidgetAppLibraryItemsResponse(
            request.SavedIds.Contains(item.SavedId, StringComparer.Ordinal) ? [item] : []));
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Any(value => value.SavedId == item.SavedId));

    Assert.Equal(0, fake.ForgottenRunningApps.Count);
    using var persisted = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.SequenceEqual([item.SavedId], persisted.RootElement
        .GetProperty("SavedIds").EnumerateArray().Select(value => value.GetString()!));
    Assert.SequenceEqual([item.SavedId], persisted.RootElement
        .GetProperty("RunningRegistrationSavedIds").EnumerateArray()
        .Select(value => value.GetString()!));
    Assert.Equal(0, persisted.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").GetArrayLength());
    await Background(widget);
}

static async Task DeniedPendingCleanupSurvivesRestart()
{
    var state = RunningRegistrationState("saved-pending", pending: true);
    var denied = new FakeAppLibraryHost { PrivateState = state };
    denied.ForgetRunningHandler = (_, _) =>
        ValueTask.FromException<WidgetCapabilityAcknowledgement>(
            new WidgetCapabilityException("permission_denied", "denied"));
    var first = Create(denied);
    await Interactive(first);
    await WaitUntil(() => first.ViewState == GamesAppsViewState.Ready);
    Assert.Contains("cleanup failed",
        Text(Snapshot(first, 923).Root, "games.status").Text!);
    using (var retained = System.Text.Json.JsonDocument.Parse(state.Json!))
        Assert.SequenceEqual(["saved-pending"], retained.RootElement
            .GetProperty("PendingRunningRegistrationSavedIds").EnumerateArray()
            .Select(item => item.GetString()!));
    await Background(first);

    var recovered = new FakeAppLibraryHost { PrivateState = state };
    var second = Create(recovered);
    await Interactive(second);
    await WaitUntil(() => second.ViewState == GamesAppsViewState.Ready &&
                          recovered.ForgottenRunningApps.Count == 1);
    using var committed = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.Equal(0, committed.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").GetArrayLength());
    await Background(second);
}

static async Task BackgroundDuringRegistrationRetainsPendingIntent()
{
    var registrationStarted = NewSignal();
    var cancellationObserved = NewSignal();
    var fake = new FakeAppLibraryHost
    {
        RunningObservation = new([
            new("saved-portable", "Portable app", WidgetAppLibraryKind.Application,
                "Portable"),
        ], "running-revision"),
        ConfirmRunningHandler = _ => null,
    };
    fake.RegisterRunningHandler = async (_, cancellationToken) =>
    {
        registrationStarted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Registration should have been canceled.");
        }
        catch (OperationCanceledException)
        {
            cancellationObserved.TrySetResult();
            throw;
        }
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await widget.OnActionAsync(new("games.open-running", "games.open-running"));
    await WaitUntil(() => widget.Page == GamesAppsPage.Running &&
                          widget.ViewState == GamesAppsViewState.Ready);
    var tile = ActionSurfaces(Snapshot(widget, 921).Root).Single(candidate =>
        candidate.ActionId == "games.toggle-curation");
    var add = widget.OnActionAsync(new("games.toggle-curation", tile.Id)).AsTask();
    await registrationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Background(widget);
    await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await add.WaitAsync(TimeSpan.FromSeconds(2));
    using var retained = System.Text.Json.JsonDocument.Parse(fake.PrivateState.Json!);
    Assert.SequenceEqual(["saved-portable"], retained.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").EnumerateArray()
        .Select(item => item.GetString()!));
    Assert.Equal(0, retained.RootElement.GetProperty("SavedIds").GetArrayLength());
}

static async Task PortableRemovalForgetsBeforeStateRemoval()
{
    var item = InstalledItem(
        "portable-current", "saved-portable", "Portable app",
        WidgetAppLibraryKind.Application, "source-portable", "Portable");
    var state = RunningRegistrationState(item.SavedId, pending: false, item);
    var fake = new FakeAppLibraryHost { PrivateState = state };
    fake.ResolveHandler = (request, _) => ValueTask.FromResult(
        new ResolveSavedWidgetAppLibraryItemsResponse(
            request.SavedIds.Contains(item.SavedId, StringComparer.Ordinal) ? [item] : []));
    fake.ForgetRunningHandler = (request, _) =>
    {
        using var persisted = System.Text.Json.JsonDocument.Parse(state.Json!);
        Assert.True(persisted.RootElement.GetProperty("SavedIds").EnumerateArray()
            .Any(candidate => candidate.GetString() == request.SavedId));
        return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Count == 1);
    var tile = ActionSurfaces(Snapshot(widget, 922).Root).Single(candidate =>
        candidate.ActionId == "games.launch");
    await widget.OnActionAsync(new("games.remove", tile.Id));

    Assert.SequenceEqual([item.SavedId],
        fake.ForgottenRunningApps.Select(request => request.SavedId));
    using var removed = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.Equal(0, removed.RootElement.GetProperty("SavedIds").GetArrayLength());
    Assert.Equal(0, removed.RootElement
        .GetProperty("RunningRegistrationSavedIds").GetArrayLength());
    await Background(widget);
}

static async Task DeniedPortableRemovalRetainsState()
{
    var item = InstalledItem(
        "portable-current", "saved-portable", "Portable app",
        WidgetAppLibraryKind.Application, "source-portable", "Portable");
    var state = RunningRegistrationState(item.SavedId, pending: false, item);
    var fake = RegisteredPortableLibraryHost(state, item);
    fake.ForgetRunningHandler = (_, _) =>
        ValueTask.FromException<WidgetCapabilityAcknowledgement>(
            new WidgetCapabilityException("permission_denied", "denied"));
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Count == 1);
    var tile = ActionSurfaces(Snapshot(widget, 926).Root).Single(candidate =>
        candidate.ActionId == "games.launch");
    await widget.OnActionAsync(new("games.remove", tile.Id));

    using var retained = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.SequenceEqual([item.SavedId], retained.RootElement
        .GetProperty("SavedIds").EnumerateArray().Select(value => value.GetString()!));
    Assert.SequenceEqual([item.SavedId], retained.RootElement
        .GetProperty("RunningRegistrationSavedIds").EnumerateArray()
        .Select(value => value.GetString()!));
    Assert.SequenceEqual([item.SavedId], retained.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").EnumerateArray()
        .Select(value => value.GetString()!));
    Assert.True(ActionSurfaces(Snapshot(widget, 927).Root).Single(candidate =>
        candidate.ActionId == "games.launch").IsDisabled is not true);
    await Background(widget);
}

static async Task PortableRemovalSaveFailureWithdrawsLaunch()
{
    var item = InstalledItem(
        "portable-current", "saved-portable", "Portable app",
        WidgetAppLibraryKind.Application, "source-portable", "Portable");
    var state = RunningRegistrationState(
        item.SavedId, pending: false, item, revision: long.MaxValue - 1);
    var fake = RegisteredPortableLibraryHost(state, item);
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Count == 1);
    var tile = ActionSurfaces(Snapshot(widget, 928).Root).Single(candidate =>
        candidate.ActionId == "games.launch");
    await widget.OnActionAsync(new("games.remove", tile.Id));

    Assert.Equal(1, fake.ForgottenRunningApps.Count);
    using var retained = System.Text.Json.JsonDocument.Parse(state.Json!);
    Assert.SequenceEqual([item.SavedId], retained.RootElement
        .GetProperty("SavedIds").EnumerateArray().Select(value => value.GetString()!));
    Assert.SequenceEqual([item.SavedId], retained.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").EnumerateArray()
        .Select(value => value.GetString()!));
    var cleanup = ActionSurfaces(Snapshot(widget, 929).Root).Single(candidate =>
        candidate.ActionId == "games.launch");
    Assert.True(cleanup.IsDisabled is true);
    Assert.True(cleanup.AccessibilityLabel?.Contains(
        "Checking availability", StringComparison.Ordinal) == true);
    await Background(widget);

    var recoveryState = new WidgetTestPrivateState(state.Json!, 1);
    var recoveryHost = RegisteredPortableLibraryHost(recoveryState, item);
    var recovered = Create(recoveryHost);
    await Interactive(recovered);
    await WaitUntil(() => recovered.ViewState == GamesAppsViewState.Ready &&
                          recoveryHost.ForgottenRunningApps.Count == 1);
    using var cleaned = System.Text.Json.JsonDocument.Parse(recoveryState.Json!);
    Assert.Equal(0, cleaned.RootElement.GetProperty("SavedIds").GetArrayLength());
    Assert.Equal(0, cleaned.RootElement
        .GetProperty("RunningRegistrationSavedIds").GetArrayLength());
    Assert.Equal(0, cleaned.RootElement
        .GetProperty("PendingRunningRegistrationSavedIds").GetArrayLength());
    await Background(recovered);
}

static async Task MalformedRunningConfirmationPreservesLibrary()
{
    var state = new WidgetTestPrivateState();
    var fake = new FakeAppLibraryHost
    {
        PrivateState = state,
        Pages =
        {
            [0] = Page([App("neighbor", "Neighbor", WidgetAppLibraryKind.Game)], null),
        },
        RunningObservation = new([
            new("saved-running", "Visible app", WidgetAppLibraryKind.Application,
                "Windows"),
        ], "running-revision"),
        ConfirmRunningHandler = request => InstalledItem(
            "bad/app", request.SavedId, "Visible app",
            WidgetAppLibraryKind.Application, "source-windows", "Windows"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    var revision = state.Revision;
    var retained = widget.CuratedItems.Select(item => item.SavedId).ToArray();

    await widget.OnActionAsync(new("games.open-running", "games.open-running"));
    await WaitUntil(() => widget.Page == GamesAppsPage.Running &&
        widget.ViewState == GamesAppsViewState.Ready);
    var tile = ActionSurfaces(Snapshot(widget, 900).Root).Single(candidate =>
        candidate.ActionId == "games.toggle-curation");
    WidgetCapabilityException? failure = null;
    try
    {
        await widget.OnActionAsync(new("games.toggle-curation", tile.Id));
    }
    catch (WidgetCapabilityException exception)
    {
        failure = exception;
    }

    Assert.Equal("malformed_response", failure?.ErrorCode);
    Assert.Equal(revision, state.Revision);
    Assert.SequenceEqual(retained, widget.CuratedItems.Select(item => item.SavedId));
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

    await RefreshCurrentRouteAndWait(widget);
    Assert.Equal(2, fake.PageRequests.Count);
    Assert.Equal(1L, state.Revision);
    Assert.SequenceEqual(["game-a", "game-b"],
        widget.CuratedItems.Select(item => item.AppId));
    Assert.False(Nodes(Snapshot(widget, 201).Root).Any(node => node.Id == "games.toast"));
    await Background(widget);
}

static async Task WindowsPackageGameProjectsTruthfully()
{
    var game = InstalledItem(
        "app-xbox", "saved-xbox", "Package Game", WidgetAppLibraryKind.Game,
        "source-microsoft-games-installed", "Xbox / Microsoft Store");
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([game], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
        widget.CuratedItems.Any(item => item.SavedId == game.SavedId));

    var current = widget.CuratedItems.Single(item => item.SavedId == game.SavedId);
    Assert.Equal(WidgetAppLibraryKind.Game, current.Presentation.Kind);
    Assert.Equal("Xbox / Microsoft Store",
        current.Presentation.Source.DisplayName);
    Assert.True(ActionSurfaces(Snapshot(widget, 910).Root).Any(node =>
        node.AccessibilityLabel?.Contains(
            "Xbox / Microsoft Store", StringComparison.Ordinal) == true));
    await Background(widget);
}

static async Task EpicGameProjectsTruthfully()
{
    var game = InstalledItem(
        "app-epic", "saved-epic", "Epic Game", WidgetAppLibraryKind.Game,
        "source-epic-installed", "Epic");
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([game], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
        widget.CuratedItems.Any(item => item.SavedId == game.SavedId));

    var current = widget.CuratedItems.Single(item => item.SavedId == game.SavedId);
    Assert.Equal("Epic", current.Presentation.Source.DisplayName);
    Assert.True(ActionSurfaces(Snapshot(widget, 911).Root).Any(node =>
        node.AccessibilityLabel?.Contains("Epic", StringComparison.Ordinal) == true));
    await Background(widget);
}

static async Task GogGameProjectsTruthfully()
{
    var game = InstalledItem(
        "app-gog", "saved-gog", "GOG Game", WidgetAppLibraryKind.Game,
        "source-gog-installed", "GOG");
    game = game with
    {
        Presentation = game.Presentation with
        {
            Availability = new(
                WidgetAppLibraryAvailabilityState.Installed,
                false, "play_unavailable"),
            Capabilities = new([]),
        },
    };
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([game], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
        widget.CuratedItems.Any(item => item.SavedId == game.SavedId));

    var current = widget.CuratedItems.Single(item => item.SavedId == game.SavedId);
    Assert.Equal("GOG", current.Presentation.Source.DisplayName);
    var tile = ActionSurfaces(Snapshot(widget, 912).Root).Single(node =>
        node.AccessibilityLabel?.Contains("GOG", StringComparison.Ordinal) == true);
    Assert.True(tile.IsDisabled is true, "Non-launchable GOG row exposed Play.");
    await widget.OnActionAsync(new("games.launch", tile.Id));
    Assert.Equal(0, fake.LaunchedIds.Count);
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
    await RefreshCurrentRouteAndWait(widget);

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
    await RefreshCurrentRouteAndWait(widget);
    Assert.SequenceEqual(["Beta", "Alpha", "Gamma"],
        widget.CuratedItems.Select(item => item.Presentation.DisplayName));
    var absent = Snapshot(widget, 219);
    var staleBeta = ActionSurfaces(absent.Root).Single(tile => TileTitle(tile) == "Beta");
    Assert.True(staleBeta.IsDisabled == true);
    Assert.Equal("Checking availability", Text(absent.Root, staleBeta.Id + ".state").Text);
    Assert.Equal(beta.Id, staleBeta.Id);
    Assert.Equal(beta.Id, absent.InitialFocusId);

    fake.Pages[0] = Page([
        App("fresh-a", "Alpha", WidgetAppLibraryKind.Game) with { SavedId = "saved-a" },
        App("fresh-b", "Beta", WidgetAppLibraryKind.Game) with { SavedId = "saved-b" },
        App("fresh-c", "Gamma", WidgetAppLibraryKind.Game) with { SavedId = "saved-c" },
    ], null);
    await RefreshCurrentRouteAndWait(widget);
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
    await RefreshCurrentRouteAndWait(second);
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
    await RefreshCurrentRouteAndWait(widget);
    Assert.SequenceEqual(["Same title", "Same title"],
        widget.CuratedItems.Select(item => item.Presentation.DisplayName));
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
            ? item.Presentation.DisplayName
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
    fake.ReadHandler = (request, _) =>
    {
        Assert.Equal(WidgetAppLibraryKind.Game, request.Query.Kind);
        return ValueTask.FromResult(Page([], null));
    };
    await RefreshCurrentRouteAndWait(widget);
    Assert.SequenceEqual(["application-now-unknown"],
        widget.CuratedItems.Select(item => item.AppId));
    Assert.SequenceEqual(["saved-game", "saved-application"],
        fake.ResolveRequests[^1]);

    fake.ReadHandler = null;
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
    await RefreshCurrentRouteAndWait(widget);
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
        ResolveHandler = (request, _) => ValueTask.FromResult(
            new ResolveSavedWidgetAppLibraryItemsResponse([
                App("fresh-game", "Game") with
                {
                    SavedId = request.SavedIds.Single(),
                },
            ])),
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
        .StyleClasses.Contains("wrail-toast--warning", StringComparer.Ordinal));
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

static async Task EmptyCatalogRemainsSelected()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await widget.OnActionAsync(new("games.open-catalog", "games.open-catalog"));
    await WaitUntil(() => widget.Page == GamesAppsPage.Catalog &&
        widget.ViewState == GamesAppsViewState.Ready && fake.PageRequests.Count == 2);

    var snapshot = Snapshot(widget, 2);
    Assert.Equal("games-apps", snapshot.ActiveInputScopeId);
    Assert.True(Buttons(snapshot.Root).Any(button =>
        button.ActionId == "games.refresh-catalog"));
    Assert.True(Nodes(snapshot.Root).Any(node => node.Id == "games.catalog.empty"));
    Assert.Contains("No applications", Text(snapshot.Root, "games.catalog.empty.title").Text!);
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
        .StyleClasses.Contains("wrail-card", StringComparer.Ordinal));
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
    Assert.True(emptySurface.StyleClasses.Contains("wrail-empty-state", StringComparer.Ordinal));
    var add = Buttons(emptySurface).Single(button => button.ActionId == "games.open-catalog");
    Assert.Equal(add.Id, empty.InitialFocusId);
    Assert.Equal("Add apps", add.Text);
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
    Assert.True(alert.StyleClasses.Contains("wrail-alert", StringComparer.Ordinal));
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
    Assert.Equal(600d, snapshot.Surface.PreferredHeight);
    Assert.Equal(300d, snapshot.Surface.MinimumHeight);
    Assert.True(snapshot.Surface.PreferredWidth > snapshot.Surface.MinimumWidth);
    Assert.True(snapshot.Surface.PreferredHeight > snapshot.Surface.MinimumHeight);
    Assert.Equal("games-apps", snapshot.ActiveInputScopeId);
    Assert.True(Buttons(snapshot.Root).Any(button => button.ActionId == "games.open-catalog"));
    Assert.False(ActionSurfaces(snapshot.Root).Any(tile => tile.ActionId == "games.launch"));

    await OpenCatalog(widget);
    snapshot = Snapshot(widget, 4);
    Assert.Equal(600d, snapshot.Surface!.PreferredHeight);
    Assert.Equal(300d, snapshot.Surface.MinimumHeight);
    Assert.Equal("games-apps", snapshot.ActiveInputScopeId);
    Assert.True(snapshot.Root.Shortcuts.Any(shortcut =>
        shortcut.Button == ControllerButton.LeftBumper &&
        shortcut.ActionId == "games.section.previous"));
    Assert.True(snapshot.Root.Shortcuts.Any(shortcut =>
        shortcut.Button == ControllerButton.RightBumper &&
        shortcut.ActionId == "games.section.next"));
    var scroll = Nodes(snapshot.Root).Single(node => node.Id == "games.catalog.scroll");
    Assert.Equal(ViewNodeKind.Scroll, scroll.Kind);
    Assert.Equal(ScrollAxis.Vertical, scroll.ScrollAxis);
    var tiles = ActionSurfaces(scroll)
        .Where(tile => tile.ActionId == "games.toggle-curation").ToArray();
    Assert.Equal(3, tiles.Length);
    Assert.True(tiles.All(tile => tile.IsFocusable));
    Assert.Equal("games.catalog.page", snapshot.FocusGroupEntryRequest!.GroupId);
    Assert.Equal(tiles[0].Id,
        Nodes(snapshot.Root).Single(node => node.Id == "games.catalog.page")
            .InitialChildFocusId);
    Assert.Equal<string?>(null, snapshot.InitialFocusId);
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
    Assert.Equal("games.library.page", snapshot.FocusGroupEntryRequest!.GroupId);
    Assert.Equal(launch.Id,
        Nodes(snapshot.Root).Single(node => node.Id == "games.library.page")
            .InitialChildFocusId);
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
    Assert.Equal(ProtocolConstants.ControllerShortcutLabelVersion, snapshot.ProtocolVersion);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task ArtworkSurvivesRefreshAndWarmRestart()
{
    const string gameHandleOne = "library.art.11111111111111111111111111111111";
    const string appHandleOne = "library.art.22222222222222222222222222222222";
    const string gameHandleTwo = "library.art.33333333333333333333333333333333";
    const string appHandleTwo = "library.art.44444444444444444444444444444444";
    var current = new[]
    {
        App("game", "Trusted Game", WidgetAppLibraryKind.Game, gameHandleOne),
        App("application", "Explicit Application", artworkHandle: appHandleOne),
        App("no-art", "No Artwork", WidgetAppLibraryKind.Game),
    };
    var privateState = ProjectedState(
        selectedSavedId: "saved-game",
        ("saved-game", "Trusted Game", WidgetAppLibraryKind.Game),
        ("saved-application", "Explicit Application", WidgetAppLibraryKind.Application),
        ("saved-no-art", "No Artwork", WidgetAppLibraryKind.Game));
    var fake = new FakeAppLibraryHost
    {
        PrivateState = privateState,
        Pages = { [0] = Page(current, null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.CuratedItems.Count == 3 &&
                          widget.CuratedItems.All(item =>
                              !item.AppId.StartsWith("pending.", StringComparison.Ordinal)));

    AssertArtwork(Snapshot(widget, 640), "Trusted Game", gameHandleOne);
    AssertArtwork(Snapshot(widget, 641), "Explicit Application", appHandleOne);
    AssertFallback(Snapshot(widget, 642), "No Artwork", disabled: false);

    current =
    [
        App("game-refresh", "Trusted Game", WidgetAppLibraryKind.Game, gameHandleTwo) with
            { SavedId = "saved-game" },
        App("application-refresh", "Explicit Application", artworkHandle: appHandleTwo) with
            { SavedId = "saved-application" },
        App("no-art-refresh", "No Artwork", WidgetAppLibraryKind.Game) with
            { SavedId = "saved-no-art" },
    ];
    fake.Pages[0] = Page(current, null);
    await widget.OnActionAsync(new("games.retry", "games.root"));
    await WaitUntil(() => widget.CuratedItems.Any(item =>
        item.AppId == "game-refresh"));
    AssertArtwork(Snapshot(widget, 643), "Trusted Game", gameHandleTwo);
    AssertArtwork(Snapshot(widget, 644), "Explicit Application", appHandleTwo);
    AssertFallback(Snapshot(widget, 645), "No Artwork", disabled: false);
    await Background(widget);

    var discovery = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var restartedHost = new FakeAppLibraryHost
    {
        PrivateState = privateState,
        Pages = { [0] = Page(current, null) },
        ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
            discovery.Task.WaitAsync(token)),
    };
    var restarted = Create(restartedHost);
    await Interactive(restarted);
    await WaitUntil(() => restarted.ViewState == GamesAppsViewState.Ready &&
                          restarted.CuratedItems.Count == 3);
    AssertArtwork(Snapshot(restarted, 646), "Trusted Game", gameHandleTwo);
    AssertArtwork(Snapshot(restarted, 647), "Explicit Application", appHandleTwo);
    AssertFallback(Snapshot(restarted, 648), "No Artwork", disabled: false);

    discovery.SetResult(Page(current, null));
    await WaitUntil(() => restarted.CuratedItems.All(item =>
        !item.AppId.StartsWith("pending.", StringComparison.Ordinal)));
    AssertArtwork(Snapshot(restarted, 649), "Trusted Game", gameHandleTwo);
    AssertArtwork(Snapshot(restarted, 650), "Explicit Application", appHandleTwo);
    AssertFallback(Snapshot(restarted, 651), "No Artwork", disabled: false);
    await Background(restarted);

    static void AssertArtwork(ViewSnapshot snapshot, string title, string handle)
    {
        var tile = ActionSurfaces(snapshot.Root).Single(row => TileTitle(row) == title);
        Assert.True(tile.IsDisabled != true);
        var artwork = Nodes(tile).Single(node => node.Id == tile.Id + ".artwork");
        Assert.Equal(handle, artwork.ArtworkHandle);
        Assert.True(artwork.Glyph is null);
        Assert.Valid(snapshot);
    }

    static void AssertFallback(ViewSnapshot snapshot, string title, bool disabled)
    {
        var tile = ActionSurfaces(snapshot.Root).Single(row => TileTitle(row) == title);
        Assert.Equal(disabled, tile.IsDisabled == true);
        var artwork = Nodes(tile).Single(node => node.Id == tile.Id + ".artwork");
        Assert.True(artwork.ArtworkHandle is null);
        Assert.Equal(WidgetGlyph.Play, artwork.Glyph);
        Assert.Valid(snapshot);
    }
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
        await AddFromOpenCatalog(widget, app.Presentation.DisplayName);
    await BackToLibrary(widget);

    var snapshot = Snapshot(widget, 63);
    Assert.Equal(600d, snapshot.Surface!.PreferredHeight);
    Assert.Equal(300d, snapshot.Surface.MinimumHeight);
    var scroll = Nodes(snapshot.Root).Single(node => node.Id == "games.library.scroll");
    var grid = Nodes(scroll).Single(node => node.Id == "games.library.grid");
    Assert.Equal(ViewNodeKind.Grid, grid.Kind);
    var launches = ActionSurfaces(scroll).Where(tile => tile.ActionId == "games.launch").ToArray();
    Assert.Equal(8, launches.Length);
    Assert.True(launches.All(launch => Nodes(launch).Single(node =>
        node.Id == launch.Id + ".artwork").Glyph == WidgetGlyph.Play));
    Assert.True(launches.All(launch => launch.IsFocusable));
    Assert.Equal("games.library.page", snapshot.FocusGroupEntryRequest!.GroupId);
    Assert.Equal(launches[^1].Id,
        Nodes(snapshot.Root).Single(node => node.Id == "games.library.page")
            .InitialChildFocusId);
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
    Assert.True(launches.All(tile => tile.IsFocusable));
    Assert.Equal(launches[0].Id,
        Nodes(snapshot.Root).Single(node => node.Id == "games.library.page")
            .InitialChildFocusId);
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
    Assert.Equal(alpha.Id,
        Nodes(feedback.Root).Single(node => node.Id == "games.catalog.page")
            .InitialChildFocusId);
    Assert.Contains("Added Alpha", Text(feedback.Root, "games.toast.message").Text!);

    await Background(widget);
    Assert.False(Nodes(Snapshot(widget, 67).Root).Any(node => node.Id == "games.toast"));
}

static async Task ToastFeedbackExpiryIsLatestWins()
{
    var clock = new ManualTimerTimeProvider();
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([
            App("opaque-a", "Alpha"), App("opaque-b", "Beta"),
            App("opaque-c", "Gamma")], null) },
    };
    var widget = CreateWithTimeProvider(fake, clock);
    await Interactive(widget);
    var addAlpha = AddFromCatalog(widget, "Alpha");
    await WaitUntil(() => widget.Page == GamesAppsPage.Catalog &&
        widget.ViewState == GamesAppsViewState.Loading);
    clock.Advance(TimeSpan.FromSeconds(1));
    await addAlpha;
    clock.Advance(TimeSpan.FromSeconds(4));
    await AddFromCatalog(widget, "Beta");

    clock.Advance(TimeSpan.FromSeconds(1));
    await Task.Yield();
    Assert.Contains("Added Beta", Text(Snapshot(widget, 67_001).Root,
        "games.toast.message").Text!);

    var stateGate = typeof(GamesAppsWidget).GetField("_gate",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!.GetValue(widget)!;
    var expiry = (WidgetTimedMutation)typeof(GamesAppsWidget).GetField(
        "_toastExpiry", System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!.GetValue(widget)!;
    var showToast = typeof(GamesAppsWidget).GetMethod("ShowToast",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!;
    Monitor.Enter(stateGate);
    try
    {
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.True(SpinWait.SpinUntil(() => !expiry.IsScheduled, 1_000));
        showToast.Invoke(widget,
            ["Replacement", "Replacement survived overlap", ToastTone.Success]);
    }
    finally
    {
        Monitor.Exit(stateGate);
    }
    await Task.Yield();
    Assert.Contains("Replacement survived overlap", Text(Snapshot(widget, 67_002).Root,
        "games.toast.message").Text!);

    clock.Advance(TimeSpan.FromSeconds(5));
    await WaitUntil(() => !Nodes(Snapshot(widget, 67_002).Root)
        .Any(node => node.Id == "games.toast"));

    await AddFromCatalog(widget, "Gamma");
    await widget.OnActionAsync(new(
        "games.refresh-catalog", "games.refresh-catalog"));
    Assert.False(Nodes(Snapshot(widget, 67_003).Root)
        .Any(node => node.Id == "games.toast"));
    var invalidations = 0;
    widget.Invalidated += (_, _) => invalidations++;
    clock.Advance(TimeSpan.FromSeconds(10));
    await Task.Yield();
    Assert.Equal(0, invalidations);
    await Background(widget);
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

    await BackToLibrary(widget);
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
        .All(tile => tile.IsDisabled != true));
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
    var failedMutation = Snapshot(widget, 349);
    Assert.Contains("durable library could not be updated",
        Text(failedMutation.Root, "games.toast.message").Text!);
    await BackToLibrary(widget);

    var rolledBack = AssertReadyLibrary(widget, 350, "Alpha", "Beta");
    Assert.Equal(beforeJson, state.Json);
    Assert.True(ActionSurfaces(rolledBack.Root)
        .Where(tile => tile.ActionId == "games.launch")
        .All(tile => tile.IsDisabled != true));
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
    Assert.Equal("opaque-c", widget.SelectedAppId);
    Assert.Equal(ActionSurfaces(library.Root).Single(tile =>
        TileTitle(tile) == "Gamma").Id,
        Nodes(library.Root).Single(node => node.Id == "games.library.page")
            .InitialChildFocusId);
    var beta = ActionSurfaces(library.Root)
        .Single(tile => TileTitle(tile) == "Beta");
    await widget.OnActionAsync(new("games.remove", beta.Id));
    var afterBeta = Snapshot(widget, 235);
    Assert.Equal("opaque-c", widget.SelectedAppId);
    Assert.Equal(ActionSurfaces(afterBeta.Root).Single(tile =>
        TileTitle(tile) == "Gamma").Id,
        Nodes(afterBeta.Root).Single(node => node.Id == "games.library.page")
            .InitialChildFocusId);

    var gamma = ActionSurfaces(afterBeta.Root).Single(tile => TileTitle(tile) == "Gamma");
    await widget.OnActionAsync(new("games.remove", gamma.Id));
    var afterGamma = Snapshot(widget, 236);
    Assert.Equal("opaque-a", widget.SelectedAppId);
    Assert.Equal(ActionSurfaces(afterGamma.Root).Single(tile =>
        TileTitle(tile) == "Alpha").Id,
        Nodes(afterGamma.Root).Single(node => node.Id == "games.library.page")
            .InitialChildFocusId);
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
    Assert.Contains("Opened Beta", Text(opened.Root, "games.toast.message").Text!);
    await Background(widget);
}

static async Task LaunchRevalidatesRotatedAppId()
{
    var original = App("opaque-old", "Alpha") with { SavedId = "saved-alpha" };
    var current = original with { AppId = "opaque-current" };
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([original], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await AddFromCatalog(widget, "Alpha");
    await BackToLibrary(widget);
    fake.ResolveHandler = (request, _) => ValueTask.FromResult(
        new ResolveSavedWidgetAppLibraryItemsResponse(
            request.SavedIds.Contains("saved-alpha", StringComparer.Ordinal)
                ? [current]
                : []));

    var alpha = ActionSurfaces(Snapshot(widget, 240).Root)
        .Single(tile => TileTitle(tile) == "Alpha");
    var resolvesBeforeLaunch = fake.ResolveRequests.Count;
    await widget.OnActionAsync(new("games.launch", alpha.Id));

    Assert.Equal(resolvesBeforeLaunch + 1, fake.ResolveRequests.Count);
    Assert.SequenceEqual(["saved-alpha"], fake.ResolveRequests[^1]);
    Assert.SequenceEqual(["opaque-current"], fake.LaunchedIds);
    Assert.False(fake.LaunchedIds.Contains("opaque-old", StringComparer.Ordinal));
    await Background(widget);
}

static async Task UnavailableAndStaleNeverLaunch()
{
    foreach (var state in new[]
             {
                 WidgetAppLibraryAvailabilityState.Unavailable,
                 WidgetAppLibraryAvailabilityState.StaleSource,
             })
    {
        var original = App("opaque-old", "Alpha") with { SavedId = "saved-alpha" };
        var fake = new FakeAppLibraryHost
        {
            Pages = { [0] = Page([original], null) },
        };
        var widget = Create(fake);
        await Interactive(widget);
        await AddFromCatalog(widget, "Alpha");
        await BackToLibrary(widget);
        fake.ResolveHandler = (request, _) => ValueTask.FromResult(
            new ResolveSavedWidgetAppLibraryItemsResponse([
                original with
                {
                    AppId = "opaque-current",
                    Presentation = original.Presentation with
                    {
                        Availability = new(state, false,
                            state == WidgetAppLibraryAvailabilityState.Unavailable
                                ? "unavailable" : "stale"),
                        Capabilities = new([]),
                    },
                },
            ]));

        var alpha = ActionSurfaces(Snapshot(widget, 241).Root)
            .Single(tile => TileTitle(tile) == "Alpha");
        await widget.OnActionAsync(new("games.launch", alpha.Id));

        Assert.Equal(0, fake.LaunchedIds.Count);
        await Background(widget);
    }
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
    using (var persisted = System.Text.Json.JsonDocument.Parse(fake.PrivateState.Json!))
        Assert.SequenceEqual(["saved-opaque-b", "saved-opaque-a"],
            persisted.RootElement.GetProperty("SavedIds").EnumerateArray()
                .Select(item => item.GetString()!));
    var reordered = Snapshot(widget, 46);
    Assert.Equal(beta.Id,
        Nodes(reordered.Root).Single(node => node.Id == "games.library.page")
            .InitialChildFocusId);
    await Background(widget);
}

static async Task LaunchRecencySurvivesBackground()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha"), App("opaque-b", "Beta")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await OpenCatalog(widget);
    await AddFromOpenCatalog(widget, "Alpha");
    await AddFromOpenCatalog(widget, "Beta");
    await BackToLibrary(widget);
    var beta = ActionSurfaces(Snapshot(widget, 50).Root).Single(tile => TileTitle(tile) == "Beta");
    fake.LaunchException = new WidgetCapabilityException("launch_cancelled", "Cancelled by user");
    await widget.OnActionAsync(new("games.launch", beta.Id));
    Assert.SequenceEqual(["opaque-a", "opaque-b"], widget.CuratedItems.Select(item => item.AppId));
    fake.LaunchException = null;
    using var action = new CancellationTokenSource();
    fake.LaunchHandler = async (_, token) =>
    {
        await Background(widget);
        action.Cancel(); // The native foreground close retires the active action.
        token.ThrowIfCancellationRequested();
        return new WidgetCapabilityAcknowledgement(true);
    };
    await widget.OnActionAsync(new("games.launch", beta.Id), action.Token);
    using var saved = System.Text.Json.JsonDocument.Parse(fake.PrivateState.Json!);
    Assert.SequenceEqual(["saved-opaque-b", "saved-opaque-a"],
        saved.RootElement.GetProperty("SavedIds").EnumerateArray().Select(item => item.GetString()!));
    var restarted = Create(fake);
    await Interactive(restarted);
    await WaitUntil(() => restarted.ViewState == GamesAppsViewState.Ready);
    Assert.SequenceEqual(["opaque-b", "opaque-a"], restarted.CuratedItems.Select(item => item.AppId));
    await Background(restarted);
}

static async Task LaunchSaveFailureIsTruthful()
{
    var alpha = App("opaque-a", "Alpha");
    var beta = App("opaque-b", "Beta");
    var seeded = ProjectedState(
        alpha.SavedId,
        (alpha.SavedId, "Alpha", WidgetAppLibraryKind.Application),
        (beta.SavedId, "Beta", WidgetAppLibraryKind.Application));
    var exhausted = new WidgetTestPrivateState(seeded.Json, long.MaxValue);
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([alpha, beta], null) },
        PrivateState = exhausted,
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Count == 2);

    var betaTile = ActionSurfaces(Snapshot(widget, 420).Root)
        .Single(tile => TileTitle(tile) == "Beta");
    var failed = false;
    try { await widget.OnActionAsync(new("games.launch", betaTile.Id)); }
    catch (Exception) { failed = true; }
    Assert.True(failed,
        "A launched-but-unsaved action did not reject automatic close completion.");

    Assert.SequenceEqual(["opaque-a", "opaque-b"],
        widget.CuratedItems.Select(item => item.AppId));
    Assert.SequenceEqual(["opaque-b", "opaque-a"],
        widget.Items.Select(item => item.AppId));
    Assert.SequenceEqual(["opaque-b"], fake.LaunchedIds);
    Assert.Equal(long.MaxValue, exhausted.Revision);
    var warning = Snapshot(widget, 421);
    Assert.SequenceEqual(["Beta", "Alpha"], ActionSurfaces(warning.Root)
        .Where(tile => tile.ActionId == "games.launch")
        .Select(tile => TileTitle(tile)!));
    using (var durable = System.Text.Json.JsonDocument.Parse(exhausted.Json!))
        Assert.SequenceEqual(["saved-opaque-a", "saved-opaque-b"],
            durable.RootElement.GetProperty("SavedIds").EnumerateArray()
                .Select(item => item.GetString()!));
    Assert.Contains("Opened Beta, but recent order was not saved",
        Text(warning.Root, "games.toast.message").Text!);
    Assert.True(Nodes(warning.Root).Single(node => node.Id == "games.toast")
        .StyleClasses.Contains("wrail-toast--warning", StringComparer.Ordinal));
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
    var alpha = ActionSurfaces(failed.Root).Single(tile => TileTitle(tile) == "Alpha");
    var selectedBeta = ActionSurfaces(failed.Root).Single(tile => TileTitle(tile) == "Beta");
    Assert.Equal("opaque-b", widget.SelectedAppId);
    Assert.True(alpha.IsDisabled != true);
    Assert.True(selectedBeta.IsSelected != true);
    Assert.Equal(selectedBeta.Id,
        Nodes(failed.Root).Single(node => node.Id == "games.library.page")
            .InitialChildFocusId);
    Assert.Contains("App library unavailable",
        Text(failed.Root, "games.toast.message").Text!);
    Assert.True(Nodes(failed.Root).Single(node => node.Id == "games.toast")
        .StyleClasses.Contains("wrail-toast--danger", StringComparer.Ordinal));
    await Background(widget);
}

static async Task ElevationLaunchOutcomesAreExplained()
{
    foreach (var (code, expected) in new[]
    {
        ("elevation_cancelled", "Administrator approval was canceled"),
        ("elevation_required", "requires administrator approval"),
    })
    {
        var fake = new FakeAppLibraryHost
        {
            Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
            LaunchException = new WidgetCapabilityException(code, "private elevation detail"),
        };
        var widget = Create(fake);
        await Interactive(widget);
        await AddFromCatalog(widget, "Alpha");
        await BackToLibrary(widget);
        var alpha = ActionSurfaces(Snapshot(widget, 49).Root)
            .Single(tile => TileTitle(tile) == "Alpha");

        await widget.OnActionAsync(new("games.launch", alpha.Id));

        var failed = Snapshot(widget, 50);
        Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
        Assert.Equal(1, widget.Items.Count);
        Assert.Contains(expected, Text(failed.Root, "games.toast.message").Text!);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(failed)
            .Contains("private elevation detail", StringComparison.Ordinal));
        await Background(widget);
    }
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

static async Task InitialActivationReconcilesOnce()
{
    const string artworkHandle = "library.art.55555555555555555555555555555555";
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha", artworkHandle: artworkHandle)], null) },
        PrivateState = SavedState("saved-opaque-a"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Any(item => item.AppId == "opaque-a"));

    Assert.Equal(1, fake.CatalogRequests.Count);
    Assert.False(fake.CatalogRequests[0].Refresh);
    Assert.Equal(1, fake.ResolveRequests.Count);
    Assert.SequenceEqual(["saved-opaque-a"], fake.ResolveRequests[0]);
    var ready = Snapshot(widget, 51);
    var tile = ActionSurfaces(ready.Root).Single(row => TileTitle(row) == "Alpha");
    Assert.True(tile.IsDisabled != true);
    Assert.False(Nodes(ready.Root).Any(node => node.Text?.Contains(
        "Checking", StringComparison.Ordinal) == true));
    Assert.Equal(artworkHandle,
        Nodes(tile).Single(node => node.Id == tile.Id + ".artwork").ArtworkHandle);
    Assert.Valid(ready);
    await Background(widget);
}

static async Task PersistedLaterSelectionKeepsFirstEntryFocus()
{
    var fake = new FakeAppLibraryHost
    {
        PrivateState = ProjectedState(
            selectedSavedId: "saved-b",
            ("saved-a", "Alpha", WidgetAppLibraryKind.Application),
            ("saved-b", "Beta", WidgetAppLibraryKind.Application)),
        Pages =
        {
            [0] = Page([
                App("opaque-a", "Alpha") with { SavedId = "saved-a" },
                App("opaque-b", "Beta") with { SavedId = "saved-b" },
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Count == 2 &&
                          widget.CuratedItems.All(item =>
                              !item.AppId.StartsWith("pending.", StringComparison.Ordinal)));

    var restored = Snapshot(widget, 352);
    var alpha = ActionSurfaces(restored.Root).Single(tile => TileTitle(tile) == "Alpha");
    var beta = ActionSurfaces(restored.Root).Single(tile => TileTitle(tile) == "Beta");
    Assert.SequenceEqual(["saved-a", "saved-b"],
        widget.CuratedItems.Select(item => item.SavedId));
    Assert.Equal("opaque-b", widget.SelectedAppId);
    Assert.True(beta.IsSelected != true);
    Assert.Equal(alpha.Id, restored.InitialFocusId);

    await widget.OnActionAsync(new("games.launch", beta.Id));
    Assert.SequenceEqual(["saved-b", "saved-a"],
        widget.CuratedItems.Select(item => item.SavedId));
    Assert.Equal(beta.Id, Snapshot(widget, 353).InitialFocusId);
    Assert.SequenceEqual(["opaque-b"], fake.LaunchedIds);
    await Background(widget);
}

static async Task ReactivationReusesCachedLibrary()
{
    const string artworkHandle = "library.art.66666666666666666666666666666666";
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha", artworkHandle: artworkHandle)], null) },
        PrivateState = SavedState("saved-opaque-a"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Any(item => item.AppId == "opaque-a"));
    var before = Snapshot(widget, 52);
    var beforeTile = ActionSurfaces(before.Root).Single(row => TileTitle(row) == "Alpha");
    var focusBefore = before.InitialFocusId;
    var selectedBefore = widget.SelectedAppId;
    var pageRequestsBefore = fake.CatalogRequests.Count;
    var resolvesBefore = fake.ResolveRequests.Count;
    await Background(widget);

    fake.ReadHandler = (_, _) => throw new InvalidOperationException(
        "Ordinary reactivation must not query the catalog.");
    fake.ResolveHandler = (_, _) => throw new InvalidOperationException(
        "Ordinary reactivation must not resolve the saved list.");
    var invalidations = 0;
    widget.Invalidated += (_, _) => invalidations++;
    await Interactive(widget);
    var retained = Snapshot(widget, 53);
    var retainedTile = ActionSurfaces(retained.Root).Single(row => TileTitle(row) == "Alpha");
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    Assert.Equal(focusBefore, retained.InitialFocusId);
    Assert.Equal(selectedBefore, widget.SelectedAppId);
    Assert.Equal(beforeTile.Id, retainedTile.Id);
    Assert.True(retainedTile.IsDisabled != true);
    Assert.False(Nodes(retained.Root).Any(node => node.Text?.Contains(
        "Checking", StringComparison.Ordinal) == true));
    Assert.Equal(artworkHandle,
        Nodes(retainedTile).Single(node => node.Id == retainedTile.Id + ".artwork")
            .ArtworkHandle);
    Assert.Equal(pageRequestsBefore, fake.CatalogRequests.Count);
    Assert.Equal(resolvesBefore, fake.ResolveRequests.Count);
    Assert.Equal(1, invalidations);
    Assert.Valid(retained);
    await Background(widget);
}

static async Task ExplicitRefreshReconcilesCachedLibrary()
{
    var refreshStarted = NewSignal();
    var refreshPage = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    const string artworkOne = "library.art.77777777777777777777777777777777";
    const string artworkTwo = "library.art.88888888888888888888888888888888";
    var original = App("opaque-a", "Alpha", artworkHandle: artworkOne);
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([original], null) },
        PrivateState = SavedState("saved-opaque-a"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready &&
                          widget.CuratedItems.Any(item => item.AppId == "opaque-a"));
    var before = Snapshot(widget, 64);
    var focusBefore = before.InitialFocusId;
    var initialCatalogRequests = fake.CatalogRequests.Count;
    Assert.Equal(1, initialCatalogRequests);
    Assert.False(fake.CatalogRequests[0].Refresh);

    fake.ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
        AwaitPage(refreshPage.Task, refreshStarted, token));
    fake.ResolveException = new WidgetCapabilityException(
        "platform_unavailable", "fixture refresh failure");
    var refresh = widget.OnActionAsync(new(
        "games.refresh-catalog", "games.refresh-catalog")).AsTask();
    await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var during = AssertReadyLibrary(widget, 350, "Alpha");
    Assert.False(Nodes(during.Root).Any(node =>
        node.Kind == ViewNodeKind.LoadingIndicator));
    var refreshButton = Buttons(during.Root).Single(button =>
        button.ActionId == "games.refresh-catalog");
    Assert.True(refreshButton.IsBusy == true);
    Assert.True(refreshButton.IsDisabled == true);
    var duringTile = ActionSurfaces(during.Root).Single(row => TileTitle(row) == "Alpha");
    Assert.True(duringTile.IsDisabled != true);
    Assert.Equal(focusBefore, during.InitialFocusId);
    Assert.Equal(artworkOne,
        Nodes(duringTile).Single(node => node.Id == duringTile.Id + ".artwork")
            .ArtworkHandle);
    Assert.Equal(initialCatalogRequests + 1, fake.CatalogRequests.Count);
    Assert.True(fake.CatalogRequests[^1].Refresh);

    refreshPage.SetException(new WidgetCapabilityException(
        "platform_unavailable", "fixture catalog failure"));
    await refresh;
    await WaitForCurrentRouteRefresh(widget);
    var failed = AssertReadyLibrary(widget, 351, "Alpha");
    var failedTile = ActionSurfaces(failed.Root).Single(row => TileTitle(row) == "Alpha");
    Assert.Equal(focusBefore, failed.InitialFocusId);
    Assert.True(failedTile.IsDisabled != true);
    Assert.Equal(artworkOne,
        Nodes(failedTile).Single(node => node.Id == failedTile.Id + ".artwork")
            .ArtworkHandle);

    var updated = original with
    {
        AppId = "opaque-current",
        Presentation = original.Presentation with
        {
            Artwork = new([
                new WidgetAppLibraryArtwork(
                    WidgetAppLibraryArtworkRole.Tile, artworkTwo, "updated",
                    WidgetAppLibraryArtworkFallback.Application),
            ]),
        },
    };
    fake.Pages[0] = Page([updated], null);
    fake.ResolveException = null;
    fake.ReadHandler = (_, _) => ValueTask.FromResult(Page([updated], null));
    await RefreshCurrentRouteAndWait(
        widget, "games.refresh-catalog", "games.refresh-catalog");

    Assert.Equal(initialCatalogRequests + 2, fake.CatalogRequests.Count);
    Assert.True(fake.CatalogRequests.Skip(1).All(request => request.Refresh));
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    var recovered = Snapshot(widget, 65);
    var recoveredTile = ActionSurfaces(recovered.Root)
        .Single(tile => tile.ActionId == "games.launch" && TileTitle(tile) == "Alpha");
    Assert.Equal(focusBefore, recovered.InitialFocusId);
    Assert.True(recoveredTile.IsDisabled != true);
    Assert.Equal(artworkTwo,
        Nodes(recoveredTile).Single(node => node.Id == recoveredTile.Id + ".artwork")
            .ArtworkHandle);
    Assert.Valid(recovered);
    await Background(widget);
}

static async Task CrossRouteLoadingIsBounded()
{
    var clock = new ManualTimerTimeProvider();
    var started = NewSignal();
    var completion = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("library", "Library app")], null) },
    };
    var widget = CreateWithTimeProvider(fake, clock);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    fake.ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
        AwaitPage(completion.Task, started, token));

    var library = Snapshot(widget, 600);
    var catalogHeader = CompactDestination(library, "games.open-catalog");
    await widget.OnActionAsync(new(catalogHeader.ActionId!, catalogHeader.Id));
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var loading = Snapshot(widget, 601);
    Assert.Equal(GamesAppsPage.Catalog, widget.Page);
    Assert.Equal(GamesAppsViewState.Loading, widget.ViewState);
    Assert.Equal("games.catalog.page", loading.FocusGroupEntryRequest!.GroupId);
    Assert.Equal<string?>(null,
        Nodes(loading.Root).Single(node => node.Id == "games.catalog.page")
            .InitialChildFocusId);
    var progress = Nodes(loading.Root).Single(node => node.Id == "games.route.progress");
    var indicator = Nodes(progress).Single(node => node.Id == "games.route.loading");
    Assert.Equal(ViewNodeKind.LoadingIndicator, indicator.Kind);
    Assert.Equal(LoadingIndicatorSize.Large, indicator.IndicatorSize);
    Assert.Equal("Loading applications", indicator.AccessibilityLabel);
    Assert.False(Nodes(progress).Any(node => node.Text is not null));
    Assert.False(Nodes(progress).Any(node => node.IsFocusable));

    completion.SetResult(Page([App("catalog", "Catalog app")], null));
    await Task.Yield();
    Assert.Equal(GamesAppsViewState.Loading, widget.ViewState);
    clock.Advance(TimeSpan.FromMilliseconds(999));
    await Task.Yield();
    Assert.Equal(GamesAppsViewState.Loading, widget.ViewState);
    clock.Advance(TimeSpan.FromMilliseconds(1));
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    var ready = Snapshot(widget, 602);
    var catalogTile = ActionSurfaces(ready.Root).Single(tile =>
        TileTitle(tile) == "Catalog app");
    Assert.Equal("games.catalog.page", ready.FocusGroupEntryRequest!.GroupId);
    Assert.Equal(catalogTile.Id,
        Nodes(ready.Root).Single(node => node.Id == "games.catalog.page")
            .InitialChildFocusId);
    Assert.False(Nodes(ready.Root).Any(node => node.Id == "games.route.loading"));
    Assert.Valid(ready);
    await Background(widget);
}

static async Task CrossRouteFailureIsBounded()
{
    var clock = new ManualTimerTimeProvider();
    var started = NewSignal();
    var completion = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("library", "Library app")], null) },
    };
    var widget = CreateWithTimeProvider(fake, clock);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    fake.ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
        AwaitPage(completion.Task, started, token));

    await widget.OnActionAsync(new("games.open-catalog", "games.open-catalog"));
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    completion.SetException(new WidgetCapabilityException(
        "platform_unavailable", "private provider detail"));
    await Task.Yield();
    Assert.Equal(GamesAppsViewState.Loading, widget.ViewState);
    clock.Advance(TimeSpan.FromMilliseconds(999));
    await Task.Yield();
    Assert.Equal(GamesAppsViewState.Loading, widget.ViewState);
    clock.Advance(TimeSpan.FromMilliseconds(1));
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    var failed = Snapshot(widget, 603);
    Assert.Equal(GamesAppsPage.Catalog, widget.Page);
    Assert.True(Nodes(failed.Root).Any(node => node.Id == "games.catalog.empty"));
    Assert.True(Nodes(failed.Root).Any(node => node.Id == "games.toast"));
    Assert.False(System.Text.Json.JsonSerializer.Serialize(failed)
        .Contains("private provider detail", StringComparison.Ordinal));
    Assert.Valid(failed);
    await Background(widget);
}

static async Task HeaderAndBumperEnterRememberedContent()
{
    var clock = new ManualTimerTimeProvider();
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("catalog", "Catalog app")], null) },
    };
    var widget = CreateWithTimeProvider(fake, clock);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);

    var library = Snapshot(widget, 604);
    var catalogHeader = CompactDestination(library, "games.open-catalog");
    await widget.OnActionAsync(new(catalogHeader.ActionId!, catalogHeader.Id));
    var headerRequest = Snapshot(widget, 605).FocusGroupEntryRequest!;
    clock.Advance(TimeSpan.FromSeconds(1));
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    var headerReady = Snapshot(widget, 606);
    var headerTile = ActionSurfaces(headerReady.Root).Single(tile =>
        TileTitle(tile) == "Catalog app");
    Assert.Equal("games.catalog.page", headerRequest.GroupId);
    Assert.Equal(headerTile.Id,
        Nodes(headerReady.Root).Single(node => node.Id == headerRequest.GroupId)
            .InitialChildFocusId);

    await widget.OnActionAsync(new("games.open-library", "games.open-library"));
    Assert.Equal(GamesAppsPage.Library, widget.Page);
    await widget.OnActionAsync(new("games.section.next", "games.root"));
    var bumperRequest = Snapshot(widget, 607).FocusGroupEntryRequest!;
    Assert.Equal(headerRequest.GroupId, bumperRequest.GroupId);
    Assert.True(bumperRequest.RequestId > headerRequest.RequestId);
    clock.Advance(TimeSpan.FromSeconds(1));
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    var bumperReady = Snapshot(widget, 608);
    Assert.Equal(headerTile.Id,
        Nodes(bumperReady.Root).Single(node => node.Id == bumperRequest.GroupId)
            .InitialChildFocusId);
    Assert.Valid(bumperReady);
    await Background(widget);
}

static async Task RouteSwitchRejectsStaleCompletion()
{
    var clock = new ManualTimerTimeProvider();
    var catalogStarted = NewSignal();
    var catalogCanceled = NewSignal();
    var catalogCompletion = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var runningStarted = NewSignal();
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("library", "Library app")], null) },
        RunningObservation = new([
            new("saved-running", "Running app", WidgetAppLibraryKind.Application,
                "Windows"),
        ], "running-revision"),
    };
    var widget = CreateWithTimeProvider(fake, clock);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    fake.ReadHandler = (_, token) =>
    {
        catalogStarted.TrySetResult();
        return new ValueTask<WidgetAppLibraryPage>(IgnorePageCancellation(
            token, catalogCanceled, catalogCompletion.Task));
    };
    fake.ObserveRunningHandler = token =>
    {
        token.ThrowIfCancellationRequested();
        runningStarted.TrySetResult();
        return ValueTask.FromResult(fake.RunningObservation);
    };

    await widget.OnActionAsync(new("games.open-catalog", "games.open-catalog"));
    await catalogStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await widget.OnActionAsync(new("games.open-running", "games.open-running"));
    await catalogCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var switched = Snapshot(widget, 609);
    Assert.Equal(GamesAppsPage.Running, widget.Page);
    Assert.Equal("games.running.page", switched.FocusGroupEntryRequest!.GroupId);
    Assert.Equal(GamesAppsViewState.Loading, widget.ViewState);

    catalogCompletion.SetResult(Page([App("stale", "Stale catalog app")], null));
    await runningStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    clock.Advance(TimeSpan.FromSeconds(1));
    await WaitUntil(() => widget.Page == GamesAppsPage.Running &&
        widget.ViewState == GamesAppsViewState.Ready);
    var ready = Snapshot(widget, 610);
    Assert.SequenceEqual(["saved-running"], widget.Items.Select(item => item.AppId));
    Assert.False(Nodes(ready.Root).Any(node =>
        string.Equals(node.Text, "Stale catalog app", StringComparison.Ordinal)));
    Assert.Equal("games.running.page", ready.FocusGroupEntryRequest!.GroupId);
    Assert.Valid(ready);
    await Background(widget);
}

static async Task WarmActiveRootsRetainReadyContent()
{
    var catalogClock = new ManualTimerTimeProvider();
    var catalogFake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("catalog", "Catalog app")], null) },
    };
    var catalogWidget = CreateWithTimeProvider(catalogFake, catalogClock);
    await Interactive(catalogWidget);
    await WaitUntil(() => catalogWidget.ViewState == GamesAppsViewState.Ready);
    await catalogWidget.OnActionAsync(new("games.open-catalog", "games.open-catalog"));
    catalogClock.Advance(TimeSpan.FromSeconds(1));
    await WaitUntil(() => catalogWidget.Page == GamesAppsPage.Catalog &&
        catalogWidget.ViewState == GamesAppsViewState.Ready);
    var catalogBefore = Snapshot(catalogWidget, 611);
    var catalogTile = ActionSurfaces(catalogBefore.Root).Single(tile =>
        TileTitle(tile) == "Catalog app");
    var catalogRefreshStarted = NewSignal();
    var catalogRefresh = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    catalogFake.ReadHandler = (_, token) => new ValueTask<WidgetAppLibraryPage>(
        AwaitPage(catalogRefresh.Task, catalogRefreshStarted, token));
    await Background(catalogWidget);
    await Interactive(catalogWidget);
    await catalogRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var catalogDuring = Snapshot(catalogWidget, 612);
    Assert.Equal(GamesAppsPage.Catalog, catalogWidget.Page);
    Assert.Equal(GamesAppsViewState.Ready, catalogWidget.ViewState);
    Assert.Equal(catalogTile.Id, ActionSurfaces(catalogDuring.Root).Single().Id);
    Assert.False(Nodes(catalogDuring.Root).Any(node => node.Id == "games.route.loading"));
    catalogRefresh.SetResult(Page([App("updated", "Updated catalog app")], null));
    await WaitUntil(() => catalogWidget.Items.Any(item => item.AppId == "updated"));
    Assert.True(ActionSurfaces(Snapshot(catalogWidget, 613).Root).Any(tile =>
        TileTitle(tile) == "Updated catalog app"));
    await Background(catalogWidget);

    var runningClock = new ManualTimerTimeProvider();
    var runningFake = new FakeAppLibraryHost
    {
        RunningObservation = new([
            new("saved-running", "Running app", WidgetAppLibraryKind.Application,
                "Windows"),
        ], "running-revision"),
    };
    var runningWidget = CreateWithTimeProvider(runningFake, runningClock);
    await Interactive(runningWidget);
    await WaitUntil(() => runningWidget.ViewState == GamesAppsViewState.Ready);
    await runningWidget.OnActionAsync(new("games.open-running", "games.open-running"));
    runningClock.Advance(TimeSpan.FromSeconds(1));
    await WaitUntil(() => runningWidget.Page == GamesAppsPage.Running &&
        runningWidget.ViewState == GamesAppsViewState.Ready);
    var runningBefore = Snapshot(runningWidget, 614);
    var runningTile = ActionSurfaces(runningBefore.Root).Single();
    var runningRefreshStarted = NewSignal();
    var runningRefresh = new TaskCompletionSource<WidgetRunningAppObservation>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    runningFake.ObserveRunningHandler = async token =>
    {
        runningRefreshStarted.TrySetResult();
        return await runningRefresh.Task.WaitAsync(token);
    };
    await Background(runningWidget);
    await Interactive(runningWidget);
    await runningRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var runningDuring = Snapshot(runningWidget, 615);
    Assert.Equal(GamesAppsPage.Running, runningWidget.Page);
    Assert.Equal(GamesAppsViewState.Ready, runningWidget.ViewState);
    Assert.Equal(runningTile.Id, ActionSurfaces(runningDuring.Root).Single().Id);
    Assert.False(Nodes(runningDuring.Root).Any(node => node.Id == "games.route.loading"));
    runningRefresh.SetResult(new([
        new("saved-updated", "Updated running app", WidgetAppLibraryKind.Application,
            "Windows"),
    ], "updated-revision"));
    await WaitUntil(() => runningWidget.Items.Any(item => item.AppId == "saved-updated"));
    Assert.True(ActionSurfaces(Snapshot(runningWidget, 616).Root).Any(tile =>
        TileTitle(tile) == "Updated running app"));
    await Background(runningWidget);
}

static async Task WarmLaterCatalogPageIsRetained()
{
    var clock = new ManualTimerTimeProvider();
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([App("first", "First page")], GamesAppsWidget.PageSize),
            [GamesAppsWidget.PageSize] = Page([App("later", "Later page")], null),
        },
    };
    var widget = CreateWithTimeProvider(fake, clock);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await widget.OnActionAsync(new("games.open-catalog", "games.open-catalog"));
    clock.Advance(TimeSpan.FromSeconds(1));
    await WaitUntil(() => widget.Page == GamesAppsPage.Catalog &&
        widget.ViewState == GamesAppsViewState.Ready);
    await widget.OnActionAsync(new("games.load-more", "games.load-more"));
    await WaitUntil(() => widget.Items.Any(item => item.AppId == "later"));
    var before = Snapshot(widget, 617);
    var laterTile = ActionSurfaces(before.Root).Single(tile => TileTitle(tile) == "Later page");
    var requestsBefore = fake.CatalogRequests.Count;

    await Background(widget);
    await Interactive(widget);
    var retained = Snapshot(widget, 618);
    Assert.Equal(GamesAppsPage.Catalog, widget.Page);
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    Assert.SequenceEqual(["later"], widget.Items.Select(item => item.AppId));
    Assert.Equal(requestsBefore, fake.CatalogRequests.Count);
    Assert.Equal(laterTile.Id, ActionSurfaces(retained.Root).Single().Id);
    Assert.Equal(before.InitialFocusId, retained.InitialFocusId);
    Assert.True(Buttons(retained.Root).Any(button => button.Id == "games.previous-page"));
    Assert.Valid(retained);
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
        restarted.CuratedItems.Select(item => item.Presentation.DisplayName));
    Assert.SequenceEqual(["fresh-gamma", "fresh-beta"],
        restarted.CuratedItems.Select(item => item.AppId));
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
        widget.CuratedItems.Select(item => item.Presentation.DisplayName));
    Assert.SequenceEqual(["fresh-b", "fresh-a"],
        widget.CuratedItems.Select(item => item.AppId));
    var warmBeta = ActionSurfaces(warm.Root).Single(tile => TileTitle(tile) == "Beta");
    Assert.True(warmBeta.IsDisabled != true);
    Assert.Equal(warmBeta.Id, warm.InitialFocusId);
    var countBadge = Nodes(warm.Root).Single(node => node.Id == "games.header.count");
    var countLabel = Text(countBadge, "games.header.count.label");
    Assert.Equal("2 saved", countLabel.Text);
    Assert.Equal("2 saved", countLabel.AccessibilityLabel);

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
    Assert.SequenceEqual(["Alpha"], widget.CuratedItems.Select(
        item => item.Presentation.DisplayName));
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
        Pages = { [0] = Page([App("fresh-a", "Alpha") with
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

    Assert.SequenceEqual(["Alpha"], widget.CuratedItems.Select(
        item => item.Presentation.DisplayName));
    Assert.Equal("fresh-a", widget.CuratedItems.Single().AppId);
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
    await WaitUntil(() => widget.SelectedAppId == "three" && !widget.HasNextPage);
    Assert.Equal(2, widget.Items.Count);
    Assert.Equal("three", widget.SelectedAppId);
    Assert.False(widget.HasNextPage);
    var snapshot = Snapshot(widget, 7);
    Assert.True(!Buttons(snapshot.Root).Any(button => button.Id == "games.load-more"));
    Assert.Equal(2, ActionSurfaces(snapshot.Root)
        .Count(tile => tile.ActionId == "games.toggle-curation"));
    var previous = Buttons(snapshot.Root).Single(button => button.Id == "games.previous-page");
    Assert.True(previous.IsFocusable);
    Assert.Equal(previous.Id,
        Nodes(snapshot.Root).Single(node => node.Id == "games.catalog.page")
            .InitialChildFocusId);
    var three = ActionSurfaces(snapshot.Root).Single(tile => TileTitle(tile) == "Three");
    Assert.Equal(GamesAppsPresentation.CatalogElementId("saved-three"), three.Id);
    Assert.Equal("three", widget.SelectedAppId);
    Assert.Equal("games.catalog.page", snapshot.FocusGroupEntryRequest!.GroupId);
    Assert.Equal<string?>(null, snapshot.InitialFocusId);
    Assert.Valid(snapshot);

    await widget.OnActionAsync(new("games.previous-page", previous.Id));
    await WaitUntil(() => widget.SelectedAppId == "one" && widget.HasNextPage);
    var restored = Snapshot(widget, 237);
    Assert.Equal(2, widget.Items.Count);
    var one = ActionSurfaces(restored.Root).Single(tile => TileTitle(tile) == "One");
    Assert.Equal(GamesAppsPresentation.CatalogElementId("saved-one"), one.Id);
    Assert.Equal(one.Id,
        Nodes(restored.Root).Single(node => node.Id == "games.catalog.page")
            .InitialChildFocusId);
    Assert.Equal<string?>(null, restored.InitialFocusId);
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
            : CursorOffset(request) == 0
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
    var duplicateHandled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.A,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        FocusedElementId: loadMore.Id,
        Sequence: 2,
        ActiveInputScopeId: busy.ActiveInputScopeId,
        SnapshotSequence: busy.Sequence));
    Assert.False(duplicateHandled);

    Assert.Equal(1, fake.PageRequests.Count(request => request.Offset == 32));
    release.TrySetResult(Page([App("two", "Two")], null));
    await first;
    await WaitUntil(() => widget.Items.Any(item => item.AppId == "two"));
    Assert.Equal(1, widget.Items.Count);
    await Background(widget);
}

static async Task LargeCatalogRetainsOneBoundedPage()
{
    var browsing = false;
    var fake = new FakeAppLibraryHost
    {
        ReadHandler = (request, _) =>
        {
            if (!browsing) return ValueTask.FromResult(Page([], null));
            var offset = CursorOffset(request);
            var items = Enumerable.Range(offset, GamesAppsWidget.PageSize)
                .Select(index => App($"opaque-{index}", $"Application {index}"))
                .ToArray();
            return ValueTask.FromResult(Page(
                items, offset + GamesAppsWidget.PageSize) with
                {
                    Before = offset > 0
                        ? Cursor(Math.Max(0, offset - GamesAppsWidget.PageSize))
                        : null,
                });
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    browsing = true;
    fake.PageRequests.Clear();
    await OpenCatalog(widget);
    for (var page = 1; page < 20; page++)
        await widget.OnActionAsync(new("games.load-more", "games.load-more"));

    Assert.Equal(GamesAppsWidget.PageSize, widget.Items.Count);
    Assert.Equal(20, fake.PageRequests.Count);
    Assert.True(widget.HasNextPage);
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
    Assert.Contains("Opened Alpha",
        Text(Snapshot(widget, 12).Root, "games.toast.message").Text!);
    await Background(widget);
}

static async Task BackgroundCancelsPageWork()
{
    var started = NewSignal();
    var canceled = NewSignal();
    var fake = new FakeAppLibraryHost
    {
        ReadHandler = (request, cancellationToken) => CursorOffset(request) == 0
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
        Text(denied.Root, "games.toast.message").Text!);
    Assert.False(System.Text.Json.JsonSerializer.Serialize(denied)
        .Contains("private detail", StringComparison.Ordinal));
    await Background(widget);
}

static async Task FailureStates()
{
    foreach (var (exception, expectedStatus) in new (Exception, string)[]
    {
        (new WidgetCapabilityException("permission_denied", "denied"),
            "Allow Games & Apps in Settings"),
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
        Assert.True(Nodes(snapshot.Root).Any(node =>
            node.Text?.Contains(expectedStatus, StringComparison.Ordinal) == true),
            $"Expected the sanitized state presentation to contain '{expectedStatus}'.");
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
    await RefreshCurrentRouteAndWait(
        widget, "games.refresh-catalog", "games.refresh-catalog");

    Assert.SequenceEqual(["b", "a"], widget.CuratedItems.Select(item => item.AppId));
    var snapshot = Snapshot(widget, 216);
    Assert.Equal(beta.Id, snapshot.InitialFocusId);
    Assert.Contains("refresh unavailable", Text(snapshot.Root, "games.status").Text!);
    var betaAfterFailure = ActionSurfaces(snapshot.Root)
        .Single(tile => TileTitle(tile) == "Beta");
    Assert.True(betaAfterFailure.IsDisabled != true);
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
    await RefreshCurrentRouteAndWait(widget, retry.ActionId!, retry.Id);

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

static async Task RegistrationMergeCapacityRetainsRecoveryOwnership()
{
    const string requested = "saved-requested";
    var baselinePending = Enumerable.Range(0, GamesAppsLibraryPolicy.MaximumCuratedItems - 1)
        .Select(index => $"pending-{index:D2}").ToArray();
    var baseline = new GamesAppsLibraryState(3, [], null)
    {
        PendingRunningRegistrationSavedIds = baselinePending,
    };
    var begin = GamesAppsLibraryPolicy.BeginRunningRegistration(baseline, requested);
    Assert.True(begin.Accepted);
    Assert.True(begin.State.PendingRunningRegistrationSavedIds.Contains(
        requested, StringComparer.Ordinal));
    var latestPending = new GamesAppsLibraryState(3, [], null)
    {
        PendingRunningRegistrationSavedIds = Enumerable.Range(
                0, GamesAppsLibraryPolicy.MaximumCuratedItems)
            .Select(index => $"latest-pending-{index:D2}").ToArray(),
    };
    var writes = 0;
    ValueTask<WidgetPrivateStateMutation> WritePending(
        GamesAppsLibraryState _, long __, CancellationToken ___)
    {
        writes++;
        return ValueTask.FromException<WidgetPrivateStateMutation>(
            new WidgetCapabilityException("state_conflict", "conflict"));
    }
    ValueTask<WidgetPrivateStateValue<GamesAppsLibraryState>> ReadPending(
        CancellationToken _) => ValueTask.FromResult(
        new WidgetPrivateStateValue<GamesAppsLibraryState>(true, latestPending, 9));
    var pendingResult = await GamesAppsLibraryStore.SaveAsync(
        WritePending, ReadPending, baseline, begin.State, 1, CancellationToken.None);
    Assert.Equal(GamesAppsLibrarySaveStatus.Rejected, pendingResult.Status);
    Assert.Equal(1, writes);
    Assert.Equal(GamesAppsLibraryPolicy.MaximumCuratedItems,
        pendingResult.State.PendingRunningRegistrationSavedIds.Count);
    Assert.False(pendingResult.State.PendingRunningRegistrationSavedIds.Contains(
        requested, StringComparer.Ordinal));

    var completionBaseline = new GamesAppsLibraryState(3, [], null)
    {
        PendingRunningRegistrationSavedIds = [requested],
    };
    var item = InstalledItem(
        "portable-current", requested, "Portable app",
        WidgetAppLibraryKind.Application, "source-portable", "Portable");
    var completion = GamesAppsLibraryPolicy.CompleteRunningRegistration(
        completionBaseline, item, []);
    Assert.True(completion.Accepted);
    var latestSavedIds = Enumerable.Range(0, GamesAppsLibraryPolicy.MaximumCuratedItems)
        .Select(index => $"saved-latest-{index:D2}").ToArray();
    var completionLatest = new GamesAppsLibraryState(
        3, latestSavedIds, latestSavedIds[0])
    {
        PendingRunningRegistrationSavedIds = [requested],
    };
    var merged = GamesAppsLibraryPolicy.Merge(
        completionBaseline, completion.State, completionLatest);
    Assert.False(merged.Accepted);
    Assert.Equal(GamesAppsLibraryPolicy.MaximumCuratedItems, merged.State.SavedIds.Count);
    Assert.False(merged.State.SavedIds.Contains(requested, StringComparer.Ordinal));
    Assert.False(merged.State.RunningRegistrationSavedIds.Contains(
        requested, StringComparer.Ordinal));
    Assert.True(merged.State.PendingRunningRegistrationSavedIds.Contains(
        requested, StringComparer.Ordinal));
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
        GamesAppsWidget.PageSize);
    Assert.False(initial.EmptyInitial);
    Assert.False(initial.State.CanLoadPrevious);
    Assert.Equal(Cursor(GamesAppsWidget.PageSize), initial.State.After?.Value);

    var next = GamesAppsCatalogPolicy.ApplyPage(
        initial.State,
        Page(second, GamesAppsWidget.PageSize * 2) with { Before = Cursor(0) },
        GamesAppsCatalogPageTransition.Next,
        GamesAppsWidget.PageSize);
    Assert.True(next.State.CanLoadPrevious);
    Assert.Equal("saved-second-0", next.State.Items[0].SavedId);

    var previous = GamesAppsCatalogPolicy.ApplyPage(
        next.State,
        Page(first, GamesAppsWidget.PageSize),
        GamesAppsCatalogPageTransition.Previous,
        GamesAppsWidget.PageSize);
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
        LibraryNavigationSnapshot(),
        GamesAppsViewState.Ready,
        "1 saved",
        [alpha],
        [alpha.SavedId],
        new HashSet<string>([alpha.SavedId], StringComparer.Ordinal),
        alpha.AppId,
        LaunchingAppId: null,
        LoadingMore: false,
        LibraryMutationBusy: false,
        CatalogRefreshBusy: false,
        HasNextPage: false,
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
        LibraryNavigationSnapshot(),
        GamesAppsViewState.Ready,
        "1 saved",
        [alpha],
        [alpha.SavedId],
        new HashSet<string>([alpha.SavedId], StringComparer.Ordinal),
        alpha.AppId,
        LaunchingAppId: null,
        LoadingMore: false,
        LibraryMutationBusy: false,
        CatalogRefreshBusy: false,
        HasNextPage: false,
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

static WidgetNavigationSnapshot<GamesAppsPage> LibraryNavigationSnapshot() => new(
    GamesAppsPage.Library,
    GamesAppsPage.Library,
    Depth: 0,
    InputScopeId: "games-apps",
    InitialFocusId: null,
    BackActionId: null,
    Revision: 0,
    RouteCancellationToken: CancellationToken.None);

static void AssertSourceContains(string source, string value) =>
    Assert.True(source.Contains(value, StringComparison.Ordinal),
        $"Expected source boundary to contain '{value}'.");

static Task PackageValidates()
{
    var root = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
    Assert.SequenceEqual(["system.apps.library.read.v1"], manifest.Permissions);
    Assert.SequenceEqual([
        "system.apps.library.launch.v1",
        "system.apps.running.read.v1",
        "system.apps.running.register.v1",
    ], manifest.OptionalPermissions);
    var package = WrssPackageLoader.Load("styles/default.wrss", new WrssFileSourceProvider(root));
    var compiled = WrssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    var stateTitle = compiled.Theme!.Resolve(new WrssElement(
        "text", StyleClasses: new HashSet<string>(["games-state-title"])))!;
    var stateHelp = compiled.Theme.Resolve(new WrssElement(
        "text", StyleClasses: new HashSet<string>(["games-state-help"])))!;
    var rootStyle = compiled.Theme.Resolve(new WrssElement(
        "stack", StyleClasses: new HashSet<string>(["games-apps-widget"])))!;
    var contentStyle = compiled.Theme.Resolve(new WrssElement(
        "stack", StyleClasses: new HashSet<string>(["games-content"])))!;
    var scrollStyle = compiled.Theme.Resolve(new WrssElement(
        "scroll", StyleClasses: new HashSet<string>(
            ["games-page-scroll", "games-library-scroll"])))!;
    var routeProgress = compiled.Theme.Resolve(new WrssElement(
        "stack", StyleClasses: new HashSet<string>(["games-route-progress"])))!;
    Assert.Equal("0", stateTitle.Get("flex-shrink")?.Text);
    Assert.Equal("center", stateTitle.Get("text-align")?.Text);
    Assert.Equal("0", stateHelp.Get("flex-shrink")?.Text);
    Assert.Equal("100vh", rootStyle.Get("height")?.Text);
    Assert.Equal("0px", rootStyle.Get("min-height")?.Text);
    Assert.Equal("0px", contentStyle.Get("min-height")?.Text);
    Assert.Equal("1", contentStyle.Get("flex-grow")?.Text);
    Assert.Equal("0px", scrollStyle.Get("min-height")?.Text);
    Assert.Equal("1", scrollStyle.Get("flex-grow")?.Text);
    Assert.Equal("center", routeProgress.Get("align")?.Text);
    Assert.Equal("center", routeProgress.Get("justify")?.Text);
    Assert.Equal("1", routeProgress.Get("flex-grow")?.Text);
    return Task.CompletedTask;
}

static WidgetAppLibraryItem App(
    string id,
    string name,
    WidgetAppLibraryKind kind = WidgetAppLibraryKind.Application,
    string? artworkHandle = null) =>
    InstalledItem(
        id,
        "saved-" + id,
        name,
        kind,
        kind == WidgetAppLibraryKind.Game ? "source-steam" : "source-windows",
        kind == WidgetAppLibraryKind.Game ? "Steam" : "Windows",
        artworkHandle,
        "fixture");

static WidgetAppLibraryItem InstalledItem(
    string appId,
    string savedId,
    string displayName,
    WidgetAppLibraryKind kind,
    string sourceId,
    string sourceDisplayName,
    string? artworkHandle = null,
    string artworkRevision = "fixture")
{
    WidgetAppLibraryArtwork[] artwork = artworkHandle is null ? [] :
    [
        new(WidgetAppLibraryArtworkRole.Tile, artworkHandle, artworkRevision,
            kind == WidgetAppLibraryKind.Game
                ? WidgetAppLibraryArtworkFallback.Game
                : WidgetAppLibraryArtworkFallback.Application),
    ];
    return new(appId, savedId, new(
        displayName,
        kind,
        new(sourceId, sourceDisplayName),
        new(WidgetAppLibraryAvailabilityState.Installed, true, "installed"),
        new(artwork),
        Metadata: null,
        new([WidgetAppLibraryAction.Launch]),
        ActiveOperation: null));
}

static WidgetAppLibraryPage Page(IReadOnlyList<WidgetAppLibraryItem> items, int? next) =>
    new(items, null, next is null ? null : Cursor(next.Value), "test-revision");

static string Cursor(int offset) => $"test.cursor.{offset}";

static int CursorOffset(WidgetAppLibraryCursorRequest request) =>
    request.Cursor is null ? 0 : int.Parse(
        request.Cursor.AsSpan(request.Cursor.LastIndexOf('.') + 1),
        System.Globalization.CultureInfo.InvariantCulture);

static WidgetTestPrivateState SavedState(params string[] savedIds)
{
    var ids = string.Join(',', savedIds.Select(id => $"\"{id}\""));
    var selected = savedIds.FirstOrDefault();
    return new WidgetTestPrivateState(
        $"{{\"Version\":3,\"SavedIds\":[{ids}],\"SelectedSavedId\":\"{selected}\"," +
        "\"AutoGameSavedIds\":[],\"ExcludedGameSavedIds\":[],\"DisplayItems\":[]}", 1);
}

static FakeAppLibraryHost PortableRunningHost(WidgetAppLibraryItem item) => new()
{
    RunningObservation = new([
        new(item.SavedId, item.Presentation.DisplayName,
            WidgetAppLibraryKind.Application, "Portable"),
    ], "running-revision"),
    ConfirmRunningHandler = _ => null,
};

static FakeAppLibraryHost RegisteredPortableLibraryHost(
    WidgetTestPrivateState state,
    WidgetAppLibraryItem item)
{
    var fake = new FakeAppLibraryHost { PrivateState = state };
    fake.ResolveHandler = (request, _) => ValueTask.FromResult(
        new ResolveSavedWidgetAppLibraryItemsResponse(
            request.SavedIds.Contains(item.SavedId, StringComparer.Ordinal) ? [item] : []));
    return fake;
}

static async Task AddFirstRunningTile(GamesAppsWidget widget, long sequence)
{
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    await widget.OnActionAsync(new("games.open-running", "games.open-running"));
    await WaitUntil(() => widget.Page == GamesAppsPage.Running &&
                          widget.ViewState == GamesAppsViewState.Ready);
    var tile = ActionSurfaces(Snapshot(widget, sequence).Root).Single(candidate =>
        candidate.ActionId == "games.toggle-curation");
    await widget.OnActionAsync(new("games.toggle-curation", tile.Id));
}

static WidgetTestPrivateState RunningRegistrationState(
    string savedId,
    bool pending,
    WidgetAppLibraryItem? item = null,
    long revision = 1,
    bool removalPending = false)
{
    var json = System.Text.Json.JsonSerializer.Serialize(new
    {
        Version = 3,
        SavedIds = pending ? Array.Empty<string>() : new[] { savedId },
        SelectedSavedId = pending ? null : savedId,
        AutoGameSavedIds = Array.Empty<string>(),
        ExcludedGameSavedIds = Array.Empty<string>(),
        DisplayItems = pending || item is null
            ? Array.Empty<object>()
            : new object[]
            {
                new
                {
                    SavedId = savedId,
                    DisplayName = item.Presentation.DisplayName,
                    Kind = item.Presentation.Kind,
                },
            },
        RunningRegistrationSavedIds = pending ? Array.Empty<string>() : new[] { savedId },
        PendingRunningRegistrationSavedIds = pending || removalPending
            ? new[] { savedId }
            : Array.Empty<string>(),
    });
    return new WidgetTestPrivateState(json, revision);
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

static GamesAppsWidget CreateWithTimeProvider(
    FakeAppLibraryHost fake, TimeProvider timeProvider) =>
    WidgetTestHost.Attach(new GamesAppsWidget(timeProvider), fake.Build());

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
    await widget.OnActionAsync(new("games.open-library", "games.open-library"));
    Assert.Equal(GamesAppsPage.Library, widget.Page);
}

static async Task RefreshCurrentRouteAndWait(
    GamesAppsWidget widget,
    string actionId = "games.retry",
    string sourceElementId = "games.root")
{
    await widget.OnActionAsync(new(actionId, sourceElementId));
    await WaitForCurrentRouteRefresh(widget);
}

static async Task WaitForCurrentRouteRefresh(GamesAppsWidget widget)
{
    await WaitUntil(() =>
    {
        if (widget.ViewState is GamesAppsViewState.Initial or GamesAppsViewState.Loading)
            return false;
        var refresh = Buttons(Snapshot(widget, 99_991).Root).FirstOrDefault(button =>
            button.Id == "games.refresh-catalog");
        return refresh is null || refresh.IsBusy != true;
    });
}

static ViewNode CompactDestination(ViewSnapshot snapshot, string actionId) =>
    Buttons(Nodes(snapshot.Root).Single(node => node.Id == "games.sections.compact"))
        .Single(button => button.ActionId == actionId);

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
    Assert.Equal("games.open-catalog",
        CompactDestination(snapshot, "games.open-catalog").ActionId);
    Assert.Equal(2, Buttons(snapshot.Root).Count(button =>
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
    public List<WidgetAppLibraryCursorRequest> CatalogRequests { get; } = [];
    public List<(int Offset, int Limit)> PageRequests { get; } = [];
    public List<IReadOnlyList<string>> ResolveRequests { get; } = [];
    public List<string> LaunchedIds { get; } = [];
    public Exception? ReadException { get; set; }
    public Exception? ResolveException { get; set; }
    public Exception? LaunchException { get; set; }
    public Func<WidgetAppLibraryCursorRequest, CancellationToken,
        ValueTask<WidgetAppLibraryPage>>? ReadHandler { get; set; }
    public Func<ResolveSavedWidgetAppLibraryItemsRequest, CancellationToken,
        ValueTask<ResolveSavedWidgetAppLibraryItemsResponse>>? ResolveHandler { get; set; }
    public Func<LaunchWidgetAppLibraryItemRequest, CancellationToken,
        ValueTask<WidgetCapabilityAcknowledgement>>? LaunchHandler { get; set; }
    public Func<CancellationToken, ValueTask<WidgetRunningAppObservation>>?
        ObserveRunningHandler { get; set; }
    public WidgetRunningAppObservation RunningObservation { get; set; } =
        new([], "running-empty");
    public Func<ConfirmWidgetRunningAppRequest, WidgetAppLibraryItem?>?
        ConfirmRunningHandler { get; set; }
    public List<ConfirmWidgetRunningAppRequest> RunningConfirmations { get; } = [];
    public Func<RegisterWidgetRunningAppRequest, CancellationToken,
        ValueTask<RegisterWidgetRunningAppResponse>>? RegisterRunningHandler { get; set; }
    public Func<ForgetWidgetRunningAppRequest, CancellationToken,
        ValueTask<WidgetCapabilityAcknowledgement>>? ForgetRunningHandler { get; set; }
    public List<RegisterWidgetRunningAppRequest> RunningRegistrations { get; } = [];
    public List<ForgetWidgetRunningAppRequest> ForgottenRunningApps { get; } = [];
    public WidgetTestPrivateState PrivateState { get; init; } = new();

    public WidgetHostServices Build() => new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetAppLibraryCapabilities.GetPage, GetPage)
        .WithHandler(WidgetAppLibraryCapabilities.ResolveSaved, ResolveSaved)
        .WithHandler(WidgetAppLibraryCapabilities.Launch, Launch)
        .WithHandler(WidgetAppLibraryCapabilities.ObserveRunning,
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ObserveRunningHandler is not null)
                    return ObserveRunningHandler(cancellationToken);
                return ValueTask.FromResult(RunningObservation);
            })
        .WithHandler(WidgetAppLibraryCapabilities.ConfirmRunning,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunningConfirmations.Add(request);
                return ValueTask.FromResult(new ConfirmWidgetRunningAppResponse(
                    ConfirmRunningHandler?.Invoke(request)));
            })
        .WithHandler(WidgetAppLibraryCapabilities.RegisterRunning,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunningRegistrations.Add(request);
                if (RegisterRunningHandler is not null)
                    return RegisterRunningHandler(request, cancellationToken);
                return ValueTask.FromException<RegisterWidgetRunningAppResponse>(
                    new WidgetCapabilityException(
                        "app_not_supported", "No portable registration fixture was configured."));
            })
        .WithHandler(WidgetAppLibraryCapabilities.ForgetRunning,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ForgottenRunningApps.Add(request);
                if (ForgetRunningHandler is not null)
                    return ForgetRunningHandler(request, cancellationToken);
                return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
            })
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
        WidgetAppLibraryCursorRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var offset = OffsetOf(request);
        CatalogRequests.Add(request);
        PageRequests.Add((offset, request.Limit));
        if (ReadException is not null)
            return ValueTask.FromException<WidgetAppLibraryPage>(ReadException);
        if (ReadHandler is not null) return ReadHandler(request, cancellationToken);
        if (!Pages.TryGetValue(offset, out var page))
            page = new WidgetAppLibraryPage([], null, null, "test-revision");
        var before = offset > 0 ? CursorFor(Math.Max(0, offset - request.Limit)) : null;
        return ValueTask.FromResult(page with { Before = before });
    }

    private static string CursorFor(int offset) => $"test.cursor.{offset}";

    private static int OffsetOf(WidgetAppLibraryCursorRequest request) =>
        request.Cursor is null ? 0 : int.Parse(
            request.Cursor.AsSpan(request.Cursor.LastIndexOf('.') + 1),
            System.Globalization.CultureInfo.InvariantCulture);

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

file sealed class ManualTimerTimeProvider : TimeProvider
{
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() => _now;
    public override long GetTimestamp() => _now.Ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(
        TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state, dueTime, period);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    internal void Advance(TimeSpan duration)
    {
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (_timers)
        {
            _now += duration;
            foreach (var timer in _timers.ToArray())
                if (timer.TryFire(_now, out var callback)) callbacks.Add(callback);
        }
        foreach (var callback in callbacks) callback.Callback(callback.State);
    }

    private sealed class Timer : ITimer
    {
        private readonly ManualTimerTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset? _dueAt;
        private TimeSpan _period;
        private bool _disposed;

        internal Timer(ManualTimerTimeProvider owner, TimerCallback callback,
            object? state, TimeSpan dueTime, TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            Change(dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) return false;
            _period = period;
            _dueAt = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : _owner.GetUtcNow() + dueTime;
            return true;
        }

        internal bool TryFire(DateTimeOffset now,
            out (TimerCallback Callback, object? State) callback)
        {
            callback = default;
            if (_disposed || _dueAt is null || _dueAt > now) return false;
            callback = (_callback, _state);
            _dueAt = _period == Timeout.InfiniteTimeSpan ? null : now + _period;
            return true;
        }

        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
