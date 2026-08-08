using System.Threading.Channels;
using GameBarAlternative.FirstPartyWidgets.RecentApps;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Visible lifecycle opens subscription before fetching snapshot", SubscriptionBeforeSnapshot),
    ("Ready UI is a bounded controller scroll with stable focus graph", RendersControllerList),
    ("Activity rows are local read-only selections", RowsAreReadOnlySelections),
    ("Events reconcile ordering and remove stale selections", EventsReconcile),
    ("Most-recent metadata never creates a second controller selection", RecencyIsNotSelection),
    ("Permission and channel failures render recoverable states", FailuresAreRecoverable),
    ("Manifest and GBSS package validate", PackageValidates),
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
Console.WriteLine($"RecentAppsWidget.Tests passed ({tests.Length} tests)");

static async Task SubscriptionBeforeSnapshot()
{
    var fake = new FakeActivityHost { Activities = [Activity("one", "One", mostRecent: true)] };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == RecentAppsViewState.Ready);
    Assert.Equal("subscribe", fake.CallOrder[0]);
    Assert.Equal("get", fake.CallOrder[1]);
    Assert.Equal(1, fake.SubscriptionCount);
    await Background(widget);
    await WaitUntil(() => fake.CanceledSubscriptions == 1);
}

static async Task RendersControllerList()
{
    var fake = new FakeActivityHost
    {
        Activities =
        [
            Activity("one", "Application One", mostRecent: true),
            Activity("two", "Editor"),
            Activity("three", "Browser"),
        ],
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == RecentAppsViewState.Ready);
    var snapshot = widget.RenderSnapshot("recent.test", 7);
    Assert.Equal(WidgetSurfaceMode.Compact, snapshot.Surface!.Mode);
    Assert.Equal(ProtocolConstants.ScrollContainerVersion, snapshot.ProtocolVersion);
    Assert.Equal("recent-apps", snapshot.ActiveInputScopeId);
    var scroll = Nodes(snapshot.Root).Single(node => node.Kind == ViewNodeKind.Scroll);
    Assert.Equal(ScrollAxis.Vertical, scroll.ScrollAxis);
    var buttons = Nodes(scroll).Where(node => node.Kind == ViewNodeKind.Button).ToArray();
    Assert.Equal(3, buttons.Length);
    Assert.True(buttons[0].IsDisabled is not true);
    Assert.True(buttons[1].IsDisabled is not true);
    Assert.Equal(buttons[0].Id, buttons[0].Focus!.Up);
    Assert.True(buttons[^1].Focus!.Down is null);
    Assert.True(snapshot.InitialFocusId == buttons[0].Id);
    Assert.False(Nodes(snapshot.Root).Any(node =>
        (node.Text ?? string.Empty).Contains("one", StringComparison.Ordinal)));
}

static async Task RowsAreReadOnlySelections()
{
    var fake = new FakeActivityHost
    {
        Activities = [Activity("one", "One"), Activity("two", "Two")],
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == RecentAppsViewState.Ready);
    var snapshot = widget.RenderSnapshot("recent.test", 2);
    var buttons = Nodes(snapshot.Root)
        .Where(node => node.Kind == ViewNodeKind.Button && node.ActionId == "recent.select")
        .ToArray();
    Assert.Equal(2, buttons.Length);
    Assert.False(Nodes(snapshot.Root).Any(node =>
        node.ActionId == "recent.activate" || node.Glyph == WidgetGlyph.Play));
    var button = buttons.Single(node => node.Text == "Two");
    var handled = await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.A, ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget,
        button.Id, SnapshotSequence: snapshot.Sequence,
        ActiveInputScopeId: snapshot.ActiveInputScopeId));
    Assert.True(handled);
    await WaitUntil(() => Nodes(widget.RenderSnapshot("recent.test", 3).Root)
        .Single(node => node.Text == "Two").IsSelected is true);
}

static async Task EventsReconcile()
{
    var fake = new FakeActivityHost
    {
        Activities = [Activity("one", "One", mostRecent: true), Activity("two", "Two")],
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Activities.Count == 2);
    fake.Publish([Activity("two", "Two", mostRecent: true)]);
    await WaitUntil(() => widget.Activities.Count == 1 && widget.Activities[0].ActivityId == "two");
    var snapshot = widget.RenderSnapshot("recent.test", 3);
    Assert.True(Nodes(snapshot.Root).Any(node => node.Text == "Two"));
    Assert.False(Nodes(snapshot.Root).Any(node => node.Text == "One"));
}

