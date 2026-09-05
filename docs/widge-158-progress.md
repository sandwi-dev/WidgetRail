# WIDGE-158 progress, rejection record, and clean-main restart

Snapshot date: 2026-09-05

This is a reviewer-owned implementation record. Plane remains the live source of
assignment state, queue order, labels, and delivery disposition. The repository
history and the preserved worktrees named below are evidence only.

## Current disposition

- Plane item: **WIDGE-158**.
- New objective: correct the original cursor implementation's focus memory.
- Production-code restart baseline:
  `226e021337c7954a07a5f57a71417c7f08cbf422`, tree
  `4517e1284593877100f70822a894bcbfb168fe19`.
- Reviewer-history checkpoint:
  `13f56afe48fc30423b3630e32a8df757f8dd149b` adds only this document on top of
  that code baseline. A fresh implementation worktree may begin at the reviewer
  checkpoint after verifying that its only delta from the production-code baseline
  is `docs/widge-158-progress.md`.
- Integration state: none of the WIDGE-158 implementation branches described in
  this document are integrated into `main`.
- Prior candidate disposition: the complete indexed/grid-aware virtualization,
  cursor-redesign, Bridge, and native-renderer stack is **rejected**.
- Preservation rule: do not delete, amend, rebase, clean, merge, cherry-pick, or
  otherwise reuse the rejected worktrees. They remain available only for source,
  log, and artifact evidence.
- Background animation work: removed from WIDGE-158 and tracked as
  **WIDGE-184 — Move BackgroundSurface crossfades onto compositor-owned visuals**.
  WIDGE-184 is related to WIDGE-158 but is not a dependency and must not be started
  as part of the cursor restart.

The user deliberately chose a clean restart because current `main` did not show the
new paging, repaint, focus-jump, lifecycle, or background-transition regressions.
The only product defect to carry into the restart is the original cursor's failure
to remember the user's focus correctly.

## Clean restart contract

The next implementation attempt must begin in a new, clean, isolated worktree whose
HEAD is the exact accepted `main` baseline above. It must not be based on any prior
WIDGE-158 branch and must not copy a prior WIDGE-158 diff.

### In scope

1. Reproduce the focus-memory failure against the original cursor implementation on
   `main`.
2. Identify which existing owner loses or fails to record the focused item.
3. Make the smallest coherent generic correction needed to remember that focus.
4. Keep focus memory private and non-visual when changing the remembered key does
   not change the current presentation.
5. Restore the same focused element when it is still valid and present.
6. Use a deterministic fallback when the remembered element was removed or no
   longer belongs to the current query or route.
7. Preserve current `main` paging, render-tree shape, provider behavior, layout,
   scrolling, background rendering, Bridge behavior, lifecycle, and add-on behavior.
8. Produce a test candidate before running focused automated tests, as requested by
   the user.

The implementation agent must first distinguish the exact failing focus case on
`main`. Potentially relevant paths include cursor anchor memory, route-specific
Home/Browse memory, child-page Back memory, and overlay close/reopen memory. These
are investigation leads, not permission to redesign all of them. Only the
reproduced current-main failure is implementation scope.

### Explicitly out of scope

- Indexed or grid-aware cursor virtualization.
- Host-requested collection windows or a new cursor pull protocol.
- `CursorViewportObservation` or any equivalent render-to-widget feedback loop.
- New `FirstItemIndex`, `TotalItemCount`, window generation, or window-change
  protocol semantics.
- Splitting the cursor into retention and presentation types.
- Replacing the original Playnite cursor with separate Home and Browse cursor
  resources unless the focus defect cannot be fixed without that product change and
  the user approves the expansion first.
- Moving native update materialization between threads.
- New native incremental layout, damage, or scroll-translation paths.
- WidgetBridge invalidation revision changes.
- BackgroundSurface rendering, crossfade scheduling, or compositor architecture.
- Artwork cache, decode, residency, or ceiling changes.
- Playnite-specific native-host behavior.
- Importing any prior WIDGE-158 commit, test oracle, package version, or generated
  artifact.

If fixing focus memory would require crossing one of those boundaries, the agent
must stop with source evidence and request reviewer disposition.

## What the rejected implementation attempted

