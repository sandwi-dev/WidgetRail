using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Exercises real native dialog focus/controller handling using synthetic rows only.</summary>
internal static class WebCapturePickerValidation
{
    internal static async Task RunAsync(FrameworkElement origin, List<string> checks)
    {
        var theme = new NativePopupTheme();
        theme.Attach(origin);
        theme.Update(null, WidgetRail.PlatformSettings.AppearanceSettings.Default);
        var dialog = new HostChoiceDialog("Synthetic share chooser", "No applications are captured by this test.",
            (HostDialogChoice[])[new("one", "Synthetic application one"), new("two", "Synthetic application two")]) { XamlRoot = origin.XamlRoot };
        var quality = dialog.AddSelectionOption("Quality", "Host.Capture.Quality",
            (HostChoiceSelection[])[new("480p15", "480p · 15 FPS"),new("480p30", "480p · 30 FPS"),new("720p15", "720p · 15 FPS"),new("720p30", "720p · 30 FPS")], "720p30");
        var content=dialog.AddSelectionOption("Content","Host.Capture.Content",
            (HostChoiceSelection[])[new("motion","Prefer smoothness"),new("detail","Prefer clarity")],"motion");
        var audio = dialog.AddBooleanOption("Share application audio", "Host.Capture.ApplicationAudio", beforeChoices: true);
        dialog.AddRememberChoice("Remember synthetic choice");
        dialog.ConfigurePreferenceList(closeLabel: "Cancel");
        using var decoration = NativePopupTheme.Dialog(dialog, origin);
        using (var nestedTheme = NativePopupTheme.Menu(new MenuFlyout(), dialog))
            Check(nestedTheme is not null, "A nested native chooser menu inherits its dialog theme authority");
        var shown = dialog.ShowAsync().AsTask();
        try
        {
            await Until(() => dialog.IsOpenForValidation && IsFocused("Host.Choice.Option.0"));
            Check(audio.IsChecked == false, "Native source chooser initially focuses first application with audio off");
            dialog.MoveFocus(FocusNavigationDirection.Up);
            await Until(() => IsFocused("Host.Capture.ApplicationAudio"));
            Press(ControllerButton.A);
            Check(audio.IsChecked == true && !shown.IsCompleted && dialog.SelectedId is null,
                "Controller Up reaches audio and A toggles it without confirming capture");
            Press(ControllerButton.A);
            Check(audio.IsChecked == false, "Controller A can turn application audio back off");
            dialog.MoveFocus(FocusNavigationDirection.Up);
            await Until(() => IsFocused("Host.Capture.Content"));
            Press(ControllerButton.A);
            await Until(() => IsFocused("Host.Capture.Content.motion"));
            Check(content()=="motion"&&!shown.IsCompleted,"Content menu reflects the preferred smoothness setting without confirming capture");
            dialog.MoveFocus(FocusNavigationDirection.Down);
            await Until(() => IsFocused("Host.Capture.Content.detail"));
            Press(ControllerButton.A);
            await Until(() => IsFocused("Host.Capture.Content"));
            Check(content()=="detail"&&!shown.IsCompleted&&dialog.SelectedId is null,"Controller can select clarity independently of quality/audio/source");
            dialog.MoveFocus(FocusNavigationDirection.Up);
            await Until(() => IsFocused("Host.Capture.Quality"));
            Press(ControllerButton.A);
            await Until(() => IsFocused("Host.Capture.Quality.720p30"));
            Check(quality() == "720p30" && !shown.IsCompleted, "Quality menu focuses the supplied preset without starting capture");
            dialog.MoveFocus(FocusNavigationDirection.Up);
            await Until(() => IsFocused("Host.Capture.Quality.720p15"));
            Press(ControllerButton.A);
            await Until(() => IsFocused("Host.Capture.Quality"));
            Check(quality() == "720p15" && !shown.IsCompleted && dialog.SelectedId is null, "Controller selects stream quality without confirming an application");
            Press(ControllerButton.A);
            await Until(() => IsFocused("Host.Capture.Quality.720p15"));
            Press(ControllerButton.B);
            await Until(() => IsFocused("Host.Capture.Quality"));
            Check(quality() == "720p15" && !shown.IsCompleted, "B closes only the quality menu and preserves the source chooser");
            dialog.MoveFocus(FocusNavigationDirection.Down);
            await Until(() => IsFocused("Host.Capture.Content"));
            dialog.MoveFocus(FocusNavigationDirection.Down);
            await Until(() => IsFocused("Host.Capture.ApplicationAudio"));
            dialog.MoveFocus(FocusNavigationDirection.Down);
            await Until(() => IsFocused("Host.Choice.Option.0"));
            Check(true, "Controller Down returns from audio to the first application");
            var remember = Descendants(dialog).OfType<CheckBox>().Single(control => AutomationProperties.GetAutomationId(control) == "Host.Choice.Remember");
            remember.Focus(FocusState.Keyboard);
            Press(ControllerButton.A);
            Check(dialog.RememberChoice && !shown.IsCompleted, "Existing Remember choice still toggles through controller A without closing");
            dialog.FocusFirstChoice();
            await Until(() => IsFocused("Host.Choice.Option.0"));
            Press(ControllerButton.A);
            await shown.WaitAsync(TimeSpan.FromSeconds(5));
            Check(dialog.SelectedId == "one", "Explicit controller A on a synthetic application confirms exactly that choice");
        }
        finally { if (!shown.IsCompleted) { dialog.Hide(); await shown.WaitAsync(TimeSpan.FromSeconds(5)); } }

        void Press(ControllerButton button)
        {
            dialog.ControllerAdmission.Observe(new() { Connected = 1 }, ownsInput: true);
            dialog.Handle(button, ControllerEventPhase.Pressed);
            dialog.Handle(button, ControllerEventPhase.Released);
        }
        bool IsFocused(string id)
        {
            for (var current = FocusManager.GetFocusedElement(origin.XamlRoot) as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
                if (AutomationProperties.GetAutomationId(current) == id) return true;
            return false;
        }
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    }
    private static async Task Until(Func<bool> ready)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!ready()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Synthetic capture picker focus did not settle."); await Task.Delay(25); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
