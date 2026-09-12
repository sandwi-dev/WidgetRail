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
using WidgetRail.Samples.SdkGalleryWidget;

if (args.Contains("--widget-pipe", StringComparer.Ordinal))
    return await RunWorkerAsync(args);

var tests = new (string Name, Func<Task> Run)[]
{
    ("Settings controls retain Settings and consume application actions once", SettingsControlsScenarios.QueueAndCatalog),
    ("Bridge framing rejects oversized messages", OversizedFrameIsRejected),
    ("Protected Wi-Fi secret frames zero every mutable managed owner", ProtectedWifiSecretFramesAreZeroed),
    ("Event cancellation preserves the serialized frame boundary", BridgeEventWriteBoundaryScenarios.CancellationPreservesFrameBoundary),
    ("Bridge-wide revision notifications are latest-wins and bounded", BridgeRevisionNotificationScenarios.LatestRevisionsAreBoundedAndOrdered),
    ("Bridge read and reply timeouts have exact frame owners", BridgeFrameOwnershipScenarios.TimeoutAndCancellationHaveExactOwners),
    ("Typed artwork notifications preserve bounded wire content", BridgeFrameOwnershipScenarios.TypedArtworkNotificationsPreserveWireContent),
    ("Strict catalog rejects unknown properties", StrictCatalogRejectsUnknownProperties),
    ("Catalog rejects host-reserved View quick actions", CatalogRejectsHostReservedView),
    ("Bridge startup scopes an explicit development installed catalog", DevelopmentCatalogRootIsScoped),
    ("Settings reviews the same catalog selected by the bridge", SettingsUsesSelectedCatalog),
    ("Catalog treats worker memory guidance as optional advisory metadata", CatalogMemoryGuidanceIsAdvisory),
    ("Worker residency count is user-selected and optional", WorkerResidencyBudgetOptionsAreUserSelected),
    ("Worker residency budget admission is race safe", WorkerResidencyBudgetAdmissionIsRaceSafe),
    ("Permitted eighth worker pre-start timeout releases its exact slot", PermittedEighthWorkerPreStartTimeoutReleasesSlot),
    ("Catalog widget icons use the closed WidgetGlyph set with a safe fallback", CatalogGlyphIsClosed),
    ("Catalog rejects invalid WRSS with safe diagnostics", InvalidThemeIsRejected),
    ("Bundled widget failures are isolated from Bridge startup and recover", BundledWidgetFailuresAreIsolated),
    ("Catalog rejects style paths outside package root", UnsafeStylePathIsRejected),
    ("Catalog enumeration does not launch workers", EnumerationIsLazy),
    ("Package SVG icons resolve lazily with exact catalog authority and no worker",
        PackageSvgIconsResolveLazily),
    ("Request dispatcher cleans success failure and cancellation", RequestDispatcherCleansTerminalPaths),
    ("Request classification is closed typed and fail-closed", RequestClassificationIsClosed),
    ("Protected Wi-Fi host admission is exact trusted and bounded", ProtectedWifiHostAdmissionIsExact),
    ("Protected Wi-Fi production dispatch clears one exact secret owner", ProtectedWifiProductionDispatchIsZeroed),
    ("Trusted artwork demand is exact current and lazy through the production bridge", TrustedArtworkDemandIsExact),
    ("Test-local BackgroundSurface artwork crosses the worker and Bridge boundary", BackgroundSurfaceArtworkCrossesBridge),
    ("SDK Gallery sealed backgrounds cross the worker and Bridge boundary", SdkGalleryBackgroundArtworkCrossesBridge),
    ("Two provider-neutral media adapters resolve through one sealed contract", EmbeddedMediaAssetsAreProviderNeutral),
    ("Built embedded media sample completes the typed playback loop", BuiltEmbeddedMediaSampleCompletesPlaybackLoop),
    ("Request dispatcher preserves FIFO and predecessor failure", RequestDispatcherOwnsWidgetOrdering),
    ("Request dispatcher rejects duplicates and global over-capacity", RequestDispatcherBoundsAdmission),
    ("Request dispatcher deadline quarantines cancellation-ignoring work", RequestDispatcherForcedDrainIsComplete),
    ("Stalled widget admission leaves bounded list and stop control responsive", StalledAdmissionKeepsControlPlaneResponsive),
    ("Pipelined requests preserve per-widget receive order", PipelinedWidgetRequestsStayOrdered),
    ("Duplicate pending request IDs fail the bridge session closed", DuplicatePendingRequestIdsFailClosed),
    ("Enabled installed widgets join the bridge catalog without eager launch", InstalledWidgetsJoinCatalog),
    ("Two unrelated full-trust applications use one ordinary runtime", FullTrustCommunityScenarios.TwoApplicationsUseTheOrdinaryRuntime),
    ("Packaged Spotify uses the ordinary full-trust runtime", FullTrustCommunityScenarios.SpotifyUsesTheOrdinaryRuntime),
    ("Packaged Playnite Library uses the ordinary full-trust runtime", FullTrustCommunityScenarios.PlayniteLibraryUsesTheOrdinaryRuntime),
    ("Full-trust missing entrypoints and silent promotion fail closed", FullTrustCommunityScenarios.MissingEntrypointAndManifestPromotionFailClosed),
    ("Installed content generations receive distinct isolation identities", InstalledContentGenerationIsIsolated),
    ("Installed launch admission rejects content changed after catalog publication", InstalledLaunchAdmissionRejectsRace),
    ("Installed widget residency policies reach the generic supervisor", InstalledResidencyPolicyIsCarried),
    ("Known installed capabilities load lazily and unknown capabilities fail closed", InstalledCapabilityDeclarationsAreClosed),
    ("Bridge alone synthesizes private state authority for capability-free workers", PrivateStateAuthorityIsHostSynthesized),
    ("Action-bound app launch closes only after exact successful terminal", ActionBoundLaunchCloseIsTerminalExact),
    ("Installed worker local data clears after exact retirement and preserves its neighbor", InstalledWorkerLocalDataClearIsExact),
    ("Disabled package uninstall is exact revisioned and preserves private data", InstalledPackageUninstallIsExact),
    ("Tampered installed catalogs fail soft to trusted widgets", TamperedInstalledCatalogFailsSoft),
    ("Invalid installed styles fail soft to trusted widgets", InvalidInstalledStyleFailsSoft),
    ("Catalog monitor retains invalid trusted state and fails closed on installed state", CatalogMonitorIsRevisionedAndLastGood),
    ("Cold installed catalog loading does not block trusted bridge readiness", ColdInstalledCatalogDoesNotBlockBridgeReadiness),
    ("Catalog reload authority is single-flight latest-wins and cancellable", CatalogReloadAuthorityIsLatestWinsAndCancellable),
    ("Live installed bytes are pinned and post-release tamper cannot relaunch", InstalledPackageTamperRetiresLiveWorker),
    ("Catalog monitor closes the startup notification window", CatalogMonitorStartupCatchUp),
    ("Catalog reconciliation preserves compatible workers and retires changed workers", CatalogReconciliationPreservesCompatibleWorkers),
    ("Client registry owns compatible replacement removal and stale generations", BridgeClientRegistryScenarios.CatalogReplacementAndRemovalOwnGenerations),
    ("Client registry isolates typed widget runtime failures", BridgeClientRegistryScenarios.WidgetRuntimeFailuresAreTypedAndRegistrationLocal),
    ("Client registry owns idle unload cancellation and replacement drain", BridgeClientRegistryScenarios.IdleUnloadCancellationAndReplacementAreOwned),
    ("Fresh generic workers require typed recovery from retained presentation bases", BridgeClientRegistryScenarios.FreshGenericWorkersRequireTypedRecoveryFromRetainedBases),
    ("Client registry restart restores lifecycle and resets generation", BridgeClientRegistryScenarios.RestartRestoresLifecycleAndResetsGeneration),
    ("Client registry restart reserves one generation and cleans failed restore", BridgeClientRegistryScenarios.RestartReservationAndRestoreFailureAreClosed),
    ("Client registry replacement retires before exact host mutation", BridgeClientRegistryScenarios.ManagedReplacementRetiresBeforeMutation),
    ("Trusted local-data management clears one exact retired generation", BridgeClientRegistryScenarios.LocalDataManagementIsExactAndDocumentBlind),
    ("Catalog retirement cancels a real running-registration request lease",
        BridgeClientRegistryScenarios.CatalogRetirementCancelsRegistrationLease),
    ("Client registry commits lifecycle and first snapshot as one generation", BridgeClientRegistryScenarios.LifecycleAndFirstSnapshotAreAtomic),
    ("Client registry retains activation updates until first presentation commits", BridgeClientRegistryScenarios.ActivationInvalidationsSurviveFirstPresentation),
    ("Client registry retains activation updates until lifecycle commits", BridgeClientRegistryScenarios.ActivationInvalidationsSurviveLifecycleCommit),
    ("Client registry publication admission serializes replacement", BridgeClientRegistryScenarios.PublicationAdmissionSerializesReplacement),
    ("Client registry notification lane bounds and balances admission", BridgeClientRegistryScenarios.NotificationLaneBoundsAndBalancesAdmission),
    ("Client registry notification burst cancels and drains on replacement", BridgeClientRegistryScenarios.NotificationBurstIsBoundedAndRetires),
    ("Client registry cancelled restart transfers exact retirement", BridgeClientRegistryScenarios.CancelledRestartTransfersRetirement),
    ("Client registry starts external retirement outside its identity gate", BridgeClientRegistryScenarios.ExternalRetirementStartsOutsideIdentityGate),
    ("Worker retirement completion includes residency release", BridgeClientRegistryScenarios.RetirementCompletionIncludesResidencyRelease),
    ("Client registry releases refused and failed-start residency", BridgeClientRegistryScenarios.BudgetRefusalAndFailedStartReleaseReservations),
    ("Client registry terminal disposal serializes with operations", BridgeClientRegistryScenarios.TerminalDisposalSerializesWithConcurrentOperation),
    ("Client registry observes retirement failures and disposes every client", BridgeClientRegistryScenarios.RetirementFailuresAreObservedAndDrained),
    ("Visible registry publication reaches its configured publisher once", BridgeClientRegistryScenarios.VisibleRegistrationPublishesInvalidationExactlyOnce),
    ("Pinned layout selection is exact current runtime generation", BridgeClientRegistryScenarios.PinnedLayoutSelectionIsGenerationBound),
    ("Full widget pinning requires explicit manifest authority", BridgeClientRegistryScenarios.FullWidgetPinningRequiresManifestAuthority),
    ("Pinned surface input requires exact generation layout scope and focus", BridgeClientRegistryScenarios.PinnedSurfaceInputRequiresExactAuthority),
    ("Pinned shortcut availability belongs to its declaring owner", BridgeClientRegistryScenarios.PinnedShortcutAvailabilityBelongsToDeclaringOwner),
    ("Open widget input retains compatible committed authority across refresh", BridgeClientRegistryScenarios.OpenWidgetInputRetainsCompatibleCommittedAuthority),
    ("Input waits for publication and admits each compatible event once", BridgeClientRegistryScenarios.InputAdmissionSerializesWithSnapshotPublication),
    ("Embedded media resolution requires exact current publication authority", BridgeClientRegistryScenarios.EmbeddedMediaRequiresExactPublicationAuthority),
    ("Local package import origin is exact current Interactive Settings", BridgeClientRegistryScenarios.LocalPackageImportOriginIsExact),
    ("Local package import is disabled revisioned and path free", LocalPackageImportIsDisabledRevisionedAndPathFree),
    ("Local package import failures preserve catalog state", LocalPackageImportFailuresPreserveCatalog),
    ("Platform appearance is bounded and does not launch workers", PlatformAppearanceIsLazy),
    ("Private diagnostics attach only to the exact trusted Settings identity", DiagnosticsAreSettingsOnly),
    ("Media Sessions diagnostics are bounded transition-only and sanitized", MediaSessionDiagnosticsAreBounded),
    ("Shared overlay diagnostics rotate with bounded retained generations", SharedOverlayDiagnosticsRotate),
    ("Worker request diagnostics are bounded developer-only records", WorkerRequestDiagnosticsAreBounded),
    ("Current worker validator diagnostics persist and correlate across the Bridge", WorkerValidatorDiagnosticsPersistAndCorrelate),
    ("Controller settings gate persistence freshness and recovery", ControllerControlScenarios.GatingAndPersistence),
    ("Diagnostics projection is bounded sanitized and read only", BridgeDiagnosticsScenarios.ProjectionIsBoundedSanitizedAndReadOnly),
    ("Catalog diagnostics identify bounded isolated widget failures", BridgeDiagnosticsScenarios.CatalogRejectionsAreBoundedAndActionable),
    ("Diagnostics partial failures malformed input and deadline are closed", BridgeDiagnosticsScenarios.PartialFailureMalformedInputAndDeadlineAreClosed),
    ("Authority recovery projection is exact bounded and cancellation safe", BridgeDiagnosticsScenarios.RecoveryRetryIsExactBoundedAndCancellationSafe),
    ("Widget styles override global theme defaults", WidgetStylesOverrideGlobalThemeDefaults),
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
    ("Select option authority crosses the managed server wire exactly", SelectAuthorityCrossesServerWire),
    ("Exact-base divergence converges through one full checkpoint", ExactBaseDivergenceConvergesThroughCheckpoint),
    ("Committed text crosses bridge and worker action execution", CommittedTextCrossesBridgeAndWorker),
    ("Managed presentation session preserves sandboxed authority lifecycle and last-good state", ManagedPresentationSessionPreservesSandboxedAuthority),
    ("Managed presentation session preserves the ordinary full-trust runtime", ManagedPresentationSessionPreservesFullTrustRuntime),
    ("Protocol-v2 scroll nodes resolve bridge render roles", ScrollRenderRole),
    ("Protocol-v19 virtual collection window crosses worker and bridge", VirtualCollectionWindowCrossesBridge),
    ("Admitted registry invalidation reaches the client event queue", AdmittedRegistryInvalidationReachesClientEventQueue),
    ("Protocol-v37 poster and ordinary action surfaces resolve bridge render roles", ActionSurfaceRenderRole),
    ("Protocol-v15 text entries resolve one closed bridge render role", TextEntryRenderRole),
    ("Overflow wrapping crosses the generic Bridge render-style boundary", OverflowWrapRenderStyle),
    ("Protocol-v41 Select nodes resolve bridge render roles", SelectRenderRole),
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

if (args.Contains("--widge-193-only", StringComparer.Ordinal))
{
    var selected = new HashSet<string>(StringComparer.Ordinal)
    {
        "Installed worker local data clears after exact retirement and preserves its neighbor",
        "Disabled package uninstall is exact revisioned and preserves private data",
        "Trusted local-data management clears one exact retired generation",
        "Catalog retirement cancels a real running-registration request lease",
        "Action-bound app launch closes only after exact successful terminal",
    };
    tests = tests.Where(test => selected.Contains(test.Name)).ToArray();
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
            : string.Equals(instance, "background-surface-test.instance", StringComparison.Ordinal)
                ? new BackgroundSurfaceBridgeFixtureWidget()
                : string.Equals(instance, "sdk-gallery-background.instance", StringComparison.Ordinal)
                    ? new SdkGalleryWidget()
                : new BridgeTestWidget(instance));
}

static async Task BackgroundSurfaceArtworkCrossesBridge()
{
    await using var harness = await BridgeHarness.StartAsync(
        instanceId: "background-surface-test.instance");
    var lifecycle = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);

    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Snapshot, response.Type);
    var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        response.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(ViewNodeKind.BackgroundSurface, snapshot.Root.Kind);
    Assert.Equal(BackgroundSurfaceBridgeFixtureWidget.ArtworkHandle, snapshot.Root.ArtworkHandle);
    Assert.Equal(ImageFit.Cover, snapshot.Root.ImageFit);
    Assert.Equal(true, snapshot.Root.UsesFocusedDescendantArtwork);
    Assert.Equal(BackgroundSurfaceBridgeFixtureWidget.FirstFocusArtworkHandle,
        FindNode(snapshot.Root, "background-surface-test.first").FocusBackgroundArtworkHandle);
    Assert.Equal(BackgroundSurfaceBridgeFixtureWidget.SecondFocusArtworkHandle,
        FindNode(snapshot.Root, "background-surface-test.second").FocusBackgroundArtworkHandle);

    foreach (var handle in new[]
    {
        BackgroundSurfaceBridgeFixtureWidget.ArtworkHandle,
        BackgroundSurfaceBridgeFixtureWidget.FirstFocusArtworkHandle,
        BackgroundSurfaceBridgeFixtureWidget.SecondFocusArtworkHandle,
    })
    {
        var acknowledged = await harness.Client.RequestAsync(
            BridgeMessageTypes.ResolveArtwork,
            new BridgeArtworkRequest("test-widget", handle));
        Assert.Equal(BridgeMessageTypes.Acknowledged, acknowledged.Type);
        var artwork = await harness.Client.ReadEventAsync(BridgeMessageTypes.Artwork);
        Assert.Equal("test-widget", artwork.Payload.GetProperty("widgetId").GetString());
        Assert.Equal(handle, artwork.Payload.GetProperty("artworkHandle").GetString());
        Assert.Equal("image/png", artwork.Payload.GetProperty("contentType").GetString());
        var bytes = Convert.FromBase64String(
            artwork.Payload.GetProperty("contentBase64").GetString()!);
        Assert.SequenceEqual(BackgroundSurfaceBridgeFixtureWidget.ArtworkBytes.ToArray(), bytes);
    }
}

