# Own-window physical geometry lease

Generated Electron fixture only. This is not installed in Discord and is not a
production foreign-window hosting API. It changes only geometry on a top-level
window owned by the **calling process and current thread**. It never changes
visibility, minimization/maximization, focus, topmost state, owner, parent, input,
capture policy or media. JavaScript owns the fixture's show/hide/topmost lifecycle.

## Build assumptions

- Windows x64, MSVC C++20, Node-API8 using Node22 headers/import library.
- Direct `node_api.h`; no V8, node-addon-api or Electron-private headers.
- Compile with `/MT /LD /EHsc /std:c++20`, `NAPI_VERSION=8`,
  `BUILDING_NODE_EXTENSION`, `UNICODE`, `WIN32_LEAN_AND_MEAN` and `NOMINMAX`.
- Link `node.lib`, `user32.lib`, `delayimp.lib` and `/DELAYLOAD:node.exe`.
  The companion `Build-NativeGeometry.ps1` owns dependency pinning/provenance.
- `win_delay_load_hook.cpp` independently implements the documented
  [Electron delay-load pattern](https://www.electronjs.org/docs/latest/tutorial/using-native-node-modules#a-note-about-win_delay_load_hook).
  It resolves Node imports from the current executable; no Node executable or
  external DLL is loaded. No upstream hook source was copied.

## API

```js
const geometry = require('./geometry_lease.node');
const handle = browserWindow.getNativeWindowHandle(); // exact pointer-sized Buffer
const before = geometry.read(handle);
const lease = geometry.acquire(handle);
const actual = lease.placeClient({ x: 120, y: 180, width: 800, height: 500 });
const current = lease.read();
const restored = lease.restore();
```

`read(handle)`, `lease.read()` and `placeClient()` return:

```js
{
  outer: { x, y, width, height }, // physical screen pixels
  client: { x, y, width, height }, // physical screen pixels, not window-relative
  windowDpi, visible, minimized, maximized
}
```

First successful `restore()` returns
`{ restored: true, observed: true, snapshot: <fresh snapshot> }`. Repeating it
returns `observed: false` and the cached verified restoration snapshot without
reading or mutating the released HWND. `lease.read()` and `placeClient()` reject
after release. `geometry.read(handle)` independently observes the currently valid
own-process/own-thread window after release; it is not a read of the retired lease.

The fixture also exposes `grantForegroundReturn(handle, receivingProcessId)`.
It requires the exact own-thread window to be foreground and calls the documented
AllowSetForegroundWindow for that specific process. Zero, ASFW_ANY, noninteger and
own-process targets reject. It does not activate a window or hide the client.
The authenticated fixture command supplies its host PID. A production bridge must
bind that PID to its authenticated host identity before this native boundary.
The intended sequence is grant while the client is still foreground, activate
the host and verify foreground, then hide/restore the client. Hiding first can
lose the foreground authority needed for the return.

An active lease for a destroyed window rejects `restore()` as stale, rather than
reporting success or touching a reused handle. Duplicate acquisition is rejected.

## Invariants and failure behavior

- Acquire validates top-level ownership and admits only non-minimized,
  non-maximized windows. Hidden ordinary windows are valid for the isolated probe.
- Every lease operation rechecks exact PID, thread, HWND and its unique `SetProp`
  generation marker. The property prevents duplicate active leases and protects
  against HWND reuse. Released objects do not affect a later lease.
- Bounds contain exactly four own fields with finite integer numeric values.
  Coordinates are limited to +/-1,048,576 and dimensions to1..32,768 pixels.
  Extra fields, fractional/NaN/Infinity values and malformed buffers are rejected.
  Property getters are read once; authority is checked again afterward.
- Geometry queries and mutations run under a scoped
  `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`; the prior thread context is restored.
  Client positioning measures actual outer/client screen rectangles and derives
  all four nonclient insets. No border size or DPI multiplier is guessed.
- Placement invokes `SetWindowPos` once with `SWP_NOACTIVATE | SWP_NOZORDER |
  SWP_NOOWNERZORDER`, then requires exact requested client pixels. Restore likewise
  makes one call and verifies exact original outer pixels. No retries or relaxed
  tolerance hide a disagreement. An unconfirmed operation may already have moved
  the generated window; the lease stays active for inspection/explicit restoration.
- Read/place/restore are serialized against reentrant window callbacks. Minimized
  or maximized state appearing after acquisition rejects geometry mutations.
- Explicit restoration is required. The generated Electron main process keeps
  the lease strongly reachable until normal restore, host-pipe disconnect or
  fixture shutdown. GC finalization only removes its exact live/current-thread
  marker and frees native memory; it **does not restore geometry**. Calling
  `SetWindowPos` from GC could dispatch Electron window messages and re-enter
  JavaScript during collection, so that recovery is deliberately not attempted.
  No C++ exception escapes a Node-API callback or finalizer. Pending JavaScript
  exceptions are preserved.

## Deliberate qualification limits

This lease assumes exclusive geometry ownership of a **generated fixture window**.
Explicit restore intentionally writes the original rectangle even if the fixture
changed its bounds elsewhere. It does not yet distinguish user moves, concurrent
application layout, or monitor removal. A production lease must reconcile those
events rather than overwrite unrelated geometry.

A cross-monitor DPI transition or native min-size/nonclient policy can invalidate
the single measured-inset placement. That is an explicit unconfirmed result, not
a reason to retry until a test passes. Native verification is immediate; Chromium
may change geometry later. Hidden probes must read again after the event loop
settles (and test disconnect restoration) before reporting stable bounds.

In particular, an Electron window created with `resizable: false` can keep its
original size even when `SetWindowPos` accepts its new position. The addon does
not remove that constraint: it reports `placement_unconfirmed`, retains the
active lease, and allows explicit restoration. Qualify resizable and fixed-size
windows separately; do not label fixed-size rejection as successful placement.

Read-only Node22 loading and generated Electron44 tests do not prove official
Discord compatibility, foreground restoration, accessibility/IME, visual stacking,
video continuity, or a complete host surface contract.
