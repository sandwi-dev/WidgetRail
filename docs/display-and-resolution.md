# Display and resolution behavior

Status: responsive per-monitor placement and layout contract implemented; real
mixed-monitor hardware and visual-regression evidence remains open

The overlay uses a Per-Monitor-V2-aware native window and host-rendered widget
UI. Widget authors work in logical device-independent pixels (DIPs); they do
not position an HWND, select a monitor, or compensate for Windows DPI.

## Monitor ownership

When the overlay opens, the host targets the nearest monitor containing the
active external foreground window. The overlay panel is bottom-centered inside
that monitor's current work area, while the dimming backdrop covers that
monitor's full bounds.

While visible, an external foreground change retargets both windows. Events
from the overlay or its backdrop, repeated foreground events, invalid HWNDs,
and destroyed targets cannot make the overlay target itself. If the remembered
target disappears, placement safely falls back to the host window until a new
external target is observed.

The current product intentionally owns one selected monitor. It does not dim
every monitor, draw over secure desktop/UAC, or promise visibility over true
Fullscreen Exclusive presentation.

## DPI, work-area, and topology changes

The host re-reads the target monitor, work area, and effective DPI for every
placement. Effective DPI comes from the resolved `HMONITOR`, not the external
foreground HWND, so a DPI-unaware game cannot force a false 96-DPI result. The
host responds without a polling loop:

- `WM_DPICHANGED` recomputes host-owned placement instead of accepting a stale
  fixed-size rectangle;
- `WM_DISPLAYCHANGE` recreates display-dependent graphics resources;
- `WM_SETTINGCHANGE` recomputes work-area placement, including taskbar changes;
- `WM_SIZE` recreates the render target atomically so viewport-relative styles
  use the new client extent; and
- visible foreground changes move the overlay and backdrop to the new target
  monitor.

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
- stable focus IDs and explicit focus neighbors where reflow makes geometry
  ambiguous.

Do not assume 1920×1080 physical pixels, 96 DPI, 16:9, a fixed widget width,
positive desktop coordinates, or a particular taskbar position. Widgets never
read monitor APIs themselves. They render the snapshot requested by the host
for the current viewport.

Focusable controls clipped out of the rendered viewport are not navigation
candidates. When catalog replacement changes a widget runtime, the host clears
that widget's remembered focus rather than restoring an ID from a different
surface generation.

## Deterministic evidence

The native Release suite currently proves these policy/math seams:

- placement across 1280×720, 1920×1040, 3840×2120, portrait 1080×1880,
  negative-coordinate 3440-wide, and offset 5120-wide work areas at 100%,
  125%, 150%, and 200% DPI;
- bounded behavior for 640×360, 1×1, 7×3, an extreme two-billion-pixel-wide
  extent, and oversized margins;
- render-metric reconstruction for 1×1, 853×479, 3440×1440, and 7680×4320
  clients across 72–480 DPI and interface scales 0.85, 1, 1.125, and 1.5;
- contained shell/widget geometry for tiny, portrait, short-wide, normal, and
  4K logical viewports;
- declarative compact/clipping behavior for constrained viewports, including
  portrait cases and non-integer physical-pixel scale; and
- foreground self-ignore, invalid-target fallback, Alt+Tab retargeting,
  duplicate suppression, and DPI-placement reentrancy coalescing.

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