static async Task SdkGalleryBackgroundArtworkCrossesBridge()
{
    await using var harness = await BridgeHarness.StartAsync(
        instanceId: "sdk-gallery-background.instance",
        declaredPackageIconAssetIds: new HashSet<string>(
            ["gallery.mark"], StringComparer.Ordinal),
        maximumBytes: BridgeProtocol.DefaultMaximumMessageBytes);
    var lifecycle = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.True(lifecycle.Type == BridgeMessageTypes.Acknowledged,
        $"SDK Gallery lifecycle failed: {lifecycle.Payload.GetRawText()}");

    var initialResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var galleryErrorCode = initialResponse.Type == BridgeMessageTypes.Error &&
        initialResponse.Payload.TryGetProperty("code", out var galleryError)
            ? galleryError.GetString()
            : null;
    Assert.True(initialResponse.Type == BridgeMessageTypes.Snapshot,
        $"SDK Gallery initial snapshot failed with safe code '{galleryErrorCode ?? "none"}'.");
    var initial = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        initialResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(SdkGalleryWidget.DefaultBackgroundArtworkHandle, initial.Root.ArtworkHandle);
    Assert.Equal(ImageFit.Cover, initial.Root.ImageFit);
    Assert.Equal(true, initial.Root.UsesFocusedDescendantArtwork);
    await AssertGalleryArtworkAsync(
        SdkGalleryWidget.DefaultBackgroundArtworkHandle, 2_241_830,
        "DBEE1BA7FFF3ABC765F684D3AC666E34DA5CC68575C19DBAF9B64A4CA8405297");

    var backgroundsTab = FindNode(initial.Root, "gallery.shell.compact").Children.Single(node =>
        node.ActionId == "gallery.tab.backgrounds");
    var action = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest("test-widget", new WidgetActionEvent(
            "gallery.tab.backgrounds", backgroundsTab.Id)));
    Assert.Equal(BridgeMessageTypes.Acknowledged, action.Type);
    _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    var backgroundsResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var backgrounds = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        backgroundsResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(0, ViewSnapshotValidator.Validate(backgrounds).Count);
    Assert.Equal(ImageFit.Contain,
        FindNode(backgrounds.Root, "gallery.backgrounds.contain.surface").ImageFit);
    Assert.Equal(ImageFit.Fill,
        FindNode(backgrounds.Root, "gallery.backgrounds.fill.surface").ImageFit);
    await AssertGalleryArtworkAsync(
        SdkGalleryWidget.WarmBackgroundArtworkHandle, 2_295_973,
        "502D596BCBB590EAF24C7FC34ED81DE81B616E4F562F36118112E971BB38D247");
    await AssertGalleryArtworkAsync(
        SdkGalleryWidget.CoolBackgroundArtworkHandle, 2_417_019,
        "B317048E3A6A455350A1C3A19FDDFF13371CE8C6F112CDEA1B80BC2879341711");

    async Task AssertGalleryArtworkAsync(string handle, int expectedLength, string expectedHash)
    {
        var acknowledged = await harness.Client.RequestAsync(
            BridgeMessageTypes.ResolveArtwork,
            new BridgeArtworkRequest("test-widget", handle));
        Assert.Equal(BridgeMessageTypes.Acknowledged, acknowledged.Type);
        var artwork = await harness.Client.ReadEventAsync(BridgeMessageTypes.Artwork);
        Assert.Equal(handle, artwork.Payload.GetProperty("artworkHandle").GetString());
        Assert.Equal("image/png", artwork.Payload.GetProperty("contentType").GetString());
        var actual = Convert.FromBase64String(
            artwork.Payload.GetProperty("contentBase64").GetString()!);
        Assert.Equal(expectedLength, actual.Length);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(actual)));
    }
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

