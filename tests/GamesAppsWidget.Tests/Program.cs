using GameBarAlternative.FirstPartyWidgets.GamesApps;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Visible lifecycle loads one bounded opaque catalog page", LoadsFirstPage),
    ("Installed apps render as a horizontal controller strip", RendersControllerStrip),
    ("Interactive A launches only the selected opaque app", LaunchesSelectedApp),
    ("Load more appends a bounded page and restores focus forward", LoadsMore),
    ("Rapid repeated load more is one busy controller command", LoadMoreIsSingleFlight),
    ("Pagination stops exactly at the bounded catalog maximum", PaginationStopsAtMaximum),
    ("Rapid repeated launch cannot duplicate a Shell launch", LaunchIsSingleFlight),
    ("Leaving the widget cancels in-flight page work", BackgroundCancelsPageWork),
    ("Denied optional launch keeps the readable library usable", LaunchDenialKeepsLibrary),
    ("Permission and provider failures stay recoverable and sanitized", FailureStates),
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
Console.WriteLine($"GamesAppsWidget.Tests passed ({tests.Length} tests)");

static async Task LoadsFirstPage()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("one", "One")], null) },
    };
    var widget = Create(fake);
    await Visible(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    Assert.Equal(1, fake.PageRequests.Count);
    Assert.Equal((0, GamesAppsWidget.PageSize), fake.PageRequests[0]);
    Assert.Equal("one", widget.Items.Single().AppId);
    await Background(widget);
}

static async Task RendersControllerStrip()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([
                App("private-one", "Alpha"),
                App("private-two", "Beta", WidgetAppLibraryKind.Game),
                App("private-three", "Gamma"),
            ], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    var snapshot = Snapshot(widget, 3);
    Assert.Equal(WidgetSurfaceMode.Standard, snapshot.Surface!.Mode);
    Assert.Equal(420d, snapshot.Surface.MinimumWidth);
    Assert.Equal(320d, snapshot.Surface.MinimumHeight);
    Assert.True(snapshot.Surface.PreferredWidth > snapshot.Surface.MinimumWidth);
    Assert.True(snapshot.Surface.PreferredHeight > snapshot.Surface.MinimumHeight);
    Assert.Equal("games-apps", snapshot.ActiveInputScopeId);
    var scroll = Nodes(snapshot.Root).Single(node => node.Id == "games.library.scroll");
    Assert.Equal(ViewNodeKind.Scroll, scroll.Kind);
    Assert.Equal(ScrollAxis.Horizontal, scroll.ScrollAxis);
    var buttons = Buttons(scroll).Where(button => button.ActionId == "games.launch").ToArray();
    Assert.Equal(3, buttons.Length);
    Assert.Equal(buttons[0].Id, buttons[0].Focus!.Left);
    Assert.Equal(buttons[1].Id, buttons[0].Focus!.Right);
    Assert.Equal(buttons[0].Id, snapshot.InitialFocusId);
    Assert.True(buttons.All(button => button.Focus!.Down is null));
    var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
    Assert.False(json.Contains("private-one", StringComparison.Ordinal));
    Assert.False(json.Contains(".lnk", StringComparison.OrdinalIgnoreCase));
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task LaunchesSelectedApp()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha"), App("opaque-b", "Beta")], null) },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.ViewState == GamesAppsViewState.Ready);
    var snapshot = Snapshot(widget, 4);
    var beta = Buttons(snapshot.Root).Single(button => button.Text == "Beta");
    await widget.OnActionAsync(new("games.launch", beta.Id));
    Assert.SequenceEqual(["opaque-b"], fake.LaunchedIds);
    Assert.Equal("opaque-b", widget.SelectedAppId);
    Assert.Contains("Opened Beta", Text(Snapshot(widget, 5).Root, "games.status").Text!);
    await Background(widget);
}

