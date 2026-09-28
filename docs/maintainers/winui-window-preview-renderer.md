# WinUI GPU window-preview renderer

This renderer consumes the trusted session contract in
[`winui-window-previews.md`](winui-window-previews.md). It does not change worker
declarations, admission authority, or input routing. A preview is visual content,
not an interactive view into the captured application.

## Integration

Create one shell-owned `WindowPreviewCaptureService`, then call its
`CreateRenderer` with each widget's `WidgetPresentationSession` and trusted host
HWND. All renderers share its device, eight slots and aggregate resource budget.
Enable `WidgetPresentationSessionOptions.WindowPreviews`.
Call `Apply` with the exact displayed frame, then create a surface using the
declared window ID and image fit. A surface keeps its immutable native identity;
replace it if the HWND, process identity, or class changes.

The presenter owns WinUI sizing, parent clips, transforms and z order. The
renderer must receive `SetVisible(false)` as soon as its overlay/presentation is
hidden. Unloaded or offscreen surfaces relinquish native capture demand. Call
`RemoveSurface` for a permanently removed element and await `DisposeAsync` when
retiring its renderer, before disposing the session. Await the capture service's
disposal when the whole shell closes. Pinned presentations should
remain placeholders unless explicitly admitted to the same lifecycle.

No resource URL, HWND, screenshot, or pixel payload comes from arbitrary worker
code. The frame's trusted host targets and renewable permission grant are the
only source authority. Missing admission, unavailable capture, invalid identity,
or capacity exhaustion leaves a placeholder.

## Ownership and threading

`WindowPreviewSurface` owns a WinUI child `SpriteVisual` and surface brush. A
background pump coalesces size, demand and permission state; it performs native
calls and publishes immutable diagnostics and one latest AddRef'd swapchain. The
WinUI dispatcher only binds that published reference and applies layout. No
normal UI resize, visibility, grant, binding or removal method waits for capture
start/stop or native drawing.

`WinUiWindowPreviewNative.dll` is a narrow GPU adapter. Its owner thread uses a
hardware D3D11 BGRA device, a free-threaded Windows Graphics Capture frame pool,
Direct2D GPU drawing into an opaque DXGI composition swapchain. WinUI binds
that surface to its native composition brush. It does not link the old renderer or layout
engine. There is no DWM-thumbnail fallback or CPU screenshot/readback path.
WinUI treats swapchains as opaque. The capture rectangle and Contain letterboxing
therefore use an explicit black background, and WinUI still controls the visual's
overall opacity, transforms and clips. See the Microsoft explanation in
[microsoft-ui-xaml#10180](https://github.com/microsoft/microsoft-ui-xaml/issues/10180).
An expired surface is detached as soon as the dispatcher runs; while it is
blocked, the native owner presents an opaque black safety blank. This removes
source pixels without depending on a XAML commit. A tested
`CompositionDrawingSurface` alternative retained source pixels during a blocked
dispatcher, including with `RequestCommitAsync`, so it is not used for capture.

Frame callbacks only publish readiness and signal a lifetime-owned event. Every
borrowed WGC frame closes after GPU submission. Native calls run on the owner
thread, and capture stop/recreation never races its drawing context.

The managed authority callback reads only atomic state and the session's
thread-safe grant guard. Native checks it on a 16 ms watchdog cadence, separately
from frame arrival and UI dispatch, and again before presenting. Its absolute QPC
deadline is conservatively measured from *before* permission refresh, and is no
later than the session's two-second grant lifetime. Capture exclusion, identity,
host visibility and source liveness are also rechecked. Expiry or denial stops
capture and clears old pixels even if no source frame or UI update occurs.

Disposal revokes demand synchronously in managed memory, then removes native
slots asynchronously. Callback GCHandles are freed only after native removal is
confirmed, or after the native owner has joined. The latest pending swap reference
is coalesced rather than queued while WinUI is stalled.

## Bounds and recovery

The native owner admits at most eight capture slots. Output is downsampled to at
most 960 by 540 pixels while preserving the viewport aspect ratio. Source images
with more than 4096 × 2160 pixels are rejected. Contain, Cover and Fill use GPU sampling.

Known requested image storage is budgeted against 192 MiB. Active allocations
reserve room for two previous maximum-size output surfaces per slot: one still
bound by WinUI and one pending binding when native recreates its device. This
accounts for buffers the renderer owns; it is not a claim that driver or WGC
internal allocations are exactly measurable from these requests.

Resize closes the borrowed frame before recreating its pool. Device removal
retires capture/device resources and advances the surface generation, including
the intermediate absence of an output surface. WinUI can detach obsolete bindings
and import replacement surfaces when available. Swapchain presentation and safety
blanking are independent of XAML dispatcher ticks. Busy presentation leaves a
pending blank or frame that retries without another source frame.

## Build and validation

Run `scripts/Build-WinUiWindowPreview.ps1 -TestPolicy` before building the WinUI
frontend. The frontend copies the resulting release DLL when present. Packaging
must explicitly require that artifact before enabling production previews.

The explicit `--validate-window-preview` fixture creates its own green source
window and captures only that window. Its magenta viewport, orange native XAML
occluder and deliberately oversized child provide distinguishable pixel evidence
for capture, clipping, retirement and layering. `scripts/Test-WinUiWindowPreview.ps1`
exercises source resize, transform, hidden demand, permission expiry while the UI
dispatcher is blocked, denial, capture exclusion, device recreation,
unload/reload and capacity bounds. Test screenshots are evidence only and never
participate in rendering.

The device reset action tests resource recreation on a healthy adapter; it does
not simulate an actual driver reset. Production shell wiring and physical
controller acceptance are separate from this owned-window graphics fixture.

The implementation checkpoint passed 15 native policy checks, 26 fixture
assertions and 19 state/pixel stages on a desktop at 125% scaling. The blocked
expiry capture contained zero source-color pixels while the fixture still
reported its dispatcher-blocked phase; afterward the authored magenta placeholder
returned. Eight-source admission was exercised across two renderer instances.
The frontend analyzer build completed without warnings or errors. These results
cover the owned fixture, not arbitrary user applications or real driver removal.
