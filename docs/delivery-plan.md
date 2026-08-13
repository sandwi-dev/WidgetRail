# Delivery plan

Status: reviewer-owned production and Avalonia-evaluation execution queue,
2026-08-13 00:00 -07:00

Planning owner: independent review and delivery-planning agent

Execution owners: widgets, platform, and isolated Avalonia prototype

This file is the sole authority for implementation selection. The complete
pre-compaction state is preserved in
[the 2026-08-12 21:06 snapshot](history/delivery-plan/2026-08-12T21-06-00-07-00.md).
That snapshot and earlier files under history/delivery-plan are historical
evidence, not implementation authority.

## Current accepted baseline

- Local accepted product baseline: 01af13c.
- Local accepted Avalonia experiment baseline: corrected AVP-003 through main
  `9d3ad8e` (branch commit `2a3c722`).
  This does not change the production overlay baseline or authorize a
  production migration.
- DLV-219 is accepted and integrated through main 01af13c; its focused
  Release Settings suite passed 60/60.
- DLV-213 is accepted and integrated through main b73eaa5 (implementation
  commits cc3bc0e and b73eaa5).
- DLV-212 is accepted and integrated through main 5a6ce0b.
- DLV-209 is accepted and integrated through main 56a09fb.
- DLV-197 is integrated as 6e2c7ff, but its private Game Launcher path is
  superseded. DLV-205 is useful evidence only and must not be integrated or
  receive further product investment.
- Spotify 0.2.15 is installed, selected, and enabled through the supported
  Community package path. Its new unsigned content identity has no inherited
  capability grant, so live account behavior remains behind explicit consent.
- The accepted 01af13c Release is visibly running from the main output as PID
  17592, launched 2026-08-12 22:55 -07:00 with `OverlayHost.exe --show` after
  coherent packaging. Its startup interval admitted ordinary first snapshots
  while cycling through the tray with no fatal error. After each newly accepted visible milestone,
  rebuild/copy the exact Release artifacts into main, replace that exact
  planner-owned process gracefully, visibly launch `OverlayHost.exe --show`,
  exercise every first page when controllable, and inspect the exact
  startup/test log.
- Physical appearance remains the user's verdict. Semantic, UIA, log, timing,
  and geometry evidence must not be described as a screenshot or clipping proof.

## Binding product boundary

- Games & Apps remains a bundled first-party widget.
- Game Launcher and Spotify are ordinary Community applications.
- Community packages have two explicit trust tiers:
  - sandboxed: the existing AppContainer/capability path with no ambient
    authority;
  - full-trust: an explicitly disclosed package-owned ordinary user process
    that may use arbitrary user-level APIs and OS facilities.
- Never silently promote a sandboxed package to full trust. Full trust is not a
  security sandbox or containment claim.
- A full-trust Community application may use its own HTTP clients, OAuth,
  databases, files, registry, COM/WinRT, child processes, native libraries, and
  third-party SDKs without a framework change.
- Community domain behavior belongs in the package. Core product assemblies
  must not contain Spotify, Game Launcher, IGDB, SteamGridDB, game-store,
  publisher, package-ID, assembly/type, element-ID, style, or tree-shape
  special cases.
- The generic App Library provider may remain for bundled Games & Apps and for
  sandboxed packages that voluntarily consume it. It is not the required
  architecture for Game Launcher.
- The shared host still bounds package ingestion and everything submitted into
  or owned by the host: IPC frames, strings, render trees, actions, update
  admission, queues, caches, sessions, and native/GPU resources. Those limits
  must not become arbitrary quotas on the Community application's own CPU,
  memory, files, database, sockets, dependencies, or child processes.
- Launcher Experience packs remain data-only and host-owned. A Community
  application may opt into a generic declared presentation contract, but gains
  no data/provider/launch/file/script/global-settings authority from it.
- Avalonia is the leading replacement candidate for the custom native UI stack.
  Evaluation happens only under `experiments/AvaloniaOverlayPrototype`. No
  production renderer migration, GBSS removal, or widget cutover is implied
  until the user accepts retained AVP evidence and a migration architecture.
- The candidate production presentation stack is intentionally bounded:
  .NET 10/C#, stable Avalonia Desktop 12 with its Skia renderer, AXAML and
  Avalonia styles/control themes, compiled XAML bindings, CommunityToolkit.Mvvm
  for shell/page state and commands, direct Microsoft dependency injection and
  logging abstractions only where lifecycle ownership benefits, Avalonia
  `XYFocus`/`FocusManager`, composition animations for performance-sensitive
  shell motion, and virtualized `ListBox`/`ItemsRepeater` collections. The host
  does not adopt ReactiveUI, DynamicData, a generic-host bootstrap, Serilog,
  EF Core, FluentAvalonia, SukiUI, or another navigation/theme framework by
  default. A specific measured requirement may add one later. Community
  applications remain free to choose their own ordinary .NET application
  stack; the public widget contract stays semantic and UI-framework-neutral.
- Testing for the candidate stack uses the repository-required MSTest.Sdk
  4.3.2, ordinary unit tests for state/services, manually configured
  Avalonia.Headless/Skia tests for layout, focus and rendered frames, and a
  small Windows UIA/Appium-class packaged suite for critical real-window flows.
  Headless evidence never replaces controller, compositor, DPI, or physical
  display testing. Windows-only input/window/media integration remains behind
  narrow adapters. The AVP XInput adapter is retired by migration; the existing
  native Microsoft GameInput/Guide implementation is authoritative.
- Retain the presentation-neutral `WidgetBridge` backend and its authenticated
  protocol. Replace only the native C++ presentation-side `WidgetBridgeClient`
  with a typed managed presentation-session facade. Avalonia controls never
  parse bridge transport or connect directly to widget processes.
- AVP-004 uses one generic current-`WidgetProtocol` semantic-tree adapter. It
  must not rebuild Settings, Audio Mixer, Network, media, library, Spotify,
  YT Music, or Game Launcher as domain-specific Avalonia pages.
- Hold DLV-216, DLV-217, and DLV-218 during AVP-001 and the architecture
  decision. Those milestones assume the current declarative presentation path
  and would create avoidable migration or deletion work.

## Execution protocol

Implementation tasks follow
[implementation-agent-goal.md](implementation-agent-goal.md).

