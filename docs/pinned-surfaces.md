# Host-owned pinned surfaces

Status: the bounded generic lifecycle is available on the Win32 tool-window
architecture selected by DLV-011. A widget opts in with the data-only
`pinningSupported` manifest flag. The host alone creates, renders, orders,
focuses, and destroys the native surface; no HWND or native authority is
exposed to widget code.

## Decision

The credible in-boundary candidate is a peer top-level Win32 `WS_POPUP` created
and destroyed by `OverlayHost`. Its extended-style policy is host-owned:

- both modes use `WS_EX_TOOLWINDOW | WS_EX_TOPMOST`; the tool-window style
  keeps the surface out of the taskbar and Alt-Tab list, while topmost is
  applied and reasserted only by the host;
- click-through mode adds `WS_EX_NOACTIVATE | WS_EX_LAYERED |
  WS_EX_TRANSPARENT`, returns `HTTRANSPARENT` from `WM_NCHITTEST`, and returns
  `MA_NOACTIVATE` from `WM_MOUSEACTIVATE`;
- focusable mode removes `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT`, accepts the
  client hit test, and permits one explicit host focus transition;
- the pinned HWND has no owner relationship to the transient main-overlay
  HWND, so hiding or closing that overlay does not destroy the pinned surface;
  unpin destroys the HWND and clears its semantic surface together;
- host exit and process failure cannot leave a same-process HWND alive.
  Startup recovery never rehydrates native-window authority from persisted
  state; it may recover only bounded placement data after a new explicit pin.

