using GameBarAlternative.Samples.SpotifyWidget;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Opening the widget never starts OAuth", OpeningNeverConnects),
    ("Disconnected copy and paired actions remain bounded and centered", DisconnectedLayoutContract),
    ("Primary actions keep theme-safe fill and focus contrast", PrimaryActionContrast),
    ("Unconfigured state provides safe exact setup guidance", UnconfiguredSetup),
    ("Setup is a nested B-dismissible input scope", NestedSetupBack),
    ("Setup uses a bounded controller-native scroll surface", SetupUsesVerticalScroll),
    ("Reopening setup starts a fresh scroll entry", SetupReopenResetsScrollIdentity),
    ("Setup code and text styles remain compact and bounded", SetupCodeAndTextAreBounded),
    ("Setup check refreshes newly saved configuration without starting OAuth", SetupCheckRefreshesConfiguration),
    ("Connect action acknowledges while OAuth remains pending", ConnectAcknowledgesWhilePending),
    ("OAuth survives Background and reconciles when visible", ConnectSurvivesBackground),
    ("Explicit connect requests the four implemented least-privilege scopes", ExplicitConnect),
    ("Ready UI exposes native controller transport and attribution", ReadyControllerUi),
    ("Ready UI publishes responsive wide and compact navigation", ResponsiveNavigation),
    ("Collection pages load lazily and remain cached", LazyPageLoading),
    ("Playlist pages load automatically in bounded cached windows", MaximumPlaylistPageContract),
    ("Controller edges traverse compact and expanded 12/12/5 playlist pages", ControllerPlaylistPaginationRoundTrip),
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
    ("Devices expose trusted local playback and safe transfer actions", DeviceActions),
    ("Missing playback device produces actionable guidance", MissingPlaybackDeviceGuidance),
    ("Progress is projected locally without provider polling", ProjectedProgress),
    ("Playback actions publish optimistic state and reconcile", OptimisticPlayback),
    ("Failed controls roll back optimistic state", FailedControlRollback),
    ("Permission denial remains an actionable UI state", PermissionDenied),
    ("Manifest declares least-privilege partial consent", ManifestContract),
    ("Time labels are stable", TimeFormatting),
};

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

    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "styles", "default.gbss"));
    var parsed = GbssParser.Parse(source, "styles/default.gbss");
    Assert.True(parsed.IsValid, string.Join(Environment.NewLine, parsed.Diagnostics));
    Assert.True(source.Contains(".spotify-primary { background: var(--accent);", StringComparison.Ordinal),
        "Primary actions must derive their fill from the active theme accent.");
    Assert.True(source.Contains("outline-color: var(--focus)", StringComparison.Ordinal),
        "Focused primary actions must derive their outline from the active theme focus token.");
    var compiled = GbssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    var theme = compiled.Theme!;
    var detailStyle = theme.Resolve(new GbssElement(
        "text", StyleClasses: new HashSet<string>(["spotify-state-detail"])))!;
    Assert.Equal("100%", detailStyle.Get("width")?.Text);
    Assert.Equal("0px", detailStyle.Get("min-width")?.Text);
    Assert.Equal("560px", detailStyle.Get("max-width")?.Text);
    Assert.Equal("0", detailStyle.Get("flex-shrink")?.Text);
    Assert.Equal<string?>(null, detailStyle.Get("max-lines")?.Text);
    Assert.Equal("1.35", detailStyle.Get("line-height")?.Text);
    Assert.Equal("center", detailStyle.Get("text-align")?.Text);

    var actionRow = theme.Resolve(new GbssElement(
        "row", StyleClasses: new HashSet<string>(["spotify-connect-actions"])))!;
    Assert.Equal("100%", actionRow.Get("width")?.Text);
    Assert.Equal("wrap", actionRow.Get("flex-wrap")?.Text);
    Assert.Equal("0", actionRow.Get("flex-shrink")?.Text);
    var button = theme.Resolve(new GbssElement(
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
        AppContext.BaseDirectory, "styles", "default.gbss"));
    var parsed = GbssParser.Parse(source, "styles/default.gbss");
    Assert.True(parsed.IsValid, string.Join(Environment.NewLine, parsed.Diagnostics));
    var compiled = GbssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    var classes = new HashSet<string>(["spotify-primary"]);
    var normal = compiled.Theme!.Resolve(new GbssElement("button", null, classes, null))!;
    var focused = compiled.Theme.Resolve(new GbssElement(
        "button", null, classes,
        new HashSet<GbssPseudoState> { GbssPseudoState.Focused }))!;
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
        WidgetSpotifyService.ExactRedirectUri, StringComparison.Ordinal),
        "Exact redirect URI was not rendered.");
    Assert.True(Find(setup.Root, "spotify.setup-command").Text!.Contains(
        @"dotnet run --project .\tools\GbarCli\GbarCli.csproj -- config set",
        StringComparison.Ordinal),
        "Setup assumed that gbar was already installed on PATH.");
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
    Assert.Equal("spotify.setup.done", setup.InitialFocusId);
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
    Assert.Equal("spotify.setup", scroll.InputScopeId);
    AssertShortcut(scroll, ControllerButton.B, "spotify.setup.close");
    Assert.NotNull(Find(scroll, "spotify.setup-step-1"));
    Assert.NotNull(Find(scroll, "spotify.setup-step-2"));
    Assert.NotNull(Find(scroll, "spotify.setup-step-3"));
    var command = Find(scroll, "spotify.setup-command");
    Assert.True(command.StyleClasses.Contains("gbar-code-text"),
        "Setup command no longer uses the semantic CodeText component.");
    Assert.True(command.StyleClasses.Contains("spotify-setup-command"),
        "Setup command lost its bounded widget style class.");
    var card = Find(scroll, "spotify.setup-card");
    Assert.Equal("spotify.setup.done", card.Children[1].Id);
    Assert.Equal("Check configuration", card.Children[1].Text);
    Assert.Equal("spotify.setup.done", setup.InitialFocusId);
    Assert.True(setup.Surface?.MinimumHeight <= 404,
        "Setup requires a surface taller than the compact widget viewport.");
    await StopAsync(widget);
}

static Task SetupCodeAndTextAreBounded()
{
    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "styles", "default.gbss"));
    var parsed = GbssParser.Parse(source, "styles/default.gbss");
    Assert.True(parsed.IsValid, string.Join(Environment.NewLine, parsed.Diagnostics));
    var compiled = GbssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));

    var step = compiled.Theme!.Resolve(new GbssElement(
        "text", StyleClasses: new HashSet<string>(["spotify-setup-step"])))!;
    var command = compiled.Theme.Resolve(new GbssElement(
        "text", StyleClasses: new HashSet<string>(
            ["gbar-code-text", "spotify-setup-command"])))!;
    Assert.Equal<string?>(null, step.Get("max-lines")?.Text);
    Assert.Equal("1.3", step.Get("line-height")?.Text);
    Assert.Equal<string?>(null, command.Get("max-lines")?.Text);
    Assert.Equal("1.25", command.Get("line-height")?.Text);
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