static Task CatalogRejectsHostReservedView()
{
    using var catalog = TemporaryCatalog.Create();
    var document = File.ReadAllText(catalog.Path);
    const string authored = "\"controllerButton\":\"x\"";
    Assert.True(document.Contains(authored, StringComparison.Ordinal),
        "The catalog fixture omitted its authored quick-action button.");
    File.WriteAllText(catalog.Path, document.Replace(
        authored, "\"controllerButton\":\"view\"", StringComparison.Ordinal));
    var exception = Assert.Throws<BridgeCatalogException>(() =>
        BridgeCatalog.Load(catalog.Path));
    Assert.True(exception.Message.Contains(
        "cannot use View because View is reserved for host pinned-surface navigation",
        StringComparison.Ordinal),
        "Bridge catalog validation did not provide the precise host-reserved View diagnostic.");
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

static Task WorkerResidencyBudgetOptionsAreUserSelected()
{
    var defaults = WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget([]);
    Assert.Equal<int?>(null, defaults.MaximumApplicationWorkers);

    var configured = WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget(
        ["--max-resident-workers", "3"]);
    Assert.Equal<int?>(3, configured.MaximumApplicationWorkers);
    Assert.Throws<ArgumentException>(() =>
        WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget(
            ["--max-resident-workers", "0"]));
    Assert.Throws<ArgumentException>(() =>
        WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget(
            ["--max-resident-workers", "-1"]));
    Assert.Throws<ArgumentException>(() =>
        WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget(
            ["--max-resident-workers", "many"]));
    Assert.Throws<ArgumentException>(() =>
        WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget(
            ["--max-resident-workers", "3", "--max-resident-workers", "4"]));
    Assert.Throws<ArgumentException>(() =>
        WidgetRail.WidgetBridge.Program.ResolveWorkerResidencyBudget(
            ["--max-resident-memory-mb", "192"]));
    return Task.CompletedTask;
}

static async Task WorkerResidencyBudgetAdmissionIsRaceSafe()
{
    var budget = new WorkerResidencyBudget(new WorkerResidencyBudgetOptions());
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

    Assert.Equal(32, admissions.Count(admitted => admitted));
    Assert.Equal(32, budget.Snapshot.ApplicationWorkers);
    Assert.Equal(32_768L, budget.Snapshot.ApplicationAdvisoryMemoryMb);
    foreach (var owner in owners) budget.Release(owner);
    Assert.Equal(0, budget.Snapshot.ApplicationWorkers);
    Assert.Equal(0L, budget.Snapshot.ApplicationAdvisoryMemoryMb);
}

static async Task PermittedEighthWorkerPreStartTimeoutReleasesSlot()
{
    var budget = new WorkerResidencyBudget(new WorkerResidencyBudgetOptions
    {
        MaximumApplicationWorkers = 8,
    });
    var retainedOwners = Enumerable.Range(0, 7).Select(_ => new object()).ToArray();
    var retainedLeases = retainedOwners.Select((owner, index) =>
        budget.Reserve(owner, $"retained-{index}", 64, isControlPlane: false)).ToArray();
    try
    {
        Assert.Equal(7, budget.Snapshot.ApplicationWorkers);
        var timedOutOwner = new object();
        var preStartEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = 0;
        var hooks = new WidgetProcessClientTestHooks
        {
            BeforeProcessStartAsync = async cancellationToken =>
            {
                preStartEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            },
        };
        await using var client = new WidgetProcessClient(
            new WidgetProcessOptions
            {
                ExecutablePath = Environment.ProcessPath!,
                WidgetInstanceId = "permitted-eighth-worker",
                ConnectTimeout = TimeSpan.FromMilliseconds(100),
                RequestTimeout = TimeSpan.FromSeconds(2),
                ProcessLeaseFactory = () => budget.Reserve(
                    timedOutOwner, "permitted-eighth", 64, isControlPlane: false),
            },
            TimeProvider.System,
            hooks);
        client.Failed += (_, _) => failures++;

        var startup = client.GetSnapshotAsync();
        await preStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var exception = await Assert.ThrowsAsync<WidgetProcessAdmissionException>(async () =>
            await startup.ConfigureAwait(false));

        Assert.Equal("Worker pre-process setup exceeded its time limit.", exception.Message);
        Assert.Equal(0, failures);
        Assert.Equal(0, client.Starts);
        Assert.True(!client.IsRunning, "Timed-out provisional worker remained running.");
        Assert.Equal(7, budget.Snapshot.ApplicationWorkers);

        var replacementOwner = new object();
        using (budget.Reserve(
            replacementOwner, "replacement-eighth", 64, isControlPlane: false))
        {
            Assert.Equal(8, budget.Snapshot.ApplicationWorkers);
        }
        Assert.Equal(7, budget.Snapshot.ApplicationWorkers);
    }
    finally
    {
        foreach (var lease in retainedLeases) lease.Dispose();
    }
    Assert.Equal(0, budget.Snapshot.ApplicationWorkers);
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
        diagnostics.RecordBridgeStartupPhase("invalid-stage", 42);
        diagnostics.RecordBridgeStartupPhase("trusted-catalog-ready", 17);
        diagnostics.RecordBridgeStartupPhase("control-plane-created");
        diagnostics.RecordBridgeStartupPhase("installed-catalog-pending");
        diagnostics.RecordInstalledCatalogLoad(new BridgeInstalledCatalogObservation(
            Succeeded: true,
            PackageCount: 3,
            VersionCount: 4,
            FileCount: 5,
            ByteCount: 6,
            ElapsedMilliseconds: 7));
        diagnostics.RecordInstalledCatalogLoad(new BridgeInstalledCatalogObservation(
            Succeeded: false,
            PackageCount: int.MaxValue,
            VersionCount: int.MaxValue,
            FileCount: int.MaxValue,
            ByteCount: long.MaxValue,
            ElapsedMilliseconds: long.MaxValue));
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
    Assert.Equal(7, lines.Length);
    Assert.True(lines[0].Contains(
        "stage=trusted-catalog-ready elapsed-ms=17",
        StringComparison.Ordinal), "Trusted-catalog startup phase was not recorded.");
    Assert.True(lines[1].Contains(
        "stage=control-plane-created elapsed-ms=0",
        StringComparison.Ordinal), "Control-plane startup phase was not recorded.");
    Assert.True(lines[2].Contains(
        "stage=installed-catalog-pending elapsed-ms=0",
        StringComparison.Ordinal), "Installed-catalog pending phase was not recorded.");
    Assert.True(lines[3].Contains(
        "stage=installed-catalog-terminal result=validated packages=3 versions=4 " +
        "files=5 bytes=6 elapsed-ms=7",
        StringComparison.Ordinal), "Validated installed-catalog counts were not recorded.");
    Assert.True(lines[4].Contains(
        "stage=installed-catalog-terminal result=rejected packages=256 versions=512 " +
        "files=32768 bytes=2147483648 elapsed-ms=300000",
        StringComparison.Ordinal), "Rejected installed-catalog counts were not bounded.");
    Assert.True(lines[5].Contains(
        "widget=media-sessions stage=snapshot-read code=channel_closed",
        StringComparison.Ordinal), "First transition was not recorded.");
    Assert.True(lines[6].Contains(
        "widget=media-sessions stage=snapshot-read code=platform_unavailable",
        StringComparison.Ordinal), "Reset transition was not recorded.");
    Assert.True(lines.All(line =>
        !line.Contains("player.exe", StringComparison.OrdinalIgnoreCase) &&
        !line.Contains("secret", StringComparison.OrdinalIgnoreCase)),
        "Unsafe diagnostic content crossed the bounded log boundary.");
}

static async Task SharedOverlayDiagnosticsRotate()
{
    using var temporary = new TemporaryDirectory("wrail-overlay-rotation");
    var path = System.IO.Path.Combine(temporary.Path, "overlay.log");
    await File.WriteAllBytesAsync(
        path, Enumerable.Repeat((byte)'x', checked((int)MediaSessionsDiagnosticLog.MaximumFileBytes + 2048)).ToArray());
    await using var diagnostics = new MediaSessionsDiagnosticLog(path);
    diagnostics.AppendBounded("legacy-tail-retained\n");
    for (var index = 0; index < 220; index++)
        diagnostics.AppendBounded($"rotation-{index:D3} {new string('x', 64 * 1024)}\n");

    var generations = Enumerable.Range(0, MediaSessionsDiagnosticLog.RetainedGenerationCount + 1)
        .Select(generation => generation == 0
            ? path
            : System.IO.Path.Combine(temporary.Path, $"overlay.{generation}.log"))
        .Where(File.Exists)
        .ToArray();
    Assert.Equal(MediaSessionsDiagnosticLog.RetainedGenerationCount + 1, generations.Length);
    Assert.True(generations.All(candidate =>
            new FileInfo(candidate).Length <= MediaSessionsDiagnosticLog.MaximumFileBytes),
        "A shared diagnostic generation exceeded its exact byte bound.");
    Assert.True(generations.Sum(candidate => new FileInfo(candidate).Length) <=
                (MediaSessionsDiagnosticLog.RetainedGenerationCount + 1) *
                MediaSessionsDiagnosticLog.MaximumFileBytes,
        "Shared diagnostic retention exceeded its documented total bound.");
    Assert.True(File.ReadAllText(path).Contains("rotation-219", StringComparison.Ordinal),
        "The current generation did not retain the newest diagnostic record.");
}

static async Task WorkerRequestDiagnosticsAreBounded()
{
    using var temporary = new TemporaryDirectory("wrail-worker-request-diagnostics");
    var path = System.IO.Path.Combine(temporary.Path, "overlay.log");
    const string structuralDiagnostic =
        "Widget protocol validation failed at $.root.children[3].contextActions " +
        "(context_actions_not_allowed; field=context_action; state=unknown_action; " +
        "identifier=surface.more).";
    const string unsafeStructuralDiagnostic =
        "Widget protocol validation failed at $.root.children[3].text " +
        "credential=FORGED_STRUCTURAL_SECRET (too_long).";
    const string unsafeIdentifierDiagnostic =
        "Widget protocol validation failed at $.initialFocusId " +
        "(invalid_focus_target; field=initial_focus; state=missing; " +
        "identifier=https://example.invalid/search?q=FORGED_IDENTIFIER_SECRET).";
    await using (var diagnostics = new MediaSessionsDiagnosticLog(path, bridgeSessionGeneration: 7))
    {
        WidgetBridgeServer.ReportWidgetRequestFailure(
            diagnostics.RecordRequestFailure,
            new BridgeWidgetRequestException(
                "installed-widget",
                "worker-runtime-failed",
                new WidgetProcessException(
                    MessageTypes.Render,
                    WorkerErrorCodes.RequestFailed,
                    "provider response credential=DEVELOPER_ONLY path=C:\\private\\widget.json")));
        WidgetBridgeServer.ReportWidgetRequestFailure(
            diagnostics.RecordRequestFailure,
            new BridgeWidgetRequestException(
                "protocol-widget",
                "worker-protocol-failed",
                new WidgetProcessException(
                    MessageTypes.Render,
                    WorkerErrorCodes.ProtocolValidationFailed,
                    structuralDiagnostic)));
        WidgetBridgeServer.ReportWidgetRequestFailure(
            diagnostics.RecordRequestFailure,
            new BridgeWidgetRequestException(
                "unsafe-protocol-widget",
                "worker-protocol-failed",
                new WidgetProcessException(
                    MessageTypes.Render,
                    WorkerErrorCodes.ProtocolValidationFailed,
                    unsafeStructuralDiagnostic)));
        WidgetBridgeServer.ReportWidgetRequestFailure(
            diagnostics.RecordRequestFailure,
            new BridgeWidgetRequestException(
                "unsafe-identifier-widget",
                "worker-protocol-failed",
                new WidgetProcessException(
                    MessageTypes.Render,
                    WorkerErrorCodes.ProtocolValidationFailed,
                    unsafeIdentifierDiagnostic)));
        diagnostics.RecordRequestFailure(new BridgeWidgetRequestDiagnostic(
            "unsafe widget/path",
            MessageTypes.Render,
            WorkerErrorCodes.RequestFailed));
    }

    var lines = File.ReadAllLines(path);
    Assert.Equal(4, lines.Length);
    Assert.True(lines[0].Contains(
        "Widget request diagnostic bridge-session=7 widget=installed-widget " +
        "request=render worker-code=worker_request_failed",
        StringComparison.Ordinal), "The correlated worker request record was not retained.");
    Assert.True(!lines[0].Contains("detail=", StringComparison.Ordinal),
        "A general worker failure retained arbitrary diagnostic detail.");
    Assert.True(lines[1].Contains(
        "widget=protocol-widget request=render " +
        "worker-code=worker_protocol_validation_failed " +
        "validation-path=$.root.children[3].contextActions " +
        "validation-code=context_actions_not_allowed " +
        "validation-field=context_action validation-state=unknown_action " +
        "validation-identifier=surface.more",
        StringComparison.Ordinal), "The bounded validator path and code were not retained.");
    Assert.True(!lines[0].Contains("validation-path=", StringComparison.Ordinal) &&
                !lines[0].Contains("validation-code=", StringComparison.Ordinal),
        "A non-validation worker error projected validation detail.");
    Assert.True(lines[2].Contains(
        "widget=unsafe-protocol-widget request=render " +
        "worker-code=worker_protocol_validation_failed",
        StringComparison.Ordinal), "The malformed validation failure lost its correlation record.");
    Assert.True(!lines[2].Contains("validation-path=", StringComparison.Ordinal) &&
                !lines[2].Contains("validation-code=", StringComparison.Ordinal),
        "An unsafe validator diagnostic crossed the bounded projection.");
    Assert.True(lines[3].Contains(
            "validation-path=$.initialFocusId validation-code=invalid_focus_target",
            StringComparison.Ordinal) &&
        !lines[3].Contains("validation-field=", StringComparison.Ordinal) &&
        !lines[3].Contains("validation-state=", StringComparison.Ordinal) &&
        !lines[3].Contains("validation-identifier=", StringComparison.Ordinal),
        "Bridge projected an independently rejected identifier context.");
    Assert.True(lines.All(line =>
        !line.Contains("credential", StringComparison.OrdinalIgnoreCase) &&
        !line.Contains("provider response", StringComparison.OrdinalIgnoreCase) &&
        !line.Contains("C:\\", StringComparison.Ordinal) &&
        !line.Contains("FORGED_STRUCTURAL_SECRET", StringComparison.Ordinal) &&
        !line.Contains("FORGED_IDENTIFIER_SECRET", StringComparison.Ordinal) &&
        !line.Contains("example.invalid", StringComparison.Ordinal)),
        "Sensitive or package-private detail crossed the persistent log boundary.");
}

static async Task WorkerValidatorDiagnosticsPersistAndCorrelate()
{
    using var temporary = new TemporaryDirectory("wrail-worker-origin-diagnostics");
    var bridgeLog = System.IO.Path.Combine(temporary.Path, "overlay.log");
    var workerRoot = System.IO.Path.Combine(temporary.Path, "workers");
    await using var diagnostics = new MediaSessionsDiagnosticLog(
        bridgeLog, bridgeSessionGeneration: 11);

    await VerifyFailureAsync(
        "validator-home.instance",
        "validator.home.details",
        "validator.home.details",
        "$.activeInputScopeId",
        "invalid_active_input_scope",
        "element_reference",
        "missing",
        "validator.missing.scope");
    await VerifyFailureAsync(
        "validator-hidden.instance",
        "validator.hidden.back",
        "validator.hidden.back",
        "$.initialFocusId",
        "invalid_focus_target",
        "initial_focus",
        "missing",
        "validator.missing.focus");
    await VerifyFailureAsync(
        "validator-return.instance",
        "validator.return.trigger",
        "validator.return.trigger",
        "$.root.initialChildFocusId",
        "invalid_initial_child_focus",
        "return_focus",
        "missing",
        "validator.missing.return");
    await VerifyFailureAsync(
        "validator-action.instance",
        "validator.action.trigger",
        "validator.action.trigger",
        "$.root.children[0].actionId",
        "action_not_allowed",
        "action",
        "unknown_action",
        "validator.unknown.action");
    await VerifyFailureAsync(
        "validator-context.instance",
        "validator.context.trigger",
        "validator.context.trigger",
        "$.root.children[0].contextActions[1].actionId",
        "duplicate_context_action",
        "context_action",
        "duplicate",
        "validator.context.more");
    await VerifyFailureAsync(
        "validator-disabled.instance",
        "validator.disabled.trigger",
        "validator.disabled.trigger",
        "$.initialFocusId",
        "invalid_focus_target",
        "initial_focus",
        "disabled",
        "validator.disabled.focus");
    var maximumPath = "$" + string.Concat(Enumerable.Repeat(".a", 126)) + ".aa";
    var maximumCode = new string('a', 64);
    var maximumIdentifier = new string('i', 128);
    await VerifyFailureAsync(
        "validator-maximum.instance",
        "validator.maximum.trigger",
        "validator.maximum.trigger",
        maximumPath,
        maximumCode,
        "element_reference",
        "outside_active_scope",
        maximumIdentifier);
    await diagnostics.DisposeAsync();

    var bridgeLines = File.ReadAllLines(bridgeLog);
    Assert.Equal(7, bridgeLines.Length);
    foreach (var workerPath in Directory.EnumerateFiles(
                 workerRoot, WidgetWorkerDiagnosticLog.FileName, SearchOption.AllDirectories))
    {
        using var document = JsonDocument.Parse(File.ReadAllText(workerPath).Trim());
        var record = document.RootElement;
        var workerRequest = record.GetProperty("requestId").GetInt64();
        var path = record.GetProperty("validationPath").GetString();
        var code = record.GetProperty("validationCode").GetString();
        var field = record.GetProperty("validationField").GetString();
        var state = record.GetProperty("validationState").GetString();
        var identifier = record.GetProperty("validationIdentifier").GetString();
        Assert.True(workerRequest > 0, "The worker-origin record lost request correlation.");
        var workerProcessId = record.GetProperty("processId").GetInt32();
        Assert.True(workerProcessId > 0 && workerProcessId != Environment.ProcessId,
            "The diagnostic fixture did not traverse a separate current worker process.");
        Assert.Equal(MessageTypes.Render, record.GetProperty("requestType").GetString());
        Assert.Equal(WorkerErrorCodes.ProtocolValidationFailed,
            record.GetProperty("code").GetString());
        Assert.True(bridgeLines.Any(line =>
                line.Contains($"worker-request={workerRequest}", StringComparison.Ordinal) &&
                line.Contains($"validation-path={path}", StringComparison.Ordinal) &&
                line.Contains($"validation-code={code}", StringComparison.Ordinal) &&
                line.Contains($"validation-field={field}", StringComparison.Ordinal) &&
                line.Contains($"validation-state={state}", StringComparison.Ordinal) &&
                line.Contains($"validation-identifier={identifier}", StringComparison.Ordinal)),
            "Bridge diagnostics did not correlate the exact worker-origin failure.");
    }
    Assert.Equal(7, Directory.EnumerateFiles(
        workerRoot, WidgetWorkerDiagnosticLog.FileName, SearchOption.AllDirectories).Count());
    var initialFocusRecordPath = Directory.EnumerateFiles(
            workerRoot, WidgetWorkerDiagnosticLog.FileName, SearchOption.AllDirectories)
        .Single(path =>
        {
            using var candidateDocument = JsonDocument.Parse(File.ReadAllText(path).Trim());
            var candidate = candidateDocument.RootElement;
            return candidate.GetProperty("validationPath").GetString() == "$.initialFocusId" &&
                   candidate.GetProperty("validationCode").GetString() ==
                   "invalid_focus_target" &&
                   candidate.GetProperty("validationField").GetString() == "initial_focus" &&
                   candidate.GetProperty("validationState").GetString() == "missing" &&
                   candidate.GetProperty("validationIdentifier").GetString() ==
                   "validator.missing.focus";
        });
    using (var initialFocusDocument = JsonDocument.Parse(
               File.ReadAllText(initialFocusRecordPath).Trim()))
    {
        var initialFocusWorkerRequest = initialFocusDocument.RootElement
            .GetProperty("requestId").GetInt64();
        Assert.True(bridgeLines.Any(line =>
                line.Contains("bridge-request=", StringComparison.Ordinal) &&
                line.Contains($"worker-request={initialFocusWorkerRequest}",
                    StringComparison.Ordinal) &&
                line.Contains("validation-path=$.initialFocusId", StringComparison.Ordinal) &&
                line.Contains("validation-code=invalid_focus_target", StringComparison.Ordinal) &&
                line.Contains("validation-field=initial_focus", StringComparison.Ordinal) &&
                line.Contains("validation-state=missing", StringComparison.Ordinal) &&
                line.Contains("validation-identifier=validator.missing.focus",
                    StringComparison.Ordinal)),
            "The exact stale InitialFocusId was not correlated from the current worker to the overlay diagnostic.");
    }
    Assert.True(bridgeLines.All(line =>
            line.Contains("bridge-request=", StringComparison.Ordinal) &&
            !line.Contains("secret", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains(temporary.Path, StringComparison.OrdinalIgnoreCase)),
        "Worker/Bridge correlation leaked unsafe data or omitted host request identity.");

    async Task VerifyFailureAsync(
        string instanceId,
        string actionId,
        string sourceElementId,
        string expectedPath,
        string expectedCode,
        string expectedField,
        string expectedState,
        string expectedIdentifier)
    {
        await using var harness = await BridgeHarness.StartAsync(
            instanceId: instanceId,
            workerDiagnosticRoot: workerRoot,
            requestDiagnosticSink: diagnostics.RecordRequestFailure);
        var activated = await harness.Client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
        Assert.Equal(BridgeMessageTypes.Acknowledged, activated.Type);
        var initial = await harness.Client.RequestAsync(
            BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
        Assert.Equal(BridgeMessageTypes.Snapshot, initial.Type);
        var action = await harness.Client.RequestAsync(
            BridgeMessageTypes.Action,
            new BridgeActionRequest(
                "test-widget", new WidgetActionEvent(actionId, sourceElementId)));
        Assert.Equal(BridgeMessageTypes.Acknowledged, action.Type);
        var failure = await harness.Client.RequestAsync(
            BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
        Assert.Equal(BridgeMessageTypes.Error, failure.Type);

        var currentWorkerPath = Directory.EnumerateFiles(
                workerRoot, WidgetWorkerDiagnosticLog.FileName, SearchOption.AllDirectories)
            .Single(path =>
            {
                using var candidateDocument = JsonDocument.Parse(File.ReadAllText(path).Trim());
                var candidate = candidateDocument.RootElement;
                return candidate.GetProperty("validationPath").GetString() == expectedPath &&
                       candidate.GetProperty("validationCode").GetString() == expectedCode &&
                       candidate.GetProperty("validationField").GetString() == expectedField &&
                       candidate.GetProperty("validationState").GetString() == expectedState &&
                       candidate.GetProperty("validationIdentifier").GetString() ==
                       expectedIdentifier;
            });
        var currentWorker = File.ReadAllText(currentWorkerPath);
        Assert.True(currentWorker.Contains(expectedCode, StringComparison.Ordinal),
            "The worker-origin record lost the exact validation code.");
        using var workerDocument = JsonDocument.Parse(currentWorker.Trim());
        var workerRecord = workerDocument.RootElement;
        Assert.Equal(expectedField,
            workerRecord.GetProperty("validationField").GetString());
        Assert.Equal(expectedState,
            workerRecord.GetProperty("validationState").GetString());
        Assert.Equal(expectedIdentifier,
            workerRecord.GetProperty("validationIdentifier").GetString());
    }
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

static async Task BundledWidgetFailuresAreIsolated()
{
    using var fixture = TemporaryBundledCatalog.Create(
        new("dev.test.healthy", "Healthy", InvalidStyle: false),
        new("dev.test.invalid-one", "Invalid one", InvalidStyle: true),
        new("dev.test.invalid-two", "Invalid two", InvalidStyle: true));
    using var installedRoot = new TemporaryDirectory("wrail-bundled-isolation-installed");
    var initial = BridgeCatalog.LoadTrustedObserved(fixture.Path, installedRoot.Path);
    Assert.SequenceEqual(["dev.test.healthy"],
        initial.Catalog.Widgets.Select(widget => widget.Id));
    Assert.SequenceEqual(["dev.test.invalid-one", "dev.test.invalid-two"],
        initial.WidgetRejections.Select(rejection => rejection.WidgetId));
    Assert.True(initial.WidgetRejections.All(rejection =>
            rejection.Code == "invalid_styles"),
        "Invalid bundled WRSS did not retain its stable rejection code.");
    Assert.True(initial.Warnings.All(warning =>
            !warning.Contains(fixture.Root, StringComparison.OrdinalIgnoreCase)),
        "Bundled rejection diagnostics disclosed their local package root.");

    await using var monitor = new BridgeCatalogMonitor(
        fixture.Path,
        installedRoot.Path,
        fixture.WorkerPath,
        initial.Catalog,
        initial.Warnings,
        installedCatalogPending: false,
        loadCatalog: _ => Task.FromResult(
            BridgeCatalog.LoadTrustedObserved(fixture.Path, installedRoot.Path)),
        initialWidgetRejections: initial.WidgetRejections);
    var pipeName = $"wrail-bundled-isolation-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(
        pipeName, initial.Catalog, 64 * 1024, catalogMonitor: monitor);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    await using var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
    try
    {
        var listed = await client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
        Assert.SequenceEqual(["dev.test.healthy"], listed.Payload
            .GetProperty("widgets").EnumerateArray()
            .Select(widget => widget.GetProperty("id").GetString()!));
        Assert.Equal(0, server.RunningWorkerCount);

        fixture.CorrectStyles("dev.test.invalid-one", "dev.test.invalid-two");
        var recovered = await monitor.ReloadNowAsync();
        Assert.True(recovered.Published,
            "Corrected bundled packages did not publish a recovered catalog revision.");
        Assert.True(!recovered.RetainedLastGood,
            "Widget-local correction was misclassified as global last-good retention.");
        Assert.SequenceEqual(
            ["dev.test.healthy", "dev.test.invalid-one", "dev.test.invalid-two"],
            recovered.Current.Widgets.Select(widget => widget.Id));
        Assert.Equal(0, monitor.DiagnosticsSnapshot().WidgetRejections.Count);

        var validCatalog = File.ReadAllBytes(fixture.Path);
        File.WriteAllText(fixture.Path, "{");
        var malformed = await monitor.ReloadNowAsync();
        Assert.True(malformed.RetainedLastGood,
            "Malformed shared catalog structure did not retain the last-good revision.");
        Assert.SequenceEqual(
            ["dev.test.healthy", "dev.test.invalid-one", "dev.test.invalid-two"],
            malformed.Current.Widgets.Select(widget => widget.Id));
        File.WriteAllBytes(fixture.Path, validCatalog);
    }
    finally
    {
        try { await client.RequestAsync(BridgeMessageTypes.Stop, new { }); }
        catch { }
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    using var duplicate = TemporaryBundledCatalog.Create(
        new("dev.test.duplicate", "Duplicate one", InvalidStyle: true),
        new("dev.test.duplicate", "Duplicate two", InvalidStyle: false,
            PackageId: "dev.test.duplicate-two"));
    Assert.Throws<BridgeCatalogException>(() =>
        BridgeCatalog.LoadTrustedObserved(duplicate.Path, installedRoot.Path));

    using var missingSharedWorker = TemporaryBundledCatalog.Create(
        new TemporaryBundledWidgetDefinition(
            "dev.test.shared-worker", "Shared worker", InvalidStyle: false));
    File.Delete(missingSharedWorker.WorkerPath);
    var sharedWorkerFailure = Assert.Throws<BridgeCatalogException>(() =>
        BridgeCatalog.LoadTrustedObserved(
            missingSharedWorker.Path, installedRoot.Path));
    Assert.Equal("invalid_shared_worker", sharedWorkerFailure.Code);

    using var malformedPackageDuplicate = TemporaryBundledCatalog.Create(
        new TemporaryBundledWidgetDefinition(
            "dev.test.cross-widget", "Bundled duplicate", InvalidStyle: false));
    malformedPackageDuplicate.AddConfiguredDeclaration(
        "dev.test.cross-widget", "invalid package identity");
    Assert.Throws<BridgeCatalogException>(() =>
        BridgeCatalog.LoadTrustedObserved(
            malformedPackageDuplicate.Path, installedRoot.Path));

    using var malformedWidgetDuplicate = TemporaryBundledCatalog.Create(
        new TemporaryBundledWidgetDefinition(
            "dev.test.cross-bundled", "Bundled package duplicate", InvalidStyle: false,
            PackageId: "dev.test.cross-package"));
    malformedWidgetDuplicate.AddConfiguredDeclaration(
        "invalid widget identity", "dev.test.cross-package");
    Assert.Throws<BridgeCatalogException>(() =>
        BridgeCatalog.LoadTrustedObserved(
            malformedWidgetDuplicate.Path, installedRoot.Path));

    using var optionalConfigured = TemporaryCatalog.Create(invalidStyle: true);
    var optional = BridgeCatalog.LoadTrustedObserved(
        optionalConfigured.Path, installedRoot.Path);
    Assert.Equal(0, optional.Catalog.Widgets.Count);
    Assert.SequenceEqual(["test-widget"],
        optional.WidgetRejections.Select(rejection => rejection.WidgetId));

    using var manyInvalid = TemporaryCatalog.CreateManyInvalidStyles(
        Enumerable.Range(0, 65).Select(index => new TemporaryWidgetDefinition(
            $"dev.test.invalid-{index:D3}",
            $"dev.test.invalid-{index:D3}",
            "dev.test",
            $"invalid.{index:D3}.instance")).ToArray());
    var many = BridgeCatalog.LoadTrustedObserved(manyInvalid.Path, installedRoot.Path);
    Assert.Equal(65, many.WidgetRejections.Count);
    await using var manyMonitor = new BridgeCatalogMonitor(
        manyInvalid.Path,
        installedRoot.Path,
        fixture.WorkerPath,
        many.Catalog,
        many.Warnings,
        installedCatalogPending: false,
        loadCatalog: null,
        initialWidgetRejections: many.WidgetRejections);
    Assert.Equal(65, manyMonitor.DiagnosticsSnapshot().WidgetRejections.Count);

    using var essentialSettings = TemporaryCatalog.Create(
        invalidStyle: true,
        id: "settings",
        packageId: "widgetrail.firstparty.settings",
        publisherId: "widgetrail.firstparty");
    Assert.Throws<BridgeCatalogException>(() =>
        BridgeCatalog.LoadTrustedObserved(essentialSettings.Path, installedRoot.Path));
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
    Assert.SequenceEqual(["fullWidgetPinningSupported", "icon", "iconAssets", "id", "instanceId", "name", "packageContentDigest", "pinningSupported", "presentationGeneration", "protectedWifiPromptSupported", "quickActions", "runtimeGeneration"],
        descriptor.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("test-widget", descriptor.GetProperty("id").GetString());
    Assert.Equal("Test Widget", descriptor.GetProperty("name").GetString());
    Assert.Equal("test.instance", descriptor.GetProperty("instanceId").GetString());
    Assert.Equal(32, descriptor.GetProperty("runtimeGeneration").GetString()!.Length);
    Assert.Equal(32, descriptor.GetProperty("presentationGeneration").GetString()!.Length);
    Assert.Equal("music", descriptor.GetProperty("icon").GetString());
    Assert.Equal(0, descriptor.GetProperty("iconAssets").GetArrayLength());
    Assert.Equal(string.Empty, descriptor.GetProperty("packageContentDigest").GetString());
    Assert.False(descriptor.GetProperty("fullWidgetPinningSupported").GetBoolean(),
        "Omitted full-widget support must project closed.");
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
static async Task PackageSvgIconsResolveLazily()
{
    using var installed = new TemporaryDirectory("wrail-package-icon-installed");
    using var fixture = TemporaryBundledCatalog.Create(
        new TemporaryBundledWidgetDefinition(
            "dev.test.package-icon", "Package icon", InvalidStyle: false,
            PackageIcon: true));
    var loaded = BridgeCatalog.LoadTrustedObserved(fixture.Path, installed.Path);
    var configured = loaded.Catalog.GetConfigured("dev.test.package-icon");
    var descriptor = configured.PublicDescriptor();
    Assert.Equal(1, descriptor.IconAssets.Count);
    Assert.True(descriptor.PackageIcon is not null,
        "Icon-bearing descriptor omitted its package icon metadata.");
    Assert.Equal(64, descriptor.PackageContentDigest.Length);
    var metadata = descriptor.IconAssets.Single();
    var descriptorWire = BridgeJson.ToElement(descriptor);
    var metadataWire = descriptorWire.GetProperty("iconAssets")[0];
    Assert.True(metadataWire.TryGetProperty("id", out var wireId) &&
                wireId.GetString() == metadata.AssetId &&
                !metadataWire.TryGetProperty("assetId", out _) &&
                metadataWire.GetProperty("sourceSha256").GetString() ==
                    metadata.SourceSha256 &&
                metadataWire.GetProperty("normalizedSha256").GetString() ==
                    metadata.NormalizedSha256,
        "BridgeJson package icon metadata must retain the native strict id wire contract.");

    var pipeName = $"wrail-package-icon-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(
        pipeName, loaded.Catalog, 256 * 1024);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    var client = await BridgeTestClient.ConnectAsync(pipeName, 256 * 1024);
    try
    {
        var response = await client.RequestAsync(
            BridgeMessageTypes.ResolvePackageIcon,
            new BridgePackageIconRequest(
                descriptor.Id,
                descriptor.RuntimeGeneration,
                descriptor.PresentationGeneration,
                descriptor.PackageContentDigest,
                metadata.AssetId,
                metadata.SourceSha256,
                metadata.NormalizedSha256));
        Assert.Equal(BridgeMessageTypes.PackageIcon, response.Type);
        Assert.Equal(descriptor.RuntimeGeneration,
            response.Payload.GetProperty("runtimeGeneration").GetString());
        Assert.Equal(metadata.SourceSha256,
            response.Payload.GetProperty("sourceSha256").GetString());
        var bytes = Convert.FromBase64String(
            response.Payload.GetProperty("normalizedSvgBase64").GetString()!);
        Assert.Equal(metadata.NormalizedBytes, bytes.Length);
        Assert.Equal(metadata.NormalizedSha256,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Equal(0, server.RunningWorkerCount);
    }
    finally
    {
        try { await client.RequestAsync(BridgeMessageTypes.Stop, new { }); }
        catch { }
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    using var replacement = TemporaryBundledCatalog.Create(
        new TemporaryBundledWidgetDefinition(
            "dev.test.package-icon", "Package icon", InvalidStyle: false,
            PackageIcon: true,
            PackageIconPathData: "M3 3 L21 3 L12 21 Z"));
    var replacementCatalog = BridgeCatalog.LoadTrustedObserved(
        replacement.Path, installed.Path).Catalog;
    var retired = Assert.Throws<BridgeStalePackageIconAuthorityException>(() =>
        replacementCatalog.ResolvePackageIcon(
        descriptor.Id,
        descriptor.PresentationGeneration,
        metadata.AssetId,
        metadata.NormalizedSha256));
    Assert.Equal("stale_package_icon_authority",
        WidgetBridgeServer.CreateRequestFailure(retired).Code);
    var replacementDescriptor = replacementCatalog.GetConfigured(
        descriptor.Id).PublicDescriptor();
    Assert.True(
        replacementDescriptor.PresentationGeneration != descriptor.PresentationGeneration &&
        replacementDescriptor.IconAssets.Single().NormalizedSha256 !=
            metadata.NormalizedSha256,
        "Package replacement did not retire the prior icon authority.");

    using var invalid = TemporaryBundledCatalog.Create(
        new TemporaryBundledWidgetDefinition(
            "dev.test.package-icon-invalid", "Invalid package icon",
            InvalidStyle: false, PackageIcon: true,
            PackageIconPathData: "M0 0 X1 1"));
    var invalidLoad = BridgeCatalog.LoadTrustedObserved(invalid.Path, installed.Path);
    var invalidDescriptor = invalidLoad.Catalog.GetConfigured(
        "dev.test.package-icon-invalid").PublicDescriptor();
    Assert.True(invalidDescriptor.PackageIcon is null &&
                invalidDescriptor.IconAssets.Count == 0 &&
                invalidDescriptor.PackageContentDigest == string.Empty,
        "A malformed package icon did not preserve the widget's semantic fallback.");
    Assert.True(invalidLoad.Warnings.Any(warning => warning.Contains(
            "semantic fallback remains active", StringComparison.Ordinal)),
        "Malformed package icon fallback did not produce a bounded safe warning.");

    using var mixed = TemporaryBundledCatalog.Create(
        new TemporaryBundledWidgetDefinition(
            "dev.test.package-icon-mixed", "Mixed package icons",
            InvalidStyle: false, PackageIcon: true, MixedPackageIcons: true));
    var mixedLoad = BridgeCatalog.LoadTrustedObserved(mixed.Path, installed.Path);
    var mixedConfigured = mixedLoad.Catalog.GetConfigured(
        "dev.test.package-icon-mixed");
    var mixedDescriptor = mixedConfigured.PublicDescriptor();
    Assert.Equal(1, mixedDescriptor.IconAssets.Count);
    Assert.Equal("test.mark", mixedDescriptor.IconAssets.Single().AssetId);
    Assert.True(mixedDescriptor.PackageIcon?.AssetId == "test.mark" &&
                mixedConfigured.DeclaredPackageIconAssetIds.SetEquals(
                    ["test.mark", "test.unavailable"]),
        "One unavailable package icon retired a valid sibling or its declared authority.");
    var unavailable = new WidgetPackageIcon(
        "test.unavailable", WidgetPackageIconColorMode.ThemeTint);
    var mixedSnapshot = new ViewSnapshot
    {
        ProtocolVersion = ProtocolConstants.PackageSvgIconVersion,
        Sequence = 1,
        WidgetInstanceId = mixedConfigured.InstanceId,
        ActiveInputScopeId = "root",
        Root = new ViewNode
        {
            Id = "root",
            Kind = ViewNodeKind.Stack,
            Children =
            [
                new ViewNode
                {
                    Id = "action",
                    Kind = ViewNodeKind.Button,
                    Text = "Action",
                    ActionId = "action.run",
                    Glyph = WidgetGlyph.Connection,
                    PackageIcon = unavailable,
                },
                new ViewNode
                {
                    Id = "select",
                    Kind = ViewNodeKind.Select,
                    Text = "Mode",
                    ActionId = "mode.changed",
                    AccessibilityLabel = "Mode",
                    SelectOptions =
                    [
                        new WidgetSelectOption(
                            "unavailable", "Unavailable", "mode.unavailable",
                            true, WidgetGlyph.Connection)
                        {
                            PackageIcon = unavailable,
                        },
                    ],
                },
                new ViewNode
                {
                    Id = "presentation",
                    Kind = ViewNodeKind.FocusPresentationSurface,
                    FocusPresentation = new ViewNode
                    {
                        Id = "focus.icon",
                        Kind = ViewNodeKind.Icon,
                        Glyph = WidgetGlyph.Connection,
                        PackageIcon = unavailable,
                        AccessibilityLabel = "Unavailable focus icon",
                    },
                    DefaultFocusPresentation = new ViewNode
                    {
                        Id = "focus.default",
                        Kind = ViewNodeKind.Text,
                        Text = "Default",
                    },
                },
            ],
        },
    };
    BridgeClientRegistry.DemandPackageIconAuthority(
        mixedConfigured, mixedSnapshot);
    var forgedSnapshot = mixedSnapshot with
    {
        Root = mixedSnapshot.Root with
        {
            Children =
            [
                mixedSnapshot.Root.Children[0] with
                {
                    PackageIcon = new(
                        "not.declared", WidgetPackageIconColorMode.ThemeTint),
                },
            ],
        },
    };
    Assert.Throws<BridgeProtocolException>(() =>
        BridgeClientRegistry.DemandPackageIconAuthority(
            mixedConfigured, forgedSnapshot));
    Assert.Equal("svg_element", mixedConfigured.PackageIconAdmissionFailure);
    Assert.True(mixedLoad.Warnings.Contains(
            "Widget 'dev.test.package-icon-mixed' package icon was rejected; semantic fallback remains active.",
            StringComparer.Ordinal),
        "Mixed icon fallback did not publish the exact bounded bundled-catalog warning.");

    using var overAggregate = TemporaryBundledCatalog.Create(
        new TemporaryBundledWidgetDefinition(
            "dev.test.package-icon-aggregate", "Aggregate package icons",
            InvalidStyle: false, PackageIcon: true,
            OverAggregatePackageIcons: true));
    var aggregateLoad = BridgeCatalog.LoadTrustedObserved(
        overAggregate.Path, installed.Path);
    var aggregateConfigured = aggregateLoad.Catalog.GetConfigured(
        "dev.test.package-icon-aggregate");
    var aggregateDescriptor = aggregateConfigured.PublicDescriptor();
    Assert.True(aggregateDescriptor.PackageIcon is null &&
                aggregateDescriptor.IconAssets.Count == 0 &&
                aggregateDescriptor.PackageContentDigest == string.Empty &&
                aggregateConfigured.DeclaredPackageIconAssetIds.Count == 10 &&
                aggregateConfigured.PackageIconAdmissionFailure ==
                    "icon_assets_too_large",
        "Aggregate overflow exposed partial icon metadata or removed declared fallback authority.");
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
            "widget-a", "library.art.0123456789abcdef0123456789abcdef",
            "runtime-generation", "presentation-generation")),
    });
    Assert.Equal(BridgeRequestKind.ResolveArtwork, artwork.Kind);
    Assert.Equal("widget-a", artwork.WidgetId);
    var halfPairedArtwork = BridgeRequestClassifier.Classify(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.ResolveArtwork,
            RequestId = 7,
            Payload = BridgeJson.ToElement(new BridgeArtworkRequest(
                "widget-a", "library.art.0123456789abcdef0123456789abcdef",
                "runtime-generation", null)),
        });
    Assert.Equal(BridgeRequestKind.Malformed, halfPairedArtwork.Kind);
    Assert.Equal<string?>(null, halfPairedArtwork.WidgetId);

    var embeddedMedia = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.ResolveEmbeddedMedia,
        RequestId = 14,
        Payload = BridgeJson.ToElement(new BridgeEmbeddedMediaRequest(
            "widget-a", "widget-a.default", "runtime-generation",
            "presentation-generation", 7, "primary-media")),
    });
    Assert.Equal(BridgeRequestKind.ResolveEmbeddedMedia, embeddedMedia.Kind);
    Assert.Equal("widget-a", embeddedMedia.WidgetId);

    var malformedEmbeddedMedia = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.ResolveEmbeddedMedia,
        RequestId = 15,
        Payload = BridgeJson.ToElement(new BridgeEmbeddedMediaRequest(
            "widget-a", "widget-a.default", "runtime-generation",
            "presentation-generation", 0, "primary-media")),
    });
    Assert.Equal(BridgeRequestKind.Malformed, malformedEmbeddedMedia.Kind);
    Assert.Equal<string?>(null, malformedEmbeddedMedia.WidgetId);

    var forgedEmbeddedMedia = BridgeRequestClassifier.Classify(new BridgeEnvelope
    {
        Type = BridgeMessageTypes.ResolveEmbeddedMedia,
        RequestId = 16,
        Payload = BridgeJson.ToElement(new
        {
            widgetId = "widget-a",
            instanceId = "widget-a.default",
            runtimeGeneration = "runtime-generation",
            presentationGeneration = "presentation-generation",
            sequence = 7,
            sessionId = "primary-media",
            path = @"C:\\forbidden.html",
        }),
    });
    Assert.Equal(BridgeRequestKind.Malformed, forgedEmbeddedMedia.Kind);
    Assert.Equal<string?>(null, forgedEmbeddedMedia.WidgetId);

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
    const string secretSentinel = "provider-neutral-secret-sentinel";
    await using var harness = await BridgeHarness.StartAsync(
        instanceId: "sensitive-text-entry.instance");
    var lifecycle = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest(
            "test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);
    var initial = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var initialSnapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        initial.Payload.GetProperty("snapshot").GetRawText()));
    var sensitiveEntry = Flatten(initialSnapshot.Root).Single(
        node => node.Id == "credential.entry");
    Assert.Equal(TextEntryInputKind.Sensitive, sensitiveEntry.TextEntryInputKind);
    Assert.Equal(string.Empty, sensitiveEntry.TextEntryValue);
    Assert.Equal<string?>(null, sensitiveEntry.AccessibilityValue);
    Assert.True(!initial.Payload.GetRawText().Contains(
        secretSentinel, StringComparison.Ordinal),
        "Initial sensitive snapshot exposed the secret sentinel.");

    var action = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest(
            "test-widget",
            new WidgetActionEvent("committed-text", "credential.entry")
            {
                CommittedText = secretSentinel,
            }));
    Assert.Equal(BridgeMessageTypes.Acknowledged, action.Type);
    _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        response.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal("committed:32:diagnostic-redacted", Flatten(snapshot.Root).Single(
        node => node.Id == "committed-text-status").Text);
    Assert.True(!System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(snapshot))
            .Contains(secretSentinel, StringComparison.Ordinal),
        "Successor sensitive snapshot exposed the secret sentinel.");
}

