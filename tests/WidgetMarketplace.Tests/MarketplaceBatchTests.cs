using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.PlatformSettings;
using Catalog = WidgetRail.WidgetCatalog.WidgetCatalog;

internal static class MarketplaceBatchTests
{
    internal static async Task ApprovalAndResults()
    {
        await using var f = new Fixture(); await f.Initialize();
        var stopped = new WidgetStopStateStore(new PlatformSettingsPaths(Path.Combine(f.Root, "profile")));
        await stopped.SetStoppedAsync(Fixture.First, true);
        var review = await f.Review();
        Check(review.UpdateBatch!.Items.All(item => item.State == "review"), "Both packages must be verified before aggregate approval");
        Check((await f.Catalog.DiscoverAsync()).Widgets.All(widget => widget.ActiveVersion.Version.ToString() == "1.0.0"), "Download mutated installations");
        var approve = f.Approve(review);
        await Rejected(() => f.Service.HandleAsync(approve with { ApproveFullTrust = false }, default));
        await Rejected(() => f.Service.HandleAsync(approve with { ReviewToken = new string('f',64) }, default));
        await Rejected(() => f.Service.HandleAsync(approve with { Action = "install" }, default));
        await f.Service.HandleAsync(approve, default);
        var result = await f.Wait("completed");
        Check(result.UpdateBatch!.Items.All(item => item.State == "completed" && item.Review is null), "Per-item completion or review retirement missing");
        var widgets = (await f.Catalog.DiscoverAsync()).Widgets;
        Check(widgets.All(widget => widget.ActiveVersion.Version.ToString() == "2.0.0" && widget.Versions.Count == 2), "Batch did not preserve previous versions");
        Check(widgets.Single(widget => widget.Id == Fixture.First).Enabled && !widgets.Single(widget => widget.Id == Fixture.Second).Enabled, "Batch changed enabled/disabled preferences");
        Check((await new WidgetStopStateStore(new PlatformSettingsPaths(Path.Combine(f.Root, "profile"))).LoadAsync()).SequenceEqual(new[] { Fixture.First }), "Package update erased persistent stopped state.");
        await Rejected(() => f.Service.HandleAsync(approve, default));
        foreach (var id in f.Ids)
        {
            await f.Catalog.SetEnabledAsync(id, false);
            await f.Catalog.RollbackAsync(id);
        }
        await f.Service.HandleAsync(new("inventory"), default);
        await f.Wait("completed");
        var restoredReview = await f.Review();
        await f.Service.HandleAsync(f.Approve(restoredReview), default);
        await f.Wait("completed");
        Check((await f.Catalog.DiscoverAsync()).Widgets.All(widget => widget.ActiveVersion.Version == new Version(2, 0, 0) && widget.Versions.Count == 2),
            "Update all failed to reuse retained versions after rollback");
        await f.Service.DisposeAsync();
        Check(!Directory.EnumerateFiles(f.Root, "*.wrwidget", SearchOption.AllDirectories).Any(), "Completed batch retained archives");
    }

    internal static async Task PartialFailureAndRetry()
    {
        await using var f = new Fixture(); await f.Initialize();
        f.Http.Failures.Add(Fixture.Second);
        var review = await f.Review();
        Check(review.UpdateBatch!.Items.Single(item => item.PackageId == Fixture.Second).State == "failed", "Download failure was hidden");
        await f.Service.HandleAsync(f.Approve(review), default);
        var completed = await f.Wait("completed");
        Check(completed.UpdateBatch!.Items.Single(item => item.PackageId == Fixture.First).State == "completed", "One failed download prevented the successful update");
        f.Http.Failures.Clear();
        await f.Service.HandleAsync(new("download-updates", PackageIds: [Fixture.Second]), default);
        var retry = await f.Wait("review");
        Check(retry.UpdateBatch!.Items.Count == 1 && retry.UpdateBatch.Items[0].PackageId == Fixture.Second, "Retry included a committed update");
        await f.Service.HandleAsync(f.Approve(retry), default);
        await f.Wait("completed");
        Check((await f.Catalog.DiscoverAsync()).Widgets.All(widget => widget.ActiveVersion.Version.ToString() == "2.0.0"), "Failed update could not be retried");
        Check(f.Http.DownloadCounts[Fixture.First] == 1 && f.Http.DownloadCounts[Fixture.Second] == 2, "Retry redownloaded successful work");
    }

