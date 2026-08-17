# Performance contract and evidence

Status: low-overhead architecture, bounded worker controls, and reproducible
Hidden/Visible/Interactive local baselines implemented; ETW/PresentMon release
evidence, private-working-set collection, and per-widget resource UI remain open

Performance is a product feature because the overlay runs beside a game. This
page separates enforceable platform behavior, author responsibilities, current
measurements, and the evidence still required before release.

## Engineering budgets

These are validation gates, not current marketing claims:

| Scenario | Initial target |
| --- | --- |
| Hidden steady-state CPU | p95 at or below 0.1% on the reference machine |
| Hidden GPU activity | No continuous presentation or animation |
| Hidden host private working set | At or below 50 MB |
| Warm Guide-to-first-frame | p95 at or below 150 ms |
| Cold Guide-to-interactive | p95 at or below 500 ms |
| Controller-to-visual response | p95 at or below 50 ms |
| Overlay frame pacing | 60 Hz without repeated game-frame spikes |
| Default visible/interactive worker | Target at or below 64 MB; measured and user-visible |

An approximately 200 MB total is acceptable for the current feature prototype.
That iteration allowance does not excuse obvious polling, hidden presentation,
unbounded caches, or unnecessary helper processes.

## Implemented low-overhead rules

- The native window stops presenting and releases dispensable graphics
  resources while hidden.
- Primary GameInput Guide acquisition is callback-driven while hidden.
  Ordinary controller polling runs only while the overlay is visible. The
  quarantined XInput ordinal-100 compatibility path still owns a 25 ms hidden
  timer when that adapter initializes; GBA-044 tracks replacing its continuous
  cadence with a lower-wake strategy that preserves Guide-open reliability.
- Widget workers start lazily; listing the catalog, rendering dashboard
  metadata, changing themes, or reviewing permissions does not start them.
- Each worker receives a host-owned Job Object before it resumes. Trusted
  policy accounts the complete non-breakaway process tree and terminates it on
  job close. It does not impose an arbitrary private-memory or one-process cap.
- Before launch, the bridge atomically reserves against a default aggregate
  application envelope of eight worker sessions. Optional manifest memory
  guidance is diagnostic metadata, not reserved capacity.
  Capacity refusal is deterministic; `keep-alive` workers are not heuristically
  evicted. The trusted Settings control plane has one separate reported slot.
- IPC frames, snapshots, strings, images, update rates, subscriptions, and
  retries are bounded.
- Platform appearance, catalog, consent, Core Audio, and network changes use
  callbacks/watchers with coalescing rather than timer scan loops.
- The Audio Mixer and Network Controls references subscribe before their first
  read and reconcile from bounded events. YT Music limits its visible progress
  interpolation and companion reconciliation rates.
- Repeated Slider changes carry absolute targets and coalesce only a contiguous
  pending tail for the same active lifetime, input scope, node, and action.
  Discrete actions remain ordering boundaries, and deactivation cancels the
  consumer and discards pending values.
- Declarative opacity, scale, and `translate-x`/`translate-y` share one bounded
  host timeline. Stable target changes retarget rather than spawn loops;
  reduced motion cancels immediately, removed/replaced widgets discard state,
  and a settled or hidden surface schedules no animation frames. Translation
  is resolved once as shared subtree presentation geometry rather than by
  separately animating paint, focus, hit testing, and Scroll behavior.
- The host shell reuses the existing visible-controller cadence for a 140 ms
  ease-out open and 100 ms ease-in close. A new/replaced widget identity uses a
  100 ms content-opacity reveal from 0.78; same-identity snapshot refreshes do
  not flash. Reversals retarget from the presented alpha, entering widget focus
  can snap content visible, reduced motion snaps/cancels immediately, and the
  timeline owns no thread/timer or follow-up frame after settlement. Physical
  hide happens once only after the close track reaches zero.

