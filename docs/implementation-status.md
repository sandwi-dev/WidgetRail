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
artwork, and fixed native semantic icon geometry.

The visible shell uses separate panel and dimming-backdrop windows on the prior
foreground app's monitor. An outside backdrop click closes the overlay. Both
windows are topmost only while visible, with foreground/z-order observation and
best-effort reassertion. This is normal DWM windowing, not game injection.

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
- `WidgetBridge`: current-user-only native sidecar pipe, trusted catalog,
  enabled-installed catalog discovery, worker forwarding, quick actions,
  controller input, identity/declaration-bound broker companions, invalidation/
  failure events, no-poll platform-appearance watching/revisions, globally
  layered widget themes, bounded shell appearance, and computed GBSS styles.
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
  discovery, enablement, and order persistence. Enabled compatible packages now
  join the bridge catalog at startup and remain lazy until first use.
- `PlatformBroker`: a version-1 audio-session/network capability foundation with
  four closed grants, nonce/identity-bound named-pipe transport, manifest/
  consent/lifecycle enforcement, strict bounded DTOs/events, atomic consent
  persistence, bounded/coalesced subscriptions, and a deterministic simulator.
  It is connected to widget `HostServices`; the trusted bridge now composes the
  real Windows audio backend with the simulated network backend.
- `WindowsAudioProvider`: an event-driven Core Audio backend for sanitized
  per-application sessions on the current default multimedia render endpoint.
  A dedicated MTA owns native objects; callbacks only enqueue coalesced refresh
  work. It supports per-session volume/mute, not endpoint master control,
  output switching, or microphone control.
- `GbarCli`: working `new`, `validate`, `render`, `replay`, deterministic
  `pack`, bounded local/HTTPS/GitHub Release `install`, and catalog `list`,
  `enable`, and `disable` commands. Remote acquisition requires SHA-256 pinning,
  reports the actual digest, and installs disabled pending explicit review.

The first-party Settings and Audio Mixer widgets and the Clock/YT Music samples
are reference widgets. Settings is packaged and registered beside YT Music,
renders through
the generic SDK/bridge/native path, uses nested controller scopes, persists
bounded appearance values, pages valid/invalid themes, exposes diagnostics,
requires confirmation before reset, and provides package → capability → grant/
deny review. Permission grants require explicit confirmation; deny/revoke is
immediate, missing/invalid state fails closed, and first-party packages are not
auto-granted. It reloads settings/themes/permissions once per active lifetime
and does not poll in Background.

Audio Mixer is the active first-party integration milestone. It is packaged and
registered through the same catalog/bridge/worker path as the other widgets. Its widget code
uses only the public typed audio service, fetches once on activation, then
reacts to provider events rather than polling. It offers controller session
selection plus optimistic per-session volume/mute controls in Interactive and
renders explicit permission, lifecycle, empty, unavailable, and failure states.
Broader hardware/churn coverage and end-to-end hidden/visible performance evidence
remain open; this is not yet an end-user release claim.

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
settings/themes, catalog, bridge, the generic worker host, and broker. The
runtime suite covers suspended pre-containment launch, memory/process limits,
kill-on-close, and restart cleanup. The generic host suite covers seven loader/
protocol/capability cases; Settings covers twenty controller/permission cases;
the SDK covers twenty-seven contracts; and the broker covers twenty
capability, consent, identity, lifecycle/event, pipe, sanitization, and
cancellation contracts. Bridge integration also covers installed-package
fail-soft and capability companion behavior.
Native build verification covers the overlay plus state-machine, remote-image,
declarative layout, semantic-icon, and native-style tests where integrated.
Current Release native-style coverage verifies 150% font-size/letter-spacing
scaling and safe fallback for an invalid non-finite scale; the full native
suite passes. The broader 150% multi-resolution visual matrix remains an
evidence gap.

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
  now renders both YT Music and Settings through catalog descriptors. Broad
  arbitrary-package, resolution/DPI, accessibility, and visual regression
  evidence is still incomplete.
- Local and bounded remote package/catalog commands are implemented. There is
  no graphical installer, live bridge catalog reload, automatic release/update
  discovery, signed publisher workflow, or marketplace yet. Enabled compatible
  packages are discovered when the bridge starts, so install/enable/disable
  or manifest changes require an overlay/bridge restart. Closed capability
  declarations are connected to the broker and Settings consent flow.
- Publisher signatures, AppContainer, CPU quotas, the Windows network provider,
  and security audit/history UI are not production-ready. The narrow Core Audio
  session backend is implemented, but still needs broader hardware/churn and
  hidden-state performance evidence. Job Object containment
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
- True Fullscreen Exclusive, HDR, broad mixed-DPI coverage, anti-cheat
  compatibility, latency, and steady-state resource budgets need a larger
  measured matrix.
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
- Audio Mixer is the active first-party public-SDK example: its widget,
  package/catalog integration, and event-driven per-session Core Audio provider
  exist, while broader hardware and performance evidence remain open. Network
  Controls is next; its real WLAN/IP Helper provider is not implemented. Initial
  Network Controls explicitly excludes password entry and profile creation. See
  [widget capabilities](capabilities.md) and [Windows provider
  architecture](windows-provider-architecture.md).

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

1. Complete resolution/DPI/accessibility/visual-regression evidence, including
   the 150% text-scale matrix for the generic renderer and themed shell.
2. Add safe theme install/scaffold/validate/preview tooling plus a user-facing
   widget install/review flow and live catalog reload.
3. Implement signing/trust and AppContainer-equivalent isolation before
   supporting untrusted community binaries.
4. Finish Audio Mixer packaging/performance evidence, then build Network
   Controls as the next event-driven, out-of-process public-SDK reference
   package through the same generic path.
5. Run the documented controller/game compatibility and performance matrix.
