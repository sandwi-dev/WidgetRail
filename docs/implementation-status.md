# Implementation status

Status: integrated Phase 0 platform prototype, 2026-08-07

This repository contains working native and managed components. It is not yet
a production overlay, signed public-distribution trust boundary, end-user
installer, or marketplace.

## Implemented

### Native overlay and input

`src/OverlayHost` starts hidden, uses a GameInput Guide callback, stops ordinary
polling/rendering while hidden, and presents a Win32/Direct2D controller
dashboard. It supports reorder mode, last-widget/order persistence, focus
routing for the reference widget, managed bridge startup, bounded HTTPS
artwork, fixed native semantic icon geometry, responsive logical viewports,
and per-monitor placement.

The visible shell uses separate panel and dimming-backdrop windows on the active
external foreground app's nearest monitor. An outside backdrop click closes the
overlay. Both windows are topmost only while visible, with foreground/z-order
observation and best-effort reassertion. While visible, Alt+Tab/external
foreground changes retarget the panel and full-monitor backdrop. DPI, display
topology, work-area, and client-size messages recompute placement/resources;
reentrant DPI placement is coalesced. This is normal DWM windowing, not game
injection.

Guide/Home shows or hides from any depth. Dashboard D-pad/left-stick movement,
A, B, and Y are host-owned; dashboard B closes the overlay. Open widgets receive
their other semantic actions; B falls back to the dashboard only when unhandled
in the root input scope. Nested scopes never bubble to the root.
GameInput is the primary Guide source. A removable XInput compatibility adapter
uses an undocumented ordinal only for drivers observed to omit Guide callbacks;
it is not a universal device-compatibility guarantee.

Open-widget left-stick navigation is two-dimensional with engage/release
hysteresis and bounded repeat. Focus uses explicit neighbors first and
deterministic rendered geometry as a fallback, without wraparound. Disabled
and Busy Buttons/Sliders retain focus but suppress action dispatch. Focused
Sliders consume horizontal input for bounded value adjustment and retain
Up/Down navigation.

### Widget platform

- `WidgetProtocol`: strict version-1 manifests and additive snapshot protocols
  v1–v3, deterministic JSON, stable IDs, focus validation, quick actions,
  Scroll/surface hints, absolute-value Sliders, images, closed semantic glyphs,
  explicit active controller scopes and snapshot correlation, and interaction
  state.
- `WidgetSdk`: typed Stack, Row, Text, Button, Progress, Slider, Scroll, Spacer,
  Image, and Icon authoring; controller-ready ToggleButton/Stepper composites;
  button glyphs; focus/shortcut/state helpers; scoped shortcut routing;
  bounded latest-wins Slider coalescing; invalidation;
  five-state lifecycle hooks/tokens; bounded, non-overlapping
  Visible/Interactive tickers; transport-neutral capability access; and typed
  audio/network services, descriptors, DTOs, events, and errors.
- `WidgetRuntime`: lazy out-of-process workers over bounded framed JSON with
  explicit lifecycle transitions, per-start host-owned companion sessions,
  timeouts, failure reporting, and limited restart. Installed/community workers
  require stable host-derived capability-free Low-integrity AppContainers,
  stripped environments, explicit read/execute roots, PID-bound isolated
  pipes, and pre-launch Job Object memory/process/UI/cleanup
  containment. Public custom workers use
  `WidgetWorkerBootstrap`, which validates host arguments, authenticates the
  optional broker before constructing the widget, attaches host services before
  creation, and owns cancellation and transport disposal.
- `WidgetBridge`: current-user-only native sidecar pipe, trusted plus installed
  catalog discovery, no-poll last-good catalog monitoring/semantic revisions,
  compatible-worker preservation and changed-worker retirement, worker
  forwarding, quick actions, controller input, host-owned mandatory community
  isolation selection, PID/identity/declaration-bound broker companions,
  invalidation/failure events, no-poll platform-appearance revisions, globally
  layered widget themes, bounded shell appearance, and computed GBSS styles.
- `WidgetWorkerHost`: a packaged generic worker executable that loads one
  installed package's public concrete SDK `Widget` entrypoint and contained
  dependencies inside the mandatory package AppContainer, authenticates an
  optional broker channel, attaches typed host services before creation, then
  serves the standard isolated snapshot/action/lifecycle protocol.
