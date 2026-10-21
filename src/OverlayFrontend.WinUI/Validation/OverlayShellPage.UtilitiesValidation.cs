using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Notifications;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using WidgetRail.PlatformSettings;
using CommunityToolkit.Mvvm.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal async Task VerifyUtilityReentryAsync()
    {
        var deadline = Environment.TickCount64 + 5000;
        while (switching)
        { if (Environment.TickCount64 >= deadline) throw new TimeoutException("Reentry did not settle."); await Task.Delay(20); }
        FocusUtility(ShellUtility.Tasks);
        while (utilityWindows.Count == 0 || utilityPanel?.IsLoaded != true)
        { if (Environment.TickCount64 >= deadline) throw new TimeoutException("Reopened utility did not load."); await Task.Delay(20); }
        NavigateTray(FocusNavigationDirection.Up);
        while (utilityPanel?.ContainsFocus != true)
        { if (Environment.TickCount64 >= deadline) throw new TimeoutException("Reopened utility did not receive focus."); await Task.Delay(20); }
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
        if (!UtilityIconFocused || RadialOpen) throw new InvalidOperationException("Reentry Back did not return to utility icon.");
    }
    internal void EnableHostUtilitiesValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                await Task.Delay(100);
                if (startup is not null) await startup;
                await Until(() => !switching && browserOwner is not null && taskSwitcherButton.IsLoaded);
                FocusUtility(ShellUtility.Tasks);
                await Until(() => utilityPanel is not null && utilityWindows.Count > 0);
                Check(UtilityIconFocused && !UtilityListFocused, "Focusing Task Switcher opens its panel without entering it");
                var panel = utilityPanel;
                NavigateTray(FocusNavigationDirection.Up);
                await Task.Delay(50);
                Check(UtilityListFocused, "Up enters Task Switcher without A");
                var selected = panel!.FocusedId;
                await RefreshUtilityTasksAsync(panel, lifetime.Token);
                Check(panel.FocusedId == selected, "Window refresh preserves focused identity");
                await Button(ControllerButton.B);
                await Task.Delay(100);
                Check(ReferenceEquals(panel, utilityPanel) && UtilityIconFocused && !UtilityListFocused && !RadialOpen,
                    "Back returns to the icon without opening the wheel or entering the panel again");
                NavigateTray(FocusNavigationDirection.Left);
                Check(shownUtility == ShellUtility.Notifications && notificationBell.FocusState != FocusState.Unfocused,
                    "Left moves between utility icons and opens Notifications automatically");
                var inbox = browserOwner!.Notifications;
                var entry = inbox.Add("settings", "fixture", "https://fixture.invalid", "utilities", "Fixture notification",
                    "Disposable local navigation fixture", this, new(() => true, () => { }, () => { }, () => { }, () => { }), NotificationPresentation.ListOnly)!;
                RefreshUtilityNotifications();
                NavigateTray(FocusNavigationDirection.Up);
                await Until(() => utilityPanel?.ContainsFocus == true);
                Check(UtilityListFocused, "Up enters notification rows");
                await Button(ControllerButton.B);
                Check(notificationBell.FocusState != FocusState.Unfocused && !RadialOpen, "Notification Back returns to bell");
                await Button(ControllerButton.B);
                await Task.Delay(100);
                Check(RadialOpen && utilityPanel is null, "Back from utility icon preserves existing radial behavior");
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Pressed);
                await RunTrayHoldAsync(trayHold.Tick(TrayGestureIdentity, true, Environment.TickCount64 + TrayHoldGesture.HoldMilliseconds));
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Released);
                await Task.Delay(50);
                Check(shownUtility == ShellUtility.Tasks && UtilityIconFocused && !RadialOpen && !reordering,
                    "Hold Y focuses Task Switcher and release does not reorder or trigger a second action");
                inbox.Dismiss(entry.Id);
                Check(utilityPanel!.ActualWidth > 0 && utilityPanel.ActualHeight > 0 && utilityPanel.ActualWidth <= 521,
                    "Utility panel stays compact and measured");
                var original = Appearance;
                try
                {
                    ApplyOverlayMotionValidationAppearance(original with { TextScale = 1.5, Contrast = ContrastPreference.High });
                    await Task.Delay(200);
                    Check(utilityPanel is { ActualHeight: > 0 } && utilityPanel.ActualHeight <= ShellOverlayLayer.ActualHeight &&
                        ShellChromePalette.Resolve(ShellPalette, Appearance).HighContrast, "Large text and high contrast retain a bounded readable surface");
                }
                finally { ApplyOverlayMotionValidationAppearance(original); }
                CloseUtilityPanel(); FocusDirectionalTray();
                await Until(() => FocusedTrayWidget() is not null);
                await ShowTrayMenuAsync(FocusedTrayWidget()!);
                var taskCommand = trayMenu!.Items.Single(item => AutomationProperties.GetAutomationId(item) == "Overlay.TrayCommand.Shell.TaskSwitcher");
                Check(taskCommand.Text == "Task Switcher" && trayMenu.Items.Any(item => AutomationProperties.GetAutomationId(item) == "Overlay.TrayCommand.Browser.Notifications"),
                    "Tray menu exposes consistently named Task Switcher beside Notifications");
                await ((IAsyncRelayCommand)taskCommand.Command).ExecuteAsync(null);
                await Task.Delay(150);
                Check(UtilityIconFocused && shownUtility == ShellUtility.Tasks && trayMenu is null,
                    "Tray command opens Task Switcher without late menu focus stealing it");
            }
            catch (Exception failure) { error = failure.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed = error is null, checks, error }));
            void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); checks.Add(text); }
            async Task Button(ControllerButton button)
            { await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
            async Task Until(Func<bool> condition)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!condition()) { if (Environment.TickCount64 >= deadline) throw new TimeoutException("Utility fixture did not settle."); await Task.Delay(50); }
            }
        };
    }
}