static async Task LoadsMore()
{
    var fake = new FakeAppLibraryHost
    {
        Pages =
        {
            [0] = Page([App("one", "One"), App("two", "Two")], 32),
            [32] = Page([App("three", "Three"), App("four", "Four")], null),
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Items.Count == 2);
    Assert.True(Buttons(Snapshot(widget, 6).Root).Any(button => button.Id == "games.load-more"));
    await widget.OnActionAsync(new("games.load-more", "games.load-more"));
    Assert.Equal(4, widget.Items.Count);
    Assert.Equal("three", widget.SelectedAppId);
    Assert.Equal<int?>(null, widget.NextOffset);
    var snapshot = Snapshot(widget, 7);
    Assert.True(!Buttons(snapshot.Root).Any(button => button.Id == "games.load-more"));
    Assert.Equal(4, Buttons(snapshot.Root).Count(button => button.ActionId == "games.launch"));
    Assert.Equal(Buttons(snapshot.Root).Single(button => button.Text == "Three").Id,
        snapshot.InitialFocusId);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task LoadMoreIsSingleFlight()
{
    var started = NewSignal();
    var release = new TaskCompletionSource<WidgetAppLibraryPage>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeAppLibraryHost
    {
        ReadHandler = (request, cancellationToken) => request.Offset == 0
            ? ValueTask.FromResult(Page([App("one", "One")], 32))
            : new ValueTask<WidgetAppLibraryPage>(
                AwaitPage(release.Task, started, cancellationToken)),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Items.Count == 1);

    var first = widget.OnActionAsync(new("games.load-more", "games.load-more")).AsTask();
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var busy = Snapshot(widget, 9);
    var loadMore = Buttons(busy.Root).Single(button => button.Id == "games.load-more");
    Assert.True(loadMore.IsBusy == true);
    Assert.True(loadMore.IsDisabled == true);
    Assert.True(Buttons(busy.Root).Where(button => button.ActionId == "games.launch")
        .All(button => button.IsDisabled == true));

    await widget.OnActionAsync(new("games.load-more", "games.load-more"));
    Assert.Equal(1, fake.PageRequests.Count(request => request.Offset == 32));
    release.TrySetResult(Page([App("two", "Two")], null));
    await first;
    Assert.Equal(2, widget.Items.Count);
    await Background(widget);
}

static async Task PaginationStopsAtMaximum()
{
    var fake = new FakeAppLibraryHost
    {
        ReadHandler = (request, _) =>
        {
            var items = Enumerable.Range(request.Offset, GamesAppsWidget.PageSize)
                .Select(index => App($"opaque-{index}", $"Application {index}"))
                .ToArray();
            return ValueTask.FromResult(Page(items, request.Offset + GamesAppsWidget.PageSize));
        },
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Items.Count == GamesAppsWidget.PageSize);
    while (widget.NextOffset is not null)
        await widget.OnActionAsync(new("games.load-more", "games.load-more"));

    Assert.Equal(GamesAppsWidget.MaximumItems, widget.Items.Count);
    Assert.Equal(16, fake.PageRequests.Count);
    Assert.Equal<int?>(null, widget.NextOffset);
    await Background(widget);
}

static async Task LaunchIsSingleFlight()
{
    var started = NewSignal();
    var release = new TaskCompletionSource<WidgetCapabilityAcknowledgement>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha"), App("opaque-b", "Beta")], null) },
        LaunchHandler = (_, cancellationToken) => new ValueTask<WidgetCapabilityAcknowledgement>(
            AwaitLaunch(release.Task, started, cancellationToken)),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Items.Count == 2);
    var initial = Snapshot(widget, 10);
    var alpha = Buttons(initial.Root).Single(button => button.Text == "Alpha");
    var beta = Buttons(initial.Root).Single(button => button.Text == "Beta");

    var first = widget.OnActionAsync(new("games.launch", alpha.Id)).AsTask();
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var busy = Snapshot(widget, 11);
    Assert.True(Buttons(busy.Root).Single(button => button.Text == "Alpha").IsBusy == true);
    Assert.True(Buttons(busy.Root).Where(button => button.ActionId == "games.launch")
        .All(button => button.IsDisabled == true));

    await widget.OnActionAsync(new("games.launch", beta.Id));
    Assert.SequenceEqual(["opaque-a"], fake.LaunchedIds);
    Assert.Equal("opaque-a", widget.SelectedAppId);
    release.TrySetResult(new WidgetCapabilityAcknowledgement(true));
    await first;
    Assert.Contains("Opened Alpha", Text(Snapshot(widget, 12).Root, "games.status").Text!);
    await Background(widget);
}

static async Task BackgroundCancelsPageWork()
{
    var started = NewSignal();
    var canceled = NewSignal();
    var fake = new FakeAppLibraryHost
    {
        ReadHandler = (request, cancellationToken) => request.Offset == 0
            ? ValueTask.FromResult(Page([App("one", "One")], 32))
            : new ValueTask<WidgetAppLibraryPage>(
                WaitForCancellation(started, canceled, cancellationToken)),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Items.Count == 1);

    var command = widget.OnActionAsync(new("games.load-more", "games.load-more")).AsTask();
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Background(widget);
    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await command;
    Assert.Equal(1, widget.Items.Count);
}

static async Task LaunchDenialKeepsLibrary()
{
    var fake = new FakeAppLibraryHost
    {
        Pages = { [0] = Page([App("opaque-a", "Alpha")], null) },
        LaunchException = new WidgetCapabilityException("permission_denied", "private detail"),
    };
    var widget = Create(fake);
    await Interactive(widget);
    await WaitUntil(() => widget.Items.Count == 1);
    var alpha = Buttons(Snapshot(widget, 13).Root).Single(button => button.Text == "Alpha");
    await widget.OnActionAsync(new("games.launch", alpha.Id));

    var denied = Snapshot(widget, 14);
    Assert.Equal(GamesAppsViewState.Ready, widget.ViewState);
    Assert.Equal(1, widget.Items.Count);
    Assert.Contains("Allow Games & Apps access in Settings",
        Text(denied.Root, "games.status").Text!);
    Assert.False(System.Text.Json.JsonSerializer.Serialize(denied)
        .Contains("private detail", StringComparison.Ordinal));
    await Background(widget);
}

static async Task FailureStates()
{
    foreach (var (exception, state) in new (Exception, GamesAppsViewState)[]
    {
        (new WidgetCapabilityException("permission_denied", "denied"),
            GamesAppsViewState.PermissionDenied),
        (new WidgetCapabilityException("lifecycle_denied", "paused"),
            GamesAppsViewState.LifecycleDenied),
        (new WidgetCapabilityException("platform_unavailable", "private path"),
            GamesAppsViewState.ServiceUnavailable),
    })
    {
        var fake = new FakeAppLibraryHost { ReadException = exception };
        var widget = Create(fake);
        await Visible(widget);
        await WaitUntil(() => widget.ViewState == state);
        var snapshot = Snapshot(widget, 8);
        Assert.True(Buttons(snapshot.Root).Any(button => button.ActionId == "retry"));
        Assert.False(System.Text.Json.JsonSerializer.Serialize(snapshot)
            .Contains("private path", StringComparison.Ordinal));
        Assert.Valid(snapshot);
        await Background(widget);
    }
}

static Task PackageValidates()
{
    var root = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
    Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
    Assert.SequenceEqual(["system.apps.library.read.v1"], manifest.Permissions);
    Assert.SequenceEqual(["system.apps.library.launch.v1"], manifest.OptionalPermissions);
    var package = GbssPackageLoader.Load("styles/default.gbss", new GbssFileSourceProvider(root));
    var compiled = GbssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid);
    return Task.CompletedTask;
}