- `WidgetStyling`: bounded GBSS parsing, safe package-relative imports,
  variables, explicit trusted cascade layers, typed allowlisted values, and
  diagnostics.
- `PlatformSettings`: strict atomic/cross-process appearance persistence,
  version-pinned development themes, built-in default, safe theme discovery,
  layer composition, and last-good snapshots.
- `WidgetCatalog`: safe `.gbarwidget` inspection/extraction, immutable versions,
  schema-1 state migration, fail-closed exact version pins, discovery,
  enablement, and pin-preserving order persistence. Enabled compatible packages
  join complete validated live bridge revisions and remain lazy until first use.
- `PlatformBroker`: a version-1 audio-session/network capability foundation with
  four closed grants, SID/Low-label/PID-bound isolated endpoints plus nonce/
  identity authentication, manifest/consent/lifecycle enforcement, strict
  bounded DTOs/events, atomic consent persistence, bounded/coalesced
  subscriptions, and a deterministic simulator. It is connected to widget
  `HostServices`; the trusted bridge composes the real Windows audio and network
  backends.
- `WindowsAudioProvider`: an event-driven Core Audio backend for sanitized
  per-application sessions on the current default multimedia render endpoint.
  A dedicated MTA owns native objects; callbacks only enqueue coalesced refresh
  work. It supports endpoint master volume/mute and per-session volume/mute,
  but not output-device switching or microphone control.
- `WindowsNetworkProvider`: a lazy event-driven Windows backend with a dedicated
  MTA owner, bounded/coalesced queues, coarse IP Helper connectivity hints,
  ACM-only Native Wi-Fi notifications, sanitized saved-profile enumeration,
  and opaque saved-profile switching. Automatic reads do not query
  location-sensitive current SSID/signal, so those details remain explicitly
  privacy-restricted in version 1.
- `GbarCli`: working `new`, `validate`, `render`, `replay`, deterministic
  `pack`, bounded local/HTTPS/GitHub Release `install`, and catalog `list`,
  `enable`, `disable`, and `version list|select|rollback` commands. Local and
  remote updates share an exact-stream pre-publish enabled-ID guard. Remote
  acquisition requires SHA-256 pinning, reports the actual digest, and installs
  disabled pending explicit review.

The first-party Settings, Audio Mixer, and Network Controls references and the
Clock/YT Music samples exercise the public widget path. Audio Mixer and Network
Controls are bounded integration slices for the larger controller-first Audio
Control and Network Control roadmap items; their presence here does not mean
those product widgets are complete or shipped. Settings is packaged and
registered beside YT Music, renders through
the generic SDK/bridge/native path, uses nested controller scopes, persists
bounded appearance values, pages valid/invalid themes, exposes diagnostics,
requires confirmation before reset, and provides two separate controller
flows: installed package identity/version/publisher/runtime/required-optional
capability review, a nested paged Manage versions surface with disabled-only
exact selection/rollback, plus enable/disable; then package → capability →
grant/deny consent. Enablement is not consent. Permission grants require explicit
confirmation; deny/revoke is immediate, missing/invalid state fails closed,
and first-party packages are not auto-granted. It reloads settings/themes/
catalog/permissions once per active lifetime and does not poll in Background.
The same public compatibility evaluator gates Bridge and Settings: details show
host API/architectures and a bounded reason, incompatible enablement is blocked,
and disable remains available for recovery.

The current Audio Mixer reference slice is packaged and registered through the
same catalog/bridge/worker path as the other widgets. Its widget code uses only
the public typed audio service,
fetches once on activation, then
reacts to provider events rather than polling. It offers controller session
selection plus optimistic per-session volume/mute controls in Interactive and
renders explicit permission, lifecycle, empty, unavailable, and failure states.
Broader hardware/churn coverage and end-to-end hidden/visible performance evidence
remain open; this is not yet an end-user release claim.

