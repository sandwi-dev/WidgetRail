# Compose Multiplatform migration plan

Superseded as the primary direction by the [WinUI 3 migration plan](winui3-migration-plan.md).
Retained as a fallback assessment. The proposal below is historical; no Compose
implementation is scheduled alongside WinUI.

Status: proposed implementation plan, 2026-09-27. This document authorizes no
release or main-branch integration. The current request is to create the plan.
Implementation and physical acceptance remain separate steps.

This plan replaces the native-evolution recommendation as the proposed direction.
It preserves the earlier assessments as historical evidence, not proof that either
frontend is faster. Preview contracts may break; sandboxed community widgets remain
required. Windows is the delivery target. Cross-platform delivery is out of scope.

## Outcome and boundaries

Move the complete UI pipeline to Compose Desktop: application chrome, widget UI,
layout, lazy realization, focus, text, painting, transitions, dialogs, menus and
pinned UI. Compose/Skiko owns its rendering resources and frame scheduling. Do not
put the native renderer, Taffy layout, native preparation scheduler, or custom
geometry-based focus engine underneath Compose.

Keep useful non-UI services where they already work. Kotlin is required for the
trusted Compose frontend, not for every widget/provider. C# workers and Windows
services can remain. Rewriting them solely for language uniformity is not a goal.

The first delivery is an integrated Playnite replacement-host slice, using the
real sandbox and controller path. It is the first part of the intended product,
not a throwaway grid demo. Resolve platform viability before porting all widgets.

## Target architecture

```text
Windows controller/window APIs
             |
      small native adapter
             |
Compose Desktop application (trusted Kotlin/JVM)
  shell + shared widget components + lazy UI + focus + themes + motion
             |
  authenticated, versioned, bounded local IPC
             |
.NET broker/service process
  catalog + permissions + worker lifecycle + capability/provider services
             |
  sandboxed C# workers / explicitly approved full-trust C# workers
```

The native adapter runs as a small library in the trusted frontend where HWND
ownership or low-latency input requires it. Start with a narrow versioned C ABI;
select and pin the JVM binding after validating callbacks, ownership and shutdown.
Callbacks enqueue immutable events; they do not re-enter Compose or block device
threads. Managed broker services remain out of process. No embedded CLR is planned.

Start with a supported Compose-owned transparent, undecorated window. Native code
may configure its HWND through a documented adapter, but must not take over
Skiko's internal DirectComposition root. Custom ComposeScene hosting or an upstream
fork is not the default. If ordinary hosting fails a platform gate, document the
specific failure and evaluate an explicit alternative before further migration.

| Area | Target ownership / disposition |
|---|---|
| `OverlayHost` rendering, layout, focus and preparation | Replace with Compose UI/Foundation and shared Kotlin product components |
| Controller backends, Guide/View+Menu, activation, display/window integration | Extract useful code into the native adapter; frontend receives one normalized input stream |
| `WidgetBridge`, `WidgetRuntime`, `WidgetWorkerHost`, `WidgetCatalog` | Retain service responsibilities; remove native-renderer-specific presentation preparation |
| `PlatformBroker`, `Windows*Provider`, `WidgetApplicationRuntime` | Retain capability/provider responsibilities and full-trust application support |
| `WidgetProtocol`, `WidgetSdk` | Introduce a new explicit frontend contract and C# authoring surface; retain useful business/state helpers |
| `WidgetStyling` / WRSS | Migrate to typed semantic themes and bounded component styling; translate only an explicitly supported legacy subset if useful |
| Native media and window previews | Retain session/security responsibilities; presentation integration must pass the early native-surface gate |
| Settings, credentials, private widget state | Preserve stored meaning and authority; migrate formats deliberately with backups |
| Native UIA provider | Do not assume it can survive without native geometry; validate Compose accessibility and address any required gap explicitly |

There is one production frontend after migration. Temporary A/B executables are
for verification and rollback, not permanent per-widget renderer selection.

## Contracts to establish before porting screens

### Safe declarative UI, without rebuilding a layout engine

Both trust levels use the same UI contract. Workers publish data and action IDs;
the frontend executes trusted Kotlin composables. No worker-supplied Kotlin/JAR,
reflection target, shader, script, native handle, or arbitrary rendering callback
is accepted. Full trust changes worker capabilities, not the default UI mechanism.

