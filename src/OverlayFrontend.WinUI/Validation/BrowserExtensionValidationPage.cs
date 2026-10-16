using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Native functional probe. Never uses the user's browser profiles.</summary>
internal sealed partial class BrowserExtensionValidationPage : Page, IAsyncDisposable
{
    private readonly StackPanel panel = new();
    private readonly TextBlock status = new() { Text = "Checking browser extensions…" };
    private readonly List<WebView2> views = [];
    private readonly List<string> checks = [];
    private Task? running;
    private readonly string root = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--extension-restart-root") ??
        Path.Combine(Path.GetTempPath(), "WidgetRail-validation", "extensions-" + Guid.NewGuid().ToString("N"));
    private const string Url = "https://extension-fixture.example/index.html";
    internal BrowserExtensionValidationPage()
    { Content = panel; panel.Children.Add(status); Loaded += (_, _) => running ??= RunAsync(); }
    private async Task RunAsync()
    {
        string? runtime = null;
        try
        {
            Directory.CreateDirectory(root);
            var shared = new Web.HostedWebViewEnvironment(Path.Combine(root, "udf"), new() { AreBrowserExtensionsEnabled = true });
            var env = await shared.GetAsync(); runtime = env.BrowserVersionString;
            var manager = new Browser.Extensions.BrowserExtensionManager(shared, root);
            if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--extension-restart-root") is not null)
            {
                var reopened = await CreateAsync(env, "persistent-managed", false);
                await manager.RegisterAsync(new("persistent-managed", "fixture", "Fixture", false), reopened.Profile, default);
                var installed = (await manager.Store.ReadAsync(default)).Library.Single();
                Check((await reopened.Profile.GetBrowserExtensionsAsync()).Any(e => e.Id == installed.Id && e.IsEnabled), "Managed installation survives process restart");
                await NavigateAsync(reopened, Url); await Task.Delay(200);
                Check(await reopened.ExecuteScriptAsync("!window.resourceLoaded && !document.querySelector('#ad')") == "true", "Filtering survives process restart");
                await NavigateAsync(reopened, "chrome-extension://" + installed.Id + "/options.html");
                Check(await EvaluateAsync(reopened, "(await chrome.storage.local.get('setting')).setting === 'kept'") == "true", "Extension settings survive process restart");
                await NavigateAsync(reopened, Url);
                await manager.RemoveAsync(installed.Slot, default);
                Check(!Directory.Exists(manager.Store.Files(installed)), "Managed removal cleans copied package files");
                var other = await CreateAsync(env, "persistent-other", false);
                Check(!(await other.Profile.GetBrowserExtensionsAsync()).Any(e => e.Id == installed.Id), "Global removal detaches other profiles too");
                Write(true, runtime, null); return;
            }
            var folder = Path.Combine(root, "fixture"); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), """
                {"manifest_version":3,"name":"WidgetRail functional fixture","version":"1.0.0",
                "permissions":["declarativeNetRequest","storage"],"host_permissions":["https://extension-fixture.example/*"],
                "content_scripts":[{"matches":["https://extension-fixture.example/*"],"js":["content.js"]}],
                "declarative_net_request":{"rule_resources":[{"id":"fixture","enabled":true,"path":"rules.json"}]},
                "options_ui":{"page":"options.html","open_in_tab":true}}
                """);
            await File.WriteAllTextAsync(Path.Combine(folder, "content.js"), "document.querySelector('#ad').remove();document.documentElement.dataset.extension='active';");
            await File.WriteAllTextAsync(Path.Combine(folder, "rules.json"), """[{"id":1,"priority":1,"action":{"type":"block"},"condition":{"urlFilter":"/blocked.js","resourceTypes":["script"]}}]""");
            await File.WriteAllTextAsync(Path.Combine(folder, "options.html"), "<html><body>Extension options<script src='options.js'></script></body></html>");
            await File.WriteAllTextAsync(Path.Combine(folder, "options.js"), "chrome.storage.local.get('count').then(x=>chrome.storage.local.set({count:(x.count||0)+1}));");
            var first = await CreateAsync(env, "first", false);
            await NavigateAsync(first, Url);
            Check(await first.ExecuteScriptAsync("window.resourceLoaded===true && !!document.querySelector('#ad')") == "true", "Baseline resource and cosmetic element load");
            var extension = await first.Profile.AddBrowserExtensionAsync(folder);
            Check(extension.IsEnabled, "MV3 installation enables extension");
            await NavigateAsync(first, Url);
            await Task.Delay(500);
            Check(await first.ExecuteScriptAsync("!window.resourceLoaded && !document.querySelector('#ad') && document.documentElement.dataset.extension==='active'") == "true", "DNR blocking and content scripts execute");
            await NavigateAsync(first, "chrome-extension://" + extension.Id + "/options.html");
            await Task.Delay(200);
            Check(await first.ExecuteScriptAsync("typeof chrome.storage.local.get==='function'") == "true", "Installed extension options page has storage API");
            await NavigateAsync(first, Url);
            var second = await CreateAsync(env, "second", false);
            checks.Add("Second profile extensions: " + string.Join(", ", (await second.Profile.GetBrowserExtensionsAsync()).Select(e => e.Id + ":" + e.Name)));
            Check(!(await second.Profile.GetBrowserExtensionsAsync()).Any(e => e.Id == extension.Id), "Separate profile has no fixture extension");
            await NavigateAsync(second, Url);
            Check(await second.ExecuteScriptAsync("window.resourceLoaded===true") == "true", "Other profile does not filter requests");
            var same = await CreateAsync(env, "first", false);
            Check((await same.Profile.GetBrowserExtensionsAsync()).Any(e => e.Id == extension.Id), "Same-profile controllers share extensions");
            await extension.EnableAsync(false);
            await NavigateAsync(first, Url);
            Check(await first.ExecuteScriptAsync("window.resourceLoaded===true") == "true", "Disabling restores resource loads");
            await extension.EnableAsync(true);
            await NavigateAsync(first, Url);
            Check(await first.ExecuteScriptAsync("!window.resourceLoaded") == "true", "Re-enabling restores blocking");
            var privateCore = await CreateAsync(env, "private", true);
            try
            {
                var privateExtension = await privateCore.Profile.AddBrowserExtensionAsync(folder);
                await NavigateAsync(privateCore, Url); await Task.Delay(200);
                checks.Add("Private profile installed=" + privateExtension.IsEnabled + "; filtered=" + await privateCore.ExecuteScriptAsync("!window.resourceLoaded"));
            }
            catch (Exception error) { checks.Add("Private extension API: " + error.GetType().Name + " " + error.HResult.ToString("X8")); }
            await extension.RemoveAsync();
            Check(!(await same.Profile.GetBrowserExtensionsAsync()).Any(e => e.Id == extension.Id), "Removal affects all controllers in profile");
            var managed = await CreateAsync(env, "persistent-managed", false);
            await manager.Store.RegisterAsync(new("persistent-managed", "fixture", "Fixture", false), default);
            await using (var import = await manager.PrepareAsync(folder, default)) await manager.InstallAsync(import, null, false, default);
            var record = (await manager.Store.ReadAsync(default)).Library.Single();
            Check(!record.EnabledByDefault && record.Overrides.Count == 0 && !(await managed.Profile.GetBrowserExtensionsAsync()).Any(e => e.Id == record.Id), "New import is disabled in existing profiles");
            await manager.SetPolicyAsync(record.Slot, null, "persistent-managed", ExtensionOverride.On, default);
            var otherManaged = await CreateAsync(env, "persistent-other", false);
            await manager.RegisterAsync(new("persistent-other", "other", "Other", false), otherManaged.Profile, default);
            Check(!(await otherManaged.Profile.GetBrowserExtensionsAsync()).Any(e => e.Id == record.Id), "Default-off installation leaves other profiles untouched");
            await manager.SetPolicyAsync(record.Slot, null, "persistent-other", ExtensionOverride.On, default);
            Check((await otherManaged.Profile.GetBrowserExtensionsAsync()).Any(e => e.Id == record.Id && e.IsEnabled), "Existing library entry enables in another profile without another import");
            await NavigateAsync(otherManaged, "chrome-extension://" + record.Id + "/options.html");
            Check(await EvaluateAsync(otherManaged, "await chrome.storage.local.set({setting:'other'}); true") == "true", "Other profile stores separate extension settings");
            await manager.SetPolicyAsync(record.Slot, true, "persistent-other", ExtensionOverride.Off, default);
            var future = await CreateAsync(env, "persistent-future", false);
            await manager.RegisterAsync(new("persistent-future", "future", "Future", false), future.Profile, default);
            Check((await future.Profile.GetBrowserExtensionsAsync()).Any(e => e.Id == record.Id && e.IsEnabled), "New profile inherits enabled default");
            Check(!(await otherManaged.Profile.GetBrowserExtensionsAsync()).Single(e => e.Id == record.Id).IsEnabled, "Explicit Off survives a default change");
            await manager.SetPolicyAsync(record.Slot, false, null, ExtensionOverride.Default, default);
            Check(!(await future.Profile.GetBrowserExtensionsAsync()).Single(e => e.Id == record.Id).IsEnabled &&
                (await managed.Profile.GetBrowserExtensionsAsync()).Single(e => e.Id == record.Id).IsEnabled, "Default Off disables inherited profile and retains explicit On");
            await manager.SetPolicyAsync(record.Slot, true, "persistent-future", ExtensionOverride.Default, default);
            await NavigateAsync(managed, "chrome-extension://" + record.Id + "/options.html");
            Check(await EvaluateAsync(managed, "await chrome.storage.local.set({setting:'kept'}); true") == "true", "Managed extension options save settings");
            await NavigateAsync(managed, Url);
            await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), (await File.ReadAllTextAsync(Path.Combine(folder, "manifest.json"))).Replace("1.0.0", "1.1.0"));
            await using (var update = await manager.PrepareAsync(folder, default)) await manager.InstallAsync(update, record.Slot, false, default);
            var updated = (await manager.Store.ReadAsync(default)).Library.Single();
            Check(updated.Id == record.Id && updated.Manifest.Version == "1.1.0", "Managed update preserves extension identity");
            await NavigateAsync(managed, "chrome-extension://" + record.Id + "/options.html");
            Check(await EvaluateAsync(managed, "(await chrome.storage.local.get('setting')).setting === 'kept'") == "true", "Managed update preserves extension settings");
            await manager.SetPolicyAsync(record.Slot, null, "persistent-other", ExtensionOverride.On, default);
            await NavigateAsync(otherManaged, "chrome-extension://" + record.Id + "/options.html");
            Check(await EvaluateAsync(otherManaged, "(await chrome.storage.local.get('setting')).setting === 'other' && chrome.runtime.getManifest().version === '1.1.0'") == "true", "Shared update reaches disabled attachments and preserves isolated settings");
            await manager.SetPolicyAsync(record.Slot, null, "persistent-other", ExtensionOverride.Off, default);
            await NavigateAsync(managed, Url);
            var validManifest = await File.ReadAllTextAsync(Path.Combine(folder, "manifest.json"));
            await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), validManifest.Replace("1.1.0", "invalid"));
            var rejected = false;
            try { await using var bad = await manager.PrepareAsync(folder, default); await manager.InstallAsync(bad, updated.Slot, false, default); }
            catch (System.Runtime.InteropServices.COMException) { rejected = true; }
            Check(rejected && (await manager.Store.ReadAsync(default)).Library.Single().Manifest.Version == "1.1.0", "Rejected native update rolls back package and catalog");
            Check(!(await otherManaged.Profile.GetBrowserExtensionsAsync()).Single(e => e.Id == record.Id).IsEnabled &&
                (await future.Profile.GetBrowserExtensionsAsync()).Single(e => e.Id == record.Id).IsEnabled, "Rollback preserves each profile's effective state");
            await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), validManifest.Replace("1.1.0", "1.2.0"));
            var slotRoot = manager.Store.SlotRoot(updated.Slot);
            File.WriteAllText(Path.Combine(slotRoot, "pending.json"), JsonSerializer.Serialize(new ExtensionTransaction(updated, updated with { Revision = "interrupted" }, ["persistent-managed"]), ExtensionJsonContext.Default.ExtensionTransaction));
            Directory.Move(manager.Store.Files(updated), Path.Combine(slotRoot, "previous"));
            Browser.Extensions.ExtensionPackage.Copy(folder, manager.Store.Files(updated), default);
            _ = await managed.Profile.AddBrowserExtensionAsync(manager.Store.Files(updated));
            manager = new Browser.Extensions.BrowserExtensionManager(shared, root);
            await manager.RegisterAsync(new("persistent-managed", "fixture", "Fixture", false), managed.Profile, default);
            var recovered = (await managed.Profile.GetBrowserExtensionsAsync()).Single(e => e.Id == updated.Id);
            Check(recovered.IsEnabled && !File.Exists(Path.Combine(slotRoot, "pending.json")) &&
                Browser.Extensions.ExtensionPackage.ReadManifest(manager.Store.Files(updated)).Version == "1.1.0", "Interrupted update recovery restores files, identity and enabled preference");
            await recovered.EnableAsync(true);
            var stockFolder = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--extension-folder");
            if (stockFolder is not null)
            {
                var stock = await first.Profile.AddBrowserExtensionAsync(stockFolder);
                Check(stock.IsEnabled, "Stock extension installed: " + stock.Name + " (" + stock.Id + ")");
                // A known advertising URL is served locally if the extension does not block it.
                await NavigateAsync(first, "https://extension-fixture.example/stock.html"); await Task.Delay(1000);
                Check(await first.ExecuteScriptAsync("!window.resourceLoaded") == "true", "Stock extension blocks controlled doubleclick advertising script");
                var manifest = Browser.Extensions.ExtensionPackage.ReadManifest(stockFolder);
                if (manifest.OptionsPage is { } optionsPage)
                {
                    await NavigateAsync(first, "chrome-extension://" + stock.Id + "/" + optionsPage);
                    await Task.Delay(500);
                    Check(await first.ExecuteScriptAsync("document.body.innerText.length > 50") == "true", "Stock extension options render");
                    checks.Add("Stock extension active-tab API: " + await EvaluateAsync(first, "JSON.stringify(await chrome.tabs.query({active:true,currentWindow:true}))"));
                }
            }
            await VerifyOptionsSurfaceAsync(shared, folder);
            Write(true, runtime, null);
        }
        catch (Exception error) { Write(false, runtime, error.ToString()); }
    }
    private async Task VerifyOptionsSurfaceAsync(Web.HostedWebViewEnvironment shared, string folder)
    {
        var surface = new Browser.BrowserSurface("fixture.options", new("options", 1, Url, "Options fixture")
        { Options = WidgetProtocol.WebBrowserOptions.WebApp, InteractionMode = WidgetProtocol.BrowserInteractionMode.InteractOnFocus }, shared.GetAsync)
        { Height = 400, IsInteractionFocused = () => true };
        panel.Children.Clear(); panel.Children.Add(status); panel.Children.Add(surface);
        try
        {
            var core = await surface.PrepareCoreAsync(default);
            AttachFixtureResources(core, await shared.GetAsync());
            var extension = await core.Profile.AddBrowserExtensionAsync(folder);
            var manifest = Browser.Extensions.ExtensionPackage.ReadManifest(folder);
            surface.SetPresentation(true, true);
            await WaitAsync(() => surface.CurrentUrl == Url && !surface.IsNavigationInProgress);
            await surface.EvaluateFixtureAsync("window.originalMarker = 'retained';");
            await surface.OpenExtensionOptionsAsync(new("fixture", extension.Id, manifest));
            await WaitAsync(() => surface.PinnablePopup is { CurrentUrl: { } url, IsNavigationInProgress: false } && url.StartsWith("chrome-extension://", StringComparison.Ordinal));
            Check(surface.CurrentUrl == Url && await surface.EvaluateFixtureAsync("window.originalMarker === 'retained'") == "true", "Opening options retains original website document");
            var child = surface.PinnablePopup!;
            Check(await child.EvaluateFixtureAsync("typeof chrome.storage.local.get === 'function'") == "true", "Hosted options retains extension API context");
            surface.HandleButton(WidgetProtocol.ControllerButton.B, WidgetProtocol.ControllerEventPhase.Pressed);
            await WaitAsync(() => !surface.HasPopup);
            surface.HandleButton(WidgetProtocol.ControllerButton.B, WidgetProtocol.ControllerEventPhase.Released);
            Check(await surface.EvaluateFixtureAsync("window.originalMarker === 'retained'") == "true", "Controller Back closes options and retains website");
            surface.NavigateForValidation("chrome-extension://" + extension.Id + "/options.html");
            await Task.Delay(200);
            Check(surface.CurrentUrl == Url, "Ordinary page cannot navigate to extension resources without host authorization");
        }
        finally { await surface.DisposeAsync(); panel.Children.Remove(surface); }
        static async Task WaitAsync(Func<bool> condition)
        { var until = DateTime.UtcNow.AddSeconds(15); while (!condition()) { if (DateTime.UtcNow > until) throw new TimeoutException("Options surface did not settle."); await Task.Delay(50); } }
    }
    private static async Task<string> EvaluateAsync(CoreWebView2 core, string expression)
    {
        var script = expression.Contains(';') ? "(async()=>{" + expression.Replace("; true", "; return true") + "})()" : "(async()=> (" + expression + "))()";
        using var result = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = script, awaitPromise = true, returnByValue = true })));
        if (result.RootElement.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException(error.ToString());
        return result.RootElement.GetProperty("result").GetProperty("value").GetRawText();
    }
    private async Task<CoreWebView2> CreateAsync(CoreWebView2Environment env, string profile, bool privateMode)
    {
        var view = new WebView2 { Height = 180 }; views.Add(view);
        var options = env.CreateCoreWebView2ControllerOptions(); options.ProfileName = profile; options.IsInPrivateModeEnabled = privateMode;
        await view.EnsureCoreWebView2Async(env, options);
        panel.Children.Add(view);
        var core = view.CoreWebView2;
        checks.Add("Requested profile=" + profile + "; actual=" + core.Profile.ProfileName + "; private=" + core.Profile.IsInPrivateModeEnabled);
        AttachFixtureResources(core, env);
        return core;
    }
    private static void AttachFixtureResources(CoreWebView2 core, CoreWebView2Environment env)
    {
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            var uri = new Uri(args.Request.Uri);
            if (uri.Host is not ("extension-fixture.example" or "doubleclick.net")) return;
            var html = uri.AbsolutePath.EndsWith(".html", StringComparison.Ordinal);
            var body = html ? "<html><body><div id='ad'>Ad fixture</div><script src='" + (uri.AbsolutePath == "/stock.html" ? "https://doubleclick.net/advertisement.js" : "/blocked.js") + "'></script></body></html>" : "window.resourceLoaded=true;";
            args.Response = env.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)).AsRandomAccessStream(), 200, "OK", "Content-Type: " + (html ? "text/html" : "application/javascript"));
        };
    }
    private static async Task NavigateAsync(CoreWebView2 core, string url)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) => completed.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += OnCompleted;
        try { core.Navigate(url); if (!await completed.Task.WaitAsync(TimeSpan.FromSeconds(20))) throw new InvalidOperationException("Navigation failed: " + url); }
        finally { core.NavigationCompleted -= OnCompleted; }
    }
    private void Check(bool passed, string description) { if (!passed) throw new InvalidOperationException(description); checks.Add(description); }
    private void Write(bool passed, string? runtime, string? error)
    {
        status.Text = passed ? "Extension checks passed" : "Extension checks failed";
        var path = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--extension-result")!;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { passed, runtime, root, checks, error }));
    }
    public async ValueTask DisposeAsync() { if (running is not null) await running; foreach (var view in views) view.Close(); }
}