- Each lane executes only its one Assigned milestone, then immediately consumes
  the first same-lane Ready milestone in document order.
- A lane does not edit this file or reviewer-owned roadmap/review/issue files.
- Findings outside scope are reported for planner triage. Only a reproducible
  P0 or destructive/data-loss risk may preempt active work.
- Review corrections queue next and do not interrupt a coherent milestone
  already in progress.
- Shared protocol and architecture changes are serialized. Concurrent work must
  have exclusive production ownership.
- A lane consumes new main or a held dependency only at a clean committed
  boundary after planner instruction. Substantial conflicts stop.
- Verification is proportional: Tier 1 affected Release suites; Tier 2 the
  smallest changed boundary; Tier 3 only when explicitly named. Every command
  is bounded.
- Do not repeat an unchanged failing command. Existing executable suites keep
  their runner; new managed test projects use MSTest.Sdk 4.3.2.
- Discard malformed or clipped captures immediately. Validate live behavior,
  state, semantics, accessibility, timing, and logs; the user reports visual
  defects.
- Security stabilization remains frozen absent a reproducible P0,
  demonstrated threat-model violation, or planned-release blocker.
- No push, external credentials, external publication, destructive recovery,
  or substantial conflict resolution.

## Avalonia prototype lane

Task: Implementation agent — Avalonia prototype lane

Branch: codex/avalonia-prototype

### Recently completed — AVP-001: transparent Avalonia overlay shell

Integrated experiment commit: `bd1ab7f`.

User hands-on verdict on 2026-08-13: the transparent overlay looks good, page
motion is smooth, and no clipping or black-transition artifact was reported.
AVP-001 therefore establishes visual feasibility, one stable shell/tray, four
representative Avalonia pages, standard controls/UIA, and an honest initial
resource baseline. It does not establish controller-first interaction. The
user explicitly directed the remaining keyboard/evidence corrections to move
into AVP-002 rather than blocking the next experiment.

### Recently completed — AVP-002: controller-first Avalonia interaction

Accepted and integrated through main `09b40da` (`f876a1b`, `2ee2de0`, and
`09b40da`). This remains an isolated feasibility project under
`experiments/AvaloniaOverlayPrototype`, not a production migration. The user
confirmed that Tray Up entering content and Down from the final content element
returning to the tray are intentional controller behavior. Left/Right tray
cycling retains tray focus; Down or A/Enter remains the direct entry action;
Back restores the selected tray item.

The implementation uses Avalonia `FocusManager.FindNextElement`/`XYFocus` with
bounded search roots instead of visual-tree index navigation. It retains only
narrow explicit Audio Mixer column links, remembers per-page focus, shares one
keyboard/controller semantic router, and preserves controller lifecycle and
neutral-gating behavior. The focused Release suite passed 18/18 in the
implementation worktree. One exact-main measurement from `09b40da` retained
executable SHA-256 `9ABB793D55D32D4E39B19E93E22B8EE0102D8A602E7333801B9590BB8FBE696B`,
about 253.79 MiB visible private memory, 191.20 MiB hidden private memory,
0.146% hidden CPU, and passing frame/transition diagnostics. The user's
2026-08-13 live verdict accepted the controller feel, focus behavior, visual
quality, and smoothness. AVP-002 therefore closes the isolated visual,
controller-routing, and spatial-navigation feasibility gates. This verdict
authorizes AVP-003 only; it is not a production migration decision.

Historical assignment detail follows until the next triggered control-plane
compaction:

Select a maintained Windows-capable controller library/adapter only after a
short recorded comparison against a narrow in-house Microsoft GameInput
adapter. Consider maintenance, Windows support, native deployment, device
identity/reconnect, analog dead zones, button press/release/repeat semantics,
licensing, package size, and compatibility with .NET 10/Avalonia. Prefer a
maintained library when it reduces real lifecycle/input ownership code without
creating a second UI or focus system. Do not reuse or reference production
Game Bar input classes.

Implement one prototype-owned input adapter that translates physical D-pad or
left-stick directions, A, and B into the same semantic navigation/action path
as keyboard input. Keyboard and controller must share one routing policy and
one focus/page state; neither path may synthesize OS keyboard events or invoke
Avalonia controls through a parallel identifier registry.

Also correct the interaction and evidence gaps carried from AVP-001:

- Remove the prototype's visual-tree-order `FocusNavigator` algorithm. Use
  Avalonia 12 `XYFocus`/`FocusManager` spatial navigation as the default target
  resolver for both keyboard and controller directions. Define bounded focus
  scopes for tray, active page, scrolling collection, and modal surfaces so
  navigation cannot leak across unrelated regions. Declarative `XYFocus.Up`,
  `Down`, `Left`, or `Right` overrides are permitted only for a small number of
  genuinely ambiguous boundary transitions; do not hand-author a complete
  per-page focus graph.
- In tray focus, Left/Right directly admits the previous/next representative
  page with wraparound and keeps tray selection/focus consistent. It must not
  require a second Enter/A activation. Cycling pages must retain focus on the
  selected tray item; it must not automatically move focus into page content.
  Down or A/Enter explicitly enters content, while Back restores the selected
  tray item. Remember the last valid focus per page for re-entry, falling back
  to the page's declared initial focus only when that element no longer exists.
- On a focused Slider, Left/Right adjusts its value while Up/Down exits through
  deterministic directional focus. Escape, keyboard `B`, and controller B
  reach Back even when a child Button, Slider, or ScrollViewer owns focus.
- Controller A invokes the focused standard Avalonia control exactly once.
  Keyboard Enter continues to use that same semantic activation route.
- Disconnect, reconnect, device loss, hidden lifecycle, focus loss, and route
  replacement clear held/repeat state. Analog dead zones and repeat timing are
  explicit, bounded, and testable. Keyboard plus controller cannot cause
  double-dispatch.
- Replace the tautological scale assertion with actual Avalonia render-scale
  and work-area fixtures. Enumerate every required visible button and text
  element, require nonzero contained bounds, and describe viewport-clipped
  collection descendants honestly.
- Sample the Avalonia transition surface at start, midpoint, and completion so
  endpoint brush inspection is not presented as proof against transient
  fallback. Preserve the user's live smooth/no-black-artifact verdict as the
  physical compositor evidence and do not overclaim automated pixel proof.

