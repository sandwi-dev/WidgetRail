# Library scrolling and guide refresh investigation

2026-11-12, following local Playnite package update 0.2.146.1.

## Final outcome — 2026-11-13

The regular pauses are resolved in the tested Release candidate. A retained
computer-use helper was a major contributor: native stacks showed cross-process
UI Automation bulk fetching, and resetting this chat's stale computer-use kernel
ended that helper. With identical frontend, Bridge and selected widget payload
hashes, input-loop p95 fell from roughly 90 ms to 31.6 ms (the fixture's timer
cadence), and UI-continuation p95 fell from roughly 64–69 ms to 0.27–0.32 ms.
External Normal-priority callback p95 fell from 62.45 ms to 0.51 ms. This is a
controlled observer-effect result, not a hardware FPS claim. The user then tested
the default-profile Release candidate and confirmed: “Smooth; the regular pauses
are gone.”

The accepted guide-refresh coalescing fix is retained. Bounded row continuations
now use Normal priority after paired callbacks reproduced Low-priority starvation.
The dispatcher demand pump retains its original Low priority. The investigation
also corrected a post-disposal guide event and a catalogue-before-HelloAccepted
startup race. The independently reproduced Playnite HTTP response-write crash is
fixed in the reproducibly built 1.5.3.2 addon package; that addon is prepared locally
and has not been installed over the user's existing addon.

The current candidate is `artifacts/input-discord/grid-stutter/release-candidate`.
Its frontend is a normal trimmed, ReadyToRun Release build with no validation
types; the emitted PE has a 196-byte ReadyToRun header. It was launched with the
default profile, retaining selected Playnite 0.2.146 and the unchanged installed
catalogue state. It remains running for the user. No merge, push, public publication
or release has been performed.

Final gates: 31 focused host tests and 75 native publication/navigation/lifetime
checks passed. The handshake regression and 17 catalogue scenarios passed.
Playnite setup tests passed 26, failed 0, with 3 explicit live opt-ins skipped.
Fresh and idempotent addon package builds passed the real net462 HTTP-handler
response-abort fixture. Patch application also checks outside a Git checkout.
Artwork-enabled scrolling, query replacement, reversal, hide/reopen and retirement
passed after the helper reset. Occasional isolated cold/layout/GC intervals remain;
these are distinct from the resolved repeating pauses. Other indexed consumers
share the same bounded host mechanism; their provider-specific live workflows were
not individually exercised by this scrolling investigation.

## Observed behavior and scope

The user reports pauses with the controller stick, D-pad and mouse wheel, and
sluggishness elsewhere in the overlay. The running production candidate's
assembly metadata confirms Release, with JIT optimization enabled. Its CLR,
WinRT runtime and native WinUI binaries match the installed local production
build. This was not a Debug frontend launch.

The Playnite widget source difference from the previously installed build is
limited to the Home rail reservation and two related style rules. Library grid
declarations are unchanged. This does not by itself rule out a host regression.

A 45-second trace of the user's scrolling is retained locally under
`artifacts/input-discord/grid-stutter/current.nettrace`. It contains UI-thread
samples in `UpdateContainer -> PublishControllerGuide -> HasCurrentGuideRows`
and native coordinate transforms, including individual sampled spans above
50 ms. It also contains substantial WinRT finalization activity; sampled wall
time is not proof of CPU usage or the cause of every pause. No explicit garbage
collection or working-set trimming has been added.

## Confirmed redundant work and proposed owner

`Presentation/WidgetIndexedCollectionView.cs:UpdateContainer` notifies its
presenter separately for each row. The callback in `WidgetViewPresenter.Create`
previously refreshed every context indicator and synchronously notified the
shell. `Shell/OverlayShellPage.Guide.cs:PublishControllerGuide` then checks
readiness across the visible collection. A batch of rows therefore repeatedly
scans the same grid while the batch is being materialized.

