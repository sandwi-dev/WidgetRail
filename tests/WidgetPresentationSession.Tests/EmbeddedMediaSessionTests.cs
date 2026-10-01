using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed partial class EmbeddedMediaSessionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(8);
    private static BridgeWidgetDescriptor Descriptor => new() { Id = "media", Name = "Media", InstanceId = "media.instance",
        RuntimeGeneration = new('a', 32), PresentationGeneration = new('b', 32), PackageContentDigest = new('c', 64), Icon = WidgetGlyph.Music, PinningSupported = true };
    private static EmbeddedMediaSession Media => new()
    {
        Id = "player", AccessibleName = "Player", EntryAsset = "adapter/index.html",
        Resources = [new() { Path = "adapter/index.html", ContentType = "text/html" }],
        Surface = new() { PreferredWidth = 640, PreferredHeight = 360, MinimumWidth = 320, MinimumHeight = 240 },
        AspectRatio = 16.0 / 9, Commands = [EmbeddedMediaCommand.TogglePlayback],
        AllowedFrameOrigins = ["https://www.youtube.com"], AllowedFrameDomainFamilies = ["googlevideo.com"],
        PendingCommand = new() { Sequence = 1, Kind = EmbeddedMediaPlaybackCommandKind.Load, MediaKey = "song-1" },
    };
    private static EmbeddedMediaPlaybackEvent Observation => new()
    { SessionId = "player", Sequence = 1, CommandSequence = 1, MediaKey = "song-1", State = EmbeddedMediaPlaybackState.Playing,
        PositionSeconds = 3, DurationSeconds = 120, Volume = 0.5 };

    [TestMethod]
    public async Task ExactBundleHasReadOnlyVerifiedBytesAndLatestPresentationWithoutResolvingAgain()
    {
        var next = Media with { AccessibleName = "New name", PendingCommand = Media.PendingCommand! with { Sequence = 2, Kind = EmbeddedMediaPlaybackCommandKind.Pause } };
        await Run(async channel =>
        {
            var resolve = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.ResolveEmbeddedMedia, resolve.Type);
            Assert.AreEqual(6, resolve.Payload.EnumerateObject().Count());
            Assert.AreEqual(1L, resolve.Payload.GetProperty("sequence").GetInt64());
            Assert.AreEqual("media.instance", resolve.Payload.GetProperty("instanceId").GetString());
            await Reply(channel, resolve.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            var refresh = await Read(channel); await Snapshot(channel, refresh.RequestId, next, 2, scope: "changed");
            var observed = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.EmbeddedMediaPlaybackEvent, observed.Type);
            Assert.AreEqual(2L, observed.Payload.GetProperty("sequence").GetInt64());
            Assert.AreEqual(2L, observed.Payload.GetProperty("event").GetProperty("commandSequence").GetInt64());
            await Reply(channel, observed.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            using var stream = document.Resources.Single().OpenRead();
            Assert.IsFalse(stream.CanWrite);
            Assert.IsFalse(((MemoryStream)stream).TryGetBuffer(out _));
            Assert.AreEqual("<html>verified</html>", await new StreamReader(stream).ReadToEndAsync());
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            var current = session.GetEmbeddedMediaState(document)!;
            Assert.AreEqual(2, current.Authority.SnapshotSequence);
            Assert.AreEqual("changed", current.Authority.ActiveInputScopeId);
            Assert.AreEqual("New name", current.Declaration.AccessibleName);
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { CommandSequence = 2 });
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { CommandSequence = 2 }));
        });
    }

    [TestMethod]
    public async Task CommandTerminalRetainsOriginAcrossCompatibleSnapshotsAndSpontaneousObservationUsesLatest()
    {
        await Run(async channel =>
        {
            var request = await Read(channel);
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            foreach (var sequence in new long[] { 2, 3 })
            {
                request = await Read(channel);
                await Snapshot(channel, request.RequestId, Media with { AccessibleName = "Updated " + sequence }, sequence, scope: "page-" + sequence);
            }
            request = await Read(channel);
            Assert.AreEqual(1L, request.Payload.GetProperty("sequence").GetInt64(), "A terminal must retain its command origin, not an intermediate UI snapshot.");
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
            request = await Read(channel);
            Assert.AreEqual(3L, request.Payload.GetProperty("sequence").GetInt64(), "Spontaneous observations use the latest document authority.");
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
            request = await Read(channel);
            await Snapshot(channel, request.RequestId, Media with { PendingCommand = Media.PendingCommand! with { Sequence = 2, Kind = EmbeddedMediaPlaybackCommandKind.Pause } }, 4);
            request = await Read(channel);
            Assert.AreEqual(4L, request.Payload.GetProperty("sequence").GetInt64(), "A replacement command starts a new origin.");
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation);
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { Sequence = 2, CommandSequence = 0 });
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { Sequence = 3, CommandSequence = 2 });
        });
    }

    [TestMethod]
    public async Task RemovedCommandIsExpectedSupersessionButUnknownFutureTerminalIsInvalid()
    {
        await Run(async channel =>
        {
            var request = await Read(channel);
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            request = await Read(channel);
            await Snapshot(channel, request.RequestId, Media with { PendingCommand = null }, 2);
            request = await Read(channel);
            Assert.AreEqual(0L, request.Payload.GetProperty("event").GetProperty("commandSequence").GetInt64());
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            var stale = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation));
            Assert.AreEqual("embedded_media_command_stale", stale.Code);
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { CommandSequence = 999 }));
            Assert.IsNotNull(session.GetEmbeddedMediaState(document));
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { CommandSequence = 0 });
        });
    }

    [TestMethod]
    [DataRow("embedded_media_stale")]
    [DataRow("embedded_media_command_stale")]
    public async Task BrokerSupersessionPreservesTypedOutcomeWithoutRetiringHealthyDocument(string code)
    {
        await Run(async channel =>
        {
            var request = await Read(channel);
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            request = await Read(channel);
            await Reply(channel, request.RequestId, BridgeMessageTypes.Error, new { code, message = "Superseded in flight." });
            request = await Read(channel);
            Assert.AreEqual(2L, request.Payload.GetProperty("event").GetProperty("sequence").GetInt64());
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            var stale = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation));
            Assert.AreEqual(code, stale.Code);
            Assert.IsNotNull(session.GetEmbeddedMediaState(document));
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation));
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { Sequence = 2, CommandSequence = 0 });
        });
    }

    [TestMethod]
    [DataRow("widgetId")][DataRow("instanceId")][DataRow("runtimeGeneration")][DataRow("presentationGeneration")]
    [DataRow("sequence")][DataRow("sessionId")][DataRow("path")][DataRow("contentType")][DataRow("hash")]
    [DataRow("base64")][DataRow("origin")][DataRow("family")][DataRow("commands")][DataRow("pending")]
    [DataRow("duplicate")][DataRow("size")][DataRow("entry")][DataRow("missing")]
    public async Task MalformedOrSubstitutedBundleIsRejected(string mutation)
    {
        await Run(async channel =>
        {
            var request = await Read(channel); var bundle = Bundle();
            switch (mutation)
            {
                case "widgetId": case "instanceId": case "runtimeGeneration": case "presentationGeneration": case "sessionId": bundle[mutation] = "foreign"; break;
                case "sequence": bundle["sequence"] = 2; break;
                case "path": bundle["resources"]![0]!["path"] = "../escape.html"; break;
                case "contentType": bundle["resources"]![0]!["contentType"] = "application/x-unknown"; break;
                case "hash": bundle["resources"]![0]!["sha256"] = new string('0', 64); break;
                case "base64": bundle["resources"]![0]!["contentBase64"] = "invalid!"; break;
                case "origin": bundle["allowedFrameOrigins"] = new JsonArray("http://evil.example"); break;
                case "family": bundle["allowedFrameDomainFamilies"] = new JsonArray("com"); break;
                case "commands": bundle["commands"] = new JsonArray("back"); break;
                case "pending": bundle["pendingCommand"]!["sequence"] = 9; break;
                case "duplicate": ((JsonArray)bundle["resources"]!).Add(bundle["resources"]![0]!.DeepClone()); break;
                case "size": bundle["resources"]![0]!["contentBase64"] = new string('A', 350000); break;
                case "entry": bundle["entryAsset"] = "other.html"; break;
                case "missing": bundle.Remove("resources"); break;
            }
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, bundle);
        }, async (session, frame) => await Assert.ThrowsAsync<BridgeProtocolException>(() => session.ResolveEmbeddedMediaAsync(frame.Authority)));
    }

    [TestMethod]
    [DataRow("entry")][DataRow("resources")][DataRow("origins")][DataRow("families")][DataRow("id")][DataRow("removed")]
    public async Task DocumentChangeRetiresOldBundlePermanentlyEvenIfOriginalReturns(string change)
    {
        var changed = change switch
        {
            "entry" => Media with { EntryAsset = "other.html", Resources = [new() { Path = "other.html", ContentType = "text/html" }] },
            "resources" => Media with { Resources = [.. Media.Resources, new() { Path = "extra.js", ContentType = "text/javascript" }] },
            "origins" => Media with { AllowedFrameOrigins = ["https://example.com"] },
            "families" => Media with { AllowedFrameDomainFamilies = ["example.com"] },
            "id" => Media with { Id = "replacement" },
            _ => null,
        };
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            request = await Read(channel); await Snapshot(channel, request.RequestId, changed, 2);
            request = await Read(channel); await Snapshot(channel, request.RequestId, Media, 3);
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            Assert.IsNull(session.GetEmbeddedMediaState(document));
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            Assert.IsNull(session.GetEmbeddedMediaState(document));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation));
        });
    }

    [TestMethod]
    public async Task PendingBundleSurvivesCompatiblePublicationAndUsesItsOriginalAuthority()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var resolve = await Read(channel); started.SetResult();
            var update = await Read(channel); await Snapshot(channel, update.RequestId, Media with { AspectRatio = 1 }, 2);
            await Reply(channel, resolve.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
        }, async (session, frame) =>
        {
            var pending = session.ResolveEmbeddedMediaAsync(frame.Authority); await started.Task.WaitAsync(Limit);
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            var document = await pending;
            Assert.AreEqual(1, document.Authority.SnapshotSequence);
            Assert.AreEqual(1d, session.GetEmbeddedMediaState(document)!.Declaration.AspectRatio);
        });
    }

    [TestMethod]
    public async Task RetirementCancelsPendingResolutionWithoutWaitingForLateReply()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var resolve = await Read(channel); started.SetResult();
            var update = await Read(channel); await Snapshot(channel, update.RequestId, null, 2);
            await canceled.Task.WaitAsync(Limit);
            await Reply(channel, resolve.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
        }, async (session, frame) =>
        {
            var pending = session.ResolveEmbeddedMediaAsync(frame.Authority); await started.Task.WaitAsync(Limit);
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Limit));
            canceled.SetResult();
        });
    }

    [TestMethod]
    [DataRow("sequence")][DataRow("session")][DataRow("mediaKey")][DataRow("nan")][DataRow("position")]
    [DataRow("volume")][DataRow("rate")][DataRow("command")][DataRow("error")][DataRow("state")]
    public async Task InvalidObservationNeverReachesBridge(string change)
    {
        await Run(async channel =>
        {
            var resolve = await Read(channel); await Reply(channel, resolve.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            var value = change switch
            {
                "sequence" => Observation with { Sequence = 0 }, "session" => Observation with { SessionId = "foreign" },
                "mediaKey" => Observation with { MediaKey = "invalid/key" }, "nan" => Observation with { DurationSeconds = double.NaN },
                "position" => Observation with { PositionSeconds = 121 }, "volume" => Observation with { Volume = 2 },
                "rate" => Observation with { PlaybackRate = 5 }, "command" => Observation with { CommandSequence = 9 },
                "error" => Observation with { ErrorCode = "unsafe error" }, _ => Observation with { State = (EmbeddedMediaPlaybackState)88 },
            };
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, value));
        });
    }

    [TestMethod]
    public async Task CommandPreferenceMustMatchAndSpontaneousEventUsesCurrentSequence()
    {
        var media = Media with { PendingCommand = new() { Sequence = 2, Kind = EmbeddedMediaPlaybackCommandKind.SetMuted, MediaKey = "song-1", Muted = true } };
        await Run(async channel =>
        {
            var resolve = await Read(channel); await Reply(channel, resolve.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle(media));
            var observed = await Read(channel); Assert.AreEqual(BridgeMessageTypes.EmbeddedMediaPlaybackEvent, observed.Type);
            await Reply(channel, observed.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { CommandSequence = 2 }));
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { CommandSequence = 0, Muted = true });
        }, media);
    }


    [TestMethod]
    public async Task AggregateBytesCannotExceedProtocolLimit()
    {
        var media = Media with { Resources = [.. Media.Resources,
            new() { Path = "extra.js", ContentType = "text/javascript" },
            new() { Path = "extra.css", ContentType = "text/css" }] };
        await Run(async channel =>
        {
            var request = await Read(channel);
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle(media, 180000));
        }, async (session, frame) => await Assert.ThrowsAsync<BridgeProtocolException>(() => session.ResolveEmbeddedMediaAsync(frame.Authority)), media);
    }

    [TestMethod]
    public async Task CallerCancellationAndLateReplyDoNotPoisonNextResolution()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var first = await Read(channel); started.SetResult();
            await canceled.Task.WaitAsync(Limit);
            await Reply(channel, first.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            var second = await Read(channel);
            await Reply(channel, second.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
        }, async (session, frame) =>
        {
            using var cancellation = new CancellationTokenSource();
            var pending = session.ResolveEmbeddedMediaAsync(frame.Authority, cancellation.Token);
            await started.Task.WaitAsync(Limit); cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Limit));
            canceled.SetResult();
            Assert.IsNotNull(session.GetEmbeddedMediaState(await session.ResolveEmbeddedMediaAsync(frame.Authority)));
        });
    }

    [TestMethod]
    public async Task DeadlineBoundsWaitAndLateReplyRemainsCorrelated()
    {
        var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var request = await Read(channel);
            await timedOut.Task.WaitAsync(Limit);
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
        }, async (session, frame) =>
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.ResolveEmbeddedMediaAsync(frame.Authority));
            timedOut.SetResult();
        }, options: new() { EmbeddedMediaTimeout = TimeSpan.FromMilliseconds(100) });
    }

    [TestMethod]
    public async Task ResolutionAdmissionHasFourActiveAndSixteenPendingBound()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var requests = new List<BridgeEnvelope>();
            for (int i = 0; i < 4; ++i) requests.Add(await Read(channel));
            started.SetResult(); await canceled.Task.WaitAsync(Limit);
            foreach (var request in requests) await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
        }, async (session, frame) =>
        {
            using var cancellation = new CancellationTokenSource();
            var pending = Enumerable.Range(0, 16).Select(_ => session.ResolveEmbeddedMediaAsync(frame.Authority, cancellation.Token)).ToArray();
            await started.Task.WaitAsync(Limit);
            var failure = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ResolveEmbeddedMediaAsync(frame.Authority));
            Assert.AreEqual("embedded_media_saturated", failure.Code);
            cancellation.Cancel();
            foreach (var task in pending) await Assert.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(Limit));
            canceled.SetResult();
        });
    }

    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task CatalogRetirementOrRestartRevokesResolvedDocument(bool restart)
    {
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            request = await Read(channel);
            if (restart)
                await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { widgetId = "media", state = WidgetLifecycleState.Interactive });
            else
                await Reply(channel, request.RequestId, BridgeMessageTypes.Widgets, new { revision = 2, isComplete = true, widgets = Array.Empty<BridgeWidgetDescriptor>() });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            if (restart) await session.RestartAsync(session.GetTarget("media"));
            else await session.ListWidgetsAsync();
            Assert.IsNull(session.GetEmbeddedMediaState(document));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation));
        });
    }

    [TestMethod]
    public async Task CancellationAfterEventSendNeverAllowsReplayOfUncertainSequence()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            request = await Read(channel); started.SetResult();
            await canceled.Task.WaitAsync(Limit);
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
            request = await Read(channel);
            Assert.AreEqual(2L, request.Payload.GetProperty("event").GetProperty("sequence").GetInt64());
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            using var cancellation = new CancellationTokenSource();
            var pending = session.SendEmbeddedMediaPlaybackEventAsync(document, Observation, cancellation.Token);
            await started.Task.WaitAsync(Limit); cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Limit));
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation));
            canceled.SetResult();
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { Sequence = 2, CommandSequence = 0 });
        });
    }

    [TestMethod]
    public async Task QueuedCommandTerminalIsRevalidatedAfterEarlierEventAndSnapshotUpdate()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var request = await Read(channel); await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            var first = await Read(channel); started.SetResult();
            var update = await Read(channel);
            await Snapshot(channel, update.RequestId, Media with { PendingCommand = Media.PendingCommand! with { Sequence = 2, MediaKey = "song-2" } }, 2);
            await updated.Task.WaitAsync(Limit);
            await Reply(channel, first.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            var first = session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { CommandSequence = 0 });
            await started.Task.WaitAsync(Limit);
            var queued = session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { Sequence = 2 });
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            updated.SetResult();
            await first;
            var stale = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => queued);
            Assert.AreEqual("embedded_media_command_stale", stale.Code);
        });
    }


    [TestMethod]
    public async Task SessionDisposalPermanentlyRetiresResolvedDocument()
    {
        await Run(async channel =>
        {
            var request = await Read(channel);
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            await session.DisposeAsync();
            Assert.IsNull(session.GetEmbeddedMediaState(document));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendEmbeddedMediaPlaybackEventAsync(document, Observation));
        });
    }

    private static JsonObject Bundle(EmbeddedMediaSession? media = null, int resourceBytes = 0)
    {
        media ??= Media;
        var bytes = resourceBytes == 0 ? Encoding.UTF8.GetBytes("<html>verified</html>") : new byte[resourceBytes];
        var bundle = new BridgeEmbeddedMediaBundle(Descriptor.Id, Descriptor.InstanceId, Descriptor.RuntimeGeneration,
            Descriptor.PresentationGeneration, 1, media.Id, media.EntryAsset, media.Surface, media.AspectRatio,
            media.AccessibleName, media.Commands, media.AllowedFrameOrigins, media.AllowedFrameDomainFamilies,
            media.PendingCommand, media.SupportedPresentations, media.MediaSeekStepSeconds,
            media.Resources.Select(resource => new BridgeEmbeddedMediaResource(resource.Path, resource.ContentType,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Convert.ToBase64String(bytes))).ToArray());
        return JsonNode.Parse(BridgeJson.ToElement(bundle).GetRawText())!.AsObject();
    }
    private static async Task Run(Func<BridgeFrameChannel, Task> serverAction,
        Func<WidgetPresentationSession, WidgetPresentationFrame, Task> clientAction, EmbeddedMediaSession? media = null,
        WidgetPresentationSessionOptions? options = null, ViewNode? root = null, BridgeWorkerRun? workerRun = null)
    {
        await using var server = new MediaBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var catalog = await Read(channel);
            await Reply(channel, catalog.RequestId, BridgeMessageTypes.Widgets, new { revision = 1, isComplete = true, widgets = new[] { Descriptor } });
            var establish = await Read(channel); await Snapshot(channel, establish.RequestId, media ?? Media, 1, root: root, workerRun: workerRun);
            await serverAction(channel);
            var stop = await Read(channel); Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            await Reply(channel, stop.RequestId, BridgeMessageTypes.Acknowledged, new { });
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, options))
        {
            await session.ListWidgetsAsync();
            var establish = session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Interactive);
            if (await Task.WhenAny(establish, serving) == serving) await serving;
            var frame = await establish.WaitAsync(Limit);
            await clientAction(session, frame).WaitAsync(Limit);
        }
        await serving.WaitAsync(Limit);
    }
    private static async Task Snapshot(BridgeFrameChannel channel, long id, EmbeddedMediaSession? media, long sequence, string scope = "root", ViewNode? root = null, BridgeWorkerRun? workerRun = null)
    {
        var snapshot = new ViewSnapshot { Sequence = sequence, WidgetInstanceId = Descriptor.InstanceId, ActiveInputScopeId = scope,
            Root = root ?? new() { Id = scope, Kind = ViewNodeKind.Stack }, EmbeddedMediaSession = media };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        Assert.AreEqual(0, errors.Count, string.Join("; ", errors.Select(error => error.Path + ": " + error.Message)));
        using var json = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        await Reply(channel, id, BridgeMessageTypes.Snapshot, new { widgetId = Descriptor.Id, transactionKind = "ordinaryCheckpoint",
            baseSequence = 0, recoveryOriginSequence = 0, snapshot = json.RootElement, renderStyles = new Dictionary<string, BridgeNodeRenderStyles>(), workerRun });
    }
    private static Task<BridgeEnvelope> Read(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Limit);
    private static async Task Reply(BridgeFrameChannel channel, long id, string type, object value) =>
        await channel.WriteAsync(new() { Type = type, RequestId = id, Payload = BridgeJson.ToElement(value) }, CancellationToken.None);
    private sealed class MediaBridgeServer : IAsyncDisposable
    {
        private readonly System.IO.Pipes.NamedPipeServerStream pipe;
        internal string PipeName { get; } = "wrail-media-test-" + Guid.NewGuid().ToString("N");
        internal MediaBridgeServer() => pipe = new(PipeName, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        internal async Task RunAuthenticatedAsync(Func<BridgeFrameChannel, Task> scenario)
        {
            await pipe.WaitForConnectionAsync().WaitAsync(Limit);
            var channel = new BridgeFrameChannel(pipe, BridgeProtocol.DefaultMaximumMessageBytes);
            var hello = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.Hello, hello.Type);
            await Reply(channel, hello.RequestId, BridgeMessageTypes.HelloAccepted, new { });
            await scenario(channel);
        }
        public ValueTask DisposeAsync() => pipe.DisposeAsync();
    }

}