    internal static async Task CancelDownloadAndReview()
    {
        await using var f = new Fixture(); await f.Initialize();
        f.Http.BlockId = Fixture.Second;
        f.Http.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = await f.Service.HandleAsync(new("download-updates", PackageIds: [Fixture.First, Fixture.Second]), default);
        await f.Http.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var status = await f.Service.HandleAsync(new("status"), default);
        Check(status.UpdateBatch!.Items[0].State == "review" && status.UpdateBatch.ReviewToken is null, "An incomplete batch became approvable");
        await f.Service.HandleAsync(new("cancel", OperationId: running.Operation!.Id), default);
        var cancelled = await f.Wait("cancelled");
        Check(cancelled.UpdateBatch!.Items.All(item => item.State == "cancelled"), "Cancellation did not retire queued/reviewed work");
        Check(!Directory.EnumerateFiles(f.Root, "*.wrwidget", SearchOption.AllDirectories).Any(), "Cancelled download retained a package");
        f.Http.Gate.SetResult(); f.Http.BlockId = null;
        var review = await f.Review();
        await f.Service.HandleAsync(new("cancel", OperationId: review.Operation!.Id), default);
        await Rejected(() => f.Service.HandleAsync(f.Approve(review), default));

        Check((await f.Catalog.DiscoverAsync()).Widgets.All(widget => widget.ActiveVersion.Version.ToString() == "1.0.0"), "Cancelled review changed a widget");
        await f.WaitForArchiveCleanup();
    }

