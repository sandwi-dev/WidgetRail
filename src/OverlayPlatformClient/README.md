# Managed overlay platform boundary

This assembly binds native OverlayPlatformInterop ABI 4 without a WinUI dependency.
It does not install drivers, opt into controller isolation, create a window,
run polling timers or dispatch widget actions. `OverlayPlatformNative` supports
the existing Windows x64 native build only. Tests use `IOverlayPlatformNative`
fakes and never initialize controller hardware.

Create one `OverlayPlatformSession` owned by the application. Supply a dispatcher
whose `TryEnqueue` queues work and returns immediately; it must never invoke the
delegate inline or wait for it. Event notifications indicate native work is
available, not that a Guide press necessarily occurred. Drain `ReadEvent` in
bounded batches on the frontend owner. Use monotonic millisecond timestamps from
the same domain as GetTickCount64, not wall-clock time or raw Stopwatch ticks.

The session serializes calls, initializes all ABI headers, and holds a SafeHandle
reference across native calls. Native callbacks copy temporary diagnostic text
and schedule frontend delivery. Wakeups coalesce, a rejected queue can be retried
with `RetryPendingNotifications`, and queued work is canceled when closing.
`CallbackFailures` reports exceptions from dispatch scheduling or consumers;
the application should report that counter rather than let callback failures go
unnoticed. Diagnostics are capped at 128 pending strings of 4096 UTF-16 characters;
this limit does not discard native controller events.

Dispose deterministically before shutting down the frontend dispatcher. Native
Destroy includes Shutdown and drains callbacks before their context is freed.
Never invoke lifecycle operations inside a custom dispatcher's synchronous
reverse-callback execution. The callback context deliberately retains consumer
delegates until shutdown; do not rely on garbage collection to close the app's
native owner, particularly when those delegates refer to the application.

The public ABI structs have native layout and integer booleans intentionally.
Create input defaults with `PlacementInput.Create()`. The returned controller
frame's `Connected`, `ForegroundExclusive`, navigation phases and RemainingFrames
have distinct meanings; the frontend must preserve those distinctions. Read one
frame per normal poll, draining bounded queued edges when RemainingFrames is set.
Polling cadence and right-stick scroll behavior remain frontend responsibilities.

`ReadNativeShortcut` exposes ABI v4's supplemental/native-only sample; it is not
the complete multi-controller View+Menu recognizer. See
`docs/maintainers/winui-platform-binding.md` for the remaining native extraction.

Build and deploy an ABI-matching OverlayPlatformInterop.dll beside the application.
The binding uses restricted application/system DLL search paths. The library
does not copy an arbitrary DLL from another checkout or fall back to stale ABI.
