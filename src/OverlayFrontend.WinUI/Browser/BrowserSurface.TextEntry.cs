using System.Text.Json;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private WidgetTextEntryDialog? editDialog;
    private readonly HashSet<ControllerButton> editorOwnedButtons = [];
    internal WidgetTextEntryDialog? ControllerTextEntry => input && interacting && !retired ? editDialog : null;
    private bool editPending;
    private long pageEpoch, editGeneration;
    internal bool HasDialog => editPending || editDialog is not null || libraryDialog is not null;
    // Fixed host scripts. No widget-supplied code, DOM handle or page text crosses IPC.
    // A remote object refers to the exact focused field, including its document.
    private const string FocusedField = "(() => { let e = document.activeElement; while(e?.shadowRoot?.activeElement) e=e.shadowRoot.activeElement; return e; })()";

    private async Task EditAsync(bool addressEntry, AutomaticFieldTarget? automatic = null)
    {
        if (!input || retired || HasDialog || core is not { } current || faulted) return;
        if (automatic is not null && !IsCurrentClick(automatic.Click)) return;
        var generation = ++editGeneration;
        var epoch = pageEpoch;
        CancelPointer();
        var editPointerEpoch = pointerEpoch;
        editPending = true; interacting = browsing = true; wheelX = wheelY = 0; cursorDirty = false;
        DrawCursor(); InteractionChanged?.Invoke();
        string? objectId = automatic?.ObjectId;
        string? editSessionId = null;
        WidgetTextEntryDialog? dialog = null;
        try
        {
            var value = BrowserAddressInput.EditorValue(current.Source);
            var sensitive = false; var rich = false; var multiline = false; var maximum = 2048;
            if (!addressEntry)
            {
                if (objectId is null)
                {
                    using var selected = JsonDocument.Parse(await Call("Runtime.evaluate",
                        JsonSerializer.Serialize(new BrowserEvaluation(FocusedField), BrowserJsonContext.Default.BrowserEvaluation)));
                    if (!selected.RootElement.GetProperty("result").TryGetProperty("objectId", out var reference))
                    { ShowStatus("Select a text field on the page first, then press R3."); return; }
                    objectId = reference.GetString();
                }
                using var started = JsonDocument.Parse(await Call("Runtime.callFunctionOn",
                    JsonSerializer.Serialize(new BrowserFieldCall(objectId!, BrowserFieldEditing.Begin, returnByValue: false), BrowserJsonContext.Default.BrowserFieldCall)));
                if (!started.RootElement.GetProperty("result").TryGetProperty("objectId", out var sessionReference))
                { if (automatic is null) ShowStatus("Select an editable text field on the page first."); return; }
                editSessionId = sessionReference.GetString();
                using var field = JsonDocument.Parse(await Call("Runtime.callFunctionOn",
                    JsonSerializer.Serialize(new BrowserFieldCall(editSessionId!, BrowserFieldEditing.Describe), BrowserJsonContext.Default.BrowserFieldCall)));
                var metadata = field.RootElement.GetProperty("result").GetProperty("value");
                value = metadata.GetProperty("value").GetString()!;
                sensitive = metadata.GetProperty("password").GetBoolean();
                rich = metadata.GetProperty("rich").GetBoolean();
                multiline = metadata.GetProperty("multiline").GetBoolean();
                maximum = metadata.GetProperty("maximum").GetInt32();
                if (maximum < 1) { ShowStatus("This field does not accept text."); return; }
            }
            if (!Current()) return;
            var accepted = false;
            dialog = new(new ViewNode { Id = "browser.edit", Kind = ViewNodeKind.TextEntry, TextEntryValue = value,
                TextEntryInputKind = sensitive ? TextEntryInputKind.Sensitive : TextEntryInputKind.Ordinary,
                TextEntryMaximumLength = maximum, AccessibilityLabel = addressEntry ? "Address or Google search" : sensitive ? "Password" : rich ? "Insert text at the page selection" : "Page text",
                TextEntryPlaceholder = addressEntry ? "Search Google or enter a URL" : sensitive ? "Replace password" : rich ? "Text to insert" : "Selected text field" },
                commit => { accepted = commit; dialog!.Hide(); }, multiline, allowSubmit: !addressEntry, commitLabel: addressEntry ? "Open" : "Apply");
            dialog.BindRoot(XamlRoot);
            dialog.GuideChanged = () => InteractionChanged?.Invoke();
            dialog.ThemeLease = NativePopupTheme.Dialog(dialog, this);
            editDialog = dialog; editPending = false;
            await dialog.ShowAsync();
            if (!accepted || !Current()) return;
            var entered = dialog.TakeValue();
            // Address commits navigate the native controller; no widget snapshot
            // is synthesized and the last authored NavigationId remains applied.
            if (addressEntry)
            {
                if (string.IsNullOrWhiteSpace(entered)) return;
                var destination = BrowserAddressInput.Resolve(entered);
                if (destination is null) { ShowStatus("Enter a valid web address or a shorter Google search."); return; }
                editDialog = null; // NavigationStarting revokes other pending edits.
                toolbarFocused = false;
                current.Navigate(destination);
            }
            else
            {
                using var result = JsonDocument.Parse(await Call("Runtime.callFunctionOn",
                    JsonSerializer.Serialize(new BrowserFieldCall(editSessionId!, BrowserFieldEditing.Prepare, [new(entered)]), BrowserJsonContext.Default.BrowserFieldCall)));
                if (!Current()) return;
                if (!result.RootElement.GetProperty("result").TryGetProperty("value", out var prepared) || prepared.ValueKind != JsonValueKind.True)
                { ShowStatus("The selected field changed or cannot accept this text. Select it again to retry."); return; }
                // Chromium owns text insertion, undo, beforeinput/input events,
                // and the page's rich-editor model. Never assign innerHTML/textContent.
                if (entered.Length > 0)
                    await Call("Input.insertText", JsonSerializer.Serialize(new BrowserInsertText(entered), BrowserJsonContext.Default.BrowserInsertText));
                else if (!rich || value.Length > 0) await Key("Backspace", 8);
                if (dialog.SubmitRequested && Current())
                {
                    using var ready = JsonDocument.Parse(await Call("Runtime.callFunctionOn",
                        JsonSerializer.Serialize(new BrowserFieldCall(editSessionId!, BrowserFieldEditing.Current), BrowserJsonContext.Default.BrowserFieldCall)));
                    if (Current() && ready.RootElement.GetProperty("result").TryGetProperty("value", out var focused) && focused.ValueKind == JsonValueKind.True)
                        await Key("Enter", 13);
                }
                if (Current()) ShowStatus(null);
            }
        }
        catch (Exception) when (!Current()) { /* Cancelled/replaced edits cannot publish a late error. */ }
        finally
        {
            if (ReferenceEquals(editDialog, dialog)) editDialog = null;
            dialog?.Erase();
            if (generation == editGeneration) editPending = false;
            if (input && !retired && !HasDialog &&
                (automatic is null || IsInteractionFocused?.Invoke() == true))
            {
                if (toolbarFocused) (rememberedToolbar ?? addressButton).Focus(FocusState.Keyboard);
                else FocusInteraction?.Invoke();
            }
            DrawCursor(); InteractionChanged?.Invoke();
            if (editSessionId is not null && !retired)
            {
                try { await Call("Runtime.releaseObject", JsonSerializer.Serialize(new BrowserObject(editSessionId), BrowserJsonContext.Default.BrowserObject)); }
                catch (Exception) { /* Document loss already invalidated the bounded edit session. */ }
            }
            if (objectId is not null && automatic is null && !retired)
            {
                try { await Call("Runtime.releaseObject", JsonSerializer.Serialize(new BrowserObject(objectId), BrowserJsonContext.Default.BrowserObject)); }
                catch (Exception) { /* Navigation/process loss already invalidated this exact object. */ }
            }
        }
        bool Current() => !retired && input && ReferenceEquals(core, current) && epoch == pageEpoch && generation == editGeneration &&
            (automatic is null || pointerEpoch == editPointerEpoch && pointerGesture == automatic.Click.Gesture &&
                (dialog is not null || IsInteractionFocused?.Invoke() == true));
        async Task<string> Call(string method, string parameters) => await current.CallDevToolsProtocolMethodAsync(method, parameters)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
        async Task Key(string key, int code)
        {
            try { await Call("Input.dispatchKeyEvent", JsonSerializer.Serialize(new BrowserKeyEvent("keyDown", key, key, code, key == "Enter" ? "\r" : null), BrowserJsonContext.Default.BrowserKeyEvent)); }
            finally { await Call("Input.dispatchKeyEvent", JsonSerializer.Serialize(new BrowserKeyEvent("keyUp", key, key, code), BrowserJsonContext.Default.BrowserKeyEvent)); }
        }
    }
    private void CancelEdit()
    {
        ++editGeneration; editPending = false;
        if (editDialog is not { } dialog) return;
        editDialog = null; dialog.Erase(); dialog.Hide();
    }
}
