using System.Globalization;
using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private double requestedZoom = 1, appliedZoom = 1;
    private double zoomAnchorX = .5, zoomAnchorY = .5;
    private bool zoomPending, zoomPumping;
    private void ChangeZoom(double delta)
    {
        CancelSpatialNavigation();
        if (!input || core is null || faulted || HasDialog) return;
        if (IsHostedPackage)
        {
            var current = core;
            var epoch = pageEpoch;
            if (!IsHostedDocument(current.Source) || loading) return;
            var origin = JsonSerializer.Serialize(new Uri(document.Url).GetLeftPart(UriPartial.Authority), BrowserJsonContext.Default.String);
            var direction = delta > 0 ? "1" : "-1";
            // Packaged canvases can consume zoom locally without magnifying their
            // toolbar or making a round trip through the widget worker.
            Run(async () =>
            {
                var handled = await current.ExecuteScriptAsync("(()=>{if(location.origin!==" + origin + ")return null;" +
                    "return !window.dispatchEvent(new CustomEvent('widgetrailzoom',{cancelable:true,detail:{direction:" + direction + "}}));})()");
                if (retired || !input || HasDialog || loading || pageEpoch != epoch || !ReferenceEquals(core, current)) return;
                if (handled == "false") ChangePageZoom(delta);
            });
            return;
        }
        ChangePageZoom(delta);
    }
    private void ChangePageZoom(double delta)
    {
        requestedZoom = Math.Clamp(Math.Round(requestedZoom + delta, 1), 1, 2.5);
        zoomAnchorX = cursorX; zoomAnchorY = cursorY;
        RefreshZoom();
    }
    private void RefreshZoom()
    {
        zoomPending = true;
        if (zoomPumping || core is null || faulted || retired || loading) return;
        zoomPumping = true;
        Run(async () =>
        {
            try
            {
                while (zoomPending && !retired && core is { } current && !loading)
                {
                    zoomPending = false;
                    var requested = requestedZoom;
                    using var before = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
                    var view = before.RootElement.GetProperty("cssVisualViewport");
                    var scale = view.GetProperty("scale").GetDouble();
                    if (Math.Abs(requested - scale) > .001)
                        await current.CallDevToolsProtocolMethodAsync("Input.synthesizePinchGesture",
                            JsonSerializer.Serialize(new BrowserPinchGesture(
                                zoomAnchorX * view.GetProperty("clientWidth").GetDouble(),
                                zoomAnchorY * view.GetProperty("clientHeight").GetDouble(), requested / scale),
                                BrowserJsonContext.Default.BrowserPinchGesture));
                    if (retired || !ReferenceEquals(current, core)) return;
                    using var metrics = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
                    appliedZoom = metrics.RootElement.GetProperty("cssVisualViewport").GetProperty("scale").GetDouble();
                    zoomLabel.Text = appliedZoom.ToString("P0", CultureInfo.CurrentCulture);
                }
            }
            finally { zoomPumping = false; }
        });
    }
}
