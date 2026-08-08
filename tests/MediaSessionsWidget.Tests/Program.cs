using System.Threading.Channels;
using GameBarAlternative.FirstPartyWidgets.MediaSessions;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Visible lifecycle subscribes before fetching the initial snapshot", SubscriptionBeforeSnapshot),
    ("Compact UI exposes honest focusable transport capability states", HonestTransportStates),
    ("X and bumpers route through the selected session from any focused control", ControllerShortcutsRoute),
    ("An in-flight play command does not flash or disable sibling transports", PendingToggleKeepsSiblingControlsStable),
    ("Quick actions expose exact media control authority while visible", DashboardQuickActions),
    ("Session selection remains stable across reorder and metadata churn", SelectionSurvivesChurn),
    ("Removed selection falls back to Windows current session", RemovedSelectionFallsBack),
    ("Playing progress interpolates locally without capability polling", ProgressInterpolatesLocally),
    ("Current media artwork renders through the bounded inline image node", ArtworkRenders),
    ("Missing media artwork renders a semantic placeholder", MissingArtworkUsesPlaceholder),
    ("A failed live subscription does not discard a valid current snapshot", SubscriptionFailurePreservesSnapshot),
    ("A live channel failure after loading preserves the last valid snapshot", ChannelFailurePreservesSnapshot),
    ("Try again starts a fresh read and subscription attempt", RetryStartsFreshAttempt),
    ("Try again cannot cancel its own in-flight native reload", RetryDoesNotRestartWhileLoading),
    ("Capability and channel failures render recoverable states", FailureStates),
    ("Manifest permissions and GBSS package validate", PackageValidates),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}
if (failures != 0) Environment.Exit(1);
Console.WriteLine($"MediaSessionsWidget.Tests passed ({tests.Length} tests)");

static async Task SubscriptionBeforeSnapshot()
{
    var fake = new FakeMediaHost { Sessions = [Session("one", current: true)] };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    Assert.Equal("subscribe", fake.CallOrder[0]);
    Assert.Equal("get", fake.CallOrder[1]);
    Assert.Equal(1, fake.GetCalls);
    await Background(widget);
    await WaitUntil(() => fake.CanceledSubscriptions == 1);
}

static async Task HonestTransportStates()
{
    var fake = new FakeMediaHost
    {
        Sessions = [Session("one", current: true) with
        {
            CanPrevious = false,
            CanNext = false,
            CanTogglePlayPause = false,
            CanPlay = false,
            CanPause = false,
        }],
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    var snapshot = widget.RenderSnapshot("media.test", 1);
    Assert.Equal(WidgetSurfaceMode.Compact, snapshot.Surface!.Mode);
    Assert.Equal("media-sessions", snapshot.ActiveInputScopeId);
    Assert.Equal(0, snapshot.QuickActions.Count);
    var controls = Nodes(snapshot.Root).Where(node =>
        node.Id is "media.previous" or "media.play-toggle" or "media.next").ToArray();
    Assert.Equal(3, controls.Length);
    Assert.True(controls.All(node => node.IsDisabled is true));
    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
}

static async Task ControllerShortcutsRoute()
{
    var fake = new FakeMediaHost { Sessions = [Session("one", current: true)] };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    var snapshot = widget.RenderSnapshot("media.test", 2);
    foreach (var button in new[]
             { ControllerButton.LeftBumper, ControllerButton.X, ControllerButton.RightBumper })
    {
        var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
            button, ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget,
            "media.play-toggle", SnapshotSequence: snapshot.Sequence,
            ActiveInputScopeId: snapshot.ActiveInputScopeId));
        Assert.True(handled);
        await WaitUntil(() => fake.Commands.Count == Array.IndexOf(
            new[] { ControllerButton.LeftBumper, ControllerButton.X, ControllerButton.RightBumper },
            button) + 1);
    }
    Assert.SequenceEqual(new[]
    {
        WidgetMediaSessionCommand.Previous,
        WidgetMediaSessionCommand.TogglePlayPause,
        WidgetMediaSessionCommand.Next,
    }, fake.Commands.Select(item => item.Command));
    Assert.True(fake.Commands.All(item => item.SessionId == "one"));
    await Background(widget);
}