The current Network Controls reference slice runs as an out-of-process
first-party SDK widget and worker with trusted catalog and Release-build
packaging wiring. It requires the network read capability, makes saved-profile
switching optional and
Interactive-only, opens its acknowledged status subscription before snapshots,
and never polls. It declares no dashboard quick actions. The open widget renders
every saved profile in one bounded vertical Scroll; D-pad/left-stick Up/Down
moves focus, and A or X routes through the exact focused row without LB/RB or
LT/RT profile cycling.
The real provider returns on `WlanConnect` acceptance, then publishes
authoritative `Connecting`, `Failed`, and refreshed status events. Its focused
provider and widget Release suites pass 18/18 and 16/16 respectively. Focused checks do not
replace hardware/privacy/performance matrices, which remain open. The current
full managed Release suite, native host suite, package required-file checks,
hidden-startup smoke, and controller input-probe smoke pass.

YT Music uses the YTMDesktop2 loopback API, performs a non-blocking automatic
connection attempt, and renders media metadata, artwork, transport state, and
dashboard quick actions through the declarative protocol. It auto-connects on
first entry into Visible/Interactive, interpolates progress there at four Hz,
reconciles the companion every two seconds, and uses bounded optimistic transport/rating
updates with rollback on command failure. Like, dislike, shuffle, and repeat
publish immediate semantic selected/busy feedback, preserve independent pending
features through stale polls, and clear or roll back on reconciliation.

### Settings and global theme pipeline

The first-party Settings widget controls text/interface scale, backdrop
opacity, System/Full/Reduced motion, System/Standard/High contrast, bold text,
reduced transparency, exact theme ID/version selection, and confirmed reset.
`PlatformAppearanceService` watches settings and theme files
with event notifications plus a 200 ms debounce—there is no polling loop.
Valid reloads increment an immutable revision, clear per-widget layered-theme
caches, and emit a bridge appearance-change event. Invalid reloads keep the
prior snapshot and revision.

The bridge resolves platform → widget → user layers, with layer priority
stronger than selector specificity. It publishes globally layered widget
`base`/`focused` styles and a bounded shell appearance containing scale,
backdrop, motion, contrast, bold-text, transparency, and semantic shell styles
without launching widget workers.
The native client consumes the initial shell appearance and live revision
events, rejects stale revisions, retains its last good state on failure, and
applies supported shell styles and the complete bounded appearance record. The
host-owned policy runs after every shell/widget GBSS layer: text scale
remeasures/reflows without compounding inherited `em`, reduced motion removes
transitions, reduced transparency removes blur and makes node surfaces opaque,
bold text enforces minimum weight 600, and System/forced high contrast corrects
text/focus against inherited surfaces with a geometric focus ring. Windows
setting changes reapply System contrast and motion immediately.

The CLI provides `gbar theme new|validate|preview|pack|inspect|install|list`.
Schema-version-2 `.gbartheme` packages are data-only, deterministic, bounded,
publisher-namespaced, digest-addressable, revalidated through the production
compiler, and installed as immutable ID/version directories through staged
atomic moves. Remote HTTPS/GitHub release installs require a pinned SHA-256.
Preview is computed terminal output, not native pixels; signing, revocation,
remove/update/rollback, asset support, and graphical preview remain open.

### Live package catalog

`BridgeCatalogMonitor` watches only the packaged trusted catalog plus the
current-user `catalog-state.json` and packages subtree. A capacity-one channel
coalesces file hints and debounces write bursts for 175 ms before a complete
bounded reload. Staging, cross-process lock, and atomic temporary files are
ignored. Semantic changes advance a bridge-lifetime revision; invalid trusted
or installed catalog/state retains the complete last-good catalog/revision.
Invalid individual styles or unsupported capabilities are isolated with
bounded diagnostics. Reload/list never starts a worker.
Starting the monitor schedules a complete catch-up reload after both watchers
are active, closing the initial load-to-watch race.

Catalog-state schema 2 adds an optional exact active-version pin while retaining
schema-1 reads. Without a pin, discovery selects the greatest installed
`System.Version`; the next successful mutation migrates legacy state to schema
2. `gbar version list|select|rollback` exposes immutable installed versions.
Selection and rollback require a disabled widget, keep it disabled for review,
and never rewrite package bytes. A missing pinned directory fails discovery
closed with `active_version_missing` rather than silently executing another
version.