Acceptance criteria:

1. A documented dependency decision explains the chosen controller path and
   rejected alternative without claiming cross-platform value the product does
   not need. Restore/build stays bounded and reproducible.
2. A real physical-controller path supports D-pad/left stick, A, and B in the
   visible prototype. The implementation remains isolated and generic; it adds
   no production host, Widget SDK, widget identity, or provider-specific code.
3. Tray cycling, slider exit/adjustment, child-focused Back, standard-control
   activation, scroll down/up, reconnect, hidden cancellation, and repeat
   ownership are covered through the shared router at focused controls. Helper-
   only tests are insufficient.
   Spatial tests must prove Audio Mixer Up/Down remains in the aligned Slider
   column, tray cycling retains tray focus, explicit content entry is required,
   Back restores tray focus, and every page has no unreachable element,
   accidental cross-scope jump, focus trap, or directionally unstable loop at
   the supported work-area/scale matrix.
4. Updated responsive tests exercise real Avalonia render scaling at the three
   work areas and 100/125/150 percent, including nonzero contained bounds for
   every required visible action/text element.
5. Transition diagnostics cover start/midpoint/completion while the live user
   verdict remains authoritative for physical smoothness and transparency.
6. Standard Avalonia UIA remains the only accessibility tree. Controller input
   changes focus and invokes the same controls visible to UIA.
7. Run only the focused bounded AVP suite and one exact-commit ordinary Windows
   measurement. Keep resource misses and unavailable GPU evidence honest. The
   planner launches the accepted copied runtime for physical-controller review.

Out of scope: production integration, remote Community widget surfaces, GBSS
adaptation, a remapping UI, rumble, controller-specific widget APIs, more
representative pages, publication, credentials, and an Avalonia migration
decision.

Required evidence: focused Release unit/component tests; one copied ordinary
Windows prototype lifecycle; retained metrics and exact commit/runtime
provenance; planner live launch and user visual verdict. No canonical product
aggregate.

The historical AVP-002 stop condition was satisfied by the user's live
controller and visual verdict. Its production-host, privilege, credential, and
undocumented-window-manipulation prohibitions remain in force for later AVP
experiments.

### Recently completed — AVP-003: production-stack architecture slice

State: Accepted and integrated through main `9d3ad8e` (`f6d0e2e`, `9d3ad8e`;
branch commits `f076622`, `2a3c722`). The correction binds UIA/focus identity
to exact `RemoteWidgetItemId`, admits only snapshot-declared exact item/action
tuples, and schedules bound publication explicitly onto the Avalonia UI thread.
The focused suite passed 23/23. Exact evidence recorded 10,000 items with 17
realized containers, stable identity through reorder, 162.54 MiB visible and
231.29 MiB hidden private memory, and 0% hidden CPU.
Dependencies: AVP-001 and AVP-002 accepted. Concurrency: isolated experiment
only; it may run while production lanes continue because it owns no production
file. Correct `f076622`, stop at one reviewable child commit, and do not start
AVP-004 automatically.

Required correction:

- derive presentation, UIA, and focus-restoration identity from the exact
  safely encoded `RemoteWidgetItemId`, not list position, and prove the same
  item and AutomationId survive a latest-wins insertion/reorder;
- make the current semantic snapshot declare a bounded typed action set per
  item and admit only an exact current item/action tuple, with unknown-item and
  undeclared-action rejection evidence; and
- keep the remote contract UI-framework-neutral while a presentation-owned
  scheduler explicitly marshals bound view-model mutation to Avalonia's UI
  thread, including a worker-thread completion test.

Reuse the existing architecture and run only affected tests while correcting,
one final focused suite after the correction commit, and one fresh exact-commit
measurement. The planner must not launch the rejected `f076622` candidate.

Refactor only the isolated experiment into one representative production-shaped
vertical slice. Use CommunityToolkit.Mvvm with compiled bindings for typed
immutable shell/page state and commands; keep focus, controller input, window
lifecycle, and navigation policy in presentation services rather than view
models. First record the current code-behind responsibility map, then leave the
shell/page view models independently testable without a Window or controller.

Use direct Microsoft dependency-injection abstractions only for explicit
singleton/scoped/transient ownership and only if a measured comparison shows an
acceptable startup/private-memory delta versus the existing manual composition.
Do not add a generic-host bootstrap, configuration framework, or logging stack.
Keep the current visual language on Avalonia styles/control themes over the
built-in Fluent control base; do not adopt FluentAvalonia, SukiUI, ReactiveUI,
DynamicData, Serilog, EF Core, or another navigation/theme framework.

Replace the prototype transition with a native Avalonia page/composition
transition and retain start/midpoint/completion evidence. Replace the small Game
Launcher fixture with a selectable, virtualized 10,000-item list or grid using
ordinary Avalonia virtualization. Prove bounded realized-container count,
stable semantic item identity across recycling, exact focused-item restoration,
scroll down/back, controller spatial movement, and standard UIA without creating
a custom accessibility tree or manual container cache.

Define one small experiment-owned, UI-framework-neutral semantic snapshot/action
contract and project a fake asynchronous remote widget through an adapter into
typed Avalonia view models. Neither that contract nor the fake widget may
reference Avalonia, controls, view models, XAML, or UI-thread types. Prove a
delayed update, latest-wins replacement, retained last-good failure state,
action dispatch, and deactivation cancellation without importing the production
Widget SDK or renderer.

Acceptance criteria:

1. AXAML uses compiled bindings and typed data templates. A separate invalid
   fixture proves an incorrect binding fails the bounded build; do not break the
   real project merely to demonstrate this.
2. View models own immutable presentation state and commands only. Focus,
   controller routing, page transitions, window visibility, and UIA remain
   Avalonia presentation concerns with the accepted AVP-002 behavior intact.
3. The 10,000-item surface remains responsive and virtualized. Retained evidence
   records total items, realized containers, focused semantic identity before and
   after recycling, scroll-return result, private memory, and switch latency.
4. The fake remote projection remains UI-framework-neutral and demonstrates
   bounded latest-wins lifecycle, last-good failure, and exact action identity.
5. Standard Avalonia UIA remains the only accessibility tree. Keyboard and the
   accepted XInput adapter continue through one semantic router and the same
   focused controls.
