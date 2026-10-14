namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private bool dpadHeld, spatialHoverDeferred;

    // Called with real button state, not the sparse directional-repeat events.
    // Keyboard/flyout/native chooser input never enters this route.
    internal void SampleDpadHold(bool held)
    {
        if (AttachedPopup is { } child) { child.SampleDpadHold(held); return; }
        dpadHeld = held;
        if (!held) FlushSpatialHover();
    }

    internal void CancelDpadHold()
    {
        dpadHeld = false;
        CancelSpatialNavigation();
    }

    private void CancelSpatialHover()
    {
        spatialHoverDeferred = false;
        cursorDirty = false;
    }

    private void BeginSpatialHover()
    {
        if (spatialHoverDeferred) return;
        spatialHoverDeferred = true;
        cursorDirty = false;
        // Keep the previous hover until commit. Parking outside the page would
        // collapse an already-open menu before its children can be selected.
        // Intermediate destinations never receive native mouse movement.
    }

    private void FlushSpatialHover()
    {
        if (dpadHeld || spatialPump || !spatialHoverDeferred) return;
        spatialHoverDeferred = false;
        if (retired || !input || !browsing || HasDialog || !controllerPointerVisible) return;
        cursorDirty = true;
        PumpPointer();
    }
}
