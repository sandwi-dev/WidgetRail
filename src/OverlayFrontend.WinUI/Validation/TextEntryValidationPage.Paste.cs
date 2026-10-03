using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using Windows.ApplicationModel.DataTransfer;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class TextEntryValidationPage
{
    private async Task VerifyPasteAsync()
    {
        // Preserve every advertised format in memory before changing the clipboard.
        // Neither the prior contents nor the pasted values enter diagnostic artifacts.
        var original = Clipboard.GetContent();
        var backup = new DataPackage();
        foreach (var format in original.AvailableFormats) backup.SetData(format, await original.GetDataAsync(format));
        var options = new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false };
        var fixture = new DataPackage(); fixture.SetText("paste-fixture");
        if (!Clipboard.SetContentWithOptions(fixture, options)) throw new InvalidOperationException("Fixture clipboard could not be set.");
        try
        {
            foreach (var secret in new[] { false, true })
            {
                sensitive = secret; value = ""; Apply(); await Open();
                var dialog = (WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
                Check(dialog.HandleKeyboardKey(Windows.System.VirtualKey.V, control: true), "Paste shortcut is accepted from the controller keyboard");
                var deadline = Environment.TickCount64 + 3000;
                while (dialog.TakeValue() != "past" && Environment.TickCount64 < deadline) await Task.Delay(25);
                Check(dialog.TakeValue() == "past", secret ? "Native sensitive paste respects MaxLength" : "Native text paste respects MaxLength");
                Check(!dialog.HandleKeyboardKey(Windows.System.VirtualKey.V, control: true), "Focused editor retains its native paste shortcut");
                if (Editor() is PasswordBox password)
                {
                    password.Password = "";
                    password.PasteFromClipboard();
                    deadline = Environment.TickCount64 + 3000;
                    while (password.Password != "past" && Environment.TickCount64 < deadline) await Task.Delay(25);
                    Check(password.Password == "past", "Sensitive native paste event is not blocked");
                }
                var count = actions.Count;
                await presenter.HandleControllerButtonAsync(ControllerButton.B); await Closed();
                Check(dialog.TakeValue().Length == 0 && actions.Count == count, "Cancelling pasted input clears the editor without committing");
            }
        }
        finally
        {
            presenter.DismissTransientControl();
            if (!Clipboard.SetContentWithOptions(backup, options)) throw new InvalidOperationException("Clipboard restoration failed.");
        }
    }
}
