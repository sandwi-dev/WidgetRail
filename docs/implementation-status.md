# Implementation status

Status: integrated Phase 0 platform prototype, 2026-08-07

This repository contains working native and managed components. It is not yet
a production overlay, public plugin security boundary, end-user installer, or
marketplace.

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
A, and Y are host-owned. Open widgets receive their other semantic actions; B
falls back to the dashboard only when unhandled in the root input scope and
never closes the overlay. Nested scopes never bubble to the root.
GameInput is the primary Guide source. A removable XInput compatibility adapter
uses an undocumented ordinal only for drivers observed to omit Guide callbacks;
it is not a universal device-compatibility guarantee.

Open-widget left-stick navigation is two-dimensional with engage/release
hysteresis and bounded repeat. Focus uses enabled explicit neighbors first and
deterministic rendered geometry as a fallback, without wraparound.

### Widget platform

- `WidgetProtocol`: strict version-1 manifests and snapshots, deterministic
  JSON, stable IDs, focus validation, quick actions, images, closed semantic
  glyphs, explicit active controller scopes and snapshot correlation, and
  button selected/disabled/busy state.
- `WidgetSdk`: typed Stack, Row, Text, Button, Progress, Spacer, Image, and Icon
  authoring; controller-ready ToggleButton/Stepper composites; button glyphs;
  focus/shortcut/state helpers; scoped shortcut routing; invalidation;
  five-state lifecycle hooks/tokens; bounded, non-overlapping
  Visible/Interactive tickers; transport-neutral capability access; and typed
  audio/network services, descriptors, DTOs, events, and errors.
- `WidgetRuntime`: lazy out-of-process workers over random named pipes with
  bounded framed JSON, explicit lifecycle transitions, per-start host-owned
  companion sessions, timeouts, failure reporting, limited restart, and
  pre-launch Windows Job Object containment. Public custom workers use
  `WidgetWorkerBootstrap`, which validates host arguments, authenticates the
  optional broker before constructing the widget, attaches host services before
  creation, and owns cancellation and transport disposal.
- `WidgetBridge`: current-user-only native sidecar pipe, trusted plus installed
  catalog discovery, no-poll last-good catalog monitoring/semantic revisions,
  compatible-worker preservation and changed-worker retirement, worker
  forwarding, quick actions, controller input, identity/declaration-bound
  broker companions, invalidation/failure events, no-poll platform-appearance
  revisions, globally layered widget themes, bounded shell appearance, and
  computed GBSS styles.
- `WidgetWorkerHost`: a packaged generic worker executable that loads one
  installed package's public concrete SDK `Widget` entrypoint and contained
  dependencies, authenticates an optional broker channel, attaches typed host
  services before creation, then serves the standard isolated snapshot/action/
  lifecycle protocol.
- `WidgetStyling`: bounded GBSS parsing, safe package-relative imports,
  variables, explicit trusted cascade layers, typed allowlisted values, and
  diagnostics.
- `PlatformSettings`: strict atomic/cross-process appearance persistence,
  version-pinned development themes, built-in default, safe theme discovery,
  layer composition, and last-good snapshots.
- `WidgetCatalog`: safe `.gbarwidget` inspection/extraction, immutable versions,
  discovery, enablement, and order persistence. Enabled compatible packages
  join complete validated live bridge revisions and remain lazy until first
  use.
- `PlatformBroker`: a version-1 audio-session/network capability foundation with
  four closed grants, nonce/identity-bound named-pipe transport, manifest/
  consent/lifecycle enforcement, strict bounded DTOs/events, atomic consent
  persistence, bounded/coalesced subscriptions, and a deterministic simulator.
  It is connected to widget `HostServices`; the trusted bridge now composes the
  real Windows audio and network backends.
- `WindowsAudioProvider`: an event-driven Core Audio backend for sanitized
  per-application sessions on the current default multimedia render endpoint.
  A dedicated MTA owns native objects; callbacks only enqueue coalesced refresh
  work. It supports per-session volume/mute, not endpoint master control,
  output switching, or microphone control.
