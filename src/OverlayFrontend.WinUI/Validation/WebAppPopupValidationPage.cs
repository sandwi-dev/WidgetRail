using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Local synthetic popup exchange; no accounts or external requests.</summary>
internal sealed partial class WebAppPopupValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    internal WebAppPopupValidationPage(string result, string profileRoot)
    {
        Task<CoreWebView2Environment>? environment = null;
        var pauseChild = false;
        var childGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BrowserSurface.FixturePage = uri => uri.Contains("/child", StringComparison.Ordinal)
            ? "<!doctype html><title>Child fixture</title><body><input aria-label='Popup input'>Local child</body>"
            : "<!doctype html><title>Parent fixture</title><body>Local opener</body>";
        var document = new WebBrowserDocument("popup.parent", 1, "https://popup.fixture.invalid/", "Popup fixture")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp };
        browser = new("fixture.popups", document,
            async () => { if (pauseChild) await childGate.Task; return await (environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null).AsTask()); })
        { IsInteractionFocused = () => true };
        Content = browser;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null; var reserved = 0; var focused = 0;
            browser.FocusInteraction = () => focused++;
            browser.ReservePopup = _ => { if (reserved != 0) return false; reserved++; return true; };
            browser.ReleasePopup = _ => reserved = 0;
            try
            {
                browser.SetPresentation(true, true);
                await Until(() => browser.IsReady && browser.CurrentTitle == "Parent fixture" && !browser.IsNavigationInProgress);
                await browser.EvaluateFixtureAsync("localStorage.setItem('popup-proof','shared');window.received=null;window.addEventListener('message',e=>{if(e.origin===location.origin)window.received=e.data;});");
                await Open();
                var child = browser.ActiveSurface;
                Check(reserved == 1 && child.ProfileName == browser.ProfileName, "Child uses the parent profile and reserves a popup slot");
                Check(await child.EvaluateFixtureAsync("!!window.opener && localStorage.getItem('popup-proof')==='shared'") == "true", "Native child preserves opener and shared profile storage");
                await child.EvaluateFixtureAsync("opener.postMessage('fixture-result',location.origin)");
                await UntilScript(browser, "window.received==='fixture-result'");
                Check(true, "Cross-window postMessage delivers to the original page");
                browser.SetPresentation(false, false);
                Check(!child.CanRequestSiteDecision, "Hidden popup cannot request website consent");
                browser.SetPresentation(true, true); browser.Enter();
                Check(child.CanRequestSiteDecision && browser.IsBrowsing, "Reopening restores popup input");
                await child.EvaluateGestureFixtureAsync("window.open('https://popup.fixture.invalid/child-nested')");
                Check(!child.HasPopup, "Nested popup is bounded without replacing either page");
                await child.EvaluateFixtureAsync("window.close()");
                await Until(() => !browser.HasPopup && reserved == 0);
                Check(focused > 0 && await browser.EvaluateFixtureAsync("window.received==='fixture-result'") == "true", "window.close returns to the unchanged parent");
                await Open();
                Check(browser.CaptureZoomGesture(ControllerButton.RightTrigger) is not null, "Controller zoom ownership follows the active popup");
                browser.HandleButton(ControllerButton.LeftStick, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.LeftStick, ControllerEventPhase.Released);
                Check(browser.IsToolbarFocused, "LS reaches the popup toolbar");
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Released);
                Check(browser.HasPopup && !browser.IsToolbarFocused, "First B returns from popup toolbar to its page");
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                await Until(() => !browser.HasPopup);
                Check(browser.HandleButton(ControllerButton.B, ControllerEventPhase.Released), "B closes popup and owns its release");
                await Open("about:blank");
                Check(await browser.ActiveSurface.EvaluateFixtureAsync("!!window.opener") == "true", "Blank authentication popup retains its opener");
                await browser.DrainPopupAsync();
                Check(!browser.HasPopup && reserved == 0, "Profile-change drain closes children and releases budget");
                await Open();
                browser.HandleButton(ControllerButton.LeftStick, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.LeftStick, ControllerEventPhase.Released);
                browser.Width = 320;
                await Until(() => browser.ActiveSurface.ActualWidth is > 300 and <= 320);
                browser.UpdateLayout();
                var close = FindButton(browser.ActiveSurface, "Browser.ClosePopup")!;
                var address = FindButton(browser.ActiveSurface, "Browser.Address")!;
                Check(address.ActualWidth > 100 && close.TransformToVisual(browser.ActiveSurface).TransformPoint(new(close.ActualWidth, 0)).X <= 321,
                    "Narrow popup keeps its origin and close control within the viewport");
                browser.Width = double.NaN;
                await browser.DrainPopupAsync();
                pauseChild = true;
                var opening = browser.EvaluateGestureFixtureAsync("window.open('https://popup.fixture.invalid/child')");
                await Until(() => browser.HasPopup);
                var disposing = browser.DisposeAsync().AsTask();
                childGate.TrySetResult();
                await disposing.WaitAsync(TimeSpan.FromSeconds(20));
                try { await opening.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception failure) when (failure is System.Runtime.InteropServices.COMException or ObjectDisposedException or TimeoutException) { }
                Check(reserved == 0 && !browser.HasPopup, "Disposal during popup startup drains its deferral, controller and budget");
            }
            catch (Exception failure) { error = failure.ToString(); }
            childGate.TrySetResult();
            try { await DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (Exception failure) { error ??= failure.ToString(); }
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, error }));
            async Task Open(string url = "https://popup.fixture.invalid/child")
            {
                await browser.EvaluateGestureFixtureAsync("window.child=window.open(" + JsonSerializer.Serialize(url) + ",'_blank')");
                await Until(() => browser.HasPopup && !ReferenceEquals(browser.ActiveSurface, browser) && browser.ActiveSurface.IsReady &&
                    (url == "about:blank" || browser.ActiveSurface.CurrentTitle == "Child fixture") && !browser.ActiveSurface.IsNavigationInProgress);
                browser.Enter();
            }
            void Check(bool passed, string name)
            { if (!passed) throw new InvalidOperationException(name); checks.Add(name); File.WriteAllText(result + ".progress", string.Join('\n', checks)); }
        };
    }
    private static async Task Until(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(20);
        while (!ready()) { if (DateTime.UtcNow > end) throw new TimeoutException("Popup fixture did not reach the expected state."); await Task.Delay(50); }
    }
    private static async Task UntilScript(BrowserSurface surface, string script)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (await surface.EvaluateFixtureAsync(script) != "true")
        { if (DateTime.UtcNow > end) throw new TimeoutException("Popup script did not reach the expected state."); await Task.Delay(50); }
    }
    public ValueTask DisposeAsync() => browser.DisposeAsync();
    private static Button? FindButton(DependencyObject root, string id)
    {
        if (root is Button button && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == id) return button;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindButton(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i), id) is { } found) return found;
        return null;
    }
}
