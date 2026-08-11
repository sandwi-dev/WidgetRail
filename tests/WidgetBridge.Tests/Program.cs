using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Pipes;
using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformDiagnostics;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

if (args.Contains("--widget-pipe", StringComparer.Ordinal))
    return await RunWorkerAsync(args);

var tests = new (string Name, Func<Task> Run)[]
{
    ("Bridge framing rejects oversized messages", OversizedFrameIsRejected),
    ("Event cancellation preserves the serialized frame boundary", BridgeEventWriteBoundaryScenarios.CancellationPreservesFrameBoundary),
    ("Bridge read and reply timeouts have exact frame owners", BridgeFrameOwnershipScenarios.TimeoutAndCancellationHaveExactOwners),
    ("Strict catalog rejects unknown properties", StrictCatalogRejectsUnknownProperties),
    ("Bridge startup scopes an explicit development installed catalog", DevelopmentCatalogRootIsScoped),
    ("Settings reviews the same catalog selected by the bridge", SettingsUsesSelectedCatalog),
    ("Catalog owns bounded worker memory policy", CatalogMemoryPolicyIsTrusted),
    ("Worker residency budget options are bounded and explicit", WorkerResidencyBudgetOptionsAreBounded),
    ("Worker residency budget admission is race safe", WorkerResidencyBudgetAdmissionIsRaceSafe),
    ("Catalog widget icons use the closed WidgetGlyph set with a safe fallback", CatalogGlyphIsClosed),
    ("Catalog rejects invalid GBSS with safe diagnostics", InvalidThemeIsRejected),
    ("Catalog rejects style paths outside package root", UnsafeStylePathIsRejected),
    ("Catalog enumeration does not launch workers", EnumerationIsLazy),
    ("Request dispatcher cleans success failure and cancellation", RequestDispatcherCleansTerminalPaths),
    ("Request classification is closed typed and fail-closed", RequestClassificationIsClosed),
    ("Trusted artwork demand is exact current and lazy through the production bridge", TrustedArtworkDemandIsExact),
    ("Request dispatcher preserves FIFO and predecessor failure", RequestDispatcherOwnsWidgetOrdering),
    ("Request dispatcher rejects duplicates and global over-capacity", RequestDispatcherBoundsAdmission),
    ("Request dispatcher deadline quarantines cancellation-ignoring work", RequestDispatcherForcedDrainIsComplete),
    ("Stalled widget admission leaves bounded list and stop control responsive", StalledAdmissionKeepsControlPlaneResponsive),
    ("Pipelined requests preserve per-widget receive order", PipelinedWidgetRequestsStayOrdered),
    ("Duplicate pending request IDs fail the bridge session closed", DuplicatePendingRequestIdsFailClosed),
    ("Enabled installed widgets join the bridge catalog without eager launch", InstalledWidgetsJoinCatalog),
    ("Installed content generations receive distinct isolation identities", InstalledContentGenerationIsIsolated),
    ("Installed launch admission rejects content changed after catalog publication", InstalledLaunchAdmissionRejectsRace),
    ("Installed widget residency policies reach the generic supervisor", InstalledResidencyPolicyIsCarried),
    ("Known installed capabilities load lazily and unknown capabilities fail closed", InstalledCapabilityDeclarationsAreClosed),
    ("Bridge alone synthesizes private state authority for capability-free workers", PrivateStateAuthorityIsHostSynthesized),
    ("Tampered installed catalogs fail soft to trusted widgets", TamperedInstalledCatalogFailsSoft),
    ("Invalid installed styles fail soft to trusted widgets", InvalidInstalledStyleFailsSoft),
    ("Catalog monitor retains invalid trusted state and fails closed on installed state", CatalogMonitorIsRevisionedAndLastGood),
    ("Live installed bytes are pinned and post-release tamper cannot relaunch", InstalledPackageTamperRetiresLiveWorker),
    ("Catalog monitor closes the startup notification window", CatalogMonitorStartupCatchUp),
    ("Catalog reconciliation preserves compatible workers and retires changed workers", CatalogReconciliationPreservesCompatibleWorkers),
    ("Client registry owns compatible replacement removal and stale generations", BridgeClientRegistryScenarios.CatalogReplacementAndRemovalOwnGenerations),
    ("Client registry owns idle unload cancellation and replacement drain", BridgeClientRegistryScenarios.IdleUnloadCancellationAndReplacementAreOwned),
    ("Client registry restart restores lifecycle and resets generation", BridgeClientRegistryScenarios.RestartRestoresLifecycleAndResetsGeneration),
    ("Client registry restart reserves one generation and cleans failed restore", BridgeClientRegistryScenarios.RestartReservationAndRestoreFailureAreClosed),
    ("Client registry commits lifecycle and first snapshot as one generation", BridgeClientRegistryScenarios.LifecycleAndFirstSnapshotAreAtomic),
    ("Client registry publication admission serializes replacement", BridgeClientRegistryScenarios.PublicationAdmissionSerializesReplacement),
    ("Client registry notification lane bounds and balances admission", BridgeClientRegistryScenarios.NotificationLaneBoundsAndBalancesAdmission),
    ("Client registry notification burst cancels and drains on replacement", BridgeClientRegistryScenarios.NotificationBurstIsBoundedAndRetires),
    ("Client registry cancelled restart transfers exact retirement", BridgeClientRegistryScenarios.CancelledRestartTransfersRetirement),
    ("Client registry starts external retirement outside its identity gate", BridgeClientRegistryScenarios.ExternalRetirementStartsOutsideIdentityGate),
    ("Client registry releases refused and failed-start residency", BridgeClientRegistryScenarios.BudgetRefusalAndFailedStartReleaseReservations),
    ("Client registry terminal disposal serializes with operations", BridgeClientRegistryScenarios.TerminalDisposalSerializesWithConcurrentOperation),
    ("Client registry observes retirement failures and disposes every client", BridgeClientRegistryScenarios.RetirementFailuresAreObservedAndDrained),
    ("Platform appearance is bounded and does not launch workers", PlatformAppearanceIsLazy),
    ("Private diagnostics attach only to the exact trusted Settings identity", DiagnosticsAreSettingsOnly),
    ("Diagnostics projection is bounded sanitized and read only", BridgeDiagnosticsScenarios.ProjectionIsBoundedSanitizedAndReadOnly),
    ("Diagnostics partial failures malformed input and deadline are closed", BridgeDiagnosticsScenarios.PartialFailureMalformedInputAndDeadlineAreClosed),
    ("Authority recovery projection is exact bounded and cancellation safe", BridgeDiagnosticsScenarios.RecoveryRetryIsExactBoundedAndCancellationSafe),
    ("User theme layers override widget selectors", UserThemeOverridesWidgetStyles),
    ("Appearance reload publishes revisions and retains last good state", AppearanceReloadIsLastGood),
    ("Widget lifecycle is explicit, lazy, and idempotent through the bridge", LifecycleIsExplicit),
    ("Suspend-when-hidden blocks work and serves only a cached view", SuspendWhenHiddenIsLogical),
    ("Idle unload is cancellable cached and lazily resumable", IdleUnloadIsPolicyDriven),
    ("Force reload retires a healthy worker and restores lifecycle", ForceReloadRestoresLifecycle),
    ("Force reload clears suspended snapshots and stale input authority", ForceReloadClearsCachedAuthority),
    ("Force reload drains a hung admitted action and restores lifecycle", ForceReloadDrainsHungAction),
    ("Force reload rejects unknown widget IDs", ForceReloadRejectsUnknownWidget),
    ("Bridge rejects runtime-owned lifecycle states", RuntimeOwnedLifecycleStatesAreRejected),
    ("Snapshots and hover quick actions cross bridge", SnapshotAndQuickAction),
    ("Protocol-v2 scroll nodes resolve bridge render roles", ScrollRenderRole),
    ("Protocol-v8 grids, action surfaces, and loading indicators resolve bridge render roles", ActionSurfaceRenderRole),
    ("Dashboard-owned controller buttons are rejected", DashboardButtonsStayHostOwned),
    ("Late action failures retain worker generation", ActionFailureIsGenerationOwned),
    ("Worker failures surface without killing bridge", WorkerFailureIsSurfaced),
    ("Worker residency budget refuses count overcommit and releases failures", WorkerResidencyCountIsBounded),
    ("Worker residency budget accounts declared memory and preserves Settings access", WorkerResidencyMemoryIsBounded),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception}");
        Console.Error.WriteLine(failures[^1]);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task<int> RunWorkerAsync(string[] arguments)
{
    var instance = RequiredValue(arguments, "--widget-instance");
    return await WidgetWorkerBootstrap.RunAsync(
        arguments,
        _ => new BridgeTestWidget(instance));
}

static async Task OversizedFrameIsRejected()
{
    var bytes = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(bytes, 1025);
    await using var stream = new MemoryStream(bytes);
    var channel = new BridgeFrameChannel(stream, 1024);
    await Assert.ThrowsAsync<BridgeProtocolException>(() =>
        channel.ReadAsync(CancellationToken.None).AsTask());
}

static Task StrictCatalogRejectsUnknownProperties()
{
    using var catalog = TemporaryCatalog.Create(addUnknownProperty: true);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(catalog.Path));
    using var forgedState = TemporaryCatalog.Create(
        declaredCapabilities: [PlatformCapabilities.PrivateStateV1]);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(forgedState.Path));
    return Task.CompletedTask;
}

static Task DevelopmentCatalogRootIsScoped()
{
    using var temporary = new TemporaryDirectory("gba-bridge-dev-root");
    var settings = Path.Combine(temporary.Path, "settings");
    var development = Path.Combine(temporary.Path, "development-catalog");
    Assert.Equal(Path.GetFullPath(development),
        GameBarAlternative.WidgetBridge.Program.ResolveInstalledCatalogRoot(
            ["--installed-catalog-root", development], settings));
    Assert.Equal(Path.Combine(Path.GetFullPath(settings), "widgets"),
        GameBarAlternative.WidgetBridge.Program.ResolveInstalledCatalogRoot([], settings));
    Assert.Throws<ArgumentException>(() =>
        GameBarAlternative.WidgetBridge.Program.ResolveInstalledCatalogRoot(
            ["--installed-catalog-root", development, "--installed-catalog-root", development], settings));
    return Task.CompletedTask;
}

static Task WorkerResidencyBudgetOptionsAreBounded()
{
    var defaults = GameBarAlternative.WidgetBridge.Program.ResolveWorkerResidencyBudget([]);
    Assert.Equal(8, defaults.MaximumApplicationWorkers);
    Assert.Equal(512, defaults.MaximumApplicationMemoryMb);

    var configured = GameBarAlternative.WidgetBridge.Program.ResolveWorkerResidencyBudget(
        ["--max-resident-workers", "3", "--max-resident-memory-mb", "192"]);
    Assert.Equal(3, configured.MaximumApplicationWorkers);
    Assert.Equal(192, configured.MaximumApplicationMemoryMb);
    Assert.Throws<ArgumentException>(() =>
        GameBarAlternative.WidgetBridge.Program.ResolveWorkerResidencyBudget(
            ["--max-resident-workers", "0"]));
    Assert.Throws<ArgumentException>(() =>
        GameBarAlternative.WidgetBridge.Program.ResolveWorkerResidencyBudget(
            ["--max-resident-memory-mb", "15"]));
    return Task.CompletedTask;
}

static async Task WorkerResidencyBudgetAdmissionIsRaceSafe()
{
    var budget = new WorkerResidencyBudget(new WorkerResidencyBudgetOptions
    {
        MaximumApplicationWorkers = 4,
        MaximumApplicationMemoryMb = 64,
    });
    var owners = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();
    var admissions = await Task.WhenAll(owners.Select((owner, index) => Task.Run(() =>
    {
        try
        {
            budget.Reserve(owner, $"race-{index}", 16, isControlPlane: false);
            return true;
        }
        catch (WidgetProcessAdmissionException)
        {
            return false;
        }
    })));

    Assert.Equal(4, admissions.Count(admitted => admitted));
    Assert.Equal(4, budget.Snapshot.ApplicationWorkers);
    Assert.Equal(64, budget.Snapshot.ApplicationMemoryMb);
    foreach (var owner in owners) budget.Release(owner);
    Assert.Equal(0, budget.Snapshot.ApplicationWorkers);
    Assert.Equal(0, budget.Snapshot.ApplicationMemoryMb);
}

static async Task SettingsUsesSelectedCatalog()
{
    using var trusted = TemporaryCatalog.Create(
        id: "settings",
        packageId: "org.gbar.firstparty.settings",
        publisherId: "org.gbar.firstparty");
    using var temporary = new TemporaryDirectory("gba-settings-selected-catalog");
    var selected = Path.Combine(temporary.Path, "selected-widgets");
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, selected, Environment.ProcessPath!);
    var settings = load.Catalog.GetConfigured("settings");
    var index = settings.WorkerArguments.ToList().IndexOf("--installed-widget-catalog-root");
    Assert.True(index >= 0 && index + 1 < settings.WorkerArguments.Count,
        "Settings was not given the bridge-selected package catalog.");
    Assert.Equal(Path.GetFullPath(selected), settings.WorkerArguments[index + 1]);
    Assert.True(!settings.RequiresAppContainer,
        "Catalog alignment must not change the exact trusted Settings worker policy.");
}