static async Task PendingToggleKeepsSiblingControlsStable()
{
    var pending = new TaskCompletionSource<WidgetCapabilityAcknowledgement>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeMediaHost
    {
        Sessions = [Session("one", current: true)],
        PendingControl = pending,
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);

    var baseline = widget.RenderSnapshot("media.test", 90);
    var baselinePrevious = Nodes(baseline.Root).Single(node => node.Id == "media.previous");
    var baselineNext = Nodes(baseline.Root).Single(node => node.Id == "media.next");
    var playTask = widget.OnActionAsync(new("media.toggle", "media.play-toggle")).AsTask();
    await WaitUntil(() => fake.Commands.Count == 1);

    var inFlight = widget.RenderSnapshot("media.test", 91);
    var inFlightPrevious = Nodes(inFlight.Root).Single(node => node.Id == "media.previous");
    var inFlightToggle = Nodes(inFlight.Root).Single(node => node.Id == "media.play-toggle");
    var inFlightNext = Nodes(inFlight.Root).Single(node => node.Id == "media.next");
    Assert.Equal(baselinePrevious.IsDisabled, inFlightPrevious.IsDisabled);
    Assert.Equal(baselinePrevious.IsBusy, inFlightPrevious.IsBusy);
    Assert.Equal(baselinePrevious.Glyph, inFlightPrevious.Glyph);
    Assert.Equal(baselineNext.IsDisabled, inFlightNext.IsDisabled);
    Assert.Equal(baselineNext.IsBusy, inFlightNext.IsBusy);
    Assert.Equal(baselineNext.Glyph, inFlightNext.Glyph);
    Assert.True(inFlightToggle.IsDisabled is true && inFlightToggle.IsBusy is true);

    // The sibling remains visually enabled, but the widget-level single-flight
    // gate consumes the attempted action without issuing another command.
    await widget.OnActionAsync(new("media.previous", "media.previous"));
    Assert.Equal(1, fake.Commands.Count);
    var afterRejectedSibling = widget.RenderSnapshot("media.test", 92);
    Assert.Equal(inFlightPrevious.IsDisabled, Nodes(afterRejectedSibling.Root)
        .Single(node => node.Id == "media.previous").IsDisabled);

    pending.SetResult(new WidgetCapabilityAcknowledgement(true));
    await playTask;
    var succeeded = widget.RenderSnapshot("media.test", 93);
    Assert.True(Nodes(succeeded.Root).Single(node => node.Id == "media.play-toggle").IsBusy is not true);
    Assert.Equal(baselinePrevious.IsDisabled, Nodes(succeeded.Root)
        .Single(node => node.Id == "media.previous").IsDisabled);
    Assert.Equal(baselineNext.IsDisabled, Nodes(succeeded.Root)
        .Single(node => node.Id == "media.next").IsDisabled);

    var failing = new TaskCompletionSource<WidgetCapabilityAcknowledgement>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    fake.PendingControl = failing;
    var failureTask = widget.OnActionAsync(new("media.toggle", "media.play-toggle")).AsTask();
    await WaitUntil(() => fake.Commands.Count == 2);
    var failingSnapshot = widget.RenderSnapshot("media.test", 94);
    Assert.True(Nodes(failingSnapshot.Root).Single(node => node.Id == "media.play-toggle").IsBusy is true);
    Assert.Equal(baselinePrevious.IsDisabled, Nodes(failingSnapshot.Root)
        .Single(node => node.Id == "media.previous").IsDisabled);
    Assert.Equal(baselineNext.IsDisabled, Nodes(failingSnapshot.Root)
        .Single(node => node.Id == "media.next").IsDisabled);
    failing.SetException(new WidgetCapabilityException("not_supported", "rejected"));
    await failureTask;
    var recovered = widget.RenderSnapshot("media.test", 95);
    var recoveredToggle = Nodes(recovered.Root).Single(node => node.Id == "media.play-toggle");
    Assert.True(recoveredToggle.IsBusy is not true && recoveredToggle.IsDisabled is not true);
    Assert.Equal(baselinePrevious.IsDisabled, Nodes(recovered.Root)
        .Single(node => node.Id == "media.previous").IsDisabled);
    Assert.Equal(baselineNext.IsDisabled, Nodes(recovered.Root)
        .Single(node => node.Id == "media.next").IsDisabled);
    await Background(widget);
}

static async Task DashboardQuickActions()
{
    var fake = new FakeMediaHost
    {
        Sessions = [Session("one", current: true) with { CanPrevious = false }],
    };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    var snapshot = widget.RenderSnapshot("media.test", 2);
    Assert.Equal(2, snapshot.QuickActions.Count);
    Assert.SequenceEqual(
        new[] { ControllerButton.X, ControllerButton.RightBumper },
        snapshot.QuickActions.Select(item => item.Button));
    Assert.True(snapshot.QuickActions.All(item =>
        item.Capability == new WidgetQuickActionCapability(
            WidgetMediaCapabilities.Control.CapabilityId,
            WidgetMediaCapabilities.Control.OperationId)));
    var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.RightBumper, ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        Sequence: 1,
        SnapshotSequence: snapshot.Sequence));
    Assert.True(handled);
    await WaitUntil(() => fake.Commands.Count == 1);
    Assert.Equal(WidgetMediaSessionCommand.Next, fake.Commands[0].Command);
    await Background(widget);
}

