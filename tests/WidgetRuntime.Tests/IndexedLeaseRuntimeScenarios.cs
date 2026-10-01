using System.IO.Pipes;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class IndexedLeaseRuntimeScenarios
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    internal static async Task ClientRoundTrip()
    {
        await using var client = NewClient(); await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        var snapshot = await client.GetSnapshotAsync();
        await using var lease = await client.AcquireIndexedRangeAsync(Request(snapshot), client.Starts);
        var changed = Observe(client);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, await Invoke(lease, ControllerButton.A, snapshot.Sequence));
        await changed.WaitAsync(Limit);
        Equal("row:first:0:open", Status(await client.GetSnapshotAsync()));
        await lease.DisposeAsync();
        await Throws<ObjectDisposedException>(async () => await Invoke(lease, ControllerButton.A, snapshot.Sequence));
        snapshot = await client.GetSnapshotAsync();
        await using var fresh = await client.AcquireIndexedRangeAsync(Request(snapshot), client.Starts);
        True(fresh.Lease.LeaseId != lease.Lease.LeaseId, "replacement lease uses a fresh ID");
        changed = Observe(client);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, await Invoke(fresh, ControllerButton.Y, snapshot.Sequence));
        await changed.WaitAsync(Limit);
        var next = await client.GetSnapshotAsync();
        Equal(2L, Request(next).Source.QueryGeneration);
        Equal("row:first:0:replace", Status(next));
    }

    internal static async Task ArtworkCancellation()
    {
        await using var client = NewClient(); await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        _ = await client.GetSnapshotAsync(); await ParentAction(client, "hold-artwork");
        await using var lease = await client.AcquireIndexedRangeAsync(Request(await client.GetSnapshotAsync()), client.Starts);
        using var cancellation = new CancellationTokenSource();
        var started = Observe(client);
        var artwork = lease.ResolveArtworkAsync("key-0", "cover", cancellation.Token);
        await started.WaitAsync(Limit);
        Equal("artwork-started", Status(await client.GetSnapshotAsync()));
        await ParentAction(client, "ping");
        Equal("ping", Status(await client.GetSnapshotAsync()));
        cancellation.Cancel();
        await Throws<OperationCanceledException>(async () => await artwork);
        True(client.IsRunning && client.Starts == 1, "artwork cancellation preserves the worker");
        await ParentAction(client, "release-artwork");
        True(await lease.ResolveArtworkAsync("key-0", "cover") is { ContentType: WidgetArtworkContentType.Png }, "artwork can reload after cancellation");
    }

    internal static async Task DuplicateAcquisitionPreservesLease()
    {
        await using var client = NewClient();
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        var snapshot = await client.GetSnapshotAsync();
        var request = Request(snapshot);
        await using var lease = await client.AcquireIndexedRangeAsync(request, client.Starts);
        await Throws<WidgetProcessException>(async () =>
            await client.AcquireIndexedRangeAsync(request, client.Starts));
        var changed = Observe(client);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued,
            await Invoke(lease, ControllerButton.A, snapshot.Sequence));
        await changed.WaitAsync(Limit);
        Equal("row:first:0:open", Status(await client.GetSnapshotAsync()));
        True(await lease.ResolveArtworkAsync("key-0", "cover") is { ContentType: WidgetArtworkContentType.Png },
            "a rejected duplicate acquisition must preserve the original artwork lease");
        Equal(1, client.Starts);
    }

    internal static async Task ReleaseDuringArtwork()
    {
        await using var client = NewClient(); await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        _ = await client.GetSnapshotAsync(); await ParentAction(client, "hold-artwork");
        var lease = await client.AcquireIndexedRangeAsync(Request(await client.GetSnapshotAsync()), client.Starts);
        var started = Observe(client); var artwork = lease.ResolveArtworkAsync("key-0", "cover");
        await started.WaitAsync(Limit);
        await lease.DisposeAsync();
        await Throws<OperationCanceledException>(async () => await artwork);
        True(client.IsRunning, "release drains artwork without killing the worker");
        _ = await client.GetSnapshotAsync();
    }

    internal static async Task ReplacedWorker()
    {
        await using var client = NewClient(); await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        var snapshot = await client.GetSnapshotAsync();
        var old = await client.AcquireIndexedRangeAsync(Request(snapshot), client.Starts);
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
        await client.UnloadAsync();
        await Throws<InvalidOperationException>(async () => await Invoke(old, ControllerButton.A, snapshot.Sequence));
        Equal(1, client.Starts);
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        var next = await client.GetSnapshotAsync();
        Equal(2, client.Starts);
        await using var current = await client.AcquireIndexedRangeAsync(Request(next), client.Starts);
        await old.DisposeAsync();
        var changed = Observe(client);
        Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, await Invoke(current, ControllerButton.A, next.Sequence));
        await changed.WaitAsync(Limit);
        Equal("row:first:0:open", Status(await client.GetSnapshotAsync()));
    }

    internal static async Task DeliveredCancellation()
    {
        await using var fixture = await PipeFixture.Create();
        var request = new IndexedCollectionRangeRequest("items", fixture.Widget.Source.Descriptor, 0, 1, "delivered");
        await fixture.Send(3, MessageTypes.AcquireIndexedRange, request);
        var reply = await fixture.Read(3); Equal(MessageTypes.IndexedLease, reply.Type);
        var lease = RuntimeJson.FromElement<IndexedCollectionLease>(reply.Payload);
        await fixture.Send(4, MessageTypes.CancelIndexedRange, request);
        True((await fixture.Read(4)).Payload.GetProperty("cancelled").GetBoolean(), "late cancellation withdraws a delivered lease");
        await fixture.Send(5, MessageTypes.IndexedInput, new IndexedInputPayload(new(new(lease.LeaseId, "key-0"), ControllerButton.A), new("root", fixture.Sequence)));
        Equal<WidgetOperationAdmission?>(null, RuntimeJson.FromElement<IndexedInputAdmissionPayload>((await fixture.Read(5)).Payload).Admission);
        True(!fixture.Widget.ReleaseIndexedRange(lease.LeaseId), "cancelled delivery released worker retention");
    }

    internal static async Task FailedDelivery()
    {
        var widget = new IndexedLeaseProbeWidget();
        await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        widget.RenderSnapshot("runtime.lease", 1);
        string? id = null;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lane = new WidgetIndexedRangeLane(widget, (response, _) =>
        {
            id = RuntimeJson.FromElement<IndexedCollectionLease>(response.Payload).LeaseId;
            throw new IOException("Test delivery failure.");
        }, _ => failed.TrySetResult(), default);
        try
        {
            True(lane.TryRead(1, new("items", widget.Source.Descriptor, 0, 1, "failed"), out _, acquire: true), "admit lease acquisition");
            await failed.Task.WaitAsync(Limit); await lane.CloseAsync();
            True(id is not null && !widget.ReleaseIndexedRange(id), "failed lease frame releases retention");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private static WidgetProcessClient NewClient() => new(new()
    {
        ExecutablePath = Environment.ProcessPath!, Arguments = ["--indexed-lease-probe"], WidgetInstanceId = "runtime.lease",
        IsolationPolicy = WidgetWorkerIsolationPolicy.HostTrustedJobOnly, RequestTimeout = TimeSpan.FromSeconds(2), ConnectTimeout = Limit,
    });
    private static IndexedCollectionRangeRequest Request(ViewSnapshot snapshot) => new("items",
        snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!, 0, 1, Guid.NewGuid().ToString("N"));
    private static string? Status(ViewSnapshot snapshot) => snapshot.Root.Children.Single(node => node.Id == "status").Text;
    private static Task<WidgetOperationAdmission?> Invoke(WidgetProcessIndexedLease lease, ControllerButton button, long sequence) =>
        lease.AdmitInputAsync(new(new(lease.Lease.LeaseId, "key-0"), button), new(lease.Lease.Range.ScopeId, sequence));
    private static Task Observe(WidgetProcessClient client)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<long>? handler = null;
        handler = (_, _) => { client.Invalidated -= handler; changed.TrySetResult(); };
        client.Invalidated += handler; return changed.Task;
    }
    private static async Task ParentAction(WidgetProcessClient client, string action)
    { var changed = Observe(client); await client.AdmitActionAsync(new(action, "release", InputScopeId: "root")); await changed.WaitAsync(Limit); }
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action().WaitAsync(Limit); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }

    private sealed class PipeFixture : IAsyncDisposable
    {
        internal readonly IndexedLeaseProbeWidget Widget = new();
        internal long Sequence;
        private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        private readonly NamedPipeServerStream pipe;
        private readonly LengthPrefixedJsonChannel channel;
        private readonly Task worker;
        private PipeFixture()
        {
            var name = "indexed-lease-" + Guid.NewGuid().ToString("N");
            pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            channel = new(pipe, WidgetRuntimeProtocol.DefaultMaximumMessageBytes);
            worker = new WidgetWorkerServer(Widget, "runtime.lease", name, sessionNonce: new string('b', 64)).RunAsync(deadline.Token);
        }
        internal static async Task<PipeFixture> Create()
        {
            var fixture = new PipeFixture(); await fixture.pipe.WaitForConnectionAsync(fixture.deadline.Token);
            _ = await fixture.channel.ReadAsync(fixture.deadline.Token);
            await fixture.Send(0, MessageTypes.HelloAccepted, new HelloAcceptedPayload());
            await fixture.Send(1, MessageTypes.SetWidgetLifecycle, new WidgetLifecyclePayload(WidgetLifecycleState.Interactive));
            _ = await fixture.Read(1);
            await fixture.Send(2, MessageTypes.Render, new RenderPayload());
            fixture.Sequence = RuntimeJson.FromElement<ViewSnapshot>((await fixture.Read(2)).Payload).Sequence;
            return fixture;
        }
        internal async Task Send<T>(long id, string type, T payload) => await channel.WriteAsync(new()
        { RequestId = id, Type = type, Payload = RuntimeJson.ToElement(payload) }, deadline.Token);
        internal async Task<RuntimeEnvelope> Read(long id)
        {
            while (true) { var result = await channel.ReadAsync(deadline.Token); if (result.RequestId == id) return result; }
        }
        public async ValueTask DisposeAsync()
        {
            deadline.Cancel(); try { await worker.WaitAsync(Limit); } catch (OperationCanceledException) { }
            pipe.Dispose(); deadline.Dispose();
        }
    }
}

