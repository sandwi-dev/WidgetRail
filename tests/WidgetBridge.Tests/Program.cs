using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.WindowsCommunityProvider;

if (args.Contains("--widget-pipe", StringComparer.Ordinal))
    return await RunWorkerAsync(args);

var tests = new (string Name, Func<Task> Run)[]
{
    ("Bridge framing rejects oversized messages", OversizedFrameIsRejected),
    ("Protected Wi-Fi secret frames zero every mutable managed owner", ProtectedWifiSecretFramesAreZeroed),
    ("Event cancellation preserves the serialized frame boundary", BridgeEventWriteBoundaryScenarios.CancellationPreservesFrameBoundary),
    ("Bridge read and reply timeouts have exact frame owners", BridgeFrameOwnershipScenarios.TimeoutAndCancellationHaveExactOwners),
    ("Strict catalog rejects unknown properties", StrictCatalogRejectsUnknownProperties),
    ("Bridge startup scopes an explicit development installed catalog", DevelopmentCatalogRootIsScoped),
    ("Settings reviews the same catalog selected by the bridge", SettingsUsesSelectedCatalog),
    ("Catalog treats worker memory guidance as optional advisory metadata", CatalogMemoryGuidanceIsAdvisory),
    ("Worker residency budget options are bounded and explicit", WorkerResidencyBudgetOptionsAreBounded),
    ("Worker residency budget admission is race safe", WorkerResidencyBudgetAdmissionIsRaceSafe),
    ("Catalog widget icons use the closed WidgetGlyph set with a safe fallback", CatalogGlyphIsClosed),
    ("Catalog rejects invalid WRSS with safe diagnostics", InvalidThemeIsRejected),
    ("Catalog rejects style paths outside package root", UnsafeStylePathIsRejected),
    ("Catalog enumeration does not launch workers", EnumerationIsLazy),
    ("Request dispatcher cleans success failure and cancellation", RequestDispatcherCleansTerminalPaths),
    ("Request classification is closed typed and fail-closed", RequestClassificationIsClosed),
    ("Protected Wi-Fi host admission is exact trusted and bounded", ProtectedWifiHostAdmissionIsExact),
    ("Protected Wi-Fi production dispatch clears one exact secret owner", ProtectedWifiProductionDispatchIsZeroed),
    ("Trusted artwork demand is exact current and lazy through the production bridge", TrustedArtworkDemandIsExact),
    ("Request dispatcher preserves FIFO and predecessor failure", RequestDispatcherOwnsWidgetOrdering),
    ("Request dispatcher rejects duplicates and global over-capacity", RequestDispatcherBoundsAdmission),
    ("Request dispatcher deadline quarantines cancellation-ignoring work", RequestDispatcherForcedDrainIsComplete),
    ("Stalled widget admission leaves bounded list and stop control responsive", StalledAdmissionKeepsControlPlaneResponsive),
    ("Pipelined requests preserve per-widget receive order", PipelinedWidgetRequestsStayOrdered),
    ("Duplicate pending request IDs fail the bridge session closed", DuplicatePendingRequestIdsFailClosed),
    ("Enabled installed widgets join the bridge catalog without eager launch", InstalledWidgetsJoinCatalog),
    ("Two unrelated full-trust applications use one ordinary runtime", FullTrustCommunityScenarios.TwoApplicationsUseTheOrdinaryRuntime),
    ("Packaged Spotify uses the ordinary full-trust runtime", FullTrustCommunityScenarios.SpotifyUsesTheOrdinaryRuntime),
    ("Packaged Game Launcher uses the ordinary full-trust runtime", FullTrustCommunityScenarios.GameLauncherUsesTheOrdinaryRuntime),
    ("Full-trust missing entrypoints and silent promotion fail closed", FullTrustCommunityScenarios.MissingEntrypointAndManifestPromotionFailClosed),
    ("Installed Community advanced presentation declarations are generic and generation owned", InstalledAdvancedPresentationDeclarationsAreGeneric),
    ("Installed content generations receive distinct isolation identities", InstalledContentGenerationIsIsolated),
    ("Installed launch admission rejects content changed after catalog publication", InstalledLaunchAdmissionRejectsRace),
    ("Installed widget residency policies reach the generic supervisor", InstalledResidencyPolicyIsCarried),
    ("Known installed capabilities load lazily and unknown capabilities fail closed", InstalledCapabilityDeclarationsAreClosed),
    ("Bridge alone synthesizes private state authority for capability-free workers", PrivateStateAuthorityIsHostSynthesized),
    ("Installed worker local data clears after exact retirement and preserves its neighbor", InstalledWorkerLocalDataClearIsExact),
    ("Disabled package uninstall is exact revisioned and preserves private data", InstalledPackageUninstallIsExact),
    ("Tampered installed catalogs fail soft to trusted widgets", TamperedInstalledCatalogFailsSoft),
    ("Invalid installed styles fail soft to trusted widgets", InvalidInstalledStyleFailsSoft),
    ("Catalog monitor retains invalid trusted state and fails closed on installed state", CatalogMonitorIsRevisionedAndLastGood),
    ("Live installed bytes are pinned and post-release tamper cannot relaunch", InstalledPackageTamperRetiresLiveWorker),
    ("Catalog monitor closes the startup notification window", CatalogMonitorStartupCatchUp),
    ("Catalog reconciliation preserves compatible workers and retires changed workers", CatalogReconciliationPreservesCompatibleWorkers),
    ("Client registry owns compatible replacement removal and stale generations", BridgeClientRegistryScenarios.CatalogReplacementAndRemovalOwnGenerations),
    ("Client registry isolates typed widget runtime failures", BridgeClientRegistryScenarios.WidgetRuntimeFailuresAreTypedAndRegistrationLocal),
    ("Client registry owns idle unload cancellation and replacement drain", BridgeClientRegistryScenarios.IdleUnloadCancellationAndReplacementAreOwned),
    ("Client registry restart restores lifecycle and resets generation", BridgeClientRegistryScenarios.RestartRestoresLifecycleAndResetsGeneration),
    ("Client registry restart reserves one generation and cleans failed restore", BridgeClientRegistryScenarios.RestartReservationAndRestoreFailureAreClosed),
    ("Client registry replacement retires before exact host mutation", BridgeClientRegistryScenarios.ManagedReplacementRetiresBeforeMutation),
    ("Trusted local-data management clears one exact retired generation", BridgeClientRegistryScenarios.LocalDataManagementIsExactAndDocumentBlind),
    ("Client registry commits lifecycle and first snapshot as one generation", BridgeClientRegistryScenarios.LifecycleAndFirstSnapshotAreAtomic),
    ("Client registry publication admission serializes replacement", BridgeClientRegistryScenarios.PublicationAdmissionSerializesReplacement),
    ("Client registry notification lane bounds and balances admission", BridgeClientRegistryScenarios.NotificationLaneBoundsAndBalancesAdmission),
    ("Client registry notification burst cancels and drains on replacement", BridgeClientRegistryScenarios.NotificationBurstIsBoundedAndRetires),
    ("Client registry cancelled restart transfers exact retirement", BridgeClientRegistryScenarios.CancelledRestartTransfersRetirement),
    ("Client registry starts external retirement outside its identity gate", BridgeClientRegistryScenarios.ExternalRetirementStartsOutsideIdentityGate),
    ("Client registry releases refused and failed-start residency", BridgeClientRegistryScenarios.BudgetRefusalAndFailedStartReleaseReservations),
    ("Client registry terminal disposal serializes with operations", BridgeClientRegistryScenarios.TerminalDisposalSerializesWithConcurrentOperation),
    ("Client registry observes retirement failures and disposes every client", BridgeClientRegistryScenarios.RetirementFailuresAreObservedAndDrained),
    ("Visible registry publication reaches its configured publisher once", BridgeClientRegistryScenarios.VisibleRegistrationPublishesInvalidationExactlyOnce),
    ("Local package import origin is exact current Interactive Settings", BridgeClientRegistryScenarios.LocalPackageImportOriginIsExact),
    ("Local package import is disabled revisioned and path free", LocalPackageImportIsDisabledRevisionedAndPathFree),
    ("Local package import failures preserve catalog state", LocalPackageImportFailuresPreserveCatalog),
    ("Platform appearance is bounded and does not launch workers", PlatformAppearanceIsLazy),
    ("Private diagnostics attach only to the exact trusted Settings identity", DiagnosticsAreSettingsOnly),
    ("Media Sessions diagnostics are bounded transition-only and sanitized", MediaSessionDiagnosticsAreBounded),
    ("Worker request diagnostics are bounded developer-only records", WorkerRequestDiagnosticsAreBounded),
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
    ("Exact-base divergence converges through one full checkpoint", ExactBaseDivergenceConvergesThroughCheckpoint),
    ("Committed text crosses bridge and worker action execution", CommittedTextCrossesBridgeAndWorker),
    ("Managed presentation session preserves sandboxed authority lifecycle and last-good state", ManagedPresentationSessionPreservesSandboxedAuthority),
    ("Managed presentation session preserves the ordinary full-trust runtime", ManagedPresentationSessionPreservesFullTrustRuntime),
    ("Protocol-v2 scroll nodes resolve bridge render roles", ScrollRenderRole),
    ("Protocol-v19 virtual collection window crosses worker and bridge", VirtualCollectionWindowCrossesBridge),
    ("Admitted registry invalidation reaches the client event queue", AdmittedRegistryInvalidationReachesClientEventQueue),
    ("Protocol-v8 grids, action surfaces, and loading indicators resolve bridge render roles", ActionSurfaceRenderRole),
    ("Protocol-v15 text entries resolve one closed bridge render role", TextEntryRenderRole),
    ("Dashboard-owned controller buttons are rejected", DashboardButtonsStayHostOwned),
    ("Late action failures retain worker generation", ActionFailureIsGenerationOwned),
    ("Worker failures surface without killing bridge", WorkerFailureIsSurfaced),
    ("Oversized worker presentation fails without harming its neighbor", OversizedPresentationPreservesNeighbor),
    ("Worker residency budget refuses count overcommit and releases failures", WorkerResidencyCountIsBounded),
    ("Worker residency reports memory guidance without private-size refusal", WorkerResidencyMemoryIsAdvisory),
};

var testPrefixIndex = Array.IndexOf(args, "--test-prefix");
if (testPrefixIndex >= 0)
{
    if (testPrefixIndex + 1 >= args.Length)
        throw new ArgumentException("Missing --test-prefix value.");
    var prefix = args[testPrefixIndex + 1];
    tests = tests.Where(test => test.Name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    if (tests.Length == 0)
        throw new ArgumentException($"No WidgetBridge tests matched prefix '{prefix}'.");
}

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
        _ => string.Equals(instance, "virtual.instance", StringComparison.Ordinal)
            ? new VirtualCollectionBridgeWidget()
            : new BridgeTestWidget(instance));
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

static async Task ProtectedWifiSecretFramesAreZeroed()
{
    var secret = Enumerable.Range(0, 14)
        .Select(index => (char)('!' + index)).ToArray();
    var expected = SecretSentinel(secret);
    var encoded = secret.Select(character => checked((byte)character)).ToArray();
    var decodeOwner = encoded.ToArray();
    using (var decoded = BridgeProtectedWifiSecret.DecodeOwned(decodeOwner))
    {
        Assert.True(decodeOwner.All(value => value == 0),
            "Secret decoder retained its owned frame bytes.");
        Assert.Equal(expected, SecretSentinel(decoded.Characters));
        var characters = decoded.Characters;
        decoded.Dispose();
        Assert.True(characters.All(value => value == '\0'),
            "Secret decoder retained its managed character owner.");
    }

    var frame = new byte[sizeof(int) + encoded.Length];
    BinaryPrimitives.WriteInt32LittleEndian(frame, encoded.Length);
    encoded.CopyTo(frame.AsSpan(sizeof(int)));
    await using (var stream = new MemoryStream(frame, writable: false))
    {
        var channel = new BridgeFrameChannel(stream, 1024);
        using var decoded = await channel.ReadProtectedWifiSecretAsync(
            secret.Length, CancellationToken.None);
        Assert.Equal(expected, SecretSentinel(decoded.Characters));
    }

    var invalid = encoded.ToArray();
    invalid[^1] = 0;
    Assert.Throws<BridgeProtocolException>(() =>
        BridgeProtectedWifiSecret.DecodeOwned(invalid));
    Assert.True(invalid.All(value => value == 0),
        "Rejected secret bytes survived decoding.");

    await using (var failing = new FailingProtectedWifiSecretStream(secret.Length))
    {
        var channel = new BridgeFrameChannel(failing, 1024);
        await Assert.ThrowsAsync<IOException>(() => channel.ReadProtectedWifiSecretAsync(
            secret.Length, CancellationToken.None).AsTask());
        Assert.True(failing.CapturedBody is not null &&
            failing.CapturedBody.All(value => value == 0),
            "A failed secret-frame read retained its partially filled body.");
    }

    using (var cancellation = new CancellationTokenSource())
    await using (var canceled = new FailingProtectedWifiSecretStream(
        secret.Length, cancellation))
    {
        var channel = new BridgeFrameChannel(canceled, 1024);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            channel.ReadProtectedWifiSecretAsync(
                secret.Length, cancellation.Token).AsTask());
        Assert.True(canceled.CapturedBody is not null &&
            canceled.CapturedBody.All(value => value == 0),
            "A canceled secret-frame read retained its partially filled body.");
    }

    Array.Clear(secret);
    CryptographicOperations.ZeroMemory(encoded);
    CryptographicOperations.ZeroMemory(frame);
}