static async Task SelectionSurvivesChurn()
{
    var fake = new FakeMediaHost
    {
        Sessions = [Session("one", current: true), Session("two", app: "Second")],
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Sessions.Count == 2);
    var snapshot = widget.RenderSnapshot("media.test", 4);
    var second = Nodes(snapshot.Root).Single(node => node.ActionId == "media.select" && node.Text == "Second");
    await widget.OnActionAsync(new("media.select", second.Id));
    Assert.Equal("two", widget.SelectedSessionId);
    fake.Publish([Session("two", app: "Second", title: "Updated"), Session("one", current: true)]);
    await WaitUntil(() => widget.Sessions.First().Title == "Updated");
    Assert.Equal("two", widget.SelectedSessionId);
    await Background(widget);
}

static async Task RemovedSelectionFallsBack()
{
    var fake = new FakeMediaHost
    {
        Sessions = [Session("one", current: true), Session("two", app: "Second")],
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Sessions.Count == 2);
    var second = Nodes(widget.RenderSnapshot("media.test", 5).Root)
        .Single(node => node.ActionId == "media.select" && node.Text == "Second");
    await widget.OnActionAsync(new("media.select", second.Id));
    fake.Publish([Session("three", app: "Third"), Session("one", current: true)]);
    await WaitUntil(() => widget.Sessions.Any(item => item.SessionId == "three"));
    Assert.Equal("one", widget.SelectedSessionId);
    await Background(widget);
}

static async Task ProgressInterpolatesLocally()
{
    var time = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(10_000));
    var fake = new FakeMediaHost
    {
        Sessions = [Session("one", current: true, playing: true) with
        {
            PositionMilliseconds = 2_000,
            DurationMilliseconds = 10_000,
            CapturedAtUnixMilliseconds = 10_000,
        }],
    };
    var widget = Create(fake, time);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    var first = Nodes(widget.RenderSnapshot("media.test", 6).Root)
        .Single(node => node.Kind == ViewNodeKind.Progress).Value;
    time.Advance(TimeSpan.FromMilliseconds(1_500));
    var second = Nodes(widget.RenderSnapshot("media.test", 7).Root)
        .Single(node => node.Kind == ViewNodeKind.Progress).Value;
    Assert.Equal(2_000D, first!.Value);
    Assert.Equal(3_500D, second!.Value);
    Assert.Equal(1, fake.GetCalls);
    await Background(widget);
}

static async Task ArtworkRenders()
{
    const string png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lK3xWQAAAABJRU5ErkJggg==";
    var fake = new FakeMediaHost
    {
        Sessions = [Session("one", title: "Covered", current: true) with
        {
            ArtworkPngBase64 = png,
        }],
    };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    var snapshot = widget.RenderSnapshot("media.test", 80);
    var artwork = Nodes(snapshot.Root).Single(node => node.Id == "media.artwork");
    Assert.Equal(ViewNodeKind.Image, artwork.Kind);
    Assert.Equal($"data:image/png;base64,{png}", artwork.ImageSource);
    Assert.Equal("Artwork for Covered", artwork.AccessibilityLabel);
    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
    await Background(widget);
}

static async Task MissingArtworkUsesPlaceholder()
{
    var fake = new FakeMediaHost { Sessions = [Session("one", current: true)] };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    var snapshot = widget.RenderSnapshot("media.test", 81);
    var placeholder = Nodes(snapshot.Root).Single(node => node.Id == "media.artwork-placeholder");
    Assert.Equal(ViewNodeKind.Icon, placeholder.Kind);
    Assert.Equal(WidgetGlyph.Music, placeholder.Glyph);
    Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
    await Background(widget);
}

static async Task SubscriptionFailurePreservesSnapshot()
{
    var fake = new FakeMediaHost
    {
        Sessions = [Session("one", current: true)],
        SubscriptionOpenException = new WidgetCapabilityException(
            "channel_closed", "subscription failed"),
    };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    Assert.Equal(1, widget.Sessions.Count);
    Assert.Equal(1, fake.GetCalls);
    Assert.Equal(1, fake.SubscriptionCalls);
    Assert.False(widget.LiveUpdatesAvailable);
    Assert.True(widget.Status.Contains("live updates disconnected", StringComparison.Ordinal));
    await Background(widget);
}

