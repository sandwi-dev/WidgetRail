using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class ArtworkDemandTests
{
    [TestMethod]
    public async Task ReplacedWorkerCannotCompleteOldArtworkEvenWithIdenticalHandle()
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            var old = await ReadDemandAsync(channel);
            await AckAsync(channel, old.RequestId);
            var refresh = await ReadAsync(channel);
            await SnapshotAsync(channel, refresh.RequestId, 2, workerRun: new(1, 1));
            var current = await ReadDemandAsync(channel);
            await AckAsync(channel, current.RequestId);
            await CompleteAsync(channel, old, Artwork(3));
            await CompleteAsync(channel, current, Artwork(4));
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var original = await EstablishAsync(session);
            var pending = session.ResolveArtworkAsync(original.Authority, Handle);
            var replacement = await session.RefreshAsync(original.Authority).WaitAsync(Deadline);
            var retired = await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () => await pending.WaitAsync(Deadline));
            Assert.AreEqual("stale_artwork_authority", retired.Code);
            CollectionAssert.AreEqual(Artwork(4), (await session.ResolveArtworkAsync(replacement.Authority, Handle).WaitAsync(Deadline)).EncodedBytes.ToArray());
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CosmeticSnapshotsRetainArtworkBeforeAndAfterAdmission(bool admitAfterUpdate)
    {
        await using var server = new ScriptedBridgeServer();
        var expected = Artwork(9);
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            BridgeEnvelope? demand = null;
            if (!admitAfterUpdate)
            {
                demand = await ReadDemandAsync(channel);
                await AckAsync(channel, demand.RequestId);
            }
            var refresh = await ReadAsync(channel);
            await SnapshotAsync(channel, refresh.RequestId, 2);
            await refreshed.Task.WaitAsync(Deadline);
            if (admitAfterUpdate)
            {
                demand = await ReadDemandAsync(channel);
                await AckAsync(channel, demand.RequestId);
            }
            await CompleteAsync(channel, demand!, expected);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var original = await EstablishAsync(session);
            var pending = admitAfterUpdate ? null : session.ResolveArtworkAsync(original.Authority, Handle);
            await session.RefreshAsync(original.Authority).WaitAsync(Deadline);
            refreshed.SetResult();
            pending ??= session.ResolveArtworkAsync(original.Authority, Handle);
            CollectionAssert.AreEqual(expected, (await pending.WaitAsync(Deadline)).EncodedBytes.ToArray());
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task RemovedThenReaddedArtworkCannotReviveOldDemandOrAuthority()
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            var old = await ReadDemandAsync(channel);
            await AckAsync(channel, old.RequestId);
            var remove = await ReadAsync(channel);
            await SnapshotAsync(channel, remove.RequestId, 2, includeArtwork: false);
            var restore = await ReadAsync(channel);
            await SnapshotAsync(channel, restore.RequestId, 3);
            var current = await ReadDemandAsync(channel);
            await AckAsync(channel, current.RequestId);
            await CompleteAsync(channel, old, Artwork(1));
            await CompleteAsync(channel, current, Artwork(2));
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var original = await EstablishAsync(session);
            var pending = session.ResolveArtworkAsync(original.Authority, Handle);
            var removed = await session.RefreshAsync(original.Authority).WaitAsync(Deadline);
            var restored = await session.RefreshAsync(removed.Authority).WaitAsync(Deadline);
            var retired = await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () => await pending.WaitAsync(Deadline));
            Assert.AreEqual("stale_artwork_authority", retired.Code);
            var readmit = await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () =>
                await session.ResolveArtworkAsync(original.Authority, Handle).WaitAsync(Deadline));
            Assert.AreEqual("stale_artwork_authority", readmit.Code);
            var forged = await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () =>
                await session.ResolveArtworkAsync(restored.Authority with { }, Handle).WaitAsync(Deadline));
            Assert.AreEqual("stale_artwork_authority", forged.Code);
            CollectionAssert.AreEqual(Artwork(2), (await session.ResolveArtworkAsync(restored.Authority, Handle).WaitAsync(Deadline)).EncodedBytes.ToArray());
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PresentationFragmentArtworkUsesSnapshotAuthority(bool focused)
    {
        await using var server = new ScriptedBridgeServer();
        var expected = Artwork(7);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel);
            await SendAsync(channel, list.RequestId, BridgeMessageTypes.Widgets,
                new { revision = 1, isComplete = true, widgets = new[] { Descriptor } });
            var establish = await ReadAsync(channel);
            await SnapshotAsync(channel, establish.RequestId, 1, focused);
            var demand = await ReadDemandAsync(channel);
            await AckAsync(channel, demand.RequestId);
            await CompleteAsync(channel, demand, expected);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var frame = await EstablishAsync(session);
            var result = await session.ResolveArtworkAsync(frame.Authority, Handle).WaitAsync(Deadline);
            CollectionAssert.AreEqual(expected, result.EncodedBytes.ToArray());
        }
        await serverTask.WaitAsync(Deadline);
    }
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const string Handle = "artwork.shared";
    private static readonly BridgeWidgetDescriptor Descriptor = new()
    {
        Id = "artwork-widget", Name = "Artwork", InstanceId = "instance.1",
        RuntimeGeneration = new string('a', 32), PresentationGeneration = new string('b', 32),
        PackageContentDigest = new string('e', 64), Icon = WidgetGlyph.Connection,
    };

    [TestMethod]
    public async Task SameHandleConcurrentDemandsResolveOutOfOrderWithExactBytesBeyondDiagnosticLimit()
    {
        await using var server = new ScriptedBridgeServer();
        var firstBytes = Artwork(1);
        var secondBytes = Artwork(2);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            var first = await ReadDemandAsync(channel);
            await AckAsync(channel, first.RequestId);
            var second = await ReadDemandAsync(channel);
            await AckAsync(channel, second.RequestId);
            Assert.AreNotEqual(DemandId(first), DemandId(second));
            await CompleteAsync(channel, second, secondBytes);
            await CompleteAsync(channel, first, firstBytes);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var frame = await EstablishAsync(session);
            var first = session.ResolveArtworkAsync(frame.Authority, Handle);
            var second = session.ResolveArtworkAsync(frame.Authority, Handle);
            CollectionAssert.AreEqual(firstBytes, (await first.WaitAsync(Deadline)).EncodedBytes.ToArray());
            CollectionAssert.AreEqual(secondBytes, (await second.WaitAsync(Deadline)).EncodedBytes.ToArray());
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task CancelledDemandCannotCompleteReplacementAndDoesNotDecodeItsLatePayload()
    {
        await using var server = new ScriptedBridgeServer();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = Artwork(3);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            var old = await ReadDemandAsync(channel);
            await AckAsync(channel, old.RequestId);
            admitted.SetResult();
            var current = await ReadDemandAsync(channel);
            Assert.AreNotEqual(DemandId(old), DemandId(current));
            await AckAsync(channel, current.RequestId);
            var oldPayload = Completion(old, expected);
            oldPayload["contentBase64"] = "NOT-VALID-BASE64";
            await SendAsync(channel, 0, BridgeMessageTypes.Artwork, oldPayload);
            await CompleteAsync(channel, current, expected);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName,
            new() { MaximumPendingArtworkRequests = 1 }))
        {
            var resolved = new ConcurrentQueue<WidgetPresentationArtwork>();
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.ArtworkResolved += (_, args) => { resolved.Enqueue(args.Artwork); published.TrySetResult(); };
            var frame = await EstablishAsync(session);
            using var cancellation = new CancellationTokenSource();
            var old = session.ResolveArtworkAsync(frame.Authority, Handle, cancellation.Token);
            await admitted.Task.WaitAsync(Deadline);
            var saturated = await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () =>
                await session.ResolveArtworkAsync(frame.Authority, Handle));
            Assert.AreEqual("artwork_saturated", saturated.Code);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await old.WaitAsync(Deadline));
            var replacement = await session.ResolveArtworkAsync(frame.Authority, Handle).WaitAsync(Deadline);
            CollectionAssert.AreEqual(expected, replacement.EncodedBytes.ToArray());
            await published.Task.WaitAsync(Deadline);
            Assert.AreEqual(1, resolved.Count);
            Assert.IsTrue(session.Diagnostics.Any(item => item.Code == "unmatched_artwork"));
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task WrongIdentityAndLegacyRepliesCannotConsumeMatchingDemand()
    {
        await using var server = new ScriptedBridgeServer();
        var expected = Artwork(4);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            var demand = await ReadDemandAsync(channel);
            await AckAsync(channel, demand.RequestId);
            foreach (var property in new[] { "runtimeGeneration", "presentationGeneration", "widgetId", "artworkHandle" })
            {
                var mismatched = Completion(demand, expected);
                mismatched[property] = "wrong.identity";
                await SendAsync(channel, 0, BridgeMessageTypes.Artwork, mismatched);
            }
            var legacy = Completion(demand, expected);
            legacy.Remove("demandId");
            await SendAsync(channel, 0, BridgeMessageTypes.Artwork, legacy);
            await CompleteAsync(channel, demand, expected);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var frame = await EstablishAsync(session);
            var artwork = await session.ResolveArtworkAsync(frame.Authority, Handle).WaitAsync(Deadline);
            CollectionAssert.AreEqual(expected, artwork.EncodedBytes.ToArray());
            Assert.AreEqual(4, session.Diagnostics.Count(item => item.Code == "stale_artwork"));
            Assert.AreEqual(1, session.Diagnostics.Count(item => item.Code == "unmatched_artwork"));
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task CompatibleSnapshotKeepsConcurrentDemandsCorrelatedToTheirOrigins()
    {
        await using var server = new ScriptedBridgeServer();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = Artwork(5);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            var old = await ReadDemandAsync(channel);
            await AckAsync(channel, old.RequestId);
            admitted.SetResult();
            var refresh = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.GetSnapshot, refresh.Type);
            await SnapshotAsync(channel, refresh.RequestId, 2);
            var current = await ReadDemandAsync(channel);
            await AckAsync(channel, current.RequestId);
            await CompleteAsync(channel, old, Artwork(6));
            await CompleteAsync(channel, current, expected);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var frame = await EstablishAsync(session);
            var old = session.ResolveArtworkAsync(frame.Authority, Handle);
            await admitted.Task.WaitAsync(Deadline);
            var replacement = await session.RefreshAsync(frame.Authority);
            var current = session.ResolveArtworkAsync(replacement.Authority, Handle);
            var retained = await old.WaitAsync(Deadline);
            Assert.AreEqual(frame.Authority, retained.Authority);
            CollectionAssert.AreEqual(Artwork(6), retained.EncodedBytes.ToArray());
            var artwork = await current.WaitAsync(Deadline);
            Assert.AreEqual(replacement.Authority, artwork.Authority);
            CollectionAssert.AreEqual(expected, artwork.EncodedBytes.ToArray());
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TimeoutBeforeOrAfterAcknowledgementReleasesLocalCapacity(bool acknowledgeFirst)
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            var old = await ReadDemandAsync(channel);
            if (acknowledgeFirst) await AckAsync(channel, old.RequestId);
            var replacement = await ReadDemandAsync(channel);
            if (!acknowledgeFirst) await AckAsync(channel, old.RequestId);
            await AckAsync(channel, replacement.RequestId);
            await CompleteAsync(channel, old, Artwork(7));
            await CompleteAsync(channel, replacement, Artwork(8));
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName,
            new() { ArtworkTimeout = TimeSpan.FromMilliseconds(200), MaximumPendingArtworkRequests = 1 }))
        {
            var frame = await EstablishAsync(session);
            var timeout = await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () =>
                await session.ResolveArtworkAsync(frame.Authority, Handle).WaitAsync(Deadline));
            Assert.AreEqual("artwork_timeout", timeout.Code);
            CollectionAssert.AreEqual(Artwork(8), (await session.ResolveArtworkAsync(frame.Authority, Handle).WaitAsync(Deadline)).EncodedBytes.ToArray());
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task DisposalCancelsAcknowledgedDemandWithoutWaitingForArtwork()
    {
        await using var server = new ScriptedBridgeServer();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            var artwork = await ReadDemandAsync(channel);
            await AckAsync(channel, artwork.RequestId);
            admitted.SetResult();
            await StopAsync(channel);
        });
        await using var session = await WidgetPresentationSession.ConnectAsync(server.PipeName);
        var frame = await EstablishAsync(session);
        var pending = session.ResolveArtworkAsync(frame.Authority, Handle);
        await admitted.Task.WaitAsync(Deadline);
        await session.DisposeAsync().AsTask().WaitAsync(Deadline);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(Deadline));
        await serverTask.WaitAsync(Deadline);
    }

    private static byte[] Artwork(byte marker)
    {
        var image = Convert.FromBase64String("UklGRh4AAABXRUJQVlA4TBEAAAAvAQAAAAdQmWZ0qf+BiOh/AAA=");
        // A permitted unknown RIFF chunk preserves the real VP8L image while
        // exercising payloads above the old, inappropriate 512-character text limit.
        var bytes = new byte[image.Length + 1032];
        image.CopyTo(bytes, 0);
        "JUNK"u8.CopyTo(bytes.AsSpan(image.Length));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(image.Length + 4), 1024);
        bytes.AsSpan(image.Length + 8).Fill(marker);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        return bytes;
    }

    private static async Task<BridgeEnvelope> ReadDemandAsync(BridgeFrameChannel channel)
    {
        var demand = await ReadAsync(channel);
        Assert.AreEqual(BridgeMessageTypes.ResolveArtwork, demand.Type);
        Assert.AreEqual(Descriptor.RuntimeGeneration, demand.Payload.GetProperty("runtimeGeneration").GetString());
        Assert.AreEqual(Descriptor.PresentationGeneration, demand.Payload.GetProperty("presentationGeneration").GetString());
        Assert.IsTrue(Guid.TryParseExact(DemandId(demand), "N", out _));
        return demand;
    }
    private static string DemandId(BridgeEnvelope demand) => demand.Payload.GetProperty("demandId").GetString()!;
    private static Dictionary<string, object> Completion(BridgeEnvelope demand, byte[] bytes) => new()
    {
        ["widgetId"] = Descriptor.Id, ["artworkHandle"] = Handle,
        ["runtimeGeneration"] = Descriptor.RuntimeGeneration, ["presentationGeneration"] = Descriptor.PresentationGeneration,
        ["demandId"] = DemandId(demand), ["contentType"] = "image/webp", ["contentBase64"] = Convert.ToBase64String(bytes),
    };
    private static Task CompleteAsync(BridgeFrameChannel channel, BridgeEnvelope demand, byte[] bytes) =>
        SendAsync(channel, 0, BridgeMessageTypes.Artwork, Completion(demand, bytes));
    private static async Task<WidgetPresentationFrame> EstablishAsync(WidgetPresentationSession session)
    {
        await session.ListWidgetsAsync().WaitAsync(Deadline);
        return await session.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Visible).WaitAsync(Deadline);
    }
    private static async Task EstablishAsync(BridgeFrameChannel channel)
    {
        var list = await ReadAsync(channel);
        await SendAsync(channel, list.RequestId, BridgeMessageTypes.Widgets,
            new { revision = 1, isComplete = true, widgets = new[] { Descriptor } });
        var establish = await ReadAsync(channel);
        await SnapshotAsync(channel, establish.RequestId, 1);
    }
    private static async Task SnapshotAsync(BridgeFrameChannel channel, long requestId, long sequence, bool? fragmentFocused = null, bool includeArtwork = true, BridgeWorkerRun? workerRun = null)
    {
        var snapshot = new ViewSnapshot
        {
            Sequence = sequence, WidgetInstanceId = Descriptor.InstanceId, ActiveInputScopeId = "root",
            Root = new ViewNode { Id = "root", Kind = ViewNodeKind.Stack, Children =
                includeArtwork ? [new ViewNode { Id = "image", Kind = ViewNodeKind.Image, ArtworkHandle = Handle, ImageFit = ImageFit.Contain, AccessibilityLabel = "Artwork" }] : [] },
        };
        if (fragmentFocused is { } focused)
        {
            var image = snapshot.Root.Children[0];
            var fallback = new ViewNode { Id = "default", Kind = ViewNodeKind.Text, Text = "Fallback" };
            snapshot = snapshot with { Root = new ViewNode
            {
                Id = "root", Kind = ViewNodeKind.FocusPresentationSurface,
                DefaultFocusPresentation = focused ? fallback : image,
                Children = [new() { Id = "item", Kind = ViewNodeKind.Button, Text = "Item", ActionId = "activate",
                    FocusPresentation = focused ? image : null }],
            } };
        }
        using var document = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        await SendAsync(channel, requestId, BridgeMessageTypes.Snapshot,
            new { widgetId = Descriptor.Id, transactionKind = "ordinaryCheckpoint", baseSequence = 0, recoveryOriginSequence = 0,
                snapshot = document.RootElement.Clone(), renderStyles = new Dictionary<string, BridgeNodeRenderStyles>(), workerRun });
    }
    private static Task<BridgeEnvelope> ReadAsync(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
    private static Task AckAsync(BridgeFrameChannel channel, long requestId) => SendAsync(channel, requestId, BridgeMessageTypes.Acknowledged, new { });
    private static async Task SendAsync(BridgeFrameChannel channel, long requestId, string type, object payload) =>
        await channel.WriteAsync(new BridgeEnvelope { Type = type, RequestId = requestId, Payload = BridgeJson.ToElement(payload) }, CancellationToken.None);
    private static async Task StopAsync(BridgeFrameChannel channel)
    {
        var stop = await ReadAsync(channel);
        Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
        await AckAsync(channel, stop.RequestId);
    }
}
