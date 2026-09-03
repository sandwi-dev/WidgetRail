# WIDGE-178 unified media session implementation plan

This is the living execution plan for Plane item **WIDGE-178, Unify embedded media sessions and presentation ownership**.

For this item only, this file is implementation-owned after assignment. The platform implementation task must update it continuously. It is not a retrospective document and it must not be left stale until the end. Its purpose is to preserve exact decisions, completed work, failures, evidence, and remaining work across context compactions.

## Status ledger

| Field | Current value |
| --- | --- |
| Work item | WIDGE-178 — Unify embedded media sessions and presentation ownership |
| Lane | Platform; serialized SDK, Bridge, host, and bundled-widget integration |
| Production source baseline | `4a5aabb9a9dcf8a71efd0ddde07111d71df9db78` on local `main` |
| Assignment baseline | Record the exact plan-bearing `main` commit before creating the implementation worktree |
| Implementation owner | Standing platform implementation task |
| Branch | Not created yet; record immediately after worktree creation |
| Worktree | Not created yet; record the absolute path immediately after creation |
| Current phase | Reviewer plan and dispatch preparation |
| Last completed checkpoint | Option B selected; WIDGE-178 created; WIDGE-54 accepted and integrated |
| Next checkpoint | Inventory exact consumers, create isolated worktree from the assignment baseline, and update this ledger |
| Last updated | 2026-09-03 by reviewer after source inventory |

Progress marks used throughout this file:

- `[ ]` not started
- `[~]` in progress
- `[x]` completed with evidence recorded
- `[!]` blocked or stopped; record the exact red and owner
- `[-]` deliberately removed, superseded, or out of scope; record why

## Compaction recovery procedure

After every context compaction, restart, or handoff, do these steps before changing source:

1. Read this entire file, including the latest checkpoint, red/correction log, changed-file ledger, and remaining unchecked tasks.
2. Confirm the absolute worktree path, current branch, `git status --short`, `git log -5 --oneline --decorate`, and the diff from the assignment baseline.
3. Reconcile the repository state against this file. Source and test evidence win over an old checkbox; correct this file immediately if they disagree.
4. Resume only the current `[~]` phase or the first valid `[ ]` dependency after it. Do not repeat a completed gate merely because context was compacted.
5. Update the status ledger and checkpoint before doing more work.

The implementation task must update this document:

- after every bounded implementation slice;
- whenever the contract, file scope, removal list, or phase order changes;
- after every genuine red and after each attempted correction;
- before every build, test run, package operation, commit, terminal report, or stop;
- before asking the reviewer for a decision;
- at completion with exact commits, artifact identity, hashes, residual risks, and physical acceptance steps.

Do not erase completed evidence or failed attempts. Append concise corrections so another context can tell what happened and what remains.

## Locked product and architecture decisions

These decisions came from the user and are not open design questions for this implementation:

1. Use one in-process, multi-session media manager. Do not introduce a separate media service or process.
2. Preserve one shared WebView2 environment and browser process group. Each resident media session owns its own composition controller and document inside that shared environment. This does not promise a single Windows renderer process; WebView2 may maintain a process group.
3. Support multiple widgets playing independent media sessions concurrently. Retaining or presenting one session must not tear down another session. Keep the existing four-session residency bound. Admission of a fifth session remains an explicit failure; do not silently evict a playing session.
4. A session has exactly one presentation state at a time:
   - `Parked`
   - `OverlayViewport`
   - `OverlayFullscreen`
   - `CompactPinned`
5. `Parked` means the controller/document remains resident and may continue provider-authorized playback, but has no visual composition endpoint. It is not a hidden viewport.
6. Overlay viewport, overlay fullscreen, and compact pin are presentations of the same controller/document. They are not separate media owners or reconstructed players.
7. Fullscreen and compact-pinned subsystems may continue to own their chrome, geometry, input routing, and endpoint arbitration. They must not own or infer media-session lifetime.
8. A declared session with no current `UI.MediaViewport` retains an already-resident exact session as parked. It must not instantiate a new background controller. Omitting the session declaration closes an existing session. No `RetainSessionWhenHidden` Boolean is needed.
9. Durable session identity is separate from widget render snapshot sequence. Ordinary presentation updates and media telemetry must not recreate the controller, exit fullscreen, or revoke a valid session merely because the snapshot sequence advances.
10. Geometry has three meanings:
    - `Ready`: a current admitted viewport and final valid geometry may be presented;
    - `Pending`: the session remains valid but the renderer has not produced usable current geometry, so presentation is deferred or parked without teardown;
    - `Invalid`: malformed, mismatched, non-finite, out-of-contract, or revoked geometry/identity fails closed.
