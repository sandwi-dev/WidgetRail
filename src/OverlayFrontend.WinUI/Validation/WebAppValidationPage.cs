using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Local synthetic website only. Never uses the user's browser profiles or accounts.</summary>
internal sealed partial class WebAppValidationPage : Page, IAsyncDisposable
{
    private readonly Grid host = new() { Margin = new(24) };
    private readonly NativePopupTheme theme = new();
    private BrowserSurface? browser;
    internal WebAppValidationPage(string result, string profileRoot, bool verify, bool permissions)
    {
        Content = host;
        theme.Attach(this); theme.Update(null, AppearanceSettings.Default);
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null);
                var app = new WebBrowserDocument("fixture", 1, "https://webapp.fixture.invalid/", "Webapp fixture")
                { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp };
                await Open("fixture.one", app);
                Check(!browser!.IsToolbarVisible, "Webapp has no permanent header");
                browser.Enter();
                var height = browser.CompositionRoot.ActualHeight;
                Press(ControllerButton.LeftStick);
                Check(browser.IsToolbarVisible, "LS opens browser controls");
                await Task.Delay(100);
                Check(Math.Abs(browser.CompositionRoot.ActualHeight - height) < 1, "Controls do not resize the webapp");
                Press(ControllerButton.B);
                Check(!browser.IsToolbarVisible && browser.IsInteracting, "B dismisses transient controls without leaving the page");
                Check(!browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed), "Page B is returned to the framework");
                if (verify)
                    Check(await Eval("localStorage.getItem('test')==='saved' && document.cookie.includes('test=saved')"), "Persistent profile survives process restart");
                else
                    Check(await Eval("localStorage.setItem('test','saved');document.cookie='test=saved;max-age=3600;path=/;Secure';true"), "Synthetic session seeded");
                browser.SetPresentation(false, false);
                browser.SetPresentation(true, true);
                Check(await Eval("localStorage.getItem('test')==='saved'"), "Hide/reopen preserves session");
                await Open("fixture.two", app);
                Check(await Eval("localStorage.getItem('test')===null && !document.cookie.includes('test=saved')"), "Different widget cannot see session");
                await Open("fixture.one", app with { Options = app.Options with { ProfileKey = "work" } });
                Check(await Eval("localStorage.getItem('test')===null && !document.cookie.includes('test=saved')"), "Different account cannot see session");
                await Open("fixture.one", app with { Options = app.Options with { Storage = BrowserStorage.Private } });
                Check(await Eval("localStorage.getItem('test')===null && !document.cookie.includes('test=saved')"), "Private profile cannot see persistent session");
                if (!verify) await Eval("localStorage.setItem('private','temporary');true");
                else Check(await Eval("localStorage.getItem('private')===null"), "Private storage does not survive process restart");
                // Keep permission/data-reset tests separate from the profile
                // whose marker must survive until the restart verification.
                await Open("fixture.permissions", app);
                var profile = browser.NativeProfile!;
                string? previousUrl;
                if (permissions)
                {
                    var permissionCalls = 0;
                    browser.Choose = (_, title, message, choices, token, _) =>
                    {
                        Check(title == "Website permission" && message.Contains("https://webapp.fixture.invalid", StringComparison.Ordinal), "Consent identifies the requesting website");
                        permissionCalls++;
                        return Task.FromResult<string?>("allow");
                    };
                    await profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Notifications, "https://webapp.fixture.invalid", CoreWebView2PermissionState.Default);
                    await browser.EvaluateGestureFixtureAsync("window.permissionResult='pending';Notification.requestPermission().then(x=>window.permissionResult=x)");
                    await WaitForScript("window.permissionResult==='granted'");
                    Check(permissionCalls == 1, "Native permission request reaches host consent");
                    Check((await profile.GetNonDefaultPermissionSettingsAsync()).Any(p => p.PermissionKind == CoreWebView2PermissionKind.Notifications && p.PermissionState == CoreWebView2PermissionState.Allow), "Allow is stored by the native profile");
                    previousUrl = await browser.ParkForProfileChangeAsync(CancellationToken.None);
                    await profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Notifications, "https://webapp.fixture.invalid", CoreWebView2PermissionState.Deny);
                    browser.RestoreAfterProfileChange(previousUrl);
                    await WaitForScript("document.title==='Webapp fixture'");
                    await browser.EvaluateGestureFixtureAsync("window.permissionResult='pending';Notification.requestPermission().then(x=>window.permissionResult=x)");
                    await WaitForScript("window.permissionResult==='denied'");
                    Check(permissionCalls == 1, "Revocation applies without another consent prompt");
                    previousUrl = await browser.ParkForProfileChangeAsync(CancellationToken.None);
                    await profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Notifications, "https://webapp.fixture.invalid", CoreWebView2PermissionState.Default);
                    browser.RestoreAfterProfileChange(previousUrl);
                    await WaitForScript("document.title==='Webapp fixture'");
                    var prompting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    browser.Choose = async (_, _, _, _, token, _) =>
                    {
                        prompting.TrySetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        return "allow";
                    };
                    await browser.EvaluateGestureFixtureAsync("window.permissionResult='pending';Notification.requestPermission().then(x=>window.permissionResult=x)");
                    await prompting.Task.WaitAsync(TimeSpan.FromSeconds(8));
                    browser.SetPresentation(false, false);
                    browser.SetPresentation(true, true);
                    await WaitForScript("window.permissionResult==='denied'");
                    Check(!(await profile.GetNonDefaultPermissionSettingsAsync()).Any(p => p.PermissionKind == CoreWebView2PermissionKind.Notifications), "Hidden-page cancellation does not save a permission decision");
                }
                await Eval("localStorage.setItem('clear-me','yes');document.cookie='clear-me=yes;path=/;Secure';true");
                previousUrl = await browser.ParkForProfileChangeAsync(CancellationToken.None);
                await profile.ClearBrowsingDataAsync();
                browser.RestoreAfterProfileChange(previousUrl);
                await WaitForScript("document.title==='Webapp fixture'");
                Check(await Eval("localStorage.getItem('clear-me')===null && !document.cookie.includes('clear-me=yes')"), "Native clear-data removes cookies and website storage");

                async Task Open(string widget, WebBrowserDocument document)
                {
                    if (browser is not null) { await browser.DisposeAsync(); host.Children.Remove(browser); }
                    BrowserSurface.FixturePage = _ => "<!doctype html><title>Webapp fixture</title><body style='background:#222;color:white'>Local webapp profile test</body>";
                    browser = new(widget, document, () => Task.FromResult(environment))
                    {
                        IsInteractionFocused = () => true,
                    };
                    host.Children.Add(browser);
                    browser.SetPresentation(true, true);
                    var end = DateTime.UtcNow.AddSeconds(20);
                    while (!browser.IsReady || browser.IsNavigationInProgress || browser.NavigationCount == 0 || browser.CurrentTitle != "Webapp fixture")
                    { if (DateTime.UtcNow > end) throw new TimeoutException("Webapp fixture failed to load."); await Task.Delay(50); }
                }
                async Task<bool> Eval(string script) => await browser!.EvaluateFixtureAsync(script) == "true";
                async Task WaitForScript(string script)
                {
                    var end = DateTime.UtcNow.AddSeconds(10);
                    while (!await Eval(script))
                    { if (DateTime.UtcNow > end) throw new TimeoutException("Local fixture condition failed: " + script + "; state=" +
                        await browser!.EvaluateFixtureAsync("JSON.stringify({result:window.permissionResult,permission:Notification.permission,secure:isSecureContext,origin:location.origin})")); await Task.Delay(50); }
                }
                void Press(ControllerButton button)
                { browser!.HandleButton(button, ControllerEventPhase.Pressed); browser.HandleButton(button, ControllerEventPhase.Released); }
                void Check(bool passed, string name)
                { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
            }
            catch (Exception failure) { error = failure.ToString(); }
            await DisposeAsync();
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, verify, permissions, checks, error, permissionTrace = browser?.FixturePermissionTrace }));
        };
    }
    public async ValueTask DisposeAsync()
    { if (browser is not null) await browser.DisposeAsync(); }
}