The bounded host fix coalesces only indexed-collection informational guide
notifications on the native dispatcher. Command/popup validation remains
synchronous, as do row admission, focus and input handling. Explicit focus and
shell interactions retain their existing immediate guide notifications. Queued
work reads the current presenter state and rejects disposed/inactive presenters;
it does not capture an item, query, input scope or action. No SDK/protocol change,
widget counters, polling or new public abstraction is needed.

This is an existing host workload issue observed during this investigation, not
proof that the Home spacer introduced the reported regression. The broader
app-wide slowdown remains unqualified until a production comparison.

## Consumer applicability

| Consumer | Treatment |
| --- | --- |
| Playnite Home, Library, favorites and achievements | Shared host fix; no widget changes. Library native scrolling/navigation is the measured case. |
| Games & Apps and Settings installed widgets | Same indexed host path; no author migration. |
| Spotify queue, YouTube Music browse, YouTube search | Same indexed host path; no provider or demand changes. |
| Discord picker, activities and participants | Same indexed host path; no media/session changes. |
| PoB indexed calculations, planning, sets and comparisons | Same indexed host path; domain revisions/commands unchanged. |
| FullApplication reference sample | Same indexed host path. |
| Other shipping widgets and templates | No equivalent guide plumbing found; ordinary controls and browser surfaces are not changed. |

## Validation method and limits

The performance fixture now subscribes to the guide notification and performs
the production readiness query. Previously it had no such consumer, so it could
not measure this workload. It records query count, aggregate duration and longest
duration alongside frame intervals, allocation, artwork and collection counters.
It also checks eventual readiness after scrolling/reopen and absence of delayed
guide publication after retirement. Existing navigation checks exercise rapid
direction changes, query replacement and background/reopen.

Initial widget-only comparisons used the existing Debug validation fixture;
both current and prior widget code exhibited occasional long frames. These are
diagnostic comparisons, not production performance measurements. An initial
baseline package combining the new fixture with old SDK dependencies failed at
startup; the corrected comparison changes only the widget assembly and styles,
keeping the fixture's required dependencies. All fixture packages/catalogues are
isolated from the user's installed widgets.

The guide comparison uses optimized Release validation builds with trimming
disabled only to retain test instrumentation. Production publication keeps its
normal trimming and compiled-out validation. Timing runs must be sequential,
without a concurrent build. Raw artifacts and results remain under
`artifacts/input-discord/grid-stutter`.

## Release comparison

**Qualification correction:** later runs exposed an uncontrolled foreground/render
cadence difference in this fixture (roughly 60/240 FPS versus a stable 32 FPS).
The guide notification-count reduction is directly measured, but these original
frame-interval differences must not be treated as isolated proof of a smoothness
gain. Raw values are retained below as historical evidence. Requalification uses
explicit native foreground admission and continuous foreground checks.

Sequential runs of the same 183-game fixture, same widget payload and artwork,
same native dependencies, with no concurrent build:

| Metric | Before | After |
| --- | ---: | ---: |
| Guide queries through the three scrolling passes and initial load | 540 | 133 |
| Time in those queries | 962.1 ms | 228.3 ms |
| Longest individual query in that interval | 51.2 ms | 25.9 ms |
| Cold pass frames over 50 ms | 25 | 13 |
| First warm pass frames over 50 ms | 22 | 0 |
| Second warm pass frames over 50 ms | 25 | 0 |
| Warm pass frame interval p95 | 18.1 / 18.3 ms | 4.9 / 4.9 ms |

Both runs passed rapid directional navigation, reversal, search replacement
during motion and background/reopen. The revised fixture also passed readiness
settling and absence of delayed guide publication after disposal. Corresponding
result directories are `performance/guide-release-before` and
`performance/guide-release-after`. The reference widget is the previous installed
Playnite assembly/styles with current compatible fixture dependencies, isolating
the host change from the Home reservation.

