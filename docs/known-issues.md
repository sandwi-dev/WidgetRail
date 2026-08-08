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
| GBA-002 | P1 | Verifying | Widget protocol / host placement | Per-view compact/standard/wide/adaptive surfaces and host work-area clamping are implemented; YT Music now has a 480 x 340 compact media budget, while packaged visual verification remains. |
| GBA-003 | P1 | Verifying | Audio Mixer / declarative renderer | One whole-widget controller Scroll contains every control and restores the last master, microphone, or application focus target; packaged visual/controller verification remains. |
| GBA-004 | P1 | Verifying | OverlayHost presentation / invalidation | Size-changing widget swaps now move without redraw and synchronously commit one complete frame; packaged visual verification remains. |
| GBA-005 | P0 | Verifying | OverlayHost controller routing | Hierarchical B routing is implemented across nested widget views, root widgets, and the icon tray; packaged controller verification remains. |
| GBA-006 | P1 | Verifying | OverlayHost presentation | All direct snapshot refreshes compare prior/next surface extents; packaged resize verification remains. |
| GBA-007 | P1 | Verifying | Declarative renderer / focus navigation | Nested fixed-point reveal and clip-feasibility filtering are implemented; packaged controller verification remains. |
| GBA-008 | P2 | Verifying | Declarative renderer | Inactive LRU offset eviction and scroll-clipped deferred focus outlines are implemented. |
| GBA-009 | P1 | Verifying | Platform diagnostics transport | First-instance ownership and mutual kernel PID authentication are implemented. |
| GBA-010 | P2 | Verifying | Platform diagnostics transport | End-to-end request deadlines and bounded client timeout validation are implemented. |
| GBA-011 | P0 | Verifying | Widget SDK / host focus / Audio Mixer | Focus-safe disabled/busy semantics and per-session Audio reconciliation are implemented; packaged controller verification remains. |
| GBA-012 | P1 | Verifying | Declarative renderer / component styles | Effective surface/ancestor focus clipping and non-scaling full-width defaults are implemented; packaged visual verification remains. |
| GBA-013 | P1 | Verifying | Network Controls / widget SDK | Separate Wi-Fi and Bluetooth controller views, LB/RB tab switching, explicit scan, and focused-row routing are implemented; packaged visual/controller verification remains. |
| GBA-014 | P0 | Verifying | YT Music / Widget SDK routing / native icons | Window-wide transport shortcuts and Previous/Next glyph orientation are corrected; packaged controller/visual verification remains. |
| GBA-015 | P1 | Verifying | Declarative renderer / built-in widget themes | Fixed regions no longer shrink into clipping, Sliders use a thin native track inside their controller target, and the built-in surfaces use a lighter visual hierarchy; packaged visual verification remains. |
| GBA-016 | P0 | Verifying | OverlayHost focus / lifecycle / controller routing | The selected widget panel now remains visible while the tray owns focus, with one-level Back, automatic tray preview, and root-boundary return behavior; packaged controller evidence remains. |
| GBA-017 | P0 | Verifying | Declarative renderer / scroll focus | Focus-follow now snaps the first/last focusable descendant to the true scroll extent; packaged controller verification remains. |
| GBA-018 | P1 | Verifying | OverlayHost controller routing | Pressed D-pad/left-stick Up from the tray now enters the visible widget without dispatching a widget action. |
| GBA-019 | P1 | Verifying | OverlayHost controller guide / layout | The guide is density-aware, contextual, bounded, and no-wrap; compact/high-scale visual evidence remains. |
| GBA-020 | P1 | Verifying | OverlayHost panel clipping / renderer | A cached host-owned rounded viewport clip now masks opaque widget roots; packaged visual evidence remains. |
| GBA-021 | P1 | Verifying | OverlayHost targeting / DPI | Visible DPI, topology, taskbar/work-area, and appearance changes now share one coalesced dynamic refresh policy; physical mixed-monitor/hot-plug evidence remains. |
| GBA-022 | P1 | Verifying | Settings permissions / controller navigation | Hidden package/capability pagination and redundant Back rows were replaced with bounded controller Scroll scopes and B-only Back; packaged controller/high-scale evidence remains. |
| GBA-023 | P1 | Verifying | Widget protocol / bridge / runtime | Versioned keep-alive, cooperative suspend, and bounded idle-unload residency are enforced with legacy migration, cached views, and lazy resume; packaged resource/churn evidence remains. |
| GBA-024 | P1 | Verifying | Gbar CLI / OverlayHost / WidgetBridge | Authenticated candidate-worker readiness, last-good recovery, complete bounded watching, and observable cleanup are implemented; packaged author-workflow evidence remains. |
| GBA-025 | P0 | Verifying | Controller quick actions / capability broker | A dormant non-authorizing host reservation now activates one exact-operation broker lease only at the typed call; packaged controller/media evidence remains. |
| GBA-026 | P1 | Verifying | Reference widgets / package isolation | Four built-in brokered references plus the YT Music Community addon pass real-package generic-AppContainer conformance; packaged overlay evidence remains. |
| GBA-027 | P0 | Verifying | Games & Apps / app-library broker / provider | Games & Apps replaced bundled Recent Apps with Start Menu catalog reads and exact revalidated opaque-ID launch; packaged controller/visual verification remains. |
| GBA-028 | P0 | Verifying | PlatformBroker consent migration / Settings permissions | The exact retired Recent Apps activation capability is tombstoned; unsupported/inactive details moved to a safe bounded read-only Review page, and packaged visual verification remains. |
| GBA-029 | P1 | Verifying | Settings installed-widget inventory | Installed Widgets now separates read-only Built-in widgets from manageable Community packages instead of omitting bundled first-party widgets; packaged visual/controller verification remains. |
| GBA-030 | P0 | Verifying | YT Music packaging / community isolation / local companion broker | YT Music now uses the public Community package/AppContainer/loopback/secret path without a trusted fallback; clean packaged controller and lifecycle evidence remains. |
| GBA-031 | P1 | Verifying | Widget SDK components / built-in themes / native renderer | The shared default, responsive Row wrapping, Picker, Scrubber, Toast, and protocol-v7 ActionSurface/MediaTile/AppTile exist; Games & Apps adopts the public tile/Toast APIs, while packaged scale/accessibility evidence remains. |
| GBA-032 | P1 | Verifying | GBSS / native renderer / accessibility | Stable declarative nodes now interpolate bounded opacity/scale targets with reduced-motion cancellation; packaged visual/performance evidence remains. |
| GBA-033 | P1 | Verifying | Games & Apps / catalog / host launch completion | Durable authority-scoped curation and close-after-correlated-success are implemented; broader sources, icons, classification, and packaged controller evidence remain. |
| GBA-034 | P1 | Verifying | Network Controls / controller state model | Focus/selection is separated from authoritative Wi-Fi/Bluetooth state; pair/manage actions and stable focus/scroll behavior have focused coverage, with packaged churn/hardware verification remaining. |
| GBA-035 | P0 | Verifying | Audio Mixer / capability degradation / focus | Optional device-name and microphone providers now degrade and recover independently without replacing healthy master/session controls; packaged partial-grant verification remains. |
| GBA-036 | P1 | Verifying | OverlayHost / native composition / declarative surface | The native client clears unused pixels to the layered color key and one packaged standard-viewport capture shows no opaque canvas; the broader paint/scale/contrast matrix remains. |
| GBA-037 | P0 | Verifying | Now Playing / media provider / retry | Current-state reads are independent from live subscription failure and Retry creates a fresh generation; packaged provider-failure recovery remains to verify visually. |
| GBA-038 | P1 | Verifying | Games & Apps / catalog loading / responsive text | Activation resolves only durable saved entries and Catalog loads only on Add; intrinsic layout regressions cover the clipped empty/card copy, with packaged visual verification remaining. |
| GBA-039 | P1 | Verifying | Settings permissions / responsive text / Scroll | Auto-height intrinsic leaves now retain measured wrapped height and long permission-copy scroll extent has native regression coverage; packaged visual verification remains. |
| GBA-040 | P0 | Verifying | Native declarative layout / Spotify / responsive text | Intrinsic leaves now measure height against their authored width/max-width before layout, with exact Spotify state/setup regressions at compact and 150% text scales; packaged visual verification remains. |
| GBA-041 | P0 | Verifying | OverlayHost / controller input ownership | A visibility-scoped GameInput lease keeps navigation alive when foreground activation is denied and uses exclusivity when confirmed; packaged backend/game evidence remains. |
| GBA-042 | P0 | Verifying | Spotify configuration / Settings permissions | Unsigned packages can resolve one unambiguous owning-publisher public configuration document, and Settings names all four Spotify grants; packaged authorization/revoke evidence remains. |

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
Mixer Release suites pass 15/15 and 25/25. This proves the provider and widget
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
Network publish Compact surfaces, YT Music publishes Standard with a flexible
480 x 340 minimum media budget, and Settings publishes Standard. API-1/no-hint
widgets retain the legacy surface. Native
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

