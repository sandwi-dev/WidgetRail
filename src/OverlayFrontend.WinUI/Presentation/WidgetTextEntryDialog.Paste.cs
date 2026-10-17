using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetTextEntryDialog
{
    private bool pastePending;

    private async Task PasteAsync()
    {
        if (completed || pastePending) return;
        repeat.Reset();
        // The browser retains ownership of clipboard access and paste events.
        if (IsLive) { SendLive(new(KeyboardEditKind.Key, Key: "v", KeyCode: 86, Modifiers: 2)); return; }
        pastePending = true;
        try
        {
            if (text is not null) { text.PasteFromClipboard(); return; }
            // PasswordBox exposes no selection API. Paste at our visible controller
            // caret without moving focus or exposing the secret through automation.
            var before = password!.Password;
            var caret = secretCaret;
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            var inserted = await content.GetTextAsync();
            if (completed || password.Password != before || secretCaret != caret) return;
            var edit = TextEntryEditing.Paste(before, caret, 0, inserted, maximumLength);
            password.Password = edit.Value; secretCaret = edit.Caret;
            RefreshCaret();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (completed) return;
            heading.Text = "Clipboard unavailable. Try Paste again.";
            heading.Visibility = Visibility.Visible;
        }
        finally { pastePending = false; }
    }
}
