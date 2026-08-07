using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using GameBarAlternative.FirstPartyWidgets.AudioMixer;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Initial empty and session surfaces are valid and controller first", StateSurfaces),
    ("D-pad and analog focus graph covers every audio control", ExplicitFocusGraph),
    ("Open and dashboard controller routes stay widget owned", ControllerRoutes),
    ("Volume updates immediately and resists stale in-flight events", OptimisticVolume),
    ("Mute and volume failures roll back with bounded feedback", OptimisticRollback),
    ("Session churn preserves identity selection and control focus", StableSelectionDuringChurn),
    ("Capability failure codes render distinct recovery states", CapabilityFailureStates),
    ("Unavailable host service fails closed without OS fallback", UnavailableService),
    ("Visible lifetime fetches once subscribes and never polls", LifecycleAndNoPolling),
    ("Acknowledged subscription closes the snapshot fetch event gap", SubscriptionPrecedesSnapshot),
    ("Lifecycle cancellation rolls back without an error state", CancellationIsNotFailure),
    ("Manifest permissions and polished GBSS validate", ShippedAssetsValidate),
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
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task StateSurfaces()
{
    var fake = new FakeCapabilityClient();
    var widget = Create(fake);
    var initial = Snapshot(widget, 0);
    Assert.Equal(AudioMixerViewState.Initial, widget.ViewState);
    Assert.Equal("audio.retry", initial.InitialFocusId);
    Assert.Valid(initial);

    await Activate(widget);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Empty);
    var empty = Snapshot(widget, 1);
    Assert.Contains("No application audio", Text(empty.Root, "audio.state.title").Text!);
    Assert.Valid(empty);

    fake.Emit([Session("game", "Space Game", 0.72, active: true)]);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Ready);
    var session = Snapshot(widget, 2);
    Assert.Equal("audio.mute", session.InitialFocusId);
    Assert.Equal("Space Game", Text(session.Root, "audio.session.name").Text);
    Assert.Equal(72D, Node(session.Root, "audio.volume.progress").Value);
    Assert.Equal(WidgetGlyph.Volume, Node(session.Root, "audio.mute").Glyph);
    Assert.Valid(session);
    await Background(widget);
}

static async Task ExplicitFocusGraph()
{
    var fake = new FakeCapabilityClient { Sessions = [Session("a", "Game", 0.5), Session("b", "Chat", 0.4)] };
    var widget = Create(fake);
    await ActivateReady(widget);
    var snapshot = Snapshot(widget, 1);
    var buttons = Buttons(snapshot.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);
    Assert.SequenceEqual(
        ["audio.session.previous", "audio.session.next", "audio.volume.down", "audio.mute", "audio.volume.up"],
        buttons.Keys);
    foreach (var button in buttons.Values)
        Assert.True(button.Focus is not null, $"{button.Id} has no explicit focus neighbors.");
    Assert.Equal("audio.mute", buttons["audio.volume.down"].Focus!.Right);
    Assert.Equal("audio.volume.up", buttons["audio.mute"].Focus!.Right);
    Assert.Equal("audio.volume.down", buttons["audio.volume.up"].Focus!.Right);
    Assert.Equal("audio.session.previous", buttons["audio.volume.down"].Focus!.Up);
    Assert.Equal("audio.session.next", buttons["audio.volume.up"].Focus!.Up);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task ControllerRoutes()
{
    var fake = new FakeCapabilityClient { Sessions = [Session("a", "Game", 0.5), Session("b", "Chat", 0.4)] };
    var widget = Create(fake);
    await ActivateReady(widget);
    var snapshot = widget.RenderSnapshot("audio.test", 42);
    Assert.SequenceEqual(
        [ControllerButton.LeftBumper, ControllerButton.RightBumper],
        snapshot.QuickActions.Select(action => action.Button));
    Assert.True(!snapshot.QuickActions.Any(action => action.Button is
            ControllerButton.A or ControllerButton.Y or
            ControllerButton.DPadUp or ControllerButton.DPadDown or
            ControllerButton.DPadLeft or ControllerButton.DPadRight),
        "Dashboard navigation leaked into widget quick actions.");
    var scope = Node(snapshot.Root, "audio.root");
    Assert.True(!scope.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.B),
        "The root widget scope captured B instead of leaving standard close behavior to the host.");

    Assert.True(await Route(widget, snapshot, ControllerButton.RightBumper), "RB was not accepted.");
    await WaitUntil(() => widget.SelectedSessionId == "b");
    Assert.True(await Route(widget, snapshot, ControllerButton.X), "X was not accepted.");
    await WaitUntil(() => fake.MuteRequests.Count == 1);
    Assert.Equal("b", fake.MuteRequests[0].SessionId);
    Assert.Equal(true, fake.MuteRequests[0].IsMuted);
    Assert.True(await Route(widget, widget.RenderSnapshot("audio.test", 43), ControllerButton.LeftTrigger),
        "LT was not accepted.");
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    Assert.Near(0.35D, fake.VolumeRequests[0].Volume);

    var dashboard = widget.RenderSnapshot("audio.test", 44);
    Assert.True(await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.LeftBumper,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        SnapshotSequence: dashboard.Sequence)), "Dashboard LB was not widget routed.");
    await WaitUntil(() => widget.SelectedSessionId == "a");
    await Background(widget);
}