static async Task TrustedArtworkDemandIsExact()
{
    Assert.Equal("image/webp", WidgetEncodedArtworkContract.ContentTypeValue(
        WidgetArtworkContentType.WebP));
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
        var initialWidgets = await client.RequestAsync(
            BridgeMessageTypes.ListWidgets, new { });
        var initialDescriptor = initialWidgets.Payload.GetProperty("widgets")
            .EnumerateArray().Single(item =>
                item.GetProperty("id").GetString() == "test-widget");
        var initialRuntimeGeneration = initialDescriptor
            .GetProperty("runtimeGeneration").GetString()!;
        var initialPresentationGeneration = initialDescriptor
            .GetProperty("presentationGeneration").GetString()!;
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
            new BridgeArtworkRequest(
                "test-widget", firstHandle,
                initialRuntimeGeneration, initialPresentationGeneration));
        Assert.Equal(BridgeMessageTypes.Acknowledged, resolved.Type);
        await iconStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var concurrent = await client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
        Assert.Equal(BridgeMessageTypes.Widgets, concurrent.Type);
        releaseIcon.TrySetResult();
        var artwork = await client.ReadEventAsync(BridgeMessageTypes.Artwork);
        Assert.Equal("image/png", artwork.Payload.GetProperty("contentType").GetString());
        Assert.Equal(png, artwork.Payload.GetProperty("contentBase64").GetString());
        Assert.Equal(initialRuntimeGeneration,
            artwork.Payload.GetProperty("runtimeGeneration").GetString());
        Assert.Equal(initialPresentationGeneration,
            artwork.Payload.GetProperty("presentationGeneration").GetString());
        Assert.Equal(1, backend.AppLibraryIconCalls);

        var replacementCatalog = new BridgeCatalog([
            catalog.GetConfigured("test-widget") with
            {
                WorkerFingerprint = new string('c', 64),
                CatalogFingerprint = new string('d', 64),
            },
        ]);
        server.ApplyCatalog(replacementCatalog, revision: 1, publishEvent: false);
        await WaitUntilAsync(
            () => server.RunningWorkerCount == 0,
            TimeSpan.FromSeconds(3));
        _ = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(
                "test-widget", WidgetLifecycleState.Interactive));
        var replacementSnapshotResponse = await client.RequestAsync(
            BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
        var replacementSnapshot = SnapshotJson.Deserialize(
            System.Text.Encoding.UTF8.GetBytes(
                replacementSnapshotResponse.Payload.GetProperty("snapshot").GetRawText()));
        var replacementHandle = Flatten(replacementSnapshot.Root).Single(
            node => node.Id == "artwork.image").ArtworkHandle!;
        var replacementWidgets = await client.RequestAsync(
            BridgeMessageTypes.ListWidgets, new { });
        var replacementDescriptor = replacementWidgets.Payload.GetProperty("widgets")
            .EnumerateArray().Single(item =>
                item.GetProperty("id").GetString() == "test-widget");
        var replacementRuntimeGeneration = replacementDescriptor
            .GetProperty("runtimeGeneration").GetString()!;
        var replacementPresentationGeneration = replacementDescriptor
            .GetProperty("presentationGeneration").GetString()!;
        Assert.True(initialRuntimeGeneration != replacementRuntimeGeneration,
            "Catalog replacement did not rotate runtime authority.");
        var staleOrigin = await client.RequestAsync(
            BridgeMessageTypes.ResolveArtwork,
            new BridgeArtworkRequest(
                "test-widget", replacementHandle,
                initialRuntimeGeneration, initialPresentationGeneration));
        Assert.Equal(BridgeMessageTypes.Error, staleOrigin.Type);
        Assert.Equal("stale_artwork_authority",
            staleOrigin.Payload.GetProperty("code").GetString());
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
            new BridgeArtworkRequest(
                "test-widget", replacementHandle,
                replacementRuntimeGeneration, replacementPresentationGeneration));
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
        server.ApplyCatalog(
            new BridgeCatalog([]),
            revision: 3,
            publishEvent: false);
        releaseStale.TrySetResult();
        await staleFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(
            () => server.RunningWorkerCount == 0,
            TimeSpan.FromSeconds(3));
        server.ApplyCatalog(catalog, revision: 4, publishEvent: false);
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
        var rotatedWidgets = await client.RequestAsync(
            BridgeMessageTypes.ListWidgets, new { });
        var rotatedDescriptor = rotatedWidgets.Payload.GetProperty("widgets")
            .EnumerateArray().Single(item =>
                item.GetProperty("id").GetString() == "test-widget");
        var rotatedRuntimeGeneration = rotatedDescriptor
            .GetProperty("runtimeGeneration").GetString()!;
        var rotatedPresentationGeneration = rotatedDescriptor
            .GetProperty("presentationGeneration").GetString()!;
        Assert.Equal(0, client.PendingEventCountOfType(BridgeMessageTypes.Artwork));
        Assert.Equal(2, backend.AppLibraryIconCalls);

        var forged = await client.RequestAsync(
            BridgeMessageTypes.ResolveArtwork,
            new BridgeArtworkRequest(
                "test-widget", "library.art.00000000000000000000000000000000",
                rotatedRuntimeGeneration, rotatedPresentationGeneration));
        Assert.Equal(BridgeMessageTypes.Error, forged.Type);
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
            new BridgeArtworkRequest(
                "test-widget", rotatedHandle,
                rotatedRuntimeGeneration, rotatedPresentationGeneration));
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

