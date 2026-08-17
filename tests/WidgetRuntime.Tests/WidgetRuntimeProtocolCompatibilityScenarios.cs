using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class WidgetRuntimeProtocolCompatibilityScenarios
{
    internal static async Task FrozenV2ApplicationCheckpointCompatibility()
    {
        using (var legacyDocument = JsonDocument.Parse("{}"))
        {
            var legacyRender = RuntimeJson.FromElement<RenderPayload>(legacyDocument.RootElement);
            var legacyCapabilities = WidgetWorkerServer.ValidateRenderRequest(legacyRender);
            var legacyPublication = new CompatibilityWidget().RenderPublication(
                "runtime.test",
                new string('0', 32),
                sequence: 1,
                expectedBaseSequence: 0,
                legacyCapabilities,
                legacyRender.RequireCheckpoint);
            True(legacyPublication.Update is null,
                "A legacy empty render payload must produce a complete checkpoint.");
        }

        var pipeName = $"wrail-runtime-v2-{Guid.NewGuid():N}";
        const string instance = "runtime.test";
        var sessionNonce = new string('C', 64);
        const int maximumBytes = 64 * 1024;
        await using var serverPipe = new System.IO.Pipes.NamedPipeServerStream(
            pipeName,
            System.IO.Pipes.PipeDirection.InOut,
            1,
            System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous);
        var worker = RunFrozenV2PeerAsync(pipeName, instance, sessionNonce, maximumBytes);
        var connection = serverPipe.WaitForConnectionAsync();
        var firstCompletion = await Task.WhenAny(connection, worker)
            .WaitAsync(TimeSpan.FromSeconds(2));
        if (ReferenceEquals(firstCompletion, worker))
            throw new InvalidOperationException(
                $"Frozen runtime-v2 peer exited before connecting with code {await worker}.");
        await connection;
        var channel = new LengthPrefixedJsonChannel(serverPipe, maximumBytes);

        var hello = await channel.ReadAsync(CancellationToken.None);
        Equal(2, hello.ProtocolVersion);
        Equal(MessageTypes.Hello, hello.Type);
        Equal(new HelloPayload(instance, sessionNonce),
            RuntimeJson.FromElement<HelloPayload>(hello.Payload));
        await channel.WriteAsync(new RuntimeEnvelope
        {
            ProtocolVersion = 2,
            Type = MessageTypes.HelloAccepted,
            Payload = RuntimeJson.ToElement(new { }),
        }, CancellationToken.None);

        await channel.WriteAsync(new RuntimeEnvelope
        {
            ProtocolVersion = 2,
            Type = MessageTypes.SetWidgetLifecycle,
            RequestId = 1,
            Payload = RuntimeJson.ToElement(new WidgetLifecyclePayload(WidgetLifecycleState.Visible)),
        }, CancellationToken.None);
        Equal(MessageTypes.Acknowledged,
            (await channel.ReadAsync(CancellationToken.None)).Type);

        var generation = new string('B', 32);
        await channel.WriteAsync(new RuntimeEnvelope
        {
            ProtocolVersion = 2,
            Type = MessageTypes.Render,
            RequestId = 2,
            Payload = RuntimeJson.ToElement(new RenderPayload
            {
                UpdateCapabilities = PresentationUpdateCapabilities.Current,
                BaseSequence = 1,
                PresentationGeneration = generation,
                RequireCheckpoint = false,
            }),
        }, CancellationToken.None);
        var firstResponse = await channel.ReadAsync(CancellationToken.None);
        Equal(MessageTypes.Snapshot, firstResponse.Type);
        var initial = SnapshotJson.Deserialize(
            Encoding.UTF8.GetBytes(firstResponse.Payload.GetRawText()));
        Equal("frozen-v2", Find(initial.Root, "frozen-v2").Text);

        await channel.WriteAsync(new RuntimeEnvelope
        {
            ProtocolVersion = 2,
            Type = MessageTypes.Render,
            RequestId = 3,
            // The frozen peer also tolerates the legacy empty render shape.
            Payload = RuntimeJson.ToElement(new { }),
        }, CancellationToken.None);
        var secondResponse = await channel.ReadAsync(CancellationToken.None);
        Equal(MessageTypes.Snapshot, secondResponse.Type);
        var repeated = SnapshotJson.Deserialize(
            Encoding.UTF8.GetBytes(secondResponse.Payload.GetRawText()));
        True(repeated.Sequence > initial.Sequence,
            "The frozen runtime-v2 checkpoint did not advance its sequence.");

        await channel.WriteAsync(new RuntimeEnvelope
        {
            ProtocolVersion = 2,
            Type = MessageTypes.Stop,
            RequestId = 4,
            Payload = RuntimeJson.ToElement(new { }),
        }, CancellationToken.None);
        Equal(MessageTypes.Acknowledged,
            (await channel.ReadAsync(CancellationToken.None)).Type);
        Equal(0, await worker.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static async Task<int> RunFrozenV2PeerAsync(
        string pipeName,
        string instanceId,
        string sessionNonce,
        int maximumBytes)
    {
        await using var pipe = new System.IO.Pipes.NamedPipeClientStream(
            ".", pipeName, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        await pipe.ConnectAsync();
        var channel = new LengthPrefixedJsonChannel(pipe, maximumBytes);
        await channel.WriteAsync(new RuntimeEnvelope
        {
            ProtocolVersion = 2,
            Type = MessageTypes.Hello,
            Payload = RuntimeJson.ToElement(new HelloPayload(instanceId, sessionNonce)),
        }, CancellationToken.None);
        var acceptance = await channel.ReadAsync(CancellationToken.None);
        if (acceptance.ProtocolVersion != 2 || acceptance.Type != MessageTypes.HelloAccepted)
            return 96;

        long sequence = 0;
        while (true)
        {
            var request = await channel.ReadAsync(CancellationToken.None);
            if (request.Type == MessageTypes.Render)
            {
                // A frozen v2 peer ignores optional Render fields and checkpoints.
                var snapshot = new ViewSnapshot
                {
                    ProtocolVersion = ProtocolConstants.CurrentVersion,
                    Sequence = ++sequence,
                    WidgetInstanceId = instanceId,
                    ActiveInputScopeId = "root",
                    Root = new ViewNode
                    {
                        Id = "root",
                        Kind = ViewNodeKind.Stack,
                        InputScopeId = "root",
                        Children =
                        [
                            new ViewNode
                            {
                                Id = "frozen-v2",
                                Kind = ViewNodeKind.Text,
                                Text = "frozen-v2",
                            },
                        ],
                    },
                };
                using var snapshotDocument = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
                await ReplyAsync(MessageTypes.Snapshot, request.RequestId,
                    snapshotDocument.RootElement.Clone());
                continue;
            }

            if (request.Type == MessageTypes.SetWidgetLifecycle)
            {
                await ReplyAsync(MessageTypes.Acknowledged, request.RequestId,
                    RuntimeJson.ToElement(new { }));
                continue;
            }

            if (request.Type == MessageTypes.Stop)
            {
                await ReplyAsync(MessageTypes.Acknowledged, request.RequestId,
                    RuntimeJson.ToElement(new { }));
                return 0;
            }
            return 95;
        }

        ValueTask ReplyAsync(string type, long requestId, JsonElement payload) =>
            channel.WriteAsync(new RuntimeEnvelope
            {
                ProtocolVersion = 2,
                Type = type,
                RequestId = requestId,
                Payload = payload,
            }, CancellationToken.None);
    }

    private sealed class CompatibilityWidget : Widget
    {
        public override WidgetView Render() => new(
            UI.Stack("root", UI.Text("legacy", "legacy-text")).InputScope("root"),
            null,
            [],
            "root");
    }

    private static ViewNode Find(ViewNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            try { return Find(child, id); }
            catch (KeyNotFoundException) { }
        }
        throw new KeyNotFoundException(id);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
