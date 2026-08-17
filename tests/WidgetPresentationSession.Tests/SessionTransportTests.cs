using System.IO.Pipes;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession.Tests;

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
                WidgetRail.WidgetSdk.WidgetLifecycleState.Visible)
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
    public async Task InvalidationRefreshFailureAfterRestartIsDiscarded()
    {
        await using var server = new ScriptedBridgeServer();
        var established = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel);
            await ReplyCatalogAsync(channel, list.RequestId, revision: 1);
            var establish = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, establish.RequestId, Descriptor(), sequence: 1);
            await established.Task.WaitAsync(TestDeadline);
            await SendInvalidationAsync(channel, revision: 1);

            var refresh = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.GetSnapshot, refresh.Type);
            refreshReceived.TrySetResult();
            var restart = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.RestartWidget, restart.Type);
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.Acknowledged,
                RequestId = restart.RequestId,
                Payload = BridgeJson.ToElement(new
                {
                    widgetId = "session-widget",
                    state = WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive,
                }),
            }, CancellationToken.None);
            await ReplyRefreshErrorAsync(channel, refresh.RequestId);
            await ExpectStopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options());
        try
        {
            _ = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
            var target = session.GetTarget("session-widget");
            _ = await session.EstablishPresentationAsync(
                target, WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive)
                .WaitAsync(TestDeadline);
            established.TrySetResult();
            await refreshReceived.Task.WaitAsync(TestDeadline);
            _ = await session.RestartAsync(target).WaitAsync(TestDeadline);
            await WaitForStaleRefreshDiagnosticAsync(session);

            var state = session.GetState("session-widget");
            Assert.IsNotNull(state);
            Assert.IsNull(state.LastGood);
            Assert.IsNull(state.Failure);
        }
        finally
        {
            await session.DisposeAsync();
            await serverTask.WaitAsync(TestDeadline);
        }
    }

    [TestMethod]
    public async Task InvalidationRefreshFailureAfterCatalogReplacementOrRemovalIsDiscarded()
    {
        await AssertCatalogRefreshFailureDiscardedAsync(removeWidget: false);
        await AssertCatalogRefreshFailureDiscardedAsync(removeWidget: true);
    }

    [TestMethod]
    public async Task InvalidationRefreshFailurePreservesANewerLastGoodSnapshot()
    {
        await using var server = new ScriptedBridgeServer();
        var established = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var newerApplied = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var descriptor = Descriptor();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel);
            await ReplyCatalogAsync(channel, list.RequestId, revision: 1);
            var establish = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, establish.RequestId, descriptor, sequence: 1);
            await established.Task.WaitAsync(TestDeadline);
            await SendInvalidationAsync(channel, revision: 1);

            var invalidationRefresh = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.GetSnapshot, invalidationRefresh.Type);
            refreshReceived.TrySetResult();
            var explicitRefresh = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.GetSnapshot, explicitRefresh.Type);
            await ReplySnapshotAsync(
                channel,
                explicitRefresh.RequestId,
                descriptor,
                sequence: 2,
                activeInputScopeId: "scope-2");
            await newerApplied.Task.WaitAsync(TestDeadline);
            await ReplyRefreshErrorAsync(channel, invalidationRefresh.RequestId);
            await ExpectStopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options());
        var failurePublications = 0;
        void CountFailures(object? sender, WidgetPresentationChangedEventArgs eventArgs)
        {
            if (eventArgs.State.Failure is not null)
                Interlocked.Increment(ref failurePublications);
        }
        session.PresentationChanged += CountFailures;
        try
        {
            _ = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
            var initial = await session.EstablishPresentationAsync(
                session.GetTarget("session-widget"),
                WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive)
                .WaitAsync(TestDeadline);
            established.TrySetResult();
            await refreshReceived.Task.WaitAsync(TestDeadline);
            var newer = await session.RefreshAsync(initial.Authority).WaitAsync(TestDeadline);
            Assert.AreEqual(2L, newer.Authority.SnapshotSequence);
            Assert.AreEqual("scope-2", newer.Authority.ActiveInputScopeId);
            newerApplied.TrySetResult();

            await WaitForStaleRefreshDiagnosticAsync(session);
            var state = session.GetState("session-widget");
            Assert.IsNotNull(state?.LastGood);
            Assert.AreEqual(newer.Authority, state.LastGood.Authority);
            Assert.IsNull(state.Failure);
            Assert.AreEqual(0, Volatile.Read(ref failurePublications));
        }
        finally
        {
            session.PresentationChanged -= CountFailures;
            await session.DisposeAsync();
            await serverTask.WaitAsync(TestDeadline);
        }
    }

    [TestMethod]
    public async Task CommittedFailurePublicationCannotFollowAConcurrentReplacement()
    {
        await AssertCommittedFailurePublicationIsOrderedAsync(
            ConcurrentPublicationReplacement.Restart);
        await AssertCommittedFailurePublicationIsOrderedAsync(
            ConcurrentPublicationReplacement.CatalogReplacement);
        await AssertCommittedFailurePublicationIsOrderedAsync(
            ConcurrentPublicationReplacement.NewerSnapshot);
    }

    [TestMethod]
    public async Task CapturedBridgeFailureCannotOverwriteConcurrentAuthorityReplacement()
    {
        await AssertCapturedBridgeFailureIsRejectedAsync(BridgeFailureReplacement.Restart);
        await AssertCapturedBridgeFailureIsRejectedAsync(
            BridgeFailureReplacement.CatalogReplacement);
        await AssertCapturedBridgeFailureIsRejectedAsync(BridgeFailureReplacement.CatalogRemoval);
        await AssertCapturedBridgeFailureIsRejectedAsync(BridgeFailureReplacement.NewerSnapshot);
    }

    [TestMethod]
    public async Task GenerationlessBridgeFailureAtomicallyRetiresSessionAuthority()
    {
        await using var server = new ScriptedBridgeServer();
        var established = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var descriptor = Descriptor();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel);
            await ReplyCatalogAsync(channel, list.RequestId, revision: 1);
            var establish = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, establish.RequestId, descriptor, sequence: 1);
            await established.Task.WaitAsync(TestDeadline);
            await SendBridgeFailureAsync(channel, runtimeGeneration: null);
            await ExpectStopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options());
        try
        {
            _ = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
            var initial = await session.EstablishPresentationAsync(
                session.GetTarget("session-widget"),
                WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive)
                .WaitAsync(TestDeadline);
            var initialPublication = session.GetState("session-widget")!.PublicationRevision;
            established.TrySetResult();
            await WaitUntilAsync(() =>
                session.GetState("session-widget")?.Failure?.Code == "bridge_test_failure");

            var failed = session.GetState("session-widget");
            Assert.IsNotNull(failed?.LastGood);
            Assert.AreEqual(initial.Authority, failed.LastGood.Authority);
            Assert.IsTrue(failed.PublicationRevision > initialPublication);
            var stale = await Assert.ThrowsExactlyAsync<WidgetPresentationSessionException>(
                () => session.RefreshAsync(initial.Authority));
            Assert.AreEqual("presentation_stale", stale.Code);
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

    private static async Task AssertCommittedFailurePublicationIsOrderedAsync(
        ConcurrentPublicationReplacement replacement)
    {
        await using var server = new ScriptedBridgeServer();
        var established = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failurePublicationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailurePublication = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var descriptor = Descriptor();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel);
            await ReplyCatalogAsync(channel, list.RequestId, revision: 1);
            var establish = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, establish.RequestId, descriptor, sequence: 1);
            await established.Task.WaitAsync(TestDeadline);
            await SendInvalidationAsync(channel, revision: 1);

            var refresh = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.GetSnapshot, refresh.Type);
            await ReplyRefreshErrorAsync(channel, refresh.RequestId);
            var replacementRequest = await ReadAsync(channel);
            switch (replacement)
            {
            case ConcurrentPublicationReplacement.Restart:
                Assert.AreEqual(BridgeMessageTypes.RestartWidget, replacementRequest.Type);
                await channel.WriteAsync(new BridgeEnvelope
                {
                    Type = BridgeMessageTypes.Acknowledged,
                    RequestId = replacementRequest.RequestId,
                    Payload = BridgeJson.ToElement(new
                    {
                        widgetId = "session-widget",
                        state = WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive,
                    }),
                }, CancellationToken.None);
                break;
            case ConcurrentPublicationReplacement.CatalogReplacement:
                Assert.AreEqual(BridgeMessageTypes.ListWidgets, replacementRequest.Type);
                await ReplyCatalogAsync(
                    channel,
                    replacementRequest.RequestId,
                    revision: 2,
                    [Descriptor(
                        instanceId: "session.replacement",
                        runtimeGeneration: new string('c', 32),
                        presentationGeneration: new string('d', 32))]);
                break;
            case ConcurrentPublicationReplacement.NewerSnapshot:
                Assert.AreEqual(BridgeMessageTypes.GetSnapshot, replacementRequest.Type);
                await ReplySnapshotAsync(
                    channel,
                    replacementRequest.RequestId,
                    descriptor,
                    sequence: 2,
                    activeInputScopeId: "scope-2");
                break;
            default:
                Assert.Fail("Unknown concurrent publication replacement.");
                break;
            }
            await ExpectStopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options());
        var observed = new List<WidgetPresentationState>();
        var observedGate = new object();
        void OnPresentationChanged(object? sender, WidgetPresentationChangedEventArgs eventArgs)
        {
            if (eventArgs.State.Failure?.Code == "refresh_test_failure")
            {
                failurePublicationEntered.TrySetResult();
                if (!releaseFailurePublication.Task.Wait(TestDeadline))
                    throw new TimeoutException("The publication replacement was not committed.");
            }
            lock (observedGate) observed.Add(eventArgs.State);
        }
        session.PresentationChanged += OnPresentationChanged;
        try
        {
            _ = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
            var target = session.GetTarget("session-widget");
            var initial = await session.EstablishPresentationAsync(
                target, WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive)
                .WaitAsync(TestDeadline);
            established.TrySetResult();
            await failurePublicationEntered.Task.WaitAsync(TestDeadline);

            switch (replacement)
            {
            case ConcurrentPublicationReplacement.Restart:
                _ = await session.RestartAsync(target).WaitAsync(TestDeadline);
                Assert.IsNull(session.GetState("session-widget")?.LastGood);
                break;
            case ConcurrentPublicationReplacement.CatalogReplacement:
                _ = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
                Assert.IsNull(session.GetState("session-widget"));
                break;
            case ConcurrentPublicationReplacement.NewerSnapshot:
                var newer = await session.RefreshAsync(initial.Authority).WaitAsync(TestDeadline);
                Assert.AreEqual(2L, newer.Authority.SnapshotSequence);
                Assert.AreEqual("scope-2", newer.Authority.ActiveInputScopeId);
                Assert.IsNull(session.GetState("session-widget")?.Failure);
                break;
            default:
                Assert.Fail("Unknown concurrent publication replacement.");
                break;
            }
            releaseFailurePublication.TrySetResult();
            await WaitUntilAsync(() =>
            {
                lock (observedGate) return observed.Count >= 3;
            });

            WidgetPresentationState failure;
            WidgetPresentationState final;
            lock (observedGate)
            {
                failure = observed[^2];
                final = observed[^1];
            }
            Assert.AreEqual("refresh_test_failure", failure.Failure?.Code);
            Assert.IsTrue(final.PublicationRevision > failure.PublicationRevision);
            Assert.IsNull(final.Failure);
            if (replacement == ConcurrentPublicationReplacement.NewerSnapshot)
            {
                Assert.AreEqual(2L, final.LastGood?.Authority.SnapshotSequence);
                Assert.AreEqual("scope-2", final.LastGood?.Authority.ActiveInputScopeId);
            }
            else
                Assert.IsNull(final.LastGood);
        }
        finally
        {
            releaseFailurePublication.TrySetResult();
            session.PresentationChanged -= OnPresentationChanged;
            await session.DisposeAsync();
            await serverTask.WaitAsync(TestDeadline);
        }
    }

    private static async Task AssertCapturedBridgeFailureIsRejectedAsync(
        BridgeFailureReplacement replacement)
    {
        await using var server = new ScriptedBridgeServer();
        var established = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failureCaptured = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailure = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var descriptor = Descriptor();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel);
            await ReplyCatalogAsync(channel, list.RequestId, revision: 1);
            var establish = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, establish.RequestId, descriptor, sequence: 1);
            await established.Task.WaitAsync(TestDeadline);
            await SendBridgeFailureAsync(channel, descriptor.RuntimeGeneration);

            var replacementRequest = await ReadAsync(channel);
            switch (replacement)
            {
            case BridgeFailureReplacement.Restart:
                Assert.AreEqual(BridgeMessageTypes.RestartWidget, replacementRequest.Type);
                await channel.WriteAsync(new BridgeEnvelope
                {
                    Type = BridgeMessageTypes.Acknowledged,
                    RequestId = replacementRequest.RequestId,
                    Payload = BridgeJson.ToElement(new
                    {
                        widgetId = "session-widget",
                        state = WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive,
                    }),
                }, CancellationToken.None);
                break;
            case BridgeFailureReplacement.CatalogReplacement:
                Assert.AreEqual(BridgeMessageTypes.ListWidgets, replacementRequest.Type);
                await ReplyCatalogAsync(
                    channel,
                    replacementRequest.RequestId,
                    revision: 2,
                    [Descriptor(
                        instanceId: "session.replacement",
                        runtimeGeneration: new string('c', 32),
                        presentationGeneration: new string('d', 32))]);
                break;
            case BridgeFailureReplacement.CatalogRemoval:
                Assert.AreEqual(BridgeMessageTypes.ListWidgets, replacementRequest.Type);
                await ReplyCatalogAsync(
                    channel,
                    replacementRequest.RequestId,
                    revision: 2,
                    Array.Empty<BridgeWidgetDescriptor>());
                break;
            case BridgeFailureReplacement.NewerSnapshot:
                Assert.AreEqual(BridgeMessageTypes.GetSnapshot, replacementRequest.Type);
                await ReplySnapshotAsync(
                    channel,
                    replacementRequest.RequestId,
                    descriptor,
                    sequence: 2,
                    activeInputScopeId: "scope-2");
                break;
            default:
                Assert.Fail("Unknown bridge failure replacement.");
                break;
            }
            await ExpectStopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options());
        session.BridgeFailureCapturedForTesting = () =>
        {
            failureCaptured.TrySetResult();
            return releaseFailure.Task;
        };
        var publications = new List<WidgetPresentationState>();
        var publicationGate = new object();
        void OnPresentationChanged(object? sender, WidgetPresentationChangedEventArgs eventArgs)
        {
            lock (publicationGate) publications.Add(eventArgs.State);
        }
        session.PresentationChanged += OnPresentationChanged;
        try
        {
            _ = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
            var target = session.GetTarget("session-widget");
            var initial = await session.EstablishPresentationAsync(
                target, WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive)
                .WaitAsync(TestDeadline);
            established.TrySetResult();
            await failureCaptured.Task.WaitAsync(TestDeadline);

            switch (replacement)
            {
            case BridgeFailureReplacement.Restart:
                _ = await session.RestartAsync(target).WaitAsync(TestDeadline);
                Assert.IsNull(session.GetState("session-widget")?.LastGood);
                break;
            case BridgeFailureReplacement.CatalogReplacement:
            case BridgeFailureReplacement.CatalogRemoval:
                _ = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
                Assert.IsNull(session.GetState("session-widget"));
                break;
            case BridgeFailureReplacement.NewerSnapshot:
                var newer = await session.RefreshAsync(initial.Authority).WaitAsync(TestDeadline);
                Assert.AreEqual(2L, newer.Authority.SnapshotSequence);
                Assert.AreEqual("scope-2", newer.Authority.ActiveInputScopeId);
                break;
            default:
                Assert.Fail("Unknown bridge failure replacement.");
                break;
            }
            releaseFailure.TrySetResult();
            await WaitUntilAsync(() => session.Diagnostics.Any(
                diagnostic => diagnostic.Code == "stale_failure"));

            WidgetPresentationState[] observed;
            lock (publicationGate) observed = publications.ToArray();
            Assert.IsFalse(observed.Any(state => state.Failure?.Code == "bridge_test_failure"));
            Assert.IsTrue(observed.Zip(observed.Skip(1),
                (left, right) => left.PublicationRevision < right.PublicationRevision).All(value => value));
            var current = session.GetState("session-widget");
            if (replacement == BridgeFailureReplacement.NewerSnapshot)
            {
                Assert.AreEqual(2L, current?.LastGood?.Authority.SnapshotSequence);
                Assert.IsNull(current?.Failure);
            }
            else if (replacement == BridgeFailureReplacement.Restart)
            {
                Assert.IsNotNull(current);
                Assert.IsNull(current.LastGood);
                Assert.IsNull(current.Failure);
            }
            else
            {
                Assert.IsNull(current);
            }
        }
        finally
        {
            releaseFailure.TrySetResult();
            session.PresentationChanged -= OnPresentationChanged;
            await session.DisposeAsync();
            await serverTask.WaitAsync(TestDeadline);
        }
    }

    private static async Task AssertCatalogRefreshFailureDiscardedAsync(bool removeWidget)
    {
        await using var server = new ScriptedBridgeServer();
        var established = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogApplied = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var initialList = await ReadAsync(channel);
            await ReplyCatalogAsync(channel, initialList.RequestId, revision: 1);
            var establish = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, establish.RequestId, Descriptor(), sequence: 1);
            await established.Task.WaitAsync(TestDeadline);
            await SendInvalidationAsync(channel, revision: 1);

            var refresh = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.GetSnapshot, refresh.Type);
            refreshReceived.TrySetResult();
            var replacementList = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.ListWidgets, replacementList.Type);
            var widgets = removeWidget
                ? Array.Empty<BridgeWidgetDescriptor>()
                : new[]
                {
                    Descriptor(
                        instanceId: "session.replacement",
                        runtimeGeneration: new string('c', 32),
                        presentationGeneration: new string('d', 32)),
                };
            await ReplyCatalogAsync(channel, replacementList.RequestId, revision: 2, widgets);
            await catalogApplied.Task.WaitAsync(TestDeadline);
            await ReplyRefreshErrorAsync(channel, refresh.RequestId);
            await ExpectStopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options());
        try
        {
            _ = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
            _ = await session.EstablishPresentationAsync(
                session.GetTarget("session-widget"),
                WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive)
                .WaitAsync(TestDeadline);
            established.TrySetResult();
            await refreshReceived.Task.WaitAsync(TestDeadline);
            var catalog = await session.ListWidgetsAsync().WaitAsync(TestDeadline);
            Assert.AreEqual(removeWidget ? 0 : 1, catalog.Widgets.Count);
            catalogApplied.TrySetResult();
            await WaitForStaleRefreshDiagnosticAsync(session);

            Assert.IsNull(session.GetState("session-widget"));
            if (!removeWidget)
                Assert.AreEqual(
                    "session.replacement",
                    session.GetTarget("session-widget").Descriptor.InstanceId);
        }
        finally
        {
            await session.DisposeAsync();
            await serverTask.WaitAsync(TestDeadline);
        }
    }

    private static BridgeWidgetDescriptor Descriptor(
        string instanceId = "session.instance",
        string? runtimeGeneration = null,
        string? presentationGeneration = null) => new()
    {
        Id = "session-widget",
        Name = "Session widget",
        InstanceId = instanceId,
        RuntimeGeneration = runtimeGeneration ?? new string('a', 32),
        PresentationGeneration = presentationGeneration ?? new string('b', 32),
        Icon = WidgetGlyph.Connection,
    };

    private static async Task<BridgeEnvelope> ReadAsync(BridgeFrameChannel channel) =>
        await channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TestDeadline);

    private static async Task ReplySnapshotAsync(
        BridgeFrameChannel channel,
        long requestId,
        BridgeWidgetDescriptor descriptor,
        long sequence,
        string activeInputScopeId = "root")
    {
        var snapshot = new ViewSnapshot
        {
            Sequence = sequence,
            WidgetInstanceId = descriptor.InstanceId,
            ActiveInputScopeId = activeInputScopeId,
            Root = new ViewNode { Id = activeInputScopeId, Kind = ViewNodeKind.Stack },
        };
        using var document = System.Text.Json.JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Snapshot,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(new
            {
                widgetId = descriptor.Id,
                snapshot = document.RootElement.Clone(),
                renderStyles = new Dictionary<string, BridgeNodeRenderStyles>(),
            }),
        }, CancellationToken.None);
    }

    private static async Task SendInvalidationAsync(BridgeFrameChannel channel, long revision)
    {
        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Invalidation,
            Payload = BridgeJson.ToElement(new
            {
                widgetId = "session-widget",
                revision,
            }),
        }, CancellationToken.None);
    }

    private static async Task ReplyRefreshErrorAsync(BridgeFrameChannel channel, long requestId)
    {
        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Error,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(new
            {
                code = "refresh_test_failure",
                message = "The deterministic refresh failed.",
            }),
        }, CancellationToken.None);
    }

    private static async Task SendBridgeFailureAsync(
        BridgeFrameChannel channel,
        string? runtimeGeneration)
    {
        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Failure,
            Payload = BridgeJson.ToElement(new
            {
                widgetId = "session-widget",
                runtimeGeneration,
                reason = "bridge_test_failure",
                message = "The deterministic bridge failure occurred.",
                canRestart = true,
            }),
        }, CancellationToken.None);
    }

    private static async Task WaitForStaleRefreshDiagnosticAsync(
        WidgetPresentationSession session)
    {
        await WaitUntilAsync(() => session.Diagnostics.Any(
            diagnostic => diagnostic.Code == "stale_refresh_failure"));
        var diagnostic = session.Diagnostics.Last(
            item => item.Code == "stale_refresh_failure");
        Assert.IsTrue(diagnostic.Message.Length <= 512);
        Assert.AreEqual("session-widget", diagnostic.WidgetId);
    }

    private static async Task ReplyCatalogAsync(
        BridgeFrameChannel channel,
        long requestId,
        long revision)
        => await ReplyCatalogAsync(channel, requestId, revision, [Descriptor()]);

    private static async Task ReplyCatalogAsync(
        BridgeFrameChannel channel,
        long requestId,
        long revision,
        IReadOnlyList<BridgeWidgetDescriptor> widgets)
    {
        await channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Widgets,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(new
            {
                revision,
                widgets,
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

internal enum ConcurrentPublicationReplacement
{
    Restart,
    CatalogReplacement,
    NewerSnapshot,
}

internal enum BridgeFailureReplacement
{
    Restart,
    CatalogReplacement,
    CatalogRemoval,
    NewerSnapshot,
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