Define versioned typed components: stacks, text/rich-text runs, image, button,
action surface/poster, list/grid, navigation rail, text input, slider, toggle,
select/menu, progress, modal and host-owned media slot. Include accessibility
semantics and explicit unsupported-feature errors. Shared components specify
interaction defaults so authors do not reproduce focus rules in every widget.

Collections carry immutable keyed item records and a bounded declarative item
template. The template may bind allowlisted fields and bounded variants; it is
not a general expression language. Compose `LazyColumn`/`LazyRow`/`LazyVerticalGrid`
realizes that template for needed items using stable keys and content types. Do
not eagerly construct a composable tree for every loaded item, or invoke the worker
synchronously during composition/measurement. Heterogeneous cards use typed
variants; custom layouts outside lazy collections remain bounded declarations.

Retaining a concrete subtree-per-item transitional adapter is acceptable only as
a temporary porting aid with a removal ticket. It cannot establish the final
collection performance result. Generate/document schema mappings and conformance
fixtures for Kotlin and C#; do not maintain loosely matched duplicate contracts.

### Data and interaction ownership

- Workers own business state, provider cursors, actions and asynchronous I/O.
- Compose owns focus, scrolling, realization and transient UI interaction state.
- The broker owns authentication, permissions and process/session lifetimes.
- Each published update identifies widget instance, worker session, page/scope,
  collection generation and revision. Discard stale results and actions explicitly.
- Parse and validate updates off the UI thread, then publish one coherent immutable
  UI state. Do not transplant the native prepare/ready/commit pipeline into Compose.
- Local focus, selection feedback, presses and scrolling do not wait for worker IPC.
  Actions needing business logic return asynchronously with bounded busy/error UI.
- IPC has payload/depth/item limits, backpressure, request correlation, cancellation,
  reconnect and protocol negotiation. Superseded state can be coalesced; actions
  requiring acknowledgement cannot silently disappear.

For cursors, distinguish logical data from realized UI. Known and unknown totals,
append/prepend, partial rows, eviction, refresh and filtered resets are first-class.
Page arrival updates keyed data; it must not reset the whole list or recreate its
scroll state. Frontend reports stable anchor/visible-range demand. The worker must
not evict the displayed or pending focus target without a compatible replacement
and acknowledged retention update. Bound retained data without requiring all items
to have layout geometry. Validate these rules through actual Compose behavior;
`LazyVerticalGrid` alone does not implement provider pagination or window eviction.

### Controller navigation and modals

Use Compose focus traversal, focus requesters/groups and lazy beyond-bounds
realization. Do not preserve a second spatial focus engine. Configure product
boundaries in shared components: rail to content, grid column continuity, dialogs,
tray, scrolling edges and restoration. Validate selected dependency versions;
Android TV components and Android remote delivery are not assumed desktop features.

Normalize device input once, including multiple connected controllers, press,
release, repeat, disconnect and analog dead zones. Support current discrete
left-stick/D-pad navigation first. Right stick drives scrolling through Compose.
Optional held-D-pad continuous scrolling is a later interaction mode, not a
prerequisite for making ordinary focus navigation correct. Do not buffer movement
at loading boundaries for replay when data arrives; reversing toward available
content must respond immediately.

Dialogs are child presentation state of the originating widget page, rendered
inside that widget's bounds through an in-tree overlay, rather than a desktop
window positioned at screen center. Give each opening an identity associated
with parent/page/game so Play/Install receives initial focus exactly once. Ordinary
updates retain focus. Dismissal restores a valid parent target and viewport.
Retain an open details dialog across overlay hide/reopen, matching the latest
accepted behavior. Worker/page replacement invalidates its ownership. Pinned
surfaces reject/ignore unsupported modal requests safely and visibly to diagnostics.

Show the details shell from existing game data immediately; load description,
achievements and activity asynchronously. Slow Bridge responses must not delay
opening or dismissal. Restore X game options, Menu additional options, Y refresh,
RS search entry, and existing Library in-list scroll behavior as explicit tests.

### Themes, motion and artwork

