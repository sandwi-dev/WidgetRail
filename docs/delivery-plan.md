# Game Bar Alternative — Delivery Plan

Status: active implementation authority

Historical review and assignment detail through planner commit `436d890` is in
the [2026-08-13 snapshot](history/delivery-plan/2026-08-13T04-23-11-07-00.md).
The complete plan immediately before this compaction is in the
[2026-08-14 snapshot](history/delivery-plan/2026-08-14T03-24-16-07-00.md).
Snapshots are evidence only. This file is the sole authority for current work.

## Current accepted baselines

- Production code: local main product baseline `acc062d`, including DLV-216,
  AVP-004 SESSION then PLATFORM extractions, generic Avalonia integration
  through `16b33d9`, generic controller-first redesign through `9db36b5`, and
  widget-owned surface envelopes through source `5ffd435`, integrated as
  `25c6639` then `acc062d`. Planner documents continue through main `1744274`.
  Production renderer cutover is not authorized.
- Latest accepted Avalonia source: `5ffd435` on
  `codex/avalonia-prototype`, integrated through main `acc062d`. Its focused
  30/30 suite, eight authored envelopes, 32/32 work-area responsive rows, 33
  transition samples with invariant absolute tray/guide bounds, 330.96-MiB
  candidate sample, and bounded shutdown are accepted.
- Crash-safe trace commits `988324c` and `e0a078c` remain unintegrated. The
  latter closes synchronous UI-thread persistence with a bounded background
  coalescer, but its exact run exposes the active mixed-DPI placement defect
  below. All dependent work remains unintegrated.
- The integrated-main Avalonia candidate crashed during physical testing and is
  closed. No candidate or owned WidgetBridge process remains running. The
  packaged production Release is also closed during isolated candidate work.

## Execution rules

- Each task implements only its lane's current Assigned milestone, then the
  first explicitly Ready same-lane milestone whose baseline is present.
- Implementation tasks do not edit reviewer-owned documents. The planner
  independently reviews actual diffs and retained evidence.
- Rejected commits remain unintegrated. Corrections stay in their existing lane
  and do not interrupt unrelated coherent work.
- Never push. Stop for credentials, destructive recovery, substantial merge
  conflicts, undocumented input/window APIs, publication, physical-only
  evidence, or a material product choice.
- User-visible defects and requested features outrank internal refactors.
- Screenshots are supporting evidence only. Exclude malformed captures rather
  than expanding ordinary product work into capture-harness work.
- Use the smallest affected Release suites, one final focused suite, and only a
  named integration boundary. Do not run the product aggregate merely for AVP
  work.
- New managed test projects use MSTest.Sdk 4.3.2; existing executable suites
  remain valid unless their migration is explicitly assigned.
- Full-trust Community applications may use ordinary user-level APIs in their
  process. Bound shared product inputs/resources, not private application CPU,
  memory, databases, files, sockets, dependencies, or child processes.

## Product and architecture decisions

- Games & Apps remains bundled. Spotify and Game Launcher are ordinary
  Community applications. Core assemblies contain no service-specific DTOs,
  OAuth, API clients, process hosts, package identities, or known-tree rules.
- Sandboxed packages retain AppContainer/capability boundaries. Explicitly
  consented full-trust applications use the generic full-trust runtime.
- Launcher Experience packs remain data-only and host-owned.
- Avalonia replaces only the presentation boundary. Retain Widget SDK/protocol,
  catalog/package/runtime, WidgetBridge/authenticated transport,
  lifecycle/trust/persistence/providers, domain implementations, and Community
  process boundaries.
- One generic Avalonia semantic adapter renders every current widget. Per-widget
  Avalonia pages, hidden role strings, identity branches, or tree-shape special
  cases are prohibited.
- Each view owns its semantic content and validated preferred/minimum logical
  envelope through `WidgetSurfaceHints`. The host clamps only to the actual
  monitor work area, accessibility, safe insets, and safety constraints.
