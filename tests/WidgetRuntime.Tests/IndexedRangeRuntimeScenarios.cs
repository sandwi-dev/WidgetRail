using System.IO.Pipes;
using System.Text;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class IndexedRangeRuntimeScenarios
{
    internal static async Task ResponsiveAndCancellable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var demand = fixture.Request("held");
        await fixture.SendAsync(10, MessageTypes.ReadIndexedRange, demand);
        await fixture.Widget.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.SendAsync(11, MessageTypes.Render, new RenderPayload());
        var rendered = await fixture.ReadAsync(11);
        Equal(MessageTypes.Snapshot, rendered.Type);
        var snapshot = SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(rendered.Payload.GetRawText()));
        await fixture.SendAsync(12, MessageTypes.ControllerInput, new ControllerInputEvent(
            ControllerButton.B, ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget, "button",
            Sequence: 1, ActiveInputScopeId: snapshot.ActiveInputScopeId, SnapshotSequence: snapshot.Sequence));
        Equal(MessageTypes.ControllerInputResult, (await fixture.ReadAsync(12)).Type);
        Equal(1, fixture.Widget.InputCount);
        await fixture.SendAsync(13, MessageTypes.Action, new WidgetActionEvent("action", "button"));
        Equal(MessageTypes.Acknowledged, (await fixture.ReadAsync(13)).Type);
        await fixture.Widget.Action.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.SendAsync(14, MessageTypes.SetWidgetLifecycle, new WidgetLifecyclePayload(WidgetLifecycleState.Background));
        Equal(MessageTypes.Acknowledged, (await fixture.ReadAsync(14)).Type);
        await fixture.SendAsync(15, MessageTypes.CancelIndexedRange, demand);
        Equal(MessageTypes.Acknowledged, (await fixture.ReadAsync(15)).Type);
        Equal("indexed_range_cancelled", Error(await fixture.ReadAsync(10)));
    }

    internal static async Task BoundedAndExact()
    {
        await using var fixture = await Fixture.CreateAsync();
        for (var i = 0; i < 4; i++) await fixture.SendAsync(10+i, MessageTypes.ReadIndexedRange, fixture.Request("d"+i));
        await fixture.SendAsync(20, MessageTypes.ReadIndexedRange, fixture.Request("d0"));
        Equal("indexed_demand_duplicate", Error(await fixture.ReadAsync(20)));
        await fixture.SendAsync(21, MessageTypes.ReadIndexedRange, fixture.Request("excess"));
        Equal("indexed_range_busy", Error(await fixture.ReadAsync(21)));
        var stale = fixture.Request("d0") with { Source = fixture.Widget.Source.Descriptor with { QueryGeneration = 0 } };
        await fixture.SendAsync(22, MessageTypes.CancelIndexedRange, stale);
        var cancellation = await fixture.ReadAsync(22);
        Equal(false, cancellation.Payload.GetProperty("cancelled").GetBoolean());
        Equal(false, fixture.Widget.Release.Task.IsCompleted);
        await fixture.SendAsync(23, MessageTypes.CancelIndexedRange, fixture.Request("d0"));
        _ = await fixture.ReadAsync(23);
        Equal("indexed_range_cancelled", Error(await fixture.ReadAsync(10)));
        fixture.Widget.Release.TrySetResult();
        for (var i = 1; i < 4; i++) Equal(MessageTypes.IndexedRange, (await fixture.ReadAsync(10+i)).Type);
        await fixture.SendAsync(24, MessageTypes.ReadIndexedRange, stale with { DemandId = "stale" });
        Equal("indexed_range_failed", Error(await fixture.ReadAsync(24)));
    }

    internal static async Task CancellationRetainsProviderBound()
    {
        await using var fixture = await Fixture.CreateAsync(ignoreCancellation: true);
        for (var i = 0; i < 4; i++) await fixture.SendAsync(10+i, MessageTypes.ReadIndexedRange, fixture.Request("d"+i));
        await fixture.Widget.FourStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var i = 0; i < 4; i++)
        {
            await fixture.SendAsync(20+i, MessageTypes.CancelIndexedRange, fixture.Request("d"+i));
            _ = await fixture.ReadAsync(20+i);
            Equal("indexed_range_cancelled", Error(await fixture.ReadAsync(10+i)));
        }
        await fixture.SendAsync(30, MessageTypes.ReadIndexedRange, fixture.Request("fifth"));
        Equal("indexed_range_failed", Error(await fixture.ReadAsync(30)));
        Equal(4, fixture.Widget.ProviderStarts);
        fixture.Widget.Release.TrySetResult();
    }

    internal static async Task ChangedQueryRejectsInflight()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SendAsync(10, MessageTypes.ReadIndexedRange, fixture.Request("held"));
        await fixture.Widget.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Widget.Source.PublishQuery(1, 100);
        await fixture.SendAsync(11, MessageTypes.Render, new RenderPayload());
        Equal(MessageTypes.Snapshot, (await fixture.ReadAsync(11)).Type);
        Equal("indexed_range_failed", Error(await fixture.ReadAsync(10)));
        await fixture.SendAsync(12, MessageTypes.ReadIndexedRange, fixture.Request("bad") with { Count = 65 });
        Equal("indexed_range_invalid", Error(await fixture.ReadAsync(12)));
        fixture.Widget.Release.TrySetResult();
        await fixture.SendAsync(13, MessageTypes.ReadIndexedRange, fixture.Request("current"));
        Equal(MessageTypes.IndexedRange, (await fixture.ReadAsync(13)).Type);
    }

    internal static async Task StopDrainsReads()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SendAsync(10, MessageTypes.ReadIndexedRange, fixture.Request("held"));
        await fixture.Widget.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.SendAsync(11, MessageTypes.Stop, new { });
        Equal(MessageTypes.Acknowledged, (await fixture.ReadAsync(11)).Type);
        await fixture.Server.WaitAsync(TimeSpan.FromSeconds(2));
        Equal(true, fixture.Widget.Destroyed);
        Equal("indexed_range_cancelled", Error(await fixture.ReadAsync(10)));
    }

    internal static async Task ClientCancellationAndRecovery()
    {
        await using var client = new WidgetProcessClient(new()
        {
            ExecutablePath = Environment.ProcessPath!, Arguments = ["--indexed-range-probe"], WidgetInstanceId = "runtime.indexed",
            IsolationPolicy = WidgetWorkerIsolationPolicy.HostTrustedJobOnly,
            RequestTimeout = TimeSpan.FromSeconds(2), ConnectTimeout = TimeSpan.FromSeconds(3),
        });
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        var snapshot = await client.GetSnapshotAsync();
        var source = snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!;
        var request = new IndexedCollectionRangeRequest("items", source, 0, 2, "client.held");
        var held = client.ReadIndexedRangeAsync(request);
        // Cancellation is accepted locally even when startup/write hasn't completed.
        Equal(true, await client.CancelIndexedRangeAsync(request));
        try { await held.WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("Read should be cancelled."); }
        catch (OperationCanceledException) { }
        _ = await client.GetSnapshotAsync();
        Equal(true, client.IsRunning);
        Equal(1, client.Starts);
        await client.SendActionAsync(new("release", "release"));
        var result = await client.ReadIndexedRangeAsync(request with { DemandId = "client.ready" });
        Equal(2, result.Items.Count);
        Equal("item.0", result.Items[0].Key);
        Equal(false, await client.CancelIndexedRangeAsync(request));
    }

    private static string Error(RuntimeEnvelope envelope)
    {
        Equal(MessageTypes.Error, envelope.Type);
        return RuntimeJson.FromElement<ErrorPayload>(envelope.Payload).Code;
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(12));
        private readonly NamedPipeServerStream pipe;
        private readonly LengthPrefixedJsonChannel channel;
        private readonly Dictionary<long, RuntimeEnvelope> received = [];
        internal readonly IndexedRangeProbeWidget Widget;
        internal readonly Task Server;
        private Fixture(bool ignoreCancellation)
        {
            var name = "indexed-runtime-" + Guid.NewGuid().ToString("N");
            pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Widget = new(ignoreCancellation);
            Server = new WidgetWorkerServer(Widget, "runtime.indexed", name, sessionNonce: new string('a',64)).RunAsync(deadline.Token);
            channel = new(pipe, WidgetRuntimeProtocol.DefaultMaximumMessageBytes);
        }
        internal static async Task<Fixture> CreateAsync(bool ignoreCancellation = false)
        {
            var result = new Fixture(ignoreCancellation);
            await result.pipe.WaitForConnectionAsync(result.deadline.Token);
            _ = await result.channel.ReadAsync(result.deadline.Token);
            await result.SendAsync(0, MessageTypes.HelloAccepted, new HelloAcceptedPayload());
            await result.SendAsync(2, MessageTypes.SetWidgetLifecycle, new WidgetLifecyclePayload(WidgetLifecycleState.Interactive));
            Equal(MessageTypes.Acknowledged, (await result.ReadAsync(2)).Type);
            await result.SendAsync(1, MessageTypes.Render, new RenderPayload());
            Equal(MessageTypes.Snapshot, (await result.ReadAsync(1)).Type);
            return result;
        }
        internal IndexedCollectionRangeRequest Request(string id) => new("items", Widget.Source.Descriptor, 0, 2, id);
        internal async Task SendAsync<T>(long id, string type, T payload) => await channel.WriteAsync(new()
        { RequestId = id, Type = type, Payload = RuntimeJson.ToElement(payload) }, deadline.Token);
        internal async Task<RuntimeEnvelope> ReadAsync(long id)
        {
            if (received.Remove(id, out var stored)) return stored;
            using var responseDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            responseDeadline.CancelAfter(TimeSpan.FromSeconds(2));
            while (true)
            {
                var value = await channel.ReadAsync(responseDeadline.Token);
                if (value.RequestId == id) return value;
                if (value.RequestId != 0) received.Add(value.RequestId, value);
            }
        }
        public async ValueTask DisposeAsync()
        {
            Widget.Release.TrySetResult();
            deadline.Cancel();
            try { await Server.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (OperationCanceledException) { }
            pipe.Dispose(); deadline.Dispose();
        }
    }
}

