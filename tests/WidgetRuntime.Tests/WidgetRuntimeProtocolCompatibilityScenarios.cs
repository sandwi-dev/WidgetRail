using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            var legacyRequest = WidgetWorkerServer.ValidateRenderRequest(legacyRender);
            Equal(WidgetPresentationTransactionKind.OrdinaryCheckpoint,
                legacyRequest.TransactionKind);
            var publication = new CompatibilityWidget().RenderPublication(
                "runtime.test",
                new string('0', 32),
                sequence: 1,
                expectedBaseSequence: 0,
                PresentationUpdateCapabilities.None,
                WidgetPresentationTransactionKind.OrdinaryCheckpoint);
            True(publication.Update is null,
                "An explicit ordinary transaction must produce a complete checkpoint.");
        }

        var generation = new string('B', 32);
        var transactionRows = new[]
        {
            ("incremental", new RenderPayload
            {
                UpdateCapabilities = PresentationUpdateCapabilities.Current,
                BaseSequence = 7,
                PresentationGeneration = generation,
                RequireCheckpoint = false,
            }, WidgetPresentationTransactionKind.IncrementalUpdate),
            ("ordinary", new RenderPayload
            {
                UpdateCapabilities = PresentationUpdateCapabilities.None,
                RequireCheckpoint = true,
            }, WidgetPresentationTransactionKind.OrdinaryCheckpoint),
            ("incremental-zero-base", new RenderPayload
            {
                UpdateCapabilities = PresentationUpdateCapabilities.Current,
                PresentationGeneration = generation,
                RequireCheckpoint = false,
            }, (WidgetPresentationTransactionKind?)null),
            ("compatibility-field-mismatch", new RenderPayload
            {
                UpdateCapabilities = PresentationUpdateCapabilities.None,
                RequireCheckpoint = false,
            }, (WidgetPresentationTransactionKind?)null),
        };
        foreach (var (name, render, expectedKind) in transactionRows)
        {
            RuntimeRenderRequest? request = null;
            try { request = WidgetWorkerServer.ValidateRenderRequest(render); }
            catch (WidgetProtocolViolationException) { }
            Equal(expectedKind, request?.TransactionKind,
                $"Runtime-v2 compatibility row '{name}' had the wrong result.");
        }

        VerifyFrozenStrictSchemas();

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
            Type = MessageTypes.Render,
            RequestId = 4,
            Payload = RuntimeJson.ToElement(new RenderPayload
            {
                UpdateCapabilities = PresentationUpdateCapabilities.Current,
                BaseSequence = repeated.Sequence,
                PresentationGeneration = generation,
                RequireCheckpoint = false,
            }),
        }, CancellationToken.None);
        var rejectedResponse = await channel.ReadAsync(CancellationToken.None);
        Equal(MessageTypes.Error, rejectedResponse.Type);
        Equal(new ErrorPayload(
                "worker_request_failed",
                "Frozen runtime-v2 render failed at $.root.children[7] (duplicate_id)."),
            RuntimeJson.FromElement<ErrorPayload>(rejectedResponse.Payload));

        await channel.WriteAsync(new RuntimeEnvelope
        {
            ProtocolVersion = 2,
            Type = MessageTypes.Stop,
            RequestId = 5,
            Payload = RuntimeJson.ToElement(new { }),
        }, CancellationToken.None);
        Equal(MessageTypes.Acknowledged,
            (await channel.ReadAsync(CancellationToken.None)).Type);
        Equal(0, await worker.WaitAsync(TimeSpan.FromSeconds(2)));

        await VerifyFrozenV2HostToCurrentWorkerAsync();
    }

    private static async Task VerifyFrozenV2HostToCurrentWorkerAsync()
    {
        var pipeName = $"wrail-runtime-v2-current-{Guid.NewGuid():N}";
        const string instance = "runtime.test";
        var sessionNonce = new string('D', 64);
        const int maximumBytes = 64 * 1024;
        await using var hostPipe = new System.IO.Pipes.NamedPipeServerStream(
            pipeName,
            System.IO.Pipes.PipeDirection.InOut,
            1,
            System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous);
        var worker = new WidgetWorkerServer(
            new CompatibilityWidget(), instance, pipeName, maximumBytes,
            sessionNonce: sessionNonce).RunAsync();
        await hostPipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var channel = new FrozenV2Channel(hostPipe, maximumBytes);

        var hello = await channel.ReadAsync(CancellationToken.None);
        Equal(MessageTypes.Hello, hello.Type);
        Equal(new FrozenV2HelloPayload(instance, sessionNonce),
            FrozenV2Json.FromElement<FrozenV2HelloPayload>(hello.Payload));
        await channel.WriteAsync(new FrozenV2Envelope
        {
            Type = MessageTypes.HelloAccepted,
            Payload = FrozenV2Json.ToElement(new { }),
        }, CancellationToken.None);

        await channel.WriteAsync(new FrozenV2Envelope
        {
            Type = MessageTypes.Render,
            RequestId = 1,
            Payload = FrozenV2Json.ToElement(new FrozenV2RenderPayload()),
        }, CancellationToken.None);
        var response = await channel.ReadAsync(CancellationToken.None);
        Equal(MessageTypes.Snapshot, response.Type);
        var snapshot = SnapshotJson.Deserialize(
            Encoding.UTF8.GetBytes(response.Payload.GetRawText()));
        Equal(instance, snapshot.WidgetInstanceId);

        await channel.WriteAsync(new FrozenV2Envelope
        {
            Type = MessageTypes.Stop,
            RequestId = 2,
            Payload = FrozenV2Json.ToElement(new { }),
        }, CancellationToken.None);
        Equal(MessageTypes.Acknowledged,
            (await channel.ReadAsync(CancellationToken.None)).Type);
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
    }

    internal static async Task<int> RunFrozenV2PeerAsync(
        string pipeName,
        string instanceId,
        string sessionNonce,
        int maximumBytes)
    {
        await using var pipe = new System.IO.Pipes.NamedPipeClientStream(
            ".", pipeName, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        await pipe.ConnectAsync();
        var channel = new FrozenV2Channel(pipe, maximumBytes);
        await channel.WriteAsync(new FrozenV2Envelope
        {
            ProtocolVersion = 2,
            Type = MessageTypes.Hello,
            Payload = FrozenV2Json.ToElement(new FrozenV2HelloPayload(instanceId, sessionNonce)),
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
                _ = FrozenV2Json.FromElement<FrozenV2RenderPayload>(request.Payload);
                if (sequence == 2)
                {
                    await ReplyAsync(MessageTypes.Error, request.RequestId,
                        FrozenV2Json.ToElement(new FrozenV2ErrorPayload(
                            "worker_request_failed",
                            "Frozen runtime-v2 render failed at $.root.children[7] (duplicate_id).")));
                    continue;
                }
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
                    FrozenV2Json.ToElement(new { }));
                continue;
            }

            if (request.Type == MessageTypes.Stop)
            {
                await ReplyAsync(MessageTypes.Acknowledged, request.RequestId,
                    FrozenV2Json.ToElement(new { }));
                return 0;
            }
            if (request.Type == MessageTypes.ControllerInput)
            {
                // A legacy callback would fail if dispatched twice or for raw
                // fallback after an unsupported revalidation request.
                var input = RuntimeJson.FromElement<ControllerInputEvent>(request.Payload);
                if (input.Button != ControllerButton.A || input.Sequence != 903)
                    throw new InvalidOperationException("Unchecked legacy raw input was dispatched.");
                await ReplyAsync(MessageTypes.ControllerInputResult, request.RequestId,
                    FrozenV2Json.ToElement(new { handled = true }));
                continue;
            }
            await ReplyAsync(MessageTypes.Error, request.RequestId,
                FrozenV2Json.ToElement(new FrozenV2ErrorPayload("worker_request_failed",
                    $"Unknown request type '{request.Type}'.")));
        }

        ValueTask ReplyAsync(string type, long requestId, JsonElement payload) =>
            channel.WriteAsync(new FrozenV2Envelope
            {
                ProtocolVersion = 2,
                Type = type,
                RequestId = requestId,
                Payload = payload,
            }, CancellationToken.None);
    }

    private static void VerifyFrozenStrictSchemas()
    {
        var currentResponse = new RuntimeEnvelope
        {
            Type = MessageTypes.Snapshot,
            RequestId = 7,
            Payload = RuntimeJson.ToElement(new { }),
        };
        _ = FrozenV2Json.Deserialize<FrozenV2Envelope>(
            JsonSerializer.SerializeToUtf8Bytes(currentResponse, RuntimeJson.Options));

        var oldHostRequest = new FrozenV2Envelope
        {
            ProtocolVersion = 2,
            Type = MessageTypes.Render,
            RequestId = 8,
            Payload = FrozenV2Json.ToElement(new FrozenV2RenderPayload()),
        };
        var currentEnvelope = JsonSerializer.Deserialize<RuntimeEnvelope>(
            FrozenV2Json.Serialize(oldHostRequest), RuntimeJson.Options)
            ?? throw new InvalidOperationException("Current runtime rejected a frozen v2 envelope.");
        var currentRender = RuntimeJson.FromElement<RenderPayload>(currentEnvelope.Payload);
        Equal(WidgetPresentationTransactionKind.OrdinaryCheckpoint,
            WidgetWorkerServer.ValidateRenderRequest(currentRender).TransactionKind);

        RejectsFrozenEnvelopeUnknownField();
        RejectsFrozenRenderUnknownField();
    }

    private static void RejectsFrozenEnvelopeUnknownField()
    {
        const string json = """
            {"protocolVersion":2,"type":"snapshot","requestId":1,"presentationTransactionKind":"ordinaryCheckpoint","payload":{}}
            """;
        var rejected = false;
        try { _ = FrozenV2Json.Deserialize<FrozenV2Envelope>(Encoding.UTF8.GetBytes(json)); }
        catch (JsonException) { rejected = true; }
        True(rejected, "The frozen strict runtime-v2 envelope accepted an unknown field.");
    }

    private static void RejectsFrozenRenderUnknownField()
    {
        const string json = """
            {"requireCheckpoint":true,"transactionKind":"ordinaryCheckpoint"}
            """;
        var rejected = false;
        try { _ = FrozenV2Json.Deserialize<FrozenV2RenderPayload>(Encoding.UTF8.GetBytes(json)); }
        catch (JsonException) { rejected = true; }
        True(rejected, "The frozen strict runtime-v2 render schema accepted an unknown field.");
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FrozenV2Envelope
    {
        public int ProtocolVersion { get; init; } = 2;
        public required string Type { get; init; }
        public long RequestId { get; init; }
        public required JsonElement Payload { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FrozenV2RenderPayload
    {
        public FrozenV2PresentationUpdateCapabilities? UpdateCapabilities { get; init; }
        public long BaseSequence { get; init; }
        public string? PresentationGeneration { get; init; }
        public bool RequireCheckpoint { get; init; } = true;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FrozenV2PresentationUpdateCapabilities
    {
        public int MaximumProtocolVersion { get; init; }
        public int MaximumOperationsPerBatch { get; init; }
        public int MaximumBatchBytes { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FrozenV2HelloPayload(string WidgetInstanceId, string SessionNonce);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FrozenV2ErrorPayload(string Code, string Message);

    private static class FrozenV2Json
    {
        internal static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        internal static JsonElement ToElement<T>(T value) =>
            JsonSerializer.SerializeToElement(value, Options);

        internal static T FromElement<T>(JsonElement value) =>
            value.Deserialize<T>(Options) ??
                throw new JsonException($"A frozen {typeof(T).Name} payload was null.");

        internal static byte[] Serialize<T>(T value) =>
            JsonSerializer.SerializeToUtf8Bytes(value, Options);

        internal static T Deserialize<T>(ReadOnlySpan<byte> value) =>
            JsonSerializer.Deserialize<T>(value, Options) ??
                throw new JsonException($"A frozen {typeof(T).Name} message was null.");
    }

    private sealed class FrozenV2Channel(Stream stream, int maximumMessageBytes)
    {
        internal async ValueTask WriteAsync(
            FrozenV2Envelope message,
            CancellationToken cancellationToken)
        {
            var payload = FrozenV2Json.Serialize(message);
            if (payload.Length == 0 || payload.Length > maximumMessageBytes)
                throw new InvalidOperationException("Frozen runtime-v2 message length was invalid.");
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        internal async ValueTask<FrozenV2Envelope> ReadAsync(CancellationToken cancellationToken)
        {
            var header = new byte[sizeof(int)];
            await stream.ReadExactlyAsync(header, cancellationToken);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length <= 0 || length > maximumMessageBytes)
                throw new InvalidOperationException("Frozen runtime-v2 peer received an invalid length.");
            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, cancellationToken);
            return FrozenV2Json.Deserialize<FrozenV2Envelope>(payload);
        }
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

    private static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ??
                $"Expected '{expected}', received '{actual}'.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
