# WinUI controller and activation binding

Status: source-backed implementation guidance, 2026-09-27. No managed binding,
build or physical controller validation is claimed by this document.

## Boundary and ownership

Use `src/OverlayPlatformInterop/OverlayPlatformInterop.h`, ABI **4**, as the native
boundary. It owns the ordinary controller reader, backend/device selection,
Guide callback and debounce, neutral priming, repeat calculation, optional
isolation routing, and foreground-target memory. It does not own HWNDs or XAML.
No existing managed wrapper was found under `src` when this document was written.

Add one process-lifetime managed `OverlayPlatformSession` around the DLL. All
ordinary calls that change/read native state must be serialized by one owner;
the implementation has mutable state outside its callback queue mutex, so
thread-safe callbacks do **not** imply that concurrent `ReadController`,
`SetWindowState`, and shutdown calls are supported.

For the first integrated shell, serialize the binding on the WinUI dispatcher.
Use a visible-only `DispatcherQueueTimer` for ordinary controller polling,
initially matching the native host's 15 ms cadence. This is input polling, not
a rendering or animation timer. Do not turn it into synchronous widget IPC,
layout work, or repaint calls. A later dedicated input executor is possible,
but would need explicit serialized commands and bounded delivery of edges and
current analog state; it must not be added as a second consumer.

The native isolated routing thread already keeps game-facing forwarding
independent of UI painting. Preserve it. Do not add managed XInput, GameInput or
HID navigation readers alongside the native session.

## Interop contract

Use generated `LibraryImport` declarations for .NET 10. Follow the native header,
not inferred C# signatures:

- Export and callback convention is **stdcall** (`OverlayPlatformExports.h`).
  Declare it explicitly, including reverse callbacks.
- Status/enums/ABI booleans are `uint`; do not marshal them as C# `bool`.
- `uintptr_t` and HWND values are `nuint`/`nint`, not fixed-width integers.
  They are borrowed HWNDs; do not destroy a WinUI HWND in a SafeHandle finalizer.
- Native `uint64_t` time values use the existing monotonic millisecond domain
  (`GetTickCount64`), not wall-clock UTC or raw Stopwatch ticks.
- Every versioned input/output struct needs its initialized `structSize` and
  `abiVersion`. Functions inspect output headers **before** writing results, so
  pass initialized structs by `ref`, not zero-initializing `out` marshalling.
- Verify `GetAbiVersion() == 4` before creating a session. On x64, native static
  asserts specify: event 32 bytes, raw state 12, navigation event 8, controller
  frame 84, placement input 48, placement output 24, create options 32. Check
  field offsets too: controller state 20, stick navigation 60, family 80,
  callback context 8. Use normal sequential native alignment, not `Pack=1`.
- Current native build supports x64 only. Restrict the first binding/candidate
  to x64; do not advertise the template's x86/ARM64 configurations as working.

A SafeHandle may own `WidgetRailOverlayPlatformHandle*` with native `Destroy`
as its release function. Deterministic application shutdown is still required:
callbacks and context must outlive native shutdown, and finalization is not the
normal controller/driver cleanup path. Do not root the whole session forever via
its own callback GCHandle and then rely on the session's finalizer to break that
cycle. Keep the explicit process-lifetime owner in `App` and dispose it.

## Callback admission and dispatch

`Create` copies callback pointers/context. `Initialize` can issue diagnostics
and signals before it returns. Prepare/root callback state first. The event
callback carries no event payload; it only means the native queue may contain
work. Copy diagnostic UTF-16 text **inside** its callback, because the native
`wstring::c_str()` lifetime ends when that callback returns.

Callbacks may run on GameInput, DualSense or isolation threads. They must:

1. Read an atomic closing flag and return when closing.
2. Post a nonblocking `DispatcherQueue.TryEnqueue` wakeup; never synchronously
   wait for XAML, enter native lifecycle functions, or throw across the ABI.
3. Coalesce redundant wakeups without losing one: clear the scheduled flag
   before draining, so a concurrent signal can schedule the next drain.
4. In the dispatcher delegate, check the lifetime/generation again before
   accessing the native handle. Queued delegates can run after native shutdown.