static Task CatalogMemoryPolicyIsTrusted()
{
    using var valid = TemporaryCatalog.Create(memoryLimitMb: 48);
    var configured = BridgeCatalog.Load(valid.Path).GetConfigured("test-widget");
    Assert.Equal(48, configured.MemoryLimitMb);
    Assert.True(!configured.RequiresAppContainer,
        "Trusted built-in catalog workers must remain explicitly host-owned.");
    using var tooSmall = TemporaryCatalog.Create(memoryLimitMb: 15);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(tooSmall.Path));
    using var tooLarge = TemporaryCatalog.Create(memoryLimitMb: 257);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(tooLarge.Path));
    return Task.CompletedTask;
}

static async Task DiagnosticsAreSettingsOnly()
{
    var exact = DiagnosticCandidate();
    Assert.True(WidgetBridgeServer.IsTrustedSettings(exact),
        "The exact built-in Settings identity did not receive diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with { Id = "settings-copy" }),
        "A different widget ID received private diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with { PackageId = "dev.example.settings" }),
        "A package spoofing the Settings widget ID received private diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with { PublisherId = "dev.example" }),
        "A publisher spoofing the Settings identity received private diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with
    {
        DeclaredCapabilities = ["audio.sessions.read"],
    }), "A capability-bearing worker received private diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with
    {
        RequiresAppContainer = true,
        IsolationKey = "spoofed-settings",
    }), "An installed isolated worker received private diagnostics.");

    await using var companion = new DiagnosticsWidgetProcessCompanion(
        _ => ValueTask.FromResult(GameBarAlternative.PlatformDiagnostics.PlatformDiagnosticsSnapshot.Unavailable()),
        (_, _) => ValueTask.FromResult(PlatformAuthorityRecoveryRetryResult.Refused("test_refused")),
        new WidgetProcessCompanionContext(
            WidgetWorkerIsolationPolicy.HostTrustedJobOnly, null, null));
    var pidIndex = companion.WorkerArguments.ToList().IndexOf("--diagnostics-server-pid");
    Assert.True(pidIndex >= 0 && pidIndex + 1 < companion.WorkerArguments.Count,
        "Diagnostics companion omitted its kernel-verifiable server PID.");
    Assert.Equal(
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        companion.WorkerArguments[pidIndex + 1]);
}

static ConfiguredWidget DiagnosticCandidate() => new()
{
    Id = "settings",
    PackageId = "org.gbar.firstparty.settings",
    PublisherId = "org.gbar.firstparty",
    Name = "Settings",
    InstanceId = "settings",
    WorkerExecutable = Environment.ProcessPath
        ?? throw new InvalidOperationException("Test process path is unavailable."),
};

static Task CatalogGlyphIsClosed()
{
    using var fallback = TemporaryCatalog.Create(icon: null);
    Assert.Equal(WidgetGlyph.Connection, BridgeCatalog.Load(fallback.Path).Widgets.Single().Icon);
    using var invalid = TemporaryCatalog.Create(icon: "arbitrary-svg");
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(invalid.Path));
    return Task.CompletedTask;
}

static Task InvalidThemeIsRejected()
{
    using var catalog = TemporaryCatalog.Create(invalidStyle: true);
    var exception = Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(catalog.Path));
    Assert.True(exception.Message.Contains("invalid_value", StringComparison.Ordinal),
        "Expected a stable GBSS diagnostic code.");
    Assert.True(!exception.Message.Contains(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
        "Catalog diagnostics must not disclose absolute package paths.");
    return Task.CompletedTask;
}

static Task UnsafeStylePathIsRejected()
{
    using var catalog = TemporaryCatalog.Create(styleFile: "../outside.gbss");
    var exception = Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(catalog.Path));
    Assert.True(exception.Message.Contains("package-relative", StringComparison.Ordinal),
        "Expected the catalog boundary diagnostic.");
    return Task.CompletedTask;
}

static async Task EnumerationIsLazy()
{
    await using var harness = await BridgeHarness.StartAsync();
    var response = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    Assert.Equal(BridgeMessageTypes.Widgets, response.Type);
    var widgets = response.Payload.GetProperty("widgets");
    Assert.Equal(1, widgets.GetArrayLength());
    var descriptor = widgets[0];
    Assert.SequenceEqual(["icon", "id", "instanceId", "name", "pinningSupported", "presentationGeneration", "quickActions", "runtimeGeneration"],
        descriptor.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("test-widget", descriptor.GetProperty("id").GetString());
    Assert.Equal("Test Widget", descriptor.GetProperty("name").GetString());
    Assert.Equal("test.instance", descriptor.GetProperty("instanceId").GetString());
    Assert.Equal(32, descriptor.GetProperty("runtimeGeneration").GetString()!.Length);
    Assert.Equal(32, descriptor.GetProperty("presentationGeneration").GetString()!.Length);
    Assert.Equal("music", descriptor.GetProperty("icon").GetString());
    Assert.False(descriptor.GetProperty("pinningSupported").GetBoolean(),
        "Omitted manifest pinning support must project closed.");
    var quickAction = descriptor.GetProperty("quickActions")[0];
    Assert.SequenceEqual(["actionId", "controllerButton", "id", "label", "sourceElementId"],
        quickAction.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("hover-refresh", quickAction.GetProperty("id").GetString());
    Assert.Equal("Refresh", quickAction.GetProperty("label").GetString());
    Assert.Equal("refresh", quickAction.GetProperty("actionId").GetString());
    Assert.Equal("hover.refresh", quickAction.GetProperty("sourceElementId").GetString());
    Assert.Equal("x", quickAction.GetProperty("controllerButton").GetString());
    Assert.False(descriptor.TryGetProperty("workerExecutable", out _),
        "Native descriptors must not expose worker paths.");
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static async Task RequestDispatcherCleansTerminalPaths()
{
    var successFatal = new TaskCompletionSource<Exception>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await using (var dispatcher = new BridgeRequestDispatcher(
        CancellationToken.None,
        exception => successFatal.TrySetResult(exception),
        maximumConcurrentRequests: 2))
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = dispatcher.TryDispatch(
            1, WidgetRequest("widget-a"), _ => completion.Task);
        Assert.Equal(BridgeRequestDispatchStatus.Accepted, accepted.Status);
        completion.SetResult();
        await accepted.Completion!;
        Assert.Equal(0, dispatcher.ActiveCount);
        Assert.Equal(0, dispatcher.WidgetTailCount);
        Assert.Equal(2, dispatcher.AvailableSlots);
        Assert.False(successFatal.Task.IsCompleted,
            "Successful request incorrectly canceled the bridge session.");
    }

    var failureFatal = new TaskCompletionSource<Exception>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await using (var dispatcher = new BridgeRequestDispatcher(
        CancellationToken.None,
        exception => failureFatal.TrySetResult(exception)))
    {
        var failed = dispatcher.TryDispatch(
            2, GlobalRequest(), _ => Task.FromException(new InvalidOperationException("fatal")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.Completion!);
        var fatal = await failureFatal.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("fatal", fatal.Message);
        await dispatcher.CancelAndDrainAsync();
        Assert.Equal(0, dispatcher.ActiveCount);
        Assert.Equal(0, dispatcher.WidgetTailCount);
        Assert.Equal(BridgeRequestDispatcher.MaximumConcurrentRequests,
            dispatcher.AvailableSlots);
    }

    using var session = new CancellationTokenSource();
    await using (var dispatcher = new BridgeRequestDispatcher(
        session.Token, _ => throw new InvalidOperationException(
            "Cancellation must not be fatal.")))
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var request = dispatcher.TryDispatch(3, WidgetRequest("widget-a"), async token =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException)
            {
                canceled.SetResult();
                throw;
            }
        });
        await started.Task;
        session.Cancel();
        await request.Completion!;
        await canceled.Task;
        Assert.Equal(0, dispatcher.ActiveCount);
        Assert.Equal(0, dispatcher.WidgetTailCount);
    }
}

static Task RequestClassificationIsClosed()
{
    var widget = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.GetSnapshot,
        RequestId = 1,
        Payload = BridgeJson.ToElement(new WidgetIdRequest("widget-a")),
    });
    Assert.Equal(BridgeRequestKind.GetSnapshot, widget.Kind);
    Assert.Equal("widget-a", widget.WidgetId);
    Assert.True(widget.IsKnown, "Known widget request was not classified as known.");

    var artwork = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.ResolveArtwork,
        RequestId = 6,
        Payload = BridgeJson.ToElement(new BridgeArtworkRequest(
            "widget-a", "library.art.0123456789abcdef0123456789abcdef")),
    });
    Assert.Equal(BridgeRequestKind.ResolveArtwork, artwork.Kind);
    Assert.Equal<string?>(null, artwork.WidgetId);

    var malformedArtwork = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.ResolveArtwork,
        RequestId = 7,
        Payload = BridgeJson.ToElement(new
        {
            widgetId = "widget-a",
            artworkHandle = "library.art.0123456789abcdef0123456789abcdef",
            path = @"C:\\forbidden.png",
        }),
    });
    Assert.Equal(BridgeRequestKind.Malformed, malformedArtwork.Kind);

    var global = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.ListWidgets,
        RequestId = 2,
        Payload = BridgeJson.ToElement(new { }),
    });
    Assert.Equal(BridgeRequestKind.ListWidgets, global.Kind);
    Assert.Equal<string?>(null, global.WidgetId);

    var malformed = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.Action,
        RequestId = 3,
        Payload = BridgeJson.ToElement(new
        {
            widgetId = "widget-a",
            unexpected = true,
        }),
    });
    Assert.Equal(BridgeRequestKind.Malformed, malformed.Kind);
    Assert.Equal<string?>(null, malformed.WidgetId);
    Assert.False(malformed.IsKnown,
        "Malformed payload acquired a per-widget scheduling key.");

    var invalidWidget = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.GetSnapshot,
        RequestId = 4,
        Payload = BridgeJson.ToElement(new WidgetIdRequest("../widget")),
    });
    Assert.Equal(BridgeRequestKind.Malformed, invalidWidget.Kind);
    Assert.Equal<string?>(null, invalidWidget.WidgetId);

    var unknown = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = "future-request",
        RequestId = 5,
        Payload = BridgeJson.ToElement(new { widgetId = "widget-a" }),
    });
    Assert.Equal(BridgeRequestKind.Unknown, unknown.Kind);
    Assert.Equal<string?>(null, unknown.WidgetId);
    Assert.False(unknown.IsKnown,
        "Unknown request acquired an implicit scheduling convention.");
    return Task.CompletedTask;
}