**Implementation evidence:** The earlier fixed master/device/microphone region
could consume more than the host content viewport and leave its nested session
Scroll partially clipped by a non-scroll ancestor. Audio now publishes one
bounded root Scroll (`audio.root`) containing the header, master output,
sanitized device summary, microphone control, and every application row. Master
and application rows use
the same icon–Slider–percentage composition: the nonfocusable icon exposes mute
state, Left/Right changes the focused Slider's absolute volume, A toggles mute,
and Up/Down moves between rows. Each row is one stable focus target.
LB/RB/LT/RT session cycling, root shortcuts, and dashboard quick actions were
removed. The widget records the last known master, microphone, or stable
application focus target from normal controller input without consuming
host-owned B/navigation. Reopen publishes that target as initial focus; session
churn retains the same opaque session or selects the nearest surviving row.
Host-owned scroll offsets remain keyed by exact worker instance, input scope,
and scroll ID. The Audio Mixer suite passes 25/25 focused Release tests,
including the complete focus chain/restore regression, rapid absolute Slider
updates, 128-session, and long-label cases. Native renderer coverage passes
4,311 checks and walks Audio-like master/input/application geometry at the
actual 464-DIP host content height and a constrained 304-DIP height. These
focused checks do not replace packaged controller and visual verification.

**Acceptance:**

1. One bounded controller Scroll contains master output, device/microphone
   content, and every current application session without a clipped nested
   viewport.
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
4,311 checks.

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
SDK Release suite passes 41/41 and Audio Mixer passes 25/25, including the exact
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
tests pass 4,311 checks including nested non-Scroll clips, scaled root-edge
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
3. Wi-Fi and Bluetooth use separate bounded vertical controller views with
   stable IDs and independent selected-row restoration. LB/RB changes the
   active view; neither shoulder nor trigger cycles items inside a list.
4. Empty, one-profile, many-profile, long-label, radio-off, wired-only,
   connecting/failure, 720p, high-DPI, and text-scale regressions pass.

