# Generated desktop-surface fixture

Validation only. This is a local Electron client with generated content, not a
Discord/Vencord plugin. It opens no network media, capture source, microphone,
account profile or credentials. No WebView, HWND reparenting or frame relay is used.

The first scenario tests borrowing/restoring the original generated frameless
window inside a WinUI placement rectangle. It checks the OS pipe peer, executable
and process creation identity, real native foreground, exact auxiliary-window
admission, placement/DPI rounding, and restored hidden state/bounds. The host uses
the existing input pump with a hardware-free ReplayNativePlatform. Physical
controller, IME, native modal layering and actual Discord video are **not** proven.

Source files in `src/OverlayFrontend.WinUI/Validation` and their argument dispatch
are excluded from production builds. No production external-surface contract was
added. The current fixture pins Electron44.9.3, matching the observed9266 client
runtime. Earlier42.7.1 evidence remains historical. This is a separate generated
runtime with default rendering flags, not a copied client binary.

1. Run `node --test tools/DiscordDesktopIntegration/SurfaceFixture/protocol.test.cjs`.
2. Run `Stage.ps1 -OutputDirectory <fresh artifacts/discord-desktop child>` to
   restore lockfile-pinned packages and download the pinned Electron runtime. This
   does not launch a window or install Discord.
3. Build/publish the existing WinUI frontend with `EnableWidgetValidation=true`
   and matching native prerequisites, using the repository's established build
   workflow and a unique binary log. Do not replace the running candidate.
4. Build the generated native geometry helper as described below. Coordinate
   foreground use, then run `Run.ps1 -FixtureDirectory <staged fixture>
   -FrontendDirectory <validation frontend> -NativeGeometryDirectory <native build>
   -OutputDirectory <fresh artifacts child> -AllowForeground`. The launcher checks
   the native build's binary hash. The helper stays hidden until the Run button.
5. Use Computer Use on the generated WinUI window and click **Run generated-window
   check**. Inspect `result.json` and visually inspect the fixture independently.
   The result explicitly keeps visual review and physical-controller testing open.

The helper accepts only sixteen bounded commands on a random local pipe using a
per-run token. It restores its own window and exits on disconnect, or after a
two-minute watchdog. This limited test protocol is not the production authenticated
companion design. Do not use it to attach to arbitrary processes or windows.

Next scenarios are added only after the initial borrow/restore evidence: native
modal overlap, passive video pin, controller/IME and accessibility, monitor/DPI
changes and crash expiry. Generated content cannot prove Discord's native-video
sink compatibility or background continuity.

## Initial evidence (2026-10-27)

Protocol fixtures pass2/2. The validation frontend published successfully after
adding the required `partial` modifier to its WinRT-derived Grid class. Build
evidence is in `artifacts/discord-desktop/surface-frontend-1-build-fix.log` and its
unique binary log; the pinned runtime is staged in `surface-runtime-1`.

The first native attempt, `artifacts/discord-desktop/surface-run-1`, did **not**
execute the scenario. Computer Use found the correct accessibility tree, but
the screenshot showed underlying desktop content, and Run/activation/Tab did
not produce the action or a `result.json`. No hidden control channel was used
as a substitute. `interaction-result.json` records the exact observations and
path-verified cleanup of the two runner-owned processes. Both exited; no user
application was modified. Native mechanics and visual acceptance remain open.

The launcher emitted valid process identities before hitting a PowerShell
null-conditional handle-disposal error; explicit null checks correct that script.
At that point the correction had not yet been exercised in another UI launch.
A successful build and protocol test must not be presented as a native surface pass.

One detached comparison was subsequently performed with the same published
frontend. Stock `--validate-controls` exposed the expected tree and initial
focused button, but Return did not increment its action count; screenshots again
showed underlying desktop content. The custom surface fixture showed the same
capture/action-delivery failure and never entered its borrow/restore scenario.
Evidence: `artifacts/discord-desktop/surface-run-detached-1/interaction-result.json`.
Both fixtures and their generated Electron helper were identity-checked and closed.
This rules out treating the custom Grid alone as the explanation; shared build,
runtime, environment and Computer Use behavior remain unseparated.

`Run.ps1` now uses detached shell execution for its GUI processes, avoiding the
short-lived tool's inherited stdio handles. That launcher correction is exercised,
but **did not fix the native capture/input observation**. Public Electron44.9.3
was confirmed available after the real Desktop client reported that runtime;
the fixture's42.7.1 runtime was not replaced during this controlled comparison.

## Read-only startup discriminator

`--validation-window-observation=<fresh output file>` adds a validation-only,
bounded observer to either stock or desktop-surface routes. Use
`--validation-window-target=Widget.second` for `--validate-controls`; the default
target is `DesktopSurface.Run`. The surface runner supplies the observation path
automatically when used with a newly built validation frontend.