6. Verification is proportional: during implementation run only changed focused
   tests; at the final clean boundary run one bounded Release build and focused
   MSTest.Sdk 4.3.2 unit/headless/UIA suite, then one exact-commit ordinary
   Windows measurement. Do not build an exhaustive navigation matrix or run the
   production aggregate.
7. Retain exact commit/executable provenance, dependency versions, startup,
   visible/hidden memory and CPU, transition samples, realized-container count,
   and unavailable GPU evidence. The planner launches the copied candidate once
   for the user's live verdict.

Out of scope: any production host/SDK/widget/catalog/package edit, GBSS adapter
or removal, Community package migration, real remote package execution,
database/web/video surface, NativeAOT, installer/publishing work, broad design-
system polish, AVP-004, or a final migration decision. Stop if the architecture
requires Avalonia types in the public widget contract, a second focus/UIA tree,
production edits, privileged installation, credentials, or a material resource
increase with no clear owner.

### Avalonia Ready queue

### Current assignment — AVP-004: complete polished overlay prototype

State: Assigned as a reuse-first migration program; implementation remains
paused until the planner reconciles and preserves the standing task's existing
uncommitted foundation work against this assignment. Do not discard, clean,
commit, or silently repurpose that work. If incompatible changes cannot be
separated without loss, stop for the user.

Architecture contract: [Avalonia presentation migration
plan](avalonia-migration-plan.md).

Baseline: local main `9d3ad8e`; accepted Avalonia branch commit `2a3c722`.
The current production overlay and accepted native Release remain authoritative
through the entire candidate build. No production cutover or renderer deletion
is part of AVP-004.

Binding migration direction:

- retain the existing Widget SDK/protocol, package/catalog/runtime/bridge,
  lifecycle, trust, persistence, provider, built-in widget, and Community
  process/domain implementations;
- retain or extract the existing native controller platform owner, including
  the supported GameInput Guide callback, background/exclusive focus policy,
  window-thread debounce/toggle, device lifecycle, and separately quarantined
  legacy compatibility path; do not implement a second C# GameInput owner;
- replace the presentation boundary: native declarative renderer/compositor,
  shell/tray visuals, layout, animation, GBSS rendering, focus projection, and
  accessibility projection move behind Avalonia;
- implement one generic adapter from the current semantic widget presentation
  and action/focus identities into standard Avalonia controls. Do not rewrite
  eight domain pages or fork widget business logic merely to obtain Avalonia
  visuals; and
- use narrow platform interop or an extracted reusable native service where
  existing C++ ownership cannot be consumed directly. Extraction must preserve
  the current production behavior and tests rather than copy its implementation
  into the experiment.

#### AVP-004-SESSION — managed presentation-session extraction

Owner: temporary `avalonia-session` task on `codex/avp004-session`, created
only after the planner records exact exclusive files from a clean accepted
baseline. It may perform a behavior-preserving extraction from the current
managed bridge/runtime code and add focused tests. It does not edit Avalonia
shell/pages, native host/input, widget implementations, public widget
semantics, catalog authority, or reviewer documents.

Objective: expose a typed managed presentation facade over the retained
`WidgetBridge` backend. It owns descriptor enumeration, lifecycle, validated
latest snapshot/last-good failure publication, exact action admission inputs,
quick actions, artwork results, restart/invalidation, and bounded diagnostics.
It must reuse the existing authenticated bridge transport and protocol rather
than add another pipe, JSON model, process path, or direct widget connection.

Acceptance: focused bridge/runtime tests prove identical sandboxed and
full-trust session behavior, generation/snapshot/action/input-scope identity,
stale rejection, lifecycle drain, restart, last-good failure, and bounded
transport. No Avalonia type crosses this facade. Commit once and stop.

#### AVP-004-PLATFORM — native platform interop extraction

Owner: temporary `avalonia-platform` task on `codex/avp004-platform`, created
only after exact file ownership excludes the active production platform
assignment. It may extract, not redesign, supported native input/window policy
and add focused parity tests. It does not edit WidgetBridge/runtime, Avalonia
views, widgets, providers, Community packages, or reviewer documents.

Objective: expose one narrow versioned native boundary for the existing
Microsoft GameInput owner and essential Win32 overlay integration: supported
Guide callback, background/exclusive policy, device/reconnect/repeat/neutral
state, visibility quarantine, debounce/toggle, targeting, DPI/work-area
placement, and focus/visibility events. The Avalonia shell owns the only
presentation window/focus tree. Do not create a second overlay HWND or managed
GameInput reader. Keep the legacy Guide compatibility adapter quarantined.

Acceptance: current production tests plus focused interop parity tests prove
Guide show/hide, device loss/reconnect, no double toggle, hidden/focus reset,
placement containment, and clean shutdown. Production native behavior remains
unchanged while the candidate is incomplete. Commit once and stop.

#### AVP-004-INTEGRATION — generic Avalonia migration candidate

Owner: standing `avalonia-prototype` task on `codex/avalonia-prototype` after
planner acceptance of both extraction commits. Integration order is SESSION,
PLATFORM, then candidate wiring. Substantial conflicts stop.

Objective: replace the AVP fake remote contract and fake widget pages with
direct use of current `WidgetProtocol`, the managed presentation session, and
the native platform boundary. Implement one generic adapter for every current
`ViewNodeKind`, responsive visibility/surface hint, stable focus/action/input-
scope identity, scrolling/pagination/anchors, quick actions, artwork, and
generic advanced-presentation slot. Use standard Avalonia controls/UIA,
compiled bindings, styles/control themes, virtualization, `FocusManager`/
`XYFocus`, and explicit UI-thread publication.

All currently installed bundled and Community widgets must enter through their
ordinary catalog/package/runtime/bridge/domain implementations. The candidate
may add generic control templates; it may not hand-author a widget page or
branch on package, publisher, assembly/type, widget ID, element ID, style
class, provider, store, Spotify, YT Music, or Game Launcher identity.

Required acceptance:

1. One simple widget, one provider-backed widget, one 10,000-item/full-
   application widget, and one Community package pass end-to-end first; final
   AVP-004 evidence covers every installed tray widget through the same adapter.