static async Task TrustedArtworkDemandIsExact()
{
    if (!OperatingSystem.IsWindows()) return;
    const string png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
        "AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";
    using var catalogFiles = TemporaryCatalog.Create(
        instanceId: "artwork.instance",
        declaredCapabilities:
        [
            PlatformCapabilities.AppLibraryReadV1,
            PlatformCapabilities.AppLibraryLaunchV1,
        ]);
    using var consentFiles = new TemporaryDirectory("gba-artwork-consent");
    var identity = new BrokerWidgetIdentity(
        "dev.test.widget", "dev.test", "artwork.instance");
    var consent = new ConsentStore(consentFiles.Path);
    await consent.SetDecisionAsync(
        identity, PlatformCapabilities.AppLibraryReadV1, ConsentDecision.Grant);
    await consent.SetDecisionAsync(
        identity, PlatformCapabilities.AppLibraryLaunchV1, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "provider-one", "stable-one", "Artwork App", AppLibraryKind.Application,
            "artwork-a"),
        new AppLibraryBackendItemSummary(
            "provider-two", "stable-two", "Second App", AppLibraryKind.Application,
            "artwork-two"),
    ]);
    var iconStarted = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseIcon = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    backend.AppLibraryIconHandler = async (_, cancellationToken) =>
    {
        iconStarted.TrySetResult();
        await releaseIcon.Task.WaitAsync(cancellationToken);
        return new AppLibraryIconSummary(png);
    };
    var catalog = BridgeCatalog.Load(catalogFiles.Path);
    var pipeName = $"gba-bridge-artwork-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(
        pipeName, catalog, 64 * 1024, consentStore: consent, platformBackend: backend);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    await using var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
    try
    {
        var lifecycle = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
        Assert.True(
            lifecycle.Type == BridgeMessageTypes.Acknowledged,
            $"Artwork lifecycle failed with {lifecycle.Payload.GetRawText()}; " +
            $"registrations={server.ArtworkRegistrationCount}.");
        var snapshotResponse = await client.RequestAsync(
            BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
        var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
            snapshotResponse.Payload.GetProperty("snapshot").GetRawText()));
        var firstHandle = Flatten(snapshot.Root).Single(
            node => node.Id == "artwork.image").ArtworkHandle!;
        Assert.True(firstHandle is { Length: 44 } &&
            firstHandle.StartsWith("library.art.", StringComparison.Ordinal),
            "Worker snapshot did not carry one bounded opaque artwork handle.");
        Assert.Equal(0, backend.AppLibraryIconCalls);
        Assert.Equal(2, backend.AppLibraryRefreshCalls);
        Assert.Equal(2, backend.AppLibraryReadCalls);
        var launched = await client.RequestAsync(
            BridgeMessageTypes.Action,
            new BridgeActionRequest(
                "test-widget", new WidgetActionEvent("artwork.launch", "artwork.launch")));
        Assert.Equal(BridgeMessageTypes.Acknowledged, launched.Type);
        Assert.Equal(1, backend.AppLibraryLaunchCalls);
        Assert.Equal("provider-one", backend.LastLaunchedAppId);

        var resolved = await client.RequestAsync(
            BridgeMessageTypes.ResolveArtwork,
            new BridgeArtworkRequest("test-widget", firstHandle));
        Assert.Equal(BridgeMessageTypes.Acknowledged, resolved.Type);
        await iconStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var concurrent = await client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
        Assert.Equal(BridgeMessageTypes.Widgets, concurrent.Type);
        releaseIcon.TrySetResult();
        var artwork = await client.ReadEventAsync(BridgeMessageTypes.Artwork);
        Assert.Equal(png, artwork.Payload.GetProperty("pngBase64").GetString());
        Assert.Equal(1, backend.AppLibraryIconCalls);

        var staleStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var staleFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.AppLibraryIconHandler = async (_, _) =>
        {
            staleStarted.TrySetResult();
            await releaseStale.Task;
            staleFinished.TrySetResult();
            return new AppLibraryIconSummary(png);
        };
        var stale = await client.RequestAsync(
            BridgeMessageTypes.ResolveArtwork,
            new BridgeArtworkRequest("test-widget", firstHandle));
        Assert.Equal(BridgeMessageTypes.Acknowledged, stale.Type);
        await staleStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        backend.SetAppLibraryBackend([
            new AppLibraryBackendItemSummary(
                "provider-one", "stable-one", "Replacement", AppLibraryKind.Application,
                "artwork-b"),
            new AppLibraryBackendItemSummary(
                "provider-two", "stable-two", "Second App", AppLibraryKind.Application,
                "artwork-two"),
        ]);
        _ = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
        _ = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
        var rotatedSnapshotResponse = await client.RequestAsync(
            BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
        var rotatedSnapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
            rotatedSnapshotResponse.Payload.GetProperty("snapshot").GetRawText()));
        var rotatedHandle = Flatten(rotatedSnapshot.Root).Single(
            node => node.Id == "artwork.image").ArtworkHandle!;
        Assert.True(rotatedHandle != firstHandle,
            "Changed trusted artwork revision reused the prior handle.");
        releaseStale.TrySetResult();
        await staleFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        _ = await client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
        Assert.Equal(0, client.PendingEventCountOfType(BridgeMessageTypes.Artwork));
        Assert.Equal(2, backend.AppLibraryIconCalls);

        var forged = await client.RequestAsync(
            BridgeMessageTypes.ResolveArtwork,
            new BridgeArtworkRequest(
                "test-widget", "library.art.00000000000000000000000000000000"));
        Assert.Equal(BridgeMessageTypes.Acknowledged, forged.Type);
        _ = await client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
        Assert.Equal(0, client.PendingEventCountOfType(BridgeMessageTypes.Artwork));
        Assert.Equal(2, backend.AppLibraryIconCalls);

        var blockedStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.AppLibraryIconHandler = async (_, _) =>
        {
            blockedStarted.TrySetResult();
            await releaseBlocked.Task;
            blockedFinished.TrySetResult();
            return new AppLibraryIconSummary(png);
        };
        var blocked = await client.RequestAsync(
            BridgeMessageTypes.ResolveArtwork,
            new BridgeArtworkRequest("test-widget", rotatedHandle));
        Assert.Equal(BridgeMessageTypes.Acknowledged, blocked.Type);
        await blockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var whileBlocked = await client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
        Assert.Equal(BridgeMessageTypes.Widgets, whileBlocked.Type);
        _ = await client.RequestAsync(BridgeMessageTypes.Stop, new { });
        await serverTask.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.False(blockedFinished.Task.IsCompleted,
            "Bridge shutdown waited for cancellation-ignoring artwork I/O.");
        releaseBlocked.TrySetResult();
        await blockedFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(3, backend.AppLibraryIconCalls);
    }
    finally
    {
        if (!client.IsTerminal && !serverTask.IsCompleted)
        {
            _ = await client.RequestAsync(BridgeMessageTypes.Stop, new { });
            await serverTask.WaitAsync(TimeSpan.FromSeconds(4));
        }
        else
        {
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(4)); }
            catch (EndOfStreamException) { }
        }
    }
}

static IEnumerable<ViewNode> Flatten(ViewNode root)
{
    yield return root;
    foreach (var child in root.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
}

static async Task RequestDispatcherOwnsWidgetOrdering()
{
    var order = new List<string>();
    var firstStarted = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirst = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await using (var dispatcher = new BridgeRequestDispatcher(
        CancellationToken.None, _ => { }, maximumConcurrentRequests: 3))
    {
        var first = dispatcher.TryDispatch(1, WidgetRequest("widget-a"), async _ =>
        {
            order.Add("first-start");
            firstStarted.SetResult();
            await releaseFirst.Task;
            order.Add("first-end");
        });
        var second = dispatcher.TryDispatch(2, WidgetRequest("widget-a"), _ =>
        {
            order.Add("second");
            return Task.CompletedTask;
        });
        var otherWidget = dispatcher.TryDispatch(3, WidgetRequest("widget-b"), _ =>
        {
            order.Add("other");
            return Task.CompletedTask;
        });
        await firstStarted.Task;
        await otherWidget.Completion!;
        Assert.False(order.Contains("second", StringComparer.Ordinal),
            "Same-widget successor ran before its predecessor completed.");
        releaseFirst.SetResult();
        await Task.WhenAll(first.Completion!, second.Completion!);
        Assert.True(
            order.IndexOf("first-end") < order.IndexOf("second"),
            "Same-widget FIFO order changed after predecessor completion.");
        Assert.Equal(0, dispatcher.ActiveCount);
        Assert.Equal(0, dispatcher.WidgetTailCount);
    }

    var fatalCount = 0;
    var successorRan = false;
    await using (var dispatcher = new BridgeRequestDispatcher(
        CancellationToken.None, _ => Interlocked.Increment(ref fatalCount)))
    {
        var releaseFailure = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var predecessor = dispatcher.TryDispatch(10, WidgetRequest("widget-a"), async _ =>
        {
            await releaseFailure.Task;
            throw new InvalidOperationException("predecessor failed");
        });
        var successor = dispatcher.TryDispatch(11, WidgetRequest("widget-a"), _ =>
        {
            successorRan = true;
            return Task.CompletedTask;
        });
        releaseFailure.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => predecessor.Completion!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => successor.Completion!);
        await dispatcher.CancelAndDrainAsync();
        Assert.False(successorRan,
            "A successor ran after its FIFO predecessor failed.");
        Assert.Equal(1, fatalCount);
        Assert.Equal(0, dispatcher.ActiveCount);
        Assert.Equal(0, dispatcher.WidgetTailCount);
    }
}

static async Task RequestDispatcherBoundsAdmission()
{
    var release = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await using var dispatcher = new BridgeRequestDispatcher(
        CancellationToken.None, _ => { }, maximumConcurrentRequests: 2);
    var first = dispatcher.TryDispatch(1, WidgetRequest("widget-a"), _ => release.Task);
    var second = dispatcher.TryDispatch(2, WidgetRequest("widget-b"), _ => release.Task);
    Assert.Equal(0, dispatcher.AvailableSlots);
    Assert.Throws<BridgeProtocolException>(() =>
        dispatcher.TryDispatch(1, GlobalRequest(), _ => Task.CompletedTask));
    var thirdRan = false;
    var saturated = dispatcher.TryDispatch(3, GlobalRequest(), _ =>
    {
        thirdRan = true;
        return Task.CompletedTask;
    });
    Assert.Equal(BridgeRequestDispatchStatus.CapacityExceeded, saturated.Status);
    Assert.Equal<Task?>(null, saturated.Completion);
    Assert.False(thirdRan, "Over-capacity handler ran without admission.");
    release.SetResult();
    await Task.WhenAll(first.Completion!, second.Completion!);
    Assert.Equal(0, dispatcher.ActiveCount);
    Assert.Equal(0, dispatcher.WidgetTailCount);
    Assert.Equal(2, dispatcher.AvailableSlots);
}

static async Task RequestDispatcherForcedDrainIsComplete()
{
    var started = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var deadline = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var starts = 0;
    var latePublications = 0;
    var fatalPublications = 0;
    await using var dispatcher = new BridgeRequestDispatcher(
        CancellationToken.None,
        _ => Interlocked.Increment(ref fatalPublications),
        maximumConcurrentRequests: 2,
        drainDeadline: _ => deadline.Task);
    Assert.True(BridgeRequestDispatcher.DrainTimeout <= TimeSpan.FromSeconds(2),
        "The production dispatcher drain deadline is not bounded.");

    var reply = dispatcher.TryDispatch(1, WidgetRequest("widget-1"), async token =>
    {
        if (Interlocked.Increment(ref starts) == 2) started.SetResult();
        await release.Task.ConfigureAwait(false); // Deliberately ignores cancellation.
        token.ThrowIfCancellationRequested();
        Interlocked.Increment(ref latePublications);
    });
    _ = dispatcher.TryDispatch(2, GlobalRequest(), async _ =>
    {
        if (Interlocked.Increment(ref starts) == 2) started.SetResult();
        await release.Task.ConfigureAwait(false); // Deliberately ignores cancellation.
        throw new InvalidOperationException("late fatal");
    });
    await started.Task;
    var drain = dispatcher.CancelAndDrainAsync();
    Assert.False(drain.IsCompleted,
        "Drain completed before its manually controlled production deadline.");
    deadline.SetResult();
    await drain;
    Assert.Equal(0, dispatcher.ActiveCount);
    Assert.Equal(0, dispatcher.WidgetTailCount);
    Assert.Equal(2, dispatcher.AvailableSlots);
    Assert.Equal(2, dispatcher.QuarantinedCount);

    var quarantineDrained = dispatcher.QuarantineDrained;
    await dispatcher.DisposeAsync();
    Assert.Equal(2, dispatcher.QuarantinedCount);
    release.SetResult();
    await reply.Completion!;
    await quarantineDrained;
    Assert.Equal(0, dispatcher.QuarantinedCount);
    Assert.Equal(0, latePublications);
    Assert.Equal(0, fatalPublications);
    Assert.Equal<Exception?>(null, dispatcher.FatalException);
}

static BridgeRequestKey WidgetRequest(string widgetId) =>
    BridgeRequestKey.Widget(BridgeRequestKind.GetSnapshot, widgetId);

static BridgeRequestKey GlobalRequest() =>
    BridgeRequestKey.Global(BridgeRequestKind.ListWidgets);

static async Task StalledAdmissionKeepsControlPlaneResponsive()
{
    if (!OperatingSystem.IsWindows()) return;
    var fixture = new StalledAdmissionFixture();
    var pipeName = $"gba-bridge-stalled-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(pipeName, fixture.Catalog, 64 * 1024);
    using var serverShutdown = new CancellationTokenSource();
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3), serverShutdown.Token);

    try
    {
        await using var connection = await RawBridgeConnection.ConnectAsync(pipeName);
        var channel = connection.Channel;

        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.GetSnapshot,
            RequestId = 2,
            Payload = BridgeJson.ToElement(new WidgetIdRequest("stalled-widget")),
        }, CancellationToken.None);
        await fixture.Started.WaitAsync(TimeSpan.FromSeconds(2));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.ListWidgets,
            RequestId = 3,
            Payload = BridgeJson.ToElement(new { }),
        }, CancellationToken.None);
        var listed = await channel.ReadAsync(CancellationToken.None).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));
        stopwatch.Stop();
        Assert.Equal(3L, listed.RequestId);
        Assert.Equal(BridgeMessageTypes.Widgets, listed.Type);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1.5),
            $"ListWidgets waited behind stalled admission for " +
            $"{stopwatch.Elapsed.TotalMilliseconds:0} ms.");

        for (var requestId = 5L; requestId <= 19L; requestId++)
        {
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.GetSnapshot,
                RequestId = requestId,
                Payload = BridgeJson.ToElement(new WidgetIdRequest("stalled-widget")),
            }, CancellationToken.None);
        }
        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.ListWidgets,
            RequestId = 20,
            Payload = BridgeJson.ToElement(new { }),
        }, CancellationToken.None);
        var saturated = await channel.ReadAsync(CancellationToken.None).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(20L, saturated.RequestId);
        Assert.Equal(BridgeMessageTypes.Error, saturated.Type);
        Assert.Equal("bridge_busy",
            saturated.Payload.GetProperty("code").GetString());

        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Stop,
            RequestId = 21,
            Payload = BridgeJson.ToElement(new { }),
        }, CancellationToken.None);
        var stopped = await channel.ReadAsync(CancellationToken.None).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(21L, stopped.RequestId);
        Assert.Equal(BridgeMessageTypes.Acknowledged, stopped.Type);
        await fixture.Canceled.WaitAsync(TimeSpan.FromSeconds(2));
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, server.ResidencyBudget.ApplicationWorkers);
    }
    finally
    {
        serverShutdown.Cancel();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (OperationCanceledException) { }
    }
}

static async Task DuplicatePendingRequestIdsFailClosed()
{
    if (!OperatingSystem.IsWindows()) return;
    var fixture = new StalledAdmissionFixture();
    var pipeName = $"gba-bridge-duplicate-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(pipeName, fixture.Catalog, 64 * 1024);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    await using var connection = await RawBridgeConnection.ConnectAsync(pipeName);

    await connection.Channel.WriteAsync(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.GetSnapshot,
        RequestId = 2,
        Payload = BridgeJson.ToElement(new WidgetIdRequest("stalled-widget")),
    }, CancellationToken.None);
    await fixture.Started.WaitAsync(TimeSpan.FromSeconds(2));
    await connection.Channel.WriteAsync(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.ListWidgets,
        RequestId = 2,
        Payload = BridgeJson.ToElement(new { }),
    }, CancellationToken.None);

    var exception = await Assert.ThrowsAsync<BridgeProtocolException>(
        () => serverTask.WaitAsync(TimeSpan.FromSeconds(3)));
    Assert.True(exception.Message.Contains("reused", StringComparison.Ordinal),
        "Duplicate active request ID lost its stable fail-closed diagnostic.");
    await fixture.Canceled.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(0, server.ResidencyBudget.ApplicationWorkers);
}