static async Task OptimisticVolume()
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("game", "Game", 0.5)],
        ControlGate = gate.Task,
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    var action = widget.OnActionAsync(new("volume.up", "audio.volume.up")).AsTask();
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    var optimistic = Snapshot(widget, 1);
    Assert.Equal(55D, Node(optimistic.Root, "audio.volume.progress").Value);
    Assert.Equal(true, Node(optimistic.Root, "audio.volume.up").IsBusy);

    fake.Emit([Session("game", "Game", 0.5)]);
    await Task.Delay(30);
    Assert.Equal(55D, Node(Snapshot(widget, 2).Root, "audio.volume.progress").Value);
    gate.SetResult();
    await action;
    Assert.Equal(55D, Node(Snapshot(widget, 3).Root, "audio.volume.progress").Value);
    Assert.True(Node(Snapshot(widget, 4).Root, "audio.volume.up").IsBusy is not true,
        "Busy feedback did not clear after acknowledgement.");
    await Background(widget);
}

static async Task OptimisticRollback()
{
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("game", "Game", 0.5)],
        ControlException = new WidgetCapabilityException("permission_denied", "denied"),
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    await widget.OnActionAsync(new("mute.toggle", "audio.mute"));
    Assert.Equal(false, widget.Sessions.Single().IsMuted);
    Assert.Contains("permission denied", Text(Snapshot(widget, 1).Root, "audio.status").Text!);
    Assert.True(Text(Snapshot(widget, 2).Root, "audio.status").StyleClasses.Contains("is-error"),
        "Control denial did not expose error feedback.");

    fake.ControlException = new InvalidOperationException("provider details must not leak");
    await widget.OnActionAsync(new("volume.up", "audio.volume.up"));
    Assert.Equal(0.5D, widget.Sessions.Single().Volume);
    var status = Text(Snapshot(widget, 3).Root, "audio.status").Text!;
    Assert.Contains("previous value restored", status);
    Assert.True(!status.Contains("provider details", StringComparison.Ordinal),
        "Provider exception details leaked into the UI.");
    await Background(widget);
}

static async Task StableSelectionDuringChurn()
{
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("a", "Game", 0.3), Session("b", "Chat", 0.4), Session("c", "Music", 0.5)],
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    await widget.OnActionAsync(new("session.next", "audio.session.next"));
    Assert.Equal("b", widget.SelectedSessionId);

    fake.Emit([Session("c", "Music", 0.5), Session("b", "Chat renamed", 0.6), Session("d", "Browser", 0.2)]);
    await WaitUntil(() => widget.Sessions.First().SessionId == "c");
    Assert.Equal("b", widget.SelectedSessionId);
    var retained = Snapshot(widget, 1);
    Assert.Equal("Chat renamed", Text(retained.Root, "audio.session.name").Text);
    Assert.Equal("audio.mute", retained.InitialFocusId);

    fake.Emit([Session("c", "Music", 0.5), Session("d", "Browser", 0.2)]);
    await WaitUntil(() => widget.Sessions.Count == 2);
    Assert.Equal("d", widget.SelectedSessionId);
    Assert.Equal("audio.mute", Snapshot(widget, 2).InitialFocusId);
    await Background(widget);
}