2. Guide, D-pad/stick, A, B, tray cycling, entry/exit, hold Y, contextual
   actions, slider adjustment/spatial exit, scroll return, reconnect/device
   loss, focus loss, hide/show, and per-widget focus memory match production
   policy without a second input or focus authority.
3. Exact runtime generation, snapshot, element, action, active input scope,
   collection item, and focus-persistence identity survive rendering,
   virtualization, responsive reflow, replacement, and action dispatch.
4. Compact 420x340 and 978x466, standard, wide, and 100/125/150-percent render
   scaling keep essential controls reachable by reflow/scrolling. Standard
   Avalonia UIA is the only accessibility tree.
5. The accepted AVP motion/design quality remains: stationary tray, native
   start/mid/end transitions, reduced motion, no intentional black fallback,
   coherent tokens, loading/empty/error/disabled/busy states, and polished
   first pages. The user owns the physical visual/controller verdict.
6. Verification is proportional: focused suites per extraction, one final
   bounded AVP Release suite, one ordinary Windows bridge/runtime lifecycle,
   and one exact-final-commit measurement. No product aggregate merely for AVP.
7. Retain exact commit/runtime hashes, dependency versions, startup/switch
   samples, visible/hidden memory and CPU, realized-container maxima, and
   honest unavailable GPU data. Visible memory stays below 500 MiB; investigate
   anything above 350 MiB before acceptance.

Stop for credentials, destructive handling of the preserved uncommitted work,
undocumented new input/window APIs, a second transport/authority, public
protocol expansion not generically required, substantial conflicts, or a
production cutover. AVP-005 and native-renderer/GBSS deletion remain out of
scope.

#### Superseded AVP-004 page inventory — not implementation authority

The historical material below is retained only as a parity checklist until the
next threshold-triggered compaction. Its fake services, page lanes, file
ownership, integration order, and out-of-scope production-reuse statements are
void. Implement the behaviors through real widget snapshots and the generic
adapter above; do not execute any `AVP-004-SYSTEM`, `MEDIA`, or `LIBRARY` task.

Objective: turn the isolated Avalonia experiment into a cohesive, polished,
controller-first prototype of the complete current overlay rather than another
small architecture sample. Include every current tray experience:

- bundled Settings, Now Playing, Games & Apps, Audio Mixer, and Network
  Controls;
- Community-reference Spotify, YT Music, and Game Launcher; and
- the stationary shared tray, controller guide, responsive shell, transitions,
  loading/empty/error/disabled/busy states, and standard UIA.

This is still an isolated presentation and interaction prototype. Reuse feature
semantics from the current implementation through deterministic prototype-owned
services and fake asynchronous data. Do not import production widget/provider
assemblies, mutate product code, use credentials, call live providers, or imply
that the Community pages are built into the future host. The three Community
pages must remain visibly identified as replaceable package projections.

#### AVP-004-FOUNDATION — integration lead

Branch/task: `codex/avalonia-prototype`, standing Avalonia prototype task.
Baseline: accepted corrected AVP-003 commit. Concurrency: first, before the page
tasks are created.

Create the shared production-quality design and integration foundation:

- one responsive shell, stationary tray, page host, modal layer, notification
  layer, shared controller guide, focus memory, reduced-motion policy, and
  native Avalonia transition owner;
- coherent Avalonia tokens/control themes for typography, spacing, shape,
  elevation, focus, selection, validation, status, cards, buttons, sliders,
  tabs, lists, grids, dialogs, and scroll affordances, with no GBSS or second
  styling system;
- typed prototype service/state contracts and feature registration that let a
  page lane supply a view/view-model/service fixture without editing shell or
  navigation owners; and
- a supported prototype-local Microsoft GameInput path for Guide-driven
  show/hide while retaining the accepted D-pad/left-stick/A/B path, neutral
  gating, focus restoration, debounce, hidden lifecycle, and one semantic
  input router. If Guide cannot be read through a documented supported API,
  stop that subfeature with evidence rather than using undocumented hooks.

Commit only the shared foundation and its focused tests. The planner reviews
it, then creates the three temporary page tasks from that exact commit.

#### AVP-004-SYSTEM — system controls page lane

Temporary branch/task: `codex/avp004-system-pages`, `AVP-004 — system pages`.
Exclusive ownership: experiment directories for Settings, Audio Mixer, Network
Controls, and their focused tests/fixtures. Do not edit shared shell, design
tokens, navigation, project files, measurement code, media/library directories,
or reviewer documents.

Implement polished, interactive feature parity for:

- Settings: installed experience list with Built-in/Community/trust/status
  labels, enable/disable/restart/update states, Launcher Experience selection,
  appearance/interface-scale controls, startup/behavior toggles, diagnostics,
  confirmations, and B/Back through every child route;
- Audio Mixer: master output and microphone volume/mute, input/output device
  selection, virtualized per-application sessions, active/idle state, exact
  slider navigation, reverse scrolling, and the LB/RB/X tray-shortcut model;
  and
- Network Controls: current connection, adapter state, available-network
  refresh, connect/disconnect/secured states, empty/offline/error/retry paths,
  and bounded virtualized results.

All behavior uses deterministic async prototype services with latest-wins,
last-good, cancellation, and failure states where applicable.

#### AVP-004-MEDIA — media page lane

Temporary branch/task: `codex/avp004-media-pages`, `AVP-004 — media pages`.
Exclusive ownership: experiment directories for Now Playing, Spotify, YT Music,
and their focused tests/fixtures. Do not edit shared shell, design tokens,
navigation, project files, measurement code, system/library directories, or
reviewer documents.

Implement polished, interactive feature parity for:

- Now Playing: session selection, artwork/fallback, metadata, transport, seek,
  timeline, volume/mute, unavailable/permission/error/retry states, and stable
  focus through session refresh;
- Spotify: Player, Queue, Playlists, playlist contents, Devices, playback
  transfer, transport, seek, shuffle/repeat, pagination with stable focus,
  authorization/disconnected/loading/last-good/error/retry states, and compact
  plus expanded layouts; and
- YT Music: disconnected/connecting/pairing/connected/unavailable states,
  pairing instructions, now-playing artwork/metadata/progress, transport,
  shuffle/repeat/rating, queue/library samples, refresh, and reconnection.