static async Task PipelinedWidgetRequestsStayOrdered()
{
    await using var harness = await BridgeHarness.StartAsync();
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));

    var responses = await harness.Client.PipelineAsync(
        (BridgeMessageTypes.Action, new BridgeActionRequest(
            "test-widget", new WidgetActionEvent("ordered.first", "button"))),
        (BridgeMessageTypes.Action, new BridgeActionRequest(
            "test-widget", new WidgetActionEvent("ordered.second", "button"))),
        (BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget")));

    Assert.SequenceEqual(
        [BridgeMessageTypes.Acknowledged, BridgeMessageTypes.Acknowledged,
            BridgeMessageTypes.Snapshot],
        responses.Select(response => response.Type));
    Assert.Equal("enqueued", responses[0].Payload.GetProperty("admission").GetString());
    Assert.Equal("enqueued", responses[1].Payload.GetProperty("admission").GetString());
    _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    var completed = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        completed.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal("first,second", FindNode(snapshot.Root, "busy-button").Text);
}

static async Task InstalledWidgetsJoinCatalog()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-installed");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.enabled", enabled: true,
        icon: WidgetGlyph.Music);
    await InstallWidgetAsync(catalog, temporary.Path, "dev.example.disabled", enabled: false);

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    Assert.Equal(0, load.Warnings.Count);
    Assert.SequenceEqual(["test-widget", "dev.example.enabled"],
        load.Catalog.Widgets.Select(widget => widget.Id));
    var installed = load.Catalog.GetConfigured("dev.example.enabled");
    var installedVersion = (await catalog.DiscoverAsync()).Widgets
        .Single(widget => widget.Id == "dev.example.enabled")
        .ActiveVersion;
    var installedManifest = installedVersion.Manifest;
    Assert.Equal(64, installed.MemoryLimitMb);
    Assert.Equal(WidgetGlyph.Music, installed.Icon);
    Assert.Equal(Environment.ProcessPath, installed.WorkerExecutable);
    Assert.Equal("styles/default.gbss", installed.StyleFile);
    Assert.True(installed.RequiresAppContainer,
        "Installed community workers must require AppContainer isolation.");
    Assert.True(!string.IsNullOrWhiteSpace(installed.IsolationKey),
        "Installed community workers need a stable host-owned isolation identity.");
    Assert.Equal(
        InstalledWidgetAuthority.PublisherId(installedVersion),
        installed.PublisherId);
    Assert.True(!string.Equals(
            installedManifest.Publisher,
            installed.PublisherId,
            StringComparison.Ordinal),
        "Installed authority trusted the manifest publisher label directly.");
    Assert.Equal(0, installed.ReadOnlyPaths.Count);
    Assert.True(installed.ContentLeaseFactory is not null,
        "Installed workers retained a broad package-root grant instead of an exact launch lease.");
    Assert.Equal(
        InstalledPackageLaunchLease.MaximumReadOnlyDirectories,
        WidgetProcessContentLimits.MaximumDirectories);
    using (var contentLease = installed.ContentLeaseFactory!(CancellationToken.None))
    {
        var roots = contentLease.Targets.Where(target => target.Target.Kind ==
            AppContainerAuthorityTargetKind.AuthorityRootDirectory).ToArray();
        var directories = contentLease.Targets.Where(target => target.Target.Kind ==
            AppContainerAuthorityTargetKind.VerifiedDirectory).ToArray();
        var files = contentLease.Targets.Where(target => target.Target.Kind ==
            AppContainerAuthorityTargetKind.VerifiedFile).ToArray();
        Assert.SequenceEqual(
            [installed.WorkerArguments[1]], roots.Select(target => target.Target.Path));
        Assert.Equal(installedVersion.VerifiedFiles.Count, files.Length);
        Assert.True(string.Equals(
                roots.Single().Target.Path,
                installed.WorkerArguments[1],
                StringComparison.OrdinalIgnoreCase),
            "The exact content lease omitted package-root traversal authority.");
        Assert.Equal(roots.Length + directories.Length + files.Length,
            contentLease.Targets.Count);
        Assert.True(contentLease.Targets.All(target =>
                target.ObjectIdentity.HasValidFormat()),
            "The bridge returned malformed object identity evidence.");
        var identitiesByPath = new Dictionary<string, AppContainerAuthorityObjectIdentity>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var target in contentLease.Targets)
        {
            if (identitiesByPath.TryGetValue(target.Target.Path, out var prior))
                Assert.Equal(prior, target.ObjectIdentity);
            else
                identitiesByPath.Add(target.Target.Path, target.ObjectIdentity);
        }
    }
    Assert.SequenceEqual(
        ["--package-root", installed.WorkerArguments[1], "--widget-assembly",
         installed.WorkerArguments[3], "--widget-type", "Example.EnabledWidget"],
        installed.WorkerArguments);
    Assert.True(Path.IsPathFullyQualified(installed.WorkerArguments[1]),
        "Installed package root must be canonical before worker launch.");
    Assert.True(Path.IsPathFullyQualified(installed.WorkerArguments[3]),
        "Installed assembly path must be canonical before worker launch.");
    Assert.Equal(WidgetResidencyPolicies.KeepAlive, installed.ResidencyPolicy.Mode);
}

static async Task InstalledLaunchAdmissionRejectsRace()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-launch-race");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.launch-race", enabled: true);
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    var configured = load.Catalog.GetConfigured("dev.example.launch-race");
    Assert.True(configured.ContentLeaseFactory is not null,
        "Installed descriptor omitted its launch-admission factory.");

    var lateDependency = Path.Combine(
        configured.WorkerArguments[1], "payload", "LateDependency.dll");
    await File.WriteAllBytesAsync(lateDependency, [0x4d, 0x5a]);
    var exception = Assert.Throws<WidgetProcessAdmissionException>(() =>
        configured.ContentLeaseFactory!(CancellationToken.None));
    Assert.True(exception.Message.Contains("content changed", StringComparison.Ordinal),
        "Launch admission lost its stable sanitized integrity diagnostic.");
    Assert.True(!exception.Message.Contains(temporary.Path, StringComparison.OrdinalIgnoreCase),
        "Launch admission exposed a host package path.");
}

static async Task InstalledContentGenerationIsIsolated()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-content-generation");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.content-generation",
        enabled: true,
        version: "1.0.0");
    var firstLoad = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    var first = firstLoad.Catalog.GetConfigured("dev.example.content-generation");

    await catalog.SetEnabledAsync("dev.example.content-generation", false);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.content-generation",
        enabled: false,
        version: "1.1.0");
    await catalog.SetActiveVersionAsync(
        "dev.example.content-generation", new Version(1, 1, 0));
    await catalog.SetEnabledAsync("dev.example.content-generation", true);
    var secondLoad = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    var second = secondLoad.Catalog.GetConfigured("dev.example.content-generation");

    Assert.True(!string.Equals(first.IsolationKey, second.IsolationKey, StringComparison.Ordinal),
        "A new verified content generation reused the prior AppContainer identity.");
    Assert.True(first.ContentLeaseFactory is not null && second.ContentLeaseFactory is not null,
        "Content-generation descriptors omitted exact launch admission.");
}

static async Task InstalledResidencyPolicyIsCarried()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-installed-residency");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.idle-unload",
        enabled: true,
        residencyPolicy: new WidgetResidencyPolicy
        {
            Mode = WidgetResidencyPolicies.UnloadAfterIdle,
            IdleSeconds = 300,
        });
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    var configured = load.Catalog.GetConfigured("dev.example.idle-unload");
    Assert.Equal(WidgetResidencyPolicies.UnloadAfterIdle, configured.ResidencyPolicy.Mode);
    Assert.Equal(300, configured.ResidencyPolicy.IdleSeconds);
    Assert.True(configured.RequiresAppContainer,
        "Residency metadata must not weaken installed worker isolation.");
}

static async Task InstalledCapabilityDeclarationsAreClosed()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-capabilities");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.audio",
        enabled: true,
        permissions: [PlatformCapabilities.AudioSessionsReadV1]);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.unknown",
        enabled: true,
        permissions: ["system.unsupported.control.v1"]);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.apps",
        enabled: true,
        permissions:
        [
            PlatformCapabilities.AppLibraryReadV1,
            PlatformCapabilities.AppLibraryLaunchV1,
        ]);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.companion",
        enabled: true,
        permissions:
        [
            "network.loopback:13091",
            PlatformCapabilities.PrivateSecretsV1,
        ]);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.forged-state",
        enabled: true,
        permissions: [PlatformCapabilities.PrivateStateV1]);

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);

    Assert.SequenceEqual(
        ["test-widget", "dev.example.audio", "dev.example.apps", "dev.example.companion"],
        load.Catalog.Widgets.Select(widget => widget.Id));
    Assert.SequenceEqual(
        [PlatformCapabilities.AudioSessionsReadV1],
        load.Catalog.GetConfigured("dev.example.audio").DeclaredCapabilities);
    Assert.SequenceEqual(
        [PlatformCapabilities.AppLibraryLaunchV1, PlatformCapabilities.AppLibraryReadV1],
        load.Catalog.GetConfigured("dev.example.apps").DeclaredCapabilities);
    Assert.SequenceEqual(
        ["network.loopback:13091", PlatformCapabilities.PrivateSecretsV1],
        load.Catalog.GetConfigured("dev.example.companion").DeclaredCapabilities);
    Assert.Equal(2, load.Warnings.Count);
    Assert.True(load.Warnings.All(warning =>
            warning.Contains("unsupported capability", StringComparison.Ordinal)),
        "Unknown and host-granted manifest capabilities need safe closed-vocabulary warnings.");
}

static async Task PrivateStateAuthorityIsHostSynthesized()
{
    using var temporary = new TemporaryDirectory("gba-bridge-state-authority");
    var context = new WidgetProcessCompanionContext(
        WidgetWorkerIsolationPolicy.HostTrustedJobOnly, null, null);
    await using var companion = new BrokerWidgetProcessCompanion(
        "dev.example.widget", "dev.example", "default", [],
        new ConsentStore(temporary.Path), new SimulatedPlatformBrokerBackend(), context);
    Assert.Equal(0, companion.DeclaredCapabilities.Count);
    Assert.SequenceEqual([PlatformCapabilities.PrivateStateV1],
        companion.HostGrantedCapabilities);
    Assert.True(companion.WorkerArguments.Contains("--broker-pipe", StringComparer.Ordinal),
        "The host-granted state service did not create an authenticated broker channel.");

    Assert.Throws<BrokerException>(() => _ = new BrokerWidgetProcessCompanion(
        "dev.example.widget", "dev.example", "default",
        [PlatformCapabilities.PrivateStateV1],
        new ConsentStore(Path.Combine(temporary.Path, "forged")),
        new SimulatedPlatformBrokerBackend(), context));
}

static async Task TamperedInstalledCatalogFailsSoft()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-tampered");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    var installed = await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.tampered", enabled: true);
    await File.WriteAllTextAsync(Path.Combine(installed.InstallPath, "manifest.json"), "{}");

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    Assert.SequenceEqual(["test-widget"], load.Catalog.Widgets.Select(widget => widget.Id));
    Assert.Equal(1, load.Warnings.Count);
    Assert.True(load.Warnings[0].Contains("invalid_manifest", StringComparison.Ordinal),
        "Expected a bounded stable installed-catalog warning.");
}

static async Task InvalidInstalledStyleFailsSoft()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-invalid-style");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.badstyle", enabled: true,
        styleSource: "button { color: definitely-not-a-color; }");

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    Assert.SequenceEqual(["test-widget"], load.Catalog.Widgets.Select(widget => widget.Id));
    Assert.Equal(1, load.Warnings.Count);
    Assert.True(load.Warnings[0].Contains("invalid styles", StringComparison.Ordinal),
        "Expected invalid installed styles to be isolated to their package.");
}