static async Task CapabilityFailureStates()
{
    var cases = new[]
    {
        ("permission_denied", AudioMixerViewState.PermissionDenied, "Audio access is off"),
        ("lifecycle_denied", AudioMixerViewState.LifecycleDenied, "paused by lifecycle"),
        ("channel_closed", AudioMixerViewState.ChannelClosed, "disconnected"),
    };
    foreach (var (code, state, expectedTitle) in cases)
    {
        var fake = new FakeCapabilityClient
        {
            GetException = new WidgetCapabilityException(code, "private broker detail"),
        };
        var widget = Create(fake);
        await Activate(widget);
        await WaitUntil(() => widget.ViewState == state);
        var snapshot = Snapshot(widget, 1);
        Assert.Contains(expectedTitle, Text(snapshot.Root, "audio.state.title").Text!);
        Assert.True(!Nodes(snapshot.Root).Any(node =>
                node.Text?.Contains("private broker detail", StringComparison.Ordinal) == true),
            "Broker details leaked into a recovery state.");
        Assert.Valid(snapshot);
        await Background(widget);
    }
}

static async Task UnavailableService()
{
    var widget = new AudioMixerWidget();
    await Activate(widget);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.ServiceUnavailable);
    var snapshot = Snapshot(widget, 1);
    Assert.Contains("service unavailable", Text(snapshot.Root, "audio.state.title").Text!);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task LifecycleAndNoPolling()
{
    var fake = new FakeCapabilityClient { Sessions = [Session("a", "Game", 0.5)] };
    var widget = Create(fake);
    await WidgetTestHost.InitializeAsync(widget);
    Assert.Equal(0, fake.GetCalls);
    Assert.Equal(0, fake.SubscriptionCount);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Ready && fake.SubscriptionCount == 1);
    Assert.Equal(1, widget.ActivationCount);
    Assert.Equal(1, widget.FetchCount);
    Assert.Equal(1, fake.GetCalls);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    await Task.Delay(120);
    Assert.Equal(1, fake.GetCalls);
    Assert.Equal(1, fake.SubscriptionCount);

    await Background(widget);
    await WaitUntil(() => fake.CanceledSubscriptions == 1);
    var calls = fake.GetCalls;
    await Task.Delay(120);
    Assert.Equal(calls, fake.GetCalls);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await WaitUntil(() => fake.GetCalls == 2 && fake.SubscriptionCount == 2);
    Assert.Equal(2, widget.ActivationCount);
    await Background(widget);
}

static async Task SubscriptionPrecedesSnapshot()
{
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("old", "Old snapshot", 0.2)],
    };
    fake.OnGet = () => fake.Emit([Session("new", "Gap event", 0.8, active: true)]);
    var widget = Create(fake);
    await Activate(widget);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Ready &&
                          widget.SelectedSessionId == "new");
    Assert.Equal(1, fake.SubscriptionCount);
    Assert.Equal(1, fake.GetCalls);
    Assert.Equal("Gap event", widget.Sessions.Single().DisplayName);
    Assert.Equal(0.8D, widget.Sessions.Single().Volume);
    await Background(widget);
}

static async Task CancellationIsNotFailure()
{
    var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("a", "Game", 0.5)],
        ControlGate = never.Task,
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    using var cancellation = new CancellationTokenSource();
    var command = widget.OnActionAsync(new("volume.up", "audio.volume.up"), cancellation.Token).AsTask();
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    cancellation.Cancel();
    await Assert.ThrowsCanceled(command);
    Assert.Equal(0.5D, widget.Sessions.Single().Volume);
    Assert.True(!Text(Snapshot(widget, 1).Root, "audio.status").StyleClasses.Contains("is-error"),
        "Normal lifecycle cancellation rendered as an error.");
    await Background(widget);
}