11. Use one transition planner/reducer and one side-effect executor for attach, detach, retarget, visibility, bounds, clip, composition commit, parking, and teardown. Do not retain overlapping projection, fullscreen, parking, and lifetime reconcilers.
12. Preserve existing security allowlists, sealed package-resource validation, browser-exit recovery, bounded resource ownership, command-terminal guarantees, input behavior, focus behavior, and accessibility semantics unless the new ownership boundary requires a direct equivalent.
13. Make a direct breaking contract update across the repository. Do not add a legacy adapter, dual protocol, compatibility shim, deprecated aliases, fallback parsing, or a translation layer for old declarations.
14. Do not build exhaustive transition-table tests, a property/model-test framework, or broad compatibility fixtures. Add only the focused tests named in this plan and the minimum compile-time updates required by the new public contract.
15. This is one cumulative implementation run. Internal phases are checkpoints, not reasons to stop or return the work early.

## Target public contract

The implementation task must validate exact naming against current source before editing, then record any mechanical naming correction here. The semantic contract is fixed:

- Replace `EmbeddedMediaSurface` with a session declaration whose name communicates lifetime, preferably `EmbeddedMediaSession`.
- Replace `WidgetView.EmbeddedMedia` with the corresponding session property, preferably `WidgetView.EmbeddedMediaSession`.
- Keep one optional declared media session per widget view. The host manager supports multiple simultaneous sessions across widget instances.
- Keep `UI.MediaViewport(...)` as the declarative, non-interactive visual placement for the current session. Its binding is the session ID, not a second lifetime owner.
- Replace the two alternative-presentation Booleans with one closed collection, preferably `IReadOnlyList<MediaPresentationKind> SupportedPresentations`, whose current values are:
  - `OverlayFullscreen`
  - `CompactPinned`
- Inline `OverlayViewport` presentation is requested by the presence of a matching `UI.MediaViewport`; it does not need a redundant capability value.
- Remove `CompactPinnedPresentation`, `RetainSessionWhenHidden`, and `OverlayFullscreenCapable` from protocol, SDK, Bridge DTOs, native declarations, validators, samples, tests, and documentation.
- A viewport requires a declared session whose ID matches exactly.
- A declared session may contain zero viewports; that means `Parked` and is valid without an extra flag.
- A view may contain at most one media viewport for its one declared session.
- Fullscreen and compact pin may be entered only when the declared capability exists and the host has current valid command/presentation authority.
- The session key must include admitted widget/catalog identity, widget instance, runtime generation, and declared session ID. Presentation generation and snapshot sequence must not be part of durable document identity. They remain current authority/correlation inputs and may advance without replacing the session.
- Resource/configuration identity changes that cannot safely apply in place close and replace the session deliberately. Telemetry, pending-command, accessible-label, and presentation-only changes must not be mistaken for resource identity changes.
- Increment the protocol version once for the direct contract. Update the source-of-truth constants/schema and regenerate checked-in native protocol output through the repository's canonical generator. Do not hand-edit generated output.
- Remove the SDK fullscreen-state callback/input-context plumbing if the Phase 0 inventory confirms there is still no production consumer: `Widget.OnOverlayFullscreenChangedAsync`, `ControllerInputContext.OverlayFullscreenPresentation`, `ControllerInputEvent.IsOverlayFullscreenActive`, and the corresponding Bridge/runtime notification. Fullscreen remains host-owned.

If existing names make the preferred names technically misleading, update this section with the chosen direct names and the reason before implementation. Do not retain the old public members for convenience.

## Host ownership model

The intended shape is one `MediaSessionManager` (or an equivalently direct name recorded here) with:

- a bounded registry keyed by durable session key;
- the shared WebView2 environment owner/reference;
- one per-session record containing the coordinator/controller/document, sealed resource identity, last valid declaration, playback state, command correlation state, current presentation state, and endpoint attachment;
- one reconciliation input that combines the admitted declaration, current widget/lifecycle visibility, renderer viewport observation, fullscreen request/state, compact-pin request/state, and endpoint availability;
- one pure or side-effect-free transition plan from current state plus input to target state and required effects;
- one executor that performs ordered effects and commits state only after successful completion;
- explicit rollback or fail-closed behavior for partial attach/detach failures;
- idempotent reconcile for repeated equivalent inputs;
- browser-process/controller-exit handling that retires only the affected session unless the shared environment itself failed;
- an explicit admission failure for a fifth resident session rather than implicit eviction;
- a typed parking reason that distinguishes host lifecycle parking from a declared route with no viewport;
- direct session-keyed callbacks so re-entrant work for session A can never mutate whichever session happens to own an endpoint later.

Endpoint contention must park the non-winning session rather than destroy it. Fullscreen is one overlay endpoint. Compact pin uses the existing pinned endpoint policy. Returning an endpoint to a parked session must reattach its exact controller without navigating or replaying the load command.

### Compact media pin and pinned-surface interaction

