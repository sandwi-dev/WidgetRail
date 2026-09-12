using WidgetRail.Samples.SpotifyWidget;
using WidgetRail.WrailCli;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using System.Security.Cryptography;
using System.Text;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Playlist disk cache survives restart and isolates authorizations", PlaylistDiskPersistence),
    ("Playlist disk failures corruption and quota limits remain cache misses", PlaylistDiskFailures),
    ("Playlist snapshot versions reuse pages only after fresh metadata", PlaylistVersionCache),
    ("Playlist cache evicts bounded pages and rejects obsolete writes", PlaylistCacheBounds),
    ("Playlist reopen reuses pages while explicit refresh fetches again", PlaylistCacheNavigation),
    ("Playlist route publication never mixes list focus with detail content", PlaylistRoutePublication),
    ("Queue selection preserves suffix and skips first item without replacing context", QueuePlaybackPreservesTail),
    ("Track menu adds once without changing X or fetching collections", TrackMenuRequestBudget),
    ("Play here follow-up reads stop after the bounded settlement budget", LocalStartSettlementBudget),
    ("Play here reconciles stale idle playback without waiting for idle poll", LocalStartReconcilesIdle),
    ("Search starts first, pages typed results and routes playback", SearchResultsAndPlayback),
    ("Search drops late responses and preserves query across reopen", SearchLateResponses),
    ("Search accepts changing totals and repeated results without stale virtual windows", SearchMutablePaging),
    ("Section round trips after search always target a rendered focus group", SearchSectionRoundTrips),
    ("Production cursor batches retain forward and reverse buffers", ProductionCursorBuffer),
    ("Opening the widget never starts OAuth", OpeningNeverConnects),
    ("Disconnected copy and paired actions remain bounded and centered", DisconnectedLayoutContract),
    ("Primary actions keep theme-safe fill and focus contrast", PrimaryActionContrast),
    ("Unconfigured state provides safe exact setup guidance", UnconfiguredSetup),
    ("Setup is a nested B-dismissible input scope", NestedSetupBack),
    ("Setup uses a bounded controller-native scroll surface", SetupUsesVerticalScroll),
    ("Reopening setup starts a fresh scroll entry", SetupReopenResetsScrollIdentity),
    ("Setup code and text styles remain compact and bounded", SetupCodeAndTextAreBounded),
    ("Setup check refreshes newly saved configuration without starting OAuth", SetupCheckRefreshesConfiguration),
    ("Configured Ready exposes one setup action in both responsive rails", ConfiguredReadySetupAction),
    ("Committed setup text writes once and refreshes configuration", SetupConfigureWritesAndRefreshes),
    ("Setup write failure remains visible and retryable", SetupConfigureFailure),
    ("Setup write cancellation retains configuration and the setup scope", SetupConfigureCancellation),
    ("Connect action acknowledges while OAuth remains pending", ConnectAcknowledgesWhilePending),
    ("OAuth survives Background and reconciles when visible", ConnectSurvivesBackground),
    ("Explicit connect requests the four implemented least-privilege scopes", ExplicitConnect),
    ("Ready UI exposes native controller transport and attribution", ReadyControllerUi),
    ("Ready UI publishes responsive wide and compact navigation", ResponsiveNavigation),
    ("Ready UI publishes persistent player navigation metadata and WRSS contracts", SpotifyResponsiveLayoutTests.PersistentPlayerNavigationAndMetadataContract),
    ("LT RT and nested Y B preserve route and focus authority", SectionNavigationAndNestedFocus),
    ("Collection pages load lazily and remain cached", LazyPageLoading),
    ("Typed pinned Up Next demand survives lifecycle and transport",
        TypedPinnedUpNextDemandSurvivesLifecycleAndTransport),
    ("Queue traverses every bounded occurrence without wrapping", QueueTraversesContinuously),
    ("Playlist pages load automatically in bounded cached windows", MaximumPlaylistPageContract),
    ("Controller prefetch preserves focus before subsequent 12/12/5 navigation", ControllerPlaylistPrefetchRoundTrip),
    ("Continuous playlist detail resets on refresh and preserves one header edge", ContinuousPlaylistDetailAnchorAndHeader),
    ("Duplicate queue occurrences keep unique exact actions", DuplicateQueueOccurrencesRouteExactly),
    ("Duplicate playlist occurrences survive paging churn and eviction", DuplicatePlaylistOccurrencesStayKeyed),
    ("Occurrence identity retention is bounded by the collection window", OccurrenceIdentityIsBounded),
    ("Single-track playlist has no self focus edge", SingleTrackPlaylistHasNoSelfEdge),
    ("Paged playlist Back restores the opened item", PagedPlaylistBackRestoresOpenedItem),
    ("Adjacent playlist failures remain visible and retryable", AdjacentPlaylistFailureRetry),
    ("Sparse Spotify pages remain controller-navigable", SparsePlaylistPage),
    ("Playlist detail is a B-dismissible navigation entry", PlaylistDetailBack),
    ("Failed playlist detail keeps valid focus and retries the detail", PlaylistDetailFailureRetry),
    ("Slow playlist detail acknowledges and cannot reopen after B", SlowPlaylistDetailBack),
    ("Superseded playlist detail cannot publish into a newer selection", SupersededPlaylistDetail),
    ("A slow refresh cannot replace a newer playlist route", RefreshPreservesNewerPlaylist),
    ("Playlist detail cancellation reloads the retained selection on reactivation", PlaylistDetailLifecycle),
    ("Active polling reuses configuration and authorization state", PollingRequestBudget),
    ("Transient refresh failures retain the last-good route and use bounded backoff", TransientRefreshRetainsLastGood),
    ("Fatal refresh failures select exact safe states", FatalRefreshFailuresSelectSafeState),
    ("Refresh polling cannot publish after the Active lifetime", RefreshPollingLifecycle),
    ("Devices expose trusted local playback and safe transfer actions", DeviceActions),
    ("Missing playback device produces actionable guidance", MissingPlaybackDeviceGuidance),
    ("Progress is projected locally without provider polling", ProjectedProgress),
    ("Playback actions publish optimistic state and reconcile", OptimisticPlayback),
    ("X playback keeps the stable page entry through pending and terminal",
        PlaybackShortcutKeepsStablePageEntry),
    ("Optimistic controls survive pre-response observations and settle",
        OptimisticControlsSurvivePreResponseObservations),
    ("Optimistic controls roll back with exact terminal ownership",
        OptimisticControlsRollbackWithExactOwnership),
    ("Repeat is binary and blocks only disallowed activation",
        RepeatIsBinaryAndPreciselyAvailable),
    ("Spotify runtime diagnostics are quiet by default and retain typed failures",
        PlaybackDiagnosticsAreFailureOnly),
    ("Local playback summary observations honor the operation revision fence",
        LocalPlaybackSummaryRevisionFence),
    ("Failed controls roll back optimistic state", FailedControlRollback),
    ("Permission denial remains an actionable UI state", PermissionDenied),
    ("Manifest declares only the generic full-trust application route", ManifestContract),
    ("Pure Spotify presentation repeats semantically", PresentationBoundaryIsPure),
    ("Spotify action and route policy is closed and value based", RouteActionPolicyIsClosed),
    ("Spotify playback reconciliation is independently deterministic", PlaybackPolicyIsDeterministic),
    ("Spotify internals have real stable-responsibility boundaries", ResponsibilitySplitContract),
    ("Time labels are stable", TimeFormatting),
};

var testPrefixIndex = Array.IndexOf(args, "--test-prefix");
if (testPrefixIndex >= 0)
{
    if (testPrefixIndex + 1 >= args.Length)
        throw new ArgumentException("Missing --test-prefix value.");
    var prefix = args[testPrefixIndex + 1];
    tests = tests
        .Where(test => test.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (tests.Length == 0)
        throw new InvalidOperationException($"No tests match prefix '{prefix}'.");
}

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {name}: {exception.Message}");
        Console.WriteLine(failures[^1]);
        if (exception is ProtocolValidationException protocol)
            foreach (var error in protocol.Errors)
                Console.WriteLine($"  {error.Path}: {error.Code}: {error.Message}");
    }
}

Console.WriteLine($"Spotify widget tests: {tests.Length - failures.Count}/{tests.Length} passed.");
if (failures.Count != 0) Environment.ExitCode = 1;

static async Task OpeningNeverConnects()
{
    var harness = new SpotifyHarness { Configured = true, Connected = false };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Disconnected);
    Assert.Equal(0, harness.ConnectCalls);
    Assert.Equal("spotify.connect", widget.Render().InitialFocusId);
    await StopAsync(widget);
}

static async Task DisconnectedLayoutContract()
{
    var harness = new SpotifyHarness { Configured = true, Connected = false };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Disconnected);
    var snapshot = widget.Render().CreateSnapshot("spotify.layout", 1);

    var detail = Find(snapshot.Root, "spotify.state-detail");
    Assert.Equal(ViewNodeKind.Scroll, Find(snapshot.Root, "spotify.state-scroll").Kind);
    Assert.Equal(
        "Spotify opens a browser and uses PKCE. Your credentials stay in the trusted host.",
        detail.Text);
    Assert.Equal("Connect", Find(snapshot.Root, "spotify.connect").Text);
    Assert.Equal("Setup", Find(snapshot.Root, "spotify.setup.open").Text);
    Assert.Equal("spotify.connect", snapshot.InitialFocusId);
    Assert.Equal(620d, snapshot.Surface?.MinimumWidth);
    Assert.Equal(400d, snapshot.Surface?.MinimumHeight);
    var brand = Find(snapshot.Root, "spotify.brand.full-logo");
    Assert.Equal(ViewNodeKind.Icon, brand.Kind);
    Assert.Equal(WidgetGlyph.Music, brand.Glyph);
    Assert.Equal("spotify.brand.full-green", brand.PackageIcon?.AssetId);
    Assert.Equal(WidgetPackageIconColorMode.OriginalColor,
        brand.PackageIcon?.ColorMode);
    Assert.Equal("Spotify", brand.AccessibilityLabel);
    Assert.True(!brand.IsFocusable,
        "The decorative Spotify header wordmark must remain nonfocusable.");
    Assert.True(!ContainsId(snapshot.Root, "spotify.eyebrow"),
        "The retired separate Spotify brand-text node is still published.");
    Assert.True(!ContainsText(snapshot.Root, "SPOTIFY"),
        "The full wordmark must not duplicate a separate SPOTIFY text label.");

    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "styles", "default.wrss"));
    var parsed = WrssParser.Parse(source, "styles/default.wrss");
    Assert.True(parsed.IsValid, string.Join(Environment.NewLine, parsed.Diagnostics));
    Assert.True(source.Contains(".spotify-primary { background: var(--accent);", StringComparison.Ordinal),
        "Primary actions must derive their fill from the active theme accent.");
    Assert.True(source.Contains("outline-color: var(--focus)", StringComparison.Ordinal),
        "Focused primary actions must derive their outline from the active theme focus token.");
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    var theme = compiled.Theme!;
    var fullLogo = theme.Resolve(new WrssElement(
        "icon", StyleClasses: new HashSet<string>(["spotify-full-logo"])))!;
    Assert.Equal("117px", fullLogo.Get("width")?.Text);
    Assert.Equal("32px", fullLogo.Get("height")?.Text);
    Assert.Equal("0", fullLogo.Get("flex-shrink")?.Text);
    var detailStyle = theme.Resolve(new WrssElement(
        "text", StyleClasses: new HashSet<string>(["spotify-state-detail"])))!;
    Assert.Equal("100%", detailStyle.Get("width")?.Text);
    Assert.Equal("0px", detailStyle.Get("min-width")?.Text);
    Assert.Equal("560px", detailStyle.Get("max-width")?.Text);
    Assert.Equal("0", detailStyle.Get("flex-shrink")?.Text);
    Assert.Equal<string?>(null, detailStyle.Get("max-lines")?.Text);
    Assert.Equal("1.35", detailStyle.Get("line-height")?.Text);
    Assert.Equal("center", detailStyle.Get("text-align")?.Text);

    var actionRow = theme.Resolve(new WrssElement(
        "row", StyleClasses: new HashSet<string>(["spotify-connect-actions"])))!;
    Assert.Equal("100%", actionRow.Get("width")?.Text);
    Assert.Equal("wrap", actionRow.Get("flex-wrap")?.Text);
    Assert.Equal("0", actionRow.Get("flex-shrink")?.Text);
    var button = theme.Resolve(new WrssElement(
        "button", StyleClasses: new HashSet<string>(
            ["spotify-primary", "spotify-responsive-action"])))!;
    Assert.Equal("center", button.Get("text-align")?.Text);
    Assert.Equal("44px", button.Get("min-height")?.Text);
    Assert.Equal("1.25", button.Get("line-height")?.Text);
    Assert.Equal("2", button.Get("max-lines")?.Text);
    Assert.Equal("1", button.Get("flex-grow")?.Text);
    await StopAsync(widget);
}

static async Task PrimaryActionContrast()
{
    var harness = new SpotifyHarness { Configured = true, Connected = false };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Disconnected);
    var disconnected = widget.RenderSnapshot("spotify.contrast", 1);
    Assert.True(Find(disconnected.Root, "spotify.connect").StyleClasses.Contains("spotify-primary"),
        "Connect lost the shared primary-action style.");

    await StopAsync(widget);

    harness = new SpotifyHarness { Configured = false, Connected = false };
    widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Unconfigured);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", "spotify.setup.open"));
    var setup = widget.RenderSnapshot("spotify.contrast", 2);
    Assert.True(Find(setup.Root, "spotify.setup.done").StyleClasses.Contains("spotify-primary"),
        "Check configuration lost the shared primary-action style.");

    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "styles", "default.wrss"));
    var parsed = WrssParser.Parse(source, "styles/default.wrss");
    Assert.True(parsed.IsValid, string.Join(Environment.NewLine, parsed.Diagnostics));
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    var classes = new HashSet<string>(["spotify-primary"]);
    var normal = compiled.Theme!.Resolve(new WrssElement("button", null, classes, null))!;
    var focused = compiled.Theme.Resolve(new WrssElement(
        "button", null, classes,
        new HashSet<WrssPseudoState> { WrssPseudoState.Focused }))!;
    Assert.Equal("#8f80ff", normal.Get("background")?.Text);
    Assert.Equal("#090908", normal.Get("color")?.Text);
    Assert.Equal("#ff7898", focused.Get("outline-color")?.Text);
    Assert.True(normal.Get("background")?.Text != focused.Get("outline-color")?.Text,
        "Primary fill and focused outline must use distinct semantic theme tokens.");
    Assert.Equal("-2px", focused.Get("outline-offset")?.Text);
    await StopAsync(widget);
}

static async Task UnconfiguredSetup()
{
    var harness = new SpotifyHarness { Configured = false };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Unconfigured);
    var snapshot = widget.Render().CreateSnapshot("spotify.test", 1);
    Assert.NotNull(Find(snapshot.Root, "spotify.setup.open"));
    Assert.Equal(0, harness.ConnectCalls);
    await StopAsync(widget);
}

static async Task NestedSetupBack()
{
    var harness = new SpotifyHarness { Configured = false };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Unconfigured);
    await widget.OnActionAsync(new WidgetActionEvent("spotify.setup.open", "spotify.setup.open"));
    var setup = widget.RenderSnapshot("spotify.test", 3);
    Assert.Equal("spotify.setup", setup.ActiveInputScopeId);
    Assert.True(Find(setup.Root, "spotify.setup-step-2").Text!.Contains(
        SpotifyApplicationContract.ExactRedirectUri, StringComparison.Ordinal),
        "Exact redirect URI was not rendered.");
    var input = Find(setup.Root, "spotify.setup.client-id");
    Assert.Equal("Enter public Spotify Client ID", input.TextEntryPlaceholder);
    Assert.Equal(ProtocolConstants.MaximumTextEntryLength,
        input.TextEntryMaximumLength);
    var captured = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.B, ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget,
        "spotify.setup.done", ActiveInputScopeId: setup.ActiveInputScopeId,
        SnapshotSequence: setup.Sequence));
    Assert.True(captured, "Nested setup did not capture B.");
    await WaitUntil(() => widget.Render().InitialFocusId == "spotify.setup.open");
    Assert.Equal("spotify.setup.open", widget.Render().InitialFocusId);
    var root = widget.RenderSnapshot("spotify.test", 4);
    Assert.True(!await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.B, ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget,
        "spotify.setup.open", ActiveInputScopeId: root.ActiveInputScopeId,
        SnapshotSequence: root.Sequence)), "Root captured host-owned B.");
    await StopAsync(widget);
}

static async Task SetupCheckRefreshesConfiguration()
{
    var harness = new SpotifyHarness { Configured = false, Connected = false };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Unconfigured);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", "spotify.setup.open"));
    var setup = widget.RenderSnapshot("spotify.test", 5);
    Assert.Equal("spotify.setup.dashboard", setup.InitialFocusId);
    var readsBeforeCheck = harness.ConfigurationCalls;

    // Simulates `config set` completing while the setup page remains open.
    harness.Configured = true;
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.done", "spotify.setup.done"));
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Disconnected);
    Assert.True(harness.ConfigurationCalls > readsBeforeCheck,
        "Check configuration did not perform a fresh configuration read.");
    Assert.Equal(0, harness.ConnectCalls);
    Assert.Equal("spotify.connect", widget.Render().InitialFocusId);
    await StopAsync(widget);
}

static async Task ConfiguredReadySetupAction()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var ready = widget.RenderSnapshot("spotify.setup.ready", 1);
    AssertShortcut(ready.Root, ControllerButton.Y, "spotify.setup.open");
    Assert.Equal("Settings",
        Find(ready.Root, "spotify.navigation.settings.hint.label").Text);

    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", ready.Root.Id));
    var setup = widget.RenderSnapshot("spotify.setup.ready", 2);
    Assert.Equal("spotify.setup", setup.ActiveInputScopeId);
    var input = Find(setup.Root, "spotify.setup.client-id");
    Assert.Equal("spotify.setup.client-id", input.ActionId);
    Assert.Equal(SpotifyApplicationContract.MaximumClientIdInputCharacters,
        input.TextEntryMaximumLength);
    Assert.Equal(ProtocolConstants.MaximumTextEntryLength, input.TextEntryMaximumLength);
    Assert.Equal(0, harness.ConfigureClientCalls);
    Assert.Equal(0, harness.ConnectCalls);
    await StopAsync(widget);
}

static async Task SetupConfigureWritesAndRefreshes()
{
    const string replacementClientId = "ReplacementClient123456";
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", "spotify.setup.open.wide"));
    var readsBeforeWrite = harness.ConfigurationCalls;

    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.client-id", "spotify.setup.client-id")
        { CommittedText = replacementClientId });

    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Disconnected);
    Assert.Equal(1, harness.ConfigureClientCalls);
    Assert.Equal(replacementClientId, harness.LastConfiguredClientId);
    Assert.True(harness.ConfigurationCalls > readsBeforeWrite,
        "Successful setup did not perform a fresh configuration read.");
    Assert.Equal(0, harness.ConnectCalls);
    Assert.True(!ContainsId(widget.RenderSnapshot("spotify.setup.write", 1).Root,
        "spotify.setup.client-id"), "Successful setup did not close its nested scope.");
    await StopAsync(widget);
}

static async Task SetupConfigureFailure()
{
    var harness = SpotifyHarness.Ready();
    harness.ConfigureClientHandler = (_, _) =>
        ValueTask.FromException<SpotifyConfigurationSummary>(
            new SpotifyApplicationException(
                "configuration_write_failed", "Spotify configuration could not be saved."));
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", "spotify.setup.open.wide"));
    var readsBeforeWrite = harness.ConfigurationCalls;

    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.client-id", "spotify.setup.client-id")
        { CommittedText = "ReplacementClient123456" });

    Assert.Equal(1, harness.ConfigureClientCalls);
    Assert.Equal(readsBeforeWrite, harness.ConfigurationCalls);
    Assert.Equal("Spotify configuration could not be saved.", widget.Status);
    Assert.NotNull(Find(widget.RenderSnapshot("spotify.setup.failure", 1).Root,
        "spotify.setup.client-id"));
    await StopAsync(widget);
}

static async Task SetupConfigureCancellation()
{
    var admitted = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var cancellationObserved = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var harness = SpotifyHarness.Ready();
    harness.ConfigureClientHandler = async (_, cancellationToken) =>
    {
        admitted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException("Canceled setup write unexpectedly completed.");
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
                cancellationObserved.TrySetResult();
        }
    };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", "spotify.setup.open.wide"));
    var readsBeforeWrite = harness.ConfigurationCalls;
    using var cancellation = new CancellationTokenSource();
    var write = widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.client-id", "spotify.setup.client-id")
        { CommittedText = "ReplacementClient123456" }, cancellation.Token).AsTask();
    await admitted.Task.WaitAsync(TimeSpan.FromSeconds(3));
    cancellation.Cancel();
    await write.WaitAsync(TimeSpan.FromSeconds(3));
    await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));

    Assert.Equal(1, harness.ConfigureClientCalls);
    Assert.Equal(readsBeforeWrite, harness.ConfigurationCalls);
    Assert.Equal("Spotify setup canceled", widget.Status);
    Assert.NotNull(Find(widget.RenderSnapshot("spotify.setup.cancel", 1).Root,
        "spotify.setup.client-id"));
    await StopAsync(widget);
}

static async Task SetupUsesVerticalScroll()
{
    var harness = new SpotifyHarness { Configured = false };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Unconfigured);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", "spotify.setup.open"));

    var setup = widget.RenderSnapshot("spotify.test", 6);
    var scroll = FindPrefix(setup.Root, "spotify.setup-scroll.");
    Assert.Equal(ViewNodeKind.Scroll, scroll.Kind);
    Assert.Equal(ScrollAxis.Vertical, scroll.ScrollAxis);
    Assert.Equal("spotify.setup", setup.ActiveInputScopeId);
    AssertShortcut(setup.Root, ControllerButton.B, "spotify.navigation.back");
    Assert.NotNull(Find(scroll, "spotify.setup-step-1"));
    Assert.NotNull(Find(scroll, "spotify.setup-step-2"));
    Assert.NotNull(Find(scroll, "spotify.setup-step-3"));
    var card = Find(scroll, "spotify.setup-card");
    Assert.Equal("spotify.setup.dashboard", card.Children[2].Id);
    Assert.Equal("spotify.setup.copy-redirect", card.Children[4].Id);
    Assert.Equal("spotify.setup.client-id", card.Children[6].Id);
    Assert.Equal("spotify.setup.done", card.Children[7].Id);
    Assert.Equal("spotify.setup.dashboard", setup.InitialFocusId);
    Assert.True(setup.Surface?.MinimumHeight <= 404,
        "Setup requires a surface taller than the compact widget viewport.");
    await StopAsync(widget);
}

