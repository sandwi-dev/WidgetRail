using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.Samples.ClockWidget;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Snapshot serialization is deterministic and round-trips", SnapshotRoundTrip),
    ("SDK snapshots use the shared maximum protocol requirement",
        ProtocolVersionRequirementsTests.SdkSnapshotsUseSharedMaximum),
    ("Raw snapshots enforce the complete shared protocol requirement matrix",
        ProtocolVersionRequirementsTests.RawSnapshotsUseCompleteRequirementMatrix),
    ("Pinned presentation layouts preserve additive bounded v20 contracts",
        PinnedPresentationLayoutTests.Run),
    ("Automatic presentation updates are atomic bounded and fallback-safe", WidgetPresentationUpdateTests.Run),
    ("Protocol v2 scroll containers round-trip with host-owned semantics", ScrollContainersRoundTrip),
    ("Protocol v11 scroll pagination is bounded and versioned", ScrollPaginationRoundTrip),
    ("Baseline widgets remain protocol v1 compatible", BaselineProtocolCompatibility),
    ("Protocol v9 responsive branches are semantic and versioned", ResponsiveVisibilityRoundTrip),
    ("Scroll containers and surface hints fail closed", ScrollAndSurfaceValidation),
    ("Protocol v17 surface axes are independent versioned and legacy compatible", SurfaceAxisModesRoundTrip),
    ("Duplicate stable IDs are rejected", DuplicateIdsAreRejected),
    ("Broken focus neighbors are rejected", BrokenFocusIsRejected),
    ("Invalid progress is rejected", InvalidProgressIsRejected),
    ("Slider v3 serializes bounded accessible value semantics", SliderV3RoundTrip),
    ("Activation-first sliders are opt-in versioned and preserve directional focus", SliderActivationModeRoundTrip),
    ("Slider validation rejects unsafe ranges and focus conflicts", InvalidSlidersAreRejected),
    ("Image and icon nodes round-trip as renderer-neutral primitives", VisualNodesRoundTrip),
    ("Loading indicators round-trip with bounded non-interactive semantics", LoadingIndicatorRoundTrip),
    ("Inline PNG images are bounded local and negotiate the highest protocol", InlinePngImagesAreBounded),
    ("Input surfaces serialize and validate scoped shortcuts", InputSurfacesValidate),
    ("Dashboard quick action authority is typed bounded and versioned", DashboardAuthorityContract),
    ("Host-reserved View mappings fail with precise author diagnostics", HostReservedViewMappingsAreRejected),
    ("Unsafe image sources are rejected", UnsafeImageSourcesAreRejected),
    ("Visual nodes require accessibility and semantic data", VisualNodeRequirementsAreEnforced),
    ("Button interaction states serialize deterministically", ButtonStatesRoundTrip),
    ("Text entry is host-owned bounded and protocol v15", TextEntryRoundTrip),
    ("Buttons expose closed semantic icons without action-ID inference", ButtonIconsRoundTrip),
    ("Settings composites expose stable controller and accessibility semantics", SettingsCompositesAreSemantic),
    ("Modern composites preserve semantic classes IDs and accessibility", ModernComponentsAreSemantic),
    ("Modern controller composites preserve tab switch and dialog semantics", ModernControllerComponentsAreSemantic),
    ("Settings rows and action sheets preserve responsive controller semantics", SettingsRowsAndActionSheetsAreSemantic),
    ("Action sheets route nested Back and suppress unavailable actions", ActionSheetRoutingIsScoped),
    ("Pickers preserve single-select controller and accessibility semantics", PickersAreSemantic),
    ("Scrubbers compose stable controller seeking and responsive time semantics", ScrubbersAreSemantic),
    ("Scrubbers route absolute millisecond targets through the native Slider contract", ScrubberInputResolves),
    ("Toasts are bounded semantic notifications that never steal controller focus", ToastsAreNonInteractive),
    ("Media tiles expose one rich full-tile controller target", TileComponentTests.MediaTilesAreSemantic),
    ("App tiles constrain artwork and keep state non-color-only", TileComponentTests.AppTilesAreSemantic),
    ("Action surfaces fail closed and route one activation", TileComponentTests.ActionSurfacesValidateAndRoute),
    ("Responsive grids round-trip typed column semantics", GridComponentTests.RoundTripsTypedSemantics),
    ("Responsive grids validate bounds and preserve old protocols", GridComponentTests.ValidatesBoundsAndCompatibility),
    ("Responsive grids preserve arbitrary child order and focus graphs", GridComponentTests.PreservesChildrenAndFocus),
    ("Code text preserves bounded non-focusable monospace semantics", CodeTextTests.Run),
    ("Minimalist rows and controller hints preserve public focus and accessibility contracts", MinimalistRowsAreSemantic),
    ("Composite child IDs enforce protocol boundaries eagerly", CompositeChildIdsValidateEagerly),
    ("Protocol rejects unsafe or unbounded WRSS style classes", RawStyleClassesAreValidated),
    ("Style helpers eagerly enforce WRSS class contracts", StyleExtensionsValidateClasses),
    ("Undefined protocol enums are rejected before renderer transport", UndefinedProtocolEnumsAreRejected),
    ("Interaction states reject invalid node combinations", InvalidInteractionStatesAreRejected),
    ("Unknown protocol JSON fields are rejected", UnknownFieldsAreRejected),
    ("Null protocol collections report validation errors", NullCollectionsAreRejected),
    ("Valid manifest passes", ValidManifestPasses),
    ("Full-trust entrypoint is versioned strict and schema-only", FullTrustEntrypointIsStrict),
    ("Manifest pinning is explicit and defaults closed", ManifestPinningIsExplicit),
    ("Manifest presentation uses only closed semantic host icons", ManifestPresentationIsSemantic),
    ("Manifest permission declarations are bounded ASCII-safe and unambiguous", ManifestPermissionsAreBounded),
    ("Residency policy is versioned bounded and legacy compatible", ResidencyPolicyIsVersioned),
    ("Worker memory guidance is optional advisory metadata", WorkerMemoryGuidanceIsAdvisory),
    ("Unsafe manifest values report errors", UnsafeManifestFails),
    ("Null manifest collections report validation errors", NullManifestCollectionsFail),
    ("Clock sample renders controller metadata", ClockRenders),
    ("Clock refresh invalidates once", ClockInvalidates),
    ("Runtime-owned operations coordinate concurrency and lifecycle cleanup", WidgetOperationTests.Run),
    ("Immutable widget models serialize state and suppress redundant invalidation", WidgetModelTests.Run),
    ("Optimistic commands coordinate projection rollback and lifecycle", WidgetOptimisticCommandTests.Run),
    ("Non-paged resources coordinate cache events retry and lifecycle", WidgetResourceTests.Run),
    ("Paged resources coordinate bounded automatic collection loading", WidgetPagedResourceTests.Run),
    ("Cursor resources append bounded keyed collection windows", WidgetCursorResourceTests.Run),
    ("Hierarchical widget IDs stay stable bounded and opaque", WidgetIdsTests.Run),
    ("Widget navigation owns bounded routes scopes focus Back and cancellation", WidgetNavigatorTests.Run),
    ("Navigation shells share responsive content and preserve controller traversal", NavigationShellTests.ComposesOneResponsiveContentTree),
    ("Navigation shells wire compact and expanded focus graphs", NavigationShellTests.PreservesControllerTraversal),
    ("Navigation shells reject ambiguous or unbounded destinations", NavigationShellTests.ValidatesAuthoringBounds),
    ("Focus persistence negotiates v13 without changing legacy snapshots", NavigationShellTests.FocusPersistenceIsOptionalAndVersioned),
    ("Default controller routing resolves dashboard quick actions", DashboardInputResolves),
    ("Default controller routing resolves focused shortcuts", FocusedShortcutResolves),
    ("Repeated row shortcuts resolve by exact focus and ambiguous fallback fails closed", FocusedRowShortcutsResolve),
    ("Default controller routing activates focused buttons with A", FocusedButtonActivates),
    ("Default controller routing ignores A on non-buttons", NonButtonDoesNotActivate),
    ("Default controller routing blocks disabled and busy buttons", DisabledAndBusyButtonsDoNotActivate),
    ("Slider routing is stale-safe and uses absolute requested values", SliderInputResolves),
    ("Pending slider actions coalesce latest-wins without crossing actions", SliderActionsCoalesceInOrder),
    ("Serial action diagnostics preserve exact correlation and terminal ownership", ActionDiagnosticsCorrelate),
    ("Controller shortcut fallback stays in explicit active input surface", ScopedShortcutRouting),
    ("Public test host services are typed immutable and attach once", HostCapabilityServices),
    ("Audio and network host services use typed provider contracts", TypedPlatformServices),
    ("App library host service uses opaque paged read and launch contracts", AppLibraryPlatformService),
    ("App library values and relationship composer validate independently",
        AppLibraryPresentationValidationTests.Run),
    ("Community services expose exact loopback and write-only secret contracts", CommunityPlatformServices),
    ("SDK and broker platform limits remain exactly parity bound", CommunityPlatformLimitParity),
    ("Private state canonicalizes JSON and exposes typed revision CAS helpers", PrivateStateServiceContracts),
    ("Capability subscriptions acknowledge before event consumption", SubscriptionOpenAcknowledges),
};

static Task CommunityPlatformLimitParity()
{
    const System.Reflection.BindingFlags allStatic =
        System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Static;
    var sharedNames = new[]
    {
        nameof(WidgetCommunityPlatformLimits.MinimumLoopbackPort),
        nameof(WidgetCommunityPlatformLimits.MaximumLoopbackPort),
        nameof(WidgetCommunityPlatformLimits.MaximumLoopbackPathCharacters),
        nameof(WidgetCommunityPlatformLimits.MaximumLoopbackHeaderCount),
        nameof(WidgetCommunityPlatformLimits.MaximumLoopbackHeaderNameCharacters),
        nameof(WidgetCommunityPlatformLimits.MaximumLoopbackHeaderValueCharacters),
        nameof(WidgetCommunityPlatformLimits.MaximumLoopbackHeaderCharacters),
        nameof(WidgetCommunityPlatformLimits.MaximumLoopbackRequestBodyUtf8Bytes),
        nameof(WidgetCommunityPlatformLimits.MaximumLoopbackResponseBodyUtf8Bytes),
        nameof(WidgetCommunityPlatformLimits.DefaultLoopbackTimeoutMilliseconds),
        nameof(WidgetCommunityPlatformLimits.MaximumLoopbackTimeoutMilliseconds),
        nameof(WidgetCommunityPlatformLimits.MaximumPrivateSecretSlotCharacters),
        nameof(WidgetCommunityPlatformLimits.MaximumPrivateSecretUtf8Bytes),
        nameof(WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes),
        "MaximumPrivateStateBase64Characters",
    };

    var sdk = typeof(WidgetCommunityPlatformLimits).GetFields(allStatic)
        .Where(field => sharedNames.Contains(field.Name, StringComparer.Ordinal))
        .ToDictionary(field => field.Name, field => (int)field.GetRawConstantValue()!,
            StringComparer.Ordinal);
    var broker = typeof(CommunityPlatformLimits).GetFields(allStatic)
        .ToDictionary(field => field.Name, field => (int)field.GetRawConstantValue()!,
            StringComparer.Ordinal);
    broker.Add(nameof(WidgetCommunityPlatformLimits.MinimumLoopbackPort),
        PlatformCapabilities.MinimumLoopbackPort);
    broker.Add(nameof(WidgetCommunityPlatformLimits.MaximumLoopbackPort),
        PlatformCapabilities.MaximumLoopbackPort);

    Assert.Equal(sharedNames.Length, sdk.Count);
    Assert.Equal(sharedNames.Length, broker.Count);
    Assert.True(sharedNames.Order(StringComparer.Ordinal).SequenceEqual(
            sdk.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
        "The SDK shared platform-limit matrix is missing or contains renamed fields.");
    Assert.True(sharedNames.Order(StringComparer.Ordinal).SequenceEqual(
            broker.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
        "The broker shared platform-limit matrix is missing or contains renamed fields.");
    foreach (var name in sharedNames)
        Assert.Equal(sdk[name], broker[name]);

    Assert.Equal(256 * 1_024,
        WidgetCommunityPlatformLimits.MaximumPrivateStateInputUtf8Bytes);
    Assert.True(!broker.ContainsKey(
            nameof(WidgetCommunityPlatformLimits.MaximumPrivateStateInputUtf8Bytes)),
        "The SDK-only private-state recovery ceiling entered the broker contract.");
    return Task.CompletedTask;
}

static Task HostReservedViewMappingsAreRejected()
{
    var quickActionFailure = Assert.Throws<ProtocolValidationException>(() =>
        new WidgetView(
            UI.Button("Ready", "ready", "view.quick.ready"),
            InitialFocusId: "view.quick.ready",
            QuickActions:
            [
                new WidgetQuickAction(ControllerButton.View, "view.quick", "Invalid View"),
            ]).CreateSnapshot("view.quick.fixture", 1));
    var quickActionError = quickActionFailure.Errors.Single(error =>
        error.Path == "$.quickActions[0].button" &&
        error.Code == "host_reserved_view");
    Assert.True(quickActionError.Message.Contains(
        "reserved for host pinned-surface navigation", StringComparison.Ordinal),
        "Host-reserved View quick-action validation did not provide the precise diagnostic.");

    var shortcutFailure = Assert.Throws<ProtocolValidationException>(() =>
        new WidgetView(
            UI.Button("Ready", "ready", "view.shortcut.ready")
                .Shortcut(ControllerButton.View, actionId: "view.shortcut"),
            InitialFocusId: "view.shortcut.ready")
            .CreateSnapshot("view.shortcut.fixture", 1));
    var shortcutError = shortcutFailure.Errors.Single(error =>
        error.Path == "$.root.shortcuts[0].button" &&
        error.Code == "host_reserved_view");
    Assert.True(shortcutError.Message.Contains(
        "reserved for host pinned-surface navigation", StringComparison.Ordinal),
        "Host-reserved View shortcut validation did not provide the precise diagnostic.");
    return Task.CompletedTask;
}

var testPrefixIndex = Array.IndexOf(args, "--test-prefix");
if (testPrefixIndex >= 0)
{
    if (testPrefixIndex + 1 >= args.Length)
        throw new ArgumentException("Missing --test-prefix value.");
    var prefix = args[testPrefixIndex + 1];
    tests = tests.Where(test => test.Name.StartsWith(
        prefix, StringComparison.Ordinal)).ToArray();
    if (tests.Length == 0)
        throw new ArgumentException($"No WidgetSdk test starts with '{prefix}'.");
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
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static Task TextEntryRoundTrip()
{
    var snapshot = new WidgetView(UI.Stack("root",
        UI.TextEntry("Halo", "Search games", "search.commit", "search", 32)))
        .CreateSnapshot("text-entry.test", 1);
    var node = snapshot.Root.Children.Single();
    Assert.Equal(ProtocolConstants.TextEntryVersion, snapshot.ProtocolVersion);
    Assert.Equal(ViewNodeKind.TextEntry, node.Kind);
    Assert.Equal("Halo", node.TextEntryValue);
    Assert.Equal("Search games", node.TextEntryPlaceholder);
    Assert.Equal(32, node.TextEntryMaximumLength);
    Assert.True(node.IsFocusable, "Text entry must be a controller focus target.");
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        UI.TextEntry("", "Search", "search.commit", "search", 97));
    var invalid = snapshot with
    {
        Root = snapshot.Root with
        {
            Children = [node with { TextEntryValue = "bad\nvalue" }],
        },
    };
    Assert.True(ViewSnapshotValidator.Validate(invalid).Any(error =>
        error.Code == "invalid_text_entry_value"),
        "Control characters must fail text-entry validation.");
    return Task.CompletedTask;
}

static Task InlinePngImagesAreBounded()
{
    const string png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
        "AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";
    var view = new WidgetView(
        UI.Stack("root",
            UI.LoadingIndicator("loading", "Loading icon"),
            UI.InlinePngImage(png, "icon", "Application icon"),
            UI.Button("Application with a long name", "launch", "app-row")
                .LeadingInlinePng(png, accessibilityLabel: "Launch application"),
            UI.Slider(1, 0, 10, 1, "change", "slider", "Value")));
    var snapshot = view.CreateSnapshot("inline-image-test", 1);
    Assert.Equal(ProtocolConstants.InlinePngImageVersion, snapshot.ProtocolVersion);
    var image = snapshot.Root.Children.Single(node => node.Id == "icon");
    Assert.True(image.ImageSource!.StartsWith(
        "data:image/png;base64,", StringComparison.Ordinal),
        "Inline image source did not use the local PNG data scheme.");
    Assert.True(image.ActionId is null, "Inline images must remain non-interactive.");
    var appRow = snapshot.Root.Children.Single(node => node.Id == "app-row");
    Assert.Equal(ViewNodeKind.Button, appRow.Kind);
    Assert.True(appRow.ImageSource!.StartsWith(
        "data:image/png;base64,", StringComparison.Ordinal),
        "Leading button image did not use the local PNG data scheme.");
    Assert.Equal(ImageFit.Contain, appRow.ImageFit);
    Assert.True(appRow.Glyph is null, "A leading image must replace the semantic glyph.");
    var mixedLeadingVisuals = snapshot with
    {
        Root = snapshot.Root with
        {
            Children = snapshot.Root.Children.Select(node => node.Id == "app-row"
                ? node with { Glyph = WidgetGlyph.Play }
                : node).ToArray(),
        },
    };
    Assert.True(ViewSnapshotValidator.Validate(mixedLeadingVisuals).Any(error =>
            error.Code == "multiple_leading_visuals"),
        "Buttons must reject simultaneous semantic glyph and leading artwork.");

    Assert.Throws<ProtocolValidationException>(() =>
        new WidgetView(UI.Stack("bad-root",
            UI.InlinePngImage(Convert.ToBase64String([1, 2, 3]), "bad", "Bad icon")))
            .CreateSnapshot("bad-inline-image", 1));
    Assert.Throws<ArgumentException>(() =>
        UI.InlinePngImage("not-base64", "bad-base64", "Bad icon"));
    return Task.CompletedTask;
}

static async Task HostCapabilityServices()
{
    var widget = new CapabilityWidget();
    Assert.True(!widget.CapabilitiesAvailable, "Capabilities must fail closed without a host channel.");
    try
    {
        _ = await widget.CallAsync();
        throw new InvalidOperationException("Expected unavailable capability client to fail.");
    }
    catch (WidgetCapabilityUnavailableException)
    {
    }

    var operation = new WidgetCapabilityOperation<string, string>(
        "test.capability.v1", "test.invoke");
    var services = new WidgetTestHostServicesBuilder()
        .WithResponse(operation, "accepted")
        .Build();
    WidgetTestHost.Attach(widget, services);
    Assert.True(widget.CapabilitiesAvailable, "Attached capability client was not visible to the widget.");
    Assert.Equal("accepted", await widget.CallAsync());
    await WidgetTestHost.InitializeAsync(widget);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    Assert.Equal(WidgetLifecycleState.Visible, widget.TestLifecycleState);
    Assert.Throws<InvalidOperationException>(() =>
        WidgetTestHost.Attach(widget, services));
    await WidgetTestHost.DestroyAsync(widget);

    var initialized = new CapabilityWidget();
    await WidgetTestHost.InitializeAsync(initialized);
    Assert.Throws<InvalidOperationException>(() =>
        WidgetTestHost.Attach(initialized, services));

    var missing = WidgetTestHost.Attach(
        new CapabilityWidget(), new WidgetTestHostServicesBuilder().Build());
    try
    {
        _ = await missing.CallAsync();
        throw new InvalidOperationException("Expected an unconfigured test operation to fail.");
    }
    catch (WidgetCapabilityException exception)
    {
        Assert.Equal("test_handler_missing", exception.ErrorCode);
    }
}

static async Task AppLibraryPlatformService()
{
    var services = new WidgetTestHostServicesBuilder()
        .WithHandler(
            WidgetAppLibraryCapabilities.GetPage,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal("cursor-64", request.Cursor);
                Assert.Equal(WidgetCursorDirection.After, request.Direction);
                Assert.Equal(12, request.Limit);
                Assert.Equal(WidgetAppLibraryKind.Application, request.Query.Kind);
                Assert.Equal("Windows", request.Query.SourceAttribution);
                Assert.Equal("Launchable App", request.Query.SearchText);
                Assert.Equal(WidgetAppLibrarySortOrder.SourceThenDisplayName,
                    request.Query.Sort);
                Assert.Equal(1, request.Query.FavoriteSavedIds.Count);
                Assert.Equal("saved-durable", request.Query.FavoriteSavedIds[0]);
                return ValueTask.FromResult(new WidgetAppLibraryPage(
                    [RichAppLibraryItem("app-opaque", "saved-durable")],
                    "cursor-32", null, "revision-2")
                {
                    Sources =
                    [
                        new("source-windows", "Windows",
                            WidgetAppLibrarySourceHealth.Healthy, 4, "healthy")
                        {
                            AccountState = WidgetAppLibrarySourceAccountState.Ready,
                            LastSuccessfulRefreshAtUnixMilliseconds = 1_700_000_000_000,
                        },
                        new("source-steam", "Steam",
                            WidgetAppLibrarySourceHealth.Degraded, 7,
                            "source_degraded"),
                    ],
                });
            })
        .WithHandler(
            WidgetAppLibraryCapabilities.ResolveSaved,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal(2, request.SavedIds.Count);
                Assert.Equal("saved-missing", request.SavedIds[0]);
                Assert.Equal("saved-durable", request.SavedIds[1]);
                return ValueTask.FromResult(new ResolveSavedWidgetAppLibraryItemsResponse(
                    [InstalledAppLibraryItem(
                        "app-current", "saved-durable", "Launchable App",
                        WidgetAppLibraryKind.Application,
                        "source-windows", "Windows",
                        "library.art.0123456789abcdef0123456789abcdef",
                        "art-revision-1")]));
            })
        .WithResponse(
            WidgetAppLibraryCapabilities.Launch,
            new WidgetCapabilityAcknowledgement(true))
        .WithResponse(
            WidgetAppLibraryCapabilities.LaunchObserved,
            new WidgetAppLaunchObservation(
                WidgetAppLaunchObservationState.LauncherStarted, true, true))
        .WithResponse(
            WidgetAppLibraryCapabilities.ObserveRunning,
            new WidgetRunningAppObservation(
                [new("saved-running", "Visible app", WidgetAppLibraryKind.Application,
                    "Windows")], "running-revision"))
        .WithHandler(
            WidgetAppLibraryCapabilities.ConfirmRunning,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal("saved-running", request.SavedId);
                Assert.Equal("running-revision", request.Revision);
                return ValueTask.FromResult(new ConfirmWidgetRunningAppResponse(
                    InstalledAppLibraryItem(
                        "app-current", request.SavedId, "Visible app",
                        WidgetAppLibraryKind.Application,
                        "source-windows", "Windows")));
            })
        .Build();
    var widget = WidgetTestHost.Attach(new CapabilityWidget(), services);

    var page = await widget.AppLibrary.QueryAsync(
        new WidgetAppLibraryQuery(
            Kind: WidgetAppLibraryKind.Application,
            SourceAttribution: "Windows",
            Sort: WidgetAppLibrarySortOrder.SourceThenDisplayName)
        {
            SearchText = "  Launchable\tApp  ",
            FavoriteSavedIds = ["saved-durable"],
        },
        new WidgetCollectionCursor("cursor-64"), WidgetCursorDirection.After, 12);
    Assert.Equal(1, page.Items.Count);
    Assert.Equal("app-opaque", page.Items[0].AppId);
    Assert.Equal("saved-durable", page.Items[0].SavedId);
    Assert.Equal("Launchable App", page.Items[0].Presentation.DisplayName);
    Assert.Equal(WidgetAppLibraryKind.Application, page.Items[0].Presentation.Kind);
    Assert.Equal(WidgetAppLibraryItem.CurrentPresentationVersion,
        page.Items[0].PresentationVersion);
    Assert.Equal("source-windows", page.Items[0].Presentation.Source.SourceId);
    Assert.Equal(WidgetAppLibraryAvailabilityState.Installed,
        page.Items[0].Presentation.Availability.State);
    Assert.True(page.Items[0].Presentation.Availability.IsLaunchable,
        "Normalized availability should preserve explicit launchability.");
    Assert.Equal(4, page.Items[0].Presentation.Artwork.Items.Count);
    Assert.Equal("library.art.tile", page.Items[0].Presentation.Artwork
        .Find(WidgetAppLibraryArtworkRole.Tile)?.Handle);
    Assert.Equal(WidgetAppLibraryArtworkFallback.Game,
        page.Items[0].Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Hero)?.Fallback);
    Assert.Equal("metadata-revision", page.Items[0].Presentation.Metadata?.Revision);
    Assert.Equal("metadata-record", page.Items[0].Presentation.Metadata?.Attribution.RecordRevision);
    Assert.Equal(2, page.Items[0].Presentation.Metadata?.Categories.Count);
    Assert.Equal(13, page.Items[0].Presentation.Capabilities.Actions.Count);
    Assert.True(page.Items[0].Presentation.Capabilities.Supports(
        WidgetAppLibraryAction.ManageAddOns),
        "The complete closed capability set should round-trip.");
    Assert.Equal(WidgetAppLibraryOperationKind.Update,
        page.Items[0].Presentation.ActiveOperation?.Kind);
    Assert.Equal(WidgetAppLibraryOperationState.Running,
        page.Items[0].Presentation.ActiveOperation?.State);
    Assert.Equal("cursor-32", page.Before);
    Assert.Equal<string?>(null, page.After);
    Assert.Equal("revision-2", page.Revision);
    Assert.Equal(2, page.Sources.Count);
    Assert.Equal(WidgetAppLibrarySourceAccountState.Ready, page.Sources[0].AccountState);
    Assert.Equal(1_700_000_000_000,
        page.Sources[0].LastSuccessfulRefreshAtUnixMilliseconds);
    Assert.Equal(WidgetAppLibrarySourceHealth.Degraded, page.Sources[1].Health);
    Assert.Equal("source_degraded", page.Sources[1].StatusCode);
    Assert.Equal(0, (await widget.AppLibrary.ResolveSavedAsync([])).Count);
    var resolved = await widget.AppLibrary.ResolveSavedAsync(
        ["saved-missing", "saved-durable"]);
    Assert.Equal(1, resolved.Count);
    Assert.Equal("app-current", resolved[0].AppId);
    Assert.Equal("saved-durable", resolved[0].SavedId);
    Assert.Equal("library.art.0123456789abcdef0123456789abcdef",
        resolved[0].Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile)?.Handle);
    var running = await widget.AppLibrary.ObserveRunningAsync();
    Assert.Equal(1, running.Items.Count);
    Assert.Equal("saved-running", running.Items[0].SavedId);
    var confirmed = await widget.AppLibrary.ConfirmRunningAsync(
        running.Items[0].SavedId, running.Revision);
    Assert.Equal("app-current", confirmed?.AppId);
    var validConfirmation = InstalledAppLibraryItem(
        "app-current", "saved-running", "Visible app",
        WidgetAppLibraryKind.Application, "source-windows", "Windows");
    var malformedConfirmations = new[]
    {
        validConfirmation with { AppId = "bad/app" },
        validConfirmation with { PresentationVersion = 2 },
        validConfirmation with { SavedId = "saved-other" },
        validConfirmation with { Presentation = validConfirmation.Presentation with
            { Kind = (WidgetAppLibraryKind)999 } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
            { DisplayName = "" } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
            { DisplayName = new string('D', 161) } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
            { DisplayName = "bad\nname" } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
            { Source = validConfirmation.Presentation.Source with { DisplayName = "" } } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
            { Source = validConfirmation.Presentation.Source with
                { DisplayName = new string('S', 65) } } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
            { Source = validConfirmation.Presentation.Source with
                { DisplayName = "bad\rsource" } } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
        {
            Availability = new(WidgetAppLibraryAvailabilityState.Unavailable,
                true, "unavailable"),
        } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
        {
            Availability = new(WidgetAppLibraryAvailabilityState.StaleSource,
                false, "stale"),
            Capabilities = new([WidgetAppLibraryAction.Launch]),
        } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
        {
            Capabilities = new([]),
        } },
        validConfirmation with { Presentation = validConfirmation.Presentation with
        {
            ActiveOperation = new("operation-update",
                WidgetAppLibraryOperationKind.Update,
                WidgetAppLibraryOperationState.Running, "updating"),
        } },
    };
    foreach (var malformedConfirmation in malformedConfirmations)
    {
        var malformedConfirmationHost = WidgetTestHost.Attach(
            new CapabilityWidget(),
            new WidgetTestHostServicesBuilder()
                .WithResponse(
                    WidgetAppLibraryCapabilities.ConfirmRunning,
                    new ConfirmWidgetRunningAppResponse(malformedConfirmation))
                .Build());
        var malformedConfirmationError = Assert.Throws<WidgetCapabilityException>(() =>
            malformedConfirmationHost.AppLibrary.ConfirmRunningAsync(
                    "saved-running", "running-revision")
                .GetAwaiter().GetResult());
        Assert.Equal("malformed_response", malformedConfirmationError.ErrorCode);
    }
    await widget.AppLibrary.LaunchAsync("app-opaque");
    var launchObservation = await widget.AppLibrary.LaunchObservedAsync(
        "app-opaque", WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess);
    Assert.Equal(WidgetAppLaunchObservationState.LauncherStarted,
        launchObservation.State);
    Assert.True(launchObservation.SupportsRunning,
        "The public launch observation lost running-state support.");
    Assert.True(launchObservation.SupportsEnded,
        "The public launch observation lost ended-state support.");
    Assert.Throws<ArgumentException>(() =>
        widget.AppLibrary.LaunchAsync(string.Empty).GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() =>
        widget.AppLibrary.QueryAsync(new WidgetAppLibraryQuery(),
                new WidgetCollectionCursor("cursor"), direction: null)
            .GetAwaiter().GetResult());
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        widget.AppLibrary.QueryAsync(new WidgetAppLibraryQuery(),
                limit: WidgetAppLibraryService.MaximumPageSize + 1)
            .GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() =>
        widget.AppLibrary.QueryAsync(new WidgetAppLibraryQuery
            {
                FavoriteSavedIds = Enumerable.Range(
                        0, WidgetAppLibraryQuery.MaximumFavoriteSavedIds + 1)
                    .Select(index => $"saved-{index}").ToArray(),
            }).GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() =>
        widget.AppLibrary.ResolveSavedAsync(["saved-same", "saved-same"])
            .GetAwaiter().GetResult());
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        widget.AppLibrary.ResolveSavedAsync(
                Enumerable.Range(0, WidgetAppLibraryService.MaximumSavedItems + 1)
                    .Select(index => $"saved-{index}").ToArray())
            .GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() =>
        widget.AppLibrary.ConfirmRunningAsync("not-saved", "revision")
            .GetAwaiter().GetResult());

    var malformed = WidgetTestHost.Attach(
        new CapabilityWidget(),
        new WidgetTestHostServicesBuilder()
            .WithResponse(
                WidgetAppLibraryCapabilities.ResolveSaved,
                new ResolveSavedWidgetAppLibraryItemsResponse(
                    [InstalledAppLibraryItem(
                        "app-current", "saved-not-requested", "Wrong app",
                        WidgetAppLibraryKind.Application,
                        "source-windows", "Windows")]))
            .Build());
    Assert.Throws<WidgetCapabilityException>(() =>
        malformed.AppLibrary.ResolveSavedAsync(["saved-requested"])
            .GetAwaiter().GetResult());

    var malformedSources = WidgetTestHost.Attach(
        new CapabilityWidget(),
        new WidgetTestHostServicesBuilder()
            .WithResponse(
                WidgetAppLibraryCapabilities.GetPage,
                new WidgetAppLibraryPage([], null, null, "revision")
                {
                    Sources = [new("source-one", "Source",
                        WidgetAppLibrarySourceHealth.Healthy, 1, "unsafe status")],
                })
            .Build());
    Assert.Throws<WidgetCapabilityException>(() =>
        malformedSources.AppLibrary.QueryAsync(new WidgetAppLibraryQuery())
            .GetAwaiter().GetResult());

    var malformedRunning = WidgetTestHost.Attach(
        new CapabilityWidget(),
        new WidgetTestHostServicesBuilder()
            .WithResponse(
                WidgetAppLibraryCapabilities.ObserveRunning,
                new WidgetRunningAppObservation(
                    [new("saved-running", "Visible app", WidgetAppLibraryKind.Application,
                        "bad\nsource")], "revision"))
            .Build());
    Assert.Throws<WidgetCapabilityException>(() =>
        malformedRunning.AppLibrary.ObserveRunningAsync().GetAwaiter().GetResult());
}

