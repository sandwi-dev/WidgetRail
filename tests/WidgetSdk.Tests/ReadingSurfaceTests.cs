using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ReadingSurfaceTests
{
    internal static async Task Contract()
    {
        var element = UI.ReadingSurface("reading", "Details", ActionSurfaceOrientation.Vertical, UI.Text("Description", "description"));
        var snapshot = new WidgetView(element, "reading").CreateSnapshot("reading.test", 1);
        Check(snapshot.Root.IsFocusable && snapshot.Root.ActionId is null);
        Check(snapshot.ProtocolVersion == ProtocolConstants.ReadingSurfaceVersion);
        Check(ViewSnapshotValidator.Validate(snapshot).Count == 0);
        Check(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = ProtocolConstants.ReadingSurfaceVersion - 1 }).Count != 0);
        try { element.Shortcut(ControllerButton.A); throw new Exception("Reading surface inferred an activation."); }
        catch (ArgumentException) { }
        var widget = new Fixture();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "reading.indexed");
        using var lease = await host.AcquireAsync("rows", 0, 1);
        Check(lease.RouteAction("entry", ControllerButton.A) != WidgetOperationAdmission.Enqueued);
        Check(lease.RouteAction("entry", ControllerButton.LeftStick) == WidgetOperationAdmission.Enqueued);
        await widget.Search.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WidgetTestHost.DestroyAsync(widget);
    }
    private static void Check(bool value) { if (!value) throw new Exception("Reading surface contract failed."); }
    private sealed class Fixture : Widget
    {
        internal readonly TaskCompletionSource Search = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly WidgetIndexedCollection<string, string> rows;
        internal Fixture() => rows = CreateIndexedCollection<string, string>("reading.source", "query", 1, new()
        {
            ReadRange = (_, _, _, _) => ValueTask.FromResult<IReadOnlyList<string>>(["entry"]),
            ItemKey = item => new(item),
            RenderItem = (_, _, ids) => UI.ReadingSurface(ids.Id("reading"), "Details", ActionSurfaceOrientation.Vertical, UI.Text("Read", ids.Id("text"))),
            OnAction = (_, _, action, _) => { if (action.ActionId == "search") Search.TrySetResult(); return ValueTask.CompletedTask; },
        });
        public override WidgetView Render() => new(UI.Stack("root", UI.CollectionList("rows", rows, 80, "Entries"))
            .Shortcut(ControllerButton.LeftStick, "search", "Search"));
        public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
        { if (action.ActionId == "search") Search.TrySetResult(); return ValueTask.CompletedTask; }
    }
}
