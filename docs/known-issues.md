# Known issues and active bug ledger

This file is the durable source of truth for user-visible bugs found during
local product testing. Roadmap items describe planned capability; entries here
describe behavior that is already expected to work or a current interaction
that must be replaced. A bug is not closed until its acceptance evidence passes
in the packaged Release overlay and the closing commit is recorded.

## Status vocabulary

- **Open** — user-visible behavior is confirmed and acceptance work is not yet complete.
- **Confirmed** — reproduced by a user or deterministic/local evidence.
- **Investigating** — the owning layer is being traced; no root-cause claim yet.
- **Implementing** — a root cause and durable design are selected.
- **Verifying** — the fix is implemented and awaiting packaged/local evidence.
- **Closed** — acceptance evidence and closing commit are recorded.

## Active issues

| ID | Priority | Status | Owning layer | Summary |
| --- | --- | --- | --- | --- |
| GBA-001 | P0 | Verifying | Audio Mixer / broker / Windows audio provider | Per-application controls now target exact session IDs and the provider passes a reversible live-volume test; packaged row control still needs hands-on verification. |
| GBA-002 | P1 | Verifying | Widget protocol / host placement | Per-view compact/standard/wide/adaptive surfaces and host work-area clamping are implemented; packaged visual verification remains. |
| GBA-003 | P1 | Verifying | Audio Mixer / declarative renderer | The all-session controller-scroll mixer and stable focus restoration are implemented; packaged visual/controller verification remains. |
| GBA-004 | P1 | Verifying | OverlayHost presentation / invalidation | Size-changing widget swaps now move without redraw and synchronously commit one complete frame; packaged visual verification remains. |
| GBA-005 | P0 | Verifying | OverlayHost controller routing | Hierarchical B routing is implemented across nested widget views, root widgets, and the icon tray; packaged controller verification remains. |
| GBA-006 | P1 | Verifying | OverlayHost presentation | All direct snapshot refreshes compare prior/next surface extents; packaged resize verification remains. |
| GBA-007 | P1 | Verifying | Declarative renderer / focus navigation | Nested fixed-point reveal and clip-feasibility filtering are implemented; packaged controller verification remains. |
| GBA-008 | P2 | Verifying | Declarative renderer | Inactive LRU offset eviction and scroll-clipped deferred focus outlines are implemented. |
| GBA-009 | P1 | Verifying | Platform diagnostics transport | First-instance ownership and mutual kernel PID authentication are implemented. |
| GBA-010 | P2 | Verifying | Platform diagnostics transport | End-to-end request deadlines and bounded client timeout validation are implemented. |
| GBA-011 | P0 | Verifying | Widget SDK / host focus / Audio Mixer | Focus-safe disabled/busy semantics and per-session Audio reconciliation are implemented; packaged controller verification remains. |
| GBA-012 | P1 | Verifying | Declarative renderer / component styles | Effective surface/ancestor focus clipping and non-scaling full-width defaults are implemented; packaged visual verification remains. |
| GBA-013 | P1 | Verifying | Network Controls / widget SDK | The full-width controller-scroll available-network list, explicit scan, and focused-row routing are implemented; packaged visual/controller verification remains. |
| GBA-014 | P0 | Verifying | YT Music / Widget SDK routing / native icons | Window-wide transport shortcuts and Previous/Next glyph orientation are corrected; packaged controller/visual verification remains. |
| GBA-015 | P1 | Verifying | Declarative renderer / built-in widget themes | Fixed regions no longer shrink into clipping, Sliders use a thin native track inside their controller target, and the built-in surfaces use a lighter visual hierarchy; packaged visual verification remains. |
| GBA-016 | P0 | Verifying | OverlayHost focus / lifecycle / controller routing | The selected widget panel now remains visible while the tray owns focus, with one-level Back, automatic tray preview, and root-boundary return behavior; packaged controller evidence remains. |
| GBA-017 | P0 | Verifying | Declarative renderer / scroll focus | Focus-follow now snaps the first/last focusable descendant to the true scroll extent; packaged controller verification remains. |
| GBA-018 | P1 | Verifying | OverlayHost controller routing | Pressed D-pad/left-stick Up from the tray now enters the visible widget without dispatching a widget action. |
| GBA-019 | P1 | Verifying | OverlayHost controller guide / layout | The guide is density-aware, contextual, bounded, and no-wrap; compact/high-scale visual evidence remains. |
| GBA-020 | P1 | Verifying | OverlayHost panel clipping / renderer | A cached host-owned rounded viewport clip now masks opaque widget roots; packaged visual evidence remains. |
| GBA-021 | P1 | Verifying | OverlayHost targeting / DPI | Visible DPI, topology, taskbar/work-area, and appearance changes now share one dynamic refresh policy; physical mixed-monitor/hot-plug evidence remains. |