static WidgetAppLibraryItem RichAppLibraryItem(string appId, string savedId) =>
    new(
        appId,
        savedId,
        new WidgetAppLibraryPresentation(
            "Launchable App",
            WidgetAppLibraryKind.Application,
            new WidgetAppLibrarySourceReference("source-windows", "Windows"),
            new WidgetAppLibraryAvailability(
                WidgetAppLibraryAvailabilityState.Installed, true, "installed"),
            new WidgetAppLibraryArtworkSet(
            [
                new(WidgetAppLibraryArtworkRole.Tile, "library.art.tile", "art-tile",
                    WidgetAppLibraryArtworkFallback.Application),
                new(WidgetAppLibraryArtworkRole.Cover, "library.art.cover", "art-cover",
                    WidgetAppLibraryArtworkFallback.Game),
                new(WidgetAppLibraryArtworkRole.Hero, "library.art.hero", "art-hero",
                    WidgetAppLibraryArtworkFallback.Game),
                new(WidgetAppLibraryArtworkRole.Logo, "library.art.logo", "art-logo",
                    WidgetAppLibraryArtworkFallback.Application),
            ]),
            new WidgetAppLibraryMetadata(
                "metadata-revision",
                new WidgetAppLibraryMetadataAttribution(
                    "Windows", "metadata-record", "Windows App Library", 1_700_000_000_000))
            {
                SortTitle = "Launchable App",
                Version = "1.2.3",
                LastPlayedAtUnixMilliseconds = 1_699_999_000_000,
                PlaytimeMinutes = 120,
                Categories = ["Application", "Utility"],
                Description = "A normalized application record.",
            },
            new WidgetAppLibraryCapabilitySet(Enum.GetValues<WidgetAppLibraryAction>()),
            new WidgetAppLibraryOperation(
                "operation-update", WidgetAppLibraryOperationKind.Update,
                WidgetAppLibraryOperationState.Running, "updating")));

static WidgetAppLibraryItem InstalledAppLibraryItem(
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

static async Task CommunityPlatformServices()
{
    var get = WidgetLoopbackCapabilities.GetJson(13091);
    var post = WidgetLoopbackCapabilities.PostJson(13091);
    var services = new WidgetTestHostServicesBuilder()
        .WithHandler(get, (request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("/api/v1/state", request.Path);
            Assert.Equal("session", request.BearerSecretSlot);
            Assert.True(request.InvalidateBearerSecretOnUnauthorized,
                "Rejected-bearer invalidation was not serialized to the host request.");
            Assert.Equal(10_000, request.TimeoutMilliseconds);
            Assert.Equal<string?>(null, request.JsonBody);
            return ValueTask.FromResult(new WidgetLoopbackJsonResponse(
                200, "{\"playing\":true}",
                [new WidgetLoopbackHttpHeader("ETag", "\"one\"")]));
        })
        .WithHandler(post, (request, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("/auth/request", request.Path);
            Assert.Equal("{\"app\":\"widget\"}", request.JsonBody);
            Assert.Equal(40_000, request.TimeoutMilliseconds);
            return ValueTask.FromResult(new WidgetLoopbackJsonResponse(200, "{}", []));
        })
        .WithResponse(
            WidgetPrivateSecretCapabilities.Exists,
            new WidgetPrivateSecretExists(true))
        .WithResponse(
            WidgetPrivateSecretCapabilities.Metadata,
            new WidgetPrivateSecretMetadata(true, 1234))
        .WithResponse(
            WidgetPrivateSecretCapabilities.Save,
            new WidgetCapabilityAcknowledgement(true))
        .WithResponse(
            WidgetPrivateSecretCapabilities.Delete,
            new WidgetCapabilityAcknowledgement(true))
        .Build();
    var widget = WidgetTestHost.Attach(new CapabilityWidget(), services);
    var state = await widget.Loopback.GetJsonAsync(13091, "/api/v1/state",
        new WidgetLoopbackRequestOptions
        {
            BearerSecretSlot = "session",
            InvalidateBearerSecretOnUnauthorized = true,
        });
    Assert.Equal(200, state.StatusCode);
    Assert.Equal("{\"playing\":true}", state.JsonBody);
    Assert.Equal("ETag", state.Headers[0].Name);
    await widget.Loopback.PostJsonAsync(13091, "/auth/request", "{\"app\":\"widget\"}",
        new WidgetLoopbackRequestOptions { Timeout = TimeSpan.FromSeconds(40) });
    Assert.True(await widget.PrivateSecrets.ExistsAsync("session"),
        "Expected private secret slot to exist.");
    var metadata = await widget.PrivateSecrets.GetMetadataAsync("session");
    Assert.Equal<long?>(1234, metadata.LastWrittenUnixMilliseconds);
    await widget.PrivateSecrets.SaveAsync("session", "new-secret");
    await widget.PrivateSecrets.DeleteAsync("session");

    Assert.Throws<ArgumentOutOfRangeException>(() =>
        widget.Loopback.GetJsonAsync(80, "/").GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() =>
        widget.Loopback.GetJsonAsync(13091, "//remote.example/path").GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() =>
        widget.Loopback.PostJsonAsync(13091, "/", "not-json").GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() =>
        widget.Loopback.GetJsonAsync(13091, "/", new WidgetLoopbackRequestOptions
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer stolen" },
        }).GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() =>
        widget.Loopback.GetJsonAsync(13091, "/", new WidgetLoopbackRequestOptions
        {
            InvalidateBearerSecretOnUnauthorized = true,
        }).GetAwaiter().GetResult());
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        widget.Loopback.GetJsonAsync(13091, "/", new WidgetLoopbackRequestOptions
        {
            Timeout = TimeSpan.FromMilliseconds(40_001),
        }).GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() =>
        widget.PrivateSecrets.SaveAsync("bad/slot", "secret").GetAwaiter().GetResult());
    Assert.True(typeof(WidgetPrivateSecretService).GetMethods()
            .Where(method => method.DeclaringType == typeof(WidgetPrivateSecretService))
            .All(method => method.Name is not ("ReadAsync" or "GetSecretAsync")),
        "The public vault must not expose stored secret values.");
}

static async Task PrivateStateServiceContracts()
{
    var readJson = "{\"count\":2,\"message\":\"line\\nbreak\"}";
    var services = new WidgetTestHostServicesBuilder()
        .WithResponse(
            WidgetPrivateStateCapabilities.Read,
            new WidgetPrivateStateTransportSnapshot(
                true, Convert.ToBase64String(Encoding.UTF8.GetBytes(readJson)), 3))
        .WithHandler(
            WidgetPrivateStateCapabilities.Write,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal<long?>(3, request.ExpectedRevision);
                Assert.Equal("{\"a\":1,\"z\":2}", Encoding.UTF8.GetString(
                    Convert.FromBase64String(request.CanonicalJsonBase64)));
                return ValueTask.FromResult(new WidgetPrivateStateTransportMutation(4));
            })
        .WithHandler(
            WidgetPrivateStateCapabilities.Clear,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal<long?>(4, request.ExpectedRevision);
                return ValueTask.FromResult(new WidgetPrivateStateTransportMutation(5));
            })
        .Build();
    var widget = WidgetTestHost.Attach(new CapabilityWidget(), services);
    var snapshot = await widget.PrivateState.ReadJsonAsync();
    Assert.True(snapshot.Exists, "Private state snapshot unexpectedly reported no document.");
    Assert.Equal(3L, snapshot.Revision);
    Assert.Equal(readJson, snapshot.Json);
    var typed = await widget.PrivateState.ReadAsync<Dictionary<string, JsonElement>>();
    Assert.Equal(2, typed.Value!["count"].GetInt32());
    var written = await widget.PrivateState.WriteJsonAsync(" { \"z\" : 2, \"a\" : 1 } ", 3);
    Assert.Equal(4L, written.Revision);
    Assert.Equal(5L, (await widget.PrivateState.ClearAsync(4)).Revision);

    Assert.Throws<ArgumentException>(() => widget.PrivateState
        .WriteJsonAsync("{\"duplicate\":1,\"duplicate\":2}").GetAwaiter().GetResult());
    Assert.Throws<ArgumentOutOfRangeException>(() => widget.PrivateState
        .ClearAsync(-1).GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() => widget.PrivateState
        .WriteJsonAsync(new string(' ',
            WidgetCommunityPlatformLimits.MaximumPrivateStateInputUtf8Bytes + 1))
        .GetAwaiter().GetResult());

    var fixture = new WidgetTestPrivateState("{\"selected\":\"one\"}", 1);
    var fixtureWidget = WidgetTestHost.Attach(new CapabilityWidget(),
        new WidgetTestHostServicesBuilder().WithPrivateState(fixture).Build());
    Assert.Equal(1L, (await fixtureWidget.PrivateState.ReadJsonAsync()).Revision);
    fixture.SimulateExternalWriteJson("{\"selected\":\"two\"}");
    try
    {
        await fixtureWidget.PrivateState.WriteJsonAsync("{}", expectedRevision: 1);
        throw new InvalidOperationException("Expected deterministic CAS conflict.");
    }
    catch (WidgetCapabilityException exception)
    {
        Assert.Equal("state_conflict", exception.ErrorCode);
    }
}

