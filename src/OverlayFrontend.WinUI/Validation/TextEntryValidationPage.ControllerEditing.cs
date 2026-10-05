using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Shapes;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class TextEntryValidationPage
{
    private async Task VerifyControllerEditingAsync()
    {
        foreach (var secret in new[] { false, true })
        {
            sensitive = secret; value = secret ? "" : "abcd"; Apply(); await Open();
            var dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
            if (secret) ((PasswordBox)Editor()).Password = "abcd";
            await Task.Delay(100);
            if (secret)
            {
                var editor = (PasswordBox)Editor();
                Check(!dialog.PasswordVisible && editor.PasswordRevealMode == PasswordRevealMode.Hidden, "password starts hidden for each edit session");
                await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
                Check(dialog.PasswordVisible && editor.PasswordRevealMode == PasswordRevealMode.Visible, "R3 explicitly reveals the password");
                var displayPeer = FrameworkElementAutomationPeer.CreatePeerForElement(FindDialog("Widget.TextEntry.ProtectedDisplay")!);
                Check(displayPeer.IsPassword() && displayPeer.GetPattern(PatternInterface.Value) is null && displayPeer.GetPattern(PatternInterface.Text) is null,
                    "revealed controller display exposes neither text nor value accessibility patterns");
                await presenter.HandleControllerButtonAsync(ControllerButton.RightStick, ControllerEventPhase.Repeated);
                Check(dialog.PasswordVisible, "holding reveal never toggles it repeatedly");
                await presenter.HandleControllerButtonAsync(ControllerButton.RightStick);
                Check(!dialog.PasswordVisible && editor.PasswordRevealMode == PasswordRevealMode.Hidden, "R3 hides the password again");
            }
            var caret = (Rectangle)FindDialog("Widget.TextEntry.Caret")!;
            Check(caret.Visibility == Visibility.Visible && caret.ActualWidth >= 2 && caret.ActualHeight > 0,
                secret ? "masked controller entry shows a caret without exposing its value" : "ordinary entry shows a caret while virtual keys own focus");
            await presenter.HandleControllerButtonAsync(ControllerButton.X);
            dialog.SampleHeldButtons(0x4000, 0x4000, 0);
            dialog.SampleHeldButtons(0x4000, 0, 399);
            Check(dialog.TakeValue().Length == 3, "held deletion waits for the original keyboard delay");
            dialog.SampleHeldButtons(0x4000, 0, 400);
            dialog.SampleHeldButtons(0x4000, 0, 490);
            Check(dialog.TakeValue().Length == 1, "held deletion repeats at keyboard cadence");
            dialog.SampleHeldButtons(0, 0, 500);
            dialog.SampleHeldButtons(0x4000, 0, 1000);
            Check(dialog.TakeValue().Length == 1, "release prevents repeats without a fresh press");
            await presenter.HandleControllerButtonAsync(ControllerButton.Y);
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            dialog.SampleHeldButtons(0x1000, 0x1000, 1100);
            dialog.SampleHeldButtons(0x1000, 0, 1500);
            Check(dialog.TakeValue() == "qq", "held character activation types the selected character");
            presenter.MoveFocus(FocusNavigationDirection.Right);
            dialog.SampleHeldButtons(0x1000, 0, 1600);
            Check(dialog.TakeValue() == "qq", "moving focus cancels character repeat without typing a different key");
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper);
            dialog.SampleHeldButtons(0x100, 0x100, 1700);
            dialog.SampleHeldButtons(0x100, 0, 2100);
            dialog.SampleHeldButtons(0, 0, 2200);
            await presenter.HandleControllerButtonAsync(ControllerButton.A);
            Check(dialog.TakeValue() == "wqq", "held bumper moves the caret through the draft before insertion");
            await Task.Delay(100);
            Check(caret.Visibility == Visibility.Visible, "controller edits keep caret feedback visible");
            if (!secret)
            {
                var editor = (TextBox)Editor();
                Check(dialog.HandleKeyboardKey(Windows.System.VirtualKey.Right), "physical Right arrow moves the caret from a virtual key");
                Check(editor.SelectionStart == 2, "physical arrow updates the actual native insertion position");
                editor.Focus(FocusState.Keyboard);
                await Task.Delay(50);
                Check(caret.Visibility == Visibility.Collapsed, "native editor focus hides the supplemental controller caret");
            }
            else
            {
                var editor = (PasswordBox)Editor();
                editor.Focus(FocusState.Keyboard);
                await Task.Delay(50);
                Check(editor.Opacity == 1 && caret.Visibility == Visibility.Collapsed, "native protected input retains its own focused presentation");
                await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper);
                await presenter.HandleControllerButtonAsync(ControllerButton.A);
                Check(dialog.TakeValue() == "wqwq", "returning from native password input visibly starts controller movement from the end");
            }
            var count = actions.Count;
            await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();
            dialog.SampleHeldButtons(0x4000, 0x4000, 3000);
            dialog.SampleHeldButtons(0x4000, 0, 4000);
            Check(dialog.TakeValue().Length == 0 && actions.Count == count, "closed keyboard clears values and ignores late held input");
        }
        sensitive = false; maximumLength = 256; value = new string('W', 128); Apply(); await Open();
        var longEditor = (TextBox)Editor();
        await Task.Delay(100);
        var longCaret = (Rectangle)FindDialog("Widget.TextEntry.Caret")!;
        var scroll = DescendantScroll(longEditor)!;
        Check(scroll.HorizontalOffset > 0 && longCaret.Visibility == Visibility.Visible,
            "controller caret scrolls long text to the insertion point without focusing the editor");
        var caretBounds = longCaret.TransformToVisual(longEditor).TransformBounds(new(0, 0, longCaret.ActualWidth, longCaret.ActualHeight));
        Check(caretBounds.Left >= 0 && caretBounds.Right <= longEditor.ActualWidth && caretBounds.Top >= 0 && caretBounds.Bottom <= longEditor.ActualHeight,
            "long-text controller caret stays inside the native editor viewport");
        await presenter.HandleControllerButtonAsync(ControllerButton.Y);
        await Task.Delay(100);
        Check(longCaret.Visibility == Visibility.Visible && longCaret.ActualHeight > 0, "empty text keeps a visible controller insertion point");
        await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();
        maximumLength = 4; value = "";

        static ScrollViewer? DescendantScroll(DependencyObject root)
        {
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); ++index)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index);
                if (child is ScrollViewer found) return found;
                if (DescendantScroll(child) is { } nested) return nested;
            }
            return null;
        }
    }
}