Use semantic tokens for colors, typography, spacing, shape, surface depth and
controller prompts; support theme packs and focused/pressed/disabled/selected
states through shared components. Keep poster cover behavior, text wrapping,
scaled-focus overflow space and readable badges. Existing themes must be ported
and checked, not merely approximated by default Material styling.

Workers declare transition purpose and stable identity; global settings resolve
section, dialog, focus and press behavior, speed and reduced motion. Compose owns
animation execution, cancellation and interruption. Preserve the accepted section
Slide and dialog Zoom defaults unless the user changes them. Do not claim Compose
animations all run on independent Windows compositor timelines. Test animation
smoothness under worker replies, decoding, GC and rapid input.

Artwork authority stays host-owned: opaque asset identities, permission-checked
fetching, bounded decoding, target-size variants, cancellation and cache limits.
Choose one decode/cache owner per asset path; do not retain duplicate native and
Skia copies by default. Some media/native surfaces necessarily retain separate
GPU resources; account for them in total process-tree and GPU budgets.

## Execution sequence and deliverables

The IDs below are proposed work packages, not created Plane tickets. Each has a
reviewable artifact and an exit gate. No phase is complete based only on assertion
counts. Platform/media failures are resolved before scaling the widget port.

| Package | Deliverable | Exit gate / dependency |
|---|---|---|
| CM-01: baseline and requirements | Archived pre-WIDGE-293 and current builds, matching packages/configuration; scripted controller scenarios and feature inventory | Reproducible full-host baseline, traces without gaps, agreed test hardware and budgets |
| CM-02: Compose shell and packaging | Pinned Gradle/Kotlin/Compose/JDK toolchain; Compose-owned overlay; native adapter; basic Settings/rail UI; self-contained Windows build | Guide/show/hide, foreground/input ownership, multi-controller, DPI/monitor changes, startup and basic-UI memory pass |
| CM-03: native media and accessibility | WebView2 media session and live window preview in that shell; one pinned window; actual accessibility inspection | Correct clipping, z-order, menus/modal above media, resize/scaling, hide/resume, device loss and accessible navigation; requires CM-02 |
| CM-04: protocol and sandbox vertical slice | Versioned UI/item-template schema, C# builders, Kotlin decoder/components, broker transport; real AppContainer worker | Malformed/stale/over-budget data rejected; capability authority retained; worker crash/restart isolated; requires CM-02 |
| CM-05: Playnite end to end | Real Bridge/provider worker, Home/Library, lazy grids, search/filter, details, game actions, artwork and themes | Full controller/loading/modal sequence passes deterministic and live-provider tests; requires CM-03/04 viability |
| CM-06: music and dynamic content | YouTube Music and Spotify lists/rails, player, queue and background playback | Frequent status updates and variable-height rows do not reset focus or interrupt traversal; native media/pinning lifecycle passes |
| CM-07: remaining product UI | All first-party widgets, Games and Apps, tray/guide/settings, keyboard, menus/selects, prompt assets, other sample widgets and gallery | Feature inventory complete; keyboard/mouse/controller/accessibility, permissions and failure UI verified |
| CM-08: authoring and themes | SDK reference, migration guide, templates, schema conformance, gallery examples, theme-pack migration, rebuilt widget packages | A sample third-party widget can be authored without Kotlin or host-specific workarounds; no silent unsupported styles |
| CM-09: qualification and cutover | Comparative report, packaged install/upgrade/recovery tests, physical acceptance, removal list | All gates below pass; only then request main/release integration |
| CM-10: retire native UI | Remove renderer/layout/focus/preparation code and dependencies no longer used; retain independently needed native integrations | No production path references old UI pipeline; clean build/license/package audit and rerun regression suite |

CM-01 precedes performance claims. CM-02 includes a resource smoke check before a
large port. CM-03 and CM-04 can proceed independently after the shell works, but
neither can be skipped. CM-05 must pass before bulk migration in CM-06/07. Complete
CM-08 alongside those ports. CM-09 is the selection/release gate; CM-10 is reviewed
cleanup, not permission to delete platform functionality used outside the old UI.

## Native surfaces and accessibility: explicit feasibility decisions