- `WindowsNetworkProvider`: a lazy event-driven Windows backend with a dedicated
  MTA owner, bounded/coalesced queues, coarse IP Helper connectivity hints,
  ACM-only Native Wi-Fi notifications, sanitized saved-profile enumeration,
  and opaque saved-profile switching. Automatic reads do not query
  location-sensitive current SSID/signal, so those details remain explicitly
  privacy-restricted in version 1.
- `GbarCli`: working `new`, `validate`, `render`, `replay`, deterministic
  `pack`, bounded local/HTTPS/GitHub Release `install`, and catalog `list`,
  `enable`, and `disable` commands. Remote acquisition requires SHA-256 pinning,
  reports the actual digest, and installs disabled pending explicit review.

The first-party Settings, Audio Mixer, and Network Controls widgets and the
Clock/YT Music samples are reference widgets. Settings is packaged and
registered beside YT Music, renders through
the generic SDK/bridge/native path, uses nested controller scopes, persists
bounded appearance values, pages valid/invalid themes, exposes diagnostics,
requires confirmation before reset, and provides two separate controller
flows: installed package identity/version/publisher/runtime/required-optional
capability review plus enable/disable, then package → capability → grant/deny
consent. Enablement is not consent. Permission grants require explicit
confirmation; deny/revoke is immediate, missing/invalid state fails closed,
and first-party packages are not auto-granted. It reloads settings/themes/
catalog/permissions once per active lifetime and does not poll in Background.
The same public compatibility evaluator gates Bridge and Settings: details show
host API/architectures and a bounded reason, incompatible enablement is blocked,
and disable remains available for recovery.

Audio Mixer is the implemented first-party integration reference. It is
packaged and registered through the same catalog/bridge/worker path as the
other widgets. Its widget code uses only the public typed audio service,
fetches once on activation, then
reacts to provider events rather than polling. It offers controller session
selection plus optimistic per-session volume/mute controls in Interactive and
renders explicit permission, lifecycle, empty, unavailable, and failure states.
Broader hardware/churn coverage and end-to-end hidden/visible performance evidence
remain open; this is not yet an end-user release claim.

Network Controls is implemented as an out-of-process first-party SDK widget and
worker with trusted catalog and Release-build packaging wiring. It requires the
network read capability, makes saved-profile switching optional and
Interactive-only, opens its acknowledged status subscription before snapshots,
and never polls. Dashboard LB/RB actions only change local profile selection;
the open widget uses LB/RB selection plus X or focused A to request a switch.
The real provider returns on `WlanConnect` acceptance, then publishes
authoritative `Connecting`, `Failed`, and refreshed status events. Its focused
provider and widget Release suites pass 17/17 and 13/13 respectively. Focused checks do not
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
opacity, System/Full/Reduced motion, exact theme ID/version selection, and
confirmed reset. `PlatformAppearanceService` watches settings and theme files
with event notifications plus a 200 ms debounce—there is no polling loop.
Valid reloads increment an immutable revision, clear per-widget layered-theme
caches, and emit a bridge appearance-change event. Invalid reloads keep the
prior snapshot and revision.

The bridge resolves platform → widget → user layers, with layer priority
stronger than selector specificity. It publishes globally layered widget
`base`/`focused` styles and a bounded shell appearance containing scale,
backdrop, motion, and semantic shell styles without launching widget workers.
The native client consumes the initial shell appearance and live revision
events, rejects stale revisions, retains its last good state on failure, and
applies supported shell styles, interface scale, shell DirectWrite text scale,
backdrop opacity, and motion. It also propagates bounded platform text scale
through the post-style accessibility policy into generic declarative widget
font size/letter spacing and layout, preserving non-compounding `em`
inheritance.

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

### Test coverage