internal sealed class IndexedRangeProbeWidget : Widget
{
    internal readonly WidgetIndexedCollection<int,int> Source;
    internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource FourStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Action = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int InputCount;
    internal int ProviderStarts;
    internal bool Destroyed;
    internal IndexedRangeProbeWidget(bool ignoreCancellation = false)
    {
        Source = CreateIndexedCollection<int,int>("source",0,100,new()
        {
            OnAction = (_, _, _, _) => ValueTask.CompletedTask,
        ReadRange = async (_,start,count,token) =>
            {
                if (Interlocked.Increment(ref ProviderStarts) == 4) FourStarted.TrySetResult();
                Started.TrySetResult();
                if (ignoreCancellation) await Release.Task; else await Release.Task.WaitAsync(token);
                return Enumerable.Range(start,count).ToArray();
            },
            ItemKey = item => new("item."+item),
            RenderItem = (_,item,context) => UI.Button("Item "+item,"item.action",context.Id("root")),
        });
    }
    public override WidgetView Render() => new(UI.Stack("root", UI.Button("Action","action","button"),
        UI.Button("Release","release","release"), UI.CollectionList("items",Source,64,"Items")),"button");
    public override ValueTask<bool> OnControllerInputAsync(ControllerInputEvent input, CancellationToken cancellationToken = default)
    { Interlocked.Increment(ref InputCount); return ValueTask.FromResult(true); }
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if(action.ActionId == "release") Release.TrySetResult();
        Action.TrySetResult(); return ValueTask.CompletedTask;
    }
    protected override ValueTask OnDestroyingAsync(CancellationToken cancellationToken)
    { Destroyed = true; return ValueTask.CompletedTask; }
}
