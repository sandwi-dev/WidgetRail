using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WebAppDialogsValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    internal WebAppDialogsValidationPage(string result, string profileRoot)
    {
        Task<CoreWebView2Environment>? environment = null;
        BrowserSurface.FixturePage = _ => "<!doctype html><title>Dialog fixture</title><body style='height:100vh;margin:0'>Local website actions</body>";
        browser = new("fixture.dialogs", new("dialogs", 1, "https://dialogs.fixture.invalid/", "Dialog fixture")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null).AsTask())
        { IsInteractionFocused = () => true };
        Content = browser;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null; var choices = 0; var menus = 0;
            try
            {
                string? reply = "accept";
                browser.Choose = (_, title, message, options, _, _) =>
                {
                    if (!message.Contains("https://dialogs.fixture.invalid", StringComparison.Ordinal)) throw new InvalidOperationException("Missing requesting origin.");
                    if (title == "Page actions")
                    { if (options.Count == 0) throw new InvalidOperationException("Empty native menu."); menus++; return Task.FromResult<string?>(null); }
                    choices++; return Task.FromResult<string?>(reply);
                };
                browser.SetPresentation(true, true);
                await UntilScript("document.title==='Dialog fixture'"); browser.Enter();
                await browser.EvaluateFixtureAsync("setTimeout(()=>{alert('Synthetic alert');window.alertDone=true;},0)");
                await UntilScript("window.alertDone===true");
                Check(choices == 1 && !browser.HasDialog, "Alert completes through origin-labelled host choice");
                await browser.EvaluateFixtureAsync("setTimeout(()=>window.confirmed=confirm('Synthetic confirmation'),0)");
                await UntilScript("window.confirmed===true");
                reply = null;
                await browser.EvaluateFixtureAsync("setTimeout(()=>window.cancelled=confirm('Cancel confirmation'),0)");
                await UntilScript("window.cancelled===false");
                Check(choices == 3, "Confirm preserves accepted and cancelled results");
                await browser.EvaluateFixtureAsync("setTimeout(()=>window.promptValue=prompt('Enter synthetic text','fixture text'),0)");
                await Until(() => browser.FixtureEditor?.IsLoaded == true);
                Check(browser.ControllerTextEntry is not null, "Website prompt uses the shared controller keyboard");
                Press(ControllerButton.RightTrigger);
                await UntilScript("window.promptValue==='fixture text'");
                Check(!browser.HasDialog, "Explicit prompt submit returns text to the website");
                await browser.EvaluateFixtureAsync("setTimeout(()=>window.promptCancelled=prompt('Cancel this prompt',''),0)");
                await Until(() => browser.FixtureEditor?.IsLoaded == true);
                Press(ControllerButton.B);
                await UntilScript("window.promptCancelled===null");
                Check(!browser.HasDialog, "Controller B cancels website prompt");
                Press(ControllerButton.Menu);
                await Until(() => menus == 1 && !browser.HasDialog);
                Check(true, "Controller Menu reaches native page actions through the themed chooser");
                await browser.EvaluateFixtureAsync("window.addEventListener('contextmenu',e=>{e.preventDefault();window.customMenu=true;})");
                Press(ControllerButton.Menu);
                await UntilScript("window.customMenu===true");
                Check(menus == 1, "Site-owned context menu receives the native right-click without a second host menu");
                browser.SetPresentation(false, false);
                await browser.EvaluateFixtureAsync("setTimeout(()=>window.hidden=confirm('Hidden page must not prompt'),0)");
                await UntilScript("window.hidden===false");
                Check(choices == 3, "Hidden website cannot open a script dialog");
                browser.SetPresentation(true, true);
                await browser.EvaluateFixtureAsync("setTimeout(()=>window.retiredPrompt=prompt('Close pending prompt',''),0)");
                await Until(() => browser.FixtureEditor?.IsLoaded == true);
                await browser.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                Check(browser.ControllerTextEntry is null && !browser.HasDialog, "Retirement cancels prompt and completes its native deferral");
            }
            catch (Exception failure) { error = failure.ToString(); }
            try { await DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception failure) { error ??= failure.ToString(); }
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, choices, menus, error }));
            void Press(ControllerButton button) { browser.HandleButton(button, ControllerEventPhase.Pressed); browser.HandleButton(button, ControllerEventPhase.Released); }
            async Task UntilScript(string script)
            {
                var end = DateTime.UtcNow.AddSeconds(15);
                while (!browser.IsReady || await browser.EvaluateFixtureAsync(script) != "true")
                { if (DateTime.UtcNow > end) throw new TimeoutException("Dialog script timed out: " + script); await Task.Delay(50); }
            }
            void Check(bool passed, string name)
            { if (!passed) throw new InvalidOperationException(name); checks.Add(name); File.WriteAllText(result + ".progress", string.Join('\n', checks)); }
        };
    }
    private static async Task Until(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException("Dialog state timed out."); await Task.Delay(50); }
    }
    public ValueTask DisposeAsync() => browser.DisposeAsync();
}
