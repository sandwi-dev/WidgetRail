# Performance contract and evidence

Status: low-overhead architecture, bounded worker controls, and repeatable
diagnostic process sampling implemented; ETW/PresentMon release evidence and
per-widget resource UI remain open

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
- Guide acquisition is callback-driven while hidden. Ordinary controller
  polling runs only while the overlay is visible.
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

Job memory containment is not a CPU or disk/profile quota. Installed/community
workers separately have mandatory capability-free AppContainer isolation with
network denied; trusted bundled Settings and YT Music remain temporarily Job-
only. Isolation does not establish publisher trust. See [security and
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

`keep-alive` is the current/default policy after first launch. Planned
`suspend-when-hidden` and `unload-after-idle` choices must be explicit manifest
and user policy; the host must not infer eviction from an idle timer. General
residency-policy enforcement is not implemented yet.

The capability broker independently rejects operations/subscriptions in
`Background`. Installed/community workers cannot bypass that denial with a
desktop token or AppContainer network/OS capabilities; their direct authority
is limited to explicit read/execute runtime/package grants. Trusted bundled
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

One visible prototype sample measured:

| Process | Private working set |
| --- | ---: |
| `OverlayHost` | 93.2 MB |
| `WidgetBridge` | 59.1 MB |
| One widget worker | 51.5 MB |
| **Total** | **203.8 MB** |

Across a five-second CPU sample, `OverlayHost` accumulated 78.12 ms; bridge and
worker deltas were below that sample's timer resolution. The current hidden
startup smoke also proves that the packaged host remains resident for its
1.2-second observation window without an initialization failure.

These are smoke observations, not a budget pass. They do not establish p95
latency, hidden steady state, GPU activity, wakeups, frame pacing, multi-widget
cost, or long-run memory behavior.

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

The safe default starts one hidden instance, waits for the host-resident plus
bridge-child readiness proxy, warms up for three seconds, samples for 15
seconds, and writes versioned JSON plus Markdown under
`artifacts/performance`. It measures the spawned process tree's Windows private
working set, private bytes, total working set, CPU-time deltas normalized by
logical processor count, handles, and threads. Cleanup targets only the host
and descendants whose exact PID and creation-time ticks were observed during
that run.

To include an independently launched visible-state observation:

```powershell
.\scripts\Measure-OverlayPerformance.ps1 `
    -Configuration Release `
    -Scenario Hidden,Visible `
    -WarmupSeconds 5 `
    -SampleSeconds 60
```

Visible mode deliberately displays and focuses the overlay. It does not inject
controller or keyboard input. Its readiness proxy is a resident host, observed
bridge child, and visible top-level window—not a presented first frame or proof
of interactivity. Consequently the harness cannot automate warm activation
without adding a supported host control seam.

The report compares observations with relevant engineering targets, but labels
every comparison `diagnostic-observation` and `releaseGate: false`. A short
WMI/CIM sample is sensitive to machine state and creates its own external
measurement load, so the script never fails a build based on those numbers.
Nearest-rank p95 target comparisons require at least 20 observations; shorter
runs are labeled `insufficient-samples` instead of treating one scheduler tick
as a representative tail result.
Validate the harness's deterministic helpers without launching the overlay via:

```powershell
.\scripts\Measure-OverlayPerformance.ps1 -SelfTest
```

The performance release gate still needs:

- an automated Windows Performance Recorder/ETW and PresentMon harness beyond
  the bounded process sampler above;
- stored machine/build metadata plus comparable baseline artifacts;
- hidden/visible CPU, GPU, wakeup, private-working-set, and handle trends;
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