static async Task TypedPlatformServices()
{
    var services = new WidgetTestHostServicesBuilder()
        .WithResponse(
            WidgetAudioCapabilities.GetSessions,
            (IReadOnlyList<WidgetAudioSession>)[new("audio-1", "Game", 0.75, false, true)])
        .WithResponse(
            WidgetAudioCapabilities.SetSessionVolume,
            new WidgetCapabilityAcknowledgement(true))
        .WithResponse(
            WidgetAudioCapabilities.SetSessionMuted,
            new WidgetCapabilityAcknowledgement(true))
        .WithResponse(
            WidgetAudioCapabilities.GetDevices,
            (IReadOnlyList<WidgetAudioDevice>)[
                new("device-output", "Speakers", WidgetAudioDeviceDirection.Output, true),
                new("device-input", "Microphone", WidgetAudioDeviceDirection.Input, true)])
        .WithResponse(
            WidgetAudioCapabilities.GetInput,
            new WidgetAudioInput(0.4, false))
        .WithResponse(
            WidgetAudioCapabilities.SetInputVolume,
            new WidgetCapabilityAcknowledgement(true))
        .WithResponse(
            WidgetAudioCapabilities.SetInputMuted,
            new WidgetCapabilityAcknowledgement(true))
        .WithResponse(
            WidgetNetworkCapabilities.GetStatus,
            new WidgetNetworkStatus(
                WidgetNetworkConnectivity.Internet,
                WidgetNetworkTransportKind.Wifi,
                WidgetNetworkWirelessAvailability.Available,
                WidgetNetworkDetailsAccess.Available,
                WidgetNetworkConnectionAttemptState.None,
                null,
                "wifi-1",
                "Wi-Fi",
                80))
        .WithResponse(
            WidgetNetworkCapabilities.GetConnectionDetails,
            new WidgetNetworkConnectionDetails(
                4, WidgetNetworkConnectionDetailsState.Available,
                WidgetNetworkConnectionDetailsConnectivity.Internet,
                WidgetNetworkTransportKind.Wifi,
                ["192.0.2.5"], ["192.0.2.1"], ["9.9.9.9"]))
        .WithResponse(
            WidgetNetworkCapabilities.GetSavedProfiles,
            (IReadOnlyList<WidgetSavedNetworkProfile>)[
                new("wifi-1", "Wi-Fi", true, 80)])
        .WithResponse(
            WidgetNetworkCapabilities.SwitchSavedProfile,
            new WidgetCapabilityAcknowledgement(true))
        .WithResponse(
            WidgetNetworkCapabilities.GetWifiRadio,
            new WidgetWifiRadio(WidgetWifiRadioState.On, true))
        .WithResponse(
            WidgetNetworkCapabilities.SetWifiRadio,
            new WidgetCapabilityAcknowledgement(true))
        .WithResponse(
            WidgetNetworkCapabilities.GetBluetooth,
            new WidgetBluetoothSnapshot(
                WidgetBluetoothRadioState.On, true,
                WidgetBluetoothDiscoveryState.Ready,
                [new("bluetooth-1", "Controller", true, true, true)]))
        .WithResponse(
            WidgetNetworkCapabilities.SetBluetoothRadio,
            new WidgetCapabilityAcknowledgement(true))
        .WithResponse(
            WidgetNetworkCapabilities.PairBluetoothDevice,
            new WidgetBluetoothPairingResult(
                WidgetBluetoothPairingOutcome.UserInteractionRequired))
        .WithResponse(
            WidgetNetworkCapabilities.OpenBluetoothDeviceSettings,
            new WidgetCapabilityAcknowledgement(true))
        .WithEvents(
            WidgetAudioCapabilities.SessionsChanged,
            [new WidgetAudioSessionsChanged(
                [new("audio-1", "Game", 0.75, false, true)])])
        .WithEvents(
            WidgetNetworkCapabilities.ConnectionDetailsChanged,
            [new WidgetNetworkConnectionDetailsChanged(5)])
        .Build();
    var widget = WidgetTestHost.Attach(new CapabilityWidget(), services);

    var sessions = await widget.Audio.GetSessionsAsync();
    Assert.Equal("audio-1", sessions.Single().SessionId);
    await widget.Audio.SetSessionVolumeAsync("audio-1", 0.5);
    await widget.Audio.SetSessionMutedAsync("audio-1", true);
    var devices = await widget.Audio.GetDevicesAsync();
    Assert.Equal("device-output", devices.Single(device => device.IsDefault &&
        device.Direction == WidgetAudioDeviceDirection.Output).DeviceId);
    var input = await widget.Audio.GetInputAsync();
    Assert.Equal(0.4, input.Volume);
    await widget.Audio.SetInputVolumeAsync(0.6);
    await widget.Audio.SetInputMutedAsync(true);

    var status = await widget.Network.GetStatusAsync();
    Assert.Equal(WidgetNetworkConnectivity.Internet, status.Connectivity);
    Assert.Equal(WidgetNetworkTransportKind.Wifi, status.Transport);
    Assert.Equal(WidgetNetworkWirelessAvailability.Available, status.WirelessAvailability);
    Assert.Equal(WidgetNetworkDetailsAccess.Available, status.DetailsAccess);
    Assert.Equal(WidgetNetworkConnectionAttemptState.None, status.ConnectionAttemptState);
    var details = await widget.Network.GetConnectionDetailsAsync();
    Assert.Equal(4L, details.Revision);
    Assert.Equal(WidgetNetworkConnectionDetailsConnectivity.Internet,
        details.Connectivity);
    Assert.Equal("192.0.2.5", details.IpAddresses.Single());
    var profiles = await widget.Network.GetSavedProfilesAsync();
    Assert.Equal("wifi-1", profiles.Single().ProfileId);
    await widget.Network.SwitchSavedProfileAsync("wifi-1");
    var radio = await widget.Network.GetWifiRadioAsync();
    Assert.Equal(WidgetWifiRadioState.On, radio.State);
    Assert.True(radio.CanControl, "Typed Wi-Fi radio response lost control availability.");
    await widget.Network.SetWifiRadioAsync(false);
    var bluetooth = await widget.Network.GetBluetoothAsync();
    Assert.Equal(WidgetBluetoothRadioState.On, bluetooth.RadioState);
    Assert.Equal("bluetooth-1", bluetooth.Devices.Single().DeviceId);
    await widget.Network.SetBluetoothRadioAsync(false);
    var pairing = await widget.Network.PairBluetoothDeviceAsync("bluetooth-1");
    Assert.Equal(
        WidgetBluetoothPairingOutcome.UserInteractionRequired, pairing.Outcome);
    await widget.Network.OpenBluetoothDeviceSettingsAsync("bluetooth-1");
    Assert.Throws<ArgumentException>(() => widget.Network
        .PairBluetoothDeviceAsync("native device/id").AsTask().GetAwaiter().GetResult());
    Assert.Throws<ArgumentException>(() => widget.Network
        .OpenBluetoothDeviceSettingsAsync("").AsTask().GetAwaiter().GetResult());

    await using var detailEvents = widget.Network.WatchConnectionDetailsAsync()
        .GetAsyncEnumerator();
    Assert.True(await detailEvents.MoveNextAsync(),
        "Typed network-details event was not forwarded.");
    Assert.Equal(5L, detailEvents.Current.Revision);

    await using var events = widget.Audio.WatchSessionsAsync().GetAsyncEnumerator();
    Assert.True(await events.MoveNextAsync(), "Typed audio event was not forwarded.");
    Assert.Equal("audio-1", events.Current.Sessions.Single().SessionId);
}

static async Task SubscriptionOpenAcknowledges()
{
    var opened = 0;
    var services = new WidgetTestHostServicesBuilder()
        .WithEventStream(
            WidgetAudioCapabilities.SessionsChanged,
            cancellationToken =>
            {
                Interlocked.Increment(ref opened);
                return OneAudioEvent(cancellationToken);
            })
        .Build();
    var widget = WidgetTestHost.Attach(new CapabilityWidget(), services);
    await using var subscription = await widget.Audio.OpenSessionsSubscriptionAsync();
    Assert.Equal(1, Volatile.Read(ref opened));
    await using var reader = subscription.ReadAllAsync().GetAsyncEnumerator();
    Assert.True(await reader.MoveNextAsync(), "Acknowledged subscription did not forward its event.");
    Assert.Equal("audio-ack", reader.Current.Sessions.Single().SessionId);
}

static async IAsyncEnumerable<WidgetAudioSessionsChanged> OneAudioEvent(
    [System.Runtime.CompilerServices.EnumeratorCancellation]
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    yield return new WidgetAudioSessionsChanged(
        [new("audio-ack", "Acknowledged", 0.5, false, true)]);
    await Task.Yield();
}

static Task SnapshotRoundTrip()
{
    var view = new WidgetView(
        UI.Row("root",
            UI.Button("Previous", "previous", "previous").FocusRight("next").Shortcut(ControllerButton.LeftBumper),
            UI.Button("Next", "next", "next").FocusLeft("previous").Shortcut(ControllerButton.RightBumper)),
        "previous");
    var snapshot = view.CreateSnapshot("test.instance", 42);
    var first = SnapshotJson.Serialize(snapshot);
    var second = SnapshotJson.Serialize(snapshot);
    Assert.SequenceEqual(first, second, "Serialization must be byte-for-byte deterministic.");
    var restored = SnapshotJson.Deserialize(first);
    Assert.Equal(42L, restored.Sequence);
    Assert.Equal("previous", restored.InitialFocusId);
    Assert.Equal(2, restored.Root.Children.Count);
    return Task.CompletedTask;
}

static Task ResponsiveVisibilityRoundTrip()
{
    var compact = UI.Stack("compact.branch",
            UI.Text("Compact", "compact.label"),
            UI.Button("Compact action", "compact.activate", "compact.action")
                .Shortcut(ControllerButton.X))
        .Classes("compact-base")
        .VisibleWhen(ResponsiveVisibility.CompactOnly)
        .AddClasses("compact-responsive");
    var expanded = UI.ResponsiveBranch(
        ResponsiveVisibility.ExpandedOnly,
        UI.Stack("expanded.branch",
            UI.Text("Expanded", "expanded.label"),
            UI.Button("Expanded action", "expanded.activate", "expanded.action")
                .Shortcut(ControllerButton.Y)));
    var snapshot = new WidgetView(
        UI.Stack("root", compact, expanded),
        "compact.action").CreateSnapshot("responsive.instance", 9);

    Assert.Equal(ProtocolConstants.ResponsiveVisibilityVersion, snapshot.ProtocolVersion);
    Assert.Equal(ResponsiveVisibility.CompactOnly, snapshot.Root.Children[0].VisibleWhen);
    Assert.Equal(ResponsiveVisibility.ExpandedOnly, snapshot.Root.Children[1].VisibleWhen);
    Assert.True(snapshot.Root.Children[0].StyleClasses.SequenceEqual(
        new[] { "compact-base", "compact-responsive" }, StringComparer.Ordinal),
        "Responsive modifiers must compose with classes before and after the modifier.");
    Assert.Equal("compact.branch", snapshot.Root.Children[0].Id);
    var json = Encoding.UTF8.GetString(SnapshotJson.Serialize(snapshot));
    Assert.True(json.Contains("\"visibleWhen\":\"compactOnly\"", StringComparison.Ordinal),
        "Compact visibility was not serialized with the closed enum value.");
    Assert.True(json.Contains("\"visibleWhen\":\"expandedOnly\"", StringComparison.Ordinal),
        "Expanded visibility was not serialized with the closed enum value.");
    var restored = SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(json));
    Assert.Equal(ResponsiveVisibility.ExpandedOnly, restored.Root.Children[1].VisibleWhen);

    var legacy = snapshot with { ProtocolVersion = ProtocolConstants.ResponsiveGridVersion };
    Assert.True(ViewSnapshotValidator.Validate(legacy).Any(error =>
        error.Code == "feature_requires_version" &&
        error.Path.EndsWith("visibleWhen", StringComparison.Ordinal)),
        "Responsive visibility must fail closed before protocol v9.");
    var invalid = snapshot with
    {
        Root = snapshot.Root with
        {
            Children =
            [
                snapshot.Root.Children[0] with
                {
                    VisibleWhen = (ResponsiveVisibility)999,
                },
            ],
        },
    };
    Assert.True(ViewSnapshotValidator.Validate(invalid).Any(error =>
        error.Code == "invalid_responsive_visibility"),
        "Unknown responsive visibility values must fail closed.");
    var conditionalRoot = snapshot with
    {
        Root = snapshot.Root with { VisibleWhen = ResponsiveVisibility.CompactOnly },
    };
    Assert.True(ViewSnapshotValidator.Validate(conditionalRoot).Any(error =>
        error.Code == "conditional_root_not_allowed"),
        "The root must remain present in every responsive mode.");
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        UI.Text("Invalid", "invalid.visibility")
            .VisibleWhen((ResponsiveVisibility)999));
    return Task.CompletedTask;
}

static Task DuplicateIdsAreRejected()
{
    var view = new WidgetView(UI.Stack("root", UI.Text("One", "same"), UI.Text("Two", "same")));
    var exception = Assert.Throws<ProtocolValidationException>(() => view.CreateSnapshot("test.instance", 0));
    Assert.True(exception.Errors.Any(error => error.Code == "duplicate_id"), "Expected duplicate_id.");
    return Task.CompletedTask;
}

static Task ScrollContainersRoundTrip()
{
    var snapshot = new WidgetView(
        UI.VerticalScroll("sessions",
            UI.Button("Game", "mute-game", "session.game.mute")
                .FocusDown("session.chat.mute"),
            UI.Button("Chat", "mute-chat", "session.chat.mute")
                .FocusUp("session.game.mute"))
            .InputScope("mixer-surface")
            .Shortcut(ControllerButton.B, "close-details"),
        InitialFocusId: "session.game.mute",
        Surface: new WidgetSurfaceHints
        {
            Mode = WidgetSurfaceMode.Compact,
            PreferredWidth = 560,
            PreferredHeight = 420,
            MinimumWidth = 360,
            MinimumHeight = 260,
        }).CreateSnapshot("scroll.instance", 8);

    Assert.Equal(ProtocolConstants.ScrollContainerVersion, snapshot.ProtocolVersion);
    Assert.Equal(ViewNodeKind.Scroll, snapshot.Root.Kind);
    Assert.Equal(ScrollAxis.Vertical, snapshot.Root.ScrollAxis);
    Assert.Equal("mixer-surface", snapshot.ActiveInputScopeId);
    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
    Assert.Equal(WidgetSurfaceMode.Compact, restored.Surface!.Mode);
    Assert.Equal(560D, restored.Surface.PreferredWidth);
    Assert.Equal(360D, restored.Surface.MinimumWidth);
    Assert.Equal("close-details", restored.Root.Shortcuts.Single().ActionId);
    return Task.CompletedTask;
}

static Task ScrollPaginationRoundTrip()
{
    var snapshot = new WidgetView(
        UI.VerticalScroll("paged-list",
                UI.Button("First", "open-first", "first"),
                UI.Button("Last", "open-last", "last"))
            .Paginate("previous-page", "next-page", threshold: 1),
        InitialFocusId: "first")
        .CreateSnapshot("pagination.instance", 1);

    Assert.Equal(ProtocolConstants.ScrollPaginationVersion, snapshot.ProtocolVersion);
    Assert.Equal("previous-page", snapshot.Root.ScrollNearStartActionId);
    Assert.Equal("next-page", snapshot.Root.ScrollNearEndActionId);
    Assert.Equal(1, snapshot.Root.ScrollPaginationThreshold);
    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
    Assert.Equal("next-page", restored.Root.ScrollNearEndActionId);

    var legacy = snapshot with
    {
        ProtocolVersion = ProtocolConstants.SliderActivationVersion,
    };
    Assert.True(ViewSnapshotValidator.Validate(legacy).Any(error =>
            error.Code == "feature_requires_version"),
        "Protocol v10 must reject host-owned scroll pagination.");
    Assert.Throws<ArgumentException>(() => UI.VerticalScroll("missing-actions")
        .Paginate(null, null));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.VerticalScroll("bad-threshold")
        .Paginate(null, "next", threshold: 0));
    return Task.CompletedTask;
}

static Task BaselineProtocolCompatibility()
{
    var baseline = new WidgetView(
        UI.Stack("root",
            UI.Text("Community widget", "title"),
            UI.Button("Refresh", "refresh", "refresh")),
        InitialFocusId: "refresh")
        .CreateSnapshot("community.clock", 1);
    Assert.Equal(ProtocolConstants.BaselineVersion, baseline.ProtocolVersion);
    var json = Encoding.UTF8.GetString(SnapshotJson.Serialize(baseline));
    Assert.True(!json.Contains("\"surface\"", StringComparison.Ordinal),
        "Baseline snapshots must not emit v2 surface fields.");
    Assert.True(!json.Contains("\"scrollAxis\"", StringComparison.Ordinal),
        "Baseline snapshots must not emit v2 scroll fields.");
    var restored = SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(json));
    Assert.Equal(ProtocolConstants.BaselineVersion, restored.ProtocolVersion);
    Assert.Equal(ViewNodeKind.Stack, restored.Root.Kind);
    return Task.CompletedTask;
}

static Task ScrollAndSurfaceValidation()
{
    var legacyScroll = new WidgetView(UI.VerticalScroll("scroll"))
        .CreateSnapshot("scroll.instance", 1) with { ProtocolVersion = 1 };
    Assert.True(ViewSnapshotValidator.Validate(legacyScroll)
        .Any(error => error.Code == "feature_requires_version"),
        "Protocol v1 must reject the v2 scroll feature.");

    var missingAxis = new ViewSnapshot
    {
        Sequence = 1,
        WidgetInstanceId = "scroll.instance",
        ActiveInputScopeId = "scroll",
        Root = new ViewNode { Id = "scroll", Kind = ViewNodeKind.Scroll },
    };
    Assert.True(ViewSnapshotValidator.Validate(missingAxis)
        .Any(error => error.Code == "required" && error.Path.EndsWith("scrollAxis", StringComparison.Ordinal)),
        "Scroll without an axis must fail closed.");

    var invalidSurface = new WidgetView(
        UI.Stack("root"),
        Surface: new WidgetSurfaceHints
        {
            Mode = (WidgetSurfaceMode)999,
            PreferredWidth = double.NaN,
            MinimumWidth = 800,
            MinimumHeight = 700,
        });
    var exception = Assert.Throws<ProtocolValidationException>(() =>
        invalidSurface.CreateSnapshot("surface.instance", 1));
    Assert.True(exception.Errors.Any(error => error.Code == "invalid_surface_mode"),
        "Unknown surface modes must fail closed.");
    Assert.True(exception.Errors.Any(error => error.Code == "incomplete_surface_size"),
        "Partial preferred dimensions must fail closed.");
    Assert.True(exception.Errors.Any(error => error.Code == "invalid_surface_size"),
        "Non-finite dimensions must fail closed.");

    var inverted = new WidgetView(
        UI.Stack("root"),
        Surface: new WidgetSurfaceHints
        {
            PreferredWidth = 400,
            PreferredHeight = 300,
            MinimumWidth = 500,
            MinimumHeight = 350,
        });
    var invertedException = Assert.Throws<ProtocolValidationException>(() =>
        inverted.CreateSnapshot("surface.instance", 2));
    Assert.True(invertedException.Errors.Count(error =>
        error.Code == "surface_minimum_exceeds_preferred") == 2,
        "Minimum dimensions cannot exceed preferred dimensions.");
    return Task.CompletedTask;
}

static Task SurfaceAxisModesRoundTrip()
{
    var preferred = new WidgetView(
        UI.Stack("preferred-root"),
        Surface: new WidgetSurfaceHints
        {
            Mode = WidgetSurfaceMode.Standard,
            PreferredWidth = 880,
            PreferredHeight = 520,
            MinimumWidth = 420,
            MinimumHeight = 280,
        }).CreateSnapshot("surface.preferred", 1);
    Assert.Equal(ProtocolConstants.SurfaceHintsVersion, preferred.ProtocolVersion);
    var preferredJson = Encoding.UTF8.GetString(SnapshotJson.Serialize(preferred));
    Assert.True(!preferredJson.Contains("\"widthMode\"", StringComparison.Ordinal) &&
                !preferredJson.Contains("\"heightMode\"", StringComparison.Ordinal),
        "Default Preferred axes must preserve the protocol-v2 wire shape.");
    var restoredPreferred = SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(preferredJson));
    Assert.Equal(WidgetSurfaceAxisMode.Preferred, restoredPreferred.Surface!.WidthMode);
    Assert.Equal(WidgetSurfaceAxisMode.Preferred, restoredPreferred.Surface.HeightMode);

    var independent = new WidgetView(
        UI.Stack("content-root", UI.Text("Measured copy", "copy")),
        Surface: preferred.Surface! with
        {
            WidthMode = WidgetSurfaceAxisMode.FillAvailable,
            HeightMode = WidgetSurfaceAxisMode.Content,
        }).CreateSnapshot("surface.axes", 2);
    Assert.Equal(ProtocolConstants.SurfaceAxisSizingVersion, independent.ProtocolVersion);
    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(independent));
    Assert.Equal(WidgetSurfaceAxisMode.FillAvailable, restored.Surface!.WidthMode);
    Assert.Equal(WidgetSurfaceAxisMode.Content, restored.Surface.HeightMode);

    var legacy = independent with
    {
        ProtocolVersion = ProtocolConstants.SurfaceAxisSizingVersion - 1,
    };
    Assert.True(ViewSnapshotValidator.Validate(legacy).Any(error =>
            error.Code == "feature_requires_version"),
        "A non-Preferred surface axis must not cross the protocol-v16 boundary.");

    var invalid = independent with
    {
        Surface = independent.Surface! with
        {
            WidthMode = (WidgetSurfaceAxisMode)999,
            HeightMode = (WidgetSurfaceAxisMode)(-1),
        },
    };
    Assert.Equal(2, ViewSnapshotValidator.Validate(invalid).Count(error =>
        error.Code == "invalid_surface_axis_mode"));

    var unknownField = Encoding.UTF8.GetString(SnapshotJson.Serialize(independent))
        .Replace("\"widthMode\":\"fillAvailable\"",
            "\"widthMode\":\"fillAvailable\",\"axisOwner\":\"widget\"",
            StringComparison.Ordinal);
    Assert.Throws<JsonException>(() =>
        SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(unknownField)));
    return Task.CompletedTask;
}