static int SecretSentinel(ReadOnlySpan<char> secret)
{
    var value = unchecked((int)2166136261);
    foreach (var character in secret)
        value = unchecked((value ^ character) * 16777619);
    return value;
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
    using var temporary = new TemporaryDirectory("wrail-bridge-dev-root");
    var settings = Path.Combine(temporary.Path, "settings");
    var development = Path.Combine(temporary.Path, "development-catalog");
    Assert.Equal(Path.GetFullPath(development),
        WidgetRail.WidgetBridge.Program.ResolveInstalledCatalogRoot(
            ["--installed-catalog-root", development], settings));
    Assert.Equal(Path.Combine(Path.GetFullPath(settings), "widgets"),
        WidgetRail.WidgetBridge.Program.ResolveInstalledCatalogRoot([], settings));
    Assert.Throws<ArgumentException>(() =>
        WidgetRail.WidgetBridge.Program.ResolveInstalledCatalogRoot(
            ["--installed-catalog-root", development, "--installed-catalog-root", development], settings));
    return Task.CompletedTask;
}

static Task WorkerResidencyBudgetOptionsAreBounded()
{
    var defaults = WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget([]);
    Assert.Equal(8, defaults.MaximumApplicationWorkers);

    var configured = WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget(
        ["--max-resident-workers", "3"]);
    Assert.Equal(3, configured.MaximumApplicationWorkers);
    Assert.Throws<ArgumentException>(() =>
        WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget(
            ["--max-resident-workers", "0"]));
    Assert.Throws<ArgumentException>(() =>
        WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget(
            ["--max-resident-memory-mb", "192"]));
    return Task.CompletedTask;
}

static async Task WorkerResidencyBudgetAdmissionIsRaceSafe()
{
    var budget = new WorkerResidencyBudget(new WorkerResidencyBudgetOptions
    {
        MaximumApplicationWorkers = 4,
    });
    var owners = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();
    var admissions = await Task.WhenAll(owners.Select((owner, index) => Task.Run(() =>
    {
        try
        {
            budget.Reserve(owner, $"race-{index}", 1_024, isControlPlane: false);
            return true;
        }
        catch (WidgetProcessAdmissionException)
        {
            return false;
        }
    })));

    Assert.Equal(4, admissions.Count(admitted => admitted));
    Assert.Equal(4, budget.Snapshot.ApplicationWorkers);
    Assert.Equal(4_096L, budget.Snapshot.ApplicationAdvisoryMemoryMb);
    foreach (var owner in owners) budget.Release(owner);
    Assert.Equal(0, budget.Snapshot.ApplicationWorkers);
    Assert.Equal(0L, budget.Snapshot.ApplicationAdvisoryMemoryMb);
}

static async Task SettingsUsesSelectedCatalog()
{
    using var trusted = TemporaryCatalog.Create(
        id: "settings",
        packageId: "widgetrail.firstparty.settings",
        publisherId: "widgetrail.firstparty");
    using var temporary = new TemporaryDirectory("wrail-settings-selected-catalog");
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

static Task CatalogMemoryGuidanceIsAdvisory()
{
    using var valid = TemporaryCatalog.Create(memoryLimitMb: 1_024);
    var configured = BridgeCatalog.Load(valid.Path).GetConfigured("test-widget");
    Assert.Equal(1_024, configured.MemoryRequestMb);
    Assert.True(!configured.RequiresAppContainer,
        "Trusted built-in catalog workers must remain explicitly host-owned.");
    using var invalid = TemporaryCatalog.Create(memoryLimitMb: 0);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(invalid.Path));
    return Task.CompletedTask;
}

static Task ProtectedWifiHostAdmissionIsExact()
{
    var trusted = new ConfiguredWidget
    {
        Id = "network-controls",
        PackageId = "widgetrail.firstparty.network-controls",
        PublisherId = "widgetrail.firstparty",
        Name = "Network Controls",
        InstanceId = "network-controls",
        WorkerExecutable = Environment.ProcessPath!,
        WorkerFingerprint = new string('A', 64),
        CatalogFingerprint = new string('B', 64),
        DeclaredCapabilities = [PlatformCapabilities.NetworkWifiConnectV1],
    };
    Assert.True(trusted.PublicDescriptor().ProtectedWifiPromptSupported,
        "Exact first-party Network Controls did not receive trusted prompt admission.");
    Assert.False((trusted with { PackageId = "dev.example.network-controls" })
        .PublicDescriptor().ProtectedWifiPromptSupported,
        "A package spoof received trusted prompt admission.");
    Assert.False((trusted with { PublisherId = "dev.example" })
        .PublicDescriptor().ProtectedWifiPromptSupported,
        "A publisher spoof received trusted prompt admission.");
    Assert.False((trusted with { DeclaredCapabilities = [] })
        .PublicDescriptor().ProtectedWifiPromptSupported,
        "A capability-free worker received trusted prompt admission.");

    Assert.True(NetworkControlsHostPolicy.TryParseNetworkId(
        "network.wifi.item.wifi_0123456789ABCDEF", out var networkId),
        "A bounded opaque Wi-Fi source was rejected.");
    Assert.Equal("wifi_0123456789ABCDEF", networkId);
    foreach (var source in new[]
    {
        "network.wifi.item.profile_secret",
        "network.wifi.item.wifi_../secret",
        "network.wifi.item.wifi-secret",
        "network.wifi.item.",
        "network.wifi.item.wifi_" + new string('A', 129),
    })
        Assert.False(NetworkControlsHostPolicy.TryParseNetworkId(source, out _),
            $"Unsafe protected Wi-Fi source was admitted: {source}");

    var metadata = BridgeJson.FromElement<BridgeProtectedWifiRequest>(BridgeJson.ToElement(new
    {
        widgetId = "network-controls",
        runtimeGeneration = "generation-a",
        sourceElementId = "network.wifi.item.wifi_0123456789ABCDEF",
        secretLength = 14,
    }));
    Assert.Equal(14, metadata.SecretLength);
    Assert.Throws<JsonException>(() =>
        BridgeJson.FromElement<BridgeProtectedWifiRequest>(BridgeJson.ToElement(new
        {
            widgetId = "network-controls",
            runtimeGeneration = "generation-a",
            sourceElementId = "network.wifi.item.wifi_0123456789ABCDEF",
            secretLength = 14,
            secret = 1,
        })));
    return Task.CompletedTask;
}

static async Task ProtectedWifiProductionDispatchIsZeroed()
{
    if (!OperatingSystem.IsWindows()) return;
    using var catalogFiles = TemporaryCatalog.Create(
        id: "network-controls",
        packageId: "widgetrail.firstparty.network-controls",
        publisherId: "widgetrail.firstparty",
        instanceId: "network-controls",
        declaredCapabilities: [PlatformCapabilities.NetworkWifiConnectV1]);
    var catalog = BridgeCatalog.Load(catalogFiles.Path);
    var descriptor = catalog.GetConfigured("network-controls").PublicDescriptor();
    var network = new ProtectedWifiNetworkBackend();
    var simulator = new SimulatedPlatformBrokerBackend();
    await using var composite = new CompositePlatformBrokerBackend(simulator, network);
    var pipeName = $"wrail-bridge-protected-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(
        pipeName, catalog, 64 * 1024, platformBackend: composite);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    await using var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
    try
    {
        var lifecycle = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(
                "network-controls", WidgetLifecycleState.Interactive));
        Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);

        var secret = Enumerable.Range(0, 15)
            .Select(index => (char)('A' + index)).ToArray();
        var expected = SecretSentinel(secret);
        var response = await client.RequestProtectedWifiAsync(
            new BridgeProtectedWifiRequest(
                "network-controls",
                descriptor.RuntimeGeneration,
                "network.wifi.item.wifi_0123456789ABCDEF",
                secret.Length),
            secret);
        Assert.Equal(BridgeMessageTypes.Acknowledged, response.Type);
        Assert.Equal(expected, network.SecretSentinel);
        Assert.Equal("wifi_0123456789ABCDEF", network.NetworkId);
        Assert.True(network.ObservedSecretOwner is not null &&
            network.ObservedSecretOwner.All(value => value == '\0'),
            "Production dispatch retained the backend-owned secret characters.");
        Array.Clear(secret);
    }
    finally
    {
        try { await client.RequestAsync(BridgeMessageTypes.Stop, new { }); }
        catch { }
        await serverTask.WaitAsync(TimeSpan.FromSeconds(8));
    }
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
        _ => ValueTask.FromResult(WidgetRail.PlatformDiagnostics.PlatformDiagnosticsSnapshot.Unavailable()),
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

static async Task MediaSessionDiagnosticsAreBounded()
{
    using var temporary = new TemporaryDirectory("wrail-media-diagnostics");
    var path = System.IO.Path.Combine(temporary.Path, "overlay.log");
    await using (var diagnostics = new MediaSessionsDiagnosticLog(path))
    {
        diagnostics.Record(
            "media-sessions",
            PlatformCapabilities.MediaSessionsReadV1,
            PlatformCapabilities.MediaSessionsGet,
            "request",
            "channel_closed");
        diagnostics.Record(
            "media-sessions",
            PlatformCapabilities.MediaSessionsReadV1,
            PlatformCapabilities.MediaSessionsGet,
            "request",
            "channel_closed");
        diagnostics.Record(
            "media-sessions",
            PlatformCapabilities.MediaSessionsReadV1,
            PlatformCapabilities.MediaSessionsGet,
            "request",
            null);
        diagnostics.Record(
            "media-sessions",
            PlatformCapabilities.MediaSessionsReadV1,
            PlatformCapabilities.MediaSessionsGet,
            "request",
            "platform_unavailable");
        diagnostics.Record(
            "unsafe widget/path",
            PlatformCapabilities.MediaSessionsReadV1,
            PlatformCapabilities.MediaSessionsChanged,
            "subscription-read",
            "secret=player.exe");
        diagnostics.Record(
            "media-sessions",
            PlatformCapabilities.AppLibraryReadV1,
            "query",
            "request",
            "platform_unavailable");
    }

    var lines = File.ReadAllLines(path);
    Assert.Equal(2, lines.Length);
    Assert.True(lines[0].Contains(
        "widget=media-sessions stage=snapshot-read code=channel_closed",
        StringComparison.Ordinal), "First transition was not recorded.");
    Assert.True(lines[1].Contains(
        "widget=media-sessions stage=snapshot-read code=platform_unavailable",
        StringComparison.Ordinal), "Reset transition was not recorded.");
    Assert.True(lines.All(line =>
        !line.Contains("player.exe", StringComparison.OrdinalIgnoreCase) &&
        !line.Contains("secret", StringComparison.OrdinalIgnoreCase)),
        "Unsafe diagnostic content crossed the bounded log boundary.");
}

static async Task WorkerRequestDiagnosticsAreBounded()
{
    using var temporary = new TemporaryDirectory("wrail-worker-request-diagnostics");
    var path = System.IO.Path.Combine(temporary.Path, "overlay.log");
    await using (var diagnostics = new MediaSessionsDiagnosticLog(path, bridgeSessionGeneration: 7))
    {
        diagnostics.RecordRequestFailure(new BridgeWidgetRequestDiagnostic(
            "installed-widget",
            MessageTypes.Render,
            "worker_request_failed",
            "provider response\tcredential=DEVELOPER_ONLY"));
        diagnostics.RecordRequestFailure(new BridgeWidgetRequestDiagnostic(
            "unsafe widget/path",
            MessageTypes.Render,
            "worker_request_failed",
            "must-not-be-recorded"));
        diagnostics.RecordRequestFailure(new BridgeWidgetRequestDiagnostic(
            "installed-widget",
            "invalid request",
            "worker_request_failed",
            "must-not-be-recorded"));
    }

    var lines = File.ReadAllLines(path);
    Assert.Equal(1, lines.Length);
    Assert.True(lines[0].Contains(
        "Widget request diagnostic bridge-session=7 widget=installed-widget " +
        "request=render worker-code=worker_request_failed",
        StringComparison.Ordinal), "The correlated worker request record was not retained.");
    Assert.True(lines[0].Contains(
        "detail=\"provider response\\tcredential=DEVELOPER_ONLY\"",
        StringComparison.Ordinal), "The bounded worker diagnostic was not JSON escaped.");
    Assert.True(!lines[0].Contains('\t'),
        "The developer diagnostic wrote a raw control character into the record.");
    Assert.True(!lines[0].Contains("must-not-be-recorded", StringComparison.Ordinal),
        "An invalid diagnostic authority crossed the bounded log boundary.");
}

