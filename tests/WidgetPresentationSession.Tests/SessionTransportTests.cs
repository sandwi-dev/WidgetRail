using System.IO.Pipes;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetPresentationSession;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetPresentationSession.Tests;

[TestClass]
public sealed class SessionTransportTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task HandshakeRejectsAnUnexpectedResponse()
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAsync(async channel =>
        {
            var hello = await channel.ReadAsync(CancellationToken.None);
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.Acknowledged,
                RequestId = hello.RequestId,
                Payload = BridgeJson.ToElement(new { }),
            }, CancellationToken.None);
        });

        await Assert.ThrowsExactlyAsync<BridgeProtocolException>(() =>
            WidgetPresentationSession.ConnectAsync(
                server.PipeName,
                Options()));
        await serverTask.WaitAsync(TestDeadline);
    }

    [TestMethod]
    public async Task CanceledCallerRetainsResponseCorrelationAndCapacity()
    {
        await using var server = new ScriptedBridgeServer();
        var firstReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var first = await channel.ReadAsync(CancellationToken.None);
            firstReceived.TrySetResult();
            await releaseFirst.Task.WaitAsync(TestDeadline);
            await ReplyCatalogAsync(channel, first.RequestId, revision: 3);

            var second = await channel.ReadAsync(CancellationToken.None);
            await ReplyCatalogAsync(channel, second.RequestId, revision: 3);
            await ExpectStopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(
            server.PipeName, Options());
        try
        {
            using var cancellation = new CancellationTokenSource();
            var first = session.ListWidgetsAsync(cancellation.Token);
            await firstReceived.Task.WaitAsync(TestDeadline);
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await first);
            releaseFirst.TrySetResult();

            var second = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
            Assert.AreEqual(3L, second.Revision);
            Assert.AreEqual("session-widget", second.Widgets.Single().Id);
        }
        finally
        {
            await session.DisposeAsync();
            await serverTask.WaitAsync(TestDeadline);
        }
    }

    [TestMethod]
    public async Task DiagnosticsRetainOnlyTheConfiguredLatestEntries()
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            for (var revision = 1; revision <= 5; revision++)
            {
                await channel.WriteAsync(new BridgeEnvelope
                {
                    Type = BridgeMessageTypes.CatalogChanged,
                    Payload = BridgeJson.ToElement(new BridgeCatalogChangedEvent(revision)),
                }, CancellationToken.None);
            }
            await ExpectStopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(
            server.PipeName,
            Options() with { MaximumRetainedDiagnostics = 2 });
        try
        {
            await WaitUntilAsync(() => session.Diagnostics.Count == 2);
            Assert.AreEqual(2, session.Diagnostics.Count);
            StringAssert.Contains(session.Diagnostics[0].Message, "4");
            StringAssert.Contains(session.Diagnostics[1].Message, "5");
        }
        finally
        {
            await session.DisposeAsync();
            await serverTask.WaitAsync(TestDeadline);
        }
    }

    [TestMethod]
    public async Task ArtworkCompletionRetainsExactSnapshotAuthority()
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await channel.ReadAsync(CancellationToken.None).AsTask()
                .WaitAsync(TestDeadline);
            await ReplyCatalogAsync(channel, list.RequestId, revision: 1);

            var establish = await channel.ReadAsync(CancellationToken.None).AsTask()
                .WaitAsync(TestDeadline);
            Assert.AreEqual(BridgeMessageTypes.SetWidgetLifecycle, establish.Type);
            var snapshot = new ViewSnapshot
            {
                Sequence = 7,
                WidgetInstanceId = "session.instance",
                ActiveInputScopeId = "root",
                Root = new ViewNode
                {
                    Id = "root",
                    Kind = ViewNodeKind.Stack,
                    Children =
                    [
                        new ViewNode
                        {
                            Id = "artwork",
                            Kind = ViewNodeKind.Image,
                            ArtworkHandle = "app-library.test-artwork",
                            ImageFit = ImageFit.Contain,
                            AccessibilityLabel = "Test artwork",
                        },
                    ],
                },
            };
            using var snapshotDocument = System.Text.Json.JsonDocument.Parse(
                SnapshotJson.Serialize(snapshot));
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.Snapshot,
                RequestId = establish.RequestId,
                Payload = BridgeJson.ToElement(new
                {
                    widgetId = "session-widget",
                    snapshot = snapshotDocument.RootElement.Clone(),
                    renderStyles = new Dictionary<string, BridgeNodeRenderStyles>(),
                }),
            }, CancellationToken.None);

            var artwork = await channel.ReadAsync(CancellationToken.None).AsTask()
                .WaitAsync(TestDeadline);
            Assert.AreEqual(BridgeMessageTypes.ResolveArtwork, artwork.Type);
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.Acknowledged,
                RequestId = artwork.RequestId,
                Payload = BridgeJson.ToElement(new { }),
            }, CancellationToken.None);
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.Artwork,
                Payload = BridgeJson.ToElement(new
                {
                    widgetId = "session-widget",
                    artworkHandle = "app-library.test-artwork",
                    pngBase64 = Convert.ToBase64String([1, 2, 3, 4]),
                }),
            }, CancellationToken.None);
            await ExpectStopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(
            server.PipeName, Options());
        try
        {
            _ = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
            var frame = await session.EstablishPresentationAsync(
                session.GetTarget("session-widget"),
                GameBarAlternative.WidgetSdk.WidgetLifecycleState.Visible)
                .WaitAsync(TestDeadline);
            var artwork = await session.ResolveArtworkAsync(
                frame.Authority, "app-library.test-artwork")
                .WaitAsync(TestDeadline);
            Assert.AreEqual(frame.Authority, artwork.Authority);
            CollectionAssert.AreEqual(
                new byte[] { 1, 2, 3, 4 }, artwork.PngBytes.ToArray());
        }
        finally
        {
            await session.DisposeAsync();
            await serverTask.WaitAsync(TestDeadline);
        }
    }

    [TestMethod]
    public async Task ConfigurationBoundsFailBeforeTransportCreation()
    {
        var invalid = Options() with { MaximumPendingRequests = 0 };
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            WidgetPresentationSession.ConnectAsync("valid-unused-pipe", invalid));
    }

    private static WidgetPresentationSessionOptions Options() => new()
    {
        ClientName = "WidgetPresentationSession.Tests",
        ConnectTimeout = TimeSpan.FromSeconds(2),
        MaximumMessageBytes = 64 * 1024,
        MaximumPendingRequests = 2,
        MaximumPendingArtworkRequests = 2,
        MaximumRetainedDiagnostics = 8,
    };

    private static async Task ReplyCatalogAsync(
        BridgeFrameChannel channel,
        long requestId,
        long revision)
    {
        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Widgets,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(new
            {
                revision,
                widgets = new[]
                {
                    new BridgeWidgetDescriptor
                    {
                        Id = "session-widget",
                        Name = "Session widget",
                        InstanceId = "session.instance",
                        RuntimeGeneration = new string('a', 32),
                        PresentationGeneration = new string('b', 32),
                        Icon = WidgetGlyph.Connection,
                    },
                },
            }),
        }, CancellationToken.None);
    }

    private static async Task ExpectStopAsync(BridgeFrameChannel channel)
    {
        var stop = await channel.ReadAsync(CancellationToken.None);
        Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Acknowledged,
            RequestId = stop.RequestId,
            Payload = BridgeJson.ToElement(new { }),
        }, CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestDeadline;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                Assert.Fail("The bounded session condition was not reached.");
            await Task.Delay(10);
        }
    }
}

internal sealed class ScriptedBridgeServer : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;

    internal ScriptedBridgeServer()
    {
        PipeName = $"gba-presentation-session-test-{Guid.NewGuid():N}";
        _pipe = new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            4096,
            4096);
    }

    internal string PipeName { get; }

    internal Task RunAuthenticatedAsync(Func<BridgeFrameChannel, Task> scenario) =>
        RunAsync(async channel =>
        {
            var hello = await channel.ReadAsync(CancellationToken.None);
            Assert.AreEqual(BridgeMessageTypes.Hello, hello.Type);
            Assert.IsTrue(hello.RequestId > 0);
            _ = BridgeJson.FromElement<BridgeHello>(hello.Payload);
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.HelloAccepted,
                RequestId = hello.RequestId,
                Payload = BridgeJson.ToElement(new { }),
            }, CancellationToken.None);
            await scenario(channel);
        });

    internal async Task RunAsync(Func<BridgeFrameChannel, Task> scenario)
    {
        await _pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await scenario(new BridgeFrameChannel(_pipe, 64 * 1024));
    }

    public ValueTask DisposeAsync() => _pipe.DisposeAsync();
}