static Task SetupCodeAndTextAreBounded()
{
    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "styles", "default.wrss"));
    var parsed = WrssParser.Parse(source, "styles/default.wrss");
    Assert.True(parsed.IsValid, string.Join(Environment.NewLine, parsed.Diagnostics));
    var compiled = WrssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));

    var step = compiled.Theme!.Resolve(new WrssElement(
        "text", StyleClasses: new HashSet<string>(["spotify-setup-step"])))!;
    var input = compiled.Theme.Resolve(new WrssElement(
        "text-entry", StyleClasses: new HashSet<string>(["spotify-setup-input"])))!;
    Assert.Equal<string?>(null, step.Get("max-lines")?.Text);
    Assert.Equal("1.3", step.Get("line-height")?.Text);
    Assert.Equal("100%", input.Get("width")?.Text);
    Assert.Equal("44px", input.Get("min-height")?.Text);
    return Task.CompletedTask;
}

static async Task SetupReopenResetsScrollIdentity()
{
    var harness = new SpotifyHarness { Configured = false };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Unconfigured);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", "spotify.setup.open"));
    var first = FindPrefix(widget.RenderSnapshot("spotify.test", 7).Root,
        "spotify.setup-scroll.").Id;
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.close", "spotify.setup.done"));
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", "spotify.setup.open"));
    var second = FindPrefix(widget.RenderSnapshot("spotify.test", 8).Root,
        "spotify.setup-scroll.").Id;
    Assert.True(!string.Equals(first, second, StringComparison.Ordinal),
        "Setup reused the prior scroll identity and could restore a clipped offset.");
    await StopAsync(widget);
}

static async Task ConnectAcknowledgesWhilePending()
{
    var completion = NewAuthorizationCompletion();
    var harness = new SpotifyHarness
    {
        Configured = true,
        Connected = false,
        ConnectCompletion = completion,
    };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Disconnected);

    var acknowledgement = widget.OnActionAsync(new WidgetActionEvent(
        "spotify.connect", "spotify.connect")).AsTask();
    await acknowledgement.WaitAsync(TimeSpan.FromMilliseconds(500));
    await WaitUntil(() => harness.ConnectCalls == 1);
    Assert.True(!completion.Task.IsCompleted,
        "Fake OAuth unexpectedly completed before the action acknowledgement.");
    Assert.Equal(SpotifyWidgetViewState.Authorizing, widget.ViewState);

    completion.SetResult(ConnectedAuthorization());
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await StopAsync(widget);
}

static async Task ConnectSurvivesBackground()
{
    var completion = NewAuthorizationCompletion();
    var harness = new SpotifyHarness
    {
        Configured = true,
        Connected = false,
        ConnectCompletion = completion,
    };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Disconnected);
    await widget.OnActionAsync(new WidgetActionEvent("spotify.connect", "spotify.connect"));
    await WaitUntil(() => harness.ConnectCalls == 1);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    Assert.True(harness.ConnectCancellationToken is { IsCancellationRequested: false },
        "Visible to Background canceled the in-flight OAuth request.");
    completion.SetResult(ConnectedAuthorization());
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    Assert.True(widget.Status.Contains("reopen", StringComparison.OrdinalIgnoreCase),
        "Background OAuth completion did not retain a reopen-ready state.");
    Assert.Equal(0, harness.PlaybackCalls);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await WaitUntil(() => harness.PlaybackCalls > 0 &&
        widget.ViewState == SpotifyWidgetViewState.Ready);
    Assert.NotNull(Find(widget.RenderSnapshot("spotify.oauth", 8).Root,
        "spotify.play-toggle"));
    await StopAsync(widget);
}

static TaskCompletionSource<SpotifyAuthorizationSummary> NewAuthorizationCompletion() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

static SpotifyAuthorizationSummary ConnectedAuthorization() => new(
    SpotifyAuthorizationState.Connected,
    [SpotifyAuthorizationScope.PlaybackStateRead,
     SpotifyAuthorizationScope.PlaybackStateControl],
    [SpotifyAuthorizationScope.PlaybackStateRead,
     SpotifyAuthorizationScope.PlaybackStateControl],
    "Connected");

static async Task ExplicitConnect()
{
    var harness = new SpotifyHarness { Configured = true, Connected = false };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Disconnected);
    await widget.OnActionAsync(new WidgetActionEvent("spotify.connect", "spotify.connect"));
    Assert.Equal(1, harness.ConnectCalls);
    Assert.SequenceEqual(new[]
    {
        SpotifyAuthorizationScope.PlaybackStateRead,
        SpotifyAuthorizationScope.PlaybackStateControl,
        SpotifyAuthorizationScope.LocalPlayback,
        SpotifyAuthorizationScope.PlaylistsRead,
    }, harness.LastScopes!);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    Assert.Equal(SpotifyWidgetViewState.Ready, widget.ViewState);
    await StopAsync(widget);
}

static async Task ReadyControllerUi()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var snapshot = widget.Render().CreateSnapshot("spotify.ready", 8);
    Assert.Equal(PlaylistFocus("shared", "playlist-one"), snapshot.InitialFocusId);
    Assert.Equal("spotify.window", snapshot.ActiveInputScopeId);
    Assert.Equal("Spotify", Find(snapshot.Root, "spotify.attribution").Text);
    Assert.Equal(WidgetGlyph.Previous, Find(snapshot.Root, "spotify.previous").Glyph);
    Assert.Equal(WidgetGlyph.Next, Find(snapshot.Root, "spotify.next").Glyph);
    var scrubber = Find(snapshot.Root, "spotify.seek.slider");
    Assert.Equal(ViewNodeKind.Slider, scrubber.Kind);
    Assert.Equal("spotify.seek", scrubber.ValueChangedActionId);
    Assert.Equal(SliderInteractionMode.ActivateToAdjust, scrubber.SliderInteractionMode);
    Assert.Equal("spotify.shuffle", Find(snapshot.Root, "spotify.previous").Focus!.Left);
    Assert.Equal("spotify.seek.slider", Find(snapshot.Root, "spotify.previous").Focus!.Up);
    Assert.Equal("spotify.play-toggle", Find(snapshot.Root, "spotify.previous").Focus!.Right);
    Assert.Equal("spotify.previous", Find(snapshot.Root, "spotify.play-toggle").Focus!.Left);
    Assert.Equal("spotify.seek.slider", Find(snapshot.Root, "spotify.play-toggle").Focus!.Up);
    Assert.Equal("spotify.next", Find(snapshot.Root, "spotify.play-toggle").Focus!.Right);
    Assert.Equal("spotify.play-toggle", Find(snapshot.Root, "spotify.next").Focus!.Left);
    Assert.Equal("spotify.seek.slider", Find(snapshot.Root, "spotify.next").Focus!.Up);
    Assert.Equal("spotify.repeat", Find(snapshot.Root, "spotify.next").Focus!.Right);
    Assert.Equal("spotify.previous", Find(snapshot.Root, "spotify.shuffle").Focus!.Right);
    Assert.Equal("spotify.next", Find(snapshot.Root, "spotify.repeat").Focus!.Left);
    Assert.Equal("spotify.play-toggle", scrubber.Focus!.Down);
    AssertShortcut(snapshot.Root, ControllerButton.LeftBumper, "spotify.previous");
    AssertShortcut(snapshot.Root, ControllerButton.X, "spotify.play-toggle");
    AssertShortcut(snapshot.Root, ControllerButton.RightBumper, "spotify.next");
    Assert.Equal(3, snapshot.QuickActions.Count);
    Assert.True(snapshot.QuickActions.All(action => action.Capability is null),
        "Full-trust package actions unexpectedly depend on a sandbox capability.");
    await StopAsync(widget);
}

static async Task ResponsiveNavigation()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var snapshot = widget.RenderSnapshot("spotify.responsive", 1);
    Assert.True(snapshot.ProtocolVersion >= ProtocolConstants.FocusPersistenceVersion,
        "Responsive navigation did not negotiate focus-persistence support.");
    var compact = FindClass(snapshot.Root, "wrail-navigation-shell__compact");
    Assert.Equal(ResponsiveVisibility.Always, compact.VisibleWhen);
    Assert.Equal(6, compact.Children.Count);
    Assert.SequenceEqual(new[]
    {
        "spotify.nav.search", "spotify.nav.queue", "spotify.nav.playlists", "spotify.nav.devices",
    }, compact.Children
        .Where(child => child.ActionId is not null)
        .Select(child => child.ActionId!));
    Assert.Equal<string?>(null, Find(compact, "spotify.section.previous.hint").ActionId);
    Assert.Equal<string?>(null, Find(compact, "spotify.section.next.hint").ActionId);
    Assert.NotNull(Find(snapshot.Root, "spotify.connected.panes"));
    Assert.NotNull(Find(snapshot.Root, "spotify.card"));
    Assert.Equal(1, CountId(snapshot.Root, "spotify.track-title"));
    Assert.Equal("Small Hours", Find(snapshot.Root, "spotify.track-title").Text);
    Assert.Equal("Northern Lines", Find(snapshot.Root, "spotify.track-subtitle").Text);
    Assert.Equal("Night Drive", Find(snapshot.Root, "spotify.context").Text);
    Assert.True(!ContainsAction(snapshot.Root, "spotify.nav.player"),
        "The retired Player destination returned beside the persistent player.");
    await StopAsync(widget);
}

static async Task SectionNavigationAndNestedFocus()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var playlists = widget.RenderSnapshot("spotify.sections", 1);
    var playlistFocus = PlaylistFocus("shared", "playlist-one");
    Assert.Equal(playlistFocus, playlists.InitialFocusId);
    AssertShortcut(playlists.Root, ControllerButton.LeftTrigger,
        "spotify.nav.previous-section");
    AssertShortcut(playlists.Root, ControllerButton.RightTrigger,
        "spotify.nav.next-section");

    await widget.OnActionAsync(new("spotify.nav.next-section", playlistFocus));
    Assert.Equal(SpotifyDestination.Devices, widget.Destination);
    await widget.OnActionAsync(new("spotify.nav.previous-section",
        "spotify.devices.refresh.shared"));
    Assert.Equal(SpotifyDestination.Playlists, widget.Destination);

    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"), playlistFocus));
    var detail = widget.RenderSnapshot("spotify.detail-navigation", 2);
    var track = TrackFocus("shared", "spotify:track:next");
    Assert.NotNull(Find(detail.Root, track));
    AssertShortcut(detail.Root, ControllerButton.Y, "spotify.setup.open");
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.setup.open", "spotify.root",
        InputScopeId: detail.ActiveInputScopeId)
    {
        FocusedElementId = track,
    });
    var setup = widget.RenderSnapshot("spotify.detail-setup", 3);
    Assert.Equal("spotify.setup", setup.ActiveInputScopeId);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.navigation.back", "spotify.root",
        Phase: ControllerEventPhase.Pressed,
        InputScopeId: setup.ActiveInputScopeId)
    {
        FocusedElementId = "spotify.setup.close",
    });
    var returned = widget.RenderSnapshot("spotify.detail-return", 4);
    Assert.Equal("spotify.playlist.detail", returned.ActiveInputScopeId);
    Assert.Equal(track, returned.InitialFocusId);
    await StopAsync(widget);
}

static async Task LazyPageLoading()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    Assert.Equal(0, harness.QueueCalls);
    Assert.Equal(1, harness.PlaylistCalls);

    await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.wide.queue"));
    Assert.Equal(1, harness.QueueCalls);
    var queueSnapshot = widget.RenderSnapshot("spotify.queue", 1);
    var queueRow = QueueFocus("wide", "spotify:track:next");
    Assert.NotNull(Find(queueSnapshot.Root, queueRow));
    Assert.Equal("Next track \u00b7 3:21", Find(queueSnapshot.Root,
        queueRow + ".state").Text);
    Assert.True(!ContainsId(queueSnapshot.Root,
        queueRow + ".metadata"),
        "Queue item rendered a redundant fourth text row.");
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.playlists"));
    await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.queue"));
    Assert.Equal(1, harness.QueueCalls);

    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    Assert.Equal(1, harness.PlaylistCalls);
    Assert.NotNull(Find(widget.RenderSnapshot("spotify.playlists", 1).Root,
        PlaylistFocus("wide", "playlist-one")));
    await StopAsync(widget);
}

static async Task TypedPinnedUpNextDemandSurvivesLifecycleAndTransport()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var host = WidgetTestHost.CreatePinnedLayoutHost(
        widget, "spotify.pinned.handle", initialSequence: 10);

    Assert.True(await host.SelectAsync(SpotifyPresentation.UpNextPinnedLayoutId),
        "The final typed handle did not accept the Up Next selection.");
    await WaitUntil(() => harness.QueueCalls == 1,
        "Selecting typed Up Next did not admit its existing queue demand.");
    await host.ReplaceSnapshotAsync();
    AssertPinnedUpNextReady(host.CurrentSnapshot);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    await host.ReplaceSnapshotAsync();
    AssertPinnedUpNextReady(host.CurrentSnapshot);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await host.ReplaceSnapshotAsync();
    AssertPinnedUpNextReady(host.CurrentSnapshot);
    Assert.Equal(1, harness.QueueCalls);

    Assert.True(await host.RouteActionAsync(
            ControllerButton.RightBumper,
            "spotify.player.pinned-up-next.play-toggle"),
        "Pinned Right Bumper was not routed through the selected projection.");
    await WaitUntil(() =>
            harness.Commands.Count(command =>
                command.Operation == SpotifyPlaybackOperation.Next) == 1 &&
            harness.QueueCalls == 2,
        "Pinned Next did not perform exactly one demanded queue refresh.");
    await host.ReplaceSnapshotAsync();
    AssertPinnedUpNextReady(host.CurrentSnapshot);

    Assert.True(await host.RouteActionAsync(
            ControllerButton.LeftBumper,
            "spotify.player.pinned-up-next.play-toggle"),
        "Pinned Left Bumper was not routed through the selected projection.");
    await WaitUntil(() =>
            harness.Commands.Count(command =>
                command.Operation == SpotifyPlaybackOperation.Previous) == 1 &&
            harness.QueueCalls == 3,
        "Pinned Previous did not perform exactly one demanded queue refresh.");
    await host.ReplaceSnapshotAsync();
    AssertPinnedUpNextReady(host.CurrentSnapshot);

    await StopAsync(widget);
}

static void AssertPinnedUpNextReady(ViewSnapshot snapshot)
{
    var layout = snapshot.PinnedLayouts.Single(candidate =>
        candidate.Id == SpotifyPresentation.UpNextPinnedLayoutId);
    Assert.NotNull(layout.Root);
    Assert.True(!ContainsId(layout.Root!, "spotify.pinned-up-next.loading"),
        "Selected Up Next returned to its NotLoaded/Loading presentation.");
    Assert.NotNull(Find(layout.Root!,
        QueueFocus("pinned-up-next", "spotify:track:next")));
}

static async Task QueueTraversesContinuously()
{
    var items = Enumerable.Range(0, 50)
        .Select(index => new SpotifyMediaItemSummary(
            SpotifyPlaybackItemType.Track,
            $"Queue track {index:D2}",
            $"Artist {index:D2}",
            180_000 + index,
            null,
            $"spotify:track:queue-{index:D2}",
            $"https://open.spotify.com/track/queue-{index:D2}",
            true))
        .ToArray();
    var harness = SpotifyHarness.Ready();
    harness.Queue = new(harness.Queue.CurrentlyPlaying, items, false);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.wide.queue"));
    await WaitUntil(() => harness.QueueCalls == 1);

    var snapshot = widget.RenderSnapshot("spotify.queue.continuous", 1);
    var rows = Find(snapshot.Root, "spotify.queue.scroll").Children.ToArray();
    Assert.Equal(50, rows.Length);
    Assert.Equal(50, rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
    Assert.Equal(50, rows.Select(row => row.CollectionItemKey)
        .Distinct(StringComparer.Ordinal).Count());
    var replay = ControllerReplay.Run(snapshot, new InputReplay
    {
        InitialFocusId = rows[0].Id,
        Events = Enumerable.Repeat(
            new ReplayInputEvent { Button = ControllerButton.DPadDown }, 50).ToArray(),
    });
    Assert.True(replay[^1].FocusAfter == rows[^1].Id,
        $"Terminal Queue Down moved to row {Array.FindIndex(rows, row => row.Id == replay[^1].FocusAfter)}.");
    Assert.True(replay[^2].FocusAfter == rows[^1].Id,
        $"The final in-range Queue Down reached row {Array.FindIndex(rows, row => row.Id == replay[^2].FocusAfter)}.");
    var reverse = ControllerReplay.Run(snapshot, new InputReplay
    {
        InitialFocusId = rows[^1].Id,
        Events = Enumerable.Repeat(
            new ReplayInputEvent { Button = ControllerButton.DPadUp }, 49).ToArray(),
    });
    Assert.Equal(rows[0].Id, reverse[^1].FocusAfter);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    var reactivated = widget.RenderSnapshot("spotify.queue.reactivated", 2);
    Assert.Equal(50, Find(reactivated.Root, "spotify.queue.scroll").Children.Count);
    Assert.Equal(1, harness.QueueCalls);
    await StopAsync(widget);
}

static async Task PlaylistDetailBack()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"),
        PlaylistFocus("compact", "playlist-one")));
    var detail = widget.RenderSnapshot("spotify.playlist.detail", 1);
    var trackRow = TrackFocus("wide", "spotify:track:next");
    Assert.NotNull(Find(detail.Root, trackRow));
    Assert.Equal("3:21", Find(detail.Root,
        trackRow + ".state").Text);
    Assert.True(!ContainsId(detail.Root,
        trackRow + ".metadata"),
        "Playlist track rendered a redundant fourth text row.");
    AssertShortcut(detail.Root, ControllerButton.B, "spotify.navigation.back");
    Assert.Equal("spotify.playlist.play.shared", detail.InitialFocusId);
    await widget.OnActionAsync(new("spotify.playlist.back", "spotify.playlist.play.shared"));
    var list = widget.RenderSnapshot("spotify.playlist.list", 2);
    Assert.NotNull(Find(list.Root, PlaylistFocus("wide", "playlist-one")));
    Assert.Equal(PlaylistFocus("compact", "playlist-one"), list.InitialFocusId);
    Assert.True(!list.Root.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.B),
        "Playlist root unexpectedly consumed B instead of returning to the tray.");
    await StopAsync(widget);
}

static async Task PlaylistDetailFailureRetry()
{
    var harness = SpotifyHarness.Ready();
    harness.PlaylistDetailError = new SpotifyApplicationException(
        "spotify_unavailable", "Spotify could not load this playlist");
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"),
        PlaylistFocus("compact", "playlist-one")));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);

    var failure = widget.RenderSnapshot("spotify.playlist.failure", 1);
    Assert.Equal("spotify.page.error.shared.action", failure.InitialFocusId);
    Assert.NotNull(Find(failure.Root, failure.InitialFocusId!));

    harness.PlaylistDetailError = null;
    await widget.OnActionAsync(new("spotify.page.retry", failure.InitialFocusId!));
    await WaitUntil(() => harness.PlaylistDetailCalls == 2);
    await WaitUntil(() => widget.Render().InitialFocusId == "spotify.playlist.play.shared");
    var recovered = widget.RenderSnapshot("spotify.playlist.recovered", 2);
    Assert.NotNull(Find(recovered.Root, TrackFocus("compact", "spotify:track:next")));
    Assert.Equal("spotify.playlist.play.shared", recovered.InitialFocusId);
    await StopAsync(widget);
}

static async Task SlowPlaylistDetailBack()
{
    var completion = new TaskCompletionSource<SpotifyPlaylistItemsPageSummary>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var harness = SpotifyHarness.Ready();
    harness.PlaylistDetailCompletion = completion;
    harness.IgnorePlaylistDetailCancellation = true;
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));

    var acknowledgement = widget.OnActionAsync(new(
        PlaylistOpen("playlist-one"), PlaylistFocus("wide", "playlist-one"))).AsTask();
    await acknowledgement.WaitAsync(TimeSpan.FromMilliseconds(250));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);
    var loading = widget.RenderSnapshot("spotify.playlist.loading", 1);
    Assert.Equal(0, ViewSnapshotValidator.Validate(loading).Count);
    Assert.NotNull(Find(loading.Root, "spotify.page.loading.shared.action"));
    await widget.OnActionAsync(new("spotify.playlist.back",
        PlaylistFocus("wide", "playlist-one")));
    completion.SetResult(harness.PlaylistDetail);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

    var list = widget.RenderSnapshot("spotify.playlist.cancelled", 1);
    Assert.NotNull(Find(list.Root, PlaylistFocus("wide", "playlist-one")));
    Assert.Equal(PlaylistFocus("wide", "playlist-one"), list.InitialFocusId);
    await WidgetTestHost.DestroyAsync(widget);
}

