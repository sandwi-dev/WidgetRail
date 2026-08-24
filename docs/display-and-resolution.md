# Display and resolution behavior

Status: responsive per-monitor placement and layout contract implemented; real
mixed-monitor hardware and visual-regression evidence remains open

The overlay uses a Per-Monitor-V2-aware native window and host-rendered widget
UI. The packaged manifest declares Per-Monitor-V2, and developer/CMake starts
also request `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2` before window
creation. Widget authors work in logical device-independent pixels (DIPs); they do
not position an HWND, select a monitor, or compensate for Windows DPI.

## Monitor ownership

When the overlay opens, the host targets the nearest monitor containing the
active external foreground window. The overlay panel is bottom-centered inside
that monitor's current work area, while the dimming backdrop covers that
monitor's full bounds.

While visible, a valid external foreground change (including Alt+Tab) closes
the overlay. Events from the overlay or its backdrop and transient invalid
HWNDs are ignored. The newly foregrounded external window becomes the restore
target, while the next explicit Guide/F1 open resolves its monitor and display
environment afresh.

The current product intentionally owns one selected monitor. It does not dim
every monitor, draw over secure desktop/UAC, or promise visibility over true
Fullscreen Exclusive presentation.

## DPI, work-area, and topology changes

The host re-reads the target monitor, work area, and effective DPI for every
placement. Effective DPI comes from the resolved `HMONITOR`, not the external
foreground HWND, so a DPI-unaware game cannot force a false 96-DPI result. The
host responds without a polling loop:

- `WM_DPICHANGED` discards display-dependent graphics and recomputes
  host-owned placement instead of accepting a stale fixed-size rectangle;
- `WM_DISPLAYCHANGE` recreates display-dependent graphics and re-reads monitor,
  DPI, work area, panel, and backdrop placement;
- `WM_SETTINGCHANGE` reapplies appearance/accessibility policy, recreates
  graphics, and recomputes work-area placement, including taskbar changes;
- `WM_SIZE` resizes an existing Direct2D HWND target in place, rebuilds only
  viewport-dependent resources, and falls back to full target recreation only
  when Direct2D rejects that resize; and
- a valid external foreground change closes the overlay; the next open resolves
  the new target monitor.

These notifications do no work while hidden; the next open resolves fresh
state. While visible they share one pure refresh policy and one authoritative
placement pass. DPI, topology, and work-area messages commonly arrive as a
burst during monitor migration or hot-plug. The host merges that burst into one
posted refresh while retaining its strongest requested work (including an
appearance refresh for system-setting changes), avoiding repeated render-target
destruction and partial old-DPI/old-work-area frames.

`SetWindowPos` may synchronously cause another DPI message. A reentrancy gate
coalesces that case into one deferred placement refresh, preventing recursive
placement and partial geometry.

## Coordinate model

The renderer no longer shrinks a fixed 1180×700 design canvas to fit. For each
paint it derives:

```text
logical viewport = client physical pixels / (monitor DPI / 96) / interface scale
physical pixels per layout DIP = (monitor DPI / 96) × interface scale
```

Interface scale remains a user-selected accessibility zoom. A constrained
monitor therefore supplies a smaller logical viewport and causes normal
responsive reflow; it does not silently reduce the user's chosen zoom. The
physical-pixel ratio is passed to declarative layout for deterministic edge
snapping.

The host computes one shared widget shell from the current `rcWork`, monitor
DPI, and interface scale. Every admitted or retained widget uses that same
logical shell and bottom tray band; surface hints change only the centered body
panel and its bounded viewport. Geometry remains finite, non-negative, and
contained even for tiny or portrait inputs. At pathological sizes, content may
clip or collapse because no useful space exists; containment is a safety
guarantee, not a claim that a 1×1 display is usable.

The tray inventory is not clipped to the tiles that fit the current widget
extent. At compact widths the host keeps the selected stable identity visible
and reserves named previous/next overflow controls for the adjacent off-page
items; controller, pointer, and UI Automation consume that same layout. Moving
between compact and wide body hints cannot move the tray or change its capacity.
Replacing the catalog preserves the exact persisted order and one selected/
focus owner. Catalog replacement is painted on the host UI thread before a
later pointer message can target the new slot map.

## Widget author contract

Use the public declarative layout and WRSS primitives:

- flex growth/shrink, minimums, maximums, and intrinsic measurement;
- `flex-wrap: wrap` on bounded Rows when controls should form additional lines
  as the logical viewport narrows;
- bounded logical lengths, percentages, `vw`, and `vh`;
- explicit line limits and ellipsis for bounded labels;
- `overflow: clip` for surfaces that must not escape their viewport; and
- semantic `UI.VerticalScroll` / `UI.HorizontalScroll` for controller content
  that can exceed the clamped viewport; and
- stable focus IDs and explicit focus neighbors where reflow makes geometry
  ambiguous.