Presentation/order-only changes preserve compatible workers while atomically
swapping their validated presentation/quick-action metadata. A package,
publisher, instance, executable, argument, declared-capability, or memory-policy
change retires the prior client; stale events are ignored and the next use
starts/authenticates the replacement lazily. The native host coalesces catalog
events, holds a revision in-flight until an atomic descriptor list parses and
reconciles, and retries a genuine in-flight change event at bounded 250, 500,
and 1000 ms delays. Hiding the overlay cancels and abandons that retry sequence;
the same revision may be announced again, and a later open/list catches up.
Ordinary open-time failures do not create polling. A new bridge session resets
tracking. Reconciliation invalidates affected snapshot/style caches, clears
every runtime-changed widget's focus/lifecycle independently of cache
residency, and safely returns from a disabled/removed active widget.

### Worker lifecycle

The implemented host-authoritative lifecycle separates presentation from
residency:

- **Created:** runtime initialization and the one-time author hook.
- **Background:** resident by default but not selected/open. Visible-lifetime
  work is canceled; explicitly permitted widget-lifetime background work may
  continue.
- **Visible:** the dashboard card is selected and can receive declared quick
  actions.
- **Interactive:** the widget is open and receives its scoped controller
  actions.
- **Destroying:** terminal bounded cleanup after widget-owned tokens are
  canceled.

The managed SDK exposes widget-lifetime, per-state, and shared
Visible/Interactive lifetime tokens, plus creation, stable-state-change, and
destroying hooks. The native host publishes `Visible` for the selected bridge
card, `Interactive` for its open surface, and `Background` when hidden or
switched. Authors cannot request their own lifecycle transitions.

Future lifecycle policy must be manifest/user controlled: `keep-alive` is the
default, with opt-in `suspend-when-hidden` and `unload-after-idle`. The current
host does not yet expose or enforce those residency choices or general
manifest background policy. The current capability broker independently denies
all operations/subscriptions while Background.
Separately, trusted bridge policy now bounds each Windows worker with a Job
Object memory ceiling and one-process limit regardless of lifecycle. Crash,
hang, shutdown, and user-requested termination remain separate safety/
administrative paths.

### Current visible resource sample

One live visible-overlay sample measured `OverlayHost` at 93.2 MB,
`WidgetBridge` at 59.1 MB, and one widget worker at 51.5 MB: **203.8 MB private
memory** in total. Across a five-second CPU sample, `OverlayHost` accumulated
**78.12 ms**; bridge and worker deltas were below the measurement timer's
resolution. This is a single prototype observation, not a steady-state budget
pass or a claim about hidden, GPU, wakeup, or multi-widget cost.

`scripts\Measure-OverlayPerformance.ps1` provides a repeatable bounded Windows
process-tree observation for packaged hidden and optional independently
launched visible states. It writes versioned JSON/Markdown with machine/build
metadata, CPU-time deltas normalized by logical processors, working/private
memory, handles, threads, percentiles, readiness proxies, and non-gating target
comparisons. Its deterministic helpers run under `-SelfTest` in verification.
This CIM/performance-counter sampler is diagnostic only: it does not measure
GPU, wakeups, presented-frame or controller latency and does not replace the
planned ETW/PresentMon release harness.

### Test coverage