WebView2's current composition target is not automatically compatible with Skiko's
window. Test a supported presentation integration with one clear owner of the HWND
and composition hierarchy. Candidate approaches include a supported native-child
surface or a separate coordinated surface; both must prove stacking and clipping
inside the real overlay, including dialogs and animation. Native-child support is
not assumed merely because Swing interop exists. Capture-texture import likewise
needs a supported resource/lifetime path. Do not accept routine full-frame CPU
readback as the default route to meet a demo deadline.

If these approaches fail, record the choices: a maintainable supported integration,
a scoped upstream change, or an explicit feature compromise. Do not silently disable
media, rounded clipping, animation or pinning and call migration complete. Select
this path in CM-03 before the music port.

Official desktop docs currently describe Windows accessibility through Java Access
Bridge, disabled by default, with `jdk.accessibility` required in the distribution.
Test Narrator/NVDA, inspectable semantics, focus events, offscreen item access, dialogs
and the normal installed-user setup. This is not automatically native UIA parity.
If required workflows fail, define and cost a semantics-based Windows adapter or
resolve upstream support before cutover; do not reintroduce an independent layout
engine to supply accessibility geometry.

## Validation that decides whether migration worked

### Baselines and test modes

The pre-WIDGE-293 integration parent is `41751085`; its intervening changes from
`092ee64d` are assessment documentation. Use `092ee64d` as the executable source
baseline and verify matching package versions. The current native comparator is
integration `20ef8dd9` (modal fix source `c3b7955f`). Record actual hashes, toolchain,
settings and package versions for each run. Do not compare an old host with new
SDK packages that it cannot interpret, or reuse settings changed by another build.

Use separate profile/package directories or an explicit backup/restore procedure.
Keep the same data, artwork, themes, display, scale, animations, warm/cold state and
provider latency. Native baseline and Compose need equivalent content and visual
complexity, not identical widget source or obsolete pixel/layout semantics.

1. Contract/unit tests: schemas, capabilities, stale requests, keys, ordering and
   action ownership. Useful prerequisites, never the product acceptance result.
2. Full-host deterministic replay: inject normalized events at the same dispatcher
   used by real controllers. Run the packaged frontend, broker and real sandboxed
   widget against a scripted Bridge-compatible data service with controlled artwork
   and response delays. Do not call the renderer or focus manager directly in this
   layer of testing. Supplement with actual controller/backend tests, since replay
   alone cannot validate Windows delivery or analog normalization.
3. Live Playnite and music runs: installed extensions, real artwork/data/network,
   two controllers, the user's ultrawide display, and interactive physical review.

The repeatable sequence includes deep traversal; above/below-page edges; reversal
before/during/after page arrival; left/right-stick handoff; quick tap versus hold;
partial rows; append/prepend/eviction; sort/filter resets; duplicate and obsolete
responses; provider failures; widget updates during motion; 100/125/150/200% scale;
opening/dismissing/reopening details on different games; hide/show; and page or
worker replacement with a dialog open. Include cold/warm artwork and animations
on/off, but qualification uses normal animations enabled.

### Observability and acceptance

Trace one input/action across normalized input, worker request/result, UI state
publication, focus/scroll change and presentation. Record session/revision/key,
monotonic timestamps, process-tree CPU/memory, GC pauses and available GPU/present
metrics. Use bounded buffered capture with an explicit gap count and sufficient
capacity for the scenario. A run with missing critical events is inconclusive.
A state publication or draw submission is not a displayed frame: use PresentMon/ETW
where applicable and correlate visual transitions; use external/high-frame-rate
recording for latency claims that presentation telemetry cannot establish.

Proposed qualification targets, to freeze in CM-01 rather than relax after results:

- Zero stuck traversal with a reachable loaded row, unintended header escape,
  viewport jumps, wrong-game modal content, stale highlights or replayed input debt
  across the scripted matrix. A loading boundary blocks only unavailable content.
- For warm, continuously moving UI at 60 Hz, presented-frame interval p95 at most
  16.7 ms and p99 at most 33.3 ms, with no recurring page-admission stalls above
  50 ms. Separate rendering, provider waiting and OS scheduling in the report.
  Characterize 120/240 Hz separately; meeting 60 Hz does not establish 240-Hz quality.