## GBA-001 — Per-application audio controls have no real effect

**Evidence:** On 2026-08-07 the packaged Release overlay displayed three real
Core Audio sessions. Repeated LT/RT and mute input was logged as handled, all
four audio read/control grants were present, but the corresponding Windows
session volume did not change. Master-output control is a separate path and is
not evidence that session control works.

**Implementation evidence:** A reversible, opt-in live provider test changed a
real `System sounds` session from 100% to 95%, read back 95% through Core Audio,
and restored 100%. The redesigned widget no longer infers a selected card from
global trigger shortcuts: every visible application row has stable opaque IDs
and an immutable action map that resolves directly to the provider's exact raw
session ID. Removed or stale actions fail closed. The Audio provider and Audio
Mixer Release suites pass 15/15 and 24/24. This proves the provider and widget
seams independently, but not yet the exact packaged worker-to-broker path
against a playing application.

**Acceptance:**

1. A reversible native test captures one real session's original volume/mute,
   sets a distinct bounded value, reads it back through Core Audio, and restores
   the original state in `finally`.
2. The packaged Audio Mixer changes and restores a playing application's volume
   and mute state, with authoritative UI reconciliation and no stale rollback.
3. Provider, broker, SDK, widget, and packaged Release tests pass.

## GBA-002 — Compact widgets waste most of a constant panel

**Evidence:** Packaged Audio Mixer and Network Controls render a roughly
single-column 440 DIP layout inside the host's common 880 DIP widget panel.

**Implementation evidence:** Snapshot protocol 2 and the public SDK expose
bounded Compact, Standard, Wide, and Adaptive hints with optional preferred and
minimum logical-DIP pairs. The native host resolves those hints without widget
IDs, reserves shell/tray/footer space, grows safely for text accessibility, and
clamps against the selected monitor after DPI and interface scaling. Audio and
Network publish Compact surfaces, YT Music publishes Standard, and Settings
publishes Standard. API-1/no-hint widgets retain the legacy surface. Native
Release placement tests cover 720p, portrait, ultrawide, invalid hints, 200% DPI,
125% interface scale, and 150% text scale.

**Acceptance:**

1. Every view can publish bounded compact/standard/wide/adaptive presentation
   hints through the public protocol and SDK.
2. OverlayHost clamps hints against active-monitor work area, DPI, interface and
   text scale, shell tray/footer reservations, and minimum controller targets.
3. Audio Mixer and Network Controls open as compact floating panels; YT Music
   can request its wider media presentation.
4. Small/portrait/ultrawide and mixed-DPI tests prove containment and responsive
   fallback without hard-coded widget IDs.

## GBA-003 — Audio Mixer needs an all-session scrollable surface

**Evidence:** The reported widget rendered one selected session and cycled
sessions with shoulder shortcuts. This made comparison slow and did not match
the requested mixer mental model.