static async Task SupersededPlaylistDetail()
{
    foreach (var failLateRequest in new[] { false, true })
    {
        var playlistA = Playlist("playlist-a", "Playlist A");
        var playlistB = Playlist("playlist-b", "Playlist B");
        var detailA = PlaylistItems("Track A", "spotify:track:a");
        var detailB = PlaylistItems("Track B", "spotify:track:b");
        var aStarted = NewSignal();
        var aCompletion = new TaskCompletionSource<SpotifyPlaylistItemsPageSummary>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = SpotifyHarness.Ready();
        harness.Playlists = new([playlistA, playlistB], 0, 50, 2);
        harness.PlaylistDetailHandler = async (request, _) =>
        {
            if (request.PlaylistId == playlistA.PlaylistId)
            {
                aStarted.TrySetResult();
                return await aCompletion.Task.ConfigureAwait(false);
            }
            Assert.Equal(playlistB.PlaylistId, request.PlaylistId);
            return detailB;
        };
        var widget = await StartAsync(harness);
        await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
        await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
        await widget.OnActionAsync(new(PlaylistOpen("playlist-a"),
            PlaylistFocus("wide", "playlist-a")));
        await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await widget.OnActionAsync(new("spotify.playlist.back",
            "spotify.playlist.play.shared"));
        await widget.OnActionAsync(new(PlaylistOpen("playlist-b"),
            PlaylistFocus("compact", "playlist-b")));
        var selectingB = widget.RenderSnapshot("spotify.playlist-b-loading", 1);
        Assert.True(!ContainsText(selectingB.Root, "Track A"),
            "Playlist A remained visible after selecting Playlist B.");

        if (failLateRequest)
            aCompletion.SetException(new SpotifyApplicationException(
                "spotify_unavailable", "Late A failure"));
        else
            aCompletion.SetResult(detailA);
        await WaitUntil(() => harness.PlaylistDetailRequests.Count == 2);
        await WaitForNode(widget, TrackFocus("wide", "spotify:track:b") + ".title");
        var selectedB = widget.RenderSnapshot("spotify.playlist-b", 1);
        Assert.Equal("Playlist B", Find(selectedB.Root,
            "spotify.playlist.detail.header.shared.title").Text);
        Assert.Equal("Track B", Find(selectedB.Root,
            TrackFocus("wide", "spotify:track:b") + ".title").Text);
        Assert.Equal("spotify.playlist.play.shared", selectedB.InitialFocusId);

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

        var afterLateA = widget.RenderSnapshot("spotify.playlist-b-after-a", 2);
        Assert.Equal("Playlist B", Find(afterLateA.Root,
            "spotify.playlist.detail.header.shared.title").Text);
        Assert.Equal("Track B", Find(afterLateA.Root,
            TrackFocus("wide", "spotify:track:b") + ".title").Text);
        Assert.True(!ContainsText(afterLateA.Root, "Track A"),
            "A superseded playlist result was rendered under Playlist B.");
        Assert.Equal(2, harness.PlaylistDetailRequests.Count);
        await StopAsync(widget);
    }
}

static async Task PlaylistDetailLifecycle()
{
    var playlist = Playlist("playlist-a", "Playlist A");
    var stale = PlaylistItems("Stale Track", "spotify:track:stale");
    var fresh = PlaylistItems("Fresh Track", "spotify:track:fresh");
    var firstStarted = NewSignal();
    var firstCancelled = NewSignal();
    var firstCompletion = new TaskCompletionSource<SpotifyPlaylistItemsPageSummary>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var harness = SpotifyHarness.Ready();
    harness.Playlists = new([playlist], 0, 50, 1);
    harness.PlaylistDetailHandler = async (request, cancellationToken) =>
    {
        Assert.Equal(playlist.PlaylistId, request.PlaylistId);
        if (harness.PlaylistDetailRequests.Count == 1)
        {
            using var registration = cancellationToken.Register(
                () => firstCancelled.TrySetResult());
            firstStarted.TrySetResult();
            return await firstCompletion.Task.ConfigureAwait(false);
        }
        return fresh;
    };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-a"),
        PlaylistFocus("wide", "playlist-a")));
    await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

    var background = WidgetTestHost.SetLifecycleStateAsync(
        widget, WidgetLifecycleState.Background).AsTask();
    await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    firstCompletion.SetResult(stale);
    await background.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.True(!ContainsText(widget.RenderSnapshot(
            "spotify.playlist-background", 1).Root, "Stale Track"),
        "A cancellation-ignoring detail result published after deactivation.");

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await WaitUntil(() => harness.PlaylistDetailRequests.Count == 2);
    await WaitForNode(widget, TrackFocus("wide", "spotify:track:fresh") + ".title");
    var reactivated = widget.RenderSnapshot("spotify.playlist-reactivated", 1);
    Assert.Equal("Playlist A", Find(reactivated.Root,
        "spotify.playlist.detail.header.shared.title").Text);
    Assert.Equal("Fresh Track", Find(reactivated.Root,
        TrackFocus("wide", "spotify:track:fresh") + ".title").Text);
    Assert.True(!ContainsText(reactivated.Root, "Stale Track"),
        "The stale detail result survived reactivation.");
    await StopAsync(widget);
}

static async Task RefreshPreservesNewerPlaylist()
{
    var playlistA = Playlist("playlist-a", "Playlist A");
    var playlistB = Playlist("playlist-b", "Playlist B");
    var detailA = PlaylistItems("Track A", "spotify:track:a");
    var detailB = PlaylistItems("Track B", "spotify:track:b");
    var harness = SpotifyHarness.Ready();
    harness.Playlists = new([playlistA, playlistB], 0, 50, 2);
    harness.PlaylistDetailHandler = (request, _) => ValueTask.FromResult(
        request.PlaylistId == playlistA.PlaylistId ? detailA : detailB);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-a"),
        PlaylistFocus("wide", "playlist-a")));
    await WaitUntil(() => harness.PlaylistDetailRequests.Count == 1);

    var refreshStarted = NewSignal();
    var refreshCompletion = new TaskCompletionSource<SpotifyPlaybackSummary>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    harness.PlaybackHandler = async cancellationToken =>
    {
        refreshStarted.TrySetResult();
        return await refreshCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    };
    var refresh = widget.OnActionAsync(new("spotify.refresh", "spotify.refresh.wide")).AsTask();
    await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await widget.OnActionAsync(new("spotify.playlist.back",
        "spotify.playlist.play.shared"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-b"),
        PlaylistFocus("compact", "playlist-b")));
    await WaitUntil(() => harness.PlaylistDetailRequests.Count == 2);
    await WaitForNode(widget, TrackFocus("wide", "spotify:track:b") + ".title");

    harness.PlaybackHandler = null;
    refreshCompletion.SetResult(harness.Playback);
    await refresh.WaitAsync(TimeSpan.FromSeconds(1));
    var current = widget.RenderSnapshot("spotify.refresh-newer-playlist", 1);
    Assert.Equal("Playlist B", Find(current.Root,
        "spotify.playlist.detail.header.shared.title").Text);
    Assert.Equal("Track B", Find(current.Root,
        TrackFocus("wide", "spotify:track:b") + ".title").Text);
    Assert.Equal("spotify.playlist.play.shared", current.InitialFocusId);
    Assert.True(!ContainsText(current.Root, "Track A"),
        "The refresh restored an older playlist-detail revision.");
    await StopAsync(widget);
}

static TaskCompletionSource NewSignal() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

static SpotifyPlaylistSummary Playlist(string id, string name) => new(
    id, name, $"{name} description", null,
    $"https://open.spotify.com/playlist/{id}", $"spotify:playlist:{id}",
    "Listener", false, true, 1);

static SpotifyPlaylistItemsPageSummary PlaylistItems(
    string trackName,
    string uri) => new(
    [new SpotifyMediaItemSummary(SpotifyPlaybackItemType.Track,
        trackName, "Artist", 180_000, null, uri,
        $"https://open.spotify.com/track/{trackName.Replace(' ', '-').ToLowerInvariant()}",
        true)],
    0, 50, 1);

static async Task PollingRequestBudget()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var configurationCalls = harness.ConfigurationCalls;
    var playbackCalls = harness.PlaybackCalls;
    var retainedTitle = widget.Playback?.Item?.Title;
    harness.PlaybackHandler = _ =>
        ValueTask.FromException<SpotifyPlaybackSummary>(
            new SpotifyApplicationException(
                "spotify_unavailable", "provider internals must not render"));
    await Task.Delay(TimeSpan.FromMilliseconds(2_250));
    Assert.Equal(playbackCalls, harness.PlaybackCalls);
    await Task.Delay(TimeSpan.FromMilliseconds(3_000));
    Assert.Equal(configurationCalls, harness.ConfigurationCalls);
    Assert.True(harness.PlaybackCalls > playbackCalls,
        "Adaptive playback polling did not refresh live state.");
    await WaitUntil(() => widget.Status.Contains("updates unavailable", StringComparison.Ordinal));
    Assert.Equal(SpotifyWidgetViewState.Ready, widget.ViewState);
    Assert.Equal(retainedTitle, widget.Playback?.Item?.Title);
    var warning = widget.RenderSnapshot("spotify.poll-warning", 1);
    Assert.NotNull(Find(warning.Root, "spotify.refresh-warning"));
    Assert.True(ContainsTextFragment(warning.Root, "Automatic retry in up to 5 seconds"),
        "The first automatic failure did not enter the bounded backoff policy.");
    Assert.True(!ContainsTextFragment(warning.Root, "provider internals"),
        "A provider exception message reached the widget surface.");

    harness.PlaybackHandler = null;
    harness.Playback = harness.Playback with
    {
        Item = harness.Playback.Item! with { Title = "Recovered automatically" },
    };
    await widget.OnActionAsync(new WidgetActionEvent("spotify.refresh", "spotify.refresh.wide"));
    Assert.Equal("Recovered automatically", widget.Playback?.Item?.Title);
    Assert.True(!ContainsId(widget.RenderSnapshot("spotify.poll-recovered", 2).Root,
            "spotify.refresh-warning"),
        "Manual recovery did not clear the warning created by automatic polling.");
    await StopAsync(widget);
}

static async Task TransientRefreshRetainsLastGood()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await WaitForNode(widget, PlaylistFocus("wide", "playlist-one"));
    var before = widget.RenderSnapshot("spotify.transient-before", 1);
    var initialFocus = before.InitialFocusId;
    var retainedTitle = widget.Playback?.Item?.Title;

    var failures = new (Exception Exception, string Code, int DelaySeconds)[]
    {
        (new SpotifyApplicationException("spotify_unavailable", "secret provider address"),
            "spotify_refresh_provider_unavailable", 5),
        (new SpotifyApplicationException("malformed_response", "raw response body"),
            "spotify_refresh_invalid_response", 15),
        (new InvalidOperationException("unexpected private diagnostic"),
            "spotify_refresh_failed", 30),
        (new SpotifyApplicationException("provider_busy", "internal retry metadata"),
            "spotify_refresh_failed", 30),
    };

    foreach (var failure in failures)
    {
        harness.PlaybackHandler = _ =>
            ValueTask.FromException<SpotifyPlaybackSummary>(failure.Exception);
        await widget.OnActionAsync(new WidgetActionEvent(
            "spotify.refresh", "spotify.refresh.wide"));
        var snapshot = widget.RenderSnapshot("spotify.transient", failure.DelaySeconds);
        Assert.Equal(SpotifyWidgetViewState.Ready, widget.ViewState);
        Assert.Equal(SpotifyDestination.Playlists, widget.Destination);
        Assert.Equal(retainedTitle, widget.Playback?.Item?.Title);
        Assert.Equal(initialFocus, snapshot.InitialFocusId);
        Assert.NotNull(Find(snapshot.Root, PlaylistFocus("wide", "playlist-one")));
        Assert.True(ContainsTextFragment(snapshot.Root, failure.Code),
            $"Safe diagnostic '{failure.Code}' was not rendered.");
        Assert.True(ContainsTextFragment(snapshot.Root,
                $"Automatic retry in up to {failure.DelaySeconds} seconds"),
            "Transient refresh backoff did not remain bounded.");
        Assert.True(!ContainsTextFragment(snapshot.Root, failure.Exception.Message),
            "A transient exception message reached the retained surface.");
    }

    harness.PlaybackHandler = null;
    harness.Playback = harness.Playback with
    {
        Item = harness.Playback.Item! with { Title = "Recovered track" },
    };
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.refresh", "spotify.refresh.wide"));
    var recovered = widget.RenderSnapshot("spotify.transient-recovered", 5);
    Assert.Equal("Recovered track", widget.Playback?.Item?.Title);
    Assert.Equal(SpotifyDestination.Playlists, widget.Destination);
    Assert.Equal(initialFocus, recovered.InitialFocusId);
    Assert.NotNull(Find(recovered.Root, PlaylistFocus("wide", "playlist-one")));
    Assert.True(!ContainsId(recovered.Root, "spotify.refresh-warning"),
        "Successful recovery retained a stale warning.");
    await StopAsync(widget);
}

static async Task FatalRefreshFailuresSelectSafeState()
{
    var failures = new (string Code, SpotifyWidgetViewState State, string ExpectedText)[]
    {
        ("forbidden", SpotifyWidgetViewState.PermissionDenied,
            "Spotify permission is off"),
        ("authorization_expired", SpotifyWidgetViewState.Disconnected,
            "Connect Spotify"),
        ("invalid_configuration", SpotifyWidgetViewState.Error,
            "Spotify could not be loaded"),
    };

    foreach (var failure in failures)
    {
        var harness = SpotifyHarness.Ready();
        var widget = await StartAsync(harness);
        await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
        harness.ConfigurationError = new SpotifyApplicationException(
            failure.Code, $"private diagnostic for {failure.Code}");
        await widget.OnActionAsync(new WidgetActionEvent(
            "spotify.refresh", "spotify.refresh.wide"));
        var snapshot = widget.RenderSnapshot("spotify.fatal", 1);
        Assert.Equal(failure.State, widget.ViewState);
        Assert.True(ContainsTextFragment(snapshot.Root, failure.ExpectedText),
            $"Fatal code '{failure.Code}' did not select its exact safe state.");
        Assert.True(!ContainsTextFragment(snapshot.Root, "private diagnostic"),
            "A fatal provider exception message reached the widget surface.");
        Assert.Equal<SpotifyPlaybackSummary?>(null, widget.Playback);
        await StopAsync(widget);
    }
}

static async Task RefreshPollingLifecycle()
{
    var delayed = new TaskCompletionSource<SpotifyPlaybackSummary>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var canceled = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var harness = SpotifyHarness.Ready();
    var baseline = harness.Playback;
    var recovered = baseline with
    {
        Item = baseline.Item! with { Title = "Fresh lifecycle result" },
    };
    var calls = 0;
    harness.PlaybackHandler = async cancellationToken =>
    {
        var call = Interlocked.Increment(ref calls);
        if (call == 1) return baseline;
        if (call == 2)
        {
            using var registration = cancellationToken.Register(() => canceled.TrySetResult());
            return await delayed.Task.ConfigureAwait(false);
        }
        return recovered;
    };

    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await Task.Delay(TimeSpan.FromMilliseconds(5_100));
    await WaitUntil(() => Volatile.Read(ref calls) == 2);
    var background = WidgetTestHost.SetLifecycleStateAsync(
        widget, WidgetLifecycleState.Background).AsTask();
    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    delayed.SetResult(recovered);
    await background.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.Equal("Small Hours", widget.Playback?.Item?.Title);
    Assert.True(!ContainsId(widget.RenderSnapshot("spotify.lifecycle-stale", 1).Root,
            "spotify.refresh-warning"),
        "A cancellation-ignoring stale poll published after deactivation.");

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await WaitUntil(() => string.Equals(widget.Playback?.Item?.Title,
        "Fresh lifecycle result", StringComparison.Ordinal));
    Assert.True(Volatile.Read(ref calls) >= 3,
        "Reactivation did not start a fresh generation-bound refresh.");
    await StopAsync(widget);
}

static async Task MaximumPlaylistPageContract()
{
    var playlists = Enumerable.Range(0, SpotifyApplicationContract.MaximumCollectionPageSize)
        .Select(index => new SpotifyPlaylistSummary(
            $"playlist-{index}", $"Playlist {index}", $"Description {index}",
            $"https://i.scdn.co/image/playlist-{index}",
            $"https://open.spotify.com/playlist/playlist-{index}",
            $"spotify:playlist:playlist-{index}", "Listener", false, true,
            SpotifyApplicationContract.MaximumCollectionPageSize))
        .ToArray();
    var tracks = Enumerable.Range(0, SpotifyApplicationContract.MaximumCollectionPageSize)
        .Select(index => new SpotifyMediaItemSummary(
            SpotifyPlaybackItemType.Track, $"Track {index}", $"Artist {index}",
            180_000, $"https://i.scdn.co/image/track-{index}",
            $"spotify:track:track-{index}",
            $"https://open.spotify.com/track/track-{index}", true))
        .ToArray();
    var harness = SpotifyHarness.Ready();
    harness.Playlists = new(playlists, 0, playlists.Length, playlists.Length);
    harness.PlaylistDetail = new(tracks, 0, tracks.Length, tracks.Length);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);

    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await WaitUntil(() => harness.PlaylistCalls == 1);
    var firstPage = widget.RenderSnapshot("spotify.maximum-playlists", 1);
    Assert.True(firstPage.ProtocolVersion >= ProtocolConstants.VirtualCollectionWindowVersion,
        "Playlist paging did not negotiate virtual-collection support.");
    var playlistScroll = Find(firstPage.Root, "spotify.playlists.scroll");
    Assert.NotNull(playlistScroll.VirtualCollectionWindow);
    Assert.True(CollectionRows(playlistScroll).Length is >= 1 and <= 12,
        "Playlist presentation exceeded its current bounded window.");
    var firstWindowCount = CollectionRows(playlistScroll).Length;
    var firstWindow = playlistScroll.VirtualCollectionWindow!;
    var stableEntry = firstPage.InitialFocusId;
    var stableAnchor = playlistScroll.CollectionAnchorKey;
    Assert.Equal("spotify.playlists.cursor.after", playlistScroll.ScrollNearEndActionId);
    Assert.True(playlistScroll.ScrollNearStartActionId is null,
        "The first page must not request a previous page.");
    Assert.True(SnapshotJson.Serialize(firstPage).Length < 400_000,
        "A bounded playlist snapshot must remain comfortably below the bridge limit.");

    await widget.OnActionAsync(new(
        "spotify.playlists.cursor.after", "spotify.playlists.scroll"));
    await WaitUntil(() => harness.PlaylistCalls == 2);
    var secondPage = widget.RenderSnapshot("spotify.second-playlists", 2);
    var secondScroll = Find(secondPage.Root, "spotify.playlists.scroll");
    var secondWindow = secondScroll.VirtualCollectionWindow!;
    var retainedCount = CollectionRows(secondScroll).Length;
    Assert.True(retainedCount > firstWindowCount && retainedCount <= 24,
        "Adjacent playlist loading did not extend the bounded retained window.");
    Assert.NotNull(Find(secondPage.Root, PlaylistFocus("wide", "playlist-12")));
    Assert.Equal(stableEntry, secondPage.InitialFocusId);
    Assert.Equal(stableAnchor, secondScroll.CollectionAnchorKey);
    Assert.True(secondWindow.RequestGeneration > firstWindow.RequestGeneration,
        "Adjacent playlist loading did not publish a successor window generation.");
    Assert.Equal(VirtualCollectionWindowChange.Append, secondWindow.Change);
    Assert.Equal(2, harness.PlaylistCalls);
    var cachedFirstPage = widget.RenderSnapshot("spotify.cached-playlists", 3);
    Assert.NotNull(Find(cachedFirstPage.Root, PlaylistFocus("wide", "playlist-0")));

    await widget.OnActionAsync(new(PlaylistOpen("playlist-0"),
        PlaylistFocus("wide", "playlist-0")));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);
    var detailPage = widget.RenderSnapshot("spotify.maximum-playlist-detail", 4);
    var trackScroll = Find(detailPage.Root, "spotify.playlist.detail.scroll");
    Assert.True(CollectionRows(trackScroll).Length is >= 1 and <= 12,
        "Playlist-detail presentation exceeded its current bounded window.");
    Assert.Equal("spotify.playlist.items.cursor.after", trackScroll.ScrollNearEndActionId);
    Assert.True(SnapshotJson.Serialize(detailPage).Length < 400_000,
        "A bounded playlist-detail snapshot must remain comfortably below the bridge limit.");
    await widget.OnActionAsync(new(PlaylistTrack("spotify:track:track-0"),
        TrackFocus("wide", "spotify:track:track-0")));
    Assert.Equal(1, harness.StartedPlayback.Count);
    Assert.Equal("spotify:track:track-0", harness.StartedPlayback[0].OffsetUri);
    await StopAsync(widget);
}