`CompactPinned` participates in the pinned-surface system without becoming a second session implementation:

- `MediaSessionManager` owns the media controller/document, session state, playback observations, command correlation, and the transition into or out of `CompactPinned`.
- `WidgetSurfaceCoordinator` and the pinned-surface host continue to own the pinned HWND, declared pinned layout/chrome, committed `MediaViewport` geometry, controller routing, accessibility projection, DPI/placement changes, and ordinary pinned-surface lifetime.
- A widget may expose ordinary `PinnedLayouts` and opt its media session into `CompactPinned` in the same snapshot. The pinned-surface selector presents the ordinary widget layouts plus one host-provided **Compact media** choice when the current media session supports it.
- `CompactPinned` remains a distinct presentation kind because it attaches the live WebView controller and supplies host-native media chrome, shortcut semantics, and accessibility. It is not a distinct HWND, placement engine, focus-mode implementation, opacity system, persistence owner, or teardown lifecycle.
- A compact media declaration requests the pinned endpoint through the manager. The manager validates the exact session and capability, resolves the current committed pinned viewport from the pinned-surface owner, and attaches the same controller only after valid geometry exists.
- Pinning never clones, reloads, or navigates the media document. Unpinning moves the session to `OverlayViewport` when a current valid ordinary viewport owns the overlay endpoint; otherwise it moves to `Parked`.
- Switching the one pinned endpoint from Compact media to an ordinary declared pinned layout parks the media session and renders that ordinary layout through the existing coordinator. Switching back reattaches the exact controller/document and restores compact chrome without replaying load.
- Retiring or recreating the pinned HWND detaches that endpoint and parks the session. It does not close the session unless the widget/session declaration, runtime, resource identity, controller, shared environment, or host itself has become terminal.
- An ordinary non-media pinned surface continues to work. If pinned endpoint policy permits only one visual endpoint, admission/arbitration is explicit and the losing media session parks without affecting its playback lifetime. Do not silently replace unrelated pinned content.
- A compact-pinned session and a different overlay/fullscreen session may remain active simultaneously because they own distinct endpoints and distinct coordinators in the shared environment.
- Compact commands and chrome updates must be session-keyed. Telemetry or re-entrant callbacks from a parked/overlay/fullscreen session must never update the compact chrome belonging to another session.
- Preserve the host-owned compact controller contract: `X` toggles play/pause; `LT`/`RT` seek backward/forward by the session's declared step; `LB`/`RB` select previous/next only when declared; `B` exits compact interaction to click-through; and `View` returns to the tray. Route every held/repeated action through the exact `CompactPinned` session key.
- Held buttons/repeat state, focus, and accessibility authority retire when compact presentation ownership changes, while the media session itself remains resident.
- Tray unpin/repin, widget cycling, overlay hide/show, pinned-window retirement, widget removal, runtime replacement, and host shutdown must each have one explicit manager transition and one terminal/non-terminal classification.

### Existing compact-pinned presentation mechanics to preserve

WIDGE-178 replaces fragmented ownership; it must port the mature placement and anti-flicker mechanics already on `main`. Do not simplify these into a detach/recreate or show-then-position path:

- [ ] Preserve the pinned aspect-fit and centering calculation in `WidgetSurfaceCoordinator::Paint`: use the pinned client area minus the one-DIP border, fit the declared aspect ratio, and center on both axes. Keep the synthetic `host.compact-media.viewport` bounds and clip coherent.
- [ ] Publish pinned media geometry only after the pinned render completes successfully. Preserve the `committedMediaViewport_`, `mediaViewportGeometryDirty_`, `nextCommittedFrameGeneration_`, and `CurrentMediaViewport(...)` invariants behind the new per-session presentation record.
- [ ] Dirty/recompute pinned geometry after resource-contract replacement, pinned-layout replacement or cycling, window move/resize, DPI/graphics lifecycle changes, and unpin/teardown. Reconcile media only after the pinned coordinator reports a successfully committed viewport.
- [ ] Preserve exact geometry authority: live pinned surface, committed region, matching session ID, and selected pinned snapshot still declaring that exact session/presentation.
- [ ] Preserve endpoint coordinate spaces. Ordinary overlay media uses content-local coordinates and the overlay content transform; compact media uses pinned-HWND endpoint-local coordinates.
- [ ] Preserve relative clipping in `OverlayCompositionSurface::CommitExternalContentPresentation`: offset the external visual by host bounds, normalize clip coordinates against the visual origin, and clamp to visual width/height.
- [ ] Preserve controller-local WebView2 bounds `{0, 0, width, height}` in `RichMediaSurfaceCoordinator::UpdateGeometry`. Never apply the host offset to both the DirectComposition visual and the controller, which recreates the historical double-offset bug.
- [ ] Compute raster scale from the actual destination owner HWND. Do not reuse overlay DPI when the compact-pinned window may be on another monitor.
- [ ] Resolve and validate destination HWND, exact committed viewport, bounds, clip, dimensions, DPI, and authority before mutating the live controller. `Pending` remains hidden/parked; `Invalid` fails closed.
- [ ] Preserve live retargeting. `BeginPresentationTransfer` hides the controller and disables input; `CompletePresentationTransfer` changes root target/parent, applies local geometry and destination scale, commits presentation, and only then reveals/re-enables the controller. Do not create another controller and do not expose stale/default coordinates.
- [ ] Preserve successful transfer ordering: create destination endpoint; resolve geometry; create visual target; hide/disable input; attach root and parent; apply local bounds and destination DPI; commit destination; retire source; reveal only if the destination is still current.
- [ ] Preserve composition-first visibility. Commit external visual bounds, clip, and visibility before `SetVisible(true)`; if WebView visibility fails, roll the composition presentation back to hidden.
- [ ] Preserve equivalent-presentation deduplication so playback telemetry does not cause redundant DirectComposition commits or flicker.
- [ ] Preserve atomic endpoint retirement: removal/root changes require composition `Commit` and the existing completion wait. An uncertain removal, commit, or completion result invalidates/rebuilds the affected composition graph rather than trusting partially mutated fields.
- [ ] Preserve unpin ordering through `beforeWindowRetirement_`: transfer or park media while the pinned HWND and committed geometry still exist, then destroy the pinned window.
- [ ] Preserve the unattached parking target. When no visual endpoint is ready, retarget the same controller to the unattached visual, keep it hidden/non-interactive, and retire the former visible endpoint only after successful retarget.
- [ ] Preserve pinned host-chrome layering above the video visual, with bounds synchronized to the same media region and visibility based on controller focus, scrub state, and playback.
- [ ] Preserve compact progress, finite/bounded seek target calculation, scrub preview/commit/cancel, repaint, and accessibility publication without making playback telemetry a presentation-owner change.
- [ ] Treat composition-graph invalidation as affecting every endpoint on that graph and reconcile every affected session from manager state; do not assume only the currently selected media session was affected.

Historical commits that introduced or corrected these invariants must be inspected as evidence during Phase 0: `b2623de4` (committed pinned geometry/frame generation), `0958bb38` (visibility reconciliation), `8260e7fb` (presentation stability), `0195ece3` / `ac839ce3` / `ac58b39b` / `a54ba422` (compact presentation, ownership, composition, progress and seeking), and the integrated WIDGE-174 chain including atomic unpin, attached retarget, parking target, fail-closed endpoint recovery, geometry deferral, and refresh authority. Port the behavior into the new manager boundary; do not preserve the old mutable bound-session facade merely to reuse it.

## Removal inventory

The implementation task must identify exact symbols and update this list before deleting them. The following concepts are expected to be removed or collapsed:

- [ ] Old protocol and SDK presentation Booleans and their version checks.
- [ ] Hidden-retention validator special cases and error text based on `RetainSessionWhenHidden`.
- [ ] Host `EmbeddedMediaProjection` two-state ownership where it competes with fullscreen/parking.
- [ ] Parallel `EmbeddedMediaAuthority` fields that bind lifetime to exact snapshot sequence.
- [ ] Route-specific parking attach/detach helpers superseded by the manager.
- [ ] Fullscreen state that is reconstructed from each render snapshot rather than owned by the media session.
- [ ] Compact-pinned transfer code that moves lifetime ownership between subsystems.
- [ ] WIDGE-174/WIDGE-177 point-fix branches or conditions that become unreachable under the unified state model.
- [ ] Tests whose only purpose is old-Boolean compatibility or exhaustive combinations of obsolete declarations.
- [ ] Documentation that teaches old retention/presentation flags.
- [ ] SDK fullscreen-state notification/callback plumbing with no production consumer, if the Phase 0 inventory remains conclusive.
- [ ] The embedded handoff-owner permutation harness in `main.cpp`, its `WRAIL_EMBEDDED_MEDIA_HANDOFF_TESTING` hooks, command-line/build selector, and claimed exhaustive case output.
- [ ] Media-specific source-text assertions that encode exact private method names or statement ordering rather than behavior.

Do not delete security validation, package-resource sealing, content allowlists, command correlation, input routing, accessibility semantics, diagnostics, or browser failure handling. Move them behind the new owner if necessary.

The rejected WIDGE-177 tail commit `849b0867` is evidence only. Do not cherry-pick it. Small ideas may be independently reimplemented only when they fit the unified manager and are recorded in the decision log.

## Phase 0 — Establish the exact baseline and inventory

