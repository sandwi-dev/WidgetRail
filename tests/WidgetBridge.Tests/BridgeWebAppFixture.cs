using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class BridgeWebAppFixture
{
    internal static async Task ServeAsync(string pipe, string settingsRoot)
    {
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
        }).ToArray());
        await using var server = new WidgetBridgeServer(pipe, catalog, appearance: appearance);
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }
}

internal sealed class WebAppFrameworkFixture : Widget
{
    private readonly bool idle;
    private readonly WidgetModel<bool> second;
    internal WebAppFrameworkFixture(bool idle) { this.idle = idle; second = CreateModel(false); }
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
                UI.Button("Toggle second session", "second", "second").InGrid(row: 1, columnSpan: 2)]), "app.page",
            Surface: new() { Mode = WidgetSurfaceMode.Standard, WidthMode = WidgetSurfaceAxisMode.Preferred,
                HeightMode = WidgetSurfaceAxisMode.Preferred, PreferredWidth = 1100, PreferredHeight = 760, MinimumWidth = 480, MinimumHeight = 360 });
    }
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    { if (action.ActionId == "second") second.Update(value => !value); return ValueTask.CompletedTask; }
}