- Warm input-to-visible feedback p95 at most 100 ms; details shell visible p95 at
  most 100 ms independent of optional provider data. Measure cold startup/details
  separately. Cached/uncached provider completion time is not modal-shell latency.
- Meaningful measured improvement against pre-WIDGE-293 on the reported loading,
  reversal and modal scenarios, plus physical acceptance. A native baseline already
  inside the targets needs preserved quality, not an artificial percentage win.
- Freeze idle-visible, hidden, populated-library, media and peak memory budgets
  from CM-01 data. Initial planning ceiling: warmed total private committed memory
  no more than 25% above the equivalent pre-WIDGE-293 run; this is a proposed product
  budget, not a prediction that a JVM+.NET implementation will meet it. If inadequate,
  make the explicit performance/memory tradeoff before the bulk port. Measure GPU
  allocations separately; JVM reserved heap alone is not resident memory.
- No sustained hidden-window repaint loop; background playback/provider work is
  accounted for separately. No monotonic memory growth over repeated 30-minute
  browsing and open/close runs; check post-idle resource reclamation.
- Packaged accessibility, media/pinning, input ownership and sandbox gates pass.

Report median/p95/p99, worst events, run count and variance over repeated runs.
Do not substitute millions of assertions, an empty Compose window, reduced visual
complexity or a single good physical run for these gates. Failed gates identify a
specific integration/contract issue or a reason to reconsider migration, not an
unbounded commitment to patch until it looks acceptable.

## SDK, package and release migration

Version the UI contract explicitly and publish supported feature negotiation.
Keep C# as a supported authoring language. Ship examples for keyed lazy collections,
async detail dialogs, cancellation, navigation scopes, dynamic text, custom themes,
media lifetime and full-trust application workers. Clearly distinguish widget data
state from frontend interaction state and toolkit-owned layout/animation.

Do not promise binary compatibility with old UI packages. Prefer a clear minimum
frontend/SDK version and rebuild bundled/sample widgets. An optional importer can
convert supported styles/data; it must report unsupported features. No legacy
native renderer fallback is planned for old packages. Keep private state and
credentials separate from package/UI version migration; use reversible backups
and explicit versioned migrations for user settings and layout.

Bundle a trimmed Java runtime with the Windows installer; do not require users to
install Java. Include accessibility modules, Skiko natives and retained native/.NET
runtime requirements. Pin dependencies, retain notices/licenses, lock builds, and
verify installation on a clean user machine. Test upgrade, rollback, uninstall,
crash recovery, package installation, controller permissions and media dependencies.

Create implementation worktrees under a new `codex/compose-*` development line.
Do not merge the native overhaul into main as a prerequisite. Preserve both native
comparators and their evidence. Compose task changes can accumulate in a dedicated
integration branch under the existing workflow; main merge, pushing, installers
for distribution and release require the applicable user authorization. Creating
this plan does not install toolchains, replace the running candidate or launch a
migration implementation.

Calendar estimates should follow CM-02/03: media hosting and accessibility are
unresolved architectural costs. This is a multi-phase product migration, not a
few-hour renderer swap. Each package ends with a runnable/reviewable result and
an evidence summary; phase completion does not imply final product acceptance.

## Sources and existing evidence

- [Current architecture](platform-architecture.md) and [security boundary](security-and-trust.md).
- [Earlier toolkit assessment](ui-architecture-assessment.md): source-inspected
  shared focus/lazy infrastructure and native integration constraints. Historical
  version observations are not a new dependency selection; re-pin in CM-02.
- [Native evolution assessment](native-ui-evolution-assessment.md),
  [collection transactions](native-collection-transactions.md), and
  [modal ordering regression](native-modal-ordering.md): preserve findings and
  test limitations when selecting which code to retire.
- Official documentation checked 2026-09-27:
  [desktop windows](https://kotlinlang.org/docs/multiplatform/compose-desktop-top-level-windows-management.html),
  [Windows accessibility](https://kotlinlang.org/docs/multiplatform/compose-desktop-accessibility.html),
  [self-contained distribution](https://kotlinlang.org/docs/multiplatform/compose-native-distribution.html).

No Compose runtime performance, media interoperability or accessibility parity
has been demonstrated by this planning task. Those are explicit delivery gates.