- [ ] Read `docs/implementation-agent-goal.md`, retrieve WIDGE-178 and its relations from Plane, and read this file in full.
- [ ] Create a new clean isolated worktree and branch from the exact assignment baseline on local `main`; do not reuse the WIDGE-177 worktree.
- [ ] Record branch, worktree, baseline commit, `git status`, and initial checkpoint in the status ledger.
- [ ] Confirm the shared WebView2 environment creation path and current resident-session bound.
- [ ] Inventory every direct declaration and consumer across `WidgetProtocol`, `WidgetSdk`, `WidgetBridge`, `OverlayHost`, generator/template inputs, YouTube, Embedded Media, CLI preview, tests, and directly affected docs.
- [ ] Record the exact planned file list in the changed-file ledger before editing.
- [ ] Record which integrated WIDGE-177 prefix pieces the new design keeps, rewrites, or deletes.
- [ ] Confirm that current `main` has one shared environment and up to four resident coordinators but still routes operations through the mutable `richMediaSurface_` / `embeddedMediaAuthority_` / `boundEmbeddedMediaSessionKey_` facade. Record any change from this reviewed baseline.

## Phase 1 — Replace the public declaration directly

- [ ] Introduce the direct media-session declaration and capability flags in the managed protocol/SDK source of truth.
- [ ] Replace the `WidgetView` property and `UI.MediaViewport` binding contract directly.
- [ ] Remove all three old Boolean properties; do not mark them obsolete and do not deserialize them.
- [ ] Update semantic validation for zero-or-one viewport, exact session binding, and required endpoint capability.
- [ ] Update protocol version requirements once.
- [ ] Update Bridge wire DTOs, resolver/materializer, declaration equality/resource identity, diagnostics, and validation.
- [ ] Run the canonical protocol generator and record its command and output paths.
- [ ] Update only the focused SDK/protocol tests necessary to prove serialization, validation, and direct removal of the old shape.
- [ ] Remove obsolete fullscreen-state SDK/runtime callbacks when the no-production-consumer inventory is confirmed.
- [ ] Update this file with the exact contract names and Phase 1 evidence before starting Phase 2.

## Phase 2 — Introduce the unified host manager

- [ ] Add the bounded `MediaSessionManager` registry and per-session state owner.
- [ ] Reuse the one shared environment; create one coordinator/controller/document per resident session.
- [ ] Define the closed presentation state and explicit geometry readiness state.
- [ ] Add the transition planner and single side-effect executor.
- [ ] Separate session/resource identity from presentation sequence and command sequence.
- [ ] Make equivalent reconciliation idempotent.
- [ ] Preserve per-session controller/document across Parked, OverlayViewport, OverlayFullscreen, and CompactPinned transitions.
- [ ] Keep `RichMediaSurfaceCoordinator` as the per-document WebView2 transport, sandbox, adapter-message, retarget, teardown, and shared-environment client. Remove its dependence on a mutable globally bound session rather than rewriting its proven security boundary.
- [ ] Make every coordinator callback carry the immutable manager session key captured at creation; an absent or replaced entry is rejected without rebinding another entry.
- [ ] Preserve bounded error mapping and diagnostics with session locator, source state, target state, and failing effect at debug severity where appropriate. Do not add general payload/value logging.
- [ ] Update this file with added files, responsibilities, and checkpoint evidence before removing old paths.

## Phase 3 — Remove competing host ownership

- [ ] Route ordinary overlay reconciliation through the manager.
- [ ] Route hidden/cycled widget lifecycle changes to `Parked` without destroying a declared session.
- [ ] Route fullscreen entry, geometry updates, input, B exit, accessibility, and teardown through the same session.
- [ ] Route compact pin, tray unpin/repin, pinned geometry, commands, and accessibility through the same session.
- [ ] Reuse the existing pinned-surface selection, HWND, placement, focus/click-through, opacity, persistence, and teardown machinery for Compact media; expose it as a host-provided choice alongside a widget's ordinary `PinnedLayouts` rather than a parallel pin implementation.
- [ ] Preserve the rule that ordinary pinned projections are declarative and do not smuggle in a second media viewport. Compact media is the explicit session-backed media choice.
- [ ] Ensure endpoint loss parks the session unless the declaration was removed or authority became invalid.
- [ ] Remove superseded projection, parking, exact-snapshot lifetime, and transfer ownership code.
- [ ] Remove the mutable bound-session facade, including `SaveBoundEmbeddedMediaSession`, `BindEmbeddedMediaSession`, `CreateAndBindEmbeddedMediaSession`, `EnsureBoundEmbeddedMediaParkingTarget`, and their equivalent state fields; do not leave same-named wrappers around the manager.
- [ ] Stop fullscreen rendering from overwriting ordinary `lastWidgetRenderResult_` or `committedWidgetVisualState_`; publish typed fullscreen geometry to the manager after the relevant composition frame commits.
- [ ] Prove ordinary playback telemetry does not exit fullscreen or replace the document.
- [ ] Prove a stale action/command still fails closed without making snapshot sequence the lifetime key.
- [ ] Update the removal inventory and record deleted symbols/files before Phase 4.

