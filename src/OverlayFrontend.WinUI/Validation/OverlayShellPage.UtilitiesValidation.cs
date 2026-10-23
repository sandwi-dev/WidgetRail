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
        { if (Environment.TickCount64 >= deadline) throw new TimeoutException($"Reopened utility did not receive focus: panel={utilityPanel is not null} pending={utilityPanel?.HasPendingFocus} icon={UtilityIconFocused} radial={RadialOpen} interactive={interactive} switching={switching} foreground={foreground} focused={FocusManager.GetFocusedElement(XamlRoot)?.GetType().Name}."); await Task.Delay(20); }
        if (shellGestures.Owns(ControllerButton.A)) throw new InvalidOperationException("Released A remained owned after handoff and reentry.");
        utilityPanel!.Move(1); // The first row opens Windows Task View; exercise an app row here.
        while (utilityPanel.HasPendingFocus || utilityPanel.FocusedId is null || SystemTaskViewFocused)
        { if (Environment.TickCount64 >= deadline) throw new TimeoutException("App row focus did not settle after reentry."); await Task.Delay(20); }
        var activation = HostTaskActivationRequested;
        var dispatched = 0;
        try
        {
            HostTaskActivationRequested = _ => { dispatched++; return Task.FromResult(new Input.HostTaskActivationOutcome(WidgetRail.WindowsWindowActivation.TaskWindowActivationResult.Requested)); };
            Receive(new() { Connected = 1, State = new() { Buttons = 0x1000 }, PressedButtons = 0x1000 });
            Receive(new() { Connected = 1, ReleasedButtons = 0x1000 });
            if (dispatched != 1) throw new InvalidOperationException("The first A press after reentry did not dispatch exactly once.");
        }
        finally { HostTaskActivationRequested = activation; }
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
                shellGestures.Claim(ControllerButton.A);
                Receive(new() { Connected = 1 });
                Check(!shellGestures.Owns(ControllerButton.A), "Neutral controller state clears a missed A release before tray navigation");
                FocusDirectionalTray();
                await Until(() => FocusedTrayWidget() is not null);
                Receive(new() { Connected = 1, State = new() { Buttons = 0x8000 }, PressedButtons = 0x8000 });
                Check(!reordering && trayHold.Capturing, "Y press waits for tap or hold before changing mode");
                Receive(new() { Connected = 1, ReleasedButtons = 0x8000 });
                Check(reordering, "Physical Y release enters reorder after a short tap");
                Receive(new() { Connected = 1, ReleasedButtons = 0x8000 });
                Check(reordering, "Duplicate Y release cannot toggle reorder again");
                Receive(new() { Connected = 1, State = new() { Buttons = 0x8000 }, PressedButtons = 0x8000 });
                Receive(new() { Connected = 1, ReleasedButtons = 0x8000 });
                Check(!reordering, "A second physical Y tap exits reorder");
                Receive(new() { Connected = 1, State = new() { Buttons = 0x8000 }, PressedButtons = 0x8000 });
                Receive(new() { Connected = 1 });
                Check(!reordering && !trayHold.Capturing && !shellGestures.Owns(ControllerButton.Y), "Missing release recovery cancels Y without inventing a tap");
                FocusUtility(ShellUtility.Tasks);
                await Until(() => utilityPanel is not null && utilityWindows.Count > 0);
                Check(UtilityIconFocused && !UtilityListFocused, "Focusing Task Switcher opens its panel without entering it");
                var panel = utilityPanel;
                NavigateTray(FocusNavigationDirection.Up);
                await Task.Delay(50);
                Check(UtilityListFocused, "Up enters Task Switcher without A");
                Check(SystemTaskViewFocused, "Windows Task View is the first focusable entry");
                var taskRows = FindControl<ListView>(panel!, "Overlay.Utility.List")!;
                Check(!taskRows.Items.Cast<ListViewItem>().Select(row => (ShellUtilityRowContent)row.Content).Any(row => row.HintShown),
                    "Windows Task View has no row close hint");
                var taskView = SystemTaskViewRequested;
                var requested = 0;
                try
                {
                    SystemTaskViewRequested = () => { requested++; return Task.FromResult(Input.WindowsTaskViewResult.KeyboardBusy); };
                    await Button(ControllerButton.X);
                    Check(requested == 0 && SystemTaskViewFocused, "X cannot close the Windows Task View action");
                    await Button(ControllerButton.A);
                    Check(requested == 1 && panel!.ContainsFocus, "A requests Windows Task View once and a rejected request retains list focus");
                    taskStatus = null;
                }
                finally { SystemTaskViewRequested = taskView; }
                // Programmatic can retain WinUI's last Pointer state, which is a
                // legitimate mouse transfer. Exercise keyboard/controller restoration.
                taskSwitcherButton.Focus(FocusState.Keyboard);
                await Until(() => utilityPanel?.ContainsFocus == true);
                Check(UtilityListFocused, "Late native icon focus cannot undo the user's entry into the list");
                ((Control)Tray.ContainerFromIndex(0)).Focus(FocusState.Programmatic);
                await Until(() => utilityPanel?.ContainsFocus == true);
                Check(ReferenceEquals(panel, utilityPanel), "Late tray focus cannot close or recreate an entered utility panel");
                var selected = panel!.FocusedId;
                await RefreshUtilityTasksAsync(panel, lifetime.Token);
                Check(panel.FocusedId == selected, "Window refresh preserves focused identity");
                panel.Move(1);
                await Until(() => panel.FocusedDismissLabel == "Close" &&
                    taskRows.Items.Cast<ListViewItem>().Any(row => ((ShellUtilityRowContent)row.Content).HintShown));
                Check(taskRows.Items.Cast<ListViewItem>().Count(row => ((ShellUtilityRowContent)row.Content).HintShown) == 1,
                    "Only the focused application row shows the close hint");
                var appRow = taskRows.Items.Cast<ListViewItem>().Single(row => (string)row.Tag == panel.FocusedId);
                var appContent = appRow.Content;
                await RefreshUtilityTasksAsync(panel, lifetime.Token);
                Check(ReferenceEquals(appContent, appRow.Content) && ((ShellUtilityRowContent)appContent).HintShown,
                    "Window refresh preserves the row and its focused close hint");
                panel.Busy = true;
                Check(!((ShellUtilityRowContent)appContent).HintShown, "Busy rows hide the unavailable close hint");
                panel.Busy = false;
                var playStation = Presentation.WidgetControllerPrompts.PlayStation;
                try
                {
                    Presentation.WidgetControllerPrompts.Set(WidgetRail.OverlayPlatformClient.ControllerFamily.PlayStation);
                    Check(AutomationProperties.GetHelpText(appRow) == "Square button: Close", "Close hint follows PlayStation controller prompts");
                    Presentation.WidgetControllerPrompts.Set(WidgetRail.OverlayPlatformClient.ControllerFamily.Xbox);
                    Check(AutomationProperties.GetHelpText(appRow) == "X button: Close", "Close hint follows Xbox controller prompts");
                }
                finally { Presentation.WidgetControllerPrompts.Set(playStation ? WidgetRail.OverlayPlatformClient.ControllerFamily.PlayStation : WidgetRail.OverlayPlatformClient.ControllerFamily.Xbox); }
                await Button(ControllerButton.B);
                await Task.Delay(100);
                Check(!taskRows.Items.Cast<ListViewItem>().Any(row => ((ShellUtilityRowContent)row.Content).HintShown),
                    "Returning to the icon removes the row hint");
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
                var notificationRows = FindControl<ListView>(utilityPanel!, "Overlay.Utility.List")!;
                Check(notificationRows.Items.Cast<ListViewItem>().Count(row => ((ShellUtilityRowContent)row.Content).HintShown) == 1 &&
                    utilityPanel!.FocusedDismissLabel == "Dismiss", "Focused notification shows a dismiss hint");
                await Button(ControllerButton.B);
                Check(notificationBell.FocusState != FocusState.Unfocused && !RadialOpen, "Notification Back returns to bell");
                await Button(ControllerButton.B);
                await Task.Delay(100);
                Check(RadialOpen && utilityPanel is null, "Back from utility icon preserves existing radial behavior");
                Receive(new() { Connected = 1, State = new() { Buttons = 0x8000 }, PressedButtons = 0x8000 });
                Receive(new() { Connected = 1, ReleasedButtons = 0x8000 });
                Check(reordering && RadialOpen, "Physical Y tap also enters reorder in the radial chooser");
                Receive(new() { Connected = 1, State = new() { Buttons = 0x8000 }, PressedButtons = 0x8000 });
                Receive(new() { Connected = 1, ReleasedButtons = 0x8000 });
                Check(!reordering && RadialOpen, "Physical Y tap exits radial reorder");
                await Until(() => trayGuide.HelpText.Contains("hold for Notifications", StringComparison.Ordinal));
                Check(true, "Pending notifications update the radial guide destination");
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Pressed);
                await RunTrayHoldAsync(trayHold.Tick(TrayGestureIdentity, true, Environment.TickCount64 + TrayHoldGesture.HoldMilliseconds));
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Released);
                await Task.Delay(50);
                await Until(() => utilityPanel?.FocusedId == entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Check(shownUtility == ShellUtility.Notifications && UtilityListFocused && !RadialOpen && !reordering,
                    "Hold Y enters the first pending notification without an extra Up or A");
                inbox.Dismiss(entry.Id);
                await Task.Delay(50);
                Check(shownUtility == ShellUtility.Notifications, "Dismissing the last notification does not switch the active panel");
                // Removing the last row can already return focus to the bell.
                if (UtilityListFocused) await Button(ControllerButton.B);
                await Button(ControllerButton.B);
                await Until(() => RadialOpen);
                await Until(() => trayGuide.HelpText.Contains("hold for Task Switcher", StringComparison.Ordinal));
                Check(true, "Empty inbox updates the guide to Task Switcher");
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Pressed);
                await RunTrayHoldAsync(trayHold.Tick(TrayGestureIdentity, true, Environment.TickCount64 + TrayHoldGesture.HoldMilliseconds));
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Released);
                await Until(() => SystemTaskViewFocused);
                Check(shownUtility == ShellUtility.Tasks && UtilityListFocused && !RadialOpen && !reordering,
                    "Hold Y with no notifications focuses the first Task Switcher row and release does not reorder");
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
                var liveEntry = inbox.Add("settings", "fixture", "https://fixture.invalid", "guide-live", "Guide change",
                    "Disposable notification", this, new(() => true, () => { }, () => { }, () => { }, () => { }), NotificationPresentation.ListOnly)!;
                await Until(() => trayGuide.HelpText.Contains("hold for Notifications", StringComparison.Ordinal));
                Check(FocusedTrayWidget() is not null, "Notification arrival updates the tray guide without stealing focus");
                inbox.Dismiss(liveEntry.Id);
                await Until(() => trayGuide.HelpText.Contains("hold for Task Switcher", StringComparison.Ordinal));
                Check(FocusedTrayWidget() is not null, "Notification dismissal updates the tray guide without stealing focus");
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
