using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;
internal sealed partial class OverlayShellPage
{
    private async Task<IReadOnlyList<string>> ValidateNotificationCategoryUiAsync(BridgeWidgetDescriptor descriptor)
    {
        var checks = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), "widgetrail-category-ui-" + Guid.NewGuid().ToString("N"));
        using var center = new NotificationCenter(root);
        notificationCenter = center;
        try
        {
            var preferences = ShowNotificationPreferencesAsync(descriptor, "songs");
            var category = await Dialog("Song changes");
            await FocusedOption(category, 0);
            if (category.CloseButtonText != "Done" || FindList(category).Items.Count != 2) throw new InvalidOperationException("Category should offer banners and opacity with Done.");
            var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
            await Task.Delay(250);
            await CaptureSettingsListAsync(category, Path.Combine(output, "notification-category.png"));
            await Choose(category, 0);
            var enabled = await Dialog("Banners");
            await FocusedOption(enabled, 1); // Initially disabled, so Off is the actual focus target.
            if (FindList(enabled).Items.Count != 2 || enabled.CloseButtonText != "Back") throw new InvalidOperationException("Banner-only category offered unsupported delivery choices.");
            await Task.Delay(350); // Capture settled native entrance animation, not its clipped opening frame.
            await FocusedOption(enabled, 1);
            await CaptureSettingsListAsync(enabled, Path.Combine(output, "notification-banner-toggle.png"));
            await Choose(enabled, 0);
            await Choose(await Dialog("Song changes"), 1);
            var opacity = await Dialog("Banner opacity");
            await FocusedOption(opacity, 0);
            await Choose(opacity, 6); // 70 percent, including scroll into view.
            category = await Dialog("Song changes");
            await FocusedOption(category, 1);
            await Choose(category, 1);
            var restoredOpacity = await Dialog("Banner opacity");
            await FocusedOption(restoredOpacity, 6); // Current 70% must scroll into view and own focus.
            restoredOpacity.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true); restoredOpacity.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            category = await Dialog("Song changes");
            await FocusedOption(category, 1);
            category.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true); category.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            await preferences.WaitAsync(TimeSpan.FromSeconds(3));
            if (center.Preferences.Category(descriptor.Id, "songs") != new NotificationCategoryPreference(NotificationPresentation.BannerAndList, 70))
                throw new InvalidOperationException("Controller category choices did not persist.");
            checks.Add("Shared category dialogs accept controller delivery and opacity choices and close on B");
            var menu = ShowNotificationPreferencesAsync(descriptor);
            var categories = await Dialog("Notification preferences");
            await FocusedOption(categories, 0);
            await Task.Delay(250);
            await CaptureSettingsListAsync(categories, Path.Combine(output, "notification-preferences.png"));
            var list = FindList(categories);
            if (list.Items.Count != 3) throw new InvalidOperationException("Missing master, general or named category.");
            categories.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true); categories.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            await menu.WaitAsync(TimeSpan.FromSeconds(3));
            checks.Add("Tray preferences expose master, General and declared category through the shared dialog");
            var pending = ChooseNotificationModeAsync("Pending example", NotificationPresentation.ListOnly, null);
            var pendingOptions = await Dialog("Notification delivery");
            await FocusedOption(pendingOptions, 1);
            if (FindList(pendingOptions).Items.Count != 3) throw new InvalidOperationException("Pending notices lost a delivery choice.");
            pendingOptions.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true); pendingOptions.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            if (await pending is not null) throw new InvalidOperationException("B changed the saved delivery choice.");
            var confirm = ShowHostChoiceAsync("Synthetic confirmation", "A decision keeps its safe default.",
                (HostDialogChoice[])[new("yes", "Proceed"), new("no", "Keep current")], lifetime.Token);
            var decision = await Dialog("Synthetic confirmation");
            var deadline = Environment.TickCount64 + 3000;
            while (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is not Control control ||
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(control) != "Host.Choice.Close")
            { if (Environment.TickCount64 > deadline) throw new InvalidOperationException("Confirmation lost safe Cancel focus."); await Task.Delay(16); }
            decision.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true); decision.Handle(ControllerButton.B, ControllerEventPhase.Pressed);
            await confirm;
            checks.Add("Preference lists focus options and current values; confirmation dialogs retain Cancel");
            return checks;
        }
        finally { notificationCenter = null; if (Directory.Exists(root)) Directory.Delete(root, true); }
        async Task<HostChoiceDialog> Dialog(string title)
        {
            var end = Environment.TickCount64 + 4000;
            while (hostChoiceDialog is not { IsLoaded: true } current || current.Title?.ToString() != title)
            { if (Environment.TickCount64 > end) throw new TimeoutException("Category dialog did not open: " + title); await Task.Delay(16); }
            return hostChoiceDialog!;
        }
        async Task FocusedOption(HostChoiceDialog dialog, int index)
        {
            var list = FindList(dialog);
            var expected = (ListViewItem)list.Items[index];
            bool Visible()
            {
                var bounds = expected.TransformToVisual(list).TransformBounds(new Windows.Foundation.Rect(0, 0, expected.ActualWidth, expected.ActualHeight));
                return expected.ActualHeight > 0 && bounds.Top >= -1 && bounds.Bottom <= list.ActualHeight + 1;
            }
            var end = Environment.TickCount64 + 3000;
            while (!ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(dialog.XamlRoot), expected) || !Visible())
            { if (Environment.TickCount64 > end) throw new InvalidOperationException($"Expected focus on option {index} in {dialog.Title}"); await Task.Delay(16); }
        }
        async Task Choose(HostChoiceDialog dialog, int index)
        {
            var list = FindList(dialog); list.SelectedIndex = index;
            list.ScrollIntoView(list.Items[index]); await Task.Delay(50);
            ((ListViewItem)list.Items[index]).Focus(FocusState.Keyboard);
            dialog.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true); dialog.Handle(ControllerButton.A, ControllerEventPhase.Pressed);
            var end = Environment.TickCount64 + 4000;
            while (ReferenceEquals(hostChoiceDialog, dialog))
            { if (Environment.TickCount64 > end) throw new TimeoutException("Controller choice did not close dialog"); await Task.Delay(16); }
        }
        static ListView FindList(DependencyObject element)
        {
            if (element is ListView list) return list;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            { try { return FindList(VisualTreeHelper.GetChild(element, i)); } catch (InvalidOperationException) { } }
            throw new InvalidOperationException("No choice list");
        }
    }
}