static WidgetAppLibraryItem App(
    string id,
    string name,
    WidgetAppLibraryKind kind = WidgetAppLibraryKind.Application) => new(id, name, kind);

static WidgetAppLibraryPage Page(IReadOnlyList<WidgetAppLibraryItem> items, int? next) =>
    new(items, next);

static GamesAppsWidget Create(FakeAppLibraryHost fake) =>
    WidgetTestHost.Attach(new GamesAppsWidget(), fake.Build());

static async Task Visible(GamesAppsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

static async Task Interactive(GamesAppsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

static async Task Background(GamesAppsWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

static ViewSnapshot Snapshot(GamesAppsWidget widget, long sequence) =>
    widget.RenderSnapshot("games.test", sequence);

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Nodes(child)) yield return descendant;
}

static IEnumerable<ViewNode> Buttons(ViewNode node) =>
    Nodes(node).Where(candidate => candidate.Kind == ViewNodeKind.Button);

static ViewNode Text(ViewNode node, string id) => Nodes(node).Single(candidate => candidate.Id == id);

static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 3000)
{
    var deadline = Environment.TickCount64 + timeoutMilliseconds;
    while (!condition())
    {
        if (Environment.TickCount64 >= deadline) throw new TimeoutException();
        await Task.Delay(10);
    }
}