Use deterministic local fixtures only. Do not add OAuth, WebView, credentials,
provider clients, EME, or media playback processes.

#### AVP-004-LIBRARY — library page lane

Temporary branch/task: `codex/avp004-library-pages`, `AVP-004 — library pages`.
Exclusive ownership: experiment directories for Games & Apps, Game Launcher,
and their focused tests/fixtures. Do not edit shared shell, design tokens,
navigation, project files, measurement code, system/media directories, or
reviewer documents.

Implement polished, interactive feature parity for:

- Games & Apps: persisted-items-first presentation, background discovery,
  game/application classification, add/remove, artwork/fallback, search,
  refresh, launch/details, empty/loading/error states, and stable restoration;
  and
- Game Launcher: large virtualized library, artwork/fallback, Search,
  collections/source filters/sort, favorites/hidden/recent/grouping, details,
  exact semantic actions, paging/continuation, source health, retained last-good
  failure, and compact/standard/wide layouts.

Preserve AVP-003 item-stable identity and snapshot-owned exact action authority.
No store APIs, filesystem discovery, launch commands, credentials, or product
providers are permitted in the prototype.

#### AVP-004-INTEGRATION — integration lead

After independent planner acceptance of all three page commits, the integration
lead incorporates them in SYSTEM, MEDIA, LIBRARY order and owns all shared
wiring. Substantial conflicts stop; page lanes do not resolve shared conflicts.

Complete one senior-level polish pass across the integrated prototype:

- consistent hierarchy, density, iconography, alignment, button text, focus
  treatment, scroll indication, empty/loading/error copy, and motion;
- controller traversal and B/Back for every first page and child route, direct
  tray cycling without content-focus theft, intentional tray/content boundary
  transitions, slider Left/Right adjustment plus spatial Up/Down exit, modal
  trapping/restoration, and Guide show/hide when the supported foundation path
  succeeded;
- responsive containment at minimum 420x340, current compact 978x466, standard
  1316x896-class, wide, and 100/125/150-percent render scaling, with essential
  controls reachable through reflow or scrolling rather than clipping;
- native start/mid/end page transitions with a stationary tray, no deliberate
  black fallback, reduced-motion behavior, bounded realized containers, and
  standard Avalonia UIA only; and
- an in-prototype feature matrix identifying each original feature as working,
  realistically simulated, credential/hardware blocked, or intentionally out
  of scope. Never label a fake provider operation as live integration.

Acceptance criteria:

1. All eight tray experiences open from one shell, have polished populated and
   failure-state presentations, and expose their principal original interaction
   paths without placeholder-only pages.
2. Keyboard and physical controller share one semantic router. Tray cycling,
   entry/exit, Back, Guide lifecycle, sliders, tabs, grids, lists, dialogs,
   paging, and scroll return behave predictably with stable semantic focus.
3. Compiled bindings and typed templates remain enabled. Remote/domain fixtures
   stay UI-framework-neutral; bound publication is explicitly scheduled to the
   Avalonia UI thread; exact snapshot item/action authority is preserved.
4. Standard Avalonia controls/UIA remain the only accessibility tree. Names,
   roles, values, states, bounds, ordering, disabled/busy semantics, and actions
   are truthful for every representative route.
5. One focused suite per work package is run once at its coherent boundary.
   The integration lead then runs one final bounded Release build and complete
   AVP MSTest.Sdk 4.3.2 suite, fixing only actual AVP-004 regressions; no product
   aggregate or exhaustive screenshot harness.
6. One exact-final-commit ordinary Windows measurement retains commit/runtime
   hash, dependency versions, startup, page-switch samples for all eight pages,
   transition samples, visible/hidden private memory and CPU, realized-container
   maxima, and honest unavailable GPU metrics. Visible private memory must stay
   below the user's 500-MiB ceiling; investigate a regression above 350 MiB
   before acceptance rather than hiding it behind the ceiling.
7. The planner launches the copied exact AVP-004 runtime and leaves it running
   for the user's return. Automated semantic/layout evidence is not described
   as a physical-display verdict; the user retains final visual, motion,
   controller, Guide, and clipping acceptance.

Out of scope: production migration, production widget/provider reuse, GBSS
adapter/removal, live accounts or credentials, live OS/media/network/game
mutation, package execution, WebView/video, publication, installer, NativeAOT,
cross-platform work, or AVP-005. Stop for a required production edit,
undocumented input/window API, external access, destructive state change,
substantial cross-lane conflict, or visible memory at/above 500 MiB with no
bounded owner.

## Widgets lane

Task: Implementation agent — widgets lane

Branch: codex/impl-widgets

### Current assignment — none; held during Avalonia evaluation

DLV-219 is accepted through main 01af13c. Every non-root Launcher Experience
projection now advertises the ordinary B-to-`back` shortcut while retaining its
existing Back/Cancel control, exact parent transition, and focus policy. The
focused Release Settings suite passed 60/60. The widgets lane must remain at
this clean boundary until DLV-215 is accepted and integrated; do not start
DLV-216 against the AppContainer-only runtime or current declarative renderer.

### Accepted dependency — DLV-212

DLV-212 is accepted through main 5a6ce0b. It proved that the current managed
Game Launcher source can build as an external public-SDK consumer and run
through the generic sandboxed worker without friend access. That portability
proof does not make the provider-based implementation the final Community
architecture. DLV-217 replaces its framework-owned domain authority.

### Widgets Ready queue

1. **Held for the Avalonia architecture decision — DLV-216: make Spotify an
   autonomous full-trust Community application.**

   Move Spotify OAuth, Web API, Web Playback host/protocol, token storage,
   response parsing, playlists, queue, devices, playback, and local backend
   ownership into the Spotify package. Its manifest uses only the generic
   full-trust entrypoint plus generic overlay contracts. It must not call
   external.spotify.* or depend on product-owned Spotify assemblies.

   Keep one source of domain behavior and move focused provider/playback tests
   with it. Credential-free fixtures prove setup, authorization state, Player,
   Queue, Playlists, Devices, errors, restart, responsive layout, packaging,
   and the ordinary full-trust host route. Live account, Premium, and EME
   behavior remain manual. Reconnection is acceptable. Do not build legacy
   host-token migration or delete old credentials. Do not remove core fallback
   code yet; DLV-218 owns deletion after cutover. No native renderer work,
   publication, capture gate, or secrets.