static async Task CatalogMonitorIsRevisionedAndLastGood()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-live-catalog");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(catalog, temporary.Path, "dev.example.alpha", enabled: true);
    await InstallWidgetAsync(catalog, temporary.Path, "dev.example.beta", enabled: true);
    var initial = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, initial.Catalog);
    var revisions = new List<long>();
    monitor.Changed += (_, change) => revisions.Add(change.Revision);

    var unchanged = await monitor.ReloadNowAsync();
    Assert.False(unchanged.Published, "An equivalent reload must not publish.");
    Assert.Equal(0L, unchanged.Revision);

    await catalog.SetOrderAsync(["dev.example.beta", "dev.example.alpha"]);
    var reordered = await monitor.ReloadNowAsync();
    Assert.True(reordered.Published, "Installed order change was not published.");
    Assert.Equal(1L, reordered.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.beta", "dev.example.alpha"],
        reordered.Current.Widgets.Select(widget => widget.Id));

    var replay = await monitor.ReloadNowAsync();
    Assert.False(replay.Published, "Equivalent catalog replay advanced the revision.");
    Assert.Equal(1L, replay.Revision);

    await catalog.SetEnabledAsync("dev.example.beta", false);
    var disabled = await monitor.ReloadNowAsync();
    Assert.True(disabled.Published, "Disable change was not published.");
    Assert.Equal(2L, disabled.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.alpha"],
        disabled.Current.Widgets.Select(widget => widget.Id));
    var validState = await File.ReadAllBytesAsync(Path.Combine(catalogRoot, "catalog-state.json"));
    var validTrusted = await File.ReadAllBytesAsync(trusted.Path);

    await File.WriteAllTextAsync(trusted.Path, "{");
    var invalidTrusted = await monitor.ReloadNowAsync();
    Assert.True(invalidTrusted.RetainedLastGood, "Invalid trusted catalog replaced last-good state.");
    Assert.Equal(2L, invalidTrusted.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.alpha"],
        invalidTrusted.Current.Widgets.Select(widget => widget.Id));

    await File.WriteAllBytesAsync(trusted.Path, validTrusted);
    await File.WriteAllTextAsync(Path.Combine(catalogRoot, "catalog-state.json"), "{");
    var invalidInstalled = await monitor.ReloadNowAsync();
    Assert.False(invalidInstalled.RetainedLastGood,
        "Invalid installed state retained stale community package authority.");
    Assert.True(invalidInstalled.Published, "Invalid installed state did not publish trusted-only state.");
    Assert.Equal(3L, invalidInstalled.Revision);
    Assert.SequenceEqual(["test-widget"], invalidInstalled.Current.Widgets.Select(widget => widget.Id));
    await File.WriteAllBytesAsync(Path.Combine(catalogRoot, "catalog-state.json"), validState);

    var restored = await monitor.ReloadNowAsync();
    Assert.True(restored.Published, "Restored installed state was not published.");
    Assert.Equal(4L, restored.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.alpha"],
        restored.Current.Widgets.Select(widget => widget.Id));

    var watched = new TaskCompletionSource<BridgeCatalogChanged>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    monitor.Changed += (_, change) =>
    {
        if (change.Revision >= 5) watched.TrySetResult(change);
    };
    monitor.Start();
    await catalog.SetEnabledAsync("dev.example.beta", true);
    var fileSystemChange = await watched.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(5L, fileSystemChange.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.beta", "dev.example.alpha"],
        fileSystemChange.Catalog.Widgets.Select(widget => widget.Id));
    Assert.SequenceEqual([1L, 2L, 3L, 4L, 5L], revisions);
}

static async Task InstalledPackageTamperRetiresLiveWorker()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-tamper-retire");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    var installed = await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.tamper", enabled: true);
    var initial = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, initial.Catalog);
    var pipeName = $"gba-bridge-tamper-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(
        pipeName, initial.Catalog, 64 * 1024, catalogMonitor: monitor);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    await using var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
    try
    {
        var started = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("dev.example.tamper", WidgetLifecycleState.Visible));
        Assert.Equal(BridgeMessageTypes.Acknowledged, started.Type);
        Assert.Equal(1, server.RunningWorkerCount);

        var entrypoint = Path.Combine(installed.InstallPath, "payload", "Widget.dll");
        var payloadDirectory = Path.GetDirectoryName(entrypoint)!;
        var movedPayloadDirectory = payloadDirectory + ".moved";
        _ = await Assert.ThrowsAsync<IOException>(() =>
            File.AppendAllTextAsync(entrypoint, "tampered"));
        _ = Assert.Throws<IOException>(() =>
            Directory.Move(payloadDirectory, movedPayloadDirectory));
        Assert.Equal(1, server.RunningWorkerCount);

        await catalog.SetEnabledAsync("dev.example.tamper", false);
        var disabled = await monitor.ReloadNowAsync();
        Assert.True(disabled.Published, "Disable did not retire installed authority.");
        Assert.SequenceEqual(["test-widget"],
            disabled.Current.Widgets.Select(widget => widget.Id));
        await WaitUntilAsync(() =>
        {
            if (server.RunningWorkerCount != 0) return false;
            try
            {
                File.AppendAllText(entrypoint, "tampered");
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }, TimeSpan.FromSeconds(3));
        Directory.Move(payloadDirectory, movedPayloadDirectory);
        Directory.Move(movedPayloadDirectory, payloadDirectory);

        var failedClosed = await monitor.ReloadNowAsync();
        Assert.False(failedClosed.RetainedLastGood, "Tampered package retained stale authority.");
        Assert.SequenceEqual(["test-widget"],
            failedClosed.Current.Widgets.Select(widget => widget.Id));
        Assert.Equal(0, server.RunningWorkerCount);

        var changed = await client.ReadEventAsync(BridgeMessageTypes.CatalogChanged);
        Assert.Equal(disabled.Revision, changed.Payload.GetProperty("revision").GetInt64());
        var listed = await client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
        Assert.SequenceEqual(["test-widget"], listed.Payload.GetProperty("widgets")
            .EnumerateArray().Select(widget => widget.GetProperty("id").GetString()!));
        var relaunch = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("dev.example.tamper", WidgetLifecycleState.Visible));
        Assert.Equal(BridgeMessageTypes.Error, relaunch.Type);
        Assert.Equal(0, server.RunningWorkerCount);
    }
    finally
    {
        await client.RequestAsync(BridgeMessageTypes.Stop, new { });
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }
}

static async Task CatalogMonitorStartupCatchUp()
{
    using var trusted = TemporaryCatalog.Create(name: "Before Watch");
    using var changed = TemporaryCatalog.Create(name: "Changed Before Watch");
    using var temporary = new TemporaryDirectory("gba-bridge-catalog-catch-up");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var initial = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, initial.Catalog);
    await File.WriteAllBytesAsync(trusted.Path, await File.ReadAllBytesAsync(changed.Path));

    var published = new TaskCompletionSource<BridgeCatalogChanged>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    monitor.Changed += (_, change) => published.TrySetResult(change);
    monitor.Start();
    var catchUp = await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(1L, catchUp.Revision);
    Assert.Equal("Changed Before Watch", catchUp.Catalog.Widgets.Single().Name);
}

static async Task CatalogReconciliationPreservesCompatibleWorkers()
{
    await using var harness = await BridgeHarness.StartAsync();
    var started = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, started.Type);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var initial = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    var initialDescriptor = initial.Payload.GetProperty("widgets")[0];
    var initialRuntime = initialDescriptor.GetProperty("runtimeGeneration").GetString();
    var initialPresentation = initialDescriptor.GetProperty("presentationGeneration").GetString();

    using var renamedSource = TemporaryCatalog.Create(
        name: "Renamed Widget",
        styleSource: "button { color: #ffffff; font-size: 23px; }");
    var renamed = BridgeCatalog.Load(renamedSource.Path);
    harness.Server.ApplyCatalog(renamed, revision: 1);
    var renamedEvent = await harness.Client.ReadEventAsync(BridgeMessageTypes.CatalogChanged);
    Assert.Equal(1L, renamedEvent.Payload.GetProperty("revision").GetInt64());
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var renamedList = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    var renamedDescriptor = renamedList.Payload.GetProperty("widgets")[0];
    Assert.Equal("Renamed Widget", renamedDescriptor.GetProperty("name").GetString());
    Assert.Equal(initialRuntime, renamedDescriptor.GetProperty("runtimeGeneration").GetString());
    Assert.True(initialPresentation != renamedDescriptor.GetProperty("presentationGeneration").GetString(),
        "Descriptor-only change did not advance presentation generation.");
    var refreshedSnapshot = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(23D, refreshedSnapshot.Payload.GetProperty("renderStyles")
        .GetProperty("button").GetProperty("base").GetProperty("font-size")
        .GetProperty("number").GetDouble());
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    harness.Server.ApplyCatalog(renamed, revision: 1);
    _ = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    Assert.Equal(0, harness.Client.PendingEventCount);
    using var staleSource = TemporaryCatalog.Create(instanceId: "stale.instance");
    harness.Server.ApplyCatalog(BridgeCatalog.Load(staleSource.Path), revision: 0);
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    using var capabilitySource = TemporaryCatalog.Create(
        name: "Renamed Widget",
        declaredCapabilities: [PlatformCapabilities.AudioSessionsReadV1]);
    var capabilityChanged = BridgeCatalog.Load(capabilitySource.Path);
    harness.Server.ApplyCatalog(capabilityChanged, revision: 2);
    var changedEvent = await harness.Client.ReadEventAsync(BridgeMessageTypes.CatalogChanged);
    Assert.Equal(2L, changedEvent.Payload.GetProperty("revision").GetInt64());
    Assert.Equal(0, harness.Server.RunningWorkerCount);
    var changedList = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    Assert.Equal(0, harness.Server.RunningWorkerCount);
    Assert.True(initialRuntime != changedList.Payload.GetProperty("widgets")[0]
        .GetProperty("runtimeGeneration").GetString(),
        "Worker-affecting declaration change did not advance runtime generation.");
}

static async Task<InstalledWidgetVersion> InstallWidgetAsync(
    GameBarAlternative.WidgetCatalog.WidgetCatalog catalog,
    string packageDirectory,
    string id,
    bool enabled,
    string styleSource = "button { color: #abcdef; }",
    IReadOnlyList<string>? permissions = null,
    WidgetResidencyPolicy? residencyPolicy = null,
    WidgetGlyph icon = WidgetGlyph.Connection,
    string version = "1.0.0")
{
    var packagePath = Path.Combine(packageDirectory, $"{id}-{version}.gbarwidget");
    var manifest = new WidgetManifest
    {
        Id = id,
        Publisher = "dev.example",
        Name = id.EndsWith("enabled", StringComparison.Ordinal) ? "Enabled Widget" : "Test Widget",
        Version = version,
        HostApi = new HostApiRange("1.0", 1),
        Entrypoint = new WidgetEntrypoint(
            "dotnet-worker", "payload/Widget.dll", "Example.EnabledWidget"),
        Presentation = new WidgetPresentation(icon),
        Permissions = permissions ?? [],
        OptionalPermissions = [],
        BackgroundPolicy = residencyPolicy is null ? "none" : null,
        ResidencyPolicy = residencyPolicy,
        ResourceRequest = new WidgetResourceRequest(256, 60),
        Architectures = [System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64"],
    };
    await using (var stream = new FileStream(packagePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
    {
        WriteArchiveEntry(archive, "manifest.json", ManifestJson.Serialize(manifest));
        WriteArchiveEntry(archive, "payload/Widget.dll", [0x4d, 0x5a]);
        WriteArchiveEntry(archive, "styles/default.gbss",
            System.Text.Encoding.UTF8.GetBytes(styleSource));
    }
    var installed = await catalog.InstallAsync(packagePath);
    if (enabled) await catalog.SetEnabledAsync(id, true);
    return installed;
}

static void WriteArchiveEntry(ZipArchive archive, string path, ReadOnlySpan<byte> content)
{
    var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
    using var output = entry.Open();
    output.Write(content);
}

static async Task PlatformAppearanceIsLazy()
{
    await using var harness = await BridgeHarness.StartAsync(withAppearance: true);
    var response = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    Assert.Equal(BridgeMessageTypes.PlatformAppearance, response.Type);
    Assert.SequenceEqual(
        ["backdropOpacity", "boldText", "contrast", "interfaceScale", "motion", "revision", "shellStyles", "textScale", "themeId", "themeVersion", "transparency"],
        response.Payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("dev.example.bridge", response.Payload.GetProperty("themeId").GetString());
    Assert.Equal("1.0.0", response.Payload.GetProperty("themeVersion").GetString());
    Assert.Equal(1.1D, response.Payload.GetProperty("interfaceScale").GetDouble());
    Assert.Equal(1.2D, response.Payload.GetProperty("textScale").GetDouble());
    Assert.Equal(0.7D, response.Payload.GetProperty("backdropOpacity").GetDouble());
    Assert.Equal("reduced", response.Payload.GetProperty("motion").GetString());
    Assert.Equal("high", response.Payload.GetProperty("contrast").GetString());
    Assert.Equal(true, response.Payload.GetProperty("boldText").GetBoolean());
    Assert.Equal("reduced", response.Payload.GetProperty("transparency").GetString());
    var shellStyles = response.Payload.GetProperty("shellStyles");
    Assert.Equal(12, shellStyles.EnumerateObject().Count());
    Assert.True(shellStyles.GetProperty("tray-item:focused")
        .TryGetProperty("outline-color", out _), "Focused tray style was not resolved.");
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static async Task UserThemeOverridesWidgetStyles()
{
    await using var harness = await BridgeHarness.StartAsync(withAppearance: true);
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Snapshot, response.Type);
    var button = response.Payload.GetProperty("renderStyles").GetProperty("button").GetProperty("base");
    Assert.Equal("#2468ac", button.GetProperty("color").GetProperty("text").GetString());
    Assert.Equal(0.55D, button.GetProperty("opacity").GetProperty("number").GetDouble());
}

static async Task AppearanceReloadIsLastGood()
{
    await using var harness = await BridgeHarness.StartAsync(withAppearance: true);
    var before = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    var revision = before.Payload.GetProperty("revision").GetInt64();
    Assert.Equal(0, harness.Server.RunningWorkerCount);

    await harness.Appearance!.WriteThemeAsync("button { color: definitely-not-a-color; }");
    var invalid = await harness.Appearance.Service.ReloadNowAsync();
    Assert.False(invalid.Published, "Invalid appearance unexpectedly replaced the last-good snapshot.");
    Assert.Equal(revision, invalid.Current.Revision);
    var retained = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    Assert.Equal(revision, retained.Payload.GetProperty("revision").GetInt64());

    await harness.Appearance.WriteThemeAsync("title { color: #abcdef; } button { color: #13579b; }");
    var valid = await harness.Appearance.Service.ReloadNowAsync();
    Assert.True(valid.Published, "Valid appearance reload did not publish.");
    var changed = await harness.Client.ReadEventAsync(BridgeMessageTypes.AppearanceChanged);
    Assert.Equal(valid.Current.Revision, changed.Payload.GetProperty("revision").GetInt64());
    var after = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    Assert.Equal(valid.Current.Revision, after.Payload.GetProperty("revision").GetInt64());
    Assert.Equal("#abcdef", after.Payload.GetProperty("shellStyles").GetProperty("title")
        .GetProperty("color").GetProperty("text").GetString());
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static async Task LifecycleIsExplicit()
{
    await using var harness = await BridgeHarness.StartAsync();
    var inactive = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
    Assert.Equal(BridgeMessageTypes.Acknowledged, inactive.Type);
    Assert.Equal(0, harness.Server.RunningWorkerCount);

    var active = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(BridgeMessageTypes.Acknowledged, active.Type);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var visible = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, visible.Type);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var deactivated = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
    Assert.Equal(BridgeMessageTypes.Acknowledged, deactivated.Type);
}

static async Task SuspendWhenHiddenIsLogical()
{
    await using var harness = await BridgeHarness.StartAsync(residencyPolicy:
        new WidgetResidencyPolicy { Mode = WidgetResidencyPolicies.SuspendWhenHidden });
    var cold = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Error, cold.Type);
    Assert.Equal(0, harness.Server.RunningWorkerCount);

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    var visibleResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var visible = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        visibleResponse.Payload.GetProperty("snapshot").GetRawText()));
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));

    var hiddenAction = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest("test-widget", new WidgetActionEvent("refresh", "button")));
    Assert.Equal(BridgeMessageTypes.Error, hiddenAction.Type);
    var cachedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var cached = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        cachedResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(visible.Sequence, cached.Sequence);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
}