The rejected work was much broader than the original focus-memory defect. It tried
to turn the cursor into a large-collection virtualization framework shared across
the SDK, protocol, runtime, Bridge, native host, renderer, and multiple widgets.

### Indexed and grid-aware presentation windows

The design added or expanded:

- logical first-index and optional total-count metadata;
- query and request/window generations;
- `Replace`, `Append`, and `Prepend` window-change claims;
- separate horizontal-rail and responsive-grid geometry;
- visible-range and measured row/column observations from the host;
- bounded overscan and presented-node ceilings;
- SDK retention of raw provider-page segments while publishing a smaller UI
  projection;
- separate Playnite Home and Browse cursor resources;
- host-side validation of directional window lineage;
- anchor and scroll-position reconciliation across structural window changes;
- dynamic artwork pinning for the projected window.

This produced a two-process control loop:

1. The widget chose and published a bounded item window.
2. The host laid it out and reported the visible/focused range.
3. The widget recomputed and sometimes republished the window.
4. The host laid out the successor and reported again.

Considerable classification, hysteresis, generation, anchor, and stale-result logic
was added to make that loop converge. The complexity and the number of new failure
modes were disproportionate to the original focus-memory problem.

### Later platform corrections

The rejected stack also accumulated native changes intended to mitigate the runtime
cost of the new dynamic presentation tree:

- materialize atomic presentation updates on the coordinator worker rather than the
  UI thread;
- keep an immutable shared base snapshot and revalidate it before final admission;
- merge compatible focus and background-animation damage;
- use retained layout for bounded focus/scroll paints;
- translate one exact retained scroll owner rather than recomputing all layout;
- prefer an exact surviving collection key before a generic overlap key;
- add detailed WIDGE-158 timing and collection-reconciliation diagnostics.

Those ideas may be useful for future independent work, but none is part of the clean
cursor-focus restart.

### SDK publication corrections

The final rejected widgets lineage tried to:

- classify only actual edge extension as `Append` or `Prepend`;
- classify pure trims as `Replace`;
- prevent a request from publishing the opposite directional change;
- keep Playnite data across overlay reactivation;
- coalesce an observation-derived anchor/window publication with its automatic
  adjacent fetch so one controller move would not publish twice;
- suppress cursor-specific busy-start visual invalidation while retaining the
  operation's internal busy state.

The last coalescing candidate was produced before its tests were closed. It is not
approved for reuse.

## Rejected lineage and retained evidence

### Principal worktrees

| Purpose | Worktree / branch | Preserved state |
| --- | --- | --- |
| Original indexed/grid implementation | `.../widge158` / `codex/widge-158-indexed-grid-virtualization` | Rejected, not integrated |
| Cursor-retention continuation | `.../widge158-cursor-retention` / `codex/widge-158-cursor-retention` | Rejected, not integrated |
| Latest widgets diagnostic/runtime work | `.../widge158-diagnostic-runtime` / `codex/widge-158-diagnostic-runtime` | HEAD `0d8592ef6b9e8a6c071d90698dfa2862f54f5fd1`; one unstaged tests-only file preserved |
| Platform correction | `.../widge158-platform-correction` / `codex/widge-158-platform-correction` | HEAD `21bc102095fcc4afb5acfcabd7c29972d18045e3`; one unstaged superseded validator experiment preserved |
| Coherent physical candidate | `.../widge158-coherent-candidate` / `codex/widge-158-coherent-candidate` | HEAD `43523cf222e66638c64f9794707ce8bf2425d74f`; rejected |
| Native diagnostic branch | `.../widge158-raster-diagnostic` / `codex/widge-158-raster-diagnostic` | HEAD `d03991b83f2667650f17dd850e553e5a54ef18d6`; diagnostic only |

The full absolute worktree paths remain available through `git worktree list`.
This document intentionally uses shortened table paths so it does not turn one
agent's temporary directory layout into an implementation instruction.

### Important late commits