## Phase 4 — Update all in-repository widget consumers

- [ ] Update YouTube to declare one session on Player, Search, Settings, and other routes whenever the media session should remain resident. Player includes a viewport; non-player routes omit it and therefore park.
- [ ] Ensure YouTube no longer synthesizes route-specific retained surfaces or toggles presentation Booleans.
- [ ] Preserve YouTube playback, settings, fullscreen, captions route, commands, LT/RT seeking, and compact media pin behavior from accepted WIDGE-54.
- [ ] Update the Embedded Media sample to teach the direct session/capability contract.
- [ ] Update every other in-repository compile-time consumer found in Phase 0. Remove obsolete examples rather than wrapping old declarations.
- [ ] Update directly affected authoring and protocol documentation, including `docs/declarative-ui.md`, `docs/embedded-media-widget.md`, and `docs/adding-declarative-ui-elements.md` where their instructions touch the changed declaration or generator flow.
- [ ] Update package manifests/versions only where the repository's package contract requires it; record exact version changes.

## Phase 5 — Focused verification only

Do not create an exhaustive state-transition matrix. Do not create a property/model test framework. Do not duplicate equivalent test cases across layers. Prefer one authoritative test at the narrowest layer plus the minimum end-to-end/package proof.

- [ ] Managed protocol/SDK: one round-trip for the new session/capability shape and focused invalid cases for mismatched viewport, missing capability, and removed declaration.
- [ ] Native parsing/admission: one production-shaped valid snapshot and focused fail-closed coverage for invalid identity/geometry.
- [ ] Manager: a small set of scenario tests proving:
  - fullscreen remains active across command-free telemetry/snapshot advancement;
  - Player to Search or Settings parks and Player return reuses the exact controller/document;
  - widget cycling parks and restores the exact controller/document;
  - compact pin, unpin, and repin work from both visible and parked routes;
  - a widget exposing both ordinary pinned layouts and Compact media can switch between them on the one pinned endpoint while retaining the exact media controller;
  - two widget instances retain independent resident sessions concurrently;
  - pending geometry defers/parks while invalid geometry fails closed;
  - dropping the session declaration closes it.
- [ ] YouTube: focused package/widget checks for the new declarations on Player/Search/Settings and preservation of accepted settings/transport behavior.
- [ ] Embedded Media: focused package/sample validation against the new public contract.
- [ ] Real WebView smoke: create two coordinators from the shared environment, keep distinct controller/document authorities, park one, and verify independent playback observations without teardown.
- [ ] Run each focused gate once after the implementation is coherent. Record command, result, duration, and exact failing case for any red.
- [ ] Follow the same-issue second-red rule from `docs/implementation-agent-goal.md`; do not stop at the first correctable red.

## Phase 6 — Coherent Release/package candidate

- [ ] Update this file before starting the build and record the exact planned command.
- [ ] Run one coherent canonical Release/package build covering the host, Bridge/SDK, and affected bundled widget packages.
- [ ] Confirm a compiler actually ran; a setup-only or skipped result is not validation.
- [ ] Record all produced artifact paths, versions, sizes, and SHA-256 hashes.
- [ ] Confirm the worktree contains only WIDGE-178 scope plus this living plan.
- [ ] Run `git diff --check` and the repository's required package validation once.
- [ ] Do not install, launch, integrate, push, or mutate Plane from the implementation task.

## Phase 7 — Final implementation handoff

- [ ] Reconcile every checkbox, removal, changed path, red, gate, artifact, and residual risk in this file.
- [ ] Make one coherent local implementation commit or a minimal reviewable commit chain when a single commit would hide an important generated boundary. Record exact commit IDs and parent.
- [ ] Confirm branch/worktree cleanliness after the commit.
- [ ] Report exact baseline, commits, changed paths, removed legacy paths, tests, build/package artifacts, hashes, and manual acceptance checklist to the reviewer.
- [ ] Leave `gate:manual` in place. The reviewer owns integration, installation, launch, and user acceptance.

## Changed-file ledger

Populate before editing and keep current. Add one row per file or tightly coupled generated family.

| Path | Planned responsibility | Status | Evidence/notes |
| --- | --- | --- | --- |
| `docs/widge-178-unified-media-session-plan.md` | Living execution and recovery record | `[~]` | Reviewer-authored initial plan; implementation-owned after assignment |
| _To be inventoried in Phase 0_ |  | `[ ]` |  |

## Removed legacy-path ledger

