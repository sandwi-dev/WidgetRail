# Prototype roadmap

Status: active implementation sequence with evidence gates, 2026-08-07

The next goal is not “build all widgets.” It is to prove that the operating-system constraints permit the product experience without turning the overlay into injected or driver-backed bloatware.

## Phase 0: platform feasibility

### Spike A: Guide button

Build the smallest event-driven GameInput program that records Guide/Share and ordinary controls.

Test:

- Xbox Series and Elite controllers
- DualSense
- One generic controller
- USB and Bluetooth
- Xbox Game Bar enabled and disabled
- Steam open and closed
- Foreground game requesting exclusive input where possible

Exit criteria:

- A documented supported controller matrix
- Reliable press/release transitions without polling while hidden
- Conflict detection or clear onboarding for Game Bar and Steam
- A controller-only fallback chord decision

### Spike B: input containment

Open and focus a minimal overlay over input test applications using XInput, GameInput, Raw Input, SDL, Unity, and Unreal.

Exit criteria:

- Evidence of which backends stop receiving input and which continue
- No stuck buttons across open/close transitions
- A written support policy that does not imply universal suppression
- Explicit decision to continue without a filter driver, or stop and reassess the product promise

### Spike C: presentation compatibility

Render a transparent controller-navigable strip with D3D11, Direct2D, and DirectComposition.

Test DirectX 11, DirectX 12, Vulkan, and OpenGL applications in windowed, borderless, Fullscreen Optimizations, and true Fullscreen Exclusive modes. Include HDR/SDR, VRR, mixed-DPI multi-monitor, and a hybrid-GPU laptop when available.

Exit criteria:

- Windowed, borderless, and Fullscreen Optimizations are reliable
- True FSE limitation is detected or documented
- Overlay focus and restoration do not strand the user
- No continuous presentation while hidden

### Spike D: resource and UI stack

Implement the same three-card screen in native D2D/DWrite and, only if useful, a minimal WinUI 3 comparison.

Measure private working set, startup, warm activation, frame time, GPU activity, and idle wakeups with ETW/Windows Performance Recorder and PresentMon.

Current single-sample evidence for the visible prototype is 93.2 MB host +
59.1 MB bridge + 51.5 MB worker = **203.8 MB private memory**. Over five
seconds, `OverlayHost` accumulated **78.12 ms CPU**; bridge and worker deltas
were below timer resolution. This does not yet satisfy the repeatable ETW,
hidden-state, GPU, wakeup, or multi-widget evidence gate.

Exit criteria:

- Native host meets or credibly approaches the product budgets
- A measured renderer decision, including the cost of custom focus/accessibility work
- Automated benchmark scripts and stored baseline results

### Spike E: isolated worker

Launch a sample worker, negotiate a protocol over a secured named pipe, render its declarative tree, forward controller events, persist state, and recover from deliberate crash/hang/oversized-message cases.

Exit criteria:

- Worker failure never terminates or blocks the shell
- Lazy cold start is acceptable or hidden by a cached snapshot
- Job Object accounting and termination work (**initial memory, one-process,
  pre-launch assignment, kill-on-close, and UI restrictions implemented and
  tested**)
- Mandatory capability-free AppContainer launch plus exact-SID/Low-label/PID-
  bound main and broker communication is implemented and tested end to end
- The supported Windows/version/architecture matrix and profile cleanup policy
  are explicit before public distribution

## Phase 1: shell vertical slice

Deliver one controller-only executable with:

- Guide toggle
- Dashboard strip with three sample widgets
- Controller edit mode for reorder, visibility, and favorites
- Widget activation and strict internal input routing
- Last widget and stable focus restoration
- Safe mode, reset, diagnostics, and invalid-config recovery
- Minimal GBSS variables, semantic selectors, focus states, and live reload
- Performance overlay for the overlay itself

No capture, Discord, marketplace, web widgets, or general community code yet.

## Phase 2: first public SDK

- Versioned manifest schema
- C# `WidgetRunner` SDK
- Bounded length-prefixed strict JSON protocol over secured named pipes
- Core declarative layout/content/input primitives
- `gbar new`, `gbar dev`, validation, packaging, and input replay
- State API, the five-state host-authoritative lifecycle, crash recovery,
  explicit lifecycle-policy controls, and resource reporting
- Typed transport-neutral audio/network host services over authenticated,
  identity/declaration/consent/lifecycle-bound local broker transport
- One built-in widget and one out-of-process sample implementing equivalent behavior
- Controller-only and accessibility conformance tests

Developer mode is local and unsigned but remains isolated. Public distribution
remains off until the publisher-trust gates in Phase 4.

## Phase 3: local product hardening and useful first-party widgets

- Make every dashboard tile a real catalog-backed worker with deterministic
  local acceptance coverage; placeholders remain absent until complete
- Finish YT Music connection, pairing persistence, transition responsiveness,
  feedback, controller navigation, and failure recovery. YT Music is the first
  Community addon integration reference, not a permanent Built-in widget.
- YT Music migration implementation is present: it is absent from the bundled/
  trusted catalog and runtime copy, packages through public SDK/CLI commands,
  and runs through the generic Community AppContainer. Reusable exact-port
  loopback JSON plus write-only private secrets replace raw sockets and direct
  Credential Manager access. Package conformance proves install, consent,
  pairing, host-side Bearer injection, dashboard input, and no trusted fallback.
  `scripts/Test-YtMusicCommunityAddon.ps1` now supplies isolated executable
  evidence for lifecycle suspend/resume, deliberate crash recovery, fresh
  force reload, content-bound update consent, rollback, disable, and uninstall
  without touching the real catalog. A real YTMDesktop2 pairing plus packaged
  physical-controller/shell playtest remains before closing GBA-030.
  DLV-009 (`08d44db`, integrated as `304102a`) additionally removes three
  widget-owned lifecycle task fields and the hidden auto-connect flag: SDK
  Active operation lanes now own auto-connect, progress, polling, and latest
  transport reconciliation, while one immutable presentation record rejects
  cancellation-ignoring stale pairing/poll/transport outcomes. Focused YT Music
  coverage passes 51/51; real companion and physical evidence remain.
- Finish Settings controller reachability, diagnostics/recovery, local package
  and theme workflows, and permission/version consistency
- Finish the evidence matrix for the implemented packaged-regression fixes:
  transparent native client pixels outside content surfaces (GBA-036), a fresh
  Now Playing read/subscription generation on Retry (GBA-037), lazy Games
  Catalog loading plus intrinsic copy reflow (GBA-038), and measured-height
  long permission descriptions (GBA-039). Milestone `8e8c90a` supplies focused
  regressions and one packaged standard-viewport Now Playing capture; compact/
  wide, scaled, error/recovery, and controller traversal evidence remains.
  Milestone `9f1af0b` additionally supplies focused regressions for authored-
  width text reflow, visible controller lease fallback, Spotify public-config
  resolution/permission metadata, full-row vertical Games & Apps lists, and
  stable Now Playing transport visuals (GBA-040 through GBA-042). Packaged
  visual/controller evidence remains for those fixes as well. The auth-free
  `final-schema-v2-20260808-final` bundle adds retained package archives,
  AppContainer snapshots, production computed styles, 12 standalone widget-
  body WIC PNGs across four profiles, and digest-bearing traces for covered
  GBA-038/GBA-042 paths. It does not cover
  GBA-036/037/039/040/041, the remaining Games error/long/max-page states, live
  OAuth, or manual hardware/window behavior. Settings generic-worker startup
  is an explicit recorded gap rather than substituted evidence. The retained
  Spotify 0.1.6 archive covers the accepted setup/button presentation, but the
  auth-free bundle does not prove live callback or lifecycle behavior.
- Complete controller-first Audio Control and Network Control through their
  locally testable hardware, privacy, denial, churn, and recovery gates
- Run an explicit reversible Audio Control hardware gate on this machine:
  retain the original default multimedia endpoint, make one bounded master
  volume/mute change, verify callbacks and reconciliation, and restore the
  original scalar/mute in `finally`. This must remain opt-in and outside normal
  verification; output switching is not part of the v1 scope.
