using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.WindowsCommunityProvider;

internal static class ReplayUiFixture
{
    internal static async Task ServeAsync(string pipe, string settingsRoot)
    {
        var paths = new PlatformSettingsPaths(Path.GetFullPath(settingsRoot));
        var settings = new PlatformSettingsStore(paths);
        await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(settings, new ThemeCatalog(paths)));
        await appearance.StartAsync();
        string[] permissions = [PlatformCapabilities.WindowCaptureV1, PlatformCapabilities.CaptureLibraryReadV1,
            PlatformCapabilities.CaptureLibrarySaveV1, PlatformCapabilities.CaptureLibraryManageV1];
        var style = WrssPackageLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "fixture", "capture-replay.wrss"));
        var widgets = new[] { Widget("capture-replay-fixture", "capture-replay.instance", "Capture Library"), Widget("replay-approval", "replay-approval.instance", "Replay approval fixture") };
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
            catalogMonitor: null, residencyBudget: null, capabilityDiagnosticSink: null,
            workerDiagnosticRoot: Path.Combine(settingsRoot, "worker-diagnostics"));
        authority = server;
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }

    internal sealed class ApprovalProbe : Widget
    {
        private string result = "Ready";
        public override WidgetView Render() => new(UI.Stack("approval.root",
            UI.Button("Enable replay", "approval.enable", "approval.enable"),
            UI.Button("Disable replay", "approval.disable", "approval.disable"),
            UI.Text(result, "approval.result")), ActiveInputScopeId: "approval.root");
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