**Implementation evidence:** Master output is pinned above a single bounded
vertical Scroll containing every session row. Master and application rows use
the same icon–Slider–percentage composition: the nonfocusable icon exposes mute
state, Left/Right changes the focused Slider's absolute volume, A toggles mute,
and Up/Down moves between rows. Each row is one stable focus target.
LB/RB/LT/RT session cycling, root shortcuts, and dashboard quick actions were
removed. Host-owned scroll offsets are keyed by exact worker instance, input
scope, and scroll ID; stable focus IDs survive close/reopen and ordinal fallback
selects the nearest focusable row after churn. SDK, native renderer/focus, and
Audio Mixer Release suites pass, including rapid absolute Slider updates,
128-session, and long-label cases.

**Acceptance:**

1. Master output remains pinned above a vertical list containing every current
   application session and its own volume/mute controls.
2. D-pad and analog navigation move through stable per-session controls; no
   bumper/trigger action changes the selected application.
3. Host-owned focus-follow scrolling keeps the focused row fully visible.
4. Closing/reopening restores the focused application and scroll location for
   the same widget runtime/input scope. Session churn retains the stable row or
   selects the nearest surviving row.
5. Empty, one-session, many-session, long-label, 720p/high-scale, and live churn
   tests pass.

## GBA-004 — Widget switching can flash tray/panel spacing

**Evidence:** The user reports a transient black border/spacing flash when
switching specifically from Audio Mixer to Network Controls in the packaged
Release overlay. This means the earlier same-extent placement optimization is
not sufficient evidence that presentation is visually continuous.

**Root cause and implementation evidence:** The Audio-to-Network transition
changes the requested panel extent, so Windows could expose an intermediate
cleared/recreated surface between placement and the later repaint. Visible
extent changes now use `SWP_NOREDRAW`, rebuild against the final client size,
then synchronously commit one complete `RedrawWindow(...RDW_UPDATENOW...)`
frame. Same-extent transitions remain repaint-only. Placement/targeting tests
cover both branches; packaged Release visual verification is still required.

**Acceptance:**

1. A deterministic host test or instrumented local trace identifies whether
   placement, render-target recreation, snapshot refresh, full-window clear, or
   tray invalidation causes the flash.
2. Switching widgets keeps the host-owned tray continuously painted; widget
   snapshot/style changes invalidate only the required regions.
3. Repeated dashboard and open-widget switching shows no flash at supported
   DPI/interface scales, and does not add idle presentation work.

## GBA-005 — B must behave as hierarchical Back

**Evidence:** The controller policy was not expressed as one testable hierarchy,
which made root-widget fallback look indistinguishable from an accidental close
and left dashboard close behavior vulnerable to inconsistent special cases.

**Acceptance:** B always goes back exactly one level: the active nested widget
scope handles it first; an unhandled B at the widget root returns to the icon
tray/dashboard; B on the icon tray closes the overlay. Guide continues to toggle
the whole overlay from any level. No other widget action is captured as Back.

**Implementation evidence:** Native routing now expresses dashboard A/B/Y,
open-widget delivery, root fallback, and nested non-bubbling as a pure ownership
policy. Managed snapshot validation and the bridge reject dashboard B quick
actions. Controller navigation passes 73 checks; nested Settings Back behavior
passes its focused Release suite.

## GBA-006 — View-specific surface transitions must resize

**Evidence:** Direct snapshot refreshes after handled actions and catalog
reconciliation replace the cached view before comparing presentation extents,
so a compact nested view can remain inside the previous standard-size HWND.

**Acceptance:** Every snapshot replacement compares the previous and next
resolved extents through the pure presentation policy. Equal extents repaint;
changed extents place exactly once.

**Implementation evidence:** Catalog, action-result, invalidation, and async
snapshot paths use one refresh-and-presentation helper. Targeting passes 31
policy checks and the native Release suite is green.

## GBA-007 — Scroll focus must always remain visibly recoverable

