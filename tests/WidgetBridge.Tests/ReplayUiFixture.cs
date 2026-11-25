using System.Security.Cryptography;
using WidgetRail.WidgetCatalog;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.WindowsCommunityProvider;

internal static class ReplayUiFixture
{
    internal static async Task SeedBatchGalleryAsync(string directory, string png)
    {
        var root = Path.GetFullPath(directory);
        if (Directory.Exists(root)) throw new IOException("Batch tests require a fresh profile.");
        Directory.CreateDirectory(root);
        using var library = new CaptureLibraryStore(Path.Combine(root, "library"), Path.Combine(root, "captures"),
            WindowsCaptureImageCodec.CreateContextAsync, WindowsCaptureImageCodec.CreateThumbnailAsync, WindowsCaptureImageCodec.InspectAsync);
        var image = new FileInfo(png);
        var linked = Directory.CreateDirectory(Path.Combine(root, "linked"));
        File.Copy(image.FullName, Path.Combine(linked.FullName, "Linked.png"));
        BrokerWidgetIdentity owner = new("example.capture-replay-fixture", "example.fixture", "capture-replay.instance");
        var source = await library.AddSourceAsync(linked.FullName, "Linked fixture", owner, default);
        await library.LinkAsync(new(source.Id, "Linked.png"), owner, default);
        for (var index = 0; index < 90; index++)
        {
            var attachment = new CaptureAttachment(Guid.NewGuid().ToString("N"), "image/png", 160, 90, image.Length,
                0, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()) { SourceApplication = new("Fixture", $"Scene {index}") };
            await library.SaveAsync(new HostCaptureAttachment(attachment, image.FullName), default);
        }
    }

    internal static async Task ServeAsync(string pipe, string settingsRoot)
    {
        var paths = new PlatformSettingsPaths(Path.GetFullPath(settingsRoot));
        var settings = new PlatformSettingsStore(paths);
        await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(settings, new ThemeCatalog(paths)));
        await appearance.StartAsync();
        string[] permissions = [PlatformCapabilities.WindowCaptureV1, PlatformCapabilities.CaptureLibraryReadV1,
            PlatformCapabilities.CaptureLibrarySaveV1, PlatformCapabilities.CaptureLibraryManageV1];
        var style = WrssPackageLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "fixture", "capture-replay.wrss"));
        var root = Path.Combine(AppContext.BaseDirectory, "fixture", "capture");
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json"));
        var manifest = ManifestJson.Deserialize(manifestBytes);
        var files = manifest.IconAssets.Values.ToDictionary(asset => asset.Path, asset =>
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, asset.Path));
            return new VerifiedPackageFile(asset.Path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }, StringComparer.Ordinal);
        var capture = Widget("capture-replay-fixture", "capture-replay.instance", "Capture Library") with
        {
            PackageRoot = root, VerifiedPackageFiles = files,
            PackageContentDigest = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
            PackageIconAssets = PackageIconAssetResolver.LoadMetadata(root, manifest, files),
            DeclaredPackageIconAssetIds = manifest.IconAssets.Keys.ToHashSet(StringComparer.Ordinal),
            Intents = manifest.Intents, TrayActions = manifest.TrayActions
        };
        var widgets = new[] { capture, Widget("replay-approval", "replay-approval.instance", "Replay approval fixture") with
            { TrayActions = [new("reject", "Fixture action"), new("mark", "Mark fixture") { WakeOnInvoke = true }] } };
        ConfiguredWidget Widget(string id, string instance, string name) => new()
        {
            Id = id, InstanceId = instance, PackageId = "example." + id, PublisherId = "example.fixture", Name = name,
            WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64),
            DeclaredCapabilities = permissions, StylePackage = style,
        };
        var consent = new ConsentStore(Path.Combine(settingsRoot, "consent"));
        foreach (var widget in widgets)
            await consent.SetDecisionsAsync(new(widget.PackageId, widget.PublisherId, widget.InstanceId), permissions, ConsentDecision.Grant);
        using var library = new CaptureLibraryStore(Path.Combine(settingsRoot, "library"), Path.Combine(settingsRoot, "captures"),
            WindowsCaptureImageCodec.CreateContextAsync, WindowsCaptureImageCodec.CreateThumbnailAsync, WindowsCaptureImageCodec.InspectAsync);
        WidgetBridgeServer? authority = null;
        await using var replay = new CaptureReplayService(library, CaptureReplayPreferencesAdapter.Read((await settings.LoadAsync()).ReplayCapture),
            (next, expected, token) => CaptureReplayPreferencesAdapter.SaveAsync(settings, next, expected, token),
            (actor, capability, token) => authority!.AuthorizeReplayFeatureAsync(actor, capability, token));
        var simulator = new SimulatedPlatformBrokerBackend();
        var privateState = new WindowsCommunityPlatformBackend(Path.Combine(settingsRoot, "widget-state"));
        await using var backend = new CompositePlatformBrokerBackend(simulator, simulator, appLibrary: simulator,
            privateState: privateState, captureLibrary: library) { CaptureReplay = replay };
        await using var server = new WidgetBridgeServer(pipe, new(widgets),
            maximumMessageBytes: BridgeProtocol.DefaultMaximumMessageBytes,
            appearance: appearance, consentStore: consent, platformBackend: backend,
            catalogMonitor: null, residencyBudget: null, capabilityDiagnosticSink: (widget, entry) =>
            {
                if (entry.Code is not null)
                    File.AppendAllText(Path.Combine(settingsRoot, "fixture-capability-errors.log"),
                        $"{widget} {entry.CapabilityId} {entry.OperationId} {entry.Stage} {entry.Code}{Environment.NewLine}");
            },
            workerDiagnosticRoot: Path.Combine(settingsRoot, "worker-diagnostics"));
        authority = server;
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }

    internal sealed class ApprovalProbe : Widget
    {
        private string result = "Ready";
        private int marks;
        public override WidgetView Render() => new(UI.Stack("approval.root",
            UI.Button("Enable replay", "approval.enable", "approval.enable"),
            UI.Button("Disable replay", "approval.disable", "approval.disable"),
            UI.Text(result, "approval.result")), ActiveInputScopeId: "approval.root",
            QuickActions: [WidgetQuickAction.ForTray("Fixture action", new("reject", "fixture")), WidgetQuickAction.ForTray("Mark fixture", new("mark", "fixture"))]);
        protected override async ValueTask<TrayActionResult> OnTrayActionAsync(TrayActionOffer offer, CancellationToken token)
        {
            if (offer.Id != "mark") return TrayActionResult.Unavailable("The fixture player disconnected.");
            await Task.Delay(350, token);
            result = "Marked " + ++marks;
            Invalidate();
            return TrayActionResult.Success;
        }
        public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken token = default)
        {
            if (action.ActionId is not ("approval.enable" or "approval.disable")) return ValueTask.CompletedTask;
            Operations.RunLatest("approval", async context =>
            {
                var current = await HostServices.Capture.Replay.GetAsync(context.CancellationToken);
                var changed = await HostServices.Capture.Replay.ConfigureAsync(current.Settings with
                    { Enabled = action.ActionId == "approval.enable", AutoStart = false, ReplayDurationSeconds = 15, MemoryBudgetBytes = 32L * 1024 * 1024 }, current.SettingsRevision, context.CancellationToken);
                if (context.IsCurrent) { result = changed.Outcome.ToString(); Invalidate(); }
            });
            return ValueTask.CompletedTask;
        }
    }
}