static async Task ControllerPlaylistPrefetchRoundTrip()
{
    foreach (var mode in new[] { "shared" })
    {
        var playlists = Enumerable.Range(0, 29)
            .Select(index => new SpotifyPlaylistSummary(
                $"playlist-{index}", $"Playlist {index}", null, null,
                $"https://open.spotify.com/playlist/playlist-{index}",
                $"spotify:playlist:playlist-{index}", "Listener", false, true, 1))
            .ToArray();
        var tracks = Enumerable.Range(0, 29)
            .Select(index => new SpotifyMediaItemSummary(
                SpotifyPlaybackItemType.Track, $"Track {index}", $"Artist {index}",
                180_000, null, $"spotify:track:track-{index}",
                $"https://open.spotify.com/track/track-{index}", true))
            .ToArray();
        var harness = SpotifyHarness.Ready();
        harness.Playlists = new(playlists, 0, 12, playlists.Length);
        harness.PlaylistDetail = new(tracks, 0, 12, tracks.Length);
        var widget = await StartAsync(harness);
        await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
        await widget.OnActionAsync(new(
            "spotify.nav.playlists", $"spotify.nav.{mode}.playlists"));
        await WaitUntil(() => harness.PlaylistCalls == 1);

        var focus = PlaylistFocus(mode, "playlist-0");
        long sequence = 1;
        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await NavigatePlaylistDirectionAsync(
                widget, mode, focus, sequence, down: true);
        Assert.Equal(PlaylistFocus(mode, "playlist-11"), focus);
        bool paged;
        harness.PlaylistCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.IgnorePlaylistCancellation = true;
        var slowPage = NavigatePlaylistDirectionAsync(
            widget, mode, focus, sequence, down: true);
        await WaitUntil(() => harness.PlaylistCalls == 2);
        var repeatedPage = NavigatePlaylistDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.Equal(2, harness.PlaylistCalls);
        harness.PlaylistCompletion.SetResult(harness.Playlists);
        harness.PlaylistCompletion = null;
        (focus, sequence, paged) = await slowPage;
        await repeatedPage;
        _ = widget.RenderSnapshot("spotify.repeated-page", sequence + 1);
        Assert.Equal(2, harness.PlaylistCalls);
        Assert.True(paged, $"{mode} controller Down did not enter page two.");
        Assert.Equal(PlaylistFocus(mode, "playlist-12"), focus);

        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await NavigatePlaylistDirectionAsync(
                widget, mode, focus, sequence, down: true);
        (focus, sequence, paged) = await NavigatePlaylistDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.True(paged, $"{mode} controller Down did not enter the five-row final page.");
        Assert.Equal(PlaylistFocus(mode, "playlist-24"), focus);
        for (var index = 0; index < 4; index++)
            (focus, sequence, _) = await NavigatePlaylistDirectionAsync(
                widget, mode, focus, sequence, down: true);
        Assert.Equal(PlaylistFocus(mode, "playlist-28"), focus);
        var terminalPlaylists = widget.RenderSnapshot(
            "spotify.playlists.terminal", sequence);
        var terminalPlaylistScroll = Find(
            terminalPlaylists.Root, "spotify.playlists.scroll");
        Assert.NotNull(Find(terminalPlaylistScroll, focus));
        Assert.True(terminalPlaylistScroll.VirtualCollectionWindow?.HasAfter == false,
            "The terminal playlist window still advertised a following page.");
        Assert.Equal<string?>(null, terminalPlaylistScroll.ScrollNearEndActionId);
        var finalFocus = focus;
        var terminalPlaylistCalls = harness.PlaylistCalls;
        (finalFocus, sequence, paged) = await NavigatePlaylistDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.True(!paged && finalFocus == focus,
            $"{mode} final page exposed a nonexistent forward transition.");
        Assert.Equal(terminalPlaylistCalls, harness.PlaylistCalls);

        for (var index = 0; index < 4; index++)
            (focus, sequence, _) = await NavigatePlaylistDirectionAsync(
                widget, mode, focus, sequence, down: false);
        Assert.Equal(PlaylistFocus(mode, "playlist-24"), focus);
        (focus, sequence, paged) = await NavigatePlaylistDirectionAsync(
            widget, mode, focus, sequence, down: false);
        Assert.True(!paged, $"{mode} controller Up replaced a retained adjacent row.");
        Assert.Equal(PlaylistFocus(mode, "playlist-23"), focus);
        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await NavigatePlaylistDirectionAsync(
                widget, mode, focus, sequence, down: false);
        (focus, sequence, paged) = await NavigatePlaylistDirectionAsync(
            widget, mode, focus, sequence, down: false);
        Assert.True(paged, $"{mode} controller Up did not restore cached page one.");
        Assert.Equal(PlaylistFocus(mode, "playlist-11"), focus);
        Assert.Equal(4, harness.PlaylistCalls);

        await widget.OnActionAsync(new(
            PlaylistOpen("playlist-0"), PlaylistFocus(mode, "playlist-0")));
        await WaitUntil(() => harness.PlaylistDetailCalls == 1);
        focus = TrackFocus(mode, "spotify:track:track-0");
        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await NavigatePlaylistItemDirectionAsync(
                widget, mode, focus, sequence, down: true);
        (focus, sequence, paged) = await NavigatePlaylistItemDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.True(paged && focus == TrackFocus(mode, "spotify:track:track-12"),
            $"{mode} playlist-detail Down did not enter page two.");
        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await NavigatePlaylistItemDirectionAsync(
                widget, mode, focus, sequence, down: true);
        (focus, sequence, paged) = await NavigatePlaylistItemDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.True(paged && focus == TrackFocus(mode, "spotify:track:track-24"),
            $"{mode} playlist-detail Down did not enter the final page.");
        for (var index = 0; index < 4; index++)
            (focus, sequence, _) = await NavigatePlaylistItemDirectionAsync(
                widget, mode, focus, sequence, down: true);
        Assert.Equal(TrackFocus(mode, "spotify:track:track-28"), focus);
        var terminalTracks = widget.RenderSnapshot(
            "spotify.playlist-detail.terminal", sequence);
        var terminalTrackScroll = Find(
            terminalTracks.Root, "spotify.playlist.detail.scroll");
        Assert.NotNull(Find(terminalTrackScroll, focus));
        Assert.True(terminalTrackScroll.VirtualCollectionWindow?.HasAfter == false,
            "The terminal playlist-detail window still advertised a following page.");
        Assert.Equal<string?>(null, terminalTrackScroll.ScrollNearEndActionId);
        var terminalTrackCalls = harness.PlaylistDetailCalls;
        var terminalTrackFocus = focus;
        (terminalTrackFocus, sequence, paged) = await NavigatePlaylistItemDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.True(!paged && terminalTrackFocus == focus,
            $"{mode} final playlist-detail page exposed a nonexistent transition.");
        Assert.Equal(terminalTrackCalls, harness.PlaylistDetailCalls);
        for (var index = 0; index < 4; index++)
            (focus, sequence, _) = await NavigatePlaylistItemDirectionAsync(
                widget, mode, focus, sequence, down: false);
        (focus, sequence, paged) = await NavigatePlaylistItemDirectionAsync(
            widget, mode, focus, sequence, down: false);
        Assert.True(!paged && focus == TrackFocus(mode, "spotify:track:track-23"),
            $"{mode} playlist-detail Up replaced a retained adjacent row.");
        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await NavigatePlaylistItemDirectionAsync(
                widget, mode, focus, sequence, down: false);
        (focus, sequence, paged) = await NavigatePlaylistItemDirectionAsync(
            widget, mode, focus, sequence, down: false);
        Assert.True(paged && focus == TrackFocus(mode, "spotify:track:track-11"),
            $"{mode} playlist-detail Up did not cross the evicted boundary.");
        Assert.Equal(4, harness.PlaylistDetailCalls);
        await StopAsync(widget);
    }
}

static Task<(string Focus, long Sequence, bool Paginated)>
    NavigatePlaylistDirectionAsync(
        SpotifyWidget widget,
        string mode,
        string focus,
        long sequence,
        bool down) => NavigatePagedDirectionAsync(
            widget,
            "spotify.playlists.scroll",
            focus,
            sequence,
            down);

static Task<(string Focus, long Sequence, bool Paginated)>
    NavigatePlaylistItemDirectionAsync(
        SpotifyWidget widget,
        string mode,
        string focus,
        long sequence,
        bool down) => NavigatePagedDirectionAsync(
            widget,
            "spotify.playlist.detail.scroll",
            focus,
            sequence,
            down);

static async Task ContinuousPlaylistDetailAnchorAndHeader()
{
    var playlists = Enumerable.Range(0, 29)
        .Select(index => Playlist($"playlist-{index}", $"Playlist {index}"))
        .ToArray();
    var tracks = Enumerable.Range(0, 29)
        .Select(index => new SpotifyMediaItemSummary(
            SpotifyPlaybackItemType.Track, $"Track {index}", $"Artist {index}",
            180_000, null, $"spotify:track:track-{index}",
            $"https://open.spotify.com/track/track-{index}", true))
        .ToArray();
    var harness = SpotifyHarness.Ready();
    harness.Playlists = new(playlists, 0, 12, playlists.Length);
    harness.PlaylistDetail = new(tracks, 0, 12, tracks.Length);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-0"),
        PlaylistFocus("wide", "playlist-0")));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);

    await widget.OnActionAsync(new("spotify.playlist.items.cursor.after",
        "spotify.playlist.detail.scroll"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 2);
    await widget.OnActionAsync(new("spotify.playlist.items.cursor.after",
        "spotify.playlist.detail.scroll"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 3);
    await widget.OnActionAsync(new("spotify.playlist.items.cursor.before",
        "spotify.playlist.detail.scroll"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 4);

    var restored = widget.RenderSnapshot("spotify.header-edge", 1);
    var firstId = TrackFocus("wide", "spotify:track:track-0");
    var secondId = TrackFocus("wide", "spotify:track:track-1");
    Assert.Equal("spotify.playlist.play.shared", Find(restored.Root, firstId).Focus?.Up);
    Assert.Equal(firstId, Find(restored.Root, "spotify.playlist.play.shared").Focus?.Down);
    var replay = ControllerReplay.Run(restored, new InputReplay
    {
        InitialFocusId = firstId,
        Events =
        [
            new ReplayInputEvent { Button = ControllerButton.DPadUp },
            new ReplayInputEvent { Button = ControllerButton.DPadDown },
            new ReplayInputEvent { Button = ControllerButton.DPadDown },
        ],
    });
    Assert.SequenceEqual(new[] { "spotify.playlist.play.shared", firstId, secondId },
        replay.Select(step => step.FocusAfter).ToArray());

    var retainedUri = "spotify:track:track-5";
    await widget.OnActionAsync(new(PlaylistTrack(retainedUri),
        TrackFocus("wide", retainedUri)));
    harness.PlaylistDetail = new(tracks.Skip(1).ToArray(), 0, 12,
        tracks.Length - 1);
    await widget.OnActionAsync(new("spotify.refresh", "spotify.refresh.wide"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 5);
    var refreshed = widget.RenderSnapshot("spotify.anchor-refresh", 2);
    var scroll = Find(refreshed.Root, "spotify.playlist.detail.scroll");
    Assert.Equal("media." + CollectionToken("spotify:track:track-1"), scroll.CollectionAnchorKey);
    Assert.NotNull(Find(refreshed.Root, TrackFocus("wide", retainedUri)));
    await StopAsync(widget);
}

static async Task DuplicateQueueOccurrencesRouteExactly()
{
    const string repeatedUri = "spotify:track:repeated-queue";
    var repeated = new SpotifyMediaItemSummary(
        SpotifyPlaybackItemType.Track, "Repeated", "Same artist",
        180_000, null, repeatedUri,
        "https://open.spotify.com/track/repeated-queue", true);
    var harness = SpotifyHarness.Ready();
    harness.Queue = new(harness.Queue.CurrentlyPlaying,
    [
        repeated,
        new SpotifyMediaItemSummary(
            SpotifyPlaybackItemType.Track, "Middle", "Other artist",
            181_000, null, "spotify:track:middle-queue",
            "https://open.spotify.com/track/middle-queue", true),
        repeated,
    ], false);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.wide.queue"));
    await WaitUntil(() => harness.QueueCalls >= 1);

    var snapshot = widget.RenderSnapshot("spotify.queue.duplicates", 1);
    var rows = Find(snapshot.Root, "spotify.queue.scroll").Children.ToArray();
    Assert.Equal(3, rows.Length);
    Assert.Equal(3, rows.Select(row => row.CollectionItemKey).Distinct().Count());
    Assert.Equal(3, rows.Select(row => row.Id).Distinct().Count());
    Assert.Equal(3, rows.Select(row => row.ActionId).Distinct().Count());
    var selected = rows[2];
    await widget.OnActionAsync(new(selected.ActionId!, selected.Id));
    Assert.Equal(repeatedUri, harness.StartedPlayback.Single().ItemUris!.Single());
    Assert.Equal(rows[0].CollectionItemKey,
        Find(widget.RenderSnapshot("spotify.queue.selected-duplicate", 2).Root,
            "spotify.queue.scroll").CollectionAnchorKey);
    await StopAsync(widget);
}

static async Task DuplicatePlaylistOccurrencesStayKeyed()
{
    const string repeatedUri = "spotify:track:repeated-playlist";
    var repeated = new SpotifyMediaItemSummary(
        SpotifyPlaybackItemType.Track, "Repeated", "Same artist",
        180_000, null, repeatedUri,
        "https://open.spotify.com/track/repeated-playlist", true);
    var tracks = Enumerable.Range(0, 29)
        .Select(index => new SpotifyMediaItemSummary(
            SpotifyPlaybackItemType.Track, $"Track {index}", $"Artist {index}",
            180_000 + index, null, $"spotify:track:occurrence-{index}",
            $"https://open.spotify.com/track/occurrence-{index}", true))
        .ToArray();
    tracks[1] = repeated;
    tracks[3] = repeated;
    tracks[13] = repeated;
    var harness = SpotifyHarness.Ready();
    harness.PlaylistDetail = new(tracks, 0, 12, tracks.Length);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"),
        PlaylistFocus("wide", "playlist-one")));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);

    var initial = PlaylistDetailRows(widget, 1);
    var samePageKeys = new[]
    {
        initial[1].CollectionItemKey!,
        initial[3].CollectionItemKey!,
    };
    Assert.Equal(2, samePageKeys.Distinct().Count());
    await widget.OnActionAsync(new(
        "spotify.playlist.items.cursor.after",
        "spotify.playlist.detail.scroll"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 2);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.second-page-wait", 2).Root,
        TrackFocus("wide", "spotify:track:occurrence-12")), "second page did not commit");
    var firstWindow = PlaylistDetailRows(widget, 2);
    var originalKeys = firstWindow
        .Where(row => row.ActionId?.Contains(
            CollectionToken(repeatedUri), StringComparison.Ordinal) == true)
        .Select(row => row.CollectionItemKey!)
        .ToArray();
    Assert.Equal(3, originalKeys.Length);
    Assert.Equal(3, originalKeys.Distinct().Count());

    await widget.OnActionAsync(new(
        "spotify.playlist.items.cursor.after",
        "spotify.playlist.detail.scroll"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 3);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.third-page-wait", 3).Root,
        TrackFocus("wide", "spotify:track:occurrence-24")), "third page did not commit");
    await widget.OnActionAsync(new(
        "spotify.playlist.items.cursor.before",
        "spotify.playlist.detail.scroll"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 4);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.reverse-wait", 3).Root,
        TrackFocus("wide", "spotify:track:occurrence-0")), "reverse page did not commit");
    Assert.Equal(
        string.Join(',', originalKeys.Order(StringComparer.Ordinal)),
        string.Join(',', PlaylistDuplicateKeys(widget, 3, repeatedUri)));
    var refreshAnchor = PlaylistDetailRows(widget, 3)
        .Single(row => row.CollectionItemKey == originalKeys[0]);
    await widget.OnActionAsync(new(refreshAnchor.ActionId!, refreshAnchor.Id));
    harness.StartedPlayback.Clear();

    var inserted = tracks.Prepend(new SpotifyMediaItemSummary(
        SpotifyPlaybackItemType.Track, "Inserted", "New artist", 179_000,
        null, "spotify:track:inserted-before-duplicates",
        "https://open.spotify.com/track/inserted-before-duplicates", true)).ToArray();
    harness.PlaylistDetail = new(inserted, 0, 12, inserted.Length);
    await widget.OnActionAsync(new("spotify.refresh", "spotify.refresh.wide"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 5);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.insert-refresh-wait", 4).Root,
        TrackFocus("wide", "spotify:track:inserted-before-duplicates")),
        "insert refresh did not commit");
    await widget.OnActionAsync(new(
        "spotify.playlist.items.cursor.after",
        "spotify.playlist.detail.scroll"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 6);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.insert-second-page-wait", 4).Root,
        TrackFocus("wide", "spotify:track:occurrence-12")),
        "inserted second page did not commit");
    Assert.Equal(
        string.Join(',', originalKeys.Order(StringComparer.Ordinal)),
        string.Join(',', PlaylistDuplicateKeys(widget, 4, repeatedUri)));

    harness.PlaylistDetail = new(inserted[..^1], 0, 12, inserted.Length - 1);
    await widget.OnActionAsync(new("spotify.refresh", "spotify.refresh.wide"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 7);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.delete-refresh-wait", 5).Root,
        TrackFocus("wide", "spotify:track:inserted-before-duplicates")),
        "delete refresh did not commit");
    await widget.OnActionAsync(new(
        "spotify.playlist.items.cursor.after",
        "spotify.playlist.detail.scroll"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 8);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.delete-second-page-wait", 5).Root,
        TrackFocus("wide", "spotify:track:occurrence-12")),
        "deleted second page did not commit");
    var finalRows = PlaylistDetailRows(widget, 5);
    Assert.Equal(
        string.Join(',', originalKeys.Order(StringComparer.Ordinal)),
        string.Join(',', finalRows.Where(row => row.ActionId?.Contains(
                CollectionToken(repeatedUri), StringComparison.Ordinal) == true)
            .Select(row => row.CollectionItemKey!)
            .Order(StringComparer.Ordinal)
            .ToArray()));
    var selected = finalRows.Single(row => row.CollectionItemKey == originalKeys[2]);
    await widget.OnActionAsync(new(selected.ActionId!, selected.Id));
    Assert.Equal(repeatedUri, harness.StartedPlayback.Single().OffsetUri);
    Assert.Equal(selected.CollectionItemKey,
        Find(widget.RenderSnapshot("spotify.playlist.duplicate-selected", 6).Root,
            "spotify.playlist.detail.scroll").CollectionAnchorKey);
    await StopAsync(widget);
}

static async Task SingleTrackPlaylistHasNoSelfEdge()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"),
        PlaylistFocus("wide", "playlist-one")));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);
    var snapshot = widget.RenderSnapshot("spotify.playlist.singleton", 1);
    var row = Find(snapshot.Root, TrackFocus("wide", "spotify:track:next"));
    var play = Find(snapshot.Root, "spotify.playlist.play.shared");
    Assert.Equal(row.Id, play.Focus?.Down);
    Assert.Equal(play.Id, row.Focus?.Up);
    Assert.Equal<string?>(null, row.Focus?.Down);
    await StopAsync(widget);
}

static Task OccurrenceIdentityIsBounded()
{
    const int retainedLimit = 24;
    var policy = new SpotifyMediaOccurrencePolicy(retainedLimit);
    var repeated = new SpotifyMediaItemSummary(
        SpotifyPlaybackItemType.Track, "Repeated", "Same artist",
        180_000, null, "spotify:track:bounded-occurrence",
        "https://open.spotify.com/track/bounded-occurrence", true);
    var retained = new List<WidgetCollectionItemKey>();
    for (var page = 0; page < 100; page++)
    {
        var request = policy.BeginPage(
            "bounded-playlist", page * 12, WidgetCursorDirection.After);
        var normalized = policy.NormalizePage(
            request,
            Enumerable.Repeat(repeated, 12).ToArray(),
            retained);
        var keys = normalized.Select(item => item.Key).ToArray();
        Assert.Equal(12, keys.Distinct().Count());
        Assert.True(!keys.Any(retained.Contains),
            "A new retained page reused an active duplicate occurrence key.");
        retained.AddRange(keys);
        if (retained.Count > retainedLimit)
            retained.RemoveRange(0, retained.Count - retainedLimit);
        Assert.True(policy.RetainedCount <= retainedLimit,
            "Occurrence identity outgrew the collection retention window.");
    }
    var stale = policy.BeginPage("bounded-playlist", 1_200, WidgetCursorDirection.After);
    var current = policy.BeginPage("bounded-playlist", 1_212, WidgetCursorDirection.After);
    _ = policy.NormalizePage(stale, [repeated], retained);
    Assert.Equal(retainedLimit, policy.RetainedCount);
    _ = policy.NormalizePage(current, [repeated], retained);
    Assert.True(policy.RetainedCount <= retainedLimit,
        "Current completion exceeded the collection retention window.");
    policy.Reset();
    Assert.Equal(0, policy.RetainedCount);
    return Task.CompletedTask;
}

static ViewNode[] PlaylistDetailRows(SpotifyWidget widget, long sequence) =>
    CollectionRows(Find(widget.RenderSnapshot(
        "spotify.playlist.occurrences", sequence).Root,
        "spotify.playlist.detail.scroll"));

static string[] PlaylistDuplicateKeys(
    SpotifyWidget widget,
    long sequence,
    string uri) => PlaylistDetailRows(widget, sequence)
    .Where(row => row.ActionId?.Contains(CollectionToken(uri), StringComparison.Ordinal) == true)
    .Select(row => row.CollectionItemKey!)
    .Order(StringComparer.Ordinal)
    .ToArray();

static async Task<(string Focus, long Sequence, bool Paginated)>
    NavigatePagedDirectionAsync(
        SpotifyWidget widget,
        string scrollId,
        string focus,
        long sequence,
        bool down)
{
    var snapshot = widget.RenderSnapshot("spotify.controller-page", sequence);
    var scroll = Find(snapshot.Root, scrollId);
    var visibleIds = CollectionRows(scroll).Select(child => child.Id).ToArray();
    var visibleIndex = Array.IndexOf(visibleIds, focus);
    Assert.True(visibleIndex >= 0,
        $"Focused row {focus} is not in the visible page for {scrollId}.");
    var adjacent = visibleIndex + (down ? 1 : -1);
    if (adjacent >= 0 && adjacent < visibleIds.Length)
        return (visibleIds[adjacent], sequence, false);

    var actionId = down ? scroll.ScrollNearEndActionId : scroll.ScrollNearStartActionId;
    if (actionId is null) return (focus, sequence, false);
    var priorWindow = scroll.VirtualCollectionWindow;
    var priorAnchor = scroll.CollectionAnchorKey;
    await widget.OnActionAsync(new(actionId, scroll.Id));
    var replacementSequence = sequence + 1;
    await WaitUntil(() =>
    {
        var candidate = widget.RenderSnapshot("spotify.controller-page", replacementSequence);
        var candidateScroll = Find(candidate.Root, scrollId);
        var candidateWindow = candidateScroll.VirtualCollectionWindow;
        if (priorWindow is not null &&
            (candidateWindow is null ||
             candidateWindow.RequestGeneration <= priorWindow.RequestGeneration))
            return false;
        var candidateIds = CollectionRows(candidateScroll).Select(child => child.Id).ToArray();
        var retainedIndex = Array.IndexOf(candidateIds, focus);
        if (retainedIndex < 0) return false;
        var nextIndex = retainedIndex + (down ? 1 : -1);
        return nextIndex >= 0 && nextIndex < candidateIds.Length;
    });
    var replacement = widget.RenderSnapshot(
        "spotify.controller-page", replacementSequence);
    var replacementScroll = Find(replacement.Root, scrollId);
    var replacementWindow = replacementScroll.VirtualCollectionWindow;
    var replacementRows = CollectionRows(replacementScroll);
    var entry = replacement.InitialFocusId is { } entryId
        ? FindEnabledInScope(replacement.Root, entryId, replacement.ActiveInputScopeId)
        : null;
    Assert.True(entry is not null,
        $"Cursor prefetch published invalid scoped page entry '{replacement.InitialFocusId}'.");
    if (priorWindow is not null)
    {
        Assert.NotNull(replacementWindow);
        Assert.True(replacementWindow!.RequestGeneration > priorWindow.RequestGeneration,
            "Cursor prefetch did not publish a successor window generation.");
        Assert.Equal(down
                ? VirtualCollectionWindowChange.Append
                : VirtualCollectionWindowChange.Prepend,
            replacementWindow.Change);
    }
    Assert.NotNull(replacementRows.SingleOrDefault(child => child.Id == focus));
    if (priorAnchor is not null && replacementRows.Any(
            child => child.CollectionItemKey == priorAnchor))
        Assert.Equal(priorAnchor, replacementScroll.CollectionAnchorKey);
    else
        Assert.True(replacementScroll.CollectionAnchorKey is { } fallback &&
                replacementRows.Any(child => child.CollectionItemKey == fallback),
            "Cursor trimming did not publish a retained collection-anchor fallback.");
    var retainedFocusIndex = Array.FindIndex(replacementRows, child => child.Id == focus);
    var target = replacementRows[retainedFocusIndex + (down ? 1 : -1)].Id;
    return (target, replacementSequence, true);
}