static ConfiguredWidget DiagnosticCandidate() => new()
{
    Id = "settings",
    PackageId = "widgetrail.firstparty.settings",
    PublisherId = "widgetrail.firstparty",
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
        "Expected a stable WRSS diagnostic code.");
    Assert.True(!exception.Message.Contains(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
        "Catalog diagnostics must not disclose absolute package paths.");
    return Task.CompletedTask;
}

static Task UnsafeStylePathIsRejected()
{
    using var catalog = TemporaryCatalog.Create(styleFile: "../outside.wrss");
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
    Assert.SequenceEqual(["icon", "id", "instanceId", "name", "pinningSupported", "presentationGeneration", "protectedWifiPromptSupported", "quickActions", "runtimeGeneration"],
        descriptor.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("test-widget", descriptor.GetProperty("id").GetString());
    Assert.Equal("Test Widget", descriptor.GetProperty("name").GetString());
    Assert.Equal("test.instance", descriptor.GetProperty("instanceId").GetString());
    Assert.Equal(32, descriptor.GetProperty("runtimeGeneration").GetString()!.Length);
    Assert.Equal(32, descriptor.GetProperty("presentationGeneration").GetString()!.Length);
    Assert.Equal("music", descriptor.GetProperty("icon").GetString());
    Assert.False(descriptor.GetProperty("pinningSupported").GetBoolean(),
        "Omitted manifest pinning support must project closed.");
    Assert.False(descriptor.GetProperty("protectedWifiPromptSupported").GetBoolean(),
        "Community descriptors cannot declare the trusted protected Wi-Fi prompt.");
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
            2, GlobalRequest(), _ => Task.FromException(new IOException("fatal transport")));
        await Assert.ThrowsAsync<IOException>(() => failed.Completion!);
        var fatal = await failureFatal.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("fatal transport", fatal.Message);
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

    var update = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.GetSnapshot,
        RequestId = 13,
        Payload = BridgeJson.ToElement(new BridgePresentationRequest(
            "widget-a", PresentationUpdateCapabilities.Current, 7,
            WidgetPresentationTransactionKind.IncrementalUpdate)),
    });
    Assert.Equal(BridgeRequestKind.GetSnapshot, update.Kind);
    Assert.Equal("widget-a", update.WidgetId);

    var artwork = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.ResolveArtwork,
        RequestId = 6,
        Payload = BridgeJson.ToElement(new BridgeArtworkRequest(
            "widget-a", "library.art.0123456789abcdef0123456789abcdef")),
    });
    Assert.Equal(BridgeRequestKind.ResolveArtwork, artwork.Kind);
    Assert.Equal<string?>(null, artwork.WidgetId);

    var launcherSelection = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.SelectLauncherExperience,
        RequestId = 11,
        Payload = BridgeJson.ToElement(new BridgeLauncherExperienceSelectionRequest(
            BridgeLauncherExperienceSelectionOperation.SelectExact,
            "dev.example.launcher", "2.0.0")),
    });
    Assert.Equal(BridgeRequestKind.SelectLauncherExperience, launcherSelection.Kind);
    Assert.Equal<string?>(null, launcherSelection.WidgetId);

    var malformedLauncherSelection = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.SelectLauncherExperience,
        RequestId = 12,
        Payload = BridgeJson.ToElement(new
        {
            operation = "recoverBuiltIn",
            id = "dev.example.forged",
        }),
    });
    Assert.Equal(BridgeRequestKind.Malformed, malformedLauncherSelection.Kind);

    var localInstall = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.InstallLocalWidgetPackage,
        RequestId = 8,
        Payload = BridgeJson.ToElement(new BridgeLocalWidgetPackageInstallRequest(
            "11111111-2222-3333-4444-555555555555",
            @"C:\fixture.wrwidget",
            new BridgeLocalWidgetPackageOrigin(
                "settings", "widgetrail.firstparty.settings", "widgetrail.firstparty",
                "settings.default", new string('a', 64), new string('b', 64)))),
    });
    Assert.Equal(BridgeRequestKind.InstallLocalWidgetPackage, localInstall.Kind);
    Assert.Equal<string?>(null, localInstall.WidgetId);

    var localCancel = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.CancelLocalWidgetPackageInstall,
        RequestId = 9,
        Payload = BridgeJson.ToElement(new BridgeLocalWidgetPackageInstallCancelRequest(
            "11111111-2222-3333-4444-555555555555")),
    });
    Assert.Equal(BridgeRequestKind.CancelLocalWidgetPackageInstall, localCancel.Kind);
    Assert.Equal<string?>(null, localCancel.WidgetId);

    var forgedLocalInstall = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.InstallLocalWidgetPackage,
        RequestId = 10,
        Payload = BridgeJson.ToElement(new
        {
            operationId = "11111111-2222-3333-4444-555555555555",
            packagePath = @"C:\fixture.wrwidget",
            origin = new
            {
                widgetId = "settings",
                packageId = "widgetrail.firstparty.settings",
                publisherId = "widgetrail.firstparty",
                instanceId = "settings.default",
                runtimeGeneration = new string('a', 64),
                presentationGeneration = new string('b', 64),
            },
            workerPath = @"C:\forbidden.exe",
        }),
    });
    Assert.Equal(BridgeRequestKind.Malformed, forgedLocalInstall.Kind);

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

static async Task CommittedTextCrossesBridgeAndWorker()
{
    await using var harness = await BridgeHarness.StartAsync();
    var lifecycle = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest(
            "test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));

    var action = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest(
            "test-widget",
            new WidgetActionEvent("committed-text", "button")
            {
                CommittedText = "Controller Proof",
            }));
    Assert.Equal(BridgeMessageTypes.Acknowledged, action.Type);
    _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        response.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal("committed:16", Flatten(snapshot.Root).Single(
        node => node.Id == "committed-text-status").Text);
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
    using var consentFiles = new TemporaryDirectory("wrail-artwork-consent");
    var identity = new BrokerWidgetIdentity(
        "dev.test.widget", "dev.test", "artwork.instance");
    var consent = new ConsentStore(consentFiles.Path);
    await consent.SetDecisionAsync(
        identity, PlatformCapabilities.AppLibraryReadV1, ConsentDecision.Grant);
    await consent.SetDecisionAsync(
        identity, PlatformCapabilities.AppLibraryLaunchV1, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibrary([
        BridgeAppLibraryItem("provider-one", "stable-one", "Artwork App", "artwork-a"),
        BridgeAppLibraryItem("provider-two", "stable-two", "Second App", "artwork-two"),
        BridgeAppLibraryItem(
            "provider-game", "stable-game", "Trusted Game", string.Empty,
            AppLibraryKind.Game, "source-steam", "Steam"),
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
    var pipeName = $"wrail-bridge-artwork-{Guid.NewGuid():N}";
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
        Assert.Equal(4, backend.AppLibraryRefreshCalls);
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

        backend.SetAppLibrary([
            BridgeAppLibraryItem(
                "provider-one", "stable-one", "Replacement", "artwork-b"),
            BridgeAppLibraryItem(
                "provider-two", "stable-two", "Second App", "artwork-two"),
            BridgeAppLibraryItem(
                "provider-game", "stable-game", "Trusted Game", string.Empty,
                AppLibraryKind.Game, "source-steam", "Steam"),
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

static AppLibraryItemSummary BridgeAppLibraryItem(
    string appId,
    string savedId,
    string displayName,
    string artworkRevision,
    AppLibraryKind kind = AppLibraryKind.Application,
    string sourceId = "source-windows",
    string sourceDisplayName = "Windows") => new(
    appId,
    savedId,
    new AppLibraryItemPresentation(
        displayName,
        kind,
        new AppLibrarySourceReference(sourceId, sourceDisplayName),
        new AppLibraryAvailabilitySummary(
            AppLibraryAvailabilityState.Installed, true, "installed"),
        new AppLibraryArtworkSet(
        [
            new AppLibraryArtworkSummary(
                AppLibraryArtworkRole.Tile, "simulated-artwork", artworkRevision,
                kind == AppLibraryKind.Game
                    ? AppLibraryArtworkFallback.Game
                    : AppLibraryArtworkFallback.Application),
        ]),
        Metadata: null,
        new AppLibraryCapabilitySet([AppLibraryAction.Launch]),
        ActiveOperation: null));

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
    var pipeName = $"wrail-bridge-stalled-{Guid.NewGuid():N}";
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
    var pipeName = $"wrail-bridge-duplicate-{Guid.NewGuid():N}";
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
    using var temporary = new TemporaryDirectory("wrail-bridge-installed");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
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
    Assert.Equal(256, installed.MemoryRequestMb);
    Assert.Equal(WidgetGlyph.Music, installed.Icon);
    Assert.Equal(Environment.ProcessPath, installed.WorkerExecutable);
    Assert.Equal("styles/default.wrss", installed.StyleFile);
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

static async Task InstalledAdvancedPresentationDeclarationsAreGeneric()
{
    using var temporary = new TemporaryDirectory("wrail-advanced-presentation");
    using var trusted = TemporaryCatalog.Create();
    var installedRoot = Path.Combine(temporary.Path, "installed");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(installedRoot);
    var declaration = new WidgetAdvancedPresentationDeclaration
    {
        SchemaVersion = WidgetAdvancedPresentationDeclaration.CurrentSchemaVersion,
        Kind = WidgetAdvancedPresentationKind.LauncherExperience,
    };
    await InstallWidgetAsync(
        catalog, temporary.Path, "org.random.alpha.surface", enabled: true,
        advancedPresentation: declaration,
        publisher: "org.random",
        assembly: "payload/AlphaSurface.dll",
        type: "Random.Alpha.SurfaceWidget");
    await InstallWidgetAsync(
        catalog, temporary.Path, "net.unrelated.bravo.deck", enabled: true,
        advancedPresentation: declaration,
        publisher: "net.unrelated",
        assembly: "payload/BravoDeck.dll",
        type: "Unrelated.Bravo.DeckWidget");
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, installedRoot, Environment.ProcessPath!);
    var declared = load.Catalog.Widgets
        .Where(widget => widget.AdvancedPresentation is not null)
        .OrderBy(widget => widget.Id, StringComparer.Ordinal)
        .ToArray();
    Assert.Equal(2, declared.Length);
    Assert.True(declared.All(widget =>
        widget.AdvancedPresentation!.SchemaVersion == 1 &&
        widget.AdvancedPresentation.Kind ==
            WidgetAdvancedPresentationKind.LauncherExperience),
        "Installed declarations did not survive the generic catalog boundary.");
    Assert.True(declared.Select(widget => widget.PresentationGeneration)
        .Distinct(StringComparer.Ordinal).Count() == 2,
        "Distinct installed package identities shared one presentation generation.");
}

static async Task InstalledLaunchAdmissionRejectsRace()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("wrail-bridge-launch-race");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
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
    using var temporary = new TemporaryDirectory("wrail-bridge-content-generation");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
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
    using var temporary = new TemporaryDirectory("wrail-bridge-installed-residency");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
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
    using var temporary = new TemporaryDirectory("wrail-bridge-capabilities");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
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
    using var temporary = new TemporaryDirectory("wrail-bridge-state-authority");
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

static async Task InstalledWorkerLocalDataClearIsExact()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("wrail-bridge-local-data");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
    var selected = await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.local-selected", enabled: true);
    var neighbor = await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.local-neighbor", enabled: true);
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    var privateRoot = Path.Combine(temporary.Path, "private-state");
    var backend = new WindowsCommunityPlatformBackend(privateRoot);
    var simulator = new SimulatedPlatformBrokerBackend();
    await using var composite = new CompositePlatformBrokerBackend(
        simulator, simulator, privateState: backend);
    var selectedConfigured = load.Catalog.GetConfigured(selected.Manifest.Id);
    var neighborConfigured = load.Catalog.GetConfigured(neighbor.Manifest.Id);
    Assert.Equal(InstalledWidgetInstanceIdentity.Derive(
        selected.Manifest.Id, selected.Manifest.Version), selectedConfigured.InstanceId);
    Assert.True(!string.Equals(selectedConfigured.InstanceId,
        InstalledWidgetInstanceIdentity.Derive(selected.Manifest.Id, "2.0.0"),
        StringComparison.Ordinal), "An active-version replacement reused local state identity.");
    var selectedIdentity = new BrokerWidgetIdentity(
        selectedConfigured.PackageId, selectedConfigured.PublisherId,
        selectedConfigured.InstanceId);
    var neighborIdentity = new BrokerWidgetIdentity(
        neighborConfigured.PackageId, neighborConfigured.PublisherId,
        neighborConfigured.InstanceId);
    var encoded = Convert.ToBase64String("{\"schemaVersion\":1}"u8);
    await backend.WritePrivateStateAsync(
        selectedIdentity, new WritePrivateStateRequest(encoded, null), CancellationToken.None);
    await backend.WritePrivateStateAsync(
        neighborIdentity, new WritePrivateStateRequest(encoded, null), CancellationToken.None);

    var pipeName = $"wrail-bridge-local-data-{Guid.NewGuid():N}";
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, load.Catalog);
    await using var server = new WidgetBridgeServer(
        pipeName, load.Catalog, 64 * 1024, platformBackend: composite,
        catalogMonitor: monitor);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    await using var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
    try
    {
        var started = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(
                selected.Manifest.Id, WidgetLifecycleState.Visible));
        Assert.Equal(BridgeMessageTypes.Acknowledged, started.Type);
        Assert.Equal(1, server.RunningWorkerCount);

        await catalog.SetEnabledAsync(selected.Manifest.Id, false);
        var disabled = await BridgeCatalog.LoadWithInstalledAsync(
            trusted.Path, catalogRoot, Environment.ProcessPath!);
        server.ApplyCatalog(disabled.Catalog, revision: 1, publishEvent: false);
        await WaitUntilAsync(() => server.RunningWorkerCount == 0,
            TimeSpan.FromSeconds(3));
        var inspection = await server.InspectWidgetLocalDataAsync(selected.Manifest.Id);
        Assert.True(inspection.Exists && inspection.ConfirmationToken is not null,
            "Installed state did not produce an exact confirmation token.");
        var currentState = await backend.ReadPrivateStateAsync(
            selectedIdentity, CancellationToken.None);
        await backend.WritePrivateStateAsync(
            selectedIdentity, new WritePrivateStateRequest(encoded, currentState.Revision),
            CancellationToken.None);
        var stale = await server.ClearWidgetLocalDataAsync(
            selected.Manifest.Id, inspection.ConfirmationToken!);
        Assert.Equal(PlatformWidgetLocalDataClearStatus.Stale, stale.Status);
        Assert.True((await backend.ReadPrivateStateAsync(
            selectedIdentity, CancellationToken.None)).Exists,
            "A stale disabled confirmation cleared current state.");
        inspection = await server.InspectWidgetLocalDataAsync(selected.Manifest.Id);
        var result = await server.ClearWidgetLocalDataAsync(
            selected.Manifest.Id, inspection.ConfirmationToken!);
        Assert.Equal(PlatformWidgetLocalDataClearStatus.Cleared, result.Status);
        Assert.True(server.RunningWorkerCount == 0,
            "Disabled local-data clear created a worker generation.");
        Assert.False((await backend.ReadPrivateStateAsync(
            selectedIdentity, CancellationToken.None)).Exists,
            "Selected installed state survived clear.");
        Assert.True((await backend.ReadPrivateStateAsync(
            neighborIdentity, CancellationToken.None)).Exists,
            "Neighbor installed state changed during clear.");

        await catalog.SetEnabledAsync(selected.Manifest.Id, true);
        var reenabled = await BridgeCatalog.LoadWithInstalledAsync(
            trusted.Path, catalogRoot, Environment.ProcessPath!);
        server.ApplyCatalog(reenabled.Catalog, revision: 2, publishEvent: false);
        var clean = await server.InspectWidgetLocalDataAsync(selected.Manifest.Id);
        Assert.False(clean.Exists,
            "Re-enabled unchanged package observed state cleared from another identity.");
        Assert.True(server.RunningWorkerCount == 0,
            "Inspection alone created a re-enabled worker generation.");
        var restarted = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(
                selected.Manifest.Id, WidgetLifecycleState.Visible));
        Assert.Equal(BridgeMessageTypes.Acknowledged, restarted.Type);
        Assert.Equal(1, server.RunningWorkerCount);
    }
    finally
    {
        await client.DisposeAsync();
        await server.DisposeAsync();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception exception) when (exception is EndOfStreamException or
                                               OperationCanceledException or
                                               ObjectDisposedException) { }
    }
}