static Task BrokenFocusIsRejected()
{
    var view = new WidgetView(UI.Stack("root", UI.Button("Go", "go", "go").FocusDown("missing")), "go");
    var exception = Assert.Throws<ProtocolValidationException>(() => view.CreateSnapshot("test.instance", 0));
    Assert.True(exception.Errors.Any(error => error.Code == "invalid_focus_target"), "Expected invalid_focus_target.");
    return Task.CompletedTask;
}

static Task InvalidProgressIsRejected()
{
    var view = new WidgetView(UI.Stack("root", UI.Progress(11, 10, "progress")));
    var exception = Assert.Throws<ProtocolValidationException>(() => view.CreateSnapshot("test.instance", 0));
    Assert.True(exception.Errors.Any(error => error.Code == "invalid_progress"), "Expected invalid_progress.");
    return Task.CompletedTask;
}

static Task SliderV3RoundTrip()
{
    var snapshot = new WidgetView(
        UI.Stack("root",
            UI.Slider(0.75, 0, 1, 0.05, "volume.changed", "volume",
                    "Game volume, unmuted, press A to mute", "75 percent", "volume.mute")
                .FocusUp("previous")
                .Busy(),
            UI.Button("Previous", "previous", "previous")),
        "volume").CreateSnapshot("slider.instance", 7);
    Assert.Equal(ProtocolConstants.SliderVersion, snapshot.ProtocolVersion);
    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
    var slider = Find(restored.Root, "volume");
    Assert.Equal(ViewNodeKind.Slider, slider.Kind);
    Assert.Equal(0d, slider.Minimum);
    Assert.Equal(1d, slider.Maximum);
    Assert.Equal(0.75d, slider.Value);
    Assert.Equal(0.05d, slider.Step);
    Assert.Equal("volume.changed", slider.ValueChangedActionId);
    Assert.Equal("volume.mute", slider.ActionId);
    Assert.Equal("75 percent", slider.AccessibilityValue);
    Assert.True(slider.IsFocusable, "Slider must be a controller focus target.");

    var legacy = new WidgetView(UI.Stack("root", UI.Button("Go", "go", "go")))
        .CreateSnapshot("legacy.instance", 1);
    Assert.Equal(ProtocolConstants.BaselineVersion, legacy.ProtocolVersion);
    return Task.CompletedTask;
}

static Task SliderActivationModeRoundTrip()
{
    var snapshot = new WidgetView(
        UI.Stack("root",
            UI.Button("Previous", "previous", "previous"),
            UI.Slider(0.5, 0, 1, 0.1, "volume.changed", "volume", "Volume")
                .RequireControllerActivation()
                .FocusLeft("previous")
                .FocusRight("next"),
            UI.Button("Next", "next", "next")),
        "volume").CreateSnapshot("slider.activation", 1);

    Assert.Equal(ProtocolConstants.SliderActivationVersion, snapshot.ProtocolVersion);
    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
    var slider = Find(restored.Root, "volume");
    Assert.Equal(SliderInteractionMode.ActivateToAdjust, slider.SliderInteractionMode);
    Assert.Equal("previous", slider.Focus!.Left);
    Assert.Equal("next", slider.Focus.Right);

    var scrubber = UI.Scrubber(
            TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3),
            TimeSpan.FromSeconds(5), "seek", "timeline")
        .RequireControllerActivation()
        .ToProtocolNode();
    Assert.Equal(
        SliderInteractionMode.ActivateToAdjust,
        Find(scrubber, "timeline.slider").SliderInteractionMode);

    var conflict = Assert.Throws<ProtocolValidationException>(() =>
        new WidgetView(UI.Stack("root",
            UI.Slider(0.5, 0, 1, 0.1, "change", "conflict", "Volume")
                .Activate("mute")
                .RequireControllerActivation()))
            .CreateSnapshot("slider.activation", 2));
    Assert.True(conflict.Errors.Any(error =>
            error.Code == "slider_activation_action_conflict"),
        "Activation-first Slider must reserve A instead of also publishing an activation action.");

    var oldWire = snapshot with { ProtocolVersion = ProtocolConstants.ResponsiveVisibilityVersion };
    Assert.True(ViewSnapshotValidator.Validate(oldWire).Any(error =>
            error.Code == "feature_requires_version" &&
            error.Path.EndsWith(".sliderInteractionMode", StringComparison.Ordinal)),
        "Activation-first mode must not be silently accepted by a protocol-v9 host.");
    return Task.CompletedTask;
}

static Task InvalidSlidersAreRejected()
{
    foreach (var slider in new[]
             {
                 UI.Slider(double.NaN, 0, 1, 0.1, "change", "nan", "Volume"),
                 UI.Slider(0.5, 1, 1, 0.1, "change", "empty", "Volume"),
                 UI.Slider(0.5, 0, 1, double.PositiveInfinity, "change", "infinite", "Volume"),
                 UI.Slider(0.5, 0, 1, 2, "change", "oversized", "Volume"),
                 UI.Slider(0, -double.MaxValue, double.MaxValue, 1,
                     "change", "overflowing-range", "Volume"),
             })
    {
        var error = Assert.Throws<ProtocolValidationException>(() =>
            new WidgetView(UI.Stack("root", slider)).CreateSnapshot("slider.invalid", 1));
        Assert.True(error.Errors.Any(item => item.Code == "invalid_slider_range"),
            "Invalid Slider range must fail closed.");
    }

    var conflicting = new ViewSnapshot
    {
        ProtocolVersion = ProtocolConstants.SliderVersion,
        Sequence = 1,
        WidgetInstanceId = "slider.invalid",
        ActiveInputScopeId = "root",
        InitialFocusId = "slider",
        Root = new ViewNode
        {
            Id = "root",
            Kind = ViewNodeKind.Stack,
            Children =
            [
                new ViewNode
                {
                    Id = "slider", Kind = ViewNodeKind.Slider,
                    Value = 0.5, Minimum = 0, Maximum = 1, Step = 0.1,
                    ValueChangedActionId = "change",
                    AccessibilityLabel = "Volume", AccessibilityValue = "50 percent",
                    Focus = new FocusNeighbors(Left: "other"),
                },
                new ViewNode { Id = "other", Kind = ViewNodeKind.Button, Text = "Other", ActionId = "other" },
            ],
        },
    };
    var focusErrors = ViewSnapshotValidator.Validate(conflicting);
    Assert.True(focusErrors.Any(item => item.Code == "slider_horizontal_focus_not_allowed"),
        "Slider horizontal neighbor must be rejected because Left/Right adjust value.");
    var oldWire = conflicting with { ProtocolVersion = ProtocolConstants.ScrollContainerVersion };
    Assert.True(ViewSnapshotValidator.Validate(oldWire).Any(item => item.Code == "feature_requires_version"),
        "Slider must not be silently overloaded onto protocol v2.");
    return Task.CompletedTask;
}

static Task VisualNodesRoundTrip()
{
    var snapshot = new WidgetView(
        UI.Row("root",
            UI.Image("https://images.example/cover.jpg", "cover", "Album cover", ImageFit.Contain)
                .Classes("album-cover"),
            UI.Icon(WidgetGlyph.Music, "music-icon", "Music").Classes("media-glyph")))
        .CreateSnapshot("test.instance", 3);

    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
    var cover = Find(restored.Root, "cover");
    Assert.Equal(ViewNodeKind.Image, cover.Kind);
    Assert.Equal("https://images.example/cover.jpg", cover.ImageSource);
    Assert.Equal(ImageFit.Contain, cover.ImageFit);
    Assert.Equal("Album cover", cover.AccessibilityLabel);
    Assert.Equal("album-cover", cover.StyleClasses.Single());
    var icon = Find(restored.Root, "music-icon");
    Assert.Equal(ViewNodeKind.Icon, icon.Kind);
    Assert.Equal(WidgetGlyph.Music, icon.Glyph);
    Assert.Equal("Music", icon.AccessibilityLabel);
    return Task.CompletedTask;
}

static Task InputSurfacesValidate()
{
    var valid = new WidgetView(
        UI.Stack("root",
            UI.Button("Root back", "root-back", "root-back").Shortcut(ControllerButton.LeftBumper),
            UI.Stack("dialog",
                UI.Button("Dialog back", "dialog-back", "dialog-back").Shortcut(ControllerButton.LeftBumper))
                .InputScope("dialog-window")
                .Shortcut(ControllerButton.B, "close-dialog")),
        InitialFocusId: "dialog-back",
        ActiveInputScopeId: "dialog-window")
        .CreateSnapshot("scope.instance", 1);
    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(valid));
    Assert.Equal("dialog-window", restored.Root.Children[1].InputScopeId);
    Assert.Equal("close-dialog", restored.Root.Children[1].Shortcuts.Single().ActionId);
    Assert.Equal("dialog-back", restored.InitialFocusId);

    var repeatedRows = new WidgetView(
        UI.Stack("root",
            UI.Button("One", "one", "one").Shortcut(ControllerButton.RightBumper),
            UI.Button("Two", "two", "two").Disabled().Shortcut(ControllerButton.RightBumper)));
    _ = repeatedRows.CreateSnapshot("scope.instance", 2);

    var crossScopeFocus = new WidgetView(
        UI.Stack("root",
            UI.Button("Outside", "outside", "outside").FocusRight("inside"),
            UI.Stack("dialog", UI.Button("Inside", "inside", "inside"))
                .InputScope("dialog-window")));
    var focusException = Assert.Throws<ProtocolValidationException>(() =>
        crossScopeFocus.CreateSnapshot("scope.instance", 3));
    Assert.True(focusException.Errors.Any(error => error.Code == "cross_input_scope_focus"),
        "Focus graphs must remain inside their owning input surface.");

    var unreachable = new WidgetView(UI.Stack("root")
        .Shortcut(ControllerButton.A, "reserved")
        .Shortcut(ControllerButton.DPadLeft, "reserved-dpad")
        .Shortcut(ControllerButton.B, "released", ControllerEventPhase.Released)
        .Shortcut(ControllerButton.X, "repeated", ControllerEventPhase.Repeated));
    var unreachableException = Assert.Throws<ProtocolValidationException>(() =>
        unreachable.CreateSnapshot("scope.instance", 4));
    Assert.True(unreachableException.Errors.Any(error => error.Code == "reserved_shortcut_button"),
        "A and D-pad shortcut bindings must be rejected.");
    Assert.True(unreachableException.Errors.Count(error => error.Code == "unsupported_shortcut_phase") == 2,
        "Released and repeated shortcut phases must be rejected for the MVP host.");

    var wrongInitialScope = new WidgetView(
        UI.Stack("root",
            UI.Button("Outside", "outside", "outside"),
            UI.Stack("dialog", UI.Button("Inside", "inside", "inside"))
                .InputScope("dialog-window")),
        InitialFocusId: "outside",
        ActiveInputScopeId: "dialog-window");
    var initialException = Assert.Throws<ProtocolValidationException>(() =>
        wrongInitialScope.CreateSnapshot("scope.instance", 5));
    Assert.True(initialException.Errors.Any(error => error.Code == "initial_focus_outside_active_scope"),
        "Initial focus must belong to the explicitly active scope.");

    var invalidActive = new WidgetView(UI.Stack("root"), ActiveInputScopeId: "missing-window");
    var activeException = Assert.Throws<ProtocolValidationException>(() =>
        invalidActive.CreateSnapshot("scope.instance", 6));
    Assert.True(activeException.Errors.Any(error => error.Code == "invalid_active_input_scope"),
        "The active input scope must exist.");

    var dashboardButtons = new WidgetView(
        UI.Stack("root"),
        QuickActions:
        [
            new WidgetQuickAction(ControllerButton.Menu, "menu", "Menu"),
            new WidgetQuickAction(ControllerButton.View, "view", "View"),
        ]);
    _ = dashboardButtons.CreateSnapshot("scope.instance", 7);
    foreach (var reserved in new[]
             {
                 ControllerButton.A, ControllerButton.B, ControllerButton.Y,
                 ControllerButton.DPadUp, ControllerButton.DPadDown,
                 ControllerButton.DPadLeft, ControllerButton.DPadRight,
             })
    {
        var reservedView = new WidgetView(
            UI.Stack("root"),
            QuickActions: [new WidgetQuickAction(reserved, "reserved", "Reserved")]);
        var reservedException = Assert.Throws<ProtocolValidationException>(() =>
            reservedView.CreateSnapshot("scope.instance", 8));
        Assert.True(reservedException.Errors.Any(error => error.Code == "reserved_button"),
            $"Dashboard button {reserved} must remain host-owned.");
    }
    return Task.CompletedTask;
}

static Task UnsafeImageSourcesAreRejected()
{
    foreach (var source in new[]
             {
                 "http://images.example/cover.jpg",
                 "file:///C:/cover.jpg",
                 "javascript:alert(1)",
                 "https://user:secret@images.example/cover.jpg",
             })
    {
        var view = new WidgetView(UI.Stack("root", UI.Image(source, "cover", "Album cover")));
        var exception = Assert.Throws<ProtocolValidationException>(() => view.CreateSnapshot("test.instance", 0));
        Assert.True(exception.Errors.Any(error => error.Code == "invalid_image_source"), $"Expected '{source}' to be rejected.");
    }
    return Task.CompletedTask;
}

static Task DashboardAuthorityContract()
{
    var capability = new WidgetQuickActionCapability(
        "system.media.sessions.control.v1", "media.session.control");
    var view = new WidgetView(
        UI.Stack("root"),
        QuickActions:
        [new WidgetQuickAction(ControllerButton.X, "toggle", "Play or pause", capability)]);
    var snapshot = view.CreateSnapshot("authority.instance", 4);
    Assert.Equal(ProtocolConstants.DashboardGestureAuthorityVersion, snapshot.ProtocolVersion);
    byte[] payload;
    try
    {
        payload = SnapshotJson.Serialize(snapshot);
    }
    catch (ProtocolValidationException exception)
    {
        throw new InvalidOperationException("serialize: " + string.Join("; ",
            exception.Errors.Select(error =>
                $"{error.Path} {error.Code}: {error.Message}")), exception);
    }
    ViewSnapshot roundTrip;
    try
    {
        roundTrip = SnapshotJson.Deserialize(payload);
    }
    catch (ProtocolValidationException exception)
    {
        throw new InvalidOperationException("deserialize: " + string.Join("; ", exception.Errors.Select(error =>
            $"{error.Path} {error.Code}: {error.Message}")), exception);
    }
    Assert.Equal(capability, roundTrip.QuickActions.Single().Capability);

    var scopedCapability = new WidgetQuickActionCapability(
        "network.loopback:13091", "loopback.http.post-json");
    var scoped = new WidgetView(
        UI.Stack("root"),
        QuickActions:
        [new WidgetQuickAction(ControllerButton.X, "next", "Next track", scopedCapability)])
        .CreateSnapshot("authority.instance", 5);
    Assert.Equal(scopedCapability, scoped.QuickActions.Single().Capability);

    var legacy = snapshot with { ProtocolVersion = ProtocolConstants.SliderVersion };
    Assert.True(ViewSnapshotValidator.Validate(legacy).Any(error =>
        error.Code == "feature_requires_version"),
        "Capability authority must require its protocol version.");
    var malformed = snapshot with
    {
        QuickActions =
        [new WidgetQuickAction(
            ControllerButton.X, "toggle", "Play or pause",
            new WidgetQuickActionCapability("", new string('x', 129)))]
    };
    var errors = ViewSnapshotValidator.Validate(malformed);
    Assert.True(errors.Any(error => error.Path.EndsWith("capabilityId", StringComparison.Ordinal) &&
        error.Code == "required"), "Empty capability IDs must fail closed.");
    Assert.True(errors.Any(error => error.Path.EndsWith("operationId", StringComparison.Ordinal) &&
        error.Code == "too_long"), "Unbounded operation IDs must fail closed.");
    var invalidScoped = scoped with
    {
        QuickActions =
        [new WidgetQuickAction(
            ControllerButton.X, "next", "Next track",
            new WidgetQuickActionCapability("network.loopback:13091/path", "loopback.http.post-json"))]
    };
    Assert.True(ViewSnapshotValidator.Validate(invalidScoped).Any(error =>
            error.Path.EndsWith("capabilityId", StringComparison.Ordinal) &&
            error.Code == "invalid_identifier"),
        "Capability qualifiers must remain bounded ASCII tokens.");
    return Task.CompletedTask;
}

static Task VisualNodeRequirementsAreEnforced()
{
    var image = new WidgetView(UI.Stack("root", UI.Image("https://images.example/cover.jpg", "cover", "")));
    var imageException = Assert.Throws<ProtocolValidationException>(() => image.CreateSnapshot("test.instance", 0));
    Assert.True(imageException.Errors.Any(error => error.Path.EndsWith("accessibilityLabel", StringComparison.Ordinal)), "Expected image accessibility label validation.");

    var icon = new ViewSnapshot
    {
        Sequence = 0,
        WidgetInstanceId = "test.instance",
        ActiveInputScopeId = "icon",
        Root = new ViewNode
        {
            Id = "icon",
            Kind = ViewNodeKind.Icon,
            AccessibilityLabel = "Music",
        },
    };
    var iconErrors = ViewSnapshotValidator.Validate(icon);
    Assert.True(iconErrors.Any(error => error.Path == "$.root.glyph" && error.Code == "required"), "Expected icon glyph validation.");

    var unknownGlyph = icon with { Root = icon.Root with { Glyph = (WidgetGlyph)999 } };
    Assert.True(ViewSnapshotValidator.Validate(unknownGlyph).Any(error => error.Code == "invalid_glyph"), "Expected the closed glyph set to reject unknown numeric values.");
    var unknownFit = icon with
    {
        Root = icon.Root with
        {
            Kind = ViewNodeKind.Image,
            ImageSource = "https://images.example/cover.jpg",
            ImageFit = (ImageFit)999,
            Glyph = null,
        },
    };
    Assert.True(ViewSnapshotValidator.Validate(unknownFit).Any(error => error.Code == "invalid_image_fit"), "Expected unknown image fit values to be rejected.");
    return Task.CompletedTask;
}

static Task ButtonStatesRoundTrip()
{
    var snapshot = new WidgetView(
        UI.Row("root",
            UI.Button("Normal", "normal", "normal"),
            UI.Button("Unavailable", "disabled", "disabled").Disabled(),
            UI.Button("Current", "selected", "selected").Selected(),
            UI.Button("Saving", "busy", "busy").Busy()))
        .CreateSnapshot("test.instance", 7);

    var first = SnapshotJson.Serialize(snapshot);
    var second = SnapshotJson.Serialize(snapshot);
    Assert.SequenceEqual(first, second, "State serialization must be byte-for-byte deterministic.");
    var json = Encoding.UTF8.GetString(first);
    Assert.True(json.Contains("\"isDisabled\":true", StringComparison.Ordinal), "Disabled state was not serialized.");
    Assert.True(json.Contains("\"isSelected\":true", StringComparison.Ordinal), "Selected state was not serialized.");
    Assert.True(json.Contains("\"isBusy\":true", StringComparison.Ordinal), "Busy state was not serialized.");
    Assert.True(!json.Contains("\"isDisabled\":false", StringComparison.Ordinal), "Default false state should be omitted.");

    var restored = SnapshotJson.Deserialize(first);
    Assert.Equal(true, Find(restored.Root, "disabled").IsDisabled);
    Assert.Equal(true, Find(restored.Root, "selected").IsSelected);
    Assert.Equal(true, Find(restored.Root, "busy").IsBusy);
    Assert.Equal(null, Find(restored.Root, "normal").IsDisabled);
    return Task.CompletedTask;
}

static Task ButtonIconsRoundTrip()
{
    var snapshot = new WidgetView(
        UI.Row("root",
            UI.Button("Previous", "previous-track", "previous")
                .Icon(WidgetGlyph.Previous, "Previous track"),
            UI.Button("Pause", "toggle-playback", "play-pause")
                .Icon(WidgetGlyph.Pause),
            UI.Button("Repeat one", "repeat", "repeat-one")
                .Icon(WidgetGlyph.RepeatOne)))
        .CreateSnapshot("test.instance", 8);

    Assert.Equal(ProtocolConstants.RepeatOneGlyphVersion, snapshot.ProtocolVersion);
    var restored = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
    Assert.Equal(WidgetGlyph.Previous, Find(restored.Root, "previous").Glyph);
    Assert.Equal("Previous track", Find(restored.Root, "previous").AccessibilityLabel);
    Assert.Equal(WidgetGlyph.Pause, Find(restored.Root, "play-pause").Glyph);
    Assert.Equal(WidgetGlyph.RepeatOne, Find(restored.Root, "repeat-one").Glyph);

    var legacy = snapshot with { ProtocolVersion = ProtocolConstants.ScrollPaginationVersion };
    Assert.True(ViewSnapshotValidator.Validate(legacy).Any(error =>
            error.Code == "feature_requires_version" &&
            error.Path.EndsWith("glyph", StringComparison.Ordinal)),
        "Protocol v11 must reject the Repeat One glyph.");

    var invalid = snapshot with
    {
        Root = snapshot.Root with
        {
            Children = [snapshot.Root.Children[0] with { Glyph = (WidgetGlyph)999 }],
        },
    };
    Assert.True(
        ViewSnapshotValidator.Validate(invalid).Any(error => error.Code == "invalid_glyph"),
        "Buttons must use the same closed semantic glyph set as icon nodes.");
    return Task.CompletedTask;
}