static async Task ChannelFailurePreservesSnapshot()
{
    var fake = new FakeMediaHost
    {
        Sessions = [Session("one", current: true)],
        SubscriptionReadException = new WidgetCapabilityException(
            "malformed_event", "invalid event"),
    };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.Status.Contains("response was invalid", StringComparison.Ordinal));
    Assert.Equal(MediaSessionsViewState.Ready, widget.ViewState);
    Assert.Equal("one", widget.Sessions.Single().SessionId);
    Assert.False(widget.LiveUpdatesAvailable);
    await Background(widget);
}

static async Task RetryStartsFreshAttempt()
{
    var fake = new FakeMediaHost
    {
        ReadException = new WidgetCapabilityException("platform_unavailable", "first read failed"),
    };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.ServiceUnavailable);
    Assert.Equal(1, fake.GetCalls);
    Assert.Equal(1, fake.SubscriptionCalls);

    fake.ReadException = null;
    fake.Sessions = [Session("recovered", current: true)];
    await widget.OnActionAsync(new("media.retry", "media.retry"));
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    Assert.Equal(2, fake.GetCalls);
    Assert.Equal(2, fake.SubscriptionCalls);
    Assert.Equal("recovered", widget.Sessions.Single().SessionId);
    Assert.True(widget.LiveUpdatesAvailable);
    await Background(widget);
}

static async Task RetryDoesNotRestartWhileLoading()
{
    var fake = new FakeMediaHost
    {
        ReadException = new WidgetCapabilityException("platform_unavailable", "first read failed"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.ServiceUnavailable);

    fake.ReadException = null;
    fake.PendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var failed = widget.RenderSnapshot("media.retry", 10);
    var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.A,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        "media.retry",
        SnapshotSequence: failed.Sequence,
        ActiveInputScopeId: failed.ActiveInputScopeId));
    Assert.True(handled);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Loading);
    await WaitUntil(() => fake.GetCalls == 2);

    var loading = widget.RenderSnapshot("media.retry", 11);
    var retry = Nodes(loading.Root).Single(node => node.Id == "media.retry");
    Assert.True(retry.IsDisabled is true);
    Assert.True(retry.IsBusy is true);
    Assert.Equal("Loading…", retry.Text);

    // Even a stale action already queued by the host must not cancel and
    // replace the native request currently making progress.
    await widget.OnActionAsync(new("media.retry", "media.retry"));
    await Task.Delay(50);
    Assert.Equal(2, fake.GetCalls);
    Assert.Equal(2, fake.SubscriptionCalls);

    fake.PendingRead.SetResult([Session("recovered", current: true)]);
    await WaitUntil(() => widget.ViewState == MediaSessionsViewState.Ready);
    Assert.Equal("recovered", widget.Sessions.Single().SessionId);
    await Background(widget);
}

static async Task FailureStates()
{
    foreach (var (error, state) in new[]
    {
        ("permission_denied", MediaSessionsViewState.PermissionDenied),
        ("lifecycle_denied", MediaSessionsViewState.LifecycleDenied),
        ("platform_unavailable", MediaSessionsViewState.ServiceUnavailable),
        ("channel_closed", MediaSessionsViewState.ChannelClosed),
    })
    {
        var fake = new FakeMediaHost { ReadException = new WidgetCapabilityException(error, error) };
        var widget = Create(fake);
        await Visible(widget);
        await WaitUntil(() => widget.ViewState == state);
        Assert.True(Nodes(widget.RenderSnapshot("media.failure", 1).Root)
            .Any(node => node.ActionId == "media.retry"));
        await Background(widget);
    }
}

static Task PackageValidates()
{
    var root = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
    Assert.True(manifest.Permissions.Contains("system.media.sessions.read.v1"));
    Assert.True(manifest.OptionalPermissions.Contains("system.media.sessions.control.v1"));
    var package = GbssPackageLoader.Load("styles/default.gbss", new GbssFileSourceProvider(root));
    Assert.True(GbssThemeCompiler.Compile(package).IsValid);
    return Task.CompletedTask;
}

static WidgetMediaSession Session(
    string id,
    string app = "Player",
    string title = "Track",
    bool current = false,
    bool playing = false) =>
    new(id, app, title, "Artist",
        playing ? WidgetMediaPlaybackStatus.Playing : WidgetMediaPlaybackStatus.Paused,
        1_000, 10_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1,
        current, true, true, true, true, true);