**Implementation evidence:** Network now consumes the resolved compact width
and publishes mutually exclusive Wi-Fi and Bluetooth views under one segmented
tab bar. LB/RB changes tabs from any root focus; A can also select a focused
tab. Wi-Fi and Bluetooth own distinct stable Scroll IDs
(`network.wifi.body.scroll` and `network.bluetooth.body.scroll`) and retain
their own opaque selected item, so returning to a tab restores its focus target
and host focus-follow reconstructs the visible location. A explicitly scans
from the Wi-Fi Scan control; A and X connect the exact current saved/open
focused result. No scan occurs on activation or a timer. LT/RT do not cycle
items, and dashboard quick actions remain absent. Credential-required and
unsupported authentication remain typed, sanitized states. Network Controls
passes 17/17 and WindowsNetworkProvider passes 31/31. The shared segmented-tab
theme now reserves a nonshrinking 50-DIP region around its 44-DIP tab targets.
A native renderer regression covers both the normal 364-DIP compact viewport
and a constrained 300-DIP viewport at 1.25 interface scale plus 150% text; each
tab must remain visible, focusable, controller-enabled, within the viewport,
and inset for an unclipped focus ring. Packaged split-view visual/controller/
privacy evidence remains.

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
Renderer passes 4,311 checks. Packaged visual screenshots are still required
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
4,311 checks. Packaged hands-on controller evidence remains required before
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
Renderer passes 4,311 checks. Status remains Verifying pending packaged input
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
monitor/work-area/DPI placement. Back-to-back notifications merge into one
posted message-loop refresh while retaining the strongest requested work;
hidden notifications defer all work to the next open. Placement passes 108,545
checks and Targeting 42 checks. Status remains Verifying until physical
mixed-DPI migration, hot-plug, taskbar-edge, and accessibility/theme screenshots
are retained.

## GBA-022 — Permission review hides pages and duplicates Back

**Evidence:** The packaged Audio Mixer permission screen displayed “Page 1 of
2” but exposed no visible page control. The same screen rendered a full-width
Back row even though hierarchical B already owned Back, wasting space and
adding an unnecessary focus stop.

**Acceptance:** Package and capability collections are ordinary bounded
vertical controller Scrolls with stable item IDs, true endpoint reveal, and no
shoulder-button pagination. Package, capability, and decision scopes expose B
as the only Back action. Empty or invalid permission states remain focusless
but B-recoverable, and long decision copy remains scrollable at supported text
scales.

**Implementation evidence:** All three permission scopes now use protocol-v2
vertical Scroll nodes. Package and capability rows are linked through one
stable focus graph, page labels/shortcuts/state and in-content Back Buttons are
removed, and malformed-state fallbacks retain their scoped B action without a
fake focus target. Settings passes 41/41 focused Release tests. Status remains
Verifying pending packaged controller and increased-text-scale evidence.

## GBA-023 — Background residency policy was metadata only

**Evidence:** Manifest-v1 accepted `backgroundPolicy: none|suspend`, but every
launched worker remained resident and the bridge did not distinguish the two.
There was no explicit, bounded way for an author to trade warm state for idle
memory without unsafe process suspension or an external kill heuristic.

**Acceptance:** A versioned manifest vocabulary preserves old package meaning,
defaults to keep-alive, offers cooperative suspend and explicit bounded idle
unload, and applies identically to bundled and installed workers. Unload must
serialize with operations, send Destroying, release worker/companion resources,
retain the last validated view, cancel cleanly on visibility, restore lifecycle
on a lazy new worker, and not consume crash-restart allowance. No policy may
suspend OS threads or authorize ordinary manifest-declared Background broker
work. The bounded host-granted private-state persistence exception remains
available in Background and is denied in Destroying.

**Implementation evidence:** `residencyPolicy` schema 1 validates
`keep-alive`, `suspend-when-hidden`, and `unload-after-idle` with a required
5–86,400-second bound. Legacy none/suspend map to keep-alive/cooperative
suspend, while mixed vocabularies fail closed. The bridge uses a per-widget
operation gate, generation-canceled timer, Volatile lifecycle state, cached
last-good snapshot, bounded runtime unload, and lazy lifecycle restoration.
Hidden suspended workers cannot render, publish invalidations, receive input,
or access the broker. SDK, Runtime, Bridge, Catalog, CLI, Settings, widget, and
documentation focused suites pass; status remains Verifying pending packaged
idle-memory and repeated hide/show churn evidence.

## GBA-024 — Developer mode can report a broken generation Ready

**Evidence:** The initial `gbar dev` implementation treated a host process that
remained alive for 750 ms as initialized. OverlayHost can remain alive while
showing a bridge/catalog initialization error, so that probe could stop the
actual last-good generation and label a broken one Ready. Cleanup also did not
surface a process tree or temporary generation that resisted reclamation, and
prebuilt package-directory watching omitted supporting payload/assets.

**Acceptance:** A replacement generation must publish an authenticated,
session-scoped readiness signal only after its exact development catalog and
WidgetBridge are usable. The previous generation remains active until then.
Cancellation and replacement must prove the launched process tree exited and
report unreclaimed state. The bounded, reparse-safe watch set must cover every
file packed from a package directory, including new supported subdirectories,
without watching build outputs or unrelated trees.

**Implementation evidence:** `gbar dev` builds each candidate into an immutable
generation and starts a controller/hotkey-free probe. The probe authenticates
the exact catalog, widget ID, and instance with a random nonce only after the
candidate AppContainer worker enters `Visible`, returns a protocol-validated
snapshot for that instance, and returns to `Background`. The interactive
candidate publishes readiness only after the overlay/backdrop are visible and
a fresh exact-generation bridge listing succeeds; active-start failure restarts
the prior generation. Every host starts suspended, is assigned to a CLI-owned
kill-on-close Job Object before its first instruction, and cleanup waits for
zero active descendants. Missing/forged readiness, a missing widget type, and a
persistent child/grandchild all fail closed. Bounded reparse-safe project
watching includes general MSBuild inputs and newly created directories while
excluding `.git`, `.vs`, `bin`, and `obj`. CLI passes 45/45 and all native
focused suites pass; packaged author-workflow evidence remains.

## GBA-025 — Tray quick actions cannot use brokered controls

**Evidence:** Dashboard quick actions correctly run while their selected widget
is `Visible`, while audio/network/activity/media control capabilities correctly
require `Interactive`. Promoting every selected tray widget to Interactive
would unnecessarily broaden authority; leaving the mismatch makes advertised
LB/RB/X controls fail with `lifecycle_denied`.