| Symbol/path | Replacement | Status | Evidence/notes |
| --- | --- | --- | --- |
| `CompactPinnedPresentation` | Closed supported-presentations collection plus manager presentation state | `[ ]` | Remove directly; no alias |
| `RetainSessionWhenHidden` | Declared-session-without-viewport means Parked | `[ ]` | Remove directly; no alias |
| `OverlayFullscreenCapable` | Closed supported-presentations collection plus manager presentation state | `[ ]` | Remove directly; no alias |
| Host projection/parking/fullscreen lifetime overlap | One manager/reducer/executor | `[ ]` | Exact symbols to be recorded in Phase 0 |
| Mutable bound-session facade | Direct immutable session-keyed manager operations and callbacks | `[ ]` | Remove state swapping; no wrapper compatibility layer |
| Embedded handoff-owner permutation harness | Focused manager scenarios plus one real shared-environment smoke case | `[ ]` | Do not recreate the exhaustive matrix |

## Gate ledger

Do not record a gate as green unless the named command actually ran and produced its expected artifact or test count.

| Gate | Command | Result | Evidence |
| --- | --- | --- | --- |
| Baseline status | Not run by implementation yet | `[ ]` |  |
| Managed protocol/SDK focused tests | To be selected once, before execution | `[ ]` |  |
| Native parser/manager focused tests | To be selected once, before execution | `[ ]` |  |
| YouTube focused tests/package validation | To be selected once, before execution | `[ ]` |  |
| Embedded Media focused tests/package validation | To be selected once, before execution | `[ ]` |  |
| Canonical Release/package build | To be selected once, before execution | `[ ]` |  |
| `git diff --check` | To be run once on final range | `[ ]` |  |
| Physical acceptance | Reviewer/user after installation | `[ ]` | Manual gate remains open |

## Red and correction log

Append; do not overwrite prior entries.

| Attempt | Phase/gate | Exact red | Classification | Correction/result |
| --- | --- | --- | --- | --- |
| 0 | Planning | None | N/A | Implementation not started |

## Decision and scope-change log

Append any implementation-specific decision before relying on it.

| Date | Decision | Reason | Scope impact |
| --- | --- | --- | --- |
| 2026-09-03 | Choose Option B: in-process multi-session manager | One lifetime/presentation owner removes the disconnected overlay, fullscreen, pin, and parking behavior | Direct SDK, Bridge, host, YouTube, and sample integration |
| 2026-09-03 | Shared environment, per-session controller/document | Supports multiple independent widgets without multiplying WebView2 environment ownership | Manager owns a bounded session registry |
| 2026-09-03 | No compatibility layer or exhaustive transition suite | Explicit user direction; old declarations are internal to the repository and can be replaced directly | Remove obsolete API/tests/docs instead of adapting them |
| 2026-09-03 | Start from accepted `main`; do not cherry-pick WIDGE-177 tail | The tail is a rejected special-case fix and conflicts with unified ownership | Preserve it only as investigation evidence |

## Residual-risk ledger

Keep this short and current; resolved risks move to the decision or evidence logs.

| Risk | Mitigation/evidence | Status |
| --- | --- | --- |
| Partial composition transfer could leave a controller detached from every endpoint | Ordered executor with explicit Parked fallback and focused failure coverage | `[ ]` |
| A snapshot-sequence check could remain embedded in lifetime code | Inventory and remove exact-sequence lifetime ownership; retain sequence only for stale actions/commands/layout observations | `[ ]` |
| Shared-environment failure could affect all sessions | Distinguish per-controller exit from shared-environment failure and preserve existing recovery policy | `[ ]` |
| Endpoint contention could destroy a non-winning session | Arbitration parks the non-winner and preserves its controller/document | `[ ]` |
| Direct protocol change could leave an old consumer compiling through stale generated output | Canonical generation plus repository-wide symbol search for removed names | `[ ]` |
| A fifth session could silently evict active playback | Explicitly reject fifth admission and leave all four resident sessions intact | `[ ]` |
| Compact media could remain a parallel pinned-window lifecycle | Reuse the existing pinned HWND, selector, placement, focus modes, opacity, persistence, and teardown; specialize only its session-backed content/chrome | `[ ]` |

## Reviewed source inventory to verify in Phase 0

This is a starting inventory, not permission to skip the repository-wide removed-symbol search.

Public managed contract and SDK:

- `src/WidgetProtocol/ViewModels.cs`
- `src/WidgetProtocol/ViewSnapshotValidator.cs`
- `src/WidgetProtocol/ProtocolVersionRequirements.cs`
- `src/WidgetProtocol/ProtocolConstants.cs`
- `src/WidgetProtocol/SnapshotJson.cs`
- `src/WidgetSdk/Widget.cs`
- `src/WidgetSdk/Elements.cs`
- `src/WidgetSdk/UI.cs`
- `src/WidgetSdk/WidgetPresentationDiff.cs`
- `src/WidgetSdk/PublicApi.txt`

Bridge, runtime, and generated/native wire propagation:

