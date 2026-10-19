using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    // The browser owns selection, undo and editor state. Read only a bounded
    // preview; password values never cross the renderer/host boundary.
    private const string LiveFieldState = """
        function() {
          const field=this;
          let active=document.activeElement;
          while(active?.shadowRoot?.activeElement) active=active.shadowRoot.activeElement;
          if(!field.isConnected||active!==field||field.disabled||field.readOnly)return null;
          const ordinary=field instanceof HTMLInputElement||field instanceof HTMLTextAreaElement;
          const sensitive=ordinary&&field.type==='password';
          if(!ordinary&&!field.isContentEditable)return null;
          let value=ordinary?field.value:(field.innerText??field.textContent??'');
          let start=ordinary?(field.selectionStart??value.length):value.length;
          let length=ordinary?Math.max(0,(field.selectionEnd??start)-start):0;
          if(!ordinary){
            const selection=field.ownerDocument.getSelection();
            if(selection?.rangeCount){
              const range=selection.getRangeAt(0);
              if(field.contains(range.startContainer)&&field.contains(range.endContainer)){
                const prefix=range.cloneRange();prefix.selectNodeContents(field);prefix.setEnd(range.startContainer,range.startOffset);
                start=prefix.toString().length;length=range.toString().length;
              }
            }
          }
          const offset=Math.max(0,start-1024);
          const display=sensitive?'●'.repeat(Math.min(2048,value.length)):value.slice(offset,offset+2048);
          return {display,caret:Math.min(display.length,Math.max(0,start-offset)),length:Math.min(length,display.length),sensitive};
        }
        """;

    private async Task EditLiveFieldAsync(AutomaticFieldTarget target)
    {
        if (!IsCurrentClick(target.Click) || core is not { } current || XamlRoot is null) return;
        var generation = ++editGeneration;
        var epoch = pageEpoch;
#if ENABLE_WIDGET_VALIDATION
        fixtureLiveTarget = target;
#endif
        CancelPointer();
        editPending = true; interacting = browsing = true;
        WidgetTextEntryDialog? dialog = null;
        using var placementLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        Task? placement = null;
        try
        {
            var initial = await Read();
            if (!Current()) return;
            using var metadata = JsonDocument.Parse(await Call("Runtime.callFunctionOn",
                JsonSerializer.Serialize(new BrowserFieldCall(target.ObjectId,
                    "function(){return {multiline:this instanceof HTMLTextAreaElement||this.isContentEditable,kind:this.type||'',mode:this.inputMode||''};}"), BrowserJsonContext.Default.BrowserFieldCall)));
            var multiline = metadata.RootElement.GetProperty("result").GetProperty("value").GetProperty("multiline").GetBoolean();
            var fieldMetadata=metadata.RootElement.GetProperty("result").GetProperty("value");
            var purpose=initial.Sensitive?TextEntryInputKind.Sensitive:fieldMetadata.GetProperty("kind").GetString() switch
            {
                "email"=>TextEntryInputKind.Email,"url"=>TextEntryInputKind.Url,"number"=>TextEntryInputKind.Decimal,
                _=>fieldMetadata.GetProperty("mode").GetString() switch {"numeric"=>TextEntryInputKind.Integer,"decimal"=>TextEntryInputKind.Decimal,_=>TextEntryInputKind.Ordinary},
            };
            dialog = new(new ViewNode { Id = "browser.liveEdit", Kind = ViewNodeKind.TextEntry,
                TextEntryValue = initial.Display, TextEntryMaximumLength = 2048,
                TextEntryInputKind = purpose,
                AccessibilityLabel = initial.Sensitive ? "Password" : "Type directly into the page",
                TextEntryPlaceholder = "Live page text" }, _ => dialog!.Hide(),
                multiline: multiline, allowSubmit: true, liveEdit: Send);
            dialog.BindRoot(XamlRoot);
            dialog.GuideChanged = () => InteractionChanged?.Invoke();
            dialog.ThemeLease = NativePopupTheme.Dialog(dialog, this);
            await PositionLiveKeyboardAsync(target, dialog, Current, placementLifetime.Token);
            if (!Current()) return;
            editDialog = dialog; editPending = false;
            DrawCursor(); InteractionChanged?.Invoke();
            var shown = dialog.ShowKeyboardAsync(this);
            placement = TrackLiveKeyboardAsync(target, dialog, Current, placementLifetime.Token);
            await shown;
            await dialog.WaitForLiveInputAsync();
        }
        catch (OperationCanceledException) { }
        finally
        {
#if ENABLE_WIDGET_VALIDATION
            if (ReferenceEquals(fixtureLiveTarget, target)) fixtureLiveTarget = null;
#endif
            if (ReferenceEquals(editDialog, dialog)) editDialog = null;
            placementLifetime.Cancel();
            if (placement is not null) await placement;
            dialog?.Erase();
            if (generation == editGeneration) editPending = false;
            if (input && !retired && !HasDialog) FocusInteraction?.Invoke();
            DrawCursor(); InteractionChanged?.Invoke();
        }

        bool Current() => !retired && input && ReferenceEquals(core, current) && epoch == pageEpoch && generation == editGeneration;
        async Task<string> Call(string method, string parameters)
        {
            if (!Current()) throw new OperationCanceledException();
            // Input commands still go to the top-level target, whose native
            // focused frame owns editing. Reads address the exact field realm.
            var result = await current.CallDevToolsProtocolMethodForSessionAsync(method.StartsWith("Input.", StringComparison.Ordinal) ? "" : target.SessionId ?? "",
                method, parameters).AsTask().WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
            if (!Current()) throw new OperationCanceledException();
            using var response = JsonDocument.Parse(result);
            if (response.RootElement.TryGetProperty("exceptionDetails", out _)) throw new OperationCanceledException();
            return result;
        }
        async Task<KeyboardTextState> Read()
        {
            using var result = JsonDocument.Parse(await Call("Runtime.callFunctionOn",
                JsonSerializer.Serialize(new BrowserFieldCall(target.ObjectId, LiveFieldState), BrowserJsonContext.Default.BrowserFieldCall)));
            var value = result.RootElement.GetProperty("result").GetProperty("value");
            if (value.ValueKind != JsonValueKind.Object) throw new OperationCanceledException();
            return new(value.GetProperty("display").GetString() ?? "", value.GetProperty("caret").GetInt32(),
                value.GetProperty("length").GetInt32(), value.GetProperty("sensitive").GetBoolean());
        }
        async Task<KeyboardTextState> Send(KeyboardEdit edit)
        {
            var current=await Read(); // Exact field must still own browser focus before every command.
            if (edit.Kind == KeyboardEditKind.Insert)
                await Call("Input.insertText", JsonSerializer.Serialize(new BrowserInsertText(edit.Text ?? ""), BrowserJsonContext.Default.BrowserInsertText));
            else if (edit.Kind == KeyboardEditKind.Clear)
            {
                await Key("a", 65, 2); await Key("Backspace", 8, 0);
            }
            else if (edit.Kind == KeyboardEditKind.Key) await Key(edit.Key!, edit.KeyCode, edit.Wordwise&&current.Sensitive?edit.Modifiers&~2:edit.Modifiers);
            return await Read();
        }
        async Task Key(string key, int code, int modifiers)
        {
            var nativeCode = code is >= 65 and <= 90 ? "Key" + (char)code : key;
            try { await Call("Input.dispatchKeyEvent", JsonSerializer.Serialize(new BrowserKeyEvent("keyDown", key, nativeCode, code,
                key == "Enter" ? "\r" : null, modifiers), BrowserJsonContext.Default.BrowserKeyEvent)); }
            finally
            {
                if (Current()) await Call("Input.dispatchKeyEvent", JsonSerializer.Serialize(new BrowserKeyEvent("keyUp", key, nativeCode, code,
                    modifiers: modifiers), BrowserJsonContext.Default.BrowserKeyEvent));
            }
        }
    }
}