- Complete and package the reusable controller Slider: absolute quantized
  values, optimistic native feedback, bounded latest-wins coalescing, stable
  focus, and D-pad/analog horizontal adjustment. Controller scrolling and
  focus-follow list restoration are implemented; Slider remains in the current
  verification milestone.
- Add an original controller-first component library over the public SDK:
  icon buttons, cards, section headers, status badges, dividers, alerts, empty
  states, switches, tabs, and scoped dialogs. Components must keep stable IDs,
  minimum controller target sizes, readable non-color state, nested Back
  behavior, themeable semantic classes, and supported-DPI focus containment.
  The minimalist warm-graphite shared default and first-party retune are now
  implemented. `SettingsRow` and the bounded nested `ActionSheet` are now also
  public, with one stable action target, scroll/focus-follow, Disabled/Busy
  focus safety, and scope-owned B. The selection-specific Picker, controller
  Scrubber, responsive Row wrapping, non-focus-stealing Toast, and protocol-v7
  ActionSurface/MediaTile/AppTile are implemented; Games & Apps adopts the
  public tile and Toast APIs. Protocol-v8 ResponsiveGrid, independent per-edge
  GBSS borders, and semantic `UI.CodeText` monospace are now implemented;
  Settings uses Grid for root categories and CodeText for diagnostics. Preserve
  44-DIP targets, non-color state, stable IDs, and one inset focus cue. Optional
  packaged fonts are lower priority and security-sensitive; they require
  immutable asset brokering/licensing/bounds and must never become arbitrary
  font loading. Verify every component at supported text/interface scales
  instead of tuning only current widgets.
- Extend the implemented native opacity/scale/translation and shell/content transition slice.
  Bounded `translate-x`/`translate-y` now move true subtree presentation
  geometry shared by paint, clip, focus, hit testing, navigation, and Scroll
  focus-follow without changing layout. Interruption/retarget, reduced-motion
  cancellation, replacement isolation, and settled hidden-idle behavior have
  deterministic native coverage. Shell open/close now use bounded 140/100 ms
  opacity tracks and new/replaced widget content uses a 100 ms reveal; reversal,
  reduced motion, same-identity no-flash, focus snap, and one-shot hide have
  native coverage. The transient pressed-state map is connected to exact
  physical actions. DLV-020 (`b0c95ca`, integrated by `7cda335`) now keeps one
  previously admitted surface painted as visual-only content during a delayed
  destination start, transfers semantic/input authority immediately, and then
  resizes the existing HWND target from presented geometry over a bounded
  140 ms. Its focused Release run and 44 reviewed production-HWND frames cover
  delayed Spotify/Games admission, rapid reversal, and same-identity reload
  without the startup dialog, black clear, square edge, stale extent, or tray
  loss in that harness. A 2026-08-10 real-product user run invalidated product
  closure: Games & Apps resize is visibly laggy, flickers the surrounding
  interface, and exposes large gray/black regions. DLV-025 now measures the
  UI-thread resize/redraw/bridge cadence and must deliver an atomic smooth path
  or replace live extent animation with an immediate/composition-only switch.
  Do not add widget-specific animation.
  Do not import web-
  centric staggered entrances, ambient looping motion, editorial serif/faux-
  macOS defaults, or decorative animation into the controller shell.
- Integrate the implemented host-granted `HostServices.PrivateState` service
  into widgets that need durable preferences. Games & Apps now persists its
  curated SavedIds, selection, recent-first order, and bounded display-only
  projection through revision/CAS. Accepted DLV-017 renders the saved Library
  immediately after worker restart as disabled **Checking…** rows, then
  reconciles fresh trusted registrations in the background without persisting
  AppIds. Unsupported pre-release schemas reset atomically rather than retaining
  obsolete compatibility code. Launch authority remains short-lived and
  revalidated. Remaining product work
  includes per-widget clear-local-data UI, uninstall/retention policy review,
  storage/profile cleanup, and other widget-specific migrations.
- Continue the public authoring-coordination layer. `WidgetOperations`,
  `WidgetModel<TState>`, non-paged `WidgetResource<TValue>`, bounded
  offset-paged resources, `WidgetNavigator<TRoute>`, and `WidgetIds` are implemented;
  Spotify exercises the resource contract and Media Sessions now supplies the
  medium production model migration with one-invalidation/repeat-suppression
  coverage. Public SingleFlight/Latest/Serial optimistic commands are also
  implemented over those primitives, and Media Sessions uses SingleFlight for
  immediate Play/Pause projection plus bounded rollback. Games & Apps also uses
  runtime-owned Active operations for initial/retry library reads, with
  deterministic cancellation/drain coverage. SDK Gallery now demonstrates the
  public bounded route stack, route-lifetime cancellation, exact-scope B,
  remembered return focus, and validated hierarchical IDs without private host
  support. Protocol-v13 explicit focus persistence, `UI.NavigationShell`, and
  assembly-free bounded `gbar preview` scenario-manifest listing are now also
  implemented and exercised by SDK Gallery; selected scenario execution fails
  closed until an AppContainer preview worker exists. DLV-009 now supplies the
  second advanced lifecycle/state adoption proof: YT Music uses SDK-owned Active
  lanes and one immutable render-facing revision without a task/CTS registry.
  Its companion-specific confirmation policy stays authored, and its still-large
  controller/view owner remains application-composition evidence rather than a
  reason to create a universal base class. Accepted DLV-051 now proves a
  responsive widget can author and replay an exact seek-to-selected-navigation
  edge without native special cases. Widgets-led serialized DLV-006 is accepted
  through `9c7438f` as the distinct cursor/append collection and bounded lazy-
  artwork foundation. DLV-022, DLV-018, and their accepted DLV-053/DLV-054
  corrections are integrated through `8c1bbdf`: Spotify has bounded repeated-
  media occurrence keys plus the singleton graph correction, while trusted Games
  artwork no longer waits on provider I/O under the native request path and
  exact revalidation changes rotate stale pixels. Accepted DLV-055 `efffa53`
  publishes/installs/selects the changed Spotify source as `0.2.12`, and the
  coherent Release is visibly running. Accepted DLV-043 `6c619e9`, integrated
  through `d534410`, now establishes real Spotify route/action, playback, and
  snapshot-only presentation boundaries over one retained orchestration owner.
  DLV-057 `90cadf4` is accepted: the supported package command owns a clean
  isolated graph and warm main, repeated main, detached root, installed content,
  and the planner refresh are identical for selected Spotify `0.2.14`. Continue suitable
  command/resource/navigation migrations,
  focused provider-event/confirmation/coalescing recipes, and an analyzer for
  duplicate/unstable IDs and unhandled actions. The SDK must not infer domain
  merge, retry, or confirmation policy.
- Add local worker/provider recovery, crash quarantine, lifecycle enforcement,
  resource evidence, and disk/profile quotas/cleanup
- Performance widget only after its real local diagnostics data and acceptance
  suite exist
- Harden the implemented Now Playing reference: its initial current-state read
  is now independent from subscription failure, live-read failures preserve the
  last valid snapshot, and Retry creates a fresh bounded generation. Finish
  packaged failure/recovery, consent, lifecycle, and controller evidence, then
  consider broader media features without bypassing the typed GSMTC broker.
- Games & Apps replaces bundled Recent Apps with a locally testable Start Menu
  catalog and exact opaque-ID launch. Its durable Library lets users explicitly
  add/remove entries, orders confirmed launches at the front, resolves only
  saved entries on activation, and enumerates the broad Catalog only after
  **Add applications**. A generation-bound host effect closes the overlay only
  after the selected exact registration launches successfully; failure keeps it
  open with feedback. Bounded current-user AppsFolder/AUMID discovery, exact
  revalidated null-argument activation, and curated Shell icons are now
  implemented beside Start Menu shortcuts. User evidence still shows the Play
  fallback where trusted artwork is absent. DLV-018 plus accepted DLV-054 now
  add bounded lazy handles for trusted Start Menu and AppsFolder artwork without
  serializing base64 images into snapshots or holding provider I/O under native
  control-plane progress; exact trusted revalidation rotates the handle, decoded
  cache entry, and render bitmap. Steam remains a documented fallback. The
  coherent post-DLV-055 Release is now running for packaged live artwork checks.
  Launcher sources,
  authoritative game classification, history, search, source grouping,
  running-program capture, and file-picker additions remain roadmap work.