The repository verification script builds and runs managed suites for the SDK,
protocol, YT Music, first-party Settings, runtime, CLI, styling, platform
settings/themes, catalog, bridge, the generic worker host, broker, and Windows
providers/reference widgets. The runtime covers suspended pre-containment
launch, memory/process/UI limits, kill-on-close, restart cleanup, and mandatory
community AppContainer authority. Its focused Release harness passes 25/25;
the isolation probe verifies distinct stable SIDs, Low integrity, zero
capability SIDs, allowed package reads, denied package writes/host and other-
profile reads/network, stripped secrets, private-profile write/isolation, and
bounded cleanup. The current SDK and YT Music Release suites pass 41/41 and
35/35 respectively. The current Settings Release suite passes 32/32, including
paginated identity review,
disabled-only version selection/rollback, required/optional separation,
enablement-versus-consent copy, fail-closed catalog/compatibility behavior,
nested visual-accessibility controls, legacy appearance defaults, and no
polling. Styling and platform settings/themes pass 18/18 and 13/13,
including Busy-state composition and legacy schema-1 theme compatibility. CLI
passes 35/35, including version
list/selection/rollback, exact-stream local/remote update policy, theme
scaffold, production validation/computed preview, deterministic
packaging/inspection, pinned-GitHub installation, catalog limits, immutable
versions, and adversarial package cases. Catalog passes 21/21, including
schema-1 state migration, exact active-version pins and disabled repair,
linearizable concurrent rollback/first-install operations, public-API
disabled-update enforcement, lock-free reads during atomic state replacement,
pin-preserving reorder, shared host-API/architecture evaluation, and exact-
version unsigned authority derivation. Bridge
passes 24/24, including semantic catalog revisions/last-good/catch-up reload,
atomic presentation metadata replacement, compatible-worker reconciliation,
trusted built-in Job-only policy, and mandatory installed-package isolation
metadata. PlatformBroker passes 23/23, including closed isolated-client SID/
pipe scopes, nonce/full-identity authentication, bounded requests/events,
consent/lifecycle gates, and revocation. An actual AppContainer-to-broker
request integration also passes with the exact SID, Low-label global endpoint,
expected PID, nonce, and widget identity checks in force.
The generic worker-host suite passes 9/9, including that real typed broker
request from an AppContainer worker. Audio provider and Audio Mixer pass 14/14
and 22/22; Network provider and Network Controls pass 18/18 and 16/16.
Pre-resume native fault injection and an installed package launched through the
published Bridge/catalog layout remain explicit release-test gaps.
Network Controls and its Windows provider retain their focused 16/16 and 18/18
coverage for controller/focus, lifecycle/no-poll subscription ordering,
privacy/explicit state, optimistic command reconciliation, opaque identity,
native churn, cancellation, bounded failure, owner-thread disposal, responsive
GBSS, and privacy-safe real Windows read smoke.

The full native aggregate passes. Focused native suites report Controller
Navigation 62 checks, Slider Interaction 2,071, Focus Navigation 17, Widget Surface
Focus 16, and Declarative Renderer 4,227. Display-sensitive evidence includes 187
declarative-layout checks, 668 placement/render-metric/surface-geometry checks,
and 18 foreground-target/reentrancy checks, alongside state-machine,
remote-image, semantic-icon, native-style, focus, catalog parsing, and renderer
suites. It covers deterministic tiny/portrait/negative-coordinate/wide/4K and
72–480-DPI math plus 150% font-size/letter-spacing adaptation. The platform
diagnostics suite passes 8/8 and the catalog suite passes 21/21. The packaged
hidden startup smoke remained resident for its 1.2-second observation. Physical
mixed-monitor migration/hot-plug screenshots and the broader 150% visual matrix
remain evidence gaps.

Run managed verification:

```powershell
.\scripts\Verify.ps1 -Configuration Release -SkipNative
```

Run the complete Windows verification with Visual Studio Desktop development
with C++ installed:

```powershell
.\scripts\Verify.ps1 -Configuration Release
```

## Honest limitations

- The generic native renderer handles the current declarative node kinds and
  renders YT Music, Settings, Audio Mixer, Network Controls, and installed
  widgets through catalog descriptors. Responsive viewport/containment math is
  covered broadly; physical mixed-DPI, localization, accessibility, and visual
  regression evidence is still incomplete.
- Local and bounded remote package/catalog commands plus CLI and Settings
  immutable-version selection/rollback are implemented. There is no
  graphical/file-picker installer, automatic release/update discovery, signed
  publisher workflow, version removal/garbage collection, or marketplace yet.
  Settings provides controller identity/version review and enable/disable after
  CLI installation. The
  bridge publishes accepted catalog changes live with last-good retention and
  safe worker reconciliation. Closed capability declarations are connected to
  the separate Settings consent flow.
- Mandatory capability-free AppContainer isolation is implemented for every
  installed/community worker, while trusted bundled Settings and YT Music
  temporarily remain Job-only for desktop-user dependencies. Publisher
  signing/revocation, CPU quotas, disk/profile quotas and cleanup, provider
  hardening, and the security audit/history UI are not production-ready. The
  narrow Core Audio and Windows network backends still need broader hardware/
  privacy/churn and hidden-state performance evidence. Isolation plus
  capability transport/consent does not establish a public publisher-trust
  boundary.
- Win32k system-call disable is not enabled for managed workers because the
  tested mitigation caused CoreCLR DLL initialization failure (`0xC0000142`).
  Job Object UI restrictions remain enabled.