- The fixed bottom-center tray and controller guide are host chrome. Content
  grows or shrinks upward/outward around that anchor. One HWND wraps the union;
  the overlay must not become a monitor-sized desktop surface.
- The production native Microsoft GameInput/Guide owner is authoritative. No
  second C# GameInput reader, bridge transport, overlay HWND, or focus tree is
  permitted.

## Active task map

| Lane | Task | Branch/worktree | Current state |
| --- | --- | --- | --- |
| Widgets | Implementation agent — widgets lane | `codex/impl-widgets-community-launcher`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-217 accepted through `d57fd06`; integration awaits explicit user approval for the known reviewer-doc-only red aggregate step |
| Platform | Implementation agent — platform lane | `codex/impl-platform-community`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Idle at accepted `fcd301a` |
| Avalonia lead | Implementation agent — Avalonia prototype lane | `codex/avalonia-prototype`; `C:\Users\dwive\.codex\worktrees\fe54\GameBarAlternative` | `e0a078c` rejected; stable visible-session monitor/work-area/DPI anchor correction Assigned |
| AVP session | AVP-004 — managed session extraction | `codex/avp004-session` | Accepted `7de4269`, integrated first as `7ec8253` |
| AVP platform | AVP-004 — native platform extraction | `codex/avp004-platform` | Accepted `849e970`, integrated second as `b5c4c6c` |

The extraction tasks are closed. The standing Avalonia lead owns only the
generic integration/correction. AVP-005 and production cutover are unauthorized.

## Widgets lane

### Accepted and integrated — DLV-216: autonomous Spotify Community application

Source `452c7dd`, integrated as `47d8ffe`. Spotify domain behavior, OAuth, Web
API/playback, token storage, response parsing, caches, and lifecycle now belong
to its ordinary full-trust Community package. Live account, Premium,
allowlisting, EME, and credentials remain manual.

### Accepted, awaiting explicit integration approval — DLV-217: autonomous Game Launcher

Accepted linear range: `7aa229e`, `c504d2f`, `6b9d032`, `d57fd06`, based on
`47d8ffe`. It makes discovery, optional enrichment, artwork/cache/provenance,
collections, details/back, exact SavedId launch, and persistence package-owned
through the public generic runtime.

Exact-clean Tier 3 at `d57fd06` ran 41 steps: 40 passed, including Spotify
Community 5/5, Game Launcher Community 6/6, and Widget Catalog trust/package
35/35. The only red step is an unchanged reviewer-owned archived delivery-plan
link. The implementation is accepted, but broad local integration remains
paused until the user explicitly approves integrating despite that honestly red
aggregate. Never describe the aggregate as fully green.

The widgets queue has fewer than three Ready items because DLV-218 must follow
both Community package cutovers and filler work would compete with the active
visible AVP correction.

## Platform lane

### Current assignment — none

DLV-215 `ed39a70`, DLV-210 `ebb6ad7`, and corrected DLV-220 `fcd301a` are
accepted and integrated through product baseline `4f502c4`.

### Ready after DLV-217 integration — DLV-218: remove retired domains

Remove retired product-owned Spotify and private Game Launcher domain paths
only after both autonomous Community packages are integrated. Retain generic
App Library behavior for bundled Games & Apps and consenting sandboxed users.
Add an architecture check rejecting Community identities/domain types in core.
Do not delete credentials, provider data, accounts, or user files.

Verification: focused architecture, packages, Games & Apps, bridge/runtime,
and one integration checkpoint because this deletes cross-process paths.

### Ready after DLV-218 — DLV-206: truthful performance provenance

Correct the rejected DLV-200 evidence without expanding measurement scope:
retain root PID/start, exact commit/SHA, scenario/profile, child roles,
available/unavailable metrics; give the eight-widget run separate provenance;
anchor composition lookup after paint; remove false ordinary-host-live wording.
Run only affected bounded performance/temporal routes. No aggregate or budget
change.