static async Task<bool> DispatchPagedEdgeAsync(
    SpotifyWidget widget,
    string scrollId,
    string focus,
    long sequence,
    bool down)
{
    var snapshot = widget.RenderSnapshot("spotify.controller-edge", sequence);
    var scroll = Find(snapshot.Root, scrollId);
    var visibleIds = CollectionRows(scroll).Select(child => child.Id).ToArray();
    var visibleIndex = Array.IndexOf(visibleIds, focus);
    if (visibleIndex < 0 ||
        (down && visibleIndex != visibleIds.Length - 1) ||
        (!down && visibleIndex != 0))
        return false;
    var actionId = down ? scroll.ScrollNearEndActionId : scroll.ScrollNearStartActionId;
    if (actionId is null) return false;
    await widget.OnActionAsync(new(actionId, scroll.Id));
    return true;
}

static async Task PagedPlaylistBackRestoresOpenedItem()
{
    var playlists = Enumerable.Range(0, 24)
        .Select(index => new SpotifyPlaylistSummary(
            $"playlist-{index}", $"Playlist {index}", null, null,
            $"https://open.spotify.com/playlist/playlist-{index}",
            $"spotify:playlist:playlist-{index}", "Listener", false, true, 1))
        .ToArray();
    var harness = SpotifyHarness.Ready();
    harness.Playlists = new(playlists, 0, 12, playlists.Length);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await WaitUntil(() => harness.PlaylistCalls == 1);
    await widget.OnActionAsync(new(
        "spotify.playlists.cursor.after", "spotify.playlists.scroll"));
    await WaitUntil(() => harness.PlaylistCalls == 2);

    await widget.OnActionAsync(new(
        PlaylistOpen("playlist-15"), PlaylistFocus("wide", "playlist-15")));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);
    await widget.OnActionAsync(new(
        "spotify.playlist.back", "spotify.playlist.play.shared"));
    var restored = widget.RenderSnapshot("spotify.playlist.return-page", 1);
    Assert.Equal(PlaylistFocus("wide", "playlist-15"), restored.InitialFocusId);
    Assert.NotNull(Find(restored.Root, restored.InitialFocusId!));
    await StopAsync(widget);
}

static async Task AdjacentPlaylistFailureRetry()
{
    var tracks = Enumerable.Range(0, 24)
        .Select(index => new SpotifyMediaItemSummary(
            SpotifyPlaybackItemType.Track, $"Track {index}", $"Artist {index}",
            180_000, null, $"spotify:track:track-{index}",
            $"https://open.spotify.com/track/track-{index}", true))
        .ToArray();
    var harness = SpotifyHarness.Ready();
    harness.PlaylistDetail = new(tracks, 0, 12, tracks.Length);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"),
        PlaylistFocus("wide", "playlist-one")));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);

    harness.PlaylistDetailError = new SpotifyApplicationException(
        "spotify_unavailable", "Spotify could not load more tracks");
    Assert.True(await DispatchPagedEdgeAsync(
        widget,
        "spotify.playlist.detail.scroll",
        TrackFocus("wide", "spotify:track:track-11"),
        sequence: 2,
        down: true),
        "Controller Down did not admit the failing adjacent detail page.");
    await WaitUntil(() => harness.PlaylistDetailCalls == 2);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.retained-wait", 2).Root,
        "spotify.page.retained-error.shared.action"));
    var retained = widget.RenderSnapshot("spotify.playlist.retained", 3);
    Assert.NotNull(Find(retained.Root, TrackFocus("wide", "spotify:track:track-0")));
    Assert.NotNull(Find(retained.Root, "spotify.page.retained-error.shared.action"));
    Assert.True(Find(retained.Root, "spotify.playlist.detail.scroll")
            .ScrollNearEndActionId is null,
        "A failed adjacent load remained in an automatic retry loop.");

    harness.PlaylistDetailError = null;
    await widget.OnActionAsync(new(
        "spotify.page.retry", "spotify.page.retained-error.shared.action"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 3);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.retry-wait", 4).Root,
        TrackFocus("wide", "spotify:track:track-12")));
    var recovered = widget.RenderSnapshot("spotify.playlist.retry", 5);
    Assert.Equal("spotify.playlist.play.shared", recovered.InitialFocusId);
    await StopAsync(widget);
}

static async Task SparsePlaylistPage()
{
    var harness = SpotifyHarness.Ready();
    harness.Playlists = new([], 0, 12, 24);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await WaitUntil(() => harness.PlaylistCalls == 1);

    var snapshot = widget.RenderSnapshot("spotify.playlists.sparse", 1);
    Assert.NotNull(Find(snapshot.Root, "spotify.page.sparse.playlist.shared"));
    Assert.Equal("spotify.playlists.cursor.after",
        Find(snapshot.Root, "spotify.playlists.scroll").ScrollNearEndActionId);
    await widget.OnActionAsync(new(
        "spotify.playlists.cursor.after", "spotify.playlists.scroll"));
    await WaitUntil(() => harness.PlaylistCalls == 2);
    var next = widget.RenderSnapshot("spotify.playlists.sparse-next", 2);
    Assert.Equal("spotify.playlists.empty.shared.action", next.InitialFocusId);
    Assert.NotNull(Find(next.Root, next.InitialFocusId!));
    await StopAsync(widget);
}

static async Task DeviceActions()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.devices", "spotify.nav.wide.devices"));
    var snapshot = widget.RenderSnapshot("spotify.devices", 1);
    Assert.NotNull(Find(snapshot.Root, "spotify.local.shared.action"));
    Assert.NotNull(Find(snapshot.Root, "spotify.device.shared.1"));

    await widget.OnActionAsync(new("spotify.local.start", "spotify.local.shared.action"));
    Assert.Equal(SpotifyLocalPlaybackOperation.StartAndTransfer,
        harness.LocalCommands.Single().Operation);
    Assert.Equal(1, harness.DeviceCalls);

    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"),
        PlaylistFocus("wide", "playlist-one")));
    await widget.OnActionAsync(new(PlaylistTrack("spotify:track:next"),
        TrackFocus("wide", "spotify:track:next")));
    Assert.Equal("local-placeholder", harness.StartedPlayback.Single().DeviceId);

    await widget.OnActionAsync(new("spotify.nav.devices", "spotify.nav.wide.devices"));
    await widget.OnActionAsync(new("spotify.device.select.1", "spotify.device.shared.1"));
    Assert.Equal("remote-device", harness.TransferredDevices.Single());

    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"),
        PlaylistFocus("wide", "playlist-one")));
    await widget.OnActionAsync(new(PlaylistTrack("spotify:track:next"),
        TrackFocus("wide", "spotify:track:next")));
    Assert.Equal("remote-device", harness.StartedPlayback[^1].DeviceId);
    await StopAsync(widget);
}

static async Task MissingPlaybackDeviceGuidance()
{
    var harness = SpotifyHarness.Ready();
    harness.StartPlaybackError = new SpotifyApplicationException(
        "resource_not_found", "The platform capability request failed (resource_not_found).");
    var widget = await StartAsync(harness, search: true);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.search.query", "spotify.search.query") { CommittedText = "night" });
    await WaitUntil(() => CollectionRows(widget.RenderSnapshot("missing-device", 1).Root).Length > 0);
    var selected = CollectionRows(widget.RenderSnapshot("missing-device", 2).Root)[0];
    await widget.OnActionAsync(new(selected.ActionId!, selected.Id));
    await WaitUntil(() => widget.Status.Contains("Open Devices", StringComparison.Ordinal));
    Assert.Equal("No active Spotify device. Open Devices and choose where to play.",
        widget.Status);
    await StopAsync(widget);
}

static async Task ProjectedProgress()
{
    var clock = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(20_000));
    var harness = SpotifyHarness.Ready(capturedAt: 20_000, progress: 30_000);
    var widget = await StartAsync(harness, clock);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var calls = harness.PlaybackCalls;
    clock.Advance(TimeSpan.FromSeconds(3));
    var snapshot = widget.Render().CreateSnapshot("spotify.progress", 2);
    Assert.Equal(33_000D, Find(snapshot.Root, "spotify.seek.slider").Value!.Value);
    Assert.Equal("0:33", Find(snapshot.Root, "spotify.seek.elapsed").Text);
    Assert.Equal(calls, harness.PlaybackCalls);
    await StopAsync(widget);
}

static async Task OptimisticPlayback()
{
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var harness = SpotifyHarness.Ready();
    harness.ControlWait = release.Task;
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var action = widget.OnActionAsync(new WidgetActionEvent(
        "spotify.play-toggle", "spotify.play-toggle")).AsTask();
    await WaitUntil(() => harness.Commands.Count == 1);
    var pending = widget.Render().CreateSnapshot("spotify.pending", 2);
    Assert.Equal(WidgetGlyph.Play, Find(pending.Root, "spotify.play-toggle").Glyph);
    Assert.True(Find(pending.Root, "spotify.play-toggle").IsBusy == true,
        "Pending playback did not expose busy feedback.");
    foreach (var id in new[]
             {
                 "spotify.previous", "spotify.next", "spotify.shuffle", "spotify.repeat",
                 "spotify.seek.slider",
             })
    {
        var control = Find(pending.Root, id);
        Assert.True(control.IsBusy is not true,
            $"Unrelated control '{id}' incorrectly entered its busy state.");
        Assert.True(control.IsDisabled is not true,
            $"Unrelated control '{id}' was transiently disabled.");
    }
    release.SetResult();
    await action;
    Assert.Equal(SpotifyPlaybackOperation.Pause, harness.Commands[0].Operation);
    await StopAsync(widget);
}

static async Task PlaybackShortcutKeepsStablePageEntry()
{
    var release = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var harness = SpotifyHarness.Ready();
    harness.ControlWait = release.Task;
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var before = widget.RenderSnapshot("spotify.focus.before", 1);
    var focus = PlaylistFocus("shared", "playlist-one");
    Assert.Equal(focus, before.InitialFocusId);

    var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.X,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        focus,
        Sequence: 211050,
        ActiveInputScopeId: before.ActiveInputScopeId,
        SnapshotSequence: before.Sequence));
    Assert.True(handled, "The root X shortcut was not admitted from the playlist leaf.");
    await WaitUntil(() => harness.Commands.Count == 1);
    var pending = widget.RenderSnapshot("spotify.focus.pending", 2);
    Assert.Equal(focus, pending.InitialFocusId);
    Assert.True(Find(pending.Root, "spotify.play-toggle").IsDisabled == true,
        "The pending Play/Pause owner was not disabled.");

    release.SetResult();
    await WaitUntil(() => Find(widget.RenderSnapshot(
            "spotify.focus.terminal", 3).Root,
        "spotify.play-toggle").IsDisabled is not true);
    var terminal = widget.RenderSnapshot("spotify.focus.terminal", 4);
    Assert.Equal(focus, terminal.InitialFocusId);
    Assert.Equal(SpotifyPlaybackOperation.Pause, harness.Commands.Single().Operation);
    await StopAsync(widget);
}

static async Task OptimisticControlsSurvivePreResponseObservations()
{
    foreach (var scenario in OptimisticControlScenarios())
    {
        var harness = SpotifyHarness.Ready();
        harness.Playback = scenario.Initial;
        var clock = new ManualTimeProvider(DateTimeOffset.FromUnixTimeSeconds(100));
        var widget = await StartAsync(harness, clock);
        await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);

        var providerResponse = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.ControlWait = providerResponse.Task;
        var commandTask = widget.OnActionAsync(scenario.Action).AsTask();
        await WaitUntil(() => harness.Commands.Count == 1,
            $"{scenario.Name} was not admitted by the provider seam.");

        var pending = widget.Render().CreateSnapshot($"spotify.{scenario.Name}.pending", 2);
        var initiatingControl = Find(pending.Root, scenario.ControlId);
        Assert.True(initiatingControl.IsBusy == true,
            $"{scenario.Name} did not retain Busy through its provider response.");
        Assert.True(initiatingControl.IsDisabled == true,
            $"{scenario.Name} did not remain disabled through its provider response.");
        var unrelated = Find(pending.Root, "spotify.next");
        Assert.True(unrelated.IsBusy is not true && unrelated.IsDisabled is not true,
            $"{scenario.Name} changed an unrelated control's visual availability.");
        Assert.Equal(scenario.ProjectedValue, scenario.OwnedValue(widget.Playback!));

        var staleObservation = scenario.Initial with
        {
            Item = scenario.Initial.Item! with { Title = $"{scenario.Name} observed title" },
            Attribution = $"{scenario.Name} observed authority",
        };
        var stalePoll = new TaskCompletionSource<SpotifyPlaybackSummary>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.PlaybackHandler = _ => new ValueTask<SpotifyPlaybackSummary>(stalePoll.Task);
        var callsBeforePoll = harness.PlaybackCalls;
        var refreshTask = widget.OnActionAsync(new WidgetActionEvent(
            "spotify.refresh", "spotify.refresh")).AsTask();
        await WaitUntil(() => harness.PlaybackCalls > callsBeforePoll,
            $"{scenario.Name} did not start the pre-response playback observation.");

        providerResponse.SetResult();
        await commandTask;
        var acknowledged = widget.Render().CreateSnapshot(
            $"spotify.{scenario.Name}.acknowledged", 3);
        initiatingControl = Find(acknowledged.Root, scenario.ControlId);
        Assert.True(initiatingControl.IsBusy is not true,
            $"{scenario.Name} remained Busy after its exact provider response.");
        Assert.True(initiatingControl.IsDisabled is not true,
            $"{scenario.Name} remained disabled after its exact provider response.");
        Assert.Equal(scenario.ProjectedValue, scenario.OwnedValue(widget.Playback!));
        var reconciliation = OptimisticReconciliation(widget);
        Assert.NotNull(reconciliation);
        Assert.Equal(clock.GetUtcNow() + TimeSpan.FromSeconds(12),
            reconciliation!.ExpiresAt);
        Assert.True(SpotifyPlaybackPolicy.MatchesOptimisticPresentation(
                widget.Playback, reconciliation),
            $"{scenario.Name} projected value was not recognized as matching.");

        stalePoll.SetResult(staleObservation);
        await refreshTask;
        Assert.Equal(scenario.ProjectedValue, scenario.OwnedValue(widget.Playback!));
        Assert.Equal(staleObservation.Item!.Title, widget.Playback!.Item!.Title);
        Assert.Equal(staleObservation.Attribution, widget.Playback.Attribution);

        var successor = scenario.Successor(staleObservation) with
        {
            Item = staleObservation.Item! with { Title = $"{scenario.Name} successor title" },
            Attribution = $"{scenario.Name} successor authority",
        };
        harness.PlaybackHandler = _ => ValueTask.FromResult(successor);
        await widget.OnActionAsync(new WidgetActionEvent(
            "spotify.refresh", "spotify.refresh"));
        Assert.Equal(scenario.SuccessorValue, scenario.OwnedValue(widget.Playback!));
        Assert.Equal(successor.Item!.Title, widget.Playback!.Item!.Title);
        Assert.Equal(successor.Attribution, widget.Playback.Attribution);
        Assert.True(OptimisticReconciliation(widget) is null,
            $"{scenario.Name} reconciliation survived a post-response successor.");

        await StopAsync(widget);
    }
}

static async Task OptimisticControlsRollbackWithExactOwnership()
{
    foreach (var scenario in OptimisticControlScenarios())
    {
        var harness = SpotifyHarness.Ready();
        harness.Playback = scenario.Initial;
        harness.ControlError = new SpotifyApplicationException(
            "forbidden", $"{scenario.Name} rejected");
        var widget = await StartAsync(harness);
        await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);

        await widget.OnActionAsync(scenario.Action);
        Assert.Equal(scenario.InitialValue, scenario.OwnedValue(widget.Playback!));
        var restored = Find(widget.RenderSnapshot(
            $"spotify.{scenario.Name}.failure", 2).Root, scenario.ControlId);
        Assert.True(restored.IsBusy is not true && restored.IsDisabled is not true,
            $"{scenario.Name} did not re-enable after provider failure.");
        Assert.True(OptimisticReconciliation(widget) is null,
            $"{scenario.Name} retained reconciliation after provider failure.");
        await StopAsync(widget);
    }

    var cancellationHarness = SpotifyHarness.Ready();
    cancellationHarness.Playback = cancellationHarness.Playback with
        { ProgressMilliseconds = 45_000 };
    var cancellationGate = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    cancellationHarness.ControlWait = cancellationGate.Task;
    var cancellationWidget = await StartAsync(cancellationHarness);
    await WaitUntil(() => cancellationWidget.ViewState == SpotifyWidgetViewState.Ready);
    using var cancellation = new CancellationTokenSource();
    var canceledAction = cancellationWidget.OnActionAsync(new WidgetActionEvent(
        "spotify.seek", "spotify.seek.slider", RequestedValue: 120_000),
        cancellation.Token).AsTask();
    await WaitUntil(() => cancellationHarness.Commands.Count == 1);
    cancellation.Cancel();
    await canceledAction;
    Assert.Equal(45_000L, cancellationWidget.Playback!.ProgressMilliseconds);
    Assert.True(OptimisticReconciliation(cancellationWidget) is null,
        "Canceled Seek retained optimistic reconciliation.");

    var secondGate = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    cancellationHarness.ControlWait = secondGate.Task;
    var secondAction = cancellationWidget.OnActionAsync(new WidgetActionEvent(
        "spotify.shuffle", "spotify.shuffle")).AsTask();
    await WaitUntil(() => cancellationHarness.Commands.Count == 2);
    var currentSequence = PendingOperationSequence(cancellationWidget);
    RestoreOptimisticForTest(cancellationWidget, cancellationHarness.Playback,
        currentSequence - 1);
    var stillPending = cancellationWidget.RenderSnapshot(
        "spotify.exact-terminal-owner", 3);
    Assert.True(Find(stillPending.Root, "spotify.shuffle").IsBusy == true,
        "A stale terminal cleared the newer Shuffle pending owner.");
    Assert.True(cancellationWidget.Playback!.ShuffleState,
        "A stale terminal rolled back the newer Shuffle projection.");
    secondGate.SetResult();
    await secondAction;
    await StopAsync(cancellationWidget);
}

static async Task RepeatIsBinaryAndPreciselyAvailable()
{
    var template = SpotifyHarness.Ready().Playback;
    foreach (var (initial, expected) in new[]
             {
                 (SpotifyRepeatState.Off, SpotifyRepeatState.Context),
                 (SpotifyRepeatState.Context, SpotifyRepeatState.Off),
                 (SpotifyRepeatState.Track, SpotifyRepeatState.Off),
             })
    {
        var harness = SpotifyHarness.Ready();
        harness.Playback = template with
        {
            RepeatState = initial,
            DisallowedActions = template.DisallowedActions with
            {
                TogglingRepeatContext = initial != SpotifyRepeatState.Off,
                TogglingRepeatTrack = true,
            },
        };
        var widget = await StartAsync(harness);
        await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
        var before = Find(widget.RenderSnapshot(
            $"spotify.repeat.{initial}.before", 1).Root, "spotify.repeat");
        Assert.True(before.IsDisabled is not true,
            $"Repeat {initial} incorrectly blocked its one-press binary transition.");
        await widget.OnActionAsync(new WidgetActionEvent(
            "spotify.repeat", "spotify.repeat"));
        Assert.Equal(expected, harness.Commands.Single().RepeatState);
        await StopAsync(widget);
    }

    var blockedHarness = SpotifyHarness.Ready();
    blockedHarness.Playback = template with
    {
        RepeatState = SpotifyRepeatState.Off,
        DisallowedActions = template.DisallowedActions with
        {
            TogglingRepeatContext = true,
            TogglingRepeatTrack = false,
        },
    };
    var blockedWidget = await StartAsync(blockedHarness);
    await WaitUntil(() => blockedWidget.ViewState == SpotifyWidgetViewState.Ready);
    Assert.True(Find(blockedWidget.RenderSnapshot(
            "spotify.repeat.blocked", 1).Root, "spotify.repeat").IsDisabled == true,
        "Repeat Off remained enabled when Off-to-Context was disallowed.");
    await blockedWidget.OnActionAsync(new WidgetActionEvent(
        "spotify.repeat", "spotify.repeat"));
    Assert.Equal(0, blockedHarness.Commands.Count);
    await StopAsync(blockedWidget);
}

static async Task PlaybackDiagnosticsAreFailureOnly()
{
    var diagnostics = new RecordingSpotifyDiagnostics();
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness, diagnostics: diagnostics);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var snapshot = widget.RenderSnapshot("spotify.diagnostics", 303);
    var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.A,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        "spotify.play-toggle",
        Sequence: 303001,
        ActiveInputScopeId: snapshot.ActiveInputScopeId,
        SnapshotSequence: snapshot.Sequence));
    Assert.True(handled, "The correlated playback action was not admitted.");
    await WaitUntil(() => harness.Commands.Count == 1);
    Assert.Equal(0, diagnostics.ForOperation(303001).Count);

    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    harness.ControlWait = release.Task;
    var first = widget.OnActionAsync(new WidgetActionEvent(
        "spotify.play-toggle", "spotify.play-toggle", Sequence: 303002)).AsTask();
    await WaitUntil(() => harness.Commands.Count >= 2);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.play-toggle", "spotify.play-toggle", Sequence: 303003));
    Assert.Equal(0, diagnostics.ForOperation(303003).Count);
    release.TrySetResult();
    await first;

    const string providerMessage = "provider title secret\nforged-log-line";
    harness.ControlError = new SpotifyApplicationException("forbidden", providerMessage);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.shuffle", "spotify.shuffle", Sequence: 303004));
    Assert.True(diagnostics.Contains("playback-provider", "failed-forbidden", 303004),
        "The provider failure was not reduced to its fixed classification.");

    harness.QueueError = new SpotifyApplicationException(
        "spotify_unavailable", "private queue response");
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.nav.queue", "spotify.nav.queue", Sequence: 303005));
    await WaitUntil(() => diagnostics.ContainsAny(
        "queue-refresh", "failure-spotify_unavailable"));

    const string dynamicActionId = "provider.media.secret.with.dots";
    await widget.OnActionAsync(new WidgetActionEvent(
        dynamicActionId, dynamicActionId, Sequence: 303006));
    Assert.Equal(0, diagnostics.ForOperation(303006).Count);

    diagnostics.Record("typed-boundary", dynamicActionId, 303007);
    diagnostics.Record("provider\nmessage", "failed", 303008);
    var encoded = diagnostics.EncodedLines;
    Assert.True(encoded.Count > 0, "The shared diagnostic encoding emitted no records.");
    Assert.True(encoded.All(line =>
            !line.Contains("spotify.play-toggle", StringComparison.Ordinal) &&
            !line.Contains(dynamicActionId, StringComparison.Ordinal) &&
            !line.Contains(providerMessage, StringComparison.Ordinal) &&
            !line.Contains("forged-log-line", StringComparison.Ordinal)),
        "A raw action identity or provider-controlled message reached encoded diagnostics.");
    await StopAsync(widget);
}

