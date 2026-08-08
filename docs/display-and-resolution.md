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
- `WM_SIZE` recreates the render target atomically so viewport-relative styles
  use the new client extent; and
- a valid external foreground change closes the overlay; the next open resolves
  the new target monitor.

These notifications do no work while hidden; the next open resolves fresh
state. While visible they share one pure refresh policy and one authoritative
placement pass, avoiding a partial old-DPI/old-work-area frame.

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

The shell computes its panel, widget viewport, and tray from the current
logical extent. Geometry remains finite, non-negative, and contained even for
tiny or portrait inputs. At pathological sizes, content may clip or collapse
because no useful space exists; containment is a safety guarantee, not a claim
that a 1×1 display is usable.

## Widget author contract

Use the public declarative layout and GBSS primitives:

- flex growth/shrink, minimums, maximums, and intrinsic measurement;
- bounded logical lengths, percentages, `vw`, and `vh`;
- explicit line limits and ellipsis for bounded labels;
- `overflow: clip` for surfaces that must not escape their viewport; and
- semantic `UI.VerticalScroll` / `UI.HorizontalScroll` for controller content
  that can exceed the clamped viewport; and
- stable focus IDs and explicit focus neighbors where reflow makes geometry
  ambiguous.

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
It is never a fixed-size request. The shell clamps the resolved surface against
the selected monitor and its own chrome after applying interface/accessibility
scale; declarative layout and Scroll then handle the final viewport.

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

Snapshots without `Surface` retain the package-API-1 compatibility canvas
(1180×700 shell with an 880-DIP panel). A worker-starting placeholder is
host-owned and compact; it can resize once when the first authoritative
snapshot arrives. Unknown, partial, non-finite, and out-of-range native hint
data cannot escape the sizing policy and falls back to bounded mode defaults.

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
- 108,545 placement checks across dense logical boundaries for
  panel, widget viewport, adaptive footer, and persistent tray geometry;
- declarative compact/clipping behavior for constrained viewports, including
  portrait cases and non-integer physical-pixel scale;
- deterministic controller-focus recovery when resize/reflow clips the
  preferred control, with hidden controls excluded from explicit navigation
  and action dispatch; and
- targeting checks covering foreground self-ignore, invalid-target fallback,
  Alt+Tab close policy, duplicate suppression, DPI-placement reentrancy
  coalescing, and visible-versus-hidden DPI/topology/settings refresh policy.

Run the native contract suite with:

```powershell
.\src\OverlayHost\build.ps1 -Configuration Release -SkipPackaging
```

The complete repository gate also packages the runtime and runs the hidden
startup smoke:

```powershell
.\scripts\Verify.ps1 -Configuration Release
```

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
