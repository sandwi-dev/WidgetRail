using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

internal static class SettingsNavigationFixture
{
    internal const string Package = "fixture.navigation-target";
    internal static HashSet<string> SettingsIconAssetIds() => ManifestJson.Deserialize(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixture", "settings-manifest.json")))
        .IconAssets.Keys.ToHashSet(StringComparer.Ordinal);
    internal static async Task ServeAsync(string pipe, string root)
    {
        Seed(root);
        var paths = new PlatformSettingsPaths(Path.GetFullPath(root));
        var store = new PlatformSettingsStore(paths);
        await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
        await appearance.StartAsync();
        var catalog = Catalog(root).WithStoppedWidgets(await new WidgetStopStateStore(paths).LoadAsync());
        await using var server = new WidgetBridgeServer(pipe, catalog, 256 * 1024, appearance: appearance,
            consentStore: null, platformBackend: null, catalogMonitor: null, residencyBudget: null,
            capabilityDiagnosticSink: null, settingsStore: store);
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }
    internal static SettingsWidget CreateSettings(string root)
    {
        var paths = new PlatformSettingsPaths(root);
        return new(new PlatformSettingsStore(paths), new ThemeCatalog(paths), new WidgetRail.WidgetCatalog.WidgetCatalog(Path.Combine(root, "widgets")),
            new ConsentStore(Path.Combine(root, "consent")), new SlowDiagnostics(), bundledWidgetRoot: Path.Combine(root, "bundled"));
    }
    internal static async Task ThroughPipes()
    {
        var root = Directory.CreateTempSubdirectory("wrail-settings-navigation-").FullName;
        try
        {
            Seed(root);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var pipe = "settings-navigation-" + Guid.NewGuid().ToString("N");
            await using var server = new WidgetBridgeServer(pipe, Catalog(root));
            var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
            await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
            await session.ListWidgetsAsync(deadline.Token);
            for (var cycle = 0; cycle < 4; cycle++)
            {
                var source = await session.EstablishPresentationAsync(session.GetTarget("source"), WidgetLifecycleState.Interactive, deadline.Token);
                var preparation = cycle < 2
                    ? await session.PrepareIntentAsync(source, new("manage", "manage", ControllerButton.A, InputScopeId: "root"), deadline.Token)
                    : await session.PrepareWidgetSettingsIntentAsync(session.GetTarget("managed"), deadline.Token);
                Check(preparation.Kind == WidgetIntentLaunchKind.Widget && preparation.Destinations.Single().WidgetId == "settings",
                    "Management request did not cross the bridge to Settings.");
                await session.SetLifecycleAsync(session.GetTarget("source"), WidgetLifecycleState.Background, deadline.Token);
                var target = await session.EstablishPresentationAsync(session.GetTarget("settings"), WidgetLifecycleState.Interactive, deadline.Token);
                var result = await session.CommitIntentAsync(preparation, target, deadline.Token);
                Check(result.Accepted, "Settings navigation incorrectly failed at the two-second worker deadline.");
                do
                {
                    target = await session.EstablishPresentationAsync(session.GetTarget("settings"), WidgetLifecycleState.Interactive, deadline.Token);
                    if (Walk(target.Snapshot.Root).FirstOrDefault(node => node.Id == "intent.destination.error") is { } error)
                        throw new InvalidOperationException("Fixture destination failed: " + error.Text);
                    if (Walk(target.Snapshot.Root).Any(node => node.Text?.Contains("Synthetic navigation target", StringComparison.Ordinal) == true)) break;
                    await Task.Delay(25, deadline.Token);
                } while (true);
                Check(target.Snapshot.ActiveInputScopeId != "settings-root", "Settings home remained the destination.");
                Check(session.GetTarget("managed").Descriptor.IsStopped, "Managing a widget must not start it.");
                // Let optional slow inspections finish before exercising the warm path.
                await Task.Delay(3300, deadline.Token);
            }
            await session.DisposeAsync();
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    internal static BridgeCatalog Catalog(string root) => new([
        new ConfiguredWidget { Id = "managed", Name = "Synthetic navigation target", PackageId = Package,
            PublisherId = "fixture.publisher", InstanceId = "managed.fixture", IsStopped = true,
            PackageRoot = Path.Combine(root, "widgets", "packages", Package, "1.0.0"),
            WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new string('c',64), CatalogFingerprint = new string('c',64) },
        new ConfiguredWidget { Id = "source", Name = "Marketplace navigation fixture", PackageId = "fixture.source", PublisherId = "fixture", InstanceId = "settings-source.fixture",
            WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new string('a',64), CatalogFingerprint = new string('a',64),
            Intents = new() { Requests = [WidgetIntentContracts.WidgetSettings] } },
        new ConfiguredWidget { Id = "settings", Name = "Settings", PackageId = "widgetrail.firstparty.settings", PublisherId = "widgetrail.firstparty", InstanceId = "settings-navigation.fixture",
            WorkerExecutable = Environment.ProcessPath!, WorkerArguments = ["--settings-fixture-root", root], WorkerFingerprint = new string('b',64), CatalogFingerprint = new string('b',64),
            StylePackage = SettingsStyle(),
            // This navigation fixture uses the icons' semantic fallbacks. The
            // declarations still match Settings' real package admission contract.
            DeclaredPackageIconAssetIds = SettingsIconAssetIds(),
            Intents = new() { Requests = [WidgetIntentContracts.Marketplace, WidgetIntentContracts.AppUpdates],
                Handles = [new(WidgetIntentContracts.WidgetSettings), new(WidgetIntentContracts.WidgetPermissions), new(WidgetIntentContracts.AppUpdates)] } }
    ]);
    internal static void Seed(string root)
    {
        var catalog = Path.Combine(root, "widgets");
        var package = Path.Combine(catalog, "packages", Package, "1.0.0");
        Directory.CreateDirectory(Path.Combine(package, "payload"));
        var manifest = new WidgetManifest { Id = Package, Publisher = "fixture.publisher", Name = "Synthetic navigation target", Version = "1.0.0", HostApi = new("1.0",1),
            Entrypoint = new(WidgetEntrypointRuntimes.DotNetWorker, "payload/Widget.dll", "Fixture.Widget"), BackgroundPolicy = "suspend", ResourceRequest = new(32,1), Architectures = ["x64"] };
        var errors = WidgetManifestValidator.Validate(manifest);
        Check(errors.Count == 0, string.Join("; ", errors));
        File.WriteAllBytes(Path.Combine(package, "manifest.json"), ManifestJson.Serialize(manifest));
        File.WriteAllBytes(Path.Combine(package, "payload", "Widget.dll"), [0x47,0x42,0x41]);
        InstalledPackageIntegrity.Seal(catalog, package, new WidgetCatalogOptions());
        for (var index = 0; index < 11; index++)
        {
            var id = "fixture.extra" + index;
            var directory = Path.Combine(catalog, "packages", id, "1.0.0");
            Directory.CreateDirectory(Path.Combine(directory, "payload"));
            File.WriteAllBytes(Path.Combine(directory, "manifest.json"), ManifestJson.Serialize(manifest with { Id = id, Name = "Synthetic widget " + index }));
            File.WriteAllBytes(Path.Combine(directory, "payload", "Widget.dll"), [0x47,0x42,0x41]);
            InstalledPackageIntegrity.Seal(catalog, directory, new WidgetCatalogOptions());
        }
        var bundled = Path.Combine(root, "bundled", "Audio");
        Directory.CreateDirectory(bundled);
        File.WriteAllBytes(Path.Combine(bundled, "manifest.json"), ManifestJson.Serialize(manifest with
            { Id = "widgetrail.firstparty.audio-mixer", Publisher = "widgetrail.firstparty", Name = "Synthetic built-in Audio" }));
    }
    internal static WrssPackageResult SettingsStyle()
    {
        var parsed = WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixture", "settings-default.wrss")), "settings-default.wrss");
        Check(parsed.IsValid, "Settings fixture theme could not parse");
        var compiled = WrssThemeCompiler.Compile([parsed.Document]);
        Check(compiled.IsValid, "Settings fixture theme could not compile");
        return new([parsed.Document], []);
    }
    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class SlowDiagnostics : IPlatformDiagnosticsService
    {
        public async ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        { await Task.Delay(1500,cancellationToken); return PlatformDiagnosticsSnapshot.Unavailable(); }
        public async ValueTask<PlatformWidgetLocalDataInspection> InspectWidgetLocalDataAsync(string id, CancellationToken cancellationToken = default)
        { await Task.Delay(3000,cancellationToken); return new(id,"Synthetic navigation target",false,"no_local_data",null); }
    }
}

internal sealed class SettingsNavigationSource : Widget
{
    public override WidgetView Render() => new(UI.Stack("root", UI.Button("Manage in Settings", "manage", "manage")
        .OpenIntent(WidgetIntentContracts.WidgetSettings, JsonSerializer.SerializeToElement(new { packageId = SettingsNavigationFixture.Package }), WidgetIntentPresentation.OpenWidget),
        UI.Button("Manage missing widget", "missing", "missing")
        .OpenIntent(WidgetIntentContracts.WidgetSettings, JsonSerializer.SerializeToElement(new { packageId = "fixture.missing" }), WidgetIntentPresentation.OpenWidget))
        .InputScope("root"), "manage", ActiveInputScopeId: "root");
}