2. **Held for the Avalonia architecture decision — DLV-217: make Game Launcher
   the autonomous full-trust Community flagship and cut it over.**

   Move or export Windows/Xbox and opt-in store discovery, metadata/artwork
   clients, caches, organization persistence, source health, and supported
   exact launch behavior into the self-contained Community repository. It must
   not request system.apps.library.*, consume WindowsAppLibraryProvider, or rely
   on a private provider/bridge. IGDB, SteamGridDB, and future store adapters
   live in this package. Keep unsupported store launch behavior honest and do
   not reintroduce undocumented commands.

   Consume DLV-213 only for generic overlay presentation. Install, disclose
   full trust, enable/select, update, disable/remove/reinstall, and run through
   the ordinary package path. Settings must list Game Launcher as Community and
   Games & Apps as Built-in. One narrow reset of retired overlay-owned Launcher
   state is allowed if needed; never reset provider data, accounts,
   credentials, or user files.

   Prove large-library paging, Search, collections, Details/Back, restart,
   source failure, exact launch, and the visible no-special-case product path.
   Tier 1 affected package suites; Tier 2 ordinary packaged full-trust host;
   Tier 3 once at the trust-boundary cutover.

### Canceled widgets work

- DLV-214 is canceled. Its AppContainer/provider cutover would label Game
  Launcher Community while retaining framework-owned domain authority.
- DLV-207 is canceled. A trusted private picker bridge would prove the opposite
  of the Community requirement.
- Live IGDB/SteamGridDB verification remains credential-gated, but its
  implementation belongs to Game Launcher rather than a product host provider.

## Platform lane

Task: Implementation agent — platform lane

Branch: codex/impl-platform-community

### Current assignment — DLV-210: generic Hero Rail no-artwork layout

State: In progress from committed DLV-215 candidate ed39a70. Do not interrupt
the coherent visible DLV-210 milestone. DLV-215's production architecture is
accepted in review, but its commit is held from integration because its clean
exact-commit verifier stopped at the directly affected Widget Runtime suite
with 74/75 and `Sequence contains more than one element`; retained evidence is
`artifacts/verification/20260813T063409Z-37fcbe6e/verification-result.json`.
DLV-220 is the next Platform item and owns only that bounded verification
correction. DLV-216 and DLV-217 remain blocked until the corrected DLV-215
ancestry is accepted and integrated.

### Platform Ready queue

1. **Committed candidate; held for DLV-220 — DLV-215: add the generic full-trust
   Community application runtime.**

   Add one explicit versioned manifest/runtime entrypoint for an immutable
   package-owned executable. Installation/enabling must clearly disclose that
   it runs with ordinary current-user authority and is not AppContainer
   sandboxed. Never silently promote an existing sandboxed package.

   Start the exact validated package executable through a generic supervisor,
   authenticate a random per-session IPC endpoint and nonce, and reuse the
   ordinary lifecycle, snapshot, action, failure, restart, catalog, update,
   disable, removal, and native presentation pipeline. The application may use
   arbitrary user-level network, filesystem, registry, COM/WinRT, database,
   window, dependency, and child-process behavior. Do not add product quotas on
   its CPU, memory, sockets, files, databases, or process count. Keep strict
   limits on package ingestion and resources entering or owned by the product.

   Preserve the sandboxed runtime unchanged. Prove the full-trust path with two
   differently named packages. One external consumer must start a child
   process, perform deterministic local OS/file/database work, and exercise a
   fake HTTPS client without adding any domain contract to core assemblies.
   Cover tampered/missing entrypoint, nonce/PID mismatch, malformed/oversized/
   stale snapshots, crash/restart, disable/remove while active, package
   replacement, lifecycle drain, and explicit trust denial.

   Document the honest trust model and copyable author path. Tier 1 manifest/
   catalog/runtime/bridge/native suites; Tier 2 packaged full-trust lifecycle;
   Tier 3 once because this creates a public trust boundary. No Spotify, Game
   Launcher, provider, OAuth, API-specific DTO, arbitrary host-global mutation,
   capture, credentials, publication, or renderer duplication. Stop if the
   design requires identity recognition or claims containment it does not
   provide.

2. **Assigned as current work — DLV-210: repair the generic Hero Rail
   no-artwork layout.**

   The user's accepted live frame exposed a disconnected header, large empty
   hero region, title-width cards, clipped controls, and competing help when
   all six visible artwork handles were terminally unavailable. This is a
   product defect, not a capture inference.

   Keep title/source status, Search/collections, one bounded equal-width game
   rail, operation status, and one readable controller-help hierarchy inside
   the admitted body. Reclaim or intentionally compose the empty region. Do
   not fabricate artwork, hard-code games, reduce the catalog, or change
   SavedId/action/collection/focus authority.

   Prove the current 978x466 compact work area plus standard/wide and available/
   mixed/all-terminal artwork states through semantic geometry, focus/action/
   UIA agreement, and the ordinary packaged host. All rectangles and guidance
   must be contained; 32-game paging and exact focus remain intact. Inspect the
   post-route log. No screenshot gate, provider enrichment, external metadata
   credentials, public protocol expansion, managed Launcher root, tray/work-
   area owner, or unrelated animation refactor.

3. **Immediately after DLV-210 — DLV-220: correct DLV-215 exact-commit Runtime
   verification.**

   Preserve the reviewed generic full-trust architecture in ed39a70. Diagnose
   only the clean verifier failure in
   `WidgetProcessOwnershipScenarios.cs` where the cancellation-ignoring retired
   gesture-grant case observed more than one matching revocation. Correct the
   runtime race if production is wrong; otherwise make the smallest
   deterministic correction to an invalid single-match test assumption.

   Update DLV-215 evidence wording so focused and aggregate results cannot
   conflict. Run the focused Widget Runtime suite and the packaged full-trust
   lifecycle, then one clean exact-correction-commit Tier 3. Retain truthful
   output and provenance. No full-trust API redesign, Community-domain work,
   DLV-210 layout change, broad test migration, or unrelated flaky-test cleanup.

