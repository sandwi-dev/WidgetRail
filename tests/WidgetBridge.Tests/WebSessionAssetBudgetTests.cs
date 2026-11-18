using System.Security.Cryptography;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;

internal static class WebSessionAssetBudgetTests
{
    internal static Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-session-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var files = new Dictionary<string, VerifiedPackageFile>(StringComparer.Ordinal);
            var declared = new List<EmbeddedMediaResource>();
            // Exactly 2 MiB, spread over the maximum 32 resources. This also
            // exercises the larger real-artwork use case without network traffic.
            for (var i = 0; i < WebSessionContract.MaximumPackageResourceCount; ++i)
            {
                var path = i == 0 ? "index.html" : $"asset-{i}.webp";
                var bytes = new byte[WebSessionContract.MaximumPackageAggregateBytes / WebSessionContract.MaximumPackageResourceCount];
                bytes[0] = (byte)i;
                File.WriteAllBytes(Path.Combine(root, path), bytes);
                files.Add(path, Verified(path, bytes));
                declared.Add(new() { Path = path, ContentType = i == 0 ? "text/html" : "image/webp" });
            }
            var configured = new ConfiguredWidget
            {
                Id = "fixture", PackageId = "fixture.assets", PublisherId = "fixture", Name = "Fixture",
                InstanceId = "fixture.instance", WorkerExecutable = "fixture.exe", WorkerFingerprint = new string('a', 64),
                CatalogFingerprint = new string('b', 64), PackageRoot = root, VerifiedPackageFiles = files,
            };
            Assert.True(WebSessionSource.Package("index.html", declared).IsWellFormed(), "The declared package boundary is valid.");
            var bundle = EmbeddedMediaAssetResolver.ResolveWebSessionResources(configured, declared);
            Assert.Equal(32, bundle.Count);
            Assert.Equal(WebSessionContract.MaximumPackageAggregateBytes, bundle.Sum(resource => Convert.FromBase64String(resource.ContentBase64).Length));
            Assert.True(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bundle).Length < BridgeProtocol.DefaultMaximumMessageBytes,
                "Encoded package bundle fits the existing bridge frame.");
            Assert.Throws<BridgeProtocolException>(() => EmbeddedMediaAssetResolver.ResolveResources(configured, declared));
            Assert.Throws<BridgeProtocolException>(() => EmbeddedMediaAssetResolver.ResolveResources(configured, declared.Take(9).ToArray()));
            var extra = declared.Append(new EmbeddedMediaResource { Path = "extra.webp", ContentType = "image/webp" }).ToArray();
            Assert.True(!WebSessionSource.Package("index.html", extra).IsWellFormed(), "Count overflow is rejected at declaration.");
            Assert.Throws<BridgeProtocolException>(() => EmbeddedMediaAssetResolver.ResolveWebSessionResources(configured, extra));

            // One byte over aggregate, then one byte over the unchanged per-file bound.
            Replace("asset-31.webp", new byte[65537]);
            Assert.Throws<BridgeProtocolException>(() => EmbeddedMediaAssetResolver.ResolveWebSessionResources(configured, declared));
            Replace("asset-31.webp", new byte[WebSessionContract.MaximumPackageResourceBytes + 1]);
            Assert.Throws<BridgeProtocolException>(() => EmbeddedMediaAssetResolver.ResolveWebSessionResources(configured, [declared[^1]]));
            Replace("asset-31.webp", new byte[32]);
            File.WriteAllBytes(Path.Combine(root, "asset-31.webp"), Enumerable.Repeat((byte)1, 32).ToArray());
            Assert.Throws<BridgeProtocolException>(() => EmbeddedMediaAssetResolver.ResolveWebSessionResources(configured, [declared[^1]]));

            var adapter = new WebSessionPageAdapter { ScriptAsset="backend.js", Origins=["https://example.com"], Mode=WebSessionPageAdapterMode.SessionBackend };
            Replace("backend.js", Enumerable.Repeat((byte)'x', WebSessionContract.MaximumSessionBackendScriptBytes).ToArray());
            var script = EmbeddedMediaAssetResolver.ResolveWebSessionAdapter(configured, adapter).Single();
            Assert.Equal(WebSessionContract.MaximumSessionBackendScriptBytes, adapter.DecodeScript(script.ContentBase64,script.Sha256).Length);
            Assert.True(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(script).Length < 180*1024 && 180*1024 < BridgeProtocol.DefaultMaximumMessageBytes,
                "Maximum backend resource leaves ample room in the existing bridge frame without transport changes.");
            Assert.Throws<BridgeProtocolException>(()=>EmbeddedMediaAssetResolver.ResolveWebSessionAdapter(configured,
                adapter with{Mode=WebSessionPageAdapterMode.IsolatedPageActions}));
            Replace("backend.js", new byte[WebSessionContract.MaximumSessionBackendScriptBytes+1]);
            Assert.Throws<BridgeProtocolException>(()=>EmbeddedMediaAssetResolver.ResolveWebSessionAdapter(configured,adapter));
            Replace("backend.js", Enumerable.Repeat((byte)'x',WebSessionContract.MaximumPageActionScriptBytes).ToArray());
            Assert.Equal(1,EmbeddedMediaAssetResolver.ResolveWebSessionAdapter(configured,adapter with{Mode=WebSessionPageAdapterMode.IsolatedPageActions}).Count);
            File.WriteAllBytes(Path.Combine(root,"backend.js"),new byte[WebSessionContract.MaximumPageActionScriptBytes]);
            Assert.Throws<BridgeProtocolException>(()=>EmbeddedMediaAssetResolver.ResolveWebSessionAdapter(configured,adapter));

            void Replace(string path, byte[] bytes)
            {
                File.WriteAllBytes(Path.Combine(root, path), bytes);
                files[path] = Verified(path, bytes);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }

    private static VerifiedPackageFile Verified(string path, byte[] bytes) =>
        new(path, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private static class Assert
    {
        internal static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        internal static void Equal(int expected, int actual) => True(expected == actual, $"Expected {expected}, got {actual}.");
        internal static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }
    }
}