| Commit | Evidence value | Disposition |
| --- | --- | --- |
| `4aa1b1fc4dea5636a61839ca4cdb2a97fe1a9111` | Earlier composed cursor-retention candidate | Rejected |
| `b7eaa7c5d0637132d31134c0cbc3bdc4f632d30d` | Earlier platform structural/reopen correction | Rejected |
| `d03991b83f2667650f17dd850e553e5a54ef18d6` | Detailed WIDGE-158 runtime timing instrumentation | Diagnostic only |
| `fd9b04cb` | Diagnostic Playnite `0.2.74` packaging point | Rejected |
| `cb98aca931265d0c6a089c87cdb14f267ba84e8e` | Shared cursor-lineage and Playnite reactivation changes | Rejected |
| `f618600e007052f9aa33591df7c05d71dc39f5ff` | Request-direction fail-closed correction | Rejected |
| `21bc102095fcc4afb5acfcabd7c29972d18045e3` | Worker materialization and retained-layout platform candidate | Rejected |
| `1cc3381000acd3faf1c79c355dff8b7b724503e6` | Pure-trim directional-classifier correction; Playnite `0.2.76` | Rejected |
| `0d8592ef6b9e8a6c071d90698dfa2862f54f5fd1` | Observation/prefetch coalescing; Playnite `0.2.77` | Rejected; tests incomplete |
| `43523cf222e66638c64f9794707ce8bf2425d74f` | Last coherent 0.2.76 physical composition | Rejected |

The preserved platform worktree has an unstaged, superseded
`WidgetSessionCoordinator.cpp` experiment with retained diff hash
`9f35201873dbd43205773ce870b6a98047f00a2d`. It must not be staged, discarded, or
used in the restart.

The final rejected Playnite `0.2.77` archive has SHA-256
`5725C6672DD35259F5542FAA5CB7BB81B0E1FBE121A69307FE25567F2B7465B8`.
It was built successfully but was never installed or physically accepted.

## Physical findings and pitfalls

The broad redesign repeatedly passed focused component tests while producing
product regressions in the packaged overlay. That is the central delivery lesson.

### Render/projection feedback loop

The first large physical failure alternated raw retained pages and projected UI
windows after provider paging had already completed. The host and SDK kept creating
new window generations for what should have been one settled viewport. This proved
that component tests were insufficient without a render -> observation -> projection
-> render fixed-point scenario.

### Duplicated geometry and wrong identity

The host initially derived logical positions from indices rather than the exact item
keys beside the rendered geometry. Playnite could filter or reorder a window, so the
reported indices named the wrong records. Separately, item size and item pitch were
confused, and the same grid geometry was declared in style, widget code, and native
logic. Small discrepancies accumulated into visible scroll drift.

### Pure trims were mislabeled as directional changes

Playnite `0.2.75` failed immediately on open with:

> The widget returned an invalid or stale virtual collection window transition.

The exact rejected transition kept the same first index and merely trimmed the end
of the initial window, but the SDK labeled it `Prepend`. Native validation correctly
requires `Prepend` to add items at the leading edge. Playnite `0.2.76` restored the
extension-only meaning of `Append`/`Prepend` and classified pure trims as `Replace`.

### Focus could disappear between two publications

In the `0.2.76` Home rail trace, moving 7 -> 8 and 8 -> 9 generated:

1. the local focus/scroll paint;
2. a same-count anchor-only cursor publication that temporarily removed native
   focus and forced a complete repaint;
3. a second publication for the actual appended item that restored focus and forced
   another complete repaint.

Moving 9 -> 10 did not approach another cursor edge, produced no cursor publication,
and looked normal. The worker performed one provider operation; the duplicate visual
updates came from SDK invalidation boundaries, not duplicate controller input.

The `0.2.77` candidate tried to collapse that intermediate publication. Its focused
tests were still being corrected when the entire stack was rejected. The preserved
tests-only diff is not restart input.

### Close/reopen and page-retention regressions

Physical candidates at different points showed:

- focus jumping to the first item when reopening while positioned beyond the ninth
  item;
- only the current page surviving close/reopen while an earlier page reloaded;
- the Home rail shifting toward the start after every close/reopen until the focused
  item reached the viewport edge;
- a stale virtual collection error immediately upon opening the widget.

The last `0.2.76` composition physically fixed the repeated Home rail shift by
preserving an exact surviving collection key and scroll position. That isolated
sub-behavior does not justify accepting the stack that introduced the problem.

### Favorite/category updates became stale after reactivation

One candidate accepted Playnite mutations, but after closing and reopening the
overlay the current widget presentation stopped updating. Favorite and category
changes were visible only after a widget reload; both the menu options and Game
Details were stale. The issue was broader than missing feedback on one control.

