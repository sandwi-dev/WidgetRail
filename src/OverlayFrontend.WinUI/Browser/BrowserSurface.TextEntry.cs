using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private WidgetTextEntryDialog? editDialog;
    private readonly HashSet<ControllerButton> editorOwnedButtons = [];
    internal WidgetTextEntryDialog? ControllerTextEntry => AttachedPopup is { } child ? child.ControllerTextEntry : input && interacting && !retired ? editDialog : null;
    private bool editPending;
    private long pageEpoch, editGeneration;
    internal bool HasDialog => AttachedPopup?.HasDialog == true || webDialogPending || editPending || editDialog is not null || libraryDialog is not null;

    // Address entry remains a draft: typing must not navigate after each letter.
    private async Task EditAddressAsync()
    {
        if (!CanEditAddress) return;
        if (!input || retired || HasDialog || core is not { } current || faulted || XamlRoot is null) return;
        var generation = ++editGeneration;
        var epoch = pageEpoch;
        CancelPointer();
        editPending = true; interacting = browsing = true; wheelX = wheelY = 0; cursorDirty = false;
        DrawCursor(); InteractionChanged?.Invoke();
        WidgetTextEntryDialog? dialog = null;
        try
        {
            var accepted = false;
            dialog = new(new ViewNode { Id = "browser.address", Kind = ViewNodeKind.TextEntry,
                TextEntryValue = BrowserAddressInput.EditorValue(current.Source), TextEntryMaximumLength = 2048,
                AccessibilityLabel = "Address or Google search", TextEntryPlaceholder = "Search Google or enter a URL" },
                commit => { accepted = commit; dialog!.Hide(); }, commitLabel: "Open");
            dialog.BindRoot(XamlRoot);
            dialog.GuideChanged = () => InteractionChanged?.Invoke();
            dialog.ThemeLease = NativePopupTheme.Dialog(dialog, this);
            editDialog = dialog; editPending = false;
            await dialog.ShowKeyboardAsync(this);
            if (!accepted || !Current()) return;
            var entered = dialog.TakeValue();
            if (string.IsNullOrWhiteSpace(entered)) return;
            var destination = BrowserAddressInput.Resolve(entered);
            if (destination is null) { ShowStatus("Enter a valid web address or a shorter Google search."); return; }
            editDialog = null;
            toolbarFocused = false;
            await NavigateRequestedAsync(destination);
        }
        catch (Exception) when (!Current()) { }
        finally
        {
            if (ReferenceEquals(editDialog, dialog)) editDialog = null;
            dialog?.Erase();
            if (generation == editGeneration) editPending = false;
            if (input && !retired && !HasDialog)
            {
                if (toolbarFocused) (rememberedToolbar ?? addressButton).Focus(FocusState.Keyboard);
                else FocusInteraction?.Invoke();
            }
            DrawCursor(); InteractionChanged?.Invoke();
        }
        bool Current() => !retired && input && ReferenceEquals(core, current) && epoch == pageEpoch && generation == editGeneration;
    }
    private void CancelEdit()
    {
        ++editGeneration; editPending = false;
        if (editDialog is not { } dialog) return;
        // Retain the dialog boundary until ShowAsync/finally retires the popup.
        // A new click must not start an overlapping edit session.
        dialog.Erase(); dialog.Hide();
    }
}