static async Task InstalledPackageUninstallIsExact()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("wrail-bridge-uninstall");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
    var selected = await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.uninstall", enabled: false);
    await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.uninstall", enabled: false,
        version: "2.0.0");
    var neighbor = await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.uninstall-neighbor", enabled: false);
    var privateState = Path.Combine(temporary.Path, "private-state", "selected.json");
    Directory.CreateDirectory(Path.GetDirectoryName(privateState)!);
    await File.WriteAllTextAsync(privateState, "retained");

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, load.Catalog);
    await using var server = new WidgetBridgeServer(
        $"wrail-bridge-uninstall-{Guid.NewGuid():N}", load.Catalog, 64 * 1024,
        catalogMonitor: monitor);

    var inspection = await server.InspectWidgetPackageUninstallAsync(selected.Manifest.Id);
    Assert.True(inspection.CanUninstall && inspection.ConfirmationToken is not null,
        "Disabled package did not receive exact uninstall admission.");
    Assert.Equal(2, inspection.VersionCount);
    Assert.True(!inspection.ConfirmationToken!.Contains(catalogRoot,
        StringComparison.OrdinalIgnoreCase), "Confirmation token exposed a path.");

    var forged = await server.UninstallWidgetPackageAsync(
        inspection.WidgetId, "forged.publisher", inspection.ActiveVersion,
        inspection.ConfirmationToken);
    Assert.Equal(PlatformWidgetPackageUninstallStatus.Stale, forged.Status);
    Assert.True(Directory.Exists(Path.Combine(catalogRoot, "packages", selected.Manifest.Id)),
        "Forged uninstall mutated package bytes.");

    var revisionBefore = monitor.Revision;
    var installed = (await catalog.DiscoverAsync()).Widgets
        .Single(item => item.Id == selected.Manifest.Id).ActiveVersion;
    using (InstalledPackageLaunchLease.Acquire(catalogRoot, installed))
    {
        var resident = await server.UninstallWidgetPackageAsync(
            inspection.WidgetId, inspection.PublisherId, inspection.ActiveVersion,
            inspection.ConfirmationToken);
        Assert.Equal(PlatformWidgetPackageUninstallStatus.Resident, resident.Status);
        Assert.Equal(revisionBefore, monitor.Revision);
        Assert.True((await catalog.DiscoverAsync()).Widgets.Any(item =>
            item.Id == selected.Manifest.Id),
            "Resident refusal removed the selected package.");
    }
    var result = await server.UninstallWidgetPackageAsync(
        inspection.WidgetId, inspection.PublisherId, inspection.ActiveVersion,
        inspection.ConfirmationToken);
    Assert.Equal(PlatformWidgetPackageUninstallStatus.Uninstalled, result.Status);
    Assert.Equal(revisionBefore + 1, monitor.Revision);
    Assert.True((await catalog.DiscoverAsync()).Widgets.All(item => item.Id != selected.Manifest.Id),
        "Exact uninstall retained the selected package.");
    Assert.True((await catalog.DiscoverAsync()).Widgets.Any(item => item.Id == neighbor.Manifest.Id),
        "Exact uninstall changed the neighbor.");
    Assert.Equal("retained", await File.ReadAllTextAsync(privateState));

    var stale = await server.UninstallWidgetPackageAsync(
        inspection.WidgetId, inspection.PublisherId, inspection.ActiveVersion,
        inspection.ConfirmationToken);
    Assert.Equal(PlatformWidgetPackageUninstallStatus.Stale, stale.Status);
    Assert.Equal(revisionBefore + 1, monitor.Revision);
}

static async Task TamperedInstalledCatalogFailsSoft()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("wrail-bridge-tampered");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
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
    using var temporary = new TemporaryDirectory("wrail-bridge-invalid-style");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
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
    using var temporary = new TemporaryDirectory("wrail-bridge-live-catalog");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
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
    using var temporary = new TemporaryDirectory("wrail-bridge-tamper-retire");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(catalogRoot);
    var installed = await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.tamper", enabled: true);
    var initial = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, initial.Catalog);
    var pipeName = $"wrail-bridge-tamper-{Guid.NewGuid():N}";
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
    using var temporary = new TemporaryDirectory("wrail-bridge-catalog-catch-up");
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

static async Task LocalPackageImportIsDisabledRevisionedAndPathFree()
{
    using var root = new TemporaryDirectory("wrail-local-package-import");
    using var trusted = TemporaryCatalog.Create(
        id: "settings",
        packageId: "widgetrail.firstparty.settings",
        publisherId: "widgetrail.firstparty",
        instanceId: "settings.default",
        declaredCapabilities: []);
    var initial = BridgeCatalog.Load(trusted.Path);
    await using var registry = new RegistryFixture(initial);
    await registry.SetLifecycleAsync("settings", WidgetLifecycleState.Interactive);
    var publicSettings = initial.Widgets.Single();
    var origin = new BridgeLocalWidgetPackageOrigin(
        publicSettings.Id, "widgetrail.firstparty.settings", "widgetrail.firstparty",
        publicSettings.InstanceId, publicSettings.RuntimeGeneration,
        publicSettings.PresentationGeneration);
    var installedRoot = Path.Combine(root.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(installedRoot);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, installedRoot, Environment.ProcessPath!, initial);
    var revisions = 0;
    monitor.Changed += (_, _) => ++revisions;
    var completion = new TaskCompletionSource<BridgeLocalWidgetPackageInstallCompleted>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await using var service = new BridgeLocalWidgetPackageImportService(
        registry.Registry, catalog, monitor,
        result => { completion.TrySetResult(result); return Task.CompletedTask; });
    var packagePath = await CreateWidgetPackageAsync(
        root.Path, "dev.example.local", "1.2.3");
    service.Start(new BridgeLocalWidgetPackageInstallRequest(
        "11111111-2222-3333-4444-555555555555", packagePath, origin),
        CancellationToken.None);
    var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.Equal("installed-disabled", result.Status);
    Assert.Equal("dev.example.local", result.WidgetId);
    Assert.Equal("1.2.3", result.Version);
    Assert.True(!result.Message.Contains(packagePath, StringComparison.OrdinalIgnoreCase) &&
                !result.Message.Contains('\\') && !result.Message.Contains('/'),
        "Local package completion exposed a filesystem path.");
    var snapshot = await catalog.DiscoverAsync();
    var installed = snapshot.Widgets.Single(widget => widget.Id == "dev.example.local");
    Assert.False(installed.Enabled, "Local import enabled a package without review.");
    Assert.Equal("1.2.3", installed.ActiveVersion.Version.ToString());
    Assert.Equal(1L, monitor.Revision);
    Assert.Equal(1, revisions);
}

