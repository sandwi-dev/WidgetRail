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
  policy bounds aggregate memory to 16–256 MiB, limits the job to one active
  process, and terminates it on job close.
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

Intentional unload does not consume the crash-restart budget. Catalog change,
crash recovery, user disable, and bridge shutdown remain distinct paths.

The capability broker independently rejects new operations/subscriptions in
`Background`. The only current continuation is an already-started Spotify
authorization `connect` request created by an explicit Interactive action. The
action acknowledges immediately, while the authorization task uses the widget's
Created-to-Destroying lifetime as browser foreground moves it through Visible/
Background. It is one retained lease, not permission to start inactive work:
new Background connect/control and disconnect requests remain denied. Its
temporary listener exists only during that action and waits at most five
minutes. Spotify package 0.1.6 uses `keep-alive` so idle unload cannot destroy
that in-flight task while the browser owns foreground; normal active polling/
presentation still stops with lifecycle tokens. The listener tolerates at most
16 malformed or early-close local probes inside the same time bound. The exact
broker deadline is seven minutes, leaving two bounded
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

The schema-2 harness completed a full three-state Settings baseline in local run
`overlay-performance-20260808-201124310-6e6c053e`. It used Release executable
SHA-256 `f55c41f34bf216ecf986919476d25961d7d4e8c995a6e6f5e9306ce73489e657`,
three seconds of warmup, 30 requested seconds, and 31 process observations per
state on a 16-logical-processor Ryzen 7 9800X3D Windows 11 machine. The report
records a dirty worktree and is therefore local implementation evidence—not a
release or marketing baseline.

| State | CPU p95 | Working set p95 | Private bytes p95 | Processes p95 | Guide fallback timer | Post-warmup D2D frames |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Hidden | 0.0977% | 95.9 MiB | 42.5 MiB | 3 | 31.65 messages/s | 0 |
| Visible | 0.2921% | 203.6 MiB | 121.2 MiB | 5 | 31.89 messages/s | 0 |
| Interactive | 0.1953% | 214.3 MiB | 142.4 MiB | 5 | 31.87 messages/s | 0 |

The Hidden CPU observation is within the initial 0.1% target for this one run,
but comparisons remain non-gating. Private-working-set targets are explicitly
`metric-unavailable`; ordinary working set or private bytes are not substituted
for them.

The baseline established all three explicit host lifecycle states, produced
working-set/private-byte/CPU/process samples, published nonce-bound native
counter records, and left no host, bridge, or worker process behind. After
warmup, Hidden recorded no controller-timer messages, paints, or successful
Direct2D frames. It directly recorded 943 quarantined Guide-compatibility timer
messages over its counter interval; that
known hidden cadence remains performance follow-up rather than being mislabeled
as a scheduler-wakeup count. Stable Visible and Interactive observations also
recorded no post-warmup paints or successful Direct2D frames, while their normal
controller/bridge timer remained active.

These observations do not establish GPU activity, DWM presentation, OS
scheduler wakeups, context switches, gameplay frame impact, multi-widget cost,
or long-run memory behavior. The harness reports those fields as unavailable
instead of deriving them from CPU or paint activity.

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
the exact `GameBarAlternative.OverlayHost` window class. Hidden establishes no
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
