using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class PinnedDemandBridgeScenarios
{
    internal static async Task PublicationRace()
    {
        var configured = new ConfiguredWidget
        {
            Id = "pinned-demand", InstanceId = "pinned-demand.instance", Name = "Pinned demand",
            PackageId = "dev.pinned", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64), CatalogFingerprint = new('b', 64), PinningSupported = true,
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        await using var registry = new BridgeClientRegistry(new([configured]), new(),
            (value, reserve) => new WidgetProcessBridgeClient(new(new()
            {
                ExecutablePath = Environment.ProcessPath!, WidgetInstanceId = value.InstanceId,
                IsolationPolicy = WidgetWorkerIsolationPolicy.HostTrustedJobOnly, ProcessLeaseFactory = reserve,
                ConnectTimeout = TimeSpan.FromSeconds(5), RequestTimeout = TimeSpan.FromSeconds(5),
            })), (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        using (await registry.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible, token, token)) { }
        var origin = await Snapshot();
        var newer = await Snapshot();
        Check(newer.Snapshot.Sequence > origin.Snapshot.Sequence, "Probe did not advance the worker publication.");
        var input = new ControllerInputEvent(ControllerButton.View, ControllerEventPhase.Pressed,
            ControllerInputContext.PinnedLayoutSelection, SnapshotSequence: origin.Snapshot.Sequence)
        { PinnedLayoutId = "queue", IsPinnedLayoutSelected = true };
        Check(await Send(input), "Content update discarded selection through the real worker pipe.");
        Check(await Send(input), "Duplicate demand was not acknowledged.");
        var selected = await Snapshot();
        Check(selected.Snapshot.Root.Text == "selected:1", "Queue demand was lost or loaded twice.");
        using (await registry.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Background, token, token)) { }
        Check((await Snapshot()).Snapshot.Root.Text == "selected:1", "Hiding the main widget lost pinned demand.");
        Check(await Send(input with { SnapshotSequence = selected.Snapshot.Sequence, PinnedLayoutId = null, IsPinnedLayoutSelected = false }),
            "Content update discarded demand revocation.");
        Check((await Snapshot()).Snapshot.Root.Text == "idle:1", "Revocation failed to release the queue.");
        Check(!await Send(input), "A delayed superseded selection restored old demand.");
        try
        {
            using var rejected = await registry.SendControllerInputAsync(configured.Id, input,
                configured.PublicDescriptor().RuntimeGeneration, token, token,
                expectedWorkerRun: origin.WorkerRun! with { StartOrdinal = origin.WorkerRun.StartOrdinal + 1 });
            throw new InvalidOperationException("Foreign worker-run demand reached the widget.");
        }
        catch (BridgeStaleControllerInputAuthorityException) { }

        async Task<BridgeClientSnapshot> Snapshot()
        {
            using var publication = await registry.GetSnapshotAsync(configured.Id, token, token);
            return publication.Value;
        }
        async Task<bool> Send(ControllerInputEvent notification)
        {
            using var publication = await registry.SendControllerInputAsync(configured.Id, notification,
                configured.PublicDescriptor().RuntimeGeneration, token, token, expectedWorkerRun: origin.WorkerRun);
            return publication.Value;
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal sealed class Probe : Widget
    {
        private readonly PinnedLayoutHandle queue;
        private int loads;
        internal Probe() => queue = CreatePinnedLayoutHandle("queue", "Queue", new(), activeInputScopeId: "queue.root");
        public override WidgetView Render() => new(UI.Text($"{(queue.IsSelected ? "selected" : "idle")}:{loads}", "main"))
        {
            PinnedLayouts = [queue.Present(UI.Stack("queue.root", UI.Text("Queue", "queue.text")))],
        };
        public override ValueTask OnPinnedLayoutSelectionChangedAsync(string? selectedLayoutId, CancellationToken cancellationToken = default)
        {
            if (queue.IsSelected) loads++;
            return ValueTask.CompletedTask;
        }
    }
}