static async Task ShippedAssetsValidate()
{
    var project = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(Path.Combine(project, "manifest.json")));
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    Assert.SequenceEqual(["system.audio.sessions.read.v1"], manifest.Permissions);
    Assert.SequenceEqual(["system.audio.sessions.control.v1"], manifest.OptionalPermissions);
    Assert.Equal("suspend", manifest.BackgroundPolicy);
    Assert.Equal(64, manifest.ResourceRequest.MemoryMb);
    Assert.SequenceEqual(["x64"], manifest.Architectures);
    var package = GbssPackageLoader.LoadFile(
        Path.Combine(project, "styles", "default.gbss"),
        Path.Combine(project, "styles"));
    var compiled = GbssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    AssertResponsiveLayoutBudget(compiled.Theme!);
    var style = await File.ReadAllTextAsync(Path.Combine(project, "styles", "default.gbss"));
    Assert.Contains("width: 44vw", style);
    Assert.Contains("max-width: 620px", style);
    Assert.True(!style.Contains("min-width: 440px", StringComparison.Ordinal),
        "The widget retained a desktop-only hard width floor.");

    var catalogPath = Path.Combine(project, "..", "..", "OverlayHost", "widget-catalog.json");
    using var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(catalogPath));
    var trusted = catalog.RootElement.GetProperty("widgets").EnumerateArray().Single(item =>
        item.GetProperty("packageId").GetString() == manifest.Id);
    Assert.Equal(manifest.Publisher, trusted.GetProperty("publisherId").GetString());
    Assert.Equal(manifest.ResourceRequest.MemoryMb, trusted.GetProperty("memoryLimitMb").GetInt32());
    var manifestCapabilities = manifest.Permissions.Concat(manifest.OptionalPermissions)
        .Order(StringComparer.Ordinal).ToArray();
    var trustedCapabilities = trusted.GetProperty("declaredCapabilities").EnumerateArray()
        .Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray();
    Assert.SequenceEqual(manifestCapabilities, trustedCapabilities);
}

static void AssertResponsiveLayoutBudget(GbssTheme theme)
{
    var root = Resolve(theme, "stack", "audio.root", "audio-mixer-widget");
    var card = Resolve(theme, "stack", "audio.session.card", "audio-session-card");
    var controls = Resolve(theme, "row", "audio.controls", "audio-controls");
    var volumeButton = Resolve(theme, "button", "audio.volume.down", "audio-volume-action");
    var muteButton = Resolve(theme, "button", "audio.mute", "audio-mute-action");
    var sessionSwitcher = Resolve(theme, "row", "audio.session.switcher", "audio-session-switcher");
    var sessionButton = Resolve(theme, "button", "audio.session.previous", "audio-session-action");
    var sessionDetails = Resolve(theme, "stack", "audio.session.details", "audio-session-details");

    var volumeTarget = Pixels(volumeButton.Get("width")!, 280);
    var sessionTarget = Pixels(sessionButton.Get("width")!, 280);
    Assert.True(volumeTarget >= 44 && sessionTarget >= 44,
        "Narrow layout reduced primary controller targets below 44px.");

    foreach (var viewport in new[] { 280D, 320D, 1280D, 3840D })
    {
        var preferred = Pixels(root.Get("width")!, viewport);
        var rootWidth = Math.Min(viewport, Math.Clamp(
            preferred,
            Pixels(root.Get("min-width")!, viewport),
            Pixels(root.Get("max-width")!, viewport)));
        var rootInner = rootWidth - HorizontalSpacing(root.Get("padding")!, viewport);
        var cardInner = rootInner - HorizontalSpacing(card.Get("padding")!, viewport);

        var controlMinimum =
            2 * Pixels(volumeButton.Get("width")!, viewport) +
            Pixels(muteButton.Get("min-width")!, viewport) +
            2 * Pixels(controls.Get("gap")!, viewport);
        Assert.True(controlMinimum <= cardInner,
            $"Audio controls need {controlMinimum}px but only {cardInner}px is available at {viewport}px.");

        var switcherMinimum =
            2 * Pixels(sessionButton.Get("width")!, viewport) +
            Pixels(sessionDetails.Get("min-width")!, viewport) +
            2 * Pixels(sessionSwitcher.Get("gap")!, viewport);
        Assert.True(switcherMinimum <= cardInner,
            $"Session switcher needs {switcherMinimum}px but only {cardInner}px is available at {viewport}px.");
        Assert.True(rootWidth <= 620 && rootWidth <= viewport,
            $"Root width {rootWidth}px escaped its viewport/max bound at {viewport}px.");
    }
}

