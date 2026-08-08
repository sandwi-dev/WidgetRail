using GameBarAlternative.Samples.SpotifyWidget;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Opening the widget never starts OAuth", OpeningNeverConnects),
    ("Disconnected copy and paired actions remain bounded and centered", DisconnectedLayoutContract),
    ("Unconfigured state provides safe exact setup guidance", UnconfiguredSetup),
    ("Setup is a nested B-dismissible input scope", NestedSetupBack),
    ("Setup uses a bounded controller-native scroll surface", SetupUsesVerticalScroll),
    ("Reopening setup starts a fresh scroll entry", SetupReopenResetsScrollIdentity),
    ("Setup code and text styles remain compact and bounded", SetupCodeAndTextAreBounded),
    ("Setup check refreshes newly saved configuration without starting OAuth", SetupCheckRefreshesConfiguration),
    ("Connect action acknowledges while OAuth remains pending", ConnectAcknowledgesWhilePending),
    ("OAuth survives Background and reconciles when visible", ConnectSurvivesBackground),
    ("Explicit connect requests only playback scopes", ExplicitConnect),
    ("Ready UI exposes native controller transport and attribution", ReadyControllerUi),
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
    var compiled = GbssThemeCompiler.Compile([parsed.Document]);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    var theme = compiled.Theme!;
    var detailStyle = theme.Resolve(new GbssElement(
        "text", StyleClasses: new HashSet<string>(["spotify-state-detail"])))!;
    Assert.Equal("100%", detailStyle.Get("width")?.Text);
    Assert.Equal("0px", detailStyle.Get("min-width")?.Text);
    Assert.Equal("560px", detailStyle.Get("max-width")?.Text);
    Assert.Equal("0", detailStyle.Get("flex-shrink")?.Text);
    Assert.Equal("4", detailStyle.Get("max-lines")?.Text);
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
    Assert.Equal("3", step.Get("max-lines")?.Text);
    Assert.Equal("1.3", step.Get("line-height")?.Text);
    Assert.Equal("4", command.Get("max-lines")?.Text);
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
    Assert.Equal("spotify.seek.slider", Find(snapshot.Root, "spotify.play-toggle").Focus!.Down);
    Assert.Equal("spotify.shuffle", scrubber.Focus!.Down);
    AssertShortcut(snapshot.Root, ControllerButton.LeftBumper, "spotify.previous");
    AssertShortcut(snapshot.Root, ControllerButton.X, "spotify.play-toggle");
    AssertShortcut(snapshot.Root, ControllerButton.RightBumper, "spotify.next");
    Assert.Equal(3, snapshot.QuickActions.Count);
    Assert.True(snapshot.QuickActions.All(action => action.Capability?.CapabilityId ==
        WidgetSpotifyCapabilities.PlaybackControlCapabilityId),
        "Quick actions bypass the public playback-control authority.");
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
    public Task? ControlWait { get; set; }
    public TaskCompletionSource<WidgetSpotifyAuthorizationSummary>? ConnectCompletion { get; set; }
    public CancellationToken? ConnectCancellationToken { get; private set; }
    public int ConnectCalls { get; private set; }
    public int ConfigurationCalls { get; private set; }
    public int PlaybackCalls { get; private set; }
    public IReadOnlyList<WidgetSpotifyAuthorizationScope>? LastScopes { get; private set; }
    public List<WidgetSpotifyPlaybackCommand> Commands { get; } = [];
    public WidgetSpotifyPlaybackSummary Playback { get; set; } = PlaybackSnapshot();
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
                (request, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PlaybackCalls++;
                    return ValueTask.FromResult(Playback);
                })
            .WithHandler(WidgetSpotifyCapabilities.ControlPlayback, ControlAsync)
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
