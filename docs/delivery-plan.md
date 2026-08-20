# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through the DLV-269 physical acceptance is
preserved in the
[2026-08-19 00:01 snapshot](history/delivery-plan/2026-08-19T00-01-42-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
evidence only; this file is the sole authority for current work.

## Current baseline and active task map

- Accepted production/test integration baseline on main is `683af77`. It
  integrates DLV-269 production as `0fc3c2d`, `32b6cea`, `f79ebba`, and
  `bb4ee08`, plus focused tests `683af77`, over accepted DLV-272 baseline
  `8100bd7`.
- Platform correction `2d09fb6` is source-reviewed and physically accepted on
  PID 98812. The executable at
  `C:\Users\dwive\AppData\Local\Temp\gba-dlv269-2d09fb6\src\OverlayHost\out\Release\OverlayHost.exe`
  has SHA-256
  `6683DAED8AA6941689A15B77BBCFDCEC6906B166E11B832805C4E4CD94673295`;
  its 104 accepted non-native launch files are byte-identical to the accepted
  graph. The user accepted Spotify Playlists focus stability and Games & Apps
  first-session right-stick behavior on 2026-08-19.
- Widgets correction DLV-265 remains saved at clean tip `13bd971` plus
  reconciliation `5fd1a06`, with immutable Spotify 0.3.3 installed and the
  existing Client ID/account state unchanged. It resumes only after accepted
  DLV-271 integration.

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-273 remains retired. DLV-275 correction `2b609fc` / exact candidate `e318ffb` is rejected and unintegrated: ordinary stale-request cancellation still taints and replaces the shared bridge. Correct that generic host cancellation boundary from accepted main plus the preserved DLV-274/275 chain. Do not test, integrate, or start DLV-271 before user acceptance. Never push. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | Preserve saved DLV-265 and installed Spotify state. Do not resume, test, integrate, reinstall, replace configuration, or reset before accepted DLV-271 integration. DLV-270 follows accepted DLV-265; DLV-248 remains deferred. |

## Execution, review, and architecture rules

- Local `main` is the reviewer-owned integration branch. Review and integrate
  only accepted DLV commits. Implementation tasks never edit reviewer-owned
  documents and never push; the planner never authors implementation code.
- Follow physical-first ordering where an assignment says so: coherent
  production/build, user verdict, then focused tests. A production-only
  candidate is not integrated before the verdict and its post-verdict coverage.
- Launch a coherent native Release only when accepted integrated production or
  runtime artifact inputs change. Retain the accepted running candidate when
  the only delta is tests or reviewer documents and it already contains the
  integrated production commit.
- Keep verification proportional. Use the affected Tier 1 suites once after a
  coherent change and only the smallest linked Tier 2 group when a changed
  boundary requires it. Do not substitute Tier 3 unless explicitly required.
- The native overlay is the sole production presentation path. Avalonia/AVP is
  closed failed-experiment history: do not resume, message, launch, integrate,
  delete, or otherwise touch it without a new explicit user decision.
- The native host owns HWND, graphics, semantic admission, layout, clipping,
  focus, input, accessibility, and scroll offset. Widgets own private data and
  item materialization. Do not add package-specific behavior to core host/SDK
  layers or create duplicate focus, scroll, render, cache, or lifecycle owners.
- Input, focus, free-scroll, and re-entry bind to one fully admitted interaction
  generation. The prior committed generation remains authoritative until its
  replacement commits atomically; ordinary refresh must not create a transient
  interaction gap or reacquire focus.
- Sandboxed and full-trust Community widgets use the same bounded host-admission
  path. Full trust grants no overlay HWND, render, input, or focus authority.
- WidgetRail is pre-release and single-user. Do not add migration or old-version
  compatibility code without a later explicit user decision. Preserve external
  provider data, credentials, accounts, applications, and user files.

## Accepted integrated platform deliverable — DLV-269: viewport-driven paged-scroll prefetch

Owner/baseline: platform lane from integrated main `8100bd7`. This is a generic
native-host paged-Scroll correction using the existing public pagination action
IDs, threshold, retained offset, rendered geometry, focus graph, and bridge
action route. Spotify playlists and playlist tracks are the required physical
proof, not a source of package-specific host behavior.

Replace focused-row-driven adjacent-page admission with viewport-driven
prefetch. A paginated Scroll must request its before/after page when its rendered
visible range reaches the authored threshold, regardless of whether the offset
was changed by right stick, pointer/UIA scrolling, D-pad/left-stick focus follow,
or collection-anchor reconciliation. D-pad and left-stick navigation must never
be consumed merely to start, join, or wait for an adjacent load. Preserve the
current semantic focus and scroll anchor while an ordinary background prefetch
is pending; do not force focus to the first item in the arriving page. If the
user reaches a still-unloaded edge, retain the current valid focus and offset
until content arrives, then let the next ordinary navigation event proceed.

Keep admission single-flight and bounded per exact Scroll/action/cursor edge.
Repeated layout, paint, focus, or right-stick frames at the same edge must not
emit duplicate worker actions, IPC refresh loops, or unbounded retries. A
successful append/prepend, cursor change, movement away from the threshold,
route change, widget/input-scope change, or terminal failure must update or
clear that edge authority deterministically. Retain the existing widget-owned
load/error state and the host's focus, scroll, accessibility, renderer, and
action authorities. Do not introduce polling, service-specific branches, a
second collection model, or true presentation-tree virtualization here.

Production correction `2d09fb6` preserves the bounded latches, resolves the
exact deepest Scroll from semantic ancestry and committed geometry, retains
focus through refresh, accepts first-current-render input, and diagnoses every
drop/focus change. Source review found no new authority or widget-specific
behavior. The user physically accepted PID 98812.

Focused test commit `7641b45` passed WidgetInteractionSession 84,
ControllerNavigation 122, FocusNavigation 47, WidgetSurfaceFocus 24,
SliderInteraction 2,096, PressedInteraction 29, AccessibilityProvider 168, and
TextEntryModal. Deterministic evidence improved the rejected nine actions in
about 12 seconds with no completion to one adjacent action and 45 ms threshold-
to-visible completion. The accepted chain is integrated on main as production
`0fc3c2d`, `32b6cea`, `f79ebba`, and `bb4ee08` plus tests `683af77`. PID 98812
already contains the integrated production tree, so it is intentionally retained
without a tests-only rebuild/relaunch. No Tier 3 or packaged-host route was run.

Stop for a required public SDK/protocol change, inability to derive a stable
visible range from the existing renderer result, a second scroll/focus/action
owner, widget-specific native behavior, destructive state action, substantial
conflict, or missing accepted DLV-268/DLV-272 baseline. Never push.

## Retired failed platform deliverable — DLV-273: extract committed widget content presenter

Original scope (now retired): platform lane from accepted DLV-269 main `683af77`
and before DLV-271 changes collection presentation. Extract the declarative widget-
body painter, renderer/cache lifetime, committed visual checkpoint, incremental
damage plan, last render result, and declarative-motion state into one
`WidgetContentPresenter`, with a before/after authority map. It receives one
immutable, already-admitted `WidgetPaintFrame` containing exact presentation
and interaction generation, geometry, appearance, focus/slider/pressed
overrides, retained offset, and layer target; it returns one typed paint outcome
containing render result, accessibility regions, committed checkpoint, motion,
damage, and diagnostics. Keep lifecycle, session admission, focus mutation,
input, chrome, placement, HWND, and device-recovery arbitration in their
existing owners.

The previous complete frame remains authoritative until the replacement frame
is admitted and committed atomically; no ordinary same-widget refresh may
publish half-old geometry/semantics or a transient inert interaction gap.
Follow physical-first ordering with Settings, Audio, Games & Apps, and live
Spotify full/bounded updates, device/resource refresh, focus/scroll continuity,
and no flicker/stale pixels. After acceptance run focused renderer, semantic-
churn, shared-geometry, incremental/no-raster, retained/failure-retained,
accessibility-projection, and device-loss coverage. No Tier 3 or protocol change.

Stop for direct `OverlayState`/session/HWND back-references, a graphics service
locator, second render checkpoint, protocol work, interaction-state ownership,
broad `main.cpp` rewrite, substantial conflict, or changed product behavior
beyond atomic committed-frame handoff. Never push.

Rejected candidate status: production commit `b54e1e2` introduces
`WidgetContentPresenter` as the sole renderer, image-cache, last-render-result,
committed-checkpoint, incremental-plan, and declarative-motion owner. The host
retains lifecycle/session, focus/input, accessibility-provider, placement/HWND,
device recovery, and final graphics-commit authority; presenter/UIA state is
published only after successful DirectComposition commit or legacy `EndDraw`.
The production-only native link succeeded; the unchanged managed publication
restore failed, so no generated managed output was accepted from that run.
Instead, the staged candidate at
`C:\Users\dwive\AppData\Local\Temp\gba-dlv273-b54e1e2\src\OverlayHost\out\Release`
uses the new `OverlayHost.exe` SHA-256
`C3D7D473CEDE7319C05708831251F598EDEDA365940CB601100FDB5D5EB5B38B`,
the accepted `OverlayPlatformInterop.dll` SHA-256
`0436948A5772EDCE861F316623DB429FF087F93650D8488BB438145B7B00D915`,
and all 104 accepted non-native launch files byte-identical. Exact prior PID
98812 was gracefully closed after path/class verification; PID 86140 launched
visibly with DirectComposition, foreground input, and successful Settings
admission/commits. The user then reproduced a focus-authority failure: enter any
Settings submenu such as Overlay, press Back to restore the Settings root, then
press Down. The root remained visibly presented but the host returned to tray
navigation instead of moving to the option below Overlay. The log captured
Settings presentation sequences 6 and 7 with `input-owner=tray`,
`visual-focus=none`, and `semantic-focus=tray:settings`, proving that restored
Settings content and controller focus authority diverged. PID 86140 was
gracefully closed after exact path/class verification. The last accepted
DLV-269 executable was restored visibly as PID 69660 from its immutable stage;
its SHA-256 is
`6683DAED8AA6941689A15B77BBCFDCEC6906B166E11B832805C4E4CD94673295`.
The user repeated the same submenu-Back-then-Down sequence on restored PID
69660 and confirmed the older accepted build does not have the issue. This is
therefore an exact DLV-273 regression rather than inherited Settings behavior.

Rejected correction status: production-only commit `e98566e` adds an exact-
authority `PendingCommittedBackFocusHandoff` and restores the remembered root
focus only after the matching newer root frame commits. Source review confirmed
that lifecycle, focus, input, renderer, checkpoint, session, and HWND authority
remain in their existing owners. The native Release build succeeded and the
candidate at
`C:\Users\dwive\AppData\Local\Temp\gba-dlv273-e98566e\src\OverlayHost\out\Release`
contains corrected `OverlayHost.exe` SHA-256
`8D0EDA5D35C74414ADAFF27D4BDCCAE2DD39E8873A5A2D36A3D641E0F008A8DD`,
the accepted interop DLL, and all 105 non-executable files byte-identical to
the accepted graph. It launched visibly as PID 73156. The user rejected it:
after returning from a Settings submenu, Up did nothing visibly, the first Down
was consumed without visible action, and only the second Down moved to the next
option. The log proves the committed handoff first restored
`input-owner=widget` with the remembered root focus, then the next direction
published the same root with `input-owner=tray`, `visual-focus=none`, and
`semantic-focus=tray:settings`. Thus the correction restores focus ownership
but does not apply the first post-Back directional intent against the committed
root. The exact rejected PID had no enumerable HWND while hidden and was
boundedly stopped after path and hash verification. The immutable accepted
DLV-269 executable was restored visibly as PID 38672 with its accepted SHA-256.

Final correction candidate: production-only commit `dcd58e0` changes
`src/OverlayHost/main.cpp` only. It extends the exact Back handoff authority with
one optional direction plus committed-root sequence/focus state. A direction
arriving before the matching root commit is retained once while repeats are
dropped; after the exact successful commit, or for the first direction arriving
after it, the token is retired before the existing navigation path applies that
direction. Existing lifecycle and authority invalidations discard the token.
Source review found no general input queue, second input/focus owner,
package-specific branch, presenter authority leak, or change to the renderer,
checkpoint, session, accessibility, HWND, or device-recovery owners. Release
build succeeded; tests and packaging were skipped. The candidate at
`C:\Users\dwive\AppData\Local\Temp\gba-dlv273-dcd58e0\src\OverlayHost\out\Release`
has `OverlayHost.exe` SHA-256
`7A845F0B5C03A3887E1B72457FC92A179795927F3E8F5BE09F76959FB62C8E66`,
rebuilt `OverlayPlatformInterop.dll` SHA-256
`9C04A1730F84249EECF688915F7D30D916B2F8DA4F9FB22742A8B4B52BAFC906`,
102/102 runtime files byte-identical to the accepted graph, and identical
catalog/notices. Exact accepted PID 38672 had no enumerable HWND while hidden
and was boundedly stopped after path/hash verification. The candidate launched
visibly as responding PID 105876. Required verdict: Settings -> Overlay or any
submenu -> Back, then the first Down must advance immediately; first Up must
execute and may remain only at a genuine boundary. Repeat with another submenu
and confirm ordinary Settings, tray, and multi-widget navigation remain intact.

Rejected verdict: the user found both Up and Down nonfunctional after returning
to the Settings root. Directional navigation resumed only after another button
event; A, X, and B all recovered it. This proves the failure is not a special B
exit path and rejects direction retention/replay as the correction model. The
log records `Committed nested Back direction` during the matching root commit,
immediately followed in reproduced cases by a root paint with
`input-owner=tray`, `visual-focus=none`, and `semantic-focus=tray:settings`;
later frames may republish widget focus, but the physical controller remains
directionally blocked until the unrelated button event. PID 105876 was
boundedly stopped after exact path/hash verification. Immutable accepted
DLV-269 was restored visibly as responding PID 36780 with accepted executable
SHA-256
`6683DAED8AA6941689A15B77BBCFDCEC6906B166E11B832805C4E4CD94673295`.

User decision: roll back and retire DLV-273. The accepted pre-refactor DLV-269
build does not reproduce the Settings regression, while the extraction and both
handoff corrections do. Moving renderer/checkpoint publication into the
presenter split an interaction-ordering boundary that had been behaviorally
atomic in the accepted host; the attempted post-commit tokens introduced new
stale controller states. None of `b54e1e2`, `e98566e`, or `dcd58e0` was
integrated into main, so no production revert commit is needed. Preserve them
only as rejected branch history. Do not resume, repair, cherry-pick, test, or
integrate this refactor. Any future host decomposition requires a newly approved
smaller seam that leaves committed checkpoint and interaction publication in
the accepted owner and begins with deterministic Back/focus regression proof.

## Ready platform deliverable — DLV-274: image-cache retention without icon churn

Owner/baseline: platform lane from accepted main `683af77`, with DLV-273 retired,
and before DLV-271. Keep policy and lifetime in the existing
`RemoteImageCache`, declarative renderer, and accepted host cache owner; do not
extract or depend on `WidgetContentPresenter`, add Games & Apps branches, or
create widget-owned native caches.

Correct the reproduced global 32-entry bottleneck. On accepted PID 56240,
Games & Apps used only about 141 KiB for 32 small icons, yet revisiting and
scrolling the list raised bitmap creates from 32 to 48 and evictions from 0 to
16 with no resource-domain invalidation. Retain explicit decoded-byte and GPU
byte budgets, but decouple retained ready entries from pending/request
admission. Bound concurrent fetch/decode work and metadata/object count
separately; select measured entry and byte limits that retain ordinary visible
app grids plus shared chrome/widget artwork without unbounded URL, COM, CPU, or
GPU growth.

Follow physical-first ordering. Build a tests-skipped Release with cache stats
that distinguish ready entries, pending work, byte pressure, count pressure,
eviction reason, widget/resource domain, and cold process/resource restart.
The user must open Games & Apps, wait for icons, switch across image-bearing
widgets, return, fast-scroll away/back, and confirm that a stable resident host
does not visibly reload the complete icon grid. Record before/after creates,
evictions, decoded/GPU bytes, host memory, and activation latency. After the
verdict add focused LRU, request-backpressure, byte/count-pressure, trusted-
artwork revision, device-loss, shutdown, and multi-widget retention coverage.

Stop for unbounded retention, per-widget native cache authority, weakened
untrusted-image bounds, a public SDK/protocol change, device/resource lifetime
regression, substantial conflict, or missing accepted DLV-269 baseline. Never
push.

Current production candidate: commit `bf6d524` changes only the two existing
cache owners and their typed diagnostics across five production files. Decoded
image policy is 320 bounded metadata entries, 256 ready entries, 32 pending
requests, and the unchanged 32 MiB decoded-byte budget. Device-compatible bitmap
policy is 256 entries under the unchanged 32 MiB GPU budget. Count, byte,
superseded-artwork, capacity-rejection, resource-domain, and generation counters
remain reported through the existing paint diagnostic; no cache/lifetime owner,
widget-specific branch, presenter dependency, protocol change, or DLV-273 code
was introduced. Source review and `git diff --check` passed. The exact-commit
native Release build succeeded with tests and packaging skipped. A coherent
stage at
`C:\Users\dwive\AppData\Local\Temp\gba-dlv274-bf6d524\src\OverlayHost\out\Release`
contains `OverlayHost.exe` SHA-256
`B29F724F9497C23EA44E36926613A0D5B231667B471DC8D8B8A195F9E158DBF2`,
`OverlayPlatformInterop.dll` SHA-256
`E9EEB28E3F13277E83D8478316FAA78D303F79939AAC663A8E1D7697D1098FAE`,
102/102 runtime files byte-identical to the accepted graph, and identical
catalog/notices. No tests, packaging, launch, integration, or push occurred.
The user explicitly approved candidate launch. Exact accepted DLV-269 PID 36780
was stopped after path/hash verification, and the coherent candidate launched
visibly as responding PID 59388 from the staged path with the exact executable
hash above. Startup diagnostics confirm process-lifetime policy
`metadata-limit=320`, `ready-limit=256`, `pending-limit=32`, unchanged decoded-
byte limit 33,554,432, bitmap-entry limit 256, and unchanged bitmap-byte limit
33,554,432. Await the physical Games & Apps revisit/fast-scroll verdict and
before/after cache counters; do not run tests or integrate first.

Rejected physical verdict: switching between Spotify and Games & Apps made each
surface visibly reload artwork, and right-stick scrolling was often unavailable.
The user confirmed the visible reloading also occurs on the currently accepted
DLV-269 build, so it is inherited behavior that `bf6d524` failed to correct, not
a new reload regression introduced by the candidate.

Two observed effects must remain separate. The artwork/loading churn exhausted
both unchanged 32 MiB aggregate image budgets with large Spotify artwork. Logs
reached 73 decoded byte-pressure evictions and 68 GPU bitmap byte-pressure
evictions, with zero count-pressure evictions; repeated image completions
produced full/checkpoint refresh churn and right-stick drops such as
`retained-refresh-gated` and `renderer-checkpoint-or-boundary`. This proves
`bf6d524` fixed the 32-entry ceiling but retained an aggregate byte ceiling below
the measured cross-widget working set.

The earlier captured detail-to-list transition remains a real but separate B
shortcut trace: authored sequence continued `19 -> 20 -> 21`, and
`spotify.playlist.back` deliberately cleared `_playlistSelection`. The user's
new menu-only reproduction does not enter playlist detail and disproves that
shortcut as the explanation for the reported full reload.

On corrected candidate PID 26848, Spotify reached its Playlists surface and
authored sequence 21. Returning after selecting other tray widgets first painted
the retained sequence-21 checkpoint, then blocked about 2.9 seconds while a new
application was established and admitted sequence 1, followed by 2, 3, and 4 on
the initial Player surface. `WidgetWorkerServer` owns that sequence as an
instance field, so `21 -> 1` proves application recreation rather than a route,
focus, renderer, or image-cache refresh. At the same moment decoded retention
was 57/57 ready entries and about 10.4 MiB, GPU retention was 52 entries and
about 7.3 MiB, and both caches reported zero byte/count evictions. Other widget
requests were still completing through the same bridge session, so the evidence
isolates Spotify application lifetime loss rather than a whole-bridge restart.
The installed 0.3.3 manifest declares `keep-alive`; this behavior violates that
contract. Current diagnostics report neither the terminal worker exit code nor
the lifecycle request/exception that ended it, so the trigger below process
exit is not yet proven and must not be guessed. DLV-274 changes no lifecycle or
worker code and cannot be accepted as the reload correction.

Correct only the measured aggregate cache budgets while preserving the accepted
owners and security bounds. Keep the existing untrusted download, dimension,
pixel, and per-image decoded safety ceilings unchanged; separate them explicitly
from bounded process-wide decoded/GPU retention budgets. The measured Spotify
working set is about 59 MiB for 36 full-size images before Games & Apps and shared
artwork, so use a documented bounded 96 MiB total decoded budget and 96 MiB total
GPU bitmap budget with the existing 320 metadata, 256 ready/bitmap, and 32
pending/request limits and LRU eviction. Do not add size-aware protocol, package-
specific behavior, lifecycle/session changes, checkpoint/input/scroll changes,
another cache owner, or unbounded retention. Build once, commit production only,
and stop for source review and another physical verdict; no tests, launch,
integration, or DLV-271 in the lane.

Corrected production candidate: commit `4645f40` changes the same five
production files with 22 insertions and 8 deletions atop preserved `bf6d524`.
`RemoteImageCache` now names the unchanged 32 MiB per-decoded-image admission
ceiling separately from its bounded 96 MiB process-wide decoded LRU budget.
The declarative renderer likewise preserves a 32 MiB per-bitmap ceiling while
raising only its process-wide GPU bitmap LRU budget to 96 MiB. Metadata remains
320 entries, ready decoded images and GPU bitmaps remain 256 entries, and
pending requests remain 32. Existing download, dimension, pixel/overflow,
resource-generation, invalidation, and LRU boundaries are unchanged. The diff
contains no lifecycle/session, checkpoint/input/right-stick, protocol,
package-specific, DLV-273, or additional cache-owner changes. Independent
source review and `git diff --check` passed.

The exact-commit native Release build succeeded with tests and packaging
skipped. A coherent stage at
`C:\Users\dwive\AppData\Local\Temp\gba-dlv274-4645f40\src\OverlayHost\out\Release`
contains `OverlayHost.exe` SHA-256
`2862540B62A47905CD4693C817595ECCF45B1BEF90858B165E980D1320140DC1`,
`OverlayPlatformInterop.dll` SHA-256
`200D887A7DF62F878F79968FD2D10AB84BC3398F2FECBC3A3792EE1EF89A6B93`,
102/102 runtime files byte-identical to the accepted graph, and identical
catalog/notices. No tests, packaging, integration, or push occurred. After the
user explicitly requested launch, the exact staged candidate started visibly as
responding PID 26848 from the immutable path above. Startup diagnostics confirm
`decoded-entry-byte-limit=33554432`, `decoded-cache-byte-limit=100663296`,
`bitmap-entry-byte-limit=33554432`, and
`bitmap-cache-byte-limit=100663296`. The previously accepted process was no
longer running when this launch began. Await the Spotify/Games & Apps revisit
and right-stick physical verdict. The menu-only Spotify reproduction above is a
rejected physical verdict for the complete no-reload objective. Preserve this
candidate and its measurements; do not test or integrate it before DLV-275.

## Assigned platform correction — DLV-275: keep-alive worker lifetime provenance and preservation

Owner/baseline: platform lane from accepted main `683af77`, before DLV-274
reconsideration and DLV-271. Reconcile only the already reviewed DLV-274 cache
candidate when needed for the final combined physical comparison; do not treat
its cache-only commit as lifecycle authority.

Reproduce the exact menu-only route: open Spotify, enter Playlists without
opening a playlist, switch through the tray to Games & Apps, then return to
Spotify. First make the terminal boundary observable with bounded production
diagnostics sufficient to distinguish worker process exit, cooperative unload,
registry retirement, bridge-session replacement, and lifecycle-request failure.
Record worker start ordinal/PID, lifecycle request and completion, exit code and
failure reason, residency mode, registry generation, and bridge session
generation without exposing paths, credentials, OAuth material, provider data,
or raw exception text on the user surface.

Then correct the proven owner so a `keep-alive` application entering Background
remains the same worker/application instance and preserves its authored route,
private collection state, and monotonic presentation sequence on reactivation.
Do not paper over process recreation by persisting Spotify route state, add a
Spotify-specific host branch, weaken crash-loop or residency admission, convert
keep-alive to unload/resume, or silently swallow protocol/lifecycle corruption.
If a normal cancellation is escaping a lifecycle hook, contain it at the
generic runtime boundary only with explicit normal-versus-fault semantics and
deterministic proof; if another owner is responsible, fix that owner instead.

Follow physical-first ordering. Produce one tests-skipped coherent Release from
a clean production commit. The user must prove Spotify Playlists returns
immediately without a loading screen or route reset while Games & Apps also
retains its prior surface, and the diagnostics must prove the same worker start
ordinal/PID and increasing Spotify presentation sequence across the switch.
After acceptance add the smallest deterministic process/runtime/bridge coverage
for Background cancellation, unexpected exit, keep-alive reactivation, route
retention, repeated rapid switching, and failure diagnostics, then review and
integrate the accepted DLV-274 and DLV-275 chain in order.

Stop for an unproven trigger, lost crash visibility, broad persistence or public
protocol work, provider-specific host behavior, changed residency semantics,
credential/private-state exposure, substantial conflict, or a fix that requires
the retired DLV-273 presenter. Never push.

Current production candidate: platform commit `baf7bd4` changes nine production
files. The generic SDK lifecycle owner now contains only an
`OperationCanceledException` whose token exactly equals the canceled active
lifetime that just ended, while the host transition token remains live. The
worker request processor otherwise continues to treat cancellation as terminal,
so host cancellation, timeout, protocol/runtime faults, and unexpected exits
remain visible. Source inspection confirms the prior failure chain: the SDK
canceled the active lifetime before deactivation, an awaited active task could
surface that exact cancellation from `OnDeactivatedAsync`, and the worker's
request processor excluded all `OperationCanceledException` values from its
ordinary error reply path, allowing the processor and worker application to
terminate normally. No Spotify route persistence, package-specific branch,
residency-policy change, or public protocol change was added.

The same commit adds bounded observational diagnostics through the existing
runtime/bridge log path: bridge session generation/PID, registry generation,
residency mode, worker start ordinal/PID, lifecycle request/completion/failure,
cooperative unload, registry retirement, exit code, and closed failure code.
Diagnostics contain no paths, provider payloads, credentials, OAuth material,
or raw exception text and cannot own worker lifetime. Independent diff review
and `git diff --check` passed. The platform worktree is clean at `baf7bd4`; no
tests, launch, integration, or push occurred there.

For the required combined physical comparison, the planner created a clean
detached candidate from reviewer main `1bc284e` and cherry-picked original
DLV-274 commits `bf6d524`/`4645f40` followed by DLV-275 `baf7bd4`. The resulting
exact candidate chain is `76a93eb`, `3d12c14`, and `cd1144f`. A full packaged
Release build with tests skipped succeeded; 102 runtime files were republished
from the same source tree. The immutable stage is
`C:\Users\dwive\AppData\Local\Temp\gba-dlv274-275-candidate\src\OverlayHost\out\Release`.
`OverlayHost.exe` SHA-256 is
`E123FE1967A91F04923D5BF3273AE1638F000627A7C08D0DA953FFB920CEF9FF`;
`OverlayPlatformInterop.dll` SHA-256 is
`5595CE17C6C14C88737E08952AD2B6096CA81013F1102CA2503DFE2FB7F01224`.
Exact prior DLV-274 PID 26848 was gracefully closed only after executable-path,
PID, and hidden `WidgetRail.OverlayHost` window ownership verification. The
combined candidate launched visibly as responding PID 30536. Startup diagnostics
confirm bridge session 1/PID 11688, typed worker lifecycle events, and the
expected 32 MiB per-image plus 96 MiB aggregate decoded/GPU cache limits.

Required verdict: open Spotify, enter Playlists without opening a playlist,
switch to Games & Apps, then return to Spotify. Spotify must return immediately
to Playlists with no loading screen or initial Player reset; Games & Apps must
retain its prior surface. After the user performs the route, inspect the new
lifetime diagnostics for the same Spotify registry generation, worker start
ordinal/PID, completed Background/reactivation transitions, and monotonically
increasing authored presentation sequence. Do not run tests or integrate first.

Rejected verdict and corrected trigger, 2026-08-19: the user reproduced the
full Spotify restart without visiting Games & Apps. The lifetime log proves
bridge session 1 first kept Spotify registry generation 2, worker start 1/PID
29412 alive through its Background transition. Starting YouTube Music then
ended only its worker, start 1/PID 28440, with exit code 1 and
`unexpected-exit`. Its Visible lifecycle request failed as `request-cancelled`;
the bridge immediately retired Settings and keep-alive Spotify, whose worker
then exited cooperatively with code 0. Bridge session 2/PID 6908 started about
0.28 seconds later and recreated Spotify as registry generation 1, worker start
1/PID 26740. This is a real whole-bridge replacement, not artwork eviction,
route navigation, or a Games & Apps lifecycle effect.

Source review establishes the propagation owner. A worker exit faults the
widget lifecycle request. `BridgeRequestDispatcher` currently treats every
non-draining request-handler exception as session-fatal, cancels the shared
session, and `WidgetBridgeServer` disposes the complete registry before
rethrowing the first dispatcher failure. DLV-275 therefore fixed the original
Spotify active-lifetime cancellation path but did not satisfy the complete
no-reload objective. The combined candidate is rejected and remains
unintegrated.

DLV-275 correction must isolate an ordinary widget runtime/worker failure to
that widget registration. Return the existing typed failure/error response to
the host and retain crash visibility and restart diagnostics, but keep the
bridge session and unrelated registrations alive. Protocol corruption,
transport failure, invalid session-level framing, host disconnect, and genuine
bridge infrastructure failures must remain session-fatal. Do not catch every
exception indiscriminately, persist provider routes, add Spotify/YouTube Music
branches, or weaken crash-loop policy. Produce a new tests-skipped coherent
Release first. Physical acceptance requires deliberately triggering the same
YouTube Music startup failure and proving the bridge session plus Spotify
registry generation, worker PID/start ordinal, route, and presentation sequence
remain continuous; only the failed widget may enter its typed failure/restart
path. Add focused tests only after that physical verdict.

Current corrected production candidate: platform commit
`2b609fcba7a7c646803632391fba6c3a9b135b0d` changes only
`BridgeClientRegistry.cs` and `WidgetBridgeServer.cs` (137 insertions, 39
deletions). Source review confirms that worker process, admission, worker-pipe,
worker-protocol, timeout, and non-host cancellation failures are wrapped as a
typed per-widget request failure. `WidgetProcessClient` retains ownership of
the failed session and restart count; the registry entry and unrelated widgets
are not retired. Shared bridge framing/transport failures, host disconnect,
out-of-memory, and unclassified widget-keyed infrastructure faults still escape
to the session-fatal dispatcher path. Response-write failure also remains
session-fatal. No provider-specific branch, route persistence, crash-loop
weakening, public protocol change, or reviewer-document edit was added. Diff
checks passed and the implementation worktree is clean.

The planner preserved the prior immutable candidate and created a new detached
stage from `cd1144f`, cherry-picking the correction as exact candidate
`e318ffb`. A full packaged Release build with tests skipped succeeded and
republished 102 runtime files totaling 34,739,048 bytes. `OverlayHost.exe`
SHA-256 is
`981A573A964CD4F8735E62772C4275940083A6FB283BD011A65F8F4982B87FBD`;
`OverlayPlatformInterop.dll` SHA-256 is
`E10AA84F8455A8EB8E6C242B94882EDC7959F2B11AF85BE4829D8649F754EA86`;
`WidgetBridge.dll` SHA-256 is
`46B2527214CAF2F59AD9C8A4BF02EE1884B2146FE9CBB98A3570905C38643768`.
The verified prior candidate PID 30536 exited normally through `WM_CLOSE`.
The corrected Release launched visibly as responding PID 16348 from
`C:\Users\dwive\AppData\Local\Temp\gba-dlv274-275b-candidate\src\OverlayHost\out\Release`.
Startup diagnostics confirm production process ownership, 32 MiB per-image and
96 MiB aggregate decoded/GPU cache limits, and bridge session 1/PID 13440.

Required physical verdict: open Spotify and enter Playlists without opening a
playlist; then select YouTube Music to reproduce its startup failure and return
to Spotify. Spotify must return immediately to Playlists without a loading
screen or route reset. After the route, inspect diagnostics for one unchanged
bridge session plus the same Spotify registry generation, worker PID/start
ordinal, and increasing presentation sequence. YouTube Music may report its
typed failure and restart, but Settings and Spotify must not emit
`registry-retirement` or cooperative worker exit. Do not run tests or integrate
before the user verdict.

Rejected physical verdict: the user still observed a complete Spotify reload.
This run did not reproduce a YouTube Music crash. Instead, ordinary rapid tray
navigation replaced the shared bridge twice while OverlayHost PID 16348 stayed
alive. Bridge session 2 retained Spotify registry generation 1, start 1, PID
12928 through presentation sequence 13. Immediately after Network Controls
completed and its now-stale completion was dropped, every session-2 registry
was retired, all three workers exited 0 by cooperative stop, and bridge session
3/PID 26664 began. Spotify was then recreated as registry generation 2, start
1, PID 25572 and presentation sequence reset to 1. This proves a second generic
bridge-replacement path; it is not a provider route reset, image-cache eviction,
worker crash, or a failure of `2b609fc`'s typed worker-exception classification.

Source review identifies the exact owner. `WidgetSessionCoordinator` calls
`CancelSynchronousIo` on its single worker thread when an in-flight request is
superseded or revoked. That aborts the blocking named-pipe `ReadFile` inside
`WidgetBridgeClient`; `ReadFrame` records the canceled read as a tainted shared
transport. The following request calls `EnsureStarted`, which deliberately
closes that transport, terminates or joins the still-healthy bridge process,
and starts a new bridge. A stale presentation completion is therefore being
implemented as bridge-wide transport destruction.

DLV-275 remains the active correction. Preserve true transport/framing failure
as session-fatal and preserve the existing single serialized bridge request
owner, but do not use destructive synchronous-I/O cancellation to discard an
obsolete widget completion. Let the in-flight request finish and drop its
generation-stale result, or introduce an equally bounded cancellation design
that cannot taint or close the shared transport. The fix must be generic, must
not persist provider routes, and must retain the already reviewed per-widget
worker-failure isolation. Produce a tests-skipped coherent Release first;
physical acceptance now requires repeated rapid tray switching while bridge
session, unrelated registry generations/PIDs, and Spotify route/sequence all
remain continuous. Add focused cancellation/rapid-switch tests only afterward.

## Ready serialized deliverable — DLV-271: virtualized collection presentation windows

Owner/baseline: platform lane as serialized cross-layer lead after DLV-274 is
accepted and integrated on main. This is deliberate public architecture work
spanning the generic managed SDK/cursor resource, versioned protocol and
admission, bridge/runtime publication, native semantic/layout/accessibility/
render owners, and directly affected author documentation. No widgets-lane work
may edit those boundaries concurrently. Spotify is one real consumer; a
provider-free large-collection reference scenario is the deterministic proof.

Introduce one generic virtualized collection-window contract so a widget may
retain an application-scale private collection while submitting only a bounded
keyed window around the host viewport. Define explicit stable item identity,
window/cursor authority, known or unknown extent, before/after availability,
estimated versus measured row extent, request generation, stale-window
rejection, and bounded append/prepend/replace semantics. The native host remains
the sole scroll-offset, clipping, focus-follow, navigation, UI Automation,
layout, renderer, and HWND authority; the widget remains the sole private-data
and item-materialization owner. Existing eager Scroll content remains the simple
default. Do not make ordinary small widgets adopt an application framework, let
authors construct raw wire patches, expose provider-specific DTOs, or retain two
simultaneously authoritative semantic trees.

Virtual window shifts must preserve stable focus and collection anchors when
their keys remain available, never silently drop an actionable focused item,
and produce deterministic recovery when provider mutation removes it. Bound
window size, outstanding requests, request rate, cursor history, native nodes,
layout work, accessibility providers, decoded resources, and failure retries.
Off-window content must not be serialized, admitted, laid out, painted, hit-
tested, or represented as a live native accessibility node merely because it
exists in the widget's private collection. Provide accurate scroll range and
virtualized-item accessibility semantics without inventing a second
accessibility tree. Preserve complete-checkpoint fallback and last-valid-window
retention on malformed, stale, failed, or cancelled updates.

Because this changes a shared public protocol, use normal verification ordering.
First produce a before/after responsibility map and the smallest versioned
contract; then run focused managed/native Tier 1 and the smallest linked Tier 2
boundary group. A provider-free scenario with at least 10,000 stable variable-
content items must prove serialized snapshot items, native semantic nodes,
layout/paint work, accessibility providers, and host memory remain proportional
to the bounded viewport window rather than the private collection. Verify right-
stick and focus-follow scrolling, viewport prefetch, forward/backward window
shifts, dynamic insert/remove/move, compact/wide reflow, cancellation, stale/
failure retention, restart, and legacy eager-Scroll coexistence. Then build and
launch the coherent Release for the user's physical long-list verdict. Tier 3
is required only if the final reviewed change alters the verification manifest
or a core security boundary beyond the named protocol.

Stop for an accessibility model that cannot remain accurate while bounded, an
unbounded or author-controlled native allocation, a second semantic/scroll/
focus authority, raw author-authored wire mutations, service-specific core
behavior, silent truncation, compatibility code for an unused pre-release
protocol generation, substantial conflict, or missing accepted DLV-269 baseline.
Never push.

## Saved later widgets deliverable — DLV-265: controller-first Spotify onboarding

Preserve clean production-only correction tip `13bd971`, reconciliation
`5fd1a06`, installed Spotify 0.3.3, and the user's existing Client ID/account
state. Do not resume, test, integrate, reinstall, replace configuration, or
reset state until DLV-271 is accepted and integrated. Then reconcile the saved
correction onto current main without rewriting it and continue physical-first.

The Spotify Community package must expose a controller-reachable Setup/Change
Client ID flow on configured Ready and unconfigured surfaces, reuse the bounded
host text-entry modal, open the developer dashboard, copy the exact non-secret
redirect URI, validate and atomically store the public Client ID, and refresh
configuration. Changing it retains fail-closed removal of the prior OAuth
refresh credential. Never request/store a Client Secret; PKCE refresh credentials
stay in package-owned Windows Credential Manager. Ordinary setup must require no
terminal, repository checkout, or source directory, and must not add a shared
protocol or service-specific core behavior.

Candidate 0.3.3 at `13bd971` uses the package-owned 96-character input bound.
After DLV-271, physically verify Setup opens/cancels in configured Ready wide and
compact layouts while Player, Queue, Playlists, Devices, account state, and
shortcuts remain intact. Do not replace the existing Client ID unless the user
chooses that account action. After the verdict add only focused presentation/
action, configuration-write, old-credential invalidation, failure, and
cancellation tests. Stop for shared protocol/capability work, Client Secret,
Credential Manager migration, third-party authentication needed for automation,
provider-dashboard automation, substantial conflict, or out-of-package work.
Never push.

## Ready widgets deliverable — DLV-270: Spotify collection paging efficiency

Owner/baseline: widgets lane after DLV-265 resumes following DLV-271, is
physically accepted, focused-tested, and integrated. Keep provider calls,
page/retention policy, queue parsing, diagnostics, and immutable package version
inside Spotify. Reuse accepted generic cursor, viewport-prefetch, and virtualized
window contracts; do not add Spotify behavior to core layers.

Load/validate selected-playlist metadata once per exact selection/configuration
generation, then fetch adjacent tracks only through paged `/items`. Preserve
cancellation, latest-generation authority, stale rejection, duplicate identity,
provider correction, retry, and bounded errors. Measure the current 12-item
page/24-item window against bounded candidates no larger than Spotify's
documented limit and select the smallest settings that avoid visible stalls
without materially increasing snapshot size, layout/render work, decoded
artwork, memory, or calls.

Unify Queue's 50-item cursor declaration and 100-item parser ceiling into one
explicit bounded unpaginated contract governing parsing, admission,
presentation, truncation/error behavior, tests, and diagnostics. Do not invent
queue cursors, poll on playback progress, silently truncate, or refresh
unchanged collections into avoidable churn.

Use physical-first ordering with one immutable package candidate and bounded
request-count/timing evidence: one metadata call per selection, one items call
per page, no duplicate edge load, measured window/snapshot effect, and consistent
Queue admission. The user traverses long collections forward/backward with
right stick and D-pad in compact/wide layouts and verifies timely rows, stable
focus/anchors, artwork, playback, refresh, and recovery. Then add focused call,
cursor, cancellation/stale-generation, bound, truncation, snapshot-size, and
presentation tests. Stop for account authentication needed for automation,
provider ambiguity changing contract, shared-boundary changes, destructive
credential/configuration action, unbounded retention, substantial conflict, or
missing accepted DLV-265/DLV-271 baseline. Never push.

## Serialized order

1. DLV-269 is accepted and integrated through main `683af77`; its immutable
   accepted build is visibly restored as responding PID 78428.
2. Preserve rejected DLV-273 commits only as failed branch history; do not
   continue, test, integrate, or base later work on the presenter refactor.
3. Preserve rejected DLV-274 correction `4645f40` and its healthy-cache evidence;
   do not test or integrate it while the keep-alive reload remains unresolved.
4. Correct DLV-275 from accepted main `683af77`: retain per-widget worker/request
   failure isolation and replace the destructive `CancelSynchronousIo` stale-
   request path so rapid navigation cannot taint or replace the shared bridge.
   Obtain both rapid-switch continuity and failed-widget isolation verdicts.
5. After acceptance, add focused tests and integrate the reviewed DLV-274 and
   DLV-275 production/test chain in order.
6. Run serialized DLV-271 in the platform lane with normal shared-protocol
   verification, provider-free 10,000-item scale proof, physical verdict,
   review, and integration.
7. Resume saved DLV-265 in the widgets lane immediately after accepted DLV-271
   integration; preserve existing Spotify configuration/account state.
8. Run DLV-270 in the widgets lane only after accepted DLV-265 integration.
9. DLV-248 remains deferred until explicit user promotion.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | Complete: all four exact mapping decisions approved on 2026-08-17. |
| Microsoft Store name | Partner Center availability/reservation through the user's account. |
| Domain | Live registrar/RDAP availability and optional registration through the user's account. |
| Trademark | Similar-mark clearance; qualified counsel recommended before public release. |
| GitHub identity | User-selected owner plus repository/organization availability and optional rename/creation. |
| DLV-269 | Complete: physical correction accepted on PID 98812, focused evidence passed, and the chain is integrated through main `683af77`. |
| DLV-273 | Retired failed refactor by user decision. Production `b54e1e2` and corrections `e98566e`/`dcd58e0` remain rejected branch history and must not be resumed or integrated. Accepted DLV-269 PID 78428 is restored; no production revert was necessary because main never contained DLV-273. |
| DLV-274 | Production `bf6d524` is rejected as insufficient. Correction `4645f40` preserves 32 MiB per-image limits and raises only decoded/GPU aggregate LRU budgets to 96 MiB; its caches remained healthy during the latest reproduction. The candidate is nevertheless rejected for the complete physical no-reload objective because Spotify sequence reset `21 -> 1`, proving keep-alive application recreation. Preserve its exact stage and measurements without tests, integration, packaging, or push until DLV-275 is accepted. |
| DLV-275 | Production `baf7bd4` fixed Spotify's original active-lifetime cancellation and added diagnostics; combined candidate `cd1144f` was rejected after YouTube Music exit 1 replaced the shared bridge. Per-widget failure correction `2b609fc` / exact candidate `e318ffb` is also rejected: rapid navigation calls `CancelSynchronousIo`, taints the healthy shared named-pipe transport, retires all registries, and recreates Spotify. Preserve both reviewed fixes while correcting this generic stale-request cancellation boundary; no tests or integration before the new physical verdict. |
| DLV-265 | Saved until immediately after accepted DLV-271; preserve installed 0.3.3 and existing configuration/account state. |
| DLV-248 | Deliberately deferred until explicit user promotion. |

## Recent accepted milestones

| DLV | Accepted evidence / integration |
| --- | --- |
| DLV-260 | Rebrand audit and active residue closure integrated through `1ff96ba`. |
| DLV-264 | Hidden-snapshot failure correction accepted and integrated through `15a26b9`. |
| DLV-266 | Resident Show activation accepted and integrated through `cd83b3a`. |
| DLV-267 | Identical scene-origin correction accepted and integrated through `a552cbf`. |
| DLV-268 | Right-stick free-scroll/focus re-entry accepted and integrated through `1cc9be8`. |
| DLV-272 | Interaction-session extraction accepted and integrated through `8100bd7`. |
| DLV-269 | Viewport-driven paging accepted and integrated through `683af77`; PID 98812 retained under the no-relaunch rule. |
