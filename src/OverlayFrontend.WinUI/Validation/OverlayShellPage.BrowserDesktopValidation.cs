using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableBrowserDesktopValidation(string path)
    {
        BrowserSurface.FixturePage = _ => """
            <!doctype html><title>Desktop input fixture</title>
            <style>body{font:24px sans-serif;background:#18334b;color:white;padding:30px;min-height:2000px}input,button{font:24px sans-serif;padding:16px;margin:16px}button{position:fixed;left:40%;top:40%;width:20%;height:20%;margin:0}</style>
            <label>Desktop typing fixture<input id="typed" aria-label="Desktop typing fixture"></label>
            <button aria-label="Desktop mouse fixture" onclick="window.clicked=true">Desktop mouse fixture</button>
            """;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                if (startup is not null) await startup;
                await Until(() => interactive && foreground && surface?.CurrentBinding is not null);
                await Until(() => Find(surface)?.Surface is { IsReady: true, IsNavigationInProgress: false });
                Find(surface)!.Surface!.NavigateForValidation("https://example.com/desktop-fixture");
                await Until(() => Find(surface)?.Surface is { IsReady: true, IsNavigationInProgress: false });
                var browser = Find(surface)!.Surface!;
                if (!((WebView2)browser.CompositionRoot).Focus(FocusState.Keyboard)) throw new InvalidOperationException("WebView focus refused.");
                await browser.EvaluateFixtureAsync("document.getElementById('typed').focus()");
                await File.WriteAllTextAsync(path + ".ready", Environment.ProcessId.ToString());
                await UntilScript("document.getElementById('typed').value==='desktoXp'");
                if (!browser.HasNativeKeyboardFocus || browser.HasDialog) throw new InvalidOperationException("Native typing lost focus or opened a controller dialog.");
                checks.Add("OS keyboard text and Left arrow edit the native field without invoking controller navigation");
                await File.WriteAllTextAsync(path + ".typed", "ready for mouse");
                await UntilScript("window.clicked===true");
                if (browser.HasDialog) throw new InvalidOperationException("Mouse click opened controller editor.");
                checks.Add("OS mouse click reaches the page without invoking controller editing");
                await File.WriteAllTextAsync(path + ".clicked", "ready for scrolling");
                await UntilScript("window.scrollY>20");
                checks.Add("Native page navigation scrolls inside the WebView");
                async Task UntilScript(string script)
                {
                    var end = Environment.TickCount64 + 60000;
                    while (await browser.EvaluateFixtureAsync(script) != "true")
                    { if (Environment.TickCount64 > end) throw new TimeoutException("Desktop input was not delivered."); await Task.Delay(50); }
                }
            }
            catch (Exception failure) { error = failure.ToString(); }
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = error is null, checks, error }));
        };
        static BrowserSlot? Find(DependencyObject? root)
        {
            if (root is null) return null;
            if (root is BrowserSlot slot) return slot;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (Find(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
            return null;
        }
        static async Task Until(Func<bool> condition)
        {
            var end = Environment.TickCount64 + 15000;
            while (!condition()) { if (Environment.TickCount64 > end) throw new TimeoutException("Browser startup timed out."); await Task.Delay(25); }
        }
    }
}