static async Task LocalPlaybackSummaryRevisionFence()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.devices", "spotify.nav.devices"));
    await WaitUntil(() => harness.DeviceCalls == 1 && harness.LocalPlaybackCalls == 1);
    var ready = Find(widget.RenderSnapshot("spotify.local.ready", 1).Root,
        "spotify.local.shared.action");
    Assert.Equal("spotify.local.start", ready.ActionId);
    Assert.True(ready.IsDisabled is not true && ready.IsBusy is not true,
        "The initial Devices publication was not settled before the revision-fence test.");

    var stale = new TaskCompletionSource<SpotifyLocalPlaybackSummary>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    harness.LocalPlaybackHandler = _ =>
        new ValueTask<SpotifyLocalPlaybackSummary>(stale.Task);
    var poll = RefreshPlaybackForTest(widget);
    await WaitUntil(() => harness.LocalPlaybackCalls == 2);

    harness.LocalControlResult = harness.LocalPlayback with
    {
        State = SpotifyLocalPlaybackState.Active,
        DisplayMessage = "Playing through this PC.",
    };
    await widget.OnActionAsync(new("spotify.local.start", "spotify.local.shared.action"));
    await WaitUntil(() => harness.LocalCommands.Count == 1);
    await WaitUntil(() => string.Equals(
            widget.Status, "Playing through this PC.", StringComparison.Ordinal),
        "The successful local operation state was not committed before stale release.");
    harness.LocalPlaybackHandler = _ => ValueTask.FromResult(harness.LocalControlResult!);
    stale.SetResult(harness.LocalPlayback with
    {
        State = SpotifyLocalPlaybackState.Ready,
        DisplayMessage = "Stale ready summary",
    });
    await poll;
    await WaitUntil(() =>
    {
        var current = widget.RenderSnapshot("spotify.local.settled", 3);
        var local = Find(current.Root, "spotify.local.shared.action");
        return local.ActionId == "spotify.local.stop" &&
            local.IsDisabled is not true && local.IsBusy is not true;
    }, "The successful local operation did not settle after stale-poll retirement.");
    var retained = widget.RenderSnapshot("spotify.local.retained", 4);
    Assert.True(ContainsTextFragment(retained.Root, "Playing here"),
        "A pre-operation local summary replaced the accepted operation result.");
    Assert.True(!ContainsTextFragment(retained.Root, "Stale ready summary"),
        "A stale local summary crossed the operation revision fence.");

    harness.LocalPlaybackHandler = _ => ValueTask.FromResult(
        harness.LocalControlResult! with
        {
            State = SpotifyLocalPlaybackState.AutoplayBlocked,
            DisplayMessage = "Spotify audio was blocked by browser autoplay policy.",
        });
    await RefreshPlaybackForTest(widget);
    var blocked = widget.RenderSnapshot("spotify.local.blocked", 5);
    Assert.NotNull(Find(blocked.Root, "spotify.local.autoplay-blocked"));
    await StopAsync(widget);
}

static async Task FailedControlRollback()
{
    var harness = SpotifyHarness.Ready();
    harness.ControlError = new SpotifyApplicationException(
        "forbidden", "Spotify did not allow playback control");
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.shuffle", "spotify.shuffle"));
    var snapshot = widget.Render().CreateSnapshot("spotify.rollback", 2);
    Assert.True(Find(snapshot.Root, "spotify.shuffle").IsSelected != true,
        "Failed shuffle did not roll back.");
    Assert.True(widget.Status.Contains("did not allow", StringComparison.OrdinalIgnoreCase),
        "Spotify account refusal was not explained.");
    await StopAsync(widget);
}

static async Task PermissionDenied()
{
    var harness = SpotifyHarness.Ready();
    harness.ConfigurationError = new SpotifyApplicationException(
        "forbidden", "Spotify configuration is unavailable");
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.PermissionDenied);
    var snapshot = widget.Render().CreateSnapshot("spotify.denied", 1);
    Assert.Equal("spotify.retry", snapshot.InitialFocusId);
    Assert.NotNull(Find(snapshot.Root, "spotify.retry"));
    await StopAsync(widget);
}

static Task ManifestContract()
{
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "manifest.json")));
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.Equal(0, errors.Count);
    Assert.Equal("widgetrail.samples.spotify", manifest.Id);
    Assert.Equal("widgetrail.samples", manifest.Publisher);
    Assert.Equal(WidgetEntrypointRuntimes.FullTrustApplicationV1,
        manifest.Entrypoint.Runtime);
    Assert.Equal("payload/SpotifyApplication.exe", manifest.Entrypoint.Executable);
    Assert.True(manifest.Entrypoint.Assembly is null && manifest.Entrypoint.Type is null,
        "Full-trust Spotify retained the sandbox worker entrypoint.");
    Assert.Equal(0, manifest.Permissions.Count);
    Assert.Equal(0, manifest.OptionalPermissions.Count);
    Assert.Equal("0.3.63", manifest.Version);
    Assert.SequenceEqual(["x64"], manifest.Architectures);
    Assert.NotNull(manifest.ResidencyPolicy);
    Assert.Equal(WidgetResidencyPolicies.KeepAlive, manifest.ResidencyPolicy!.Mode);
    Assert.Equal(WidgetGlyph.Music, manifest.Presentation.Icon);
    Assert.Equal("spotify.brand.green", manifest.Presentation.PackageIcon?.AssetId);
    Assert.Equal(WidgetPackageIconColorMode.OriginalColor,
        manifest.Presentation.PackageIcon?.ColorMode);
    var expectedAssets = new Dictionary<string, (string Path, int Bytes, string Hash)>(
        StringComparer.Ordinal)
    {
        ["spotify.brand.black"] = ("assets/icons/spotify-black.svg", 1242,
            "5595AFEA0E6F009B1DD8529511204D0FD5CA035E49C85409D1697063B3C27A05"),
        ["spotify.brand.full-green"] = ("assets/icons/spotify-full-green.svg", 4522,
            "AB2131B5F1BA0BE90CD2F1B9F9584717158668C6755952D24C4D762331100ADC"),
        ["spotify.brand.green"] = ("assets/icons/spotify-green.svg", 1255,
            "EAD72F82725038389CCA09F439FDB7807640E122500C934F2500C7036BF40DBB"),
        ["spotify.brand.white"] = ("assets/icons/spotify-white.svg", 1252,
            "8929D148F54CEDE78F0F36CE90DF815E5EA5E5559E7FAECCAD3669302EF2DAA1"),
    };
    Assert.Equal(expectedAssets.Count, manifest.IconAssets.Count);
    foreach (var (id, expected) in expectedAssets)
    {
        Assert.Equal(expected.Path, manifest.IconAssets[id].Path);
        var path = Path.Combine(
            AppContext.BaseDirectory, expected.Path.Replace('/', Path.DirectorySeparatorChar));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(expected.Bytes, bytes.Length);
        Assert.Equal(expected.Hash, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    var packageScript = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "package", "Build-CommunityPackage.ps1"));
    foreach (var expected in expectedAssets.Values)
        Assert.Equal(2, CountOccurrences(packageScript, Path.GetFileName(expected.Path)));
    return Task.CompletedTask;
}

