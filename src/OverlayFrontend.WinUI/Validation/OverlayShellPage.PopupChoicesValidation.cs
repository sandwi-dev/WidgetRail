using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyPopupPlacementChoicesAsync(BrowserSurface parent, Action<bool, string> check)
    {
        const string origin = "https://shell.fixture.invalid";
        await PopupPreferences.SetAsync(parent.WidgetId, parent.ProfileName, origin, BrowserPopupPlacement.Ask, lifetime.Token);
        var pending = Open();
        await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
        check(parent.PinnablePopup is null && ReferenceEquals(parent.ActiveSurface, parent), "Placement dialog appears before a popup takes over the parent");
        FindControl<CheckBox>(hostChoiceDialog!, "Host.Choice.Remember")!.Focus(FocusState.Keyboard);
        await Press(ControllerButton.A);
        await CapturePopupDecisionAsync(hostChoiceDialog!, "popup-choice");
        await Press(ControllerButton.B);
        await pending;
        await WebAppUntil(() => !parent.HasPopup && !HostChoiceActive);
        check(true, "B cancels popup creation and releases the pending popup reservation");
        check(PopupPreferences.Get(parent.WidgetId, parent.ProfileName, origin) == BrowserPopupPlacement.Ask,
            "Cancelling a checked Remember choice does not save a preference");

        pending = Open(); await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
        parent.NavigateForValidation("https://shell.fixture.invalid/primary?cancel-popup=1");
        // Native navigation can invalidate the old document's pending CDP
        // evaluation reply; the actual admission/child state is checked below.
        try { await pending; }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        await WebAppUntil(() => !HostChoiceActive && !parent.HasPopup && !parent.IsNavigationInProgress && parent.IsBrowsing);
        check(true, "Parent navigation cancels pending popup admission without creating a late child");

        pending = Open(); await Decide("widget", remember: true); await pending;
        var child = await Child();
        check(PopupPreferences.Get(parent.WidgetId, parent.ProfileName, origin) == BrowserPopupPlacement.InWidget && !child.IsToolbarVisible,
            "Controller A toggles Remember and opens an in-widget popup without permanent chrome");
        await Press(ControllerButton.LeftStick);
        check(child.IsToolbarVisible, "LS reveals an in-widget popup's browser controls");
        await Press(ControllerButton.B);
        check(!child.IsToolbarVisible && parent.HasPopup, "B dismisses popup controls while retaining the live child");
        await parent.ClosePopupAsync();
        await Open(); await Child();
        check(!HostChoiceActive, "Remembered in-widget placement skips the next prompt");
        await parent.ClosePopupAsync();

        await WebAppUntil(() => parent.IsBrowsing);
        await Press(ControllerButton.LeftStick);
        FindControl<Button>(parent, "Browser.SiteControls")!.Focus(FocusState.Keyboard);
        await Press(ControllerButton.A);
        await Decide("popups"); await Decide("ask");
        await WebAppUntil(() => !HostChoiceActive && PopupPreferences.Get(parent.WidgetId, parent.ProfileName, origin) == BrowserPopupPlacement.Ask);
        if (parent.IsToolbarFocused) await Press(ControllerButton.LeftStick);
        check(true, "Website controls resets remembered placement to Ask each time");

        pending = Open(); await Decide("pin", remember: true); await pending;
        child = await Child();
        await WebAppUntil(() => pinned?.Popup?.Child == child && parent.IsBrowsing);
        check(ReferenceEquals(parent.ActiveSurface, parent) && !child.IsToolbarVisible && !HostChoiceActive,
            "Open pinned places the new child directly in the shared pin and leaves its parent usable");
        check(await child.EvaluateFixtureAsync("window.opener!==null") == "true", "Direct pin admission preserves the popup opener relationship");
        await EnterPinnedAsync(); await Press(ControllerButton.LeftStick);
        check(child.IsToolbarVisible, "LS reveals controls on a directly pinned popup");
        await Press(ControllerButton.B);
        check(!child.IsToolbarVisible && PinnedInputActive, "B hides pinned popup controls without leaving interaction");
        await UnpinAsync(save: false); await parent.ClosePopupAsync();
        await WebAppUntil(() => parent.IsBrowsing && foreground);
        await Open(); child = await Child();
        await WebAppUntil(() => pinned?.Popup?.Child == child);
        check(!HostChoiceActive, "Remembered pin placement directly opens the next popup when the pin is free");
        await UnpinAsync(save: false); await parent.ClosePopupAsync();

        await PinAsync(parent.WidgetId, WidgetPinnedProjection.FullWidgetLayoutId);
        await EnterPinnedAsync(); await WebAppUntil(() => PinnedInputActive && parent.IsBrowsing &&
            Slots(pinned?.Presenter).Any(slot => slot.Surface == parent) && ReferenceEquals(parent.XamlRoot, pinned!.Window.AutomationRoot.XamlRoot));
        var previousPin = pinned;
        pending = Open();
        await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
        check(ReferenceEquals(pinned, previousPin) && hostChoiceDialog!.Title.ToString() == "Where should this pop-out open?",
            "Remembered pin placement still asks before replacing an existing pin");
        await CapturePopupDecisionAsync(hostChoiceDialog!, "popup-replace");
        await Press(ControllerButton.B); await pending;
        check(ReferenceEquals(pinned, previousPin) && !parent.HasPopup, "Cancelling replacement preserves the existing pin");
        pending = Open(); await Decide("pin"); await pending; child = await Child();
        await WebAppUntil(() => pinned?.Popup?.Child == child && foreground);
        check(!ReferenceEquals(pinned, previousPin) && ReferenceEquals(parent.ActiveSurface, parent),
            "One explicit Replace pin decision replaces the parent pin without a second confirmation");
        await UnpinAsync(save: false); await parent.ClosePopupAsync();
        await PopupPreferences.SetAsync(parent.WidgetId, parent.ProfileName, origin, BrowserPopupPlacement.InWidget, lifetime.Token);
        await WebAppUntil(() => parent.IsBrowsing && foreground);

        Task Open() => parent.EvaluateGestureFixtureAsync("window.child=window.open('https://shell.fixture.invalid/placement-popup')");
        async Task<BrowserSurface> Child()
        {
            await WebAppUntil(() => parent.PinnablePopup is { CurrentTitle: "Shell /placement-popup", IsNavigationInProgress: false });
            return parent.PinnablePopup!;
        }
        async Task Press(ControllerButton button)
        { Receive(new ControllerFrame { Connected = 1 }); await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
        async Task Decide(string id, bool remember = false)
        {
            await WebAppUntil(() => hostChoiceDialog is { IsLoaded: true });
            var dialog = hostChoiceDialog!;
            if (remember)
            {
                FindControl<CheckBox>(dialog, "Host.Choice.Remember")!.Focus(FocusState.Keyboard);
                await Press(ControllerButton.A);
                check(dialog.RememberChoice, "The remember checkbox accepts controller A without closing the dialog");
            }
            var list = FindControl<ListView>(dialog, "Host.Choice.Options");
            if (list is { IsLoaded: true })
            {
                var item = list.Items.OfType<ListViewItem>().Single(item => (string?)item.Tag == id);
                list.SelectedItem = item; item.Focus(FocusState.Keyboard);
            }
            else FindControl<Button>(dialog, id == "pin" ? "Host.Choice.Primary" : "Host.Choice.Secondary")!.Focus(FocusState.Keyboard);
            await Press(ControllerButton.A);
            await WebAppUntil(() => !ReferenceEquals(hostChoiceDialog, dialog));
        }
    }

    private static async Task CapturePopupDecisionAsync(HostChoiceDialog dialog, string name)
    {
        // Visual review only: let the native dialog's entrance animation settle.
        await Task.Delay(300);
        var result = FrontendArguments.Value(Environment.GetCommandLineArgs(), "--validate-webapp-shell")!;
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(dialog);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync(); stream.Seek(0);
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(Path.ChangeExtension(result, "." + name + ".png"), bytes);
    }
}