Job memory containment is not a CPU or disk/profile quota. Installed/community
workers separately have mandatory capability-free AppContainer isolation with
ambient network denied; trusted bundled Settings remains temporarily Job-only.
YT Music uses the Community AppContainer and narrow loopback/secret broker.
Isolation does not establish publisher trust. See [security and
trust](security-and-trust.md).

## Lifecycle and background work

The five-state lifecycle controls work ownership, not automatic eviction:

- `Created` performs one-time initialization.
- `Background` cancels state-specific presentation work; explicitly authored
  widget-lifetime work may continue.
- `Visible` permits the selected dashboard card and declared local quick
  actions.
- `Interactive` permits the open surface and scoped controller actions.
- `Destroying` cancels widget-owned tokens and performs bounded cleanup.

Residency is explicit manifest policy, never a resource heuristic:

- `keep-alive` is the default. The worker stays resident after first launch,
  but presentation work still follows lifecycle tokens.
- `suspend-when-hidden` is cooperative: the worker receives `Background`, its
  visible/state tokens cancel, the bridge suppresses hidden invalidations and
  interaction, and the broker denies capabilities. Windows threads are never
  suspended with undocumented process APIs.
- `unload-after-idle` requires a manifest duration from 5 through 86,400
  seconds. The bridge caches the last validated view, sends `Destroying`, and
  tears down the process tree and companion within a bound. Visibility cancels
  a pending unload; the next visible transition lazily creates a fresh worker.

The protocol default remains `keep-alive` for compatibility, while new
controller scaffolds and the Clock sample explicitly select a five-minute
`unload-after-idle` policy. Aggregate admission accounts configured Job ceilings,
not sampled working set. Failed launch/crash, idle unload, restart retirement,
catalog removal, and shutdown return capacity. Settings diagnostics report the
current application worker/memory reservations and control-plane count. A
measured per-widget resource UI, user overrides, temporary critical-work leases,
and retained 1/8/many-widget churn evidence remain open.

Intentional unload does not consume the crash-restart budget. Catalog change,
crash recovery, user disable, and bridge shutdown remain distinct paths.

The capability broker independently rejects new operations/subscriptions in
`Background`. The only current continuation is an already-started Spotify
authorization `connect` request created by an explicit Interactive action. The
action acknowledges immediately, while the authorization task uses the widget's
Created-to-Destroying lifetime as browser foreground moves it through Visible/
Background. It is one retained lease, not permission to start inactive work:
new Background connect/control and disconnect requests remain denied. Its
temporary listener exists only during that action and waits at most fifteen
minutes. Spotify package 0.1.7 uses `keep-alive` so idle unload cannot destroy
that in-flight task while the browser owns foreground; normal active polling/
presentation still stops with lifecycle tokens. The listener tolerates at most
16 malformed or early-close local probes inside the same time bound. The exact
broker deadline is seventeen minutes, leaving two bounded
minutes for token exchange, retry/backoff, and vault persistence. Revocation,
Destroying, cancellation, or timeout still terminates it. Installed/community workers cannot bypass
the general denial with a desktop token or AppContainer network/OS
capabilities; their direct authority is limited to explicit read/execute
runtime/package grants. Trusted bundled
Job-only workers remain a temporary exception, so their background behavior
must still be treated as trusted platform code and measured separately.

## Widget author checklist

- Keep `Render()` deterministic and free of network, device, and blocking file
  work.
- Open acknowledged event subscriptions before the first snapshot when a
  fetch/subscription race is possible.
- Tie UI work to the SDK's state or shared Visible/Interactive token.
- Use `WidgetTicker` only for bounded visible interpolation; never create an
  unowned infinite timer.
- Coalesce provider bursts and call `Invalidate()` only when rendered state
  changes.
- Cancel command retries and subscriptions promptly on lifecycle transition.
- Bound history, decoded image dimensions, caches, labels, and concurrent
  operations.
- Request the smallest honest manifest memory budget. A request is not
  permission to exceed host policy.
