using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifySettingsHomeDownAsync(Action<bool, string> check)
    {
        var settings = catalogItems.Single(widget => widget.Id == "settings");
        check(!settings.CanStop && !settings.IsStopped, "Settings is available and cannot be stopped");
        await ShowTrayMenuAsync(settings);
        await Task.Delay(200);
        check(trayMenu is not null && !trayMenu.Flyout.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Stop widget"),
            "Settings tray omits Stop widget");
        check(trayMenu!.Flyout.Items.OfType<MenuFlyoutSubItem>().Where(item => item.Text == "Widget")
            .SelectMany(item => item.Items.OfType<MenuFlyoutItem>()).Any(item => item.Text == "Restart widget"),
            "Settings retains Restart widget");
        CloseTrayMenu(restoreFocus: false);
        await SelectAsync("settings", true);
        await Task.Delay(500);
        var viewport = FindControl<ScrollViewer>(surface!, "Widget.settings.categories")!;
        var traces = new List<string>();
        surface!.NavigationObserved = traces.Add;
        var originalHeight = viewport.MaxHeight;
        try
        {
            // Both the natural home layout and a smaller viewport must leave
            // the last card without re-entering earlier cards after scrolling.
            foreach (var height in new[] { originalHeight, 260d })
            {
                viewport.MaxHeight = height;
                viewport.ChangeView(null, 0, null, disableAnimation: true);
                var first = FindControl<Control>(surface, "Widget.category.overlay")!;
                first.Focus(FocusState.Keyboard);
                first.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                await Task.Delay(250);
                var reachedAbout = false;
                var reachedFooter = false;
                for (var press = 0; press < 16 && !reachedFooter; press++)
                {
                    surface.MoveFocus(FocusNavigationDirection.Down);
                    await Task.Delay(180);
                    var id = FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";
                    if (id == "Widget.category.diagnostics") reachedAbout = true;
                    else if (reachedAbout && id.StartsWith("Widget.category.", StringComparison.Ordinal))
                        throw new InvalidOperationException("Down re-entered " + id + " after About. " + string.Join("; ", traces));
                    reachedFooter = id == "Widget.settings.restart";
                }
                if (!reachedAbout || !reachedFooter) throw new InvalidOperationException("Settings Down did not reach About then Restart. " + string.Join("; ", traces));
                check(true, "Settings Down reaches About then Restart without returning to earlier cards (viewport " + viewport.ViewportHeight + ")");
            }
        }
        finally
        {
            viewport.MaxHeight = originalHeight;
            surface.NavigationObserved = null;
        }
    }
}
