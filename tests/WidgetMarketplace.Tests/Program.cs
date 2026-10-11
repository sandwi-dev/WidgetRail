using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PackageDistribution;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using Catalog = WidgetRail.WidgetCatalog.WidgetCatalog;

if (args is ["--probe-live-review"])
{
    var root = Path.Combine(Path.GetTempPath(), "wrail-marketplace-probe-" + Guid.NewGuid().ToString("N"));
    try
    {
        var catalog = new Catalog(root);
        await using (var service = new BridgeWidgetMarketplace(catalog, _ => Task.CompletedTask))
        {
            await service.HandleAsync(new("refresh"), default);
            var refreshed = await Wait(service, "completed", TimeSpan.FromSeconds(40));
            Console.WriteLine(JsonSerializer.Serialize(refreshed.Items.Select(i => new { i.Id, i.AvailableVersion, i.DownloadBytes })));
            await service.HandleAsync(new("download", PackageId: "widgetrail.samples.youtube-video"), default);
            var ready = await Wait(service, "review", TimeSpan.FromMinutes(3));
            var review = ready.Operation!.Review!;
            Console.WriteLine(JsonSerializer.Serialize(new { review.PackageId, review.Version, review.Compatible, review.FullTrust,
                review.RequiredPermissions, review.OptionalPermissions, review.Sha256 }));
            await service.HandleAsync(new("cancel", OperationId: ready.Operation.Id), default);
            Check((await catalog.DiscoverAsync()).Widgets.Count == 0, "Read-only probe installed a widget");
        }
        Check(!Directory.EnumerateFiles(root, "*.wrwidget", SearchOption.AllDirectories).Any(), "Probe retained a download");
        Console.WriteLine("Live metadata and archive review passed; no widget installed; download discarded.");
        return 0;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Download review leaves an enabled old version untouched; approval selects update disabled", UpdateReview),
    ("Full-access installation requires exact reviewed approval and rejects replay", FullTrust),
    ("A changed installed baseline invalidates its permission review", ChangedBaseline),
    ("Expired reviews release the archive and cannot install", Expiry),
    ("Wrong package identity fails without catalog mutation", IdentityMismatch),
    ("Cancellation reaches the network job without blocking status", Cancellation),
    ("Curated embedded inventory and command contracts are bounded", Contracts),
};
var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static async Task UpdateReview()
{
    await using var fixture = new Fixture("2.0.0", permissions: ["system.audio.sessions.read.v1"]);
    using (var old = new MemoryStream(Fixture.Package("1.0.0"))) await fixture.Catalog.InstallAsync(old);
    await fixture.Catalog.SetEnabledAsync(Fixture.Id, true);
    var review = await fixture.Review();
    var previous = (await fixture.Catalog.DiscoverAsync()).Widgets.Single();
    Check(previous.Enabled && previous.ActiveVersion.Version == new Version("1.0.0"), "Review changed the active widget");
    Check(review.Operation!.Review!.AddedPermissions.SequenceEqual(["system.audio.sessions.read.v1"]), "New permission was not identified");
    await fixture.Service.HandleAsync(new("install", OperationId: review.Operation.Id, ReviewToken: review.Operation.Review.Token), default);
    await Wait(fixture.Service, "completed");
    var updated = (await fixture.Catalog.DiscoverAsync()).Widgets.Single();
    Check(!updated.Enabled && updated.ActiveVersion.Version == new Version("2.0.0") && updated.Versions.Count == 2,
        "Update failed to select disabled and retain rollback");
    Check(PackageSources.Find(fixture.Root, "widget", Fixture.Id, "2.0.0", updated.ActiveVersion.ContentDigest)?.Source
        == $"github:sandwi-dev/WidgetRail@v-test/{Fixture.Id}-2.0.0.wrwidget", "CLI source metadata was not preserved");
    Check(fixture.Reloads == 1 && fixture.Service.TakeNotification().Message.Length != 0, "Completed install did not refresh the host");
    await fixture.Catalog.RollbackAsync(Fixture.Id, new Version("1.0.0"));
    Check((await fixture.Catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version == new Version("1.0.0"), "Existing rollback stopped working");
    await fixture.Service.HandleAsync(new("inventory"), default);
    var inventory = await Wait(fixture.Service, "completed");
    Check(inventory.Items.Single().InstalledVersion == "1.0.0" && inventory.Items.Single().AvailableVersionSaved,
        "Rollback did not refresh the selected version or offered a duplicate download");
}
static async Task FullTrust()
{
    await using var fixture = new Fixture("1.0.0", fullTrust: true);
    var review = await fixture.Review();
    var command = new MarketplaceCommand("install", OperationId: review.Operation!.Id, ReviewToken: review.Operation.Review!.Token);
    await Reject(() => fixture.Service.HandleAsync(command, default).AsTask());
    Check((await fixture.Catalog.DiscoverAsync()).Widgets.Count == 0, "Unapproved full access was installed");
    await Reject(() => fixture.Service.HandleAsync(command with { ReviewToken = new string('A', 64), ApproveFullTrust = true }, default).AsTask());
    await fixture.Service.HandleAsync(command with { ApproveFullTrust = true }, default);
    await Wait(fixture.Service, "completed");
    await Reject(() => fixture.Service.HandleAsync(command with { ApproveFullTrust = true }, default).AsTask());
    Check(!(await fixture.Catalog.DiscoverAsync()).Widgets.Single().Enabled, "Full-access install auto-enabled");
}
static async Task ChangedBaseline()
{
    await using var fixture = new Fixture("2.0.0");
    using (var first = new MemoryStream(Fixture.Package("1.0.0"))) await fixture.Catalog.InstallAsync(first);
    var review = await fixture.Review();
    using (var other = new MemoryStream(Fixture.Package("1.5.0"))) await fixture.Catalog.InstallAsync(other);
    await fixture.Catalog.SetActiveVersionAsync(Fixture.Id, new Version("1.5.0"));
    await fixture.Service.HandleAsync(new("install", OperationId: review.Operation!.Id, ReviewToken: review.Operation.Review!.Token), default);
    var failed = await Wait(fixture.Service, "failed");
    Check(failed.Operation!.Message!.Contains("changed after review"), "Stale baseline was not identified");
    var installed = (await fixture.Catalog.DiscoverAsync()).Widgets.Single();
    Check(installed.ActiveVersion.Version == new Version("1.5.0") && installed.Versions.Count == 2, "Stale review published a new version");
}
static async Task Expiry()
{
    await using var fixture = new Fixture("1.0.0");
    var review = await fixture.Review();
    fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
    await Reject(() => fixture.Service.HandleAsync(new("install", OperationId: review.Operation!.Id, ReviewToken: review.Operation.Review!.Token), default).AsTask());
    Check(!Directory.EnumerateFiles(fixture.Root, "*.wrwidget", SearchOption.AllDirectories).Any(), "Expired review retained its file");
    Check((await fixture.Catalog.DiscoverAsync()).Widgets.Count == 0, "Expired review installed");
}
static async Task IdentityMismatch()
{
    await using var fixture = new Fixture("1.0.0", wrongId: true);
    await fixture.Service.HandleAsync(new("refresh"), default); await Wait(fixture.Service, "completed");
    await fixture.Service.HandleAsync(new("download", PackageId: Fixture.Id), default);
    await Wait(fixture.Service, "failed");
    Check((await fixture.Catalog.DiscoverAsync()).Widgets.Count == 0, "Wrong identity was admitted");
}
static async Task Cancellation()
{
    await using var fixture = new Fixture("1.0.0");
    fixture.Handler.DownloadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await fixture.Service.HandleAsync(new("refresh"), default); await Wait(fixture.Service, "completed");
    var running = await fixture.Service.HandleAsync(new("download", PackageId: Fixture.Id), default);
    await fixture.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check((await fixture.Service.HandleAsync(new("status"), default)).Operation!.State == "running", "Status blocked behind network");
    await fixture.Service.HandleAsync(new("cancel", OperationId: running.Operation!.Id), default);
    await Wait(fixture.Service, "cancelled");
    Check((await fixture.Catalog.DiscoverAsync()).Widgets.Count == 0, "Cancelled download installed");
}
static async Task Contracts()
{
    var root = Path.Combine(Path.GetTempPath(), "marketplace-inventory-" + Guid.NewGuid().ToString("N"));
    await using var service = new BridgeWidgetMarketplace(new Catalog(root), _ => Task.CompletedTask);
    var snapshot = await service.HandleAsync(new("status"), default);
    MarketplaceContract.ValidateSnapshot(snapshot);
    Check(snapshot.Items.Count == 4 && snapshot.Items.Any(i => i.Screenshots.Count == 2), "Curated inventory or screenshots missing");
    foreach (var command in new[] { new MarketplaceCommand("install"), new("status", ApproveFullTrust: true), new("download", PackageId: "../../bad") })
        await Reject(() => service.HandleAsync(command, default).AsTask());
}
static async Task<MarketplaceSnapshot> Wait(BridgeWidgetMarketplace service, string state, TimeSpan? timeout = null)
{
    using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
    while (true)
    {
        var result = await service.HandleAsync(new("status"), deadline.Token);
        MarketplaceContract.ValidateSnapshot(result);
        if (result.Operation?.State == state) return result;
        if (result.Operation?.State == "failed") throw new Exception(result.Operation.Message);
        await Task.Delay(10, deadline.Token);
    }
}
static async Task Reject(Func<Task> action)
{
    try { await action(); } catch (PlatformDiagnosticsException) { return; }
    throw new Exception("Expected marketplace request rejection");
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

file sealed class Fixture : IAsyncDisposable
{
    internal const string Id = "dev.test.marketplace";
    internal string Root = Path.Combine(Path.GetTempPath(), "wrail-marketplace-test-" + Guid.NewGuid().ToString("N"));
    internal Catalog Catalog;
    internal FakeHttp Handler;
    internal BridgeWidgetMarketplace Service;
    internal Clock Clock = new();
    internal int Reloads;
    internal Fixture(string version, bool fullTrust = false, string[]? permissions = null, bool wrongId = false)
    {
        Catalog = new(Root);
        Handler = new(Package(version, fullTrust, permissions, wrongId), version);
        Service = new(Catalog, _ => { Reloads++; return Task.CompletedTask; },
            [new(Id, "dev.test", "Test widget", "A fixture widget.", [])], Handler, Clock);
    }
    internal async Task<MarketplaceSnapshot> Review()
    {
        await Service.HandleAsync(new("refresh"), default);
        await AwaitState("completed");
        await Service.HandleAsync(new("download", PackageId: Id), default);
        return await AwaitState("review");
    }
    private async Task<MarketplaceSnapshot> AwaitState(string state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var result = await Service.HandleAsync(new("status"), timeout.Token);
            if (result.Operation?.State == state) return result;
            if (result.Operation?.State == "failed") throw new Exception(result.Operation.Message);
            await Task.Delay(10, timeout.Token);
        }
    }
    internal static byte[] Package(string version, bool fullTrust = false, string[]? permissions = null, bool wrongId = false)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var entry = fullTrust ? "payload/Widget.exe" : "payload/Widget.dll";
            var manifest = new WidgetManifest { Id = wrongId ? "dev.test.other" : Id, Publisher = "dev.test", Name = "Test widget", Version = version,
                HostApi = new("1.0", 1), Entrypoint = fullTrust ? new("full-trust-application-v1", Executable: entry) : new("dotnet-worker", entry, "Example.Widget"),
                Permissions = permissions ?? [] };
            using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(ManifestJson.Serialize(manifest));
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("fixture; never executed");
        }
        return buffer.ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync(); Handler.Dispose();
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
file sealed class Clock : TimeProvider
{
    internal DateTimeOffset Now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
file sealed class FakeHttp(byte[] package, string version) : HttpMessageHandler
{
    internal TaskCompletionSource? DownloadGate;
    internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri!.Host == "api.github.com")
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { new { draft = false, tag_name = "v-test", prerelease = true,
                assets = new[] { new { name = Fixture.Id + "-" + version + ".wrwidget", size = package.Length, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(package)) } } } })) };
        Entered.TrySetResult();
        if (DownloadGate is { } gate) await gate.Task.WaitAsync(token);
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(package) };
    }
}