The drain calls `DrainEvent` until `hasEvent == 0` (or a bounded batch followed
by another queued drain). A signal is not a synthetic Guide press. Handle actual
`GuideToggleRequested` events only, and respect the configured open shortcut.
`LegacyGuidePollingChanged` controls a separate 25 ms compatibility timer only
when Guide mode is selected. Hidden normal operation requires no navigation
polling; native Guide callbacks remain registered while hidden.

## Lifecycle sequence

### Start

1. Create the WinUI dispatcher and main HWND; retain the process activation and
   frontend instance owner so two production frontends do not contend for input.
2. Allocate callback context; verify ABI; `Create`; `Initialize`; check each
   status. Handle failure without exposing a partially initialized controller UI.
3. `SetOwnedWindows(mainHwnd, backdropHwndOrZero)` and publish initial hidden
   state. The current ABI accepts only two HWND identities. Frontend popups and
   pinned windows also need process-owned filtering before observing foreground
   targets; do not invent extra registrations the ABI does not support.
4. Start only the hidden shortcut mechanisms required by settings. Keep native
   isolation disabled unless the existing user preference explicitly enables it.

### Show

1. Observe a valid external foreground target before taking activation.
2. Call `PrepareVisible`. For isolation this neutralizes the game-facing output
   before the overlay admits local input. A failure is a real show failure.
3. Place/show the WinUI window and attempt activation once. The native host's
   `AcquireOverlayForegroundInput` uses direct activation and one bounded
   `AttachThreadInput` fallback; do not introduce a foreground-stealing loop.
4. Publish `SetWindowState(visible: 1, focused: actualForegroundOwnership)`.
   Confirm ownership using the foreground process, including WinUI popup/island
   HWNDs, not solely `Window.Activated` or the return from `SetForegroundWindow`.
5. `PrimeController(actualForegroundOwnership, now)` and start visible polling.
   A control held to open the overlay must not immediately activate a widget.

Ordinary visible input has a background-read lease when Windows denies foreground
activation. `frame.connected` and its read path determine whether a frame is
usable; do not add a blanket `foregroundConfirmed` gate that disables the existing
visible lease. `foregroundExclusive` reports an additional property, not the
same thing as connected/usable input. Some sensitive interaction policies can
still require actual foreground ownership deliberately.

### Visible events and frames

- `ReadController` produces button/trigger edges and separate D-pad/stick
  navigation events. Preserve native priming, device-family hints and repeat
  phases. Do not compute a second independent set of button edges in C#.
- Isolated input can return queued edges and `remainingFrames`; the native host
  drains at most 16 frames per pass and finishes with a current read for repeats.
  Preserve edge ordering and bounded work. Never replay queued analog deltas as
  if they were current continuous scrolling or accumulate a catch-up movement
  burst during UI stalls.
- Route semantic actions to the active scope (modal, popup, page, tray). Keep
  widget execution asynchronous and reject responses from retired contexts.
- Use WinUI `FocusManager` directional movement with `SearchRoot` set to the
  appropriate active scope. Use `FocusRequester`-like application policy only
  for stable logical targets; WinUI performs actual traversal/realization.
  `FocusManager.TryMoveFocus`/`TryMoveFocusAsync` and control-specific behavior
  must be checked against the target control; do not fall back to the old
  geometry-based engine when an unrealized item has no control yet.
- A/B and other actions should invoke shared semantic command paths used by
  pointer and keyboard, rather than global `SendInput` or arbitrary UIA calls.
  Lists, text fields, sliders and popups need their own native control semantics
  before fallback directional traversal. Suppress duplicate framework gamepad
  delivery if both WinUI and our native backend receive one physical press; prove
  exactly one action through an actual controller test.
- Right stick targets the active scroll owner via supported scrolling APIs and
  current bounded intent. It must not create layout state, manually paint or run
  a second animation clock. WinUI owns scrolling presentation.

### Hide and shutdown

Hide is not disposal. Stop admitting widget actions, stop visible polling, clear
pending analog/repeat intent, publish hidden state, hide the window, and restore
only a still-valid remembered foreground target according to existing policy.
`SetWindowState(false, false)` resets controller selection/tracker and closes the
isolation overlay lease. Guide/open-shortcut mechanisms remain active for reopen.