**Evidence:** Nested scroll corrections are computed from one stale layout, and
any descendant under a Scroll is marked revealable even when another clip or the
scroll's cross-axis prevents exposure. Focus can therefore land invisibly or be
overscrolled out of an ancestor.

**Acceptance:** Bounded inner-to-outer correction produces a final visible
target; candidates clipped on an uncorrectable axis or by a non-scroll ancestor
are rejected; D-pad and analog navigation never enter an invisible focus trap.

**Implementation evidence:** Reveal resolves inner-to-outer through a bounded
32-pass fixed point and requires actual ancestor-axis/range feasibility. Native
renderer regressions cover nested, cross-axis, and non-scroll clipping.

## GBA-008 — Scroll resource bounds must not create visual corruption

**Evidence:** Crossing the saved-offset cap clears every offset, including the
active view, and deferred focus borders are painted after scroll clips are
removed using the full un-clipped control box.

**Acceptance:** Incremental bounded eviction preserves active state, and focus
outlines remain clipped to every containing scroll viewport without changing
ordinary non-scroll focus rendering.

**Implementation evidence:** The 4,096-entry guard now evicts least-recent
inactive offsets only. Deferred focus uses the effective intersection of scroll
viewports while ordinary focus remains unchanged. Renderer Release tests pass
4,204 checks.

## GBA-009 — Diagnostics client must authenticate its server

**Evidence:** The worker verifies the nonce but not the kernel-reported pipe
server PID before sending it. A same-user process that observes launch data can
race-create the endpoint and forge sanitized-looking diagnostics.

**Acceptance:** Host-owned launch data binds the expected server process, the
client verifies it before sending secrets, and a squatted fake-server test fails
closed without poisoning a later legitimate refresh.

**Implementation evidence:** The bridge pre-creates and retains the first pipe
instance before worker launch. Server and client verify the kernel-reported
peer PID before nonce exchange. Fake-server and squatter regressions fail closed.

## GBA-010 — Diagnostics requests need end-to-end deadlines

**Evidence:** The one-instance server uses only process-lifetime cancellation
for frame reads, provider execution, and response writes; the client constructor
also accepts infinite or nonsensical custom timeouts.

**Acceptance:** A short per-request server deadline covers the complete exchange
and returns to accepting clients after stalled hello/provider cases. Client
timeouts are finite, positive, and capped with deterministic validation.

**Implementation evidence:** A per-connection deadline covers hello, request,
provider, and response work; client timeouts must be finite, positive, and no
more than ten seconds. The diagnostics Release suite passes 8/8, including
stalled hello/provider recovery.

## GBA-011 — Async control state must not move controller focus

**Evidence:** In the packaged Audio Mixer, activating mute beneath an
application row immediately moves focus to the master-output volume `+`
control. The widget globally marks controls unavailable while one broker
request is pending, and the host interprets that transient state as removal
from the focus graph.

**Acceptance:**

1. `Disabled` prevents activation but remains focusable and controller-
   navigable; it exposes a readable unavailable state rather than disappearing.
2. `Busy` remains focused and navigable while duplicate activation or value
   changes are suppressed or coalesced.
3. Only an explicit hidden/non-navigable state removes a control from the focus
   graph. A focused control changing enabled, disabled, or busy state retains
   its exact stable focus ID.
4. Audio pending state is scoped to the exact output/session operation and does
   not disable unrelated rows. Post-acknowledgement stale events cannot roll
   back the optimistic value or move focus.
5. SDK, host focus, bridge/runtime, and Audio Mixer regressions cover focus
   retention, ignored disabled activation, busy coalescing, and session churn.

**Implementation evidence:** Buttons and Sliders remain in the host focus graph
when disabled or busy; those states suppress actions without changing stable
focus identity. Audio now renders one Slider focus target per master/session
row, with A mute and absolute left/right volume. Independent per-session/output
state coalesces rapid volume targets latest-wins, retains authoritative state
through stale post-acknowledgement events, and rolls back bounded failures. The
SDK Release suite passes 41/41 and Audio Mixer passes 24/24, including the exact
application-mute focus regression. Packaged controller evidence is still
required before closing.

