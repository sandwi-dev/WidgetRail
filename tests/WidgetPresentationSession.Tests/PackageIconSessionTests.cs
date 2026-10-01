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
    public async Task PackageIconDemandIsBoundedAndConcurrentReadersReuseAdmittedBytes()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var active = new List<BridgeEnvelope>();
            for (var index = 0; index < 4; ++index) active.Add(await Read(channel));
            started.SetResult(); await release.Task.WaitAsync(Limit);
            foreach (var request in active) await Reply(channel, request.RequestId, BridgeMessageTypes.PackageIcon, Payload());
        }, async (session, target) =>
        {
            var waiting = Enumerable.Range(0, 16).Select(_ => session.ResolvePackageIconAsync(target, "cover")).ToArray();
            await started.Task.WaitAsync(Limit);
            var error = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ResolvePackageIconAsync(target, "cover"));
            Assert.AreEqual("package_icon_saturated", error.Code);
            release.SetResult();
            foreach (var icon in await Task.WhenAll(waiting).WaitAsync(Limit)) CollectionAssert.AreEqual(Svg, icon.NormalizedSvg.ToArray());
        });
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
            await started.Task.WaitAsync(Limit);
            await session.ListWidgetsAsync();
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => pending);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ResolvePackageIconAsync(target, "cover"));
        });
    }

    private static Dictionary<string, object> Payload() => new() { ["widgetId"] = Descriptor.Id, ["runtimeGeneration"] = Descriptor.RuntimeGeneration,
        ["presentationGeneration"] = Descriptor.PresentationGeneration, ["packageContentDigest"] = Descriptor.PackageContentDigest,
        ["assetId"] = "cover", ["sourceSha256"] = new string('d', 64), ["normalizedSha256"] = Hash, ["normalizedSvgBase64"] = Convert.ToBase64String(Svg) };
    private static async Task Run(Func<BridgeFrameChannel, Task> serverAction, Func<WidgetPresentationSession, WidgetPresentationTarget, Task> clientAction)
    {
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var catalog = await Read(channel);
            await Reply(channel, catalog.RequestId, BridgeMessageTypes.Widgets, new { revision = 1, isComplete = true, widgets = new[] { Descriptor } });
            await serverAction(channel);
            var stop = await Read(channel); Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            await Reply(channel, stop.RequestId, BridgeMessageTypes.Acknowledged, new { });
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        { await session.ListWidgetsAsync(); await clientAction(session, session.GetTarget("icons")); }
        await serving.WaitAsync(Limit);
    }
    private static Task<BridgeEnvelope> Read(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Limit);
    private static async Task Reply(BridgeFrameChannel channel, long id, string type, object payload) =>
        await channel.WriteAsync(new() { RequestId = id, Type = type, Payload = BridgeJson.ToElement(payload) }, CancellationToken.None);
}