These are single-run native frame-callback measurements, not hardware present
latency or a universal smoothness guarantee. Cold-pass maximum frame intervals
remain 113.2 ms before and 106.3 ms after; this change does not eliminate all
materialization/finalization cost. User acceptance of the full production shell
and real artwork remains separate.

The first final-publish invocation mistakenly set `PublishTrimmed=true` globally,
enabling trim analyzers in otherwise untrimmed referenced libraries and producing
IL2026 build errors. The corrected invocation uses the frontend project's normal
Release trimming setting, without changing code or suppressing diagnostics.

The corrected trimmed Release publication passed without warnings/errors.
After the user's restart approval, `production-fixed/OverlayFrontend.WinUI.exe`
was launched with the existing default-profile shell configuration. Process and
live shell diagnostics confirmed a responsive foreground window and completed
installed-catalog discovery. The host fix does not require another widget update.

## Provider-to-UI diagnostic follow-up

The user retained the guide fix but reproduced lag with the installed host and
rolled-back Playnite package. The three widget exits at 22:06 local time were
confirmed by the user to follow launching the older installed host with the new
widget package; they are excluded as evidence of scrolling-related worker crashes.

Playnite's own logs independently show a Bridge add-on disconnect-handling bug:
the response write for `/api/eval` fails with `HttpListenerException`, then
`HttpApiServer.HandleRequest` attempts a second JSON response in its catch block.
Setting ContentLength64 on that already-submitted response throws an unhandled
InvalidOperationException and opens Playnite's extension crash dialog. The pinned
Bridge 1.5.3.1 source at 66a32935703c658bfb5d829dcd8b9fe3b8e96f67 has this exact
path. It belongs to the Playnite Bridge HTTP server, not widget cancellation;
disconnects must remain safe. This dependency has not been modified or replaced
in this diagnostic follow-up. It is not proof of the cause of the lag.

Existing WidgetRail Bridge logs record lifecycle, request failures and catalogue
startup timing, not successful range round-trip latency. Playnite Browse reads
slices of an already-captured in-memory game query; artwork is resolved separately.
The host now exposes an opt-in `WidgetRail-PresentationPipeline` EventSource:

| Stage | Meaning |
| --- | --- |
| range-data | Scheduling, widget/Bridge round trip and data/style validation before native publication is queued. This does not isolate provider CPU. |
| range-ui-queue | Time waiting for the first native dispatcher callback after data arrives. |
| range-batch | Synchronous slot publication and native binding callbacks within each bounded batch. Count is attempted rows, not presented pixels. |
| range-total | Start through completed/retired range publication; includes yields between batches. |
| artwork-resolve | Resolve the admitted source through the widget/Bridge. |
| artwork-decode | Source opening, metadata, asynchronous native decoding and continuation scheduling. |
| artwork-publish | Assign the decoded image to its admitted consumer. |
| artwork-total | Complete image operation, including cancelled/failed/retired outcomes. |

Records contain numeric operation IDs, stage, elapsed milliseconds, count and
outcome. They include no titles, URLs, paths, handles, authentication or response
bodies. IDs are process-local; stages are elapsed durations, not CPU durations or
GPU presentation. No provider is enabled and no file is written by default.
All native indexed consumers in the matrix above share these hooks; ordinary
artwork consumers share the artwork timings. No widget migration is required.

Attach `dotnet-trace` to the normally started frontend with provider
`WidgetRail-PresentationPipeline:0xffffffffffffffff:4` for a bounded reproduction, then use
`scripts/Summarize-PresentationPipelineTrace.ps1` with the TraceEvent assembly
bundled with that tool. Disabled-operation, numeric-payload and correlation tests
pass in WinUiShell.Tests. The first trace-assisted child launch timed out starting
the Bridge; normal launch followed by attaching the recorder starts successfully.
Do not use the trace launcher for this multi-process reproduction.