- Capture proof and widget if Windows API tests pass
- Expand the implemented Spotify Community addon beyond its controller-first
  player core. The trusted provider is composed by `WidgetBridge`, the local
  `gbar config` path stores the package-scoped Client ID, and the addon is
  packageable through the public SDK path. Next prove login/playback with an
  allowlisted Development Mode account. The exact callback is
  `http://127.0.0.1:43827/callback/`; there is no client-secret field. After an
  explicit Interactive Connect starts, the input action acknowledges
  immediately and only that in-flight authorization task continues on the
  widget's Created-to-Destroying lifetime while browser activation moves the
  widget through Visible/Background. No callback listener exists while idle,
  and new inactive controls remain denied. The listener window is fifteen minutes;
  the exact broker Connect deadline is seventeen minutes, leaving two bounded
  minutes for token exchange, retry/backoff, and credential-vault persistence.
  Revoke, Destroying, and cancellation still terminate it. Package 0.1.7 uses
  `keep-alive` so idle unload cannot destroy the one explicit in-flight OAuth
  task, accepts at most 16 bounded local connections inside the same
  listener window, and gives every setup entry a fresh Scroll identity. No live
  allowlisted-account success is claimed. Beyond the core player, add nested device/queue, search, recent,
  library, playlist,
  album, and artist surfaces from the pinned current OpenAPI subset. Add actual
  local audio by orchestrating the implemented separately trusted singleton Web
  Playback SDK/WebView2 host with incremental `streaming` scope, Premium
  eligibility, PID-safe ownership, and live EME/autoplay/resource evidence.
  Do not claim public distribution while the documented five-user Development
  Mode gate applies. See [Spotify Web API integration](spotify-integration.md).
  Accepted DLV-055 now runs the integrated DLV-022/053 Queue/Playlist traversal,
  bounded duplicate-occurrence identity, and non-self-linking singleton header
  graph from unique selected Spotify `0.2.12`. DLV-043's real private ownership
  boundaries are accepted and integrated. Accepted DLV-057 now supplies an
  isolated script-generated artifact graph and exact main-built `0.2.14`;
  immutable `0.2.11`/`0.2.12`/`0.2.13` remain rollback generations.
  Accepted DLV-023 (`3cfdd27`, integrated by `4dc1bd5`)
  now keeps transient provider/poll failures on the last-good player with
  bounded warning/backoff while preserving explicit fatal configuration,
  permission, and authentication states; live recurrence testing remains.
- Discord remains deferred until eligibility and production communications/RPC
  access are confirmed; do not build against undocumented client internals. See
  [Discord integration research](discord-integration.md).

Every first-party widget contributes a focused SDK example and regression suite.

### Immediate authentication-free work order

These items can advance on an isolated Windows machine while Spotify and
Discord external gates are unavailable. Order reflects current product impact,
not irreversible API priority:

1. **Restore the installed community addons used by the visible Release.**
   GBA-068/DLV-052 must make the built, validated, selected Spotify and YT Music
   payloads content-current and uniquely versioned, then compose lifecycle
   failure with snapshot admission so one worker-start failure cannot become a
   misleading hidden-cache error. Prove both addons through the generic
   AppContainer install/start/first-snapshot path without credentials, then
   rebuild and visibly relaunch the accepted product.
2. **Continue the visible regression evidence.** Exercise GBA-036 through GBA-042
   across compact/standard/wide viewports, 100–150% text/interface scale,
   reduced transparency, high contrast, long/error content, controller Back,
   first/last Scroll reachability, and foreground-activation denial. Confirm
   Spotify setup and permission copy without treating live OAuth as a local
   gate. `final-schema-v2-20260808-final` supplies deterministic retained-
   package/standalone-widget-body captures for the covered GBA-038/GBA-042
   paths; retain the recorded Settings activation gap and add the uncaptured/
   error/manual cases without broadening what that run proves.
3. **Finish the manual YT Music Community-addon proof.** The isolated auth-free
   workflow now covers clean pack/install/review/consent/enable,
   dashboard/open-widget input, lifecycle/crash/force-reload recovery,
   content-bound update/rollback, disable, and uninstall without a trusted
   worker or direct Credential Manager/socket workaround. Run real companion
   pairing and packaged physical-controller/shell verification next.
4. **Fill the public component gaps before more one-off UI.** `SettingsRow`,
   bounded nested `ActionSheet`, and the single-select `Picker` contracts are
   public. Settings now adopts Picker, Spotify adopts the public controller
   Scrubber, responsive Row wrapping, lifecycle-owned Toast, and protocol-v7
   ActionSurface/MediaTile/AppTile contracts are public. Games & Apps adopts
   AppTile and Toast. Protocol-v8 ResponsiveGrid, per-edge borders, and semantic
   CodeText are also implemented, with Settings as the first Grid/CodeText
   production adopter. The current full Release gate is green; next complete
   hands-on packaged visual/controller/accessibility testing on the relaunched
   overlay, then design advanced/virtualized collections
   and optional font assets without weakening controller or package safety.
4. **Prove motion without web-style overhead.** Subtree translation plus bounded
   140/100 ms shell open/close and 100 ms widget-identity reveal are implemented
   with reduced-motion and no-settled-idle native evidence. Capture packaged
   visual/frame-time evidence before adding later product-target transitions.
5. **Deepen Games & Apps through safe sources.** Start Menu and bounded
   AppsFolder/AUMID sources plus curated icons and a bounded Steam manifest/URI
   adapter are implemented. Add other reviewed launcher adapters,
   running-program capture, and a host-owned file picker.
   Keep classification evidence-backed and launch identities opaque/revalidated.
6. **Harden local providers and resource behavior.** The bounded schema-2
   process/native-counter harness now establishes independent Hidden, Visible,
   and Interactive Settings lifecycles without authentication or persisted
   user-state changes, records working set/private bytes/CPU/process counts,
   and exposes timer/paint/successful-EndDraw evidence with exact provenance.
   Next expand Audio/Network denial/churn/recovery tests, perform reversible
   Bluetooth pairing only with disposable hardware, add worker crash
   quarantine/profile cleanup, capture legacy-controller and hidden-performance
   evidence for GBA-044's new device-gated Guide compatibility path, and add
   ETW/PresentMon/private-working-set evidence.

### Near-term interaction and UI corrections

1. **Automatically curate known games in Games & Apps.** When a trusted catalog
   source classifies an entry as `Game`, reconcile it into the Library without
   requiring the user to add it manually. This applies to the first catalog
   reconciliation as well as newly discovered games. Persist an explicit
   exclusion when the user removes an auto-added game so refreshes do not add it
   back; ordinary `Application` and `Unknown` entries remain opt-in. The merge
   must be idempotent, preserve the user's explicit ordering and current focus,
   handle registrations that disappear or change identity, and explain newly
   added games without interrupting controller navigation.
   **Implemented in DLV-002 (`1738618`) and pending packaged visual/controller
   evidence:** schema-v2 state records automatic provenance and explicit
   exclusions; bounded multi-page reconciliation, disappearance/reappearance,
   replacement identity, authoritative reclassification, order/focus, failure,
   and lifecycle races have deterministic focused and real-package coverage.
2. **Repair the Games & Apps presentation.** Treat the Library, empty state, and
   Add applications catalog as one responsive product surface. No row, focus
   outline, label, or call-to-action may be clipped at compact, standard, wide,
   100-150% text/interface-scale, or long-name profiles. Normalize card height,
   icon/text alignment, section spacing, metadata hierarchy, safe-area padding,
   and scroll reachability; preserve stable focus while the catalog or curated
   Library changes. Close this item with semantic layout assertions plus
   packaged screenshots and controller traversal for empty, short, long,
   maximum-page, loading, and error states.
   **Accepted in DLV-004 (`7e0b83e`, integrated as `76032bb`) at automated and
   retained-capture level; physical packaged controller/display review remains.**
   **DLV-017 is accepted (`24a8944` plus `b844fd8`, integrated as `5aedfe8`):**
   restart warm start and non-authorizing reconciliation are implemented with
   focused SDK 84/84, worker 9/9, and Games 49/49 evidence. DLV-018 owns trusted
   lazy artwork. User testing subsequently exposed a remove-one/Back Library
   disappearance plus unstable Add applications visibility and low surface
   density; DLV-024 is the bounded managed-widget correction before the warm-
   start issue can close. These concerns must not be buried in presentation
   offsets or base64 row payloads.