The repository verification script builds and runs managed suites for the SDK,
protocol, YT Music, first-party Settings, runtime, CLI, styling, platform
settings/themes, catalog, bridge, the generic worker host, broker, and Windows
providers/reference widgets. The runtime covers suspended pre-containment
launch, memory/process limits, kill-on-close, and restart cleanup. The current
Settings Release suite passes 26/26, including paginated identity review,
required/optional separation, enablement-versus-consent copy, fail-closed
catalog/compatibility behavior, and no polling. Catalog passes 13/13, including
shared host-API/architecture evaluation. Bridge passes 22/22, including
semantic catalog revisions/last-good/catch-up reload, atomic presentation
metadata replacement, and compatible-worker reconciliation.
Network Controls and its Windows provider retain their focused 13/13 and 17/17
coverage for controller/focus, lifecycle/no-poll subscription ordering,
privacy/explicit state, optimistic command reconciliation, opaque identity,
native churn, cancellation, bounded failure, owner-thread disposal, responsive
GBSS, and privacy-safe real Windows read smoke.

The full native aggregate passes. Display-sensitive evidence includes 175
declarative-layout checks, 668 placement/render-metric/surface-geometry checks,
and 18 foreground-target/reentrancy checks, alongside state-machine,
remote-image, semantic-icon, native-style, focus, catalog parsing, and renderer
suites. It covers deterministic tiny/portrait/negative-coordinate/wide/4K and
72–480-DPI math plus 150% font-size/letter-spacing adaptation. The packaged
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
- Local and bounded remote package/catalog commands are implemented. There is
  no graphical/file-picker installer, automatic release/update discovery,
  signed publisher workflow, rollback UI, or marketplace yet. Settings provides
  controller identity review and enable/disable after CLI installation. The
  bridge publishes accepted catalog changes live with last-good retention and
  safe worker reconciliation. Closed capability declarations are connected to
  the separate Settings consent flow.
- Publisher signatures, AppContainer, CPU quotas, provider hardening, and the
  security audit/history UI are not production-ready. The narrow Core Audio and
  Windows network backends are implemented, but still need broader hardware/
  privacy/churn and hidden-state performance evidence. Job Object containment
  plus capability transport/consent are implemented but do not form a complete
  untrusted-widget boundary.
- Closed audio/network manifest permissions are enforced through the broker.
  Resource requests and arbitrary direct desktop API/file/network access are
  not constrained without the planned restricted worker token.
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
- GBSS compilation and bridge-global layering are implemented. `selected` and
  `disabled` snapshot state participates in the complete `base`/`focused` maps;
  a full separate family for pressed/busy and every dynamic semantic state is
  not connected end to end.
- Controller Settings, strict persistence, version-pinned theme selection,
  no-poll watching, last-good revisions, and globally layered widget styles are
  implemented. Native shell style/interface-scale/backdrop/motion revisions are
  also applied, including shell DirectWrite text scaling. Theme package
  install/scaffold/preview tooling, the full 150% text-scale visual matrix, and
  the complete high-contrast/bold/reduced-transparency/auto-scroll accessibility
  system are not. See [settings and global themes](settings-and-themes.md).
- Audio Mixer is the implemented first-party public-SDK example: its widget,
  package/catalog integration, and event-driven per-session Core Audio provider
  exist, while broader hardware and performance evidence remain open. Network
  Controls is the second implemented integration: its real provider, widget,
  worker, catalog, and packaging hooks exist and focused/packaged gates pass,
  while hardware/privacy/performance gates remain open. Version 1
  explicitly excludes password entry, profile creation, scans, radio controls,
  and automatic current SSID/signal access. See
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
2. Add repeatable ETW/PresentMon performance automation, stored baselines, and
   per-widget resource diagnostics.
3. Add safe theme install/scaffold/validate/preview tooling and `gbar dev`.
4. Implement signing/trust, rollback/quarantine, and AppContainer-equivalent
   isolation before supporting untrusted community binaries.
5. Continue Audio Mixer/Network Controls hardware evidence, then non-auth
   Performance, general media, recent apps/games, and capture references.
6. Run the documented controller/game/presentation/anti-cheat matrix.