static async Task RecencyIsNotSelection()
{
    var fake = new FakeActivityHost
    {
        Activities = [Activity("one", "One", mostRecent: true), Activity("two", "Two")],
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Activities.Count == 2);

    fake.Publish([Activity("two", "Two", mostRecent: true), Activity("one", "One")]);
    await WaitUntil(() => widget.Activities[0].ActivityId == "two");
    var buttons = Nodes(widget.RenderSnapshot("recent.test", 10).Root)
        .Where(node => node.Kind == ViewNodeKind.Button && node.ActionId == "recent.select")
        .ToArray();
    Assert.Equal(1, buttons.Count(node => node.IsSelected is true));
    Assert.Equal("One", buttons.Single(node => node.IsSelected is true).Text);
    Assert.True(buttons.Single(node => node.Text == "Two").StyleClasses.Contains("is-most-recent"));
}

static async Task FailuresAreRecoverable()
{
    foreach (var (error, expected) in new[]
    {
        ("permission_denied", RecentAppsViewState.PermissionDenied),
        ("lifecycle_denied", RecentAppsViewState.LifecycleDenied),
        ("channel_closed", RecentAppsViewState.ChannelClosed),
        ("platform_unavailable", RecentAppsViewState.ServiceUnavailable),
    })
    {
        var fake = new FakeActivityHost { ReadException = new WidgetCapabilityException(error, error) };
        var widget = Create(fake);
        await Visible(widget);
        await WaitUntil(() => widget.ViewState == expected);
        Assert.True(Nodes(widget.Render().CreateSnapshot("failure", 1).Root)
            .Any(node => node.ActionId == "retry"));
        await Background(widget);
    }
}

static Task PackageValidates()
{
    var root = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
    Assert.True(manifest.Permissions.Contains("system.activity.recent.read.v1"));
    Assert.Equal(0, manifest.OptionalPermissions.Count);
    var package = GbssPackageLoader.Load("styles/default.gbss", new GbssFileSourceProvider(root));
    var compiled = GbssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid);
    return Task.CompletedTask;
}

static WidgetRecentActivity Activity(
    string id,
    string name,
    WidgetRecentActivityKind kind = WidgetRecentActivityKind.Application,
    bool mostRecent = false) => new(id, name, kind, true, mostRecent);

static RecentAppsWidget Create(FakeActivityHost fake) =>
    WidgetTestHost.Attach(new RecentAppsWidget(), fake.Build());
static async Task Visible(RecentAppsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
static async Task Interactive(RecentAppsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
static async Task Background(RecentAppsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Nodes(child)) yield return descendant;
}

static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 2000)
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
            "src", "FirstPartyWidgets", "RecentAppsWidget");
        if (Directory.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException();
}

file sealed class FakeActivityHost
{
    private readonly Channel<WidgetRecentActivitiesChanged> _events =
        Channel.CreateUnbounded<WidgetRecentActivitiesChanged>();
    private int _subscriptionCount;
    private int _canceledSubscriptions;
    internal IReadOnlyList<WidgetRecentActivity> Activities { get; set; } = [];
    internal Exception? ReadException { get; set; }
    internal List<string> CallOrder { get; } = [];
    internal int SubscriptionCount => Volatile.Read(ref _subscriptionCount);
    internal int CanceledSubscriptions => Volatile.Read(ref _canceledSubscriptions);

    internal WidgetHostServices Build() => new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetRecentActivityCapabilities.GetRecent, GetAsync)
        .WithEventStream(WidgetRecentActivityCapabilities.Changed, Subscribe)
        .Build();

    internal void Publish(IReadOnlyList<WidgetRecentActivity> activities) =>
        _events.Writer.TryWrite(new(activities));

    private ValueTask<IReadOnlyList<WidgetRecentActivity>> GetAsync(
        WidgetCapabilityQuery request, CancellationToken cancellationToken)
    {
        lock (CallOrder) CallOrder.Add("get");
        if (ReadException is not null)
            return ValueTask.FromException<IReadOnlyList<WidgetRecentActivity>>(ReadException);
        return ValueTask.FromResult(Activities);
    }

    private IAsyncEnumerable<WidgetRecentActivitiesChanged> Subscribe(
        CancellationToken cancellationToken)
    {
        lock (CallOrder) CallOrder.Add("subscribe");
        Interlocked.Increment(ref _subscriptionCount);
        return ReadEvents(cancellationToken);
    }

    private async IAsyncEnumerable<WidgetRecentActivitiesChanged> ReadEvents(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var change in _events.Reader.ReadAllAsync(cancellationToken))
                yield return change;
        }
        finally { Interlocked.Increment(ref _canceledSubscriptions); }
    }
}

file static class Assert
{
    internal static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }
    internal static void False(bool value) => True(!value);
    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }
    internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