The recorder must enable all keyword bits: the initial zero-keyword capture did
not emit the timing events even though EventSource reported enabled. The reader
also handles TraceEvent's unmanifested EventPipe representation by validating the
explicit provider GUID, event ID/version and 28-byte numeric schema. A trimmed
standalone probe emitted and recovered all ten expected records. Do not interpret
an empty timing trace as fast/no-work behavior.

### Live data-path capture

`live-pipeline-corrected.nettrace` and its numeric JSON summary capture a normally
started, trimmed Release frontend using the user's reverted Playnite 0.2.146.
Library navigation and 13 native UI Automation scroll actions were exercised;
the recorder attached after normal launch. This includes cold loading, not a
pure warm-scroll benchmark, and does not replace the earlier user's physical run.

For the four Library 32-row operations, data-stage elapsed times were 83.1,
104.3, 89.7 and 92.2 ms. Full publication elapsed times were 719.5, 1068.8,
828.1 and 355.7 ms. Initial UI queue waits were only 0.7–17.8 ms. Synchronous
batch work totalled 10.7–45.8 ms per operation; gaps between later batches reached
197.7, 282.7 and 228.5 ms. The excess includes native dispatcher scheduling,
layout/binding work between callbacks and small admission/release overhead; it
must not be labelled pure provider latency or pure CPU time.

Across 140 image operations, artwork resolution median/p95 was 3.38/14.00 ms;
decode-stage median/p95 was 25.54/70.21 ms, with a 577.72-ms maximum. Decode-stage
elapsed time includes asynchronous source/codec work and UI continuation waits.
Image assignment median was 0.15 ms. No failed/cancelled operations occurred in
this bounded capture. One earlier Home range took 516.31 ms to provide its data.

This establishes substantial post-reply delay in the tested host path, but not
the cause of each gap or a universal diagnosis of all reported app-wide lag.
Keep the existing bounded batches, cancellation and authority checks until a
focused scheduling/layout trace identifies a safer improvement; no queue-priority
or batch-limit change has been made in this follow-up.

## Active scrolling goal

The user requested an independent end-to-end investigation and fix. Preserve the
accepted guide coalescing change and the reverted Playnite package. Do not merge
or release as part of this goal.

Next decision: distinguish normal dispatcher congestion from starvation of the
Low-priority continuation lane. With tracing enabled only, each queued row batch
now has a paired Normal-priority timing callback (`range-normal-probe`) and its
actual continuation reports `range-batch-queue`. Both measure from the same queue
point. The normal probe performs no UI work or input action. Outside tracing no
probe is queued. Production scheduling and the eight-row/two-ms batch budget are
unchanged until this comparison supplies evidence.

Validation after any scheduling change must cover provider cancellation during
scrolling, query replacement, direction reversal, focus through unrealized rows,
background/reopen, stale actions and bounded retention, as well as timing. Compare
optimized builds sequentially without concurrent compilation. Inspect all indexed
consumers listed above; no widget-authored scheduling workaround is appropriate.

### Paired-priority result and bounded correction

The normally launched trimmed Release probe captured 56 matched pairs in
`priority-comparison.nettrace`. Normal-priority callbacks had median/p95/max waits
of 0.27/1.03/1.54 ms. Row continuations queued at the same instants with Low
priority waited 18.07/121.10/202.69 ms. Twenty-nine Low callbacks waited over 16 ms;
none of their Normal counterparts did. This reproduces starvation of the row
publication lane, distinct from a generally blocked UI thread.

`IndexedItemsSource.FetchAsync/PublishBatch` now queues its continuation at Normal
priority. The eight-row/two-ms soft limit and all per-item lifetime/query checks
remain intact. The initial range data callback was already Normal priority. The
range-demand pump remains unchanged pending separate evidence; other background
queues are not promoted globally. Input handlers and provider work are unchanged.
Native publication/interruption tests and sequential scrolling/navigation timing
comparisons are required before calling this correction validated.

