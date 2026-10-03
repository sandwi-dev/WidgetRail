using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private sealed record AutomaticFieldClick(CoreWebView2 Core, long Gesture, long PointerEpoch,
        long PageEpoch, long EditGeneration, double RatioX, double RatioY, double X, double Y);
    private sealed record AutomaticFieldTarget(AutomaticFieldClick Click, string ObjectId);

    // Only host-generated numeric coordinates enter this fixed script. A click
    // elsewhere must not reopen a previously focused field. Open shadow roots
    // and associated labels follow the same exact-field editor contract.
    private const string ClickedField = """
        (p => {
          let hit = document.elementFromPoint(p.x, p.y);
          while (hit?.shadowRoot) {
            const child = hit.shadowRoot.elementFromPoint(p.x, p.y);
            if (!child || child === hit) break;
            hit = child;
          }
          let field = document.activeElement;
          while (field?.shadowRoot?.activeElement) field = field.shadowRoot.activeElement;
          const supported = field instanceof HTMLTextAreaElement ||
            field instanceof HTMLInputElement && ['text','search','url','email','tel'].includes(field.type);
          if (!supported || !field.isConnected || field.disabled || field.readOnly || field.value.length > 2048) return null;
          return hit === field || hit?.closest('label')?.control === field ? field : null;
        })
        """;

    private bool IsCurrentClick(AutomaticFieldClick click) => !retired && input && browsing && !HasDialog && !loading &&
        ReferenceEquals(core, click.Core) && pointerGesture == click.Gesture && pointerEpoch == click.PointerEpoch &&
        pageEpoch == click.PageEpoch && editGeneration == click.EditGeneration && !pointerPressed && !pointerMoved &&
        cursorX == click.RatioX && cursorY == click.RatioY;

    private async Task TryEditClickedFieldAsync(AutomaticFieldClick click)
    {
        if (!IsCurrentClick(click)) return;
        string? objectId = null;
        var handedToEditor = false;
        try
        {
            var point = JsonSerializer.Serialize(new BrowserHitPoint(click.X, click.Y), BrowserJsonContext.Default.BrowserHitPoint);
            using var result = JsonDocument.Parse(await click.Core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new BrowserEvaluation(ClickedField + "(" + point + ")"), BrowserJsonContext.Default.BrowserEvaluation))
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token));
            if (!result.RootElement.TryGetProperty("result", out var remote) ||
                !remote.TryGetProperty("objectId", out var reference)) return;
            objectId = reference.GetString();
            if (objectId is null || !IsCurrentClick(click)) return;
            handedToEditor = true;
            await EditAsync(addressEntry: false, automatic: new(click, objectId));
        }
        catch (Exception) when (!handedToEditor && !IsCurrentClick(click)) { /* A newer gesture, page or input owner superseded this click. */ }
        finally
        {
            if (objectId is not null && !retired)
                try
                {
                    await click.Core.CallDevToolsProtocolMethodAsync("Runtime.releaseObject",
                        JsonSerializer.Serialize(new BrowserObject(objectId), BrowserJsonContext.Default.BrowserObject))
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
                }
                catch (Exception) { /* Document/process loss already retired the object. */ }
        }
    }
}