Shutdown stops timers and marks managed callback admission closing first. Then,
serialized against all ordinary calls, invoke native `Shutdown` and `Destroy`.
Native shutdown stops isolation, closes callback admission, stops/unregisters
GameInput callbacks, stops DualSense, waits for callbacks already executing,
clears events and releases readers. Only then free callback GCHandles/delegates.
Never call Shutdown from inside a reverse callback: it can wait for that same
callback to leave. Dispatcher delegates already queued must become no-ops.

## Important ABI gap: View + Menu

Do not assume ABI v4 contains the complete open-shortcut recognizer.
`NativeShortcutButtons` returns only a supplemental DualSense sample or the
authoritative isolated sample. The current `OverlayHost/ControllerOpenShortcut.cpp`
separately owns a GameInput device observer and enumerates XInput/GameInput sources
for the per-device chord tracker. `PollOpenShortcut` chooses NativeOnly for
isolation and AllControllers otherwise; this distinction fixed the simultaneous
Xbox/DualSense regression.

The smallest sound extraction is to expose that existing native recognizer
through a narrow versioned native shortcut service/configuration API, preserving
per-device neutral arming, chord consumption and backend distinction. Do not
reimplement it with one merged button mask in C#, or silently ship Guide-only
while claiming feature parity. A second managed reader is not necessary. The
present source contains a separate native shortcut GameInput observer, so the
README's phrase 'single production Microsoft GameInput instance' must not be
used as evidence that the entire old host currently has only one instance.

When View+Menu is selected, suppress its consumed button edges from ordinary
widget Menu/Back dispatch until the chord release policy clears them. Keep the
existing setting's mutually exclusive Guide/View+Menu behavior.

## Native build and deployment

`src/OverlayHost/build.ps1`, function `Invoke-OverlayPlatformInteropBuild`, already
defines the DLL source list, macros, architecture and links. Its existing focused
platform mode also runs isolation and parity tests, but the script performs
Taffy/artwork setup before reaching that mode. Do not make the new frontend depend
on rebuilding the retired renderer merely to acquire this DLL.

Extract a shared native-platform build entrypoint under `OverlayPlatformInterop`
and call it from both hosts while migration is active. Preserve current compiled
sources/macros, ViGEmClient source and current GameInput SDK pin `3.5.262`. Relevant linked libraries
include GameInput, user32, HID, XInput 9.1.0, bcrypt, advapi32, shell32, setupapi,
cfgmgr32 and ole32. Use a unique object/PDB output directory per configuration and
architecture; serialize shared outputs to avoid MSVC PDB conflicts.

The first proof can consume a freshly built, identified x64 DLL, but shipping
must have an explicit build graph. Copy `OverlayPlatformInterop.dll` beside the
frontend with `PreserveNewest` for output and publish; do not discover an arbitrary
old native DLL from another worktree at runtime. Restrict DLL resolution to the
application's trusted installation/output location. Record hash/build identity
alongside the managed candidate and inspect native imports so GameInput/runtime
deployment requirements are preserved. Driver installation remains a separate
explicit feature; migration must not install or change HidHide/ViGEm policy just
to test the ordinary frontend.

## Evidence required before calling this bound

- Native/managed ABI size, offsets, enum values and version rejection.
- Startup callback before Initialize returns; queued callback after close;
  dispatcher rejection; shutdown while callback is executing; repeated disposal.
- Physical Guide and View+Menu on Xbox and DualSense, including both connected.
- Held open chord, neutral priming, one action per press, hide/reopen, disconnect
  and active controller changes; no stale repeat replay after a stalled UI.
- Foreground denial versus visible input lease; popup/island focus does not close
  the overlay; Alt+Tab does; restoration never steals activation back afterward.
- Actual WinUI list/grid focus and scrolling during page arrival, reverse input,
  modal scope changes and background/presentation retention.

Source anchors: `OverlayPlatformInterop.h/.cpp`, `OverlayPlatformExports.h`,
`OverlayPlatformPolicy.cpp`, `ControllerActivitySelection.h`,
`OverlayHost/ControllerInputOwnership.h`, `ControllerOpenShortcut.cpp`,
`OverlayHost/main.cpp` (`HandlePlatformEvents`, `RefreshControllerSettings`,
`PollOpenShortcut`, `AcquireOverlayForegroundInput`, `PollController`),
`OverlayHost/build.ps1`, and `tests/OverlayPlatformInterop.Tests`.