At most64 snapshots record the own native HWND/foreground, visible/cloaked/capture
affinity state, extended styles/alpha, native/XAML size and DPI, target bounds,
which native root covers the target's center, focus IDs and pointer/key event
counts. No key values, text, user-window titles, pixels, input injection or control
invocation are collected. This can distinguish capture-only/tool coordinate issues
from an invisible/transparent/not-hit-testable native surface. It does not itself
prove visual rendering or cause any test action to run.

Static review found one concrete isolation omission: the custom route was missing
from `Program.Validation.cs`'s early fixture classifier. It is now explicitly
classified, avoiding production-profile election before its validation UI. This
cannot explain the stock controls failure, whose flag was already classified.
No source path setting capture exclusion on the main window was found; the
ordinary border suppression only removes caption/border chrome. Custom-run
controller diagnostics recorded a safely caught shared-log rotation failure,
but stock controls do not create that replay pump. Neither finding establishes
the common screenshot/input cause.

Next discriminating run: launch stock controls with the observer, inspect startup
snapshots before any input, then compare one observed button action against native
foreground/hit-owner and event counters. Only repeat the custom scenario if those
observations establish an actionable difference. Do not alter backdrop/styles,
change Grid to Page, or bypass input merely to obtain a passing result.

## Current executed result and geometry qualification

The user physically ran the44.9.3 fixture. Nine checks passed, including exact
foreground handoff without overlay dismissal; the first bounds assertion failed
before restoration (`surface-manual-44-1/result.json`). This is not a full pass.

`geometry-probe.cjs` is a separate hidden, generated-window-only discriminator.
Launch it with the staged Electron executable and `--output=<fresh artifact child>`.
Its adjacent `Read-GeneratedGeometry.ps1` reads the exact generated HWND after
checking its process, using a physical-pixel DPI context. No input or foreground
operation occurs. Launch the executable detached with hidden startup; do not
use this helper against user application windows.

`surface-geometry-native-3/result.json` established that client placement is within
one pixel at125% DPI, while saving/restoring Electron integer-DIP bounds changes
the original native dimensions. The validation frontend now records requested,
Electron, native outer and native client bounds before asserting, checks client
pixels with the original two-pixel tolerance, and requires exact restoration of
native outer bounds. The Electron-only restore remains a failing baseline until
an exact, crash-safe lease strategy is qualified. No additional physical run is
useful merely to rediscover that known restoration defect.

## Native geometry candidate

`Build-NativeGeometry.ps1` builds only a generated-fixture Node-API addon. It does
not install anything into Discord or load into a user application. Build with
existing MSVC x64 tools and a fresh output child of `artifacts/discord-desktop`.
Supply a dependency directory containing these official Node22.22.3 files:

- `https://nodejs.org/dist/v22.22.3/node-v22.22.3-headers.tar.gz`, saved as `headers.tar.gz`.
- `https://nodejs.org/dist/v22.22.3/win-x64/node.lib`, saved as `node.lib`.

The script pins their SHA256 hashes from the official release checksums, extracts
headers afresh, links against stable Node-API8 and records source/compiler/binary
provenance. There is no Node runtime installation and no V8 ABI dependency.

```powershell
./tools/DiscordDesktopIntegration/SurfaceFixture/Build-NativeGeometry.ps1 `
  -DependencyDirectory artifacts/discord-desktop/geometry-native-deps-1 `
  -OutputDirectory artifacts/discord-desktop/geometry-native-build-1
```

`native-geometry-probe.cjs` runs with the staged Electron executable,
`--geometry-addon=<absolute generated geometry_lease.node>` and
`--output=<fresh absolute artifact child>`. It creates only hidden generated
windows, then checks physical placement, exact restoration, invalid/retired
leases and restoration on a generated host process exiting. The test client
remains alive after that host exits. It does not establish foreground, controller,
visual, modal, minimized/maximized-window or native Discord video acceptance.

The updated `main.cjs` uses this addon for fixture placement/restoration and still
uses Electron for fixture visibility and foreground. The original Electron-only
build and its failing geometry evidence remain in their prior artifact directories.

Final hidden evidence: `geometry-native-build-3` compiles with /W4 /WX, and
`geometry-native-run-5/result.json` passes35 checks using Electron44.9.3. This
includes exact restoration after generated host termination, fixed-size refusal,
Discord-like minimum-size preservation and malformed/retired/destroyed leases.
Fixed-size refusal is expected: SetWindowPos cannot bypass Electron's size policy.
Installed9266 explicitly creates a resizable main window with default940x500 DIP
minimums, so the interactive fixture now models resizable:true. A production
surface host must honor actual client minimums rather than disabling constraints.
