# Scrolling and startup review — 2026-09-30

> Implementation record: checkpoint dates, temporary candidates, former source
> paths and pending-work statements below describe the stage recorded, not current
> release status. See [current platform status](current-platform-status.md) and
> [WinUI authoring](../reference/winui-authoring.md) for the implemented contract.

## Synthetic lifecycle follow-up

Evidence: `artifacts/winui-shell/away-polish-20260930/soak02/result.json`.
The isolated switch fixture completed 1,000 selections across four fake widgets
against the three-presenter cache, plus 250 shell hide/reopen cycles in 129.25s.
All sampled states retained exactly three native surfaces; no recovery or abandoned
preparation occurred. Frontend private bytes ranged 143.75–157.29 MiB and ended
154.46 MiB; first/last sampled handles were 1400/1409. Managed heap varied with GC
and ended at 11.34 MiB; the fixture did not force collection.

This demonstrates bounded behavior for this synthetic selection/eviction workload.
It excludes real artwork grids, browser/media, worker process memory, GPU allocation
and physical frame pacing. It is not evidence that Playnite scrolling is fixed;
that optimization remains deferred. See `winui-shell-memory.md` for the optional
reproducible soak mode. The original scrolling/startup review follows.

User reports otherwise satisfactory performance, with Playnite Library stutter
during fast scrolling on both sticks, including previously loaded posters.
This pass targets demonstrated redundant work, not a framework rewrite or an
unproven 60/120 FPS claim. All evidence is under
`artifacts/winui-shell/performance-review-20260930/`.

## Official guidance checked

- [Animations and media](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-animations-and-media)
- [Startup performance](https://learn.microsoft.com/en-us/windows/apps/develop/performance/app-startup-performance)
- [ListView/GridView](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview)

The downloaded Microsoft Learn Markdown is preserved with the evidence.
The supplied third-party tips were treated as hypotheses, not authority.

Already implemented: constrained native ListView/GridView and virtualizing panels;
compiled one-way item bindings; indexed/discovered data demand; asynchronous,
explicit target-sized native image decoding with DPI/focus-scale allowance;
compositor presentation animations; lazy browser creation; hidden startup that
does not start workers. ISupportIncrementalLoading is not a replacement for the
existing range-demand/known-prefix source. Blindly phasing the entire dynamic row
would also delay its focus/action declaration. Replacing Grid with Border is not
a general optimization when the layout actually needs multiple children/tracks.

No browser prewarming, broad image cache, cache-size increase, custom scroll
scheduler, package-validation bypass, or speculative template rewrite was added.

## Repeated row application

Physical follow-up: user confirms Playnite still stutters mid-list. Do not treat
the reduction below as closure. Source review identifies remaining investigation
targets: WidgetIndexedRowView retires its entire presenter on actual Unloaded or
null Row; this can sacrifice inner-tree reuse even though WinUI recycles outer
containers. The compiled template binds the whole row without progressive phases;
asynchronous artwork is separate but row construction is still synchronous. The
artwork decoder has no shared decoded-image cache, so previously viewed posters
are not guaranteed to avoid decoding after retirement. These are verified design
properties, not measured causes of the user's remaining frame stalls. The next
step is real-Playnite frame/UI-thread profiling correlated with realization,
retirement and decode activity, followed by a bounded correction to the dominant
cost. Preserve lease/action retirement independently of reusable visual resources.

Compiled data binding and native Loaded can both apply the same row presentation.
The native synthetic GridView warm-scroll comparison found 99 full fragment
applications, 36 redundant, across the observed realized controls. The corrected
run applies 63 and skips 36. Both runs have 52 realized rows, 52 images, three
columns, offset524.8, focus at item36 and no stalled navigation in this sequence.
This is a36% reduction in those full applications, not a measured FPS improvement.

WidgetIndexedRowView now skips fragment reconciliation only when the immutable
row, owning presentation and styles have identical object references. Container
attachment/style/focus/context decoration still update. Presentation activity
still reconciles; retirement clears stamps; new content/lease/style/owner invokes
the normal path. No extra artwork or offscreen row retention was introduced.

Full native indexed regression passes21 checks, including content refresh, focus,
deep navigation, reversal, grouping, surfaces and suspension. Native retained-theme
regression passes21 checks. The user's actual fast-scroll symptom still requires
physical acceptance; no presented-frame capture of that incident was obtained.

## Startup

The "Connecting to widget service" label covered several operations. Added bounded
phase timing to the existing asynchronous switch log. Baseline: preferences89.8ms,
preview readiness91.4ms, Bridge connection522.4ms, catalog2189.8ms, initial widget
3015.4ms. Logs also show installed catalog validation scanning34 retained versions,
2902 files,557MB; warm validation is around1.2s and one post-update scan was14.9s.

Two waits were separate:

1. Bridge acknowledged Hello, then eagerly scanned the app/game library before
   reading subsequent requests. Removed this redundant warmup: the Windows app
   library provider already scans lazily on first query, serializes concurrent
   scans and serves a cached immutable snapshot afterward. Explicit refresh is
   unchanged. A real protocol test verifies catalog requests do not call the app
   provider and later artwork/query/launch behavior still works with fake data.
2. The shell waited for all installed widgets before opening already-admitted
   Settings. Startup now accepts the requested initial widget when admitted, or
   trusted Settings for ordinary startup. Missing explicit requests retain the
   complete-catalog fallback. Installed widgets still require validation. Saved
   pin restoration waits for the complete catalog and cannot override a newer
   user pin intent.

The first rebuilt-service run reached Settings at2171.4ms, before catalog completion
at2790.8ms; its Bridge connection was1300.8ms. These are individual logical timing
samples with different cache state, not a repeatable cold-start benchmark.
The final candidate restart (PID54988) reached Bridge connection at467.8ms,
Settings-ready at1289.4ms and complete catalog at1903.9ms. The pre-change sample
was3015.4ms to Settings. This supports the intended startup ordering and provides
a warm-run observation; it is not a first-present or statistically controlled result.

## Settings home

Home category cards are now available during asynchronous initialization. Selecting
an unready section displays a section-local loading indicator and Back control;
the current page survives initialization completion. Mutating actions remain
blocked, so uninitialized/default preference values cannot be edited. The existing
operation gate still owns real data operations.77 Settings tests pass, including
navigation/Back while initialization is deliberately blocked and mutation denial.

## Delivery notes

145 managed shell tests,77 Settings tests, the focused Bridge test, both21-check
native collection suites, and clean frontend/worker/Bridge builds qualify this
batch. Settings and Bridge were staged into the isolated candidate installation;
prior payloads are preserved in the evidence directory. Initial Settings staging
retained an old integrity receipt and Bridge correctly refused startup. Resealed
the rebuilt bundled package with the repository's BundledWidgetPackageSeal tool;
the succeeding native startup rendered the real Settings home. Integrity checking
was not weakened. Physical scrolling acceptance remains outstanding.