- Closed audio/network manifest permissions are enforced through the broker.
  Community AppContainers have zero OS capabilities/network authority and no
  general desktop token; direct resource access is limited to explicit
  read/execute runtime/package grants. OS capability APIs remain brokered.
- The CLI's DLL render command executes trusted development code in the CLI
  process; it is not a sandbox.
- Universal controller suppression is unresolved for games using background
  Raw Input or direct HID access.
- The quarantined XInput Guide fallback depends on an undocumented system-DLL
  ordinal/bit and may vary by controller model, mode, firmware, and transport.
- True Fullscreen Exclusive, HDR, physical mixed-DPI/hot-plug coverage,
  anti-cheat compatibility, latency, and steady-state resource budgets need a
  larger measured matrix.
- Topmost/foreground reassertion is best effort. Secure desktop, elevated
  windows, exclusive render paths, and multi-monitor backdrop coverage are not
  supported contracts.
- Background worker processes intentionally remain resident by default.
  Visible/Interactive work is canceled, and the current broker denies every
  capability in Background. Manifest `backgroundPolicy`, resource-policy, and
  opt-in suspend/unload enforcement are not implemented yet.
- GBSS compilation and bridge-global layering are implemented. `selected`,
  `disabled`, and `busy` snapshot state participates in the complete
  `base`/`focused` maps. A transient `pressed` map and every future dynamic
  semantic state are not connected end to end.
- Controller Settings, strict persistence, version-pinned theme selection,
  no-poll watching, last-good revisions, and globally layered widget styles are
  implemented. Safe theme scaffold/validate/computed-preview/pack/inspect/
  install/list tooling and native post-cascade contrast/bold/reduced-
  transparency policy are implemented. Native graphical preview, signing,
  removal/update/rollback, auto-scroll, screen-reader/magnification integration,
  and the physical combined 150% accessibility matrix are not. See [settings
  and global themes](settings-and-themes.md) and [theme packaging and
  distribution](theme-packaging.md).
- Audio Mixer is a bounded first-party public-SDK reference slice: its widget,
  package/catalog integration, and event-driven per-session Core Audio provider
  exist, while the controller-first **Audio Control** roadmap remains
  incomplete. That roadmap adds supported output/default-role selection and
  explicit-capability microphone mute/level only after provider, privacy,
  hardware, and performance review. Network Controls is likewise a bounded
  reference slice with provider, worker, catalog, controller, and packaging
  evidence—not a completed **Network Control** product widget. Its roadmap adds
  privacy-gated identity/link details, bounded IP/gateway/DNS summaries,
  measured throughput/latency/loss diagnostics, and reviewed recovery actions.
  Version 1 explicitly excludes password entry, profile creation, scans, radio
  controls, and automatic current SSID/signal access. See
  [widget capabilities](capabilities.md) and [Windows provider
  architecture](windows-provider-architecture.md) and [Network Controls
  reference](network-controls.md).

## Diagnostics

- Latest overlay initialization error:
  `%LOCALAPPDATA%\GameBarAlternative\startup-error.log`
- Overlay order/last-widget state:
  `%LOCALAPPDATA%\GameBarAlternative\overlay-state.ini`
- Input-probe logs: timestamped in the launch directory by default, or the
  explicit `--log` path.

See the [documentation index](README.md), [security and trust](security-and-trust.md),
and [troubleshooting](troubleshooting.md).

## Next vertical slices

1. Complete physical mixed-DPI/accessibility/visual-regression evidence,
   including the 150% text-scale matrix and controller focus reachability.
2. Extend the bounded process sampler with ETW/PresentMon automation, stored
   comparable baselines, latency scenarios, and per-widget resource diagnostics.
3. Add native graphical theme preview, package remove/update discovery, and
   `gbar dev` (Settings and CLI exact-version rollback are implemented).
4. Implement publisher signing/revocation, crash quarantine, CPU and disk/
   profile quotas/cleanup, and security audit UI before public community
   distribution; migrate trusted built-ins as their desktop dependencies become
   brokered.
5. Advance the controller-first Audio Control and Network Control roadmap from
   their bounded reference slices through hardware/privacy/performance gates,
   then continue non-auth Performance, general media, recent apps/games, and
   capture references.
6. Run the documented controller/game/presentation/anti-cheat matrix.
