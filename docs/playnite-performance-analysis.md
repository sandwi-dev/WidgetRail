# Playnite library scrolling: memory and performance analysis

Baseline: `7facc40a`. Branch: `codex/playnite-performance`. Investigation started
2026-10-19. The first phase retained two memory/publication optimizations; the
later controller-animation findings are recorded below. Fixtures and diagnostics
are excluded from ordinary Release frontends.

## Findings

The repeatable 2,000-game workload reproduces high memory use and scrolling
pauses. Virtualization was working: settled realization remained around 78 tiles
and 112 resident slots, rather than retaining all 2,000 games. The largest
confirmed frontend waste was repeated computed-style data in retired range
payloads. Synchronously publishing a whole arriving page also monopolized the
UI thread while bindings created native rows.

The retained changes are:

1. **Share equal immutable style states within one validated payload.** Every
   incoming property is still validated and counted against the original
   budgets. Caller-owned maps are copied. Immutable scalar value records and
   equal frozen maps can be shared. There is no global cache, protocol change,
   or sharing of mutable state across requests.
2. **Publish arriving indexed rows in small batches.** At most eight rows or
   approximately two milliseconds of work are performed before yielding at low
   dispatcher priority. This is a scheduling budget, not a hard upper bound on
   one row's cost. Every continuation checks active state, cancellation, content
   revision, and exact page ownership. Fetch admission stays bounded until
   publication finishes. Retired data cannot regain input authority.

These initial changes apply to shared host infrastructure, not just the Playnite widget. No
widget cache budget, image quality, native cache length, or controller behavior
was changed. Production does not force garbage collection or trim working sets.

## Final matched measurements

The same fixture, generated artwork, optimized Release build settings and
navigation checks were used in all four runs. Order was baseline, optimized,
optimized, baseline. The baseline executable contains the final diagnostics but
neither production optimization. Assembly/package hashes are in each run's
`provenance.json`. The bridge and fixture package are held constant here to
isolate the frontend changes; the later cumulative-payload check is separate.

All memory figures are MiB. Private working set is the closest measurement to
Task Manager's normal Memory column. Private bytes measures committed private
memory; it is not the same metric. Peaks include the identical navigation and
lifecycle phases, while settled figures are taken after the third timed pass.

| Run | Frontend peak private working set | Frontend settled private bytes | Widget process peak private working set | Bridge peak private working set | Callback p99 across three passes |
| --- | ---: | ---: | ---: | ---: | --- |
| `final-baseline-artwork` | 1,036.7 | 1,066.0 | 413.1 | 119.2 | 287–446 ms |
| `final-baseline-artwork-02` | 1,007.8 | 1,013.5 | 341.9 | 132.6 | 275–304 ms |
| `final-optimized-artwork-01` | 628.1 | 634.5 | 397.3 | 108.9 | 128–152 ms |
| `final-optimized-artwork-02` | 603.3 | 642.1 | 360.4 | 119.7 | 117–151 ms |

Frontend peak private working set fell about **40%**, and settled private bytes
about **39%**, comparing the average of each pair. These are repeated local
observations, not a statistically controlled cross-machine benchmark.

Composition callback intervals are **not GPU present times**. Their long tail
improved substantially, but many gaps still exceed 50 ms. The proportion ranged
roughly 22–29% in the baseline and 21–27% after optimization; raw counts alone
would mislead because the optimized runs delivered more callbacks. Median
callback intervals remained near 30–31 ms. This does not establish smooth
60-fps scrolling.

Sampled visible tiles with artwork during the extreme scroll increased from
10.8–12.0% to 36.4–37.8%. This is one-second sampling, not frame-by-frame coverage.
All visible tiles had artwork after settling. The improvement is not obtained
by withholding artwork: both variants completed 182 page loads by the third
settled sample, and the optimized frontend completed substantially more decodes
before images retired. Consequently total transient allocation did not fall:
by that point it was 14,821–14,947 MiB versus 13,469–13,515 MiB in the baseline, with
more collections. Memory retention and allocation throughput are separate issues.

