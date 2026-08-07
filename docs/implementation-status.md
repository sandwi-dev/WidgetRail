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
  authoring; button glyphs; focus/shortcut/state helpers; scoped shortcut
  routing; invalidation; five-state lifecycle hooks/tokens; and bounded,
  non-overlapping Visible/Interactive tickers.
- `WidgetRuntime`: lazy out-of-process workers over random named pipes with
  bounded framed JSON, explicit lifecycle transitions, timeouts, failure
  reporting, and limited restart.
- `WidgetBridge`: current-user-only native sidecar pipe, trusted catalog,
  worker forwarding, quick actions, controller input, invalidation/failure
  events, and computed GBSS styles.
- `WidgetStyling`: bounded GBSS parsing, safe package-relative imports,
  variables, deterministic cascade, typed allowlisted values, and diagnostics.
- `WidgetCatalog`: safe `.gbarwidget` inspection/extraction, immutable versions,
  discovery, enablement, and order persistence as a managed library.
- `GbarCli`: working `new`, `validate`, `render`, `replay`, deterministic
  `pack`, and local `install`, `list`, `enable`, and `disable` commands.

`samples/ClockWidget` and `samples/YtMusicWidget` are reference widgets. YT
Music uses the YTMDesktop2 loopback API, performs a non-blocking automatic
connection attempt, and renders media metadata, artwork, transport state, and
dashboard quick actions through the declarative protocol. It auto-connects on
first entry into Visible/Interactive, interpolates progress there at four Hz,
reconciles the companion every two seconds, and uses bounded optimistic transport/rating
updates with rollback on command failure. Like, dislike, shuffle, and repeat
publish immediate semantic selected/busy feedback, preserve independent pending
features through stale polls, and clear or roll back on reconciliation.

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
host does not yet expose or enforce those choices, background permissions, or
resource limits. Crash, hang, shutdown, and user-requested termination remain
separate safety/administrative paths.

### Current visible resource sample

One live visible-overlay sample measured `OverlayHost` at 93.2 MB,
`WidgetBridge` at 59.1 MB, and one widget worker at 51.5 MB: **203.8 MB private
memory** in total. Across a five-second CPU sample, `OverlayHost` accumulated
**78.12 ms**; bridge and worker deltas were below the measurement timer's
resolution. This is a single prototype observation, not a steady-state budget
pass or a claim about hidden, GPU, wakeup, or multi-widget cost.

### Test coverage

The repository verification script builds and runs managed suites for the SDK,
protocol, YT Music, runtime, CLI, styling, catalog, and bridge. Native build
verification covers the overlay plus state-machine, remote-image, declarative
layout, and semantic-icon tests where integrated.

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

- The native renderer remains a prototype. The YT Music reference path is
  integrated, while complete generic rendering/layout/styling for arbitrary
  packages is still being finished.
- Local package/catalog commands are implemented. There is no graphical
  installer, native-host discovery from the user catalog, URL downloader,
  signed publisher workflow, or marketplace yet.
- Publisher signatures, revocation, AppContainer, Job Object enforcement,
  capability brokering, and user permission consent are not production-ready.
- Manifest permission strings and resource requests are validated metadata,
  not complete enforcement.
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
  Visible/Interactive work is canceled; background permission enforcement and
  opt-in suspend/unload lifecycle policies are not implemented yet.
- GBSS compilation is implemented. The bridge currently publishes `base` and
  `focused` computed maps; all additional semantic state maps are not yet
  connected end to end.

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

1. Finish the generic native renderer for all validated node kinds and typed
   computed styles.
2. Connect the safe user catalog to host discovery and add a user-facing
   install/review workflow.
3. Implement signing/trust, capability brokering, and production worker
   containment before supporting untrusted community binaries.
4. Run the documented controller/game compatibility and performance matrix.