## AVP-004 accepted extraction boundaries

### AVP-004-SESSION

Accepted `7de4269`, integrated first as `7ec8253`. It is the sole typed managed
presentation-session facade over authenticated WidgetBridge, owning descriptor
enumeration, lifecycle, validated latest/last-good publication, exact action
admission, artwork, restart/invalidation, and bounded diagnostics. No Avalonia
type or duplicate transport/schema/domain identity crosses it.

### AVP-004-PLATFORM

Accepted `849e970`, integrated second as `b5c4c6c`. It is the sole narrow native
boundary for Microsoft GameInput/Guide, device/reconnect/repeat/neutral,
visibility/focus, debounce/toggle, targeting, DPI/work-area placement, and
shutdown. The legacy Guide compatibility adapter remains quarantined.

## Assigned — AVP-004-REDESIGN: crash-safe trace and stable monitor anchor

Owner: standing Avalonia prototype task on `codex/avalonia-prototype`.

Baseline: clean source `e0a078c14f39ef890e7511c722f391f4c2f14528`, based on
accepted widget-envelope source `5ffd435`. Commits `988324c` and `e0a078c` are
reviewed but rejected/unintegrated until this correction is accepted. Planner
main is `1744274` before this document update.

### Physical crash evidence

The exact integrated-main candidate crashed at 2026-08-14 02:50:51 local.
Windows .NET Runtime event 1026 records unhandled `System.IO.IOException`:
`Unable to remove the file to be replaced.` The stack runs from
`InputTraceRecorder.PersistSnapshotLocked()` line 94 through the Avalonia UI
controller callback in `MainWindow` line 315. The valid manual trace ends at
sequence 186 with connected native visible lease and routed input, but without
normal close.

The root cause is synchronous serialization and atomic replacement of the
complete trace on every input event. Expected sharing/replacement failure was
allowed to escape the UI callback and terminate the candidate.

### Accepted direction within the rejected prefix

`988324c` removes all serialization/file I/O from `Record`, uses one background
writer with unique temp plus atomic replacement, preserves the last good JSON,
retries the latest pending revision after a sharing violation, and exposes a
bounded explicit normal-shutdown flush. It is insufficient alone because it
starts publication immediately whenever idle, so ordinary 125-ms controller
repeat still replaces the complete trace for almost every event.

`e0a078c` adds a bounded 225-ms background batch window, makes `FlushAsync`
cancel/bypass the delay and publish its captured latest revision, and proves 32
rapid records produce one publication attempt while sequence ordering and
locked-target recovery remain exact. This trace design is accepted and must not
be reopened by the placement correction.

### Exact placement rejection

The exact `e0a078c` run is red because `MainWindow` re-evaluates
`Screens.ScreenFromWindow(this)` on timer, position, and widget-envelope
transitions, then overwrites stable work area/scaling. An envelope-driven HWND
resize can select a different monitor by intersection, causing the next tick to
adopt another work area and DPI and move/rescale again.

The first six widgets retained tray bounds `1985,1305 1150x95` at 125 percent;
YT Music alone moved to `540,1836 2760x228` at 240 percent; Spotify immediately
returned to the original anchor. No display-topology/DPI event or stable screen
identity was retained. The responsive artifact also labels YT Music fixtures at
1.25 while live window bounds reflect the 2.4 monitor. This is a product defect,
not proven external display activity, and absolute chrome/transition failures
cannot be waived.

### Bounded objective

Retain one explicit screen identity, work area, and render scaling anchor for
the complete visible overlay session. Every widget envelope transition, HWND
position callback, timer revalidation, transition cancellation/restoration,
and evidence-fixture restoration must resolve against that anchor. A change in
the overlay's own size or position must never select another monitor.

The anchor may change only when an explicit observed display-topology/DPI event
requires it or the anchored screen disappears. A real change must be admitted
atomically through the existing sole platform/window owner and record the old
and new screen identity, screen bounds, work area, scaling, and reason.