**Acceptance:** A pressed, declared dashboard quick action may carry an
optional host-validated authority for one exact declared capability operation.
The authority is bound to widget identity, worker session, current snapshot,
controller sequence, and bounded deadlines. Queueing must not start broker
authority: a dormant host-owned reservation may last at most 10 seconds, but
only the exact typed operation may atomically activate a one-use broker lease
lasting at most two seconds. Replay, mismatch, lifecycle loss, replacement, or
shutdown revokes the relevant stage. Neither stage enables a subscription or
promotes lifecycle. Consent and provider checks still apply.

**Implementation evidence:** `WidgetQuickAction` can name one typed capability
operation. The bridge derives a dormant reservation only from the selected
widget's cached snapshot, positive host input sequence, declared control
capability, and `Visible` lifecycle. A private SDK action context requests
activation only when that exact queued or custom handler actually invokes the
typed operation, so time spent behind an earlier action cannot consume the
two-second broker window. The dormant reservation uses a separate host-owned
monotonic clock and expires within 10 seconds; it is not broker authority. The
runtime atomically removes a matching capability, operation, input, and snapshot
reservation before its identity/PID-bound companion starts the at-most-two-
second broker lease; widget APIs cannot mint authority. A continuous bounded worker reader permits
custom async controller handlers to receive the activation acknowledgement
without concurrent pipe readers or deadlock. The broker keeps at most 16
two-second active grants, consumes each exact tuple once, and clears dormant
and active state on lifecycle, consent, process, or session teardown.
Subscriptions remain ineligible and lifecycle is never promoted. Focused SDK,
Broker, Runtime, Bridge, and Now Playing suites pass 60/60, 46/46, 33/33,
35/35, and 16/16 respectively and cover wrong operation/capability,
slow-first rapid-second input, custom async routing, replay, expiry, stale
snapshot, denial, and revocation cases.

## GBA-026 — Reference widgets lack real community-package conformance

**Original evidence:** The references had focused widget tests with injected
fake services while installed-catalog isolation tests used synthetic packages.
No acceptance test packaged each real reference, installed/enabled it, resolved
it through the generic worker host, launched it in the package-specific
AppContainer, and rendered/acted through its authenticated simulated broker.

**Acceptance:** A bounded Windows conformance suite performs that complete
path for every claimed public-SDK reference without real OS mutation. It must
assert immutable package resolution, AppContainer-required launch, exact
declared authority, valid rendered snapshots, lifecycle enforcement, and at
least one read/action path. A widget that depends on a trusted-only facility is
explicitly classified as such instead of passing by exception.

**Implementation evidence:** The shipped catalog now separates trusted worker
entries from ordered `bundledWidgets`. Settings is the only temporary Job-only
exception. Audio Mixer, Network Controls, Games & Apps, and Now Playing derive
entrypoint, publisher, permissions, memory, residency, and
styles from their real manifests and launch through the generic worker in a
capability-free package AppContainer. The Release conformance suite builds and
installs those four `.gbarwidget` packages plus the YT Music Community package,
then merges them through
`WidgetCatalog`/`BridgeCatalog`, grants simulated consent through the production
PID-bound broker companion, and executes both the installed and separately
configured bundled routes. Both forms drive lifecycle and validate rendered
snapshots. Games & Apps proves separate app-library read and launch
authority; the other built-in references exercise their safe brokered control.
YT Music additionally proves the public CLI package flow, pairing/private-
secret/Bearer path, dashboard transport, lifecycle enforcement, and absence of
a trusted fallback. The suite passes 5/5; Bridge passes 35/35. The packaged
build and host catalog contain and select no dedicated worker executable for
these references.

## GBA-027 — Recent Apps switching replaced by a scoped app launcher

**Evidence:** Selecting an observed terminal could create a new tab, a later
attempt could close the terminal, and Windows could reject foreground
activation. The feature also duplicated controller task switching while not
providing the requested installed games/app launcher.

**Acceptance:** No widget input delegates generic Windows foreground authority
to WidgetBridge. Recent activity remains read-only. A separate Games & Apps
package lists bounded installed registrations through opaque IDs and launches
only one current provider-revalidated registration through a separately
declared, consented, Interactive-only capability.

**Implementation evidence:** OverlayHost no longer calls
`AllowSetForegroundWindow` for widget controller input. The retired activation
capability, DTOs, broker dispatch/backend method, provider
`SetForegroundWindow` path, Settings copy, manifest declaration, and SDK method
remain removed. Recent Apps is no longer in the bundled catalog.

Games & Apps now uses the public generic-worker/AppContainer path with required
`system.apps.library.read.v1` and optional
`system.apps.library.launch.v1`. The SDK returns paged sanitized names, kinds,
short-lived launch IDs plus authority-scoped durable SavedIds. The broker gates
read/resolve to Visible/Interactive and launch to
Interactive, rejects path-like payloads, and never grants launch dashboard
gesture authority. The trusted provider re-enumerates immediately before
launch and requires one exact scope/identity/path/shortcut-fingerprint match,
then invokes only Shell `open` on that `.lnk` with no arguments, elevation,
working directory, or window handle. Widget/provider focused suites pass 15/15
and 13/13; SDK, PlatformBroker, Settings, and packaged
AppContainer conformance passes 5/5. The current catalog is Start
Menu-only, iconless, and conservatively Application-only; packaged overlay
hands-on evidence remains before closure.

## GBA-028 — A retired capability invalidates the entire consent document

**Evidence:** Removing the retired Recent Apps foreground-activation capability
from the closed capability vocabulary left its older durable consent decision
behind. Strict consent validation treated that entry as an arbitrary unknown
capability, rejected the complete document as `invalid_consent`, and disabled
permission review and changes even though the user's other decisions were
still current and structurally valid.

**Implementation evidence:** Consent validation now has an exact tombstone for
`system.activity.recent.activate.v1`. Loading an otherwise valid document
filters only that retired decision from the broker-visible result while
preserving its revision and every current decision. The next atomic consent
write omits the tombstoned entry from persistence. The tombstone does not make
unknown capability IDs forward-compatible: arbitrary unknown IDs, duplicate
retired entries, malformed identities, invalid decisions, and invalid document
bounds still reject the complete document and fail closed. Focused broker and
Settings tests cover current Grant/Deny preservation, the next-write cleanup,
unknown-capability rejection, duplicate rejection, and usable permission
review after migration.

