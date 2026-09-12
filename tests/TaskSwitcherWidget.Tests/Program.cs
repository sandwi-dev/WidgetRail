using WidgetRail.FirstPartyWidgets.TaskSwitcher;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Root scope, per-window actions and separate windows", Routing),
    ("Close request preserves the window until Windows reports it gone", CloseRefresh),
    ("Switch errors are themed and leave the list usable", Failure),
    ("Stable periodic ordering and explicit refresh ordering", Ordering),
    ("Hidden widget stops refreshing and reopen reads current windows", Lifecycle),
    ("Malformed window data cannot cross the SDK boundary", Malformed),
};
foreach (var test in tests) { await test.Run(); Console.WriteLine("PASS " + test.Name); }
Console.WriteLine($"TaskSwitcherWidget.Tests passed ({tests.Length} tests)");

static async Task Routing()
{
    var fake = new Fake();
    var widget = fake.Create();
    await Activate(widget);
    var snapshot = Snapshot(widget);
    Check(snapshot.Root.InputScopeId == snapshot.ActiveInputScopeId && snapshot.ActiveInputScopeId == "tasks");
    Check(snapshot.InitialFocusId == "window-a");
    Check(snapshot.ProtocolVersion == ProtocolConstants.WindowPreviewVersion);
    Check(Nodes(snapshot.Root).Count(node => node.Kind == ViewNodeKind.WindowPreview) == 2);
    Check(Nodes(snapshot.Root).Where(node => node.Kind == ViewNodeKind.WindowPreview)
        .All(node => !node.IsFocusable && node.ImageFit == ImageFit.Contain));
    Check(Nodes(snapshot.Root).Any(node => node.Kind == ViewNodeKind.Grid));
    Check(Nodes(snapshot.Root).Count(node => node.Kind == ViewNodeKind.ActionSurface) == 2);
    Check(Nodes(snapshot.Root).Single(node => node.Id == "window-a").Shortcuts.Single().ActionId == "close.window-a");
    await widget.OnActionAsync(new("switch.window-b", "window-b"));
    Check(fake.Switched == "window-b");
    Check(!await widget.OnControllerInputAsync(new(ControllerButton.B, ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget)));
    await Hide(widget);
}
static async Task CloseRefresh()
{
    var fake = new Fake(); var widget = fake.Create(); await Activate(widget);
    await widget.OnActionAsync(new("close.window-a", "window-a"));
    Check(fake.Closed == "window-a");
    Check(Nodes(Snapshot(widget).Root).Any(node => node.Id == "window-a"));
    fake.Windows = [fake.Windows[1]];
    await widget.OnActionAsync(new("refresh", "tasks.refresh"));
    Check(!Nodes(Snapshot(widget).Root).Any(node => node.Id == "window-a"));
    Check(Snapshot(widget).InitialFocusId == "window-b");
    await Hide(widget);
}
static async Task Failure()
{
    var fake = new Fake { Error = new WidgetCapabilityException("window_switch_denied", "private native details") };
    var widget = fake.Create(); await Activate(widget);
    await widget.OnActionAsync(new("switch.window-a", "window-a"));
    var snapshot = Snapshot(widget);
    Check(Nodes(snapshot.Root).Single(node => node.Id == "tasks.toast").StyleClasses.Contains("wrail-toast--danger"));
    Check(!Nodes(snapshot.Root).Any(node => node.Text?.Contains("private") == true));
    Check(Nodes(snapshot.Root).Any(node => node.Id == "window-a"));
    await Hide(widget);
}
static async Task Ordering()
{
    var fake = new Fake(); var widget = fake.Create(); await Activate(widget);
    fake.Windows = fake.Windows.Reverse().ToArray();
    var before = fake.Reads;
    await WaitUntil(() => fake.Reads > before);
    Check(Nodes(Snapshot(widget).Root).First(node => node.Kind == ViewNodeKind.ActionSurface).Id == "window-a");
    await widget.OnActionAsync(new("refresh", "tasks.refresh"));
    Check(Nodes(Snapshot(widget).Root).First(node => node.Kind == ViewNodeKind.ActionSurface).Id == "window-b");
    await Hide(widget);
}
static async Task Lifecycle()
{
    var fake = new Fake(); var widget = fake.Create(); await Activate(widget); await Hide(widget);
    var reads = fake.Reads;
    await Task.Delay(2200);
    Check(fake.Reads == reads);
    fake.Windows = [];
    await Activate(widget);
    Check(Snapshot(widget).InitialFocusId == "tasks.refresh");
    await Hide(widget);
}
static async Task Malformed()
{
    var fake = new Fake { Windows = [new("bad id", "App", "Title", false)] };
    var widget = fake.Create(); await Activate(widget);
    Check(!Nodes(Snapshot(widget).Root).Any(node => node.Kind == ViewNodeKind.ActionSurface));
    Check(Snapshot(widget).InitialFocusId == "tasks.refresh");
    await Hide(widget);
}
static ViewSnapshot Snapshot(TaskSwitcherWidget widget) => widget.Render().CreateSnapshot("tasks.test", 1);
static IEnumerable<ViewNode> Nodes(ViewNode root)
{
    yield return root;
    foreach (var child in root.Children) foreach (var node in Nodes(child)) yield return node;
}
static async Task Activate(TaskSwitcherWidget widget) => await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
static async Task Hide(TaskSwitcherWidget widget) => await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
static void Check(bool condition) { if (!condition) throw new Exception("Task Switcher assertion failed."); }
static async Task WaitUntil(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition()) await Task.Delay(20, timeout.Token);
}
file sealed class Fake
{
    internal IReadOnlyList<WidgetTaskWindow> Windows = [new("window-a", "Editor", "Document A", false), new("window-b", "Editor", "Document B", true)];
    internal int Reads;
    internal string? Switched;
    internal string? Closed;
    internal Exception? Error;
    internal TaskSwitcherWidget Create() => WidgetTestHost.Attach(new TaskSwitcherWidget(),
        new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetTaskSwitcherCapabilities.List, (request, token) =>
            {
                Interlocked.Increment(ref Reads);
                return ValueTask.FromResult(Windows);
            })
            .WithHandler(WidgetTaskSwitcherCapabilities.Switch, (request, token) =>
            {
                if (Error is not null) throw Error;
                Switched = request.WindowId;
                return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
            })
            .WithHandler(WidgetTaskSwitcherCapabilities.Close, (request, token) =>
            {
                Closed = request.WindowId;
                return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
            }).Build());
}