static async Task IdleUnloadIsPolicyDriven()
{
    var policy = new WidgetResidencyPolicy
    {
        Mode = WidgetResidencyPolicies.UnloadAfterIdle,
        IdleSeconds = WidgetResidencyPolicies.MinimumIdleSeconds,
    };
    await using var harness = await BridgeHarness.StartAsync(residencyPolicy: policy);
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    var firstResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var first = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        firstResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
    await Task.Delay(TimeSpan.FromMilliseconds(250));
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    await Task.Delay(TimeSpan.FromMilliseconds(250));
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
    await WaitUntilAsync(() => harness.Server.RunningWorkerCount == 0,
        TimeSpan.FromSeconds(WidgetResidencyPolicies.MinimumIdleSeconds + 3));
    Assert.Equal(0, harness.Server.ResidencyBudget.ApplicationWorkers);
    Assert.Equal(0, harness.Server.ResidencyBudget.ApplicationMemoryMb);

    var cachedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var cached = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        cachedResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(first.Sequence, cached.Sequence);
    Assert.Equal(0, harness.Server.RunningWorkerCount);

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    Assert.Equal(1, harness.Server.ResidencyBudget.ApplicationWorkers);
    var resumedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var resumed = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        resumedResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(first.WidgetInstanceId, resumed.WidgetInstanceId);
    // Leave a fresh idle timer pending. Harness disposal must cancel it,
    // serialize with teardown, and release the resumed worker without waiting
    // for the manifest duration.
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
}

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!condition())
    {
        if (DateTime.UtcNow >= deadline)
            throw new TimeoutException("Condition was not reached before the timeout.");
        await Task.Delay(50);
    }
}

static async Task ForceReloadRestoresLifecycle()
{
    await using var harness = await BridgeHarness.StartAsync();
    foreach (var state in new[]
             {
                 WidgetLifecycleState.Visible,
                 WidgetLifecycleState.Interactive,
             })
    {
        var lifecycle = await harness.Client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("test-widget", state));
        Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);

        var beforeResponse = await harness.Client.RequestAsync(
            BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
        var before = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
            beforeResponse.Payload.GetProperty("snapshot").GetRawText()));
        _ = await harness.Client.RequestAsync(
            BridgeMessageTypes.Action,
            new BridgeActionRequest("test-widget", new WidgetActionEvent("refresh", "button")));
        _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
        var advancedResponse = await harness.Client.RequestAsync(
            BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
        var advanced = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
            advancedResponse.Payload.GetProperty("snapshot").GetRawText()));
        Assert.True(advanced.Sequence > before.Sequence,
            "The pre-reload worker did not advance its snapshot generation.");

        var restarted = await harness.Client.RequestAsync(
            BridgeMessageTypes.RestartWidget, new WidgetIdRequest("test-widget"));
        Assert.Equal(BridgeMessageTypes.Acknowledged, restarted.Type);
        Assert.Equal("test-widget", restarted.Payload.GetProperty("widgetId").GetString());
        Assert.Equal(state.ToString().ToLowerInvariant(),
            restarted.Payload.GetProperty("state").GetString());
        Assert.Equal(1, harness.Server.RunningWorkerCount);

        var freshResponse = await harness.Client.RequestAsync(
            BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
        var fresh = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
            freshResponse.Payload.GetProperty("snapshot").GetRawText()));
        Assert.True(fresh.Sequence < advanced.Sequence,
            "Force reload retained the prior worker's snapshot generation.");
    }
}

static async Task ForceReloadClearsCachedAuthority()
{
    await using var harness = await BridgeHarness.StartAsync(residencyPolicy:
        new WidgetResidencyPolicy { Mode = WidgetResidencyPolicies.SuspendWhenHidden });
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    var oldResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var old = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        oldResponse.Payload.GetProperty("snapshot").GetRawText()));
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));

    var restarted = await harness.Client.RequestAsync(
        BridgeMessageTypes.RestartWidget, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Acknowledged, restarted.Type);
    Assert.Equal("background", restarted.Payload.GetProperty("state").GetString());
    Assert.Equal(0, harness.Server.RunningWorkerCount);
    var staleSnapshot = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Error, staleSnapshot.Type);

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    var freshResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var fresh = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        freshResponse.Payload.GetProperty("snapshot").GetRawText()));
    var staleInput = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            SnapshotSequence: old.Sequence + 1,
            Sequence: 700,
            MonotonicTimestampMicroseconds: 700)));
    Assert.Equal(BridgeMessageTypes.Error, staleInput.Type);
    Assert.True(fresh.Sequence > 0, "Fresh snapshot was not rendered after reload.");
}

static async Task ForceReloadDrainsHungAction()
{
    await using var harness = await BridgeHarness.StartAsync();
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    var admitted = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest("test-widget", new WidgetActionEvent("hang", "button")));
    Assert.Equal(BridgeMessageTypes.Acknowledged, admitted.Type);
    Assert.Equal("enqueued", admitted.Payload.GetProperty("admission").GetString());
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    var restarted = await harness.Client.RequestAsync(
        BridgeMessageTypes.RestartWidget, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Acknowledged, restarted.Type);
    Assert.Equal("interactive", restarted.Payload.GetProperty("state").GetString());
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var snapshot = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Snapshot, snapshot.Type);
}

static async Task ForceReloadRejectsUnknownWidget()
{
    await using var harness = await BridgeHarness.StartAsync();
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.RestartWidget, new WidgetIdRequest("unknown-widget"));
    Assert.Equal(BridgeMessageTypes.Error, response.Type);
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static async Task RuntimeOwnedLifecycleStatesAreRejected()
{
    await using var harness = await BridgeHarness.StartAsync();
    foreach (var state in new[] { WidgetLifecycleState.Created, WidgetLifecycleState.Destroying })
    {
        var response = await harness.Client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("test-widget", state));
        Assert.Equal(BridgeMessageTypes.Error, response.Type);
    }
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static async Task SnapshotAndQuickAction()
{
    await using var harness = await BridgeHarness.StartAsync();
    var snapshotResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Snapshot, snapshotResponse.Type);
    var snapshotJson = snapshotResponse.Payload.GetProperty("snapshot").GetRawText();
    var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(snapshotJson));
    Assert.Equal("test.instance", snapshot.WidgetInstanceId);
    var renderStyles = snapshotResponse.Payload.GetProperty("renderStyles");
    Assert.Equal(5, renderStyles.EnumerateObject().Count());
    var buttonStyles = renderStyles.GetProperty("button");
    var fontSize = buttonStyles.GetProperty("base").GetProperty("font-size");
    Assert.Equal("length", fontSize.GetProperty("kind").GetString());
    Assert.Equal("18px", fontSize.GetProperty("text").GetString());
    Assert.Equal(18D, fontSize.GetProperty("number").GetDouble());
    Assert.Equal("px", fontSize.GetProperty("unit").GetString());
    var focusedScale = buttonStyles.GetProperty("focused").GetProperty("scale");
    Assert.Equal("number", focusedScale.GetProperty("kind").GetString());
    Assert.Equal(1.1D, focusedScale.GetProperty("number").GetDouble());
    Assert.Equal(JsonValueKind.Null, focusedScale.GetProperty("unit").ValueKind);
    var pressedOpacity = buttonStyles.GetProperty("pressed").GetProperty("opacity");
    Assert.Equal("number", pressedOpacity.GetProperty("kind").GetString());
    Assert.Equal(0.62D, pressedOpacity.GetProperty("number").GetDouble());
    Assert.Equal(1.1D,
        buttonStyles.GetProperty("pressed").GetProperty("scale").GetProperty("number").GetDouble());
    Assert.Equal("3px", buttonStyles.GetProperty("base").GetProperty("border-width").GetProperty("text").GetString());
    Assert.Equal("3px", buttonStyles.GetProperty("focused").GetProperty("border-width").GetProperty("text").GetString());
    var disabledStyles = renderStyles.GetProperty("disabled-button");
    Assert.Equal(0.4D, disabledStyles.GetProperty("base").GetProperty("opacity").GetProperty("number").GetDouble());
    Assert.Equal(0.4D, disabledStyles.GetProperty("focused").GetProperty("opacity").GetProperty("number").GetDouble());
    var busyStyles = renderStyles.GetProperty("busy-button");
    Assert.Equal(0.7D, busyStyles.GetProperty("base").GetProperty("opacity").GetProperty("number").GetDouble());
    Assert.Equal(0.7D, busyStyles.GetProperty("focused").GetProperty("opacity").GetProperty("number").GetDouble());
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    var activation = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, activation.Type);

    var acknowledgement = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 7,
            MonotonicTimestampMicroseconds: 1000,
            SnapshotSequence: snapshot.Sequence)));
    Assert.Equal(BridgeMessageTypes.ControllerInputResult, acknowledgement.Type);
    Assert.True(acknowledgement.Payload.GetProperty("handled").GetBoolean(),
        "Expected dashboard quick action to be handled.");
    var invalidation = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    Assert.Equal("test-widget", invalidation.Payload.GetProperty("widgetId").GetString());
    Assert.Equal(1L, invalidation.Payload.GetProperty("revision").GetInt64());

    var automation = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.LeftBumper,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 8,
            SnapshotSequence: snapshot.Sequence,
            Origin: ControllerInputOrigin.AccessibilityAutomation)));
    Assert.Equal(BridgeMessageTypes.ControllerInputResult, automation.Type);
    Assert.True(automation.Payload.GetProperty("handled").GetBoolean(),
        "Capability-bearing accessibility input should remain an ordinary action.");
    var automationInvalidation = await harness.Client.ReadEventAsync(
        BridgeMessageTypes.Invalidation);
    Assert.Equal(2L, automationInvalidation.Payload.GetProperty("revision").GetInt64());

    var replay = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 7,
            SnapshotSequence: snapshot.Sequence)));
    Assert.Equal(BridgeMessageTypes.Error, replay.Type);
    var stale = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 9,
            SnapshotSequence: snapshot.Sequence + 1)));
    Assert.Equal(BridgeMessageTypes.Error, stale.Type);

    var interactive = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(BridgeMessageTypes.Acknowledged, interactive.Type);

    var shortcut = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.RightBumper,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "button",
            ActiveInputScopeId: snapshot.ActiveInputScopeId,
            SnapshotSequence: snapshot.Sequence)));
    Assert.Equal(BridgeMessageTypes.ControllerInputResult, shortcut.Type);
    Assert.True(shortcut.Payload.GetProperty("handled").GetBoolean(),
        "Expected focused shortcut to be handled.");
    var secondInvalidation = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    Assert.Equal(3L, secondInvalidation.Payload.GetProperty("revision").GetInt64());

    var sliderChange = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.DPadRight,
            ControllerEventPhase.Repeated,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "volume",
            ActiveInputScopeId: snapshot.ActiveInputScopeId,
            SnapshotSequence: snapshot.Sequence,
            RequestedValue: 0.6)));
    Assert.Equal(BridgeMessageTypes.ControllerInputResult, sliderChange.Type);
    Assert.True(sliderChange.Payload.GetProperty("handled").GetBoolean(),
        "Expected absolute Slider target to cross the bridge.");
    var sliderInvalidation = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    Assert.Equal(4L, sliderInvalidation.Payload.GetProperty("revision").GetInt64());
    var updatedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var updated = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        updatedResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(0.6D, FindNode(updated.Root, "volume").Value);
    Assert.Equal("physical,automation,physical", FindNode(updated.Root, "busy-button").Text);
}

static ViewNode FindNode(ViewNode node, string id)
{
    if (node.Id == id) return node;
    foreach (var child in node.Children)
    {
        var found = FindNodeOrNull(child, id);
        if (found is not null) return found;
    }
    throw new InvalidOperationException($"Node '{id}' was not found.");

    static ViewNode? FindNodeOrNull(ViewNode candidate, string target)
    {
        if (candidate.Id == target) return candidate;
        foreach (var child in candidate.Children)
        {
            var nested = FindNodeOrNull(child, target);
            if (nested is not null) return nested;
        }
        return null;
    }
}

