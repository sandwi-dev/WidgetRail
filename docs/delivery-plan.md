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
  narrow adapters; the XInput AVP is not yet a production GameInput decision.
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

### Current assignment — AVP-002: controller-first Avalonia interaction

State: Assigned on the AVP-001 implementation branch after the user's hands-on
verdict. Baseline: exact AVP-001 branch commit `0fd0d5b`. This is still an
isolated feasibility project under `experiments/AvaloniaOverlayPrototype`, not
a production migration.

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

Stop and report rather than working around the platform if controller access,
transparency, topmost/no-taskbar behavior, a stationary tray, UIA, or bounded
measurement requires production-host edits, privileged installation, external
credentials, or undocumented window manipulation. Do not start AVP-003
automatically; the planner and user test AVP-002 first.

### Avalonia Ready queue

1. **Held until corrected AVP-002 is accepted — AVP-003: production-stack
   architecture slice.**

   Refactor only the isolated experiment into one representative production-
   shaped vertical slice. Use CommunityToolkit.Mvvm with compiled bindings for
   typed immutable shell/page state and commands; keep focus and input in
   presentation services rather than view models. Use direct Microsoft DI for
   explicit singleton/scoped/transient ownership only if a measured comparison
   shows acceptable startup/memory cost versus manual composition. Keep the
   current custom visual language on Avalonia styles/control themes over the
   built-in Fluent control base; do not adopt FluentAvalonia or SukiUI unless a
   named missing control has a better measured result. Replace the prototype
   page transition with an Avalonia page/composition transition and compare
   UI-thread versus composition-thread timing. Replace the 16-item fixture with
   a virtualized, selectable 10,000-item list/grid and prove focus identity,
   recycling, scroll return, memory, and accessibility. Keep the semantic
   widget snapshot/action contract UI-framework-neutral and demonstrate one
   fake remote projection mapping into typed Avalonia view models without
   exposing Avalonia types to a Community application.

   Acceptance must include bounded Release build, focused MSTest.Sdk 4.3.2
   unit/headless/UIA evidence, compiled-binding build failures for an invalid
   fixture, dependency/startup/memory/process provenance, hidden-idle behavior,
   transition timing, virtualized realized-item counts, and a planner/user live
   launch. No production edits, GBSS adapter, Community package migration,
   ReactiveUI/DynamicData, database, web surface, NativeAOT, installer change,
   or AVP-004 work. Do not start automatically.

AVP-004 through AVP-006 remain planned architecture experiments and will be
assigned only after AVP-003 proves the bounded production-shaped stack.

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
