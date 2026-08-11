# Host-owned pinned surfaces

Status: DLV-058 implements the first bounded generic lifecycle on the Win32
tool-window architecture selected by DLV-011. A widget opts in with the
data-only `pinningSupported` manifest flag. The host alone creates, renders,
orders, focuses, and destroys the native surface; no HWND or native authority
is exposed to widget code.

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

1. open a supporting widget and press `P`; the new peer surface starts in
   nonactivating click-through mode and the main overlay keeps controller focus;
2. while that widget remains open in the main overlay, press `P` again to toggle
   the pinned surface between Interactive and Click-through;
3. press `U`, use the pinned Interactive chrome, close the pinned window, remove
   or replace its package generation, restart its worker, or exit the host to
   perform one exact paired HWND/semantic teardown;
4. closing the main overlay preserves the surface but always returns it to
   click-through. Reopen the main overlay before explicitly restoring
   Interactive mode.

Unsupported widgets retain their existing behavior. Omitted
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

Persisted placement is data, not window authority: monitor stable ID plus
left/top/width/height in DIPs. Resolution follows one deterministic rule:

1. use the recorded monitor only when its work area and effective DPI are
   valid;
2. otherwise use the valid primary monitor, or the first valid monitor;
3. scale DIPs at the selected monitor DPI, clamp size to 240x135 through
   960x540 DIPs and then to the physical work area;
4. fully clamp the rectangle to the work area; malformed values, monitor loss,
   or an absent monitor ID use the 480x270-DIP top-right default with a 16-DIP
   margin;
5. when no valid monitor exists, fail closed and create no HWND.

The rule covers work-area shrink, taskbar movement, rotation, mixed DPI,
negative virtual-screen coordinates, and hot-plug fallback as math. A pinned
HWND currently applies the bounded top-right default and its own
`WM_DPICHANGED` suggested rectangle. Durable move/resize persistence and
complete display-environment reconciliation remain DLV-068. Windows
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
real-HWND fixture. The current DLV-058 Release run passed 33 admission,
closed-state, style, real-window, UI Automation, generation, live-update,
overlay-hide, cap, and exact-teardown checks. Its incremental pinned private
working-set observation was 10,264,576 bytes, below DLV-016's 128 MiB material
gate. This short single-process observation is not a production process-tree,
GPU, idle-CPU, or long-run benchmark.

## Explicit limitations

- This is a generic declarative host surface, not an arbitrary-window or public
  native-window API. The only public opt-in is `pinningSupported`.
- Durable placement, controller action routing, and final composed widget/UIA
  interaction are intentionally owned by DLV-068 and DLV-069.
- No WebView2, YouTube, authentication, playback, new compositor, Windows App
  SDK dependency, game hook, or elevated hook was implemented.
- No screenshot, GPU, DWM, presentation, game-frame, hardware-input, or
  exclusive-fullscreen evidence was collected.
- Style/hit-test assertions and deterministic display math do not prove
  compatibility with a physical borderless game, secure desktop, elevated
  windows, anti-cheat, HDR, or exclusive fullscreen. Those claims remain
  explicitly unverified.
