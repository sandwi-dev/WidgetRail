using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Marketplace;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Catalog = WidgetRail.WidgetCatalog.WidgetCatalog;

/// <summary>Real widget, broker, reviewed package service and disk; fake HTTP and inert archives.
/// This is integrated logic/pipe evidence, not native UI or payload execution acceptance.</summary>
internal static class MarketplaceIntegratedWorkflow
{
    private const string First = "fixture.catalogue.first", Second = "fixture.catalogue.second";
    private const string Publisher = "fixture.catalogue", Permission = PlatformCapabilities.AudioOutputReadV1;
    internal static async Task RunAsync()
    {
        var root = Directory.CreateTempSubdirectory("wrail-catalogue-workflow-").FullName;
        try
        {
            await using var f = new Workflow(root);
            await f.Start();
            foreach (var id in new[] { First, Second })
            {
                await f.Select(id);
                await f.Act("marketplace.download");
                var review = await f.WaitOperation("review");
                Check((await f.Catalog.DiscoverAsync()).Widgets.All(w => w.Id != id), "Download installed without approval");
                await f.Act("marketplace.review.open");
                Check(f.View().InitialFocusId == "marketplace.review.cancel", "Review did not start on Cancel");
                await f.Act("marketplace.review.cancel");
                Check((await f.Catalog.DiscoverAsync()).Widgets.All(w => w.Id != id), "Review Cancel installed a package");
                await Reject(() => f.Call(new("install", OperationId: review.Operation!.Id, ReviewToken: new string('f', 64), ApproveFullTrust: true)));
                if (id == First)
                    await Reject(() => f.Call(new("install", OperationId: review.Operation!.Id, ReviewToken: review.Operation.Review!.Token)));
                await f.Act("marketplace.review.open");
                await f.Act("marketplace.install");
                await f.WaitOperation("completed");
                Check((await f.Catalog.DiscoverAsync()).Widgets.Single(w => w.Id == id).Enabled, "Explicit fresh installation did not enable the widget");
                await f.Act("marketplace.back");
                Check(f.View().InitialFocusId == Card(id), "Installed detail Back lost the listing");
            }
            Check((await f.Consent.LoadAsync()).Entries.Count == 0, "Package approval silently granted runtime permission");
            await PermissionRoundTrip(f);
            var originalSecond = (await f.Catalog.DiscoverAsync()).Widgets.Single(w => w.Id == Second).ActiveVersion;
            var originalIdentity = new BrokerWidgetIdentity(Second,
                WidgetRail.WidgetCatalog.InstalledWidgetAuthority.PublisherId(originalSecond), "original");
            f.Http.Version = "2.0.0";
            f.Clock.Now += TimeSpan.FromMinutes(16);
            await f.Act("marketplace.refresh");
            await f.WaitOperation("completed");
            await f.Act("marketplace.tab.updates");
            f.Http.FailedId = Second;
            await f.Act("marketplace.update-all");
            var batch = await f.WaitOperation("review");
            Check(batch.UpdateBatch!.Items.Single(i => i.PackageId == Second).State == "failed", "Partial failure was hidden");
            await f.Act("marketplace.batch.review");
            await f.Act("marketplace.review.cancel");
            Check((await f.Catalog.DiscoverAsync()).Widgets.All(w => w.ActiveVersion.Version == new Version(1, 0, 0)), "Batch Cancel mutated installations");
            await f.Act("marketplace.batch.review");
            await f.Act("marketplace.batch.install");
            await f.WaitOperation("completed");
            Check(f.Node("marketplace.retry-updates") is not null, "Partial update omitted Retry remaining");
            var firstDownloads = f.Http.DownloadCounts.GetValueOrDefault(First);
            f.Http.FailedId = null;
            await f.Act("marketplace.retry-updates");
            var retry = await f.WaitOperation("review");
            Check(retry.UpdateBatch!.Items.Count == 1 && retry.UpdateBatch.Items[0].PackageId == Second, "Retry included successful work");
            await f.Act("marketplace.batch.review");
            await f.Act("marketplace.batch.install");
            await f.WaitOperation("completed");
            Check(f.Http.DownloadCounts.GetValueOrDefault(First) == firstDownloads, "Retry downloaded successful work");
            var updatedSecond = (await f.Catalog.DiscoverAsync()).Widgets.Single(w => w.Id == Second).ActiveVersion;
            var updatedIdentity = new BrokerWidgetIdentity(Second,
                WidgetRail.WidgetCatalog.InstalledWidgetAuthority.PublisherId(updatedSecond), "updated");
            Check(await f.Consent.GetDecisionAsync(updatedIdentity, Permission) == ConsentDecision.Grant,
                "Catalogue update lost an approved matching permission");
            // Same catalog API used by Settings; no payload is ever launched.
            await f.Catalog.SetEnabledAsync(First, false);
            await f.Catalog.RollbackAsync(First);
            await f.Call(new("inventory"));
            await f.WaitOperation("completed");
            Check(f.Node(Card(First) + ".update") is not null, "Reverted package lacks ordinary Update");
            await f.Act("marketplace.update." + Key(First));
            await f.WaitOperation("review");
            await f.Act("marketplace.batch.review");
            await f.Act("marketplace.batch.install");
            await f.WaitOperation("completed");
            var installed = (await f.Catalog.DiscoverAsync()).Widgets;
            Check(installed.All(w => w.ActiveVersion.Version == new Version(2, 0, 0) && w.Versions.Count == 2), "Re-update did not select retained newer content");
            Check(!installed.Single(w => w.Id == First).Enabled, "Re-update changed disabled preference");
            Check(await f.Consent.GetDecisionAsync(originalIdentity, Permission) == ConsentDecision.Grant, "Update erased consent");
            Check(f.Http.MetadataRequests == 2, "Unexpected metadata refresh outside deliberate expiration");
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task PermissionRoundTrip(Workflow f)
    {
        var installed = (await f.Catalog.DiscoverAsync()).Widgets.Single(w => w.Id == Second).ActiveVersion;
        var manifest = installed.Manifest;
        var identity = new BrokerWidgetIdentity(manifest.Id, WidgetRail.WidgetCatalog.InstalledWidgetAuthority.PublisherId(installed), "installed-permission");
        await using var broker = new PlatformCapabilityBroker(identity, manifest.OptionalPermissions, f.Consent,
            new SimulatedPlatformBrokerBackend(), [PlatformCapabilities.WidgetPermissionsV1]);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        await broker.RefreshConsentAsync();
        var configured = new ConfiguredWidget { Id = "installed-permission", PackageId = identity.PackageId,
            PublisherId = identity.PublisherId, InstanceId = identity.InstanceId, Name = manifest.Name,
            WorkerExecutable = Environment.ProcessPath!, DeclaredCapabilities = manifest.OptionalPermissions,
            WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64) };
        var pipe = "catalogue-permission-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server = new WidgetBridgeServer(pipe, new([configured]), consentStore: f.Consent);
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), timeout.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: timeout.Token);
        long serial = 0;
        async Task Read() => await broker.ExecuteAsync(new(1, ++serial, identity, Permission, PlatformCapabilities.AudioOutputGet, BrokerJson.ToElement(new { })), timeout.Token);
        await Reject(Read);
        foreach (var outcome in new[] { WidgetPermissionOutcome.Denied, WidgetPermissionOutcome.Granted })
        {
            var request = BrokerJson.ParsePayload<WidgetPermissionRequestState>(await broker.ExecuteAsync(new(1, ++serial, identity,
                PlatformCapabilities.WidgetPermissionsV1, "permissions.request", BrokerJson.ToElement(new WidgetPermissionQuery([Permission]))), timeout.Token));
            var prompt = (await session.TakePermissionAsync(timeout.Token)).Prompt;
            Check(prompt is not null && prompt.RequestId == request.RequestId && prompt.WidgetId == configured.Id, "Permission prompt lost installed identity");
            Check(await session.CompletePermissionAsync(request.RequestId!, outcome, timeout.Token), "Permission response not admitted");
            Check(await f.Consent.GetDecisionAsync(identity, Permission) == (outcome == WidgetPermissionOutcome.Granted ? ConsentDecision.Grant : ConsentDecision.Deny), "Permission decision not persisted");
            if (outcome == WidgetPermissionOutcome.Denied) await Reject(Read); else await Read();
            Check(!await session.CompletePermissionAsync(request.RequestId!, outcome, timeout.Token), "Replayed permission response accepted");
        }
        await session.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (error is BrokerException or PlatformDiagnosticsException) { return; }
        throw new Exception("Unapproved operation was accepted");
    }
    private static string Key(string id) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)));
    private static string Card(string id) => "marketplace.item." + Key(id);
    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private sealed class Workflow : IAsyncDisposable
    {
        internal readonly Catalog Catalog;
        internal readonly ConsentStore Consent;
        internal readonly FakeHttp Http = new();
        internal readonly Clock Clock = new();
        internal readonly BridgeWidgetMarketplace Service;
        private readonly PlatformCapabilityBroker broker;
        private readonly BrokerWidgetIdentity manager = new("widgetrail.firstparty.marketplace", "widgetrail.firstparty", "catalogue-workflow");
        private readonly MarketplaceWidget widget;
        private int completed;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> calls = new();
        private long serial;
        internal Workflow(string root)
        {
            Catalog = new(Path.Combine(root, "widgets")); Consent = new(Path.Combine(root, "consent"));
            Service = new(Catalog, _ => Task.CompletedTask, new[] { First, Second }.Select(id => new CuratedWidget(id, Publisher, id, "Inert integrated fixture", [])).ToArray(), Http, Clock, consent: Consent);
            broker = new(manager, [], Consent, new SimulatedPlatformBrokerBackend(), [PlatformCapabilities.WidgetMarketplaceV1], AppLibrarySavedIdIssuer.Shared, marketplace: Service.HandleAsync);
            broker.SetLifecycle(BrokerLifecycleState.Interactive);
            var builder = new WidgetTestHostServicesBuilder();
            foreach (var action in new[] { "status", "refresh", "inventory", "download", "install", "cancel", "download-updates", "install-updates" })
                builder.WithHandler(WidgetMarketplaceCapabilities.Command(action), async (command, token) =>
                {
                    try { return await Call(command, token); }
                    finally { calls.AddOrUpdate(command.Action, 1, (_, count) => count + 1); Interlocked.Increment(ref completed); }
                });
            widget = WidgetTestHost.Attach(new MarketplaceWidget(), builder.Build());
        }
        internal async Task Start()
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Volatile.Read(ref completed) >= 2);
            await WaitOperation("completed");
        }
        internal async Task<MarketplaceSnapshot> Call(MarketplaceCommand command, CancellationToken token = default) =>
            BrokerJson.ParsePayload<MarketplaceSnapshot>(await broker.ExecuteAsync(new(1, Interlocked.Increment(ref serial), manager,
                PlatformCapabilities.WidgetMarketplaceV1, "marketplace." + command.Action, BrokerJson.ToElement(command)), token));
        internal ViewSnapshot View() => widget.RenderSnapshot("catalogue-workflow", 1);
        internal ViewNode? Node(string id) => Walk(View().Root).FirstOrDefault(n => n.Id == id);
        internal async Task Act(string action)
        {
            var command = action switch
            {
                "marketplace.refresh" => "refresh", "marketplace.status" => "status",
                "marketplace.download" => "download", "marketplace.install" => "install",
                "marketplace.update-all" or "marketplace.retry-updates" => "download-updates",
                "marketplace.batch.install" => "install-updates",
                _ when action.StartsWith("marketplace.update.", StringComparison.Ordinal) => "download-updates",
                _ => null
            };
            var before = command is null ? 0 : calls.GetValueOrDefault(command);
            await widget.OnActionAsync(new(action, action));
            if (command is not null) await Until(() => calls.GetValueOrDefault(command) > before);
        }
        internal async Task Select(string id) { await Act("marketplace.tab.browse"); await Act("marketplace.select." + Key(id)); }
        internal async Task<MarketplaceSnapshot> WaitOperation(string state)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            MarketplaceSnapshot snapshot;
            do
            {
                snapshot = await Call(new("status"), deadline.Token);
                if (snapshot.Operation?.State == "failed") throw new Exception(snapshot.Operation.Message);
                if (snapshot.Operation?.State == state) break;
                await Task.Delay(10, deadline.Token);
            } while (true);
            // Service jobs complete independently of widget reads. The real widget's poll reconciles running jobs.
            // Only issue a manual status read after the originating action's short work has settled.
            await Task.Delay(20, deadline.Token);
            var baseline = Volatile.Read(ref completed);
            await Act("marketplace.status");
            await Until(() => Volatile.Read(ref completed) > baseline);
            await Task.Delay(20, deadline.Token);
            return snapshot;
        }
        private static async Task Until(Func<bool> ready)
        { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12)); while (!ready()) await Task.Delay(10, timeout.Token); }
        public async ValueTask DisposeAsync() { await WidgetTestHost.DestroyAsync(widget); await broker.DisposeAsync(); await Service.DisposeAsync(); Http.Dispose(); }
    }
    private sealed class Clock : TimeProvider
    { internal DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FakeHttp : HttpMessageHandler
    {
        internal string Version = "1.0.0";
        internal string? FailedId;
        internal int MetadataRequests;
        internal readonly Dictionary<string, int> DownloadCounts = new();
        private readonly Dictionary<string, byte[]> archives = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var packages = new[] { First, Second }.ToDictionary(id => id, id =>
            {
                var key = id + Version;
                if (!archives.TryGetValue(key, out var bytes)) archives[key] = bytes = Package(id, Version);
                return bytes;
            });
            if (request.RequestUri!.Host == "api.github.com")
            {
                MetadataRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] {
                    new { draft = false, tag_name = "fixture-" + Version, prerelease = false, assets = packages.Select(pair => new {
                        name = pair.Key + "-" + Version + ".wrwidget", size = pair.Value.Length, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(pair.Value)) }).ToArray() } })) });
            }
            var id = packages.Keys.Single(id => request.RequestUri.AbsolutePath.EndsWith(id + "-" + Version + ".wrwidget", StringComparison.Ordinal));
            DownloadCounts[id] = DownloadCounts.GetValueOrDefault(id) + 1;
            return Task.FromResult(id == FailedId ? new(HttpStatusCode.ServiceUnavailable) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(packages[id]) });
        }
        private static byte[] Package(string id, string version)
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                var fullTrust = id == First; var entry = fullTrust ? "payload/Widget.exe" : "payload/Widget.dll";
                var manifest = new WidgetManifest { Id = id, Publisher = Publisher, Name = id, Version = version, HostApi = new("1.0", 1),
                    Entrypoint = fullTrust ? new("full-trust-application-v1", Executable: entry) : new("dotnet-worker", entry, "Fixture.Widget"), OptionalPermissions = [Permission] };
                using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(ManifestJson.Serialize(manifest));
                using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("Inert package fixture; never executed.");
            }
            return bytes.ToArray();
        }
    }
}