static async Task ActionBoundLaunchCloseIsTerminalExact()
{
    using var temporary = new TemporaryDirectory("wrail-bridge-action-close");
    var identity = new BrokerWidgetIdentity(
        "dev.example.action-close", "dev.example", "default");
    var consent = new ConsentStore(temporary.Path);
    await consent.SetDecisionAsync(
        identity, PlatformCapabilities.AppLibraryReadV1, ConsentDecision.Grant);
    await consent.SetDecisionAsync(
        identity, PlatformCapabilities.AppLibraryLaunchV1, ConsentDecision.Grant);
    var backend = new SimulatedPlatformBrokerBackend();
    backend.SetAppLibraryBackend([
        new AppLibraryBackendItemSummary(
            "provider-app", "stable-app", "Test App", AppLibraryKind.Application),
    ]);
    var effects = new List<BrokerHostEffect>();
    var context = new WidgetProcessCompanionContext(
        WidgetWorkerIsolationPolicy.HostTrustedJobOnly, null, null);
    await using var companion = new BrokerWidgetProcessCompanion(
        identity.PackageId,
        identity.PublisherId,
        identity.InstanceId,
        [PlatformCapabilities.AppLibraryReadV1, PlatformCapabilities.AppLibraryLaunchV1],
        consent,
        backend,
        context,
        hostEffectSink: effects.Add);
    await companion.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var server = companion.RunAsync(lifetime.Token);
    var arguments = companion.WorkerArguments;
    string Argument(string name) => arguments[Array.IndexOf(arguments.ToArray(), name) + 1];
    await using var client = new BrokerPipeClient(
        Argument("--broker-pipe"), identity, Argument("--broker-nonce"));
    await client.ConnectAsync(lifetime.Token);

    var page = await client.RequestAsync(
        PlatformCapabilities.AppLibraryReadV1,
        PlatformCapabilities.AppLibraryList,
        new AppLibraryBackendCursorRequest(new AppLibraryBackendQuery(), null, null, 1),
        lifetime.Token);
    var appId = page.Payload!.Value.GetProperty("items")[0]
        .GetProperty("appId").GetString()!;
    async Task<BrokerResponseEnvelope> Launch(long? executionId) => executionId is null
        ? await client.RequestAsync(
            PlatformCapabilities.AppLibraryLaunchV1,
            PlatformCapabilities.AppLibraryLaunch,
            new LaunchAppLibraryItemRequest(appId) { CloseOverlayOnSuccess = true },
            lifetime.Token)
        : await client.RequestWithActionAsync(
            PlatformCapabilities.AppLibraryLaunchV1,
            PlatformCapabilities.AppLibraryLaunch,
            new LaunchAppLibraryItemRequest(appId) { CloseOverlayOnSuccess = true },
            null,
            null,
            executionId,
            lifetime.Token);

    Assert.True((await Launch(1)).Succeeded,
        "First action-bound launch was not acknowledged.");
    Assert.Equal(0, effects.Count);
    Assert.True((await Launch(1)).Succeeded,
        "Duplicate action-bound launch was not acknowledged.");
    Assert.Equal(0, effects.Count);
    ((IWidgetActionEffectCoordinator)companion).CompleteAction(
        new WidgetActionExecutionTerminal(1, WidgetActionExecutionOutcome.Succeeded));
    Assert.Equal(1, effects.Count);
    ((IWidgetActionEffectCoordinator)companion).CompleteAction(
        new WidgetActionExecutionTerminal(1, WidgetActionExecutionOutcome.Succeeded));
    Assert.Equal(1, effects.Count);

    Assert.True((await Launch(2)).Succeeded,
        "Failure-terminal launch setup was not acknowledged.");
    ((IWidgetActionEffectCoordinator)companion).CompleteAction(
        new WidgetActionExecutionTerminal(2, WidgetActionExecutionOutcome.Failed));
    Assert.Equal(1, effects.Count);

    Assert.True((await Launch(3)).Succeeded,
        "Lifecycle-cancellation launch setup was not acknowledged.");
    await companion.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    ((IWidgetActionEffectCoordinator)companion).CompleteAction(
        new WidgetActionExecutionTerminal(3, WidgetActionExecutionOutcome.Succeeded));
    Assert.Equal(1, effects.Count);

    await companion.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    ((IWidgetActionEffectCoordinator)companion).CompleteAction(
        new WidgetActionExecutionTerminal(4, WidgetActionExecutionOutcome.Succeeded));
    var callsBeforeStale = backend.AppLibraryLaunchCalls;
    var stale = await Launch(4);
    Assert.True(!stale.Succeeded,
        "A terminal action execution was readmitted to the broker.");
    Assert.Equal("stale_action_context", stale.ErrorCode);
    Assert.Equal(callsBeforeStale, backend.AppLibraryLaunchCalls);

    var lateStarted = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseLate = new TaskCompletionSource<AppLibraryLaunchObservationSummary>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    backend.AppLibraryObservedLaunchHandler = (_, _) =>
    {
        lateStarted.TrySetResult();
        return releaseLate.Task;
    };
    var late = client.RequestWithActionAsync(
        PlatformCapabilities.AppLibraryLaunchV1,
        PlatformCapabilities.AppLibraryLaunchObserved,
        new LaunchAppLibraryItemRequest(appId) { CloseOverlayOnSuccess = true },
        null,
        null,
        5,
        lifetime.Token);
    await lateStarted.Task.WaitAsync(lifetime.Token);
    await companion.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    await companion.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    releaseLate.TrySetResult(new AppLibraryLaunchObservationSummary(
        AppLibraryLaunchObservationState.LauncherStarted, false, false));
    Assert.True((await late).Succeeded,
        "Cancellation-ignoring backend did not exercise late success response.");
    ((IWidgetActionEffectCoordinator)companion).CompleteAction(
        new WidgetActionExecutionTerminal(5, WidgetActionExecutionOutcome.Succeeded));
    Assert.Equal(1, effects.Count);

    Assert.True((await Launch(null)).Succeeded,
        "Out-of-action launch compatibility was not acknowledged.");
    Assert.Equal(2, effects.Count);
    Assert.True(effects.All(effect =>
            effect.Kind == BrokerHostEffectKind.CloseOverlayAfterAppLaunch),
        "The coordinator published an unexpected host effect kind.");

    lifetime.Cancel();
    try { await server; }
    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
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
    var registrations = new BridgeRegistrationBackend();
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
    registrations.Seed(selectedIdentity);
    registrations.Seed(neighborIdentity);
    await using var composite = new CompositePlatformBrokerBackend(
        simulator, simulator, appLibrary: registrations, privateState: backend);
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
        Assert.False((await registrations.GetRunningAppRegistrationStateAsync(
            selectedIdentity, CancellationToken.None)).Exists,
            "Selected portable registrations survived local-data clear.");
        Assert.True((await backend.ReadPrivateStateAsync(
            neighborIdentity, CancellationToken.None)).Exists,
            "Neighbor installed state changed during clear.");
        Assert.True((await registrations.GetRunningAppRegistrationStateAsync(
            neighborIdentity, CancellationToken.None)).Exists,
            "Neighbor portable registrations changed during local-data clear.");

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
    var appLibrary = new BridgeRegistrationBackend();
    var simulator = new SimulatedPlatformBrokerBackend();
    await using var composite = new CompositePlatformBrokerBackend(
        simulator, simulator, appLibrary: appLibrary);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, load.Catalog);
    await using var server = new WidgetBridgeServer(
        $"wrail-bridge-uninstall-{Guid.NewGuid():N}", load.Catalog, 64 * 1024,
        platformBackend: composite, catalogMonitor: monitor);

    var inspection = await server.InspectWidgetPackageUninstallAsync(selected.Manifest.Id);
    Assert.True(inspection.CanUninstall && inspection.ConfirmationToken is not null,
        "Disabled package did not receive exact uninstall admission.");
    Assert.Equal(2, inspection.VersionCount);
    Assert.True(!inspection.ConfirmationToken!.Contains(catalogRoot,
        StringComparison.OrdinalIgnoreCase), "Confirmation token exposed a path.");
    var uninstallIdentity = new BrokerWidgetIdentity(
        inspection.WidgetId, inspection.PublisherId,
        InstalledWidgetInstanceIdentity.Derive(
            inspection.WidgetId, inspection.ActiveVersion));
    var olderUninstallIdentity = new BrokerWidgetIdentity(
        inspection.WidgetId, "unsigned." + new string('e', 64),
        InstalledWidgetInstanceIdentity.Derive(
            inspection.WidgetId, "1.0.0"));
    appLibrary.Seed(uninstallIdentity);
    appLibrary.Seed(olderUninstallIdentity);

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
    appLibrary.FailRetirement = true;
    var cleanupFailed = await server.UninstallWidgetPackageAsync(
        inspection.WidgetId, inspection.PublisherId, inspection.ActiveVersion,
        inspection.ConfirmationToken);
    Assert.Equal(PlatformWidgetPackageUninstallStatus.RecoveryPending,
        cleanupFailed.Status);
    Assert.True((await catalog.DiscoverAsync()).Widgets.Any(item =>
        item.Id == selected.Manifest.Id),
        "Provider precommit failure did not roll the disabled package back.");
    Assert.True((await appLibrary.GetRunningAppRegistrationStateAsync(
        uninstallIdentity, CancellationToken.None)).Exists,
        "Provider precommit failure removed registration authority.");
    appLibrary.FailRetirement = false;
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
    Assert.False((await appLibrary.GetRunningAppRegistrationStateAsync(
        uninstallIdentity, CancellationToken.None)).Exists,
        "Exact package uninstall retained package-owned portable registrations.");
    Assert.False((await appLibrary.GetRunningAppRegistrationStateAsync(
        olderUninstallIdentity, CancellationToken.None)).Exists,
        "Exact package uninstall retained an older publisher generation.");

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
        "Invalid installed state retained rejected Community authority.");
    Assert.True(invalidInstalled.Published,
        "Invalid installed state did not publish trusted-only authority.");
    Assert.Equal(3L, invalidInstalled.Revision);
    Assert.SequenceEqual(["test-widget"],
        invalidInstalled.Current.Widgets.Select(widget => widget.Id));
    await File.WriteAllBytesAsync(Path.Combine(catalogRoot, "catalog-state.json"), validState);

    var restored = await monitor.ReloadNowAsync();
    Assert.True(restored.Published, "Restored installed authority was not published.");
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

static async Task ColdInstalledCatalogDoesNotBlockBridgeReadiness()
{
    var trustedCatalogPath = Path.Combine(
        FindRepositoryRoot(), "src", "OverlayHost", "out", "Release", "widget-catalog.json");
    Assert.True(File.Exists(trustedCatalogPath),
        "The coherent bundled catalog fixture was not built before the startup gate.");
    using var installedFiles = TemporaryCatalog.Create(
        id: "dev.example.delayed",
        packageId: "dev.example.delayed",
        publisherId: "dev.example",
        name: "Delayed Widget",
        instanceId: "delayed.instance");
    using var installedRoot = new TemporaryDirectory("wrail-bridge-delayed-catalog");
    var trusted = BridgeCatalog.LoadTrusted(trustedCatalogPath, installedRoot.Path);
    var trustedIds = trusted.Widgets.Select(widget => widget.Id).ToArray();
    Assert.True(trustedIds.Contains("settings", StringComparer.Ordinal) && trustedIds.Length > 1,
        "The cold-readiness test did not use the real bundled catalog shape.");
    var installed = BridgeCatalog.Load(installedFiles.Path);
    var combined = new BridgeCatalog(
    [
        .. trusted.Widgets.Select(widget => trusted.GetConfigured(widget.Id)),
        installed.GetConfigured("dev.example.delayed"),
    ]);
    var loadStarted = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseLoad = new TaskCompletionSource<BridgeCatalogLoadResult>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var watcherSetupStarted = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await using var monitor = new BridgeCatalogMonitor(
        trustedCatalogPath,
        installedRoot.Path,
        Environment.ProcessPath!,
        trusted,
        initialDiagnostics: null,
        installedCatalogPending: true,
        loadCatalog: async cancellationToken =>
        {
            loadStarted.TrySetResult(true);
            return await releaseLoad.Task.WaitAsync(cancellationToken);
        },
        prepareWatchers: async cancellationToken =>
        {
            watcherSetupStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
    await using var appearance = await TemporaryAppearance.CreateAsync();
    var pipeName = $"wrail-bridge-delayed-startup-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(
        pipeName,
        trusted,
        64 * 1024,
        appearance.Service,
        catalogMonitor: monitor);
    monitor.Start();
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    await using var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
    try
    {
        await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await watcherSetupStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(!releaseLoad.Task.IsCompleted,
            "The delayed installed-catalog loader unexpectedly completed.");

        var listedWhilePending = await client.RequestAsync(
            BridgeMessageTypes.ListWidgets, new { });
        Assert.Equal(BridgeMessageTypes.Widgets, listedWhilePending.Type);
        Assert.SequenceEqual(trustedIds, listedWhilePending.Payload
            .GetProperty("widgets").EnumerateArray()
            .Select(widget => widget.GetProperty("id").GetString()!));
        var appearanceWhilePending = await client.RequestAsync(
            BridgeMessageTypes.GetPlatformAppearance, new { });
        Assert.Equal(BridgeMessageTypes.PlatformAppearance, appearanceWhilePending.Type);
        Assert.Equal("dev.example.bridge",
            appearanceWhilePending.Payload.GetProperty("themeId").GetString());
        var pending = monitor.DiagnosticsSnapshot();
        Assert.True(pending.InstalledCatalogPending,
            "Installed catalog authority was not explicitly pending.");
        Assert.Equal(0L, pending.Revision);
        Assert.Equal(0, server.RunningWorkerCount);

        var revisions = new List<long>();
        monitor.Changed += (_, change) => revisions.Add(change.Revision);
        releaseLoad.TrySetResult(new BridgeCatalogLoadResult(combined, []));
        var changed = await client.ReadEventAsync(BridgeMessageTypes.CatalogChanged);
        Assert.Equal(1L, changed.Payload.GetProperty("revision").GetInt64());
        var listedAfterValidation = await client.RequestAsync(
            BridgeMessageTypes.ListWidgets, new { });
        Assert.SequenceEqual([.. trustedIds, "dev.example.delayed"], listedAfterValidation.Payload
            .GetProperty("widgets").EnumerateArray()
            .Select(widget => widget.GetProperty("id").GetString()!));
        Assert.SequenceEqual([1L], revisions);
        Assert.True(!monitor.DiagnosticsSnapshot().InstalledCatalogPending,
            "Successful installed validation did not close pending authority.");
        Assert.Equal(0, server.RunningWorkerCount);
    }
    finally
    {
        releaseLoad.TrySetCanceled();
        try { await client.RequestAsync(BridgeMessageTypes.Stop, new { }); }
        catch { }
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }
}

static async Task CatalogReloadAuthorityIsLatestWinsAndCancellable()
{
    using var trustedFiles = TemporaryCatalog.Create(name: "Trusted");
    using var firstFiles = TemporaryCatalog.Create(name: "Superseded");
    using var latestFiles = TemporaryCatalog.Create(name: "Latest");
    using var installedRoot = new TemporaryDirectory("wrail-bridge-latest-catalog");
    var trusted = BridgeCatalog.LoadTrusted(trustedFiles.Path, installedRoot.Path);
    var superseded = BridgeCatalog.Load(firstFiles.Path);
    var latest = BridgeCatalog.Load(latestFiles.Path);
    var starts = new[]
    {
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    var completions = new[]
    {
        new TaskCompletionSource<BridgeCatalogLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously),
        new TaskCompletionSource<BridgeCatalogLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously),
        new TaskCompletionSource<BridgeCatalogLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    var invocation = -1;
    await using var monitor = new BridgeCatalogMonitor(
        trustedFiles.Path,
        installedRoot.Path,
        Environment.ProcessPath!,
        trusted,
        initialDiagnostics: null,
        installedCatalogPending: true,
        loadCatalog: async cancellationToken =>
        {
            var index = Interlocked.Increment(ref invocation);
            starts[index].TrySetResult(true);
            return await completions[index].Task.WaitAsync(cancellationToken);
        });

    var first = monitor.ReloadNowAsync();
    await starts[0].Task.WaitAsync(TimeSpan.FromSeconds(3));
    var second = monitor.ReloadNowAsync();
    completions[0].TrySetResult(new BridgeCatalogLoadResult(superseded, []));
    var stale = await first.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.False(stale.Published, "A superseded catalog result was published.");
    Assert.Equal(0L, stale.Revision);
    Assert.Equal("Trusted", stale.Current.Widgets.Single().Name);

    await starts[1].Task.WaitAsync(TimeSpan.FromSeconds(3));
    completions[1].TrySetResult(new BridgeCatalogLoadResult(latest, []));
    var accepted = await second.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.True(accepted.Published, "The latest catalog result was not published.");
    Assert.Equal(1L, accepted.Revision);
    Assert.Equal("Latest", accepted.Current.Widgets.Single().Name);

    using var canceled = new CancellationTokenSource();
    var canceledReload = monitor.ReloadNowAsync(canceled.Token);
    await starts[2].Task.WaitAsync(TimeSpan.FromSeconds(3));
    canceled.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        await canceledReload.WaitAsync(TimeSpan.FromSeconds(3)));
    Assert.Equal(1L, monitor.Revision);
    Assert.Equal("Latest", monitor.Current.Widgets.Single().Name);

    var disposalStarted = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var disposalCanceled = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var disposalMonitor = new BridgeCatalogMonitor(
        trustedFiles.Path,
        installedRoot.Path,
        Environment.ProcessPath!,
        trusted,
        initialDiagnostics: null,
        installedCatalogPending: true,
        loadCatalog: async cancellationToken =>
        {
            disposalStarted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Canceled catalog load continued.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                disposalCanceled.TrySetResult(true);
                throw;
            }
        });
    disposalMonitor.Start();
    await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
    await disposalMonitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    await disposalCanceled.Task.WaitAsync(TimeSpan.FromSeconds(3));

    await using var rejectedMonitor = new BridgeCatalogMonitor(
        trustedFiles.Path,
        installedRoot.Path,
        Environment.ProcessPath!,
        latest,
        initialDiagnostics: null,
        installedCatalogPending: true,
        loadCatalog: _ => Task.FromResult(new BridgeCatalogLoadResult(
            trusted,
            ["Rejected installed validation."],
            InstalledCatalogValid: false)));
    var rejected = await rejectedMonitor.ReloadNowAsync().WaitAsync(TimeSpan.FromSeconds(3));
    Assert.False(rejected.RetainedLastGood,
        "Rejected installed validation retained rejected installed authority.");
    Assert.True(rejected.Published,
        "Rejected installed validation did not publish trusted-only authority.");
    Assert.Equal("Trusted", rejected.Current.Widgets.Single().Name);
    Assert.True(!rejectedMonitor.DiagnosticsSnapshot().InstalledCatalogPending,
        "Rejected installed validation remained incorrectly pending after its terminal result.");

    var queuedStarts = new[]
    {
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    var queuedCompletions = new[]
    {
        new TaskCompletionSource<BridgeCatalogLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously),
        new TaskCompletionSource<BridgeCatalogLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    var queuedInvocation = -1;
    await using var queuedCancellationMonitor = new BridgeCatalogMonitor(
        trustedFiles.Path,
        installedRoot.Path,
        Environment.ProcessPath!,
        trusted,
        initialDiagnostics: null,
        installedCatalogPending: true,
        loadCatalog: async cancellationToken =>
        {
            var index = Interlocked.Increment(ref queuedInvocation);
            queuedStarts[index].TrySetResult(true);
            return await queuedCompletions[index].Task.WaitAsync(cancellationToken);
        });
    var cancellationSuccessor = new TaskCompletionSource<BridgeCatalogChanged>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    queuedCancellationMonitor.Changed += (_, change) =>
        cancellationSuccessor.TrySetResult(change);
    queuedCancellationMonitor.Start();
    await queuedStarts[0].Task.WaitAsync(TimeSpan.FromSeconds(3));
    using var preCanceled = new CancellationTokenSource();
    preCanceled.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        await queuedCancellationMonitor.ReloadNowAsync(preCanceled.Token));
    using var queuedCancellation = new CancellationTokenSource();
    var queued = queuedCancellationMonitor.ReloadNowAsync(queuedCancellation.Token);
    queuedCancellation.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        await queued.WaitAsync(TimeSpan.FromSeconds(3)));
    queuedCompletions[0].TrySetResult(new BridgeCatalogLoadResult(latest, []));
    await queuedStarts[1].Task.WaitAsync(TimeSpan.FromSeconds(3));
    queuedCompletions[1].TrySetResult(new BridgeCatalogLoadResult(latest, []));
    var recoveredCancellation = await cancellationSuccessor.Task.WaitAsync(
        TimeSpan.FromSeconds(3));
    Assert.Equal(1L, recoveredCancellation.Revision);
    Assert.Equal("Latest", recoveredCancellation.Catalog.Widgets.Single().Name);
    Assert.Equal(1, Volatile.Read(ref queuedInvocation));

    using var boundaryReached = new ManualResetEventSlim();
    using var releaseBoundary = new ManualResetEventSlim();
    var boundaryChecks = 0;
    var boundaryStarts = new[]
    {
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    var boundaryCompletions = new[]
    {
        new TaskCompletionSource<BridgeCatalogLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously),
        new TaskCompletionSource<BridgeCatalogLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    var boundaryInvocation = -1;
    var admittedTerminalObservations = new List<BridgeInstalledCatalogObservation>();
    await using var boundaryMonitor = new BridgeCatalogMonitor(
        trustedFiles.Path,
        installedRoot.Path,
        Environment.ProcessPath!,
        trusted,
        initialDiagnostics: null,
        installedCatalogPending: false,
        loadCatalog: async cancellationToken =>
        {
            var index = Interlocked.Increment(ref boundaryInvocation);
            boundaryStarts[index].TrySetResult(true);
            return await boundaryCompletions[index].Task.WaitAsync(cancellationToken);
        },
        catalogLoadObserved: observation => admittedTerminalObservations.Add(observation),
        beforePublicationCheck: () =>
        {
            if (Interlocked.Increment(ref boundaryChecks) != 1) return;
            boundaryReached.Set();
            Assert.True(releaseBoundary.Wait(TimeSpan.FromSeconds(3)),
                "The deterministic publication boundary was not released.");
        });
    var forced = boundaryMonitor.ReloadAfterMutationAsync();
    await boundaryStarts[0].Task.WaitAsync(TimeSpan.FromSeconds(3));
    boundaryCompletions[0].TrySetResult(new BridgeCatalogLoadResult(trusted, [])
    {
        InstalledCatalogObservation = new BridgeInstalledCatalogObservation(
            Succeeded: true,
            PackageCount: 1,
            VersionCount: 1,
            FileCount: 1,
            ByteCount: 1,
            ElapsedMilliseconds: 1),
    });
    Assert.True(boundaryReached.Wait(TimeSpan.FromSeconds(3)),
        "The first reload did not reach the atomic publication boundary.");
    var coalesced = boundaryMonitor.ReloadGuaranteedForTesting(forceRevision: false);
    releaseBoundary.Set();
    var supersededAtCommit = await forced.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.False(supersededAtCommit.Published,
        "A reload published after a newer demand won the atomic boundary.");
    Assert.Equal(0, admittedTerminalObservations.Count);
    await boundaryStarts[1].Task.WaitAsync(TimeSpan.FromSeconds(3));
    boundaryCompletions[1].TrySetResult(new BridgeCatalogLoadResult(trusted, [])
    {
        InstalledCatalogObservation = new BridgeInstalledCatalogObservation(
            Succeeded: true,
            PackageCount: 2,
            VersionCount: 2,
            FileCount: 2,
            ByteCount: 2,
            ElapsedMilliseconds: 2),
    });
    var forcedSuccessor = await coalesced.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.True(forcedSuccessor.Published,
        "A watcher-equivalent successor lost the superseded mutation's forceRevision authority.");
    Assert.Equal(1L, forcedSuccessor.Revision);
    Assert.Equal(1, admittedTerminalObservations.Count);
    Assert.Equal(2, admittedTerminalObservations[0].PackageCount);
    Assert.Equal(2L, admittedTerminalObservations[0].ElapsedMilliseconds);

    using var failureBoundaryReached = new ManualResetEventSlim();
    using var releaseFailureBoundary = new ManualResetEventSlim();
    var successorLoadReached = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseSuccessorLoad = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var failureChecks = 0;
    var failureInvocation = 0;
    await using var failureMonitor = new BridgeCatalogMonitor(
        trustedFiles.Path,
        installedRoot.Path,
        Environment.ProcessPath!,
        trusted,
        initialDiagnostics: null,
        installedCatalogPending: true,
        loadCatalog: async _ =>
        {
            await Task.Yield();
            if (Interlocked.Increment(ref failureInvocation) == 1)
                throw new IOException("obsolete load");
            successorLoadReached.TrySetResult();
            await releaseSuccessorLoad.Task;
            return new BridgeCatalogLoadResult(latest, []);
        },
        beforePublicationCheck: () =>
        {
            if (Interlocked.Increment(ref failureChecks) != 1) return;
            failureBoundaryReached.Set();
            Assert.True(releaseFailureBoundary.Wait(TimeSpan.FromSeconds(3)),
                "The obsolete failure boundary was not released.");
        });
    var obsoleteFailure = failureMonitor.ReloadNowAsync();
    Assert.True(failureBoundaryReached.Wait(TimeSpan.FromSeconds(3)),
        "The obsolete failure did not reach the atomic publication boundary.");
    var correctedSuccessor = failureMonitor.ReloadGuaranteedForTesting();
    releaseFailureBoundary.Set();
    try
    {
        var staleFailure = await obsoleteFailure.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(staleFailure.Published, "An obsolete throwing load published state.");
        await successorLoadReached.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(failureMonitor.DiagnosticsSnapshot().InstalledCatalogPending,
            "An obsolete failure cleared the newer pending installed authority.");
    }
    finally
    {
        releaseSuccessorLoad.TrySetResult();
    }
    var corrected = await correctedSuccessor.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.True(corrected.Published, "The corrected successor was not published.");
    Assert.Equal("Latest", corrected.Current.Widgets.Single().Name);
    Assert.Equal(0, failureMonitor.DiagnosticsSnapshot().DiagnosticCount);
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
        Assert.False(failedClosed.RetainedLastGood,
            "Rejected installed discovery was misclassified as retained installed authority.");
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
    await WaitUntilAsync(() => harness.Server.RunningWorkerCount == 0,
        TimeSpan.FromSeconds(3));
    Assert.Equal(0, harness.Server.RunningWorkerCount);
    Assert.Equal(0, harness.Server.ResidencyBudget.ApplicationWorkers);
    Assert.Equal(0L, harness.Server.ResidencyBudget.ApplicationAdvisoryMemoryMb);
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
    string publisher = "dev.example",
    string assembly = "payload/Widget.dll",
    string type = "Example.EnabledWidget")
{
    var packagePath = await CreateWidgetPackageAsync(
        packageDirectory, id, version, styleSource, permissions, residencyPolicy, icon,
        publisher, assembly, type);
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
        ["animateWidgetSwitching", "backdropOpacity", "boldText", "contrast", "interfaceScale", "motion", "revision", "shellStyles", "textScale", "themeId", "themeVersion", "transparency", "widgetSurfaceAppearance", "widgetSurfaceAppearanceOverrides"],
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

static async Task WidgetStylesOverrideGlobalThemeDefaults()
{
    await using var harness = await BridgeHarness.StartAsync(withAppearance: true);
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Snapshot, response.Type);
    var button = response.Payload.GetProperty("renderStyles").GetProperty("button").GetProperty("base");
    Assert.Equal("#ffffff", button.GetProperty("color").GetProperty("text").GetString());
    Assert.Equal(0.8D, button.GetProperty("opacity").GetProperty("number").GetDouble());
    Assert.Equal("7px", button.GetProperty("corner-radius").GetProperty("text").GetString());
}