## GBA-012 — Focus outlines must stay inside their effective clip

**Evidence:** The Settings category selection outline loses its left and right
edges because the focused full-width row expands beyond the widget drawing
window. Similar full-width list controls can be clipped by a scroll or surface
boundary.

**Acceptance:**

1. Focus decoration is resolved inside the final intersection of the control,
   widget surface, and every ancestor clip at all supported DPI/text scales.
2. Full-width list rows do not use a focus transform that grows outside their
   layout allocation.
3. Renderer and component-gallery regressions cover edge-aligned controls,
   nested scrolling, 720p through 4K, mixed DPI, and increased text scale.

**Implementation evidence:** The built-in focused Button and Slider styles use
an inset outline and no scale transform. Settings removes its full-width focus
growth. Deferred native focus decoration now starts with the render surface,
intersects every Scroll or `overflow: clip` ancestor, and preserves explicit
`overflow: visible`. Platform theme Release tests pass 13/13; native renderer
tests pass 4,245 checks including nested non-Scroll clips, scaled root-edge
controls, visible-overflow freedom, and retained Scroll behavior. Packaged
screenshots at supported scale settings remain required before closing.

## GBA-013 — Network Controls needs a dense controller-first surface

**Evidence:** The compact Network Controls widget constrains its content root
to 44 percent of an already compact host surface. This leaves a large dead area
on the right and forces saved profiles into a single arrow-cycled card rather
than a scannable controller list.

**Acceptance:**

1. Content uses the resolved compact surface width without viewport-relative
   double-constraining or horizontal overflow.
2. Current transport, radio/privacy state, explicit scan, and available-network actions use a
   clear visual hierarchy with concise alert, empty, connecting, and failure
   states.
3. Current available networks form a bounded vertical controller-scroll list with stable
   IDs, focus restoration, and no shoulder/trigger cycling.
4. Empty, one-profile, many-profile, long-label, radio-off, wired-only,
   connecting/failure, 720p, high-DPI, and text-scale regressions pass.

**Implementation evidence:** Network now consumes the resolved compact width
and publishes the current ready scan as generation-bound identity-derived rows
in one bounded vertical Scroll. A explicitly scans from the Scan control; A
and X connect the exact current saved/open focused result. No scan occurs on
activation or a timer. LB/RB cycling and dashboard quick actions are absent.
Credential-required and unsupported authentication remain typed, sanitized
states. Pending rows remain focused and selection survives within a scan
generation. Network Controls passes 17/17, WindowsNetworkProvider 31/31, and
PlatformBroker 32/32; packaged visual/controller/privacy evidence remains.

## GBA-014 — YT Music window shortcuts and transport glyphs are focus-dependent or reversed

**Evidence:** In the packaged YT Music widget, the user's controller test and
screenshot showed LB, RB, X, and Y working only while focus was on the sibling
transport Button that declared that shortcut. Moving focus to like, dislike,
shuffle, repeat, or another connected control made the intended window command
unavailable. The same screenshot showed Previous and Next rendered with their
directional geometry reversed.

**Acceptance:**

1. In the connected YT Music window, X toggles playback, LB selects Previous,
   RB selects Next, and Y refreshes from every focus target and a focusless root.
2. A remains local activation for the exact focused Button. A shortcut declared
   on a Button remains exact-focus-only and is never discovered through a
   sibling search.
3. Window-wide commands are declared once on the active scope-root container.
   A nested active scope cannot inherit or leak to the parent YT Music scope,
   and stale or mismatched scope/snapshot input remains unhandled.
4. The Previous glyph has its stop bar on the left and triangle pointing left;
   Next has its stop bar on the right and triangle pointing right. Semantic IDs,
   accessibility labels, and actions remain unchanged.
5. Focused YT Music routing tests, native pixel-orientation tests, the complete
   Release gate, and packaged controller/screenshot verification pass.