    internal static async Task CancelAfterCommit()
    {
        await using var f = new Fixture(); await f.Initialize();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.AfterReload = async () => { entered.TrySetResult(); await release.Task; };
        var review = await f.Review();
        try
        {
            var installing = await f.Service.HandleAsync(f.Approve(review), default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await f.Service.HandleAsync(new("cancel", OperationId: installing.Operation!.Id), default);
        }
        finally { release.TrySetResult(); }
        var result = await f.Wait("cancelled");
        Check(result.UpdateBatch!.Items[0].State == "completed" && result.UpdateBatch.Items[1].State == "cancelled", "A durable commit was mislabeled cancelled");
        var widgets = (await f.Catalog.DiscoverAsync()).Widgets;
        Check(widgets.Single(widget => widget.Id == Fixture.First).ActiveVersion.Version.ToString() == "2.0.0" &&
            widgets.Single(widget => widget.Id == Fixture.Second).ActiveVersion.Version.ToString() == "1.0.0", "Cancellation changed untouched work");
    }

    internal static async Task ExpiryAndChangedBaseline()
    {
        await using var f = new Fixture(); await f.Initialize();
        var expired = await f.Review();
        f.Clock.Now += TimeSpan.FromMinutes(11);
        await Rejected(() => f.Service.HandleAsync(f.Approve(expired), default));
        var status = await f.Service.HandleAsync(new("status"), default);
        Check(status.UpdateBatch!.Items.All(item => item.State == "failed") && status.UpdateBatch.ReviewToken is null, "Expired batch retained approval");
        await f.WaitForArchiveCleanup();
        var review = await f.Review();
        await f.Catalog.SetEnabledAsync(Fixture.First, false);
        using (var package = new MemoryStream(Fixture.Package(Fixture.First, "1.5.0", true)))
            await f.Catalog.InstallAsync(package, WidgetPackageTrustApproval.FullTrustCurrentUser);
        await f.Catalog.SetActiveVersionAsync(Fixture.First, new("1.5.0"));
        await f.Service.HandleAsync(f.Approve(review), default);
        var result = await f.Wait("completed");
        Check(result.UpdateBatch!.Items[0].State == "failed" && result.UpdateBatch.Items[1].State == "completed", "Stale baseline was installed or stopped independent valid work");
        Check((await f.Catalog.DiscoverAsync()).Widgets.Single(widget => widget.Id == Fixture.First).ActiveVersion.Version.ToString() == "1.5.0", "Stale approval replaced a changed version");
    }

    internal static async Task BoundsAndDisposal()
    {
        await using var f = new Fixture(); await f.Initialize();
        foreach (var command in new[] { new MarketplaceCommand("download-updates", PackageIds: []),
            new("download-updates", PackageIds: [Fixture.First, Fixture.First]),
            new("download-updates", PackageIds: ["dev.batch.uninstalled"]),
            new("status", PackageIds: [Fixture.First]) })
            await Rejected(() => f.Service.HandleAsync(command, default));
        await f.Review();
        await f.Service.DisposeAsync();
        Check(!Directory.EnumerateFiles(f.Root, "*.wrwidget", SearchOption.AllDirectories).Any(), "Shutdown retained pending batch archives");
    }

    internal static async Task ReviewTransportBound()
    {
        await using var f = new Fixture(16, largeReviews: true); await f.Initialize();
        var review = await f.Review();
        Check(review.UpdateBatch!.Items.Any(item => item.State == "review") && review.UpdateBatch.Items.Any(item => item.State == "failed"),
            "Oversized review set was not divided into a bounded review and individual retries");
        var response = BrokerJson.SerializeResponse(new(1,1,true,BrokerJson.ToElement(review),null));
        Check(response.Length < BrokerJson.MaximumResponseBytes, "Aggregate review exceeds the real broker transport bound");
        Check(review.UpdateBatch.Items.Where(item => item.State == "review").All(item => item.Review!.OptionalPermissions.Count == ProtocolConstants.MaximumManifestPermissionCount),
            "Review was made smaller by omitting permissions");
    }

    private static async Task Rejected(Func<ValueTask<MarketplaceSnapshot>> action)
    {
        try { await action(); throw new Exception("Unreviewed/invalid command accepted"); }
        catch (PlatformDiagnosticsException) { }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private sealed class Fixture : IAsyncDisposable
    {
        internal const string First = "dev.batch.one", Second = "dev.batch.two";
        internal readonly string Root = Directory.CreateTempSubdirectory("wrail-marketplace-batch-").FullName;
        internal readonly Catalog Catalog;
        internal readonly HttpFixture Http;
        internal readonly string[] Ids;
        internal readonly FixtureClock Clock = new();
        internal readonly BridgeWidgetMarketplace Service;
        internal Func<Task>? AfterReload;
        internal Fixture(int count = 2, bool largeReviews = false)
        {
            Ids = Enumerable.Range(0,count).Select(index => index == 0 ? First : index == 1 ? Second : "dev.batch.extra"+index).ToArray();
            Http = new(Ids,largeReviews);
            Catalog = new(Root);
            Service = new(Catalog, _ => AfterReload?.Invoke() ?? Task.CompletedTask,
                Ids.Select(id => new CuratedWidget(id,"dev.batch",id,"Synthetic update",[])).ToArray(), Http, Clock);
        }
        internal async Task Initialize()
        {
            foreach (var id in Ids)
            {
                using var package = new MemoryStream(Package(id,"1.0.0",id == First));
                await Catalog.InstallAsync(package, WidgetPackageTrustApproval.FullTrustCurrentUser);
            }
            await Catalog.SetEnabledAsync(First, true, WidgetPackageTrustApproval.FullTrustCurrentUser);
            await Service.HandleAsync(new("refresh"),default); await Wait("completed");
        }
        internal async Task<MarketplaceSnapshot> Review()
        {
            await Service.HandleAsync(new("download-updates",PackageIds:Ids),default);
            return await Wait("review");
        }
        internal MarketplaceCommand Approve(MarketplaceSnapshot snapshot) => new("install-updates", OperationId:snapshot.Operation!.Id,
            ReviewToken:snapshot.UpdateBatch!.ReviewToken, ApproveFullTrust:true);
        internal async Task<MarketplaceSnapshot> Wait(string state)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (true)
            {
                var snapshot = await Service.HandleAsync(new("status"),timeout.Token);
                MarketplaceContract.ValidateSnapshot(snapshot);
                if (snapshot.Operation?.State == state) return snapshot;
                if (snapshot.Operation?.State == "failed") throw new Exception(snapshot.Operation.Message);
                await Task.Delay(10,timeout.Token);
            }
        }
        internal async Task WaitForArchiveCleanup()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Directory.EnumerateFiles(Root,"*.wrwidget",SearchOption.AllDirectories).Any())
                await Task.Delay(10,timeout.Token);
        }
        internal static byte[] Package(string id,string version,bool fullTrust, string[]? permissions = null)
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes,ZipArchiveMode.Create,true))
            {
                var entry = fullTrust ? "payload/Widget.exe" : "payload/Widget.dll";
                var manifest = new WidgetManifest { Id=id, Publisher="dev.batch", Name=id, Version=version, HostApi=new("1.0",1),
                    Entrypoint=fullTrust ? new("full-trust-application-v1",Executable:entry) : new("dotnet-worker",entry,"Example.Widget"), OptionalPermissions = permissions ?? [] };
                Check(WidgetManifestValidator.Validate(manifest).Count == 0, "Synthetic package manifest must be valid");
                using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(ManifestJson.Serialize(manifest));
                using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("Synthetic bytes; never executed");
            }
            return bytes.ToArray();
        }
        public async ValueTask DisposeAsync()
        { await Service.DisposeAsync(); Http.Dispose(); Directory.Delete(Root,true); }
    }
    private sealed class FixtureClock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class HttpFixture : HttpMessageHandler
    {
        private readonly Dictionary<string,byte[]> packages;
        internal HttpFixture(string[] ids, bool largeReviews)
        {
            var permissions = largeReviews ? Enumerable.Range(0,ProtocolConstants.MaximumManifestPermissionCount)
                .Select(index => "external."+new string('a',108)+index+".read.v1").ToArray() : null;
            packages = ids.ToDictionary(id => id, id => Fixture.Package(id,"2.0.0",id == Fixture.First,permissions));
        }
        internal readonly HashSet<string> Failures = [];
        internal readonly Dictionary<string,int> DownloadCounts = new();
        internal string? BlockId;
        internal TaskCompletionSource? Gate;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if (request.RequestUri!.Host == "api.github.com")
                return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new[] { new { draft=false,tag_name="v-batch",prerelease=false,
                    assets=packages.Select(item => new { name=item.Key+"-2.0.0.wrwidget",size=item.Value.Length,digest="sha256:"+Convert.ToHexString(SHA256.HashData(item.Value)) }).ToArray() } })) };
            var id = packages.Keys.Single(id => request.RequestUri.AbsolutePath.EndsWith(id+"-2.0.0.wrwidget",StringComparison.Ordinal));
            DownloadCounts[id]=DownloadCounts.GetValueOrDefault(id)+1;
            if (id == BlockId && Gate is not null) { Entered.TrySetResult(); await Gate.Task.WaitAsync(token); }
            return Failures.Contains(id) ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK) { Content=new ByteArrayContent(packages[id]) };
        }
    }
}