static async Task AppearanceReloadIsLastGood()
{
    await using var harness = await BridgeHarness.StartAsync(withAppearance: true);
    var widgetSource = WrssParser.Parse(
        "button { color: #abcdef; }", "widget.wrss");
    var widgetPackage = new WrssPackageResult(
        [widgetSource.Document], widgetSource.Diagnostics);
    var initialWidgetStyle = harness.Appearance!.Service
        .ResolveWidgetTheme("test-widget", widgetPackage)
        .Resolve(new WrssElement("button"));
    Assert.Equal("#abcdef", initialWidgetStyle.Get("color")!.Text);
    Assert.Equal("0.55", initialWidgetStyle.Get("opacity")!.Text);

    var before = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    var revision = before.Payload.GetProperty("revision").GetInt64();
    Assert.Equal(0, harness.Server.RunningWorkerCount);

    await harness.Appearance!.WriteThemeAsync("button { color: definitely-not-a-color; }");
    var invalid = await harness.Appearance.Service.ReloadNowAsync();
    Assert.False(invalid.Published, "Invalid appearance unexpectedly replaced the last-good snapshot.");
    Assert.Equal(revision, invalid.Current.Revision);
    var retained = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    Assert.Equal(revision, retained.Payload.GetProperty("revision").GetInt64());
    var retainedWidgetStyle = harness.Appearance.Service
        .ResolveWidgetTheme("test-widget", widgetPackage)
        .Resolve(new WrssElement("button"));
    Assert.Equal("#abcdef", retainedWidgetStyle.Get("color")!.Text);
    Assert.Equal("0.55", retainedWidgetStyle.Get("opacity")!.Text);

    await harness.Appearance.WriteThemeAsync(
        "title { color: #abcdef; } button { color: #13579b; opacity: 0.72; }");
    var valid = await harness.Appearance.Service.ReloadNowAsync();
    Assert.True(valid.Published, "Valid appearance reload did not publish.");
    var changed = await harness.Client.ReadEventAsync(BridgeMessageTypes.AppearanceChanged);
    Assert.Equal(valid.Current.Revision, changed.Payload.GetProperty("revision").GetInt64());
    var after = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    Assert.Equal(valid.Current.Revision, after.Payload.GetProperty("revision").GetInt64());
    Assert.Equal("#abcdef", after.Payload.GetProperty("shellStyles").GetProperty("title")
        .GetProperty("color").GetProperty("text").GetString());
    var refreshedWidgetStyle = harness.Appearance.Service
        .ResolveWidgetTheme("test-widget", widgetPackage)
        .Resolve(new WrssElement("button"));
    Assert.Equal("#abcdef", refreshedWidgetStyle.Get("color")!.Text);
    Assert.Equal("0.72", refreshedWidgetStyle.Get("opacity")!.Text);
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

static async Task SelectAuthorityCrossesServerWire()
{
    await using var harness = await BridgeHarness.StartAsync(
        instanceId: "select.instance");
    var lifecycle = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest(
            "test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var responseErrorCode = response.Type == BridgeMessageTypes.Error &&
        response.Payload.TryGetProperty("code", out var errorCode)
            ? errorCode.GetString()
            : null;
    var responseErrorMessage = response.Type == BridgeMessageTypes.Error &&
        response.Payload.TryGetProperty("message", out var errorMessage)
            ? errorMessage.GetString()
            : null;
    Assert.True(response.Type == BridgeMessageTypes.Snapshot,
        $"Expected '{BridgeMessageTypes.Snapshot}', got '{response.Type}'; " +
        $"BridgeError code='{responseErrorCode ?? "<missing>"}', " +
        $"message='{responseErrorMessage ?? "<missing>"}'.");
    var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        response.Payload.GetProperty("snapshot").GetRawText()));
    var select = FindNode(snapshot.Root, "wire.select");
    Assert.Equal(ViewNodeKind.Select, select.Kind);

    var valid = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest(
            "test-widget",
            new ControllerInputEvent(
                ControllerButton.A,
                ControllerEventPhase.Pressed,
                ControllerInputContext.OpenWidget,
                FocusedElementId: select.Id,
                ActiveInputScopeId: snapshot.ActiveInputScopeId,
                SnapshotSequence: snapshot.Sequence,
                Sequence: 1),
            ExpectedSelectOptionActionId: "wire.select.spacious"));
    Assert.Equal(BridgeMessageTypes.ControllerInputResult, valid.Type);
    Assert.True(valid.Payload.GetProperty("handled").GetBoolean(),
        "Exact Select option authority was not admitted across the server wire.");
    _ = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    var changedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var changed = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        changedResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal("Spacious", FindNode(changed.Root, select.Id).AccessibilityValue);

    var wrongPhase = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest(
            "test-widget",
            new ControllerInputEvent(
                ControllerButton.A,
                ControllerEventPhase.Repeated,
                ControllerInputContext.OpenWidget,
                FocusedElementId: select.Id,
                ActiveInputScopeId: changed.ActiveInputScopeId,
                SnapshotSequence: changed.Sequence,
                Sequence: 2),
            ExpectedSelectOptionActionId: "wire.select.compact"));
    Assert.Equal(BridgeMessageTypes.Error, wrongPhase.Type);
    var staleBinding = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest(
            "test-widget",
            new ControllerInputEvent(
                ControllerButton.A,
                ControllerEventPhase.Pressed,
                ControllerInputContext.OpenWidget,
                FocusedElementId: select.Id,
                ActiveInputScopeId: changed.ActiveInputScopeId,
                SnapshotSequence: changed.Sequence,
                Sequence: 3),
            ExpectedSelectOptionActionId: "wire.select.missing"));
    Assert.Equal(BridgeMessageTypes.Error, staleBinding.Type);
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
            ClientName = "WidgetBridge.Tests.SessionFixture",
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
        "dev.sessionfixture.full-trust");
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
            ClientName = "WidgetBridge.Tests.FullTrustSessionFixture",
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
        Publisher = "dev.sessionfixture",
        Name = "Full-trust session fixture",
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
    Assert.Equal(ProtocolConstants.CollectionResetGenerationVersion, first.ProtocolVersion);
    Assert.Equal(1L, first.Root.CollectionGeneration);
    Assert.Equal(1L, first.Root.CollectionResetGeneration);
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
            UI.Tile(
                "Long application name",
                "Ready",
                "launch",
                "library.app",
                subtitle: "Application",
                artwork: TileArtwork.FromGlyph(WidgetGlyph.Play, "Application icon")),
            UI.PosterTile(
                "Poster application",
                "Ready",
                "launch.poster",
                "library.poster",
                subtitle: "Provider",
                metadata: "Platform",
                artwork: TileArtwork.FromHttps(
                    "https://cdn.example.test/poster.jpg", "Poster artwork")),
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
    Assert.True(styles.ContainsKey("library.poster") &&
                styles.ContainsKey("library.poster.artwork") &&
                styles.ContainsKey("library.poster.scrim"),
        "Poster ActionSurface layering roles were omitted from bridge styles.");
    Assert.True(styles.ContainsKey("library.grid"),
        "Grid role was omitted from bridge styles.");
    var backgroundSnapshot = new WidgetView(
        UI.BackgroundSurface(
            UI.Button("Open", "open", "background.open"),
            "background",
            BackgroundSurfaceArtwork.FromHandle(
                new WidgetArtworkHandle("library.background"))),
        InitialFocusId: "background.open")
        .CreateSnapshot("bridge.background", 1);
    var backgroundStyles = BridgeRenderStyleResolver.Resolve(backgroundSnapshot, theme: null);
    Assert.True(backgroundStyles.ContainsKey("background") &&
                backgroundStyles.ContainsKey("background.open"),
        "BackgroundSurface and its semantic foreground roles were omitted from bridge styles.");
    var focusPresentationSnapshot = new WidgetView(
        UI.FocusPresentationSurface(
            UI.Button("Open", "open", "focus-presentation.open")
                .PresentOnFocus(UI.Stack("focus-presentation.selected",
                    UI.Text("Selected details", "focus-presentation.selected.text"))),
            UI.Stack("focus-presentation.default",
                UI.Text("Choose an item", "focus-presentation.default.text")),
            "focus-presentation.surface"),
        InitialFocusId: "focus-presentation.open")
        .CreateSnapshot("bridge.focus-presentation", 1);
    var focusPresentationStyles = BridgeRenderStyleResolver.Resolve(
        focusPresentationSnapshot, theme: null);
    foreach (var id in new[]
    {
        "focus-presentation.surface",
        "focus-presentation.open",
        "focus-presentation.selected",
        "focus-presentation.selected.text",
        "focus-presentation.default",
        "focus-presentation.default.text",
    })
        Assert.True(focusPresentationStyles.ContainsKey(id),
            $"Focus-associated presentation role '{id}' was omitted from bridge styles.");
    Assert.Equal(ProtocolConstants.BackgroundSurfaceVersion, backgroundSnapshot.ProtocolVersion);
    Assert.Equal(ProtocolConstants.FocusAssociatedPresentationVersion,
        focusPresentationSnapshot.ProtocolVersion);
    Assert.Equal(ProtocolConstants.PosterTileVersion, snapshot.ProtocolVersion);
    return Task.CompletedTask;
}

static Task TextEntryRenderRole()
{
    var snapshot = new WidgetView(
        UI.TextEntry("", "Search games", "playnite-library.search.commit", "playnite-library.search", 96),
        InitialFocusId: "playnite-library.search")
        .CreateSnapshot("bridge.text-entry", 1);

    var unthemed = BridgeRenderStyleResolver.Resolve(snapshot, theme: null);
    Assert.True(unthemed.ContainsKey("playnite-library.search"),
        "TextEntry node ID was omitted from the unthemed bridge style map.");
    Assert.Equal(0, unthemed["playnite-library.search"].Base.Count);

    var parsed = WrssParser.Parse("textEntry { color: #2468ac; }", "text-entry.wrss");
    Assert.Equal(0, parsed.Diagnostics.Count(diagnostic =>
        diagnostic.Severity == WrssDiagnosticSeverity.Error));
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, "TextEntry WRSS fixture did not compile.");
    var themed = BridgeRenderStyleResolver.Resolve(snapshot, compiled.Theme);
    Assert.Equal("#2468ac", themed["playnite-library.search"].Base["color"].Text);

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

static Task OverflowWrapRenderStyle()
{
    var snapshot = new WidgetView(UI.Text("Diagnostic", "diagnostic"))
        .CreateSnapshot("bridge.overflow-wrap", 1);
    var parsed = WrssParser.Parse(
        "text { max-lines: 8; overflow-wrap: anywhere; }", "overflow-wrap.wrss");
    Assert.Equal(0, parsed.Diagnostics.Count(diagnostic =>
        diagnostic.Severity == WrssDiagnosticSeverity.Error));
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, "Overflow-wrap WRSS fixture did not compile.");
    var styles = BridgeRenderStyleResolver.Resolve(snapshot, compiled.Theme);
    Assert.Equal("anywhere", styles["diagnostic"].Base["overflow-wrap"].Text);
    Assert.Equal(WrssValueKind.Keyword,
        styles["diagnostic"].Base["overflow-wrap"].Kind);
    return Task.CompletedTask;
}

static Task SelectRenderRole()
{
    var snapshot = new WidgetView(
        UI.Stack("settings.root",
            UI.Select(
                "Density",
                [new SelectOption("compact", "Compact", "density.compact", IsSelected: true)],
                "settings.density"),
            UI.Button("Apply", "settings.apply", "settings.apply")),
        InitialFocusId: "settings.density")
        .CreateSnapshot("bridge.select", 1);

    var unthemed = BridgeRenderStyleResolver.Resolve(snapshot, theme: null);
    Assert.True(unthemed.ContainsKey("settings.density"),
        "Select node ID was omitted from the unthemed bridge style map.");
    Assert.Equal(0, unthemed["settings.density"].Base.Count);

    var repositoryRoot = FindRepositoryRoot();
    var builtInPath = Path.Combine(
        repositoryRoot, "src", "PlatformSettings", "Themes", "builtin-default.wrss");
    var builtInParsed = WrssParser.Parse(File.ReadAllText(builtInPath), builtInPath);
    Assert.Equal(0, builtInParsed.Diagnostics.Count(diagnostic =>
        diagnostic.Severity == WrssDiagnosticSeverity.Error));
    var builtInCompiled = WrssThemeCompiler.Compile([builtInParsed.Document]);
    Assert.True(builtInCompiled.IsValid, "Built-in default Select WRSS did not compile.");
    var builtIn = BridgeRenderStyleResolver.Resolve(snapshot, builtInCompiled.Theme);
    AssertSameStyles(
        builtIn["settings.apply"].Base,
        builtIn["settings.density"].Base,
        "base");
    AssertSameStyles(
        builtIn["settings.apply"].Focused,
        builtIn["settings.density"].Focused,
        "focused");

    var parsed = WrssParser.Parse("select { color: #2468ac; }", "select.wrss");
    Assert.Equal(0, parsed.Diagnostics.Count(diagnostic =>
        diagnostic.Severity == WrssDiagnosticSeverity.Error));
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, "Select WRSS fixture did not compile.");
    var themed = BridgeRenderStyleResolver.Resolve(snapshot, compiled.Theme);
    Assert.Equal("#2468ac", themed["settings.density"].Base["color"].Text);
    Assert.Equal(ProtocolConstants.AnchoredSelectVersion, snapshot.ProtocolVersion);
    return Task.CompletedTask;

    static void AssertSameStyles(
        IReadOnlyDictionary<string, BridgeComputedStyleValue> expected,
        IReadOnlyDictionary<string, BridgeComputedStyleValue> actual,
        string state)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (property, value) in expected)
        {
            Assert.True(actual.TryGetValue(property, out var candidate),
                $"Built-in Select {state} style omitted '{property}'.");
            Assert.Equal(value, candidate);
        }
    }
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

