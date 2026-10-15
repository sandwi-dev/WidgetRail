using System.Text.Json;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using WidgetRail.PlatformSettings;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyPassiveBrowserPinAsync(BrowserSurface page, string path, Action<bool, string> check)
    {
        var parent = page;
        var popup = Environment.GetCommandLineArgs().Contains("--webapp-passive-pin-popup");
        if (popup)
        {
            await PopupPreferences.SetAsync(parent.WidgetId, parent.ProfileName, "https://shell.fixture.invalid", BrowserPopupPlacement.InWidget, lifetime.Token);
            await parent.EvaluateGestureFixtureAsync("window.child=window.open('https://shell.fixture.invalid/popup')");
            await WebAppUntil(() => parent.PinnablePopup is { CurrentTitle: "Shell /popup", IsNavigationInProgress: false });
            page = parent.PinnablePopup!;
        }
        if (Environment.GetCommandLineArgs().Contains("--webapp-passive-pin-radial"))
        {

            await SelectAsync("webapp-a", enterWidget: false);
            PrepareRadialBackEntry(); RefreshRadialChooser();
        }
        var navigations = page.NavigationCount;
        await page.EvaluateFixtureAsync("document.body.style.background='#007d48';document.body.innerHTML='<h1>PASSIVE PIN</h1><button>Local synthetic button</button>';window.pinFrames=0;requestAnimationFrame(function tick(){pinFrames++;requestAnimationFrame(tick);});");
        check(((Microsoft.UI.Xaml.Controls.WebView2)page.CompositionRoot).Focus(FocusState.Keyboard), "The native page receives focus before its tray command");
        await ShowTrayMenuAsync(catalogItems.Single(item => item.Id == "webapp-a"));
        var commandId = popup ? "Overlay.TrayCommand.Pin.Popup.0" : "Overlay.TrayCommand.Pin.Full";
        await WebAppUntil(() => trayMenu?.Items.Any(item => AutomationProperties.GetAutomationId(item) == commandId) == true);
        trayMenu!.Items.Single(item => AutomationProperties.GetAutomationId(item) == commandId).Command.Execute(null);
        await WebAppUntil(() => popup ? pinned?.Popup?.Slot.Surface == page : Slots(pinned?.Presenter).Any(slot => slot.Surface == page));
        check(!PinnedInputActive && !pinned!.Window.Interactive, "A menu-created pin starts passive without acquiring input");
        await Observe("passive");
        NativePickerVisibilityForValidation!(false);
        await WebAppUntil(() => !visible);
        await Observe("passive-hidden");
        NativePickerVisibilityForValidation(true);
        await WebAppUntil(() => visible && foreground && !switching);
        await Observe("passive-reopened");
        await EnterPinnedAsync();
        await WebAppUntil(() => PinnedInputActive && page.IsBrowsing);
        await Observe("interactive");
        ExitPinnedInteraction(restoreMain: true);
        await Observe("passive-after-input");
        await UnpinAsync(save: true);
        check(page.NavigationCount == navigations && Slots(surface).Any(slot => slot.Surface == parent) && parent.ActiveSurface == page,
            "Pin, hide/reopen, interaction and unpin preserve the same live page without navigation");
        if (popup) await parent.ClosePopupAsync();

        async Task Observe(string stage)
        {
            await Task.Delay(800);
            var root = page.CompositionRoot;
            await File.WriteAllTextAsync(path + "." + stage + ".json", JsonSerializer.Serialize(new
            {
                stage, hwnd = (long)pinned!.Window.Handle, nativeVisible = pinned.Window.IsVisible,
                interactive = pinned.Window.Interactive, root.IsLoaded, root.Visibility,
                focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot)?.GetType().Name,
                bounds = pinned.Window.Bounds,
                hostVisible = root.XamlRoot?.IsHostVisible, root.ActualWidth, root.ActualHeight,
                page = await page.EvaluateFixtureAsync("JSON.stringify({visibility:document.visibilityState,frames:pinFrames,width:innerWidth,height:innerHeight})")
            }));
            // External raster capture targets only this generated fixture window.
            if (Environment.GetCommandLineArgs().Contains("--webapp-pin-raster"))
                await WebAppUntil(() => File.Exists(path + "." + stage + ".captured"));
        }
    }
}