The Normal-continuation validation build passed 74 native publication, reentrancy,
lifetime and retention checks, plus the Playnite scrolling/navigation/reopen checks.
However, its initial performance run rendered near 32 FPS throughout, unlike the
earlier fixture's changing cadence. That comparison is not accepted as evidence
for or against the scheduling change. The fixture previously did not request or
record native foreground ownership.

The performance runner now requests the existing `--validation-platform-activation`
path, awaits actual foreground through `WindowsTaskWindowActivation`, records it
in samples and rejects any timed loop that loses it. A validation-only partial
hook accepts `--playnite-low-priority-baseline` so the old and proposed lanes can
be compared with the same optimized executable. Its implementation and flag are
compiled out of the production host. No focus override or UI activation behavior
has been added to shipping code.

The foreground-controlled Normal run exposed one post-disposal guide event and
therefore failed the retirement assertion; this is not waived. Native focus and
popup callbacks can already be queued when their handlers are detached. The
shared `NotifyControllerGuideChanged` boundary now rejects disposed presenters
and forwarding to a disposed preview owner. An added native inline-link focus
scenario queues the real deferred notification and retires its presenter before
the dispatcher runs it.

The timing fixture also previously traversed the full visual tree and inspected
all bitmap resources every second *inside* its timed loop. Those snapshots can
force layout and create WinRT wrappers/finalization work. They are now taken only
before/after each timed motion pass; external process sampling remains outside
the UI thread. Further comparison uses the same optimized Release validation
binary without trimming or ReadyToRun (shipping keeps both). Raw Rendering event
cadence is not hardware FPS: the synthetic motion driver and on-demand rendering
also affect callback frequency. Evaluate input-loop stalls, publication timing,
correctness and retention rather than inferring smoothness from event count alone.

### Foreground-controlled isolation and startup ordering

`performance/clean-low` and `performance/clean-normal` used the same Release
validation executable, actual foreground ownership, and no visual-tree inspection
inside timed motion. Both passed navigation and lifetime checks. Input-loop p95
remained about 83–86 ms and Rendering callback p95 about 85–91 ms; both had
roughly 120–130 ms maxima. Promoting row continuations does not explain the wider
motion pauses. These callback intervals are not hardware frame-rate measurements.

The first no-artwork run failed before scrolling with a rejected handshake.
`WidgetBridgeServer.RunAsync` subscribed to catalogue changes before sending
HelloAccepted. Progressive loading could therefore write CatalogChanged as the
first frame. A real named-pipe regression (`BridgeHandshakeScenarios`) reproduced
this against the original server. The host now subscribes after acceptance and
then applies the monitor's atomic snapshot. Existing revision checks reject an
older snapshot if a newer callback wins. No client-side skipping of unexpected
frames and no unbounded pre-handshake notification queue were introduced.

Applicability: all presentation clients share this server/transport; no shipping
widget, template, or SDK example needs widget-level handshake plumbing. Catalogue
monitor catch-up, revision coalescing and cancellation behavior remain relevant
regression checks. This fixes a reproduced startup defect; it is not evidence
that startup was the cause of the user's scrolling pauses.

The performance runner now records the selected fixture version and each selected
file's digest, plus the Bridge assembly and catalogue-state digests. Earlier runs
hashed the initial fixture archive although the isolated catalogue selected
0.2.146.3. Those comparisons used the same selection, but their archive digest
must not be presented as proof of the loaded payload.

### Artwork isolation and runtime trace

The corrected Bridge passed the handshake regression (previously red) and all
17 catalogue regression scenarios. `performance/clean-normal-no-art-handshake`
then completed all scrolling, navigation, query-replacement and retirement checks.
With only one background decode and no poster decodes, the first pass still had
input-loop p95 90.27 ms/max 119.36 ms and Rendering callback p95 92.58 ms/max
129.11 ms. Artwork is therefore not sufficient to explain this fixture's pauses.

