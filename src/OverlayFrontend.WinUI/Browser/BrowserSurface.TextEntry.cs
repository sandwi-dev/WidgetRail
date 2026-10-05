using System.Text.Json;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private WidgetTextEntryDialog? editDialog;
    internal WidgetTextEntryDialog? ControllerTextEntry => input && interacting && !retired ? editDialog : null;
    private bool editPending;
    private long pageEpoch, editGeneration;
    internal bool HasDialog => editPending || editDialog is not null || libraryDialog is not null;
    // Fixed host scripts. No widget-supplied code, DOM handle or page text crosses IPC.
    // A remote object refers to the exact focused field, including its document.
    private const string FocusedField = "(() => { let e = document.activeElement; while(e?.shadowRoot?.activeElement) e=e.shadowRoot.activeElement; return e; })()";
    private const string ReadField = "function() { let e=document.activeElement; while(e?.shadowRoot?.activeElement) e=e.shadowRoot.activeElement; const ok = this instanceof HTMLTextAreaElement || this instanceof HTMLInputElement && ['text','search','url','email','tel'].includes(this.type); return ok && e===this && this.isConnected && !this.disabled && !this.readOnly && this.value.length <= 2048 ? this.value : null; }";
    private const string WriteField = "function(value) { let e=document.activeElement; while(e?.shadowRoot?.activeElement) e=e.shadowRoot.activeElement; const proto=this instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : this instanceof HTMLInputElement && ['text','search','url','email','tel'].includes(this.type) ? HTMLInputElement.prototype : null; if(!proto || e!==this || !this.isConnected || this.disabled || this.readOnly || typeof value!=='string' || value.length>2048 || this.maxLength>=0 && value.length>this.maxLength) return false; Object.getOwnPropertyDescriptor(proto,'value').set.call(this,value); this.dispatchEvent(new Event('input',{bubbles:true})); this.dispatchEvent(new Event('change',{bubbles:true})); return true; }";

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
        WidgetTextEntryDialog? dialog = null;
        try
        {
            var value = BrowserAddressInput.EditorValue(current.Source);
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
                using var field = JsonDocument.Parse(await Call("Runtime.callFunctionOn",
                    JsonSerializer.Serialize(new BrowserFieldCall(objectId!, ReadField), BrowserJsonContext.Default.BrowserFieldCall)));
                if (!field.RootElement.GetProperty("result").TryGetProperty("value", out var text) || text.ValueKind != JsonValueKind.String)
                { if (automatic is null) ShowStatus("Select a text field first. Use the external browser for passwords or embedded editors."); return; }
                value = text.GetString()!;
            }
            if (!Current()) return;
            var accepted = false;
            dialog = new(new ViewNode { Id = "browser.edit", Kind = ViewNodeKind.TextEntry, TextEntryValue = value,
                TextEntryMaximumLength = 2048, AccessibilityLabel = addressEntry ? "Address or Google search" : "Page text",
                TextEntryPlaceholder = addressEntry ? "Search Google or enter a URL" : "Selected text field" },
                commit => { accepted = commit; dialog!.Hide(); });
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
                    JsonSerializer.Serialize(new BrowserFieldCall(objectId!, WriteField, [new(entered)]), BrowserJsonContext.Default.BrowserFieldCall)));
                if (Current() && (!result.RootElement.GetProperty("result").TryGetProperty("value", out var applied) || applied.ValueKind != JsonValueKind.True))
                    ShowStatus("The selected field changed or cannot accept this text. Select it again to retry.");
                else if (Current()) ShowStatus(null);
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
    }
    private void CancelEdit()
    {
        ++editGeneration; editPending = false;
        if (editDialog is not { } dialog) return;
        editDialog = null; dialog.Erase(); dialog.Hide();
    }
}