static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

static async Task<WidgetAppLibraryPage> AwaitPage(
    Task<WidgetAppLibraryPage> page,
    TaskCompletionSource started,
    CancellationToken cancellationToken)
{
    started.TrySetResult();
    return await page.WaitAsync(cancellationToken);
}

static async Task<WidgetCapabilityAcknowledgement> AwaitLaunch(
    Task<WidgetCapabilityAcknowledgement> acknowledgement,
    TaskCompletionSource started,
    CancellationToken cancellationToken)
{
    started.TrySetResult();
    return await acknowledgement.WaitAsync(cancellationToken);
}

static async Task<WidgetAppLibraryPage> WaitForCancellation(
    TaskCompletionSource started,
    TaskCompletionSource canceled,
    CancellationToken cancellationToken)
{
    started.TrySetResult();
    using var registration = cancellationToken.Register(() => canceled.TrySetResult());
    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    throw new InvalidOperationException("Cancellation should end the delayed page request.");
}

static string ProjectDirectory()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName,
            "src", "FirstPartyWidgets", "GamesAppsWidget");
        if (Directory.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException();
}

file sealed class FakeAppLibraryHost
{
    public Dictionary<int, WidgetAppLibraryPage> Pages { get; } = [];
    public List<(int Offset, int Limit)> PageRequests { get; } = [];
    public List<string> LaunchedIds { get; } = [];
    public Exception? ReadException { get; set; }
    public Exception? LaunchException { get; set; }
    public Func<WidgetAppLibraryPageRequest, CancellationToken,
        ValueTask<WidgetAppLibraryPage>>? ReadHandler { get; set; }
    public Func<LaunchWidgetAppLibraryItemRequest, CancellationToken,
        ValueTask<WidgetCapabilityAcknowledgement>>? LaunchHandler { get; set; }

    public WidgetHostServices Build() => new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetAppLibraryCapabilities.GetPage, GetPage)
        .WithHandler(WidgetAppLibraryCapabilities.Launch, Launch)
        .Build();

    private ValueTask<WidgetAppLibraryPage> GetPage(
        WidgetAppLibraryPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PageRequests.Add((request.Offset, request.Limit));
        if (ReadException is not null)
            return ValueTask.FromException<WidgetAppLibraryPage>(ReadException);
        if (ReadHandler is not null) return ReadHandler(request, cancellationToken);
        return ValueTask.FromResult(Pages.TryGetValue(request.Offset, out var page)
            ? page
            : new WidgetAppLibraryPage([], null));
    }

    private ValueTask<WidgetCapabilityAcknowledgement> Launch(
        LaunchWidgetAppLibraryItemRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LaunchException is not null)
            return ValueTask.FromException<WidgetCapabilityAcknowledgement>(LaunchException);
        LaunchedIds.Add(request.AppId);
        if (LaunchHandler is not null) return LaunchHandler(request, cancellationToken);
        return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
    }
}

file static class Assert
{
    public static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    public static void False(bool value) => True(!value);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Sequences differ.");
    }

    public static void Valid(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(" | ", errors));
    }
}
