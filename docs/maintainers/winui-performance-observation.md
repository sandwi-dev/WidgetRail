# Observe WinUI process resources

Use actual widget workloads and record the build, profile, display scale, motion
settings, foreground state and cache state. A native fixture pass or a successful
UIA command does not prove smooth displayed motion.

`scripts/Measure-WinUiProcessResources.ps1` observes an already-running frontend
and its descendants. It neither launches nor closes anything and does not alter
focus, settings or widgets. Run it beside an independent browsing/switching replay:

```powershell
./scripts/Measure-WinUiProcessResources.ps1 -AppPid <owned-process-id> `
    -Scenario 'warm Playnite and music switching; Debug; 125 percent' `
    -SampleSeconds 30 -OutputPath artifacts/winui-performance/run-01.json
```

The observer records the executable hash and creation identity, per-process
private commit/working set/CPU/handle counts, aggregate samples and observation
cost. It follows identified children through reparenting while rejecting PID reuse.
CPU percentage is relative to one logical core; the first interval is omitted.
Sampler work runs outside the frontend's dispatcher. It never collects command
lines, window titles, artwork or credentials. A new output path is required so
earlier evidence is preserved.

Sampled private commit includes the frontend, Bridge and worker processes; it is
not just the managed heap or frontend. Short-lived children and between-sample
peaks can be missed. Working sets contain shared pages and should not be treated
as additive exclusive memory. GPU allocations and presented-frame intervals are
not measured by this script. Use compositor/ETW evidence and captured frames for
motion and first-frame claims; application request/ready/commit timings are only
logical milestones.

For a repeatable scrolling workload, use the [Playnite fixture](playnite-performance.md).
See [performance investigation](performance.md) for comparison and acceptance
criteria. Historical startup numbers are retained in Git history; remeasure the
actual payload before making a release claim.
