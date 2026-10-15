using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyWebsiteSettingsAsync(BrowserSurface page, Action<bool, string> check)
    {
        const string origin = "https://shell.fixture.invalid";
        var location = PinnedInputActive ? "pin" : "main";
        await PopupPreferences.SetAsync(page.WidgetId, page.ProfileName, origin, BrowserPopupPlacement.InWidget, lifetime.Token);
        var settings = page.ShowSettingsFromHostAsync(ShowBrowserChoiceAsync);
        var root = await Dialog("Website settings");
        await Focused(root, "permission.0");
        check(Options(root).GroupStyle.Count == 1 && Options(root).Items.OfType<ListViewItem>()
            .All(item => !Descendants(item.Content as DependencyObject).OfType<TextBlock>()
                .Any(text => text.Text is "Permissions" or "Behavior" or "Data")),
            location + ": section headings are native group headers outside actionable rows");
        check(root.CloseButtonText == "Done" && Options(root).Items.Count == 9,
            location + ": website settings shows seven permissions, one behavior row and clear data with Done");
        await CapturePopupDecisionAsync(root, "website-settings-" + location);
        var viewport = Descendants(Options(root)).OfType<ScrollViewer>().First();
        root.SampleScroll(0, -32767, 1000); root.SampleScroll(0, -32767, 1040);
        await WebAppUntil(() => viewport.VerticalOffset > 0);
        root.SampleScroll(0, 0, 1100); root.SampleScroll(0, 0, 1240);
        check(viewport.VerticalOffset > 0, location + ": right stick scrolls the choice list");
        var lastPermission = Options(root).Items.OfType<ListViewItem>().Single(item => (string?)item.Tag == "permission.6");
        Options(root).ScrollIntoView(lastPermission); lastPermission.Focus(FocusState.Keyboard);
        root.MoveFocus(FocusNavigationDirection.Down); await Focused(root, "windows");
        root.MoveFocus(FocusNavigationDirection.Down); await Focused(root, "clear");
        check(true, location + ": controller navigation crosses sections without focusing headings");
        await Select(root, "windows");
        var windows = await Dialog("New windows");
        await Focused(windows, "widget");
        check(windows.CloseButtonText == "Back", location + ": new-window picker focuses the saved choice and offers Back");
        await CapturePopupDecisionAsync(windows, "website-new-windows-" + location);
        await Press(ControllerButton.B);
        root = await Dialog("Website settings"); await Focused(root, "windows");
        check(PopupPreferences.Get(page.WidgetId, page.ProfileName, origin) == BrowserPopupPlacement.InWidget,
            location + ": Back preserves placement and restores the behavior row");
        await Select(root, "windows");
        await Select(await Dialog("New windows"), "ask");
        root = await Dialog("Website settings"); await Focused(root, "windows");
        check(PopupPreferences.Get(page.WidgetId, page.ProfileName, origin) == BrowserPopupPlacement.Ask,
            location + ": new-window changes save and return to settings");
        await CapturePopupDecisionAsync(root, "website-settings-behavior-" + location);
        await Select(root, "clear");
        var clear = await Dialog("Clear website data?");
        await WebAppUntil(() => FindControl<Button>(clear, "Host.Choice.Close")?.FocusState == FocusState.Keyboard);
        await Press(ControllerButton.B);
        root = await Dialog("Website settings"); await Focused(root, "clear");
        check(true, location + ": clear-data confirmation keeps safe Cancel focus; cancelling restores its settings row");
        await Press(ControllerButton.B); await WebAppUntil(() => !HostChoiceActive);
        await settings.WaitAsync(TimeSpan.FromSeconds(3));

        async Task Press(ControllerButton button)
        { Receive(new ControllerFrame { Connected = 1 }); await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
        async Task<HostChoiceDialog> Dialog(string title)
        {
            await WebAppUntil(() => hostChoiceDialog is { IsOpenForValidation: true } dialog && (string?)dialog.Title == title);
            return hostChoiceDialog!;
        }
        ListView Options(HostChoiceDialog dialog) => FindControl<ListView>(dialog, "Host.Choice.Options")!;
        Task Focused(HostChoiceDialog dialog, string id) => WebAppUntil(() =>
            FocusManager.GetFocusedElement(dialog.XamlRoot) is ListViewItem { Tag: string focused } && focused == id);
        async Task Select(HostChoiceDialog dialog, string id)
        {
            var list = Options(dialog);
            var item = list.Items.OfType<ListViewItem>().Single(value => (string?)value.Tag == id);
            list.ScrollIntoView(item); item.Focus(FocusState.Keyboard);
            await Press(ControllerButton.A);
            await WebAppUntil(() => !ReferenceEquals(hostChoiceDialog, dialog));
        }
        static IEnumerable<DependencyObject> Descendants(DependencyObject? node)
        {
            if (node is null) yield break;
            yield return node;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i))) yield return child;
        }
    }
}