3. **Select input and output devices from Audio Mixer.** Add controller-first
   pickers that show the current defaults and change the intended Windows audio
   endpoint and role with explicit pending, success, denial, disappearance, and
   rollback feedback. Revalidate opaque device identities at invocation, react
   to hot-plug/default changes without polling, and keep master/session controls
   usable when one picker is unavailable. Completion requires an actual switch
   through a supported documented Windows API, separate least-privilege control
   grants, and reversible hardware tests; opening Windows Settings, using an
   undocumented `PolicyConfig` interface, writing the registry, or automating a
   Shell surface does not satisfy the milestone.
4. **Correct shared button-content alignment.** Fix icon, label, checkmark, and
   busy-content centering in the native declarative renderer and shared
   component styles so ordinary Buttons, icon-and-label actions, selection
   rows, and first-party navigation tiles inherit the same geometry. Do not add
   per-widget pixel offsets. Verify optical and measured alignment with and
   without icons at every supported scale, including wrapped labels, and retain
   renderer assertions plus packaged Games & Apps, Spotify, and component-
   gallery captures.
   **DLV-003 (`27b0319`, integrated by `703c5bb`) implemented the narrow Button
   geometry slice. DLV-021 (`b714efe`, integrated by `bc2de86`) now gives
   Button, ActionSurface, and SectionHeader one DirectWrite measurement/paint
   model and exact five-product profile coverage. Packaged visual confirmation
   of the original Now Playing and Spotify reports remains.**
5. **Hold Y to refresh the selected widget from the icon tray.** Keep tap Y as
   the existing enter/exit-reorder command, but defer that tap decision long
   enough for a clearly hinted hold gesture. Crossing the bounded hold threshold
   refreshes the selected visible widget exactly once through the same host-
   owned reload path as F5, without entering reorder or forwarding Y to widget
   code. Release before the threshold performs the normal tap; focus changes,
   overlay close, controller loss, and cancellation clear pending progress.
   Show hold progress and a bounded success/failure result, retain F5 as the
   desktop fallback, and cover threshold boundaries, repeat suppression,
   reorder preservation, stale generations, and failed worker restart.
   **Implemented by DLV-005 (`3fc3770`, integrated by `aaf36d9`); physical
   controller threshold proof remains.**

### Flagship widget investigations and platform prerequisites

These are product families, not single widget tickets. They should start only
after the near-term interaction corrections above are coherent. Each one must
leave behind reusable platform capabilities and a smaller first-party widget,
not another application-sized class that privately owns discovery, caching,
windowing, input, and recovery.

#### Unified multi-store Game Launcher

