# Performance monitoring

The production Performance Monitor widget uses reusable broker services, also
available to sandboxed and full-trust widget authors through
`HostServices.Performance`. It does not inspect games inside widget code.

## Controller workflow

The Application dropdown defaults to the top open application in Task Switcher's
window order, excluding WidgetRail. Choose another current window to correct it;
Automatic restores detection. This identifies an application, not a guaranteed
game title. Main view shows FPS and system CPU/GPU/RAM/VRAM. A compact pin keeps
the readings visible during play. Y refreshes; the normal host B/tray behavior
is unchanged.

FPS starts disabled. **Enable FPS** requires the separate FPS-control permission
and requests Windows approval for a dedicated collector. If the permission is
missing, the widget directs you to its Permissions page in Settings.
The main overlay never elevates.
Production uses the existing bundled .NET host (the UAC publisher may display
Microsoft .NET Host); development candidates can use the collector executable.
Declining leaves other metrics available. **Stop FPS** stops tracing. The helper
can remain idle for this app session, allowing reenablement without another UAC
prompt. No service is installed and no account-group membership is changed.

Collection pauses after five seconds without a visible or pinned consumer. All
helper ownership ends with the Bridge; loss of its authenticated heartbeat ends
the helper and its trace. Windows approval may require mouse/keyboard interaction
on the secure desktop. Accounts unable to approve elevation cannot use FPS yet.

## Measurement semantics and limits

- CPU is system busy time across processors; the first sample and the first
  sample after idle are unmeasured while a new interval is established.
- GPU is the busiest engine after aggregating the per-process Windows GPU Engine
  counters by adapter/engine. It is system usage, not selected-game usage.
- RAM is system used/total physical memory. VRAM is dedicated memory usage
  summed over adapters, not an invented capacity or a per-game allocation.
- FPS is application presents per second, calculated from PresentMon's frame
  intervals for the process's busiest swap chain. It is not a guarantee of frames
  displayed, generated-frame rate, or game-engine simulation ticks. Multiple
  swap chains are not added together.
- Unknown metrics are null and display **—**, never zero. A game that is idle,
  minimized, protected or unsupported may produce no frames. Values expire.
- Hardware sampling is cached for one second across consumers. The UI requests
  one snapshot per second while active. FPS aggregation retains at most 256
  streams, 1024 samples per stream, and a two-second interval window; stale
  streams expire. No frame trace, application name CSV or input events are saved.

The bundled Intel PresentMon console is pinned at 2.6.0 with a SHA256 lock in
`eng/runtime-dependencies.json`. Publication downloads and verifies it; runtime
verifies it again before execution and retains a read lock. Its MIT license ships
beside it. Each trace has a unique WidgetRail name and a 120-second hard recording
window. Cleanup stops only that exact session; another tool's PresentMon session
is never stopped. GPU/input/display-detail tracking is disabled in this FPS path.

## SDK contract

Declare `system.performance.read.v1` to call:

```csharp
var metrics = await HostServices.Performance.GetAsync(cancellationToken: token);
if (metrics.Targets.FirstOrDefault() is { } target)
    metrics = await HostServices.Performance.GetAsync(target.Id, token);
```

Target IDs are temporary, per broker session, and valid only for this service.
They do not expose native window handles or process IDs. `Targets` contains up to
64 windows. A vanished target must be corrected or returned to Automatic.

Declare the optional `system.performance.fps.control.v1` permission for
`EnableFpsAsync` and `DisableFpsAsync`. Invoke these only in response to an explicit
interactive action; reads never request Windows approval. The broker gates
control separately from performance/window-title reads. Apps without that
permission can still read existing authorized FPS collection and other metrics.

`WidgetPerformanceSnapshot` reports sample UTC time, nullable metrics, the FPS
state, selected target ID and current targets. Handle missing or declined
collection without disabling the rest of the widget. SDK validation rejects
nonfinite/out-of-range metrics, duplicate targets, and inconsistent FPS states.

## Verification status

Automated checks cover broker consent/lifecycle and target scoping, SDK response
validation, widget/pin structure, focus preservation, stopping hidden polling,
bounded CSV aggregation, native sample warm-up, and private helper authentication.
An actual unelevated helper probe confirmed idle-without-demand, explicit
permission-required status, and exit after owner disconnection.

On the development PC, a 10.58-second warm hardware-sampling probe consumed
31.25 ms of CPU time (about 0.30% of one core). This is a short idle-system probe,
not a game-load or FPS-collector performance claim. Live elevated game FPS,
DirectX/Vulkan/OpenGL compatibility, controller/UAC behavior, and end-to-end
pinned readability remain physical qualification items.