static Task SettingsCompositesAreSemantic()
{
    var snapshot = new WidgetView(
        UI.Stack("settings-root",
            UI.ToggleButton("Reduced motion", true, "toggle-motion", "motion-toggle"),
            UI.ToggleButton("Bold text", false, "toggle-bold", "bold-toggle"),
            UI.Stepper("Text scale", "110%", "text-smaller", "text-larger", "text-scale",
                canDecrement: false)),
        "motion-toggle").CreateSnapshot("settings.test", 1);

    var toggle = Find(snapshot.Root, "motion-toggle");
    Assert.Equal("Reduced motion: On", toggle.Text);
    Assert.Equal("Reduced motion, On", toggle.AccessibilityLabel);
    Assert.Equal(true, toggle.IsSelected);
    Assert.Equal(null, toggle.Glyph);
    Assert.Equal("setting-toggle", toggle.StyleClasses.Single());

    var offToggle = Find(snapshot.Root, "bold-toggle");
    Assert.Equal("Bold text: Off", offToggle.Text);
    Assert.Equal("Bold text, Off", offToggle.AccessibilityLabel);
    Assert.Equal(null, offToggle.IsSelected);
    Assert.Equal(null, offToggle.Glyph);

    var explicitIcon = UI.Button("Liked", "like", "liked")
        .Icon(WidgetGlyph.Like, "Like track")
        .Selected()
        .ToProtocolNode();
    Assert.Equal(WidgetGlyph.Like, explicitIcon.Glyph);
    Assert.Equal(true, explicitIcon.IsSelected);

    var decrement = Find(snapshot.Root, "text-scale.decrement");
    var increment = Find(snapshot.Root, "text-scale.increment");
    Assert.Equal(true, decrement.IsDisabled);
    Assert.Equal("text-scale.increment", decrement.Focus!.Right);
    Assert.Equal("text-scale.decrement", increment.Focus!.Left);
    Assert.Equal("Text scale: 110%", Find(snapshot.Root, "text-scale.value").AccessibilityLabel);
    return Task.CompletedTask;
}

static Task ModernComponentsAreSemantic()
{
    var iconButton = UI.IconButton(
            WidgetGlyph.Settings,
            "open-settings",
            "modern.settings",
            "Open settings",
            IconButtonVariant.Quiet,
            IconButtonSize.Large)
        .AddClasses("widget-accent", "wrail-icon-button");
    var view = new WidgetView(
        UI.Stack("modern.root",
            iconButton,
            UI.Card("modern.card", CardVariant.Subtle,
                UI.SectionHeader(
                    "Connections",
                    "modern.header",
                    eyebrow: "Control center",
                    description: "Saved networks",
                    trailing: UI.StatusBadge("Online", StatusTone.Success, "modern.status")),
                UI.Divider("modern.divider"),
                UI.Alert(
                    "Wi-Fi unavailable",
                    "Turn on Wi-Fi to connect.",
                    AlertTone.Warning,
                    "modern.alert",
                    new ComponentAction("Retry", "retry-network", WidgetGlyph.Refresh)),
                UI.EmptyState(
                    "No devices",
                    "Connect a device to continue.",
                    "modern.empty"))),
        InitialFocusId: "modern.settings");

    var snapshot = view.CreateSnapshot("modern.components", 1);
    Assert.Equal(ProtocolConstants.BaselineVersion, snapshot.ProtocolVersion);
    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
    var button = Find(snapshot.Root, "modern.settings");
    Assert.Equal(string.Empty, button.Text);
    Assert.Equal("Open settings", button.AccessibilityLabel);
    Assert.Equal(WidgetGlyph.Settings, button.Glyph);
    Assert.True(
        new[] { "wrail-icon-button", "wrail-icon-button--large", "wrail-icon-button--quiet", "widget-accent" }
            .SequenceEqual(button.StyleClasses),
        "AddClasses must preserve ordered semantic component classes and remove duplicates.");
    Assert.True(Find(snapshot.Root, "modern.card").StyleClasses.Contains("wrail-card--subtle"),
        "Card variant class was omitted.");
    Assert.Equal("Connections", Find(snapshot.Root, "modern.header.title").Text);
    var headerContent = Find(snapshot.Root, "modern.header.content");
    Assert.Equal(ViewNodeKind.Row, headerContent.Kind);
    Assert.Equal("modern.header.text", headerContent.Children[0].Id);
    Assert.Equal(ViewNodeKind.Stack, headerContent.Children[0].Kind);
    Assert.True(headerContent.Children[0].StyleClasses.Contains("wrail-section-header__text"),
        "Section-header text must remain a vertical stack beside optional trailing content.");
    Assert.Equal("modern.header.trailing", headerContent.Children[1].Id);
    Assert.Equal(WidgetGlyph.Check, Find(snapshot.Root, "modern.status.icon").Glyph);
    Assert.Equal("Success status", Find(snapshot.Root, "modern.status.icon").AccessibilityLabel);
    Assert.True(Find(snapshot.Root, "modern.status.label").StyleClasses.Contains("wrail-badge__label--success"),
        "Badge tone did not reach its semantic label.");
    Assert.Equal(ViewNodeKind.Spacer, Find(snapshot.Root, "modern.divider").Kind);
    Assert.Equal("retry-network", Find(snapshot.Root, "modern.alert.action").ActionId);
    Assert.Equal("warning alert", Find(snapshot.Root, "modern.alert.icon").AccessibilityLabel);
    Assert.Equal("No devices", Find(snapshot.Root, "modern.empty.title").Text);
    Assert.Equal("Empty state", Find(snapshot.Root, "modern.empty.icon").AccessibilityLabel);

    var info = UI.Alert("Connected", "No action needed.", AlertTone.Info, "modern.info")
        .ToProtocolNode();
    Assert.Equal(null, FindOrNull(info, "modern.info.icon"));
    var customInfo = UI.Alert(
            "Connected",
            "Ethernet is active.",
            AlertTone.Info,
            "modern.custom-info",
            glyph: WidgetGlyph.Connection)
        .ToProtocolNode();
    Assert.Equal("info alert", Find(customInfo, "modern.custom-info.icon").AccessibilityLabel);

    Assert.Throws<ArgumentException>(() => UI.IconButton(
        WidgetGlyph.Settings, "action", "id", ""));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Card(
        "bad-card", (CardVariant)999));
    return Task.CompletedTask;
}

static Task ModernControllerComponentsAreSemantic()
{
    var tabs = UI.SegmentedTabs(
        "modern.tabs",
        "modern.tabs.audio",
        new SegmentedTab("modern.tabs.audio", "Audio", "select-audio"),
        new SegmentedTab("modern.tabs.voice", "Voice", "select-voice", IsDisabled: true),
        new SegmentedTab("modern.tabs.output", "Output", "select-output", "Output devices"));
    var enabledSwitch = UI.Switch("Reduce motion", true, "toggle-motion", "modern.motion");
    var disabledSwitch = UI.Switch("HDR", false, "toggle-hdr", "modern.hdr", isDisabled: true);
    var dialog = UI.ScopedDialog(
        "Confirm reset",
        "modern.dialog",
        "modern.dialog.scope",
        "dismiss-dialog",
        UI.Text("Your settings will return to defaults.", "modern.dialog.message"),
        UI.Button("Reset", "confirm-reset", "modern.dialog.confirm"));
    var snapshot = new WidgetView(
        UI.Stack("modern.controller.root", tabs, enabledSwitch, disabledSwitch, dialog),
        InitialFocusId: "modern.dialog.confirm",
        ActiveInputScopeId: "modern.dialog.scope")
        .CreateSnapshot("modern.controller", 2);

    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
    var audio = Find(snapshot.Root, "modern.tabs.audio");
    var voice = Find(snapshot.Root, "modern.tabs.voice");
    var output = Find(snapshot.Root, "modern.tabs.output");
    Assert.Equal(true, audio.IsSelected);
    Assert.Equal("Audio, Selected", audio.AccessibilityLabel);
    Assert.Equal("modern.tabs.output", audio.Focus!.Left);
    Assert.Equal("modern.tabs.voice", audio.Focus!.Right);
    Assert.Equal(true, voice.IsDisabled);
    Assert.Equal("modern.tabs.audio", voice.Focus!.Left);
    Assert.Equal("modern.tabs.output", voice.Focus.Right);
    Assert.Equal("Output devices, Not selected", output.AccessibilityLabel);
    Assert.Equal("modern.tabs.voice", output.Focus!.Left);
    Assert.Equal("modern.tabs.audio", output.Focus.Right);
    Assert.Equal(true, Find(snapshot.Root, "modern.motion").IsSelected);
    Assert.Equal(WidgetGlyph.Check, Find(snapshot.Root, "modern.motion").Glyph);
    Assert.Equal(true, Find(snapshot.Root, "modern.hdr").IsDisabled);
    Assert.Equal("modern.dialog.scope", Find(snapshot.Root, "modern.dialog").InputScopeId);
    Assert.Equal("dismiss-dialog", Find(snapshot.Root, "modern.dialog").Shortcuts.Single().ActionId);
    Assert.Equal(ControllerButton.B, Find(snapshot.Root, "modern.dialog").Shortcuts.Single().Button);
    Assert.Equal("Confirm reset", Find(snapshot.Root, "modern.dialog.title").Text);
    Assert.Equal("modern.dialog.confirm", Find(snapshot.Root, "modern.dialog.content").Children[1].Id);

    Assert.Throws<ArgumentException>(() => UI.SegmentedTabs(
        "tabs", "one", new SegmentedTab("one", "One", "select-one")));
    Assert.Throws<ArgumentException>(() => UI.SegmentedTabs(
        "tabs", "missing",
        new SegmentedTab("one", "One", "select-one"),
        new SegmentedTab("two", "Two", "select-two")));
    Assert.Throws<ArgumentException>(() => UI.SegmentedTabs(
        "tabs", "one",
        new SegmentedTab("one", "One", "select-one"),
        new SegmentedTab("one", "Duplicate", "select-duplicate")));
    Assert.Throws<ArgumentException>(() => UI.ScopedDialog(
        "Dialog", new string('d', 123), "scope", "back"));
    return Task.CompletedTask;
}

static Task SettingsRowsAndActionSheetsAreSemantic()
{
    const string longDescription =
        "This intentionally long setting description remains a separate text node so the host can reflow it at compact widths and increased text scales.";
    var setting = UI.SettingsRow(
        "Controller vibration",
        new ComponentAction("Change intensity", "settings.vibration.change", WidgetGlyph.Settings),
        "settings.vibration",
        description: longDescription,
        value: "Medium with adaptive trigger feedback",
        status: "Available",
        statusTone: StatusTone.Success,
        isBusy: true,
        glyph: WidgetGlyph.Settings);
    var sheet = UI.ActionSheet(
        "Choose an intensity",
        "settings.vibration.sheet",
        "settings.vibration.sheet.scope",
        "settings.vibration.dismiss",
        [
            new ActionSheetItem(
                "settings.vibration.low", "Low", "settings.vibration.select-low"),
            new ActionSheetItem(
                "settings.vibration.medium", "Medium", "settings.vibration.select-medium",
                WidgetGlyph.Check, "Medium vibration", IsBusy: true),
            new ActionSheetItem(
                "settings.vibration.reset", "Reset custom profile", "settings.vibration.reset-profile",
                Tone: ActionSheetItemTone.Danger, IsDisabled: true),
        ],
        "The current game may override this setting.");
    var snapshot = new WidgetView(
        UI.Stack("settings.root", setting, sheet),
        InitialFocusId: "settings.vibration.medium",
        ActiveInputScopeId: "settings.vibration.sheet.scope")
        .CreateSnapshot("settings.components", 8);

    Assert.Equal(ProtocolConstants.ScrollContainerVersion, snapshot.ProtocolVersion);
    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);

    var settingNode = Find(snapshot.Root, "settings.vibration");
    Assert.Equal(ViewNodeKind.Stack, settingNode.Kind);
    Assert.True(settingNode.StyleClasses.Contains("wrail-settings-row"),
        "SettingsRow must publish its semantic theme hook.");
    Assert.Equal(longDescription,
        Find(snapshot.Root, "settings.vibration.description").Text);
    Assert.Equal(ViewNodeKind.Stack,
        Find(snapshot.Root, "settings.vibration.copy").Kind);
    Assert.Equal("Medium with adaptive trigger feedback",
        Find(snapshot.Root, "settings.vibration.value").Text);
    Assert.Equal("Available", Find(snapshot.Root, "settings.vibration.status.label").Text);
    var settingAction = Find(snapshot.Root, "settings.vibration.action");
    Assert.Equal(ViewNodeKind.Button, settingAction.Kind);
    Assert.Equal(true, settingAction.IsBusy);
    Assert.Equal(WidgetGlyph.Settings, settingAction.Glyph);
    Assert.True(settingAction.AccessibilityLabel!.Contains("Current value: Medium", StringComparison.Ordinal),
        "Settings action must announce the associated value.");
    Assert.True(settingAction.AccessibilityLabel.Contains("Status: Available", StringComparison.Ordinal),
        "Settings action must announce the associated status.");

    var sheetNode = Find(snapshot.Root, "settings.vibration.sheet");
    Assert.Equal("settings.vibration.sheet.scope", sheetNode.InputScopeId);
    Assert.Equal(ControllerButton.B, sheetNode.Shortcuts.Single().Button);
    Assert.Equal("settings.vibration.dismiss", sheetNode.Shortcuts.Single().ActionId);
    Assert.Equal(ViewNodeKind.Scroll,
        Find(snapshot.Root, "settings.vibration.sheet.list").Kind);
    Assert.Equal(ScrollAxis.Vertical,
        Find(snapshot.Root, "settings.vibration.sheet.list").ScrollAxis);
    var low = Find(snapshot.Root, "settings.vibration.low");
    var medium = Find(snapshot.Root, "settings.vibration.medium");
    var reset = Find(snapshot.Root, "settings.vibration.reset");
    Assert.Equal("settings.vibration.medium", low.Focus!.Down);
    Assert.Equal("settings.vibration.low", medium.Focus!.Up);
    Assert.Equal("settings.vibration.reset", medium.Focus.Down);
    Assert.Equal("settings.vibration.medium", reset.Focus!.Up);
    Assert.Equal(true, medium.IsBusy);
    Assert.Equal("Medium vibration, Busy", medium.AccessibilityLabel);
    Assert.Equal(true, reset.IsDisabled);
    Assert.Equal("Reset custom profile, Destructive action, Unavailable", reset.AccessibilityLabel);
    Assert.True(reset.StyleClasses.Contains("wrail-action-sheet__item--danger"),
        "Destructive actions must expose a semantic theme class.");

    Assert.Throws<ArgumentOutOfRangeException>(() => UI.ActionSheet(
        "Empty", "empty.sheet", "empty.scope", "empty.back", []));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.ActionSheet(
        "Too many", "large.sheet", "large.scope", "large.back",
        Enumerable.Range(0, UI.MaximumActionSheetItems + 1)
            .Select(index => new ActionSheetItem($"item.{index}", $"Item {index}", $"action.{index}"))
            .ToArray()));
    Assert.Throws<ArgumentException>(() => UI.ActionSheet(
        "Duplicate", "duplicate.sheet", "duplicate.scope", "duplicate.back",
        [
            new ActionSheetItem("same", "One", "one"),
            new ActionSheetItem("same", "Two", "two"),
        ]));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.ActionSheet(
        "Bad tone", "tone.sheet", "tone.scope", "tone.back",
        [new ActionSheetItem("tone.item", "Bad", "bad", Tone: (ActionSheetItemTone)999)]));
    Assert.Throws<ArgumentException>(() => UI.SettingsRow(
        "Setting", new ComponentAction("Open", "open"), "unsafe/id"));
    return Task.CompletedTask;
}

static async Task ActionSheetRoutingIsScoped()
{
    var widget = new ActionSheetRoutingWidget();
    _ = widget.RenderSnapshot("action-sheet.instance", 12);
    await widget.SetActiveAsync(true, CancellationToken.None);

    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.B,
        "sheet.busy",
        12,
        "sheet.scope")),
        "B must resolve on the action-sheet scope even when Busy has focus.");
    Assert.Equal("sheet.dismiss", (await widget.NextActionAsync()).ActionId);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.B,
        null,
        12,
        "sheet.scope")),
        "B must resolve on a temporarily focusless action-sheet scope.");
    Assert.Equal("sheet.dismiss", (await widget.NextActionAsync()).ActionId);

    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A,
        "sheet.busy",
        12,
        "sheet.scope")),
        "Busy action-sheet items must suppress A without leaving focus.");
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A,
        "sheet.disabled",
        12,
        "sheet.scope")),
        "Disabled action-sheet items must suppress A without leaving focus.");
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A,
        "sheet.ready",
        12,
        "sheet.scope")),
        "Available action-sheet items must activate normally.");
    Assert.Equal("sheet.choose", (await widget.NextActionAsync()).ActionId);
}

static Task PickersAreSemantic()
{
    var picker = UI.Picker(
        "Choose output",
        "picker.output",
        "picker.output.scope",
        "picker.output.dismiss",
        [
            new PickerOption("picker.output.tv", "Living room TV", "picker.output.select-tv"),
            new PickerOption(
                "picker.output.headset",
                "Wireless headset",
                "picker.output.select-headset",
                IsSelected: true,
                AccessibilityLabel: "Wireless gaming headset"),
            new PickerOption(
                "picker.output.speakers",
                "Desk speakers",
                "picker.output.select-speakers",
                IsDisabled: true),
        ],
        "The selected device is used by media controls.");
    var snapshot = new WidgetView(
        picker,
        InitialFocusId: "picker.output.headset",
        ActiveInputScopeId: "picker.output.scope")
        .CreateSnapshot("picker.instance", 18);

    Assert.Equal(ProtocolConstants.ScrollContainerVersion, snapshot.ProtocolVersion);
    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
    Assert.Equal("picker.output.scope", Find(snapshot.Root, "picker.output").InputScopeId);
    Assert.Equal("picker.output.dismiss",
        Find(snapshot.Root, "picker.output").Shortcuts.Single().ActionId);
    Assert.Equal(ViewNodeKind.Scroll, Find(snapshot.Root, "picker.output.options").Kind);

    var tv = Find(snapshot.Root, "picker.output.tv");
    var headset = Find(snapshot.Root, "picker.output.headset");
    var speakers = Find(snapshot.Root, "picker.output.speakers");
    Assert.Equal("picker.output.headset", tv.Focus!.Down);
    Assert.Equal("picker.output.tv", headset.Focus!.Up);
    Assert.Equal("picker.output.speakers", headset.Focus.Down);
    Assert.Equal("picker.output.headset", speakers.Focus!.Up);
    Assert.Equal(true, headset.IsSelected);
    Assert.Equal(WidgetGlyph.Check, headset.Glyph);
    Assert.Equal("Wireless gaming headset, Selected", headset.AccessibilityLabel);
    Assert.Equal(true, speakers.IsDisabled);
    Assert.Equal("Desk speakers, Not selected, Unavailable", speakers.AccessibilityLabel);
    Assert.True(headset.StyleClasses.Contains("wrail-picker__option--selected"),
        "Selected picker options must expose a semantic theme hook.");

    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Picker(
        "Empty", "picker.empty", "picker.empty.scope", "picker.empty.back", []));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Picker(
        "Large", "picker.large", "picker.large.scope", "picker.large.back",
        Enumerable.Range(0, UI.MaximumPickerOptions + 1)
            .Select(index => new PickerOption(
                $"picker.large.{index}", $"Option {index}", $"picker.select-{index}"))
            .ToArray()));
    Assert.Throws<ArgumentException>(() => UI.Picker(
        "Duplicate", "picker.duplicate", "picker.duplicate.scope", "picker.duplicate.back",
        [
            new PickerOption("picker.same", "One", "picker.one"),
            new PickerOption("picker.same", "Two", "picker.two"),
        ]));
    Assert.Throws<ArgumentException>(() => UI.Picker(
        "Multiple", "picker.multiple", "picker.multiple.scope", "picker.multiple.back",
        [
            new PickerOption("picker.multiple.one", "One", "picker.one", IsSelected: true),
            new PickerOption("picker.multiple.two", "Two", "picker.two", IsSelected: true),
        ]));
    return Task.CompletedTask;
}

