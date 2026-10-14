using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private long spatialEpoch;
    private bool spatialPump;
    private SpatialDirection? spatialPending;
#if ENABLE_WIDGET_VALIDATION
    internal SpatialMoveResult? LastSpatialMoveForValidation { get; private set; }
    internal string? SpatialErrorForValidation { get; private set; }
    internal bool SpatialBusyForValidation => spatialPump;
    internal int SpatialRequestsForValidation { get; private set; }
    internal (double X, double Y) SpatialPointerForValidation => (cursorX, cursorY);
#endif
    private void CancelSpatialNavigation() { ++spatialEpoch; spatialPending = null; }

    private void JumpPointer(FocusNavigationDirection direction)
    {
        if (!input || !browsing || core is null || loading || zoomPumping || retired || HasDialog || toolbarFocused || pointerPressed || nativePointerDown) return;
        var next = direction switch
        {
            FocusNavigationDirection.Left => SpatialDirection.Left,
            FocusNavigationDirection.Right => SpatialDirection.Right,
            FocusNavigationDirection.Up => SpatialDirection.Up,
            FocusNavigationDirection.Down => SpatialDirection.Down,
            _ => (SpatialDirection?)null,
        };
        if (next is null) return;
        UseControllerInput();
        // Bound held-input pressure. Repeats never create an accumulating queue.
        spatialPending = next;
        if (spatialPump) return;
        spatialPump = true;
        Run(async () =>
        {
            try
            {
                while (spatialPending is { } pending && input && browsing && !retired)
                {
                    spatialPending = null;
                    var epoch = spatialEpoch; var page = pageEpoch; var browser = core;
                    if (browser is null) break;
                    bool Current() => !retired && input && browsing && !HasDialog && !toolbarFocused && !loading &&
                        !zoomPumping && !pointerPressed && !nativePointerDown && controllerPointerVisible && spatialEpoch == epoch && pageEpoch == page && ReferenceEquals(core, browser);
                    try
                    {
#if ENABLE_WIDGET_VALIDATION
                        LastSpatialMoveForValidation = null; SpatialErrorForValidation = null;
                        SpatialRequestsForValidation++;
#endif
                        await pointer.WaitAsync(lifetime.Token);
                        pointer.Release();
                        if (!Current()) continue;
                        await using var navigation = new BrowserSpatialNavigator(browser, Current, lifetime.Token);
                        var move = await navigation.MoveAsync(pending, cursorX, cursorY);
                        if (move is null || !Current()) continue;
                        cursorX = Math.Clamp(move.X, 0, 1); cursorY = Math.Clamp(move.Y, 0, 1);
                        cursorDirty = true; DrawCursor(); PumpPointer();
#if ENABLE_WIDGET_VALIDATION
                        LastSpatialMoveForValidation = move;
#endif
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    {
#if ENABLE_WIDGET_VALIDATION
                        SpatialErrorForValidation = error.ToString();
#endif
                        // Navigation/document loss and unsupported renderers keep
                        // the existing free pointer. Never fault the browser view.
                        if (Current() && error is not OperationCanceledException)
                            System.Diagnostics.Trace.TraceWarning("Browser directional navigation unavailable: {0}", error.GetType().Name);
                    }
                }
            }
            finally { spatialPump = false; }
        });
    }
}