Permission diagnostics are now controller reachable without making them
actionable. The package list exposes one focusable Review row; its nested
VerticalScroll contains disabled read-only rows with stable opaque IDs and
B-only return. Unsupported requests and inactive decisions share one 16-item
detail budget with an accurate remainder count. Exact publisher authorities
remain distinguishable, while all labels/accessibility text are sanitized and
bounded. If catalog projection is truncated, catalog data is invalid, or
consent is invalid, inactive classification is explicitly unavailable and no
inactive rows are shown. There is deliberately no consent cleanup action.

**Acceptance:**

1. Only the exact retired capability is migrated; it never produces broker
   authority and cannot become valid again through accidental re-registration.
2. Current decisions and the loaded revision survive migration unchanged, and
   the next atomic write removes the tombstoned entry from durable storage.
3. Arbitrary unknown capabilities and duplicate tombstones continue to fail
   closed as `invalid_consent`.
4. Settings keeps current permission rows actionable and accurately displays
   their retained decisions after migration.
5. The packaged Release Settings permission flow receives controller and visual
   verification with a migrated consent document.

## GBA-029 — Installed Widgets omits bundled first-party widgets

**Evidence:** Settings projected only the installed community-package catalog.
Manifest-backed widgets bundled with the app could appear in the overlay and in
permission review but were absent from Installed Widgets, making the inventory
look incomplete and obscuring the difference between app-owned and
user-installed packages.

**Implementation evidence:** Installed Widgets now renders one bounded
controller Scroll with explicit **Built in** and **Community** sections.
Bundled manifests supply stable read-only rows and details for identity,
publisher, version, runtime, compatibility, and required/optional capabilities.
Built-in details expose no enable/disable or version-management action, and
forged community-management actions cannot mutate built-in manifests or create
community catalog state. Community package paging, review, enablement, and
version management remain separate and unchanged. Focused Settings tests cover
multiple built-ins with an empty community catalog, read-only details, guarded
actions, and valid controller snapshots.

**Acceptance:**

1. Every discovered bundled first-party manifest appears under Built in, even
   when no community package is installed.
2. Installed community packages appear under Community and retain their
   existing review, enable/disable, compatibility, and version workflows.
3. Built-in details are explicitly read-only and cannot mutate bundled files or
   community catalog state through normal or forged actions.
4. B, Up/Down focus-follow, community LB/RB paging, empty sections, long labels,
   and high text/interface scale remain bounded in the packaged Release overlay.
5. Packaged visual/controller verification confirms the two sections are clear
   and the complete inventory is reachable without clipping.

## GBA-030 — YT Music is bundled instead of proving the Community addon path

**Original evidence:** YT Music shipped in the host-owned trusted catalog and
used a custom Job-only desktop worker with direct loopback/Credential Manager
access. That could not prove that an independent developer could package,
install, authorize, run, update, recover, and remove the same addon through the
public Community workflow.

**Required direction:** YT Music is the first Community addon integration
reference, not a permanent Built-in widget. Its local companion access must be
provided by reusable exact-port loopback HTTP broker operations, and optional
token persistence by a private per-widget secret service. The addon must not
receive ambient network authority, direct Credential Manager access, a trusted
desktop token, or another first-party-only escape.

**Implementation evidence:** The host catalog and build no longer publish YT
Music or its retired custom worker, and incremental builds remove the old
runtime directory. The addon declares `network.loopback:13091` plus optional
`storage.private-secrets.v1`, creates its production client only after public
`HostServices` attachment, and never opens a socket, reads a persisted value,
or calls Credential Manager. Its build helper stages the real payload and runs
public validate/pack/install/enable commands. Community conformance builds that
`.gbarwidget`, installs it into the current catalog model, launches the generic
worker in the mandatory AppContainer, grants declarations through the consent
store, pairs through host-side secret persistence, and invokes RB through one
exact dashboard POST lease. It also asserts no trusted catalog/build fallback.
Focused/provider tests cover bounded loopback, response sanitization, vault
round trips, broker lifecycle/revocation, SDK validation, and YT behavior.
Authenticated requests opt into host-side rejected-Bearer invalidation; an
actual 401 deletes the exact scoped slot while the dependent lease is valid,
and the widget clears local state without a second delete. Restart coverage
confirms the rejected slot is absent.

The complete conformance suite passes 5/5; YT Music and PlatformBroker focused
suites cover the typed companion path. Clean packaged controller/lifecycle testing on the
real companion remains before closure.

**Acceptance:**

1. The public CLI packages and installs YT Music into the Community catalog;
   it is absent from the host's Built-in catalog and runtime-copy list.
2. Settings displays its real package identity, publisher, version,
   compatibility, declarations, consent state, enablement, and rollback using
   the same surfaces available to another developer.
3. The generic package AppContainer launches it without a trusted-token
   fallback, while authenticated broker operations provide only its declared
   exact-port loopback requests and host-side private-secret use; values are
   never returned to the worker.
4. Auto-connect, progress interpolation, artwork, transport/like/shuffle/repeat
   feedback, dashboard quick actions, nested controller navigation, lifecycle,
   crash recovery, update/rollback, and uninstall retain executable tests.
5. A clean-machine local install and packaged Release playtest prove the full
   author workflow, controller behavior, lifecycle recovery, and removal with
   the trusted exception absent.

## GBA-031 — Default components need a minimalist visual system

**Evidence:** The built-in theme now supplies warm graphite surfaces, regular-
weight typography, smaller radii, thin borders/tracks, compact spacing, and one
inset neutral focus treatment across the semantic component classes. Audio
Mixer, Network Controls, Settings, Now Playing, and Games & Apps were retuned
against those public classes rather than private host geometry. Parser/theme
tests cover the shared defaults, but packaged screenshots across the complete
display/accessibility matrix are not recorded yet.

