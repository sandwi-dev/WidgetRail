# OverlayPlatformInterop

`OverlayPlatformInterop` is the version-1 native presentation-platform boundary.
It owns the single production Microsoft GameInput instance, supported Guide
callback, quarantined legacy Guide adapter, controller device/sample lifecycle,
repeat and neutral priming, Guide debounce, foreground-target memory, and safe
work-area placement entrypoint.

The boundary never creates a window, renderer, focus tree, widget transport, or
managed input reader. Its caller supplies the one presentation HWND lifecycle,
marshals event-availability callbacks onto that window's UI thread, and retains
shell action and semantic focus policy. All ABI structures carry `structSize`
and `abiVersion`; callers must require `WRAIL_OVERLAY_PLATFORM_ABI_VERSION`.
The version-1 managed ABI uses only fixed-width `uint32_t` scalars for Boolean
fields, parameters, return values, and out values (`0` is false; `1` is true),
and the public header asserts every managed-facing structure size and critical
offset. Focused verification links through the generated import library and
loads the built DLL rather than compiling a private copy of its implementation.

Shutdown is idempotent. It stops and unregisters both GameInput callbacks,
atomically closes callback admission and accounts entrants under one mutex,
waits for any callback already in flight to leave the opaque owner, clears
queued events, and only then marks shutdown complete and permits destroy to
release the owner. Concurrent shutdown callers wait for that completed state.

The legacy XInput Guide ordinal remains implemented only by
`OverlayHost/GuideInputCompatibility.*` and is consumed privately by this
boundary. It is not part of the public ABI.

## Dormant controller-isolation worker

The separately built controller-isolation worker remains disconnected from the
live OverlayHost ABI. Its versioned private protocol requires an explicit
`PrepareSession` command carrying exact selected-device enrollment and routing
authority before it creates a GameInput owner or ViGEm target. `Hello` and
`Heartbeat` never activate either backend. Preparation revalidates the exact
GameInput device ID, root ID, container ID, normalized PnP-path digest,
vendor/product identity, connected gamepad capability, and non-ViGEm ancestry;
unknown ancestry fails closed. Only then may the worker create its one owned
virtual target and delegate neutral/barrier behavior to
`ControllerIsolationCore`.

Physical ancestry is accepted only when Configuration Manager resolves the
interface's exact device-instance property and the walk reaches the actual
device-tree root returned by `CM_Locate_DevNode(nullptr)`. Missing, malformed,
cyclic, over-depth, or partially failed ancestry is unknown and rejected; an
arbitrary `CM_Get_Parent` error is never interpreted as a physical root.

GameInput reading, device, and Guide callbacks publish fixed records into one
preallocated bounded multi-producer queue. The worker thread is the sole
consumer and the sole caller of the routing core and virtual output. Queue
pressure, conflicting source timestamps, disconnect, Guide-sink pressure,
authority mismatch, and lease expiry retire the owned target rather than drop
or reorder transitions. Guide edges remain passive ordered signals and never
enter the gamepad report.

Each routing cycle snapshots a finite reserved-tail barrier, so continuous
callback traffic cannot monopolize control work. A producer that has reserved
but not published a cell yields `Waiting`; only the existing queue-age budget
and a final acquire check can classify it as stuck. Current-reading samples are
taken only while output is contained. Their exact timestamp/state forms a
fence that retires delayed pre-fence reading callbacks while preserving every
post-fence edge; disconnect and Guide signals are never suppressed. Worker
control replies remain correlated until the requested transition is actually
applied: Enter waits for its pre-barrier drain and neutral submission, while
Commit/Close wait until the bounded neutral dwell has restored Playing.

This is compile- and fake-test-only foundation. The present host-owned guardian
job topology cannot keep a healthy Playing route alive across OverlayHost
death, and therefore this path must not be enabled in the product. Guardian
independence, a reconnect endpoint, atomic handoff from the existing live
GameInput/Guide owner, HidHide application, and physical XInput containment are
separate required integration work. Until that work is accepted, the existing
OverlayPlatformInterop reader remains the only live controller owner.