static async Task DashboardButtonsStayHostOwned()
{
    await using var harness = await BridgeHarness.StartAsync();
    foreach (var button in new[]
             {
                 ControllerButton.A, ControllerButton.B, ControllerButton.Y,
                 ControllerButton.DPadUp, ControllerButton.DPadDown,
                 ControllerButton.DPadLeft, ControllerButton.DPadRight,
             })
    {
        var response = await harness.Client.RequestAsync(
            BridgeMessageTypes.ControllerInput,
            new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
                button,
                ControllerEventPhase.Pressed,
                ControllerInputContext.DashboardQuickAction)));
        Assert.Equal(BridgeMessageTypes.Error, response.Type);
    }
    var invalidOrigin = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 1,
            SnapshotSequence: 1,
            Origin: (ControllerInputOrigin)99)));
    Assert.Equal(BridgeMessageTypes.Error, invalidOrigin.Type);
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static Task ScrollRenderRole()
{
    var snapshot = new WidgetView(
        UI.VerticalScroll("sessions",
            UI.Button("Game", "mute", "session.game.mute")),
        InitialFocusId: "session.game.mute",
        Surface: new WidgetSurfaceHints { Mode = WidgetSurfaceMode.Compact })
        .CreateSnapshot("bridge.scroll", 1);
    var styles = BridgeRenderStyleResolver.Resolve(snapshot, theme: null);
    Assert.Equal(2, styles.Count);
    Assert.True(styles.ContainsKey("sessions"), "Scroll role was omitted from bridge styles.");
    Assert.Equal(ProtocolConstants.ScrollContainerVersion, snapshot.ProtocolVersion);
    return Task.CompletedTask;
}

static Task ActionSurfaceRenderRole()
{
    var snapshot = new WidgetView(
        UI.Stack("root",
            UI.AppTile(
                "Long application name",
                "Ready",
                "launch",
                "library.app",
                subtitle: "Application",
                artwork: TileArtwork.FromGlyph(WidgetGlyph.Play, "Application icon")),
            UI.ResponsiveGrid(
                "library.grid", 180, 2,
                UI.Button("One", "open.one", "library.one"),
                UI.Button("Two", "open.two", "library.two")),
            UI.LoadingIndicator("library.loading", "Loading applications")),
        InitialFocusId: "library.app")
        .CreateSnapshot("bridge.action-surface", 1);
    var styles = BridgeRenderStyleResolver.Resolve(snapshot, theme: null);
    Assert.True(styles.ContainsKey("library.app"),
        "ActionSurface role was omitted from bridge styles.");
    Assert.True(styles.ContainsKey("library.loading"),
        "LoadingIndicator role was omitted from bridge styles.");
    Assert.True(styles.ContainsKey("library.grid"),
        "Grid role was omitted from bridge styles.");
    Assert.Equal(ProtocolConstants.ResponsiveGridVersion, snapshot.ProtocolVersion);
    return Task.CompletedTask;
}

static async Task WorkerFailureIsSurfaced()
{
    await using var harness = await BridgeHarness.StartAsync();
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest("test-widget", new WidgetActionEvent("crash", "button")));
    Assert.Equal(BridgeMessageTypes.Acknowledged, response.Type);
    Assert.Equal("enqueued", response.Payload.GetProperty("admission").GetString());
    var failure = await harness.Client.ReadEventAsync(BridgeMessageTypes.Failure);
    Assert.Equal("test-widget", failure.Payload.GetProperty("widgetId").GetString());

    var widgets = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    Assert.Equal(BridgeMessageTypes.Widgets, widgets.Type);
}

static async Task ActionFailureIsGenerationOwned()
{
    await using var harness = await BridgeHarness.StartAsync();
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest("test-widget", new WidgetActionEvent("fail", "button")));
    Assert.Equal(BridgeMessageTypes.Acknowledged, response.Type);

    var failure = await harness.Client.ReadEventAsync(BridgeMessageTypes.Failure);
    Assert.Equal("test-widget", failure.Payload.GetProperty("widgetId").GetString());
    Assert.True(!string.IsNullOrWhiteSpace(
        failure.Payload.GetProperty("runtimeGeneration").GetString()),
        "Action failure omitted its worker generation.");
    Assert.Equal("controllerActionFailed", failure.Payload.GetProperty("reason").GetString());
    Assert.Equal("fail", failure.Payload.GetProperty("actionId").GetString());
    Assert.Equal("button", failure.Payload.GetProperty("sourceElementId").GetString());
    Assert.True(!failure.Payload.GetProperty("canRestart").GetBoolean(),
        "An action failure incorrectly offered a worker restart.");
    Assert.Equal(1, harness.Server.RunningWorkerCount);
}

static async Task WorkerResidencyCountIsBounded()
{
    await using var harness = await BridgeHarness.StartBudgetAsync(
        new WorkerResidencyBudgetOptions
        {
            MaximumApplicationWorkers = 1,
            MaximumApplicationMemoryMb = 128,
        },
        new TemporaryWidgetDefinition("worker-0", "dev.test.worker0", "dev.test", "worker.0", 64),
        new TemporaryWidgetDefinition("worker-1", "dev.test.worker1", "dev.test", "worker.1", 64));

    var first = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("worker-0", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, first.Type);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    Assert.Equal(1, harness.Server.ResidencyBudget.ApplicationWorkers);
    Assert.Equal(64, harness.Server.ResidencyBudget.ApplicationMemoryMb);

    var refused = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("worker-1", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Error, refused.Type);
    Assert.True(
        refused.Payload.GetProperty("message").GetString()!
            .Contains("application worker limit (1/1)", StringComparison.Ordinal),
        "Count-bound refusal did not explain the exhausted worker limit.");
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    Assert.Equal(1, harness.Server.ResidencyBudget.ApplicationWorkers);

    var crashed = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest("worker-0", new WidgetActionEvent("crash", "button")));
    Assert.Equal(BridgeMessageTypes.Acknowledged, crashed.Type);
    Assert.Equal("enqueued", crashed.Payload.GetProperty("admission").GetString());
    _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Failure);
    await WaitUntilAsync(() => harness.Server.ResidencyBudget.ApplicationWorkers == 0,
        TimeSpan.FromSeconds(3));

    var replacement = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("worker-1", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, replacement.Type);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    Assert.Equal(1, harness.Server.ResidencyBudget.ApplicationWorkers);
}

static async Task WorkerResidencyMemoryIsBounded()
{
    await using var harness = await BridgeHarness.StartBudgetAsync(
        new WorkerResidencyBudgetOptions
        {
            MaximumApplicationWorkers = 3,
            MaximumApplicationMemoryMb = 96,
        },
        new TemporaryWidgetDefinition("worker-64", "dev.test.worker64", "dev.test", "worker.64", 64),
        new TemporaryWidgetDefinition("worker-48", "dev.test.worker48", "dev.test", "worker.48", 48),
        new TemporaryWidgetDefinition("settings", "org.gbar.firstparty.settings",
            "org.gbar.firstparty", "settings.instance", 64));

    var first = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("worker-64", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, first.Type);

    var refused = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("worker-48", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Error, refused.Type);
    Assert.True(
        refused.Payload.GetProperty("message").GetString()!
            .Contains("application memory limit (64+48/96 MiB)", StringComparison.Ordinal),
        "Memory-bound refusal did not explain the accounted Job limit.");

    var settings = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("settings", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, settings.Type);
    var budget = harness.Server.ResidencyBudget;
    Assert.Equal(1, budget.ApplicationWorkers);
    Assert.Equal(64, budget.ApplicationMemoryMb);
    Assert.Equal(1, budget.ControlPlaneWorkers);
    Assert.Equal(64, budget.ControlPlaneMemoryMb);
    Assert.Equal(2, budget.TotalWorkers);
    Assert.Equal(128, budget.TotalMemoryMb);
}

static string RequiredValue(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    if (index < 0 || index + 1 >= values.Length) throw new ArgumentException($"Missing {name}.");
    return values[index + 1];
}

file sealed class BridgeTestWidget : Widget
{
    private double _volume = 0.5;
    private string _actionOrder = "none";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, string>
        _inputOrigins = new();
    private readonly bool _artworkFixture;
    private WidgetAppLibraryItem? _artworkItem;

    internal BridgeTestWidget(string instanceId) =>
        _artworkFixture = string.Equals(
            instanceId, "artwork.instance", StringComparison.Ordinal);

    public override WidgetView Render()
    {
        var children = new List<WidgetElement>
        {
            UI.Button("Refresh", "refresh", "button")
                .Selected()
                .Shortcut(ControllerButton.RightBumper).Classes("primary"),
            UI.Button("Unavailable", "disabled", "disabled-button")
                .Disabled().Classes("disabled"),
            UI.Button(_actionOrder == "none" ? "Saving" : _actionOrder, "busy", "busy-button")
                .Busy().Classes("busy"),
            UI.Slider(_volume, 0, 1, 0.1, "volume.changed", "volume",
                "Volume", $"{_volume:P0}"),
        };
        if (_artworkFixture)
        {
            children.Add(UI.Button("Load artwork", "artwork.load", "artwork.load"));
            children.Add(UI.Button("Launch app", "artwork.launch", "artwork.launch"));
            children.Add(_artworkItem?.ArtworkHandle is { } handle
                ? UI.Artwork(
                    new WidgetArtworkHandle(handle), "artwork.image", "Application icon",
                    ImageFit.Contain)
                : UI.Icon(WidgetGlyph.Play, "artwork.image", "Application icon fallback"));
        }
        return new WidgetView(
            UI.Stack("root", children.ToArray()),
            "button",
            [
            new WidgetQuickAction(ControllerButton.X, "refresh", "Refresh"),
            // This capability is deliberately absent from the test catalog.
            // Automation must route as an ordinary action without reaching
            // declaration validation or creating gesture authority.
            new WidgetQuickAction(
                ControllerButton.LeftBumper,
                "refresh",
                "Automation refresh",
                new WidgetQuickActionCapability(
                    WidgetMediaCapabilities.Control.CapabilityId,
                    WidgetMediaCapabilities.Control.OperationId)),
            ]);
    }

    protected override async ValueTask OnLifecycleStateChangedAsync(
        WidgetLifecycleState previous,
        WidgetLifecycleState current,
        CancellationToken stateLifetime)
    {
        if (_artworkFixture && current == WidgetLifecycleState.Interactive)
        {
            var query = new WidgetAppLibraryQuery();
            var first = await HostServices.AppLibrary.QueryAsync(
                query, limit: 1, refresh: true,
                cancellationToken: stateLifetime);
            var after = first.After is { } afterValue
                ? new WidgetCollectionCursor(afterValue)
                : throw new InvalidOperationException("Artwork fixture lacked a forward cursor.");
            var second = await HostServices.AppLibrary.QueryAsync(
                query, after, WidgetCursorDirection.After, 1,
                cancellationToken: stateLifetime);
            var before = second.Before is { } beforeValue
                ? new WidgetCollectionCursor(beforeValue)
                : throw new InvalidOperationException("Artwork fixture lacked a reverse cursor.");
            var reversed = await HostServices.AppLibrary.QueryAsync(
                query, before, WidgetCursorDirection.Before, 1,
                cancellationToken: stateLifetime);
            if (reversed.Items.Single().SavedId != first.Items.Single().SavedId)
                throw new InvalidOperationException("Artwork cursor traversal was not reversible.");
            var refreshed = await HostServices.AppLibrary.QueryAsync(
                query, limit: 1, refresh: true,
                cancellationToken: stateLifetime);
            _artworkItem = refreshed.Items.Single();
            if (_artworkItem.ArtworkHandle is null)
                throw new InvalidOperationException(
                    "Artwork fixture received an item without a registered handle.");
            Invalidate();
        }
    }

    public override ValueTask<bool> OnControllerInputAsync(
        ControllerInputEvent input,
        CancellationToken cancellationToken = default)
    {
        _inputOrigins[input.Sequence] = input.Origin == ControllerInputOrigin.PhysicalController
            ? "physical"
            : "automation";
        return base.OnControllerInputAsync(input, cancellationToken);
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (action.ActionId == "refresh")
        {
            var origin = _inputOrigins.GetValueOrDefault(action.Sequence, "unknown");
            _actionOrder = _actionOrder == "none" ? origin : $"{_actionOrder},{origin}";
            Invalidate();
        }
        else if (action.ActionId == "artwork.load" && _artworkFixture)
        {
            var page = await HostServices.AppLibrary.QueryAsync(
                new WidgetAppLibraryQuery(), limit: 1, refresh: true,
                cancellationToken: cancellationToken);
            _artworkItem = page.Items.Single();
            Invalidate();
        }
        else if (action.ActionId == "artwork.launch" && _artworkFixture &&
            _artworkItem is not null)
        {
            await HostServices.AppLibrary.LaunchAsync(
                _artworkItem.AppId, cancellationToken);
        }
        else if (action is { ActionId: "volume.changed", RequestedValue: { } requested })
        {
            _volume = requested;
            Invalidate();
        }
        else if (action.ActionId == "crash")
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            Environment.Exit(31);
        }
        else if (action.ActionId == "hang")
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        else if (action.ActionId == "fail")
            throw new InvalidOperationException("intentional bridge action failure");
        else if (action.ActionId == "ordered.first")
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
            _actionOrder = "first";
            Invalidate();
        }
        else if (action.ActionId == "ordered.second")
        {
            _actionOrder += ",second";
            Invalidate();
        }
    }
}

file sealed record TemporaryWidgetDefinition(
    string Id,
    string PackageId,
    string PublisherId,
    string InstanceId,
    int? MemoryLimitMb = null,
    string Name = "Test Widget",
    string? Icon = "music",
    IReadOnlyList<string>? DeclaredCapabilities = null,
    WidgetResidencyPolicy? ResidencyPolicy = null,
    string StyleFile = "styles/default.gbss");

