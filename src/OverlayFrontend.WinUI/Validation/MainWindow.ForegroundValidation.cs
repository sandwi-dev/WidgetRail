using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private void EnableForegroundDismissalValidation(Shell.OverlayShellPage page, string path, nint peer)
    {
        var started = false;
        page.Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = new List<string>();
            Shell.PinnedWidgetWindow? pin = null;
            try
            {
                await page.WaitForNativeStartupAsync().WaitAsync(TimeSpan.FromSeconds(45));
                await Until(() => input?.IsForeground == true && Find("Widget.category.controllers") is not null);
                Check(!AppWindow.IsShownInSwitchers, "overlay stays excluded from system switchers");
                Check(Find("Widget.category.controllers")!.Focus(FocusState.Keyboard), "noninitial Settings category receives focus");
                await Task.Delay(700);
                await Until(() => FocusId() == "Widget.category.controllers");

                pin = new("foreground validation");
                pin.SetContent(new TextBlock { Text = "Pinned test surface", Margin = new(20) });
                pin.Place(new(40, 40, 280, 120)); pin.Show();
                await Task.Delay(100);
                Check(AppWindow.IsVisible && input!.IsForeground, "passive pin does not dismiss or activate over main");
                pin.SetInteraction(true); pin.Activate();
                await Task.Delay(150);
                Check(AppWindow.IsVisible && input!.IsForeground && !input.IsCurrentExternalForeground(pin.Handle),
                    "same-process pinned activation keeps the main overlay open");
                Activate(); input!.AcquireForeground(); pin.SetInteraction(false);
                await Until(() => FocusId() == "Widget.category.controllers");

                WinUIEx.HwndExtensions.SetForegroundWindow(peer);
                await Until(() => !AppWindow.IsVisible);
                Check(!input.IsActive && desktopBackdrop?.IsVisible != true, "external activation hides main, backdrop and visible input");
                Check(pin.IsVisible, "passive pinned surface survives external dismissal");
                ShowOverlay();
                await Until(() => AppWindow.IsVisible && input.IsForeground && FocusId() == "Widget.category.controllers");
                Check(true, "Guide show path restores the last widget control without a click");
                DismissForExternalForeground(peer);
                Check(AppWindow.IsVisible, "obsolete external observation cannot hide the reopened overlay");

                // Main is already inactive when leaving an interactive pin.
                pin.SetInteraction(true); pin.Activate();
                await Task.Delay(100);
                Check(AppWindow.IsVisible, "second own-window handoff stays visible");
                WinUIEx.HwndExtensions.SetForegroundWindow(peer);
                await Until(() => !AppWindow.IsVisible);
                Check(pin.IsVisible && !input.IsActive, "external activation from pin also dismisses only main overlay");
                pin.SetInteraction(false);
                ShowOverlay();
                await Until(() => AppWindow.IsVisible && input.IsForeground && FocusId() == "Widget.category.controllers");
                Check(true, "repeat external dismissal and reopening preserve remembered focus");
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }
            finally { pin?.Dispose(); }

            Control? Find(string id) => Descendants(page).OfType<Control>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id);
            string? FocusId() => FocusManager.GetFocusedElement(page.XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : null;
            async Task Until(Func<bool> predicate)
            {
                var deadline = Environment.TickCount64 + 8000;
                while (!predicate())
                {
                    if (Environment.TickCount64 >= deadline) throw new TimeoutException($"Foreground validation timed out: visible={AppWindow.IsVisible}, foreground={input?.IsForeground}, focus={FocusId()}");
                    await Task.Delay(20);
                }
            }
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, error }));
            }
        };
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
                foreach (var node in Descendants(VisualTreeHelper.GetChild(root, i))) yield return node;
        }
    }
}