The widget process remained 342–413 MiB in the baseline and 360–397 MiB afterward.
There is no established widget-process memory reduction. The actual bridge was
109–133 MiB in these runs; it must not be conflated with the separate widget
application process. Do not add independent per-process peaks and describe the
sum as a simultaneous total.

Sampled processor-time counters during the timed passes averaged 1.23–1.38
logical-core equivalents for the baseline frontend and 1.37–1.38 afterward.
The bridge used 0.31–0.40 versus 0.36–0.46; the widget used 0.12–0.17 versus
0.14–0.24. There is no demonstrated CPU reduction. More artwork completes in
the optimized run, so memory and worst-pause improvements do not imply less
total work. These are processor-time counters, not Task Manager's
frequency-adjusted processor-utility measurement.

## Heap and trace evidence

Earlier exploratory runs, all under `artifacts/performance/playnite-stress/`:

| Run | Frontend peak private working set | Frontend settled private bytes |
| --- | ---: | ---: |
| `baseline-no-artwork-02` | 635.6 | 710.2 |
| `baseline-artwork-heap` | 1,023.1 | 1,116.8 |
| `shared-styles-no-artwork` | 304.1 | 383.0 |
| `shared-styles-artwork` | 561.5 | 636.7 |

The no-artwork comparison isolates substantial overhead from presentation/style
data, independently of JPEG decoding. The configured decoded-image estimate in
the artwork workload was only about 22 MiB for realized images; that estimate is
not a complete accounting of native/GPU memory.

`baseline-artwork-heap/frontend.gcdump` showed about 1.19 million style-value
objects and 46,387 view nodes. Raw heap counts alone do **not** establish a leak.
The later `baseline-no-artwork-roots/frontend.dmp` and SOS live/root analysis
found approximately 50.3 MiB of live managed objects, 12 live indexed leases,
158 live rows and 112 live slots. An examined retired lease had no GC roots.
Much of the large heap was unreachable retired data awaiting collection through
the managed/native lifecycle. This is evidence against an unbounded live
collection in that inspected workload, not proof that every possible lifetime
path is leak-free.

A 20-second frontend `.nettrace` in `baseline-artwork-02` highlighted WinRT/COM
stream work. It sampled thread time, not pure CPU time, so stack percentages must
not be presented as CPU utilization. Bridge runtime counters from the same run
reported about 521 MB/s average allocation (829 MB/s peak). Those decimal-byte
rates are transient allocation, not retained memory. That first exploratory run
had a fixture teardown error and is not used as successful lifecycle evidence.

Heap collection happened in a separate post-timing diagnostic phase. No timed
comparison includes a forced GC or heap dump. Portable tools (`dotnet-counters`,
`dotnet-trace`, `dotnet-gcdump`, `dotnet-dump`) and diagnostic captures are under
`artifacts/performance/`, outside shipped payloads.

## What Playnite does