- Test denial, cancellation, device loss, worker restart, and stale events so
  failure does not become a busy loop.

The [widget quickstart](widget-quickstart.md), [declarative UI
reference](declarative-ui.md), and first-party Audio/Network tests demonstrate
these patterns.

## Current measurement

DLV-016 establishes two complementary local baselines. The production-host
sampler measures the real host process tree in Hidden and Visible-idle states.
The native semantic harness isolates deterministic renderer/UI Automation
projection over one stable representative tree, including private resident
pages and input-to-projection latency. Neither substitutes one metric for an
unavailable one.

Production-host run
`overlay-performance-20260811-063511359-b40af482` used Release executable
SHA-256 `27494a696e7f5e9731b3fff670c335c839f5ee7f4b28d04bf0525d96f7e651f0`,
two seconds of warmup, 20 requested measurement seconds, and 21 observations
per state on a 16-logical-processor Ryzen 7 9800X3D Windows 11 machine:

| State | CPU p95 | Working set p95 | Private bytes p95 | Processes p95 | Host timer messages/s | Post-warmup D2D frames |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Hidden | 0.09737% | 102.5 MiB | 49.8 MiB | 3 | 0 | 0 |
| Visible idle | 0.09773% | 219.4 MiB | 132.4 MiB | 5 | 34.12584 | 0 |

Hidden is within the initial 0.1% diagnostic target in this observation and
owns no sampled host timer or renderer work. Visible idle retains the ordinary
controller/bridge timer but schedules no successful Direct2D frame after
warmup. Process-tree private working set remains `metric-unavailable` because
the bounded Windows provider is unreliable on this machine; working set and
private bytes are reported under their own names and are not treated as the
missing metric. The dirty-worktree provenance makes this focused implementation
evidence, not release evidence.

Native repeated run `dlv016-native-20260811T063924Z-2b5ac019` used five
independent Release processes over an exact 55-node snapshot, 48 projected
semantic nodes, 256 updates per process, 12,288 total projected nodes, 960
changed semantic nodes, and 19,588 canonical snapshot bytes:

| Metric | Minimum | Median | Maximum | Material gate |
| --- | ---: | ---: | ---: | ---: |
| Input-to-semantic-projection p95 | 0.399 ms | 0.405 ms | 0.591 ms | 50 ms |
| Hidden normalized CPU | 0% | 0% | 0.1285% | 5% |
| Visible-idle normalized CPU | 0% | 0% | 0% | 5% |
| Hidden private working set | 0.63 MiB | 0.63 MiB | 0.64 MiB | 128 MiB |
| Visible-idle private working set | 1.04 MiB | 1.04 MiB | 1.05 MiB | 128 MiB |
| Input-update private working set | 1.28 MiB | 1.28 MiB | 1.32 MiB | 128 MiB |

The synthetic workload admits at most four changed semantic nodes per input
update. Snapshot bytes use the existing 1 MiB protocol limit, and projection
uses the documented 50 ms controller-response budget. CPU and private-working-
set ceilings are deliberately broad material-regression guards, not new product
targets. QPC-pair and empty-phase overhead are recorded separately in every
sample; both rounded to zero at the reported timer/process-accounting
resolution in this run.

The native harness uses null-target layout, so it excludes paint, GPU, DWM,
presentation, and game-frame cost. Its private pages exclude bridge and worker
processes. The host sampler excludes private working set, GPU, OS scheduler
wakeups, context switches, controller hardware latency, gameplay impact,
multi-widget cost, and long-run trends. Both outputs publish these limitations
instead of inferring unavailable metrics.

DLV-011 adds one narrower incremental feasibility observation without changing
those baselines. Five fresh Release processes in
`dlv011-native-20260811T070422Z` created a real host-owned top-level tool window,
published a two-node host UI Automation tree, held it visible without a timer
for 750 ms, and performed 256 stable semantic mode projections. Incremental
private working set ranged **0.684-0.707 MiB** (median **0.684 MiB**), observed
normalized idle CPU was **0%**, and semantic projection p95 ranged
**0.0003-0.0005 ms** (median **0.0004 ms**). One of two stable semantic nodes
changed per mode update.