**Acceptance:**

1. The component/theme layer defines one restrained hierarchy for panel,
   section, row, divider, value, status, slider, and focus surfaces.
2. Minimum controller targets and non-color state remain intact while redundant
   backgrounds, radius, border weight, and typographic emphasis are reduced.
3. First-party widgets consume shared defaults without private geometry or
   focus hacks unavailable to an independent SDK author.
4. Compact through 8K logical viewports, long labels, 150% text, high contrast,
   and reduced transparency remain readable and unclipped.
5. Packaged screenshots and controller traversal verify the complete component
   set, not only one hand-tuned widget.
6. Common product composition no longer requires private widget hacks:
   `SettingsRow`, `ActionSheet`, single-select `Picker`, controller `Scrubber`,
   lifecycle-owned `Toast`, and protocol-v7 `ActionSurface`/`MediaTile`/
   `AppTile` have reviewed bounded contracts, and Rows can wrap responsively;
   per-edge borders, responsive grid, and semantic monospace retain explicit
   deferrals.
7. The default rejects web-centric stagger/ambient motion, editorial serif or
   faux-macOS chrome, and treats packaged fonts as lower-priority security-
   sensitive assets rather than a baseline dependency.

**Implementation evidence:** Settings now uses the public Picker contract for
its complete theme catalog, including selected focus restoration, disabled
invalid entries, one host-owned Scroll, scope-owned B, and no LB/RB pagination.
Its focused Release suite passes 41/41. Responsive Row wrapping and the public
controller Scrubber now have source and focused regression coverage; Spotify
uses the Scrubber instead of a private seek composition. Protocol-v7
ActionSurface supplies one clipped full-tile focus/pointer/pressed target with
bounded presentation-only descendants. Public MediaTile/AppTile and lifecycle-
owned Toast helpers plus stable theme hooks are implemented, and Games & Apps
adopts AppTile and Toast. Complete Release and packaged visual evidence remain
before ledger closure; per-edge borders, responsive grid, semantic monospace,
and broader motion remain incomplete.

## GBA-032 — GBSS transition declarations do not animate

**Evidence:** `transition-duration` and `transition-easing` now drive a bounded
native timeline for stable declarative node opacity and scale. First
observation snaps, later target changes retarget from the presented value,
durations are capped at two seconds, at most 1,024 nodes are tracked, removed
widgets are forgotten, settled content stops requesting frames, and reduced
motion snaps/cancels. Other properties and shell-level transitions remain
immediate, and packaged visual/performance evidence is still open.

**Acceptance:**

1. Documentation identifies the exact animated property set and does not imply
   that unsupported properties or shell transitions animate.
2. A host-owned bounded clock interpolates only an explicit safe property set,
   with deterministic start, interruption, retarget, and completion behavior.
3. Reduced motion makes every transition immediate and stops outstanding work.
4. Hidden/background widgets produce no animation wakeups; frame scheduling
   coalesces visible nodes and respects performance budgets.
5. Native tests plus packaged visual/performance evidence cover focus, selected,
   busy, rapid reversal, resize/DPI change, and widget replacement.
6. The transient pressed-state pipeline is implemented. Follow-on scope still
   covers true composited subtree transforms/translation and shell/widget open,
   close, and replacement transitions; none is implied by the current paint-
   only opacity/scale slice.

## GBA-033 — Games & Apps needs durable curated launch semantics

**Evidence:** The current slice safely pages executable-backed Start Menu
shortcuts into a nested Catalog where A adds/removes entries from a separate
Library view. The Library supports explicit removal and moves an exact item to
the front only after launch success. The SDK/broker/provider issue an
authority-scoped durable SavedId, resolve it to a fresh launch token, and the
widget persists curation/recent-first order through private compare-and-swap
state. A host-owned effect closes only after the exact provider success and is
rejected for stale widget generations. Icons, source-aware grouping, game
classification, and broader catalog sources remain absent.

**Acceptance:**

1. Catalog sources are explicit and bounded; supported AppsFolder and launcher
   adapters retain provider-owned opaque identities and exact revalidation.
2. The default view is a curated, user-controllable library with safe fallback
   access to other applications; curation/order use SavedId plus private state
   to persist across worker restart, and game classification is evidence-backed.
3. Icons/artwork cross a bounded broker/cache contract and cannot become an
   arbitrary package file or URL escape.
4. One successful correlated launch closes the overlay and restores normal app
   focus. Stale ID, provider failure, cancellation, or denial keeps the panel
   open with focus and bounded feedback.
5. Update/churn, duplicates, favorites/order, empty sources, and packaged
   controller/resolution behavior have deterministic coverage.

## GBA-034 — Network rows conflate focus and connection state

**Evidence:** Wi-Fi and Bluetooth share controller row/card patterns, but focus,
candidate selection, radio state, saved/paired state, and authoritative
connection state are different concepts. A focused row can look connected, a
Bluetooth row can imply an unsafe generic Connect action, and refresh churn can
jump focus.

**Implementation evidence:** Focus no longer sets selected/connected state.
Wi-Fi rows distinguish saved/open/password-required, pending, connected, and
failure presentation from the controller target. Bluetooth rows expose Pair
only for an actionable unpaired device and Windows-managed details for paired/
connected or unsupported-ceremony cases; no row claims generic Connect. Wi-Fi
and Bluetooth retain independent stable focus/Scroll IDs across tab switches,
and deterministic tests cover churn and nearest-survivor fallback. Packaged
controller churn and reversible physical pairing remain before closure.

**Acceptance:**

1. Wi-Fi focus, saved/open/password-required state, connecting, connected, and
   failure have distinct text/glyph/state cues; only supported `A` actions run.
2. Bluetooth focus, present, paired, connected, and actionable profile/pairing
   operations remain distinct; informational rows do not imply generic Connect.
3. Wi-Fi and Bluetooth tabs preserve independent last focus and scroll position
   across LB/RB switches and widget reopen.