[One Game Launcher](https://ogl.app/) demonstrates the target experience: one
controller-first library spanning Steam, Epic, EA/Origin, GOG, Ubisoft,
Battle.net, Xbox PC, and manually added entries, with compact/desktop modes and
RB/LB paging. Its public MYUI API exposes a normalized game list and a launch
operation rather than requiring each UI to understand every store. Playnite's
documented extension model independently separates
[library importers, metadata providers, and game actions](https://api.playnite.link/docs/tutorials/extensions/intro.html),
and its launch model accounts for multiple play actions, intermediary launchers,
and delayed process tracking. GOG likewise describes cross-store aggregation as
a mix of official and community integrations and notes that official adapters
require platform-holder support and policy agreements. The design implication
is that store breadth must be an adapter platform behind a normalized service,
not switch statements accumulated inside one widget.

The product should add a dedicated **Game Launcher** widget for the full-library
experience while retaining **Games & Apps** as the lightweight curated quick
launcher. Both must consume one trusted host-owned library model and the same
opaque launch authority. They must not maintain competing discovery caches,
store identities, artwork, or launch rules.

Initial scope is deliberately **installed local games that can be revalidated
and launched through documented local registrations**. Account-wide ownership,
uninstalled/cloud libraries, installs and updates, achievements, play-time sync,
remote launch, account switching, mouse macros, and emulator scripting are later
stages. Those features introduce third-party authentication, partner policy,
credential storage, arbitrary execution, or background tracking and must not be
smuggled into the first slice.

Framework prerequisites:

1. **One trusted adapter contract.** Add an internal `IGameLibrarySource`
   boundary with independently bounded discovery, exact resolve, launch,
   optional change observation, health, and source-version diagnostics. A
   failed or changed adapter must degrade only that source. Adapters return a
   normalized host record; they never publish raw paths, registry keys, command
   lines, AUMIDs, store tokens, account identifiers, PIDs, or HWNDs to widget
   code. Current Start Menu/AppsFolder and Steam implementations should prove
   this boundary before another store is added.
2. **A versioned game-library capability, not more fields on an unbounded app
   page.** The public model needs opaque short-lived launch IDs, durable
   authority-scoped saved IDs, sanitized source attribution, installed/
   unavailable state, explicit supported actions, optional running state, and
   an on-demand artwork reference. Results must be tied to a library revision
   and opaque cursor so insertions cannot corrupt offset paging. Same-title
   entries from different stores remain separate variants until the user or a
   reviewed canonical-ID source merges them; title heuristics must never choose
   launch authority.
3. **A virtualized controller collection.** Add reusable `VirtualizedGrid` and
   `VirtualizedList` presentation over a cursor/append resource contract with
   stable item/focus IDs, bounded prefetch, deterministic eviction, page-jump
   actions, focus restoration, and loading/partial-source/error rows. A library
   of thousands of games must not be serialized into one widget snapshot or
   represented by thousands of retained native nodes. This is the principal
   framework proof that makes the widget more complex than Spotify in product
   breadth without making its presentation code more fragile.
4. **Host-owned query and text entry.** Provide a controller-accessible search
   field/onscreen-keyboard contract plus typed sort, source, installed, favorite,
   and recent filters. Execute bounded queries against one immutable library
   revision; do not transfer the complete catalog to the worker for filtering.
   Filter changes, refreshes, and source failures must preserve a valid focus
   anchor or choose a deterministic nearby result.
5. **On-demand artwork and metadata.** Replace per-item base64 cover transfer
   with opaque image-resource handles resolved lazily at requested dimensions.
   The trusted cache must enforce origin/license metadata, dimensions, decoded-
   pixel, file, total-byte, concurrency, expiry, and disk-cleanup limits. Use
   store-provided local artwork first. Remote IGDB/store metadata is a separate
   consent, API-key, quota, attribution, and cache-policy milestone.
6. **Honest launch lifecycle.** Separate “the store accepted the request” from
   “the game is running.” Preserve the existing exact revalidation and host-
   owned overlay-close signal, then add sanitized states such as Pending,
   LauncherStarted, Running, Failed, and Ended only where an adapter can prove
   them without leaking process identity. Multiple play actions require a typed
   host-owned picker. Timeouts, cancellation, launcher updates, stale entries,
   already-running games, and games that spawn through another launcher need
   explicit behavior.
7. **Durable user state above replaceable source records.** Favorites, manual
   grouping, explicit duplicate merges, preferred launch variants, exclusions,
   and recent order must reference durable opaque identities and migrate across
   adapter revisions. A missing source or game is retained as unavailable long
   enough for recovery rather than silently deleting user organization.
8. **A separate trust tier for future store plugins.** First-party signed
   adapters may inspect narrowly documented local store state. A normal widget
   package must not gain filesystem/registry/credential authority to implement
   a store adapter. If external adapters are later supported, define a signed,
   separately permissioned provider-plugin process with per-adapter roots,
   network destinations, secrets, quotas, review, and revocation; do not load
   adapter DLLs into the shell or broker.

Staged rollout:

1. Normalize the existing Start Menu/AppsFolder and Steam sources behind the
   adapter contract, close their production terminal ownership, replace the
   old 512-item app-library snapshot capability with bounded opaque cursor
   queries, and render a 2,000/10,000-entry deterministic fake library through
   the virtualized grid.
2. Ship installed-only Steam plus one independently implemented local adapter
   such as Epic or GOG. Prove source isolation, refresh, stale launch refusal,
   artwork bounds, duplicate names, offline store clients, and controller-only
   search/filter/launch.
3. Add EA, Ubisoft, Battle.net, and Xbox PC only where stable local registration
   and documented launch behavior can be maintained. Every adapter receives an
   opt-in live-client compatibility fixture and can be disabled remotely or by
   version without breaking the rest of the library.
4. Consider account-owned/uninstalled libraries only after official APIs,
   platform terms, OAuth/credential handling, privacy deletion, rate limits,
   and partner requirements are documented. This is not a prerequisite for a
   strong installed-game launcher.

**Current delivery sequence:** DLV-059 `c7c354d`, corrected by DLV-071
`355a858`, is accepted and integrated through `6f4c642`; the trusted Start
Menu/AppsFolder and Steam sources now sit behind one normalized internal
contract with a real production terminal owner. Accepted DLV-072 `fe66470`,
integrated through `7f23738`, replaces the obsolete 512-item snapshot with
bounded opaque cursor queries and migrates Games & Apps. DLV-060 `8ca0859`,
DLV-066 `8a5ec7f`, and DLV-074 `d0a1014` are accepted and integrated through
`bb449d0`: the bundled installed-only Game Launcher now traverses 10,000-row
fixtures with bounded cursor/grid/artwork state, revalidates exact SavedIds,
retains non-authorizing warm display state, and provides durable favorites plus
explicit preferred variants. Accepted DLV-067 `ddf7626`, integrated through
`3d8f486`, adds only adapter-provable launch lifecycle, gates overlay close on
stronger-than-acknowledgement evidence, and retains at most 32 per-game results.
Assigned DLV-075 adds host-owned controller text entry and full-catalog query/
filter; DLV-076 and DLV-077 then add bounded recent ordering and explicit manual
inclusion from trusted registrations. Additional store adapters remain
behind the documented-registration admission gate; current official
[GOG client](https://docs.gog.com/gc-client-overview/) and
[SDK](https://docs.gog.com/sdk/) material describes game-side/client integration
but does not document a supported consumer-library enumeration/launch API, so
no speculative adapter is pre-authorized.

Completion evidence includes deterministic adapter contract suites; 2,000 and
10,000 item cold/refresh/search/scroll measurements; bounded memory, decoded
artwork, disk, CPU, and hidden-idle cost; compact/standard/wide and 100-150%
scale captures; rapid source churn and cancellation; keyboard, controller,
touch, and accessibility traversal; store-client installed/missing/updating
matrices; and exact proof that no widget snapshot, log, persisted state, or
failure message contains raw launch authority or account data.

#### YouTube video widget, picture-in-picture, placement, and pinning

This feature must use the official
[YouTube IFrame Player API](https://developers.google.com/youtube/iframe_api_reference)
inside a trusted WebView2 media process. It must not download streams, extract
audio/video URLs, suppress advertisements, obscure or replace standard player
features, or attempt to reproduce the player in Direct2D. YouTube requires an
identified embed/referrer, a player viewport of at least 200x200 pixels, and a
standard playback experience. Its policy also prohibits background playback
when the API-client window is closed or minimized. Therefore closing the main
overlay may leave playback running only when a pinned player surface remains
visibly restored on-screen; hiding, minimizing, unpinning-and-closing, or losing
that visible surface must pause or stop playback.

Picture-in-picture here means a **host-owned compact overlay window**, not the
browser's nested Picture-in-Picture API. Windows App SDK's
[`CompactOverlayPresenter`](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows)
provides an always-on-top picture-in-picture-like presenter, while the existing
shell already owns topmost Win32/DWM placement. Begin with a spike comparing a
plain host-owned tool window and `AppWindow` compact overlay for focus, taskbar,
click-through, DPI, monitor migration, protected media, borderless games, and
resource cost. Keep the public pin/presentation contract independent of the
chosen Windows backend.

DLV-011 completed the bounded comparison and selected a host-owned Win32 tool
window. Accepted DLV-058 (`e160690`, integrated through `ae34f9a`) now ships the
first generic declarative Pin/Unpin/click-through lifecycle with a closed-by-
default manifest opt-in, one host-owned surface coordinator, live snapshot
updates, generation-bound teardown, and no widget HWND or z-order authority.
Physical game click-through, final controller/UIA composition, protected media,
and physical mixed-display behavior remain later gates. Durable logical
placement is accepted through DLV-068.

**Current delivery sequence:** Accepted DLV-070 `c61a49d`, integrated through
`0b21384`, restores one authoritative OverlayHost across ordinary and `--show`
launches. Accepted DLV-068 `b83b3f7`, integrated through `9e795ac`, adds one
controller/pointer/UIA move-resize state machine with atomic monitor-safe
placement. DLV-069 candidate `ea2691c`, corrected by accepted DLV-073 `aaafefc`
and integrated through `eef3162`, adds input/focus/UIA and emergency-hide
composition while preserving current widget content in Click-through. Assigned
DLV-062 now runs the fixed-video trusted-media feasibility gate.
YouTube URL/video-ID v1 is not authorized until those remaining gates are
accepted.

Framework prerequisites:

1. **A real per-widget surface/session model.** Introduce a native
   `WidgetSurfaceCoordinator` that owns stable surface identity, HWND/AppWindow,
   monitor/work-area placement, z-order, focus, input mode, visibility, pin
   state, opacity, close, and teardown. The current single panel/backdrop
   orchestration cannot model an independently surviving player safely. Surface
   state must be bounded and generation-owned so a restarted or removed widget
   cannot control a stale pinned window.
2. **General pinning with conservative defaults.** Add manifest-declared
   `pinningSupported` (default false) and host-owned Pin, Unpin, Close, and
   click-through chrome. Follow the interaction lessons in Microsoft's
   [pinned-widget click-through guidance](https://learn.microsoft.com/en-us/xbox/game-bar/guide/click-through):
   when the overlay is closed, pinned surfaces stay
   topmost and visible but do not receive controller navigation; click-through
   mode must send pointer activity to the game and hide/disable controls that
   appear interactive. Reopening the overlay can make the selected pinned
   surface Interactive again. Cap simultaneous pinned surfaces and retain a
   global emergency hide/unpin action.
3. **Controller-first placement and persistence.** Add an explicit move/resize
   mode with clear focus isolation: D-pad/stick moves, a separate bounded resize
   gesture, A commits, and B cancels. Persist logical size plus normalized
   work-area anchor and monitor affinity, then clamp safely after DPI, work-area,
   orientation, topology, or monitor changes. Never restore an offscreen or
   below-minimum YouTube viewport. Pointer drag/resize and accessibility actions
   must invoke the same state machine.
4. **Presentation context separate from business lifecycle.** Publish bounded
   `WidgetPresentationState` such as Overlay, PinnedInteractive,
   PinnedClickThrough, Hidden, requested opacity, and size class without giving
   widgets raw HWND or z-order authority. Existing Created/Visible/Interactive/
   Background work lifetime remains authoritative: a pinned but non-interactive
   declarative widget is Visible, and only explicit active media receives a
   host-owned activity lease. Capability gesture authority must not persist
   merely because a widget is pinned.
5. **A trusted rich-media host, not a generic community WebView.** Reuse the
   lessons and process containment from `SpotifyPlaybackHost`, but define a
   narrow `MediaSurfaceSession` protocol for load/cue/play/pause/seek/volume,
   player state/errors, title, and surface ownership. Host WebView2 at standard
   user integrity in a dedicated Job/process boundary; allowlist top-level and
   frame navigation, popups, downloads, permissions, certificates, and external
   browser handoff; follow Microsoft's
   [secure WebView2 guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
   by disabling host objects and generic script/native proxies and validating
   every origin and typed message. Remote page content never receives
   widget broker or shell authority.
6. **One bounded WebView2 environment and explicit cost model.** WebView2 uses a
   [browser process plus renderer, GPU, audio, and helper processes](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-model).
   Start with
   one active rich-media surface, one managed user-data-folder lifecycle, and a
   shared environment where isolation policy permits. Close controllers on
   teardown, recover `ProcessFailed`, feature-detect Evergreen runtime APIs,
   clean private data on uninstall/reset, and publish measured process, working-
   set, private-byte, CPU, GPU, decode, network, startup, and hidden-idle costs.
   Do not create one browser process group per ordinary widget.
7. **Input and accessibility composition.** WebView2 can participate in the
   Win32 accessibility tree and can transfer focus through its controller APIs.
   Define deterministic controller entry/exit, browser accelerator suppression,
   B/Close semantics, captions/settings access, pointer capture, click-through,
   and UIA composition with host Pin/Move/Close chrome. The player must never
   trap focus or forward hidden-overlay controller input away from the game.
8. **YouTube compliance and configuration as versioned platform policy.** Use
   the documented embed `origin` plus required Referer/client identity, preserve
   controls, captions, metadata, links, ads, and playback-context signals, and
   handle autoplay-blocked plus embed errors 5/100/101/150/153 honestly. Keep a
   reviewed policy checklist and a kill switch because YouTube requirements can
   change independently from the application.

Staged rollout:

1. **Feasibility gate:** play one fixed embeddable public video in the trusted
   process; prove compliant referrer/origin, user-initiated audio, pause on
   hidden/minimized, crash recovery, standard controls/captions, and measured
   CPU/GPU/memory while a representative game runs. Fail the feature if EME,
   composition, anti-cheat, or frame-time cost is unacceptable.
2. **Generic pinning gate:** pin a declarative test widget before YouTube owns
   the path. Prove move/resize, click-through, opacity, overlay close/reopen,
   monitor/DPI/hot-plug clamping, focus restoration, emergency hide, restart,
   removal, and no game input theft.
3. **YouTube v1:** accept a validated YouTube URL/video ID, show recent items in
   declarative UI, open exactly one player surface, and support typed play/pause/
   seek/volume plus Pin/Unpin/Move/Close. No Google login or Data API is required
   for this slice.
4. **Discovery v2:** add controller search, thumbnails, and result paging through
   a trusted [YouTube Data API](https://developers.google.com/youtube/v3/getting-started)
   provider with an application API key, quota/rate
   handling, cache attribution, embeddable/region/error states, and privacy
   documentation. Search is quota-limited and must never poll while hidden.
5. **Authenticated library v3:** subscriptions, user playlists, and other
   account-specific state exposed by the supported API require
   [system-browser OAuth/PKCE](https://developers.google.com/youtube/v3/guides/auth/installed-apps),
   the minimum
   `youtube.readonly` scope, vault storage, revoke/delete UX, Google verification,
   and live account evidence. This authentication-gated stage must not block
   pinning or URL playback. Do not promise Watch Later: the YouTube Data API
   explicitly rejects attempts to retrieve its items with
   `watchLaterNotAccessible`.

Completion requires a retained YouTube-policy checklist; fixed-video and
non-embeddable/removed/region/error fixtures; offline/slow/crashed WebView2
recovery; autoplay and audio-device changes; standard/compact/PiP visual and UIA
evidence; pointer/controller/click-through testing; overlay close while pinned
and unpinned; hidden/minimized pause proof; multi-monitor DPI/hot-plug and
foreground restoration; DX11/DX12/Vulkan/OpenGL borderless compatibility; and
long-running CPU/GPU/memory/network measurements. True Fullscreen Exclusive,
secure desktop, elevated games, and universal anti-cheat compatibility remain
outside the current platform guarantee.

Spotify's provider/configuration tests can proceed offline; its live login,
playback, and Community-addon evidence require a registered Development Mode
app and allowlisted test account. Discord remains fully gated.

### First-party system-control reference widgets

**Audio Control** and **Network Control** are explicit first-party widget
roadmap items. They occupy the same useful system-utility category as the Xbox
Game Bar audio and network surfaces, but their interaction design is
controller-first and belongs to this platform: dashboard summaries, focused
open panels, predictable D-pad/analog navigation, widget-owned shortcuts, and
clear busy/error/permission feedback. They are not privileged shell panels.

The repository has bounded Audio Mixer and Network Controls reference slices
that validate parts of the SDK, broker, provider, packaging, and controller
design. Those slices are evidence for the roadmap, not a claim that either
full product widget below is implemented, shipped, or production-ready.

The generic declarative path, controller Settings/global-theme foundation, and
typed broker/consent path support both integrations. They remain behind their
hardware/privacy/performance evidence gates and must stay ordinary first-party
packages built on the public SDK—not special panels hard-coded into
`OverlayHost`. Any primitive or broker API they need becomes documented,
testable platform surface that community widgets can request under the same
permission policy.

**Audio Control roadmap** starts with a deliberately narrow Audio Mixer slice:

- enumerate sanitized per-application audio sessions on the current default
  multimedia render endpoint;
- observe session/default-endpoint changes through Core Audio callbacks;
- set volume or mute for one opaque session while the widget is Interactive;
- expose a compact dashboard summary and a controller-first session list where
  focus, adjustment, mute, and error feedback remain unambiguous; and
- expose only the exact master-output actions authorized for dashboard use:
  LB/RB apply a fixed, clamped volume step and X toggles mute, with immediate
  feedback, coalescing, and provider reconciliation; and
- publish bounded, coalesced session-change events without a timer polling
  loop.

Later Audio Control phases add, in evidence-gated increments:

- controller-first default output- and input-device selection, with explicit
  Windows role scope, where supported documented Windows setters are available;
- broader device, communications, and application-churn coverage for the
  implemented master and per-session volume/mute surface;
- broader capture-device/role visibility and controlled selection; and
- live device/session/default-role updates without a background polling loop.

Audio phase dependencies are: declarative list/slider/toggle states and stable
controller focus; typed read/control grants and Settings consent; an
event-driven Core Audio provider; then hardware/churn/performance evidence.
Output switching and any audio-sample capture additionally require supported
Windows APIs, separate permissions, privacy review, and unmistakable feedback.

Endpoint master volume/mute is implemented on the current default multimedia
render endpoint. Sanitized default output/input names and current default-
microphone volume/mute are also implemented behind independent read/control
grants. Output-device selection and default communications-role changes are not
implemented. Input control does not grant microphone audio capture. Each
remaining item requires a separate capability, privacy/feedback design, and provider/API review. No
undocumented `PolicyConfig`, registry write, or shell-automation output switch
is acceptable.

Version-1 broker control grants remain Interactive-only except for the accepted
DLV-019 exact current-master dashboard path. That path grants only one declared,
snapshot-bound set-volume or set-mute operation for the selected Audio Mixer
card while Visible; it does not promote broad Audio Control authority or expose
session, input, device-selection, or arbitrary-value control. Saved-network
switching and other general controls remain prohibited without their own
reviewed authority.

The host broker owns OS handles, COM lifetime, device/session observation,
permission policy, and sanitized identity. The widget receives bounded semantic
models/events and invokes narrow commands; it never receives a raw endpoint,
session, or microphone handle. It must demonstrate device/session arrival and
removal, default-device changes, application churn, communication-device
policy, and recovery without keeping Visible/Interactive polling alive in
`Background`.

The documented `IMMDeviceEnumerator` surface reads the default endpoint but
does not provide a system-default setter. Do not ship an undocumented
`PolicyConfig` interface, registry write, or shell-automation workaround; if no
supported API passes the spike, default-device switching leaves the initial
scope.

Audio Control must treat optional providers independently. A missing, denied,
revoked, or temporarily unavailable microphone/device-name capability cannot
blank or trap the working master-output and application-session mixer. Each
section needs its own loading/empty/denied/error state, focus-preserving
reconciliation, and provider-churn tests.

**Games & Apps roadmap** has replaced Recent Apps in the bundled product
catalog. The current public-SDK package presents a durable curated Library
and a nested vertically scrolling Start Menu Catalog, then launches one selected
broker-issued opaque ID only while Interactive. A toggles Catalog membership,
X removes from the Library, and confirmed launches move to the persisted front.
Read and launch have separate
manifest declarations and consent. The trusted provider keeps paths, shortcut
targets, arguments, AUMIDs, package identities, PIDs, and HWNDs private; every
launch re-enumerates and requires one exact unchanged shortcut before invoking
the Windows Shell without arguments or elevation.

This slice scans executable `.lnk` registrations in the current-user/all-user
Start Menu Programs folders plus bounded current-user AppsFolder/AUMID
registrations on a trusted STA lane. Curated entries can
carry bounded host-rasterized Shell icons while broad discovery stays text-only,
and every real entry is deliberately reported as Application rather than
guessing games from filenames or paths. The host now
issues an authority-scoped SavedId, stores curation/recent-first order through
package-private compare-and-swap state, and resolves it to a fresh provider-
lifetime launch token. The full Catalog now loads only when requested. Next
auto-curate entries already classified as `Game`, with a persisted user
exclusion so removal remains durable, before adding reviewed launcher-specific
adapters, richer source artwork, and further evidence-backed game
classification. Search,
grouping, history, source attribution, running-
program capture, file-picker additions, and refresh observation must remain
bounded and privacy reviewed. None may become arbitrary path/process launch
authority. Recent Apps remains a separate read-only activity API/test reference,
not a bundled dashboard widget.

The planned full **Game Launcher** is a second presentation over this same
trusted catalog and opaque launch authority, not a fork of the provider. Games
& Apps remains the small curated/recent quick surface; Game Launcher owns the
virtualized all-games grid, source/filter/search views, artwork, and variant
selection. Favorites, exclusions, preferred variants, and duplicate decisions
must live in one versioned user-library model so the two widgets cannot disagree
about what a saved game means.

On a successful launch result, the shell closes the overlay through the
implemented host-owned, correlation-safe completion signal. The widget does
not receive
generic window-management authority, synthesize Guide, or confuse enqueueing
with provider success. Failure, stale ID, or cancellation keeps Games & Apps
visible and focused with a bounded retry state.

**Network Control roadmap** follows the Audio Control foundation. Its first
slice uses Windows WLAN/network change notifications rather than continuously
polling adapters and targets:

- Ethernet and Wi-Fi connection state;
- coarse active transport and Wi-Fi adapter/service/radio availability;
- current coarse-status Wi-Fi identity and signal remain omitted when Windows
  precise-location access is unavailable; the separate explicit scan flow
  presents sanitized current results and clear required/denied states;
- controller selection among current available scan results, with connection
  limited to saved-profile-backed or unsaved open results;
- a compact dashboard summary plus a focused open panel with explicit
  Connecting, Connected, Failed, permission, and unavailable states; and
- the dashboard card is read-only and declares no profile-selection or connect
  quick actions; no capability-backed network control runs while merely
  Visible. Any future dashboard connect control needs
  separate host-mediated authority and must not silently disclose credentials
  or connect to an unreviewed network.

Password entry, editing/creating protected Wi-Fi profiles, captive-portal
interaction, and exposing stored network keys are explicitly outside the
current scope. The broker owns WLAN/network handles and returns sanitized
state/events. Connection uses a current saved-profile-backed or open scan
result, requires clear focus/feedback, and must
handle adapter removal, airplane/radio state, connection failure, and Ethernet
priority without trapping controller focus.

IP Helper notifications drive aggregate/Ethernet changes. Native Wi-Fi uses a
long-lived WLAN client and asynchronous ACM connection notifications; it does
not scan continuously or register MSM. Version 1 does not automatically query
SSID/current-connection/signal details that Windows treats as
location-sensitive; it reports privacy-restricted state instead. Any future
explicit access request must degrade to required/denied/revoked states, never
trigger a retry loop, and never expose BSSID/profile XML/key material.
See [Windows provider architecture](windows-provider-architecture.md) for the
documented API facts, threading/lifetime rules, privacy boundary, and simulator
matrix, and the [Network Controls reference](network-controls.md) for the
author-facing contract and completion evidence.

The current Network Control milestone includes controller-visible **currently
available Wi-Fi networks**. The closed broker/SDK contracts, trusted provider,
first-party UI, bundled capability declaration/consent, and dedicated tests for
explicit user-initiated `WlanScan`, asynchronous completion/timeout, a bounded
`WlanGetAvailableNetworkList` snapshot, and current saved/open result connection
are implemented. Separate software-radio read/control grants are also
implemented with authoritative reconciliation and explicit multi-PHY partial
failure. Windows gates
both scan/list APIs behind
precise-location consent on current releases, so the flow must begin from a
controller action, explain the OS prompt, and render required, denied, and
revoked states without retrying. The resulting rows use generation-bound opaque
scan IDs that expire on the next scan/provider generation; widgets never receive
BSSID, interface identity, raw WLAN structures, profile XML, or keys.

Connection support currently accepts saved-profile-backed and unsaved open
results. The next step is a host-owned credential prompt for new WPA/WPA2/WPA3
Personal networks. Credentials never enter the widget snapshot,
worker process, widget-owned storage, diagnostics, or logs. Enterprise/802.1X,
certificate, SIM, domain-credential, hidden-network, and captive-portal setup is
unsupported initially. `WlanConnect` remains asynchronous and authoritative
ACM events determine success/failure. `WlanSetInterface` with
`wlan_intf_opcode_radio_state` may control only the software radio state; a
hardware switch, policy, or airplane-mode restriction remains authoritative.

Network Controls now includes a first Bluetooth slice behind separate closed
read, radio-control, pair, and manage grants. The trusted WinRT provider reports
sanitized, bounded paired/present/connected device state through event-driven
discovery, controls only the software radio, and pairs one current opaque
association endpoint through `DeviceInformationPairing.PairAsync`. Unsupported
ceremonies and paired-device management open Windows Bluetooth Settings through
the separate manage grant. The provider refreshes authoritative state after
every result and never equates pairing with profile connectivity. Unpair remains
future host-owned work and physical pairing remains unverified. No generic Bluetooth
device Connect/Disconnect command is
promised: public Windows communication APIs are profile-specific (for example
GATT services/characteristics and RFCOMM sockets), so each future functional
connection needs its own reviewed profile contract and capability.

The Wi-Fi/Bluetooth tabs also need explicit selection semantics. A focused Wi-
Fi row is only a candidate; it must not look Connected until authoritative WLAN
state confirms it. A focused Bluetooth row is informational unless the current
row explicitly offers Pair or Windows-managed details. Focus, presence, paired state, and
connected state need distinct non-color cues. Tab switches must preserve each
tab's last focus/scroll position, and refresh churn must select a stable nearest
survivor without jumping to another action.

Later Network Control phases add:

- a host-owned WPA Personal credential flow after the implemented explicit,
  privacy-gated available-network scans and saved/open connections; enterprise
  authentication remains unsupported initially;
- Bluetooth host-owned unpair and reviewed profile-specific operations after
  the implemented radio/discovery/pair/Settings-management slice;
- sanitized active-adapter state and Ethernet/Wi-Fi identity;
- SSID and signal/link quality only through an explicit Windows privacy-access
  flow with required/denied/revoked states;
- bounded IP address, gateway, and DNS summaries that never expose credentials
  or raw provider handles;
- throughput, latency, and packet-loss diagnostics with explicit sampling
  ownership, frequency bounds, cancellation, and visible resource cost; and
- safe reconnect/renew/diagnostic actions only after capability and failure-
  recovery review.

Network phase dependencies through scan, saved/open connection, and Bluetooth
association pairing are now
implemented: bounded models and controller focus, separate closed grants,
event-driven IP Helper/Native Wi-Fi, explicit scan, generation-bound IDs, and
precise-location denial states. Remaining dependencies are the host-owned
credential prompt, Bluetooth unpair/profile operations, hardware/privacy matrices, and
measured diagnostic sampling. Identity/address details
and recovery commands do not enter the public contract before those reviews.

The user-visible first-party reference includes explicit scans and unsaved open
networks, software Wi-Fi/Bluetooth radio controls, association pairing, and a
Windows-owned Bluetooth management fallback, but still excludes password
entry, protected profile creation/editing, unpair, and generic connection. The staged credential flow
must remain host-owned and WPA Personal-only at first; it never exposes stored
keys. Captive-portal automation, enterprise/802.1X provisioning, arbitrary
adapter configuration, and privileged troubleshooting scripts remain outside
the initial expanded scope. Diagnostic sampling must stop outside its declared
lifecycle state and must be measured against the overlay's CPU, network,
wakeup, and memory budgets.

Exit criteria for both widgets:

- the worker uses only published SDK and declared brokered capabilities;
- the same package runs out of process through the generic catalog/bridge path;
- dashboard quick actions and the open surface follow standard input scopes;
- hidden/background CPU and wakeups are measured with no presentation polling;
- capability denial, service/device loss, worker restart, and stale-event races
  have deterministic controller-readable states; and
- source, contract documentation, simulator fixtures, and regression tests are
  suitable as production SDK examples.

Before either package is labeled production-ready, complete controller polish,
physical hardware/device/router matrices, privacy and denial UX, long-running
churn/recovery tests, and hidden/background resource measurements. Every new
primitive or capability must remain reusable by community widgets; these two
packages are the canonical templates for event-driven system-control widgets.

## Phase 4: ecosystem

- Aggregate installed-catalog admission is implemented with host-owned ID,
  version, file, byte, and discovery-time ceilings plus pre-publication refusal.
  Add controller-first cleanup/remediation and retain a maximum-catalog cold/
  reload memory-and-time baseline before ecosystem release.
- Verified package launch leasing is implemented: every lazy start/restart
  reacquires the exact digest and complete path/length/hash inventory, pins the
  verified files for the session, and replaces recursive package-root ACLs with
  exact non-inheriting AppContainer grants. A 512-file Release fixture records
  325.283 ms lease acquisition and 360.426 ms exact-grant activation on the
  current local machine; retain repeated clean/hosted measurements and packaged
  abuse evidence before public community distribution. Content generations use
  distinct AppContainer identities so later versions cannot inherit stale root
  grants. The exact 1,024-directory public pack/install/enable/launch edge now
  records 376.140 ms packing and 2,528.883 ms through first validated render in
  clean retained selected run `20260809T155221Z-ae6e5d8d`, while 1,025
  directories fail before output/publication. The run covers CLI 50/50,
  Documentation 1/1, and First-Party Conformance 6/6 for documentation commit
  `b2956ab` over implementation `4f903b0`; it is not a complete all-manifest
  run. The managed bridge now bounds correlated work to 16 requests, preserves
  same-widget receive order, lets a pipelined listing and Stop bypass a
  cooperative stalled admission, rejects duplicate active IDs, and returns
  `bridge_busy` on saturation. Clean retained selected run
  `20260809T161934Z-5d0bee6a` passes Bridge 45/45, Documentation 1/1, and First-
  Party Conformance 6/6 for commit `fdcf253`. The shipping native client cannot
  yet issue that pipelined recovery work while its synchronous read blocks;
  cancellation-ignoring ACL work, bounded drain, native off-UI-thread I/O, one
  aggregate start deadline, and the alternate-group-ACE policy remain open.
- Publisher signing/revocation and a signed update channel, mandatory before
  public community distribution but intentionally sequenced after local
  product-completion gates
- Curated catalog and controller-first install/update/rollback
- Publisher identity and moderation process
- Compatibility and resource labels
- WinGet or Microsoft Store discovery where useful
- Theme gallery
- Public API stability policy and migration tooling
- Optional WASM logic tier evaluation
- A shared WebView2 tier is now demanded by YouTube playback, but it begins as
  one trusted, typed, resource-labeled media-surface process. Do not expose a
  generic community WebView/native-window tier until a second compliant use
  case proves the same bounded lifecycle and authority model.

## Risk register

| Risk | Severity | Evidence needed | Current response |
| --- | --- | --- | --- |
| Games receive controller input behind overlay | Critical | Backend test matrix | Phase 0 stop/go gate; no driver by default |
| Guide conflict or unavailable system button | Critical | Controller/client matrix | GameInput callback, conflict onboarding, controller-only fallback |
| Overlay not visible in true FSE | High | Presentation matrix | Do not support true FSE initially; no injection |
| Native UI scope expands uncontrollably | High | Three-card implementation effort and accessibility audit | Small primitive set; renderer-independent widget protocol; compare WinUI only with data |
| Community widget compromises user | Critical | AppContainer/broker abuse tests plus verified launch, signing, quota, and audit evidence | Unsigned local development remains AppContainer-isolated and explicitly labeled; session launch now pins the exact verified executable/dependency/style/asset inventory and denies late-file authority, while signing/revocation and maximum-scale evidence remain mandatory before public community distribution |
| Worker model feels slow or heavy | High | Cold-start and working-set measurements | Lazy first launch, resident-Background measurement, explicit user lifecycle choices, resource labels |
| GBSS updates break themes | Medium | Theme compatibility fixtures | Stable semantic selectors, typed allowlist, versioned tokens |
| Discord rejects overlay use case | High for social only | Written eligibility/production access | Keep Discord as optional first-party integration, not a core dependency |
| Store adapter drift launches the wrong game or breaks the whole library | High | Per-adapter exact-registration fixtures, opt-in live-client matrix, stale-launch refusal, and independent health/disable tests | Trusted adapter boundary, opaque launch authority, source isolation, installed-only first slice, no title-based launch merging |
| Pinned rich media steals game input or remains active invisibly | Critical | Click-through/controller/focus matrix, hidden/minimized pause proof, emergency hide, foreground restoration, and representative games | Host-owned pin/surface coordinator; pinned playback is allowed only while a visible restored surface exists; hidden/minimized closes or pauses media |
| WebView2/YouTube adds unacceptable game-time cost or policy exposure | High | Official-policy checklist, WebView process/GPU/network measurements, runtime-update tests, origin/navigation abuse tests, and compliance kill switch | One trusted typed media process, one active surface initially, no generic widget browser, official IFrame player only, no downloads/background play |
| Anti-cheat reacts to overlay | High | Representative signed/unsigned game tests | No injection, no game-memory access, compatibility matrix |
| Feature creep recreates bloatware | High | Continuous resource regression tests | Budgets in CI; every background capability justified |

## Original Phase 0 implementation order

1. Create the native solution and diagnostics harness.
2. Complete Guide-button and input-containment spikes before polishing UI.
3. Complete the presentation matrix.
4. Measure native rendering against the budgets.
5. Prove one crashing out-of-process declarative widget.
6. Review the evidence and accept or revise the proposed architecture.

The native foundation, isolated worker path, controller package review/live
catalog reload, and responsive per-monitor geometry seams are now implemented.
The current product order is:

1. make every dashboard tile honest and locally complete: YT Music, Settings,
   Audio Control, and Network Control; remove placeholders until their
   acceptance suites pass;
2. complete controller reachability/auto-scroll and the physical
   resolution/DPI/accessibility visual matrix for every first-party surface;
3. add controller-visible diagnostics, worker/provider recovery, crash
   quarantine, local performance/resource evidence, and lifecycle-policy
   enforcement;
4. complete locally testable Audio/Network hardware, churn, privacy, and denial
   paths; use the accepted DLV-016 local performance baseline for named feature
   deltas, then continue Games & Apps catalog depth and capture feasibility;
5. prove the reusable foundations for the two flagship widgets before filling
   out their integrations: trusted game-source adapters plus a virtualized fake
   2,000/10,000-game library, then generic pinned-surface/click-through/placement
   behavior and one fixed-video trusted WebView2 feasibility gate;
6. harden local developer mode and public references: exact-generation
   `gbar dev` readiness, real first-party community-package conformance,
   graphical/native theme preview, known-local package/theme import,
   remove/rollback, and clean-profile end-to-end samples;
7. finish controller/game/presentation/anti-cheat matrices plus CPU and
   disk/profile quotas/cleanup; and
8. only then implement publisher signing/revocation, signed update metadata,
   moderation/gallery policy, and public distribution. Signing remains a hard
   pre-public gate, not a blocker for isolated unsigned local development.

Do not use a first-party widget to justify a private host API that external
widgets cannot exercise. Discord/social work remains deferred until eligibility
and production authentication/communications access exist.

Spotify is the exception to the general third-party-auth deferral because the
product explicitly approved it as the next authenticated Community-addon proof.
Its provider must expose reusable public SDK/broker contracts; the addon does
not receive ambient Internet, tokens, a trusted-worker exception, or an
undocumented desktop-client shortcut. Development Mode remains a local/tester
milestone rather than a public-distribution claim. The optional Web Playback SDK
runs only in a distinct trusted singleton
host and does not create a general web capability for Community packages.

The first irreversible ecosystem decisions—public API 1.0, package signing
rules, marketplace policy, and optional web/WASM tiers—wait until these local
product steps have evidence.
