using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class PackageIconSessionTests
{
    private static readonly byte[] Svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\"><rect width=\"20\" height=\"20\" fill=\"#26a8ed\" /></svg>");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Svg)).ToLowerInvariant();
    private static BridgeWidgetDescriptor Descriptor => new() { Id = "icons", Name = "Icons", InstanceId = "icons.instance",
        RuntimeGeneration = new('a', 32), PresentationGeneration = new('b', 32), PackageContentDigest = new('c', 64), Icon = WidgetGlyph.Music,
        IconAssets = [new("cover", new('d', 64), Hash, Svg.Length, Svg.Length)] };
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(6);

    [TestMethod]
    public async Task StaticPackageIconUsesExactInventoryAndBoundedPrivateCacheWithoutSnapshot()
    {
        await Run(async channel =>
        {
            var request = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.ResolvePackageIcon, request.Type);
            var body = request.Payload;
            Assert.AreEqual("cover", body.GetProperty("assetId").GetString());
            Assert.AreEqual(Descriptor.PackageContentDigest, body.GetProperty("packageContentDigest").GetString());
            Assert.AreEqual(Hash, body.GetProperty("normalizedSha256").GetString());
            Assert.AreEqual(7, body.EnumerateObject().Count());
            await Reply(channel, request.RequestId, BridgeMessageTypes.PackageIcon, Payload());
        }, async (session, target) =>
        {
            var result = await session.ResolvePackageIconAsync(target, "cover");
            CollectionAssert.AreEqual(Svg, result.NormalizedSvg.ToArray());
            Assert.AreEqual(Hash, result.NormalizedSha256);
            var repeated = await session.ResolvePackageIconAsync(target, "cover");
            CollectionAssert.AreEqual(Svg, repeated.NormalizedSvg.ToArray());
            Assert.IsFalse(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(result.NormalizedSvg, out var first) &&
                System.Runtime.InteropServices.MemoryMarshal.TryGetArray(repeated.NormalizedSvg, out var second) && ReferenceEquals(first.Array, second.Array),
                "Callers must not receive the private cached byte array.");
        });
    }

    [TestMethod]
    [DataRow("identity")]
    [DataRow("bytes")]
    [DataRow("base64")]
    [DataRow("size")]
    public async Task CorruptPackageIconResponseCannotPopulateCache(string variant)
    {
        await Run(async channel =>
        {
            var request = await Read(channel); var body = Payload();
            if (variant == "identity") body["assetId"] = "foreign";
            if (variant == "bytes") body["normalizedSvgBase64"] = Convert.ToBase64String(new byte[Svg.Length]);
            if (variant == "base64") body["normalizedSvgBase64"] = "not-base64!";
            if (variant == "size") body["normalizedSvgBase64"] = Convert.ToBase64String(new byte[1]);
            await Reply(channel, request.RequestId, BridgeMessageTypes.PackageIcon, body);
            request = await Read(channel);
            await Reply(channel, request.RequestId, BridgeMessageTypes.PackageIcon, Payload());
        }, async (session, target) =>
        {
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.ResolvePackageIconAsync(target, "cover"));
            CollectionAssert.AreEqual(Svg, (await session.ResolvePackageIconAsync(target, "cover")).NormalizedSvg.ToArray());
        });
    }

    [TestMethod]
    public async Task ConcurrentReadersShareOneRequestAndReceiveDetachedBytes()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var request = await Read(channel);
            started.SetResult(); await release.Task.WaitAsync(Limit);
            await Reply(channel, request.RequestId, BridgeMessageTypes.PackageIcon, Payload());
        }, async (session, target) =>
        {
            // More callers than the unique-demand limit must still need only one request.
            var waiting = Enumerable.Range(0, 40).Select(_ => session.ResolvePackageIconAsync(target, "cover")).ToArray();
            await started.Task.WaitAsync(Limit);
            release.SetResult();
            var icons = await Task.WhenAll(waiting).WaitAsync(Limit);
            foreach (var icon in icons) CollectionAssert.AreEqual(Svg, icon.NormalizedSvg.ToArray());
            Assert.IsTrue(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(icons[0].NormalizedSvg, out var first));
            first.Array![first.Offset] = 0;
            foreach (var icon in icons.Skip(1)) CollectionAssert.AreEqual(Svg, icon.NormalizedSvg.ToArray());
            CollectionAssert.AreEqual(Svg, (await session.ResolvePackageIconAsync(target, "cover")).NormalizedSvg.ToArray());
        });
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task CancelingOneReaderDoesNotCancelTheSharedRequest(int canceledReader)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var request = await Read(channel);
            started.SetResult(); await release.Task.WaitAsync(Limit);
            await Reply(channel, request.RequestId, BridgeMessageTypes.PackageIcon, Payload());
        }, async (session, target) =>
        {
            using var cancellation = new CancellationTokenSource();
            var waiting = Enumerable.Range(0, 2).Select(index => session.ResolvePackageIconAsync(target, "cover",
                index == canceledReader ? cancellation.Token : CancellationToken.None)).ToArray();
            await started.Task.WaitAsync(Limit);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => waiting[canceledReader]);
            Assert.IsFalse(waiting[1 - canceledReader].IsCompleted);
            release.SetResult();
            CollectionAssert.AreEqual(Svg, (await waiting[1 - canceledReader].WaitAsync(Limit)).NormalizedSvg.ToArray());
        });
    }

    [TestMethod]
    public async Task DistinctPackageIconDemandsRemainBoundedWhileDuplicateReadersCanJoin()
    {
        var descriptor = Descriptor with { IconAssets = Enumerable.Range(0, 17)
            .Select(index => new BridgePackageIconAssetDescriptor($"cover{index}", new('d', 64), Hash, Svg.Length, Svg.Length)).ToArray() };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var active = new List<BridgeEnvelope>();
            for (var index = 0; index < 4; ++index) active.Add(await Read(channel));
            started.SetResult(); await release.Task.WaitAsync(Limit);
            foreach (var request in active)
                await Reply(channel, request.RequestId, BridgeMessageTypes.PackageIcon, Payload(request.Payload.GetProperty("assetId").GetString()!));
            for (var index = 4; index < 16; ++index)
            {
                var request = await Read(channel);
                await Reply(channel, request.RequestId, BridgeMessageTypes.PackageIcon, Payload(request.Payload.GetProperty("assetId").GetString()!));
            }
        }, async (session, target) =>
        {
            var waiting = Enumerable.Range(0, 16).Select(index => session.ResolvePackageIconAsync(target, $"cover{index}")).ToArray();
            await started.Task.WaitAsync(Limit);
            var duplicate = session.ResolvePackageIconAsync(target, "cover0");
            var error = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ResolvePackageIconAsync(target, "cover16"));
            Assert.AreEqual("package_icon_saturated", error.Code);
            release.SetResult();
            foreach (var icon in await Task.WhenAll(waiting.Append(duplicate)).WaitAsync(Limit))
                CollectionAssert.AreEqual(Svg, icon.NormalizedSvg.ToArray());
        }, descriptor);
    }

    [TestMethod]
    public async Task UndeclaredIconAndPathCannotReachBridge()
    {
        await Run(_ => Task.CompletedTask, async (session, target) =>
        {
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ResolvePackageIconAsync(target, "missing"));
            await Assert.ThrowsAsync<ArgumentException>(() => session.ResolvePackageIconAsync(target, "../cover.svg"));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.ResolvePackageIconAsync(target, "cover", cancellation.Token));
        });
    }

    [TestMethod]
    public async Task CatalogReplacementRejectsDelayedBytesAndPriorCacheAuthority()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var request = await Read(channel); started.SetResult();
            var catalog = await Read(channel);
            await Reply(channel, catalog.RequestId, BridgeMessageTypes.Widgets, new { revision = 2, isComplete = true,
                widgets = new[] { Descriptor with { PackageContentDigest = new('e', 64) } } });
            await Reply(channel, request.RequestId, BridgeMessageTypes.PackageIcon, Payload());
        }, async (session, target) =>
        {
            var pending = session.ResolvePackageIconAsync(target, "cover");
            var duplicate = session.ResolvePackageIconAsync(target, "cover");
            await started.Task.WaitAsync(Limit);
            await session.ListWidgetsAsync();
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => pending);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => duplicate);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ResolvePackageIconAsync(target, "cover"));
        });
    }

    [TestMethod]
    public async Task NewCatalogDoesNotJoinAStaleDemandEvenWhenIconMetadataIsUnchanged()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var oldRequest = await Read(channel); started.SetResult();
            var catalog = await Read(channel);
            await Reply(channel, catalog.RequestId, BridgeMessageTypes.Widgets,
                new { revision = 2, isComplete = true, widgets = new[] { Descriptor } });
            var currentRequest = await Read(channel);
            await Reply(channel, oldRequest.RequestId, BridgeMessageTypes.PackageIcon, Payload());
            await Reply(channel, currentRequest.RequestId, BridgeMessageTypes.PackageIcon, Payload());
        }, async (session, target) =>
        {
            var old = session.ResolvePackageIconAsync(target, "cover");
            await started.Task.WaitAsync(Limit);
            await session.ListWidgetsAsync();
            var current = session.ResolvePackageIconAsync(session.GetTarget("icons"), "cover");
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => old);
            CollectionAssert.AreEqual(Svg, (await current.WaitAsync(Limit)).NormalizedSvg.ToArray());
        });
    }

    [TestMethod]
    public async Task TimedOutSharedDemandIsRetiredAndLateReplyCannotCompleteRetry()
    {
        await Run(async channel =>
        {
            var old = await Read(channel);
            var retry = await Read(channel);
            var corrupt = Payload(); corrupt["assetId"] = "foreign";
            await Reply(channel, old.RequestId, BridgeMessageTypes.PackageIcon, corrupt);
            await Reply(channel, retry.RequestId, BridgeMessageTypes.PackageIcon, Payload());
        }, async (session, target) =>
        {
            var first = session.ResolvePackageIconAsync(target, "cover");
            var second = session.ResolvePackageIconAsync(target, "cover");
            await Assert.ThrowsAsync<OperationCanceledException>(() => first.WaitAsync(Limit));
            await Assert.ThrowsAsync<OperationCanceledException>(() => second.WaitAsync(Limit));
            CollectionAssert.AreEqual(Svg, (await session.ResolvePackageIconAsync(target, "cover")).NormalizedSvg.ToArray());
        }, options: new() { ArtworkTimeout = TimeSpan.FromMilliseconds(250) });
    }

    [TestMethod]
    public async Task SessionShutdownCancelsSharedDemandWithoutWaitingForBridgeReply()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel => { _ = await Read(channel); started.SetResult(); }, async (session, target) =>
        {
            var first = session.ResolvePackageIconAsync(target, "cover");
            var second = session.ResolvePackageIconAsync(target, "cover");
            await started.Task.WaitAsync(Limit);
            await session.DisposeAsync().AsTask().WaitAsync(Limit);
            await Assert.ThrowsAsync<OperationCanceledException>(() => first.WaitAsync(Limit));
            await Assert.ThrowsAsync<OperationCanceledException>(() => second.WaitAsync(Limit));
        });
    }

    private static Dictionary<string, object> Payload(string assetId = "cover") => new() { ["widgetId"] = Descriptor.Id, ["runtimeGeneration"] = Descriptor.RuntimeGeneration,
        ["presentationGeneration"] = Descriptor.PresentationGeneration, ["packageContentDigest"] = Descriptor.PackageContentDigest,
        ["assetId"] = assetId, ["sourceSha256"] = new string('d', 64), ["normalizedSha256"] = Hash, ["normalizedSvgBase64"] = Convert.ToBase64String(Svg) };
    private static async Task Run(Func<BridgeFrameChannel, Task> serverAction, Func<WidgetPresentationSession, WidgetPresentationTarget, Task> clientAction, BridgeWidgetDescriptor? descriptor = null, WidgetPresentationSessionOptions? options = null)
    {
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var catalog = await Read(channel);
            await Reply(channel, catalog.RequestId, BridgeMessageTypes.Widgets, new { revision = 1, isComplete = true, widgets = new[] { descriptor ?? Descriptor } });
            await serverAction(channel);
            var stop = await Read(channel); Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            await Reply(channel, stop.RequestId, BridgeMessageTypes.Acknowledged, new { });
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, options))
        { await session.ListWidgetsAsync(); await clientAction(session, session.GetTarget("icons")); }
        await serving.WaitAsync(Limit);
    }
    private static Task<BridgeEnvelope> Read(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Limit);
    private static async Task Reply(BridgeFrameChannel channel, long id, string type, object payload) =>
        await channel.WriteAsync(new() { RequestId = id, Type = type, Payload = BridgeJson.ToElement(payload) }, CancellationToken.None);
}
