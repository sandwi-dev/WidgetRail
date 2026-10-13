# Presentation layers and logical ownership

`PresentationLayers` is the WinUI host's root paint-order contract. Do not fix
overlap by adding another local `Canvas.ZIndex`: that cannot escape an ancestor's
stacking context. Root-level native surfaces and overlays use these named bands:

| Back to front | Contents |
| --- | --- |
| Content | Ordinary widget tree, Browser/provider documents and window-preview controls |
| ContentMedia | Resident inline native/embedded video |
| WidgetDialog | Widget-local scrim and dialog, placed through a portal |
| WidgetDialogMedia | Resident video owned by that dialog |
| FullscreenBackdrop | Expanded presentation's backdrop |
| FullscreenMedia | The existing resident renderer while expanded |
| FullscreenChrome | Expanded image/player content and transport controls |
| ShellOverlay | Radial chooser and other shell overlays |
| Startup | Startup presentation |

WinUI ContentDialog, Flyout and text-entry popups retain the platform popup layer.
Inline resident surfaces are clipped to their owning widget/dialog viewport, so
they cannot spill into the tray or guide outside that viewport. Browser and
provider-document controls remain in their native XAML subtree and inherit its
position in the stack. No SDK change or widget-specific workaround is required.

## Resident surfaces

Native video and embedded video share `MediaPresentationStage`. The fullscreen
host tracks these planes, but each is a sibling in the common root; it is no
longer trapped inside a single high-priority fullscreen container. Placement
derives the inline band from the slot's dialog ancestry. Expansion changes its
band and transform, not its native attachment. Collapse restores the slot's band.
Fullscreen keyboard/transition gating is on the shared root, and its chrome has
no transparent hit-test surface that would block the sibling native video.

## Dialog portals

`WidgetMotionStage` owns the modal logically. `PresentationPortal` places its
visuals in the root dialog band while following that stage's bounds, opacity,
visibility, input eligibility, viewport clipping and rounded surface boundary.
Rectangular XAML clipping and rounded composition clipping use separate visuals;
combining them on one visual makes WinUI query a geometric clip as an inset clip
and fail with `E_NOINTERFACE` during composition.

The presenter attaches the same input policy to its ordinary and portaled trees.
Its automation stage exposes the modal once and excludes inactive background
controls; the paint portal contributes no duplicate automation subtree. Closing
retains a noninteractive portal until the exit ends, then releases its event
subscriptions and visuals. Owner retirement releases it immediately.

Paint ancestry is not ownership ancestry. `PresentationLayers.LogicalParent`
bridges portals and resident planes back to their anchors. Ownership consumers
use it for pointer gap handling, intent focus return, theme/text-scale lookup,
responsive viewport selection, visibility and artwork sizing. Geometry walks
still use the real visual tree. Artwork sizing skips a portal's placement
transform, which already represents the logical ancestors' scale.

Ordinary dialogs retain their opening/closing animations, including dialogs over
playing video. Dialogs that themselves contain a native media/browser/preview
surface currently open and retire without the old compositor dialog animation.
That timeline does not provide the synchronized native placement contract used
by page transitions; skipping it avoids native opacity holes and independent
movement. This renderer-wide fallback does not affect page/fullscreen animations.

## Extending and validating

New host overlays must join a named band and keep an explicit logical anchor;
do not introduce another unowned root child or independent ordering convention.
New resident renderer types must preserve their scope, viewport clip, input and
teardown contracts when moved between inline, dialog and fullscreen presentation.

`--validate-styles --presentation-layer-fixtures=<directory>` uses a synthetic
`pattern.mp4` and an actual local WebView. It checks native/embedded ordering,
unchanged playback attachment, modal focus, inherited text scale and viewport,
clipping, fullscreen band restoration, hide/show, closing retention and cleanup.
`--validate-modals` now runs the existing modal action/focus/accessibility suite
through root portals. Existing media/fullscreen and page-navigation suites remain
regression gates. Desktop recordings supplement these structural checks.

Current evidence: `artifacts/presentation-layering` (24 layering, 38 modal,
79 media/fullscreen and 62 page-navigation checks passed). Candidate/live pointer
verification is tracked in `docs/page-navigation-transitions.md`.

The broad production-shell fixture stops after 92 checks at its radial Back
assertion. The same failure was reproduced from unchanged `bc535391` in an
isolated source copy; it is not counted as passing or changed in this work.
Both result files are preserved in the evidence directory.