4. **Held until the Avalonia decision and accepted DLV-216/DLV-217 — DLV-218: remove retired
   Community-domain code from the product core.**

   Remove external.spotify.*, WidgetSdk/SpotifyService.cs, the PlatformBroker
   Spotify domain/contracts, WindowsSpotifyProvider, product-shipped Spotify
   playback host/protocol, and WidgetBridge Spotify construction after the
   autonomous Spotify package is live.

   Remove first-party Game Launcher catalog/runtime identity and every private
   widget-facing provider/selection path after its full-trust Community cutover.
   Retain the generic App Library provider only for bundled Games & Apps and
   sandboxed consumers that explicitly choose it.

   Add an architecture check rejecting Community package IDs and Spotify/Game
   Launcher domain types in production core directories. Prove both Community
   packages still install/run through generic contracts and bundled Games &
   Apps remains functional. Do not delete or migrate old credentials without
   separate user authorization.

5. **After DLV-218 — DLV-206: retain truthful performance provenance.**

   Correct rejected DLV-200 without expanding measurement scope. Retain one
   bounded sanitized committed artifact or summary containing root PID/start
   time, exact commit/executable SHA, scenario/process-profile IDs, child
   identities/roles, available metrics, and explicit unavailable metrics. Give
   the separate eight-widget timing run its own exact provenance. Anchor the
   complete composition search after the retained paint record and correct the
   false ordinary-host-live wording. Rerun only affected bounded measurement/
   temporal routes. No aggregate, speculative optimization, budget change,
   capture, or managed widget work.

## Serialized integration queue

1. Finish coherent DLV-210, then correct DLV-215 verification through DLV-220
   and integrate the accepted corrected ancestry. DLV-219 is already accepted
   on main; do not resolve any resulting product-code conflict in the planner.
2. DLV-216 and DLV-217 may start only from accepted and integrated DLV-215.
   Keep their domain ownership disjoint; integrate Spotify autonomy before Game
   Launcher cutover.
3. Only after DLV-216 and DLV-217 are accepted may DLV-218 delete retired core
   contracts.
4. DLV-210 is the active visible generic presentation correction and must not
   reintroduce Game Launcher identity recognition.
5. DLV-206 remains evidence-only and follows DLV-218.
6. Live metadata/artwork and account verification remain credential-gated, but
   adapter implementation belongs to the Community package.

## Blocked work

| Item | Blocker | Required evidence |
| --- | --- | --- |
| Trusted fixed-video/PiP surface | One paused WebView2 surface measured about 348.7 MiB private memory and 4% CPU against the 128-MiB gate. | User changes the budget or authorizes a content/process-specific experiment. |
| Audio default input/output selection | No documented supported Windows setter is established; undocumented PolicyConfig/registry/Shell mutation is forbidden. | Primary Microsoft API plus reversible provider/hardware plan. |
| Direct computer-control discovery | The owned no-taskbar OverlayHost is omitted from the control surface. | User accepts taskbar/Alt-Tab presence or the control tool gains tool-window discovery. |
| Native uninstall reconciliation | Synthetic catalog removal emitted no managed revision/native event. | Deterministic disabled/nonresident removal event. |
| Live Spotify Web Playback | Premium eligibility, allowlist, OAuth, EME, and account. | User-authorized account and retained manual evidence. |
| YouTube authenticated library | Google OAuth/account; Watch Later is not supported by the Data API. | Approved minimum-scope OAuth plan and user-authorized account. |
| Physical controller/display/audio/Bluetooth/game/Narrator matrix | Requires user hardware. | Retained named packaged/manual results. |
| Manual artwork/background override | No trusted opaque artwork/background selection seam exists. | Separately reviewed trusted selection/binding design. |

## Verification queue

1. On the next accepted visible Release, retest Launcher Experiences B/Back,
   Spotify layout, generic Hold Y
   restart, Game Launcher Hero Rail/no-artwork layout, switching borders/flicker,
   and Games & Apps layout; inspect the exact log interval.
2. Physical Audio Mixer LB/RB/X tray actions and reverse traversal.
3. Game Launcher shortcuts, top controls, last-row continuation, exact launch,
   Community classification, and full-trust disclosure.
4. Spotify seek/list traversal, pagination/reverse focus, transient failure,
   and live OAuth/Web Playback/device behavior when an account is authorized.
5. YT Music companion pairing/reconnection and physical controller behavior.
6. Narrator/MSAA, mixed-DPI/display, Bluetooth/audio hardware, game foreground
   input, widget-switch temporal continuity, and long-run resource baselines.

## Recent accepted milestones

| Assignment | Integrated main | Result |
| --- | --- | --- |
| DLV-219 | 01af13c | Launcher Experiences B/Back restored across list, version, missing-version, and removal confirmation; focused Settings 60/60. |
| DLV-213 | b73eaa5 | Generic protocol-v16 Community advanced presentation; exact exported Game Launcher and unrelated package use the same identity-independent host contract. |
| DLV-212 | 5a6ce0b | External public-SDK Game Launcher portability proof; final domain autonomy remains DLV-217. |
| DLV-209 | 56a09fb | Generic tray Hold Y restart and truthful guidance. |
| DLV-208 | 8e38df5 | Spotify 0.2.15 package and generic sandboxed snapshot proof; historical rollback is not a gate. |
| DLV-195–204 | 04fbdc0 | Full-application reference, offline external onboarding, compatibility report, and shortest author loop. |
| DLV-194/199 | 73117f7 / c4cf0af | Exact versioned SDK restored into a fresh external NuGet cache. |
| DLV-193 | 70a33e3 | Earlier descriptor-advertised Hold Y action proof, superseded by DLV-209 restart semantics. |
| DLV-172–190 | ae1dee8 | Launcher collection/lifecycle/focus/availability/exact-launch corrections and focused widget suites. |
| DLV-168 | 06dc8d0 | Stable host tray/shell accessibility across widget replacement. |
| DLV-180 | 0c321a3 | Native TextEntry cancel, exact focus restoration, and installed-host route. |
| DLV-169–171 | be69735 / 1829140 / d58e50e | Spotify focus, Games & Apps artwork, and Launcher continuation audits. |

When this active file exceeds 1,000 physical lines, first create one complete
timestamped snapshot, then compact it to no more than 500 practical lines while
preserving all live assignments, dependencies, blockers, acceptance criteria,
verification debt, and recent provenance.