Those increments are below DLV-016's 128 MiB material memory and 50 ms response
gates. The DLV-011 two-node projection is intentionally much smaller than
DLV-016's 48-node workload, so the timings are not interchangeable throughput
benchmarks. The fixture's private pages are also not a production process-tree
total. See [host-owned pinned-surface feasibility](pinned-surfaces.md) for the
window-policy decision and complete evidence limits.

DLV-058's focused production-coordinator fixture adds one validated immutable
declarative snapshot, native renderer, and host semantic projection to the real
tool window. Its current Release run passed 33 lifecycle and teardown checks and
observed a 10,264,576-byte incremental private-working-set delta, below the same
128 MiB material gate. The 750 ms single-process observation does not establish
production process-tree totals, idle CPU, GPU/DWM cost, long-run behavior, or a
physical-game claim.

DLV-068 adds no hidden or idle placement timer. Its 16-check pure fixture covers
normalized mixed-DPI restore, invalid-state reset, monitor loss, declared-minimum
failure, generation rejection, constraints, cancel/commit, and atomic storage.
The 80-check real-HWND coordinator fixture includes controller focus, pointer
capture cancellation, widget/host UI Automation, move/resize, minimum-size
action bounds, exact cancel, coordinator monitor-loss reconciliation, durable
repin, Close, emergency hide, and stateless Click-through/hidden-snapshot paint
evidence; its incremental pinned private-working-set observation was 11,370,496
bytes, below the existing 128 MiB material gate.
DLV-069 adds no hidden or idle input timer. This is still a short fixture-process observation, not a
production process-tree, display-hot-plug, GPU, or long-run measurement.

## Verification and remaining tooling

Run functional/regression gates with:

```powershell
.\scripts\Verify.ps1 -Configuration Release
```

That command proves builds, bounded contracts, native tests, packaging, hidden
startup, and input-probe smoke. It is not an ETW performance benchmark.

For a repeatable, bounded local process observation, build the packaged Release
overlay and run:

```powershell
.\scripts\Measure-OverlayPerformance.ps1 -Configuration Release
```

The default performs independent Hidden, Visible, and Interactive observations
against the installed, authentication-free Settings widget. Each process gets
an explicit ephemeral startup lifecycle; performance mode neither inherits nor
writes the user's tray order, last widget, or reopen preference. It warms up for
three seconds, samples for 30 seconds, and writes one provenance-bearing run
directory under `artifacts/performance` containing schema-2 JSON, Markdown, and
one nonce-bound native record per state.

The process sampler records working set, private bytes, CPU-time deltas
normalized by logical processor count, process count, handles, and threads for
the spawned host tree. Native opt-in counters start only after warmup and report
host timer messages, controller-timer messages, paint messages, and successful
Direct2D `EndDraw` calls. Those last two are renderer-work evidence, not DWM or
game presentation evidence. The report records the executable and harness
hashes, Git state, machine/build metadata, exact parameters, optional WPR and
PresentMon availability, runtime-sidecar hashes, and every documented gap.

Sampling has finite startup, CIM, warmup, measurement, and graceful-shutdown
bounds. The harness first requests normal `WM_CLOSE`; its `finally` cleanup only
touches the spawned host and descendants whose exact PID plus creation-time
ticks it observed. It refuses to run when that exact overlay executable is
already active, preventing both file/state conflicts and contaminated results.

To run a shorter focused diagnostic:

```powershell
.\scripts\Measure-OverlayPerformance.ps1 `
    -Configuration Release `
    -Scenario Visible,Interactive `
    -WidgetId settings `
    -WarmupSeconds 2 `
    -SampleSeconds 10