4. Scan/device/radio churn retains the exact stable item when possible, then a
   deterministic nearest survivor, without moving to a destructive action.
5. Empty, denied, radio-off, hardware-off, stale-ID, and packaged controller/
   accessibility cases pass.

## GBA-035 — Optional audio-provider failure can break the mixer

**Evidence:** Audio Mixer composes master output, sessions, default-device
names, and microphone controls from separate capabilities/providers. A denied,
revoked, or unavailable optional slice must not blank healthy sections or
rebuild focus onto an unrelated master control.

**Acceptance:**

1. Each audio section owns independent loading, healthy-empty, denied, revoked,
   unavailable, and retry state.
2. Master and application controls remain usable when microphone or device-name
   services fail; optional loss never becomes a whole-widget error.
3. Focus stays on the same semantic control when its section survives, moves to
   a deterministic nearest control when it disappears, and never jumps to an
   unrelated `+` action after a mute/slider command.
4. Revocation, endpoint generation changes, session churn, partial recovery,
   and rapid lifecycle transitions are race-tested.
5. Packaged controller testing verifies partial grants and real provider
   failure where safely reproducible.

**Implementation evidence:** Device-name and microphone enrichment now run in
independent, event-driven section workers after the required master/session
snapshot is live. Each section records loading, healthy, healthy-empty, denied,
revoked, or unavailable state; provider details remain sanitized; and an
explicit section retry reopens only that capability stream. Optional loss
clears only data owned by that grant. Master output and application rows keep
their stable IDs and remain actionable, while a disappearing microphone moves
semantic focus to its same-section retry or the nearest surviving session
instead of rebuilding focus at the master control. Focused deterministic tests
cover startup denial, live revocation, unavailable-to-healthy recovery,
endpoint/session churn, retry, and lifecycle generation replacement. The
remaining gate is packaged controller testing with safe real grant revocation.

## GBA-036 — Native client canvas was opaque outside widget surfaces

**Original evidence:** Packaged screenshots showed a rectangular opaque native
client/canvas region extending beyond the intended rounded content surface.
That region masks the dimmed application backdrop and makes content-sized
widgets look like they are embedded in an extra black window. This is distinct
from the intentional full-screen dimming backdrop and is not a theme color
choice.

**Implementation/current evidence:** The native client now clears unused
pixels to the exact layered color key rather than the theme canvas color. The
packaged Release capture after `8e8c90a` shows the live Now Playing surface over
the dimmed application without the former extra client rectangle. That single
standard-viewport capture does not yet cover initial paint, replacement,
compact/wide, reduced-transparency, high-contrast, or DPI changes, so the issue
remains Verifying rather than Closed.

**Acceptance:**

1. Pixels outside the declared shell/widget surface remain transparent so the
   host backdrop is visible; no extra rectangular canvas, inset, or border
   appears during initial paint, resize, widget replacement, or animation.
2. Rounded clips affect only the intended content surface and retain correct
   antialiasing at supported DPI/interface scales.
3. Clear/present/composition paths are alpha-correct and do not rely on a theme
   painting over the defect.
4. Native deterministic coverage plus packaged screenshots exercise compact,
   standard, wide, reduced-transparency, and high-contrast presentations.

## GBA-037 — Now Playing retry did not recover provider failure

**Original evidence:** A packaged run rendered the Now Playing provider-failure
surface. Activating **Try again** left the same failed state with no observable
new load, recovery, or changed bounded diagnostic. A focusable Retry control
that acknowledges input without beginning a real attempt is a functional
failure, not merely missing polish.

**Implementation/current evidence:** Subscription startup and current-state
read are separate failure domains. A failed subscription can still publish a
valid current snapshot, a later live-read failure preserves the last valid
snapshot, and Retry cancels the old attempt and creates one fresh bounded
generation. Pending transport feedback is target-specific: invoking Play/Pause
no longer disables or flashes Previous/Next, while the widget-level single-
flight gate still prevents overlapping commands and clears target Busy state
after both success and failure. Focused media tests cover those paths. The post-`8e8c90a` packaged
capture shows a live GSMTC session, proving the normal provider path on this
machine, but it does not reproduce failure followed by recovery; status remains
Verifying.

**Acceptance:**

1. Retry starts exactly one fresh bounded read/subscription attempt while the
   widget is active, publishes Busy feedback, and cannot overlap itself.
2. A recovered provider replaces the failure surface with the authoritative
   current sessions; healthy empty is distinct from provider unavailable.
3. Continued failure clears Busy, retains focus on Retry, and shows one safe
   actionable status without exposing native details or silently succeeding.
4. Lifecycle cancellation, consent revocation, worker restart, provider loss/
   recovery, and repeated controller activation have deterministic tests plus
   packaged reproduction evidence.

## GBA-038 — Games & Apps eagerly loads catalog and clips text

**Evidence:** The original packaged viewport showed eager Start Menu loading
and clipped card/status copy. The widget now resolves only saved entries at the
root and loads the catalog after **Add applications**. Library and Catalog use
vertical full-width rows: the icon and two-line application name share one
Button focus target instead of outlining an inner label. The native column
allocator preserves measured height for auto-height intrinsic wrapped leaves.
Focused lifecycle/layout regressions are green; refreshed packaged visual
evidence remains outstanding.

**Acceptance:**

1. Opening the root restores only the durable curated Library data needed for
   that surface; broad Catalog enumeration begins lazily when the user opens
   Add applications (or through another explicit bounded refresh).
2. Catalog loading, paging, cancellation, and retry do not blank or reorder the
   existing Library and do not run while Background merely because the worker
   is resident.
3. Titles, type/status, counts, help, and prompts fit or reflow at compact/wide
   surfaces and 150% text without clipping or covering focus cues.
4. Vertical focus-follow reaches the first/last complete row and preserves
   separate Library/Catalog focus across nested B return.