**Implementation evidence:** The connected view now attaches X/LB/RB/Y once to
the `ytmusic-root` scope container and leaves the sibling transport Buttons
without non-A shortcuts. The SDK still resolves an exact focused-node shortcut
first and then only the active scope root. YT Music passes 38/38, including
every-focus/focusless routing, focused-A ownership, and nested-scope isolation.
Native Icons passes 188 checks, including pixel assertions for the stop-bar and
triangle direction of both glyphs. Packaged hands-on controller and screenshot
verification is still required before closing.

## GBA-015 — Fixed widget regions clip and the built-in visual hierarchy is too heavy

**Evidence:** Packaged Audio Mixer and Network Controls screenshots showed
header copy, row labels, and trailing values crowded or clipped when the
scrollable content needed more room. Sliders appeared as a thick filled capsule
with a second track painted over it, while large radii, heavy typography, and
dense card surfaces made YT Music, Settings, Audio Mixer, and Network Controls
feel visually bulky.

**Root cause:** The layout engine allowed fixed headers, section labels, and
trailing metadata to use the default flex shrink behavior alongside the actual
scroll region. The renderer also painted a Slider node's background as a full
control surface before drawing the Slider track, producing two competing
surfaces instead of one thin control.

**Acceptance:**

1. Headers, section labels, fixed controls, and trailing values use
   non-shrinking allocations; only the intended scroll/content region yields
   space at compact sizes.
2. Long and localized labels truncate or wrap deliberately without hiding the
   value, state, or focused controller target at 720p and supported DPI/text
   scales.
3. A Slider renders one thin track, fill, and thumb inside a minimum 44-DIP
   focus/hit target. It has no duplicate full-height background surface.
4. Built-in theme, YT Music, Settings, Audio Mixer, and Network Controls use a
   consistent lighter hierarchy for typography, spacing, radii, surfaces, and
   focused states without weakening contrast or controller legibility.
5. Layout/style/renderer regressions and packaged screenshots pass across the
   compact, standard, high-DPI, and increased-text-scale matrix.

**Implementation evidence:** Fixed widget regions and trailing metadata now
opt out of flex shrinking while bounded scroll regions retain the available
flex. The native Slider draws a thin track within its unchanged 44-DIP
controller target instead of painting a second full control background. The
built-in platform theme and all four packaged widget themes use the revised
lighter typography, radius, spacing, and surface treatment. Declarative
Renderer passes 4,245 checks. Packaged visual screenshots are still required
before closing.

## GBA-016 — Returning to the tray must not hide the selected widget panel

**Evidence:** The earlier shell modeled the widget panel as visible only while
the widget owned controller focus. Pressing B at the widget root therefore
returned to the tray by hiding the panel, forcing A to reopen it and preventing
PS5-style preview navigation between a persistent tray and the selected panel.

**Acceptance:**

1. While the overlay is open, the selected widget panel and icon tray remain
   visible as separate regions regardless of which region owns focus.
2. An unhandled B at the widget root moves focus to the tray and keeps that
   widget visible. B on the tray closes the overlay. Nested scopes consume
   their own B and never fall through multiple levels.
3. Left/Right D-pad or left-stick navigation on the tray selects the adjacent
   widget and swaps the visible panel automatically. A enters that panel's
   controls; it is not required to reveal the panel.
4. Down from the last root-scope control returns focus to the tray, including a
   deliberate root self-loop. Directional escape never crosses a nested scope.
5. Guide remains a host-global toggle detached from either region's navigation
   graph. Tray and widget retain independent focus restoration.
6. Lifecycle distinguishes presentation from interaction: the tray-selected
   panel is `Visible`, the panel owning focus is `Interactive`, and a replaced
   or closed panel becomes `Background`.
7. Native state, routing, lifecycle, and packaged controller tests cover Back,
   tray switching, root-boundary escape, nested containment, focus restoration,
   Guide close/reopen, and rapid region changes without flicker.