```

Visible and Interactive modes deliberately display and focus the overlay. The
host-owned startup seam establishes the exact selected widget and lifecycle
before returning from initialization; the script never uses global synthetic
controller or keyboard input. Readiness additionally requires the bridge and
the exact `WidgetRail.OverlayHost` window class. Hidden establishes no
widget worker, while Visible/Interactive lazily start only the selected widget.

The report compares observations with relevant engineering targets, but labels
every comparison `diagnostic-observation` and `releaseGate: false`. A short
process sample is sensitive to machine state and creates its own external
measurement load, so the script never fails a build based on those numbers.
Nearest-rank p95 target comparisons require at least 20 observations; shorter
runs are labeled `insufficient-samples` instead of treating one scheduler tick
as a representative tail result.

This machine exposes built-in `wpr.exe` but not PresentMon. The default never
elevates, starts a machine-wide ETW session, or downloads a tool. Therefore OS
wake/context-switch and presentation evidence remain explicitly uncollected.
The previously used `Win32_PerfRawData_PerfProc_Process` provider also rejected
bounded operation timeouts on this machine. Schema 2 uses bounded process APIs
for working set and private bytes and reports private working set unavailable;
it does not silently fall back to a potentially indefinite provider call.
Validate the harness's deterministic helpers without launching the overlay via:

```powershell
.\scripts\Measure-OverlayPerformance.ps1 -SelfTest
```

Build and run only the DLV-016 native semantic target with:

```powershell
.\src\OverlayHost\build.ps1 `
    -Configuration Release `
    -SemanticChurnTestsOnly
```

Retain repeated bounded native samples, provenance, metric ranges, and the
material-regression classification with:

```powershell
.\scripts\Measure-NativeSemanticChurn.ps1 `
    -Configuration Release `
    -SampleCount 5
```

The runner gives every child sample a 15-second deadline by default and writes
five immutable sample files plus aggregate JSON and Markdown under a new
`artifacts/performance/dlv016-native-*` directory. `-SelfTest` validates its
nearest-rank range logic without building or launching native code.

The performance release gate still needs:

- an automated Windows Performance Recorder/ETW and PresentMon harness beyond
  the bounded process sampler above;
- checked-in comparable reference-machine baselines and noise envelopes;
- hidden/visible GPU, scheduler-wakeup, private-working-set, and handle trends;
- warm/cold Guide, controller-to-visual, and worker-start latency percentiles;
- one-, three-, and many-widget background/interactive scenarios;
- provider/device churn and catalog/theme reload burst measurements;
- regression thresholds that account for measurement noise; and
- a controller Settings/Performance surface showing per-widget CPU, working
  set, wakeups, crash count, and network activity.

Until those artifacts exist, do not describe the prototype as meeting the
performance budgets. The next reference widget can expose measured resource
data only through a reviewed, typed performance capability; it must not become
a private host shortcut or imply that an unimplemented capability ID is public.

### Accepted-host provenance correction (DLV-206)

The bounded host sampler launches a new isolated process profile for each
scenario. It does not measure, reuse, or stop an already-running ordinary
production-profile host. Every scenario record now retains the exact root PID
and creation time, scenario and private profile, repository commit, executable
SHA-256, dirty-worktree flag, and the PID/start/name/role identity of every
observed host, bridge, worker, and other child. The report also separates the
metrics actually available from process queries and native counters from
private-working-set, GPU/presentation, scheduler, and long-run metrics that
remain unavailable.

The eight-widget temporal route is separate evidence. Its output records its
own production-shaped scenario name, root PID and creation FILETIME, private
profile, repository commit, host SHA-256, and observed child roles. Retained
and admitted timings accept a composition record only after the corresponding
complete paint record has ended; a prior or unrelated commit cannot satisfy
the ordering check. These changes correct provenance and temporal ordering
only. They add no scenario, metric provider, threshold, or product optimization,
and numeric results are authoritative only in a retained clean-commit report.