static async Task LocalPackageImportFailuresPreserveCatalog()
{
    using var root = new TemporaryDirectory("wrail-local-package-failures");
    var installedRoot = Path.Combine(root.Path, "catalog");
    var catalog = new WidgetRail.WidgetCatalog.WidgetCatalog(installedRoot);
    var origin = new BridgeLocalWidgetPackageOrigin(
        "settings", "widgetrail.firstparty.settings", "widgetrail.firstparty",
        "settings.default", new string('a', 64), new string('b', 64));
    var operation = 0;
    var originCurrent = true;
    var beforePublish = new Func<CancellationToken, Task>(_ => Task.CompletedTask);

    async Task<BridgeLocalWidgetPackageInstallCompleted> RunAsync(
        string packagePath,
        Func<string, Stream>? opener = null,
        Func<CancellationToken, Task>? prePublish = null,
        Action<BridgeLocalWidgetPackageImportService>? started = null)
    {
        var completion = new TaskCompletionSource<BridgeLocalWidgetPackageInstallCompleted>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new BridgeLocalWidgetPackageImportService(
            catalog,
            _ => originCurrent
                ? NoopDisposable.Instance
                : throw new BridgeProtocolException("Synthetic stale origin."),
            _ => Task.CompletedTask,
            result => { completion.TrySetResult(result); return Task.CompletedTask; },
            opener,
            prePublish ?? beforePublish);
        var operationId = $"00000000-0000-0000-0000-{++operation:000000000000}";
        service.Start(new BridgeLocalWidgetPackageInstallRequest(
            operationId, packagePath, origin), CancellationToken.None);
        started?.Invoke(service);
        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    var invalid = Path.Combine(root.Path, "invalid.wrwidget");
    await File.WriteAllTextAsync(invalid, "not a package");
    var invalidResult = await RunAsync(invalid);
    Assert.Equal("failed", invalidResult.Status);
    Assert.Equal(0, (await catalog.DiscoverAsync()).Widgets.Count);
    Assert.True(!invalidResult.Message.Contains(root.Path, StringComparison.OrdinalIgnoreCase),
        "Invalid-package diagnostic exposed its source path.");

    var valid = await CreateWidgetPackageAsync(root.Path, "dev.example.stable", "1.0.0");
    var first = await RunAsync(valid);
    Assert.Equal("installed-disabled", first.Status);
    var beforeDuplicate = (await catalog.DiscoverAsync()).Widgets.Single()
        .ActiveVersion.ContentDigest;
    var duplicate = await RunAsync(valid);
    Assert.Equal("failed", duplicate.Status);
    var afterDuplicate = (await catalog.DiscoverAsync()).Widgets.Single();
    Assert.Equal(beforeDuplicate, afterDuplicate.ActiveVersion.ContentDigest);
    Assert.False(afterDuplicate.Enabled, "Duplicate import changed enabled state.");

    var stalePackage = await CreateWidgetPackageAsync(
        root.Path, "dev.example.stale", "1.0.0");
    originCurrent = true;
    var stale = await RunAsync(
        stalePackage,
        prePublish: _ => { originCurrent = false; return Task.CompletedTask; });
    Assert.Equal("failed", stale.Status);
    Assert.True((await catalog.DiscoverAsync()).Widgets.All(
        widget => widget.Id != "dev.example.stale"),
        "Stale Settings origin published a package.");
    originCurrent = true;

    var lockedPackage = await CreateWidgetPackageAsync(
        root.Path, "dev.example.locked", "1.0.0");
    var lockEntered = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var lockRelease = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var lockedTask = RunAsync(
        lockedPackage,
        prePublish: async token =>
        {
            lockEntered.TrySetResult();
            await lockRelease.Task.WaitAsync(token);
        });
    await lockEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    _ = Assert.Throws<IOException>(() =>
    {
        using var writer = new FileStream(
            lockedPackage, FileMode.Open, FileAccess.Write, FileShare.None);
    });
    lockRelease.TrySetResult();
    Assert.Equal("installed-disabled", (await lockedTask).Status);

    var cancelPackage = await CreateWidgetPackageAsync(
        root.Path, "dev.example.cancel", "1.0.0");
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    BridgeLocalWidgetPackageImportService? active = null;
    var cancelTask = RunAsync(
        cancelPackage,
        prePublish: async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        },
        started: service => active = service);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.True(active!.Cancel("00000000-0000-0000-0000-000000000006"),
        "Active local import did not accept exact cancellation.");
    Assert.Equal("cancelled", (await cancelTask).Status);
    Assert.True((await catalog.DiscoverAsync()).Widgets.All(
        widget => widget.Id != "dev.example.cancel"),
        "Cancelled package was published.");

    var linkTarget = await CreateWidgetPackageAsync(
        root.Path, "dev.example.reparse", "1.0.0");
    var link = Path.Combine(root.Path, "reparse.wrwidget");
    File.CreateSymbolicLink(link, linkTarget);
    var reparse = await RunAsync(link);
    Assert.Equal("failed", reparse.Status);
    Assert.True((await catalog.DiscoverAsync()).Widgets.All(
        widget => widget.Id != "dev.example.reparse"),
        "Reparse-point package was published.");
}

static async Task<InstalledWidgetVersion> InstallWidgetAsync(
    WidgetRail.WidgetCatalog.WidgetCatalog catalog,
    string packageDirectory,
    string id,
    bool enabled,
    string styleSource = "button { color: #abcdef; }",
    IReadOnlyList<string>? permissions = null,
    WidgetResidencyPolicy? residencyPolicy = null,
    WidgetGlyph icon = WidgetGlyph.Connection,
    string version = "1.0.0",
    WidgetAdvancedPresentationDeclaration? advancedPresentation = null,
    string publisher = "dev.example",
    string assembly = "payload/Widget.dll",
    string type = "Example.EnabledWidget")
{
    var packagePath = await CreateWidgetPackageAsync(
        packageDirectory, id, version, styleSource, permissions, residencyPolicy, icon,
        advancedPresentation, publisher, assembly, type);
    var installed = await catalog.InstallAsync(packagePath);
    if (enabled) await catalog.SetEnabledAsync(id, true);
    return installed;
}

static async Task<string> CreateWidgetPackageAsync(
    string packageDirectory,
    string id,
    string version,
    string styleSource = "button { color: #abcdef; }",
    IReadOnlyList<string>? permissions = null,
    WidgetResidencyPolicy? residencyPolicy = null,
    WidgetGlyph icon = WidgetGlyph.Connection,
    WidgetAdvancedPresentationDeclaration? advancedPresentation = null,
    string publisher = "dev.example",
    string assembly = "payload/Widget.dll",
    string type = "Example.EnabledWidget")
{
    var packagePath = Path.Combine(
        packageDirectory, $"{id}-{version}-{Guid.NewGuid():N}.wrwidget");
    var manifest = new WidgetManifest
    {
        Id = id,
        Publisher = publisher,
        Name = id.EndsWith("enabled", StringComparison.Ordinal) ? "Enabled Widget" : "Test Widget",
        Version = version,
        HostApi = new HostApiRange("1.0", 1),
        Entrypoint = new WidgetEntrypoint(
            "dotnet-worker", assembly, type),
        Presentation = new WidgetPresentation(icon),
        AdvancedPresentation = advancedPresentation,
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
        WriteArchiveEntry(archive, assembly, [0x4d, 0x5a]);
        WriteArchiveEntry(archive, "styles/default.wrss",
            System.Text.Encoding.UTF8.GetBytes(styleSource));
    }
    return packagePath;
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
        ["animateWidgetSwitching", "backdropOpacity", "boldText", "contrast", "interfaceScale", "motion", "revision", "shellStyles", "textScale", "themeId", "themeVersion", "transparency"],
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
    Assert.Equal(true, response.Payload.GetProperty("animateWidgetSwitching").GetBoolean());
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
    Assert.Equal(0L, harness.Server.ResidencyBudget.ApplicationAdvisoryMemoryMb);

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
    Assert.True(snapshot.ProtocolVersion < ProtocolConstants.AtomicPresentationUpdateVersion,
        "A legacy Bridge request unexpectedly activated protocol-18 update traffic.");
    var renderStyles = snapshotResponse.Payload.GetProperty("renderStyles");
    Assert.Equal(6, renderStyles.EnumerateObject().Count());
    Assert.True(renderStyles.TryGetProperty("committed-text-status", out _),
        "Committed text status was omitted from the bridge render-style map.");
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
    var negotiatedRequest = new BridgePresentationRequest(
        "test-widget",
        PresentationUpdateCapabilities.Current,
        snapshot.Sequence,
        WidgetPresentationTransactionKind.IncrementalUpdate);
    _ = BridgeJson.FromElement<BridgePresentationRequest>(
        BridgeJson.ToElement(negotiatedRequest));
    var updatedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot,
        negotiatedRequest);
    Assert.True(updatedResponse.Type == BridgeMessageTypes.PresentationUpdate,
        $"Expected presentation-update, received '{updatedResponse.Type}': " +
        updatedResponse.Payload.GetRawText());
    Assert.Equal("incrementalUpdate",
        updatedResponse.Payload.GetProperty("transactionKind").GetString());
    var update = PresentationUpdateJson.Deserialize(
        System.Text.Encoding.UTF8.GetBytes(
            updatedResponse.Payload.GetProperty("update").GetRawText()));
    Assert.Equal(snapshot.Sequence, update.BaseSequence);
    Assert.True(update.Sequence > update.BaseSequence,
        "The bridge did not forward a newer atomic presentation sequence.");
    Assert.True(update.Operations.Count > 0,
        "The changed worker view produced an empty bridge update.");
    Assert.Equal(6,
        updatedResponse.Payload.GetProperty("renderStyles").EnumerateObject().Count());

    var checkpointResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Snapshot, checkpointResponse.Type);
    var updated = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        checkpointResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.True(updated.ProtocolVersion < ProtocolConstants.AtomicPresentationUpdateVersion,
        "Legacy checkpoint fallback unexpectedly required atomic-update support.");
    Assert.Equal(0.6D, FindNode(updated.Root, "volume").Value);
    Assert.Equal("physical,automation,physical", FindNode(updated.Root, "busy-button").Text);

    var malformedCapabilities = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot,
        new BridgePresentationRequest(
            "test-widget",
            PresentationUpdateCapabilities.Current with
            {
                MaximumOperationsPerBatch =
                    ProtocolConstants.MaximumPresentationUpdateOperations + 1,
            },
            updated.Sequence,
            WidgetPresentationTransactionKind.IncrementalUpdate));
    Assert.Equal(BridgeMessageTypes.Error, malformedCapabilities.Type);
    Assert.Equal("request_failed",
        malformedCapabilities.Payload.GetProperty("code").GetString());

    var malformedRecovery = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot,
        new BridgePresentationRequest(
            "test-widget",
            PresentationUpdateCapabilities.None,
            BaseSequence: 0,
            TransactionKind: WidgetPresentationTransactionKind.RecoveryCheckpoint,
            RecoveryOriginSequence: 0));
    Assert.Equal(BridgeMessageTypes.Error, malformedRecovery.Type);
    Assert.Equal("request_failed",
        malformedRecovery.Payload.GetProperty("code").GetString());
}

static async Task ExactBaseDivergenceConvergesThroughCheckpoint()
{
    await using var harness = await BridgeHarness.StartAsync();
    var initialResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Snapshot, initialResponse.Type);
    Assert.Equal("ordinaryCheckpoint",
        initialResponse.Payload.GetProperty("transactionKind").GetString());
    var initial = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        initialResponse.Payload.GetProperty("snapshot").GetRawText()));

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    var action = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest("test-widget", new WidgetActionEvent("refresh", "button")));
    Assert.Equal(BridgeMessageTypes.Acknowledged, action.Type);
    _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);

    var advancedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot,
        new BridgePresentationRequest(
            "test-widget", PresentationUpdateCapabilities.Current, initial.Sequence,
            WidgetPresentationTransactionKind.IncrementalUpdate));
    Assert.Equal(BridgeMessageTypes.PresentationUpdate, advancedResponse.Type);
    Assert.Equal("incrementalUpdate",
        advancedResponse.Payload.GetProperty("transactionKind").GetString());
    var advancedUpdate = PresentationUpdateJson.Deserialize(
        System.Text.Encoding.UTF8.GetBytes(
            advancedResponse.Payload.GetProperty("update").GetRawText()));
    Assert.Equal(initial.Sequence, advancedUpdate.BaseSequence);
    Assert.True(advancedUpdate.Sequence > initial.Sequence,
        "The bridge did not advance its private checkpoint from the exact host base.");

    var staleResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot,
        new BridgePresentationRequest(
            "test-widget", PresentationUpdateCapabilities.Current, initial.Sequence,
            WidgetPresentationTransactionKind.IncrementalUpdate));
    Assert.Equal(BridgeMessageTypes.Error, staleResponse.Type);
    Assert.Equal("stale_presentation_base",
        staleResponse.Payload.GetProperty("code").GetString());

    var recoveryResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot,
        new BridgePresentationRequest(
            "test-widget",
            PresentationUpdateCapabilities.None,
            BaseSequence: 0,
            TransactionKind: WidgetPresentationTransactionKind.RecoveryCheckpoint,
            RecoveryOriginSequence: initial.Sequence));
    Assert.Equal(BridgeMessageTypes.Snapshot, recoveryResponse.Type);
    Assert.Equal("recoveryCheckpoint",
        recoveryResponse.Payload.GetProperty("transactionKind").GetString());
    var recovery = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        recoveryResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.True(recovery.Sequence > initial.Sequence,
        "The base-zero recovery checkpoint did not converge beyond the retained host base.");
    Assert.Equal("unknown", FindNode(recovery.Root, "busy-button").Text);

    var secondAction = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest(
            "test-widget",
            new WidgetActionEvent("volume.changed", "volume", RequestedValue: 0.7)));
    Assert.Equal(BridgeMessageTypes.Acknowledged, secondAction.Type);
    _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    var convergedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot,
        new BridgePresentationRequest(
            "test-widget", PresentationUpdateCapabilities.Current, recovery.Sequence,
            WidgetPresentationTransactionKind.IncrementalUpdate));
    Assert.Equal(BridgeMessageTypes.PresentationUpdate, convergedResponse.Type);
    Assert.Equal("incrementalUpdate",
        convergedResponse.Payload.GetProperty("transactionKind").GetString());
    var converged = PresentationUpdateJson.Deserialize(
        System.Text.Encoding.UTF8.GetBytes(
            convergedResponse.Payload.GetProperty("update").GetRawText()));
    Assert.Equal(recovery.Sequence, converged.BaseSequence);
    Assert.True(converged.Sequence > converged.BaseSequence,
        "The post-recovery exact base did not resume ordinary incremental publication.");
    var convergedSnapshot = PresentationUpdateMaterializer.Apply(
        recovery, converged, converged.PresentationGeneration);
    Assert.Equal(0.7D, FindNode(convergedSnapshot.Root, "volume").Value);
}