Microsoft documents the taskbar/Alt-Tab, activation, and topmost meanings of
these [extended window styles](https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles).
Microsoft's DWM guidance says a top-level `WS_EX_TRANSPARENT` window should be
combined with `WS_EX_LAYERED` for hit testing. `HTTRANSPARENT` alone forwards
only within the same thread, so it is defense in depth rather than the claimed
cross-process mechanism; explicit mouse-activation denial completes the
candidate policy. See [DWM performance considerations](https://learn.microsoft.com/windows/win32/dwm/bestpractices-ovw)
and [`WM_NCHITTEST`](https://learn.microsoft.com/windows/win32/inputdev/wm-nchittest).

The product coordinator admits only the current catalog ID, package instance,
runtime generation, presentation generation, display name, opt-in flag, and a
validated immutable declarative snapshot. It cannot admit a native window,
renderer, process, provider, compositor, or z-order handle. It renders that
snapshot with the existing native declarative renderer and publishes separate
host-owned heading and mode status semantics through the existing UI Automation
provider.

## User lifecycle

Only one surface may be pinned in this first bounded release:

1. focus a supporting widget in the tray and press controller Menu/Options, or
   right-click that exact tray item, then choose the host-owned **Pin** menu
   item. UI Automation invokes the same typed menu action. The `P` keyboard
   fallback remains available while the widget is open. The new peer surface
   starts in nonactivating click-through mode and the main overlay keeps
   controller focus;
2. while that widget remains open in the main overlay, press `P` or controller
   right-stick click to enter the pinned surface. D-pad/stick navigation and
   `A` then use the exact current widget generation; `B` or another right-stick
   click returns focus to the overlay and restores Click-through;
3. reopen the selected tray item's menu and choose **Unpin**, use the `U`
   keyboard fallback, use the pinned Interactive chrome, close the pinned
   window, remove or replace its package generation, restart its worker, or
   exit the host to perform one exact paired HWND/semantic teardown;
4. closing the main overlay preserves the current declarative content and keeps
   repainting accepted snapshot replacements, but always returns the surface to
   click-through. Reopen the main overlay before explicitly restoring
   Interactive mode. Click-through never covers the widget with substitute or
   placeholder content.

While the pin owns controller focus, `X` closes it. `LB`+`RB`+`X` is the
host-owned emergency action: it unpins the bounded surface and releases input
without consulting widget code. `Ctrl`+`Shift`+`H` provides the same emergency
path from the visible overlay. Guide closes the overlay through its existing
global authority, which cancels placement/focus and leaves every surviving pin
nonactivating and click-through. No hidden-overlay controller input is forwarded.

Interactive placement uses one host state machine across input routes. The
current pin's tray menu exposes **Adjust pinned widget**: D-pad or left stick
moves, right stick resizes, `A` commits, and `B` restores the exact pre-gesture
rectangle. The pinned surface shows the live dimensions and control legend.
Keyboard uses `M`/`R`, arrows, Enter, and Escape.
Dragging the host-owned Move or Resize chrome commits on pointer release, and
UI Automation exposes the same Move/Resize then Commit/Cancel actions. Closing
the main overlay or losing input capture cancels an unfinished gesture.

Interactive UI Automation composes the exact current widget semantic tree after
the ordered host Enter/Exit, Move, Resize, Click-through, Unpin, Close, and
Emergency actions. Every queued widget Invoke/RangeValue request is revalidated
against widget ID, runtime generation, snapshot sequence, active scope, enabled
state, and focused element before bridge dispatch. Click-through publishes only
the noninteractive host heading/status, so hidden controls and stale actions are
not discoverable. Safe action failures publish one bounded assertive live status.
High contrast uses Windows system colors; reduced-motion rendering remains
immediate with no new ambient animation or timer.

The tray menu and accessibility action report Not pinned, Pinned
Click-through, Pinned Interactive, still loading, unsupported, or the
one-surface capacity state from current host authority. A complete Menu action
is consumed only while tray focus owns input; Menu in widget content and every
package-declared bumper, stick-click, trigger, Menu, or View action remain
package input. Unsupported widgets retain their existing behavior. Omitted
`pinningSupported` is exactly `false`, a wrong JSON type fails manifest parsing,
and admission failures produce bounded host diagnostics rather than a fallback
window. Worker loss, stale runtime or presentation generations, catalog
removal, and host exit cannot leave an orphaned surface.

## Candidate comparison

| Candidate | What it establishes | Boundary/result |
| --- | --- | --- |
| Host-owned Win32 tool window | Uses the current HWND process, Win32 styles, existing UI Automation provider, and current monitor/DPI primitives. Explicit focusable/click-through modes and independent overlay-hide lifetime are directly testable. | **Selected for bounded follow-up.** No new runtime, dependency, compositor, public protocol, or widget window authority. |
| Windows App SDK `AppWindow` with `CompactOverlayPresenter` | Supplies a system compact-overlay presenter for an always-on-top picture-in-picture-like window; it can wrap a Win32 top-level HWND. | **Not adopted.** The API belongs to Windows App SDK, which is not a current dependency and crosses DLV-011's explicit stop boundary. It would also require a deployment/runtime and lifecycle decision before comparable evidence could be produced. |
| UWP compact overlay | Supplies an application-view presentation mode inside the UWP application model. | **Rejected for this Win32 host.** Moving the host into that application/window model would be a material architecture change, not a smaller feasibility candidate. |

The supported `AppWindow` comparison is based on Microsoft's
[window-management documentation](https://learn.microsoft.com/windows/apps/develop/ui/manage-app-windows),
which describes the one-to-one top-level-HWND mapping, Win32 interoperability,
and `CompactOverlayPresenter`. Windows App SDK is delivered separately and has
its own deployment/runtime contract; adopting it requires planner authority.

## Placement and display lifecycle

Persisted placement is data, not window authority: schema version, monitor
stable ID, normalized work-area X/Y anchors, and logical width/height in DIPs.
The host atomically replaces
`%LOCALAPPDATA%\WidgetRail\pinned-surface-placement.ini`; at most 64
bounded widget records are accepted. Resolution follows one deterministic rule:

1. use the recorded monitor only when its work area and effective DPI are
   valid;
2. otherwise use the valid primary monitor, or the first valid monitor;
3. scale logical size at the selected effective DPI, with the generic 240x135
   through 960x540-DIP limits (a trusted host surface may inject a stricter
   minimum; widgets cannot);
4. resolve normalized anchors over the remaining work-area travel and fully
   clamp the rectangle; malformed/incompatible state resets as a whole;
5. monitor loss falls back to the valid primary/first monitor, re-normalizes,
   and atomically records that fallback; a first/default pin uses 480x270 DIPs
   at the top-right with a 16-DIP safe margin;
6. if no work area can contain the declared minimum, fail closed rather than
   creating an offscreen or undersized HWND.

The rule covers work-area shrink, taskbar movement, rotation, mixed DPI,
negative virtual-screen coordinates, hot-plug fallback, interrupted gestures,
and rapid display notifications through the same resolver. `WM_DPICHANGED`,
`WM_DISPLAYCHANGE`, and work-area changes cancel any preview and reapply the
last committed logical placement. Runtime and presentation generations are
captured when a gesture begins; a stale generation cannot write placement.
No placement timer runs while hidden or idle. Windows
documents that `WM_DPICHANGED` supplies a new DPI and suggested rectangle, and
that monitor APIs resolve rectangles in virtual-screen coordinates:
[WM_DPICHANGED](https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged),
[MonitorFromRect](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-monitorfromrect).

## Focused evidence

`src\OverlayHost\build.ps1 -Configuration Release
-PinnedSurfaceTestsOnly` builds and runs only `PinnedSurfaceHostTests.exe`.
The fixture asserts policy, placement, focus, hit testing, styles, visible
lifecycle, teardown, and actual UI Automation publication. It then records one
750 ms idle observation and 256 stable two-node semantic projections.

Focused five-process run `dlv011-native-20260811T070422Z` produced:

| Metric | Minimum | Median | Maximum | DLV-016 comparison |
| --- | ---: | ---: | ---: | --- |
| Incremental private working set | 0.684 MiB | 0.684 MiB | 0.707 MiB | Below DLV-016's 128 MiB material gate; DLV-016 visible-idle synthetic total was 1.04-1.05 MiB. |
| Normalized idle CPU | 0% | 0% | 0% | Same observed result as DLV-016 visible-idle at process-time resolution. |
| Host semantic projection p95 | 0.0003 ms | 0.0004 ms | 0.0005 ms | Below the existing 50 ms response gate; DLV-016's larger 48-node projection measured 0.399-0.591 ms. |
| Stable/changed semantic nodes | 2 / 1 per update | 2 / 1 | 2 / 1 | Smaller feasibility surface; not presented as a throughput improvement over DLV-016. |

The private-working-set value is the incremental cost inside the small fixture
process, not the production process-tree total. The two-node semantic workload
is deliberately smaller than DLV-016, so only gates and direction are compared;
the timings are not interchangeable product benchmarks.

`src\OverlayHost\build.ps1 -Configuration Release
-WidgetSurfaceTestsOnly` builds the production coordinator with its focused
real-HWND fixture. After DLV-073 it passes 80 admission, closed-state, style,
real-window, controller focus, pointer capture, UI Automation composition,
generation, live-update, placement/cancel/commit, minimum-size action bounds,
coordinator monitor-loss reconciliation, durable-repin, overlay-hide, Close,
emergency-hide, cap, and exact-teardown checks. The deterministic production
paint trace proves the same sentinel render is present in Interactive and
Click-through and that a hidden-overlay snapshot replacement advances without
a placeholder while actions remain inert. The pure host policy fixture passes
308 checks. The current incremental pinned private-working-set observation is
11,370,496 bytes, below
DLV-016's 128 MiB material gate.

`src\OverlayHost\build.ps1 -Configuration Release
-PinnedPlacementTestsOnly` runs the 16-check pure normalized placement,
mixed-DPI, monitor-loss, generation, constraint, minimum, and atomic-persistence
fixture. Neither short single-process fixture is a production process-tree,
GPU, idle-CPU, physical-display, or long-run benchmark.

### Coordinator responsibility disposition

Before and after DLV-073, `WidgetSurfaceCoordinator.cpp` plus its header contain
1,416 physical lines (1,203 + 213 before; 1,188 + 228 after). The same sole
coordinator still owns admission/generation, the one HWND and mode, render/focus
composition, bounded input requests, placement delegation, UIA projection, and
exact teardown. Its coordination state remains one admission, one policy, one
renderer result, one focus identity, one bounded request queue, and one optional
placement session; mutable dependencies remain the existing factories, renderer,
image cache, placement store, UIA provider, and host notification window. The
paint evidence seam is test-only and stateless: it reads the existing admitted
sequence and renderer result and introduces no lifecycle, renderer, or focus
owner. Retaining the cohesive owner is therefore bounded for this correction;
any broader decomposition remains separate planner-authorized work.

## Explicit limitations

- This is a generic declarative host surface, not an arbitrary-window or public
  native-window API. The only public opt-in is `pinningSupported`.
- The fixed-video/WebView2 trust and resource feasibility gate remains DLV-062;
  generic pinning grants no browser, media, provider, or native-object authority.
- No WebView2, YouTube, authentication, playback, new compositor, Windows App
  SDK dependency, game hook, or elevated hook was implemented.
- No screenshot, GPU, DWM, presentation, game-frame, hardware-input, or
  exclusive-fullscreen evidence was collected.
- Style/hit-test assertions and deterministic display math do not prove
  compatibility with a physical borderless game, secure desktop, elevated
  windows, anti-cheat, HDR, or exclusive fullscreen. Those claims remain
  explicitly unverified.