static async Task PlaylistDiskPersistence()
{
    var root = Path.Combine(Path.GetTempPath(), "wrail-playlist-disk-" + Guid.NewGuid().ToString("N"));
    try
    {
        var partition = Guid.NewGuid().ToString("N");
        var harness = SpotifyHarness.Ready();
        var playlist = harness.Playlists.Items[0] with { SnapshotId = "persistent-one" };
        harness.Playlists = harness.Playlists with { Items = [playlist] };
        var cache = new SpotifyPlaylistCache(root); cache.SetPartition(partition);
        await new SpotifySelectedPlaylistPageSource(harness, new(playlist.PlaylistId, 1), cache).LoadAsync(0, 24, default);
        await cache.WhenIdleAsync(); // Graceful application exit drains the optional writer.
        var restarted = new SpotifyPlaylistCache(root); restarted.SetPartition(partition);
        await new SpotifySelectedPlaylistPageSource(harness, new(playlist.PlaylistId, 2), restarted).LoadAsync(0, 24, default);
        Assert.Equal(2, harness.PlaylistMetadataCalls);
        Assert.Equal(1, harness.PlaylistDetailCalls);
        var otherAccount = new SpotifyPlaylistCache(root); otherAccount.SetPartition(Guid.NewGuid().ToString("N"));
        await new SpotifySelectedPlaylistPageSource(harness, new(playlist.PlaylistId, 3), otherAccount).LoadAsync(0, 24, default);
        Assert.Equal(2, harness.PlaylistDetailCalls);
        await otherAccount.WhenIdleAsync();
        harness.Playlists = harness.Playlists with { Items = [playlist with { SnapshotId = "persistent-two" }] };
        await new SpotifySelectedPlaylistPageSource(harness, new(playlist.PlaylistId, 4), restarted).LoadAsync(0, 24, default);
        Assert.Equal(3, harness.PlaylistDetailCalls);
        restarted.Clear(); await restarted.WhenIdleAsync();
        Assert.True(!Directory.EnumerateDirectories(root).Any(path => Path.GetFileName(path).StartsWith(partition, StringComparison.Ordinal)),
            "Disconnect retained the current authorization's pages.");
        var disk = new SpotifyPlaylistDiskCache(root, partition);
        Assert.True(await disk.ReadAsync(playlist.PlaylistId, "persistent-two", 0, 24, default) is null,
            "Cleared disk pages survived restart.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static async Task PlaylistDiskFailures()
{
    var root = Path.Combine(Path.GetTempPath(), "wrail-playlist-failures-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var partition = Guid.NewGuid().ToString("N");
        var page = SpotifyHarness.Ready().PlaylistDetail;
        var disk = new SpotifyPlaylistDiskCache(root, partition, maximumPlaylists: 2);
        Assert.True(await disk.WriteAsync("one", "v1", 0, 24, page, default), "Initial disk write failed.");
        var file = Directory.GetFiles(root, "*.page", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(file, "broken JSON");
        Assert.True(await disk.ReadAsync("one", "v1", 0, 24, default) is null, "Corrupt JSON was admitted.");
        Assert.True(await disk.WriteAsync("one", "v1", 0, 24, page, default), "Corrupt entry could not be replaced.");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        Assert.NotNull(await disk.ReadAsync("one", "v1", 0, 24, default));
        Assert.True(!await disk.WriteAsync("one", "v1", 0, 24, page, default), "Locked cache entry was overwritten.");
        File.SetAttributes(file, FileAttributes.Normal);
        Assert.True(await disk.WriteAsync("two", "v1", 0, 24, page, default), "Second playlist failed.");
        Assert.True(await disk.WriteAsync("three", "v1", 0, 24, page, default), "LRU playlist write failed.");
        Assert.Equal(2, Directory.GetDirectories(root).Length);
        var byteRoot = Path.Combine(root, "byte-budget");
        var seed = new SpotifyPlaylistDiskCache(byteRoot, partition);
        Assert.True(await seed.WriteAsync("one", "v1", 0, 24, page, default), "Byte fixture failed.");
        var pageBytes = new FileInfo(Directory.GetFiles(byteRoot, "*.page", SearchOption.AllDirectories).Single()).Length;
        var bounded = new SpotifyPlaylistDiskCache(byteRoot, partition, maximumBytes: pageBytes * 2 + 16);
        Assert.True(await bounded.WriteAsync("two", "v1", 0, 24, page, default), "Budgeted second page failed.");
        Assert.True(await bounded.WriteAsync("three", "v1", 0, 24, page, default), "Byte eviction failed.");
        Assert.True(Directory.GetFiles(byteRoot, "*.page", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length) <= pageBytes * 2 + 16, "Disk byte budget was exceeded.");
        var small = new SpotifyPlaylistDiskCache(Path.Combine(root, "small"), partition, maximumBytes: 16);
        Assert.True(!await small.WriteAsync("one", "v1", 0, 24, page, default), "Over-budget entry was written.");
        var blockedRoot = Path.Combine(root, "not-a-directory"); await File.WriteAllTextAsync(blockedRoot, "blocked");
        var blocked = new SpotifyPlaylistDiskCache(blockedRoot, partition);
        Assert.True(!await blocked.WriteAsync("one", "v1", 0, 24, page, default), "Unavailable disk accepted a write.");
        Assert.True(await blocked.ReadAsync("one", "v1", 0, 24, default) is null, "Unavailable disk was not a miss.");
        var harness = SpotifyHarness.Ready();
        harness.Playlists = harness.Playlists with { Items = [harness.Playlists.Items[0] with { SnapshotId = "one" }] };
        var fallback = new SpotifyPlaylistCache(blockedRoot); fallback.SetPartition(partition);
        var loaded = await new SpotifySelectedPlaylistPageSource(harness, new("playlist-one", 1), fallback).LoadAsync(0, 24, default);
        Assert.Equal(1, loaded.Items.Items.Count);
        Assert.Equal(1, harness.PlaylistDetailCalls);
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static async Task PlaylistVersionCache()
{
    var harness = SpotifyHarness.Ready();
    var playlist = harness.Playlists.Items[0] with { SnapshotId = "version-ONE" };
    harness.Playlists = harness.Playlists with { Items = [playlist] };
    var cache = new SpotifyPlaylistCache();
    var first = new SpotifySelectedPlaylistPageSource(harness, new(playlist.PlaylistId, 1), cache);
    await first.LoadAsync(0, 24, default);
    await first.LoadAsync(0, 24, default);
    Assert.Equal(1, harness.PlaylistMetadataCalls);
    Assert.Equal(1, harness.PlaylistDetailCalls);
    var reopened = new SpotifySelectedPlaylistPageSource(harness, new(playlist.PlaylistId, 2), cache);
    await reopened.LoadAsync(0, 24, default);
    Assert.Equal(2, harness.PlaylistMetadataCalls);
    Assert.Equal(1, harness.PlaylistDetailCalls);
    await reopened.LoadAsync(24, 24, default);
    Assert.Equal(2, harness.PlaylistDetailCalls);
    harness.Playlists = harness.Playlists with { Items = [playlist with { SnapshotId = "version-TWO" }] };
    await new SpotifySelectedPlaylistPageSource(harness, new(playlist.PlaylistId, 3), cache).LoadAsync(0, 24, default);
    Assert.Equal(3, harness.PlaylistDetailCalls);
    harness.Playlists = harness.Playlists with { Items = [playlist with { SnapshotId = null }] };
    await new SpotifySelectedPlaylistPageSource(harness, new(playlist.PlaylistId, 4), cache).LoadAsync(0, 24, default);
    await new SpotifySelectedPlaylistPageSource(harness, new(playlist.PlaylistId, 5), cache).LoadAsync(0, 24, default);
    Assert.Equal(5, harness.PlaylistDetailCalls);
}

static Task PlaylistCacheBounds()
{
    var harness = SpotifyHarness.Ready();
    var playlist = harness.Playlists.Items[0] with { SnapshotId = "one" };
    var cache = new SpotifyPlaylistCache();
    var entry = cache.Observe(playlist);
    var page = harness.PlaylistDetail with { Items = Enumerable.Repeat(harness.PlaylistDetail.Items[0], 24).ToArray() };
    var retainedPages = SpotifyPlaylistCache.MaximumItems / 24;
    for (var i = 0; i <= retainedPages; i++) cache.Put(entry, i * 24, 24, page);
    Assert.True(cache.Get(entry, 0, 24) is null, "Oldest page exceeded the cache budget.");
    Assert.NotNull(cache.Get(entry, retainedPages * 24, 24));
    Assert.Equal(retainedPages * 24, entry!.Pages.Values.Sum(value => value.Value.Items.Count));
    var replacement = cache.Observe(playlist with { SnapshotId = "two" });
    cache.Put(entry, 0, 24, page);
    Assert.True(cache.Get(replacement, 0, 24) is null, "Old-version completion repopulated the cache.");
    Assert.Equal(0, entry.Pages.Count);
    for (var i = 0; i < SpotifyPlaylistCache.MaximumPlaylists; i++) cache.Observe(playlist with { PlaylistId = "other-" + i });
    cache.Put(replacement, 0, 24, page);
    Assert.True(cache.Get(replacement, 0, 24) is null, "Evicted playlist retained authority.");
    var active = cache.Observe(playlist);
    cache.Put(active, 0, 24, page);
    cache.Clear();
    cache.Put(active, 0, 24, page);
    Assert.True(cache.Get(active, 0, 24) is null, "Cleared account cache accepted an old completion.");
    Assert.Equal(0, active!.Pages.Count);
    return Task.CompletedTask;
}

static async Task PlaylistCacheNavigation()
{
    var harness = SpotifyHarness.Ready();
    harness.Playlists = harness.Playlists with { Items = [harness.Playlists.Items[0] with { SnapshotId = "same" }] };
    var widget = await StartAsync(harness);
    await WaitUntil(() => harness.PlaylistCalls == 1);
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"), PlaylistFocus("wide", "playlist-one")));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);
    await widget.OnActionAsync(new("spotify.playlist.back", "spotify.playlist.play.shared"));
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"), PlaylistFocus("wide", "playlist-one")));
    await WaitUntil(() => harness.PlaylistMetadataCalls == 2);
    Assert.Equal(1, harness.PlaylistDetailCalls);
    await widget.OnActionAsync(new("spotify.refresh", "spotify.refresh"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 2);
    Assert.Equal(3, harness.PlaylistMetadataCalls);
    await StopAsync(widget);
}

static Task PlaylistRoutePublication()
{
    var playlist = SpotifyHarness.Ready().Playlists.Items[0];
    var key = SpotifyCollectionIdentity.Playlist(playlist.PlaylistId);
    var media = new WidgetCursorResourceSnapshot<SpotifyMediaCollectionItem>(
        WidgetPagedResourceStatus.NotLoaded, [], null, null, null, null, null, 0);
    var playlists = new WidgetCursorResourceSnapshot<SpotifyPlaylistCollectionItem>(
        WidgetPagedResourceStatus.Ready, [new(playlist, key)], null, null, key, null, null, 1);
    var navigation = new WidgetNavigationSnapshot<SpotifyRoute>(
        SpotifyRoute.Playlists, SpotifyRoute.Playlists, 0,
        "spotify.window", null, null, 0, CancellationToken.None);
    // Selection is prepared before navigator publication. A concurrent progress
    // render can observe this legitimate intermediate state.
    var state = new SpotifyPresentationState(
        new(1, 1, 0, 1), SpotifyWidgetViewState.Ready, SpotifyHarness.Ready().Playback, null,
        "Ready", null, 0, false, navigation, media,
        new(playlists, UI.VerticalScroll("spotify.playlists.scroll", []) with
            { CollectionAnchorKey = key.Value }),
        new(new(new(playlist.PlaylistId, 1), playlist),
            new(media, UI.VerticalScroll("spotify.playlist.detail.scroll", []))),
        null, null, false, null, false, null);
    var handles = new SpotifyPresentationHandleFixture();
    var list = SpotifyPresentation.Render(state, handles.Compact, handles.UpNext)
        .CreateSnapshot("playlist-publication", 1);
    Assert.Equal(0, ViewSnapshotValidator.Validate(list).Count);
    Assert.NotNull(Find(list.Root, PlaylistFocus("wide", playlist.PlaylistId)));
    var detail = SpotifyPresentation.Render(state with
    {
        Navigation = navigation with { Route = SpotifyRoute.PlaylistDetail, Depth = 1 },
    }, handles.Compact, handles.UpNext).CreateSnapshot("playlist-publication", 2);
    Assert.Equal(0, ViewSnapshotValidator.Validate(detail).Count);
    Assert.NotNull(Find(detail.Root, "spotify.page.loading.shared.action"));
    return Task.CompletedTask;
}

static Task PresentationBoundaryIsPure()
{
    var media = new WidgetCursorResourceSnapshot<SpotifyMediaCollectionItem>(
        WidgetPagedResourceStatus.NotLoaded, [], null, null, null, null, null, 0);
    var playlists = new WidgetCursorResourceSnapshot<SpotifyPlaylistCollectionItem>(
        WidgetPagedResourceStatus.NotLoaded, [], null, null, null, null, null, 0);
    var navigation = new WidgetNavigationSnapshot<SpotifyRoute>(
        SpotifyRoute.Playlists, SpotifyRoute.Playlists, 0,
        "spotify.window", null, null, 0, CancellationToken.None);
    var state = new SpotifyPresentationState(
        new(1, 0, 0, null), SpotifyWidgetViewState.Unconfigured, null, null,
        "Spotify client ID required", null, 0, false, navigation,
        media, new SpotifyCursorPresentation<SpotifyPlaylistCollectionItem>(
            playlists, UI.VerticalScroll("spotify.playlists.scroll", [])),
        null, null, null, false, null, false, null);

    var handles = new SpotifyPresentationHandleFixture();
    var first = SnapshotJson.Serialize(
        SpotifyPresentation.Render(state, handles.Compact, handles.UpNext)
            .CreateSnapshot("spotify.presentation", 1));
    var second = SnapshotJson.Serialize(
        SpotifyPresentation.Render(state, handles.Compact, handles.UpNext)
            .CreateSnapshot("spotify.presentation", 1));
    Assert.SequenceEqual(first, second);
    return Task.CompletedTask;
}

static Task RouteActionPolicyIsClosed()
{
    var queue = SpotifyRouteActionPolicy.Classify(
        new WidgetActionEvent("spotify.nav.queue", "spotify.nav.wide.queue"));
    Assert.Equal(SpotifyActionKind.Navigate, queue.Kind);
    Assert.Equal(SpotifyDestination.Queue, queue.Destination);

    var back = SpotifyRouteActionPolicy.Classify(
        new WidgetActionEvent("spotify.playlist.back", "spotify.playlist.back.wide"));
    Assert.Equal(SpotifyActionKind.PlaylistBack, back.Kind);
    var seek = SpotifyRouteActionPolicy.Classify(
        new WidgetActionEvent(
            "spotify.seek", "spotify.seek.slider", RequestedValue: 12_345.4));
    Assert.Equal(12_345L, seek.RequestedPositionMs);
    Assert.Equal(SpotifyActionKind.Unknown, SpotifyRouteActionPolicy.Classify(
        new WidgetActionEvent("spotify.device.select.-1", "spotify.device.invalid")).Kind);
    Assert.Equal("spotify.nav.compact.playlists",
        SpotifyRouteActionPolicy.NavigationFocusId(
            SpotifyDestination.Playlists, "compact"));
    return Task.CompletedTask;
}

static Task PlaybackPolicyIsDeterministic()
{
    var playback = new SpotifyPlaybackSummary(
        true, true, 10_000, 60_000, 1_000,
        SpotifyRepeatState.Off, false,
        new SpotifyPlaybackItemSummary(
            SpotifyPlaybackItemType.Track, "Policy", "Artist", "Album",
            null, "spotify:track:policy"),
        new SpotifyPlaybackDisallowedActions(
            false, false, false, false, false, false, false, false),
        "Spotify");

    Assert.Equal(SpotifyPlaybackOperation.Pause,
        SpotifyPlaybackPolicy.ResolveToggle(playback));
    var seek = SpotifyPlaybackPolicy.BuildCommand(
        playback, SpotifyPlaybackOperation.Seek, 90_000);
    Assert.NotNull(seek);
    Assert.Equal(60_000L, seek!.PositionMilliseconds);
    var optimistic = SpotifyPlaybackPolicy.ApplyOptimistic(playback, seek, 2_000);
    Assert.Equal(60_000L, optimistic.ProgressMilliseconds);
    var projected = SpotifyPlaybackPolicy.Project(playback, 6_000);
    Assert.NotNull(projected);
    Assert.Equal(15_000L, projected!.ProgressMilliseconds);

    var blocked = playback with
    {
        DisallowedActions = playback.DisallowedActions with { Seeking = true },
    };
    Assert.Equal<SpotifyPlaybackCommand?>(null,
        SpotifyPlaybackPolicy.BuildCommand(
            blocked, SpotifyPlaybackOperation.Seek, 20_000));
    return Task.CompletedTask;
}

static Task ResponsibilitySplitContract()
{
    var sourceRoot = Path.Combine(AppContext.BaseDirectory, "source");
    var lifecycle = File.ReadAllText(Path.Combine(sourceRoot, "SpotifyWidget.cs"));
    var routes = File.ReadAllText(Path.Combine(sourceRoot, "SpotifyWidget.Routes.cs"));
    var playback = File.ReadAllText(Path.Combine(sourceRoot, "SpotifyWidget.Playback.cs"));
    var presentation = File.ReadAllText(
        Path.Combine(sourceRoot, "SpotifyWidget.Presentation.cs"));

    AssertSourceContains(lifecycle, "public sealed class SpotifyWidget : Widget");
    Assert.True(!new[] { lifecycle, routes, playback, presentation }.Any(source =>
            source.Contains("partial class SpotifyWidget", StringComparison.Ordinal)),
        "SpotifyWidget remains a logical partial type.");

    AssertSourceContains(lifecycle, "OnActivatedAsync");
    AssertSourceContains(lifecycle, "OnActionAsync");
    AssertSourceContains(lifecycle, "CapturePresentationState");
    AssertSourceContains(lifecycle, "private readonly WidgetCursorResource");
    Assert.True(!lifecycle.Contains("RenderConnected(", StringComparison.Ordinal),
        "Lifecycle/action wiring regained view composition.");

    AssertSourceContains(routes, "internal static class SpotifyRouteActionPolicy");
    AssertSourceContains(routes, "SpotifyActionIntent Classify");
    Assert.True(!routes.Contains("lock (", StringComparison.Ordinal) &&
                !routes.Contains("HostServices", StringComparison.Ordinal),
        "Route/action policy acquired mutable state or provider authority.");

    AssertSourceContains(playback, "internal static class SpotifyPlaybackPolicy");
    AssertSourceContains(playback, "BuildCommand");
    AssertSourceContains(playback, "ApplyOptimistic");
    Assert.True(!playback.Contains("lock (", StringComparison.Ordinal) &&
                !playback.Contains("HostServices", StringComparison.Ordinal) &&
                !playback.Contains("Task", StringComparison.Ordinal),
        "Playback policy acquired state, provider, or task authority.");

    AssertSourceContains(presentation, "internal static class SpotifyPresentation");
    AssertSourceContains(presentation, "SpotifyPresentationState presentation");
    AssertSourceContains(presentation, "RenderConnected");
    Assert.True(!presentation.Contains("lock (_gate)", StringComparison.Ordinal),
        "Pure presentation builders read mutable widget state.");
    Assert.True(!presentation.Contains("HostServices", StringComparison.Ordinal),
        "Pure presentation builders acquired provider ownership.");
    Assert.True(!presentation.Contains("private readonly WidgetCursorResource",
            StringComparison.Ordinal),
        "Presentation code duplicated resource ownership.");
    return Task.CompletedTask;
}

static void AssertSourceContains(string source, string value) =>
    Assert.True(source.Contains(value, StringComparison.Ordinal),
        $"Expected source boundary to contain '{value}'.");

static Task TimeFormatting()
{
    Assert.Equal("0:00", SpotifyWidget.FormatTime(-1));
    Assert.Equal("4:05", SpotifyWidget.FormatTime(245_000));
    Assert.Equal("1:01:01", SpotifyWidget.FormatTime(3_661_000));
    return Task.CompletedTask;
}

static async Task ProductionCursorBuffer()
{
    var harness = SpotifyHarness.Ready();
    var playlists = Enumerable.Range(0, 150).Select(index => new SpotifyPlaylistSummary(
        $"buffer-{index}", $"Buffer {index}", null, null,
        $"https://open.spotify.com/playlist/buffer-{index}", $"spotify:playlist:buffer-{index}",
        "Listener", false, true, 1)).ToArray();
    harness.Playlists = new(playlists, 0, 24, playlists.Length);
    var widget = await StartAsync(harness, productionPaging: true);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await WaitUntil(() => harness.PlaylistCalls == 1);
    await WaitUntil(() => CollectionRows(Find(widget.RenderSnapshot("buffer.test", 1).Root, "spotify.playlists.scroll")).Length == 24);
    Assert.Equal(24, harness.LastPlaylistLimit);
    var first = CollectionRows(Find(widget.RenderSnapshot("buffer.test", 1).Root, "spotify.playlists.scroll"))[0].Id;
    for (int page = 2; page <= 4; page++)
    {
        await widget.OnActionAsync(new("spotify.playlists.cursor.after", "spotify.playlists.scroll"));
        var expected = page * 24;
        await WaitUntil(() => CollectionRows(Find(widget.RenderSnapshot("buffer.test", page).Root, "spotify.playlists.scroll")).Length == expected);
    }
    var retained = CollectionRows(Find(widget.RenderSnapshot("buffer.test", 5).Root, "spotify.playlists.scroll"));
    Assert.Equal(96, retained.Length);
    Assert.Equal(first, retained[0].Id);
    await StopAsync(widget);
}

static async Task QueuePlaybackPreservesTail()
{
    var harness = SpotifyHarness.Ready();
    var original = harness.Queue.Items[0];
    harness.Queue = harness.Queue with { Items = [original,
        original with { Uri = "spotify:track:second", Title = "Second" }, original] };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.queue"));
    await WaitUntil(() => harness.QueueCalls == 1);
    var rows = Find(widget.RenderSnapshot("queue-tail", 1).Root, "spotify.queue.scroll").Children;
    await widget.OnActionAsync(new(rows[1].ActionId!, rows[1].Id));
    Assert.SequenceEqual(new[] { "spotify:track:second", original.Uri }, harness.StartedPlayback.Single().ItemUris!);
    Assert.Equal<string?>(null, harness.StartedPlayback.Single().ContextUri);
    rows = Find(widget.RenderSnapshot("queue-tail", 2).Root, "spotify.queue.scroll").Children;
    Assert.Equal(QueuePlay(original.Uri), rows[0].ActionId);
    await widget.OnActionAsync(new(rows[0].ActionId!, rows[0].Id));
    Assert.Equal(SpotifyPlaybackOperation.Next, harness.Commands.Last().Operation);
    Assert.Equal(1, harness.StartedPlayback.Count);
    await StopAsync(widget);
}

static async Task TrackMenuRequestBudget()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness, search: true);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.search.query", "spotify.search.query") { CommittedText = "night" });
    await WaitUntil(() => harness.SearchCalls == 1);
    var snapshot = widget.RenderSnapshot("track-menu", 1);
    var row = CollectionRows(snapshot.Root)[0];
    Assert.Equal(ControllerButton.Menu, row.ContextMenuButton);
    Assert.Equal("Add to queue", row.ContextActions.Single().Label);
    var playbackCalls = harness.PlaybackCalls;
    await widget.OnActionAsync(new(row.ContextActions.Single().ActionId, row.Id));
    Assert.SequenceEqual(new[] { "spotify:track:result0" }, harness.QueuedUris);
    Assert.Equal(1, harness.SearchCalls);
    Assert.Equal(0, harness.QueueCalls);
    Assert.Equal(playbackCalls, harness.PlaybackCalls);
    Assert.Equal(0, harness.StartedPlayback.Count);
    var updated = widget.RenderSnapshot("track-menu", 2);
    Assert.Equal(0, ViewSnapshotValidator.Validate(updated).Count);
    Assert.NotNull(Find(updated.Root, "spotify.action-toast"));
    Assert.True(updated.QuickActions.Any(action => action.Button == ControllerButton.X && action.ActionId == "spotify.play-toggle"), "X changed meaning.");
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.playlists"));
    await WaitUntil(() => harness.PlaylistCalls == 1);
    await widget.OnActionAsync(new(PlaylistOpen("playlist-one"), PlaylistFocus("wide", "playlist-one")));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);
    row = CollectionRows(widget.RenderSnapshot("track-menu", 3).Root)[0];
    Assert.Equal(ControllerButton.Menu, row.ContextMenuButton);
    await widget.OnActionAsync(new(row.ContextActions.Single().ActionId, row.Id));
    Assert.Equal("spotify:track:next", harness.QueuedUris.Last());
    Assert.Equal(1, harness.PlaylistDetailCalls);
    await widget.OnActionAsync(new(row.ActionId!, row.Id));
    Assert.Equal("spotify:playlist:playlist-one", harness.StartedPlayback.Single().ContextUri);
    Assert.Equal("spotify:track:next", harness.StartedPlayback.Single().OffsetUri);
    await StopAsync(widget);
}

static async Task LocalStartSettlementBudget()
{
    var harness = SpotifyHarness.Ready();
    harness.Playback = harness.Playback with { IsAvailable = false, IsPlaying = false, Item = null };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.local.start", "spotify.local.start"));
    await WaitUntil(() => harness.PlaybackCalls == 2);
    await Task.Delay(TimeSpan.FromMilliseconds(8_100));
    Assert.Equal(5, harness.PlaybackCalls);
    Assert.Equal(1, harness.LocalCommands.Count);
    await StopAsync(widget);
}

static async Task LocalStartReconcilesIdle()
{
    var harness = SpotifyHarness.Ready();
    var playing = harness.Playback;
    harness.Playback = playing with { IsAvailable = false, IsPlaying = false, Item = null };
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.devices", "spotify.nav.devices"));
    await WaitUntil(() => harness.DeviceCalls == 1);
    var before = harness.PlaybackCalls;
    await widget.OnActionAsync(new("spotify.local.start", "spotify.local.start"));
    await WaitUntil(() => harness.PlaybackCalls == before + 1);
    harness.Playback = playing;
    await WaitUntil(() => widget.Playback?.IsPlaying == true, "Local transfer waited for idle polling.");
    Assert.Equal(before + 2, harness.PlaybackCalls);
    Assert.Equal(1, harness.LocalCommands.Count);
    await StopAsync(widget);
}

static async Task SearchResultsAndPlayback()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness, search: true);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    Assert.Equal(SpotifyDestination.Search, widget.Destination);
    Assert.Equal(0, harness.SearchCalls);
    var initial = widget.RenderSnapshot("spotify.search-test", 1);
    Assert.Equal("spotify.search.query", initial.InitialFocusId);
    Assert.Equal(0, ViewSnapshotValidator.Validate(initial).Count);
    await widget.OnActionAsync(new("spotify.search.query", "spotify.search.query") { CommittedText = "night" });
    await WaitUntil(() => CollectionRows(widget.RenderSnapshot("spotify.search-test", 2).Root).Length == 10);
    var first = CollectionRows(widget.RenderSnapshot("spotify.search-test", 3).Root)[0];
    await widget.OnActionAsync(new(first.ActionId!, first.Id));
    Assert.Equal("spotify:track:result0", harness.StartedPlayback.Single().ItemUris!.Single());
    await widget.OnActionAsync(new("spotify.search.cursor.after", "spotify.search.scroll"));
    await WaitUntil(() => CollectionRows(widget.RenderSnapshot("spotify.search-test", 4).Root).Length == 20,
        "Search cursor pagination was swallowed by authored action handling.");
    foreach (var kind in new[] { SpotifySearchKind.Album, SpotifySearchKind.Artist, SpotifySearchKind.Playlist })
    {
        await widget.OnActionAsync(new("spotify.search.type." + kind, "spotify.search.type"));
        await WaitUntil(() => CollectionRows(widget.RenderSnapshot("spotify.search-test", 5).Root).Length == 10);
        var snapshot = widget.RenderSnapshot("spotify.search-test", 6);
        Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var row = CollectionRows(snapshot.Root)[0];
        await widget.OnActionAsync(new(row.ActionId!, row.Id));
        Assert.Equal("spotify:" + kind.ToString().ToLowerInvariant() + ":result0", harness.StartedPlayback[^1].ContextUri);
    }
    var calls = harness.SearchCalls;
    await widget.OnActionAsync(new("spotify.search.clear", "spotify.search.clear"));
    Assert.Equal(0, CollectionRows(widget.RenderSnapshot("spotify.search-test", 7).Root).Length);
    Assert.Equal(calls, harness.SearchCalls);
    await StopAsync(widget);
}

static async Task SearchSectionRoundTrips()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness, search: true);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.search.query", "spotify.search.query") { CommittedText = "night" });
    await WaitUntil(() => CollectionRows(widget.RenderSnapshot("search.sections", 1).Root).Length == 10);
    long sequence = 2;
    foreach (var direction in new[] { "next", "previous" })
    {
        for (var step = 0; step < 4; ++step)
        {
            var before = widget.RenderSnapshot("search.sections", sequence++);
            await widget.OnActionAsync(new("spotify.nav." + direction + "-section", before.InitialFocusId!));
            var after = widget.RenderSnapshot("search.sections", sequence++);
            Assert.Equal(0, ViewSnapshotValidator.Validate(after).Count);
            var request = after.FocusGroupEntryRequest;
            Assert.NotNull(request);
            Assert.NotNull(Find(after.Root, request!.GroupId));
        }
        Assert.Equal(SpotifyDestination.Search, widget.Destination);
    }
    Assert.Equal(1, harness.SearchCalls);
    Assert.Equal(10, CollectionRows(widget.RenderSnapshot("search.sections", sequence).Root).Length);
    await StopAsync(widget);
}

static async Task SearchMutablePaging()
{
    var harness = SpotifyHarness.Ready();
    var fail = false;
    harness.SearchHandler = (_, kind, offset, limit, _) => fail
        ? ValueTask.FromException<SpotifySearchPage>(new SpotifyApplicationException("spotify_network_error", "Connection lost"))
        : ValueTask.FromResult(new SpotifySearchPage(Enumerable.Range(0, 10).Select(index =>
            new SpotifySearchItem(kind, "same" + index, "Result " + index, "Artist", null,
                "spotify:track:same" + index, "https://open.spotify.com/track/same" + index, true)).ToArray(),
            offset, limit, offset == 0 ? 100 : 90));
    var widget = await StartAsync(harness, search: true);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.search.query", "spotify.search.query") { CommittedText = "live rankings" });
    await WaitUntil(() => CollectionRows(widget.RenderSnapshot("search.mutable", 1).Root).Length == 10);
    await widget.OnActionAsync(new("spotify.search.cursor.after", "spotify.search.scroll"));
    await WaitUntil(() => CollectionRows(widget.RenderSnapshot("search.mutable", 2).Root).Length == 20);
    var ready = widget.RenderSnapshot("search.mutable", 3);
    var scroll = Find(ready.Root, "spotify.search.scroll");
    Assert.Equal<long?>(null, scroll.VirtualCollectionWindow!.TotalItemCount);
    Assert.Equal(20, CollectionRows(ready.Root).Select(row => row.CollectionItemKey).Distinct().Count());
    fail = true;
    await widget.OnActionAsync(new("spotify.search.cursor.after", "spotify.search.scroll"));
    await WaitUntil(() => ContainsId(widget.RenderSnapshot("search.mutable", 4).Root, "spotify.search.error"));
    var error = widget.RenderSnapshot("search.mutable", 5);
    var failedScroll = Find(error.Root, "spotify.search.scroll");
    Assert.Equal(20, CollectionRows(error.Root).Length);
    Assert.True(failedScroll.VirtualCollectionWindow!.RequestGeneration > scroll.VirtualCollectionWindow.RequestGeneration,
        "Search error must publish a forward metadata transition.");
    Assert.Equal(scroll.CollectionResetGeneration, failedScroll.CollectionResetGeneration);
    Assert.Equal(0, ViewSnapshotValidator.Validate(error).Count);
    fail = false;
    await widget.OnActionAsync(new("spotify.page.retry", "spotify.search.error.action"));
    await WaitUntil(() => CollectionRows(widget.RenderSnapshot("search.mutable", 6).Root).Length == 30);
    await StopAsync(widget);
}

static async Task SearchLateResponses()
{
    var pending = new TaskCompletionSource<SpotifySearchPage>(TaskCreationOptions.RunContinuationsAsynchronously);
    var harness = SpotifyHarness.Ready();
    harness.SearchHandler = (query, kind, offset, limit, token) => query == "old"
        ? new(pending.Task) : ValueTask.FromResult(new SpotifySearchPage(
            [new(kind, "new", "New result", "Artist", null, "spotify:track:new", "https://open.spotify.com/track/new", true)], 0, 10, 1));
    var widget = await StartAsync(harness, search: true);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.search.query", "spotify.search.query") { CommittedText = "old" });
    await WaitUntil(() => harness.SearchCalls == 1, "Old search did not start");
    await widget.OnActionAsync(new("spotify.search.query", "spotify.search.query") { CommittedText = "new" });
    await WaitUntil(() => CollectionRows(widget.RenderSnapshot("spotify.search-stale", 1).Root).Length == 1, "New search results did not replace the pending query");
    pending.SetResult(new SpotifySearchPage([], 0, 10, 0));
    await Task.Delay(50);
    var view = widget.RenderSnapshot("spotify.search-stale", 2);
    Assert.Equal(1, CollectionRows(view.Root).Length);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    Assert.Equal("new", Find(widget.RenderSnapshot("spotify.search-stale", 3).Root, "spotify.search.query").TextEntryValue);
    Assert.Equal(2, harness.SearchCalls);
    harness.SearchHandler = (_, _, _, _, _) => ValueTask.FromException<SpotifySearchPage>(new SpotifyApplicationException("search_unavailable", "Search is unavailable."));
    await widget.OnActionAsync(new("spotify.search.query", "spotify.search.query") { CommittedText = "failure" });
    await WaitUntil(() => ContainsId(widget.RenderSnapshot("spotify.search-stale", 4).Root, "spotify.search.error"), "Search failure did not show feedback");
    Assert.Equal(0, ViewSnapshotValidator.Validate(widget.RenderSnapshot("spotify.search-stale", 5)).Count);
    await StopAsync(widget);
}

static async Task<SpotifyWidget> StartAsync(
    SpotifyHarness harness,
    TimeProvider? timeProvider = null,
    ISpotifyRuntimeDiagnostics? diagnostics = null,
    bool productionPaging = false, bool search = false)
{
    // Small deterministic windows keep the boundary/eviction regressions concise.
    // A separate test exercises the actual production batching configuration.
    var widget = productionPaging
        ? new SpotifyWidget(harness, timeProvider, diagnostics ?? SpotifyRuntimeDiagnostics.None)
        : new SpotifyWidget(harness, timeProvider, diagnostics ?? SpotifyRuntimeDiagnostics.None, 12, 24);
    await WidgetTestHost.InitializeAsync(widget);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    // Existing scenarios exercise the Library route; Search has its own entry tests.
    if (!search && harness.Configured && harness.Connected && harness.ConfigurationError is null)
        await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.compact.playlists"));
    return widget;
}

static async Task StopAsync(SpotifyWidget widget)
{
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    await WidgetTestHost.DestroyAsync(widget);
}

static async Task WaitUntil(Func<bool> predicate, string? failure = null)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    try
    {
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
        throw new InvalidOperationException(failure ?? "Condition did not become true.");
    }
}

static OptimisticControlScenario[] OptimisticControlScenarios()
{
    var template = SpotifyHarness.Ready().Playback;
    return
    [
        new("pause", template with { IsPlaying = true },
            new WidgetActionEvent("spotify.play-toggle", "spotify.play-toggle"),
            "spotify.play-toggle", "True", "False", "True",
            playback => playback.IsPlaying.ToString(),
            observed => observed with { IsPlaying = true }),
        new("play", template with { IsPlaying = false },
            new WidgetActionEvent("spotify.play-toggle", "spotify.play-toggle"),
            "spotify.play-toggle", "False", "True", "False",
            playback => playback.IsPlaying.ToString(),
            observed => observed with { IsPlaying = false }),
        new("seek", template with { ProgressMilliseconds = 45_000 },
            new WidgetActionEvent("spotify.seek", "spotify.seek.slider",
                RequestedValue: 120_000),
            "spotify.seek.slider", "45000", "120000", "90000",
            playback => playback.ProgressMilliseconds.ToString(),
            observed => observed with { ProgressMilliseconds = 90_000 }),
        new("shuffle", template with { ShuffleState = false },
            new WidgetActionEvent("spotify.shuffle", "spotify.shuffle"),
            "spotify.shuffle", "False", "True", "False",
            playback => playback.ShuffleState.ToString(),
            observed => observed with { ShuffleState = false }),
        new("repeat", template with { RepeatState = SpotifyRepeatState.Off },
            new WidgetActionEvent("spotify.repeat", "spotify.repeat"),
            "spotify.repeat", "Off", "Context", "Off",
            playback => playback.RepeatState.ToString(),
            observed => observed with { RepeatState = SpotifyRepeatState.Off }),
    ];
}