This exposed the danger of changing lifecycle/invalidation behavior while also
changing cursor presentation. The restart must not touch those systems unless the
current-main focus reproduction proves they own the loss.

### Version skew caused global invalidation errors

Installing newly built add-ons against a mismatched host/runtime produced
`WidgetBridge invalidation has an invalid revision` across widgets. Subsequent
physical candidates were installed and launched only as coherent host plus Playnite,
Spotify, and YouTube sets. The clean-main baseline must likewise use add-ons built
from the exact same `main` source as the launched host.

### Focus, paging, and navigation defects exposed during the stack

Other reported regressions included:

- Browse sometimes failing to load the next row until the user moved up and down;
- `StopFocusLeft` on the Hero rail allowing focus to reach Refresh;
- the guide briefly reverting from page-specific content while scrolling;
- X favorite/category UI appearing unchanged in a reactivated session;
- removing the Favorites menu row causing the poster rail to move vertically;
- page-boundary focus and reopen behavior depending on item position.

These are historical warnings, not new restart scope. Current `main` must be treated
as the product baseline, and only a defect reproduced there may be changed.

## BackgroundSurface finding moved to WIDGE-184

The final Browse trace separated background-animation architecture from cursor
correctness:

- the crossfade started at `02:23:40.130`;
- a pure leading cursor-window trim changed 28 presented items to 16;
- native materialization was already off the UI thread;
- the incremental planner rejected the `Replace` window for bounded structural
  layout and performed a 52.285 ms complete raster, including 42.221 ms layout;
- there were zero bitmap creates or evictions and DirectComposition commit took
  0.569 ms;
- transition progress 0.478 was sampled but not presented;
- the first presented frame arrived 140 ms into the 400 ms fade at progress 0.725;
- later paint-only frames were smooth.

The durable architectural proposal is to place outgoing/incoming background visuals
beneath Content in the existing overlay DirectComposition tree and let the compositor
animate opacity. The existing full-monitor backdrop remains the black dimmer and
click-to-dismiss owner. This work is WIDGE-184 and must not be used to broaden the
clean WIDGE-158 cursor restart.

## Focus-memory lessons worth carrying forward

These are conceptual lessons, not reusable code:

1. A remembered cursor key is state, not necessarily a presentation change. Merely
   learning that focus moved should not force a new widget snapshot when the visible
   UI is already correct.
2. The host remains the current focus and scroll owner. The widget or SDK may remember
   a stable item key for later restoration, but it must not fight current host focus.
3. Route memory is route-specific. Home focus must not become Browse focus, and a
   child route's remembered focus must not be confused with the parent element that
   receives focus immediately after Back.
4. `WidgetNavigator.TryHandleBack(action, sourceFocusId)` uses `sourceFocusId` for
   the element focused on the page being left. It remembers that element in case the
   user enters the same child route again. The parent destination is separately
   restored from the return-focus value recorded during forward navigation.
5. Current `main` contains a Playnite `(Sequence, ScopeId)` raw-input correlation map
   because a synthesized shortcut action identifies the shortcut owner rather than
   carrying the focused element at dispatch. That is evidence of a missing or awkward
   SDK seam. It is not permission to add the entire rejected cursor protocol.
6. A removed remembered item needs a deterministic fallback. Stale focus must never
   create a loop, load an unrelated page indefinitely, or move focus to a control in
   another route.
7. Exact current-main packaged behavior is the acceptance baseline. Unit tests alone
   cannot establish lifecycle, focus, scroll, or visual correctness.

## Verification order for the restart

1. Build `main` and package every installed test add-on from that exact source.
2. Install/select/enable the coherent main packages and visibly launch the exact
   hashed main Release host.
3. Physically reproduce the original focus-memory defect and record the smallest
   action sequence.
4. Implement only the source-proven focus-memory correction in a fresh worktree from
   the exact current main baseline.
5. Produce and report the new immutable test candidate before automated test work.
6. Physically verify the exact reproduction first.
7. Add/run focused deterministic tests for the corrected focus owner and fallbacks.
8. Review exact scope and ancestry before any integration.

Do not merge or push a restart candidate until the user accepts the physical focus
behavior and reviewer verification confirms that current-main behavior outside that
focus path remains unchanged.