5. Automated lifecycle/layout tests and packaged screenshots cover empty,
   populated, loading, failure, long-name, and maximum-page states.

## GBA-039 — Permission descriptions did not reflow or scroll fully

**Original evidence:** Settings permission detail screenshots showed long
capability descriptions running beyond their row/content allocation. Text can
be clipped before the next focus target, and the Scroll extent follows focus
rows rather than guaranteeing the full description is readable.

**Implementation/current evidence:** The native column allocator now preserves
measured intrinsic height for auto-height wrapped leaves, and Settings
permission detail composition no longer forces the old clipped allocation.
Native layout/renderer regressions cover long wrapped leaves and Scroll extent.
A packaged long-description controller traversal across the supported scale
matrix has not yet been captured, so status remains Verifying.

**Acceptance:**

1. Capability name, required/optional decision, and long description use a
   bounded responsive row/detail layout with explicit line wrapping and no
   overlap at supported interface/text scales.
2. Controller Scroll can reveal the complete first and last description as
   well as every decision control; fixed footer prompts never cover content.
3. Focus geometry remains inside the surface when a row grows, and B returns
   one scope without a redundant Back row.
4. Unknown/unsupported/inactive declarations use the same safe layout and
   bounded/truncated accessible strings.
5. Tests cover longest valid/localized copy, all decision states, compact and
   wide viewports, 150% text, high contrast, and packaged controller traversal.

## GBA-040 — Authored text width was applied after intrinsic height measurement

**Original evidence:** Spotify's centered Client-ID state and setup view showed
titles, details, and setup steps vertically clipped inside otherwise spacious
cards. Similar failures repeatedly appeared when a text node declared a
`max-width`: the host measured its height against the wider parent, then
clamped only the resulting width. DirectWrite therefore painted wrapped lines
into a box whose height still described the pre-clamp single-line layout.

**Implementation/current evidence:** The native layout engine now applies an
intrinsic leaf's explicit/max outer width, minus padding, to the measurement
constraint before asking the renderer for line metrics. The auto-height leaf
retains that reflowed height through flex allocation. Exact native regressions
cover the Spotify Client-ID state card and setup card at compact width and at
150% text, in addition to the generic centered-state and permission Scroll
coverage. The full native Release suite is green at milestone `9f1af0b`;
refreshed packaged screenshots remain outstanding, so status remains Verifying.

**Acceptance:**

1. Text and labeled controls with authored `width` or `max-width` measure their
   intrinsic height at the effective content width, including padding.
2. Centered state-card title/detail/action flow does not overlap or clip at
   compact and standard surfaces through 150% text scale.
3. Spotify setup title, instructions, exact redirect URI, command, and Done
   action remain fully readable/reachable without widget-specific spacer or
   margin compensation.
4. Width constraints, flex shrink, explicit fixed height, max-lines, Scroll,
   and focus visibility retain deterministic native regression coverage.
5. Packaged Release screenshots confirm Client-ID, setup, long/localized copy,
   and supported DPI/interface/text scale combinations.

## GBA-041 — Foreground-only controller polling could suspend a visible overlay

**Original evidence:** The packaged overlay intermittently stopped responding
to controller navigation while still visible and did not reliably take priority
over the application behind it. Diagnostics showed foreground-acquisition
failures followed by deliberate polling suspension. A no-activate desktop
overlay cannot treat transient Win32 foreground ownership as its sole
navigation lease.

**Implementation/current evidence:** Showing the overlay now creates one
explicit visible-controller lease. GameInput combines background input for
reliable navigation with foreground-exclusive arbitration whenever Windows
confirms foreground ownership. Show performs at most one bounded activation
attempt; polling no longer retries focus stealing or suspends merely because
activation was denied. Hiding ends the lease, and external foreground activation
still closes the overlay. Native ownership tests are green. Desktop APIs still
cannot universally suppress separate XInput, Raw Input, HID, Steam Input, or
virtual-controller delivery, so packaged game-by-game evidence remains open.

**Acceptance:**

1. Navigation remains responsive for the complete visible lifetime, including
   when Windows denies activation or briefly reports another owner.
2. Confirmed foreground uses GameInput exclusive arbitration; denied activation
   uses the documented background-shared path without focus-steal loops.
3. Alt+Tab/external activation closes the overlay and ends controller reads.
4. Diagnostics distinguish foreground-exclusive, background-shared, hidden,
   and unavailable paths without logging controller data.
5. Packaged trials document any input backend needing a future opt-in
   interception layer.

## GBA-042 — Spotify public configuration and permission metadata diverged

**Original evidence:** `gbar config set` stored the Spotify Client ID under the
manifest publisher, while the unsigned installed worker ran under its sealed
content-digest authority. Reloading therefore remained on **Client ID
required**. Settings also rendered all four supported Spotify grants as
**Unsupported capability** because their display metadata was missing.

**Implementation/current evidence:** Non-secret configuration resolves an
exact runtime-authority document first, then permits an unsigned authority to
read one unambiguous declared-publisher document whose namespace owns the
package ID. Ambiguous matches fail closed. Consent, private state, OAuth tokens,
and credentials retain exact digest authority. Spotify Setup **Done** performs
a serialized bounded fresh configuration read without starting OAuth, and
Settings has names/descriptions for all four Spotify capabilities. Focused
configuration, provider, widget, and Settings tests are green; packaged live
authorization confirmation remains.

**Acceptance:**

1. The source-tree CLI command runs without a PATH install, and a successful
   write becomes visible after **Done** without restarting the bridge.
2. Only one owning declared-publisher document can resolve; ambiguous or
   unrelated documents do not cross package boundaries.
3. Client secrets and OAuth tokens remain outside public configuration.
4. Settings shows accurate names, descriptions, required/optional state, and
   decisions for all Spotify capabilities.
5. Packaged testing covers configure, digest update, Done refresh, connect,
   revoke, and malformed/ambiguous recovery.

## Closed issues

None yet. Closed entries remain here with their acceptance evidence and commit
instead of being deleted.
