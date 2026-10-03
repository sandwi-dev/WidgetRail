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
        if (!input || core is null || faulted || HasDialog) return;
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