static Task ScrubbersAreSemantic()
{
    var scrubber = UI.Scrubber(
            TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3.8),
            TimeSpan.FromHours(2) + TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(5),
            "media.seek",
            "media.timeline",
            accessibilityLabel: "Track position",
            activationAction: "media.toggle-preview")
        .FocusUp("media.previous")
        .FocusDown("media.play")
        .Disabled()
        .Busy();
    var snapshot = new WidgetView(
        UI.Stack("media.root",
            UI.Button("Previous", "media.previous", "media.previous"),
            scrubber,
            UI.Button("Play", "media.play", "media.play")),
        scrubber.SliderId)
        .CreateSnapshot("media.components", 20);

    Assert.Equal(ProtocolConstants.SliderVersion, snapshot.ProtocolVersion);
    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
    var root = Find(snapshot.Root, "media.timeline");
    Assert.Equal(ViewNodeKind.Stack, root.Kind);
    Assert.True(root.StyleClasses.Contains("wrail-scrubber"),
        "Scrubber must publish its semantic theme hook.");
    Assert.True(new[] { "media.timeline.slider", "media.timeline.times" }
        .SequenceEqual(root.Children.Select(child => child.Id)),
        "Scrubber child order or stable IDs changed.");

    var slider = Find(snapshot.Root, "media.timeline.slider");
    Assert.Equal(ViewNodeKind.Slider, slider.Kind);
    Assert.Equal(3_723_800d, slider.Value);
    Assert.Equal(7_384_000d, slider.Maximum);
    Assert.Equal(5_000d, slider.Step);
    Assert.Equal("media.seek", slider.ValueChangedActionId);
    Assert.Equal("media.toggle-preview", slider.ActionId);
    Assert.Equal("media.previous", slider.Focus!.Up);
    Assert.Equal("media.play", slider.Focus.Down);
    Assert.Equal(true, slider.IsDisabled);
    Assert.Equal(true, slider.IsBusy);
    Assert.Equal("Track position, Unavailable, Busy", slider.AccessibilityLabel);
    Assert.Equal("1:02:03 of 2:03:04", slider.AccessibilityValue);
    Assert.True(slider.StyleClasses.Contains("wrail-scrubber__slider"),
        "Scrubber Slider must publish its semantic theme hook.");
    Assert.Equal("1:02:03", Find(snapshot.Root, "media.timeline.elapsed").Text);
    Assert.Equal("2:03:04", Find(snapshot.Root, "media.timeline.duration").Text);

    var localized = UI.Scrubber(
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(1),
        "localized.seek",
        "localized.timeline",
        elapsedLabel: "00 min 08 sec",
        durationLabel: "01 min 30 sec");
    var localizedNode = localized.ToProtocolNode();
    Assert.Equal("00 min 08 sec", Find(localizedNode, "localized.timeline.elapsed").Text);
    Assert.Equal("01 min 30 sec", Find(localizedNode, "localized.timeline.duration").Text);
    Assert.Equal("00 min 08 sec of 01 min 30 sec",
        Find(localizedNode, "localized.timeline.slider").AccessibilityValue);

    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Scrubber(
        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1), "seek", "zero.duration"));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Scrubber(
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
        "seek", "past.duration"));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Scrubber(
        TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.Zero, "seek", "zero.step"));
    Assert.Throws<ArgumentException>(() => UI.Scrubber(
        TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
        "seek", "empty.label", elapsedLabel: " "));
    Assert.Throws<ArgumentException>(() => UI.Scrubber(
        TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
        "seek", new string('s', 120)));
    return Task.CompletedTask;
}

static async Task ScrubberInputResolves()
{
    var widget = new ScrubberRoutingWidget();
    _ = widget.RenderSnapshot("scrubber.instance", 6);
    await widget.SetActiveAsync(true, CancellationToken.None);

    var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.DPadRight,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        "media.seek.slider",
        ActiveInputScopeId: "root",
        SnapshotSequence: 6,
        RequestedValue: 35_000));
    Assert.True(handled, "Scrubber must route through its nested native Slider.");
    var seek = await widget.NextActionAsync();
    Assert.Equal("media.seek.changed", seek.ActionId);
    Assert.Equal("media.seek.slider", seek.SourceElementId);
    Assert.Equal(35_000d, seek.RequestedValue);

    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "media.seek.slider", 6, "root")),
        "Scrubber optional activation must retain Slider A-button semantics.");
    var activation = await widget.NextActionAsync();
    Assert.Equal("media.seek.preview", activation.ActionId);
    Assert.Equal(null, activation.RequestedValue);
}

static Task ToastsAreNonInteractive()
{
    var toast = UI.Toast(
        "Network restored",
        "Your game can use online services again.",
        ToastTone.Success,
        "network.toast")
        .AddClasses("widget-toast");
    var snapshot = new WidgetView(
        UI.Stack("toast.root",
                UI.Button("Continue", "continue", "continue"),
                toast)
            .InputScope("toast.scope")
            .Shortcut(ControllerButton.B, "back"),
        InitialFocusId: "continue",
        ActiveInputScopeId: "toast.scope")
        .CreateSnapshot("toast.instance", 3);

    Assert.Equal(ProtocolConstants.BaselineVersion, snapshot.ProtocolVersion);
    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
    Assert.Equal("continue", snapshot.InitialFocusId);
    Assert.Equal("toast.scope", snapshot.ActiveInputScopeId);

    var root = Find(snapshot.Root, "network.toast");
    Assert.Equal(ViewNodeKind.Row, root.Kind);
    Assert.True(
        new[] { "wrail-toast", "wrail-toast--success", "widget-toast" }
            .SequenceEqual(root.StyleClasses),
        "Toast tone and author classes must remain stable theme hooks.");
    Assert.True(!root.IsFocusable, "A Toast root must never enter controller focus.");
    Assert.True(AllNodes(root).All(node => !node.IsFocusable),
        "A Toast must contain zero controller focus stops.");
    Assert.True(AllNodes(root).All(node => node.ActionId is null),
        "A Toast must not expose an action.");
    Assert.True(AllNodes(root).All(node => node.Shortcuts.Count == 0),
        "A Toast must not intercept shortcuts from its owning input scope.");
    Assert.True(
        new[] { "network.toast.icon", "network.toast.copy" }
            .SequenceEqual(root.Children.Select(child => child.Id)),
        "Toast child order or stable IDs changed.");
    Assert.Equal(WidgetGlyph.Check, Find(root, "network.toast.icon").Glyph);
    Assert.Equal("Success notification", Find(root, "network.toast.icon").AccessibilityLabel);
    Assert.Equal("Network restored", Find(root, "network.toast.title").Text);
    Assert.Equal("Your game can use online services again.",
        Find(root, "network.toast.message").Text);
    Assert.Equal(UI.DefaultToastDuration, toast.Duration);

    var neutral = UI.Toast(
        "Saved", "Settings were saved.", ToastTone.Neutral, "neutral.toast",
        TimeSpan.FromSeconds(2)).ToProtocolNode();
    Assert.Equal(null, FindOrNull(neutral, "neutral.toast.icon"));
    Assert.True(AllNodes(neutral).All(node => !node.IsFocusable),
        "A glyph-free Toast must remain nonfocusable.");

    var custom = UI.Toast(
        "Connected", "Ethernet is active.", ToastTone.Info, "custom.toast",
        TimeSpan.FromSeconds(30), WidgetGlyph.Ethernet).ToProtocolNode();
    Assert.Equal(WidgetGlyph.Ethernet, Find(custom, "custom.toast.icon").Glyph);
    Assert.Equal(TimeSpan.FromSeconds(30), UI.Toast(
        "Connected", "Ethernet is active.", ToastTone.Info, "duration.toast",
        TimeSpan.FromSeconds(30)).Duration);
    var maximumCopy = UI.Toast(
        new string('t', UI.MaximumToastTitleCharacters),
        new string('m', UI.MaximumToastMessageCharacters),
        ToastTone.Neutral,
        "maximum.copy");
    Assert.Equal(UI.MaximumToastTitleCharacters, maximumCopy.Title.Length);
    Assert.Equal(UI.MaximumToastMessageCharacters, maximumCopy.Message.Length);

    Assert.Throws<ArgumentException>(() => UI.Toast(
        " ", "Message", ToastTone.Info, "empty.title"));
    Assert.Throws<ArgumentException>(() => UI.Toast(
        "Title", " ", ToastTone.Info, "empty.message"));
    Assert.Throws<ArgumentException>(() => UI.Toast(
        new string('t', UI.MaximumToastTitleCharacters + 1),
        "Message", ToastTone.Info, "long.title"));
    Assert.Throws<ArgumentException>(() => UI.Toast(
        "Title", new string('m', UI.MaximumToastMessageCharacters + 1),
        ToastTone.Info, "long.message"));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Toast(
        "Title", "Message", ToastTone.Info, "short.duration",
        UI.MinimumToastDuration - TimeSpan.FromMilliseconds(1)));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Toast(
        "Title", "Message", ToastTone.Info, "long.duration",
        UI.MaximumToastDuration + TimeSpan.FromMilliseconds(1)));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Toast(
        "Title", "Message", (ToastTone)999, "bad.tone"));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.Toast(
        "Title", "Message", ToastTone.Info, "bad.glyph", glyph: (WidgetGlyph)999));
    Assert.Throws<ArgumentException>(() => UI.Toast(
        "Title", "Message", ToastTone.Info, new string('i', 125)));
    return Task.CompletedTask;

    static IEnumerable<ViewNode> AllNodes(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in AllNodes(child))
            yield return descendant;
    }
}

static Task MinimalistRowsAreSemantic()
{
    var value = UI.ValueRow(
        "Output device",
        "Living room TV",
        "minimal.output",
        description: "HDMI audio",
        glyph: WidgetGlyph.Volume,
        valueAccessibilityLabel: "Current output: Living room TV");
    var selected = UI.ChoiceRow(
        "Living room TV",
        "select-output",
        "minimal.choice.selected",
        isSelected: true);
    var unavailable = UI.ChoiceRow(
        "Headset",
        "select-headset",
        "minimal.choice.unavailable",
        isDisabled: true,
        accessibilityLabel: "Wireless headset");
    var pending = UI.ChoiceRow(
        "Speakers",
        "select-speakers",
        "minimal.choice.pending",
        isBusy: true,
        glyph: WidgetGlyph.Volume);
    var bumperHint = UI.ControllerHint(
        ControllerButton.LeftBumper,
        "Previous track",
        "minimal.hint.previous");
    var view = new WidgetView(
        UI.Stack("minimal.root", value, selected, unavailable, pending, bumperHint),
        InitialFocusId: "minimal.choice.unavailable");

    var snapshot = view.CreateSnapshot("minimal.components", 1);
    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);

    var valueNode = Find(snapshot.Root, "minimal.output");
    Assert.Equal(ViewNodeKind.Row, valueNode.Kind);
    Assert.True(valueNode.StyleClasses.Contains("wrail-value-row"),
        "ValueRow must publish its semantic theme hook.");
    Assert.True(new[]
    {
        "minimal.output.icon",
        "minimal.output.text",
        "minimal.output.value",
    }.SequenceEqual(valueNode.Children.Select(child => child.Id)),
        "ValueRow child order or stable IDs changed.");
    Assert.Equal(WidgetGlyph.Volume, Find(snapshot.Root, "minimal.output.icon").Glyph);
    Assert.Equal("Output device", Find(snapshot.Root, "minimal.output.label").Text);
    Assert.Equal("HDMI audio", Find(snapshot.Root, "minimal.output.description").Text);
    Assert.Equal("Current output: Living room TV",
        Find(snapshot.Root, "minimal.output.value").AccessibilityLabel);

    var selectedNode = Find(snapshot.Root, "minimal.choice.selected");
    Assert.Equal(ViewNodeKind.Button, selectedNode.Kind);
    Assert.Equal(true, selectedNode.IsSelected);
    Assert.Equal(WidgetGlyph.Check, selectedNode.Glyph);
    Assert.Equal("Living room TV, Selected", selectedNode.AccessibilityLabel);
    Assert.True(selectedNode.StyleClasses.Contains("wrail-choice-row--selected"),
        "Selected ChoiceRow did not expose its state theme hook.");

    var unavailableNode = Find(snapshot.Root, "minimal.choice.unavailable");
    Assert.Equal(true, unavailableNode.IsDisabled);
    Assert.Equal("Wireless headset, Not selected, Unavailable", unavailableNode.AccessibilityLabel);
    Assert.Equal("minimal.choice.unavailable", snapshot.InitialFocusId);
    Assert.Equal(null, unavailableNode.IsBusy);

    var pendingNode = Find(snapshot.Root, "minimal.choice.pending");
    Assert.Equal(true, pendingNode.IsBusy);
    Assert.Equal(WidgetGlyph.Volume, pendingNode.Glyph);
    Assert.Equal("Speakers, Not selected, Busy", pendingNode.AccessibilityLabel);

    var hint = Find(snapshot.Root, "minimal.hint.previous");
    Assert.Equal(ViewNodeKind.Row, hint.Kind);
    Assert.Equal(null, hint.ActionId);
    Assert.Equal("LB", Find(snapshot.Root, "minimal.hint.previous.key").Text);
    Assert.Equal("Left bumper", Find(snapshot.Root, "minimal.hint.previous.key").AccessibilityLabel);
    Assert.Equal("Previous track", Find(snapshot.Root, "minimal.hint.previous.label").Text);

    Assert.Throws<ArgumentException>(() => UI.ValueRow("", "value", "row"));
    Assert.Throws<ArgumentException>(() => UI.ValueRow("Label", "", "row"));
    Assert.Throws<ArgumentException>(() => UI.ChoiceRow("", "action", "choice"));
    Assert.Throws<ArgumentException>(() => UI.ControllerHint(ControllerButton.A, "", "hint"));
    Assert.Throws<ArgumentOutOfRangeException>(() => UI.ControllerHint(
        (ControllerButton)999, "Action", "hint"));
    return Task.CompletedTask;
}

static Task CompositeChildIdsValidateEagerly()
{
    const int maximumIdentifierLength = 128;

    const string descriptionSuffix = ".description";
    var headerBoundaryId = new string('h', maximumIdentifierLength - descriptionSuffix.Length);
    var header = UI.SectionHeader(
        "Network",
        headerBoundaryId,
        description: "Connection details");
    Assert.Equal(maximumIdentifierLength,
        Find(header.ToProtocolNode(), $"{headerBoundaryId}{descriptionSuffix}").Id.Length);
    Assert.Throws<ArgumentException>(() => UI.SectionHeader(
        "Network",
        new string('h', headerBoundaryId.Length + 1),
        description: "Connection details"));

    const string stepperSuffix = ".decrement";
    var stepperBoundaryId = new string('s', maximumIdentifierLength - stepperSuffix.Length);
    var stepper = UI.Stepper(
        "Text scale",
        "100%",
        "text.decrease",
        "text.increase",
        stepperBoundaryId);
    Assert.Equal(maximumIdentifierLength,
        Find(stepper.ToProtocolNode(), $"{stepperBoundaryId}{stepperSuffix}").Id.Length);
    Assert.Throws<ArgumentException>(() => UI.Stepper(
        "Text scale",
        "100%",
        "text.decrease",
        "text.increase",
        new string('s', stepperBoundaryId.Length + 1)));

    Assert.Throws<ArgumentException>(() => UI.StatusBadge(
        "Online", StatusTone.Success, "unsafe/id"));
    return Task.CompletedTask;
}

static Task RawStyleClassesAreValidated()
{
    var valid = RawStyleSnapshot(["wrail-icon-button--large", "_private2"]);
    Assert.Equal(0, ViewSnapshotValidator.Validate(valid).Count);
    Assert.True(StyleClassContract.IsValidIdentifier("wrail-icon-button--large"),
        "The shared style-class grammar rejected a valid WRSS identifier.");

    var invalidCases = new (IReadOnlyList<string> Classes, string Code)[]
    {
        (["two words"], "invalid_style_class"),
        ([".primary"], "invalid_style_class"),
        (["1primary"], "invalid_style_class"),
        ([new string('a', ProtocolConstants.MaximumStyleClassLength + 1)], "style_class_too_long"),
        (["primary", "primary"], "duplicate_style_class"),
        ([null!], "required"),
    };
    foreach (var (classes, code) in invalidCases)
    {
        var errors = ViewSnapshotValidator.Validate(RawStyleSnapshot(classes));
        Assert.True(errors.Any(error => error.Code == code &&
                                       error.Path.StartsWith("$.root.styleClasses", StringComparison.Ordinal)),
            $"Raw style classes did not report '{code}'.");
    }

    var overCount = Enumerable.Range(0, ProtocolConstants.MaximumStyleClassCount + 1)
        .Select(index => $"class-{index}")
        .ToArray();
    Assert.True(
        ViewSnapshotValidator.Validate(RawStyleSnapshot(overCount))
            .Any(error => error.Code == "too_many_style_classes" &&
                          error.Path == "$.root.styleClasses"),
        "Raw snapshots must reject an unbounded number of classes per node.");
    return Task.CompletedTask;
}

static Task StyleExtensionsValidateClasses()
{
    var explicitClasses = UI.Stack("classes.explicit")
        .Classes("wrail-icon-button--large", "_private2");
    Assert.True(
        explicitClasses.StyleClasses.SequenceEqual(
            new[] { "wrail-icon-button--large", "_private2" }, StringComparer.Ordinal),
        "Classes must preserve explicit class order.");

    var appended = UI.Stack("classes.appended")
        .Classes("base", "accent")
        .AddClasses("accent", "last", "last");
    Assert.True(
        appended.StyleClasses.SequenceEqual(new[] { "base", "accent", "last" }, StringComparer.Ordinal),
        "AddClasses must retain original order and deduplicate additions ordinally.");

    foreach (var invalid in new[] { "", "two words", ".primary", "1primary", "nonascii-é" })
        Assert.Throws<ArgumentException>(() => UI.Stack("classes.invalid").Classes(invalid));
    Assert.Throws<ArgumentException>(() => UI.Stack("classes.long").Classes(
        new string('a', ProtocolConstants.MaximumStyleClassLength + 1)));
    Assert.Throws<ArgumentException>(() => UI.Stack("classes.duplicate").Classes("same", "same"));
    Assert.Throws<ArgumentNullException>(() => UI.Stack("classes.null-array").Classes((string[])null!));
    Assert.Throws<ArgumentException>(() => UI.Stack("classes.null-item").Classes("valid", null!));
    Assert.Throws<ArgumentException>(() => UI.Stack("classes.null-addition").AddClasses(null!));

    var maximum = Enumerable.Range(0, ProtocolConstants.MaximumStyleClassCount)
        .Select(index => $"class-{index}")
        .ToArray();
    var maximumElement = UI.Stack("classes.maximum").Classes(maximum);
    Assert.Equal(ProtocolConstants.MaximumStyleClassCount, maximumElement.StyleClasses.Count);
    Assert.Equal(ProtocolConstants.MaximumStyleClassCount,
        maximumElement.AddClasses(maximum[0]).StyleClasses.Count);
    Assert.Throws<ArgumentException>(() => maximumElement.AddClasses("overflow"));
    Assert.Throws<ArgumentException>(() => UI.Stack("classes.over-count").Classes(
        Enumerable.Range(0, ProtocolConstants.MaximumStyleClassCount + 1)
            .Select(index => $"class-{index}")
            .ToArray()));
    return Task.CompletedTask;
}

static ViewSnapshot RawStyleSnapshot(IReadOnlyList<string> styleClasses) => new()
{
    ProtocolVersion = ProtocolConstants.BaselineVersion,
    Sequence = 1,
    WidgetInstanceId = "styles.raw",
    ActiveInputScopeId = "root",
    Root = new ViewNode
    {
        Id = "root",
        Kind = ViewNodeKind.Stack,
        StyleClasses = styleClasses,
    },
};

static Task UndefinedProtocolEnumsAreRejected()
{
    var invalidKind = new ViewSnapshot
    {
        ProtocolVersion = ProtocolConstants.BaselineVersion,
        Sequence = 1,
        WidgetInstanceId = "enum.test",
        ActiveInputScopeId = "root",
        Root = new ViewNode
        {
            Id = "root",
            Kind = (ViewNodeKind)999,
        },
    };
    Assert.True(ViewSnapshotValidator.Validate(invalidKind).Any(error =>
        error.Path == "$.root.kind" && error.Code == "invalid_node_kind"),
        "Undefined node kinds must fail before bridge/native parsing.");

    var invalidShortcut = invalidKind with
    {
        Root = new ViewNode
        {
            Id = "root",
            Kind = ViewNodeKind.Stack,
            Shortcuts =
            [
                new ControllerShortcut(
                    (ControllerButton)999,
                    "invalid.shortcut",
                    (ControllerEventPhase)999),
            ],
        },
        QuickActions =
        [
            new WidgetQuickAction((ControllerButton)999, "invalid.quick", "Invalid"),
        ],
    };
    var errors = ViewSnapshotValidator.Validate(invalidShortcut);
    Assert.True(errors.Count(error => error.Code == "invalid_controller_button") == 2,
        "Undefined shortcut and quick-action buttons must both fail closed.");
    Assert.True(errors.Any(error => error.Code == "invalid_controller_phase"),
        "Undefined shortcut phases must fail closed.");
    return Task.CompletedTask;
}

static Task InvalidInteractionStatesAreRejected()
{
    var nonButton = new ViewSnapshot
    {
        Sequence = 0,
        WidgetInstanceId = "test.instance",
        ActiveInputScopeId = "status",
        Root = new ViewNode
        {
            Id = "status",
            Kind = ViewNodeKind.Text,
            Text = "Ready",
            IsDisabled = false,
        },
    };
    Assert.True(
        ViewSnapshotValidator.Validate(nonButton).Any(error => error.Code == "interaction_state_not_allowed"),
        "Even explicit false interaction state is invalid on a non-button node.");

    var unnamedStatefulButton = nonButton with
    {
        Root = new ViewNode
        {
            Id = "choice",
            Kind = ViewNodeKind.Button,
            ActionId = "choose",
            IsSelected = true,
        },
    };
    Assert.True(
        ViewSnapshotValidator.Validate(unnamedStatefulButton).Any(error => error.Code == "missing_accessible_name"),
        "A stateful button needs a visible or accessibility name.");
    return Task.CompletedTask;
}

static Task UnknownFieldsAreRejected()
{
    var snapshot = new WidgetView(UI.Stack("root")).CreateSnapshot("test.instance", 0);
    var json = Encoding.UTF8.GetString(SnapshotJson.Serialize(snapshot));
    var malformed = Encoding.UTF8.GetBytes(json.Insert(json.Length - 1, ",\"surprise\":true"));
    Assert.Throws<System.Text.Json.JsonException>(() => SnapshotJson.Deserialize(malformed));
    return Task.CompletedTask;
}

