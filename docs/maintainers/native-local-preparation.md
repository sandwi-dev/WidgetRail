# Local preparation and structural publication

WIDGE-294 extends the existing native impact vocabulary and retained scene; it
does not change the public SDK or permit widgets to authorize cache reuse.

## Dependencies and boundaries

`WidgetPresentationImpact.nodeEffects` records typed dependencies at the changed
node or structural parent. Atomic operations and full-snapshot comparison populate
the same representation. Child insertion, deletion and reordering invalidate the
parent; a same-ID kind replacement invalidates that node. Resolved WRSS changes
remain dependencies even when semantic JSON is unchanged. Coalescing merges node
effects alongside aggregate flags. Missing metadata is not proof of cleanliness.

A structural parent can keep its allocation when the committed node is clipped,
has definite width/height and no margins, and its resolved base style is unchanged.
Otherwise preparation uses the enclosing safe boundary or full-layout fallback.
Runtime, sequence, viewport, DPI, accessibility and presentation-source checks
remain prerequisites. Collection realization retains its separate lifecycle.

Local layout resolves the ancestor inheritance chain without preparing siblings.
It removes old descendant geometry and measurement proofs, then performs estimate
and correction passes inside the boundary. A changed boundary allocation rejects
local reuse. Unaffected nodes rebind current semantic pointers and reuse prepared
base styles; focused/pressed nodes and inspector captures still resolve styles.
This preserves current actions and accessibility identities without restoring old
semantic nodes from a layout cache.

## Publication and limits

All candidate geometry remains behind the existing renderer publication token.
Rejection retains the previous scene; changing DPI invalidates a pending local
plan. Structural updates currently request full-surface raster transport while
keeping the local preparation plan. The host opens its drawing surface before
layout can reject reuse, so limiting transport damage prematurely could publish
new geometry over stale pixels. WIDGE-295 can optimize capture/painting separately.

This is a contained-layout path, not arbitrary subtree isolation. Auto-sized
ancestors and uncertain dependencies retain full preparation. Pointer rebinding,
logical traversal and final scene assembly still visit retained nodes.

## Verification

`StructuralLocalLayoutMatchesFullRebuild` compares exact pixels, geometry and
interaction regions after insertion, deletion, reordering, replacement and text
changes. It covers percentage widths, inherited `em` fonts, 100%/125% scale,
container-font fallback, rejected publication and a pending-plan DPI change.
With 12 versus 120 unrelated siblings, insertion prepares 13 nodes in both cases;
full preparation grows from 36 to 252. Reused preparation grows from 14 to 122.
These are deterministic work counts, not live frame-rate measurements.

Bridge tests cover structural ownership alongside independent sibling WRSS changes;
session tests cover merging typed dependencies without weakening event authority.

Final focused gates: 47,239 renderer checks, bridge contract suite, 33 session
scenarios and 409 pinned-owner checks pass. The normal Release host/runtime build
passes. Evidence is retained under `artifacts/native-local-preparation`; these
checks do not claim physical compositor latency or live-overlay frame rates.
