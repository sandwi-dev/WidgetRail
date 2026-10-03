using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class BridgeBrowserFixture
{
    internal static async Task ServeAsync(string pipe, string settingsRoot)
    {
        var paths = new PlatformSettingsPaths(Path.GetFullPath(settingsRoot));
        var store = new PlatformSettingsStore(paths);
        await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
        await appearance.StartAsync();
        var catalog = new BridgeCatalog([Entry("browser", true), Entry("intent-source", false), Entry("browser-modes", true)]);
        await using var server = new WidgetBridgeServer(pipe, catalog, appearance: appearance);
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }
    private static ConfiguredWidget Entry(string id, bool browser) => new()
    {
        Id = id, PackageId = "example." + id, PublisherId = "example.publisher", Name = browser ? "Browser fixture" : "Intent source",
        InstanceId = id + ".instance", WorkerExecutable = Environment.ProcessPath!,
        WorkerFingerprint = new string(browser ? 'b' : 'a', 64), CatalogFingerprint = new string(browser ? 'b' : 'a', 64),
        PinningSupported = browser, FullWidgetPinningSupported = browser,
        Intents = id == "browser" ? new() { Handles = [new(WidgetIntentContracts.Web) { SupportsPassiveDelivery = true }] }
            : id == "intent-source" ? new() { Requests = [WidgetIntentContracts.Web] } : null,
    };
}

internal sealed class ActivationBrowserFixture : Widget
{
    public override WidgetView Render() => new(UI.Stack("mode.root",
        UI.WebBrowser(new("mode.document", 1, "https://example.com/explicit", "Explicit browser"),
            BrowserInteractionMode.ActivateToInteract, "browser.page"),
        UI.Button("Sibling control", "noop", "sibling")), "browser.page");
}