Wrapped Rows recompute line membership from the current logical width on every
layout pass. Do not branch a widget snapshot on a guessed monitor resolution.
With `gap: 8px 12px`, the first value separates wrapped lines and the second
separates items within each line. Prefer Scroll over wrapping when the item
count is unbounded or vertical traversal is the primary interaction.

Do not assume 1920×1080 physical pixels, 96 DPI, 16:9, a fixed widget width,
positive desktop coordinates, or a particular taskbar position. Widgets never
read monitor APIs themselves. They render the snapshot requested by the host
for the current viewport.

Focusable controls clipped by an ordinary Stack/Row are not navigation
candidates. Descendants clipped by a semantic Scroll remain navigation
candidates because the host can reveal them before dispatch. Scroll offsets
are remembered per exact widget runtime, active input scope, and container ID.
When catalog replacement changes a widget runtime, the host clears that
widget's remembered focus and scroll state rather than restoring IDs or
geometry from a different surface generation.

`WidgetView.Surface` may publish a bounded `Adaptive`, `Compact`, `Standard`,
or `Wide` presentation hint plus optional preferred/minimum logical dimensions.
It is never a fixed-size request. The shell is independently fitted to the
selected monitor's live work area after interface/accessibility scale. The host
then clamps the requested body inside the space above its stationary guide and
tray; declarative layout and Scroll handle the final viewport.

Surface dimensions describe the useful floating **widget panel**, including
the host footer inside that panel. They do not include the persistent icon tray,
the space separating tray and panel, outer safety margins, or the overlay HWND.
The current host defaults are:

| Mode | Preferred panel |
| --- | ---: |
| `Compact` | 560×420 DIPs |
| `Adaptive` | 880×520 DIPs |
| `Standard` | 880×520 DIPs |
| `Wide` | 1120×620 DIPs |

An explicit preferred pair refines its mode. A valid minimum pair is advisory:
the host honors it when space exists, but monitor containment always wins.
Larger text receives additional reflow room before the result is clamped;
interface scale remains a physical zoom and therefore reduces how much of the
requested logical surface fits in a fixed physical work area. Widgets must use
responsive layout and semantic Scroll rather than treating either pair as a
guaranteed measurement.

Snapshots without `Surface` retain the package-API-1 compatibility body
(an 880×522-DIP panel) inside the shared 1180×700 design shell. A worker-starting
message is never body-sizing authority. When another widget presentation has
already been admitted, the host keeps that bounded snapshot painted as
visual-only content inside the same final shell until the destination publishes
a valid snapshot. Input and accessibility authority still transfer immediately
to the destination; the retained controls are neither actionable nor projected
to UI Automation. Only the first widget open, where no committed presentation
exists, uses the compact startup body hint.

Widget-to-widget switching does not resize or transform the host-owned shell,
guide, or tray. Snapshot admission may reflow the body from its retained hint to
the destination hint, but both are clipped to the same bounded body viewport
above the stationary tray. DirectComposition continues to render and commit one
complete premultiplied destination surface inside the single transparent host
container; there is no second presentation owner. The existing dashboard/open
shell timeline remains separate. Hiding retires any pending presentation state
and schedules no hidden frame work.
The composition HWND has no class background brush; the separate backdrop is
the only opaque full-monitor owner. Unknown, partial, non-finite, and
out-of-range native hint data cannot escape the sizing policy and falls back to
bounded mode defaults.

## Deterministic evidence

The native Release suite currently proves these policy/math seams:

- placement across 1280×720, 1920×1040, 3840×2120, portrait 1080×1880,
  negative-coordinate 3440-wide, and offset 5120-wide work areas at 100%,
  125%, 150%, and 200% DPI;
- bounded behavior for 640×360, 1×1, 7×3, an extreme two-billion-pixel-wide
  extent, and oversized margins;
- render-metric reconstruction for 1×1, 853×479, 3440×1440, and 7680×4320
  clients across 72–480 DPI and the supported 0.8–1.25 interface-scale range;
- an integrated placement-to-render matrix spanning handheld-sized, legacy,
  portrait, ultrawide, 4K, 5K, and 8K work areas, including negative and
  offset desktop coordinates, ten DPI values, and dashboard/loading/widget
  heights;
- semantic Compact/Adaptive/Standard/Wide resolution, explicit preferred and
  minimum pairs, API-1 compatibility fallback, malformed-hint fallback, and
  extent stability across identical snapshots;
- 1280×720, portrait, offset-ultrawide, and combined 200%-DPI/125%-interface/
  150%-text surface clamping with host tray/footer/controller reservations;
- 111,253 placement checks across dense logical boundaries for
  panel, widget viewport, adaptive footer, and persistent tray geometry;
- declarative compact/clipping behavior for constrained viewports, including
  portrait cases and non-integer physical-pixel scale;
- deterministic controller-focus recovery when resize/reflow clips the
  preferred control, with hidden controls excluded from explicit navigation
  and action dispatch; and