Read-only reference: [Playnite](https://github.com/JosefNemec/Playnite) at
`3e8abb241873e326e27c75b59dd0d2ea367698e3`.

- [ImageSourceManager.cs](https://github.com/JosefNemec/Playnite/blob/3e8abb241873e326e27c75b59dd0d2ea367698e3/source/Playnite/ImageSourceManager.cs)
  uses a shared 100 MB image cache charged by decoded image size and accounts
  for decode properties when reusing an entry.
- [GamesCollectionViewEntry.cs](https://github.com/JosefNemec/Playnite/blob/3e8abb241873e326e27c75b59dd0d2ea367698e3/source/Playnite/GamesCollectionViewEntry.cs)
  derives fullscreen decode dimensions from screen size, columns/rows and DPI.
- [FullscreenTilePanel.cs](https://github.com/JosefNemec/Playnite/blob/3e8abb241873e326e27c75b59dd0d2ea367698e3/source/Playnite.FullscreenApp/Controls/FullscreenTilePanel.cs)
  computes visible ranges and recycles/removes containers outside them.
- [FadeImage.xaml.cs](https://github.com/JosefNemec/Playnite/blob/3e8abb241873e326e27c75b59dd0d2ea367698e3/source/Playnite/Controls/FadeImage.xaml.cs)
  supports background loading and an optional source-update delay.

WidgetRail already uses native virtualizing panels and display-sized decoding.
Its Playnite application has a bounded **encoded** artwork cache of 128 MiB;
that cannot be compared directly with Playnite's decoded cache. WidgetRail also
transports generic styled presentation and encoded images through application,
bridge, and frontend process boundaries. These are concrete additional costs,
not a reason to accept avoidable allocation or UI-thread work.

Playnite Fullscreen was not benchmarked against this synthetic provider. The
user's approximately 300 MB observation remains a useful reference, not a
controlled result or a target this patch claims to meet.

[Microsoft ListView/GridView guidance](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/optimize-gridview-and-listview)
on bounded viewports, native virtualization, reducing elements and progressive
rendering informed the audit. WinUI skills were also used to review UI-thread
work, resource ownership and Release validation. Existing virtualization was
retained; no custom scrolling/layout implementation was introduced.

## Experiments deliberately excluded

- Early reference clearing in retired presenters/styles/artwork callbacks:
  no material reduction; reverted.
- Copying encoded data once into a native stream instead of using the managed
  stream adapter: no material gain; reverted.
- `x:Phase` on artwork binding: no useful gain when data arrives after initial
  binding; reverted.
- Runtime native `CacheLength=1` after initial realization: the pool was already
  allocated, so this is inconclusive about a cold cache default. No default
  changed, and the experimental flag was removed from the final fixture.
- Viewport-specific artwork admission: correctness checks passed after fixing
  cancellation reentrancy, but full-run cadence worsened. Entire experiment
  reverted, including its tests and helper APIs.

Diagnostic patches and runs are retained under `artifacts/performance/`. None
of these rejected implementations remains in the production diff.

## Repeatable fixture and reproduction

`scripts/Test-PlaynitePerformance.ps1` uses an isolated catalogue/profile and the
production Playnite widget/application, bridge, indexed leases, native GridView
and artwork decoder. Only the upstream provider is fake. The normal three-game,
no-artwork process-test fixture remains the default.

Performance mode uses 2,000 games, distinct resource handles, eight 1000×1500
cover JPEGs and eight 1920×1080 background JPEGs (about 5.4 MB on disk), plus
15 ms provider delay. It traverses/reverses the library in three 20-second
passes, settles between passes, then tests lifecycle transitions. There are no
network requests, credentials, game launches or user-library edits. Generated
high-entropy artwork and this scroll rate deliberately stress the pipeline.

First build a normal candidate with `scripts/Build-WinUiReleasePayload.ps1`.
For measurements, build the validation frontend with the same native inputs:

```powershell
$candidate = (Resolve-Path 'artifacts/performance/playnite-candidate').Path
dotnet build src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj `
  -c Release -p:EnableWidgetValidation=true -p:PublishTrimmed=false `
  -p:Platform=x64 `
  "-p:OverlayPlatformInteropPath=$candidate/native/platform/OverlayPlatformInterop.dll" `
  "-p:WindowPreviewNativePath=$candidate/native/preview/WinUiWindowPreviewNative.dll" `
  '-bl:artifacts/performance/validation-{}.binlog'

& scripts/Test-PlaynitePerformance.ps1 -Prepare -NavigationChecks `
  -CandidateRoot $candidate `
  -FrontendExecutable src/OverlayFrontend.WinUI/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/OverlayFrontend.WinUI.exe `
  -Workspace artifacts/performance/reproduction -RunName artwork-01

& scripts/Summarize-PlaynitePerformance.ps1 `
  -RunDirectory artifacts/performance/reproduction/artwork-01
```

Use a fresh workspace with `-Prepare`; omit it for further runs with fresh run
names. Use `-WithoutArtwork` to isolate presentation cost, and `-HeapSnapshot`
only with the portable dump tools available at the paths expected by the script.
Keep builds, other tests and profiler collection out of timed runs. Do not
compare a validation build with a trimmed production frontend: the former has
additional test metadata. Preserve executable hashes and identical build flags
when comparing revisions.

The output includes per-process private working set/private bytes/CPU/handles,
managed allocation/GC statistics, native tile/slot/image counts, sampled visible
artwork coverage, composition callback gaps, dispatcher delays, and test
results. These are validation-only diagnostics, not permanent production logs.

## Remaining work suggested by evidence

The highest-priority next investigation is allocation stacks across artwork
transport and native image/row creation, with per-delivered-image/page costs.
A further cache or transport change should first improve the same fixture while
preserving artwork coverage, input latency, authority and cancellation tests.
Simply lowering a cache cap or forcing GC would obscure the problem. A live
library comparison can then check representativeness without editing its data.

This patch improves frontend memory and long pauses, but does not claim to
eliminate stutter, reduce widget-process memory, or match Playnite Fullscreen's
single-process footprint.

## Validation

| Check | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Presentation-session tests | 353 | 0 | 0 |
| Playnite widget tests | 165 | 0 | 0 |
| Playnite application tests | 39 | 0 | 0 |
| WinUI shell tests | 306 | 0 | 0 |
| Bridge indexed scenarios | 19 | 0 | 0 |
| Bridge production Playnite workflow | 1 | 0 | 0 |
| Native publication/reentrancy/lifetime/retention assertions | 68 | 0 | 0 |

Each of the four matched stress runs passed rapid Down movement, reverse
movement, search replacement/restoration, and background/reopen navigation.
The native publication tests interrupt a 32-row page between batches with
suspend, disposal and content refresh, check that stale rows do not publish,
and require each delivered lease to be released exactly once. Existing delayed
and cancellation-insensitive provider scenarios also remain green.

Managed test commands use the repository's native MTP runner:
`dotnet test --project tests/<project>/<project>.csproj -c Release --no-restore -bl:<unique>.binlog`.
The bridge's standalone scenario runner uses `--test-prefix Indexed` and
`--test-prefix "Playnite process"` after its Release build. Native checks use
the validation frontend with `--validate-styles --indexed-publication-output=<absolute-json-path>`.
All build/test binlogs and result logs are under `artifacts/performance/`.

The cumulative production payload built successfully with the WinUI analyzer
integration. Published dependency/cleanup verification passed, and its actual
trimmed executable passed startup through browser-owner initialization to
Settings with no startup failure.

`cumulative-optimized-artwork` then exercised the validation frontend against
that newly published bridge. All four navigation checks passed. Frontend peak
private working set was 569.4 MiB, settled private bytes 594.6 MiB, widget-process
peak 334.5 MiB and bridge peak 136.4 MiB. Callback p99 was 113 / 161 / 136 ms;
sampled visible-artwork coverage was 37%. This is a separate integration run,
not a replacement for the repeated matched comparison above.

The cumulative candidate is `artifacts/performance/playnite-candidate`. It uses
the existing installed widget packages; no Playnite or PoB package update is
required. Performance changes are on `codex/playnite-performance`, awaiting
user acceptance before integration into main.

## Follow-up: continuous right-stick scroll stability

The reported symptom was clarified as holding the right stick down continuously
in the Library grid. This is different from directional focus navigation and
from a pause caused solely by allocation or rendering work.

A confirmed input bug was found in the shared `WidgetViewPresenter.ScrollBy`
path. `ChangeView` applies asynchronously, but every relative movement was based
on the last reported `VerticalOffset`. The native input pump can drain up to 16
queued controller packets in one dispatcher turn. For example, a movement of
100 followed by 1 could request destinations 100 and then 1 while the reported
offset was still zero. The second request discarded most of the first movement.

The correction retains one accepted destination per axis and active viewport,
accumulates subsequent deltas against that destination, and clears it when the
native view catches up. Bounds and physical-pixel rounding are respected. Wheel
input drops the controller's pending offsets; owner changes discard the whole
gesture. It adds no movement queue, timer, smoothing, or change to stick speed,
deadzone, axis hysteresis or the existing hitch cap. Focus settlement retains its
existing behavior.

The fixture now has `-ScrollDirectionChecks`. It uses the production right-stick
sampler at full and partial deflection, plus short batches of packets in one UI
dispatch. Short synchronous delays in that **test only** emulate time spent
polling/dispatching queued packets; no such delay is in production. It records
actual requested destinations, native offsets, visible tile positions, and
whether artwork was already present. Holding and release phases are separate.

- `burst-direction-baseline`: 157 backward requested destinations while holding
  down and 163 while holding up; maximum retreats were 74.8 and 70.4 DIPs.
- `held-scroll-final`: zero backward requests, zero backward native offsets,
  and zero reversals of already-present visible artwork during all four held
  scenarios (full down, batched down, batched up, partial down).
- The observed small tile-position shifts were placeholders acquiring their
  six-DIP margins before their artwork appeared. These are recorded separately
  from movement of already-present artwork.
- An exploratory run also showed automatic viewport adjustment after releasing
  the stick and transferring focus to a scaled tile. That is a separate
  release-time case; it is not evidence for the continuous-hold symptom and was
  not changed in this correction.

The trace confirms incorrect destination ordering and discarded movement. It
does **not** establish that every backward request was visibly presented by the
compositor, or that this fully explains the user's perceived rubberband. The
candidate requires the user's comparison with the actual library before that
claim can be made.

Validation: all 310 shell tests passed, including four accumulator regression
cases; all 44 native scroll/focus checks passed, including a real two-request
burst and reverse burst in one dispatcher turn. Existing checks cover ordinary
and horizontal reading regions, focus continuity, boundary handling and focus
revocation. The original memory stress test drove absolute positions, and its
navigation checks used directional focus: neither asserted relative-scroll
request ordering. The new fixture closes that specific coverage gap.

One initial diagnostic launch failed its bridge handshake before scrolling and
was excluded; subsequent launches completed. Raw observations, including that
failed launch and release-time cases, remain under the performance artifacts.
The scroll-request trace is compiled only into validation builds, with no new
production logging.

## Native wheel and controller animation comparison

The original callback measurements did not establish displayed smoothness.
The next experiment therefore recorded the actual native grid at 30 samples per
second, with the same 2,000-game provider and roughly 17,000 DIPs of downward
travel over eight seconds. `Analyze-PlayniteMotion.py` crops out static chrome
and compares the middle seven seconds. Runs are under
`artifacts/performance/playnite-stress/`.

| Run | Input route | Identical viewport samples / 209 | Longest identical run | Mean image-correlation confidence |
| --- | --- | ---: | ---: | ---: |
| `stick-native-capture` | Previously unanimated controller offsets | 32 (15.3%) | 69 ms | .962 |
| `wheel-native-final` | Actual OS wheel input | 0 | 0 ms | .972 |
| `controller-native-final` | Controller through animated native `ChangeView` | 0 | 0 ms | .968 |

The older controller capture also had 43 high-confidence estimates of zero
vertical displacement, including frames where artwork fading changed pixels
without moving the grid. Wheel and animated-controller captures had none.
All 209 pairs in each of these three captures contained trackable texture.
These are captured-screen observations, not GPU present times or proof of
60/120-fps presentation. The UI callback p99 for the old controller and wheel
was approximately 56–57 ms in both cases; callback timing alone concealed the
visible difference.

Targeted `WM_MOUSEWHEEL` delivery to the main window and its deepest WinUI child
produced no wheel events or scrolling. Actual `SendInput` wheel input worked,
but depends on foreground/pointer routing. It remains an opt-in validation
baseline guarded to the fixture's own process. **No synthetic mouse input is
used for production controller scrolling.** Rejected message routes and direct
animation prototypes were removed from maintained source and archived under
`artifacts/performance/reference/`.
The final wheel fixture also verifies takeover from an active controller scroll:
the controller relinquishes pending focus settlement without cancelling the
new native wheel motion.

The production route retains `GridView`, `ScrollViewer`, native virtualization,
the existing stick sampler and the accepted destination accumulator. It lets
WinUI animate the viewport rather than applying every offset without animation.
Reversing resets the old destination and retargets native animation from the
current position. Reduced motion still requests immediate offsets.

Interruption uses the public `UIElement.CancelDirectManipulations` on the scroll
content. An isolated native probe showed that sending an immediate offset
alongside cancellation can cause a later position correction. Cancellation
alone stops at the compositor's current position and reports that final pose
asynchronously; reversal through animated retargeting reaches its new destination.
Neither path adds an application animation timer or a movement queue.

A separate release-time bug was reproduced: native focus reveal can run after
the synchronous `Focus()` call, moving the viewport back toward a newly focused
scaled tile. Passive focus settlement now temporarily disables
`BringIntoViewOnFocusChange` on that viewport. Explicit navigation, pointer or
wheel input, author-requested reveal, focus departure, unloading and presentation
teardown retire that ownership. Original effective policy and ordinary bindings
are restored. Raw `ReadLocalValue` expressions cannot safely be restored through
`SetValue`; the full GridView test caught that during development.

The `native-animation-directions-2` run passed full downward hold, downward
bursts, upward bursts and partial downward hold with **zero backward requested
destinations, native offsets, loaded-artwork movement or release corrections**.
The native regression suite also covers full/reduced/system-disabled motion,
reversal, input revocation, hide/resume, replacement, directional interruption
and pressing Select before animation finishes. Select cannot activate the old
offscreen row.

Query replacement received a separate regression check with 100 ms provider
delay. The same native collection can survive a new query, so viewport identity
alone was insufficient: an old pending destination could prevent settlement
after the replacement restored its position. The collection now notifies its
presenter before replacing its view/source. The presenter retires that
collection's scroll intent without duplicating the source's replacement rules.
The existing continuity policy still preserves surviving items and position.
`native-cache2-query-motion-fixed` verifies replacement, narrowing to one item,
restoration and subsequent settlement during active native motion.

## Cold native cache experiment

The earlier runtime cache experiment was inconclusive because containers had
already been realized. The fixture now accepts `-CacheLength 1`, `2` or `4` and
replaces the grid template **before the first collection is created**.

| Native cache length | Ready containers | Ready frontend private bytes, MiB | Observation |
| --- | ---: | ---: | --- |
| Default 4 | 70 | 246–254 | Retained production default |
| 2 | 42 | 205–221 | Lower ready memory, but less margin for delayed rows |
| 1 | 28 | 217 | Visibly late/fading covers during normal fast scrolling |

These are exploratory runs, not matched estimates of Task Manager's Memory
column; private bytes and private working set differ. The reduced caches saved
roughly 25–35 MiB in representative ready-state comparisons, but did not
establish an equivalent artwork experience. With an intentionally slower
100 ms provider, both cache 2 and cache 4 could outscroll delivered artwork;
cache 2 also exposed a completely empty grid region in an inspected frame.
Cache 1's late covers were visible even with the normal 15 ms provider.
**Neither reduced cache was adopted.**

Blank/background-only captures cannot establish stationary scrolling: the
pixels can remain identical while empty slots move. The analyzer now labels
whether each pair has enough texture to track, alongside its raw repeated-frame
count. Do not treat the delayed-provider captures as a smoothness benchmark.
The normal-provider controller/wheel comparisons use textured visible artwork.

Reproduce native captured comparisons with the same candidate/fixture arguments
described above, adding `-WheelComparison stick -CaptureComparison` or
`-WheelComparison wheel-input -CaptureComparison`. Only the latter moves the
pointer and injects wheel input; it aborts if foreground ownership or the
pointer changes. `-NavigationOnly -ProviderDelayMilliseconds 100` runs the query
and navigation regression without repeating the long allocation workload.

Final validation of the animation changes:

- `dotnet test --project tests/WinUiShell.Tests/WinUiShell.Tests.csproj -c Release --no-restore`
  with a unique binary log: **310 passed, 0 failed, 0 skipped**.
- `release-native-focus.json`: **74 native checks passed**.
- `release-native-directional.json`: **93 native checks passed**.
- `native-final-delayed-lifecycle`: passed all three stress traversals with
  100 ms provider delay, rapid directional navigation, reversal, query replacement
  while animating, background/reopen and disposal.
- `controller-native-final` and `wheel-native-final`: completed native captures,
  each with **0 / 209 stationary textured viewport pairs**; wheel takeover
  additionally passed without inheriting controller focus settlement.

These checks establish the reproduced improvement and the tested lifecycle
behavior. They do not promise the same memory figure or frame cadence for every
library, driver, monitor or slow upstream provider. A user comparison in the
normal profile remains the acceptance step before integrating the branch.