static GbssResolvedStyle Resolve(GbssTheme theme, string role, string id, string styleClass) =>
    theme.Resolve(new GbssElement(role, id,
        new HashSet<string>([styleClass], StringComparer.Ordinal)));

static double Pixels(GbssComputedValue value, double viewport)
{
    var unit = value.Unit;
    var number = value.Number;
    if (unit is null)
    {
        unit = value.Text.EndsWith("px", StringComparison.Ordinal) ? "px" :
            value.Text.EndsWith("vw", StringComparison.Ordinal) ? "vw" : null;
        if (unit is not null && double.TryParse(value.Text[..^unit.Length], out var parsed))
            number = parsed;
    }
    return unit switch
    {
        "px" => number ?? throw new InvalidOperationException("Length has no numeric value."),
        "vw" => (number ?? 0) * viewport / 100,
        _ => throw new InvalidOperationException($"Unsupported test length '{value.Text}'."),
    };
}

static double HorizontalSpacing(GbssComputedValue value, double viewport)
{
    var parts = value.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    static GbssComputedValue Part(string text)
    {
        var unit = text.EndsWith("px", StringComparison.Ordinal) ? "px" :
            text.EndsWith("vw", StringComparison.Ordinal) ? "vw" : null;
        if (unit is null || !double.TryParse(text[..^unit.Length], out var number))
            throw new InvalidOperationException($"Unsupported spacing '{text}'.");
        return new GbssComputedValue(GbssValueKind.Length, text, number, unit);
    }
    return parts.Length switch
    {
        1 => 2 * Pixels(Part(parts[0]), viewport),
        2 or 3 => 2 * Pixels(Part(parts[1]), viewport),
        4 => Pixels(Part(parts[1]), viewport) + Pixels(Part(parts[3]), viewport),
        _ => throw new InvalidOperationException($"Unsupported spacing list '{value.Text}'."),
    };
}

static AudioMixerWidget Create(FakeCapabilityClient fake)
{
    return WidgetTestHost.Attach(new AudioMixerWidget(), fake.BuildServices());
}

static async Task Activate(AudioMixerWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

static async Task ActivateReady(AudioMixerWidget widget)
{
    await Activate(widget);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Ready);
}

static async Task Background(AudioMixerWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

static async ValueTask<bool> Route(
    AudioMixerWidget widget,
    ViewSnapshot snapshot,
    ControllerButton button) =>
    await widget.OnControllerInputAsync(new ControllerInputEvent(
        button,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        FocusedElementId: "audio.mute",
        Sequence: 7,
        ActiveInputScopeId: snapshot.ActiveInputScopeId,
        SnapshotSequence: snapshot.Sequence));

static WidgetAudioSession Session(
    string id,
    string name,
    double volume,
    bool muted = false,
    bool active = false) => new(id, name, volume, muted, active);

static ViewSnapshot Snapshot(AudioMixerWidget widget, long sequence) =>
    widget.Render().CreateSnapshot("audio.test", sequence);

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Nodes(child))
        yield return descendant;
}

static IEnumerable<ViewNode> Buttons(ViewNode root) =>
    Nodes(root).Where(node => node.Kind == ViewNodeKind.Button);

static ViewNode Node(ViewNode root, string id) =>
    Nodes(root).Single(node => node.Id == id);

static ViewNode Text(ViewNode root, string id) =>
    Nodes(root).Single(node => node.Id == id && node.Kind == ViewNodeKind.Text);

static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 2_000)
{
    var deadline = Environment.TickCount64 + timeoutMilliseconds;
    while (!condition())
    {
        if (Environment.TickCount64 >= deadline)
            throw new TimeoutException("Timed out waiting for asynchronous widget state.");
        await Task.Delay(10);
    }
}

static string ProjectDirectory()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, "src", "FirstPartyWidgets", "AudioMixerWidget");
        if (Directory.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate AudioMixerWidget project directory.");
}

