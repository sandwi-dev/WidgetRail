# Avalonia Overlay Prototype — AVP-002

This remains an isolated .NET 10/Avalonia 12.1.1 feasibility process, not a
production migration. AVP-002 adds one narrow physical-controller adapter and
closes the carried scale, bounds, and transition-surface evidence gaps. It does
not reference production renderer/layout/focus/accessibility code, GBSS, remote
widget surfaces, or production build scripts.

## Controller and semantic input

`Vortice.XInput` 3.8.3 supplies the polling boundary. Its snapshots enter one
`SemanticInputRouter` as Up, Down, Left, Right, Activate, or Back. Keyboard and
controller input then use the same focus, page, tray, Slider, and Button state;
there is no synthesized keyboard input or second control registry.

- Tray Left/Right immediately cycles and opens destinations with wraparound.
- Tray cycling retains the selected tray item. Down or A/Enter explicitly
  enters page content; Back returns to the selected tray item. Each page keeps
  its last valid AutomationId-backed focus with a declared initial fallback.
- Slider Left/Right adjusts the value; Up/Down leaves the Slider.
- Escape, keyboard B, and controller B work while a child control has focus.
- Enter and controller A raise the focused Avalonia Button once. Enter is
  edge-triggered until KeyUp (and rearmed on focus loss), so OS key repeat does
  not repeatedly activate a Button. A short cross-source duplicate window
  rejects co-reported activation/back events.
- Directional input applies a 28% stick dead zone, 350 ms initial repeat, and
  100 ms repeat interval. A and B are edge-triggered and do not repeat.
- Disconnect, replacement slot, reconnect, route replacement, hide, and focus
  loss reset held state. Reconnect/replacement must observe neutral before a
  newly held input is admitted.

The bounded dependency decision is recorded in
[`controller-dependency-decision.md`](controller-dependency-decision.md).
XInput is a prototype limitation: only four XInput-compatible slots, no durable
device identity, and incomplete native coverage of non-XInput controllers.

## Focused verification and evidence

From the repository root:

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Verify-Avp002.ps1 -TimeoutSeconds 180
```

Directional movement uses Avalonia 12 `FocusManager.FindNextElement`/XYFocus
with rectilinear spatial selection and bounded tray/page/ScrollViewer search
roots. A strict directional check prevents non-monotonic repeat loops. The
Audio Mixer uses a small set of explicit XYFocus links only for its genuinely
ambiguous parallel Slider/Mute columns; it does not define a full page graph.

The focused 18-test suite builds only this solution. A deterministic
`IControllerStateSource` drives the real adapter and `ControllerStateProcessor`
through the actual `MainWindow`, shared router, and focused Avalonia controls.
It covers held directional repeat, Deactivated/Activated, hide/show, route
replacement, disconnect/reconnect neutral gating, device replacement,
edge-triggered controller A and keyboard Enter, dead zone, and
no-double-dispatch. Direct regressions cover aligned Audio Slider Up/Down,
tray focus retention, explicit content entry, Back restoration, and per-page
focus re-entry. The remaining tests cover standard Avalonia UIA, ScrollViewer
focus movement, and bounded lifecycle. Its responsive matrix
sets actual Avalonia render scaling, captures Skia-backed headless pixel frames,
and verifies all four pages at three work areas × three scales. Every authored
required Button/TextBlock is enumerated by AutomationId; a fully or partially
visible bound must have nonzero contained bounds, while off-viewport
ScrollViewer children are explicitly recorded as clipped.

Transitions retain start/midpoint/completion samples of the Avalonia visual and
composition surface: transparent root, brush inspection, visual child count,
and nontransparent surface coverage. These diagnostics can detect an opaque
black brush fallback inside the Avalonia surface; they do not claim to prove
the physical Windows compositor result.

After committing the clean milestone, run the exact-commit lifecycle once:

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Measure-Avp002.ps1 -TimeoutSeconds 90
```

The ignored artifact and copied framework-dependent runtime are retained under
`experiments/AvaloniaOverlayPrototype/artifacts/avp002`.

## Visible planner/user launch

```powershell
& .\experiments\AvaloniaOverlayPrototype\artifacts\avp002\runtime-win-x64\AvaloniaOverlayPrototype.exe
```

Physical controller compatibility, controller feel, focus rings, transparency,
mixed-monitor behavior, and the physical no-black-frame verdict remain
planner/user checks. GPU timing remains unavailable without a separately
authorized ETW/PresentMon lane. The 250 MiB value remains an initial comparison
target rather than an acceptance gate; evidence also states whether memory is
below the user's roughly 500 MiB unacceptable region. Stop after AVP-002; no
AVP-003 work is included.