### Required implementation

- Preserve the accepted 225-ms background trace batcher, explicit flush,
  last-good atomic replacement, latest-state retry, and attempt-count seam.
- Introduce no second monitor/window authority. The existing MainWindow and
  OverlayPlatformClient remain the only top-level placement owners.
- Do not infer a new screen from the overlay's own post-resize intersection.
  `PositionChanged`, the platform timer, widget switching, and animation may
  revalidate containment against the retained anchor but may not replace it.
- Detect explicit topology/DPI changes only through supported Avalonia/Win32
  events already available to this prototype. Stop before undocumented APIs.
- Keep evidence fixtures isolated from live anchor state. After a fixture, the
  exact visible-session anchor and placement must be restored.
- Retain per-placement screen identity/bounds, work area, render scaling, and
  anchor-change reason so a physical run can distinguish a real display event
  from widget-driven movement.
- Add one identity-neutral deterministic multi-monitor overlap/resize test:
  compact, medium, and wide authored envelopes remain on one mixed-DPI anchor
  through both directions; a simulated explicit topology/DPI change may
  deliberately select/reflow once.
- Require every responsive fixture's actual scaling/window geometry to match
  its declared work-area constraint. Do not weaken chrome or transition gates.

### Preserved redesign contract

- Each admitted view owns its content structure, preferred/minimum logical
  dimensions, responsive branches, scrolling, and focus relationships through
  the existing semantic tree and `WidgetSurfaceHints`.
- Tray and guide stay fixed at identical absolute screen coordinates while the
  one HWND wraps the content-plus-chrome union. Widget switching animates only
  content/envelope. No full-work-area backdrop.
- The generic component compiler branches only on typed node kind/properties,
  collection/scroll/action orientation, responsive visibility, and declared
  advanced preset/slot. Never branch on widget/package identity, text, element
  ID, provider, style role, or known tree shape.
- Preserve one vertical scroll owner per axis, standard Avalonia controls/UIA,
  exact runtime/action/input/focus identities, single bridge transport, one
  focus tree, Community boundaries, and the existing native Guide owner.
- Controller state remains tray Left/Right selection, Up/A content entry, B to
  tray/close, bounded content navigation zones, slider Left/Right adjustment,
  scroll reveal, repeat/reconnect/focus-loss/hide-show, and per-widget focus
  memory without keyboard synthesis.

### Acceptance and verification

- Focused Release build and full affected AVP suite pass at the final commit.
  Direct tests cover trace batching/flush/recovery plus stable mixed-DPI anchor
  and explicit allowed anchor replacement.
- One exact changed-tip measurement only. No product aggregate.
- Exact source commit, ProductVersion, executable SHA, and flush truth match.
- All eight ordinary widgets pass through the real catalog/session/bridge;
  eight authored envelopes remain distinct.
- The 32-row matrix covers each installed widget under 420x340, 978x466,
  1180x680, and 1440x810 work-area constraints with truthful scaling,
  reflow/reachability, and no missing/duplicate rows.
- Start/mid/completion transition samples include compact-to-wide and
  wide-to-compact. Absolute tray/guide bounds remain identical on the retained
  anchor; no monitor-sized backdrop appears.
- Trace evidence is valid JSON during the live run, batches representative
  repeats materially below one replacement per event, survives one deterministic
  denied replacement, and flushes the exact latest revision within two seconds.
- Candidate private memory remains below 500 MiB, with process-tree memory
  separately reported; one tracked render, zero pending/owned artwork, hidden
  idle, and bounded normal no-force zero-process shutdown remain green.
- Worktree is clean. Scope stays under `experiments/AvaloniaOverlayPrototype`
  and directly affected prototype evidence/docs. No production cutover.

