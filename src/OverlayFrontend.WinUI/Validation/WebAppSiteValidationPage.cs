using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Read-only signed-out compatibility probe using a new empty browser profile.</summary>
internal sealed partial class WebAppSiteValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    internal WebAppSiteValidationPage(string result, string profileRoot, string url, string? registrationUrl)
    {
        if (Directory.Exists(profileRoot) && Directory.EnumerateFileSystemEntries(profileRoot).Any())
            throw new ArgumentException("Site validation requires a new empty profile directory.");
        if (!Valid(url) || registrationUrl is not null && !Valid(registrationUrl)) throw new ArgumentException("HTTPS site URLs are required.");
        Task<CoreWebView2Environment>? environment = null;
        browser = new("fixture.signed-out-site", new("site", 1, url, "Signed-out webapp compatibility")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null).AsTask())
        { IsInteractionFocused = () => true, Width = 480, HorizontalAlignment = HorizontalAlignment.Center };
        Content = browser;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null; string? path = null;
            try
            {
                browser.SetPresentation(true, true);
                await WaitForFields();
                path = new Uri(browser.CurrentUrl!).AbsolutePath;
                Check(!browser.IsToolbarVisible, "Signed-out website renders inside the public full-page webapp element");
                browser.Enter();
                await browser.EvaluateFixtureAsync("[...document.querySelectorAll('input[type=password]')].find(e=>e.getClientRects().length>0).focus()");
                browser.HandleButton(ControllerButton.RightStick, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.RightStick, ControllerEventPhase.Released);
                await Until(() => browser.FixtureEditor?.IsLoaded == true);
                Check(browser.FixtureEditor is { IsSensitive: true, PasswordVisible: false }, "Controller Type opens the protected keyboard for the site's actual password field");
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => !browser.HasDialog);
                Check(!browser.HasDialog, "Controller B closes the keyboard without submitting the website form");
                if (registrationUrl is not null)
                {
                    browser.NavigateForValidation(registrationUrl);
                    await Until(() => browser.CurrentUrl is { } current && new Uri(current).AbsolutePath == new Uri(registrationUrl).AbsolutePath && !browser.IsNavigationInProgress);
                    await WaitForFields();
                    Check(true, "Public registration page renders without an account or form submission");
                }
            }
            catch (Exception failure) { error = failure.ToString(); }
            var title = browser.CurrentTitle;
            await DisposeAsync();
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, path, title, error }));
            async Task WaitForFields()
            {
                var end = DateTime.UtcNow.AddSeconds(60);
                while (!browser.IsReady || await browser.EvaluateFixtureAsync("[...document.querySelectorAll('input[type=password]')].some(e=>e.getClientRects().length>0)") != "true")
                { if (DateTime.UtcNow > end) throw new TimeoutException("Signed-out form did not appear."); await Task.Delay(100); }
            }
            void Check(bool passed, string name)
            { if (!passed) throw new InvalidOperationException(name); checks.Add(name); File.WriteAllText(result + ".progress", string.Join('\n', checks)); }
        };
    }
    private static bool Valid(string value) => WebBrowserDocument.IsWebUrl(value) && new Uri(value).Scheme == "https";
    private static async Task Until(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException("Site interaction did not complete."); await Task.Delay(50); }
    }
    public ValueTask DisposeAsync() => browser.DisposeAsync();
}