static Task NullCollectionsAreRejected()
{
    const string json = "{\"protocolVersion\":1,\"sequence\":0,\"widgetInstanceId\":\"test.instance\",\"activeInputScopeId\":\"root\",\"root\":{\"id\":\"root\",\"kind\":\"stack\",\"styleClasses\":null,\"shortcuts\":null,\"children\":null}}";
    var exception = Assert.Throws<ProtocolValidationException>(() => SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(json)));
    Assert.True(exception.Errors.Any(error => error.Path.EndsWith("styleClasses", StringComparison.Ordinal)), "Expected null style classes to be rejected.");
    return Task.CompletedTask;
}

static Task LoadingIndicatorRoundTrip()
{
    var view = new WidgetView(
        UI.Stack("root",
            UI.LoadingIndicator("loading", "Loading saved applications",
                LoadingIndicatorSize.Compact)),
        ActiveInputScopeId: "root");
    var snapshot = view.CreateSnapshot("loading.instance", 11);
    var indicator = Find(snapshot.Root, "loading");
    Assert.Equal(ViewNodeKind.LoadingIndicator, indicator.Kind);
    Assert.Equal("Loading saved applications", indicator.AccessibilityLabel);
    Assert.Equal(WidgetRail.WidgetProtocol.LoadingIndicatorSize.Compact,
        indicator.IndicatorSize);
    Assert.True(!indicator.IsFocusable, "A loading indicator must never enter controller focus.");
    Assert.True(indicator.StyleClasses.SequenceEqual(
        ["loading-indicator", "loading-indicator-compact"]),
        "The SDK did not emit the bounded semantic loading-indicator classes.");

    var validation = ViewSnapshotValidator.Validate(snapshot);
    Assert.True(validation.Count == 0,
        string.Join("; ", validation.Select(error =>
            $"{error.Path} {error.Code}: {error.Message}")));
    var payload = SnapshotJson.Serialize(snapshot);
    ViewSnapshot roundTrip;
    try
    {
        roundTrip = SnapshotJson.Deserialize(payload);
    }
    catch (ProtocolValidationException exception)
    {
        throw new InvalidOperationException(string.Join("; ", exception.Errors.Select(error =>
            $"{error.Path} {error.Code}: {error.Message}")), exception);
    }
    Assert.Equal(ViewNodeKind.LoadingIndicator, Find(roundTrip.Root, "loading").Kind);
    Assert.Throws<ArgumentException>(() => UI.LoadingIndicator("bad", " "));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        UI.LoadingIndicator("bad-size", "Loading", (LoadingIndicatorSize)999));

    var legacy = snapshot with { ProtocolVersion = ProtocolConstants.LoadingIndicatorVersion - 1 };
    Assert.True(ViewSnapshotValidator.Validate(legacy).Any(error =>
        error.Path == "$.root.children[0]" && error.Code == "feature_requires_version"),
        "A pre-v5 snapshot accepted a loading-indicator node.");
    var unnamed = snapshot with
    {
        Root = snapshot.Root with
        {
            Children = [indicator with { AccessibilityLabel = null }],
        },
    };
    Assert.True(ViewSnapshotValidator.Validate(unnamed).Any(error =>
        error.Path == "$.root.children[0].accessibilityLabel" && error.Code == "required"),
        "A loading indicator without an accessible name was accepted.");
    var interactive = snapshot with
    {
        Root = snapshot.Root with
        {
            Children = [indicator with { ActionId = "not.allowed", IsBusy = true }],
        },
    };
    var errors = ViewSnapshotValidator.Validate(interactive);
    Assert.True(errors.Any(error => error.Code == "action_not_allowed"),
        "A loading indicator accepted an action ID.");
    Assert.True(errors.Any(error => error.Code == "interaction_state_not_allowed"),
        "A loading indicator accepted interactive state.");
    return Task.CompletedTask;
}

static Task ValidManifestPasses()
{
    var manifest = ValidManifest();
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest with
    {
        OptionalPermissions = ["system.network.saved-profile.switch.v1"],
    }).Count);
    var roundTrip = ManifestJson.Deserialize(ManifestJson.Serialize(manifest));
    Assert.Equal(manifest.Id, roundTrip.Id);
    return Task.CompletedTask;
}

static Task FullTrustEntrypointIsStrict()
{
    var manifest = ValidManifest() with
    {
        Entrypoint = new WidgetEntrypoint(
            WidgetEntrypointRuntimes.FullTrustApplicationV1,
            Executable: "payload/UnrelatedApplication.exe"),
        Permissions = [],
    };
    Assert.Equal(WidgetExecutionTrust.FullTrustCurrentUser,
        WidgetManifestTrust.Resolve(manifest));
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);

    var payload = ManifestJson.Serialize(manifest);
    var text = Encoding.UTF8.GetString(payload);
    Assert.True(text.Contains("\"executable\": \"payload/UnrelatedApplication.exe\"",
        StringComparison.Ordinal), "The versioned executable was not serialized.");
    Assert.True(!text.Contains("packagePath", StringComparison.Ordinal),
        "The derived package path leaked into the versioned manifest schema.");
    var roundTrip = ManifestJson.Deserialize(payload);
    Assert.Equal("payload/UnrelatedApplication.exe",
        WidgetEntrypointRuntimes.ResolvePackagePath(roundTrip.Entrypoint));
    Assert.Equal(0, WidgetManifestValidator.Validate(roundTrip).Count);

    var document = JsonNode.Parse(payload)!.AsObject();
    document["entrypoint"]!["packagePath"] = "payload/tampered.exe";
    Assert.Throws<JsonException>(() => ManifestJson.Deserialize(
        Encoding.UTF8.GetBytes(document.ToJsonString())));

    var withCapabilities = manifest with { Permissions = ["storage.own"] };
    Assert.True(WidgetManifestValidator.Validate(withCapabilities).Any(error =>
        error.Path == "$.permissions" &&
        error.Code == "full_trust_capabilities_forbidden"),
        "Full-trust packages accepted sandbox broker capabilities.");
    var mixedEntrypoint = manifest with
    {
        Entrypoint = manifest.Entrypoint with
        {
            Assembly = "payload/not-used.dll",
            Type = "Unrelated.NotUsed",
        },
    };
    Assert.True(WidgetManifestValidator.Validate(mixedEntrypoint).Any(error =>
        error.Code == "conflicting_entrypoint"),
        "The full-trust entrypoint accepted a second loader identity.");
    return Task.CompletedTask;
}

static Task ManifestPinningIsExplicit()
{
    var omittedDocument = JsonNode.Parse(ManifestJson.Serialize(ValidManifest()))!.AsObject();
    omittedDocument.Remove("pinningSupported");
    var omitted = ManifestJson.Deserialize(
        Encoding.UTF8.GetBytes(omittedDocument.ToJsonString()));
    Assert.True(!omitted.PinningSupported,
        "A manifest that omits pinningSupported was admitted for pinning.");

    var supported = ValidManifest() with { PinningSupported = true };
    var payload = ManifestJson.Serialize(supported);
    var json = Encoding.UTF8.GetString(payload);
    Assert.True(json.Contains("\"pinningSupported\": true", StringComparison.Ordinal),
        "The explicit pinning declaration was not serialized.");
    Assert.True(ManifestJson.Deserialize(payload).PinningSupported,
        "The explicit pinning declaration did not round-trip.");
    Assert.Equal(0, WidgetManifestValidator.Validate(supported).Count);
    return Task.CompletedTask;
}

static Task ManifestPresentationIsSemantic()
{
    var manifest = ValidManifest() with
    {
        Presentation = new WidgetPresentation(WidgetGlyph.Music),
    };
    var payload = ManifestJson.Serialize(manifest);
    var json = Encoding.UTF8.GetString(payload);
    Assert.True(json.Contains("\"presentation\"", StringComparison.Ordinal) &&
                json.Contains("\"icon\": \"music\"", StringComparison.Ordinal),
        "Manifest presentation icon was not serialized as a semantic camel-case glyph.");
    var roundTrip = ManifestJson.Deserialize(payload);
    Assert.Equal(WidgetGlyph.Music, roundTrip.Presentation.Icon);

    var withoutPresentation = System.Text.Json.JsonSerializer.Deserialize<
        Dictionary<string, System.Text.Json.JsonElement>>(payload)!;
    Assert.True(withoutPresentation.Remove("presentation"),
        "Serialized manifest omitted the presentation member.");
    var omitted = ManifestJson.Deserialize(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(withoutPresentation));
    Assert.Equal(WidgetGlyph.Connection, omitted.Presentation.Icon);

    Assert.Throws<System.Text.Json.JsonException>(() => ManifestJson.Deserialize(
        Encoding.UTF8.GetBytes(json.Replace("\"music\"", "\"../../icon.svg\"",
            StringComparison.Ordinal))));
    Assert.Throws<System.Text.Json.JsonException>(() => ManifestJson.Deserialize(
        Encoding.UTF8.GetBytes(json.Replace("\"music\"", "999",
            StringComparison.Ordinal))));
    var invalid = WidgetManifestValidator.Validate(manifest with
    {
        Presentation = new WidgetPresentation((WidgetGlyph)999),
    });
    Assert.True(invalid.Any(error => error.Path == "$.presentation.icon" &&
                                     error.Code == "unsupported_icon"),
        "Undefined semantic glyph was not rejected by manifest validation.");
    return Task.CompletedTask;
}

static Task ManifestPermissionsAreBounded()
{
    var supportedFormats = ValidManifest() with
    {
        Name = "Música 🎵",
        Permissions =
        [
            "system.audio.sessions.read.v1",
            "network.loopback:13091",
        ],
    };
    Assert.Equal(0, WidgetManifestValidator.Validate(supportedFormats).Count);

    var required = Enumerable.Range(0, ProtocolConstants.MaximumManifestPermissionCount / 2)
        .Select(index => $"system.test.required-{index}.v1")
        .ToArray();
    var optional = Enumerable.Range(0, ProtocolConstants.MaximumManifestPermissionCount / 2)
        .Select(index => $"system.test.optional-{index}.v1")
        .ToArray();
    Assert.Equal(0, WidgetManifestValidator.Validate(ValidManifest() with
    {
        Permissions = required,
        OptionalPermissions = optional,
    }).Count);

    var tooMany = WidgetManifestValidator.Validate(ValidManifest() with
    {
        Permissions = Enumerable.Range(0, ProtocolConstants.MaximumManifestPermissionCount + 1)
            .Select(index => $"system.test.capability-{index}.v1")
            .ToArray(),
    });
    Assert.True(tooMany.Any(error => error.Path == "$.permissions" &&
                                     error.Code == "too_many_permissions"),
        "Combined permission declarations were not bounded.");

    var longPermission = "system." +
        new string('a', ProtocolConstants.MaximumCapabilityIdLength);
    var tooLong = WidgetManifestValidator.Validate(ValidManifest() with
    {
        Permissions = [longPermission],
    });
    Assert.True(tooLong.Any(error => error.Path == "$.permissions[0]" &&
                                    error.Code == "too_long"),
        "Oversized capability ID did not fail at the manifest boundary.");

    var duplicated = WidgetManifestValidator.Validate(ValidManifest() with
    {
        Permissions = ["system.audio.sessions.read.v1"],
        OptionalPermissions = ["system.audio.sessions.read.v1"],
    });
    Assert.True(duplicated.Any(error => error.Path == "$.optionalPermissions[0]" &&
                                       error.Code == "duplicate_permission"),
        "Required and optional declarations were not treated as one authority set.");

    var malicious = WidgetManifestValidator.Validate(ValidManifest() with
    {
        Permissions =
        [
            "system.audio.\u202Eread.v1",
            "system.audio.read.v1\0",
            "system\uFF0Eaudio.read.v1",
            null!,
        ],
    });
    Assert.Equal(4, malicious.Count(error => error.Code == "invalid_permission"));

    var spoofedIdentity = WidgetManifestValidator.Validate(ValidManifest() with
    {
        Id = "dev.test.\u202Ewidget",
        Publisher = "dev.test\npublisher",
        Name = "Trusted \u2066publisher\u2069",
    });
    Assert.True(spoofedIdentity.Any(error => error.Code == "invalid_id"),
        "Bidi-spoofed package ID was accepted.");
    Assert.True(spoofedIdentity.Any(error => error.Code == "invalid_publisher"),
        "Control-bearing publisher ID was accepted.");
    Assert.True(spoofedIdentity.Any(error => error.Code == "invalid_name"),
        "Bidi-formatting display name was accepted.");
    return Task.CompletedTask;
}

static Task ResidencyPolicyIsVersioned()
{
    var defaultPolicy = WidgetResidencyPolicies.Resolve(ValidManifest());
    Assert.Equal(WidgetResidencyMode.KeepAlive, defaultPolicy.Mode);

    var legacyKeepAlive = ValidManifest() with { BackgroundPolicy = "none" };
    var legacySuspend = ValidManifest() with { BackgroundPolicy = "suspend" };
    Assert.Equal(0, WidgetManifestValidator.Validate(legacyKeepAlive).Count);
    Assert.Equal(WidgetResidencyMode.KeepAlive,
        WidgetResidencyPolicies.Resolve(legacyKeepAlive).Mode);
    Assert.Equal(WidgetResidencyMode.SuspendWhenHidden,
        WidgetResidencyPolicies.Resolve(legacySuspend).Mode);

    var unload = ValidManifest() with
    {
        ResidencyPolicy = new WidgetResidencyPolicy
        {
            SchemaVersion = 1,
            Mode = WidgetResidencyPolicies.UnloadAfterIdle,
            IdleSeconds = 120,
        },
    };
    Assert.Equal(0, WidgetManifestValidator.Validate(unload).Count);
    Assert.Equal(TimeSpan.FromSeconds(120),
        WidgetResidencyPolicies.Resolve(unload).IdleDuration);
    var roundTrip = ManifestJson.Deserialize(ManifestJson.Serialize(unload));
    Assert.Equal(WidgetResidencyPolicies.UnloadAfterIdle, roundTrip.ResidencyPolicy?.Mode);

    var tooShort = unload with
    {
        ResidencyPolicy = unload.ResidencyPolicy! with { IdleSeconds = 4 },
    };
    Assert.True(WidgetManifestValidator.Validate(tooShort).Any(error =>
        error.Path == "$.residencyPolicy.idleSeconds" && error.Code == "out_of_range"),
        "Expected short idle unload to fail closed.");
    var conflict = unload with { BackgroundPolicy = "suspend" };
    Assert.True(WidgetManifestValidator.Validate(conflict).Any(error =>
        error.Code == "conflicting_policy"),
        "Legacy and versioned policy must not be combined.");
    var strayIdle = ValidManifest() with
    {
        ResidencyPolicy = new WidgetResidencyPolicy { IdleSeconds = 120 },
    };
    Assert.True(WidgetManifestValidator.Validate(strayIdle).Any(error =>
        error.Code == "not_applicable"),
        "Idle duration must apply only to unload-after-idle.");
    return Task.CompletedTask;
}

static Task UnsafeManifestFails()
{
    var manifest = ValidManifest() with
    {
        Entrypoint = new WidgetEntrypoint("native-dll", "../escape.dll", "NoNamespace"),
        Permissions = ["network.client:https://bad value", "storage.own", "storage.own"],
        OptionalPermissions = ["storage.own"],
        ResourceRequest = new WidgetResourceRequest(1024, 1000),
    };
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Any(error => error.Code == "invalid_path"), "Expected invalid_path.");
    Assert.True(errors.Any(error => error.Code == "unsupported_runtime"), "Expected unsupported_runtime.");
    Assert.True(errors.Any(error => error.Code == "duplicate_permission"), "Expected duplicate_permission.");
    Assert.True(errors.Count >= 6, "Expected independent manifest failures.");
    return Task.CompletedTask;
}

static Task WorkerMemoryGuidanceIsAdvisory()
{
    var omitted = ValidManifest() with { ResourceRequest = new WidgetResourceRequest() };
    Assert.Equal(0, WidgetManifestValidator.Validate(omitted).Count);
    Assert.Equal(null, omitted.ResourceRequest.MemoryMb);

    var fullApplication = omitted with
    {
        ResourceRequest = new WidgetResourceRequest(1_024, 1),
    };
    Assert.Equal(0, WidgetManifestValidator.Validate(fullApplication).Count);
    var roundTrip = ManifestJson.Deserialize(ManifestJson.Serialize(fullApplication));
    Assert.Equal(1_024, roundTrip.ResourceRequest.MemoryMb);

    var invalid = omitted with
    {
        ResourceRequest = new WidgetResourceRequest(0, 1),
    };
    Assert.True(WidgetManifestValidator.Validate(invalid).Any(error =>
        error.Path == "$.resourceRequest.memoryMb" && error.Code == "out_of_range"),
        "Non-positive memory guidance did not fail manifest validation.");
    return Task.CompletedTask;
}

static Task NullManifestCollectionsFail()
{
    var manifest = ValidManifest() with
    {
        Permissions = null!,
        OptionalPermissions = null!,
        Architectures = null!,
        ResourceRequest = null!,
    };
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Count(error => error.Code == "required") >= 4, "Expected null manifest properties to be reported.");
    return Task.CompletedTask;
}

static Task ClockRenders()
{
    var clock = new ClockWidget(new FixedTimeProvider(new DateTimeOffset(2026, 8, 7, 21, 5, 0, TimeSpan.Zero)));
    var snapshot = clock.Render().CreateSnapshot("clock.instance", 1);
    Assert.Equal("refresh", snapshot.InitialFocusId);
    var refresh = Find(snapshot.Root, "refresh");
    Assert.Equal(ControllerButton.X, refresh.Shortcuts.Single().Button);
    Assert.Equal("9:05 PM", Find(snapshot.Root, "clock-time").Text);
    return Task.CompletedTask;
}

static async Task ClockInvalidates()
{
    var clock = new ClockWidget();
    long observedRevision = 0;
    clock.Invalidated += (_, args) => observedRevision = args.Revision;
    await clock.OnActionAsync(new WidgetActionEvent("ignored", "source"));
    Assert.Equal(0L, observedRevision);
    await clock.OnActionAsync(new WidgetActionEvent("refresh", "refresh", ControllerButton.X));
    Assert.Equal(1L, observedRevision);
    Assert.Equal(1L, clock.Revision);
}

static async Task DashboardInputResolves()
{
    var widget = new RoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        Sequence: 9,
        MonotonicTimestampMicroseconds: 1234,
        SnapshotSequence: 1));
    Assert.True(handled, "Expected dashboard quick action to resolve.");
    var action = await widget.NextActionAsync();
    Assert.Equal("quick-refresh", action.ActionId);
    Assert.Equal("dashboard-card", action.SourceElementId);
    Assert.Equal(9L, action.Sequence);
    Assert.Equal(9L, widget.LastGestureContext?.InputSequence);
    Assert.Equal(1L, widget.LastGestureContext?.SnapshotSequence);
    await widget.OnActionAsync(new WidgetActionEvent("direct", "test"));
    Assert.Equal("direct", (await widget.NextActionAsync()).ActionId);
    Assert.Equal<WidgetCapabilityGestureContext?>(null, widget.LastGestureContext);

    handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        Sequence: 10,
        SnapshotSequence: 1,
        Origin: ControllerInputOrigin.AccessibilityAutomation));
    Assert.True(handled, "Expected accessibility automation to resolve the ordinary action.");
    Assert.Equal("quick-refresh", (await widget.NextActionAsync()).ActionId);
    Assert.Equal<WidgetCapabilityGestureContext?>(null, widget.LastGestureContext);
    Assert.True(!await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        SnapshotSequence: 1,
        Origin: (ControllerInputOrigin)99)), "Unknown origins must fail closed.");
}

static async Task FocusedShortcutResolves()
{
    var widget = new RoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    var handled = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.RightBumper, "play", 1, "root"));
    Assert.True(handled, "Expected focused shortcut to resolve.");
    Assert.Equal("next", (await widget.NextActionAsync()).ActionId);

    var ignored = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftTrigger, "play", 1, "root"));
    Assert.True(!ignored, "Undeclared input must remain widget-owned and unhandled by default.");
}

static async Task FocusedRowShortcutsResolve()
{
    var widget = new RowShortcutWidget();
    _ = widget.RenderSnapshot("rows.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.X, "first", 1, "root")),
        "First row did not resolve its exact focused shortcut.");
    Assert.Equal("first.toggle", (await widget.NextActionAsync()).ActionId);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.X, "second", 1, "root")),
        "Second row did not resolve the same button in its exact focus context.");
    Assert.Equal("second.toggle", (await widget.NextActionAsync()).ActionId);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.Y, "nested", 1, "root")),
        "Nearest intermediate container shortcut did not resolve from focused content.");
    var ancestor = await widget.NextActionAsync();
    Assert.Equal("group.options", ancestor.ActionId);
    Assert.Equal("nested.group", ancestor.SourceElementId);
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.X, null, 1, "root")),
        "Focusless input must not guess between row-local shortcuts.");
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.Y, null, 1, "root")),
        "Focusless input must not guess an intermediate-container shortcut.");

    widget.DisableSecond = true;
    _ = widget.RenderSnapshot("rows.instance", 2);
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.X, "second", 2, "root")),
        "Disabled focused row must not leak into another row's shortcut.");
}

static async Task FocusedButtonActivates()
{
    var widget = new RoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    var handled = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "play", 1, "root", inputSequence: 12));
    Assert.True(handled, "A Pressed should activate the focused button without a shortcut.");
    var action = await widget.NextActionAsync();
    Assert.Equal("play", action.ActionId);
    Assert.Equal("play", action.SourceElementId);
    Assert.Equal(12L, action.Sequence);
}