internal sealed class IndexedLeaseProbeWidget : Widget
{
    internal readonly WidgetIndexedCollection<string, int> Source;
    private string status = "ready";
    private TaskCompletionSource? artworkGate;
    internal IndexedLeaseProbeWidget()
    {
        Source = CreateIndexedCollection<string, int>("source", "first", 32, new()
        {
            ReadRange = (_, start, count, _) => ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start, count).ToArray()),
            ItemKey = index => new($"key-{index}"),
            RenderItem = (query, index, context) => new Raw(new ViewNode
            {
                Id = context.Id("row"), Kind = ViewNodeKind.ActionSurface, ActionId = "open", AccessibilityLabel = query,
                FocusBackgroundArtworkHandle = "cover", ActionSurfaceOrientation = ActionSurfaceOrientation.Horizontal,
                Shortcuts = [new(ControllerButton.Y, "replace")],
                Children = [new() { Id = context.Id("title"), Kind = ViewNodeKind.Text, Text = query }],
            }),
            OnAction = (query, index, action, _) =>
            {
                if (action.ActionId == "replace") Source!.PublishQuery("next", 32);
                Volatile.Write(ref status, $"row:{query}:{index}:{action.ActionId}"); Invalidate(); return ValueTask.CompletedTask;
            },
            ResolveArtwork = async (_, _, _, token) =>
            {
                Volatile.Write(ref status, "artwork-started"); Invalidate();
                if (artworkGate is { } gate) await gate.Task.WaitAsync(token);
                return new(WidgetArtworkContentType.Png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII="));
            },
        });
    }
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (action.ActionId == "hold-artwork") artworkGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (action.ActionId == "release-artwork") { artworkGate?.TrySetResult(); artworkGate = null; }
        Volatile.Write(ref status, action.ActionId); Invalidate(); return ValueTask.CompletedTask;
    }
    public override WidgetView Render() => new(UI.Stack("root", UI.Text(Volatile.Read(ref status), "status"),
        UI.Button("Release artwork", "release-artwork", "release"), UI.CollectionList("items", Source, 64, "Items")).InputScope("root"), "release");
    private sealed record Raw(ViewNode Node) : WidgetElement(Node.Id) { internal override ViewNode ToProtocolNode() => Node; }
}