static async Task ManagedPresentationSessionPreservesSandboxedAuthority()
{
    using var temporary = TemporaryCatalog.Create();
    var catalog = BridgeCatalog.Load(temporary.Path);
    var pipeName = $"wrail-session-sandboxed-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(pipeName, catalog, 64 * 1024);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    var session = await WidgetPresentationSession.ConnectAsync(
        pipeName,
        new WidgetPresentationSessionOptions
        {
            ClientName = "WidgetBridge.Tests.AVP004.Session",
            MaximumMessageBytes = 64 * 1024,
            MaximumRetainedDiagnostics = 8,
        });
    try
    {
        var listed = await session.ListWidgetsAsync();
        Assert.Equal(0L, listed.Revision);
        Assert.SequenceEqual(["test-widget"], listed.Widgets.Select(widget => widget.Id));
        var target = session.GetTarget("test-widget");
        var initial = await session.EstablishPresentationAsync(
            target, WidgetLifecycleState.Interactive);
        Assert.Equal(target.Descriptor.RuntimeGeneration, initial.Authority.RuntimeGeneration);
        Assert.Equal(
            target.Descriptor.PresentationGeneration,
            initial.Authority.PresentationGeneration);
        Assert.Equal(initial.Snapshot.Sequence, initial.Authority.SnapshotSequence);
        Assert.Equal(initial.Snapshot.ActiveInputScopeId, initial.Authority.ActiveInputScopeId);
        Assert.Equal(6, initial.RenderStyles.Count);

        var refreshed = WaitForPresentationAsync(
            session,
            state => state.LastGood is { } frame &&
                     frame.Authority.SnapshotSequence > initial.Authority.SnapshotSequence);
        var admission = await session.SendActionAsync(
            initial.Authority,
            new WidgetActionEvent(
                "refresh",
                "button",
                Sequence: 10,
                MonotonicTimestampMicroseconds: 1_000,
                InputScopeId: initial.Authority.ActiveInputScopeId));
        Assert.Equal(WidgetOperationAdmission.Enqueued, admission);
        var latest = (await refreshed).LastGood!;
        Assert.Equal("unknown", FindNode(latest.Snapshot.Root, "busy-button").Text);

        var input = new ControllerInputEvent(
            ControllerButton.RightBumper,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "button",
            Sequence: 11,
            MonotonicTimestampMicroseconds: 2_000,
            ActiveInputScopeId: latest.Authority.ActiveInputScopeId,
            SnapshotSequence: latest.Authority.SnapshotSequence);
        var controllerRefresh = WaitForPresentationAsync(
            session,
            state => state.LastGood is { } frame &&
                     frame.Authority.SnapshotSequence > latest.Authority.SnapshotSequence);
        Assert.True(await session.SendControllerInputAsync(latest.Authority, input),
            "Managed controller input was not handled through the bridge.");
        latest = (await controllerRefresh).LastGood!;

        var quickRefresh = WaitForPresentationAsync(
            session,
            state => state.LastGood is { } frame &&
                     frame.Authority.SnapshotSequence > latest.Authority.SnapshotSequence);
        var quickAdmission = await session.InvokeQuickActionAsync(
            target, "hover-refresh", 12, 3_000);
        Assert.Equal(WidgetOperationAdmission.Enqueued, quickAdmission);
        latest = (await quickRefresh).LastGood!;

        var failureState = WaitForPresentationAsync(
            session,
            state => state.Failure is { ActionId: "fail" });
        var failureAdmission = await session.SendActionAsync(
            latest.Authority,
            new WidgetActionEvent(
                "fail",
                "button",
                Sequence: 13,
                MonotonicTimestampMicroseconds: 4_000,
                InputScopeId: latest.Authority.ActiveInputScopeId));
        Assert.Equal(WidgetOperationAdmission.Enqueued, failureAdmission);
        var failed = await failureState;
        Assert.Equal(latest, failed.LastGood);
        Assert.Equal(latest.Authority.RuntimeGeneration, failed.Failure!.RuntimeGeneration);
        Assert.Equal("Action failed.", failed.Failure.Message);

        var staleInput = input with
        {
            SnapshotSequence = latest.Authority.SnapshotSequence + 1,
            ActiveInputScopeId = latest.Authority.ActiveInputScopeId,
        };
        var staleException = await Assert.ThrowsAsync<WidgetPresentationSessionException>(
            () => session.SendControllerInputAsync(latest.Authority, staleInput));
        Assert.Equal("snapshot_stale", staleException.Code);

        var restoredState = await session.RestartAsync(target);
        Assert.Equal(WidgetLifecycleState.Interactive, restoredState);
        Assert.True(session.GetState("test-widget")?.LastGood is null,
            "Restart retained stale presentation authority.");
        var staleAction = await Assert.ThrowsAsync<WidgetPresentationSessionException>(
            () => session.SendActionAsync(
                latest.Authority,
                new WidgetActionEvent(
                    "refresh", "button",
                    InputScopeId: latest.Authority.ActiveInputScopeId)));
        Assert.Equal("presentation_stale", staleAction.Code);
        var restarted = await session.EstablishPresentationAsync(
            target, WidgetLifecycleState.Interactive);
        Assert.Equal("test.instance", restarted.Authority.WidgetInstanceId);
        Assert.True(session.Diagnostics.Count <= 8,
            "Managed presentation diagnostics exceeded their configured bound.");
    }
    finally
    {
        await session.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }
}

static async Task ManagedPresentationSessionPreservesFullTrustRuntime()
{
    using var temporary = new TemporaryDirectory("wrail-session-full-trust");
    var repositoryRoot = FindRepositoryRoot();
    var fixtureOutput = Path.Combine(
        repositoryRoot,
        "tests",
        "FullTrustAlphaFixture",
        "bin",
        "Release",
        "net8.0",
        "win-x64");
    var package = CreateFullTrustSessionPackage(
        temporary.Path,
        fixtureOutput,
        "FullTrustAlphaFixture.exe",
        "dev.avp004.session-full-trust");
    var installedRoot = Path.Combine(temporary.Path, "installed");
    var installedCatalog = new WidgetRail.WidgetCatalog.WidgetCatalog(installedRoot);
    var installed = await installedCatalog.InstallAsync(
        package, WidgetPackageTrustApproval.FullTrustCurrentUser);
    await installedCatalog.SetEnabledAsync(
        installed.Id, true, WidgetPackageTrustApproval.FullTrustCurrentUser);

    var trustedWorker = Path.Combine(temporary.Path, "trusted-worker.exe");
    File.Copy(Environment.ProcessPath!, trustedWorker);
    var trustedCatalogPath = Path.Combine(temporary.Path, "trusted-catalog.json");
    await File.WriteAllTextAsync(trustedCatalogPath, """
        {
          "catalogVersion": 1,
          "widgets": [],
          "bundledWidgets": [],
          "genericWorkerExecutable": "trusted-worker.exe"
        }
        """);
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trustedCatalogPath, installedRoot, trustedWorker);
    Assert.Equal(0, load.Warnings.Count);
    var configured = load.Catalog.GetConfigured(installed.Id);
    Assert.Equal(WidgetExecutionTrust.FullTrustCurrentUser, configured.ExecutionTrust);

    var pipeName = $"wrail-session-full-trust-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(pipeName, load.Catalog, 64 * 1024);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    var session = await WidgetPresentationSession.ConnectAsync(
        pipeName,
        new WidgetPresentationSessionOptions
        {
            ClientName = "WidgetBridge.Tests.AVP004.FullTrust",
            MaximumMessageBytes = 64 * 1024,
        });
    try
    {
        _ = await session.ListWidgetsAsync();
        var target = session.GetTarget(installed.Id);
        var frame = await session.EstablishPresentationAsync(
            target, WidgetLifecycleState.Interactive);
        var result = FindNode(frame.Snapshot.Root, "alpha-result").Text ?? string.Empty;
        Assert.True(
            result.Contains("child=True", StringComparison.Ordinal) &&
            result.Contains("file=True", StringComparison.Ordinal) &&
            result.Contains("database=True", StringComparison.Ordinal) &&
            result.Contains("https=True", StringComparison.Ordinal),
            "The managed presentation session changed full-trust runtime behavior.");

        var failedState = WaitForPresentationAsync(
            session,
            state => state.Failure is { CanRestart: true });
        var admissionOutcome = "not-observed";
        try
        {
            var admission = await session.SendActionAsync(
                frame.Authority,
                new WidgetActionEvent(
                    "crash",
                    "alpha-crash",
                    Sequence: 1,
                    MonotonicTimestampMicroseconds: 1_000,
                    InputScopeId: frame.Authority.ActiveInputScopeId));
            admissionOutcome = admission.ToString();
            Assert.True(
                admission == WidgetOperationAdmission.Enqueued,
                $"Unexpected full-trust crash admission outcome '{admissionOutcome}'.");
        }
        catch (WidgetPresentationSessionException exception)
        {
            admissionOutcome = $"{nameof(WidgetPresentationSessionException)}:{exception.Code}";
            Assert.True(
                exception.Code == "worker-runtime-failed",
                $"Unexpected full-trust crash admission outcome '{admissionOutcome}'.");
        }

        var failed = await failedState;
        Assert.True(
            Equals(frame, failed.LastGood),
            $"Full-trust crash changed LastGood after admission outcome '{admissionOutcome}'.");
        Assert.True(failed.Failure!.CanRestart,
            $"Full-trust restart authority was not preserved after admission outcome '{admissionOutcome}'.");
        var staleAfterFailure = await Assert.ThrowsAsync<WidgetPresentationSessionException>(
            () => session.SendActionAsync(
                frame.Authority,
                new WidgetActionEvent(
                    "crash", "alpha-crash",
                    InputScopeId: frame.Authority.ActiveInputScopeId)));
        Assert.True(
            staleAfterFailure.Code == "presentation_stale",
            $"Expected stale old authority after admission outcome '{admissionOutcome}', " +
            $"got '{staleAfterFailure.Code}'.");

        var recovered = await session.EstablishPresentationAsync(
            target, WidgetLifecycleState.Interactive);
        Assert.True(
            FindNode(recovered.Snapshot.Root, "alpha-result").Text?.Contains(
                "run=", StringComparison.Ordinal) == true,
            $"The full-trust session did not recover a typed snapshot after admission outcome " +
            $"'{admissionOutcome}'.");
        Assert.True(session.GetState(installed.Id)?.Failure is null,
            $"A successful full-trust refresh did not clear the retained failure after admission " +
            $"outcome '{admissionOutcome}'.");
    }
    finally
    {
        await session.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }
}