static async Task NonButtonDoesNotActivate()
{
    var widget = new RoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    var handled = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "status", 1, "root"));
    Assert.True(!handled, "A must not synthesize an action for a non-button node with no action ID.");
    Assert.Equal(0, widget.Actions.Count);
}

static async Task DisabledAndBusyButtonsDoNotActivate()
{
    var widget = new StateRoutingWidget();
    _ = widget.RenderSnapshot("routing.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);

    foreach (var input in new[]
             {
                 OpenInput(ControllerButton.A, "disabled", 1, "root"),
                 OpenInput(ControllerButton.RightBumper, "disabled", 1, "root"),
                 OpenInput(ControllerButton.A, "busy", 1, "root"),
                 OpenInput(ControllerButton.X, "busy", 1, "root"),
             })
    {
        Assert.True(!await widget.OnControllerInputAsync(input), "Disabled or busy controls must not handle controller activation.");
    }
    Assert.Equal(0, widget.Actions.Count);

    var selectedHandled = await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "selected", 1, "root"));
    Assert.True(selectedHandled, "Selected is semantic state, not an activation lock.");
    Assert.Equal("select", (await widget.NextActionAsync()).ActionId);
}

static async Task SliderInputResolves()
{
    var widget = new SliderRoutingWidget();
    _ = widget.RenderSnapshot("slider.instance", 3);
    await widget.SetActiveAsync(true, CancellationToken.None);

    Assert.True(!await widget.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadRight, ControllerEventPhase.Pressed, 2, "root", 0.6)),
        "Stale slider snapshot input must be rejected.");
    Assert.True(!await widget.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadRight, ControllerEventPhase.Pressed, 3, "dialog", 0.6)),
        "Wrong slider input scope must be rejected.");
    Assert.True(await widget.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadRight, ControllerEventPhase.Pressed, 3, "root", 0.55)),
        "Focused Slider must consume malformed horizontal input.");
    Assert.Equal(0, widget.Actions.Count);

    Assert.True(await widget.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadRight, ControllerEventPhase.Pressed, 3, "root", 0.6)),
        "Valid Slider change should queue.");
    var changed = await widget.NextActionAsync();
    Assert.Equal("volume.changed", changed.ActionId);
    Assert.Equal(0.6d, changed.RequestedValue);
    Assert.Equal("root", changed.InputScopeId);
    Assert.Equal(ControllerButton.DPadRight, changed.ControllerButton);

    Assert.True(await widget.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadLeft, ControllerEventPhase.Pressed, 3, "root", 0.5)),
        "A quick reversal to the still-published value must not be rejected as stale direction.");
    var reversed = await widget.NextActionAsync();
    Assert.Equal(0.5d, reversed.RequestedValue);
    Assert.Equal(ControllerButton.DPadLeft, reversed.ControllerButton);

    Assert.True(await widget.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadRight, ControllerEventPhase.Repeated, 3, "root", 0.7)),
        "Repeated Slider change should queue through the same bounded path.");
    Assert.Equal(ControllerEventPhase.Repeated, (await widget.NextActionAsync()).Phase);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "volume", 3, "root")),
        "Optional Slider activation should use A without changing value.");
    var activation = await widget.NextActionAsync();
    Assert.Equal("volume.mute", activation.ActionId);
    Assert.Equal(null, activation.RequestedValue);

    widget.Disabled = true;
    _ = widget.RenderSnapshot("slider.instance", 4);
    Assert.True(await widget.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadRight, ControllerEventPhase.Pressed, 4, "root", 0.6)),
        "Disabled Slider retains focus and consumes horizontal input.");
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "volume", 4, "root")),
        "Disabled Slider suppresses optional activation.");
    Assert.Equal(4, widget.Actions.Count);

    widget.Disabled = false;
    widget.Busy = true;
    _ = widget.RenderSnapshot("slider.instance", 5);
    Assert.True(await widget.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadLeft, ControllerEventPhase.Pressed, 5, "root", 0.4)),
        "Busy Slider retains focus and consumes horizontal input.");
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "volume", 5, "root")),
        "Busy Slider suppresses optional activation.");
    Assert.Equal(4, widget.Actions.Count);
}

static async Task SliderActionsCoalesceInOrder()
{
    var widget = new CoalescingSliderWidget();
    _ = widget.RenderSnapshot("coalesce.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "block", 1, "root")), "Blocking boundary action was not queued.");
    await widget.BlockStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    foreach (var requested in new[] { 0.6, 0.7, 0.8 })
        Assert.True(await widget.OnControllerInputAsync(SliderInput(
            ControllerButton.DPadRight, ControllerEventPhase.Repeated, 1, "root", requested)),
            "Contiguous Slider target should be accepted/coalesced.");
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "volume", 1, "root")), "Discrete activation boundary was not queued.");
    Assert.True(await widget.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadRight, ControllerEventPhase.Repeated, 1, "root", 0.9)),
        "Slider target after a discrete boundary was not queued.");
    widget.ReleaseBlock.TrySetResult();
    await widget.WaitForActionsAsync(4);
    Assert.Equal("block", widget.Actions[0].ActionId);
    Assert.Equal(0.8d, widget.Actions[1].RequestedValue);
    Assert.Equal("volume.mute", widget.Actions[2].ActionId);
    Assert.Equal(0.9d, widget.Actions[3].RequestedValue);

    var canceled = new CoalescingSliderWidget();
    _ = canceled.RenderSnapshot("cancel.instance", 1);
    await canceled.SetActiveAsync(true, CancellationToken.None);
    Assert.True(await canceled.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "block", 1, "root")), "Cancellation boundary was not queued.");
    await canceled.BlockStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.True(await canceled.OnControllerInputAsync(SliderInput(
        ControllerButton.DPadRight, ControllerEventPhase.Repeated, 1, "root", 0.8)),
        "Pending Slider action was not queued before deactivation.");
    await canceled.SetActiveAsync(false, CancellationToken.None);
    await canceled.SetActiveAsync(true, CancellationToken.None);
    Assert.True(await canceled.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "volume", 1, "root")), "Reactivated Slider activation was not queued.");
    await canceled.WaitForActionsAsync(2);
    Assert.Equal("block", canceled.Actions[0].ActionId);
    Assert.Equal("volume.mute", canceled.Actions[1].ActionId);
}

static async Task ActionDiagnosticsCorrelate()
{
    var widget = new DiagnosticRoutingWidget();
    _ = widget.RenderSnapshot("diagnostic.instance", 7);
    await widget.SetActiveAsync(true, CancellationToken.None);

    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.A, "diagnostic.action", 7, "diagnostic.root", 303001)),
        "The diagnostic action was not admitted.");
    await widget.AllObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

    var observations = widget.Observations;
    Assert.Equal(3, observations.Count);
    Assert.True(observations.Any(item =>
        item.Stage == "admission" && item.Code == "enqueued"),
        "Admission diagnostics were not observed.");
    Assert.True(observations.Any(item =>
        item.Stage == "dequeue" && item.Code == "started"),
        "Dequeue diagnostics were not observed.");
    Assert.True(observations.Any(item =>
        item.Stage == "terminal" && item.Code == "succeeded"),
        "Terminal diagnostics were not observed.");
    foreach (var observation in observations)
    {
        Assert.Equal("diagnostic.activate", observation.Action.ActionId);
        Assert.Equal("diagnostic.action", observation.Action.SourceElementId);
        Assert.Equal(303001L, observation.Action.Sequence);
        Assert.Equal("diagnostic.root", observation.Action.InputScopeId);
    }

    await widget.SetActiveAsync(false, CancellationToken.None);
}

static async Task ScopedShortcutRouting()
{
    var widget = new SurfaceRoutingWidget();
    _ = widget.RenderSnapshot("scope.instance", 1);
    await widget.SetActiveAsync(true, CancellationToken.None);

    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "root-focus", 1, "root")), "Root surface shortcut did not resolve.");
    var rootAction = await widget.NextActionAsync();
    Assert.Equal("root-back", rootAction.ActionId);
    Assert.Equal("root", rootAction.InputScopeId);

    widget.SetSurface("dialog-window");
    _ = widget.RenderSnapshot("scope.instance", 2);
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "dialog-focus", 1, "dialog-window")),
        "Stale snapshot input must be rejected.");
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "dialog-focus", 2, "root")),
        "Input naming a non-active scope must be rejected.");
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "root-focus", 2, "dialog-window")),
        "Focus outside the active scope must be rejected.");
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "dialog-focus", 2, "dialog-window")), "Nested surface shortcut did not resolve.");
    var nestedAction = await widget.NextActionAsync();
    Assert.Equal("dialog-back", nestedAction.ActionId);
    Assert.Equal("dialog-window", nestedAction.InputScopeId);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.B, null, 2, "dialog-window")),
        "A focusless modal should resolve its scope-container B shortcut.");
    var closeAction = await widget.NextActionAsync();
    Assert.Equal("dialog-close", closeAction.ActionId);
    Assert.Equal("dialog-window", closeAction.InputScopeId);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.B, "dialog-focus", 2, "dialog-window")),
        "Focused input without a local B shortcut should resolve its ancestor B shortcut.");
    Assert.Equal("dialog-close", (await widget.NextActionAsync()).ActionId);
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.B, "dialog-own", 2, "dialog-window")),
        "A focused B shortcut should outrank its ancestor.");
    Assert.Equal("dialog-own-back", (await widget.NextActionAsync()).ActionId);
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.B, "dialog-disabled", 2, "dialog-window")),
        "A disabled focused B shortcut must suppress ancestor fallback.");
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.B, "dialog-busy", 2, "dialog-window")),
        "A busy focused B shortcut must suppress ancestor fallback.");
    Assert.True(await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.B, "dialog-disabled-inherit", 2, "dialog-window")),
        "A disabled focus without a local B shortcut should retain ancestor fallback.");
    Assert.Equal("dialog-close", (await widget.NextActionAsync()).ActionId);

    widget.SetSurface("empty-window");
    _ = widget.RenderSnapshot("scope.instance", 3);
    Assert.True(!await widget.OnControllerInputAsync(OpenInput(
        ControllerButton.LeftBumper, "empty-focus", 3, "empty-window")),
        "Nested surfaces must not bubble to their parent by default.");
}

static ControllerInputEvent OpenInput(
    ControllerButton button,
    string? focusedElementId,
    long snapshotSequence,
    string activeInputScopeId,
    long inputSequence = 0) => new(
        button,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        focusedElementId,
        Sequence: inputSequence,
        ActiveInputScopeId: activeInputScopeId,
        SnapshotSequence: snapshotSequence);

static ControllerInputEvent SliderInput(
    ControllerButton button,
    ControllerEventPhase phase,
    long snapshotSequence,
    string activeInputScopeId,
    double requestedValue) => new(
        button,
        phase,
        ControllerInputContext.OpenWidget,
        "volume",
        ActiveInputScopeId: activeInputScopeId,
        SnapshotSequence: snapshotSequence,
        RequestedValue: requestedValue);

static WidgetManifest ValidManifest() => new()
{
    Id = "widgetrail.samples.clock",
    Publisher = "widgetrail.samples",
    Name = "Clock",
    Version = "0.1.0",
    HostApi = new HostApiRange("1.0", 1),
    Entrypoint = new WidgetEntrypoint("dotnet-worker", "payload/ClockWidget.dll", "Example.ClockWidget"),
    Permissions = ["storage.own"],
};

static ViewNode Find(ViewNode node, string id)
{
    if (node.Id == id) return node;
    foreach (var child in node.Children)
    {
        var found = FindOrNull(child, id);
        if (found is not null) return found;
    }
    throw new InvalidOperationException($"Node '{id}' was not found.");
}

static ViewNode? FindOrNull(ViewNode node, string id)
{
    if (node.Id == id) return node;
    foreach (var child in node.Children)
    {
        var found = FindOrNull(child, id);
        if (found is not null) return found;
    }
    return null;
}

file sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => value;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

file sealed class RoutingWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();
    public List<WidgetActionEvent> Actions { get; } = [];
    public WidgetCapabilityGestureContext? LastGestureContext { get; private set; }

    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Button("Play", "play", "play").Shortcut(ControllerButton.RightBumper, actionId: "next"),
            UI.Text("Ready", "status")),
        "play",
        [new WidgetQuickAction(ControllerButton.X, "quick-refresh", "Refresh")]);

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        LastGestureContext = WidgetCapabilityInvocationContext.Current;
        Actions.Add(action);
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class DiagnosticRoutingWidget : Widget
{
    private readonly object _gate = new();
    private readonly List<ActionDiagnosticObservation> _observations = [];

    public TaskCompletionSource AllObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<ActionDiagnosticObservation> Observations
    {
        get
        {
            lock (_gate) return _observations.ToArray();
        }
    }

    public override WidgetView Render() => new(
        UI.Stack("diagnostic.root",
            UI.Button("Activate", "diagnostic.activate", "diagnostic.action")),
        "diagnostic.action",
        ActiveInputScopeId: "diagnostic.root");

    protected override void OnActionDiagnostic(
        WidgetActionEvent action,
        string stage,
        string code)
    {
        lock (_gate)
        {
            _observations.Add(new(action, stage, code));
            if (_observations.Count == 3) AllObserved.TrySetResult();
        }
    }

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

file sealed record ActionDiagnosticObservation(
    WidgetActionEvent Action,
    string Stage,
    string Code);

file sealed class SliderRoutingWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();
    public List<WidgetActionEvent> Actions { get; } = [];
    public bool Disabled { get; set; }
    public bool Busy { get; set; }

    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Slider(0.5, 0, 1, 0.1, "volume.changed", "volume",
                    "Game volume, unmuted, press A to mute", "50 percent", "volume.mute")
                .Disabled(Disabled)
                .Busy(Busy)),
        "volume");

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        Actions.Add(action);
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class ScrubberRoutingWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();

    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Scrubber(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(5),
                "media.seek.changed",
                "media.seek",
                activationAction: "media.seek.preview")),
        "media.seek.slider");

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class RowShortcutWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();
    public bool DisableSecond { get; set; }
    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Button("First", "first.activate", "first")
                .Shortcut(ControllerButton.X, actionId: "first.toggle"),
            UI.Stack("second.group",
                UI.Button("Second", "second.activate", "second")
                    .Shortcut(ControllerButton.X, actionId: "second.toggle")
                    .Disabled(DisableSecond))
                .Shortcut(ControllerButton.X, "second.group.toggle"),
            UI.Stack("nested.group",
                UI.Button("Nested", "nested.activate", "nested"))
                .Shortcut(ControllerButton.Y, "group.options")),
        "first");

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class CoalescingSliderWidget : Widget
{
    private readonly object _lock = new();
    public List<WidgetActionEvent> Actions { get; } = [];
    public TaskCompletionSource BlockStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseBlock { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Button("Block", "block", "block"),
            UI.Slider(0.5, 0, 1, 0.1, "volume.changed", "volume",
                "Game volume, unmuted, press A to mute", "50 percent", "volume.mute")),
        "block");

    public override async ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        lock (_lock) Actions.Add(action);
        if (action.ActionId == "block")
        {
            BlockStarted.TrySetResult();
            await ReleaseBlock.Task.WaitAsync(cancellationToken);
        }
    }

    public async Task WaitForActionsAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (true)
        {
            lock (_lock)
            {
                if (Actions.Count >= count) return;
            }
            await Task.Delay(10, timeout.Token);
        }
    }
}

file sealed class StateRoutingWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();
    public List<WidgetActionEvent> Actions { get; } = [];

    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public override WidgetView Render() => new(
        UI.Row("root",
            UI.Button("Unavailable", "disabled", "disabled").Disabled().Shortcut(ControllerButton.RightBumper),
            UI.Button("Saving", "busy", "busy").Busy().Shortcut(ControllerButton.X),
            UI.Button("Selected", "select", "selected").Selected()),
        "selected");

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        Actions.Add(action);
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class SurfaceRoutingWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();

    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public string ActiveScope { get; private set; } = "root";
    public void SetSurface(string scopeId) => ActiveScope = scopeId;

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Button("Root back", "root-back", "root-back"),
            UI.Button("Root focus", "root-focus", "root-focus"),
            UI.Stack("dialog",
                UI.Button("Dialog back", "dialog-back", "dialog-back"),
                UI.Button("Dialog focus", "dialog-focus", "dialog-focus"),
                UI.Button("Dialog own", "dialog-own-back", "dialog-own")
                    .Shortcut(ControllerButton.B, actionId: "dialog-own-back"),
                UI.Button("Dialog disabled", "dialog-disabled-back", "dialog-disabled")
                    .Disabled().Shortcut(ControllerButton.B, actionId: "dialog-disabled-back"),
                UI.Button("Dialog busy", "dialog-busy-back", "dialog-busy")
                    .Busy().Shortcut(ControllerButton.B, actionId: "dialog-busy-back"),
                UI.Button("Dialog disabled inherit", "dialog-disabled-inherit-action",
                    "dialog-disabled-inherit").Disabled())
                .InputScope("dialog-window")
                .Shortcut(ControllerButton.LeftBumper, "dialog-back")
                .Shortcut(ControllerButton.B, "dialog-close"),
            UI.Stack("empty-dialog",
                UI.Button("Empty focus", "empty-focus", "empty-focus"))
                .InputScope("empty-window"))
            .Shortcut(ControllerButton.LeftBumper, "root-back"),
        ActiveScope switch
        {
            "dialog-window" => "dialog-focus",
            "empty-window" => "empty-focus",
            _ => "root-focus",
        },
        ActiveInputScopeId: ActiveScope);

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class ActionSheetRoutingWidget : Widget
{
    private readonly System.Threading.Channels.Channel<WidgetActionEvent> _observed =
        System.Threading.Channels.Channel.CreateUnbounded<WidgetActionEvent>();

    public ValueTask<WidgetActionEvent> NextActionAsync() => _observed.Reader.ReadAsync();

    public override WidgetView Render() => new(
        UI.ActionSheet(
            "Choose",
            "sheet",
            "sheet.scope",
            "sheet.dismiss",
            [
                new ActionSheetItem("sheet.ready", "Ready", "sheet.choose"),
                new ActionSheetItem("sheet.busy", "Busy", "sheet.busy-action", IsBusy: true),
                new ActionSheetItem("sheet.disabled", "Disabled", "sheet.disabled-action", IsDisabled: true),
            ]),
        InitialFocusId: "sheet.busy",
        ActiveInputScopeId: "sheet.scope");

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        _observed.Writer.TryWrite(action);
        return ValueTask.CompletedTask;
    }
}

file sealed class CapabilityWidget : Widget
{
    private static readonly WidgetCapabilityOperation<string, string> Operation =
        new("test.capability.v1", "test.invoke");

    public bool CapabilitiesAvailable => HostServices.Capabilities.IsAvailable;
    public WidgetLifecycleState TestLifecycleState => LifecycleState;
    public WidgetAudioService Audio => HostServices.Audio;
    public WidgetNetworkService Network => HostServices.Network;
    public WidgetAppLibraryService AppLibrary => HostServices.AppLibrary;
    public WidgetLoopbackHttpService Loopback => HostServices.Loopback;
    public WidgetPrivateSecretService PrivateSecrets => HostServices.PrivateSecrets;
    public WidgetPrivateStateService PrivateState => HostServices.PrivateState;
    public ValueTask<string> CallAsync() =>
        HostServices.Capabilities.InvokeAsync(Operation, "request");
    public override WidgetView Render() => new(UI.Text("Ready", "root"));
}

file sealed class FakeCapabilityClient : IWidgetCapabilityClient
{
    public bool IsAvailable => true;
    public string? LastCapabilityId { get; private set; }
    public List<string> OperationIds { get; } = [];

    public ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        LastCapabilityId = operation.CapabilityId;
        OperationIds.Add(operation.OperationId);
        object response = operation.OperationId switch
        {
            "test.invoke" => "accepted",
            "audio.sessions.list" => new WidgetAudioSession[]
                { new("audio-1", "Game", 0.75, false, true) },
            "network.status.get" => new WidgetNetworkStatus(
                WidgetNetworkConnectivity.Internet,
                WidgetNetworkTransportKind.Wifi,
                WidgetNetworkWirelessAvailability.Available,
                WidgetNetworkDetailsAccess.Available,
                WidgetNetworkConnectionAttemptState.None,
                null,
                "wifi-1",
                "Wi-Fi",
                80),
            "network.saved-profiles.list" => new WidgetSavedNetworkProfile[]
                { new("wifi-1", "Wi-Fi", true, 80) },
            _ => new WidgetCapabilityAcknowledgement(true),
        };
        return ValueTask.FromResult((TResponse)response);
    }

    public ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(
        WidgetCapabilityEvent<TPayload> platformEvent,
        CancellationToken cancellationToken = default)
    {
        var item = (TPayload)(object)new WidgetAudioSessionsChanged(
            [new("audio-1", "Game", 0.75, false, true)]);
        return ValueTask.FromResult<IWidgetCapabilitySubscription<TPayload>>(
            new SingleEventSubscription<TPayload>(item));
    }
}

file sealed class SingleEventSubscription<TPayload>(TPayload item) :
    IWidgetCapabilitySubscription<TPayload>
{
    public async IAsyncEnumerable<TPayload> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return item;
        await Task.Yield();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual(byte[] expected, byte[] actual, string message)
    {
        if (!expected.AsSpan().SequenceEqual(actual)) throw new InvalidOperationException(message);
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