static MediaSessionsWidget Create(FakeMediaHost fake, TimeProvider? time = null) =>
    WidgetTestHost.Attach(new MediaSessionsWidget(time), fake.Build());
static async Task Visible(MediaSessionsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
static async Task Interactive(MediaSessionsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
static async Task Background(MediaSessionsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Nodes(child)) yield return descendant;
}

static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 2_000)
{
    var deadline = Environment.TickCount64 + timeoutMilliseconds;
    while (!condition())
    {
        if (Environment.TickCount64 >= deadline) throw new TimeoutException();
        await Task.Delay(10);
    }
}

static string ProjectDirectory()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName,
            "src", "FirstPartyWidgets", "MediaSessionsWidget");
        if (Directory.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException();
}

file sealed class FakeMediaHost
{
    private readonly Channel<WidgetMediaSessionsChanged> _events =
        Channel.CreateUnbounded<WidgetMediaSessionsChanged>();
    private int _canceledSubscriptions;
    internal IReadOnlyList<WidgetMediaSession> Sessions { get; set; } = [];
    internal Exception? ReadException { get; set; }
    internal Exception? ControlException { get; set; }
    internal Exception? SubscriptionOpenException { get; set; }
    internal Exception? SubscriptionReadException { get; set; }
    internal TaskCompletionSource<IReadOnlyList<WidgetMediaSession>>? PendingRead { get; set; }
    internal TaskCompletionSource<WidgetCapabilityAcknowledgement>? PendingControl { get; set; }
    internal List<string> CallOrder { get; } = [];
    internal List<ControlWidgetMediaSessionRequest> Commands { get; } = [];
    internal int GetCalls { get; private set; }
    internal int SubscriptionCalls { get; private set; }
    internal int CanceledSubscriptions => Volatile.Read(ref _canceledSubscriptions);

    internal WidgetHostServices Build() => new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetMediaCapabilities.GetSessions, GetAsync)
        .WithHandler(WidgetMediaCapabilities.Control, ControlAsync)
        .WithEventStream(WidgetMediaCapabilities.Changed, Subscribe)
        .Build();

    internal void Publish(IReadOnlyList<WidgetMediaSession> sessions) =>
        _events.Writer.TryWrite(new(sessions));

    private ValueTask<IReadOnlyList<WidgetMediaSession>> GetAsync(
        WidgetCapabilityQuery request, CancellationToken cancellationToken)
    {
        CallOrder.Add("get");
        GetCalls++;
        if (ReadException is not null)
            return ValueTask.FromException<IReadOnlyList<WidgetMediaSession>>(ReadException);
        if (PendingRead is not null)
            return new ValueTask<IReadOnlyList<WidgetMediaSession>>(
                PendingRead.Task.WaitAsync(cancellationToken));
        return ValueTask.FromResult(Sessions);
    }

    private ValueTask<WidgetCapabilityAcknowledgement> ControlAsync(
        ControlWidgetMediaSessionRequest request, CancellationToken cancellationToken)
    {
        Commands.Add(request);
        if (PendingControl is not null)
            return new ValueTask<WidgetCapabilityAcknowledgement>(
                PendingControl.Task.WaitAsync(cancellationToken));
        if (ControlException is not null)
            return ValueTask.FromException<WidgetCapabilityAcknowledgement>(ControlException);
        return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
    }

    private IAsyncEnumerable<WidgetMediaSessionsChanged> Subscribe(CancellationToken cancellationToken)
    {
        CallOrder.Add("subscribe");
        SubscriptionCalls++;
        if (SubscriptionOpenException is not null) throw SubscriptionOpenException;
        return ReadEvents(cancellationToken);
    }

    private async IAsyncEnumerable<WidgetMediaSessionsChanged> ReadEvents(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            if (SubscriptionReadException is not null) throw SubscriptionReadException;
            await foreach (var change in _events.Reader.ReadAllAsync(cancellationToken))
                yield return change;
        }
        finally { Interlocked.Increment(ref _canceledSubscriptions); }
    }
}

file sealed class ManualTimeProvider(DateTimeOffset current) : TimeProvider
{
    private DateTimeOffset _current = current;
    public override DateTimeOffset GetUtcNow() => _current;
    internal void Advance(TimeSpan duration) => _current += duration;
}

file static class Assert
{
    internal static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }
    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
    internal static void False(bool value) => True(!value);
    internal static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Sequences differ.");
    }
}
