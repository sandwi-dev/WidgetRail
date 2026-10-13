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
    internal WebAppValidationPage(string result, string profileRoot, bool verify)
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
                    while (!browser.IsReady || browser.IsNavigationInProgress || browser.NavigationCount == 0)
                    { if (DateTime.UtcNow > end) throw new TimeoutException("Webapp fixture failed to load."); await Task.Delay(50); }
                }
                async Task<bool> Eval(string script) => await browser!.EvaluateFixtureAsync(script) == "true";
                void Press(ControllerButton button)
                { browser!.HandleButton(button, ControllerEventPhase.Pressed); browser.HandleButton(button, ControllerEventPhase.Released); }
                void Check(bool passed, string name)
                { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
            }
            catch (Exception failure) { error = failure.ToString(); }
            await DisposeAsync();
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, verify, checks, error }));
        };
    }
    public async ValueTask DisposeAsync()
    { if (browser is not null) await browser.DisposeAsync(); }
}