static Task EmbeddedMediaAssetsAreProviderNeutral()
{
    foreach (var adapterName in new[] { "aurora-adapter", "cedar-adapter" })
    {
        var root = Path.Combine(Path.GetTempPath(), $"wrail-{adapterName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "media"));
        try
        {
            var htmlPath = Path.Combine(root, "media", "adapter.html");
            var audioPath = Path.Combine(root, "media", "sample.wav");
            var html = System.Text.Encoding.UTF8.GetBytes(
                $"<!doctype html><title>{adapterName}</title><audio src='sample.wav'></audio>");
            var audio = new byte[] { 0x52, 0x49, 0x46, 0x46, 4, 0, 0, 0 };
            File.WriteAllBytes(htmlPath, html);
            File.WriteAllBytes(audioPath, audio);
            var verified = new Dictionary<string, VerifiedPackageFile>(StringComparer.Ordinal)
            {
                ["media/adapter.html"] = Verified("media/adapter.html", html),
                ["media/sample.wav"] = Verified("media/sample.wav", audio),
            };
            var configured = new ConfiguredWidget
            {
                Id = adapterName,
                PackageId = $"fixture.{adapterName}",
                PublisherId = "fixture",
                Name = adapterName,
                InstanceId = $"{adapterName}.instance",
                WorkerExecutable = "fixture.exe",
                WorkerFingerprint = new string('a', 64),
                CatalogFingerprint = new string('b', 64),
                PackageRoot = root,
                VerifiedPackageFiles = verified,
            };
            var descriptor = configured.PublicDescriptor();
            var media = new EmbeddedMediaSession
            {
                Id = "primary-media",
                AccessibleName = $"{adapterName} media",
                EntryAsset = "media/adapter.html",
                SupportedPresentations =
                [
                    MediaPresentationKind.OverlayFullscreen,
                    MediaPresentationKind.CompactPinned,
                ],
                Surface = new WidgetSurfaceHints
                {
                    PreferredWidth = 760,
                    PreferredHeight = 425,
                    MinimumWidth = 320,
                    MinimumHeight = 180,
                },
                AspectRatio = 16.0 / 9.0,
                Resources =
                [
                    new() { Path = "media/adapter.html", ContentType = "text/html" },
                    new() { Path = "media/sample.wav", ContentType = "audio/wav" },
                ],
                Commands =
                [
                    EmbeddedMediaCommand.Previous,
                    EmbeddedMediaCommand.Next,
                    EmbeddedMediaCommand.Activate,
                    EmbeddedMediaCommand.Back,
                    EmbeddedMediaCommand.TogglePlayback,
                    EmbeddedMediaCommand.SeekBackward,
                    EmbeddedMediaCommand.SeekForward,
                ],
                AllowedFrameOrigins = [$"https://{adapterName}.invalid"],
                AllowedFrameDomainFamilies = ["example.com"],
                PendingCommand = new()
                {
                    Sequence = 9,
                    Kind = EmbeddedMediaPlaybackCommandKind.Cue,
                    MediaKey = $"{adapterName}.tone",
                },
            };
            var snapshot = new ViewSnapshot
            {
                ProtocolVersion = ProtocolConstants.EmbeddedMediaSessionVersion,
                Sequence = 7,
                WidgetInstanceId = configured.InstanceId,
                ActiveInputScopeId = "root",
                EmbeddedMediaSession = media,
                Root = new ViewNode
                {
                    Id = "root",
                    Kind = ViewNodeKind.Stack,
                    Children = [],
                },
            };
            Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
            var wire = SnapshotJson.Serialize(snapshot);
            Assert.True(wire.Length > 0,
                "MediaViewport snapshot must serialize through the production wire path.");
            var request = new BridgeEmbeddedMediaRequest(
                configured.Id, configured.InstanceId, descriptor.RuntimeGeneration,
                descriptor.PresentationGeneration, snapshot.Sequence, media.Id);
            var bundle = EmbeddedMediaAssetResolver.Resolve(
                request, new BridgeClientSnapshot(configured, snapshot));
            Assert.Equal(adapterName, bundle.WidgetId);
            Assert.Equal(media.Id, bundle.SessionId);
            Assert.SequenceEqual(media.SupportedPresentations,
                bundle.SupportedPresentations);
            Assert.Equal(2, bundle.Resources.Count);
            Assert.Equal($"{adapterName}.tone", bundle.PendingCommand?.MediaKey);
            Assert.SequenceEqual(
                new[] { $"https://{adapterName}.invalid" },
                bundle.AllowedFrameOrigins);
            Assert.SequenceEqual(new[] { "example.com" },
                bundle.AllowedFrameDomainFamilies);
            Assert.SequenceEqual(html, Convert.FromBase64String(bundle.Resources[0].ContentBase64));
            Assert.SequenceEqual(audio, Convert.FromBase64String(bundle.Resources[1].ContentBase64));
            var bundleWire = BridgeJson.ToElement(bundle);
            var commands = bundleWire.GetProperty("commands").EnumerateArray()
                .Select(command => command.GetString()).ToArray();
            Assert.SequenceEqual(
                new[]
                {
                    "navigatePrevious", "navigateNext", "activate", "back",
                    "togglePlayback", "seekBackward", "seekForward",
                },
                commands);

            File.WriteAllText(htmlPath, "tampered");
            Assert.Throws<BridgeProtocolException>(() => EmbeddedMediaAssetResolver.Resolve(
                request, new BridgeClientSnapshot(configured, snapshot)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static VerifiedPackageFile Verified(string path, byte[] bytes) => new(
        path,
        bytes.LongLength,
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    return Task.CompletedTask;
}

static async Task BuiltEmbeddedMediaSampleCompletesPlaybackLoop()
{
    if (!OperatingSystem.IsWindows()) return;
    var catalogPath = Path.GetFullPath(
        Path.Combine("src", "OverlayHost", "out", "Release", "widget-catalog.json"));
    var catalog = BridgeCatalog.Load(catalogPath);
    var descriptor = catalog.Widgets.Single(widget => widget.Id == "embedded-media-sample");
    var pipeName = $"wrail-embedded-media-sample-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(
        pipeName, catalog, BridgeProtocol.DefaultMaximumMessageBytes);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(5));
    await using var client = await BridgeTestClient.ConnectAsync(
        pipeName, BridgeProtocol.DefaultMaximumMessageBytes);
    try
    {
        var lifecycle = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest(
                descriptor.Id, WidgetLifecycleState.Visible));
        Assert.Equal(BridgeMessageTypes.Acknowledged, lifecycle.Type);
        var initial = await SampleSnapshotAsync(client, descriptor.Id);
        var media = initial.EmbeddedMediaSession
            ?? throw new InvalidOperationException("Built sample omitted embedded media.");
        Assert.Equal(
            ProtocolConstants.EmbeddedMediaSessionVersion,
            initial.ProtocolVersion);
        Assert.True(media.SupportedPresentations.Contains(
                MediaPresentationKind.CompactPinned),
            "Built sample omitted compact pinned media presentation.");
        Assert.Equal<double?>(2D, media.MediaSeekStepSeconds);
        Assert.True(media.SupportedPresentations.Contains(
                MediaPresentationKind.OverlayFullscreen),
            "Built sample omitted overlay fullscreen presentation.");
        Assert.SequenceEqual(
            new[]
            {
                EmbeddedMediaCommand.Activate,
                EmbeddedMediaCommand.TogglePlayback,
                EmbeddedMediaCommand.Previous,
                EmbeddedMediaCommand.Next,
                EmbeddedMediaCommand.SeekBackward,
                EmbeddedMediaCommand.SeekForward,
            },
            media.Commands);
        Assert.True(Flatten(initial.Root).Any(node =>
                node.Id == "media-shell.fullscreen" &&
                node.ActionId == "host.embeddedMediaSession.enterFullscreen"),
            "Built sample omitted its exact fullscreen action declaration.");
        Assert.Equal<EmbeddedMediaPlaybackCommand?>(null, media.PendingCommand);

        var resolved = await client.RequestAsync(
            BridgeMessageTypes.ResolveEmbeddedMedia,
            new BridgeEmbeddedMediaRequest(
                descriptor.Id, initial.WidgetInstanceId,
                descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
                initial.Sequence, media.Id));
        Assert.Equal(BridgeMessageTypes.EmbeddedMediaSession, resolved.Type);
        var bundle = BridgeJson.FromElement<BridgeEmbeddedMediaBundle>(resolved.Payload);
        Assert.True(bundle.Resources.Any(resource =>
                resource.Path == "payload/media/adapter.html" &&
                Convert.FromBase64String(resource.ContentBase64).Length > 0),
            "Built sample did not resolve its exact sealed adapter bytes.");
        Assert.True(bundle.Resources.Any(resource =>
                resource.Path == "payload/media/sample.mp4" &&
                resource.ContentType == "video/mp4" &&
                Convert.FromBase64String(resource.ContentBase64).Length > 0),
            "Built sample did not resolve its exact sealed video/audio asset bytes.");
        Assert.True(bundle.Resources.Any(resource =>
                resource.Path == "payload/media/horizon.mp4" &&
                resource.ContentType == "video/mp4" &&
                Convert.FromBase64String(resource.ContentBase64).Length > 0),
            "Built sample did not resolve its alternate sealed video/audio asset bytes.");
        var adapter = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
            bundle.Resources.Single(resource =>
                resource.Path == "payload/media/adapter.html").ContentBase64));
        static void AssertSpatialNavigationContract(string content, string owner)
        {
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(
                    content,
                    @"previous\s*\(\s*command\s*\)\s*\{\s*return\s+navigate\s*\(\s*-1\s*,\s*command\.signal\s*\)\s*;\s*\}",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant),
                $"{owner} omitted the exact previous(command) navigation route.");
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(
                    content,
                    @"next\s*\(\s*command\s*\)\s*\{\s*return\s+navigate\s*\(\s*1\s*,\s*command\.signal\s*\)\s*;\s*\}",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant),
                $"{owner} omitted the exact next(command) navigation route.");
        }
        AssertSpatialNavigationContract(
            File.ReadAllText(Path.Combine(
                "samples", "EmbeddedMediaWidget", "media", "adapter.html")),
            "Built sample adapter source");
        AssertSpatialNavigationContract(adapter, "Sealed built sample adapter");

        var eventSequence = 0L;
        async Task<(ViewSnapshot Snapshot, EmbeddedMediaPlaybackCommand Command)>
            CommandAsync(string actionId, string sourceElementId)
        {
            _ = await client.RequestAsync(
                BridgeMessageTypes.Action,
                new BridgeActionRequest(descriptor.Id, new WidgetActionEvent(
                    actionId, sourceElementId)));
            _ = await client.ReadEventAsync(BridgeMessageTypes.Invalidation);
            var snapshot = await SampleSnapshotAsync(client, descriptor.Id);
            var command = snapshot.EmbeddedMediaSession?.PendingCommand
                ?? throw new InvalidOperationException(
                    $"{actionId} did not publish a typed command.");
            return (snapshot, command);
        }

        async Task<ViewSnapshot> AcknowledgeAsync(
            ViewSnapshot snapshot,
            EmbeddedMediaPlaybackCommand command,
            EmbeddedMediaPlaybackState state,
            double position)
        {
            var response = await client.RequestAsync(
                BridgeMessageTypes.EmbeddedMediaPlaybackEvent,
                new BridgeEmbeddedMediaPlaybackEventRequest(
                    descriptor.Id, snapshot.WidgetInstanceId,
                    descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
                    snapshot.Sequence,
                    new EmbeddedMediaPlaybackEvent
                    {
                        SessionId = snapshot.EmbeddedMediaSession!.Id,
                        Sequence = ++eventSequence,
                        CommandSequence = command.Sequence,
                        MediaKey = command.MediaKey,
                        State = state,
                        PositionSeconds = position,
                        DurationSeconds = 60,
                        Volume = 0.8,
                    }));
            Assert.Equal(BridgeMessageTypes.Acknowledged, response.Type);
            _ = await client.ReadEventAsync(BridgeMessageTypes.Invalidation);
            var acknowledged = await SampleSnapshotAsync(client, descriptor.Id);
            Assert.Equal<EmbeddedMediaPlaybackCommand?>(
                null, acknowledged.EmbeddedMediaSession?.PendingCommand);
            return acknowledged;
        }

        async Task<ViewSnapshot> ObserveAsync(
            ViewSnapshot snapshot,
            EmbeddedMediaPlaybackState state,
            double position)
        {
            var response = await client.RequestAsync(
                BridgeMessageTypes.EmbeddedMediaPlaybackEvent,
                new BridgeEmbeddedMediaPlaybackEventRequest(
                    descriptor.Id, snapshot.WidgetInstanceId,
                    descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
                    snapshot.Sequence,
                    new EmbeddedMediaPlaybackEvent
                    {
                        SessionId = snapshot.EmbeddedMediaSession!.Id,
                        Sequence = ++eventSequence,
                        CommandSequence = 0,
                        MediaKey = snapshot.EmbeddedMediaSession.PendingCommand!.MediaKey,
                        State = state,
                        PositionSeconds = position,
                        DurationSeconds = 60,
                        Volume = 0.8,
                    }));
            Assert.Equal(BridgeMessageTypes.Acknowledged, response.Type);
            _ = await client.ReadEventAsync(BridgeMessageTypes.Invalidation);
            return await SampleSnapshotAsync(client, descriptor.Id);
        }

        async Task<ViewSnapshot> ObserveMediaAsync(
            ViewSnapshot snapshot,
            string mediaKey,
            EmbeddedMediaPlaybackState state,
            double position)
        {
            var response = await client.RequestAsync(
                BridgeMessageTypes.EmbeddedMediaPlaybackEvent,
                new BridgeEmbeddedMediaPlaybackEventRequest(
                    descriptor.Id, snapshot.WidgetInstanceId,
                    descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
                    snapshot.Sequence,
                    new EmbeddedMediaPlaybackEvent
                    {
                        SessionId = snapshot.EmbeddedMediaSession!.Id,
                        Sequence = ++eventSequence,
                        CommandSequence = 0,
                        MediaKey = mediaKey,
                        State = state,
                        PositionSeconds = position,
                        DurationSeconds = 60,
                        Volume = 0.8,
                    }));
            Assert.Equal(BridgeMessageTypes.Acknowledged, response.Type);
            _ = await client.ReadEventAsync(BridgeMessageTypes.Invalidation);
            return await SampleSnapshotAsync(client, descriptor.Id);
        }

        var (play, playCommand) = await CommandAsync(
            "host.embeddedMediaSession.togglePlayback", "media-shell.play");
        Assert.Equal(
            ProtocolConstants.EmbeddedMediaSessionVersion,
            play.ProtocolVersion);
        Assert.Equal(EmbeddedMediaPlaybackCommandKind.Play, playCommand.Kind);
        var playing = await AcknowledgeAsync(
            play, playCommand, EmbeddedMediaPlaybackState.Playing, 8);
        Assert.Equal(8D, Flatten(playing.Root).Single(
            node => node.Id == "media-shell.timeline.slider").Value);
        var nativeNext = await ObserveMediaAsync(
            playing, "horizon-video-1", EmbeddedMediaPlaybackState.Playing, 0);
        Assert.Equal("Horizon Grid", Flatten(nativeNext.Root).Single(
            node => node.Id == "media-shell.title").Text);
        var nativePrevious = await ObserveMediaAsync(
            nativeNext, "aurora-video-0", EmbeddedMediaPlaybackState.Playing, 0);
        Assert.Equal("Aurora Signal", Flatten(nativePrevious.Root).Single(
            node => node.Id == "media-shell.title").Text);

        var (pause, pauseCommand) = await CommandAsync(
            "host.embeddedMediaSession.togglePlayback", "media-shell.play");
        Assert.Equal(EmbeddedMediaPlaybackCommandKind.Pause, pauseCommand.Kind);
        var paused = await AcknowledgeAsync(
            pause, pauseCommand, EmbeddedMediaPlaybackState.Paused, 8);
        var pausedNext = await ObserveMediaAsync(
            paused, "horizon-video-1", EmbeddedMediaPlaybackState.Paused, 0);
        Assert.Equal("Horizon Grid", Flatten(pausedNext.Root).Single(
            node => node.Id == "media-shell.title").Text);
        var pausedPrevious = await ObserveMediaAsync(
            pausedNext, "aurora-video-0", EmbeddedMediaPlaybackState.Paused, 0);
        Assert.Equal("Aurora Signal", Flatten(pausedPrevious.Root).Single(
            node => node.Id == "media-shell.title").Text);

        var (resume, resumeCommand) = await CommandAsync(
            "host.embeddedMediaSession.togglePlayback", "media-shell.play");
        Assert.Equal(EmbeddedMediaPlaybackCommandKind.Play, resumeCommand.Kind);
        var compatibleResume = await ObserveAsync(
            resume, EmbeddedMediaPlaybackState.Paused, 8);
        Assert.True(compatibleResume.Sequence > resume.Sequence,
            "Unsolicited progress did not advance the compatible snapshot.");
        Assert.Equal(resumeCommand, compatibleResume.EmbeddedMediaSession!.PendingCommand);
        _ = await AcknowledgeAsync(
            resume, resumeCommand, EmbeddedMediaPlaybackState.Playing, 8);

        var (seekForward, seekForwardCommand) = await CommandAsync(
            "host.embeddedMediaSession.seekForward", "media-shell.seek-forward");
        Assert.Equal(EmbeddedMediaPlaybackCommandKind.Seek, seekForwardCommand.Kind);
        Assert.Equal(10D, seekForwardCommand.PositionSeconds);
        _ = await AcknowledgeAsync(
            seekForward, seekForwardCommand, EmbeddedMediaPlaybackState.Playing, 10);

        var (next, nextCommand) = await CommandAsync(
            "host.embeddedMediaSession.next", "media-shell.next");
        Assert.Equal(EmbeddedMediaPlaybackCommandKind.Load, nextCommand.Kind);
        Assert.True(!string.Equals(
            playCommand.MediaKey, nextCommand.MediaKey, StringComparison.Ordinal),
            "Next did not publish a distinct provider-neutral media key.");
        var nextReady = await AcknowledgeAsync(
            next, nextCommand, EmbeddedMediaPlaybackState.Ready, 0);
        Assert.True(Flatten(nextReady.Root).Single(
            node => node.Id == "media-shell.title").Text!.Contains(
                "Horizon Grid", StringComparison.Ordinal),
            "Next acknowledgement did not project the current native scene title.");

        var (previous, previousCommand) = await CommandAsync(
            "host.embeddedMediaSession.previous", "media-shell.previous");
        Assert.Equal(EmbeddedMediaPlaybackCommandKind.Load, previousCommand.Kind);
        Assert.Equal(playCommand.MediaKey, previousCommand.MediaKey);
        _ = await AcknowledgeAsync(
            previous, previousCommand, EmbeddedMediaPlaybackState.Ready, 0);

        var (finalSeek, finalSeekCommand) = await CommandAsync(
            "host.embeddedMediaSession.seekForward", "media-shell.seek-forward");
        Assert.Equal(EmbeddedMediaPlaybackCommandKind.Seek, finalSeekCommand.Kind);
        Assert.True(finalSeekCommand.Sequence > previousCommand.Sequence &&
            previousCommand.Sequence > nextCommand.Sequence &&
            nextCommand.Sequence > seekForwardCommand.Sequence &&
            seekForwardCommand.Sequence > resumeCommand.Sequence &&
            resumeCommand.Sequence > pauseCommand.Sequence &&
            pauseCommand.Sequence > playCommand.Sequence,
            "Typed playback command correlation did not advance monotonically.");
        _ = await AcknowledgeAsync(
            finalSeek, finalSeekCommand, EmbeddedMediaPlaybackState.Paused, 10);
    }
    finally
    {
        if (!client.IsTerminal)
            _ = await client.RequestAsync(BridgeMessageTypes.Stop, new { });
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

static async Task<ViewSnapshot> SampleSnapshotAsync(
    BridgeTestClient client,
    string widgetId)
{
    var response = await client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest(widgetId));
    Assert.Equal(BridgeMessageTypes.Snapshot, response.Type);
    return SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        response.Payload.GetProperty("snapshot").GetRawText()));
}

