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
    ("An older periodic read cannot undo a newly admitted FPS action", StaleRead),
    ("Correction uses opaque target IDs and automatic selection can be restored", Selection),
    ("Hidden monitor stops polling and stale lifecycle results are cancelled", Lifecycle),
    ("SDK rejects malformed and nonfinite metrics", Malformed),
    ("Broker scopes targets and gates FPS control behind interactive consent", BrokerAuthority),
};
var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
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
        await widget.OnActionAsync(new("performance.auto", "performance.pin.auto"));
        Check(fake.Requested is null, "Automatic mode not restored");
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

file sealed class Fake
{
    public int Reads, Enables;
    public string? Requested;
    public TaskCompletionSource? EnableGate, ReadGate;
    public WidgetPerformanceSnapshot Sample = new(DateTimeOffset.UtcNow, 10, 0, 2L << 30, 8L << 30, 0, null,
        "not-enabled", "target-a", [new("target-a", "Game", "Game window"), new("target-b", "Editor", "Document")]);
    public WidgetHostServices Client() => new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetPerformanceCapabilities.Get, async (request, token) =>
        {
            Requested = request.TargetId; Interlocked.Increment(ref Reads);
            if (ReadGate is { } read) await read.Task.WaitAsync(token);
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