`performance/no-art-runtime-trace` captures 45 seconds of managed stack sampling,
GC and pipeline timings. Fourteen GC collections occurred, with measured stop-the-
world intervals up to 37.82 ms. Sampling also creates runtime suspension events;
those must not be counted as GC. UI-thread managed samples included roughly
1.04 seconds in row application and 0.26 seconds in repeated parent-snapshot
validation. PlatformInputPump.Read accounted for about 22 ms, so this capture does
not support blaming synchronous controller polling. Most sampled wall time is
unresolved native time, which includes waiting and is not equivalent to CPU use.

Six range operations supplied data in median 88.44/max 112.70 ms and fully
published in median 95.13/max 134.04 ms. Actual Normal-priority row continuation
waits were median/p95/max 0.15/0.39/0.48 ms across 35 callbacks. This verifies that
the changed lane promptly publishes this no-artwork workload, while broader
scrolling stalls remain. It is not an artwork-enabled before/after claim.

Windows Performance Recorder refused CPU recording with 0xc5585011 (insufficient
profiling privilege); it did not start a recording. A plain native GridView
baseline and separate timer-completion/UI-continuation timestamps are being added
to isolate the remaining native/control/scheduling component without changing
shipping input or rendering policy.

### Reproducible Playnite response-crash correction

The local correction is now wired into `Build-BridgePackage.ps1` as package
1.5.3.2. `bridge/source.json` pins the patch digest and both target-source digests.
The package records that provenance and fails if either source or patch differs.
Application is idempotent; fresh-source and already-patched builds both passed.
The durable net462 fixture compiles the real pinned HTTP handler and tests an
aborted committed response, later healthy request, auth rejection, preflight,
missing route, route exception, serialization exception and invalid image data.
Both package builds passed these checks. No Playnite addon or user package was
installed/replaced, and preferences, authentication and network settings were not
changed. Source is prepared; live addon acceptance remains unverified.

### Timer/control isolation

`performance/native-grid-baseline` uses the same validation shell/foreground path
but a plain native GridView with 183 strings and a fixed native template; no Bridge
process is started and no widget renderer, indexed leases or artwork is involved.
Across three passes, input-loop p95 was 31.8–31.9 ms; timer completion p95 was
31.35–31.37 ms and UI-continuation p95 1.15–1.20 ms. There were still 6–10 isolated
intervals over 50 ms per pass, with 595–722 ms maxima. Only about 6 MB was allocated
by the final pass with no new Gen2 collections. This establishes occasional stalls
in the simpler control baseline too, but does not identify their cause.

The same instrumented binary in `performance/widget-timer-no-art` passed all
navigation/lifetime checks. Input-loop p95 was 80.66/77.23/80.93 ms, timer completion
p95 31.39/31.40/31.36 ms, and UI-continuation p95 53.05/46.68/58.60 ms. The regular
extra delay is therefore after timer completion in this workload, not explained
by the 16-ms timer rounding to roughly 31 ms. No production timer resolution or
scheduler policy was changed. No additional third-party module outside Windows,
.NET, WindowsApps and the candidate directory was found in that running fixture;
that check alone cannot exclude external hooks or drivers.

Additional opt-in stages `indexed-row-apply` (11) and `indexed-row-retire` (12)
measure synchronous native binding work that occurs after range publication.
Retirement timing covers the synchronous detach/start-disposal boundary, not
completion of asynchronous cleanup. The reader accepts the added numeric stage
IDs; no content, path or identifier was added to the trace payload.

The 1.5.3.2 setup regression run passed 26 tests, failed 0, skipped 3 explicit
live opt-ins. It did not replace the user's reverted Playnite widget or addon.

