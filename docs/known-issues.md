# Known issues and active bug ledger

This file is the durable source of truth for user-visible bugs found during
local product testing. Roadmap items describe planned capability; entries here
describe behavior that is already expected to work or a current interaction
that must be replaced. A bug is not closed until its acceptance evidence passes
in the packaged Release overlay and the closing commit is recorded.

## Status vocabulary

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
| GBA-004 | P1 | Verifying | OverlayHost presentation / invalidation | The persistent icon tray flickers when moving between widgets. |
| GBA-005 | P0 | Verifying | OverlayHost controller routing | Hierarchical B routing is implemented across nested widget views, root widgets, and the icon tray; packaged controller verification remains. |
| GBA-006 | P1 | Verifying | OverlayHost presentation | All direct snapshot refreshes compare prior/next surface extents; packaged resize verification remains. |
| GBA-007 | P1 | Verifying | Declarative renderer / focus navigation | Nested fixed-point reveal and clip-feasibility filtering are implemented; packaged controller verification remains. |
| GBA-008 | P2 | Verifying | Declarative renderer | Inactive LRU offset eviction and scroll-clipped deferred focus outlines are implemented. |
| GBA-009 | P1 | Verifying | Platform diagnostics transport | First-instance ownership and mutual kernel PID authentication are implemented. |
| GBA-010 | P2 | Verifying | Platform diagnostics transport | End-to-end request deadlines and bounded client timeout validation are implemented. |

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
session ID. Removed or stale actions fail closed. The Audio Mixer Release suite
passes 15/15. This proves the provider and widget seams independently, but not
yet the exact packaged worker-to-broker path against a playing application.

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
vertical scroll containing every session row. Each row exposes its own progress,
volume-down, mute, and volume-up controls with an explicit four-way focus graph.
LB/RB/LT/RT session cycling, root shortcuts, and dashboard quick actions were
removed. Host-owned scroll offsets are keyed by exact worker instance, input
scope, and scroll ID; stable focus IDs survive close/reopen and ordinal fallback
selects the nearest enabled row after churn. SDK, native renderer/focus, and
Audio Mixer Release suites pass, including 128-session and long-label cases.

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

## GBA-004 — Icon tray flickers between widgets

**Evidence:** User-visible flicker occurs when changing the selected/open widget
in the packaged Release overlay.

**Root cause and implementation evidence:** Dashboard navigation called
`ShowOverlay()` for every selection change and then posted a snapshot refresh
that called `ShowOverlay()` again. Both paths issued `SetWindowPos(...,
SWP_SHOWWINDOW)` even when the monitor and requested surface extent were
unchanged, creating redundant HWND/DWM presentation churn around a full-window
Direct2D repaint. The host now uses a pure presentation policy: opening,
retargeting, or an extent change requests placement; selection, focus, and
same-extent snapshot changes request repaint only. `OverlayTargetingTests`
proves those boundaries and the complete native Debug suite passes. Packaged
Release visual verification is still required before closing this issue.

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
actions. Controller navigation passes 49 checks; nested Settings Back behavior
passes its focused Release suite.

## GBA-006 — View-specific surface transitions must resize

**Evidence:** Direct snapshot refreshes after handled actions and catalog
reconciliation replace the cached view before comparing presentation extents,
so a compact nested view can remain inside the previous standard-size HWND.

**Acceptance:** Every snapshot replacement compares the previous and next
resolved extents through the pure presentation policy. Equal extents repaint;
changed extents place exactly once.

**Implementation evidence:** Catalog, action-result, invalidation, and async
snapshot paths use one refresh-and-presentation helper. Targeting passes 27
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

## Closed issues

None yet. Closed entries remain here with their acceptance evidence and commit
instead of being deleted.