file sealed class BridgeTestWidget : Widget
{
    private double _volume = 0.5;
    private string _actionOrder = "none";
    private string _committedTextStatus = "committed:none";
    private int _committedTextDiagnosticState;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, string>
        _inputOrigins = new();
    private readonly bool _artworkFixture;
    private readonly bool _oversizedFixture;
    private readonly bool _sensitiveTextEntryFixture;
    private readonly bool _validatorHomeFixture;
    private readonly bool _validatorHiddenFixture;
    private readonly bool _validatorReturnFixture;
    private readonly bool _validatorActionFixture;
    private readonly bool _validatorContextFixture;
    private readonly bool _validatorDisabledFixture;
    private readonly bool _validatorMaximumFixture;
    private readonly bool _selectFixture;
    private bool _validatorFailure;
    private bool _oversized;
    private string _selectedDensity = "wire.select.compact";
    private WidgetAppLibraryItem? _artworkItem;

    internal BridgeTestWidget(string instanceId)
    {
        _artworkFixture = string.Equals(
            instanceId, "artwork.instance", StringComparison.Ordinal);
        _oversizedFixture = string.Equals(
            instanceId, "oversized.instance", StringComparison.Ordinal);
        _sensitiveTextEntryFixture = string.Equals(
            instanceId, "sensitive-text-entry.instance", StringComparison.Ordinal);
        _validatorHomeFixture = string.Equals(
            instanceId, "validator-home.instance", StringComparison.Ordinal);
        _validatorHiddenFixture = string.Equals(
            instanceId, "validator-hidden.instance", StringComparison.Ordinal);
        _validatorReturnFixture = string.Equals(
            instanceId, "validator-return.instance", StringComparison.Ordinal);
        _validatorActionFixture = string.Equals(
            instanceId, "validator-action.instance", StringComparison.Ordinal);
        _validatorContextFixture = string.Equals(
            instanceId, "validator-context.instance", StringComparison.Ordinal);
        _validatorDisabledFixture = string.Equals(
            instanceId, "validator-disabled.instance", StringComparison.Ordinal);
        _validatorMaximumFixture = string.Equals(
            instanceId, "validator-maximum.instance", StringComparison.Ordinal);
        _selectFixture = string.Equals(
            instanceId, "select.instance", StringComparison.Ordinal);
    }

    public override WidgetView Render()
    {
        if (_selectFixture)
        {
            var options = new[]
            {
                new SelectOption(
                    "compact", "Compact", "wire.select.compact",
                    IsSelected: _selectedDensity == "wire.select.compact"),
                new SelectOption(
                    "spacious", "Spacious", "wire.select.spacious",
                    IsSelected: _selectedDensity == "wire.select.spacious"),
            };
            return new WidgetView(
                UI.Select("Density", options, "wire.select", "Density"),
                InitialFocusId: "wire.select");
        }
        if (_validatorFailure)
        {
            if (_validatorHomeFixture)
                return new WidgetView(
                    UI.Stack("root"),
                    ActiveInputScopeId: "validator.missing.scope");
            if (_validatorHiddenFixture)
                return new WidgetView(
                    UI.Stack("root"),
                    InitialFocusId: "validator.missing.focus");
            if (_validatorReturnFixture)
                return new WidgetView(new RawProtocolElement(new ViewNode
                {
                    Id = "root",
                    Kind = ViewNodeKind.Stack,
                    InitialChildFocusId = "validator.missing.return",
                    Children =
                    [
                        new ViewNode
                        {
                            Id = "validator.return.available",
                            Kind = ViewNodeKind.Button,
                            Text = "Available",
                            ActionId = "validator.return.available",
                        },
                    ],
                }));
            if (_validatorActionFixture)
                return new WidgetView(new RawProtocolElement(new ViewNode
                {
                    Id = "root",
                    Kind = ViewNodeKind.Stack,
                    Children =
                    [
                        new ViewNode
                        {
                            Id = "validator.action.text",
                            Kind = ViewNodeKind.Text,
                            Text = "Text",
                            ActionId = "validator.unknown.action",
                        },
                    ],
                }));
            if (_validatorContextFixture)
                return new WidgetView(new RawProtocolElement(new ViewNode
                {
                    Id = "root",
                    Kind = ViewNodeKind.Stack,
                    Children =
                    [
                        new ViewNode
                        {
                            Id = "validator.context.surface",
                            Kind = ViewNodeKind.ActionSurface,
                            ActionId = "validator.context.open",
                            AccessibilityLabel = "Context surface",
                            ActionSurfaceOrientation = ActionSurfaceOrientation.Vertical,
                            ContextActions =
                            [
                                new("validator.context.more", "More"),
                                new("validator.context.more", "More again"),
                            ],
                            Children =
                            [
                                new ViewNode
                                {
                                    Id = "validator.context.text",
                                    Kind = ViewNodeKind.Text,
                                    Text = "Context",
                                },
                            ],
                        },
                    ],
                }));
            if (_validatorDisabledFixture)
                throw SyntheticValidationFailure(
                    "$.initialFocusId", "invalid_focus_target",
                    ProtocolValidationIdentifierKind.InitialFocus,
                    ProtocolValidationIdentifierState.Disabled,
                    "validator.disabled.focus");
            if (_validatorMaximumFixture)
                throw SyntheticValidationFailure(
                    "$" + string.Concat(Enumerable.Repeat(".a", 126)) + ".aa",
                    new string('a', 64),
                    ProtocolValidationIdentifierKind.ElementReference,
                    ProtocolValidationIdentifierState.OutsideActiveScope,
                    new string('i', 128));
        }
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
        if (_validatorHomeFixture)
            children.Add(UI.Button(
                "Details", "validator.home.details", "validator.home.details"));
        if (_validatorHiddenFixture)
            children.Add(UI.Button(
                "Back", "validator.hidden.back", "validator.hidden.back"));
        if (_validatorReturnFixture)
            children.Add(UI.Button(
                "Return", "validator.return.trigger", "validator.return.trigger"));
        if (_validatorActionFixture)
            children.Add(UI.Button(
                "Action", "validator.action.trigger", "validator.action.trigger"));
        if (_validatorContextFixture)
            children.Add(UI.Button(
                "Context", "validator.context.trigger", "validator.context.trigger"));
        if (_validatorDisabledFixture)
            children.Add(UI.Button(
                "Disabled", "validator.disabled.trigger", "validator.disabled.trigger"));
        if (_validatorMaximumFixture)
            children.Add(UI.Button(
                "Maximum", "validator.maximum.trigger", "validator.maximum.trigger"));
        if (_sensitiveTextEntryFixture)
            children.Add(UI.SensitiveTextEntry(
                "Enter provider-neutral secret", "committed-text", "credential.entry", 64));
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
        else if ((_validatorHomeFixture &&
                  action.ActionId == "validator.home.details") ||
                 (_validatorHiddenFixture &&
                  action.ActionId == "validator.hidden.back") ||
                 (_validatorReturnFixture &&
                  action.ActionId == "validator.return.trigger") ||
                 (_validatorActionFixture &&
                  action.ActionId == "validator.action.trigger") ||
                 (_validatorContextFixture &&
                  action.ActionId == "validator.context.trigger") ||
                 (_validatorDisabledFixture &&
                  action.ActionId == "validator.disabled.trigger") ||
                 (_validatorMaximumFixture &&
                  action.ActionId == "validator.maximum.trigger"))
        {
            _validatorFailure = true;
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
        else if (_selectFixture && action.ActionId is
            ("wire.select.compact" or "wire.select.spacious"))
        {
            _selectedDensity = action.ActionId;
            Invalidate();
        }
        else if (action.ActionId == "committed-text")
        {
            _committedTextStatus = action.CommittedText is { } committed
                ? $"committed:{committed.Length}:" +
                  (Volatile.Read(ref _committedTextDiagnosticState) == -1
                      ? "diagnostic-leaked"
                      : "diagnostic-redacted")
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

    protected override void OnActionDiagnostic(
        WidgetActionEvent action,
        string stage,
        string code)
    {
        _ = stage;
        _ = code;
        if (action.ActionId != "committed-text") return;
        if (action.CommittedText is null)
            Interlocked.CompareExchange(ref _committedTextDiagnosticState, 1, 0);
        else
            Interlocked.Exchange(ref _committedTextDiagnosticState, -1);
    }

    private static ProtocolValidationException SyntheticValidationFailure(
        string path,
        string code,
        ProtocolValidationIdentifierKind kind,
        ProtocolValidationIdentifierState state,
        string identifier) => new(
        [
            new ProtocolValidationError(path, code, "PRIVATE_VALIDATION_MESSAGE")
            {
                IdentifierContext = ProtocolValidationIdentifierContext.Create(
                    kind, state, identifier),
            },
        ]);
}

file sealed record RawProtocolElement(ViewNode Node) : WidgetElement(Node.Id)
{
    internal override ViewNode ToProtocolNode() => Node;
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
        var capture = _items.Capture();
        var snapshot = capture.Snapshot;
        var rows = snapshot.Items.Select(item => capture.PresentItem(item,
            UI.Button($"Item {item.Index}", "virtual.open", $"virtual.item.{item.Index}")))
            .ToArray();
        var scroll = capture.Present(UI.VerticalScroll("virtual.scroll", rows));
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

    public static TemporaryCatalog CreateManyInvalidStyles(
        params TemporaryWidgetDefinition[] widgets) =>
        CreateCore(widgets, addUnknownProperty: false, invalidStyle: true, styleSource: null);

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

file sealed record TemporaryBundledWidgetDefinition(
    string Id,
    string Name,
    bool InvalidStyle,
    string? PackageId = null,
    bool PackageIcon = false,
    string PackageIconPathData = "M2 12 L12 2 L22 12 L12 22 Z",
    bool MixedPackageIcons = false,
    bool OverAggregatePackageIcons = false);

file sealed class TemporaryBundledCatalog : IDisposable
{
    private readonly Dictionary<string, string> _packageRoots;
    public string Root { get; }
    public string Path { get; }
    public string WorkerPath { get; }

    private TemporaryBundledCatalog(
        string root,
        string path,
        string workerPath,
        Dictionary<string, string> packageRoots)
    {
        Root = root;
        Path = path;
        WorkerPath = workerPath;
        _packageRoots = packageRoots;
    }

    public static TemporaryBundledCatalog Create(
        params TemporaryBundledWidgetDefinition[] widgets)
    {
        if (widgets.Length == 0)
            throw new ArgumentException("At least one bundled widget is required.", nameof(widgets));
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"wrail-bundled-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var workerPath = System.IO.Path.Combine(root, "WidgetWorkerHost.exe");
        File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException(
            "Test process path is unavailable."), workerPath);
        var packageRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        var definitions = new List<object>();
        for (var index = 0; index < widgets.Length; index++)
        {
            var widget = widgets[index];
            var packageId = widget.PackageId ?? widget.Id;
            var packageRoot = System.IO.Path.Combine(root, "packages", $"package-{index + 1}");
            Directory.CreateDirectory(System.IO.Path.Combine(packageRoot, "payload"));
            Directory.CreateDirectory(System.IO.Path.Combine(packageRoot, "styles"));
            File.WriteAllBytes(
                System.IO.Path.Combine(packageRoot, "payload", "FixtureWidget.dll"),
                [0x57, 0x52, 0x41, 0x49, 0x4c]);
            File.WriteAllText(
                System.IO.Path.Combine(packageRoot, "styles", "default.wrss"),
                widget.InvalidStyle
                    ? "button { background: url(https://example.test/rejected.png); }"
                    : "button { color: #ffffff; }");
            if (widget.PackageIcon)
            {
                Directory.CreateDirectory(System.IO.Path.Combine(packageRoot, "assets"));
                File.WriteAllText(
                    System.IO.Path.Combine(packageRoot, "assets", "mark.svg"),
                    $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path fill=\"currentColor\" d=\"{widget.PackageIconPathData}\"/></svg>",
                    new System.Text.UTF8Encoding(false));
                if (widget.MixedPackageIcons)
                    File.WriteAllText(
                        System.IO.Path.Combine(packageRoot, "assets", "unavailable.svg"),
                        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><script/></svg>",
                        new System.Text.UTF8Encoding(false));
                if (widget.OverAggregatePackageIcons)
                    for (var assetIndex = 0; assetIndex < 9; assetIndex++)
                        File.WriteAllBytes(
                            System.IO.Path.Combine(
                                packageRoot, "assets", $"aggregate-{assetIndex}.svg"),
                            new byte[60 * 1024]);
            }
            object presentation = widget.PackageIcon
                ? new
                {
                    icon = "connection",
                    packageIcon = new { assetId = "test.mark", colorMode = "themeTint" },
                }
                : new { icon = "connection" };
            var iconAssets = new Dictionary<string, object>(StringComparer.Ordinal);
            if (widget.PackageIcon)
                iconAssets["test.mark"] = new { path = "assets/mark.svg" };
            if (widget.MixedPackageIcons)
                iconAssets["test.unavailable"] = new
                {
                    path = "assets/unavailable.svg",
                };
            if (widget.OverAggregatePackageIcons)
                for (var assetIndex = 0; assetIndex < 9; assetIndex++)
                    iconAssets[$"test.aggregate-{assetIndex}"] = new
                    {
                        path = $"assets/aggregate-{assetIndex}.svg",
                    };
            var manifest = JsonSerializer.Serialize(new
            {
                manifestVersion = 1,
                id = packageId,
                publisher = "dev.test",
                name = widget.Name,
                version = "1.0.0",
                hostApi = new { minimum = "1.0", maximumMajor = 1 },
                entrypoint = new
                {
                    runtime = "dotnet-worker",
                    assembly = "payload/FixtureWidget.dll",
                    type = "WidgetRail.Tests.FixtureWidget",
                },
                presentation,
                iconAssets,
                permissions = Array.Empty<string>(),
                optionalPermissions = Array.Empty<string>(),
                residencyPolicy = new
                {
                    schemaVersion = 1,
                    mode = "unload-after-idle",
                    idleSeconds = 120,
                },
                resourceRequest = new { memoryMb = 32, updateHz = 1 },
                architectures = new[] { "x64" },
            });
            File.WriteAllText(System.IO.Path.Combine(packageRoot, "manifest.json"), manifest);
            InstalledPackageIntegrity.Seal(root, packageRoot, new WidgetCatalogOptions());
            packageRoots[widget.Id] = packageRoot;
            definitions.Add(new
            {
                id = widget.Id,
                packageId,
                instanceId = $"{widget.Id}.instance",
                packageRoot = System.IO.Path.GetRelativePath(root, packageRoot)
                    .Replace(System.IO.Path.DirectorySeparatorChar, '/'),
                icon = "connection",
                quickActions = Array.Empty<object>(),
            });
        }
        var catalogPath = System.IO.Path.Combine(root, "widget-catalog.json");
        File.WriteAllText(catalogPath, JsonSerializer.Serialize(new
        {
            catalogVersion = 1,
            genericWorkerExecutable = "WidgetWorkerHost.exe",
            widgets = Array.Empty<object>(),
            bundledWidgets = definitions,
        }));
        return new TemporaryBundledCatalog(root, catalogPath, workerPath, packageRoots);
    }

    public void CorrectStyles(params string[] widgetIds)
    {
        foreach (var widgetId in widgetIds)
        {
            var packageRoot = _packageRoots[widgetId];
            File.Delete(System.IO.Path.Combine(packageRoot, ".wrail-integrity.json"));
            File.WriteAllText(
                System.IO.Path.Combine(packageRoot, "styles", "default.wrss"),
                "button { color: #ffffff; }");
            InstalledPackageIntegrity.Seal(Root, packageRoot, new WidgetCatalogOptions());
        }
    }

    public void AddConfiguredDeclaration(string id, string packageId)
    {
        var declaration = JsonSerializer.Serialize(new
        {
            id,
            packageId,
            publisherId = "dev.test",
            name = "Configured duplicate",
            instanceId = "configured.duplicate.instance",
            workerExecutable = "WidgetWorkerHost.exe",
            workerArguments = Array.Empty<string>(),
            declaredCapabilities = Array.Empty<string>(),
            quickActions = Array.Empty<object>(),
        });
        var document = File.ReadAllText(Path);
        var updated = document.Replace(
            "\"widgets\":[]",
            $"\"widgets\":[{declaration}]",
            StringComparison.Ordinal);
        if (string.Equals(updated, document, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Bundled catalog fixture did not contain the configured-widget insertion point.");
        File.WriteAllText(Path, updated);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
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
        WidgetResidencyPolicy? residencyPolicy = null,
        string instanceId = "test.instance",
        string? workerDiagnosticRoot = null,
        Action<BridgeWidgetRequestDiagnostic>? requestDiagnosticSink = null,
        IReadOnlySet<string>? declaredPackageIconAssetIds = null,
        int maximumBytes = 64 * 1024)
    {
        var temporary = TemporaryCatalog.Create(
            instanceId: instanceId, residencyPolicy: residencyPolicy);
        TemporaryAppearance? appearance = null;
        try
        {
            if (withAppearance) appearance = await TemporaryAppearance.CreateAsync();
            var catalog = BridgeCatalog.Load(temporary.Path);
            if (declaredPackageIconAssetIds is not null)
                catalog = new BridgeCatalog(catalog.Widgets.Select(widget =>
                    catalog.GetConfigured(widget.Id) with
                    {
                        DeclaredPackageIconAssetIds = declaredPackageIconAssetIds,
                    }));
            var pipeName = $"wrail-bridge-test-{Guid.NewGuid():N}";
            var server = new WidgetBridgeServer(
                pipeName,
                catalog,
                maximumBytes,
                appearance?.Service,
                consentStore: null,
                platformBackend: null,
                catalogMonitor: null,
                residencyBudget: null,
                capabilityDiagnosticSink: null,
                lifetimeDiagnosticSink: null,
                requestDiagnosticSink: requestDiagnosticSink,
                workerDiagnosticRoot: workerDiagnosticRoot);
            var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
            var client = await BridgeTestClient.ConnectAsync(pipeName, maximumBytes);
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
            "button { color: #2468ac; opacity: 0.55; corner-radius: 7px; } " +
            "title { color: #fedcba; }");
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

file sealed class BridgeRegistrationBackend : IAppLibraryPlatformBrokerBackend
{
    private readonly Dictionary<string, long> _revisions =
        new(StringComparer.Ordinal);

    internal void Seed(BrokerWidgetIdentity identity)
    {
        identity.Validate();
        _revisions[Key(identity)] = 1;
    }

    internal bool FailRetirement { get; set; }

    public Task<AppLibraryRegistrationStateSummary>
        GetRunningAppRegistrationStateAsync(
            BrokerWidgetIdentity identity,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exists = _revisions.TryGetValue(Key(identity), out var revision);
        return Task.FromResult(new AppLibraryRegistrationStateSummary(
            exists, exists ? revision : 0));
    }

    public Task ClearRunningAppRegistrationsAsync(
        BrokerWidgetIdentity identity,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(identity);
        if (!_revisions.TryGetValue(key, out var revision) ||
            revision != expectedRevision)
            throw new BrokerException(
                "app_registration_conflict", "Synthetic registration conflict.");
        _revisions.Remove(key);
        return Task.CompletedTask;
    }

    public Task<AppLibraryPackageRegistrationRetirementSummary>
        RetireRunningAppPackageRegistrationsAsync(
            string packageId,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailRetirement)
            throw new BrokerException(
                "registration_store_unavailable",
                "Synthetic registration cleanup failure.");
        var suffix = "\0" + packageId;
        foreach (var key in _revisions.Keys.Where(key => key.EndsWith(
                     suffix, StringComparison.Ordinal)).ToArray())
            _revisions.Remove(key);
        return Task.FromResult(
            new AppLibraryPackageRegistrationRetirementSummary(true, false));
    }

    private static string Key(BrokerWidgetIdentity identity) =>
        identity.PublisherId + "\0" + identity.PackageId;
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