static TaskCompletionSource<WidgetSpotifyAuthorizationSummary> NewAuthorizationCompletion() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

static WidgetSpotifyAuthorizationSummary ConnectedAuthorization() => new(
    WidgetSpotifyAuthorizationState.Connected,
    [WidgetSpotifyAuthorizationScope.PlaybackStateRead,
     WidgetSpotifyAuthorizationScope.PlaybackStateControl],
    [WidgetSpotifyAuthorizationScope.PlaybackStateRead,
     WidgetSpotifyAuthorizationScope.PlaybackStateControl],
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
        WidgetSpotifyAuthorizationScope.PlaybackStateRead,
        WidgetSpotifyAuthorizationScope.PlaybackStateControl,
        WidgetSpotifyAuthorizationScope.LocalPlayback,
        WidgetSpotifyAuthorizationScope.PlaylistsRead,
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
    Assert.Equal("spotify.play-toggle", snapshot.InitialFocusId);
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
    Assert.True(snapshot.QuickActions.All(action => action.Capability?.CapabilityId ==
        WidgetSpotifyCapabilities.PlaybackControlCapabilityId),
        "Quick actions bypass the public playback-control authority.");
    await StopAsync(widget);
}

static async Task ResponsiveNavigation()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    var snapshot = widget.RenderSnapshot("spotify.responsive", 1);
    Assert.Equal(ProtocolConstants.SliderActivationVersion, snapshot.ProtocolVersion);
    Assert.Equal(ResponsiveVisibility.ExpandedOnly,
        Find(snapshot.Root, "spotify.shell.wide").VisibleWhen);
    Assert.Equal(ResponsiveVisibility.CompactOnly,
        Find(snapshot.Root, "spotify.shell.compact").VisibleWhen);
    Assert.True(Find(snapshot.Root, "spotify.nav.wide.player").IsSelected == true,
        "Wide player destination was not selected.");
    Assert.True(Find(snapshot.Root, "spotify.nav.compact.player").IsSelected == true,
        "Compact player destination was not selected.");
    var compactPlayerScroll = Find(snapshot.Root, "spotify.player.compact.scroll");
    Assert.Equal(ViewNodeKind.Scroll, compactPlayerScroll.Kind);
    Assert.NotNull(Find(compactPlayerScroll, "spotify.player.compact.play-toggle"));
    await StopAsync(widget);
}

static async Task LazyPageLoading()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    Assert.Equal(0, harness.QueueCalls);
    Assert.Equal(0, harness.PlaylistCalls);

    await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.wide.queue"));
    Assert.Equal(1, harness.QueueCalls);
    var queueSnapshot = widget.RenderSnapshot("spotify.queue", 1);
    Assert.NotNull(Find(queueSnapshot.Root, "spotify.queue.item.wide.0"));
    Assert.Equal("3:21", Find(queueSnapshot.Root,
        "spotify.queue.item.wide.0.state").Text);
    Assert.True(!ContainsId(queueSnapshot.Root,
        "spotify.queue.item.wide.0.metadata"),
        "Queue item rendered a redundant fourth text row.");
    await widget.OnActionAsync(new("spotify.nav.player", "spotify.nav.wide.player"));
    await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.wide.queue"));
    Assert.Equal(1, harness.QueueCalls);

    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    Assert.Equal(1, harness.PlaylistCalls);
    Assert.NotNull(Find(widget.RenderSnapshot("spotify.playlists", 1).Root,
        "spotify.playlist.item.wide.0"));
    await StopAsync(widget);
}

static async Task PlaylistDetailBack()
{
    var harness = SpotifyHarness.Ready();
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new("spotify.playlist.open.0", "spotify.playlist.item.compact.0"));
    var detail = widget.RenderSnapshot("spotify.playlist.detail", 1);
    Assert.NotNull(Find(detail.Root, "spotify.playlist.track.wide.0"));
    Assert.Equal("3:21", Find(detail.Root,
        "spotify.playlist.track.wide.0.state").Text);
    Assert.True(!ContainsId(detail.Root,
        "spotify.playlist.track.wide.0.metadata"),
        "Playlist track rendered a redundant fourth text row.");
    AssertShortcut(detail.Root, ControllerButton.B, "spotify.playlist.back");
    Assert.Equal("spotify.playlist.play.compact", detail.InitialFocusId);
    await widget.OnActionAsync(new("spotify.playlist.back", "spotify.playlist.play.compact"));
    var list = widget.RenderSnapshot("spotify.playlist.list", 2);
    Assert.NotNull(Find(list.Root, "spotify.playlist.item.wide.0"));
    Assert.Equal("spotify.playlist.item.compact.0", list.InitialFocusId);
    Assert.True(!list.Root.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.B),
        "Playlist root unexpectedly consumed B instead of returning to the tray.");
    await StopAsync(widget);
}

static async Task PlaylistDetailFailureRetry()
{
    var harness = SpotifyHarness.Ready();
    harness.PlaylistDetailError = new WidgetCapabilityException(
        "spotify_unavailable", "Spotify could not load this playlist");
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new("spotify.playlist.open.0", "spotify.playlist.item.compact.0"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);

    var failure = widget.RenderSnapshot("spotify.playlist.failure", 1);
    Assert.Equal("spotify.page.error.compact.action", failure.InitialFocusId);
    Assert.NotNull(Find(failure.Root, failure.InitialFocusId!));

    harness.PlaylistDetailError = null;
    await widget.OnActionAsync(new("spotify.page.retry", failure.InitialFocusId!));
    await WaitUntil(() => harness.PlaylistDetailCalls == 2);
    await WaitUntil(() => widget.Render().InitialFocusId == "spotify.playlist.play.compact");
    var recovered = widget.RenderSnapshot("spotify.playlist.recovered", 2);
    Assert.NotNull(Find(recovered.Root, "spotify.playlist.track.compact.0"));
    Assert.Equal("spotify.playlist.play.compact", recovered.InitialFocusId);
    await StopAsync(widget);
}

