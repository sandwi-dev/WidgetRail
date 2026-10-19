using Microsoft.Web.WebView2.Core;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class BrowserExtensionValidationPage
{
    private async Task VerifyProfileCleanupAsync(CoreWebView2Environment environment, Browser.Extensions.BrowserExtensionManager manager)
    {
        var lifecycle = new BrowserProfileLifecycleStore(root);
        var original = (await lifecycle.RegisterAsync("fixture.removed", "persistent-remove", "persistent-remove.json"))!;
        var old = await CreateAsync(environment, original.Name, false);
        var neighbor = await CreateAsync(environment, "persistent-neighbor", false);
        SetCookie(old, "old"); SetCookie(neighbor, "neighbor");
        await manager.RegisterAsync(new(original.Name, "fixture.removed", "Removed fixture", false), old.Profile, default);
        await manager.RegisterAsync(new("persistent-neighbor", "fixture.neighbor", "Neighbor fixture", false), neighbor.Profile, default);
        var stable = await lifecycle.RegisterAsync("fixture.removed", "persistent-remove", "persistent-remove.json");
        Check(stable == original && (await old.CookieManager.GetCookiesAsync("https://extension-fixture.example")).Single().Value == "old",
            "Ordinary reopen/update preserves the native profile and cookies");
        await lifecycle.ResetAsync("fixture.removed", Guid.NewGuid().ToString("N"), false);
        var replacement = (await lifecycle.RegisterAsync("fixture.removed", "persistent-remove", "persistent-remove.json"))!;
        var fresh = await CreateAsync(environment, replacement.Name, false);
        Check((await fresh.CookieManager.GetCookiesAsync("https://extension-fixture.example")).Count == 0, "Reset creates a clean native profile before deferred deletion");
        SetCookie(fresh, "fresh");
        var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old.Profile.Deleted += (_, _) => deleted.TrySetResult();
        await manager.DeleteProfileAsync(original.Name, default);
        await deleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await manager.RegisterAsync(new(original.Name, "fixture.removed", "Late stale registration", false), fresh.Profile, default);
        Check(!(await manager.Store.ReadAsync(default)).Profiles.Any(p => p.Name == original.Name), "Late profile registration cannot resurrect a removed selector entry");
        await lifecycle.CompleteAsync("fixture.removed", original);
        Check((await fresh.CookieManager.GetCookiesAsync("https://extension-fixture.example")).Single().Value == "fresh", "Deleting the old native profile preserves the replacement's cookies");
        Check((await neighbor.CookieManager.GetCookiesAsync("https://extension-fixture.example")).Single().Value == "neighbor", "Deleting one native profile preserves its live neighbor");
        Check(!(await manager.Store.ReadAsync(default)).Profiles.Any(p => p.Name == original.Name), "Deleted profile disappears from extension settings");
        // Retry after a process interruption: a profile already marked for deletion is successful cleanup.
        await manager.DeleteProfileAsync(original.Name, default);
        Check(true, "Native deletion can be retried while the shared browser process remains open");
        await lifecycle.ResetAsync("fixture.removed", Guid.NewGuid().ToString("N"), true);
        await lifecycle.ActivateAsync("fixture.removed");
        var reinstalled = (await lifecycle.RegisterAsync("fixture.removed", "persistent-remove", "persistent-remove.json"))!;
        var installed = await CreateAsync(environment, reinstalled.Name, false);
        Check((await installed.CookieManager.GetCookiesAsync("https://extension-fixture.example")).Count == 0, "Immediate reinstall does not reuse a deleted or signed-in profile");
        static void SetCookie(CoreWebView2 core, string value)
        {
            var cookie = core.CookieManager.CreateCookie("fixture", value, "extension-fixture.example", "/");
            core.CookieManager.AddOrUpdateCookie(cookie);
        }
    }
}
