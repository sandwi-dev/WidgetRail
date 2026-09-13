# OverlayHost `main.cpp` decomposition — declined

Status: decision record; no extraction authorized
Baseline: local `main` at `6afde5dc` on 2026-08-28
Supersedes: [the DLV-255 analysis](history/overlay-host-main-refactoring-review/2026-08-28T21-24-06-07-00.md)

## Decision

`OverlayApp` decomposition is not scheduled. The candidate seams remove roughly
700 of 13,922 lines (~5%) while the only prerequisite that makes them safe —
deterministic Back/focus coverage — is itself the most expensive part and
reduces nothing. The cost/benefit does not clear.

EQ-003 stays open as a known, accepted condition rather than a planned work
item. This document exists to stop the next re-attempt from repeating DLV-273,
not to sequence one.

## The invariant DLV-273 established

This is the durable content of this page and applies whether or not any
extraction is ever attempted.

DLV-273 moved renderer, checkpoint, and committed-visual publication into a
`WidgetContentPresenter`. After a nested Back returned to the Settings root the
controller went directionally dead until an unrelated button event; the log
recorded a root paint with `input-owner=tray`, `visual-focus=none`, and
`semantic-focus=tray:settings` immediately after the matching root commit. Two
corrections — post-commit direction retention, then a bounded replay token —
each introduced new stale controller states and were also rejected.

The cause is a boundary, not a bug:

> `inputOwner`, `renderedFocusId`, `semanticFocus`, the presentation authority,
> and `lastWidgetPresentationPaintKey_` are derived from **one** frame in a
> single straight-line block ([main.cpp:13389](../../src/OverlayHost/main.cpp)) and
> committed together. `DiscardGraphicsResources`
> ([main.cpp:11063](../../src/OverlayHost/main.cpp)) clears that checkpoint
> alongside `pendingContentRenderPlan_`, `activeContentRenderPlan_`,
> `lastWidgetRenderResult_`, and `committedWidgetVisualState_`, and already
> carries a comment explaining that retained hit/focus geometry is valid only
> for the current render target viewport.

**Any seam that introduces a call boundary between deciding a frame and
publishing who owns input and where focus is will reproduce DLV-273.**

## Rejected seams

### Widget content presenter — rejected

Not deferred. The retirement decision requires that committed checkpoint and
interaction publication stay in the accepted owner. A future proposal needs a
new decision record, not a reference to this page.

### Device resource set — rejected on inspection

Proposed during this review and withdrawn after reading the code.
`EnsureGraphicsResources` is not a device-resource function. It calls
`RebuildShellStyles` at the top ([main.cpp:10910](../../src/OverlayHost/main.cpp)),
derives ~13 colors through a fallback chain, creates 12 brushes and 4 text
formats, then writes `panelCornerRadius_`, `trayCornerRadius_`,
`trayItemCornerRadius_`, and `focusOutlineWidth_` at the bottom. Style rebuild
and device creation are interleaved inside one function, so the "device objects
now, style values later" split it was based on does not exist.

### Accessibility session — rejected

DLV-255 ranked this second-easiest. Measurement does not support that: 10
fields, 22 straddling methods, zero methods touching the cluster alone. It also
publishes `semanticFocus`, one of the three values in the DLV-273 failure
signature.

### Fixed chrome, embedded media — not candidates

Both sit in code changed within the last week (`WIDGE-25`, `WIDGE-77`,
`WIDGE-80`). Extracting from actively debugged code compounds two risks.

### `WidgetRuntime` host/worker assembly split — rejected

The host half (`WidgetProcessClient`, `WidgetProcessOptions`,
`RuntimeProtocol`) touches `WidgetSdk` for exactly two types,
`WidgetLifecycleState` and `WidgetLifecyclePayload`. Both are public SDK API
([Widget.cs:152](../../src/WidgetSdk/Widget.cs)) used by eight widgets including
the installed Spotify and YT Music community packages, so relocating them to
`WidgetProtocol` is a binary break for shipped packages. The transitive
`WidgetSdk.dll` in the bridge runtime directory is a closure artifact, not a
trust or correctness issue.

## On the fan-out measurements

The cluster and fan-out figures produced for DLV-255 and for this revision
count **direct field references only**. They cannot see field access through a
method call, so any function that delegates measures cleaner than it is. The
device-resource seam above was proposed and withdrawn for exactly this reason.

Treat every published fan-out number as a lower bound on coupling, and do not
propose a seam from these numbers without reading the function bodies.

## Separately worth doing

Neither of these is refactoring work; both stand alone.

### Deterministic Back/focus coverage

Focus, controller, input, and routing account for **53 of 107** ledger
entries, 21 of them P0. **17 entries are currently blocked only on a physical
controller verdict** — that bug class is detected today by a person holding a
controller, which is how DLV-273 was caught three times across two correction
attempts.

`HostAccessibilityTests` covers nested-Back *publication* (lines 282, 301,
324). Nothing covers input ownership or directional navigation *after* the root
commit, which is the state DLV-273 actually broke. A deterministic host-level
case driving widget → submenu → Back → root, asserting against the committed
root frame that the widget remains input owner, visible focus is restored, and
the first subsequent Up and Down each act exactly once, would close the
detection gap for the largest category in the ledger.

### Field-count ceiling

A build-failing ceiling on `OverlayApp`'s field count — fields are authority,
lines are text — set at today's 148 and lowered only deliberately. Since
decomposition is declined, this is what keeps the condition from worsening; a
new field then requires either an explicit raise or a home in one of the
existing focused owners.

### Delete the source-text assertions

23 `has(` checks in `PinnedSurfaceHostTests.cpp` and two in
`WidgetBridgeCatalogTests.cpp` read `main.cpp` as a string and assert exact
substrings including embedded newlines and indentation. They assert formatting,
not behavior: they fire spuriously on reflow and stay silent on behavior
changes that preserve the text. Where one names real behavior worth keeping
(compact media X/LB/RB/LT/RT routing, embedded-media observation sequencing),
re-express it as a behavioral assertion in the owning focused test.