static async Task SlowPlaylistDetailBack()
{
    var completion = new TaskCompletionSource<WidgetSpotifyPlaylistItemsSummary>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var harness = SpotifyHarness.Ready();
    harness.PlaylistDetailCompletion = completion;
    harness.IgnorePlaylistDetailCancellation = true;
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));

    var acknowledgement = widget.OnActionAsync(new(
        "spotify.playlist.open.0", "spotify.playlist.item.wide.0")).AsTask();
    await acknowledgement.WaitAsync(TimeSpan.FromMilliseconds(250));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);
    await widget.OnActionAsync(new("spotify.playlist.back", "spotify.playlist.item.wide.0"));
    completion.SetResult(harness.PlaylistDetail);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

    var list = widget.RenderSnapshot("spotify.playlist.cancelled", 1);
    Assert.NotNull(Find(list.Root, "spotify.playlist.item.wide.0"));
    Assert.Equal("spotify.playlist.item.wide.0", list.InitialFocusId);
    await WidgetTestHost.DestroyAsync(widget);
}

static async Task SupersededPlaylistDetail()
{
    foreach (var failLateRequest in new[] { false, true })
    {
        var playlistA = Playlist("playlist-a", "Playlist A");
        var playlistB = Playlist("playlist-b", "Playlist B");
        var detailA = PlaylistDetail(playlistA, "Track A", "spotify:track:a");
        var detailB = PlaylistDetail(playlistB, "Track B", "spotify:track:b");
        var aStarted = NewSignal();
        var aCompletion = new TaskCompletionSource<WidgetSpotifyPlaylistItemsSummary>(
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
        await widget.OnActionAsync(new("spotify.playlist.open.0", "spotify.playlist.item.wide.0"));
        await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await widget.OnActionAsync(new("spotify.playlist.open.1", "spotify.playlist.item.compact.1"));
        var selectingB = widget.RenderSnapshot("spotify.playlist-b-loading", 1);
        Assert.True(ContainsText(selectingB.Root, "Loading Playlist B"),
            "The replacement selection did not publish its own loading state.");
        Assert.True(!ContainsText(selectingB.Root, "Track A"),
            "Playlist A remained visible after selecting Playlist B.");

        if (failLateRequest)
            aCompletion.SetException(new WidgetCapabilityException(
                "spotify_unavailable", "Late A failure"));
        else
            aCompletion.SetResult(detailA);
        await WaitUntil(() => harness.PlaylistDetailRequests.Count == 2);
        await WaitForNode(widget, "spotify.playlist.track.wide.0.title");
        var selectedB = widget.RenderSnapshot("spotify.playlist-b", 1);
        Assert.Equal("Playlist B", Find(selectedB.Root,
            "spotify.playlist.detail.header.wide.title").Text);
        Assert.Equal("Track B", Find(selectedB.Root,
            "spotify.playlist.track.wide.0.title").Text);
        Assert.Equal("spotify.playlist.play.compact", selectedB.InitialFocusId);

        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

        var afterLateA = widget.RenderSnapshot("spotify.playlist-b-after-a", 2);
        Assert.Equal("Playlist B", Find(afterLateA.Root,
            "spotify.playlist.detail.header.wide.title").Text);
        Assert.Equal("Track B", Find(afterLateA.Root,
            "spotify.playlist.track.wide.0.title").Text);
        Assert.True(!ContainsText(afterLateA.Root, "Track A"),
            "A superseded playlist result was rendered under Playlist B.");
        Assert.Equal(2, harness.PlaylistDetailRequests.Count);
        await StopAsync(widget);
    }
}

static async Task PlaylistDetailLifecycle()
{
    var playlist = Playlist("playlist-a", "Playlist A");
    var stale = PlaylistDetail(playlist, "Stale Track", "spotify:track:stale");
    var fresh = PlaylistDetail(playlist, "Fresh Track", "spotify:track:fresh");
    var firstStarted = NewSignal();
    var firstCancelled = NewSignal();
    var firstCompletion = new TaskCompletionSource<WidgetSpotifyPlaylistItemsSummary>(
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
    await widget.OnActionAsync(new("spotify.playlist.open.0", "spotify.playlist.item.wide.0"));
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
    await WaitForNode(widget, "spotify.playlist.track.wide.0.title");
    var reactivated = widget.RenderSnapshot("spotify.playlist-reactivated", 1);
    Assert.Equal("Playlist A", Find(reactivated.Root,
        "spotify.playlist.detail.header.wide.title").Text);
    Assert.Equal("Fresh Track", Find(reactivated.Root,
        "spotify.playlist.track.wide.0.title").Text);
    Assert.True(!ContainsText(reactivated.Root, "Stale Track"),
        "The stale detail result survived reactivation.");
    await StopAsync(widget);
}

static async Task RefreshPreservesNewerPlaylist()
{
    var playlistA = Playlist("playlist-a", "Playlist A");
    var playlistB = Playlist("playlist-b", "Playlist B");
    var detailA = PlaylistDetail(playlistA, "Track A", "spotify:track:a");
    var detailB = PlaylistDetail(playlistB, "Track B", "spotify:track:b");
    var harness = SpotifyHarness.Ready();
    harness.Playlists = new([playlistA, playlistB], 0, 50, 2);
    harness.PlaylistDetailHandler = (request, _) => ValueTask.FromResult(
        request.PlaylistId == playlistA.PlaylistId ? detailA : detailB);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new("spotify.playlist.open.0", "spotify.playlist.item.wide.0"));
    await WaitUntil(() => harness.PlaylistDetailRequests.Count == 1);

    var refreshStarted = NewSignal();
    var refreshCompletion = new TaskCompletionSource<WidgetSpotifyPlaybackSummary>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    harness.PlaybackHandler = async cancellationToken =>
    {
        refreshStarted.TrySetResult();
        return await refreshCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    };
    var refresh = widget.OnActionAsync(new("spotify.refresh", "spotify.refresh.wide")).AsTask();
    await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await widget.OnActionAsync(new("spotify.playlist.open.1", "spotify.playlist.item.compact.1"));
    await WaitUntil(() => harness.PlaylistDetailRequests.Count == 2);
    await WaitForNode(widget, "spotify.playlist.track.wide.0.title");

    harness.PlaybackHandler = null;
    refreshCompletion.SetResult(harness.Playback);
    await refresh.WaitAsync(TimeSpan.FromSeconds(1));
    var current = widget.RenderSnapshot("spotify.refresh-newer-playlist", 1);
    Assert.Equal("Playlist B", Find(current.Root,
        "spotify.playlist.detail.header.wide.title").Text);
    Assert.Equal("Track B", Find(current.Root,
        "spotify.playlist.track.wide.0.title").Text);
    Assert.Equal("spotify.playlist.play.compact", current.InitialFocusId);
    Assert.True(!ContainsText(current.Root, "Track A"),
        "The refresh restored an older playlist-detail revision.");
    await StopAsync(widget);
}

static TaskCompletionSource NewSignal() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

static WidgetSpotifyPlaylistSummary Playlist(string id, string name) => new(
    id, name, $"{name} description", null,
    $"https://open.spotify.com/playlist/{id}", $"spotify:playlist:{id}",
    "Listener", false, true, 1);

static WidgetSpotifyPlaylistItemsSummary PlaylistDetail(
    WidgetSpotifyPlaylistSummary playlist,
    string trackName,
    string uri) => new(
    playlist,
    [new WidgetSpotifyMediaItemSummary(WidgetSpotifyPlaybackItemType.Track,
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
    await Task.Delay(TimeSpan.FromMilliseconds(2_250));
    Assert.Equal(configurationCalls, harness.ConfigurationCalls);
    Assert.True(harness.PlaybackCalls > playbackCalls,
        "Adaptive playback polling did not refresh live state.");
    await StopAsync(widget);
}

static async Task MaximumPlaylistPageContract()
{
    var playlists = Enumerable.Range(0, WidgetSpotifyService.MaximumCollectionPageSize)
        .Select(index => new WidgetSpotifyPlaylistSummary(
            $"playlist-{index}", $"Playlist {index}", $"Description {index}",
            $"https://i.scdn.co/image/playlist-{index}",
            $"https://open.spotify.com/playlist/playlist-{index}",
            $"spotify:playlist:playlist-{index}", "Listener", false, true,
            WidgetSpotifyService.MaximumCollectionPageSize))
        .ToArray();
    var tracks = Enumerable.Range(0, WidgetSpotifyService.MaximumCollectionPageSize)
        .Select(index => new WidgetSpotifyMediaItemSummary(
            WidgetSpotifyPlaybackItemType.Track, $"Track {index}", $"Artist {index}",
            180_000, $"https://i.scdn.co/image/track-{index}",
            $"spotify:track:track-{index}",
            $"https://open.spotify.com/track/track-{index}", true))
        .ToArray();
    var harness = SpotifyHarness.Ready();
    harness.Playlists = new(playlists, 0, playlists.Length, playlists.Length);
    harness.PlaylistDetail = new(playlists[0], tracks, 0, tracks.Length, tracks.Length);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);

    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await WaitUntil(() => harness.PlaylistCalls == 1);
    var firstPage = widget.RenderSnapshot("spotify.maximum-playlists", 1);
    Assert.Equal(ProtocolConstants.ScrollPaginationVersion, firstPage.ProtocolVersion);
    var playlistScroll = Find(firstPage.Root, "spotify.playlists.scroll.wide");
    Assert.Equal(12, playlistScroll.Children.Count);
    Assert.Equal("spotify.playlists.page.next", playlistScroll.ScrollNearEndActionId);
    Assert.True(playlistScroll.ScrollNearStartActionId is null,
        "The first page must not request a previous page.");
    Assert.True(SnapshotJson.Serialize(firstPage).Length < 400_000,
        "A bounded playlist snapshot must remain comfortably below the bridge limit.");

    await widget.OnActionAsync(new(
        "spotify.playlists.page.next", "spotify.playlists.scroll.wide"));
    await WaitUntil(() => harness.PlaylistCalls == 2);
    var secondPage = widget.RenderSnapshot("spotify.second-playlists", 2);
    Assert.NotNull(Find(secondPage.Root, "spotify.playlist.item.wide.12"));
    Assert.Equal("spotify.playlist.item.wide.12", secondPage.InitialFocusId);
    await widget.OnActionAsync(new(
        "spotify.playlists.page.previous", "spotify.playlists.scroll.wide"));
    Assert.Equal(2, harness.PlaylistCalls);
    var cachedFirstPage = widget.RenderSnapshot("spotify.cached-playlists", 3);
    Assert.NotNull(Find(cachedFirstPage.Root, "spotify.playlist.item.wide.0"));
    Assert.Equal("spotify.playlist.item.wide.11", cachedFirstPage.InitialFocusId);

    await widget.OnActionAsync(new("spotify.playlist.open.0", "spotify.playlist.item.wide.0"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);
    var detailPage = widget.RenderSnapshot("spotify.maximum-playlist-detail", 4);
    var trackScroll = Find(detailPage.Root, "spotify.playlist.detail.scroll.wide");
    Assert.Equal(12, trackScroll.Children.Count);
    Assert.Equal("spotify.playlist.items.page.next", trackScroll.ScrollNearEndActionId);
    Assert.True(SnapshotJson.Serialize(detailPage).Length < 400_000,
        "A bounded playlist-detail snapshot must remain comfortably below the bridge limit.");
    await widget.OnActionAsync(new("spotify.playlist.track.0", "spotify.playlist.track.wide.0"));
    Assert.Equal(1, harness.StartedPlayback.Count);
    Assert.Equal("spotify:track:track-0", harness.StartedPlayback[0].OffsetUri);
    await StopAsync(widget);
}

static async Task ControllerPlaylistPaginationRoundTrip()
{
    foreach (var mode in new[] { "wide", "compact" })
    {
        var playlists = Enumerable.Range(0, 29)
            .Select(index => new WidgetSpotifyPlaylistSummary(
                $"playlist-{index}", $"Playlist {index}", null, null,
                $"https://open.spotify.com/playlist/playlist-{index}",
                $"spotify:playlist:playlist-{index}", "Listener", false, true, 1))
            .ToArray();
        var tracks = Enumerable.Range(0, 29)
            .Select(index => new WidgetSpotifyMediaItemSummary(
                WidgetSpotifyPlaybackItemType.Track, $"Track {index}", $"Artist {index}",
                180_000, null, $"spotify:track:track-{index}",
                $"https://open.spotify.com/track/track-{index}", true))
            .ToArray();
        var harness = SpotifyHarness.Ready();
        harness.Playlists = new(playlists, 0, 12, playlists.Length);
        harness.PlaylistDetail = new(
            playlists[0], tracks, 0, 12, tracks.Length);
        var widget = await StartAsync(harness);
        await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
        await widget.OnActionAsync(new(
            "spotify.nav.playlists", $"spotify.nav.{mode}.playlists"));
        await WaitUntil(() => harness.PlaylistCalls == 1);

        var focus = $"spotify.playlist.item.{mode}.0";
        long sequence = 1;
        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await PressPlaylistDirectionAsync(
                widget, mode, focus, sequence, down: true);
        Assert.Equal($"spotify.playlist.item.{mode}.11", focus);
        bool paged;
        if (mode == "wide")
        {
            harness.PlaylistCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.IgnorePlaylistCancellation = true;
            var slowPage = PressPlaylistDirectionAsync(
                widget, mode, focus, sequence, down: true);
            await WaitUntil(() => harness.PlaylistCalls == 2);
            var repeatedPage = PressPlaylistDirectionAsync(
                widget, mode, focus, sequence, down: true);
            Assert.Equal(2, harness.PlaylistCalls);
            Assert.True(!repeatedPage.IsCompleted,
                "Repeated controller input started another adjacent page load.");
            harness.PlaylistCompletion.SetResult(harness.Playlists);
            harness.PlaylistCompletion = null;
            (focus, sequence, paged) = await slowPage;
            var repeated = await repeatedPage;
            Assert.True(repeated.Paginated && repeated.Focus == focus &&
                        harness.PlaylistCalls == 2,
                "Repeated controller input did not join the in-flight page transition.");
        }
        else
        {
            (focus, sequence, paged) = await PressPlaylistDirectionAsync(
                widget, mode, focus, sequence, down: true);
        }
        Assert.True(paged, $"{mode} controller Down did not enter page two.");
        Assert.Equal($"spotify.playlist.item.{mode}.12", focus);

        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await PressPlaylistDirectionAsync(
                widget, mode, focus, sequence, down: true);
        (focus, sequence, paged) = await PressPlaylistDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.True(paged, $"{mode} controller Down did not enter the five-row final page.");
        Assert.Equal($"spotify.playlist.item.{mode}.24", focus);
        for (var index = 0; index < 4; index++)
            (focus, sequence, _) = await PressPlaylistDirectionAsync(
                widget, mode, focus, sequence, down: true);
        Assert.Equal($"spotify.playlist.item.{mode}.28", focus);
        var finalFocus = focus;
        (finalFocus, sequence, paged) = await PressPlaylistDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.True(!paged && finalFocus == focus,
            $"{mode} final page exposed a nonexistent forward transition.");

        for (var index = 0; index < 4; index++)
            (focus, sequence, _) = await PressPlaylistDirectionAsync(
                widget, mode, focus, sequence, down: false);
        Assert.Equal($"spotify.playlist.item.{mode}.24", focus);
        (focus, sequence, paged) = await PressPlaylistDirectionAsync(
            widget, mode, focus, sequence, down: false);
        Assert.True(paged, $"{mode} controller Up did not restore cached page two.");
        Assert.Equal($"spotify.playlist.item.{mode}.23", focus);
        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await PressPlaylistDirectionAsync(
                widget, mode, focus, sequence, down: false);
        (focus, sequence, paged) = await PressPlaylistDirectionAsync(
            widget, mode, focus, sequence, down: false);
        Assert.True(paged, $"{mode} controller Up did not restore cached page one.");
        Assert.Equal($"spotify.playlist.item.{mode}.11", focus);
        Assert.Equal(3, harness.PlaylistCalls);

        await widget.OnActionAsync(new(
            "spotify.playlist.open.0", $"spotify.playlist.item.{mode}.0"));
        await WaitUntil(() => harness.PlaylistDetailCalls == 1);
        focus = $"spotify.playlist.track.{mode}.0";
        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await PressPlaylistItemDirectionAsync(
                widget, mode, focus, sequence, down: true);
        (focus, sequence, paged) = await PressPlaylistItemDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.True(paged && focus == $"spotify.playlist.track.{mode}.12",
            $"{mode} playlist-detail Down did not enter page two.");
        for (var index = 0; index < 11; index++)
            (focus, sequence, _) = await PressPlaylistItemDirectionAsync(
                widget, mode, focus, sequence, down: true);
        (focus, sequence, paged) = await PressPlaylistItemDirectionAsync(
            widget, mode, focus, sequence, down: true);
        Assert.True(paged && focus == $"spotify.playlist.track.{mode}.24",
            $"{mode} playlist-detail Down did not enter the final page.");
        (focus, sequence, paged) = await PressPlaylistItemDirectionAsync(
            widget, mode, focus, sequence, down: false);
        Assert.True(paged && focus == $"spotify.playlist.track.{mode}.23",
            $"{mode} playlist-detail Up did not restore cached page two.");
        Assert.Equal(3, harness.PlaylistDetailCalls);
        await StopAsync(widget);
    }
}

static Task<(string Focus, long Sequence, bool Paginated)>
    PressPlaylistDirectionAsync(
        SpotifyWidget widget,
        string mode,
        string focus,
        long sequence,
        bool down) => PressPagedDirectionAsync(
            widget,
            $"spotify.playlists.scroll.{mode}",
            $"spotify.playlist.item.{mode}.",
            focus,
            sequence,
            down);

static Task<(string Focus, long Sequence, bool Paginated)>
    PressPlaylistItemDirectionAsync(
        SpotifyWidget widget,
        string mode,
        string focus,
        long sequence,
        bool down) => PressPagedDirectionAsync(
            widget,
            $"spotify.playlist.detail.scroll.{mode}",
            $"spotify.playlist.track.{mode}.",
            focus,
            sequence,
            down);

static async Task<(string Focus, long Sequence, bool Paginated)>
    PressPagedDirectionAsync(
        SpotifyWidget widget,
        string scrollId,
        string itemIdPrefix,
        string focus,
        long sequence,
        bool down)
{
    var snapshot = widget.RenderSnapshot("spotify.controller-page", sequence);
    var scroll = Find(snapshot.Root, scrollId);
    var visibleIds = scroll.Children.Select(child => child.Id).ToArray();
    var visibleIndex = Array.IndexOf(visibleIds, focus);
    Assert.True(visibleIndex >= 0,
        $"Focused row {focus} is not in the visible page for {scrollId}.");
    var adjacent = visibleIndex + (down ? 1 : -1);
    if (adjacent >= 0 && adjacent < visibleIds.Length)
        return (visibleIds[adjacent], sequence, false);

    var actionId = down ? scroll.ScrollNearEndActionId : scroll.ScrollNearStartActionId;
    if (actionId is null) return (focus, sequence, false);
    var absoluteIndex = int.Parse(
        focus[(focus.LastIndexOf('.') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    var target = itemIdPrefix + (absoluteIndex + (down ? 1 : -1)).ToString(
        System.Globalization.CultureInfo.InvariantCulture);
    await widget.OnActionAsync(new(actionId, scroll.Id));
    var replacementSequence = sequence + 1;
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.controller-page", replacementSequence).Root,
        target));
    var replacement = widget.RenderSnapshot(
        "spotify.controller-page", replacementSequence);
    Assert.Equal(target, replacement.InitialFocusId);
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
    var visibleIds = scroll.Children.Select(child => child.Id).ToArray();
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
        .Select(index => new WidgetSpotifyPlaylistSummary(
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
        "spotify.playlists.page.next", "spotify.playlists.scroll.wide"));
    await WaitUntil(() => harness.PlaylistCalls == 2);

    await widget.OnActionAsync(new(
        "spotify.playlist.open.15", "spotify.playlist.item.wide.15"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);
    await widget.OnActionAsync(new(
        "spotify.playlist.back", "spotify.playlist.play.wide"));
    var restored = widget.RenderSnapshot("spotify.playlist.return-page", 1);
    Assert.Equal("spotify.playlist.item.wide.15", restored.InitialFocusId);
    Assert.NotNull(Find(restored.Root, restored.InitialFocusId!));
    await StopAsync(widget);
}

static async Task AdjacentPlaylistFailureRetry()
{
    var tracks = Enumerable.Range(0, 24)
        .Select(index => new WidgetSpotifyMediaItemSummary(
            WidgetSpotifyPlaybackItemType.Track, $"Track {index}", $"Artist {index}",
            180_000, null, $"spotify:track:track-{index}",
            $"https://open.spotify.com/track/track-{index}", true))
        .ToArray();
    var harness = SpotifyHarness.Ready();
    harness.PlaylistDetail = new(harness.Playlists.Items[0], tracks, 0, 12, tracks.Length);
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new("spotify.playlist.open.0", "spotify.playlist.item.wide.0"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 1);

    harness.PlaylistDetailError = new WidgetCapabilityException(
        "spotify_unavailable", "Spotify could not load more tracks");
    Assert.True(await DispatchPagedEdgeAsync(
        widget,
        "spotify.playlist.detail.scroll.wide",
        "spotify.playlist.track.wide.11",
        sequence: 2,
        down: true),
        "Controller Down did not admit the failing adjacent detail page.");
    await WaitUntil(() => harness.PlaylistDetailCalls == 2);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.retained-wait", 2).Root,
        "spotify.page.retained-error.wide.action"));
    var retained = widget.RenderSnapshot("spotify.playlist.retained", 3);
    Assert.NotNull(Find(retained.Root, "spotify.playlist.track.wide.0"));
    Assert.NotNull(Find(retained.Root, "spotify.page.retained-error.wide.action"));
    Assert.True(Find(retained.Root, "spotify.playlist.detail.scroll.wide")
            .ScrollNearEndActionId is null,
        "A failed adjacent load remained in an automatic retry loop.");

    harness.PlaylistDetailError = null;
    await widget.OnActionAsync(new(
        "spotify.page.retry", "spotify.page.retained-error.wide.action"));
    await WaitUntil(() => harness.PlaylistDetailCalls == 3);
    await WaitUntil(() => ContainsId(
        widget.RenderSnapshot("spotify.playlist.retry-wait", 4).Root,
        "spotify.playlist.track.wide.12"));
    var recovered = widget.RenderSnapshot("spotify.playlist.retry", 5);
    Assert.Equal("spotify.playlist.track.wide.12", recovered.InitialFocusId);
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
    Assert.NotNull(Find(snapshot.Root, "spotify.page.sparse.playlist.wide"));
    Assert.Equal("spotify.playlists.page.next",
        Find(snapshot.Root, "spotify.playlists.scroll.wide").ScrollNearEndActionId);
    await widget.OnActionAsync(new(
        "spotify.playlists.page.next", "spotify.playlists.scroll.wide"));
    await WaitUntil(() => harness.PlaylistCalls == 2);
    var next = widget.RenderSnapshot("spotify.playlists.sparse-next", 2);
    Assert.Equal("spotify.page.sparse.playlist.wide", next.InitialFocusId);
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
    Assert.NotNull(Find(snapshot.Root, "spotify.local.wide.action"));
    Assert.NotNull(Find(snapshot.Root, "spotify.device.wide.1"));

    await widget.OnActionAsync(new("spotify.local.start", "spotify.local.wide.action"));
    Assert.Equal(WidgetSpotifyLocalPlaybackOperation.StartAndTransfer,
        harness.LocalCommands.Single().Operation);
    Assert.Equal(1, harness.DeviceCalls);

    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new("spotify.playlist.open.0", "spotify.playlist.item.wide.0"));
    await widget.OnActionAsync(new("spotify.playlist.track.0", "spotify.playlist.track.wide.0"));
    Assert.Equal("local-placeholder", harness.StartedPlayback.Single().DeviceId);

    await widget.OnActionAsync(new("spotify.nav.devices", "spotify.nav.wide.devices"));
    await widget.OnActionAsync(new("spotify.device.select.1", "spotify.device.wide.1"));
    Assert.Equal("remote-device", harness.TransferredDevices.Single());

    await widget.OnActionAsync(new("spotify.nav.playlists", "spotify.nav.wide.playlists"));
    await widget.OnActionAsync(new("spotify.playlist.open.0", "spotify.playlist.item.wide.0"));
    await widget.OnActionAsync(new("spotify.playlist.track.0", "spotify.playlist.track.wide.0"));
    Assert.Equal("remote-device", harness.StartedPlayback[^1].DeviceId);
    await StopAsync(widget);
}

static async Task MissingPlaybackDeviceGuidance()
{
    var harness = SpotifyHarness.Ready();
    harness.StartPlaybackError = new WidgetCapabilityException(
        "resource_not_found", "The platform capability request failed (resource_not_found).");
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new("spotify.nav.queue", "spotify.nav.wide.queue"));
    await widget.OnActionAsync(new("spotify.queue.play.0", "spotify.queue.item.wide.0"));
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
    Assert.Equal(WidgetSpotifyPlaybackOperation.Pause, harness.Commands[0].Operation);
    await StopAsync(widget);
}

static async Task FailedControlRollback()
{
    var harness = SpotifyHarness.Ready();
    harness.ControlError = new WidgetCapabilityException(
        "permission_denied", "Playback control permission is off");
    var widget = await StartAsync(harness);
    await WaitUntil(() => widget.ViewState == SpotifyWidgetViewState.Ready);
    await widget.OnActionAsync(new WidgetActionEvent(
        "spotify.shuffle", "spotify.shuffle"));
    var snapshot = widget.Render().CreateSnapshot("spotify.rollback", 2);
    Assert.True(Find(snapshot.Root, "spotify.shuffle").IsSelected != true,
        "Failed shuffle did not roll back.");
    Assert.True(widget.Status.Contains("permission", StringComparison.OrdinalIgnoreCase),
        "Permission failure was not explained.");
    await StopAsync(widget);
}

static async Task PermissionDenied()
{
    var harness = SpotifyHarness.Ready();
    harness.ConfigurationError = new WidgetCapabilityException(
        "permission_denied", "Spotify configuration permission is off");
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
    Assert.Equal("org.gbar.samples.spotify", manifest.Id);
    Assert.Equal("org.gbar.samples", manifest.Publisher);
    Assert.True(manifest.Permissions.Contains(
        WidgetSpotifyCapabilities.ConfigurationCapabilityId), "Configuration permission missing.");
    Assert.True(manifest.Permissions.Contains(
        WidgetSpotifyCapabilities.AuthorizationCapabilityId), "Authorization permission missing.");
    Assert.True(manifest.Permissions.Contains(
        WidgetSpotifyCapabilities.PlaybackReadCapabilityId), "Playback read permission missing.");
    Assert.True(manifest.OptionalPermissions.Contains(
        WidgetSpotifyCapabilities.PlaybackControlCapabilityId), "Control must remain optional.");
    Assert.True(manifest.OptionalPermissions.Contains(
        WidgetSpotifyCapabilities.LocalPlaybackCapabilityId), "Local playback must remain optional.");
    Assert.True(manifest.OptionalPermissions.Contains(
        WidgetSpotifyCapabilities.PlaylistsReadCapabilityId), "Playlist reading must remain optional.");
    Assert.Equal("0.2.10", manifest.Version);
    Assert.NotNull(manifest.ResidencyPolicy);
    Assert.Equal(WidgetResidencyPolicies.KeepAlive, manifest.ResidencyPolicy!.Mode);
    return Task.CompletedTask;
}

static Task TimeFormatting()
{
    Assert.Equal("0:00", SpotifyWidget.FormatTime(-1));
    Assert.Equal("4:05", SpotifyWidget.FormatTime(245_000));
    Assert.Equal("1:01:01", SpotifyWidget.FormatTime(3_661_000));
    return Task.CompletedTask;
}

static async Task<SpotifyWidget> StartAsync(
    SpotifyHarness harness,
    TimeProvider? timeProvider = null)
{
    var widget = WidgetTestHost.Attach(new SpotifyWidget(timeProvider), harness.Services);
    await WidgetTestHost.InitializeAsync(widget);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    return widget;
}

static async Task StopAsync(SpotifyWidget widget)
{
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    await WidgetTestHost.DestroyAsync(widget);
}

static async Task WaitUntil(Func<bool> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while (!predicate()) await Task.Delay(10, timeout.Token);
}

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

static void AssertShortcut(ViewNode root, ControllerButton button, string action)
{
    var shortcut = root.Shortcuts.Single(item => item.Button == button);
    Assert.Equal(action, shortcut.ActionId);
}

file sealed class SpotifyHarness
{
    public bool Configured { get; set; } = true;
    public bool Connected { get; set; } = true;
    public Exception? ConfigurationError { get; set; }
    public Exception? ControlError { get; set; }
    public Exception? StartPlaybackError { get; set; }
    public Task? ControlWait { get; set; }
    public TaskCompletionSource<WidgetSpotifyAuthorizationSummary>? ConnectCompletion { get; set; }
    public CancellationToken? ConnectCancellationToken { get; private set; }
    public int ConnectCalls { get; private set; }
    public int ConfigurationCalls { get; private set; }
    public int PlaybackCalls { get; private set; }
    public int QueueCalls { get; private set; }
    public int PlaylistCalls { get; private set; }
    public int PlaylistDetailCalls { get; private set; }
    public int DeviceCalls { get; private set; }
    public Func<CancellationToken, ValueTask<WidgetSpotifyPlaybackSummary>>?
        PlaybackHandler { get; set; }
    public IReadOnlyList<WidgetSpotifyAuthorizationScope>? LastScopes { get; private set; }
    public List<WidgetSpotifyPlaybackCommand> Commands { get; } = [];
    public List<WidgetSpotifyLocalPlaybackCommand> LocalCommands { get; } = [];
    public List<string> TransferredDevices { get; } = [];
    public List<StartWidgetSpotifyPlaybackRequest> StartedPlayback { get; } = [];
    public WidgetSpotifyPlaybackSummary Playback { get; set; } = PlaybackSnapshot();
    public WidgetSpotifyQueueSummary Queue { get; set; } = new(
        new WidgetSpotifyMediaItemSummary(WidgetSpotifyPlaybackItemType.Track,
            "Small Hours", "Northern Lines", 240_000,
            "https://i.scdn.co/image/current", "spotify:track:current",
            "https://open.spotify.com/track/current", true),
        [new WidgetSpotifyMediaItemSummary(WidgetSpotifyPlaybackItemType.Track,
            "Midnight Run", "Northern Lines", 201_000,
            "https://i.scdn.co/image/next", "spotify:track:next",
            "https://open.spotify.com/track/next", true)], false);
    public WidgetSpotifyPlaylistPageSummary Playlists { get; set; } = new(
        [new WidgetSpotifyPlaylistSummary("playlist-one", "Night Drive", "Late-night focus",
            "https://i.scdn.co/image/playlist", "https://open.spotify.com/playlist/playlist-one",
            "spotify:playlist:playlist-one", "Listener", false, true, 1)], 0, 50, 1);
    public TaskCompletionSource<WidgetSpotifyPlaylistPageSummary>?
        PlaylistCompletion { get; set; }
    public bool IgnorePlaylistCancellation { get; set; }
    public WidgetSpotifyPlaylistItemsSummary PlaylistDetail { get; set; } = new(
        new WidgetSpotifyPlaylistSummary("playlist-one", "Night Drive", "Late-night focus",
            "https://i.scdn.co/image/playlist", "https://open.spotify.com/playlist/playlist-one",
            "spotify:playlist:playlist-one", "Listener", false, true, 1),
        [new WidgetSpotifyMediaItemSummary(WidgetSpotifyPlaybackItemType.Track,
            "Midnight Run", "Northern Lines", 201_000,
            "https://i.scdn.co/image/next", "spotify:track:next",
            "https://open.spotify.com/track/next", true)], 0, 50, 1);
    public Exception? PlaylistDetailError { get; set; }
    public TaskCompletionSource<WidgetSpotifyPlaylistItemsSummary>?
        PlaylistDetailCompletion { get; set; }
    public bool IgnorePlaylistDetailCancellation { get; set; }
    public Func<WidgetSpotifyPlaylistItemsRequest, CancellationToken,
        ValueTask<WidgetSpotifyPlaylistItemsSummary>>? PlaylistDetailHandler { get; set; }
    public List<WidgetSpotifyPlaylistItemsRequest> PlaylistDetailRequests { get; } = [];
    public WidgetSpotifyDevicesSummary Devices { get; set; } = new(
        [new WidgetSpotifyDeviceSummary("local-placeholder", "Game Bar Alternative",
            "Computer", false, false, true, 60, true),
         new WidgetSpotifyDeviceSummary("remote-device", "Living Room", "Speaker",
            true, false, true, 45, false)]);
    public WidgetSpotifyLocalPlaybackSummary LocalPlayback { get; set; } = new(
        WidgetSpotifyLocalPlaybackState.Ready, "Game Bar Alternative", 60, "Ready to play here");
    public WidgetHostServices Services { get; }

    public SpotifyHarness()
    {
        Services = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetSpotifyCapabilities.GetConfiguration,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ConfigurationCalls++;
                    if (ConfigurationError is not null)
                        return ValueTask.FromException<WidgetSpotifyConfigurationSummary>(
                            ConfigurationError);
                    return ValueTask.FromResult(new WidgetSpotifyConfigurationSummary(
                        Configured, WidgetSpotifyService.ExactRedirectUri));
                })
            .WithHandler(WidgetSpotifyCapabilities.GetAuthorization,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var state = !Configured ? WidgetSpotifyAuthorizationState.Unconfigured :
                        Connected ? WidgetSpotifyAuthorizationState.Connected :
                        WidgetSpotifyAuthorizationState.Disconnected;
                    return ValueTask.FromResult(new WidgetSpotifyAuthorizationSummary(
                        state, [], Connected ?
                        [WidgetSpotifyAuthorizationScope.PlaybackStateRead,
                         WidgetSpotifyAuthorizationScope.PlaybackStateControl] : [], null));
                })
            .WithHandler(WidgetSpotifyCapabilities.Connect, ConnectAsync)
            .WithHandler(WidgetSpotifyCapabilities.Disconnect,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Connected = false;
                    return ValueTask.FromResult(new WidgetSpotifyAuthorizationSummary(
                        WidgetSpotifyAuthorizationState.Disconnected, [], [], null));
                })
            .WithHandler(WidgetSpotifyCapabilities.GetPlayback,
                async (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PlaybackCalls++;
                    if (PlaybackHandler is not null)
                        return await PlaybackHandler(cancellationToken).ConfigureAwait(false);
                    return Playback;
                })
            .WithHandler(WidgetSpotifyCapabilities.ControlPlayback, ControlAsync)
            .WithHandler(WidgetSpotifyCapabilities.GetQueue,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    QueueCalls++;
                    return ValueTask.FromResult(Queue);
                })
            .WithHandler(WidgetSpotifyCapabilities.GetPlaylists,
                async (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PlaylistCalls++;
                    var source = PlaylistCompletion is null
                        ? Playlists
                        : IgnorePlaylistCancellation
                            ? await PlaylistCompletion.Task.ConfigureAwait(false)
                            : await PlaylistCompletion.Task.WaitAsync(cancellationToken)
                                .ConfigureAwait(false);
                    var items = source.Items.Skip(request.Offset).Take(request.Limit).ToArray();
                    return source with
                    {
                        Items = items,
                        Offset = request.Offset,
                        Limit = request.Limit,
                    };
                })
            .WithHandler(WidgetSpotifyCapabilities.GetPlaylistItems,
                async (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PlaylistDetailCalls++;
                    PlaylistDetailRequests.Add(request);
                    if (PlaylistDetailError is not null) throw PlaylistDetailError;
                    var detail = PlaylistDetailHandler is not null
                        ? await PlaylistDetailHandler(request, cancellationToken)
                            .ConfigureAwait(false)
                        : PlaylistDetailCompletion is not null
                            ? IgnorePlaylistDetailCancellation
                                ? await PlaylistDetailCompletion.Task.ConfigureAwait(false)
                                : await PlaylistDetailCompletion.Task.WaitAsync(cancellationToken)
                                    .ConfigureAwait(false)
                            : PlaylistDetail;
                    return detail with
                    {
                        Items = detail.Items.Skip(request.Offset).Take(request.Limit).ToArray(),
                        Offset = request.Offset,
                        Limit = request.Limit,
                    };
                })
            .WithHandler(WidgetSpotifyCapabilities.GetDevices,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DeviceCalls++;
                    return ValueTask.FromResult(Devices);
                })
            .WithHandler(WidgetSpotifyCapabilities.TransferPlayback,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TransferredDevices.Add(request.DeviceId);
                    return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
                })
            .WithHandler(WidgetSpotifyCapabilities.StartPlayback,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (StartPlaybackError is not null) throw StartPlaybackError;
                    StartedPlayback.Add(request);
                    return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
                })
            .WithHandler(WidgetSpotifyCapabilities.GetLocalPlayback,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(LocalPlayback);
                })
            .WithHandler(WidgetSpotifyCapabilities.ControlLocalPlayback,
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LocalCommands.Add(request);
                    LocalPlayback = request.Operation == WidgetSpotifyLocalPlaybackOperation.Stop
                        ? LocalPlayback with { State = WidgetSpotifyLocalPlaybackState.Disabled }
                        : LocalPlayback with { State = WidgetSpotifyLocalPlaybackState.Active };
                    return ValueTask.FromResult(LocalPlayback);
                })
            .Build();
    }

    public static SpotifyHarness Ready(long capturedAt = 0, long progress = 45_000) => new()
    {
        Configured = true,
        Connected = true,
        Playback = PlaybackSnapshot(capturedAt, progress),
    };

    private async ValueTask<WidgetSpotifyAuthorizationSummary> ConnectAsync(
        ConnectWidgetSpotifyRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectCalls++;
        LastScopes = request.RequestedScopes.ToArray();
        ConnectCancellationToken = cancellationToken;
        if (ConnectCompletion is not null)
        {
            var result = await ConnectCompletion.Task.WaitAsync(cancellationToken);
            Connected = result.State == WidgetSpotifyAuthorizationState.Connected;
            return result;
        }
        Connected = true;
        return new WidgetSpotifyAuthorizationSummary(
            WidgetSpotifyAuthorizationState.Connected, request.RequestedScopes,
            request.RequestedScopes, "Connected");
    }

    private async ValueTask<WidgetCapabilityAcknowledgement> ControlAsync(
        WidgetSpotifyPlaybackCommand request, CancellationToken cancellationToken)
    {
        Commands.Add(request);
        if (ControlError is not null) throw ControlError;
        if (ControlWait is not null) await ControlWait.WaitAsync(cancellationToken);
        Playback = request.Operation switch
        {
            WidgetSpotifyPlaybackOperation.Play => Playback with { IsPlaying = true },
            WidgetSpotifyPlaybackOperation.Pause => Playback with { IsPlaying = false },
            WidgetSpotifyPlaybackOperation.SetShuffle => Playback with
                { ShuffleState = request.Enabled!.Value },
            WidgetSpotifyPlaybackOperation.SetRepeat => Playback with
                { RepeatState = request.RepeatState!.Value },
            WidgetSpotifyPlaybackOperation.Seek => Playback with
                { ProgressMilliseconds = request.PositionMilliseconds!.Value },
            _ => Playback,
        };
        return new WidgetCapabilityAcknowledgement(true);
    }

    private static WidgetSpotifyPlaybackSummary PlaybackSnapshot(
        long capturedAt = 0, long progress = 45_000) => new(
        true, true, progress, 240_000, capturedAt,
        WidgetSpotifyRepeatState.Off, false,
        new WidgetSpotifyPlaybackItemSummary(
            WidgetSpotifyPlaybackItemType.Track, "Small Hours", "Northern Lines",
            "Night Drive", "https://i.scdn.co/image/example", "spotify:track:test"),
        new WidgetSpotifyPlaybackDisallowedActions(
            false, false, false, false, false, false, false, false),
        "Spotify");
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
}