static SpotifyOptimisticPlaybackReconciliation? OptimisticReconciliation(
    SpotifyWidget widget) =>
    (SpotifyOptimisticPlaybackReconciliation?)typeof(SpotifyWidget).GetField(
        "_optimisticReconciliation",
        System.Reflection.BindingFlags.Instance |
    System.Reflection.BindingFlags.NonPublic)!.GetValue(widget);

static Task<bool> RefreshPlaybackForTest(SpotifyWidget widget)
{
    var generation = (long)typeof(SpotifyWidget).GetField("_activeGeneration",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!.GetValue(widget)!;
    return (Task<bool>)typeof(SpotifyWidget).GetMethod("RefreshPlaybackAsync",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!.Invoke(
        widget, [generation, CancellationToken.None, true])!;
}

static long PendingOperationSequence(SpotifyWidget widget) =>
    (long)typeof(SpotifyWidget).GetField("_pendingOperationSequence",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!.GetValue(widget)!;

static void RestoreOptimisticForTest(
    SpotifyWidget widget,
    SpotifyPlaybackSummary playback,
    long operationSequence) =>
    typeof(SpotifyWidget).GetMethod("RestoreOptimistic",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!.Invoke(widget,
        [playback, operationSequence,
         new WidgetActionEvent("spotify.shuffle", "spotify.shuffle"), null]);

static Task WaitForNode(SpotifyWidget widget, string id) => WaitUntil(() =>
{
    try
    {
        Find(widget.RenderSnapshot("spotify.wait", 1).Root, id);
        return true;
    }
    catch (InvalidOperationException)
    {
        return false;
    }
});

static ViewNode Find(ViewNode node, string id)
{
    if (node.Id == id) return node;
    foreach (var child in node.Children)
    {
        try { return Find(child, id); }
        catch (InvalidOperationException) { }
    }
    throw new InvalidOperationException($"Node '{id}' was not found.");
}

static ViewNode? FindEnabledInScope(
    ViewNode node,
    string id,
    string? activeInputScopeId,
    string? inheritedInputScopeId = null)
{
    var inputScopeId = node.InputScopeId ?? inheritedInputScopeId;
    if (node.Id == id && inputScopeId == activeInputScopeId && node.IsDisabled is not true)
        return node;
    foreach (var child in node.Children)
        if (FindEnabledInScope(child, id, activeInputScopeId, inputScopeId) is { } match)
            return match;
    return null;
}

static ViewNode FindClass(ViewNode node, string value)
{
    if (node.StyleClasses.Contains(value)) return node;
    foreach (var child in node.Children)
        try { return FindClass(child, value); }
        catch (InvalidOperationException) { }
    throw new InvalidOperationException($"Missing class '{value}'.");
}

static ViewNode[] CollectionRows(ViewNode node)
{
    var rows = new List<ViewNode>();
    void Visit(ViewNode current)
    {
        if (current.CollectionItemKey is not null) rows.Add(current);
        foreach (var child in current.Children) Visit(child);
    }
    Visit(node);
    return rows.ToArray();
}

static int CountId(ViewNode node, string id) =>
    (node.Id == id ? 1 : 0) + node.Children.Sum(child => CountId(child, id));

static bool ContainsAction(ViewNode node, string actionId) =>
    node.ActionId == actionId ||
    node.Children.Any(child => ContainsAction(child, actionId));

static ViewNode FindPrefix(ViewNode node, string prefix)
{
    if (node.Id.StartsWith(prefix, StringComparison.Ordinal)) return node;
    foreach (var child in node.Children)
    {
        try { return FindPrefix(child, prefix); }
        catch (InvalidOperationException) { }
    }
    throw new InvalidOperationException($"Node prefix '{prefix}' was not found.");
}

static bool ContainsId(ViewNode node, string id) =>
    node.Id == id || node.Children.Any(child => ContainsId(child, id));

static bool ContainsText(ViewNode node, string text) =>
    string.Equals(node.Text, text, StringComparison.Ordinal) ||
    node.Children.Any(child => ContainsText(child, text));

static int CountOccurrences(string source, string value)
{
    var count = 0;
    var offset = 0;
    while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}

static bool ContainsTextFragment(ViewNode node, string text) =>
    (node.Text?.Contains(text, StringComparison.Ordinal) ?? false) ||
    node.Children.Any(child => ContainsTextFragment(child, text));

static string PlaylistFocus(string mode, string playlistId) =>
    $"spotify.playlist.item.{SharedMode(mode)}.playlist.{CollectionToken(playlistId)}";

static string TrackFocus(string mode, string uri) =>
    $"spotify.playlist.track.{SharedMode(mode)}.media.{CollectionToken(uri)}";

static string QueueFocus(string mode, string uri) =>
    $"spotify.queue.item.{SharedMode(mode)}.media.{CollectionToken(uri)}";

static string SharedMode(string mode) => mode is "wide" or "compact" ? "shared" : mode;

static string PlaylistOpen(string playlistId) =>
    $"spotify.playlist.open.playlist.{CollectionToken(playlistId)}";

static string PlaylistTrack(string uri) =>
    $"spotify.playlist.track.media.{CollectionToken(uri)}";

static string QueuePlay(string uri) =>
    $"spotify.queue.play.media.{CollectionToken(uri)}";

static string CollectionToken(string value) => Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 12)).ToLowerInvariant();

static void AssertShortcut(ViewNode root, ControllerButton button, string action)
{
    var shortcut = root.Shortcuts.Single(item => item.Button == button);
    Assert.Equal(action, shortcut.ActionId);
}

file sealed record OptimisticControlScenario(
    string Name,
    SpotifyPlaybackSummary Initial,
    WidgetActionEvent Action,
    string ControlId,
    string InitialValue,
    string ProjectedValue,
    string SuccessorValue,
    Func<SpotifyPlaybackSummary, string> OwnedValue,
    Func<SpotifyPlaybackSummary, SpotifyPlaybackSummary> Successor);

file sealed class SpotifyHarness : ISpotifyApplicationService
{
    public List<string> QueuedUris { get; } = [];
    public int SearchCalls { get; private set; }
    public Func<string, SpotifySearchKind, int, int, CancellationToken, ValueTask<SpotifySearchPage>>? SearchHandler { get; set; }
    public ValueTask<SpotifySearchPage> SearchAsync(string query, SpotifySearchKind kind, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        ++SearchCalls;
        if (SearchHandler is not null) return SearchHandler(query, kind, offset, limit, cancellationToken);
        var type = kind.ToString().ToLowerInvariant();
        return ValueTask.FromResult(new SpotifySearchPage(Enumerable.Range(offset, Math.Min(limit, 25-offset)).Select(index =>
            new SpotifySearchItem(kind, "result" + index, query + index, "Artist", null,
                "spotify:" + type + ":result" + index, "https://open.spotify.com/" + type + "/result" + index, true)).ToArray(), offset, limit, 25));
    }
    public bool Configured { get; set; } = true;
    public bool Connected { get; set; } = true;
    public Exception? ConfigurationError { get; set; }
    public Exception? ControlError { get; set; }
    public Exception? StartPlaybackError { get; set; }
    public Exception? QueueError { get; set; }
    public Task? ControlWait { get; set; }
    public TaskCompletionSource<SpotifyAuthorizationSummary>? ConnectCompletion { get; set; }
    public CancellationToken? ConnectCancellationToken { get; private set; }
    public int ConnectCalls { get; private set; }
    public int ConfigureClientCalls { get; private set; }
    public string? LastConfiguredClientId { get; private set; }
    public Func<string, CancellationToken, ValueTask<SpotifyConfigurationSummary>>?
        ConfigureClientHandler { get; set; }
    public int ConfigurationCalls { get; private set; }
    public int PlaybackCalls { get; private set; }
    public int QueueCalls { get; private set; }
    public int PlaylistCalls { get; private set; }
    public int PlaylistMetadataCalls { get; private set; }
    public int LastPlaylistLimit { get; private set; }
    public int PlaylistDetailCalls { get; private set; }
    public int DeviceCalls { get; private set; }
    public int LocalPlaybackCalls { get; private set; }
    public Func<CancellationToken, ValueTask<SpotifyPlaybackSummary>>?
        PlaybackHandler { get; set; }
    public Func<CancellationToken, ValueTask<SpotifyLocalPlaybackSummary>>?
        LocalPlaybackHandler { get; set; }
    public SpotifyLocalPlaybackSummary? LocalControlResult { get; set; }
    public IReadOnlyList<SpotifyAuthorizationScope>? LastScopes { get; private set; }
    public List<SpotifyPlaybackCommand> Commands { get; } = [];
    public List<SpotifyLocalPlaybackCommand> LocalCommands { get; } = [];
    public List<string> TransferredDevices { get; } = [];
    public List<StartSpotifyPlaybackRequest> StartedPlayback { get; } = [];
    public SpotifyPlaybackSummary Playback { get; set; } = PlaybackSnapshot();
    public SpotifyQueueSummary Queue { get; set; } = new(
        new SpotifyMediaItemSummary(SpotifyPlaybackItemType.Track,
            "Small Hours", "Northern Lines", 240_000,
            "https://i.scdn.co/image/current", "spotify:track:current",
            "https://open.spotify.com/track/current", true),
        [new SpotifyMediaItemSummary(SpotifyPlaybackItemType.Track,
            "Midnight Run", "Northern Lines", 201_000,
            "https://i.scdn.co/image/next", "spotify:track:next",
            "https://open.spotify.com/track/next", true)], false);
    public SpotifyPlaylistPageSummary Playlists { get; set; } = new(
        [new SpotifyPlaylistSummary("playlist-one", "Night Drive", "Late-night focus",
            "https://i.scdn.co/image/playlist", "https://open.spotify.com/playlist/playlist-one",
            "spotify:playlist:playlist-one", "Listener", false, true, 1)], 0, 50, 1);
    public TaskCompletionSource<SpotifyPlaylistPageSummary>?
        PlaylistCompletion { get; set; }
    public bool IgnorePlaylistCancellation { get; set; }
    public SpotifyPlaylistItemsPageSummary PlaylistDetail { get; set; } = new(
        [new SpotifyMediaItemSummary(SpotifyPlaybackItemType.Track,
            "Midnight Run", "Northern Lines", 201_000,
            "https://i.scdn.co/image/next", "spotify:track:next",
            "https://open.spotify.com/track/next", true)], 0, 50, 1);
    public Exception? PlaylistDetailError { get; set; }
    public TaskCompletionSource<SpotifyPlaylistItemsPageSummary>?
        PlaylistDetailCompletion { get; set; }
    public bool IgnorePlaylistDetailCancellation { get; set; }
    public Func<SpotifyPlaylistItemsRequest, CancellationToken,
        ValueTask<SpotifyPlaylistItemsPageSummary>>? PlaylistDetailHandler { get; set; }
    public List<SpotifyPlaylistItemsRequest> PlaylistDetailRequests { get; } = [];
    public SpotifyDevicesSummary Devices { get; set; } = new(
        [new SpotifyDeviceSummary("local-placeholder", "WidgetRail",
            "Computer", false, false, true, 60, true),
         new SpotifyDeviceSummary("remote-device", "Living Room", "Speaker",
            true, false, true, 45, false)]);
    public SpotifyLocalPlaybackSummary LocalPlayback { get; set; } = new(
        SpotifyLocalPlaybackState.Ready, "WidgetRail", 60, "Ready to play here");
    public async ValueTask<SpotifyConfigurationSummary> ConfigureClientAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigureClientCalls++;
        LastConfiguredClientId = clientId;
        if (ConfigureClientHandler is not null)
            return await ConfigureClientHandler(clientId, cancellationToken)
                .ConfigureAwait(false);
        Configured = true;
        Connected = false;
        return new SpotifyConfigurationSummary(
            true, SpotifyApplicationContract.ExactRedirectUri);
    }

    public ValueTask<SpotifyConfigurationSummary> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigurationCalls++;
        if (ConfigurationError is not null)
            return ValueTask.FromException<SpotifyConfigurationSummary>(ConfigurationError);
        return ValueTask.FromResult(new SpotifyConfigurationSummary(
            Configured, SpotifyApplicationContract.ExactRedirectUri));
    }

    public ValueTask<SpotifyAuthorizationSummary> GetAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = !Configured ? SpotifyAuthorizationState.Unconfigured :
            Connected ? SpotifyAuthorizationState.Connected :
            SpotifyAuthorizationState.Disconnected;
        return ValueTask.FromResult(new SpotifyAuthorizationSummary(
            state, [], Connected ?
            [SpotifyAuthorizationScope.PlaybackStateRead,
             SpotifyAuthorizationScope.PlaybackStateControl] : [], null));
    }

    public ValueTask<SpotifyAuthorizationSummary> DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Connected = false;
        return ValueTask.FromResult(new SpotifyAuthorizationSummary(
            SpotifyAuthorizationState.Disconnected, [], [], null));
    }

    public async ValueTask<SpotifyPlaybackSummary> GetPlaybackAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlaybackCalls++;
        return PlaybackHandler is null
            ? Playback
            : await PlaybackHandler(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<SpotifyQueueSummary> GetQueueAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QueueCalls++;
        if (QueueError is not null)
            return ValueTask.FromException<SpotifyQueueSummary>(QueueError);
        return ValueTask.FromResult(Queue);
    }

    public async ValueTask<SpotifyPlaylistPageSummary> GetPlaylistsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlaylistCalls++;
        LastPlaylistLimit = limit;
        var source = PlaylistCompletion is null
            ? Playlists
            : IgnorePlaylistCancellation
                ? await PlaylistCompletion.Task.ConfigureAwait(false)
                : await PlaylistCompletion.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
        return source with
        {
            Items = source.Items.Skip(offset).Take(limit).ToArray(),
            Offset = offset,
            Limit = limit,
        };
    }

    public ValueTask<SpotifyPlaylistSummary> GetPlaylistAsync(
        string playlistId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlaylistMetadataCalls++;
        var playlist = Playlists.Items.FirstOrDefault(item =>
            string.Equals(item.PlaylistId, playlistId, StringComparison.Ordinal));
        return playlist is null
            ? ValueTask.FromException<SpotifyPlaylistSummary>(
                new SpotifyApplicationException(
                    "spotify_not_found", "Spotify could not find this playlist"))
            : ValueTask.FromResult(playlist);
    }

    public async ValueTask<SpotifyPlaylistItemsPageSummary> GetPlaylistItemsAsync(
        string playlistId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlaylistDetailCalls++;
        var request = new SpotifyPlaylistItemsRequest(playlistId, offset, limit);
        PlaylistDetailRequests.Add(request);
        if (PlaylistDetailError is not null) throw PlaylistDetailError;
        var detail = PlaylistDetailHandler is not null
            ? await PlaylistDetailHandler(request, cancellationToken).ConfigureAwait(false)
            : PlaylistDetailCompletion is not null
                ? IgnorePlaylistDetailCancellation
                    ? await PlaylistDetailCompletion.Task.ConfigureAwait(false)
                    : await PlaylistDetailCompletion.Task.WaitAsync(cancellationToken)
                        .ConfigureAwait(false)
                : PlaylistDetail;
        return detail with
        {
            Items = detail.Items.Skip(offset).Take(limit).ToArray(),
            Offset = offset,
            Limit = limit,
        };
    }

    public ValueTask<SpotifyDevicesSummary> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeviceCalls++;
        return ValueTask.FromResult(Devices);
    }

    public ValueTask TransferPlaybackAsync(
        string deviceId,
        bool continuePlaying,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TransferredDevices.Add(deviceId);
        return ValueTask.CompletedTask;
    }

    public ValueTask StartPlaybackAsync(
        StartSpotifyPlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (StartPlaybackError is not null) throw StartPlaybackError;
        StartedPlayback.Add(request);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<SpotifyLocalPlaybackSummary> GetLocalPlaybackAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalPlaybackCalls++;
        return LocalPlaybackHandler is null
            ? LocalPlayback
            : await LocalPlaybackHandler(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<SpotifyLocalPlaybackSummary> ControlLocalPlaybackAsync(
        SpotifyLocalPlaybackCommand request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalCommands.Add(request);
        LocalPlayback = LocalControlResult ??
            (request.Operation == SpotifyLocalPlaybackOperation.Stop
                ? LocalPlayback with { State = SpotifyLocalPlaybackState.Disabled }
                : LocalPlayback with { State = SpotifyLocalPlaybackState.Active });
        return ValueTask.FromResult(LocalPlayback);
    }

    public ValueTask AddToQueueAsync(
        string uri,
        string? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QueuedUris.Add(uri);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static SpotifyHarness Ready(long capturedAt = 0, long progress = 45_000) => new()
    {
        Configured = true,
        Connected = true,
        Playback = PlaybackSnapshot(capturedAt, progress),
    };

    public async ValueTask<SpotifyAuthorizationSummary> ConnectAsync(
        IReadOnlyCollection<SpotifyAuthorizationScope> requestedScopes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectCalls++;
        LastScopes = requestedScopes.ToArray();
        ConnectCancellationToken = cancellationToken;
        if (ConnectCompletion is not null)
        {
            var result = await ConnectCompletion.Task.WaitAsync(cancellationToken);
            Connected = result.State == SpotifyAuthorizationState.Connected;
            return result;
        }
        Connected = true;
        return new SpotifyAuthorizationSummary(
            SpotifyAuthorizationState.Connected, requestedScopes.ToArray(),
            requestedScopes.ToArray(), "Connected");
    }

    public async ValueTask ControlPlaybackAsync(
        SpotifyPlaybackCommand request, CancellationToken cancellationToken)
    {
        Commands.Add(request);
        if (ControlError is not null) throw ControlError;
        if (ControlWait is not null) await ControlWait.WaitAsync(cancellationToken);
        Playback = request.Operation switch
        {
            SpotifyPlaybackOperation.Play => Playback with { IsPlaying = true },
            SpotifyPlaybackOperation.Pause => Playback with { IsPlaying = false },
            SpotifyPlaybackOperation.SetShuffle => Playback with
                { ShuffleState = request.Enabled!.Value },
            SpotifyPlaybackOperation.SetRepeat => Playback with
                { RepeatState = request.RepeatState!.Value },
            SpotifyPlaybackOperation.Seek => Playback with
                { ProgressMilliseconds = request.PositionMilliseconds!.Value },
            _ => Playback,
        };
    }

    private static SpotifyPlaybackSummary PlaybackSnapshot(
        long capturedAt = 0, long progress = 45_000) => new(
        true, true, progress, 240_000, capturedAt,
        SpotifyRepeatState.Off, false,
        new SpotifyPlaybackItemSummary(
            SpotifyPlaybackItemType.Track, "Small Hours", "Northern Lines",
            "Night Drive", "https://i.scdn.co/image/example", "spotify:track:test"),
        new SpotifyPlaybackDisallowedActions(
            false, false, false, false, false, false, false, false),
        "Spotify");
}

file sealed record SpotifyDiagnosticObservation(
    string Boundary,
    string Code,
    long Operation,
    long Generation,
    long ElapsedMilliseconds);

internal sealed class SpotifyPresentationHandleFixture : Widget
{
    internal SpotifyPresentationHandleFixture()
    {
        Compact = CreatePinnedLayoutHandle(
            SpotifyPresentation.CompactPinnedLayoutId,
            SpotifyPresentation.CompactPinnedLayoutName,
            SpotifyPresentation.CompactPinnedSurface,
            activeInputScopeId: SpotifyPresentation.CompactPinnedScope);
        UpNext = CreatePinnedLayoutHandle(
            SpotifyPresentation.UpNextPinnedLayoutId,
            SpotifyPresentation.UpNextPinnedLayoutName,
            SpotifyPresentation.UpNextPinnedSurface,
            activeInputScopeId: SpotifyPresentation.UpNextPinnedScope);
    }

    internal PinnedLayoutHandle Compact { get; }
    internal PinnedLayoutHandle UpNext { get; }

    public override WidgetView Render() => new(UI.Text("Fixture", "fixture.root"));
}

file sealed class RecordingSpotifyDiagnostics : ISpotifyRuntimeDiagnostics
{
    private readonly object _gate = new();
    private readonly List<SpotifyDiagnosticObservation> _observations = [];
    private readonly List<string> _encodedLines = [];

    public void Record(
        string boundary,
        string code,
        long operation = 0,
        long generation = 0,
        long elapsedMilliseconds = 0)
    {
        if (!SpotifyRuntimeDiagnostics.TryEncode(
                new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
                boundary, code, operation, generation, elapsedMilliseconds,
                out var line))
            return;
        lock (_gate)
        {
            _observations.Add(new(
                boundary, code, operation, generation, elapsedMilliseconds));
            _encodedLines.Add(line);
        }
    }

    public bool Contains(string boundary, string code, long operation)
    {
        lock (_gate) return _observations.Any(item =>
            item.Boundary == boundary && item.Code == code && item.Operation == operation);
    }

    public bool ContainsAny(string boundary, string code)
    {
        lock (_gate) return _observations.Any(item =>
            item.Boundary == boundary && item.Code == code);
    }

    public IReadOnlyList<SpotifyDiagnosticObservation> ForOperation(long operation)
    {
        lock (_gate) return _observations
            .Where(item => item.Operation == operation)
            .ToArray();
    }

    public IReadOnlyList<string> EncodedLines
    {
        get
        {
            lock (_gate) return _encodedLines.ToArray();
        }
    }
}

file sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset _now = initial;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void NotNull(object? value)
    {
        if (value is null) throw new InvalidOperationException("Expected a value.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Sequences differ.");
    }

    public static void Ordered(
        IReadOnlyList<SpotifyDiagnosticObservation> actual,
        params (string Boundary, string Code)[] expected)
    {
        var index = 0;
        foreach (var item in actual)
        {
            if (index < expected.Length &&
                item.Boundary == expected[index].Boundary &&
                item.Code == expected[index].Code)
                index++;
        }
        if (index != expected.Length)
            throw new InvalidOperationException(
                $"Expected diagnostic {expected[index].Boundary}:{expected[index].Code} " +
                $"at ordered position {index}.");
    }
}