After independent source acceptance, the planner integrates the full accepted
prefix in order, rebuilds/copies from exact main, and launches visibly with a
manual live trace. The user then repeats all eight first pages, stationary
chrome, motion, Guide, D-pad/stick, A/B/Y, sliders, scrolling, focus restoration,
mixed-monitor stability, and normal close. Synthetic evidence cannot accept the
physical gate.

Out of scope: AVP-005, production cutover, GBSS deletion, widget-domain rewrites,
public SDK/protocol changes, per-widget pages, another input/transport/HWND/focus
owner, credentials, publication, capture-harness work, or changing any resource
gate.

## Serialized integration order

1. SESSION `7de4269` then PLATFORM `849e970` are already accepted/integrated.
2. Review the final AVP crash/anchor correction independently. If accepted,
   integrate `988324c`, `e0a078c`, then the correction in linear order.
3. Refresh the exact integrated-main candidate and obtain physical acceptance.
4. Do not begin AVP-005 or production cutover before explicit user approval.
5. DLV-217 integration remains a separate user-approval decision.
6. DLV-218 follows integrated DLV-216 and DLV-217; DLV-206 follows DLV-218.

## Manual and packaged verification queue

- User verdict on the latest accepted Release/candidate remains authoritative
  for visual clipping, motion, controller feel, Guide, and monitor behavior.
- Physical controller/display evidence remains required for AVP-004.
- Live Spotify account/Premium/Web Playback/EME/OAuth are credential-gated.
- Live IGDB and SteamGridDB enrichment are credential-gated; offline launcher
  behavior must not depend on them.
- Computer control may omit the no-taskbar overlay; use exact HWND/UIA/log
  fallback rather than changing taskbar behavior.

## Blocked work

| Item | Blocker | Required evidence |
| --- | --- | --- |
| AVP-005 / production cutover | AVP-004 physical verdict is open after crash and monitor-hop defects. | Accepted exact crash/anchor build plus user display/controller approval. |
| DLV-217 local integration | Exact aggregate is 40/41 with one known reviewer-history link red. | Explicit user approval to integrate despite that honest documentation-only red step. |
| Trusted fixed-video/PiP | Paused WebView2 measured about 348.7 MiB private and 4% CPU against prior gate. | User changes budget or authorizes content/process experiment. |
| Audio default-device selection | No documented supported Windows setter established. | Primary Microsoft API plus reversible provider/hardware plan. |
| Direct computer-control discovery | No-taskbar overlay is omitted from tool discovery. | Tool gains tool-window discovery or user accepts taskbar/Alt-Tab presence. |
| Native uninstall reconciliation | Synthetic catalog removal emitted no managed revision/native event. | Deterministic disabled/nonresident removal event. |
| YouTube authenticated library | Google OAuth/account; Watch Later is unsupported by Data API. | Approved minimum-scope OAuth plan and authorized account. |

## Recent accepted milestones

| Milestone | Accepted result |
| --- | --- |
| AVP-004 envelope redesign | `5ffd435`, integrated through `acc062d`: eight authored envelopes, fixed chrome, atomic HWND geometry, 32 responsive rows, 33 transitions. |
| AVP-004 controller redesign | `16b33d9`, integrated through `09f038f`: generic renderer/controller baseline and truthful Y trace routing. |
| AVP-004 generic presentation correction | `9db36b5`, integrated through `289f7d6`: readable generic layout, tray reveal, scroll semantics. |
| DLV-217 | Autonomous Game Launcher accepted through `d57fd06`; integration awaits explicit approval for the known doc-only red aggregate. |
| DLV-216 | Autonomous Spotify Community package accepted and integrated. |
| DLV-220 | Correct retired gesture revocation evidence. |
| DLV-210 | Contained generic Hero Rail while retaining launch/action/focus authority. |
| DLV-215 | Generic consented full-trust Community application runtime. |

Do not mark the continuing delivery goal complete because these milestones
closed. Continue until the user pauses/replaces it or all useful lanes reach a
genuine stop condition. Never push.
