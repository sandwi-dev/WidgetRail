using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class TrayActionTests
{
    internal static Task UnavailableReasons()
    {
        var root = UI.Text(new string('x', 2048), "root");
        var fallback = new TrayActionOffer("start", "idle", "save");
        var view = new WidgetView(root, QuickActions: [WidgetQuickAction.ForTray("Start", fallback)])
            .WithUnavailableTrayActions("Start recording first.", "save", "start");
        var before = view.CreateSnapshot("fixture", 1);
        Check(before.UnavailableTrayActions!.Count == 1 && before.UnavailableTrayActions["save"] == "Start recording first.");
        Check(TrayActionContract.AllowsFallback(before.QuickActions, fallback, "save"));
        var after = view.WithUnavailableTrayActions("Waiting for the application.", "save").CreateSnapshot("fixture", 2);
        const string generation = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        ViewSnapshot Apply(ViewSnapshot first, ViewSnapshot next)
        {
            var batch = WidgetPresentationDiff.Create(first, next, generation, first.Sequence,
                PresentationUpdateCapabilities.Current, WidgetPresentationTransactionKind.IncrementalUpdate).Update!;
            return PresentationUpdateMaterializer.Apply(first,
                PresentationUpdateJson.Deserialize(PresentationUpdateJson.Serialize(batch)), generation);
        }
        Check(Apply(before, after).UnavailableTrayActions!["save"] == "Waiting for the application.");
        Check(Apply(after, before with { Sequence = 3, UnavailableTrayActions = null }).UnavailableTrayActions is null);
        foreach (var invalid in new[] { "", "line\nbreak", new string('x', 161) })
            Check(ViewSnapshotValidator.Validate(before with { UnavailableTrayActions = new Dictionary<string, string> { ["save"] = invalid } }).Count > 0);
        Check(ViewSnapshotValidator.Validate(before with { UnavailableTrayActions = new Dictionary<string, string> { ["start"] = "Already offered" } }).Count > 0);
        Check(ViewSnapshotValidator.Validate(before with { UnavailableTrayActions = Enumerable.Range(0, 17).ToDictionary(i => $"id{i}", _ => "Unavailable") }).Count > 0);
        return Task.CompletedTask;
    }

    internal static async Task ResidentDispatch()
    {
        var widget = new Probe();
        await WidgetTestHost.InitializeAsync(widget);
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            var snapshot = widget.RenderSnapshot("tray.fixture", 1);
            var input = new ControllerInputEvent(ControllerButton.X, ControllerEventPhase.Pressed,
                ControllerInputContext.TrayQuickAction, Sequence: 1, SnapshotSequence: snapshot.Sequence) { TrayOffer = widget.Offer };
            Check(!(await widget.DispatchTrayActionAsync(input with { Phase = ControllerEventPhase.Repeated }, default)).Handled);
            Check(!(await widget.DispatchTrayActionAsync(input with { TrayOffer = widget.Offer with { TargetKey = "other" } }, default)).Handled);
            var pending = widget.DispatchTrayActionAsync(input, default);
            Check(!(await widget.DispatchTrayActionAsync(input, default)).Handled);
            Check(widget.Count == 1 && !widget.IsActive);
            widget.Release.SetResult();
            Check((await pending).Handled);
            widget.RenderSnapshot("tray.fixture", 2);
            Check(!(await widget.DispatchTrayActionAsync(input, default)).Handled);
        }
        finally { widget.Release.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }
    }

    internal static async Task TrackedWorkSurvivesForegroundRoundTrip()
    {
        var widget = new Probe();
        await WidgetTestHost.InitializeAsync(widget);
        try
        {
            var snapshot = widget.RenderSnapshot("tray.fixture", 1);
            var input = new ControllerInputEvent(ControllerButton.X, ControllerEventPhase.Pressed,
                ControllerInputContext.TrayQuickAction, Sequence: 1, SnapshotSequence: snapshot.Sequence)
                { TrayOffer = widget.Offer, TrackTrayWork = true };
            var task = widget.DispatchTrayActionAsync(input, default).AsTask();
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            Check(!task.IsCompleted && widget.Count == 1);
            widget.Release.SetResult();
            Check((await task).Handled);
        }
        finally { widget.Release.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }
    }

    private static void Check(bool result)
    {
        if (!result) throw new InvalidOperationException("Tray dispatch admitted the wrong target, lifetime, or gesture.");
    }

    private sealed class Probe : Widget
    {
        public TrayActionOffer Offer { get; } = new("pause", "device-1");
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count { get; private set; }
        public override WidgetView Render() => new(UI.Text("Music", "root"),
            QuickActions: [new WidgetQuickAction(ControllerButton.X, "pause", "Pause") { TrayOffer = Offer }]);
        protected override async ValueTask<TrayActionResult> OnTrayActionAsync(TrayActionOffer offer, CancellationToken token)
        {
            Count++;
            await Release.Task.WaitAsync(token);
            return TrayActionResult.Success;
        }
    }
}
