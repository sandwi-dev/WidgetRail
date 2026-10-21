using System.Text.Json;
using WidgetRail.FirstPartyWidgets.PerformanceMonitor;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Production manifest admits the implemented compact pin", ManifestPin),
    ("Monitor and compact pin are valid controller surfaces with unavailable distinct from zero", Presentation),
    ("FPS enablement is explicit and button focus survives its busy state", Enablement),
    ("Missing FPS access asks on action, and only approval starts the collector", PermissionContinuation),
    ("An older periodic read cannot undo a newly admitted FPS action", StaleRead),
    ("Correction uses opaque target IDs and automatic selection can be restored", Selection),
    ("Manual application selection survives reopening before fresh readings", ReopenSelection),
    ("Manual application selection survives read failures and reconciles a closed target", FailedReadSelection),
    ("Hidden monitor stops polling and stale lifecycle results are cancelled", Lifecycle),
    ("SDK rejects malformed and nonfinite metrics", Malformed),
    ("Broker scopes targets and gates FPS control behind interactive consent", BrokerAuthority),
    ("Graphs preserve gaps, reject malformed data and require native protocol support", GraphContract),
    ("Frame statistics reach the main view and compact read-only pin", StatisticsPresentation),
};
var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error)
    {
        failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error);
        if (error is ProtocolValidationException validation) Console.Error.WriteLine(JsonSerializer.Serialize(validation.Errors));
    }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static Task ManifestPin()
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "FirstPartyWidgets", "PerformanceMonitorWidget", "manifest.json"))) root = root.Parent;
    if (root is null) throw new Exception("Repository manifest was not found");
    using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "src", "FirstPartyWidgets", "PerformanceMonitorWidget", "manifest.json")));
    Check(json.RootElement.TryGetProperty("pinningSupported", out var pinning) && pinning.GetBoolean(), "The host would reject the compact pin");
    return Task.CompletedTask;
}
static ViewSnapshot Snapshot(PerformanceMonitorWidget widget) => widget.Render().CreateSnapshot("performance-test", 1);
static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
static ViewNode Node(PerformanceMonitorWidget widget, string id) => Walk(Snapshot(widget).Root).Single(n => n.Id == id);
static async Task Activate(PerformanceMonitorWidget widget)
{
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await Until(() => Node(widget, "performance.app").Text == "Game");
}
static Task Hide(PerformanceMonitorWidget widget) => WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background).AsTask();
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Until(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    while (!condition()) await Task.Delay(10, timeout.Token);
}
static async Task Presentation()
{
    var fake = new Fake(); var widget = fake.Create(); await Activate(widget);
    try
    {
        var snapshot = Snapshot(widget);
        Check(WinUiPresentationContract.Validate(snapshot).Count == 0 && ViewSnapshotValidator.Validate(snapshot).Count == 0,
            string.Join("; ", ViewSnapshotValidator.Validate(snapshot)));
        Check(snapshot.PinnedLayouts.Count == 1, "Missing compact pin");
        Check(!Walk(snapshot.PinnedLayouts[0].Root!).Any(n => n.IsFocusable), "Pinned display still contains actions");
        Check(Walk(snapshot.Root).Count(n => n.Kind == ViewNodeKind.LineGraph) == 6, "Missing live graphs");
        Check(Node(widget, "performance.fps.metric.value").Text == "—", "Unknown FPS shown as zero");
        Check(Node(widget, "performance.gpu.value").Text == "0%", "Measured zero shown as unavailable");
        Check(fake.Enables == 0, "Read requested UAC");
    }
    finally { await Hide(widget); }
}
static async Task Enablement()
{
    var fake = new Fake { EnableGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    var widget = fake.Create(); await Activate(widget);
    try
    {
        var operation = widget.OnActionAsync(new("performance.fps", "performance.fps.control")).AsTask();
        await Until(() => fake.Enables == 1);
        Check(Node(widget, "performance.fps.control").IsBusy == true && Node(widget, "performance.fps.control").IsDisabled != true,
            "Enablement removed controller focus");
        fake.EnableGate.TrySetResult(); await operation;
        Check(Node(widget, "performance.fps.control").Id == "performance.fps.control", "FPS control replaced its identity");
    }
    finally { fake.EnableGate.TrySetResult(); await Hide(widget); }
}
static async Task Selection()
{
    var fake = new Fake(); var widget = fake.Create(); await Activate(widget);
    try
    {
        await widget.OnActionAsync(new("performance.target.target-b", "performance.target"));
        Check(fake.Requested == "target-b", "Selected target not forwarded");
        await widget.OnActionAsync(new("performance.auto", "performance.target"));
        Check(fake.Requested is null, "Automatic mode not restored");
    }
    finally { await Hide(widget); }
}
static void SelectedApplication(PerformanceMonitorWidget widget, string expected)
{
    var snapshot = Snapshot(widget);
    Check(ViewSnapshotValidator.Validate(snapshot).Count == 0 && WinUiPresentationContract.Validate(snapshot).Count == 0,
        "Application selection produced an invalid presentation");
    var options = Walk(snapshot.Root).Single(node => node.Id == "performance.target").SelectOptions;
    Check(options.Single(option => option.IsSelected).Id == expected, "Application selection changed unexpectedly");
}
static async Task ReopenSelection()
{
    var fake = new Fake(); var widget = fake.Create(); await Activate(widget);
    try
    {
        await widget.OnActionAsync(new("performance.target.target-b", "performance.target"));
        await Hide(widget);
        fake.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        SelectedApplication(widget, "target-b");
        Check(Node(widget, "performance.cpu.value").Text == "—", "Reopening displayed stale readings as current");
        fake.ReadGate.TrySetResult();
        await Until(() => Node(widget, "performance.cpu.value").Text != "—");
        SelectedApplication(widget, "target-b");
    }
    finally { fake.ReadGate?.TrySetResult(); await Hide(widget); }
}
static async Task FailedReadSelection()
{
    var fake = new Fake(); var widget = fake.Create(); await Activate(widget);
    try
    {
        await widget.OnActionAsync(new("performance.target.target-b", "performance.target"));
        fake.FailRead = true;
        await widget.OnActionAsync(new("performance.refresh", "performance.refresh"));
        SelectedApplication(widget, "target-b");
        Check(Node(widget, "performance.cpu.value").Text == "—", "Read failure retained stale readings");
        fake.FailRead = false;
        fake.Sample = fake.Sample with { Targets = [fake.Sample.Targets[0]] };
        await widget.OnActionAsync(new("performance.refresh", "performance.refresh"));
        SelectedApplication(widget, "auto");
        Check(Node(widget, "performance.target").SelectOptions.All(option => option.Id != "target-b"),
            "Closed application remained in the refreshed picker");
    }
    finally { await Hide(widget); }
}
static Task GraphContract()
{
    var graph = new LineGraphDefinition([new(-60, 10), new(-30, null), new(0, 20)], -60, 0, 0, 100, "%");
    var snapshot = new WidgetView(UI.LineGraph("CPU usage", graph, "test.graph")).CreateSnapshot("graph-test", 1);
    Check(snapshot.ProtocolVersion == ProtocolConstants.LineGraphVersion && !snapshot.Root.IsFocusable, "Graph focus or version is wrong");
    Check(WinUiPresentationContract.Validate(snapshot).Count == 0 && snapshot.Root.Graph!.Points[1].Value is null, "Gap lost in protocol");
    Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 77 }).Any(e => e.Code == "feature_requires_version"), "Old host admitted graph");
    foreach (var invalid in new[] {
        graph with { Points = [new(0, double.NaN)] }, graph with { Points = [new(0, double.PositiveInfinity)] },
        graph with { Points = [new(-1, 1), new(-2, 2)] }, graph with { Points = [new(0, 101)] },
        graph with { Points = [new(0, 1), new(0, 2)] }, graph with { XMaximum = -60 },
        graph with { Points = Enumerable.Range(0, 121).Select(i => new GraphPoint(-60 + i / 2d, 1)).ToArray() } })
        Check(!invalid.IsWellFormed(), "Invalid graph accepted");
    var forged = snapshot with { Root = snapshot.Root with { Kind = ViewNodeKind.Text } };
    Check(ViewSnapshotValidator.Validate(forged).Any(e => e.Code == "invalid_graph_owner"), "Graph attached to another element");
    Check(PresentationPropertyMetadata.Impact(PresentationProperty.Graph) ==
        (PresentationPropertyImpact.Paint | PresentationPropertyImpact.Accessibility), "Graph sample update unnecessarily changes input/layout authority");
    return Task.CompletedTask;
}
static async Task StatisticsPresentation()
{
    var fake = new Fake();
    fake.Sample = fake.Sample with { FpsState = "collecting", FramesPerSecond = 120,
        FrameStatistics = new(100, 50, 10, 20, 5000, 50) };
    var widget = fake.Create(); await Activate(widget);
    try
    {
        Check(Node(widget, "performance.average.value").Text == "100", "Average missing");
        Check(Node(widget, "performance.low.value").Text == "50", "Low missing");
        Check(Node(widget, "performance.frame.graph").Graph!.Points.Last().Value == 20, "Frame spike missing");
        fake.Sample = fake.Sample with { SelectedTargetId = "target-b", FramesPerSecond = 60, FrameStatistics = null };
        await Task.Delay(1000);
        await widget.OnActionAsync(new("performance.target.target-b", "performance.target"));
        var points = Node(widget, "performance.fps.graph").Graph!.Points;
        Check(points.Last().Value == 60 && points.Take(points.Count - 1).All(p => p.Value is null), "Another application's history remained visible");
    }
    finally { await Hide(widget); }
}
static async Task StaleRead()
{
    var fake = new Fake(); var widget = fake.Create(); await Activate(widget);
    try
    {
        fake.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = fake.Reads;
        await Until(() => fake.Reads > before);
        await widget.OnActionAsync(new("performance.fps", "performance.fps.control"));
        Check(Node(widget, "performance.fps.control").IsBusy == true, "Enable state was not published");
        fake.ReadGate.TrySetResult();
        await Task.Delay(100);
        Check(Node(widget, "performance.fps.control").IsBusy == true, "Stale read reverted the enable state");
    }
    finally { fake.ReadGate?.TrySetResult(); await Hide(widget); }
}
static async Task Lifecycle()
{
    var fake = new Fake(); var widget = fake.Create(); await Activate(widget); await Hide(widget);
    var reads = fake.Reads; await Task.Delay(1200); Check(fake.Reads == reads, "Hidden widget kept sampling");
    fake.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await Until(() => fake.Reads > reads);
    await Hide(widget).WaitAsync(TimeSpan.FromSeconds(2));
    fake.ReadGate.TrySetResult();
}
static async Task Malformed()
{
    var fake = new Fake();
    var services = fake.Client();
    fake.Sample = fake.Sample with { CpuPercent = double.NaN };
    try { await services.Performance.GetAsync(); throw new Exception("Malformed metric accepted"); }
    catch (WidgetCapabilityException error) { Check(error.ErrorCode == "malformed_response", "Unexpected validation result"); }
}
static async Task BrokerAuthority()
{
    var root = Path.Combine(Path.GetTempPath(), "performance-broker-" + Guid.NewGuid().ToString("N"));
    try
    {
        var identity = new BrokerWidgetIdentity("dev.test.performance", "dev.test", "one");
        var consent = new ConsentStore(root);
        var caps = new[] { PlatformCapabilities.PerformanceReadV1, PlatformCapabilities.PerformanceFpsControlV1 };
        var backend = new SimulatedPlatformBrokerBackend
        {
            Performance = new(DateTimeOffset.UtcNow, 10, 0, 100, 1000, 0, 60, "collecting", "private-native-id", [new("private-native-id", "Game", "Game")]),
        };
        await using var broker = new PlatformCapabilityBroker(identity, caps, consent, backend);
        broker.SetLifecycle(BrokerLifecycleState.Visible);
        long sequence = 0;
        Task<JsonElement> Send(string cap, string operation, object value) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion,
            ++sequence, identity, cap, operation, JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        try { await Send(caps[0], PlatformCapabilities.PerformanceGet, new PerformanceRequest()); throw new Exception("Missing consent accepted"); }
        catch (BrokerException) { }
        await consent.SetDecisionAsync(identity, caps[0], ConsentDecision.Grant);
        var response = await Send(caps[0], PlatformCapabilities.PerformanceGet, new PerformanceRequest());
        Check(!response.GetRawText().Contains("private-native-id"), "Native target identity leaked");
        var snapshot = response.Deserialize<PerformanceSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await Send(caps[0], PlatformCapabilities.PerformanceGet, new PerformanceRequest(snapshot.SelectedTargetId));
        Check(backend.PerformanceTargetRequested == "private-native-id", "Opaque target was not resolved");
        await consent.SetDecisionAsync(identity, caps[1], ConsentDecision.Grant);
        try { await Send(caps[1], PlatformCapabilities.PerformanceEnableFps, new { }); throw new Exception("Background-like control accepted"); }
        catch (BrokerException) { }
        Check(backend.PerformanceEnableRequests == 0, "UAC requested from noninteractive lifecycle");
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await Send(caps[1], PlatformCapabilities.PerformanceEnableFps, new { });
        Check(backend.PerformanceEnableRequests == 1, "Interactive request did not reach provider");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static async Task PermissionContinuation()
{
    var fake = new Fake { Granted = false }; var widget = fake.Create(); await Activate(widget);
    try
    {
        Check(fake.PermissionRequests == 0, "Activation requested permission");
        await widget.OnActionAsync(new("performance.fps", "performance.fps"));
        await Until(() => fake.PermissionRequests == 1);
        Check(fake.Enables == 0, "Denied action started the collector");
        await Until(() => Walk(Snapshot(widget).Root).Any(node => node.Text?.Contains("Access was not allowed") == true));
        fake.PermissionOutcome = WidgetPermissionOutcome.Granted;
        await widget.OnActionAsync(new("performance.fps", "performance.fps"));
        await Until(() => fake.Enables == 1);
        Check(fake.PermissionRequests == 2, "Approved action did not resume exactly once");
    }
    finally { await Hide(widget); }
}

file sealed class Fake
{
    public int Reads, Enables;
    public bool Granted = true;
    public bool FailRead;
    public int PermissionRequests;
    public WidgetPermissionOutcome PermissionOutcome = WidgetPermissionOutcome.Denied;
    public string? Requested;
    public TaskCompletionSource? EnableGate, ReadGate;
    public WidgetPerformanceSnapshot Sample = new(DateTimeOffset.UtcNow, 10, 0, 2L << 30, 8L << 30, 0, null,
        "not-enabled", "target-a", [new("target-a", "Game", "Game window"), new("target-b", "Editor", "Document")]);
    public WidgetHostServices Client() => new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetPermissionCapabilities.Check, (query, _) => ValueTask.FromResult(
                new WidgetPermissionStatuses(query.CapabilityIds.Select(id => new WidgetPermissionStatus(id, Granted ? WidgetPermissionState.Granted : WidgetPermissionState.NotDecided)).ToArray())))
        .WithHandler(WidgetPermissionCapabilities.Request, (_, _) => { PermissionRequests++; return ValueTask.FromResult(new WidgetPermissionRequestState(PermissionOutcome)); })
        .WithHandler(WidgetPerformanceCapabilities.Get, async (request, token) =>
        {
            Requested = request.TargetId; Interlocked.Increment(ref Reads);
            if (ReadGate is { } read) await read.Task.WaitAsync(token);
            if (FailRead) throw new WidgetCapabilityException("unavailable", "Fixture read failed.");
            return Sample;
        })
        .WithHandler(WidgetPerformanceCapabilities.EnableFps, async (_, token) =>
        {
            Interlocked.Increment(ref Enables);
            if (EnableGate is { } gate) await gate.Task.WaitAsync(token);
            return new WidgetPerformanceEnableResult("enabling");
        })
        .WithHandler(WidgetPerformanceCapabilities.DisableFps, (_, _) => ValueTask.FromResult(new WidgetPerformanceEnableResult("not-enabled")))
        .Build();
    public PerformanceMonitorWidget Create() => WidgetTestHost.Attach(new PerformanceMonitorWidget(), Client());
}
