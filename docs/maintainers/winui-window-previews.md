# Window previews in the WinUI migration

The existing `WindowPreview` SDK declaration remains a view-only opaque window ID,
aspect ratio and image-fit intent. HWNDs, process identities, permissions and pixels
remain host-owned. No new widget capability or executable UI contract is introduced.

## Session admission

Enable `WidgetPresentationSessionOptions.WindowPreviews` only in a frontend that
implements capture retirement and permission renewal. The existing Bridge hello
then includes the opt-in. Snapshot metadata is accepted only for declared preview
IDs, with bounded exact native identity fields. The frame exposes a read-only
`WindowPreviews` candidate inventory; that inventory alone is not capture permission.

`RefreshWindowPreviewPermissionsAsync(displayedFrame)` queries the existing broker
permission control plane. It requires the exact frame published by this session,
intersects allowed IDs with the displayed/current native identities, and returns
a `WidgetWindowPreviewGrant`. At most one refresh is pending, with a two-second
response deadline. The grant lasts at most two seconds; renew before expiry.

Call `IsWindowPreviewCurrent(grant, windowId)` before delivering or displaying a
frame. A newer permission response supersedes older grants immediately, including
denial. A failed refresh invalidates prior permission. Ordinary compatible snapshot
updates retain target identity. Omission, changed HWND/process/class identity,
runtime/session replacement, failure and disposal retire it; a later identical
declaration cannot revive an old grant.

## Renderer obligations

The renderer must additionally validate the live native window identity and capture
exclusion policy, stop captures when no visible presentation demands them, and bound
capture count, GPU storage and frame lifetime. Keep capture frames GPU-backed and
use native composition; do not route pixels through the worker or the snapshot tree.
Permission expiry/denial must clear the presented capture, not merely stop requesting
new frames while stale pixels remain visible. A preview is never an input target.

The production shell now connects the shared GPU capture service and negotiates
preview support only after that service initializes. Retained widget presenters
revoke capture demand before suspension and rebind against their current admitted
frame on resume. Native checks cover capture pixels, permission expiry, target
replacement, device reset, visibility, and retirement; real Task Switcher acceptance
remains distinct from these owned-window fixtures.

Preview slots are graphics viewports. An authored `text-align` value may style
placeholder text but must not override their stretched capture-content alignment.
Task Switcher's centered style previously centered an empty capture Grid at zero
intrinsic width, preventing viewport demand and leaving every thumbnail blank.
The centered-style presenter fixture now verifies nonzero full-slot capture width
and receipt of native frames. Layout diagnostics include bounded demand/state/frame
counts without window titles, capture pixels, or native target handles.
