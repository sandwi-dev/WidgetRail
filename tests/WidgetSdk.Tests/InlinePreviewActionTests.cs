using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class InlinePreviewActionTests
{
    internal static async Task Run()
    {
        foreach (var change in new[] { "unchanged", "value", "disabled", "passive", "source", "fragment", "moved", "remove-restore" })
        {
            var widget = new Fixture();
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            try
            {
                var first = widget.RenderSnapshot("inline", 1);
                Check(first.ProtocolVersion >= ProtocolConstants.InlinePreviewActionVersion, "Worker support must be advertised.");
                widget.AdmitAction(new("block", "block"));
                await widget.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var action = new WidgetActionEvent("commit", "choice", InputScopeId: "root") { FocusedElementId = "choice" };
                Check(widget.AdmitInlinePreviewAction(new(1, action)) == WidgetOperationAdmission.Enqueued, "Preview should enter the bounded queue.");
                widget.Change = change;
                widget.RenderSnapshot("inline", 2);
                if (change == "remove-restore") { widget.Change = "unchanged"; widget.RenderSnapshot("inline", 3); }
                widget.AdmitAction(new("barrier", "barrier"));
                widget.Release.TrySetResult();
                await widget.Barrier.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(widget.Commits == (change is "unchanged" or "value" ? 1 : 0), "Incorrect queued authority for " + change);
            }
            finally { widget.Release.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Fixture : Widget
    {
        internal string Change = "unchanged";
        internal int Commits;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override WidgetView Render()
        {
            var source = UI.Button("Source", "open", Change == "source" ? "other-source" : "source").Disabled(Change == "disabled");
            WidgetElement content = Change == "remove-restore" ? source : source.PresentOnFocus(
                UI.Stack(Change == "fragment" ? "other-fragment" : "fragment", Change == "passive" ? UI.Text("Read only", "choice") : UI.Select("Choice",
                    [new("first", "First", "first", Change != "value"), new("second", "Second", "commit", Change == "value")], "choice")));
            return new(UI.Stack("root", UI.FocusPresentationSurface(
                UI.Stack(Change == "moved" ? "other-parent" : "parent", content), UI.Text("Empty", "empty"), "surface") with
                { Preview = new() { EnterButton = Change == "passive" ? null : ControllerButton.X } }));
        }
        public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken token = default)
        {
            if (action.ActionId == "block") { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            else if (action.ActionId == "commit") Commits++;
            else if (action.ActionId == "barrier") Barrier.TrySetResult();
        }
    }
}