- `src/WidgetBridge/BridgeProtocol.cs`
- `src/WidgetBridge/EmbeddedMediaAssetResolver.cs`
- `src/WidgetBridge/BridgeClientRegistry.cs`
- `src/WidgetBridge/BridgeRequestClassification.cs`
- `src/WidgetBridge/WidgetBridgeServer.cs`
- `src/WidgetRuntime/RuntimeProtocol.cs`
- `src/WidgetRuntime/WidgetProcessClient.cs`
- `src/WidgetRuntime/WidgetWorkerServer.cs`
- `src/OverlayHost/WidgetBridgeClient.h`
- `src/OverlayHost/WidgetBridgeClient.cpp`
- `src/OverlayHost/WidgetProtocolPresentationContract.generated.h`
- `src/OverlayHost/EmbeddedMediaResourceContract.h`

Native manager/presentation ownership:

- new `src/OverlayHost/MediaSessionManager.h`
- new `src/OverlayHost/MediaSessionManager.cpp`
- `src/OverlayHost/main.cpp`
- `src/OverlayHost/RichMediaSurfaceCoordinator.h`
- `src/OverlayHost/RichMediaSurfaceCoordinator.cpp`
- `src/OverlayHost/OverlayCompositionSurface.h`
- `src/OverlayHost/OverlayCompositionSurface.cpp`
- `src/OverlayHost/DeclarativeRenderer.h`
- `src/OverlayHost/DeclarativeRenderer.cpp`
- `src/OverlayHost/WidgetSurfaceCoordinator.h`
- `src/OverlayHost/WidgetSurfaceCoordinator.cpp`
- pinned-surface host/placement consumers found by Phase 0
- `src/OverlayHost/build.ps1`, `src/OverlayHost/OverlayHost.vcxproj`, and applicable CMake/test registration

Direct widget and template consumers:

- `samples/YouTubeWidget/YouTubeVideoWidget.cs`
- `samples/YouTubeWidget/YouTubeVideoWidget.Search.cs`
- `samples/EmbeddedMediaWidget/EmbeddedMediaSampleWidget.cs`
- `templates/ControllerWidget/profiles/embedded-media.cs.template`
- `templates/ControllerWidget/tests/EmbeddedMediaScenarioTests.cs.template`
- `templates/ControllerWidget/README.embedded-media.md.template`
- `templates/ControllerWidget/template.json`

Direct documentation and focused-test families:

- `src/WidgetSdk/README.md`
- `docs/declarative-ui.md`
- `docs/embedded-media-widget.md`
- `docs/widget-quickstart.md`
- `docs/widget-authoring-guide.md`
- `docs/adding-declarative-ui-elements.md`
- focused SDK, Bridge, native manager, YouTube, Embedded Media, CLI preview, template, and package tests that fail to compile or directly assert the replaced contract

`MediaSessionsWidget` and Spotify/Windows media-session code are not embedded-WebView consumers and are outside this source change unless implementation discovers a real shared type dependency and records it first.

## Physical acceptance checklist

The implementation task prepares this checklist; only the reviewer/user performs it after installing the final candidate.

- [ ] Start a YouTube video, cycle to another widget, wait, and return. Playback/session continuity remains intact; Play resumes normally if provider policy paused it.
- [ ] Enter fullscreen and wait through multiple telemetry updates. Video remains fullscreen, the ordinary overlay does not redraw over it, and B exits exactly once to the same player.
- [ ] From Player, return to Search, open Settings, and return to Player. No retained-hidden error appears and the exact player/document resumes without reloading.
- [ ] Compact-pin the playing video, go to Search, unpin from the tray, and repin. No `presentation-transfer-detach`, retained-hidden, or stale-session error appears.
- [ ] Exercise compact pin while the owning widget is cycled away and back. The same session survives and controls remain current.
- [ ] Start media in a second widget while YouTube remains resident. Both sessions remain independent; presenting or closing one does not retire the other.
- [ ] Verify ordinary overlay controller input, fullscreen input, compact-pinned controls, Guide/B behavior, focus restoration, and media accessibility labels.
- [ ] In Compact media, verify `X` play/pause, `LT`/`RT` session-step seeking, conditional `LB`/`RB` previous/next, `B` click-through exit, and `View` tray return before and after an ordinary-layout switch and unpin/repin.
- [ ] Remove the media session declaration by ending/closing playback. Its controller is retired and no hidden undeclared playback remains.

## Final evidence and handoff

Populate at the end; do not claim manual acceptance here.

| Field | Final value |
| --- | --- |
| Implementation commit(s) | Pending |
| Final tree | Pending |
| Release artifacts | Pending |
| SHA-256 hashes | Pending |
| Focused tests | Pending |
| Build/package result | Pending |
| Removed legacy symbols | Pending |
| Residual risks | Pending |
| Worktree clean | Pending |
| Manual gate | Pending reviewer/user acceptance |