static async Task<WidgetPresentationState> WaitForPresentationAsync(
    WidgetPresentationSession session,
    Func<WidgetPresentationState, bool> predicate)
{
    if (session.GetState("test-widget") is { } current && predicate(current)) return current;
    var completion = new TaskCompletionSource<WidgetPresentationState>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    void Changed(object? sender, WidgetPresentationChangedEventArgs eventArgs)
    {
        if (predicate(eventArgs.State)) completion.TrySetResult(eventArgs.State);
    }
    session.PresentationChanged += Changed;
    try
    {
        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    finally
    {
        session.PresentationChanged -= Changed;
    }
}

static string CreateFullTrustSessionPackage(
    string destination,
    string fixtureOutput,
    string executable,
    string id)
{
    if (!Directory.Exists(fixtureOutput))
        throw new InvalidOperationException("The full-trust fixture was not built.");
    var package = Path.Combine(destination, $"{id}.wrwidget");
    var manifest = new WidgetManifest
    {
        Id = id,
        Publisher = "dev.avp004",
        Name = "AVP-004 full-trust session fixture",
        Version = "1.0.0",
        HostApi = new HostApiRange("1.0", 1),
        Entrypoint = new WidgetEntrypoint(
            WidgetEntrypointRuntimes.FullTrustApplicationV1,
            Executable: $"payload/{executable}"),
        Permissions = [],
        OptionalPermissions = [],
        Architectures = ["x64"],
    };
    using var stream = new FileStream(
        package, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
    WriteArchiveEntry(archive, "manifest.json", ManifestJson.Serialize(manifest));
    foreach (var file in Directory.EnumerateFiles(fixtureOutput)
                 .Where(path => !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                 .Order(StringComparer.Ordinal))
        WriteArchiveEntry(
            archive,
            "payload/" + Path.GetFileName(file),
            File.ReadAllBytes(file));
    return package;
}

static string FindRepositoryRoot()
{
    for (var current = new DirectoryInfo(AppContext.BaseDirectory);
         current is not null;
         current = current.Parent)
        if (Directory.Exists(Path.Combine(current.FullName, "src", "WidgetBridge")))
            return current.FullName;
    throw new InvalidOperationException("Repository root was not found.");
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

static async Task VirtualCollectionWindowCrossesBridge()
{
    await using var harness = await BridgeHarness.StartBudgetAsync(
        new WorkerResidencyBudgetOptions { MaximumApplicationWorkers = 1 },
        new TemporaryWidgetDefinition(
            "virtual", "dev.test.virtual", "dev.test", "virtual.instance"));
    var widgets = await harness.Client.RequestAsync(
        BridgeMessageTypes.ListWidgets, new { });
    Assert.Equal(BridgeMessageTypes.Widgets, widgets.Type);
    var descriptor = widgets.Payload.GetProperty("widgets")
        .EnumerateArray()
        .Single(candidate => candidate.GetProperty("id").GetString() == "virtual");
    var presentationGeneration = descriptor
        .GetProperty("presentationGeneration").GetString()
        ?? throw new InvalidOperationException(
            "Virtual widget omitted its presentation generation.");
    var lifecycle = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("virtual", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);
    var initialInvalidation = await harness.Client.ReadEventAsync(
        BridgeMessageTypes.Invalidation);
    Assert.Equal("virtual",
        initialInvalidation.Payload.GetProperty("widgetId").GetString());
    var lastRevision = initialInvalidation.Payload.GetProperty("revision").GetInt64();
    Assert.True(lastRevision > 0,
        "Initial virtual invalidation did not carry a positive revision.");

    var firstResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("virtual"));
    Assert.Equal(BridgeMessageTypes.Snapshot, firstResponse.Type);
    var first = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        firstResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(ProtocolConstants.VirtualCollectionWindowVersion, first.ProtocolVersion);
    Assert.Equal(32, first.Root.Children.Count);
    Assert.Equal(10_000L, first.Root.VirtualCollectionWindow?.TotalItemCount);
    Assert.Equal(0L, first.Root.VirtualCollectionWindow?.FirstItemIndex);
    Assert.True(first.Root.VirtualCollectionWindow is
    {
        RequestGeneration: 1,
        HasBefore: false,
        HasAfter: true,
        Change: VirtualCollectionWindowChange.Replace,
    },
        "Initial virtual bridge window lost its exact boundary authority.");
    Assert.SequenceEqual(
        Enumerable.Range(0, 32).Select(index => $"virtual.item.{index}"),
        first.Root.Children.Select(child => child.Id));

    var pageAction = first.Root.ScrollNearEndActionId ??
        throw new InvalidOperationException("Virtual bridge window omitted its next-page action.");
    var admitted = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest(
            "virtual", new WidgetActionEvent(pageAction, first.Root.Id)));
    Assert.Equal(BridgeMessageTypes.Acknowledged, admitted.Type);
    Assert.Equal("enqueued", admitted.Payload.GetProperty("admission").GetString());
    var materialized = first;
    ViewSnapshot? durable = null;
    for (var publication = 0; publication < 8; publication++)
    {
        var notification = await harness.Client.ReadNextEventAsync();
        if (notification.Type == BridgeMessageTypes.Failure)
        {
            Assert.Equal("virtual",
                notification.Payload.GetProperty("widgetId").GetString());
            Assert.Equal("controllerActionFailed",
                notification.Payload.GetProperty("reason").GetString());
            Assert.Equal(pageAction,
                notification.Payload.GetProperty("actionId").GetString());
            Assert.Equal(first.Root.Id,
                notification.Payload.GetProperty("sourceElementId").GetString());
            throw new InvalidOperationException(
                $"Virtual pagination action '{pageAction}' failed from " +
                $"'{first.Root.Id}': " +
                notification.Payload.GetProperty("message").GetString());
        }
        Assert.Equal(BridgeMessageTypes.Invalidation, notification.Type);
        Assert.Equal("virtual",
            notification.Payload.GetProperty("widgetId").GetString());
        var revision = notification.Payload.GetProperty("revision").GetInt64();
        Assert.True(revision > lastRevision,
            "Virtual bridge invalidation did not advance its current revision.");
        lastRevision = revision;
        var candidateResponse = await harness.Client.RequestAsync(
            BridgeMessageTypes.GetSnapshot,
            new BridgePresentationRequest(
                "virtual", PresentationUpdateCapabilities.Current, materialized.Sequence,
                WidgetPresentationTransactionKind.IncrementalUpdate));
        Assert.Equal(BridgeMessageTypes.PresentationUpdate, candidateResponse.Type);
        var update = PresentationUpdateJson.Deserialize(
            System.Text.Encoding.UTF8.GetBytes(
                candidateResponse.Payload.GetProperty("update").GetRawText()));
        Assert.Equal(materialized.Sequence, update.BaseSequence);
        Assert.Equal(presentationGeneration, update.PresentationGeneration);
        materialized = PresentationUpdateMaterializer.Apply(
            materialized, update, presentationGeneration);
        if (materialized.Root.Children.Count == 64 &&
            materialized.Root.VirtualCollectionWindow is
            {
                RequestGeneration: 2,
                FirstItemIndex: 0,
                TotalItemCount: 10_000,
                HasBefore: false,
                HasAfter: true,
                Change: VirtualCollectionWindowChange.Append,
            })
        {
            durable = materialized;
            break;
        }
    }
    var second = durable ?? throw new InvalidOperationException(
        "Virtual bridge window did not publish its durable appended state.");
    Assert.Equal(64, second.Root.Children.Count);
    Assert.Equal(2L, second.Root.VirtualCollectionWindow?.RequestGeneration);
    Assert.Equal(10_000L, second.Root.VirtualCollectionWindow?.TotalItemCount);
    Assert.Equal(0L, second.Root.VirtualCollectionWindow?.FirstItemIndex);
    Assert.Equal(VirtualCollectionWindowChange.Append,
        second.Root.VirtualCollectionWindow?.Change);
    Assert.True(second.Root.VirtualCollectionWindow is { HasBefore: false, HasAfter: true },
        "Appended virtual bridge window lost its exact boundary authority.");
    Assert.SequenceEqual(
        Enumerable.Range(0, 64).Select(index => $"virtual.item.{index}"),
        second.Root.Children.Select(child => child.Id));
    Assert.True(second.Root.Children.Count <= 96,
        "Bridge publication materialized the private 10,000-item collection.");

    var checkpointResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("virtual"));
    Assert.Equal(BridgeMessageTypes.Snapshot, checkpointResponse.Type);
    var checkpoint = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        checkpointResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.SequenceEqual(
        Enumerable.Range(0, 64).Select(index => $"virtual.item.{index}"),
        checkpoint.Root.Children.Select(child => child.Id));
    Assert.True(checkpoint.Root.VirtualCollectionWindow is
    {
        RequestGeneration: 2,
        FirstItemIndex: 0,
        TotalItemCount: 10_000,
        HasBefore: false,
        HasAfter: true,
        Change: VirtualCollectionWindowChange.Replace,
    }, "Base-zero Bridge checkpoint did not normalize the durable window to Replace.");
    Assert.True(checkpoint.Root.Children.Count <= 96,
        "Base-zero Bridge checkpoint exceeded the bounded host window.");
}

static async Task AdmittedRegistryInvalidationReachesClientEventQueue()
{
    await using var harness = await BridgeHarness.StartAsync();
    var lifecycle = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest(
            "test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);

    var admission = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest(
            "test-widget", new WidgetActionEvent("refresh", "button")));
    Assert.Equal(BridgeMessageTypes.Acknowledged, admission.Type);

    var invalidation = await harness.Client.ReadEventAsync(
        BridgeMessageTypes.Invalidation);
    Assert.Equal("test-widget",
        invalidation.Payload.GetProperty("widgetId").GetString());
    Assert.Equal(1L, invalidation.Payload.GetProperty("revision").GetInt64());
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

static Task TextEntryRenderRole()
{
    var snapshot = new WidgetView(
        UI.TextEntry("", "Search games", "game-launcher.search.commit", "game-launcher.search", 96),
        InitialFocusId: "game-launcher.search")
        .CreateSnapshot("bridge.text-entry", 1);

    var unthemed = BridgeRenderStyleResolver.Resolve(snapshot, theme: null);
    Assert.True(unthemed.ContainsKey("game-launcher.search"),
        "TextEntry node ID was omitted from the unthemed bridge style map.");
    Assert.Equal(0, unthemed["game-launcher.search"].Base.Count);

    var parsed = WrssParser.Parse("textEntry { color: #2468ac; }", "text-entry.wrss");
    Assert.Equal(0, parsed.Diagnostics.Count(diagnostic =>
        diagnostic.Severity == WrssDiagnosticSeverity.Error));
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, "TextEntry WRSS fixture did not compile.");
    var themed = BridgeRenderStyleResolver.Resolve(snapshot, compiled.Theme);
    Assert.Equal("#2468ac", themed["game-launcher.search"].Base["color"].Text);

    var unknown = snapshot with
    {
        Root = snapshot.Root with { Kind = (ViewNodeKind)999 },
    };
    var exception = Assert.Throws<BridgeProtocolException>(() =>
        BridgeRenderStyleResolver.Resolve(unknown, compiled.Theme));
    Assert.Equal("Unsupported view node kind '999'.", exception.Message);
    Assert.Equal(ProtocolConstants.TextEntryVersion, snapshot.ProtocolVersion);
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

static async Task OversizedPresentationPreservesNeighbor()
{
    await using var harness = await BridgeHarness.StartBudgetAsync(
        new WorkerResidencyBudgetOptions { MaximumApplicationWorkers = 2 },
        new TemporaryWidgetDefinition(
            "oversized", "dev.test.oversized", "dev.test", "oversized.instance"),
        new TemporaryWidgetDefinition(
            "neighbor", "dev.test.neighbor", "dev.test", "neighbor.instance"));

    foreach (var id in new[] { "oversized", "neighbor" })
    {
        var lifecycle = await harness.Client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(id, WidgetLifecycleState.Visible));
        Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);
        var initial = await harness.Client.RequestAsync(
            BridgeMessageTypes.GetSnapshot, new WidgetIdRequest(id));
        Assert.Equal(BridgeMessageTypes.Snapshot, initial.Type);
    }

    var admission = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest(
            "oversized", new WidgetActionEvent("oversize", "button")));
    Assert.Equal(BridgeMessageTypes.Acknowledged, admission.Type);
    _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    var rejected = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("oversized"));
    Assert.Equal(BridgeMessageTypes.Error, rejected.Type);
    Assert.True(!string.IsNullOrWhiteSpace(
        rejected.Payload.GetProperty("message").GetString()),
        "Oversized presentation rejection omitted its bounded diagnostic.");

    var neighbor = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("neighbor"));
    Assert.Equal(BridgeMessageTypes.Snapshot, neighbor.Type);
    Assert.Equal("neighbor.instance",
        neighbor.Payload.GetProperty("snapshot").GetProperty("widgetInstanceId").GetString());
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
        },
        new TemporaryWidgetDefinition("worker-0", "dev.test.worker0", "dev.test", "worker.0", 64),
        new TemporaryWidgetDefinition("worker-1", "dev.test.worker1", "dev.test", "worker.1", 64));

    var first = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("worker-0", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, first.Type);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    Assert.Equal(1, harness.Server.ResidencyBudget.ApplicationWorkers);
    Assert.Equal(64L, harness.Server.ResidencyBudget.ApplicationAdvisoryMemoryMb);

    var refused = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("worker-1", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Error, refused.Type);
    Assert.Equal("request_failed",
        refused.Payload.GetProperty("code").GetString());
    Assert.Equal("Widget 'worker-1' runtime request failed (worker-admission-failed).",
        refused.Payload.GetProperty("message").GetString());
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

