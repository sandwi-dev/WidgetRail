# WinUI collection continuation — 2026-09-29

Scope: collection lifecycle and smoothness audit for the continued WinUI migration.
The existing native `ListView`/`GridView`, `IItemsRangeInfo`, and bounded indexed
source remain the right ownership split: WinUI realizes/layouts/scrolls containers;
the application owns sandboxed range authority and payload retention. No custom
layout engine or compatibility adapter was added.

## Implemented: page ownership before binding publication

`IndexedItemsSource.FetchAsync` previously called `Admit`, which synchronously
notified row bindings, before placing the incoming result in its owned-page cache.
A binding/focus callback could suspend or dispose the source at that point. Cleanup
could not see the incoming owner; publication then continued into later rows and
added a page after cleanup. A reentrant revision change could similarly publish
the remainder of the obsolete revision.

The result is now fully validated without changing rows, then installed in the
owned cache before row notifications. Each following row verifies the source is
still active, undisposed, at the admitted revision, and still owns that exact page.
Suspension/disposal therefore drains the incoming lease, and a refresh stops old
row publication while preserving the owned previous pixels until replacement.
Existing bounded requests, cancellation, native viewport, and stable slot identity
remain in use.

## Verification

- Added `IndexedSourceReentrancyScenarios`: actual WinUI dispatcher, synchronous
  row notification that suspends, disposes, or refreshes. Ten assertions cover
  stopping obsolete row publication, draining ownership, valid refresh recovery,
  and exactly-once releases.
- Included it in `IndexedSourceLifetimeScenarios` (16 existing + 10 new = 26).
- Corrected the indexed collection script's stale expected count (7) to 26.
- `git diff --check` for changed tracked files passed (line-ending warning only).
- Native build and fixture execution are coordinated by the parent agent; pending.

This fixes a concrete lifetime edge. It does not establish smooth frame pacing or
reproduce the user's physical scrolling experience. Real-provider frame-time and
controller acceptance remain separate; no real widget action or system setting
was invoked by this lane, and the running candidate was left alone.

## Explicit Grid transport integrity audit

Checked worker runtime checkpoint/materialized update admission, Bridge generated
JSON metadata, indexed lease replies, presentation-session checkpoint admission,
indexed fragment freezing, and pin projection copies. No host-owned manual
`ViewNode` copy drops `GridLayout`/`GridCell`: checkpoints use `SnapshotJson`, the
Bridge recursively generates nested contract metadata, projections use record
copies, and indexed session results invoke the shared SDK freezer. The authoring
lane owns defensive track-list copying at the DTO boundary; no redundant host
deep-copy implementation was added.

Identified a deferred-item version issue for the authoring lane: an indexed parent
can negotiate its protocol version before a Grid-only item template is materialized.
`IndexedCollectionContract.ValidateRange` correctly validates fetched items in the
parent's version context, so the authoring/version contract must account for this
without weakening validation. The SDK owner corrected indexed parent publication
to advertise the current supported protocol (64) while preserving minimum-feature
validation and lazy item materialization.

Added `GridTransportTests.cs` in the session tests: actual authenticated checkpoint
admission accepts version64 Grid and rejects pre64 Grid, and a typed indexed reply
round-trips Grid through generated Bridge metadata/framing. Focused native-MTP
session test run passed: **3 passed, 0 failed, 0 skipped**. Evidence:
`artifacts/winui-shell/grid-transport-20260929/session-grid02.binlog`.
The initial build caught a protocol callback-signature error; the SDK owner fixed
it before this successful run. No Bridge/Session/Runtime production correction
was necessary based on this audit.
