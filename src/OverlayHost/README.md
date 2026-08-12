# Native overlay host spike

This directory contains a Windows SDK-only C++20 feasibility shell. It starts
hidden by default, registers a GameInput Guide-button callback, and does no
controller polling or rendering while hidden.

Build and run tests from PowerShell:

```powershell
.\build.ps1 -Configuration Debug
```

Run `out\Debug\OverlayHost.exe --show` to display it immediately. Without
`--show`, press Guide on a supported controller or F1 (developer fallback).

Visible controls:

- Dashboard D-pad or horizontal left stick: select a dashboard card
- Open-widget D-pad or two-dimensional left stick: move focus
- A: open a card or confirm reorder mode
- B: return from the sample widget; it never closes the overlay
- Tray Y: tap to enter/leave reorder mode; hold for 700 ms to refresh the
  selected worker widget once
- X, LB, RB, triggers, or stick clicks: run a selected card's declared quick action, when present
- Guide: show/hide from any state
- Keyboard fallback: arrows navigate, Enter selects, Escape goes Back; F1
  toggles and F5 refreshes the current tray/open widget through the same path

The widget order and last activated widget are atomically persisted under
`%LOCALAPPDATA%\GameBarAlternative`. B returning from the placeholder widget is
the widget's own sample action, not a host-reserved binding. Normal controller
buttons use a visibility-scoped GameInput read lease while the overlay is open.
Confirmed foreground ownership adds GameInput exclusivity; denied activation
keeps navigation working through diagnosed background-shared GameInput. If
GameInput is unavailable, the compatibility path uses non-exclusive XInput. Stick
navigation uses engage/release hysteresis and repeat timing; explicit focus
neighbors fall back to deterministic geometry when no usable target is
declared. A tray Y hold shows bounded progress and is canceled by any accepted
shell transition, focus loss, overlay hide, or controller loss. A winning hold
consumes the eventual Y release, so it neither toggles reorder nor reaches
widget code.

GameInput is the primary Guide path. For Xbox-360-class drivers observed to omit
Guide callbacks, the host also contains a quarantined compatibility adapter for
the undocumented `xinput1_4.dll` ordinal-100 extended-state call. It polls only
for Guide and is rising-edge/dedup guarded. This fallback is not a supported
Microsoft API and must not be treated as universal support for every 8BitDo
model, firmware, mode, or transport. GameInput exclusivity does not suppress a
game reading XInput, Raw Input, HID, Steam Input, or a remapped virtual device;
universal containment would require an optional interception/virtual-controller
layer that this prototype does not install.
The full policy is documented in
[`docs/controller-input.md`](../../docs/controller-input.md).

## Widget lifecycle mapping

The host publishes one stable lifecycle state for the tracked bridge widget:

- selected dashboard card: `Visible`;
- opened widget surface: `Interactive`; and
- hidden overlay, selection change, or another surface: `Background`.

`Created` and `Destroying` are owned by the managed runtime and are not valid
native requests. Background does not itself unload a worker. The managed bridge
enforces the validated manifest residency policy: keep-alive by default,
cooperative suspend-when-hidden, or explicit bounded unload-after-idle. The
native host never applies an idle/resource heuristic and never suspends worker
threads; it restores focus by stable ID when a lazily recreated snapshot arrives.

The local performance harness has a separate opt-in startup contract. Supplying
all four `--performance-state`, `--performance-widget-id`,
`--performance-diagnostics-path`, and `--performance-diagnostics-nonce`
arguments replaces only this process's overlay presentation state with an
ephemeral one, establishes the requested `hidden`, `visible`, or `interactive`
lifecycle for an already-installed widget, and suppresses persistence writes.
The harness resets native counters after warmup through the exact spawned HWND;
graceful close atomically creates (never replaces) a bounded nonce-bound record
with timer, paint, and successful Direct2D-frame counts. Partial argument sets,
invalid IDs/nonces/states, missing widgets, lifecycle failure, an existing
destination, and combinations with development readiness fail closed. This is
local evidence tooling, not a widget API, automation backdoor, ETW trace, or
presentation telemetry surface.

## Window contract

While visible, the prototype creates two non-injecting DWM windows on the
monitor that contained the previously foreground app:

- a uniform black backdrop at alpha 164/255 covering that monitor; and
- the controller panel above it.

Both are tool windows and are promoted to topmost while visible. The backdrop
does not activate. The panel asks Windows for foreground/focus once when shown;
the visible GameInput lease does not depend on activation succeeding. Alt+Tab or another
valid external foreground activation closes the overlay instead of following
the new app. On hide, both are removed from the topmost band and the host
attempts to restore the prior foreground window.

A left-button release on the backdrop closes the overlay. A left click on an
icon-tray item selects/enters that widget. When the complete catalog does not
fit, stable previous/next controls select the adjacent off-page widget without
entering its content; D-pad/keyboard Left and Right continue through every
widget in catalog order. UI Automation exposes the same visible window plus
the overflow controls and each tile's full-catalog position. A left click on a
visible declarative Button or Slider moves focus to its stable ID and invokes
its A/select behavior when enabled. Pointer hit testing uses the renderer's clipped active-scope
geometry, so it cannot activate hidden or offscreen controls. The UI remains
controller-first and no raw pointer event crosses into widget code.

When Left/Right changes the selected widget from the tray, the outgoing
snapshot may remain visible while a cold destination starts, but it is rendered
without a widget focus ring and exposes no widget UIA descendants. Input and
semantic focus transfer immediately to the selected tray item; the destination
enters widget focus only after an explicit Up/A or equivalent activation.

This is best-effort normal windowing, not injection or a render hook. It is
intended for desktop, windowed games, borderless games, and games using
Fullscreen Optimizations. It is not guaranteed above true Fullscreen Exclusive,
the secure desktop/UAC, higher-integrity foreground windows, or anti-cheat and
exclusive-render paths. Windows may also deny foreground activation. The
backdrop covers one selected monitor, not every monitor.