**Implementation evidence:** Overlay state now tracks visible widget selection
separately from focus ownership. Root Back transfers focus to the persistent
tray; tray Back closes; tray selection swaps the visible widget; A enters its
controls; and a root-scope Down boundary returns to the tray without escaping
nested scopes. Guide remains region-independent. Lifecycle transitions publish
`Visible` for a previewed panel and `Interactive` only while its controls own
focus. Controller Navigation passes 73 checks and Declarative Renderer passes
4,245 checks. Packaged hands-on controller evidence remains required before
closing.

## GBA-017 — Scroll boundaries do not fully reveal the first/last control

**Evidence:** Current packaged hands-on testing reports that a Scroll can stop
with its top or bottom focus target only partially visible.

**Acceptance:** Focus-follow reveal includes the control, focus decoration, and
container padding at both boundaries; repeated Up/Down cannot leave the focused
target clipped, and compact/high-scale/nested-scroll regressions pass.

**Implementation evidence:** The renderer now identifies the first/last
focusable descendant of each Scroll and snaps those endpoints to offset zero or
the maximum extent while preserving nested fixed-point reveal. Declarative
Renderer passes 4,245 checks. Status remains Verifying pending packaged input
and screenshot evidence.

## GBA-018 — Tray Up should enter the visible widget

**Evidence:** A enters the already-visible widget from the tray, but Up does not
provide the spatially natural equivalent transition.

**Acceptance:** Pressed Up or upward left-stick navigation from a tray item
enters that item's visible panel at its remembered/root focus. It must not
activate a control, change selection, or escape a nested widget scope.

**Implementation evidence:** Host routing maps only a pressed Up boundary from
the tray (not a repeat or widget action) to the existing enter-widget path for
both D-pad and left stick. Controller Navigation passes 73 checks. Status
remains Verifying pending packaged controller evidence.

## GBA-019 — Controller guide wraps and clips

**Evidence:** The current packaged footer/control guide wraps and clips labels,
making button assignments visually noisy and difficult to scan.

**Acceptance:** The guide uses a compact non-wrapping hierarchy, prioritizes
context-relevant actions, truncates or adapts safely at compact/high-scale
sizes, and never overlaps panel or tray bounds.

**Implementation evidence:** The host now chooses guide density from available
width and text scale, emits one no-wrap line, sanitizes bounded labels, and
retains up to three selected-widget quick actions alongside host navigation.
Status remains Verifying pending compact/high-scale visual-matrix evidence.

## GBA-020 — Rounded panel corners clip content

**Evidence:** Packaged hands-on testing reports visible clipping around rounded
floating-panel corners.

**Acceptance:** Background, border, focus decoration, and child clips share one
inset corner geometry at every supported DPI/interface/text scale, with no
content loss or square artifact.

**Implementation evidence:** The native renderer owns a cached rounded viewport
clip/mask, preventing an opaque widget root from painting square panel corners;
a WIC pixel regression covers the mask. Status remains Verifying pending
packaged screenshots.

## GBA-021 — Display changes must refresh all dependent geometry atomically

**Evidence:** DPI, display-topology, and system-setting notifications previously
used separate partial refresh paths, allowing work-area or appearance changes
to miss an authoritative monitor/DPI placement pass.

**Implementation evidence:** The Per-Monitor-V2 host now maps visible DPI,
topology, and system-settings changes through one tested refresh policy that
recreates graphics, conditionally reapplies appearance, and performs one fresh
monitor/work-area/DPI placement. Hidden notifications defer all work to the
next open. Placement passes 108,545 checks and Targeting 34 checks. Status
remains Verifying until physical mixed-DPI migration, hot-plug, taskbar-edge,
and accessibility/theme screenshots are retained.

## Closed issues

None yet. Closed entries remain here with their acceptance evidence and commit
instead of being deleted.
