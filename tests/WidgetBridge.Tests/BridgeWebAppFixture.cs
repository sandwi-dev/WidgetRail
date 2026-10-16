using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.PlatformBroker;

internal static class BridgeWebAppFixture
{
    internal static async Task ServeAsync(string pipe, string settingsRoot)
    {
        var extensions = File.Exists(Path.Combine(AppContext.BaseDirectory, "extensions-settings-fixture.marker"));
        var paths = new PlatformSettingsPaths(Path.GetFullPath(settingsRoot));
        var store = new PlatformSettingsStore(paths);
        await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
        await appearance.StartAsync();
        var catalog = new BridgeCatalog(new[] { "webapp-a", "webapp-b", "webapp-c", "webapp-d", "webapp-e", "webapp-idle" }.Select(id => new ConfiguredWidget
        {
            Id = id, PackageId = "fixture." + id, PublisherId = "fixture.webapp", Name = id,
            InstanceId = id + ".instance", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new string(id[^1], 64), CatalogFingerprint = new string(id[^1], 64),
            PinningSupported = id != "webapp-idle", FullWidgetPinningSupported = id != "webapp-idle",
            ResidencyPolicy = new() { Mode = WidgetResidencyPolicies.UnloadAfterIdle, IdleSeconds = 5 },
            Intents = id == "webapp-a" ? new() { Requests = [WidgetIntentContracts.Web], Handles = [new(WidgetIntentContracts.Web)] } : null,
        }).Append(new ConfiguredWidget
        {
            Id = "settings", PackageId = "fixture.settings", PublisherId = "fixture.webapp", Name = "Settings fixture",
            InstanceId = extensions ? "settings-navigation.fixture" : "settings.fixture", WorkerExecutable = Environment.ProcessPath!,
            WorkerArguments = extensions ? ["--settings-fixture-root", settingsRoot] : [],
            StylePackage = extensions ? SettingsNavigationFixture.SettingsStyle() : new([], []),
            DeclaredPackageIconAssetIds = extensions ? new HashSet<string>(["settings.appearance", "settings.accessibility", "settings.overlay", "settings.controllers", "settings.widgets", "settings.diagnostics"], StringComparer.Ordinal) : new HashSet<string>(),
            WorkerFingerprint = new string('f', 64), CatalogFingerprint = new string('f', 64),
            Intents = new() { Requests = [WidgetIntentContracts.AppUpdates], Handles = [new(WidgetIntentContracts.AppUpdates)] },
        }).ToArray());
        await using var server = new WidgetBridgeServer(pipe, catalog.WithStoppedWidgets(await new WidgetStopStateStore(paths).LoadAsync()), BridgeProtocol.DefaultMaximumMessageBytes, appearance,
            new ConsentStore(Path.Combine(settingsRoot, "fixture-consent")), new SimulatedPlatformBrokerBackend(), null, null, null, settingsStore: store,
            appUpdates: new(new("0.1.0-preview.31", "production", "", false), Path.Combine(settingsRoot, "updates"),
                new ShellUpdateApi(), profileRoot: settingsRoot));
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }
}

internal sealed class WebAppFrameworkFixture : Widget
{
    private readonly bool idle;
    private readonly WidgetModel<bool> second;
    private readonly WidgetModel<string> noticeStatus;
    internal WebAppFrameworkFixture(bool idle) { this.idle = idle; second = CreateModel(false); noticeStatus = CreateModel("idle"); }
    public override ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ContractId != WidgetIntentContracts.Web.Id || !request.IsWellFormed() || request.Payload.GetProperty("url").GetString() != "https://shell.fixture.invalid/secondary")
            return ValueTask.FromResult(WidgetIntentResult.Rejected);
        second.Update(_ => true);
        return ValueTask.FromResult(WidgetIntentResult.Accepted);
    }
    public override WidgetView Render()
    {
        if (idle) return new(UI.Button("Idle fixture", "noop", "idle"), "idle");
        var primary = UI.WebBrowser(new("app.primary", 1, "https://shell.fixture.invalid/primary", "Webapp primary")
        { Options = WebBrowserOptions.WebApp }, BrowserInteractionMode.InteractOnFocus, "app.page");
        var other = UI.WebBrowser(new("app.secondary", 1, "https://shell.fixture.invalid/secondary", "Webapp secondary")
        { Options = WebBrowserOptions.WebApp }, BrowserInteractionMode.InteractOnFocus, "app.other");
        return new(UI.Grid("app.root", [GridTrack.Star(), GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()],
            [primary.InGrid(columnSpan: second.Value ? 1 : 2),
                .. (second.Value ? new[] { other.InGrid(column: 1) } : []),
                UI.Row("app.commands", UI.Button("Toggle second session", "second", "second"),
                    UI.Button("Synthetic song banner: " + noticeStatus.Value, "notify", "notify"),
                    UI.Button("Synthetic pending notification", "notify-pending", "notify-pending"),
                    UI.Button("Update synthetic metadata", "notify-update", "notify-update"),
                    UI.Button("Withdraw notification", "notify-withdraw", "notify-withdraw")).InGrid(row: 1, columnSpan: 2)]), "app.page",
            Surface: new() { Mode = WidgetSurfaceMode.Standard, WidthMode = WidgetSurfaceAxisMode.Preferred,
                HeightMode = WidgetSurfaceAxisMode.Preferred, PreferredWidth = 1100, PreferredHeight = 760, MinimumWidth = 480, MinimumHeight = 360 });
    }
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (action.ActionId == "second") second.Update(value => !value);
        if (action.ActionId.StartsWith("notify", StringComparison.Ordinal))
            Operations.RunLatest("fixture.notification", async context =>
            {
                noticeStatus.Update(_ => "requested");
                await Task.Delay(400, context.CancellationToken);
                var content = new WidgetNotification("fixture-song", "Synthetic track")
                {
                    OpenIntent = action.ActionId == "notify-pending" ? WidgetIntentRequest.Create(WidgetIntentContracts.Web,
                        System.Text.Json.JsonSerializer.SerializeToElement(new { url = "https://shell.fixture.invalid/secondary" }), WidgetIntentPresentation.OpenWidget) : null,
                    Body = "Fixture artist", Metadata = action.ActionId == "notify-update" ? "3:46" : "3:45", ShowWidgetIcon = false,
                    ArtworkUrl = "https://fixture.invalid/thumbnail.png",
                    Retention = action.ActionId == "notify-pending" ? WidgetNotificationRetention.Pending : WidgetNotificationRetention.BannerOnly,
                    Interaction = action.ActionId == "notify-pending" ? WidgetNotificationInteraction.OpenWidget : WidgetNotificationInteraction.Informational,
                    Visibility = WidgetNotificationVisibility.Always, BackgroundOpacityPercent = 80,
                };
                if (action.ActionId == "notify-withdraw") await HostServices.Notifications.DismissAsync(content.Key, context.CancellationToken);
                else if (action.ActionId == "notify-update") await HostServices.Notifications.UpdateAsync(content, context.CancellationToken);
                else
                {
                    var receipt = await HostServices.Notifications.ShowAsync(content, context.CancellationToken);
                    noticeStatus.Update(_ => receipt.Accepted ? "accepted" : "rejected");
                }
            }, WidgetOperationLifetime.Widget);
        return ValueTask.CompletedTask;
    }
}