file sealed class TemporaryCatalog : IDisposable
{
    private readonly string _directory;
    public string Path { get; }

    private TemporaryCatalog(string directory, string path)
    {
        _directory = directory;
        Path = path;
    }

    public static TemporaryCatalog Create(
        bool addUnknownProperty = false,
        bool invalidStyle = false,
        string styleFile = "styles/default.gbss",
        string? icon = "music",
        int? memoryLimitMb = null,
        string name = "Test Widget",
        string instanceId = "test.instance",
        string id = "test-widget",
        string packageId = "dev.test.widget",
        string publisherId = "dev.test",
        IReadOnlyList<string>? declaredCapabilities = null,
        string? styleSource = null,
        WidgetResidencyPolicy? residencyPolicy = null)
    {
        return CreateCore(
            [new TemporaryWidgetDefinition(
                id,
                packageId,
                publisherId,
                instanceId,
                memoryLimitMb,
                name,
                icon,
                declaredCapabilities,
                residencyPolicy,
                styleFile)],
            addUnknownProperty,
            invalidStyle,
            styleSource);
    }

    public static TemporaryCatalog CreateMany(params TemporaryWidgetDefinition[] widgets) =>
        CreateCore(widgets, addUnknownProperty: false, invalidStyle: false, styleSource: null);

    private static TemporaryCatalog CreateCore(
        IReadOnlyList<TemporaryWidgetDefinition> widgets,
        bool addUnknownProperty,
        bool invalidStyle,
        string? styleSource)
    {
        if (widgets.Count == 0) throw new ArgumentException("At least one widget is required.", nameof(widgets));
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"gba-bridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "widgets.json");
        var stylesDirectory = System.IO.Path.Combine(directory, "styles");
        Directory.CreateDirectory(stylesDirectory);
        File.WriteAllText(System.IO.Path.Combine(stylesDirectory, "default.gbss"), invalidStyle
            ? "button { background: url(https://example.test/evil.png); }"
            : styleSource ?? "stack { gap: 12px; } button { color: #ffffff; font-size: 18px; } #button { opacity: 0.8; } #button:pressed { opacity: 0.62; } .primary:selected { border-width: 3px; } .primary:focused { outline-color: #8b7cff; scale: 1.1; } .disabled:disabled { opacity: 0.4; } .busy:busy { opacity: 0.7; }");
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Test process path is unavailable.");
        var json = JsonSerializer.Serialize(new
        {
            catalogVersion = 1,
            widgets = widgets.Select(widget => new
                {
                    widget.Id,
                    widget.PackageId,
                    widget.PublisherId,
                    widget.Name,
                    widget.InstanceId,
                    widget.Icon,
                    workerExecutable = executable,
                    widget.StyleFile,
                    workerArguments = Array.Empty<string>(),
                    declaredCapabilities = widget.DeclaredCapabilities ?? Array.Empty<string>(),
                    widget.MemoryLimitMb,
                    widget.ResidencyPolicy,
                    quickActions = new[]
                    {
                        new
                        {
                            id = "hover-refresh",
                            label = "Refresh",
                            actionId = "refresh",
                            sourceElementId = "hover.refresh",
                            controllerButton = "x",
                        },
                    },
                }).ToArray(),
            unknown = addUnknownProperty ? true : (bool?)null,
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(path, json);
        return new TemporaryCatalog(directory, path);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

file sealed class BridgeHarness : IAsyncDisposable
{
    private readonly TemporaryCatalog _temporaryCatalog;
    private readonly TemporaryAppearance? _appearance;
    private readonly Task _serverTask;
    public WidgetBridgeServer Server { get; }
    public BridgeTestClient Client { get; }
    public TemporaryAppearance? Appearance => _appearance;

    private BridgeHarness(
        TemporaryCatalog temporaryCatalog,
        TemporaryAppearance? appearance,
        WidgetBridgeServer server,
        BridgeTestClient client,
        Task serverTask)
    {
        _temporaryCatalog = temporaryCatalog;
        _appearance = appearance;
        Server = server;
        Client = client;
        _serverTask = serverTask;
    }

    public static async Task<BridgeHarness> StartAsync(
        bool withAppearance = false,
        WidgetResidencyPolicy? residencyPolicy = null)
    {
        var temporary = TemporaryCatalog.Create(residencyPolicy: residencyPolicy);
        TemporaryAppearance? appearance = null;
        try
        {
            if (withAppearance) appearance = await TemporaryAppearance.CreateAsync();
            var catalog = BridgeCatalog.Load(temporary.Path);
            var pipeName = $"gba-bridge-test-{Guid.NewGuid():N}";
            var server = new WidgetBridgeServer(pipeName, catalog, 64 * 1024, appearance?.Service);
            var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
            var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
            return new BridgeHarness(temporary, appearance, server, client, serverTask);
        }
        catch
        {
            if (appearance is not null) await appearance.DisposeAsync();
            temporary.Dispose();
            throw;
        }
    }

    public static async Task<BridgeHarness> StartBudgetAsync(
        WorkerResidencyBudgetOptions budget,
        params TemporaryWidgetDefinition[] widgets)
    {
        var temporary = TemporaryCatalog.CreateMany(widgets);
        try
        {
            var catalog = BridgeCatalog.Load(temporary.Path);
            var pipeName = $"gba-bridge-budget-{Guid.NewGuid():N}";
            var server = new WidgetBridgeServer(
                pipeName,
                catalog,
                64 * 1024,
                residencyBudget: budget);
            var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
            var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
            return new BridgeHarness(temporary, null, server, client, serverTask);
        }
        catch
        {
            temporary.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Client.IsTerminal)
                await Client.RequestAsync(BridgeMessageTypes.Stop, new { });
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception exception) when (
                Client.ReadWasCanceled &&
                exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
        finally
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
            if (_appearance is not null) await _appearance.DisposeAsync();
            _temporaryCatalog.Dispose();
        }
    }
}

file sealed class TemporaryAppearance : IAsyncDisposable
{
    private readonly string _directory;
    private readonly string _themeFile;
    public PlatformAppearanceService Service { get; }

    private TemporaryAppearance(string directory, string themeFile, PlatformAppearanceService service)
    {
        _directory = directory;
        _themeFile = themeFile;
        Service = service;
    }

    public static async Task<TemporaryAppearance> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gba-bridge-appearance-{Guid.NewGuid():N}");
        var paths = new PlatformSettingsPaths(directory);
        var themeDirectory = Path.Combine(paths.ThemesDirectory, "dev.example.bridge", "1.0.0");
        Directory.CreateDirectory(themeDirectory);
        var themeFile = Path.Combine(themeDirectory, "theme.gbss");
        await File.WriteAllTextAsync(Path.Combine(themeDirectory, "theme.json"),
            "{\"schemaVersion\":1,\"id\":\"dev.example.bridge\",\"name\":\"Bridge Test\",\"version\":\"1.0.0\",\"entryFile\":\"theme.gbss\"}");
        await File.WriteAllTextAsync(themeFile,
            "button { color: #2468ac; opacity: 0.55; } title { color: #fedcba; }");
        var store = new PlatformSettingsStore(paths);
        await store.ReplaceAsync(new PlatformSettingsDocument
        {
            SchemaVersion = PlatformSettingsDocument.CurrentSchemaVersion,
            Appearance = AppearanceSettings.Default with
            {
                ThemeId = "dev.example.bridge",
                ThemeVersion = "1.0.0",
                InterfaceScale = 1.1,
                TextScale = 1.2,
                BackdropOpacity = 0.7,
                Motion = MotionPreference.Reduced,
                Contrast = ContrastPreference.High,
                BoldText = true,
                Transparency = TransparencyPreference.Reduced,
            },
        });
        var service = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
        var result = await service.ReloadNowAsync();
        Assert.True(result.Published, "Initial bridge appearance did not load.");
        return new TemporaryAppearance(directory, themeFile, service);
    }

    public Task WriteThemeAsync(string source) => File.WriteAllTextAsync(_themeFile, source);

    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync();
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

file sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; }

    public TemporaryDirectory(string prefix)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

file sealed class StalledAdmissionFixture
{
    private readonly TaskCompletionSource<bool> _started = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _canceled = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public StalledAdmissionFixture()
    {
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("Test process path is unavailable.");
        Catalog = new BridgeCatalog([new ConfiguredWidget
        {
            Id = "stalled-widget",
            PackageId = "dev.test.stalled",
            PublisherId = "dev.test",
            Name = "Stalled Widget",
            InstanceId = "stalled.instance",
            WorkerExecutable = executable,
            RequiresAppContainer = true,
            IsolationKey = $"bridge-stalled-admission-{Guid.NewGuid():N}",
            ContentLeaseFactory = cancellationToken =>
            {
                _started.TrySetResult(true);
                try
                {
                    cancellationToken.WaitHandle.WaitOne();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Unreachable admission branch.");
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                        _canceled.TrySetResult(true);
                }
            },
            WorkerFingerprint = new string('a', 64),
            CatalogFingerprint = new string('b', 64),
        }]);
    }

    public BridgeCatalog Catalog { get; }
    public Task Started => _started.Task;
    public Task Canceled => _canceled.Task;
}

file sealed class RawBridgeConnection : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;

    private RawBridgeConnection(
        NamedPipeClientStream pipe,
        BridgeFrameChannel channel)
    {
        _pipe = pipe;
        Channel = channel;
    }

    public BridgeFrameChannel Channel { get; }

    public static async Task<RawBridgeConnection> ConnectAsync(string pipeName)
    {
        var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(3000);
            var channel = new BridgeFrameChannel(pipe, 64 * 1024);
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.Hello,
                RequestId = 1,
                Payload = BridgeJson.ToElement(new BridgeHello("raw-bridge-test")),
            }, CancellationToken.None);
            var hello = await channel.ReadAsync(CancellationToken.None).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(BridgeMessageTypes.HelloAccepted, hello.Type);
            return new RawBridgeConnection(pipe, channel);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _pipe.DisposeAsync();
}

file sealed class BridgeTestClient : IAsyncDisposable
{
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(4);
    private readonly NamedPipeClientStream _pipe;
    private readonly BridgeFrameChannel _channel;
    private readonly BridgeTestFrameReader _reader;
    private readonly Queue<BridgeEnvelope> _events = new();
    private long _requestId;
    public int PendingEventCount => _events.Count;
    public int PendingEventCountOfType(string type) =>
        _events.Count(message => string.Equals(message.Type, type, StringComparison.Ordinal));

    private BridgeTestClient(NamedPipeClientStream pipe, BridgeFrameChannel channel)
    {
        _pipe = pipe;
        _channel = channel;
        _reader = new BridgeTestFrameReader(channel, pipe.Dispose);
    }

    public bool IsTerminal => _reader.IsTerminal;
    public bool ReadWasCanceled => _reader.WasCanceled;

    public static async Task<BridgeTestClient> ConnectAsync(string pipeName, int maximumBytes)
    {
        var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(3000);
        var client = new BridgeTestClient(pipe, new BridgeFrameChannel(pipe, maximumBytes));
        var hello = await client.RequestAsync(
            BridgeMessageTypes.Hello, new BridgeHello("WidgetBridge.Tests"));
        Assert.Equal(BridgeMessageTypes.HelloAccepted, hello.Type);
        return client;
    }

    public async Task<BridgeEnvelope> RequestAsync<T>(string type, T payload)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        await _channel.WriteAsync(new BridgeEnvelope
        {
            Type = type,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(payload),
        }, CancellationToken.None);
        while (true)
        {
            var response = await _reader.ReadAsync(ReadDeadline);
            if (response.RequestId == 0)
            {
                _events.Enqueue(response);
                continue;
            }
            if (response.RequestId != requestId)
                throw new InvalidOperationException("Received response for a different request.");
            return response;
        }
    }

    public async Task<IReadOnlyList<BridgeEnvelope>> PipelineAsync(
        params (string Type, object Payload)[] requests)
    {
        var requestIds = new long[requests.Length];
        for (var index = 0; index < requests.Length; index++)
        {
            requestIds[index] = Interlocked.Increment(ref _requestId);
            await _channel.WriteAsync(new BridgeEnvelope
            {
                Type = requests[index].Type,
                RequestId = requestIds[index],
                Payload = BridgeJson.ToElement(requests[index].Payload),
            }, CancellationToken.None);
        }

        var expected = requestIds.ToHashSet();
        var responses = new Dictionary<long, BridgeEnvelope>();
        while (responses.Count != requestIds.Length)
        {
            var response = await _reader.ReadAsync(ReadDeadline);
            if (response.RequestId == 0)
            {
                _events.Enqueue(response);
                continue;
            }
            if (!expected.Contains(response.RequestId) ||
                !responses.TryAdd(response.RequestId, response))
                throw new InvalidOperationException(
                    "Received an unknown or duplicate pipelined response.");
        }
        return requestIds.Select(requestId => responses[requestId]).ToArray();
    }

    public async Task<BridgeEnvelope> ReadEventAsync(string type)
    {
        var count = _events.Count;
        for (var index = 0; index < count; index++)
        {
            var message = _events.Dequeue();
            if (message.Type == type) return message;
            _events.Enqueue(message);
        }
        while (true)
        {
            var message = await _reader.ReadAsync(ReadDeadline);
            if (message.RequestId != 0)
                throw new InvalidOperationException("Expected an event, received a response.");
            if (message.Type == type) return message;
            _events.Enqueue(message);
        }
    }

    public async ValueTask DisposeAsync() => await _pipe.DisposeAsync();
}

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