file sealed class FakeCapabilityClient
{
    private readonly object _gate = new();
    private readonly List<Channel<WidgetAudioSessionsChanged>> _subscribers = [];
    private int _getCalls;
    private int _subscriptionCount;
    private int _canceledSubscriptions;

    public bool IsAvailable => true;
    public IReadOnlyList<WidgetAudioSession> Sessions { get; set; } = [];
    public Exception? GetException { get; set; }
    public Exception? ControlException { get; set; }
    public Task? ControlGate { get; set; }
    public Action? OnGet { get; set; }
    public List<SetWidgetAudioSessionVolumeRequest> VolumeRequests { get; } = [];
    public List<SetWidgetAudioSessionMutedRequest> MuteRequests { get; } = [];
    public int GetCalls => Volatile.Read(ref _getCalls);
    public int SubscriptionCount => Volatile.Read(ref _subscriptionCount);
    public int CanceledSubscriptions => Volatile.Read(ref _canceledSubscriptions);

    public WidgetHostServices BuildServices() => new WidgetTestHostServicesBuilder()
        .WithHandler(
            WidgetAudioCapabilities.GetSessions,
            (request, cancellationToken) => InvokeAsync(
                WidgetAudioCapabilities.GetSessions, request, cancellationToken))
        .WithHandler(
            WidgetAudioCapabilities.SetSessionVolume,
            (request, cancellationToken) => InvokeAsync(
                WidgetAudioCapabilities.SetSessionVolume, request, cancellationToken))
        .WithHandler(
            WidgetAudioCapabilities.SetSessionMuted,
            (request, cancellationToken) => InvokeAsync(
                WidgetAudioCapabilities.SetSessionMuted, request, cancellationToken))
        .WithEventStream(
            WidgetAudioCapabilities.SessionsChanged,
            OpenEventStream)
        .Build();

    public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        if (operation.OperationId == WidgetAudioCapabilities.GetSessions.OperationId)
        {
            Interlocked.Increment(ref _getCalls);
            if (GetException is not null) throw GetException;
            var snapshot = Sessions.ToArray();
            OnGet?.Invoke();
            return (TResponse)(object)snapshot;
        }
        if (operation.OperationId == WidgetAudioCapabilities.SetSessionVolume.OperationId)
        {
            lock (_gate) VolumeRequests.Add((SetWidgetAudioSessionVolumeRequest)(object)request!);
            if (ControlGate is not null) await ControlGate.WaitAsync(cancellationToken);
            if (ControlException is not null) throw ControlException;
            return (TResponse)(object)new WidgetCapabilityAcknowledgement(true);
        }
        if (operation.OperationId == WidgetAudioCapabilities.SetSessionMuted.OperationId)
        {
            lock (_gate) MuteRequests.Add((SetWidgetAudioSessionMutedRequest)(object)request!);
            if (ControlGate is not null) await ControlGate.WaitAsync(cancellationToken);
            if (ControlException is not null) throw ControlException;
            return (TResponse)(object)new WidgetCapabilityAcknowledgement(true);
        }
        throw new WidgetCapabilityException("unsupported_operation", operation.OperationId);
    }

    private IAsyncEnumerable<WidgetAudioSessionsChanged> OpenEventStream(
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<WidgetAudioSessionsChanged>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        lock (_gate) _subscribers.Add(channel);
        Interlocked.Increment(ref _subscriptionCount);
        return ReadEvents(channel, cancellationToken);
    }

    private async IAsyncEnumerable<WidgetAudioSessionsChanged> ReadEvents(
        Channel<WidgetAudioSessionsChanged> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            lock (_gate) _subscribers.Remove(channel);
            Interlocked.Increment(ref _canceledSubscriptions);
        }
    }

    public void Emit(IReadOnlyList<WidgetAudioSession> sessions)
    {
        Sessions = sessions;
        Channel<WidgetAudioSessionsChanged>[] subscribers;
        lock (_gate) subscribers = _subscribers.ToArray();
        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(new WidgetAudioSessionsChanged(sessions));
    }
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

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }

    public static void Near(double expected, double actual, double tolerance = 0.000001)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static async Task ThrowsCanceled(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new InvalidOperationException("Expected an OperationCanceledException.");
    }

    public static void Valid(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
}