- targeting checks covering admitted/retained/first-open content and body-hint
  authority plus in-place render-target resize planning;
- transition checks covering the 140 ms extent curve, exact endpoint, reduced
  motion, interrupted retargeting from presented geometry, and the existing
  shell/content timeline;
- exact color-key rounded-chrome raster checks at 1.0 and 1.5 pixel scale; and
- targeting checks covering foreground self-ignore, invalid-target fallback,
  Alt+Tab close policy, duplicate suppression, DPI-placement reentrancy
  coalescing, display-notification burst coalescing, and visible-versus-hidden
  DPI/topology/settings refresh policy.

The production `WidgetSwitchHostTests` fixture drives the real OverlayHost HWND
through eight production-shaped widget identities. Its isolated workers signal
and delay selected first renders so checks occur during—not after—the startup
interval. The matrix asserts retained source content before admission, coherent
destination reveal in the same shell afterward, rapid reversal, same-identity
refresh, exact retained/admitted tray bounds, stable widget-switch tray
capacity, live work-area/DPI re-resolution, contained HWND placement, and
synchronous catalog addition/removal without stale tray focus or dispatch.
Visible catalog-order replacement first re-establishes the fixed-chrome anchor
and composition session for the new tray order, then commits the synchronous
paint. Disabling a widget therefore cannot strand content on a retired chrome
session or force the main overlay into its legacy HWND fallback.

The current packaged continuity verdict also uses this eight-widget route as a
functional temporal gate rather than capture output. It requires the shared tray
to remain fixed, the no-redirection HWND to have no opaque class background, the
complete surface to stay premultiplied-clear, and every destination to commit
before any coordinated placement. It rejects direct-HWND fallback, stale frame
work after hide, shell motion during widget switches, or an incomplete timing
sample.

Run the native contract suite with:

```powershell
.\src\OverlayHost\build.ps1 -Configuration Release -SkipPackaging
```

The complete repository gate also packages the runtime and runs the hidden
startup smoke:

```powershell
.\scripts\Verify.ps1 -Configuration Release
```

The auth-free evidence pipeline adds a reproducible real-package, standalone
widget-body render slice without pretending that deterministic pixels replace
physical display testing:

```powershell
.\scripts\Capture-OverlayEvidence.ps1 -Configuration Release
```

It launches selected retained `.wrwidget` archives through the generic
AppContainer worker and simulated authenticated broker, exports validated
semantic snapshots plus production computed WRSS styles, then parses those
snapshots through the native bridge model and renders offscreen with a harness
built from the recorded production Direct2D renderer sources. This is not an
`OverlayHost.exe` shell/window capture: it excludes backdrop, z-order, tray and
footer composition, focus/input ownership, transitions, the onscreen
compositor, and physical-display fidelity.

The schema-v2 manifest records the exact package archives, every retained
snapshot/trace/index/PNG, the retained renderer executable, source revision and
dirty-state digest, source-file hashes, toolchain versions, bounded stage
results, and SHA-256/length inventory. Every external process has a timeout and
full process-tree termination. Generation fails on any renderer diagnostic or
semantic path/secret scan finding; the verifier also rejects missing, extra, or
mismatched retained files. Build intermediates stay outside the published
bundle. Verify a retained bundle independently with:

```powershell
.\scripts\Capture-OverlayEvidence.ps1 `
  -VerifyManifest .\artifacts\evidence\auth-free\<build-id>\manifest.json
```

Golden full-image hashes are deliberately not pass criteria because Windows
font/raster revisions can change pixels without violating layout semantics.

The retained `final-schema-v2-20260808-final` development run produced 12 Games &
Apps/Spotify PNGs from five authoritative snapshots with zero renderer
diagnostics. Its independently verified inventory contains 24 retained files,
including the exact Games & Apps, Settings, and Spotify 0.1.6 archives. It
supplies auth-free widget-body evidence only for the covered GBA-038/GBA-042
paths. Settings exited before connecting through the generic package worker
because its host-owned settings/catalog service path is not yet injectable;
that sanitized gap is recorded in the manifest, not replaced with a hand-built
snapshot. No live Spotify OAuth/callback, controller ownership, topmost-window,
shell transition, or physical-monitor result follows from this run.

## Remaining evidence

Deterministic geometry is necessary but not sufficient for professional visual
quality. Before release, run and retain screenshots/measurements for:

- physical mixed-DPI monitor migration and hot-plug/remove;
- a DPI-unaware/system-aware foreground app on a scaled monitor;
- taskbar/work-area changes on each edge;
- 21:9, 32:9, portrait, HDR/SDR, and hybrid-GPU systems;
- 150% text with long localized labels and every accessibility combination;
- controller focus reachability after every responsive reflow; and
- supported game presentation modes, including Alt+Tab and focus restoration.

See the [visual design system](visual-design-system.md) for intended geometry,
[settings and global themes](settings-and-themes.md) for author-facing styling,
and [performance](performance.md) for presentation and measurement budgets.