`performance/row-cost-trace` completed the scrolling/navigation/lifetime checks.
Its 45-second numeric-only trace measured 251 row applications: median/p95/max
1.43/2.13/26.60 ms. Twenty synchronous retirements measured 0.36/0.48/0.54 ms.
Actual Normal publication continuation p95/max stayed 0.32/0.35 ms. These costs
must not be used to justify a speculative row-lifecycle rewrite: per-row managed
application/retirement does not explain the whole recurring UI-continuation gap.
Next investigation should focus on native layout/rendering/dispatch between
callbacks, ideally with native stacks once profiling access is available. Keep
all current ownership and validation checks; do not introduce arbitrary delays,
forced collection or blanket high-priority work.

Checkpoint: the targeted numeric pipeline test passed (1/1) after adding stages
11–12; all test-owned frontend/Bridge processes and recordings are closed. The
latest optimized validation build is `artifacts/input-discord/grid-stutter/row-validation`.
A new trimmed/ReadyToRun production candidate with all of these changes has not
yet been published or launched; do not describe validation binaries as that final
candidate. The scrolling goal remains active and unresolved. No commit, merge,
push, release, installation or user-package rollback reversal occurred here.

### Native stack evidence without system profiling privileges

A task-local x64 sampler in `artifacts/input-discord/grid-stutter/native-sampler`
reads only the isolated frontend's UI thread context and up to 128 KiB of its
stack. It resumes the thread before StackWalk/symbol work, admits only frontend
executables under this investigation's artifact directory, and performs no target
memory writes or code injection. `performance/native-stack-widget` passed the
ordinary scrolling/navigation/lifetime checks. Across 376 samples collected over
12 seconds, mean/max suspension was 42.76/81.00 microseconds, with no read failures.
Unwinding stops being reliable at JIT-managed frames; unknown tails are not evidence.

Matching PDBs for Microsoft.UI.Xaml, CoreMessagingXP and UIAutomationCore were
retrieved from Microsoft's public symbol server and resolved offline. Of 376
samples, 31 included UI Automation, specifically incoming cross-process
`RemoteUiaNodeStub::Incoming_BulkFetch`, `BulkFetch::Traverse`, pattern/property
fetch and native XAML automation peers. Other samples mostly show the ordinary
GetMessage loop or CoreMessaging's signaled-wait drain. This is sampled wall time,
not CPU utilization, and does not identify the client or prove that UI Automation
accounts for every pause. Symbol-less export names initially printed by the
sampler were approximate; use `resolved-stacks.json` for resolved native frames.

A validation-only RPC-attribution probe returned RPC_S_NO_CALL_ACTIVE (1725),
so it could not identify the UIA client. It was removed, including its custom root
automation peer. Its one query-replacement assertion failure is retained in
`performance/automation-attribution`; the unmodified peer passed the focused
reproduction and subsequent full runs. No shipping automation behavior was changed.

`performance/external-dispatcher-queue` records the loaded dispatcher synchronization
context and a bounded background-to-Normal callback probe. Before the helper reset,
2,919 callbacks had median/p95/max waits of 0.16/62.45/125.35 ms, with 241 above
50 ms. `performance/post-automation-reset` used identical frontend and Bridge hashes
and the same selected payload files (compare Path/Sha256 values, not JSON property
ordering). It recorded 3,162 callbacks with p95 0.51 ms and seven over 50 ms. All
three motion passes had zero UI-continuation intervals over 50 ms. The stale helper
PID was no longer present after the supported kernel reset.

`performance/clean-artwork-final` then passed with artwork enabled. Its 35-second
pipeline trace measured actual publication continuation median/p95/max waits of
0.02/0.51/0.51 ms; 146 image operations resolved, decoded and published without
failure. The default-profile candidate subsequently started its real Playnite
worker and completed catalogue discovery. Automated UIA inspection is a smoke
check, not a timing run; physical controller activity arrived during this smoke
check, so automation was stopped and the candidate left to the user. The user's
smooth-scrolling confirmation is the final physical acceptance evidence.
