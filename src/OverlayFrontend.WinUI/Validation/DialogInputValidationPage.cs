using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

// External native-input tests click real rows. F6 routes the controller A path;
// it is deliberately distinct from InvokePattern and native keyboard activation.
internal sealed partial class DialogInputValidationPage : Page
{
    private readonly TextBlock status = new() { Text = "Ready" };
    private readonly NativePopupTheme theme = new();
    private ContentDialog? active;
    internal DialogInputValidationPage()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(32) };
        Content = panel;
        theme.Attach(panel);
        theme.Update(null, WidgetRail.PlatformSettings.AppearanceSettings.Default);
        AutomationProperties.SetAutomationId(status, "DialogInput.Status");
        panel.Children.Add(status);
        foreach (var kind in new[] { "choices", "grouped", "library", "decision" })
        {
            var button = new Button { Content = "Open " + kind };
            AutomationProperties.SetAutomationId(button, "DialogInput.Open." + kind);
            button.Click += async (_, _) => await OpenAsync(kind, panel);
            panel.Children.Add(button);
        }
    }

    private async Task OpenAsync(string kind, FrameworkElement origin)
    {
        if (active is not null) return;
        status.Text = "Open: " + kind;
        ContentDialog dialog;
        if (kind == "library")
            dialog = new BrowserLibraryDialog(true, (BrowserLibraryEntry[])[new("https://one.invalid", "First page", DateTimeOffset.UtcNow), new("https://two.invalid", "Second page", DateTimeOffset.UtcNow)]);
        else
        {
            var choice = new HostChoiceDialog("Input fixture: " + kind, "Synthetic options; no settings or extensions are changed.",
                (HostDialogChoice[])[new("one", "First option", Group: kind == "grouped" ? "First group" : null), new("two", "Second option", Group: kind == "grouped" ? "Second group" : null)], directDecisionButtons: kind == "decision");
            if (kind != "decision") choice.ConfigurePreferenceList();
            if (kind == "grouped") choice.SetChoiceContent(entry =>
            {
                var row = new Grid();
                row.Children.Add(new TextBlock { Text = entry.Label });
                return row;
            });
            dialog = choice;
        }
        active = dialog;
        dialog.XamlRoot = XamlRoot;
        var popupRoots = new List<UIElement>();
        KeyEventHandler key = (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.F6) return;
            args.Handled = true;
            if (dialog is HostChoiceDialog choice) { choice.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true); choice.Handle(ControllerButton.A, ControllerEventPhase.Pressed); }
            else ((BrowserLibraryDialog)dialog).Handle(ControllerButton.A, ControllerEventPhase.Pressed);
        };
        dialog.Opened += (_, _) =>
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
                if (popup.Child is { } root) { root.AddHandler(UIElement.KeyDownEvent, key, true); popupRoots.Add(root); }
        };
        using var decoration = NativePopupTheme.Dialog(dialog, origin);
        try
        {
            await dialog.ShowAsync();
            status.Text = kind + ": " + (dialog is HostChoiceDialog choice ? choice.SelectedId : ((BrowserLibraryDialog)dialog).SelectedUrl ?? "cancel");
        }
        finally
        {
            foreach (var root in popupRoots) root.RemoveHandler(UIElement.KeyDownEvent, key);
            active = null;
        }
    }
}