static async Task WorkerResidencyMemoryIsAdvisory()
{
    await using var harness = await BridgeHarness.StartBudgetAsync(
        new WorkerResidencyBudgetOptions
        {
            MaximumApplicationWorkers = 3,
        },
        new TemporaryWidgetDefinition("worker-64", "dev.test.worker64", "dev.test", "worker.64", 64),
        new TemporaryWidgetDefinition("worker-1024", "dev.test.worker1024", "dev.test", "worker.1024", 1_024),
        new TemporaryWidgetDefinition("settings", "widgetrail.firstparty.settings",
            "widgetrail.firstparty", "settings.instance", 64));

    var first = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("worker-64", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, first.Type);

    var second = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("worker-1024", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, second.Type);

    var settings = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("settings", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, settings.Type);
    var budget = harness.Server.ResidencyBudget;
    Assert.Equal(2, budget.ApplicationWorkers);
    Assert.Equal(1_088L, budget.ApplicationAdvisoryMemoryMb);
    Assert.Equal(1, budget.ControlPlaneWorkers);
    Assert.Equal(64L, budget.ControlPlaneAdvisoryMemoryMb);
    Assert.Equal(3, budget.TotalWorkers);
    Assert.Equal(1_152L, budget.TotalAdvisoryMemoryMb);
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
    private string _committedTextStatus = "committed:none";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, string>
        _inputOrigins = new();
    private readonly bool _artworkFixture;
    private readonly bool _oversizedFixture;
    private bool _oversized;
    private WidgetAppLibraryItem? _artworkItem;

    internal BridgeTestWidget(string instanceId)
    {
        _artworkFixture = string.Equals(
            instanceId, "artwork.instance", StringComparison.Ordinal);
        _oversizedFixture = string.Equals(
            instanceId, "oversized.instance", StringComparison.Ordinal);
    }

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
            UI.Text(_committedTextStatus, "committed-text-status"),
            UI.Slider(_volume, 0, 1, 0.1, "volume.changed", "volume",
                "Volume", $"{_volume:P0}"),
        };
        if (_artworkFixture)
        {
            children.Add(UI.Button("Load artwork", "artwork.load", "artwork.load"));
            children.Add(UI.Button("Launch app", "artwork.launch", "artwork.launch"));
            children.Add(_artworkItem?.Presentation.Artwork.Find(
                    WidgetAppLibraryArtworkRole.Tile)?.Handle is { } handle
                ? UI.Artwork(
                    new WidgetArtworkHandle(handle), "artwork.image", "Application icon",
                    ImageFit.Contain)
                : UI.Icon(WidgetGlyph.Play, "artwork.image", "Application icon fallback"));
        }
        if (_oversizedFixture && _oversized)
        {
            var maximumText = new string('X', ProtocolConstants.MaximumStringLength);
            for (var index = 0; index < 300; index++)
                children.Add(UI.Text(maximumText, $"oversized.presentation.{index}"));
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
            var gamePage = await HostServices.AppLibrary.QueryAsync(
                new WidgetAppLibraryQuery(Kind: WidgetAppLibraryKind.Game),
                limit: 64, refresh: true,
                cancellationToken: stateLifetime);
            var game = gamePage.Items.Single();
            if (game.Presentation.Source.SourceId != "source-steam" ||
                game.Presentation.Source.DisplayName != "Steam" ||
                game.Presentation.Kind != WidgetAppLibraryKind.Game ||
                game.Presentation.Artwork.Items.Count != 0)
                throw new InvalidOperationException(
                    "Normalized multi-source no-artwork game projection was not preserved.");

            var query = new WidgetAppLibraryQuery(Kind: WidgetAppLibraryKind.Application);
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
            var presentation = _artworkItem.Presentation;
            if (_artworkItem.PresentationVersion !=
                    WidgetAppLibraryItem.CurrentPresentationVersion ||
                presentation.Source.DisplayName != "Windows" ||
                presentation.Availability.State != WidgetAppLibraryAvailabilityState.Installed ||
                !presentation.Availability.IsLaunchable ||
                !presentation.Capabilities.Supports(WidgetAppLibraryAction.Launch) ||
                presentation.Metadata is not null ||
                presentation.ActiveOperation is not null ||
                presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile) is null)
                throw new InvalidOperationException(
                    "Artwork fixture did not receive the normalized launchable presentation.");
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
        else if (action.ActionId == "committed-text")
        {
            _committedTextStatus = action.CommittedText is { } committed
                ? $"committed:{committed.Length}"
                : "committed:missing";
            Invalidate();
        }
        else if (action.ActionId == "crash")
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            Environment.Exit(31);
        }
        else if (action.ActionId == "hang")
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        else if (action.ActionId == "oversize" && _oversizedFixture)
        {
            _oversized = true;
            Invalidate();
        }
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

file sealed record VirtualBridgeItem(int Index);

file sealed class VirtualCollectionBridgeWidget : Widget
{
    private const int TotalItems = 10_000;
    private readonly WidgetCursorResource<VirtualBridgeItem> _items;

    internal VirtualCollectionBridgeWidget()
    {
        _items = CreateCursorResource<VirtualBridgeItem>("virtual.items", new()
        {
            PageSize = 32,
            MaximumRetainedItems = 96,
            PaginationThreshold = 2,
            LoadPage = (cursor, _, limit, _) =>
            {
                var start = cursor is null
                    ? 0
                    : int.Parse(cursor.Value.Value.AsSpan(1));
                var count = Math.Min(limit, TotalItems - start);
                var page = new WidgetCursorPage<VirtualBridgeItem>(
                    Enumerable.Range(start, count).Select(index =>
                        new VirtualBridgeItem(index)).ToArray(),
                    start > 0
                        ? new WidgetCollectionCursor($"p{Math.Max(0, start - limit)}")
                        : null,
                    start + count < TotalItems
                        ? new WidgetCollectionCursor($"p{start + count}")
                        : null)
                {
                    FirstItemIndex = start,
                    TotalItemCount = TotalItems,
                };
                return ValueTask.FromResult(page);
            },
            MapError = _ => new WidgetResourceError(
                "virtual_page_failed", "The virtual page could not be loaded."),
            Viewports =
            [
                new WidgetCursorViewport<VirtualBridgeItem>(
                    "virtual.scroll",
                    item => new WidgetCollectionItemKey($"item.{item.Index}"),
                    item => $"virtual.item.{item.Index}")
                {
                    EstimatedItemExtent = 52,
                },
            ],
        });
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        _items.EnsureLoaded();
        return ValueTask.CompletedTask;
    }

    public override WidgetView Render()
    {
        var snapshot = _items.Snapshot;
        var rows = snapshot.Items.Select(item => _items.PresentItem(item,
            UI.Button($"Item {item.Index}", "virtual.open", $"virtual.item.{item.Index}")))
            .ToArray();
        var scroll = _items.Present(UI.VerticalScroll("virtual.scroll", rows));
        return new WidgetView(
            scroll,
            snapshot.RequestedFocusId ?? rows.FirstOrDefault()?.Id,
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard,
                PreferredWidth = 640,
                PreferredHeight = 520,
            });
    }

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_items.TryHandlePagination(action, out var operation))
            return;
        var result = await operation.Completion.ConfigureAwait(false);
        if (result.Status != WidgetOperationStatus.Succeeded)
            throw new InvalidOperationException(
                $"Virtual pagination completed with '{result.Status}'.",
                result.Exception);
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
    string StyleFile = "styles/default.wrss");

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
        string styleFile = "styles/default.wrss",
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
            System.IO.Path.GetTempPath(), $"wrail-bridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "widgets.json");
        var stylesDirectory = System.IO.Path.Combine(directory, "styles");
        Directory.CreateDirectory(stylesDirectory);
        File.WriteAllText(System.IO.Path.Combine(stylesDirectory, "default.wrss"), invalidStyle
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
            var pipeName = $"wrail-bridge-test-{Guid.NewGuid():N}";
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
            var pipeName = $"wrail-bridge-budget-{Guid.NewGuid():N}";
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
        var directory = Path.Combine(Path.GetTempPath(), $"wrail-bridge-appearance-{Guid.NewGuid():N}");
        var paths = new PlatformSettingsPaths(directory);
        var themeDirectory = Path.Combine(paths.ThemesDirectory, "dev.example.bridge", "1.0.0");
        Directory.CreateDirectory(themeDirectory);
        var themeFile = Path.Combine(themeDirectory, "theme.wrss");
        await File.WriteAllTextAsync(Path.Combine(themeDirectory, "theme.json"),
            "{\"schemaVersion\":1,\"id\":\"dev.example.bridge\",\"name\":\"Bridge Test\",\"version\":\"1.0.0\",\"entryFile\":\"theme.wrss\"}");
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
                AnimateWidgetSwitching = true,
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

file sealed class NoopDisposable : IDisposable
{
    internal static NoopDisposable Instance { get; } = new();
    public void Dispose() { }
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

file sealed class ProtectedWifiNetworkBackend :
    INetworkPlatformBrokerBackend,
    IProtectedWifiHostBackend
{
    private readonly SimulatedPlatformBrokerBackend _inner = new();
    public int? SecretSentinel { get; private set; }
    public char[]? ObservedSecretOwner { get; private set; }
    public string? NetworkId { get; private set; }

    public event EventHandler<BrokerPlatformEvent>? EventPublished
    {
        add => _inner.EventPublished += value;
        remove => _inner.EventPublished -= value;
    }

    public Task<ProtectedWifiConnectionResult> ConnectProtectedWifiAsync(
        string networkId,
        char[] secret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NetworkId = networkId;
        SecretSentinel = ComputeSentinel(secret);
        ObservedSecretOwner = secret;
        return Task.FromResult(new ProtectedWifiConnectionResult(
            ProtectedWifiConnectionStatus.Connecting, "connecting"));
    }

    public Task<NetworkStatusSummary> GetNetworkStatusAsync(CancellationToken cancellationToken) =>
        _inner.GetNetworkStatusAsync(cancellationToken);
    public Task<IReadOnlyList<SavedNetworkProfileSummary>> GetSavedNetworkProfilesAsync(
        CancellationToken cancellationToken) =>
        _inner.GetSavedNetworkProfilesAsync(cancellationToken);
    public Task SwitchSavedNetworkProfileAsync(
        string profileId,
        CancellationToken cancellationToken) =>
        _inner.SwitchSavedNetworkProfileAsync(profileId, cancellationToken);
    public Task<AvailableWifiNetworksSummary> GetAvailableWifiNetworksAsync(
        CancellationToken cancellationToken) =>
        _inner.GetAvailableWifiNetworksAsync(cancellationToken);
    public Task RequestWifiScanAsync(CancellationToken cancellationToken) =>
        _inner.RequestWifiScanAsync(cancellationToken);
    public Task ConnectAvailableWifiNetworkAsync(
        string networkId,
        CancellationToken cancellationToken) =>
        _inner.ConnectAvailableWifiNetworkAsync(networkId, cancellationToken);
    public Task<WifiRadioSummary> GetWifiRadioAsync(CancellationToken cancellationToken) =>
        _inner.GetWifiRadioAsync(cancellationToken);
    public Task SetWifiRadioAsync(bool enabled, CancellationToken cancellationToken) =>
        _inner.SetWifiRadioAsync(enabled, cancellationToken);

    private static int ComputeSentinel(ReadOnlySpan<char> secret)
    {
        var value = unchecked((int)2166136261);
        foreach (var character in secret)
            value = unchecked((value ^ character) * 16777619);
        return value;
    }
}

file sealed class FailingProtectedWifiSecretStream(
    int length,
    CancellationTokenSource? cancellation = null) : Stream
{
    private int _read;
    public byte[]? CapturedBody { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _read) == 1)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Span, length);
            return ValueTask.FromResult(sizeof(int));
        }
        Assert.True(
            MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment),
            "Secret read did not expose its owned array to the failure fixture.");
        CapturedBody = segment.Array;
        buffer.Span[..Math.Min(3, buffer.Length)].Fill(0x5A);
        if (cancellation is not null)
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<int>(cancellation.Token);
        }
        return ValueTask.FromException<int>(new IOException("controlled secret read failure"));
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
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

    public async Task<BridgeEnvelope> RequestProtectedWifiAsync(
        BridgeProtectedWifiRequest request,
        char[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var requestId = Interlocked.Increment(ref _requestId);
        var encoded = new byte[secret.Length];
        try
        {
            for (var index = 0; index < secret.Length; index++)
                encoded[index] = checked((byte)secret[index]);
            await _channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.ConnectProtectedWifi,
                RequestId = requestId,
                Payload = BridgeJson.ToElement(request),
            }, CancellationToken.None);
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, encoded.Length);
            try
            {
                await _pipe.WriteAsync(header);
                await _pipe.WriteAsync(encoded);
                await _pipe.FlushAsync();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(header);
            }
            while (true)
            {
                var response = await _reader.ReadAsync(ReadDeadline);
                if (response.RequestId == 0)
                {
                    _events.Enqueue(response);
                    continue;
                }
                Assert.Equal(requestId, response.RequestId);
                return response;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
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

    public async Task<BridgeEnvelope> ReadNextEventAsync()
    {
        if (_events.Count != 0) return _events.Dequeue();
        var message = await _reader.ReadAsync(ReadDeadline);
        if (message.RequestId != 0)
            throw new InvalidOperationException("Expected an event, received a response.");
        return message;
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
